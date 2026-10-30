using Station.Contracts;
using Station.Core;
using Station.Mind;
using Xunit;

namespace Station.Tests;

// Focused tests of the pure Mind work, recovery and guard policies that Core applies.
public sealed class WorkPolicyTests
{
    private static readonly Id<ActorTag> Actor = new(1);
    private static PlanResult Found(params ActionIntent[] steps) =>
        new(PlanOutcome.PlanFound, steps.Select((s, i) => new SymbolicAction("s" + i, 0, 0, 0, 0, 1, s)), steps.Length, 3);
    private static KnowledgeFact Fact(FactKey key, FactValue value, EvidenceKind source = EvidenceKind.Direct, long observed = 10) => new(1, key, value, source, 0, observed, observed);

    [Fact]
    public void MirrorEnumsMatchCoreOrdinals()
    {
        static int[] Ordinals(Type t) => Enum.GetValues(t).Cast<object>().Select(Convert.ToInt32).ToArray();
        Assert.Equal(Enum.GetNames(typeof(LocalOrderStatus)), Enum.GetNames(typeof(KnownWorkOrderStatus)));
        Assert.Equal(Ordinals(typeof(LocalOrderStatus)), Ordinals(typeof(KnownWorkOrderStatus)));
        Assert.Equal(Enum.GetNames(typeof(GuardOrderStatus)), Enum.GetNames(typeof(KnownGuardOrderStatus)));
        Assert.Equal(Ordinals(typeof(GuardOrderStatus)), Ordinals(typeof(KnownGuardOrderStatus)));
    }

    [Fact]
    public void BackoffDoublesCapsAndNeverOverflows()
    {
        Assert.Equal(100, WorkBackoff.Next(50, 1000));
        Assert.Equal(800, WorkBackoff.Next(400, 1000));
        Assert.Equal(1000, WorkBackoff.Next(500, 1000));
        Assert.Equal(1000, WorkBackoff.Next(501, 1000));
        Assert.Equal(long.MaxValue, WorkBackoff.Next(long.MaxValue / 2 + 1, long.MaxValue));
        Assert.Equal(70, WorkBackoff.RetryTick(20, 50, failed: true));
        Assert.Equal(20, WorkBackoff.RetryTick(20, 50, failed: false));
    }

    [Fact]
    public void OrderLifecycleRoutesAcceptsAndSettles()
    {
        Assert.True(WorkOrderPolicy.AwaitsDecision(KnownWorkOrderStatus.Read, WorkKind.RepairMachine));
        Assert.False(WorkOrderPolicy.AwaitsDecision(KnownWorkOrderStatus.Working, WorkKind.RepairMachine));
        Assert.True(WorkOrderPolicy.AwaitsDecision(KnownWorkOrderStatus.Working, WorkKind.CleanTile));
        Assert.Equal(WorkOrderRoute.Cleanup, WorkOrderPolicy.Route(KnownWorkOrderStatus.Working, WorkKind.CleanTile));
        Assert.Equal(WorkOrderRoute.Verification, WorkOrderPolicy.Route(KnownWorkOrderStatus.Working, WorkKind.VerifyMachine));
        Assert.Equal(WorkOrderRoute.None, WorkOrderPolicy.Route(KnownWorkOrderStatus.Working, WorkKind.RepairMachine));
        Assert.Equal(WorkOrderRoute.Acceptance, WorkOrderPolicy.Route(KnownWorkOrderStatus.Read, WorkKind.ConstructHabitat));
        Assert.Equal(WorkOrderVerdict.Retry, WorkOrderPolicy.Accept(PlanOutcome.BudgetExhausted, WorkKind.CleanTile).Verdict);
        Assert.Equal("NoKnownVerificationRoute", WorkOrderPolicy.Accept(PlanOutcome.NoPlan, WorkKind.VerifyMachine).Reason);
        Assert.Equal("Accepted", WorkOrderPolicy.Accept(PlanOutcome.PlanFound, WorkKind.RepairMachine).Reason);
        Assert.True(WorkOrderPolicy.Settled(KnownWorkOrderStatus.Rejected));
        Assert.False(WorkOrderPolicy.Settled(KnownWorkOrderStatus.Suspended));
        // Own completion memory outranks expiry; expiry outranks evaluation.
        Assert.Equal(WorkOrderReadOutcome.OwnCompletionAlreadyPosted, WorkOrderPolicy.OnRead(true, 5, 10));
        Assert.Equal(WorkOrderReadOutcome.Expired, WorkOrderPolicy.OnRead(false, 10, 10));
        Assert.Equal(WorkOrderReadOutcome.Evaluate, WorkOrderPolicy.OnRead(false, 11, 10));
    }

