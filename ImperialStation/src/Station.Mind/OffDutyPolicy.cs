using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Duty-roster policy: what a citizen does around and outside its known duty hours.
    public static class OffDutyPolicy
    {
        private static readonly PriorityGate[] Routine = { PriorityGate.EscortControls, PriorityGate.Inactive, PriorityGate.OnDuty, PriorityGate.CommittedAction, PriorityGate.UrgentNeed, PriorityGate.DueGoal,
            PriorityGate.OptionalRest, PriorityGate.AfterDutyCheckIn, PriorityGate.MissingRestDestination, PriorityGate.AtRest };

        // Without a known roster a citizen is treated as on duty.
        public static bool KnownOnDuty(RosterPayload? knownRoster, long tick) => knownRoster == null || knownRoster.OnDuty(tick);
        // Off duty, only the owed end-of-shift check-in is allowed.
        public static bool AllowsCheckIn(bool knownOnDuty, bool endShiftCheckIn) => knownOnDuty || endShiftCheckIn;
        // A check-in that cannot be planned off duty is abandoned rather than retried.
        public static bool AbandonsFailedCheckIn(bool knownOnDuty) => !knownOnDuty;
        // Optional rest: work strain at seek while off duty or in a scheduled rest window.
        public static bool OptionalRestOpportunity(bool workStrainAtSeek, RosterPayload? knownRoster, long now) =>
            workStrainAtSeek && knownRoster != null && (!knownRoster.OnDuty(now) || DailyRoutine.Around(knownRoster, now).Any(w => w.Kind == RoutineOpportunityKind.ScheduledRest && w.Opens <= now && now < w.Closes));
        // Known on-duty work that optional relief must not displace.
        public static bool OrdinaryWorkReady(bool knownOnDuty, Func<bool> repairDue, Func<bool> assignedWork) => knownOnDuty && (repairDue() || assignedWork());
        // A retained repair is due unless held back by its own retry delay.
        public static bool RepairDue(int pending, bool hasBlockedReason, long retryAt, long now) => pending > 0 && (!hasBlockedReason || retryAt <= now);

        // Off-duty decision: checks in order; the first that applies sets the activity.
        public static IReadOnlyList<PriorityGate> RoutineGates => Routine;
        public static string Activity(PriorityGate gate) => gate switch
        {
            PriorityGate.EscortControls => "EscortProtocol",
            PriorityGate.Inactive => "UnknownRoster",
            PriorityGate.OnDuty => "OnDuty",
            PriorityGate.CommittedAction => "FinishingAction",
            PriorityGate.UrgentNeed or PriorityGate.DueGoal => "SeekingNeedRelief",
            PriorityGate.OptionalRest => "SeekingAdditionalRest",
            PriorityGate.AfterDutyCheckIn => "AfterDutyCheckIn",
            PriorityGate.MissingRestDestination => "MissingRestDestination",
            PriorityGate.AtRest => "OffDuty",
            _ => throw new ArgumentOutOfRangeException(nameof(gate)),
        };
        // Session end reason when the off-duty decision settles there, or null.
        public static string? EndsSession(PriorityGate gate) => gate == PriorityGate.MissingRestDestination ? "OffDutyMissingRestDestination" : gate == PriorityGate.AtRest ? "OffDuty" : null;
        public static string RestPlanFailure(PlanOutcome outcome) => outcome == PlanOutcome.BudgetExhausted ? "RestPlanBudgetExhausted" : "NoKnownRestRoute";
        // A citizen without autonomous needs still owes its queued reports or end-of-shift check-in.
        public static bool OwesAfterDutyCheckIn(bool hasNeedAgent, Func<bool> reportsPending, Func<bool> endShiftCheckInDue) => !hasNeedAgent && (reportsPending() || endShiftCheckInDue());
        // Activity at a duty boundary.
        public static string BoundaryActivity(bool onDuty, bool hasAction) => onDuty ? "OnDuty" : !hasAction ? "AfterDutyCheckIn" : "FinishingAction";
        // Going off duty, movement is stopped at the next tile unless an escort controls
        // it or it serves an own need; finite local work may finish.
        public static bool StopsMoveAtBoundary(bool moving, Func<bool> escortControls, Func<bool> travellingForDailyNeed, Func<bool> travellingForNeed) =>
            moving && !escortControls() && !travellingForDailyNeed() && !travellingForNeed();
    }
}
