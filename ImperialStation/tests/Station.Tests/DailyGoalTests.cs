using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class DailyGoalTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    // A declared 240-tick day, 80-tick duty and scaled finite service blocks.
    // These fixtures test ordering/timing; they do not claim a 24-hour needs gate.
    private static World Make(bool work = false, bool card = true, bool services = true, long hunger = 0, int cohort = 0, bool known = true, long sleep = 30,
        IEnumerable<InitialMessage>? messages = null, IEnumerable<ReportGoalSeed>? reports = null, int tickMs = 100)
    {
        var items = new List<Item> { new(new(2), "meal", 20, new(LocationKind.TileContainer, 2)) };
        if (card) items.Add(new(new(1), "card", 1, new(LocationKind.ActorInventory, 1)));
        if (work) items.Add(new(new(3), "correct", 10, new(LocationKind.ActorInventory, 1)));
        var machines = Enumerable.Range(1, 10).Select(i => new MachineDefinition(i, Fixtures.A, "cartridge", "correct", new WorkAccess(new[] { "worker" }), installTicks: 20, inspectTicks: 1, testTicks: 1)).ToArray();
        var duties = work ? new WorkSeed(new[] { new ResourceDefinition("meal"), new ResourceDefinition("card"), new ResourceDefinition("correct", "cartridge") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(), machines) : null;
        return new World("daily-goals", 42, new WorldConfig(tickDurationMs: tickMs, itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 4) },
            new[] { new ActorSeed(new(1), "Citizen", "worker", Fixtures.A) }, items, true,
            information: new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }) },
                credentials: card ? new[] { new CredentialSeed(1, new(1), new(1), new(1)) } : null, repairGoals: work ? machines.Select(m => new RepairGoalSeed(new(1), m.Id)) : null, messages: messages, reportGoals: reports),
            work: duties, routines: new RoutineSeed(new RoutineConfig(dayTicks: 240, dutyTicks: 80, checkInWindow: 1, periodicCheckInTicks: 30), new[] { new RosterAssignmentSeed(new(1), cohort, Fixtures.B, known) }),
            needs: new NeedSeed(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, hunger * H) }, services: services ? new[] {
                new NeedServiceDefinition(1, NeedServiceKind.Cafeteria, Fixtures.B, durationTicks: 5), new NeedServiceDefinition(2, NeedServiceKind.RestSpot, Fixtures.B, durationTicks: 5),
                new NeedServiceDefinition(3, NeedServiceKind.Bed, Fixtures.B, durationTicks: sleep, assignedActorId: 1) } : null));
    }
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void RestIsScheduledBelowStrainThresholdAndUsesPhysicalCapacity()
    {
        var w = Make(); w.AdvanceTo(59); Assert.DoesNotContain(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.ScheduledRest); RoundTrip(w);
        w.AdvanceTo(75); var goal = Assert.Single(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.ScheduledRest);
        Assert.Equal(60, goal.DueTick); Assert.Equal(DailyGoalStatus.Completed, goal.Status); Assert.True(goal.CompletedActionId > 0);
        var activity = Assert.Single(w.InspectNeedActivities(), a => a.ActionId == goal.CompletedActionId); Assert.Equal(ActionKind.Rest, activity.Kind); Assert.Equal(5, activity.EndedTick - activity.StartedTick);
        Assert.Equal(0, w.InspectOwnNeeds(new(1)).Single(n => n.Kind == NeedKind.WorkStrain).Q); RoundTrip(w);
    }
    [Fact]
    public void MealRequiresSeekAndTheKnownOpportunity()
    {
        var w = Make(hunger: 300); w.AdvanceTo(39); Assert.Equal(0, w.MealsConsumed); w.AdvanceTo(55); Assert.Equal(1, w.MealsConsumed);
        Assert.Equal(DailyGoalStatus.Completed, Assert.Single(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.DutyMeal).Status); RoundTrip(w);
        var unnecessary = Make(hunger: 0); unnecessary.AdvanceTo(55); Assert.Equal(0, unnecessary.MealsConsumed);
        Assert.Equal(DailyGoalStatus.Unnecessary, Assert.Single(unnecessary.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.DutyMeal).Status); RoundTrip(unnecessary);
    }
    [Fact]
    public void SeekCrossingInsideOpenMealWindowWakesRetainedGoal()
    {
        // 297 points plus 60 points/hour at 4400 ms/tick crosses at tick 41,
        // while the meal opportunity is open. Travel and service remain finite.
        var w = Make(hunger: 297, tickMs: 4400); w.AdvanceTo(40); Assert.Equal(0, w.MealsConsumed); Assert.Equal(DailyGoalStatus.Pending, Assert.Single(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.DutyMeal).Status);
        w.AdvanceTo(60); Assert.True(w.MealsConsumed == 1, string.Join("\n", w.Trace.Where(e => e.Tick >= 40).Select(e => e.Tick + " " + e.Type + " " + e.Detail))); RoundTrip(w);
    }
    [Fact]
    public void BusyWorkerTakesDueRestAtNextSafeBoundaryAheadOfMoreWork()
    {
        var w = Make(work: true); w.AdvanceTo(110);
        var goal = Assert.Single(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.ScheduledRest); Assert.Equal(DailyGoalStatus.Completed, goal.Status);
        Assert.True(Assert.Single(w.InspectRepairAgents()).PendingMachines.Count > 0);
        var first = w.Trace.First(e => e.Type == "PlanSelected" && e.Tick >= goal.DueTick);
        Assert.StartsWith("daily-routine/v1", first.Detail); Assert.InRange(first.Tick, 60, 80);
        Assert.True(w.InspectCheckIns()[0].Completed >= 2); Assert.True(w.ActionsFailed == 0, string.Join("\n", w.Trace.Where(e => e.Type is "ActionRequested" or "ActionFailed" or "PlanSelected").Select(e => e.Tick + " " + e.Type + " " + e.Detail))); RoundTrip(w);
    }
    [Fact]
    public void MissingServiceBlocksRetainedRestAndAllowsOrdinaryWork()
    {
        var w = Make(work: true, services: false); w.AdvanceTo(79); var goal = Assert.Single(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.ScheduledRest);
        Assert.Equal(DailyGoalStatus.Pending, goal.Status); Assert.Equal("NoKnownReliefRouteOrEligibleService", goal.Reason); Assert.True(goal.RetryAt > goal.DueTick);
        Assert.Contains(w.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("repair-known-equipment") && e.Tick > goal.DueTick); RoundTrip(w);
    }
    [Fact]
    public void ScheduledSleepDoesNotRequireUrgentSleepPressureAndMailCannotWakeIt()
    {
        var original = Make(); var changed = Make(messages: new[] { new InitialMessage(TerminalFixtures.Draft(1), new(1), 160) });
        original.AdvanceTo(165); changed.AdvanceTo(165);
        Assert.Equal(NeedMode.Sleeping, Assert.Single(changed.InspectNeeds()).Mode); Assert.Equal(ActionKind.Sleep, changed.InspectActor(new(1)).Action);
        Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(changed.InspectMailbox(new(1))).Stage);
        Assert.Equal(original.Trace.Where(e => e.Type == "PlanSelected").Select(e => (e.Tick, e.Detail)), changed.Trace.Where(e => e.Type == "PlanSelected").Select(e => (e.Tick, e.Detail))); RoundTrip(changed);
        changed.AdvanceTo(200); Assert.Equal(DailyGoalStatus.Completed, Assert.Single(changed.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.Sleep).Status); RoundTrip(changed);
    }
    [Fact]
    public void RosterPublicationCannotChangeSleepingCitizensGoalsUntilRead()
    {
        var w = Make(); long newer = w.ScheduleRosterChange(160, new(new(1), 1, Fixtures.B), "new-cohort"); w.AdvanceTo(170);
        Assert.Equal(1, w.InspectRoutines()[0].KnownRoster!.Version); Assert.All(w.InspectDailyGoals(), g => Assert.Equal(1, g.RosterVersion)); Assert.Equal(ActionKind.Sleep, w.InspectActor(new(1)).Action); RoundTrip(w);
        w.AdvanceTo(245); Assert.Equal(newer, w.InspectRoutines()[0].KnownRoster!.Version); Assert.Contains(w.InspectDailyGoals(), g => g.RosterVersion == newer); RoundTrip(w);
    }
    [Fact]
    public void UnknownRosterHasNoDailyCalendarBeforeActualRead()
    {
        var w = Make(known: false); Assert.Empty(w.InspectDailyGoals()); w.AdvanceTo(1); Assert.Empty(w.InspectDailyGoals());
        w.AdvanceTo(75); Assert.NotNull(w.InspectRoutines()[0].KnownRoster); Assert.Contains(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.ScheduledRest); RoundTrip(w);
    }
    [Fact]
    public void EveryBoundaryAndMidSleepCheckpointContinuesWithIdenticalTrace()
    {
        World Variant() { var w = Make(hunger: 300); w.ScheduleRosterChange(160, new(new(1), 1, Fixtures.B), "shift"); return w; }
        var baseline = Variant(); baseline.AdvanceTo(300);
        foreach (long tick in new long[] { -1, 0, 1, 39, 40, 44, 45, 50, 60, 64, 65, 70, 80, 149, 150, 160, 170, 181, 230, 240, 245 })
        {
            var w = Variant(); if (tick >= 0) w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); Assert.Equal(w.Serialize(), copy.Serialize());
            for (long t = Math.Max(0, tick + 1); t <= 300; t++) copy.AdvanceTo(t);
            Assert.Equal(baseline.Serialize(), copy.Serialize()); Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }
    [Fact]
    public void SameDueTickUsesStableReportThenCheckInGoalSequence()
    {
        var w = Make(reports: new[] { new ReportGoalSeed(new(1), TerminalFixtures.Draft(1)) }); w.AdvanceTo(10);
        var posted = Assert.Single(w.Trace, e => e.Type == "ReportGoalCompleted"); var read = w.Trace.First(e => e.Type == "ActionCompleted" && e.Detail == "ReadInbox");
        Assert.True(posted.Tick < read.Tick); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void InterruptedSleepKeepsElapsedBenefitAndDoesNotCompleteGoal()
    {
        var w = Make(sleep: 100); w.Submit(165, new(new(1), ActionKind.Cancel, Fixtures.B)); w.AdvanceTo(165);
        var goal = Assert.Single(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.Sleep); Assert.Equal(DailyGoalStatus.Pending, goal.Status); Assert.Equal(0, goal.CompletedActionId);
        Assert.Equal(NeedActivityStatus.Cancelled, w.InspectNeedActivities().Last().Status); Assert.Equal(NeedMode.Awake, Assert.Single(w.InspectNeeds()).Mode); RoundTrip(w);
    }
    [Fact]
    public void DailyTemplateStaggersBreaksAndKeepsNormalWindowsSeparate()
    {
        var roster = new RosterPayload(1, 1, 0, 864000, 0, 288000, 1234, Fixtures.B);
        var windows = DailyRoutine.Around(roster, 0).Where(w => w.Opens >= 0 && w.Opens < roster.DayTicks).OrderBy(w => w.Opens).ToArray(); Assert.Equal(5, windows.Length);
        Assert.Equal(145234, windows.Single(w => w.Kind == RoutineOpportunityKind.DutyMeal).Opens); Assert.Equal(217234, windows.Single(w => w.Kind == RoutineOpportunityKind.ScheduledRest).Opens);
        // The sleep opportunity opens one configured hour early so a full sleep fits before the pre-duty meal.
        var sleep = windows.Single(w => w.Kind == RoutineOpportunityKind.Sleep);
        Assert.Equal(288000 + 36000, sleep.Closes - sleep.Opens); Assert.Equal(roster.DayTicks - 36000, sleep.Closes);
        Assert.True(sleep.Opens + 288000 <= windows.Single(w => w.Kind == RoutineOpportunityKind.PreDutyMeal).Opens - 36000);
        for (int i = 1; i < windows.Length; i++) Assert.True(windows[i - 1].Closes <= windows[i].Opens);
    }
    [Fact]
    public void SnapshotRejectsACompletedRoutineWithoutItsPhysicalAction()
    {
        var w = Make(); w.AdvanceTo(75); var g = Assert.Single(w.InspectDailyGoals(), g => g.Status == DailyGoalStatus.Completed); var bytes = w.Serialize();
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(g.Id); writer.Write(g.ActorId); writer.Write(g.RosterVersion); writer.Write((int)g.Kind); writer.Write(g.Opens); writer.Write(g.DueTick); writer.Write(g.Closes); writer.Write((int)g.Status);
            writer.Write(g.RetryAt); writer.Write(g.Backoff); writer.Write(g.Reason); writer.Write(g.ServiceId); writer.Write(g.ActiveActionId); writer.Write(g.CompletedActionId); writer.Write(g.EndedTick);
        }
        var block = stream.ToArray(); int at = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        foreach (var mutation in new[] { (at + 36, 999L), (at + 56, -1L), (at + block.Length - 16, 0L), (at + block.Length - 8, 0L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Item2).CopyTo(bad, mutation.Item1); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }
    [Fact]
    public void GoldenDailyOpportunitiesUseOnlyAutonomousPhysicalActions()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "daily_opportunities.json")); Assert.Empty(loaded.Scenario.ScriptedActors);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(14, loaded.Scenario.Assertions.Count);
        Assert.Equal(3, run.Result.Metrics["dailyGoalsCompleted"]); Assert.Equal(0, run.World.ActionsFailed); RoundTrip(run.World);
    }
}