    [Fact]
    public void RepairOrderRevisesOnlyOlderObservedOperationalBelief()
    {
        var key = WorkOrderPolicy.ClaimedCondition(WorkKind.RepairMachine, 7)!.Value;
        Assert.Null(WorkOrderPolicy.ClaimedCondition(WorkKind.CleanTile, 7));
        Assert.True(WorkOrderPolicy.OrderSupersedesBelief(Fact(key, FactValue.Boolean(true), observed: 4), 5));
        Assert.False(WorkOrderPolicy.OrderSupersedesBelief(Fact(key, FactValue.Boolean(true), observed: 5), 5));
        Assert.False(WorkOrderPolicy.OrderSupersedesBelief(Fact(key, FactValue.Boolean(true), EvidenceKind.Seeded, 0), 5));
        Assert.False(WorkOrderPolicy.OrderSupersedesBelief(Fact(key, FactValue.Boolean(false), observed: 1), 5));
        Assert.False(WorkOrderPolicy.OrderSupersedesBelief(null, 5));
    }

    [Fact]
    public void SupersedingAndCleanupOrderingUseAttemptOrder()
    {
        var own = new[]
        {
            new WorkOrderBrief(2, WorkKind.CleanTile, KnownWorkOrderStatus.Working, 0, 9),
            new WorkOrderBrief(3, WorkKind.CleanTile, KnownWorkOrderStatus.Read, 0, 9),
            new WorkOrderBrief(4, WorkKind.CleanTile, KnownWorkOrderStatus.Working, 0, 8),
            new WorkOrderBrief(5, WorkKind.CleanTile, KnownWorkOrderStatus.Working, 0, 9),
            new WorkOrderBrief(6, WorkKind.CleanTile, KnownWorkOrderStatus.Working, 0, 9),
        };
        Assert.Equal(new long[] { 2, 6 }, WorkOrderPolicy.SupersededCleanups(own[3], own));
        Assert.True(CleanupPolicy.WaitsForEarlierCleanup(4, own));
        Assert.False(CleanupPolicy.WaitsForEarlierCleanup(2, own));
        Assert.True(WorkOrderPolicy.SupersedesRepair(3, 4));
        Assert.False(WorkOrderPolicy.SupersedesRepair(4, 4));
        Assert.False(WorkOrderPolicy.SupersedesRepair(null, 4));
        Assert.Equal("NoWorkNeeded", CleanupPolicy.CompletionReason(0));
        Assert.Equal("ReportedComplete", CleanupPolicy.CompletionReason(3));
        Assert.True(WorkOrderPolicy.ReportsFirst(false, 1, false));
        Assert.True(WorkOrderPolicy.ReportsFirst(false, 0, true));
        Assert.False(WorkOrderPolicy.ReportsFirst(true, 1, true));
    }

