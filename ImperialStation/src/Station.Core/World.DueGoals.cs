using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    internal readonly struct GoalStamp
    {
        internal long Due { get; }
        internal long Sequence { get; }
        internal GoalStamp(long due, long sequence) { Due = due; Sequence = sequence; }
    }
    internal enum DueSource { None, Report, CheckIn, Routine, Toilet }
    public sealed partial class World
    {
        private long nextGoalSequence = 1;
        private long DecisionSource(Actor actor, DueSource source) => (needAgents.ContainsKey(actor.Id.Value) || recoveryAgents.ContainsKey(actor.Id.Value)) ? (long)source + 1 : 0;
        private bool AlreadyPlanned(Actor actor, DueSource source) => (needAgents.ContainsKey(actor.Id.Value) || recoveryAgents.ContainsKey(actor.Id.Value)) && scheduled.ForActor(actor.Id.Value).Any(e => e.Tick == processingTick && e.Kind == ScheduledKind.Command && e.Value == (long)source + 1);
        private bool MayStartDecision(ScheduledEvent command)
        {
            if (command.Value == 0) return true;
            var actor = actors[command.Intent.Actor]; var source = (DueSource)(command.Value - 1);
            if (actor.Action != null) { Emit("PlanDeferred", actor, detail: "CommittedAction", causeId: command.CauseId); return false; }
            if (source != DueSource.Report && KnownUrgentOrder(actor.Id))
            { WakeGuard(actor); WakePatrol(actor); Emit("PlanDeferred", actor, detail: "KnownUrgentOrder", causeId: command.CauseId); return false; }
            if (DeferToNeed(actor) || DeferToDueGoal(actor, source)) { Emit("PlanDeferred", actor, detail: "HigherPriorityGoal", causeId: command.CauseId); return false; }
            return true;
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
        private (DueSource Source, long Id) SelectedDueGoal(Actor actor)
        {
            var options = new List<(DueSource Source, long Id, GoalStamp Stamp)>();
            if (reportAgents.TryGetValue(actor.Id, out var report) && report.Pending.Count > 0 && !(report.BlockedReason.Length > 0 && report.NextDecision > NeedNow))
            {
                // The retained FIFO reporting protocol must be able to post the
                // acknowledgment/outcome that lets an urgent order progress.
                if (KnownUrgentOrder(actor.Id)) return (DueSource.Report, 0);
                options.Add((DueSource.Report, 0, report.PendingStamps[0]));
            }
            if (checkIns.TryGetValue(actor.Id, out var checkIn) && checkIn.Due && checkIn.BlockedUntil <= NeedNow && RoutineAllowsCheckIn(actor.Id))
                options.Add((DueSource.CheckIn, 0, checkIn.Stamp));
            foreach (var goal in DailyFor(actor.Id.Value).Where(DailyGoalEligible))
                options.Add((DueSource.Routine, goal.Id, new GoalStamp(goal.DueTick, goal.Id)));
            if (needAgents.TryGetValue(actor.Id.Value, out var needs) && needs.ToiletSequence > 0 && needs.RetryAt <= NeedNow)
                options.Add((DueSource.Toilet, 0, new GoalStamp(needs.ToiletDueTick, needs.ToiletSequence)));
            var selected = options.OrderBy(o => o.Stamp.Due).ThenBy(o => o.Stamp.Sequence).FirstOrDefault();
            return (selected.Source, selected.Id);
        }
        private bool DeferToDueGoal(Actor actor, DueSource requesting = DueSource.None, long id = 0)
        {
            if (DeferToRecovery(actor)) return true; if (!needAgents.ContainsKey(actor.Id.Value)) return false;
            var selected = SelectedDueGoal(actor);
            if (selected.Source == DueSource.None || selected.Source == requesting && selected.Id == id) return false;
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
