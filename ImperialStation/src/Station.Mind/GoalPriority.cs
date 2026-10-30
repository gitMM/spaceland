using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Citizen goal priority. Each decision lists its priority checks in order; the
    // caller evaluates one check at a time (some schedule the goal that wins) and the
    // first check that defers ends the decision. Due goals compete by due tick, then
    // creation sequence; urgent orders and needs precede them.
    public static class GoalPriority
    {
        private static readonly PriorityGate[] Report = { PriorityGate.EscortControls, PriorityGate.CommittedAction, PriorityGate.Inactive, PriorityGate.UrgentNeed, PriorityGate.CredentialRecovery, PriorityGate.DueGoal, PriorityGate.AlreadyPlanned };
        private static readonly PriorityGate[] CheckIn = { PriorityGate.EscortControls, PriorityGate.Inactive, PriorityGate.CommittedAction, PriorityGate.UrgentNeed, PriorityGate.CredentialRecovery, PriorityGate.RoutineForbidsCheckIn, PriorityGate.GuardPriority, PriorityGate.DueGoal, PriorityGate.AlreadyPlanned, PriorityGate.ReportQueueFirst };
        private static readonly PriorityGate[] Daily = { PriorityGate.CommittedAction, PriorityGate.PlannedThisTick, PriorityGate.UrgentNeed, PriorityGate.Recovery, PriorityGate.KnownUrgentOrder };
        private static readonly PriorityGate[] DailyStart = { PriorityGate.CommittedAction, PriorityGate.Inactive, PriorityGate.UrgentNeed, PriorityGate.Recovery, PriorityGate.KnownUrgentOrder, PriorityGate.DueGoal };
        private static readonly PriorityGate[] ReportStart = { PriorityGate.CommittedAction, PriorityGate.UrgentNeed, PriorityGate.DueGoal };
        private static readonly PriorityGate[] OtherStart = { PriorityGate.CommittedAction, PriorityGate.KnownUrgentOrder, PriorityGate.UrgentNeed, PriorityGate.DueGoal };
        private static readonly PriorityGate[] DueArbitration = { PriorityGate.Recovery };

        // Checks before a report-queue decision plans.
        public static IReadOnlyList<PriorityGate> ReportGates => Report;
        // Checks before a periodic check-in plans.
        public static IReadOnlyList<PriorityGate> CheckInGates => CheckIn;
        // Checks before a daily-routine goal plans.
        public static IReadOnlyList<PriorityGate> DailyGates => Daily;
        // Checks when a planned daily-routine step is about to start.
        public static IReadOnlyList<PriorityGate> DailyStartGates => DailyStart;
        // Checks before arbitrating between due goals.
        public static IReadOnlyList<PriorityGate> DueArbitrationGates => DueArbitration;
        // Checks when an autonomous step planned by a due-goal source is about to start.
        // The report queue itself carries urgent-order acknowledgments, so an urgent order
        // does not stop it.
        public static IReadOnlyList<PriorityGate> StartGates(DueSource source) => source == DueSource.Report ? ReportStart : OtherStart;

        // The first check that defers, or None when the decision may proceed.
        public static PriorityGate FirstDeferring(IReadOnlyList<PriorityGate> gates, Func<PriorityGate, bool> defers)
        {
            foreach (var gate in gates) if (defers(gate)) return gate;
            return PriorityGate.None;
        }

        // Trace detail for a planned step that is not started.
        public static string DeferralDetail(PriorityGate gate) => gate switch
        {
            PriorityGate.CommittedAction => "CommittedAction",
            PriorityGate.KnownUrgentOrder => "KnownUrgentOrder",
            _ => "HigherPriorityGoal",
        };

        // A retry delay after an own observed failure holds a goal back.
        public static bool LocallyBlocked(bool hasBlockedReason, long retryAt, long now) => hasBlockedReason && retryAt > now;
        public static bool ReportCompetes(int pending, bool hasBlockedReason, long retryAt, long now) => pending > 0 && !LocallyBlocked(hasBlockedReason, retryAt, now);
        public static bool CheckInCompetes(bool due, long blockedUntil, long now, bool routineAllows) => due && blockedUntil <= now && routineAllows;
        public static bool ToiletCompetes(long toiletSequence, long retryAt, long now) => toiletSequence > 0 && retryAt <= now;

        // Selects the due goal with priority. The retained FIFO reporting protocol must
        // be able to post the acknowledgment/outcome that lets a known urgent order
        // progress, so a competing report wins outright while such an order is known.
        // Other options are read lazily, in their given order (ties keep that order).
        public static DueOption SelectDue(DueOption? report, Func<bool> knownUrgentOrder, IEnumerable<DueOption> others)
        {
            if (report.HasValue && knownUrgentOrder()) return new DueOption(DueSource.Report, 0, report.Value.Due, report.Value.Sequence);
            var options = report.HasValue ? new[] { report.Value }.Concat(others) : others;
            return options.OrderBy(o => o.Due).ThenBy(o => o.Sequence).FirstOrDefault();
        }
        // Only citizens with autonomous needs arbitrate between due goals by stamp.
        public static bool ArbitratesDueGoals(bool hasNeedAgent) => hasNeedAgent;
        // Whether the selected goal takes priority over the requesting goal.
        public static bool Supersedes(DueOption selected, DueSource requesting, long requestingId) =>
            selected.Source != DueSource.None && !(selected.Source == requesting && selected.Id == requestingId);
        // Citizens without autonomous needs post their queued reports before a check-in.
        public static bool ReportQueueFirst(bool hasNeedAgent, bool reportsPending) => !hasNeedAgent && reportsPending;

        // Report kinds that let an urgent escort/patrol/detention order progress.
        public static bool UrgentReport(MessageKind kind) => kind is MessageKind.EscortAcknowledged or MessageKind.EscortOutcome or MessageKind.PatrolAccepted or MessageKind.PatrolFound or MessageKind.PatrolNotLocated or MessageKind.PatrolBlocked or MessageKind.RequestDetention;
        // Whether the citizen knows of an urgent order. Existing guard/patrol protocols
        // wait for their reporting queue; a locally blocked queue must not prohibit all
        // useful relief.
        public static bool KnownUrgentOrder(bool reportsPending, bool reportsLocallyBlocked, IEnumerable<MessageKind> pendingReports, Func<bool> guardHasPriority, bool knownOnDuty, Func<bool> guardAcknowledgmentPending, Func<bool> patrolInProgress)
        {
            if (reportsPending)
            {
                if (reportsLocallyBlocked) return false;
                if (pendingReports.Any(UrgentReport)) return true;
            }
            return guardHasPriority() || knownOnDuty && (guardAcknowledgmentPending() || patrolInProgress());
        }
    }
}