    [Fact]
    public void RepairDecisionsBindAttemptsAndRefuseHypotheticalOutputs()
    {
        var plan = Found(new ActionIntent(Actor, ActionKind.PickUp, default, new Id<ItemTag>(4)));
        Assert.Equal(WorkGoalStep.Execute, RepairPolicy.Classify(plan));
        Assert.Equal(WorkGoalStep.Completed, RepairPolicy.Classify(Found()));
        Assert.Equal(WorkGoalStep.Blocked, RepairPolicy.Classify(new PlanResult(PlanOutcome.NoPlan)));
        Assert.Equal(9, RepairPolicy.FirstIntent(plan, 9).AttemptId);
        Assert.Equal(0, RepairPolicy.FirstIntent(plan, null).AttemptId);
        Assert.Throws<InvalidOperationException>(() => RepairPolicy.FirstIntent(Found(new ActionIntent(Actor, ActionKind.Install, default)), 9));
        Assert.Equal("PlanBudgetExhausted", RepairPolicy.BlockedReason(PlanOutcome.BudgetExhausted));
        Assert.Equal("NoKnownRepairPrerequisitesOrRoute", RepairPolicy.BlockedReason(PlanOutcome.NoPlan));
        Assert.True(RepairPolicy.KnowledgeWakes(new FactKey(SubjectKind.Tile, 3, AttributeKind.Dirt), new long[0]));
        Assert.True(RepairPolicy.KnowledgeWakes(new FactKey(SubjectKind.Machine, 7, AttributeKind.Operational), new long[] { 7 }));
        Assert.False(RepairPolicy.KnowledgeWakes(new FactKey(SubjectKind.Machine, 8, AttributeKind.Operational), new long[] { 7 }));
        var works = Fact(new FactKey(SubjectKind.Machine, 7, AttributeKind.Operational), FactValue.Boolean(true));
        Assert.True(RepairPolicy.Proceeds(false, new[] { works }, 7));
        Assert.False(RepairPolicy.Proceeds(false, new[] { works }, 8));
        Assert.True(RepairPolicy.Proceeds(true, Array.Empty<KnowledgeFact>(), 8));
        var pins = RepairPolicy.PlannedItems(plan);
        Assert.True(RepairPolicy.PinsForPlan(new FactKey(SubjectKind.Item, 4, AttributeKind.Resource), 7, pins));
        Assert.False(RepairPolicy.PinsForPlan(new FactKey(SubjectKind.Item, 5, AttributeKind.Resource), 7, pins));
    }

    [Fact]
    public void RecoveryProgressRetriesAndDeskBlacklist()
    {
        Assert.Equal(RecoveryProgress.AwaitReply, RecoveryPolicy.AfterAction(ActionKind.PostLostCard, false, false));
        Assert.Equal(RecoveryProgress.RetryUnauthorized, RecoveryPolicy.AfterAction(ActionKind.ReadRecoveryInbox, false, false));
        Assert.Equal(RecoveryProgress.Continue, RecoveryPolicy.AfterAction(ActionKind.ReadRecoveryInbox, true, false));
        Assert.Equal(RecoveryProgress.RetryUnreadInbox, RecoveryPolicy.AfterAction(ActionKind.ReadInbox, true, true));
        Assert.Equal(RecoveryProgress.Complete, RecoveryPolicy.AfterAction(ActionKind.ReadInbox, false, false));
        Assert.False(RecoveryPolicy.ResetsBackoff(ActionKind.Move));
        Assert.Equal(30, RecoveryPolicy.ReplyWaitUntil(10, 10));
        Assert.Equal(1060, RecoveryPolicy.DeskUnavailableUntil(FailureReason.RequiredServiceNotPresent, 3, 10, 50, 1000));
        Assert.Null(RecoveryPolicy.DeskUnavailableUntil(FailureReason.RequiredServiceNotPresent, 0, 10, 50, 1000));
        Assert.Null(RecoveryPolicy.DeskUnavailableUntil(FailureReason.NoRoute, 3, 10, 50, 1000));
        Assert.True(RecoveryPolicy.ReplacementRevoked(FailureReason.CredentialRevoked, ActionKind.Login, 4));
        Assert.False(RecoveryPolicy.ReplacementRevoked(FailureReason.CredentialRevoked, ActionKind.Login, 0));
        Assert.Equal("Interrupted", RecoveryPolicy.FailureRetryReason(FailureReason.None));
        Assert.Equal("MissingRecoveryCapabilityOrKnownCard", RecoveryPolicy.RetryReason(PlanOutcome.NoPlan, false));
        Assert.Equal("NoKnownRecoveryRouteOrService", RecoveryPolicy.RetryReason(PlanOutcome.PlanFound, true));
        Assert.True(RecoveryPolicy.YieldsToUrgentOrder(true, false));
        Assert.False(RecoveryPolicy.YieldsToUrgentOrder(true, true));
        Assert.False(RecoveryPolicy.Ready(true, 11, 10));
        Assert.Null(RecoveryPolicy.FirstIntent(Found()));
        Assert.Throws<InvalidOperationException>(() => RecoveryPolicy.FirstIntent(Found(new ActionIntent(Actor, ActionKind.PickUp, default))));
    }

