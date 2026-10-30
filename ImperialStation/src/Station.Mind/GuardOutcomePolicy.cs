using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Classification of guard-reported escort outcomes, the authority's reaction to them and a
    // guard's own retry choice after a failed local custody handoff.
    public static class GuardOutcomePolicy
    {
        // Outcomes after which the reporting guard will not continue this order.
        public static bool Terminal(string outcome) => outcome is "TargetNotLocated" or "EscortInterrupted" or "KnownOrderExpired" or "AuthorizationRevoked" or "TeamNotReady";
        public static bool Valid(string outcome) => Terminal(outcome) || outcome is "EquipmentUnavailable" or "RendezvousUnreachable" or "SearchUnreachable";
        // A guard report on an order of this dispatch flags it for attention unless the dispatch is settled.
        public static bool NeedsAttention(bool orderOfDispatch, EscortDispatchProgress status) => orderOfDispatch &&
            !(status is EscortDispatchProgress.Blocked or EscortDispatchProgress.Expired or EscortDispatchProgress.Cancelled or EscortDispatchProgress.Detained or EscortDispatchProgress.Released);
        // Every dispatched guard has finished without custody, and the authorization still stands.
        public static bool RequestReplacementTeam(EscortDispatchProgress status, IEnumerable<IEnumerable<string>> outcomesPerOrder, bool authorizationLive) =>
            status == EscortDispatchProgress.NeedsAttention && outcomesPerOrder.All(outcomes => outcomes.Any(Terminal)) && authorizationLive;
        // How a guard interprets a revocation-style notice about its own order.
        public static string RevocationReason(string claim) => claim == "AcknowledgmentOverdue" ? "KnownOrderReassigned" : "KnownAuthorizationRevoked";
        public static CustodyRetry AfterFailedHandoff(FailureReason failure, bool preparingForEscort, bool targetLocated)
        {
            if (preparingForEscort) return CustodyRetry.None;
            if (failure == FailureReason.UnknownRequiredFact && targetLocated) return CustodyRetry.AwaitLocalHandoff;
            return failure == FailureReason.NotAtRequiredTile ? CustodyRetry.Reinspect : CustodyRetry.None;
        }
    }
}
