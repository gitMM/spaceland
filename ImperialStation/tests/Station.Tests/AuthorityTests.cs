using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class AuthorityTests
{
    private static AuthorityConfig Policy => new(1000, 0, 4000, 0, 1, 2, 30);
    private static World Make(AuthorityConfig? policy = null, bool roster = false, bool installed = false, int population = 1,
        ulong seed = 42, long workTicks = 3, long nominal = 1, long physicalCost = 1, long latency = 2, bool trace = true, IEnumerable<InitialMessage>? initial = null)
    {
        var people = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "Engineer" + i, "worker", Fixtures.A)).ToArray();
        var resources = new[] { new ResourceDefinition("card"), new ResourceDefinition("part", "cartridge") };
        var terminals = new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }, capabilities: TerminalCapability.Post | TerminalCapability.Records) };
        var equipment = new[] { new PublicEquipment(1, Fixtures.B, "cartridge", "", new[] { "worker" }, workTicks, 1) };
        var publication = new PublicKnowledge(1, new[] { new KnownTile(new(1), Fixtures.A, 1), new KnownTile(new(2), Fixtures.B, 1), new KnownTile(new(3), Fixtures.C, 1) },
            new[] { new PublicService(1, Fixtures.A, TerminalCapability.Post | TerminalCapability.Records, new[] { "worker" }) },
            new PublicWorkCatalog(resources, Array.Empty<RecipeDefinition>(), Array.Empty<PublicWorkstation>(), equipment, 1));
        return new World("authority-reports", seed, new WorldConfig(itemActionTicks: 1, authorityNominalTicks: nominal), Fixtures.Catalog,
            new[] { new Tile(new(1), "Command", Fixtures.A, traversalOverride: physicalCost), new Tile(new(2), "Power", Fixtures.B, traversalOverride: physicalCost), new Tile(new(3), "Storage", Fixtures.C, traversalOverride: physicalCost) },
            people, people.Select(a => new Item(new(100 + a.Id.Value), "card", 1, new(LocationKind.ActorInventory, a.Id.Value)))
                .Append(new Item(new(1), "part", 1, new(installed ? LocationKind.MachineSlot : LocationKind.TileContainer, installed ? 1 : 3))), trace,
            new InformationSeed(new InformationConfig(loginTicks: 1, readBaseTicks: 1, readPerMessageTicks: 0, readMaximumTicks: 1, postTicks: 1, latencyTicks: latency, sessionTicks: 100, queryTicks: 2),
                terminals, people.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(100 + a.Id.Value))), messages: initial, publication: publication),
            new WorkSeed(resources, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(), new[] { new MachineDefinition(1, Fixtures.B, "cartridge", "part", new WorkAccess(new[] { "worker" }), installTicks: workTicks) }),
            new JobSystemSeed(new[] { new JobSeed(1, 1) }, new[] { new AssignmentSeed("initial", 1, new(1), 2000) }),
            roster ? new RoutineSeed(new RoutineConfig(30, 3, 10, 1, 10), people.Select(a => new RosterAssignmentSeed(a.Id, 0, Fixtures.A))) : null,
            policy ?? Policy);
    }
    private static void Claim(World w, long receipt, MessageKind kind, long attempt = 1, long order = 1, long actor = 1, string key = "")
    {
        long post = receipt - w.Information!.LatencyTicks - w.Information.PostTicks;
        w.Submit(post - w.Information.LoginTicks, new(new(actor), ActionKind.Login, Fixtures.A, serviceId: 1));
        var draft = new MessageDraft(kind, RecipientKind.Authority, default, new(SubjectKind.Machine, 1, AttributeKind.Operational), FactValue.Boolean(true), post,
            1, attempt, kind is MessageKind.OrderAccepted or MessageKind.OrderRejected ? order : 0, operationKey: key);
        w.Submit(post, new(new(actor), ActionKind.PostMessage, Fixtures.A, serviceId: 1, message: draft));
    }

    [Fact]
    public void ExpectationBeginsOnAcknowledgmentReceiptNotDispatchOrPosting()
    {
        var w = Make(); Claim(w, 5, MessageKind.OrderAccepted);
        w.AdvanceTo(3); Assert.Empty(w.InspectExpectations()); Assert.Equal(AcknowledgmentStatus.Awaiting, w.InspectAcknowledgmentWatches()[0].Status);
        w.AdvanceTo(5); var e = Assert.Single(w.InspectExpectations());
        Assert.Equal(5, e.AcknowledgmentTick); Assert.Equal(9, e.UnscaledEstimate); Assert.Equal(14, e.Deadline);
        Assert.Equal(1, e.OriginTerminalId); Assert.Equal(1, e.ReturnTerminalId); Assert.Equal(ExpectationStatus.AwaitingResult, e.Status);
        Assert.Equal(AcknowledgmentStatus.Acknowledged, w.InspectAcknowledgmentWatches()[0].Status);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Theory]
    [InlineData(MessageKind.WorkCompleted)]
    [InlineData(MessageKind.WorkBlocked)]
    [InlineData(MessageKind.UnableToContinue)]
    public void QualifyingResultDeliveredAtDeadlineIsOnTimeAndDoesNotChangePhysicalTruth(MessageKind kind)
    {
        var w = Make(); Claim(w, 5, MessageKind.OrderAccepted); Claim(w, 14, kind); w.AdvanceTo(20);
        Assert.Equal(ExpectationStatus.Satisfied, w.InspectExpectations()[0].Status); Assert.Equal(14, w.InspectExpectations()[0].ResultTick);
        Assert.Empty(w.InspectMissingReports()); Assert.Equal(1000, Assert.Single(w.InspectLearnedDurations()).Ratio); Assert.Equal(1, w.InspectLearnedDurations()[0].Samples);
        Assert.False(w.InspectMachines()[0].Operational); Assert.Equal(WorkExecution.Assigned, w.InspectJobs()[0].PhysicalStatus);
        Assert.Equal(FactValue.Boolean(true), Assert.Single(w.InspectBeliefs()).Value);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void OverdueOpensOneIncidentAndLateReportSatisfiesResponsivenessWithoutDeclaringTaskDone()
    {
        var w = Make(); Claim(w, 5, MessageKind.OrderAccepted); Claim(w, 23, MessageKind.WorkBlocked); w.AdvanceTo(14);
        var incident = Assert.Single(w.InspectMissingReports()); Assert.Equal(14, incident.OpenedTick); Assert.Equal("MissingWorkReport", incident.Kind);
        Assert.Equal(ExpectationStatus.Overdue, w.InspectExpectations()[0].Status); Assert.False(w.InspectMachines()[0].Operational);
        var resumed = World.Restore(w.Serialize()); w.AdvanceTo(25); resumed.AdvanceTo(25); Assert.Equal(w.Digest(), resumed.Digest());
        Assert.Single(w.InspectMissingReports()); Assert.True(w.InspectMissingReports()[0].ResponseMessageId > 0);
        Assert.Equal(ExpectationStatus.Satisfied, w.InspectExpectations()[0].Status); Assert.Equal(1500, w.InspectLearnedDurations()[0].Ratio);
        Assert.False(w.InspectMachines()[0].Operational);
        Assert.Contains(w.InspectRecordHistory(), r => r.Kind == RecordKind.MissingWorkReport && r.Fields.Any(f => f.Value.Text == "ResponseReceivedTaskUnverified"));
    }

    [Fact]
    public void LearnedCorrectionChangesNewDeadlinesOnlyAndRepeatedResultsDoNotResample()
    {
        var w = Make(); Claim(w, 5, MessageKind.OrderAccepted); Claim(w, 23, MessageKind.WorkCompleted); w.AdvanceTo(23);
        long attempt = w.ScheduleWorkAssignment(24, new("next", 1, new(1), 2000)); w.AdvanceTo(24);
        long order = w.InspectWorkAttempts().Single(a => a.Id == attempt).OrderMessageId;
        Claim(w, 29, MessageKind.OrderAccepted, attempt, order); Claim(w, 33, MessageKind.WorkCompleted, key: "repeat"); w.AdvanceTo(33);
        var later = w.InspectExpectations().Single(e => e.AttemptId == attempt);
        Assert.Equal(1500, later.RatioAtCreation); Assert.Equal(1500, later.Slack); Assert.Equal(43, later.Deadline);
        Assert.Equal(14, w.InspectExpectations()[0].Deadline); Assert.Equal(1, w.InspectLearnedDurations()[0].Samples);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        Claim(w, 43, MessageKind.WorkCompleted, attempt, order); w.AdvanceTo(43);
        Assert.Equal(2, w.InspectLearnedDurations()[0].Samples); Assert.Equal(1527, w.InspectLearnedDurations()[0].Ratio);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void KnownOffDutyGapAndCheckInWindowExtendDeadlineAndLaterRosterDoesNotRewriteIt()
    {
        var w = Make(roster: true); Claim(w, 5, MessageKind.OrderAccepted); w.ScheduleRosterChange(6, new(new(1), 1, Fixtures.A), "new-roster");
        w.AdvanceTo(5); var e = Assert.Single(w.InspectExpectations());
        Assert.Equal(14, e.BaseDeadline); Assert.Equal(21, e.OffDutyExtension); Assert.Equal(35, e.Deadline); Assert.Equal(1, e.RosterVersion);
        Assert.Equal(51, w.InspectAcknowledgmentWatches()[0].Deadline);
        w.AdvanceTo(14); Assert.Equal(35, w.InspectExpectations()[0].Deadline); Assert.Empty(w.InspectMissingReports());
        w.AdvanceTo(35); Assert.Single(w.InspectMissingReports()); Assert.Equal(1, w.InspectMissingReports()[0].Cohort);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void UnacknowledgedOrderHasItsOwnWatchAndNeverCreatesMissingWorkReport()
    {
        var w = Make(); w.AdvanceTo(30); Assert.Equal(AcknowledgmentStatus.Overdue, Assert.Single(w.InspectAcknowledgmentWatches()).Status);
        Assert.Empty(w.InspectExpectations()); Assert.Empty(w.InspectMissingReports());
        Assert.Null(w.InspectMessages()[0].FirstReadTick); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        Claim(w, 35, MessageKind.OrderAccepted); w.AdvanceTo(35); Assert.Equal(35, w.InspectExpectations()[0].AcknowledgmentTick);
        Assert.Equal(44, w.InspectExpectations()[0].Deadline);
    }

    [Fact]
    public void WrongAccountAndUnrelatedMessagesCannotSatisfyExpectation()
    {
        var w = Make(population: 2); Claim(w, 5, MessageKind.OrderAccepted); Claim(w, 10, MessageKind.Observation); Claim(w, 14, MessageKind.WorkCompleted, actor: 2);
        w.AdvanceTo(14); Assert.Equal(ExpectationStatus.Overdue, w.InspectExpectations()[0].Status);
        Assert.Empty(w.InspectLearnedDurations()); Assert.Single(w.InspectMissingReports());
        Assert.Contains(w.Trace, e => e.Type == "WorkReportRejected" && e.Detail == "AttemptOrAccountMismatch");
    }

    [Fact]
    public void CancellationStopsWatchdogButLateResultRetainsAttemptAndSingleSample()
    {
        var w = Make(); Claim(w, 5, MessageKind.OrderAccepted); w.ScheduleWorkCancellation(10, 1, "cancel"); Claim(w, 23, MessageKind.UnableToContinue); w.AdvanceTo(24);
        Assert.Equal(ExpectationStatus.Cancelled, w.InspectExpectations()[0].Status); Assert.Equal(23, w.InspectExpectations()[0].ResultTick);
        Assert.Empty(w.InspectMissingReports()); Assert.Equal(1, w.InspectLearnedDurations()[0].Samples);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void EarlyResultAndMissingTerminalEvidenceDoNotInventEstimates()
    {
        var w = Make(); Claim(w, 4, MessageKind.WorkCompleted); Claim(w, 8, MessageKind.OrderAccepted); w.AdvanceTo(8);
        Assert.Equal(ExpectationStatus.AwaitingResult, w.InspectExpectations()[0].Status); Assert.Empty(w.InspectLearnedDurations());
        Assert.Contains(w.Trace, e => e.Type == "ResultBeforeAcknowledgment");
        var seed = new InitialMessage(new MessageDraft(MessageKind.OrderAccepted, RecipientKind.Authority, default, new(SubjectKind.Attempt, 1, AttributeKind.WorkStatus), FactValue.Integer(1), 0, 1, 1, 2), new(1), 5);
        var absent = Make(initial: new[] { seed }); absent.AdvanceTo(40);
        Assert.Equal(ExpectationStatus.Unestimated, absent.InspectExpectations()[0].Status); Assert.Equal(-1, absent.InspectExpectations()[0].Deadline);
        Assert.Empty(absent.InspectMissingReports()); Assert.Equal(absent.Serialize(), World.Restore(absent.Serialize()).Serialize());
    }

    [Fact]
    public void AuthorityDecisionIgnoresHiddenMachineTruthActualRouteCostAndCitizenKnowledge()
    {
        var faulty = Make(installed: false, physicalCost: 50); var working = Make(installed: true, physicalCost: 1);
        Claim(faulty, 5, MessageKind.OrderAccepted); Claim(working, 5, MessageKind.OrderAccepted); faulty.AdvanceTo(5); working.AdvanceTo(5);
        Assert.Equal(JsonData.Write(faulty.InspectExpectations()), JsonData.Write(working.InspectExpectations()));
        Assert.Equal(faulty.Trace.Where(e => e.Type == "ReportExpectationCreated").Select(e => e.Detail), working.Trace.Where(e => e.Type == "ReportExpectationCreated").Select(e => e.Detail));
        Assert.Equal(9, faulty.InspectExpectations()[0].UnscaledEstimate); Assert.NotEqual(faulty.InspectMachines()[0].Operational, working.InspectMachines()[0].Operational);
        var bytes = faulty.Serialize(); faulty.AuthorityView(); faulty.InspectExpectations(); faulty.InspectMissingReports(); faulty.InspectLearnedDurations(); Assert.Equal(bytes, faulty.Serialize());
    }

    [Fact]
    public void ExactRoundingOffDutyBoundaryClampsAndCheckedEma()
    {
        var roster = new RosterPayload(1, 1, 0, 30, 0, 10, 0, null);
        var plain = new AuthorityConfig(1000, 0, 4000, 0);
        Assert.Equal(0, AuthorityEstimates.Deadline(1, 9, 1000, plain, roster, 1).OffDutyExtension);
        Assert.Equal(21, AuthorityEstimates.Deadline(1, 10, 1000, plain, roster, 1).OffDutyExtension);
        Assert.Equal(5, AuthorityEstimates.Deadline(1, 3, 1250, plain, roster: null, checkInWindow: 0).Tick);
        var defaults = new AuthorityConfig(); Assert.Equal(1500, AuthorityEstimates.Deadline(0, 1, 1000, defaults, null, 0).Slack);
        Assert.Equal(4000, AuthorityEstimates.Deadline(0, 1, 9000, defaults, null, 0).Slack);
        Assert.Equal(1125, AuthorityEstimates.Learn(1000, 5, 23, 9, defaults));
        Assert.Equal(500, AuthorityEstimates.Learn(1000, 5, 5, 9, Policy));
        Assert.Throws<OverflowException>(() => AuthorityEstimates.Learn(1000, 0, long.MaxValue, 1, defaults));
        Assert.Throws<OverflowException>(() => AuthorityEstimates.Deadline(0, long.MaxValue, 1000, defaults, null, 0));
        Assert.Throws<ArgumentException>(() => new AuthorityConfig(emaNumerator: 2, emaDenominator: 1));
    }

    [Fact]
    public void GeneratedReceiptSchedulesContinueAcrossDeadlinesAndConcurrentWorlds()
    {
        (World World, long Deadline) Variant(int i, bool trace = true)
        {
            var w = Make(seed: (ulong)i, workTicks: 3 + i % 5, nominal: 1 + i % 3, latency: 1 + i % 3, trace: trace);
            long estimate = AuthorityEstimates.Repair(w.AuthorityView(), 1, 1, 1)!.Total;
            long deadline = AuthorityEstimates.Deadline(5, estimate, 1000, Policy, null, 0).Tick;
            Claim(w, 5, MessageKind.OrderAccepted); Claim(w, deadline + i % 2, new[] { MessageKind.WorkCompleted, MessageKind.WorkBlocked, MessageKind.UnableToContinue }[i % 3]); return (w, deadline);
        }
        var digests = new string[20];
        for (int i = 0; i < 20; i++)
        {
            var (baseline, deadline) = Variant(i); baseline.AdvanceTo(deadline + 10); digests[i] = baseline.Digest();
            foreach (long tick in new[] { -1, 0, 3, 5, deadline - 1, deadline, deadline + 1 })
            {
                var (w, _) = Variant(i); if (tick >= 0) w.AdvanceTo(tick); var bytes = w.Serialize(); var restored = World.Restore(bytes, true);
                Assert.Equal(bytes, restored.Serialize()); restored.AdvanceTo(deadline + 10); Assert.Equal(digests[i], restored.Digest());
                Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
            }
            var (chunked, _) = Variant(i, false); for (int tick = 0; tick <= deadline + 10; tick++) { chunked.AdvanceTo(tick); chunked.AuthorityView(); chunked.InspectExpectations(); }
            Assert.Equal(digests[i], chunked.Digest());
        }
        foreach (int concurrency in new[] { 2, 4, 8 }) Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
        { var (w, deadline) = Variant(i); w.AdvanceTo(deadline + 10); Assert.Equal(digests[i], w.Digest()); });
    }

    [Fact]
    public void OverflowRemainsAnInspectableFailedWorldWithoutFabricatedDeadline()
    {
        var w = Make(new AuthorityConfig(1000, 0, 4000, long.MaxValue)); Claim(w, 5, MessageKind.OrderAccepted);
        Assert.Throws<OverflowException>(() => w.AdvanceTo(5)); Assert.NotNull(w.RuntimeError); Assert.Empty(w.InspectExpectations());
        var restored = World.Restore(w.Serialize()); Assert.Equal(w.Serialize(), restored.Serialize());
        Assert.Throws<InvalidOperationException>(() => restored.AdvanceTo(6));
    }

    [Theory]
    [InlineData("authority_reports", 0, true)]
    [InlineData("late_report", 1, false)]
    public void CanonicalScenariosPreserveClaimsTruthAndReplay(string name, long incidents, bool operational)
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", name + ".json"));
        var run = Runs.Execute(loaded, 42, true);
        Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(incidents, run.Result.Metrics["missingReportsOpened"]);
        Assert.Equal(0, run.Result.Metrics["actionsFailed"]);
        Assert.Equal(operational, Assert.Single(run.World.InspectMachines()).Operational);
        Assert.Equal(ExpectationStatus.Satisfied, Assert.Single(run.World.InspectExpectations()).Status);
        Assert.Equal(1, Assert.Single(run.World.InspectLearnedDurations()).Samples);
        if (operational) Assert.Empty(loaded.Scenario.ScriptedActors);
        else
        {
            Assert.Equal(35, run.World.InspectExpectations()[0].Deadline);
            Assert.Equal(40, run.World.InspectExpectations()[0].ResultTick);
            Assert.Equal(2444, run.World.InspectLearnedDurations()[0].Ratio);
            Assert.Equal(FactValue.Boolean(true), Assert.Single(run.World.InspectBeliefs()).Value);
        }
        var start = loaded.CreateWorld(42);
        var replay = Runs.Execute(loaded, 42, true, startingWorld: World.Restore(start.Serialize(), true));
        Assert.Equal(run.Result.Digest, replay.Result.Digest);
        Assert.Equal(run.World.Trace.Select(Runs.EventJson), replay.World.Trace.Select(Runs.EventJson));
    }
}