    private sealed class Inputs : IGuardPreparationInputs
    {
        public bool Critical, Relief, Intake, IsBusy, OnDuty = true, Reports, Escort; public int Calls;
        public bool CriticalNeed() { Calls++; return Critical; }
        public bool JourneyNeedsRelief() { Calls++; return Relief; }
        public bool AwaitingIntake() { Calls++; return Intake; }
        public bool Busy() { Calls++; return IsBusy; }
        public bool KnownOnDuty() { Calls++; return OnDuty; }
        public bool ReportsPending() { Calls++; return Reports; }
        public bool EscortControls() { Calls++; return Escort; }
    }

    [Fact]
    public void GuardStepsFollowPriorityAndReadInputsLazily()
    {
        var critical = new Inputs { Critical = true, Relief = true };
        Assert.Equal(GuardStep.WithdrawForCriticalNeed, GuardPolicy.Next(KnownGuardOrderStatus.Searching, 5, 10, critical));
        Assert.Equal(1, critical.Calls);
        Assert.Equal(GuardStep.Expire, GuardPolicy.Next(KnownGuardOrderStatus.Searching, 10, 10, new Inputs()));
        Assert.Equal(GuardStep.SeekRelief, GuardPolicy.Next(KnownGuardOrderStatus.Searching, 5, 10, new Inputs { Relief = true, Intake = true }));
        Assert.Equal(GuardStep.RegisterIntake, GuardPolicy.Next(KnownGuardOrderStatus.TargetLocated, 5, 10, new Inputs { Intake = true, IsBusy = true }));
        Assert.Equal(GuardStep.Wait, GuardPolicy.Next(KnownGuardOrderStatus.TargetLocated, 5, 10, new Inputs { OnDuty = false }));
        Assert.Equal(GuardStep.Wait, GuardPolicy.Next(KnownGuardOrderStatus.WaitingTeam, 5, 10, new Inputs()));
        Assert.Equal(GuardStep.Wait, GuardPolicy.Next(KnownGuardOrderStatus.Searching, 5, 10, new Inputs { Reports = true }));
        Assert.Equal(GuardStep.Wait, GuardPolicy.Next(KnownGuardOrderStatus.TargetLocated, 5, 10, new Inputs { Escort = true }));
        Assert.Equal(GuardStep.BeginEscort, GuardPolicy.Next(KnownGuardOrderStatus.TargetLocated, 5, 10, new Inputs()));
        Assert.Equal(GuardStep.Search, GuardPolicy.Next(KnownGuardOrderStatus.Searching, 5, 10, new Inputs()));
        Assert.Equal(GuardStep.Prepare, GuardPolicy.Next(KnownGuardOrderStatus.EquipmentPending, 5, 10, new Inputs()));
        Assert.Equal(KnownGuardOrderStatus.EquipmentPending, GuardPolicy.AfterCriticalNeed(KnownGuardOrderStatus.WaitingTeam));
        Assert.Equal(KnownGuardOrderStatus.AtRendezvous, GuardPolicy.AfterCriticalNeed(KnownGuardOrderStatus.AtRendezvous));
    }

