using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class RoutineTests
{
    private static World Make(int cohort = 1, bool known = true, bool rest = true, int population = 1, ulong seed = 42, bool trace = true, bool cards = true)
    {
        var people = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "Citizen" + i, "worker", Fixtures.A)).ToArray();
        var info = new InformationSeed(new InformationConfig(loginTicks: 1, readBaseTicks: 2, readPerMessageTicks: 0, readMaximumTicks: 2, postTicks: 1, latencyTicks: 2, sessionTicks: 100),
            new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }, capabilities: TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.Records) },
            cards ? people.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(a.Id.Value))) : null);
        return new World("routines", seed, new WorldConfig(itemActionTicks: 5), Fixtures.Catalog,
            new[] { new Tile(new(1), "Command", Fixtures.A, traversalOverride: 2), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 2), new Tile(new(3), "Storage", Fixtures.C, traversalOverride: 2) },
            people, cards ? people.Select(a => new Item(new(a.Id.Value), "card", 1, new ItemLocation(LocationKind.ActorInventory, a.Id.Value))) : Array.Empty<Item>(), trace, info,
            routines: new RoutineSeed(new RoutineConfig(60, 3, 20, 5, 10), people.Select(a => new RosterAssignmentSeed(a.Id, cohort, rest ? Fixtures.B : null, known))));
    }
    private static string Decisions(World w) => string.Join("\n", w.Trace.Where(e => e.Type is "PlanSelected" or "ActionRequested" or "ActionCompleted").Select(Runs.EventJson));

    [Fact]
    public void DailyWindowsWrapExactlyAndCheckInTimesUseTheirAssignedOffset()
    {
        var roster = new RosterPayload(1, 1, 2, 60, 50, 20, 4, null);
        Assert.True(roster.OnDuty(0)); Assert.True(roster.OnDuty(9)); Assert.False(roster.OnDuty(10));
        Assert.False(roster.OnDuty(49)); Assert.True(roster.OnDuty(50)); Assert.True(roster.OnDuty(60));
        Assert.Equal(10, roster.NextBoundary(0)); Assert.Equal(50, roster.NextBoundary(10)); Assert.Equal(70, roster.NextBoundary(50));
        Assert.Equal(54, roster.NextCheckIn(53, true)); Assert.Equal(54, roster.NextCheckIn(54, true)); Assert.Equal(114, roster.NextCheckIn(54, false));
        Assert.Throws<OverflowException>(() => roster.NextBoundary(long.MaxValue));
    }

    [Fact]
    public void OffDutyCitizenPhysicallyTravelsToKnownRestThenChecksInDuringDuty()
    {
        var w = Make(); Assert.Empty(w.Trace); Assert.Equal(-1, w.LastProcessedTick);
        w.AdvanceTo(0); Assert.True(w.InspectActor(new(1)).InTransit); Assert.Equal("GoingOffDuty", w.InspectRoutines()[0].Activity);
        w.AdvanceTo(2); Assert.Equal(Fixtures.B, w.InspectActor(new(1)).Position); Assert.Equal("OffDuty", w.InspectRoutines()[0].Activity);
        w.AdvanceTo(19); Assert.Empty(w.InspectSessions()); Assert.False(w.InspectRoutines()[0].OnDuty);
        w.AdvanceTo(30); Assert.True(w.InspectRoutines()[0].OnDuty); Assert.True(w.InspectCheckIns()[0].Completed > 0);
        w.AdvanceTo(50); Assert.Equal("OffDuty", w.InspectRoutines()[0].Activity); Assert.Equal(Fixtures.B, w.InspectActor(new(1)).Position);
        Assert.DoesNotContain(w.Trace, e => e.Type == "ActionFailed"); Assert.Empty(w.InspectSessions());
    }

    [Fact]
    public void PublicationAndMailboxDeliveryDoNotChangePrivateRosterOrWakeRestingCitizen()
    {
        var original = Make(); var changed = Make();
        long version = changed.ScheduleRosterChange(5, new(new(1), 0, Fixtures.C), "new-shift");
        original.AdvanceTo(19); changed.AdvanceTo(19);
        Assert.Equal(Decisions(original), Decisions(changed)); Assert.Equal(0, changed.PublishedRosters()[0].Cohort);
        var local = changed.InspectRoutines()[0]; Assert.Equal(1, local.KnownRoster!.Cohort); Assert.Equal(1, local.KnownRoster.Version);
        Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(changed.InspectMailbox(new(1))).Stage);
        Assert.Equal(Fixtures.B, changed.InspectActor(new(1)).Position); Assert.Equal("OffDuty", local.Activity);
        changed.AdvanceTo(35); local = changed.InspectRoutines()[0]; Assert.Equal(version, local.KnownRoster!.Version);
        Assert.Equal(Fixtures.C, changed.InspectActor(new(1)).Position); Assert.Equal("OffDuty", local.Activity);
        var fact = changed.InspectKnowledge(new(1)).Single(f => f.Key.Attribute == AttributeKind.RosterVersion);
        Assert.True(fact.Pinned); Assert.Equal(EvidenceKind.MessageRead, fact.SourceKind); Assert.Equal(5, fact.ObservedTick);
        Assert.Equal(2, changed.InspectRecordHistory().Count(r => r.Kind == RecordKind.Roster));
    }

    [Fact]
    public void InitiallyUnknownRosterArrivesThroughActualLoginAndRead()
    {
        var w = Make(known: false); w.AdvanceTo(1); Assert.Null(w.InspectRoutines()[0].KnownRoster);
        w.AdvanceTo(12); Assert.Null(w.InspectRoutines()[0].KnownRoster);
        w.AdvanceTo(18); Assert.Equal(1, w.InspectRoutines()[0].KnownRoster!.Version);
        Assert.True(Assert.Single(w.InspectMailbox(new(1))).FirstReadTick >= 2);
        Assert.Equal(EvidenceKind.MessageRead, w.InspectKnowledge(new(1)).Single(f => f.Key.Attribute == AttributeKind.RosterVersion).SourceKind);
    }

    [Fact]
    public void MissingRestAndMissingCredentialAreExplainedWithoutInventedCapacity()
    {
        var noRest = Make(rest: false); noRest.AdvanceTo(10);
        Assert.Equal("MissingRestDestination", noRest.InspectRoutines()[0].Activity); Assert.Equal(Fixtures.A, noRest.InspectActor(new(1)).Position);
        var noCard = Make(cohort: 0, cards: false); noCard.AdvanceTo(30);
        Assert.Equal("OffDuty", noCard.InspectRoutines()[0].Activity); Assert.Equal(Fixtures.B, noCard.InspectActor(new(1)).Position);
        Assert.Empty(noCard.InspectMessages()); Assert.Empty(noCard.InspectSessions()); Assert.Empty(noCard.InspectItems());
    }

    [Fact]
    public void EndOfDutyStopsMovementAtArrivalAndRetainsInventory()
    {
        var w = Make(cohort: 0); w.Submit(19, new(new(1), ActionKind.Move, Fixtures.C)); w.AdvanceTo(21);
        Assert.Contains(w.Trace, e => e.Type == "ActionCancelled" && e.Tick == 21 && e.Detail == "Move");
        Assert.DoesNotContain(w.Trace, e => e.Type == "TileArrived" && e.Target == Fixtures.C);
        Assert.Single(w.InspectActor(new(1)).Inventory); Assert.Empty(w.InspectResourceClaims());
        w.AdvanceTo(35); Assert.Equal("OffDuty", w.InspectRoutines()[0].Activity);
    }

    [Fact]
    public void StaggeredCohortCheckInsUseActorRandomStreamsAndRealTerminalCapacity()
    {
        var w = Make(cohort: 0, population: 12); var rosters = w.PublishedRosters();
        Assert.All(rosters, r => Assert.InRange(r.CheckInOffset, 0, 4)); Assert.True(rosters.Select(r => r.CheckInOffset).Distinct().Count() > 1);
        for (int i = 1; i <= 12; i++) Assert.Equal(Pcg32.Create(42, "actor/" + i).Next(5), rosters[i - 1].CheckInOffset);
        w.AdvanceTo(19); Assert.True(w.MaximumTerminalQueue > 0);
        Assert.True(w.InspectCheckIns().Sum(c => c.Completed) > 1); Assert.InRange(w.InspectSessions().Count, 0, 1);
    }

    [Fact]
    public void ChangesAreIdempotentAndCannotBeForgedByCitizenPosts()
    {
        var w = Make(cohort: 0); var assignment = new RosterAssignmentSeed(new(1), 2, Fixtures.C);
        long version = w.ScheduleRosterChange(5, assignment, "revision"); var bytes = w.Serialize();
        Assert.Equal(version, w.ScheduleRosterChange(5, assignment, "revision")); Assert.Equal(bytes, w.Serialize());
        Assert.Throws<ArgumentException>(() => w.ScheduleRosterChange(6, assignment, "revision"));
        var forged = new MessageDraft(MessageKind.RosterUpdated, RecipientKind.Account, new(1), new(SubjectKind.Actor, 1, AttributeKind.RosterVersion), FactValue.Integer(900), 0,
            roster: new RosterPayload(900, 1, 0, 60, 0, 20, 0, Fixtures.A));
        w.Submit(0, new(new(1), ActionKind.Login, Fixtures.A, serviceId: 1));
        w.Submit(1, new(new(1), ActionKind.PostMessage, Fixtures.A, serviceId: 1, message: forged));
        w.AdvanceTo(1); Assert.Contains(w.Trace, e => e.Type == "ActionFailed" && e.Detail == "InvalidMessage");
        Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.Roster?.Version == 900);
    }

    [Fact]
    public void GeneratedRosterContinuationsChunkingTracingAndConcurrentRunsAgree()
    {
        World Variant(int i, bool trace = true)
        {
            var w = Make(i % 3, population: 3, seed: (ulong)i, trace: trace);
            w.ScheduleRosterChange(7 + i % 5, new(new(1), (i + 1) % 3, Fixtures.C), "update"); return w;
        }
        var expected = new string[20];
        for (int i = 0; i < 20; i++)
        {
            var baseline = Variant(i); baseline.AdvanceTo(125); expected[i] = baseline.Digest();
            foreach (long tick in new long[] { -1, 0, 2, 8, 19, 20, 39, 60, 80 })
            {
                var w = Variant(i); if (tick >= 0) w.AdvanceTo(tick); var bytes = w.Serialize(); var restored = World.Restore(bytes, true);
                Assert.Equal(bytes, restored.Serialize()); restored.AdvanceTo(125); Assert.Equal(expected[i], restored.Digest());
                Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
            }
            var chunked = Variant(i, false);
            for (int tick = 0; tick <= 125; tick++) { chunked.AdvanceTo(tick); var bytes = chunked.Serialize(); chunked.InspectRosters(); chunked.PublishedRosters(); chunked.InspectRoutines(); chunked.ActorView(new(1)); Assert.Equal(bytes, chunked.Serialize()); }
            Assert.Equal(expected[i], chunked.Digest());
        }
        foreach (int concurrency in new[] { 2, 4, 8 }) Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
        { var w = Variant(i); w.AdvanceTo(125); Assert.Equal(expected[i], w.Digest()); });
    }

    [Fact]
    public void FiniteRepairFinishesAfterDutyAndRetainsItsKnownResultWithoutStartingMoreWork()
    {
        var info = new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }) },
            new[] { new CredentialSeed(1, new(1), new(1), new(1)) },
            new[] { new FactSeed(new(1), new(SubjectKind.Machine, 1, AttributeKind.RequiredResource), new(FactValueKind.Text, text: "part")) },
            repairGoals: new[] { new RepairGoalSeed(new(1), 1) });
        var w = new World("routine-repair", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Command", Fixtures.A, traversalOverride: 2), new Tile(new(2), "Power", Fixtures.B, traversalOverride: 2) },
            new[] { new ActorSeed(new(1), "Engineer", "worker", Fixtures.A) },
            new[] { new Item(new(1), "card", 1, new(LocationKind.ActorInventory, 1)), new Item(new(2), "part", 1, new(LocationKind.ActorInventory, 1)) }, true, info,
            new WorkSeed(new[] { new ResourceDefinition("card"), new ResourceDefinition("part", "cartridge") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
                new[] { new MachineDefinition(1, Fixtures.B, "cartridge", "part", new WorkAccess(new[] { "worker" }), installTicks: 30) }),
            routines: new RoutineSeed(new RoutineConfig(60, 3, 20, 1, 10), new[] { new RosterAssignmentSeed(new(1), 0, Fixtures.B) }));
        w.AdvanceTo(20); Assert.Equal(ActionKind.Install, w.InspectActor(new(1)).Action);
        Assert.False(w.InspectMachines()[0].Operational); Assert.Equal("FinishingAction", w.InspectRoutines()[0].Activity);
        var resumed = World.Restore(w.Serialize()); w.AdvanceTo(50); resumed.AdvanceTo(50); Assert.Equal(w.Digest(), resumed.Digest());
        Assert.True(w.InspectMachines()[0].Operational); Assert.Equal(1, w.InspectRepairAgents()[0].CompletedRepairs);
        Assert.Equal("OffDuty", w.InspectRoutines()[0].Activity); Assert.Empty(w.InspectResourceClaims());
        Assert.DoesNotContain(w.Trace, e => e.Type == "ActionFailed");
        Assert.DoesNotContain(w.Trace, e => e.Type == "ActionStarted" && e.Tick >= 20 && e.Detail is "Install" or "ProduceRecipe");
    }

    [Fact]
    public void OlderScheduledPublicationCannotRollBackCurrentInstitutionalRoster()
    {
        var w = Make(); long old = w.ScheduleRosterChange(15, new(new(1), 2, Fixtures.C), "older");
        long newer = w.ScheduleRosterChange(5, new(new(1), 0, Fixtures.A), "newer"); w.AdvanceTo(19);
        Assert.Equal(newer, w.PublishedRosters()[0].Version);
        Assert.Contains(w.Trace, e => e.Type == "RosterPublicationSuperseded");
        Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.Roster?.Version == old);
        var last = w.InspectRecordHistory().Last(r => r.Kind == RecordKind.Roster);
        Assert.Equal(newer, last.Fields.Single(f => f.Attribute == AttributeKind.RosterVersion).Value.Number);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void InvalidDayCohortAndRestLocationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new RoutineConfig(60, 3, 21, 5));
        Assert.Throws<ArgumentException>(() => new RoutineConfig(61, 3, 20, 5));
        Assert.Throws<ArgumentException>(() => new RoutineConfig(60, 3, 20, 21));
        var w = Make(); var bytes = w.Serialize();
        Assert.Throws<ArgumentException>(() => w.ScheduleRosterChange(5, new(new(1), 3), "bad-cohort"));
        Assert.Throws<ArgumentException>(() => w.ScheduleRosterChange(5, new(new(1), 0, new Coord(999, 0, 0)), "unknown-rest"));
        Assert.Equal(bytes, w.Serialize());
    }

    [Fact]
    public void CanonicalCohortScenarioHasRealContentionAndSelfContainedReplay()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "roster_routines.json"));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(0, run.Result.Metrics["actionsFailed"]); Assert.True(run.Result.Metrics["maximumTerminalQueue"] > 1);
        Assert.Equal(60, run.Result.RoutineConfiguration!.DayTicks); Assert.Equal(7, run.Result.Metrics["rosterVersions"]);
        var initial = loaded.CreateWorld(42); var replay = Runs.Execute(loaded, 42, true, startingWorld: World.Restore(initial.Serialize(), true));
        Assert.Equal(run.Result.Digest, replay.Result.Digest); Assert.Equal(run.World.Trace.Select(Runs.EventJson), replay.World.Trace.Select(Runs.EventJson));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("terminal")]
    [InlineData("credential")]
    public void InterruptedRosterReadDoesNotAdoptOrMarkRead(string interruption)
    {
        var w = Make(known: false);
        w.Submit(4, new(new(1), ActionKind.Login, Fixtures.A, serviceId: 1));
        w.Submit(6, new(new(1), ActionKind.ReadInbox, Fixtures.A, serviceId: 1));
        if (interruption == "cancel") w.Submit(7, new(new(1), ActionKind.Cancel, Fixtures.A));
        else if (interruption == "terminal") w.ScheduleTerminalChange(7, 1, false, false);
        else w.ScheduleCredentialRevocation(7, 1);
        w.AdvanceTo(6); Assert.Equal(ActionKind.ReadInbox, w.InspectActor(new(1)).Action);
        var resumed = World.Restore(w.Serialize()); w.AdvanceTo(10); resumed.AdvanceTo(10); Assert.Equal(w.Digest(), resumed.Digest());
        Assert.Null(w.ActorView(new(1)).KnownRoster); Assert.Null(w.InspectRoutines()[0].KnownRoster);
        var message = Assert.Single(w.InspectMailbox(new(1))); Assert.Null(message.FirstReadTick); Assert.Equal(MessageStage.DeliveredToMailbox, message.Stage);
        Assert.DoesNotContain(w.Trace, e => e.Type == "RosterRead");
    }
}
