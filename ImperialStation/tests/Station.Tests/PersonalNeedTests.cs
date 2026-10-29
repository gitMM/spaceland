using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class PersonalNeedTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    private static World Make(long bladder = 500, long strain = 0, bool routine = false, int cohort = 0, bool knownRoster = true, bool toilet = true, bool rest = true, bool work = false, bool report = false, int tickMs = 100, long hunger = 0)
    {
        var items = new List<Item>();
        if (routine || report) items.Add(new(new(1), "card", 1, new(LocationKind.ActorInventory, 1)));
        if (work) items.Add(new(new(2), "correct", 30, new(LocationKind.ActorInventory, 1)));
        var machines = Enumerable.Range(1, 30).Select(i => new MachineDefinition(i, Fixtures.A, "cartridge", "correct", new WorkAccess(new[] { "worker" }), installTicks: 10, inspectTicks: 1, testTicks: 1)).ToArray();
        var duties = work ? new WorkSeed(new[] { new ResourceDefinition("card"), new ResourceDefinition("correct", "cartridge") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(), machines) : null;
        var services = new List<NeedServiceDefinition>();
        if (toilet) services.Add(new(1, NeedServiceKind.Toilet, Fixtures.B, durationTicks: 2));
        if (rest) services.Add(new(2, NeedServiceKind.RestSpot, Fixtures.B, durationTicks: 5));
        return new World("personal-needs", 42, new WorldConfig(tickDurationMs: tickMs, itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 4) },
            new[] { new ActorSeed(new(1), "Citizen", "worker", Fixtures.A) }, items, true,
            information: new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }) },
                credentials: routine || report ? new[] { new CredentialSeed(1, new(1), new(1), new(1)) } : null,
                repairGoals: work ? machines.Select(m => new RepairGoalSeed(new(1), m.Id)) : null, reportGoals: report ? new[] { new ReportGoalSeed(new(1), TerminalFixtures.Draft(1)) } : null),
            work: duties, routines: routine ? new RoutineSeed(new RoutineConfig(dayTicks: 240, dutyTicks: 80, checkInWindow: 1, periodicCheckInTicks: 30), new[] { new RosterAssignmentSeed(new(1), cohort, Fixtures.B, knownRoster) }) : null,
            needs: new NeedSeed(initial: new[] { new NeedInitialValue(new(1), NeedKind.Bladder, bladder * H), new NeedInitialValue(new(1), NeedKind.WorkStrain, strain * H), new NeedInitialValue(new(1), NeedKind.Hunger, hunger * H) }, services: services));
    }
    private static NeedAgentSnapshot Agent(World w) => Assert.Single(w.InspectNeedAgents());
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void SeekToiletCreatesRetainedDueGoalAndResetsOnlyAfterFiniteCardlessUse()
    {
        var w = Make(); Assert.Equal(0, Agent(w).ToiletDueTick); Assert.True(Agent(w).ToiletSequence > 0); RoundTrip(w);
        w.AdvanceTo(6); Assert.Equal(NeedActivityStatus.Active, Assert.Single(w.InspectNeedActivities()).Status); Assert.True(w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.Bladder).Q >= 500 * H); RoundTrip(w);
        w.AdvanceTo(7); Assert.Equal(0, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.Bladder).Q); Assert.Equal(-1, Agent(w).ToiletDueTick); Assert.Equal(0, Agent(w).ToiletSequence);
        Assert.Equal(ActionKind.UseToilet, Assert.Single(w.InspectNeedActivities()).Kind); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void SeekCrossingCreatesDueGoalAtExactTickAndWaitsForFiniteWorkBoundary()
    {
        var w = Make(bladder: 499, work: true, tickMs: 10000); w.AdvanceTo(3); Assert.Equal(-1, Agent(w).ToiletDueTick);
        w.AdvanceTo(4); Assert.Equal(4, Agent(w).ToiletDueTick); Assert.Equal(ActionKind.Install, w.InspectActor(new(1)).Action); long completion = w.InspectActor(new(1)).ActionCompletion; RoundTrip(w);
        w.AdvanceTo(30); Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.UseToilet && a.Status == NeedActivityStatus.Completed);
        var first = w.Trace.First(e => e.Type == "PlanSelected" && e.Tick >= completion); Assert.StartsWith("seek-toilet/v1", first.Detail);
        Assert.True(Assert.Single(w.InspectRepairAgents()).PendingMachines.Count > 0); Assert.Equal(0, w.CriticalNeedInterruptions); RoundTrip(w);
    }
    [Fact]
    public void EarlierReportSequenceWinsBeforeSeekToiletAtSameDueTick()
    {
        var w = Make(report: true); w.AdvanceTo(20);
        var report = Assert.Single(w.Trace, e => e.Type == "ReportGoalCompleted"); var toilet = w.Trace.First(e => e.Type == "PlanSelected" && e.Detail.StartsWith("seek-toilet/v1"));
        Assert.True(report.Id < toilet.Id, string.Join("\n", w.Trace.Where(e => e.Type is "PlanSelected" or "ReportGoalCompleted").Select(e => e.Id + " " + e.Tick + " " + e.Detail))); Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.UseToilet && a.Status == NeedActivityStatus.Completed); RoundTrip(w);
    }
    [Fact]
    public void BlockedSeekGoalRetainsOriginalStampAndAllowsUsefulWork()
    {
        var w = Make(toilet: false, work: true); long sequence = Agent(w).ToiletSequence; w.AdvanceTo(30);
        Assert.Equal(0, Agent(w).ToiletDueTick); Assert.Equal(sequence, Agent(w).ToiletSequence); Assert.True(Agent(w).RetryAt > 30);
        Assert.Contains(w.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("repair-known-equipment")); Assert.Empty(w.InspectNeedActivities()); RoundTrip(w);
    }
    [Fact]
    public void AdditionalRestRequiresSeekAndKnownOffDutyOrBreakOpportunity()
    {
        var offDuty = Make(bladder: 0, strain: 500, routine: true, cohort: 2); offDuty.AdvanceTo(15);
        Assert.Contains(offDuty.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("optional-rest/v1")); Assert.Contains(offDuty.InspectNeedActivities(), a => a.Kind == ActionKind.Rest && a.Status == NeedActivityStatus.Completed); RoundTrip(offDuty);
        foreach (var w in new[] { Make(bladder: 0, strain: 499, routine: true, cohort: 2), Make(bladder: 0, strain: 500), Make(bladder: 0, strain: 500, routine: true) })
        { w.AdvanceTo(30); Assert.DoesNotContain(w.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("optional-rest/v1")); RoundTrip(w); }
    }
    [Fact]
    public void RequiredBreakPrecedesOptionalRestAndOrdinaryWorkPrecedesExtraRest()
    {
        var idle = Make(bladder: 0, strain: 700, routine: true); idle.AdvanceTo(79);
        var required = Assert.Single(idle.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.ScheduledRest);
        var optional = idle.Trace.First(e => e.Type == "PlanSelected" && e.Detail.StartsWith("optional-rest/v1")); Assert.Equal(DailyGoalStatus.Completed, required.Status); Assert.True(optional.Tick >= required.EndedTick); RoundTrip(idle);
        var busy = Make(bladder: 0, strain: 700, routine: true, work: true); busy.AdvanceTo(79);
        Assert.DoesNotContain(busy.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("optional-rest/v1")); Assert.True(Assert.Single(busy.InspectRepairAgents()).PendingMachines.Count > 0); RoundTrip(busy);
        busy.AdvanceTo(120); Assert.Contains(busy.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("optional-rest/v1") && e.Tick >= 80); RoundTrip(busy);
    }
    [Fact]
    public void BlockedOptionalRestYieldsWithBackoffRatherThanSpinning()
    {
        var w = Make(bladder: 0, strain: 700, routine: true, cohort: 2, rest: false); w.AdvanceTo(30);
        Assert.Empty(w.InspectNeedActivities()); Assert.Equal("NoKnownReliefRouteOrEligibleService", Agent(w).BlockedReason); Assert.True(Agent(w).RetryAt > 30);
        Assert.InRange(w.PlannerRequests, 1, 10); RoundTrip(w);
    }
    [Fact]
    public void SeekAndOptionalPoliciesContinueIdenticallyAcrossThresholdAndDutyChanges()
    {
        World Variant() => Make(bladder: 499, strain: 700, routine: true, tickMs: 10000);
        var baseline = Variant(); baseline.AdvanceTo(240);
        foreach (long tick in new long[] { -1, 0, 3, 4, 7, 10, 30, 59, 60, 65, 70, 79, 80, 85, 100, 149, 150, 180, 230 })
        {
            var w = Variant(); if (tick >= 0) w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); Assert.Equal(w.Serialize(), copy.Serialize()); copy.AdvanceTo(240);
            Assert.Equal(baseline.Serialize(), copy.Serialize()); Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }
    [Fact]
    public void UnavailableUrgentServiceYieldsToUsefulSeekAndOptionalReliefWithoutLosingItsRetry()
    {
        foreach (var w in new[] { Make(hunger: 900), Make(bladder: 0, strain: 500, routine: true, cohort: 2, hunger: 900) })
        {
            w.AdvanceTo(20); Assert.Contains(w.InspectNeedActivities(), a => a.Status == NeedActivityStatus.Completed);
            Assert.Equal(NeedBand.Critical, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.Hunger).Band);
            Assert.True(Agent(w).UrgentRetryAt > 20); RoundTrip(w);
            long retry = Agent(w).UrgentRetryAt; w.AdvanceTo(retry + 10);
            Assert.True(w.Trace.Any(e => e.Type == "PlanUnavailable" && e.Detail.StartsWith("urgent-need/v1") && e.Tick >= retry), string.Join("\n", w.Trace.Where(e => e.Type is "PlanSelected" or "PlanUnavailable").Select(e => e.Tick + " " + e.Detail))); RoundTrip(w);
        }
    }
    [Fact]
    public void SnapshotRejectsMissingFutureOrReusedToiletStampAndUnboundedUrgentRetry()
    {
        var w = Make(report: true); w.AdvanceTo(0); var bytes = w.Serialize(); var a = Agent(w);
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(a.ActorId); writer.Write(a.NextDecision); writer.Write(a.RetryAt); writer.Write(a.Backoff); writer.Write((int)a.Goal); writer.Write(a.ServiceId); writer.Write(a.ActiveActionId); writer.Write(a.PlannedTick);
            writer.Write(a.CompletedActivities); writer.Write(a.BlockedReason); writer.Write((int)a.LastOutcome); writer.Write(a.LastCost); writer.Write(a.SleepFallbackRetryAt); writer.Write(a.ToiletDueTick); writer.Write(a.ToiletSequence); writer.Write(a.UrgentRetryAt); writer.Write(a.ConversationCursor); writer.Write(0);
        }
        var block = stream.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        foreach (var mutation in new[] { (block.Length - 36, 1L), (block.Length - 28, 0L), (block.Length - 28, 1L), (block.Length - 20, 1001L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Item2).CopyTo(bad, start + mutation.Item1); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }
    [Fact]
    public void GoldenPersonalNeedsUseNoScriptOrCards()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "personal_needs.json")); Assert.Empty(loaded.Scenario.ScriptedActors);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(10, loaded.Scenario.Assertions.Count);
        Assert.Contains(run.World.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("seek-toilet/v1")); Assert.Contains(run.World.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("optional-rest/v1"));
        Assert.Equal(0, run.World.ActionsFailed); RoundTrip(run.World);
    }
    [Fact]
    public void ExportedTraceStartsAfterItsCheckpointIncludingInitiallyOpenRoutineWindows()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "personal_needs.json"));
        string root = Path.Combine(Fixtures.Root, "artifacts", "test-personal-trace", Guid.NewGuid().ToString("N"));
        var run = Runs.Execute(loaded, 42, true, Path.Combine(root, "run"));
        var start = loaded.CreateWorld(42, true); var copy = World.Restore(start.Serialize(), true);
        var replay = Runs.Execute(loaded, 42, true, Path.Combine(root, "replay"), copy);
        Assert.Contains(run.World.Trace, e => e.Tick == -1 && e.Type == "DailyGoalDue");
        Assert.Equal(File.ReadAllText(run.Result.Artifacts["trace"]), File.ReadAllText(replay.Result.Artifacts["trace"]));
        Assert.Equal(run.Result.Digest, replay.Result.Digest);
    }
}
