using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Retained cleanup-goal decisions around the bounded CleanupPlanning search.
    public static class CleanupPolicy
    {
        public const string BudgetExhaustedReason = "PlanBudgetExhausted";
        public const string NoPlanReason = "NoKnownCleanupPrerequisitesOrRoute";

        // One retained cleanup goal may issue an intent for this citizen. Later
        // assignments wait in stable attempt order and wake after the report.
        public static bool WaitsForEarlierCleanup(long attemptId, IEnumerable<WorkOrderBrief> own) =>
            own.Any(o => o.Kind == WorkKind.CleanTile && o.Status == KnownWorkOrderStatus.Working && o.AttemptId < attemptId);

        // The citizen checked the tile here and privately knows it is clean.
        public static bool KnownDone(IActorView view, long tileId, bool cleanupChecked)
        {
            var site = view.PublicKnowledge?.Work?.Cleanable.SingleOrDefault(c => c.Tile.Value == tileId);
            return cleanupChecked && site != null && view.CurrentTile == site.Position &&
                view.Knowledge.Any(f => f.Key.Equals(new FactKey(SubjectKind.Tile, tileId, AttributeKind.Dirt)) && f.Value.Equals(FactValue.Integer(0)));
        }

        public static WorkGoalStep Classify(PlanResult result) =>
            result.Outcome != PlanOutcome.PlanFound ? WorkGoalStep.Blocked : result.Steps.Count == 0 ? WorkGoalStep.Completed : WorkGoalStep.Execute;

        public static ActionIntent FirstIntent(PlanResult result, long attemptId)
        { var first = result.Steps[0].Intent; return new ActionIntent(first.Actor, first.Kind, first.Target, first.Item, attemptId: attemptId); }

        public static string BlockedReason(PlanOutcome outcome) => outcome == PlanOutcome.BudgetExhausted ? BudgetExhaustedReason : NoPlanReason;

        public static string CompletionReason(long cleanedUnits) => cleanedUnits == 0 ? "NoWorkNeeded" : WorkOrderPolicy.ReportedCompleteReason;

        // Moving away invalidates a local tile check.
        public static bool InvalidatesCheck(ActionKind kind) => kind == ActionKind.Move;

        public static bool ChecksTile(ActionKind kind) => kind is ActionKind.InspectTile or ActionKind.Clean;

        // Only an observation made by this completing action counts as a check here.
        public static bool ObservationCurrent(KnowledgeFact? observed, long now) => observed != null && observed.ObservedTick == now;

        // Preserve a completed local observation that the tile is clean before
        // another due goal moves the worker away.
        public static bool ObservedClean(KnownWorkOrderStatus status, long expires, long now, bool cleanupChecked, KnowledgeFact? observed) =>
            status == KnownWorkOrderStatus.Working && expires > now && cleanupChecked && observed!.Value.Equals(FactValue.Integer(0));
    }
}