    [Fact]
    public void GuardPreparationDeadlinesFailuresAndSearch()
    {
        Assert.Equal(120, GuardPolicy.TargetWaitDeadline(500, 100, 10, null, 20));
        Assert.Equal(400, GuardPolicy.TargetWaitDeadline(500, 100, 10, 300, 20));
        Assert.Equal(250, GuardPolicy.TargetWaitDeadline(250, 100, 10, 300, 20));
        Assert.True(GuardPolicy.TeamWaitExpired(120, 120, 121));
        Assert.False(GuardPolicy.TeamWaitExpired(121, 120, 121));
        Assert.Equal(GuardPreparationStep.ArriveAtRendezvous, GuardPolicy.Prepare(KnownGuardOrderStatus.EquipmentPending, true, true));
        Assert.Equal(GuardPreparationStep.Join, GuardPolicy.Prepare(KnownGuardOrderStatus.AtRendezvous, true, true));
        Assert.Equal(GuardPreparationStep.Plan, GuardPolicy.Prepare(KnownGuardOrderStatus.AtRendezvous, false, true));
        var unreachable = GuardPolicy.PreparationFailure(PlanOutcome.NoPlan, true);
        Assert.Equal(("NoKnownRendezvousRoute", "RendezvousUnreachable"), (unreachable.Reason, unreachable.Outcome));
        Assert.Null(GuardPolicy.PreparationFailure(PlanOutcome.BudgetExhausted, false).Outcome);
        Assert.False(GuardPolicy.HoldsPriority(KnownGuardOrderStatus.Searching, "NoKnownSearchRoute", 10, 5));
        Assert.True(GuardPolicy.HoldsPriority(KnownGuardOrderStatus.Searching, "guard-search/v1", 10, 5));
        Assert.False(GuardPolicy.HoldsPriority(KnownGuardOrderStatus.SearchExhausted, "", 10, 5));
        Assert.True(GuardPolicy.ForecastsJourney(KnownGuardOrderStatus.Searching, "PreparingForEscort", 10, 5));
        Assert.False(GuardPolicy.ForecastsJourney(KnownGuardOrderStatus.Searching, "guard-search/v1", 10, 5));
        Assert.Equal("RetryLocalHandoff", GuardPolicy.ReasonOnDecision("AwaitingLocalHandoff"));
        Assert.Equal("PreparingForEscort", GuardPolicy.ReinspectReason("PreparingForEscort"));
        Assert.Equal("TargetObservationNeedsRefresh", GuardPolicy.ReinspectReason("LocallyIdentifiedTarget"));
        Assert.Equal(GuardSearchObservation.Located, GuardSearchPolicy.Observe(true, 3, 3));
        Assert.Equal(GuardSearchObservation.Exhausted, GuardSearchPolicy.Observe(false, 3, 3));
        Assert.Equal(GuardSearchObservation.Continue, GuardSearchPolicy.Observe(false, 2, 3));
        Assert.Equal("SearchUnreachable", GuardSearchPolicy.Failure(PlanOutcome.NoPlan).Outcome);
        Assert.False(GuardSearchPolicy.ResetsBackoff(ActionKind.BeginEscort));
        Assert.Equal("LocallyIdentifiedTarget", GuardSearchPolicy.SelectedReason(KnownGuardOrderStatus.TargetLocated, "guard-search/v1", "LocallyIdentifiedTarget"));
    }

    [Fact]
    public void JourneyReliefNeedsAnUrgentCriticalNeedAndKeepsPreparationWithoutAView()
    {
        var rules = new NeedConfig().Rules;
        var hunger = rules.Single(r => r.Kind == NeedKind.Hunger);
        long urgentQ = (long)Math.Max(1, hunger.Urgent) * NeedAccumulator.HourMilliseconds;
        Func<IActorView> noView = () => throw new InvalidOperationException("view must not be read");
        Assert.False(GuardPolicy.JourneyNeedsRelief("PreparingForEscort", default, Array.Empty<NeedValue>(), rules, 0, 0, 1000, 10, () => 0, noView));
        if (hunger.Critical > 0 && hunger.Urgent > 0)
            Assert.True(GuardPolicy.JourneyNeedsRelief("PreparingForEscort", default, new[] { new NeedValue(NeedKind.Hunger, urgentQ, NeedBand.Urgent) }, rules, 0, 0, 1000, 10, () => 0, noView));
    }
}
