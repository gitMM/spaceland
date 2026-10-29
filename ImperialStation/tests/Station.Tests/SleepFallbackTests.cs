using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class SleepFallbackTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    private const string Reason = "SleepUnmet/NoKnownUsableBerth";
    private static World Make(bool daily = false, bool bed = false, bool rest = true, bool alternateNeed = false, long restDuration = 5, bool criticalCrossing = false, bool publication = true, int budget = 512)
    {
        var tiles = new[] { new Tile(new(1), "Habitat", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 4) };
        var services = new List<NeedServiceDefinition>();
        if (rest) services.Add(new(1, NeedServiceKind.RestSpot, Fixtures.B, durationTicks: restDuration));
        if (bed) services.Add(new(2, NeedServiceKind.Bed, Fixtures.B, durationTicks: 5, assignedActorId: 1));
        if (alternateNeed) services.Add(new(3, NeedServiceKind.Toilet, Fixtures.A, durationTicks: 2));
        var initial = new List<NeedInitialValue> { new(new(1), NeedKind.SleepPressure, criticalCrossing ? 1000 * H - 7 * 5000 : (daily ? 100 : 850) * H), new(new(1), NeedKind.WorkStrain, 100 * H) };
        if (alternateNeed) initial.Add(new(new(1), NeedKind.Bladder, 800 * H));
        return new World("sleep-fallback", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog, tiles,
            new[] { new ActorSeed(new(1), "Unassigned", "worker", Fixtures.A) }, Array.Empty<Item>(), true,
            information: new InformationSeed(TerminalFixtures.Tiny(), publication: new PublicKnowledge(1, publication ? tiles.Select(t => new KnownTile(t.Id, t.Position, 4)) : Array.Empty<KnownTile>(), Array.Empty<PublicService>()), planner: new PlannerConfig(nodeBudget: budget)),
            routines: daily ? new RoutineSeed(new RoutineConfig(dayTicks: 240, dutyTicks: 80, checkInWindow: 1, periodicCheckInTicks: 30), new[] { new RosterAssignmentSeed(new(1), 0, Fixtures.B) }) : null,
            needs: new NeedSeed(initial: initial, services: services));
    }
    private static NeedAgentSnapshot Agent(World w) => Assert.Single(w.InspectNeedAgents());
    private static DailyGoalSnapshot SleepGoal(World w) => Assert.Single(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.Sleep);
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void CardlessRestUsesPhysicalTimeWithoutRelievingSleepAndYieldsAfterward()
    {
        var w = Make(); w.AdvanceTo(5); Assert.Equal(NeedMode.Resting, Assert.Single(w.InspectNeeds()).Mode); Assert.Equal(Reason, Agent(w).BlockedReason); RoundTrip(w);
        w.AdvanceTo(10); var activity = Assert.Single(w.InspectNeedActivities()); Assert.Equal(ActionKind.Rest, activity.Kind); Assert.Equal(NeedActivityStatus.Completed, activity.Status);
        Assert.Equal(850 * H + 50000, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.SleepPressure).Q);
        Assert.Equal(100 * H - 60000, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.WorkStrain).Q);
        Assert.Equal(NeedKind.SleepPressure, Agent(w).Goal); Assert.Equal(Reason, Agent(w).BlockedReason); Assert.Equal(60, Agent(w).SleepFallbackRetryAt);
        w.AdvanceTo(59); Assert.Single(w.InspectNeedActivities()); Assert.Null(w.InspectActor(new(1)).Action); RoundTrip(w);
    }
    [Fact]
    public void RealBerthPlanAlwaysPrecedesFallbackRest()
    {
        var w = Make(bed: true); w.AdvanceTo(10); Assert.Equal(ActionKind.Sleep, Assert.Single(w.InspectNeedActivities()).Kind);
        Assert.DoesNotContain(w.Trace, e => e.Type == "SleepUnmet"); Assert.Equal(0, Agent(w).SleepFallbackRetryAt); RoundTrip(w);
    }
    [Fact]
    public void OtherUrgentReliefPrecedesRestForAnUnmetSleepNeed()
    {
        var w = Make(alternateNeed: true); w.AdvanceTo(3); Assert.Equal(ActionKind.UseToilet, Assert.Single(w.InspectNeedActivities()).Kind);
        w.AdvanceTo(20); Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.Rest && a.Status == NeedActivityStatus.Completed); RoundTrip(w);
    }
    [Fact]
    public void NoKnownRouteOrRestServiceKeepsAnExplainedBlockedSleepNeed()
    {
        foreach (var w in new[] { Make(rest: false), Make(publication: false) })
        { w.AdvanceTo(20); Assert.Empty(w.InspectNeedActivities()); Assert.Equal(Reason, Agent(w).BlockedReason); Assert.True(Agent(w).RetryAt > 20); RoundTrip(w); }
    }
    [Fact]
    public void LocalRestOutageIsLearnedWithoutRemoteAvailabilityKnowledge()
    {
        var w = Make(); w.ScheduleNeedServiceChange(0, 1, false); w.AdvanceTo(3); Assert.Empty(Agent(w).Failures); Assert.True(w.InspectActor(new(1)).InTransit);
        w.AdvanceTo(10); Assert.Equal(FailureReason.ServiceBusy, Assert.Single(Agent(w).Failures).Reason); Assert.Empty(w.InspectNeedActivities()); Assert.Equal(Reason, Agent(w).BlockedReason); RoundTrip(w);
    }
    [Fact]
    public void CriticalSleepCrossingKeepsItsFallbackRestButCancellationGrantsOnlyElapsedStrainRelief()
    {
        var w = Make(criticalCrossing: true); w.AdvanceTo(7); Assert.Equal(0, w.CriticalNeedInterruptions); Assert.Equal(ActionKind.Rest, w.InspectActor(new(1)).Action); RoundTrip(w);
        w.Submit(8, new(new(1), ActionKind.Cancel, Fixtures.B)); w.AdvanceTo(8);
        Assert.Equal(NeedActivityStatus.Cancelled, Assert.Single(w.InspectNeedActivities()).Status); Assert.Equal(0, Agent(w).CompletedActivities); Assert.Equal(0, Agent(w).SleepFallbackRetryAt);
        Assert.Equal(100 * H - 36000, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.WorkStrain).Q); Assert.Equal(1000 * H, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.SleepPressure).Q); RoundTrip(w);
    }
    [Fact]
    public void ReopenedOwnBerthIsFoundByPrivateRetryAndCanActuallySatisfySleep()
    {
        var w = Make(bed: true); w.ScheduleNeedServiceChange(0, 2, false); w.ScheduleNeedServiceChange(30, 2, true); w.AdvanceTo(30);
        Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.Rest && a.Status == NeedActivityStatus.Completed); Assert.DoesNotContain(w.InspectNeedActivities(), a => a.Kind == ActionKind.Sleep); RoundTrip(w);
        w.AdvanceTo(120); Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.Sleep && a.Status == NeedActivityStatus.Completed); Assert.Equal(2, w.ActorView(new(1)).OwnBerthId); RoundTrip(w);
    }
    [Fact]
    public void DailyFallbackDoesNotCompleteSleepAndPreservesExplanationWhenWindowCloses()
    {
        var w = Make(daily: true); w.AdvanceTo(180); var goal = SleepGoal(w);
        Assert.Equal(DailyGoalStatus.Pending, goal.Status); Assert.Equal(0, goal.CompletedActionId); Assert.Equal(Reason, goal.Reason); Assert.True(goal.RetryAt > 180);
        Assert.Contains(w.Trace, e => e.Type == "SleepUnmet" && e.Tick >= 150); Assert.DoesNotContain(w.InspectNeedActivities(), a => a.Kind == ActionKind.Sleep); RoundTrip(w);
        w.AdvanceTo(230); Assert.Equal(DailyGoalStatus.MissedWindow, SleepGoal(w).Status); Assert.Contains(Reason, SleepGoal(w).Reason); RoundTrip(w);
    }
    [Fact]
    public void DailyFallbackFinishingAfterWindowStillDoesNotBecomeSleepCompletion()
    {
        var w = Make(daily: true, restDuration: 100); w.AdvanceTo(280);
        Assert.Equal(DailyGoalStatus.MissedWindow, SleepGoal(w).Status); Assert.Equal(0, SleepGoal(w).CompletedActionId); Assert.Contains(Reason, SleepGoal(w).Reason); RoundTrip(w);
    }
    [Fact]
    public void DailyGoalCanCompleteOnlyAfterRecoveredBerthAndRealSleep()
    {
        var w = Make(daily: true, bed: true); w.ScheduleNeedServiceChange(0, 2, false); w.ScheduleNeedServiceChange(180, 2, true); w.AdvanceTo(180);
        Assert.Equal(DailyGoalStatus.Pending, SleepGoal(w).Status); Assert.Equal(Reason, SleepGoal(w).Reason); RoundTrip(w);
        w.AdvanceTo(230); var goal = SleepGoal(w); Assert.Equal(DailyGoalStatus.Completed, goal.Status);
        Assert.Equal(ActionKind.Sleep, w.InspectNeedActivities().Single(a => a.ActionId == goal.CompletedActionId).Kind); RoundTrip(w);
    }
    [Fact]
    public void BothUrgentAndDailyFallbackContinueExactlyAcrossMovementQueueRestAndRetry()
    {
        foreach (bool daily in new[] { false, true })
        {
            var baseline = Make(daily: daily); baseline.AdvanceTo(240);
            foreach (long tick in new long[] { -1, 0, 3, 4, 5, 7, 10, 59, 60, 150, 151, 155, 156, 180, 205, 210, 230 })
            {
                var w = Make(daily: daily); if (tick >= 0) w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); Assert.Equal(w.Serialize(), copy.Serialize()); copy.AdvanceTo(240);
                Assert.Equal(baseline.Serialize(), copy.Serialize()); Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
            }
        }
    }
    [Fact]
    public void ExhaustedPrimarySearchCannotInventAnUnavailableBerthOrUseAnotherBudget()
    {
        var w = Make(bed: true, budget: 1); w.AdvanceTo(20);
        Assert.Equal(PlanOutcome.BudgetExhausted, Agent(w).LastOutcome); Assert.Empty(w.InspectNeedActivities()); Assert.DoesNotContain(w.Trace, e => e.Type == "SleepUnmet");
        Assert.True(w.PlannerExpansions <= w.PlannerRequests); RoundTrip(w);
    }
    [Fact]
    public void SnapshotRejectsFallbackRestClaimedAsCompletedSleep()
    {
        var w = Make(daily: true); w.AdvanceTo(180); var goal = SleepGoal(w); var bytes = w.Serialize();
        var rest = w.InspectNeedActivities().Last(a => a.Kind == ActionKind.Rest && a.Status == NeedActivityStatus.Completed);
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(goal.Id); writer.Write(goal.ActorId); writer.Write(goal.RosterVersion); writer.Write((int)goal.Kind); writer.Write(goal.Opens); writer.Write(goal.DueTick); writer.Write(goal.Closes); writer.Write((int)goal.Status);
            writer.Write(goal.RetryAt); writer.Write(goal.Backoff); writer.Write(goal.Reason); writer.Write(goal.ServiceId); writer.Write(goal.ActiveActionId); writer.Write(goal.CompletedActionId); writer.Write(goal.EndedTick);
        }
        var block = stream.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        BitConverter.GetBytes((int)DailyGoalStatus.Completed).CopyTo(bytes, start + 52); BitConverter.GetBytes(rest.ActionId).CopyTo(bytes, start + block.Length - 16); BitConverter.GetBytes(rest.EndedTick).CopyTo(bytes, start + block.Length - 8);
        Assert.Throws<ArgumentException>(() => World.Restore(bytes));
    }
    [Fact]
    public void SnapshotRejectsFallbackCooldownWithoutCompletedPhysicalRest()
    {
        var w = Make(); w.AdvanceTo(5); var bytes = w.Serialize(); var a = Agent(w);
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(a.ActorId); writer.Write(a.NextDecision); writer.Write(a.RetryAt); writer.Write(a.Backoff); writer.Write((int)a.Goal); writer.Write(a.ServiceId); writer.Write(a.ActiveActionId); writer.Write(a.PlannedTick);
            writer.Write(a.CompletedActivities); writer.Write(a.BlockedReason); writer.Write((int)a.LastOutcome); writer.Write(a.LastCost); writer.Write(a.SleepFallbackRetryAt); writer.Write(a.ToiletDueTick); writer.Write(a.ToiletSequence); writer.Write(a.UrgentRetryAt); writer.Write(a.ConversationCursor); writer.Write(0);
        }
        var block = stream.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        BitConverter.GetBytes(55L).CopyTo(bytes, start + block.Length - 44); Assert.Throws<ArgumentException>(() => World.Restore(bytes));
    }
    [Fact]
    public void CardlessFallbackGoldenScenarioHasNoScriptAndPassesAssertions()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "sleep_fallback.json")); Assert.Empty(loaded.Scenario.ScriptedActors);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(10, loaded.Scenario.Assertions.Count);
        Assert.Equal(Reason, Agent(run.World).BlockedReason); Assert.Equal(60, Agent(run.World).SleepFallbackRetryAt); Assert.Equal(0, run.World.ActionsFailed); RoundTrip(run.World);
    }
}
