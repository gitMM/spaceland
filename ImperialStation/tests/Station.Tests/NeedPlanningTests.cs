using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class NeedPlanningTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    private static World Make(NeedKind need = NeedKind.Hunger, long points = 700, long meals = 2, bool alternate = false, bool publication = true,
        IEnumerable<NeedServiceDefinition>? services = null, IEnumerable<NeedInitialValue>? initial = null, bool report = false, int requests = 64, bool bothStocked = false)
    {
        var kind = need switch { NeedKind.Bladder => NeedServiceKind.Toilet, NeedKind.SleepPressure => NeedServiceKind.Bed, NeedKind.WorkStrain => NeedServiceKind.RestSpot, _ => NeedServiceKind.Cafeteria };
        var defs = services ?? new[] { new NeedServiceDefinition(1, kind, Fixtures.B, durationTicks: 5, assignedActorId: kind == NeedServiceKind.Bed ? 1 : 0) }.Concat(alternate ? new[] { new NeedServiceDefinition(2, kind, Fixtures.C, durationTicks: 5) } : Array.Empty<NeedServiceDefinition>());
        var tiles = new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 4), new Tile(new(3), "Habitat", Fixtures.C, traversalOverride: 4) };
        return new World("autonomous-needs", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog, tiles,
            new[] { new ActorSeed(new(1), "Resident", "worker", Fixtures.A) }, meals > 0 ? new[] { new Item(new(1), "meal", meals, new(LocationKind.TileContainer, alternate ? 3 : 2)) }.Concat(bothStocked ? new[] { new Item(new(2), "meal", meals, new(LocationKind.TileContainer, 2)) } : Array.Empty<Item>()).ToArray() : Array.Empty<Item>(), true,
            information: new InformationSeed(publication: publication ? new PublicKnowledge(1, tiles.Select(t => new KnownTile(t.Id, t.Position, 4)), Array.Empty<PublicService>()) : new PublicKnowledge(1, Array.Empty<KnownTile>(), Array.Empty<PublicService>()),
                reportGoals: report ? new[] { new ReportGoalSeed(new(1), TerminalFixtures.Draft(1)) } : null, planner: new PlannerConfig(requestsPerTick: requests)),
            needs: new NeedSeed(initial: initial ?? new[] { new NeedInitialValue(new(1), need, points * H) }, services: defs));
    }
    private static NeedAgentSnapshot Agent(World w) => Assert.Single(w.InspectNeedAgents());
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());

    [Fact]
    public void UrgentCitizenTravelsQueuesAndCompletesARealMealWithoutCommands()
    {
        var w = Make(); w.AdvanceTo(0); Assert.True(w.InspectActor(new(1)).InTransit); Assert.Equal(1, Agent(w).ServiceId); RoundTrip(w);
        w.AdvanceTo(4); Assert.Equal(NeedActivityStatus.Queued, Assert.Single(w.InspectNeedActivities()).Status); Assert.Equal(0, w.MealsConsumed);
        w.AdvanceTo(5); Assert.Equal(NeedActivityStatus.Active, Assert.Single(w.InspectNeedActivities()).Status); RoundTrip(w);
        w.AdvanceTo(10); Assert.Equal(1, w.MealsConsumed); Assert.Equal(1, Agent(w).CompletedActivities); Assert.Equal(200 * H + 60000, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.Hunger).Q);
        Assert.Equal(0, w.ActionsFailed); Assert.Equal(0, Agent(w).ActiveActionId); w.AssertAccounting(); RoundTrip(w);
    }
    [Fact]
    public void PlannerCannotSeeHiddenStockOrLiveServiceOutages()
    {
        var stocked = Make(alternate: true); var empty = Make(alternate: true, meals: 0); empty.ScheduleNeedServiceChange(0, 1, false); empty.AdvanceTo(0); stocked.AdvanceTo(0);
        Assert.Equal(1, Agent(stocked).ServiceId); Assert.Equal(1, Agent(empty).ServiceId);
        Assert.Equal(NeedPlanning.Plan(stocked.ActorView(new(1)), NeedKind.Hunger).Outcome, NeedPlanning.Plan(empty.ActorView(new(1)), NeedKind.Hunger).Outcome);
        Assert.Empty(Agent(empty).Failures); Assert.Equal(stocked.Trace.Single(e => e.Type == "PlanSelected").Detail, empty.Trace.Single(e => e.Type == "PlanSelected").Detail);
    }
    [Fact]
    public void EmptyCounterSeenOnArrivalSelectsAKnownAlternativeWithoutTelepathy()
    {
        // The empty counter is unknown from afar; at arrival it is seen locally and the
        // resident continues to the alternative without queueing for a meal that is not there.
        var w = Make(alternate: true); w.AdvanceTo(0); Assert.Equal(1, Agent(w).ServiceId);
        w.AdvanceTo(5); Assert.Equal(0, w.ActionsFailed);
        var seen = Assert.Single(w.ActorView(new(1)).Knowledge, f => f.Key.Equals(new FactKey(SubjectKind.Service, 1, AttributeKind.Quantity)));
        Assert.Equal(0, seen.Value.Number); Assert.Equal(4, seen.ObservedTick); Assert.Equal(EvidenceKind.Direct, seen.SourceKind); RoundTrip(w);
        w.AdvanceTo(16); Assert.Equal(Fixtures.C, w.InspectActor(new(1)).Position); Assert.Equal(1, w.MealsConsumed); Assert.Equal(2, Agent(w).ServiceId);
        Assert.Equal(NeedActivityStatus.Completed, Assert.Single(w.InspectNeedActivities()).Status); w.AssertAccounting(); RoundTrip(w);
    }
    [Fact]
    public void ActualStockFailureAtAStockedLookingCounterIsLearnedAndBounded()
    {
        // Meals are visible but reserved by nobody else; a disabled service still fails only locally.
        var w = Make(alternate: true, bothStocked: true); w.ScheduleNeedServiceChange(0, 1, false); w.AdvanceTo(5);
        Assert.Equal(1, w.ActionsFailed); Assert.Equal(FailureReason.ServiceBusy, Assert.Single(Agent(w).Failures).Reason); RoundTrip(w);
        w.AdvanceTo(20); Assert.Equal(1, w.MealsConsumed); Assert.Equal(2, Assert.Single(w.InspectNeedActivities(), a => a.Status == NeedActivityStatus.Completed).ServiceId);
    }
    [Fact]
    public void OutageIsLearnedOnlyAtArrivalAndAllowsAlternative()
    {
        var w = Make(alternate: true, bothStocked: true); w.ScheduleNeedServiceChange(0, 1, false); w.AdvanceTo(3); Assert.Empty(Agent(w).Failures);
        w.AdvanceTo(4); Assert.Equal(FailureReason.ServiceBusy, Assert.Single(Agent(w).Failures).Reason); RoundTrip(w);
        w.AdvanceTo(20); Assert.Equal(1, w.MealsConsumed); Assert.Equal(1, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void ShortageRetriesAreBoundedAndNeverCreateMeals()
    {
        // Both counters are seen empty on arrival: no failed service attempts are needed,
        // and the remembered empty counters keep planning bounded until they go stale.
        var w = Make(alternate: true, meals: 0); w.AdvanceTo(3000);
        Assert.InRange(w.ActionsFailed, 0, 16); Assert.InRange(w.PlannerRequests, 2, 45); Assert.Equal(0, w.MealsConsumed); Assert.Empty(w.InspectItems());
        Assert.Equal(2, w.ActorView(new(1)).Knowledge.Count(f => f.Key.SubjectKind == SubjectKind.Service && f.Key.Attribute == AttributeKind.Quantity && f.Value.Number == 0));
        Assert.True(Agent(w).NextDecision > 3000); Assert.True(Agent(w).RetryAt > 3000); w.AssertAccounting(); RoundTrip(w);
    }
    [Fact]
    public void PrivateRetryEventuallyDiscoversRestoredServiceWithoutBroadcast()
    {
        var w = Make(); w.ScheduleNeedServiceChange(0, 1, false); w.ScheduleNeedServiceChange(30, 1, true); w.AdvanceTo(30);
        Assert.Equal(0, w.MealsConsumed); Assert.Single(Agent(w).Failures); Assert.True(Agent(w).NextDecision > 30);
        w.AdvanceTo(70); Assert.Equal(1, w.MealsConsumed); Assert.Empty(Agent(w).Failures); RoundTrip(w);
    }
    [Fact]
    public void UnknownRouteDoesNotReadPhysicalTopologyButLocalServiceNeedsNoMap()
    {
        var w = Make(publication: false); w.AdvanceTo(100); Assert.Equal(0, w.PathRequests); Assert.Equal("NoKnownReliefRouteOrEligibleService", Agent(w).BlockedReason); RoundTrip(w);
        var local = Make(NeedKind.Bladder, 800, publication: false, services: new[] { new NeedServiceDefinition(1, NeedServiceKind.Toilet, Fixtures.A, durationTicks: 2) });
        local.AdvanceTo(3); Assert.Equal(1, Agent(local).CompletedActivities); Assert.Equal(0, local.PathRequests); Assert.Equal(0, local.ActionsFailed); RoundTrip(local);
    }
    [Fact]
    public void SleepUsesOwnBerthAndNeverTreatsRestAsSleep()
    {
        var w = Make(NeedKind.SleepPressure, 850); w.AdvanceTo(6); Assert.Equal(NeedMode.Sleeping, Assert.Single(w.InspectNeeds()).Mode); RoundTrip(w);
        var noBed = Make(NeedKind.SleepPressure, 850, services: new[] { new NeedServiceDefinition(1, NeedServiceKind.RestSpot, Fixtures.B), new NeedServiceDefinition(2, NeedServiceKind.Bed, Fixtures.C) });
        noBed.AdvanceTo(100); Assert.Equal(ActionKind.Rest, Assert.Single(noBed.InspectNeedActivities()).Kind); Assert.Equal(NeedMode.Resting, Assert.Single(noBed.InspectNeeds()).Mode);
        Assert.Equal("SleepUnmet/NoKnownUsableBerth", Agent(noBed).BlockedReason); Assert.True(noBed.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.SleepPressure).Q > 850 * H); RoundTrip(noBed);
    }
    [Fact]
    public void SeekHungerDoesNotInventAScheduledMealWindow()
    {
        var w = Make(points: 300); w.AdvanceTo(100); Assert.Equal(0, w.PlannerRequests); Assert.Empty(w.InspectNeedActivities()); Assert.Equal(-1, Agent(w).NextDecision); RoundTrip(w);
    }
    [Fact]
    public void CriticalCrossingDoesNotInterruptTravelAlreadyRelievingThatNeed()
    {
        var w = Make(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, 900 * H - 2 * 6000) }); w.AdvanceTo(2);
        Assert.True(w.InspectActor(new(1)).InTransit); Assert.Equal(0, w.CriticalNeedInterruptions); RoundTrip(w);
        w.AdvanceTo(10); Assert.Equal(1, w.MealsConsumed); Assert.Equal(0, w.CriticalNeedInterruptions); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void DifferentCriticalNeedInterruptsOnlyAtTheCommittedEdgeBoundary()
    {
        var w = Make(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, 700 * H), new NeedInitialValue(new(1), NeedKind.Bladder, 950 * H - 2 * 10000) },
            services: new[] { new NeedServiceDefinition(1, NeedServiceKind.Cafeteria, Fixtures.C, durationTicks: 5), new NeedServiceDefinition(2, NeedServiceKind.Toilet, Fixtures.B, durationTicks: 2) });
        w.AdvanceTo(2); Assert.True(w.InspectActor(new(1)).InTransit); Assert.Equal(1, w.CriticalNeedInterruptions); RoundTrip(w);
        w.AdvanceTo(7); Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.UseToilet && a.Status == NeedActivityStatus.Completed); Assert.Equal(0, w.MealsConsumed);
        Assert.Contains(w.Trace, e => e.Type == "ActionCancelled" && e.Tick == 4); RoundTrip(w);
    }
    [Fact]
    public void UnrelatedRejectedCommandDoesNotPoisonServiceFailureMemory()
    {
        var w = Make(); w.Submit(2, new(new(1), ActionKind.Inspect, Fixtures.C)); w.AdvanceTo(3); Assert.Empty(Agent(w).Failures); Assert.True(Agent(w).ActiveActionId > 0);
        w.AdvanceTo(10); Assert.Equal(1, w.MealsConsumed); Assert.Empty(Agent(w).Failures); RoundTrip(w);
    }
    [Fact]
    public void UrgentReliefPrecedesRequiredReportButBlockedReliefYields()
    {
        var w = Make(report: true, requests: 1); w.AdvanceTo(9); Assert.DoesNotContain(w.Trace, e => e.Type == "PlanUnavailable" && e.Detail.StartsWith("report-due"));
        w.AdvanceTo(15); Assert.Equal(1, w.MealsConsumed); Assert.Contains(w.Trace, e => e.Type == "PlanUnavailable" && e.Detail.StartsWith("report-due") && e.Tick >= 10); RoundTrip(w);
        var blocked = Make(report: true, publication: false, points: 900); blocked.AdvanceTo(5);
        Assert.Contains(blocked.Trace, e => e.Type == "PlanUnavailable" && e.Detail.StartsWith("report-due")); Assert.True(Agent(blocked).RetryAt > 5); RoundTrip(blocked);
    }
    [Fact]
    public void EveryTravelQueueFailureAndReliefCheckpointReplaysExactly()
    {
        var baseline = Make(alternate: true); baseline.AdvanceTo(90);
        foreach (long tick in Enumerable.Range(-1, 32).Select(i => (long)i))
        {
            var prefix = Make(alternate: true); if (tick >= 0) prefix.AdvanceTo(tick); var bytes = prefix.Serialize(); var restored = World.Restore(bytes, true); Assert.Equal(bytes, restored.Serialize());
            for (long t = Math.Max(0, tick + 1); t <= 90; t++) { restored.AdvanceTo(t); _ = restored.InspectNeedAgents(); _ = restored.ActorView(new(1)); }
            Assert.Equal(baseline.Serialize(), restored.Serialize()); Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        }
    }
    [Fact]
    public void ExplicitScriptedActorDoesNotAcquireAutonomousGoals()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "need_services.json")); var w = loaded.CreateWorld(42);
        Assert.Empty(w.InspectNeedAgents()); w.AdvanceTo(50); Assert.Equal(5, w.ActionsCompleted); RoundTrip(w);
    }
    [Fact]
    public void UnavailableHigherNeedDoesNotPreventOtherUsefulRelief()
    {
        var w = Make(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, 900 * H), new NeedInitialValue(new(1), NeedKind.Bladder, 800 * H) },
            services: new[] { new NeedServiceDefinition(1, NeedServiceKind.Toilet, Fixtures.B, durationTicks: 2) }, requests: 1);
        w.AdvanceTo(7); Assert.Equal(1, Agent(w).CompletedActivities); Assert.Equal(ActionKind.UseToilet, Assert.Single(w.InspectNeedActivities()).Kind);
        Assert.Equal(NeedBand.Critical, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.Hunger).Band); Assert.True(w.PlannerExpansions <= w.PlannerRequests * 512); RoundTrip(w);
    }
    private static World EscortWithNeeds(long actor, NeedKind kind, long q, NeedServiceKind serviceKind, long facility = 0, int needRate = 0)
    {
        var setup = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "detention_release.json"));
        var j = JsonNode.Parse(setup.ScenarioJson)!.AsObject(); j["assertions"] = new JsonArray(); j["scriptedActors"] = new JsonArray();
        j["initialState"]!["actors"]!.AsArray().Single(a => a!["id"]!.GetValue<long>() == actor)!["scripted"] = false;
        j["initialState"]!["items"]!.AsArray().Add(new JsonObject { ["id"] = 900, ["resource"] = "meal", ["quantity"] = 2, ["locationKind"] = "TileContainer", ["owner"] = 3 });
        j["initialState"]!["needs"] = new JsonObject {
            ["initial"] = new JsonArray(new JsonObject { ["actorId"] = actor, ["kind"] = kind.ToString(), ["q"] = q }),
            ["services"] = new JsonArray(new JsonObject { ["id"] = 1, ["kind"] = serviceKind.ToString(), ["position"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 }, ["detentionFacilityId"] = facility, ["durationTicks"] = 5 }) };
        if (needRate > 0)
        {
            j["initialState"]!["needs"]!["rules"] = JsonNode.Parse(JsonData.Write(new NeedConfig().Rules.Select(r =>
                r.Kind == kind ? new NeedRule(r.Kind, needRate, needRate, needRate, needRate, r.Seek, r.Urgent, r.Critical) : r)));
            j["initialState"]!["routines"]!["configuration"]!["dayTicks"] = 6000;
            j["initialState"]!["routines"]!["configuration"]!["dutyTicks"] = 2000;
        }
        var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")))!.AsObject(); catalog["resources"]!.AsArray().Add(new JsonObject { ["id"] = "meal" });
        var world = new LoadedScenario(j.ToJsonString(), catalog.ToJsonString(), "autonomous-custody-needs").CreateWorld(42, true);
        // Reuse the earlier physical misconduct setup (ticks 0..12). The subject's
        // later need actions have no commands. This is an integration fixture,
        // not evidence that the entire detention setup is autonomous.
        foreach (var script in setup.Scenario.ScriptedActors)
        foreach (var c in script.Commands) world.Submit(c.Tick, new(new(script.ActorId), c.Kind, c.Target.ToCoord(), new(c.ItemId), c.ServiceId, c.Message?.ToDraft(), c.RecipeId, c.AttemptId));
        return world;
    }
    [Fact]
    public void UrgentNeedDefersToReadEscortAndResumesAfterPhysicalIntake()
    {
        var w = EscortWithNeeds(3, NeedKind.Hunger, 700 * H - 210 * 6000, NeedServiceKind.Cafeteria);
        w.AdvanceTo(734); Assert.Empty(w.InspectNeedActivities()); Assert.Equal(NeedBand.Urgent, w.InspectOwnNeeds(new(3)).Single(n => n.Kind == NeedKind.Hunger).Band); RoundTrip(w);
        w.AdvanceTo(950); Assert.Equal(1, w.MealsConsumed); Assert.Single(w.InspectNeedActivities(), a => a.ActorId == 3 && a.Status == NeedActivityStatus.Completed); Assert.Equal(DetentionStayStatus.Detained, Assert.Single(w.InspectDetentionStays()).Status); RoundTrip(w);
    }
    [Theory]
    [InlineData(NeedKind.Hunger, NeedServiceKind.Cafeteria, 700, 20000)]
    [InlineData(NeedKind.Bladder, NeedServiceKind.Toilet, 500, 40000)]
    public void GuardRelievesPredictedCriticalNeedBeforeCommittingToCustody(NeedKind kind, NeedServiceKind service, int threshold, int rate)
    {
        // Hunger becomes urgent after order adoption and critical during the
        // predicted journey/intake, so startup relief cannot satisfy this test.
        var w = EscortWithNeeds(3, kind, threshold * H - 210L * rate * 100, service, needRate: rate);
        w.AdvanceTo(400);
        Assert.Empty(w.InspectEscorts());
        var copy = World.Restore(w.Serialize(), true);
        w.ClearRecordedTrace(); copy.ClearRecordedTrace();
        w.AdvanceTo(1800); copy.AdvanceTo(1800);
        Assert.DoesNotContain(w.InspectEscorts(), e => e.Status == EscortStatus.Interrupted);
        Assert.True(Assert.Single(w.InspectDetentionStays()).IntakeTick > 0);
        Assert.Contains(w.InspectNeedActivities(), a => a.ActorId == 3 && a.Status == NeedActivityStatus.Completed && a.EndedTick < Assert.Single(w.InspectEscorts()).StartedTick);
        Assert.DoesNotContain(w.Trace, e => e.Type == "NeedThresholdReached" && e.ActorId == 3 && e.Detail.StartsWith(kind + "/Critical") && e.Tick < Assert.Single(w.InspectDetentionStays()).IntakeTick);
        Assert.Equal(w.Digest(), copy.Digest());
        Assert.Equal(w.Trace.Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
    }
    [Fact]
    public void DetaineeAutonomouslyUsesOnlyItsLocalPermittedService()
    {
        var w = EscortWithNeeds(1, NeedKind.Bladder, 950 * H - 900 * 10000, NeedServiceKind.Toilet, 1);
        Assert.Equal(-1, w.ActorView(new(1)).NeedCustodyFacilityId); w.AdvanceTo(570); Assert.Equal(0, w.ActorView(new(1)).NeedCustodyFacilityId); Assert.Empty(w.InspectNeedActivities()); RoundTrip(w);
        w.AdvanceTo(950); Assert.Equal(1, w.ActorView(new(1)).NeedCustodyFacilityId); Assert.Equal(1, w.InspectNeedAgents().Single(a => a.ActorId == 1).CompletedActivities);
        Assert.Equal(Fixtures.C, w.InspectActor(new(1)).Position); Assert.Equal(DetentionStayStatus.Detained, Assert.Single(w.InspectDetentionStays()).Status); RoundTrip(w);
    }
    [Fact]
    public void AutonomousGoldenScenarioHasNoScriptAndPassesAllAssertions()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_need_relief.json")); Assert.Empty(loaded.Scenario.ScriptedActors);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(10, loaded.Scenario.Assertions.Count);
        Assert.Equal(1, Agent(run.World).CompletedActivities); Assert.Equal(0, run.World.ActionsFailed); RoundTrip(run.World);
    }
    [Fact]
    public void SnapshotRejectsInvalidNeedActionOwnershipRetryClockAndFailureMemory()
    {
        var w = Make(alternate: true); w.AdvanceTo(6); var bytes = w.Serialize(); var a = Agent(w);
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(1); writer.Write(a.ActorId); writer.Write(a.NextDecision); writer.Write(a.RetryAt); writer.Write(a.Backoff); writer.Write((int)a.Goal);
            writer.Write(a.ServiceId); writer.Write(a.ActiveActionId); writer.Write(a.PlannedTick); writer.Write(a.CompletedActivities); writer.Write(a.BlockedReason); writer.Write((int)a.LastOutcome); writer.Write(a.LastCost); writer.Write(a.SleepFallbackRetryAt); writer.Write(a.ToiletDueTick); writer.Write(a.ToiletSequence); writer.Write(a.UrgentRetryAt); writer.Write(a.ConversationCursor);
            writer.Write(a.Failures.Count); foreach (var f in a.Failures) { writer.Write(f.ServiceId); writer.Write(f.ActionId); writer.Write(f.Tick); writer.Write(f.RetryAt); writer.Write((int)f.Reason); }
        }
        var block = stream.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        // Count, actor, timer, retry, backoff, kind, service, owned action, planned tick.
        foreach (var mutation in new[] { (start + 12, 0L), (start + 20, -1L), (start + 28, 0L), (start + 40, 999L), (start + 48, 1L), (start + 56, 999L), (start + block.Length - 12, 5L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Item2).CopyTo(bad, mutation.Item1); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }
}
