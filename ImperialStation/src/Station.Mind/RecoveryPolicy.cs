using System;
using System.Collections.Generic;
using Station.Contracts;

namespace Station.Mind
{
    // Autonomous credential-recovery decisions around CredentialPlanning.
    public static class RecoveryPolicy
    {
        public const string AwaitingReplyReason = "AwaitingRecoveryReply";

        // A pending recovery whose private retry clock has expired claims the citizen.
        public static bool Ready(bool pending, long retryAt, long now) => pending && retryAt <= now;

        // A known urgent order wins unless that order itself needs the missing credential.
        public static bool YieldsToUrgentOrder(bool knownUrgentOrder, bool urgentCredentialBlocked) => knownUrgentOrder && !urgentCredentialBlocked;

        // With a usable credential the remaining step is reading current orders;
        // otherwise plan the recovery protocol over the known desk directory.
        public static PlanResult Plan(IActorView view, RecoveryPlanningState memory, Func<IReadOnlyList<PublicRecoveryDesk>> knownDesks, CredentialRecoveryTimings timings, int candidates, int depth, int budget) =>
            CredentialPlanning.KnownUsableCredential(view) ? ReportPlanning.PlanInbox(view, timings.LoginTicks, timings.ReadTicks, candidates, depth, budget) :
                CredentialPlanning.Plan(view, memory, knownDesks(), timings.IdentityCheckTicks, timings.PostTicks, timings.ReadTicks, timings.LatencyTicks, timings.ItemActionTicks, Math.Min(6, candidates), depth, budget);

        // The step to start, or null when the plan offers none.
        public static ActionIntent? FirstIntent(PlanResult plan)
        {
            if (plan.Outcome != PlanOutcome.PlanFound || plan.Steps.Count == 0) return null;
            var intent = plan.Steps[0].Intent;
            if (intent.Kind == ActionKind.PickUp && intent.Item.Value == 0) throw new InvalidOperationException("Unbound recovery output cannot execute.");
            return intent;
        }

        public static string RetryReason(PlanOutcome outcome, bool alternateCapability) =>
            outcome == PlanOutcome.BudgetExhausted ? "PlanBudgetExhausted" : !alternateCapability ? "MissingRecoveryCapabilityOrKnownCard" : "NoKnownRecoveryRouteOrService";

        // Reaching another desk does not establish that recovery service works
        // there. Preserve failure backoff across travel so a large station cannot
        // turn an outage into a rapid tour of every desk.
        public static bool ResetsBackoff(ActionKind completed) => completed != ActionKind.Move;

        public static RecoveryProgress AfterAction(ActionKind completed, bool caseKnown, bool capturedInboxUnread)
        {
            if (completed == ActionKind.PostLostCard) return RecoveryProgress.AwaitReply;
            if (completed == ActionKind.ReadRecoveryInbox && !caseKnown) return RecoveryProgress.RetryUnauthorized;
            if (completed == ActionKind.ReadInbox) return capturedInboxUnread ? RecoveryProgress.RetryUnreadInbox : RecoveryProgress.Complete;
            return RecoveryProgress.Continue;
        }

        public static long ReplyWaitUntil(long now, long latencyTicks) => checked(now + 2 * latencyTicks);

        public static bool RevealsMissingCredential(FailureReason reason) => reason is FailureReason.MissingCredential or FailureReason.CredentialRevoked;

        // A revoked replacement at login means the recovered card itself is unusable.
        public static bool ReplacementRevoked(FailureReason reason, ActionKind plannedKind, long issuedCardId) =>
            reason == FailureReason.CredentialRevoked && plannedKind == ActionKind.Login && issuedCardId > 0;

        // Keep observed service failure beyond the next decision. Expiring it at
        // RetryAt made every retry choose the same failed desk.
        public static long? DeskUnavailableUntil(FailureReason reason, long plannedTerminal, long now, long backoff, long maximumBackoff) =>
            plannedTerminal > 0 && reason is FailureReason.RequiredServiceNotPresent or FailureReason.InsufficientMaterials or FailureReason.NotQualified or FailureReason.IdentityVerificationFailed
                ? checked(now + backoff + maximumBackoff) : (long?)null;

        public static string FailureRetryReason(FailureReason reason) => reason == FailureReason.None ? "Interrupted" : reason.ToString();
    }
}
