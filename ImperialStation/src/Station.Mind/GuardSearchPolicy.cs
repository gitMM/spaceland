using Station.Contracts;

namespace Station.Mind
{
    // A formed guard team's local search over the order's published candidates.
    public static class GuardSearchPolicy
    {
        public const string SearchReason = "SearchKnownCandidates";

        // A team formed while this guard waited moves straight to searching.
        public static bool TeamFormed(long formedTick) => formedTick >= 0;

        public static GuardPlanFailure Failure(PlanOutcome outcome) =>
            outcome == PlanOutcome.BudgetExhausted ? new GuardPlanFailure("PlanBudgetExhausted", null) : new GuardPlanFailure("NoKnownSearchRoute", "SearchUnreachable");

        // Beginning the escort is not progress that clears earlier failure backoff.
        public static bool ResetsBackoff(ActionKind step) => step != ActionKind.BeginEscort;

        // A located target keeps its sighting reason; otherwise record the policy.
        public static string SelectedReason(KnownGuardOrderStatus status, string policy, string reason) => status != KnownGuardOrderStatus.TargetLocated ? policy : reason;

        public static ActionIntent Intent(PlanResult plan, Id<ActorTag> actor, long orderId)
        { var step = plan.Steps[0].Intent; return new ActionIntent(actor, step.Kind, step.Target, step.Item, serviceId: step.ServiceId, targetActor: step.TargetActor, guardOrderId: orderId); }

        public static GuardSearchObservation Observe(bool found, int inspectedCount, int candidateCount) =>
            found ? GuardSearchObservation.Located : inspectedCount == candidateCount ? GuardSearchObservation.Exhausted : GuardSearchObservation.Continue;
    }
}
