using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class IncidentResponseTests
{
    private static World Make(int originals = 3, int reserves = 3, int reserveCohort = 0, int tickMs = 60000,
        long cap = 50, int depth = 2, ulong seed = 42, bool trace = true, bool hiddenInstalled = false, long hiddenTravel = 1, bool isolatedFirst = false)
    {
        int population = originals + reserves;
        var people = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "Worker " + i, isolatedFirst && i == 1 ? "specialist" : "worker", Fixtures.A)).ToArray();
        var resources = new[] { new ResourceDefinition("card"), new ResourceDefinition("part", "cartridge") };
        var machineDefinitions = Enumerable.Range(1, originals).Select(i => new MachineDefinition(i, Fixtures.B, "cartridge", "part", new WorkAccess(new[] { isolatedFirst && i == 1 ? "specialist" : "worker" }), installTicks: 3)).ToArray();
        var publication = new PublicKnowledge(1, new[] { new KnownTile(new(1), Fixtures.A, 1), new KnownTile(new(2), Fixtures.B, 1) },
            new[] { new PublicService(1, Fixtures.A, TerminalCapability.Post | TerminalCapability.Records, new[] { "worker", "specialist" }) },
            new PublicWorkCatalog(resources, Array.Empty<RecipeDefinition>(), Array.Empty<PublicWorkstation>(),
                machineDefinitions.Select(m => new PublicEquipment(m.Id, Fixtures.B, "cartridge", "", m.Access.Roles, 3, 1)), 1));
        var items = people.Select(a => new Item(new(100 + a.Id.Value), "card", 1, new(LocationKind.ActorInventory, a.Id.Value)))
            .Concat(hiddenInstalled ? machineDefinitions.Select(m => new Item(new(m.Id), "part", 1, new(LocationKind.MachineSlot, m.Id))) : Array.Empty<Item>());
        var world = new World("incident-response", seed, new WorldConfig(tickDurationMs: tickMs, itemActionTicks: 1, authorityNominalTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Command", Fixtures.A, traversalOverride: hiddenTravel), new Tile(new(2), "Power", Fixtures.B, traversalOverride: hiddenTravel) }, people, items, trace,
            new InformationSeed(new InformationConfig(loginTicks: 1, readBaseTicks: 1, readPerMessageTicks: 0, readMaximumTicks: 1, postTicks: 1, latencyTicks: 2, sessionTicks: 2000),
                new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker", "specialist" }, capacity: population, capabilities: TerminalCapability.Post | TerminalCapability.Records) },
                people.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(100 + a.Id.Value))), publication: publication),
            new WorkSeed(resources, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(), machineDefinitions),
            new JobSystemSeed(machineDefinitions.Select(m => new JobSeed(m.Id, m.Id)), machineDefinitions.Select(m => new AssignmentSeed("initial/" + m.Id, m.Id, new(m.Id), 2000))),
            new RoutineSeed(new RoutineConfig(900, 3, 300, 1, 100), people.Select(a => new RosterAssignmentSeed(a.Id, a.Id.Value <= originals ? 0 : reserveCohort, Fixtures.A))),
            new AuthorityConfig(1000, 0, 4000, 0, 1, 2, 30, cap, depth, 2000));
        for (int i = 1; i <= originals; i++) Claim(world, isolatedFirst && i == 1 ? 4 : 5, i, i, i, i, MessageKind.OrderAccepted);
        return world;
    }
    private static void Claim(World world, long receipt, long actor, long attempt, long job, long order, MessageKind kind)
    {
        long post = receipt - 3;
        world.Submit(post - 1, new(new(actor), ActionKind.Login, Fixtures.A, serviceId: 1));
        world.Submit(post, new(new(actor), ActionKind.PostMessage, Fixtures.A, serviceId: 1,
            message: new MessageDraft(kind, RecipientKind.Authority, default, new(SubjectKind.Machine, job, AttributeKind.Operational), FactValue.Boolean(true), post, job, attempt,
                kind == MessageKind.OrderAccepted ? order : 0)));
    }
    private static MissingReportIncident[] Queue(World w) => w.InspectMissingReports().OrderBy(i => i.OpenedTick).ThenBy(i => i.OpeningSequence).ToArray();

    [Fact]
    public void FifoAdmissionSpendsOneTokenAtPostingAndDefersExcessIncidents()
    {
        var w = Make(); w.AdvanceTo(14); Assert.Equal(3, w.InspectMissingReports().Count);
        Assert.All(w.InspectAdmissionBuckets(), b => Assert.Equal(0, b.Used));
        w.AdvanceTo(15); var queue = Queue(w);
        Assert.Equal(IncidentWorkResponse.Dispatched, queue[0].WorkResponse);
        Assert.All(queue.Skip(1), i => Assert.Equal(IncidentWorkResponse.EscalationDeferred, i.WorkResponse));
        var child = w.InspectWorkAttempts().Single(a => a.Id == queue[0].ReplacementAttemptId);
        Assert.Equal(4, child.ActorId); Assert.Equal(15, child.DispatchTick); Assert.Equal(AssignmentStatus.Active, child.Assignment);
        Assert.Empty(w.InspectLocalOrders(new(4))); Assert.Equal(MessageStage.Posted, w.InspectMessages().Single(m => m.Id == child.OrderMessageId).Stage);
        Assert.All(queue, i => Assert.Equal("PendingPatrol", i.WorkerStatusResponse));
        var bucket = w.InspectAdmissionBuckets().Single(b => b.Cohort == 0); Assert.Equal(6, bucket.KnownOnDutyHeadcount); Assert.Equal(1, bucket.Budget); Assert.Equal(1, bucket.Used);
        w.AdvanceTo(59); Assert.Equal(1, w.InspectMissingReports().Count(i => i.ReplacementAttemptId > 0));
        w.AdvanceTo(60); Assert.Equal(IncidentWorkResponse.Dispatched, Queue(w)[1].WorkResponse);
        Assert.Equal(2, w.InspectMissingReports().Count(i => i.ReplacementAttemptId > 0));
        Assert.All(w.InspectAdmissionBuckets(), b => Assert.True(b.Used <= b.Budget));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void IneligibleQueueSpendsNothingAndRosterPublicationWakesItWithoutChangingCurrentBudget()
    {
        var w = Make(originals: 1, reserves: 1, reserveCohort: 1, cap: 1000); w.AdvanceTo(15);
        Assert.Equal(IncidentWorkResponse.NoEligibleResponder, Assert.Single(w.InspectMissingReports()).WorkResponse);
        Assert.All(w.InspectAdmissionBuckets(), b => Assert.Equal(0, b.Used));
        var before = w.InspectAdmissionBuckets().Single(b => b.Cohort == 0); Assert.Equal(1, before.KnownOnDutyHeadcount);
        w.ScheduleRosterChange(20, new(new(2), 0, Fixtures.A), "available"); w.AdvanceTo(20);
        var incident = Assert.Single(w.InspectMissingReports()); Assert.Equal(IncidentWorkResponse.Dispatched, incident.WorkResponse);
        Assert.Equal(0, incident.AdmissionBucket); Assert.Equal(20, incident.AdmissionTick);
        Assert.Equal(1, w.InspectAdmissionBuckets().Single(b => b.Cohort == 0).KnownOnDutyHeadcount);
        w.AdvanceTo(60); Assert.Equal(2, w.InspectAdmissionBuckets().Single(b => b.Cohort == 0 && b.Bucket == 1).KnownOnDutyHeadcount);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void ReplacementDepthIsBoundedAndLateOriginalResultNeverCompletesChild()
    {
        var w = Make(originals: 1, reserves: 2, cap: 1000); w.AdvanceTo(15);
        var first = Assert.Single(w.InspectMissingReports()); var child = w.InspectWorkAttempts().Single(a => a.Id == first.ReplacementAttemptId);
        Claim(w, 20, child.ActorId, child.Id, 1, child.OrderMessageId, MessageKind.OrderAccepted);
        Claim(w, 24, 1, 1, 1, 1, MessageKind.WorkCompleted); w.AdvanceTo(24);
        Assert.Equal("Accepted", w.InspectWorkAttempts().Single(a => a.Id == child.Id).AuthorityStatus);
        Assert.False(w.InspectMachines()[0].Operational);
        w.AdvanceTo(30); var second = w.InspectMissingReports().Single(i => i.AttemptId == child.Id);
        Assert.Equal(1, second.ReplacementDepth); var grandchild = w.InspectWorkAttempts().Single(a => a.Id == second.ReplacementAttemptId);
        Claim(w, 35, grandchild.ActorId, grandchild.Id, 1, grandchild.OrderMessageId, MessageKind.OrderAccepted);
        w.AdvanceTo(100); var last = w.InspectMissingReports().Single(i => i.AttemptId == grandchild.Id);
        Assert.Equal(2, last.ReplacementDepth); Assert.Equal(IncidentWorkResponse.DepthLimited, last.WorkResponse); Assert.Equal(0, last.ReplacementAttemptId);
        Assert.Equal(3, w.InspectWorkAttempts().Count); Assert.Equal(2, w.InspectAdmissionBuckets().Sum(b => b.Used));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void NonIntegralHourBoundariesUseMillisecondsAndGettersDoNotSpendTokens()
    {
        var w = Make(tickMs: 70000); w.AdvanceTo(51); Assert.Equal(4, w.InspectAdmissionBuckets().Count);
        var bytes = w.Serialize(); w.InspectAdmissionBuckets(); w.InspectMissingReports(); w.AuthorityView(); Assert.Equal(bytes, w.Serialize());
        w.AdvanceTo(52); Assert.Equal(8, w.InspectAdmissionBuckets().Count); Assert.Equal(52, w.InspectAdmissionBuckets().Single(b => b.Bucket == 1 && b.Cohort == 0).OpenedTick);
        w.AdvanceTo(103); Assert.Equal(12, w.InspectAdmissionBuckets().Count); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void HiddenTravelAndMachineTruthCannotChangeSelectionOrAdmission()
    {
        var slow = Make(hiddenTravel: 50); var fast = Make(hiddenInstalled: true); slow.AdvanceTo(60); fast.AdvanceTo(60);
        Assert.Equal(JsonData.Write(slow.InspectAdmissionBuckets()), JsonData.Write(fast.InspectAdmissionBuckets()));
        Assert.Equal(JsonData.Write(slow.InspectMissingReports()), JsonData.Write(fast.InspectMissingReports()));
        Assert.Equal(slow.InspectWorkAttempts().Select(a => a.ActorId), fast.InspectWorkAttempts().Select(a => a.ActorId));
        Assert.NotEqual(slow.InspectMachines()[0].Operational, fast.InspectMachines()[0].Operational);
    }

    [Fact]
    public void IneligibleOlderIncidentIsSkippedWithoutLosingAgeAndCancellationRemovesIt()
    {
        var w = Make(originals: 2, reserves: 1, isolatedFirst: true); w.AdvanceTo(15);
        var queue = Queue(w); Assert.Equal(1, queue[0].AttemptId); Assert.Equal(13, queue[0].OpenedTick);
        Assert.Equal(IncidentWorkResponse.NoEligibleResponder, queue[0].WorkResponse);
        Assert.Equal(IncidentWorkResponse.Dispatched, queue[1].WorkResponse); Assert.Equal(1, w.InspectAdmissionBuckets().Sum(b => b.Used));
        w.ScheduleWorkCancellation(16, 1, "cancel-first"); w.AdvanceTo(120);
        Assert.Equal(IncidentWorkResponse.Cancelled, Queue(w)[0].WorkResponse); Assert.Equal(13, Queue(w)[0].OpenedTick);
        Assert.Equal(1, w.InspectAdmissionBuckets().Sum(b => b.Used)); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void BudgetUsesFloorAndKnownCohortHeadcount()
    {
        var w = Make(originals: 3, reserves: 38); w.AdvanceTo(15);
        var bucket = w.InspectAdmissionBuckets().Single(b => b.Cohort == 0);
        Assert.Equal(41, bucket.KnownOnDutyHeadcount); Assert.Equal(2, bucket.Budget); Assert.Equal(2, bucket.Used);
        Assert.Equal(2, w.InspectMissingReports().Count(i => i.ReplacementAttemptId > 0));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void CanonicalReplacementPhysicallyReadsRepairsAndReportsWithoutScriptedResponse()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "replacement_response.json"));
        Assert.Equal(1, Assert.Single(loaded.Scenario.ScriptedActors).ActorId);
        Assert.False(loaded.Scenario.InitialState.Actors.Single(a => a.Id == 2).Scripted);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(0, run.Result.Metrics["actionsFailed"]); Assert.True(Assert.Single(run.World.InspectMachines()).Operational);
        var incident = Assert.Single(run.World.InspectMissingReports()); Assert.Equal(25, incident.AdmissionTick); Assert.Equal(2, incident.ReplacementAttemptId);
        var order = Assert.Single(run.World.InspectLocalOrders(new(2))); Assert.Equal(LocalOrderStatus.Completed, order.Status); Assert.True(order.ReadTick > 25);
        var attempt = run.World.InspectWorkAttempts().Single(a => a.Id == order.AttemptId);
        Assert.Equal(WorkExecution.CompletedPhysically, attempt.Execution); Assert.Equal("WorkCompleted", attempt.AuthorityStatus);
        Assert.Equal("ReportedComplete", incident.WorkResolution); Assert.True(incident.ReplacementResultMessageId > 0); Assert.Equal("PendingPatrol", incident.WorkerStatusResponse);
        var posted = Assert.Single(run.World.Trace, e => e.Type == "AuthorityOrderPosted" && e.MessageId == attempt.OrderMessageId);
        Assert.Equal(incident.OpeningEventId, posted.CauseId);
        Assert.Contains(TraceChains.Select(run.World.Trace, posted.Id), e => e.Type == "MissingWorkReportOpened");
        foreach (long tick in new long[] { -1, 24, 25, 27, 38, 42, 46, 47, 53 })
        {
            var prefix = loaded.CreateWorld(42); if (tick >= 0) prefix.AdvanceTo(tick);
            var replay = Runs.Execute(loaded, 42, true, startingWorld: World.Restore(prefix.Serialize(), true));
            Assert.Equal(run.Result.Digest, replay.Result.Digest);
            Assert.Equal(run.World.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), replay.World.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void DeferredContinuationsChunkingAndConcurrentRunsAreIdentical()
    {
        var digests = new string[20];
        for (int i = 0; i < 20; i++)
        {
            var direct = Make(seed: (ulong)i, tickMs: 60000 + i * 137); direct.AdvanceTo(130); digests[i] = direct.Digest();
            foreach (long tick in new long[] { -1, 0, 5, 14, 15, 59, 60, 120 })
            {
                var prefix = Make(seed: (ulong)i, tickMs: 60000 + i * 137); if (tick >= 0) prefix.AdvanceTo(tick);
                var bytes = prefix.Serialize(); var resumed = World.Restore(bytes, true); Assert.Equal(bytes, resumed.Serialize()); resumed.AdvanceTo(130);
                Assert.Equal(digests[i], resumed.Digest()); Assert.Equal(direct.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), resumed.Trace.Select(Runs.EventJson));
            }
            var chunked = Make(seed: (ulong)i, tickMs: 60000 + i * 137, trace: false);
            for (int tick = 0; tick <= 130; tick++) { chunked.AdvanceTo(tick); chunked.InspectAdmissionBuckets(); chunked.AuthorityView(); }
            Assert.Equal(digests[i], chunked.Digest());
        }
        foreach (int concurrency in new[] { 2, 4, 8 }) Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
        { var w = Make(seed: (ulong)i, tickMs: 60000 + i * 137); w.AdvanceTo(130); Assert.Equal(digests[i], w.Digest()); });
    }
}
