using Station.Contracts;
using Station.Mind;
using Xunit;

namespace Station.Tests;

// Pure Mind priority policy, exercised without a world.
public sealed class GoalPolicyTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    private static readonly NeedConfig Rules = new();
    private static NeedValue Need(NeedKind kind, NeedBand band, long q = 0) => new(kind, q, band);

    [Fact]
    public void SelectDuePicksEarliestDueThenSequenceAndKeepsInsertionOrderOnTies()
    {
        var options = new[] { new DueOption(DueSource.CheckIn, 0, 10, 7), new DueOption(DueSource.Routine, 9, 5, 9), new DueOption(DueSource.Toilet, 0, 5, 3) };
        Assert.Equal(DueSource.Toilet, GoalPriority.SelectDue(null, () => throw new InvalidOperationException(), options).Source);
        var tie = new[] { new DueOption(DueSource.CheckIn, 0, 5, 3), new DueOption(DueSource.Toilet, 0, 5, 3) };
        Assert.Equal(DueSource.CheckIn, GoalPriority.SelectDue(null, () => false, tie).Source);
        Assert.Equal(DueSource.None, GoalPriority.SelectDue(null, () => false, Array.Empty<DueOption>()).Source);
    }

    [Fact]
    public void CompetingReportWinsOutrightOnlyWhileAnUrgentOrderIsKnown()
    {
        var report = new DueOption(DueSource.Report, 0, 50, 50);
        var earlier = new[] { new DueOption(DueSource.Routine, 4, 1, 4) };
        Assert.Equal(DueSource.Routine, GoalPriority.SelectDue(report, () => false, earlier).Source);
        int read = 0;
        var urgent = GoalPriority.SelectDue(report, () => true, earlier.Select(o => { read++; return o; }));
        Assert.Equal((DueSource.Report, 0L), (urgent.Source, urgent.Id));
        Assert.Equal(0, read); // other goals are not consulted
    }

    [Fact]
    public void SupersedesIgnoresTheRequestingGoalItself()
    {
        Assert.False(GoalPriority.Supersedes(default, DueSource.CheckIn, 0));
        Assert.False(GoalPriority.Supersedes(new DueOption(DueSource.Routine, 3, 0, 3), DueSource.Routine, 3));
        Assert.True(GoalPriority.Supersedes(new DueOption(DueSource.Routine, 4, 0, 4), DueSource.Routine, 3));
        Assert.True(GoalPriority.Supersedes(new DueOption(DueSource.Toilet, 0, 0, 1), DueSource.None, 0));
    }

    [Fact]
    public void FirstDeferringEvaluatesChecksInOrderAndStopsAtTheFirst()
    {
        var seen = new List<PriorityGate>();
        var gate = GoalPriority.FirstDeferring(GoalPriority.CheckInGates, g => { seen.Add(g); return g == PriorityGate.CredentialRecovery; });
        Assert.Equal(PriorityGate.CredentialRecovery, gate);
        Assert.Equal(new[] { PriorityGate.EscortControls, PriorityGate.Inactive, PriorityGate.CommittedAction, PriorityGate.UrgentNeed, PriorityGate.CredentialRecovery }, seen);
        Assert.Equal(PriorityGate.None, GoalPriority.FirstDeferring(GoalPriority.ReportGates, _ => false));
        Assert.DoesNotContain(PriorityGate.KnownUrgentOrder, GoalPriority.StartGates(DueSource.Report));
        Assert.Contains(PriorityGate.KnownUrgentOrder, GoalPriority.StartGates(DueSource.CheckIn));
    }

    [Fact]
    public void UrgentOrderFollowsTheReportQueueBeforeGuardAndPatrolOrders()
    {
        Assert.True(GoalPriority.KnownUrgentOrder(true, false, new[] { MessageKind.PatrolFound }, () => throw new InvalidOperationException(), true, () => false, () => false));
        Assert.False(GoalPriority.KnownUrgentOrder(true, true, new[] { MessageKind.PatrolFound }, () => true, true, () => true, () => true));
        Assert.False(GoalPriority.KnownUrgentOrder(false, false, Array.Empty<MessageKind>(), () => false, false, () => true, () => true));
        Assert.True(GoalPriority.KnownUrgentOrder(false, false, Array.Empty<MessageKind>(), () => false, true, () => false, () => true));
        Assert.True(GoalPriority.ReportCompetes(1, true, 5, 5));
        Assert.False(GoalPriority.ReportCompetes(1, true, 6, 5));
    }

    [Fact]
    public void CriticalNeedsPreemptEvenKnownUrgentOrders()
    {
        var urgent = NeedPriority.UrgentNeed(new[] { Need(NeedKind.SleepPressure, NeedBand.Urgent), Need(NeedKind.Hunger, NeedBand.Urgent), Need(NeedKind.Bladder, NeedBand.Seek) });
        Assert.Equal(NeedKind.Hunger, urgent!.Kind);
        Assert.False(NeedPriority.Preempts(urgent, false, () => true));
        Assert.True(NeedPriority.Preempts(urgent, false, () => false));
        Assert.True(NeedPriority.Preempts(Need(NeedKind.Bladder, NeedBand.Critical), true, () => throw new InvalidOperationException()));
        Assert.Equal(UrgentNeedStep.LowerNeed, NeedPriority.UrgentStep(urgent, 9, 8, () => false));
        Assert.Equal(UrgentNeedStep.KnownUrgentOrder, NeedPriority.UrgentStep(urgent, 0, 8, () => true));
    }

    [Fact]
    public void PreSleepReliefIsTriedFirstAndStaysInItsOwnPlace()
    {
        var needs = new[] { Need(NeedKind.Hunger, NeedBand.Urgent), Need(NeedKind.Bladder, NeedBand.Seek), Need(NeedKind.SleepPressure, NeedBand.Urgent) };
        var candidates = NeedPriority.UrgentCandidates(needs, () => false, () => NeedKind.Bladder, out var preSleep);
        Assert.Null(preSleep); // hunger, not sleep, leads
        var sleepy = new[] { Need(NeedKind.Hunger, NeedBand.Urgent), Need(NeedKind.SleepPressure, NeedBand.Critical) };
        Assert.Equal(new[] { NeedKind.SleepPressure, NeedKind.Hunger }, NeedPriority.UrgentCandidates(sleepy, () => false, () => throw new InvalidOperationException(), out _).Select(n => n.Kind));
        var urgentSleep = new[] { Need(NeedKind.Bladder, NeedBand.Seek), Need(NeedKind.SleepPressure, NeedBand.Urgent, 900) };
        var ordered = NeedPriority.UrgentCandidates(urgentSleep, () => false, () => NeedKind.Bladder, out preSleep);
        Assert.Equal(NeedKind.Bladder, preSleep);
        Assert.Equal(new[] { NeedKind.Bladder, NeedKind.SleepPressure }, ordered.Select(n => n.Kind));
        Assert.Empty(NeedPriority.UrgentCandidates(new[] { Need(NeedKind.Hunger, NeedBand.Urgent) }, () => true, () => null, out _));
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void RetryFoldsKnownFailedServiceRetriesIntoOwnBackoff()
    {
        Assert.Equal(110, NeedPriority.RetryAt(100, 10, Array.Empty<long>()));
        Assert.Equal(104, NeedPriority.RetryAt(100, 10, new long[] { 107, 104, 120 }));
        Assert.Throws<OverflowException>(() => NeedPriority.RetryAt(long.MaxValue, 1, Array.Empty<long>()));
    }

    [Fact]
    public void LowerNeedsStartOnlyWithoutDueGoalsOrReadyWork()
    {
        Assert.Equal(NeedKind.Bladder, NeedPriority.LowerNeedGoal(null, DueSource.Toilet, () => true));
        Assert.Equal(NeedKind.WorkStrain, NeedPriority.LowerNeedGoal(null, DueSource.None, () => true));
        Assert.Equal(NeedKind.Social, NeedPriority.LowerNeedGoal(null, DueSource.Routine, () => false));
        Assert.Equal(NeedKind.Hunger, NeedPriority.LowerNeedGoal(NeedKind.Hunger, DueSource.Toilet, () => true));
        Assert.True(NeedPriority.LowerNeedMayStart(NeedKind.Bladder, false, () => DueSource.Toilet, () => true, () => false, () => false, () => true));
        Assert.False(NeedPriority.LowerNeedMayStart(NeedKind.WorkStrain, false, () => DueSource.None, () => true, () => true, () => false, () => true));
        Assert.True(NeedPriority.LowerNeedMayStart(NeedKind.Social, false, () => DueSource.None, () => true, () => false, () => true, () => false));
        Assert.True(NeedPriority.LowerNeedMayStart(NeedKind.Hunger, true, () => throw new InvalidOperationException(), () => false, () => false, () => false, () => true));
        Assert.Equal(new[] { PriorityGate.KnownUrgentOrder, PriorityGate.Recovery }, NeedPriority.LowerNeedGates(true));
    }

    [Fact]
    public void CriticalResponseInterruptsUnrelatedWorkAndStopsMovementAtTheBoundary()
    {
        Assert.Equal(CriticalPreemption.CancelNow, NeedPriority.CriticalResponse(false, false, false, ActionKind.ProduceRecipe, NeedKind.Hunger, () => false));
        Assert.Equal(CriticalPreemption.StopAtBoundary, NeedPriority.CriticalResponse(false, false, false, ActionKind.Move, NeedKind.Hunger, () => false));
        Assert.Equal(CriticalPreemption.None, NeedPriority.CriticalResponse(false, false, false, ActionKind.Move, NeedKind.Hunger, () => true));
        Assert.Equal(CriticalPreemption.None, NeedPriority.CriticalResponse(false, false, false, ActionKind.Sleep, NeedKind.WorkStrain, () => false));
        Assert.Equal(CriticalPreemption.InterruptEscort, NeedPriority.CriticalResponse(true, false, true, null, NeedKind.Hunger, () => false));
        Assert.Equal(CriticalPreemption.None, NeedPriority.CriticalResponse(true, true, true, ActionKind.Move, NeedKind.Hunger, () => false));
    }

    [Fact]
    public void MealIsNeededAtSeekOrWhenHungerWouldTurnCriticalBeforeTheNextMeal()
    {
        var hunger = Rules.Rules[(int)NeedKind.Hunger];
        var roster = new RosterPayload(1, 1, 0, 2400, 0, 800, 0, null);
        Assert.True(DailyPolicy.MealNeeded(hunger, () => 300 * H, roster, 1, 1000, 900, 1000));
        Assert.False(DailyPolicy.MealNeeded(hunger, () => 0, roster, 2, 1000, 900, 1000)); // different roster version
        Assert.False(DailyPolicy.MealNeeded(hunger, () => 10 * H, roster, 1, 1000, 900, 1000));
        Assert.False(DailyPolicy.MealNeeded(new NeedRule(NeedKind.Hunger, 60, 60, 60, 60), () => throw new InvalidOperationException(), roster, 1, 1000, 900, 1000));
        Assert.True(DailyPolicy.Eligible(true, RoutineOpportunityKind.ScheduledRest, NeedKind.WorkStrain, 5, 5, 4, 5, () => false));
        Assert.False(DailyPolicy.Eligible(true, RoutineOpportunityKind.DutyMeal, NeedKind.Hunger, 5, 5, 9, 5, () => false));
        Assert.False(DailyPolicy.Eligible(false, RoutineOpportunityKind.Sleep, NeedKind.SleepPressure, 5, 5, 9, 5, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void ServiceWaitsUseFreshLearnedEstimatesAndVisibleLocalQueues()
    {
        Assert.Equal(0, ServicePreference.VisibleWait(0, 1, 2, 5));
        Assert.Equal(10, ServicePreference.VisibleWait(2, 2, 2, 5));
        Assert.Equal(60, ServicePreference.StaleTicks(10));
        Assert.Equal(60 + 7, ServicePreference.LearnedExtraWait(100, 60, 90, 12, 50, 0, 5));
        Assert.Equal(0, ServicePreference.LearnedExtraWait(200, 60, 90, 12, 50, 0, 5)); // both stale
        Assert.Equal(0, ServicePreference.LearnedExtraWait(100, 60, 90, 4, -1, -1, 5)); // shorter than one service
        var waits = new SortedDictionary<long, long> { [1] = 9, [3] = 4 };
        ServicePreference.CompareVisibleQueues(waits, new (long, NeedServiceKind, long, Func<long>)[] { (1, NeedServiceKind.Bed, 7, () => 0), (2, NeedServiceKind.Bed, 7, () => 5), (3, NeedServiceKind.Toilet, 7, () => throw new InvalidOperationException()) });
        Assert.Equal(new[] { (2L, 5L), (3L, 4L) }, waits.Select(p => (p.Key, p.Value)));
    }

    [Fact]
    public void AvoidedTerminalsAreOrderedByRecheckAndDelaysDoubleToTheBound()
    {
        Assert.Equal(new long[] { 4, 2, 3 }, TerminalAvoidance.Avoided(new[] { (3L, 30L), (2L, 20L), (4L, 20L - 1), (5L, 10L) }, 10));
        Assert.Equal(20, TerminalAvoidance.NextRecheckDelay(10, 10));
        Assert.Equal(80, TerminalAvoidance.NextRecheckDelay(60, 10));
        Assert.True(TerminalAvoidance.KeepsSession(false, false, () => false, ActionKind.ReadInbox));
        Assert.False(TerminalAvoidance.KeepsSession(false, false, () => false, ActionKind.Move));
    }

    [Fact]
    public void OffDutyDecisionMapsEachCheckToItsActivity()
    {
        var roster = new RosterPayload(1, 1, 0, 2400, 0, 800, 0, null);
        Assert.True(OffDutyPolicy.KnownOnDuty(null, 1000));
        Assert.True(OffDutyPolicy.KnownOnDuty(roster, 10));
        Assert.False(OffDutyPolicy.KnownOnDuty(roster, 1000));
        Assert.Equal(PriorityGate.EscortControls, OffDutyPolicy.RoutineGates[0]);
        Assert.Equal("SeekingNeedRelief", OffDutyPolicy.Activity(PriorityGate.DueGoal));
        Assert.Equal("OffDuty", OffDutyPolicy.EndsSession(PriorityGate.AtRest));
        Assert.Null(OffDutyPolicy.EndsSession(PriorityGate.OnDuty));
        Assert.True(OffDutyPolicy.OptionalRestOpportunity(true, roster, 1000));
        Assert.False(OffDutyPolicy.OptionalRestOpportunity(true, roster, 10));
        Assert.False(OffDutyPolicy.OptionalRestOpportunity(true, null, 1000));
    }

    [Fact]
    public void ConversationPartnersRotateAfterTheCursor()
    {
        var local = new SortedSet<long> { 1, 2, 3, 4, 5 };
        Assert.Equal(new long[] { 4, 5, 1 }, ConversationPolicy.PartnerCandidates(local, 2, 3, 3));
        Assert.Equal(new long[] { 1, 3 }, ConversationPolicy.PartnerCandidates(local, 2, long.MaxValue, 2));
        Assert.True(ConversationPolicy.Consents(true, false, () => throw new InvalidOperationException()));
        Assert.False(ConversationPolicy.Consents(false, true, () => false));
    }
}
