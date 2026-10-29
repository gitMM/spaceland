using Station.Contracts;
using Station.Core;
using Station.Runner;
using Station.Scenarios;
using Xunit;

namespace Station.Tests;

public sealed class JobTests
{
    private static readonly Id<ActorTag> Worker = new(1);
    private static readonly Coord Home = new(0, 0, 0);
    private static World Make(int distance = 1, long? firstCheckIn = 0, long readTicks = 1, long postTicks = 1, bool falseBelief = false, bool hasPart = true, ulong seed = 42, long expiry = 200, long interval = 100)
    {
        var at = new Coord(distance, 0, 0); var access = new WorkAccess(new[] { "worker" }, "tool");
        var work = new WorkSeed(new[] { new ResourceDefinition("card"), new ResourceDefinition("tool"), new ResourceDefinition("part", "slot") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
            new[] { new MachineDefinition(1, at, "slot", "part", access, installTicks: 3, inspectTicks: 2) });
        var items = new List<Item> { new(new(1), "card", 1, new(LocationKind.ActorInventory, 1)), new(new(2), "tool", 1, new(LocationKind.ActorInventory, 1)), new(new(4), "card", 1, new(LocationKind.ActorInventory, 2)) };
        if (hasPart) items.Add(new(new(3), "part", 1, new(LocationKind.ActorInventory, 1)));
        var facts = new List<FactSeed> { new(Worker, new(SubjectKind.Machine, 1, AttributeKind.RequiredResource), new(FactValueKind.Text, text: "part")) };
        if (falseBelief) facts.Add(new(Worker, new(SubjectKind.Machine, 1, AttributeKind.Operational), FactValue.Boolean(true)));
        var info = new InformationSeed(new InformationConfig(loginTicks: 1, readBaseTicks: readTicks, readPerMessageTicks: 0, readMaximumTicks: readTicks, postTicks: postTicks, latencyTicks: 1, sessionTicks: 500),
            new[] { new TerminalSeed(1, Home, new[] { "worker" }) }, new[] { new CredentialSeed(1, Worker, new(1), new(1)), new CredentialSeed(2, new(2), new(2), new(4)) }, facts: facts);
        var jobs = new JobSystemSeed(new[] { new JobSeed(1, 1) }, new[] { new AssignmentSeed("initial", 1, Worker, expiry) },
            firstCheckIn.HasValue ? new[] { new CheckInSeed(Worker, firstCheckIn.Value, interval) } : null);
        return new World("jobs", seed, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            Enumerable.Range(0, distance + 1).Select(i => new Tile(new(i + 1), "Industrial", new(i, 0, 0), traversalOverride: 2)),
            new[] { new ActorSeed(Worker, "Worker", "worker", Home), new ActorSeed(new(2), "Replacement", "worker", Home) }, items, true, info, work, jobs);
    }
    [Fact]
    public void OrderMustBeReadAndAcceptancePostedBeforeAutonomousWork()
    {
        var w = Make(); w.AdvanceTo(1);
        Assert.Empty(w.InspectLocalOrders(Worker)); Assert.Equal(MessageStage.DeliveredToMailbox, w.InspectMessages()[0].Stage);
        Assert.Empty(w.InspectRepairAgents()); Assert.Null(w.InspectWorkAttempts()[0].AcknowledgmentReceivedTick);
        w.AdvanceTo(2); Assert.Equal(LocalOrderStatus.AcknowledgmentPending, Assert.Single(w.InspectLocalOrders(Worker)).Status);
        Assert.Equal(WorkExecution.Assigned, w.InspectWorkAttempts()[0].Execution);
        w.AdvanceTo(3); Assert.Equal(ActionKind.PostMessage, w.InspectActor(Worker).Action); Assert.Empty(w.InspectRepairAgents());
        w.AdvanceTo(4); Assert.Equal(LocalOrderStatus.Working, w.InspectLocalOrders(Worker)[0].Status); Assert.True(w.InspectActor(Worker).InTransit);
        Assert.Null(w.InspectWorkAttempts()[0].AcknowledgmentReceivedTick);
        w.AdvanceTo(5); Assert.Equal(5, w.InspectWorkAttempts()[0].AcknowledgmentReceivedTick);
        w.AdvanceTo(9); Assert.Equal(WorkExecution.CompletedPhysically, w.InspectWorkAttempts()[0].Execution);
        Assert.Equal("Accepted", w.InspectJobs()[0].AuthorityStatus); Assert.True(w.InspectMachines()[0].Operational);
        w.AdvanceTo(15); Assert.Equal("WorkCompleted", w.InspectJobs()[0].AuthorityStatus); Assert.Equal(2, w.InspectWorkAttempts()[0].ReportMessageIds.Count);
        Assert.Empty(w.InspectSessions()); Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting();
        Assert.All(w.Trace.Where(e => e.Type == "ActionRequested"), e => Assert.True(e.CauseId > 0));
    }
    [Fact]
    public void DeliveredUnreadOrderDoesNotCreateAwarenessOrWakeWorker()
    {
        var w = Make(firstCheckIn: null); w.AdvanceTo(100);
        Assert.Empty(w.InspectLocalOrders(Worker)); Assert.Equal(0, w.ActionsCompleted);
        Assert.Empty(w.InspectRepairAgents()); Assert.Empty(w.InspectAgents()); Assert.Equal("Assigned", w.InspectJobs()[0].AuthorityStatus);
        Assert.DoesNotContain(w.InspectKnowledge(Worker), f => f.Key.SubjectKind == SubjectKind.Attempt);
    }
    [Fact]
    public void CancelledReadAndPostCannotStartWork()
    {
        var read = Make(readTicks: 3); read.Submit(2, new(Worker, ActionKind.Cancel, Home)); read.AdvanceTo(3);
        Assert.Empty(read.InspectLocalOrders(Worker)); Assert.Equal(MessageStage.DeliveredToMailbox, read.InspectMessages()[0].Stage);
        var post = Make(postTicks: 3); post.Submit(4, new(Worker, ActionKind.Cancel, Home)); post.AdvanceTo(10);
        Assert.Equal(LocalOrderStatus.AcknowledgmentPending, post.InspectLocalOrders(Worker)[0].Status);
        Assert.Empty(post.InspectRepairAgents()); Assert.Null(post.InspectWorkAttempts()[0].AcknowledgmentReceivedTick);
        Assert.DoesNotContain(post.InspectMessages(), m => m.Draft.Kind == MessageKind.OrderAccepted);
    }
    [Fact]
    public void ReassignmentDoesNotStopOfflineWorkOrReattributeLateReport()
    {
        var w = Make(distance: 6); long replacement = w.ScheduleWorkAssignment(5, new AssignmentSeed("replacement", 1, new(2), 300));
        Assert.Equal(2, replacement); w.AdvanceTo(6);
        Assert.Equal(AssignmentStatus.Superseded, w.InspectWorkAttempts()[0].Assignment);
        Assert.Equal(LocalOrderStatus.Working, w.InspectLocalOrders(Worker)[0].Status); Assert.True(w.InspectActor(Worker).InTransit);
        Assert.Contains(new Id<ItemTag>(3), w.InspectActor(Worker).Inventory);
        w.AdvanceTo(40); Assert.Equal(WorkExecution.CompletedPhysically, w.InspectWorkAttempts()[0].Execution);
        Assert.Equal("WorkCompleted", w.InspectWorkAttempts()[0].AuthorityStatus);
        Assert.Equal(2, w.InspectJobs()[0].CurrentAttemptId); Assert.Equal("Assigned", w.InspectJobs()[0].AuthorityStatus);
        Assert.Empty(w.InspectWorkAttempts()[1].ReportMessageIds); Assert.Empty(w.InspectLocalOrders(new(2)));
        Assert.All(w.InspectMessages().Where(m => m.Draft.Kind == MessageKind.WorkCompleted), m => Assert.Equal(1, m.Draft.AttemptId)); w.AssertAccounting();
    }
    [Fact]
    public void SuspensionReleasesClaimsRetainsItemsAndResumeRevalidates()
    {
        var w = Make(); w.Submit(7, new(Worker, ActionKind.SuspendWork, new(1, 0, 0), attemptId: 1));
        w.Submit(10, new(Worker, ActionKind.ResumeWork, new(1, 0, 0), attemptId: 1)); w.AdvanceTo(7);
        Assert.Equal(LocalOrderStatus.Suspended, w.InspectLocalOrders(Worker)[0].Status); Assert.Empty(w.InspectResourceClaims());
        Assert.Equal(WorkExecution.Waiting, w.InspectWorkAttempts()[0].Execution); Assert.Contains(new Id<ItemTag>(3), w.InspectActor(Worker).Inventory);
        Assert.False(w.InspectMachines()[0].Operational); var restored = World.Restore(w.Serialize());
        w.AdvanceTo(30); restored.AdvanceTo(30); Assert.Equal(w.Digest(), restored.Digest());
        Assert.Equal(WorkExecution.CompletedPhysically, w.InspectWorkAttempts()[0].Execution); Assert.True(w.InspectMachines()[0].Operational); w.AssertAccounting();
    }
    [Fact]
    public void DispatchAndCancellationKeysAreIdempotentAcrossRestoration()
    {
        var w = Make(); Assert.Equal(1, w.ScheduleWorkAssignment(0, new AssignmentSeed("initial", 1, Worker, 200)));
        w.ScheduleWorkCancellation(5, 1, "cancel-one"); w.ScheduleWorkCancellation(5, 1, "cancel-one"); w.AdvanceTo(6);
        var restored = World.Restore(w.Serialize()); restored.ScheduleWorkCancellation(7, 1, "cancel-one");
        Assert.Equal(1, restored.ScheduleWorkAssignment(7, new AssignmentSeed("initial", 1, Worker, 200)));
        Assert.Throws<ArgumentException>(() => restored.ScheduleWorkAssignment(7, new AssignmentSeed("initial", 1, new(2), 200)));
        w.AdvanceTo(120); restored.AdvanceTo(120); Assert.Equal(w.Digest(), restored.Digest());
        Assert.Single(w.InspectMessages(), m => m.Draft.Kind == MessageKind.OrderCancelled);
        Assert.Equal(LocalOrderStatus.Cancelled, w.InspectLocalOrders(Worker)[0].Status);
    }
    [Fact]
    public void KnownMissingPrerequisitesProducesReasonedRejection()
    {
        var w = Make(hasPart: false); w.AdvanceTo(15);
        Assert.Equal(LocalOrderStatus.Rejected, w.InspectLocalOrders(Worker)[0].Status);
        Assert.Equal("OrderRejected", w.InspectJobs()[0].AuthorityStatus); Assert.False(w.InspectMachines()[0].Operational);
        Assert.Empty(w.InspectRepairAgents()); Assert.Contains(w.InspectMessages(), m => m.Draft.Kind == MessageKind.OrderRejected && m.Draft.Claim.Text == "NoKnownRepairPrerequisitesOrRoute");
    }
    [Fact]
    public void BelievedCompletionDoesNotCompletePhysicalJob()
    {
        // A belief older than the order is confirmed locally before any claim.
        var w = Make(falseBelief: true, hasPart: false); w.AdvanceTo(15);
        Assert.Equal("WorkBlocked", w.InspectJobs()[0].AuthorityStatus);
        Assert.Contains(w.Trace, e => e.Type == "ObservationRecorded" && e.ActorId == Worker.Value && e.Detail.StartsWith("Machine:1/Operational = 0", StringComparison.Ordinal));
        Assert.Equal(WorkExecution.Blocked, w.InspectJobs()[0].PhysicalStatus); Assert.False(w.InspectMachines()[0].Operational);
        Assert.DoesNotContain(w.Trace, e => e.Type == "ItemInstalled"); Assert.Empty(w.InspectResourceTransactions());
    }
    [Fact]
    public void AttemptReportCannotBeSubmittedByAnotherAccount()
    {
        var w = Make(); w.Submit(6, new(new(2), ActionKind.Login, Home, serviceId: 1));
        w.Submit(7, new(new(2), ActionKind.PostMessage, Home, serviceId: 1, message: new MessageDraft(MessageKind.WorkCompleted, RecipientKind.Authority, default,
            new FactKey(SubjectKind.Attempt, 1, AttributeKind.WorkStatus), new FactValue(FactValueKind.Text, text: "Completed"), 7, 1, 1, 1, operationKey: "forged")));
        w.AdvanceTo(9); Assert.Equal("Accepted", w.InspectWorkAttempts()[0].AuthorityStatus);
        Assert.Single(w.InspectWorkAttempts()[0].ReportMessageIds); Assert.Contains(w.Trace, e => e.Type == "WorkReportRejected" && e.Detail == "AttemptOrAccountMismatch");
    }
    [Fact]
    public void JobSnapshotsPreserveOrdersClaimsMessagesAndCheckInSchedule()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var original = Make(distance: 1 + (int)(seed % 4), seed: seed); original.AdvanceTo(150);
            foreach (long tick in new long[] { 0, 1, 2, 4, 7, 12, 30 })
            {
                var w = Make(distance: 1 + (int)(seed % 4), seed: seed); w.AdvanceTo(tick); var data = w.Serialize(); var resumed = World.Restore(data);
                Assert.Equal(data, resumed.Serialize()); resumed.AdvanceTo(150); Assert.Equal(original.Digest(), resumed.Digest()); resumed.AssertAccounting();
            }
        }
    }
    [Fact]
    public void KnownExpiryCancelsPendingAcknowledgmentBeforeItCanBePosted()
    {
        var w = Make(postTicks: 10, expiry: 5); w.AdvanceTo(20);
        Assert.Equal(LocalOrderStatus.Expired, w.InspectLocalOrders(Worker)[0].Status);
        Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.Kind == MessageKind.OrderAccepted);
        Assert.Null(w.InspectWorkAttempts()[0].AcknowledgmentReceivedTick); Assert.False(w.InspectMachines()[0].Operational);
        Assert.Empty(w.InspectRepairAgents()); w.AssertAccounting();
    }
    [Fact]
    public void KnownExpiryStopsTravelAtCommittedSegmentBoundaryAndKeepsInventory()
    {
        var w = Make(distance: 6, expiry: 8); w.AdvanceTo(8);
        Assert.True(w.InspectActor(Worker).InTransit); Assert.Equal(LocalOrderStatus.Expired, w.InspectLocalOrders(Worker)[0].Status);
        w.AdvanceTo(10); Assert.Contains(new Id<ItemTag>(3), w.InspectActor(Worker).Inventory);
        Assert.False(w.InspectMachines()[0].Operational); Assert.DoesNotContain(w.Trace, e => e.Type == "ItemInstalled");
        Assert.Equal(WorkExecution.Cancelled, w.InspectWorkAttempts()[0].Execution); w.AssertAccounting();
    }
    [Fact]
    public void PeriodicCheckInWaitsForWorkBoundaryAndNeverReactsToUnreadDelivery()
    {
        var w = Make(interval: 5); w.AdvanceTo(7);
        Assert.True(w.InspectCheckIns()[0].Due); Assert.Equal(ActionKind.Install, w.InspectActor(Worker).Action);
        Assert.DoesNotContain(w.Trace, e => e.Tick == 7 && e.Type == "ActionRequested");
        w.AdvanceTo(20); Assert.True(w.InspectCheckIns()[0].Completed >= 2); Assert.Equal(WorkExecution.CompletedPhysically, w.InspectJobs()[0].PhysicalStatus);
        var notDue = Make(firstCheckIn: 50); notDue.AdvanceTo(49); Assert.Equal(0, notDue.ActionsCompleted); Assert.Empty(notDue.InspectLocalOrders(Worker));
        notDue.AdvanceTo(52); Assert.Single(notDue.InspectLocalOrders(Worker));
    }
    [Fact]
    public void RepeatedAuthenticatedReportOperationKeyDoesNotDuplicateRecordMutation()
    {
        var w = Make(falseBelief: true, hasPart: false); w.AdvanceTo(13);
        var result = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.WorkBlocked);
        long received = w.InspectWorkHistory().Count(h => h.Kind == "WorkReportReceived");
        w.Submit(14, new(Worker, ActionKind.Login, Home, serviceId: 1));
        w.Submit(15, new(Worker, ActionKind.PostMessage, Home, serviceId: 1, message: result.Draft)); w.AdvanceTo(18);
        Assert.Equal(received, w.InspectWorkHistory().Count(h => h.Kind == "WorkReportReceived"));
        Assert.Equal(2, w.InspectWorkAttempts()[0].ReportMessageIds.Count); Assert.False(w.InspectMachines()[0].Operational);
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }
    [Fact]
    public void LaterAcknowledgmentCannotRegressACompletedAttempt()
    {
        var w = Make(); w.AdvanceTo(20);
        var accepted = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.OrderAccepted).Draft;
        var duplicate = new MessageDraft(accepted.Kind, accepted.RecipientKind, accepted.Recipient, accepted.Subject, accepted.Claim, 20,
            accepted.TaskId, accepted.AttemptId, accepted.RelatedMessageId, operationKey: "second-acknowledgment");
        w.Submit(21, new(Worker, ActionKind.Login, Home, serviceId: 1));
        w.Submit(22, new(Worker, ActionKind.PostMessage, Home, serviceId: 1, message: duplicate)); w.AdvanceTo(25);
        Assert.Equal("WorkCompleted", w.InspectJobs()[0].AuthorityStatus); Assert.Equal(5, w.InspectWorkAttempts()[0].AcknowledgmentReceivedTick);
        Assert.Equal(2, w.InspectWorkAttempts()[0].ReportMessageIds.Count); Assert.Contains(w.Trace, e => e.Type == "WorkReportRejected" && e.Detail == "AlreadyAcknowledged");
    }
    [Fact]
    public void WorkActionsAndReportsCarryTheirOriginalAttemptInTrace()
    {
        var w = Make(); w.AdvanceTo(20);
        Assert.All(w.Trace.Where(e => e.Type == "ActionRequested" && e.Detail is "Move" or "Install" && e.Tick < 9), e => { Assert.Equal(1, e.AttemptId); Assert.Equal(1, e.TaskId); });
        Assert.All(w.Trace.Where(e => e.Type == "WorkReportReceived"), e => { Assert.Equal(1, e.AttemptId); Assert.Equal(1, e.TaskId); });
    }
    [Fact]
    public void WorkOrderScenarioRunsAndIsIsolatedAtConcurrencyTwoFourAndEight()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "work_orders.json"));
        Assert.Empty(loaded.Scenario.ScriptedActors); Assert.Empty(loaded.Scenario.InitialState.Information!.RepairGoals);
        var baseline = Enumerable.Range(0, 24).Select(i => Runs.Execute(loaded, (ulong)i, true).Result).ToArray();
        Assert.All(baseline, r => Assert.Equal("completed", r.Status));
        foreach (int concurrency in new[] { 2, 4, 8 })
        {
            var actual = new RunResult[24]; Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i => actual[i] = Runs.Execute(loaded, (ulong)i, false).Result);
            for (int i = 0; i < actual.Length; i++) { Assert.Equal(baseline[i].Digest, actual[i].Digest); Assert.Equal(baseline[i].Metrics, actual[i].Metrics); }
        }
    }
    [Fact]
    public void GeneratedWorkOrdersKeepPhysicalAndAccountIdentitySeparateAndReplay()
    {
        var canonical = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "work_orders.json"));
        for (ulong seed = 0; seed < 20; seed++)
        {
            var loaded = WorkOrderGenerator.Generate(canonical.ScenarioJson, canonical.CatalogJson, seed);
            var run = Runs.Execute(loaded, seed, true); Assert.Equal("completed", run.Result.Status);
            var actor = loaded.Scenario.InitialState.Actors[0];
            Assert.All(run.World.InspectMessages().Where(m => m.Draft.RecipientKind == RecipientKind.Authority), m => { Assert.Equal(actor.Id, m.PhysicalSender.Value); Assert.Equal(actor.AccountId, m.SenderAccount.Value); });
            Assert.Equal("WorkCompleted", run.World.InspectJobs()[0].AuthorityStatus);
            foreach (long tick in new long[] { 0, 5, 11, 20, 40 })
            {
                var w = loaded.CreateWorld(); w.AdvanceTo(tick); var data = w.Serialize(); var restored = World.Restore(data);
                Assert.Equal(data, restored.Serialize()); Assert.Equal(run.Result.Digest, Runs.Execute(loaded, seed, false, startingWorld: restored).Result.Digest);
            }
        }
    }
}
