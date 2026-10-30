using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    internal readonly struct GoalStamp
    {
        internal long Due { get; }
        internal long Sequence { get; }
        internal GoalStamp(long due, long sequence) { Due = due; Sequence = sequence; }
    }
    public sealed partial class World
    {
        private long nextGoalSequence = 1;
        private long DecisionSource(Actor actor, DueSource source) => (needAgents.ContainsKey(actor.Id.Value) || recoveryAgents.ContainsKey(actor.Id.Value)) ? (long)source + 1 : 0;
        private bool AlreadyPlanned(Actor actor, DueSource source) => (needAgents.ContainsKey(actor.Id.Value) || recoveryAgents.ContainsKey(actor.Id.Value)) && scheduled.ForActor(actor.Id.Value).Any(e => e.Tick == processingTick && e.Kind == ScheduledKind.Command && e.Value == (long)source + 1);
        private bool MayStartDecision(ScheduledEvent command)
        {
            if (command.Value == 0) return true;
            var actor = actors[command.Intent.Actor]; var source = (DueSource)(command.Value - 1);
            var gate = GoalPriority.FirstDeferring(GoalPriority.StartGates(source), g => Defers(actor, g, source));
            if (gate == PriorityGate.None) return true;
            if (gate == PriorityGate.KnownUrgentOrder) { WakeGuard(actor); WakePatrol(actor); }
            Emit("PlanDeferred", actor, detail: GoalPriority.DeferralDetail(gate), causeId: command.CauseId); return false;
        }
        // Evaluates one shared priority check for a citizen. Checks that defer to another
        // goal schedule that goal's decision; Mind decides which checks apply and in what order.
        private bool Defers(Actor actor, PriorityGate gate, DueSource source = DueSource.None, long id = 0)
        {
            switch (gate)
            {
                case PriorityGate.EscortControls: return EscortControls(actor.Id);
                case PriorityGate.CommittedAction: return actor.Action != null;
                case PriorityGate.UrgentNeed: return DeferToNeed(actor);
                case PriorityGate.CredentialRecovery: return PrepareCredentialGoal(actor);
                case PriorityGate.Recovery: return DeferToRecovery(actor);
                case PriorityGate.KnownUrgentOrder: return KnownUrgentOrder(actor.Id);
                case PriorityGate.CriticalNeed: return HasCriticalNeed(actor.Id);
                case PriorityGate.StaffClaimed: return resourceClaims.Any(c => c.Kind == ClaimKind.Staff && c.ResourceId == actor.Id.Value);
                case PriorityGate.RoutineForbidsCheckIn: return !RoutineAllowsCheckIn(actor.Id);
                case PriorityGate.GuardPriority: return GuardHasPriority(actor.Id);
                case PriorityGate.DueGoal: return DeferToDueGoal(actor, source, id);
                case PriorityGate.DueGoalSelected: return SelectedDueGoal(actor).Source != DueSource.None;
                case PriorityGate.AlreadyPlanned: return AlreadyPlanned(actor, source);
                case PriorityGate.ReportQueueFirst: return GoalPriority.ReportQueueFirst(needAgents.ContainsKey(actor.Id.Value), reportAgents.TryGetValue(actor.Id, out var report) && report.Pending.Count > 0);
                case PriorityGate.OrdinaryWork: return OrdinaryWorkReady(actor);
                case PriorityGate.OptionalRest: return DeferToOptionalRest(actor);
                default: throw new ArgumentOutOfRangeException(nameof(gate), gate, "This priority check needs the deciding goal's own state.");
            }
        }
        private GoalStamp NewGoalStamp(long due) => new GoalStamp(due, nextGoalSequence++);
        private void QueueReport(ReportAgent agent, MessageDraft draft)
        { agent.Pending.Add(draft); agent.PendingStamps.Add(NewGoalStamp(NeedNow)); }
        private static void RemoveReports(ReportAgent agent, Predicate<MessageDraft> predicate)
        {
            for (int i = agent.Pending.Count - 1; i >= 0; i--)
                if (predicate(agent.Pending[i])) { agent.Pending.RemoveAt(i); agent.PendingStamps.RemoveAt(i); }
        }
        private void MarkCheckInDue(CheckInAgent agent)
        {
            if (!agent.Due) { agent.Stamp = NewGoalStamp(NeedNow); agent.BlockedUntil = 0; }
            agent.Due = true;
        }
        // Gathers the citizen's own due goals; Mind selects the one with priority.
        private DueOption SelectedDueGoal(Actor actor)
        {
            DueOption? report = null;
            if (reportAgents.TryGetValue(actor.Id, out var reports) && GoalPriority.ReportCompetes(reports.Pending.Count, reports.BlockedReason.Length > 0, reports.NextDecision, NeedNow))
                report = new DueOption(DueSource.Report, 0, reports.PendingStamps[0].Due, reports.PendingStamps[0].Sequence);
            return GoalPriority.SelectDue(report, () => KnownUrgentOrder(actor.Id), CompetingDueGoals(actor));
        }
        // Own due goals other than the report queue, in arbitration order (read lazily).
        private IEnumerable<DueOption> CompetingDueGoals(Actor actor)
        {
            if (checkIns.TryGetValue(actor.Id, out var checkIn) && GoalPriority.CheckInCompetes(checkIn.Due, checkIn.BlockedUntil, NeedNow, RoutineAllowsCheckIn(actor.Id)))
                yield return new DueOption(DueSource.CheckIn, 0, checkIn.Stamp.Due, checkIn.Stamp.Sequence);
            foreach (var goal in DailyFor(actor.Id.Value).Where(DailyGoalEligible))
                yield return new DueOption(DueSource.Routine, goal.Id, goal.DueTick, goal.Id);
            if (needAgents.TryGetValue(actor.Id.Value, out var needs) && GoalPriority.ToiletCompetes(needs.ToiletSequence, needs.RetryAt, NeedNow))
                yield return new DueOption(DueSource.Toilet, 0, needs.ToiletDueTick, needs.ToiletSequence);
        }
        // Wakes the selected due goal when it takes priority over the requesting one.
        private bool DeferToDueGoal(Actor actor, DueSource requesting = DueSource.None, long id = 0)
        {
            if (GoalPriority.FirstDeferring(GoalPriority.DueArbitrationGates, g => Defers(actor, g)) != PriorityGate.None) return true;
            if (!GoalPriority.ArbitratesDueGoals(needAgents.ContainsKey(actor.Id.Value))) return false;
            var selected = SelectedDueGoal(actor);
            if (!GoalPriority.Supersedes(selected, requesting, id)) return false;
            switch (selected.Source)
            {
                case DueSource.Report: RequestReportDecision(reportAgents[actor.Id], NeedNow); break;
                case DueSource.CheckIn: RequestCheckInDecision(checkIns[actor.Id], NeedNow); break;
                case DueSource.Routine: RequestDailyDecision(actor.Id, NeedNow); break;
                case DueSource.Toilet: RequestNeedDecision(needAgents[actor.Id.Value], NeedNow); break;
            }
            return true;
        }
    }
}
