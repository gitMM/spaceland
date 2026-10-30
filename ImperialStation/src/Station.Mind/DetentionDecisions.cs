using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Detention approval at the authority desk, and a supervisor's choice to request it.
    // Core supplies registers and validation; this service only chooses state, reason and
    // whether an existing authorization is reused or a new one should be issued.
    public static class DetentionDecisions
    {
        public static DetentionDecisionState EvidenceRejectionState(string validationReason) =>
            validationReason == "PersonnelUnavailable" ? DetentionDecisionState.Waiting : DetentionDecisionState.Rejected;
        public static bool ThresholdMet(int qualifyingCount, int threshold) => qualifyingCount >= threshold;
        public static bool PriorAuthorizationReusable(DetentionDecisionPayload prior, long authorizationExpires, long tick, bool released, bool revoked) =>
            prior.State == DetentionDecisionState.Approved && authorizationExpires > tick && !released && !revoked;
        // A retried identical request whose earlier answer can no longer be reused.
        public static bool PriorRequestClosed(DetentionDecisionPayload prior, long authorizationExpires, long tick, bool released, bool revoked) =>
            prior.State == DetentionDecisionState.Rejected || prior.State == DetentionDecisionState.Approved && !PriorAuthorizationReusable(prior, authorizationExpires, tick, released, revoked);
        public static KnownPersonnel? UniqueSubject(IEnumerable<KnownPersonnel> registry, long account)
        {
            var subject = registry.Where(r => r.AccountId == account).ToArray();
            return subject.Length == 1 ? subject[0] : null;
        }
        public static DetentionRequestChoice Decide(DetentionRequestFacts f)
        {
            string reason = f.ValidationReason; var state = EvidenceRejectionState(reason);
            var prior = f.PriorDecision;
            if (prior != null)
            {
                if (!f.PriorSameDraft) return new DetentionRequestChoice(DetentionDecisionState.Rejected, "IdempotencyConflict");
                // A failed validation keeps its own reason and state for a matching retry.
                if (reason.Length != 0) return new DetentionRequestChoice(state, reason);
                if (PriorAuthorizationReusable(prior, f.PriorAuthorizationExpires, f.Tick, f.PriorReleased, f.PriorRevoked))
                    return new DetentionRequestChoice(DetentionDecisionState.Approved, "DuplicateRequest", DetentionAuthorizationChoice.ReusePrior, prior.AuthorizationId, prior.Expires);
                return prior.State == DetentionDecisionState.Waiting ? new DetentionRequestChoice(DetentionDecisionState.Waiting, prior.Reason) : new DetentionRequestChoice(DetentionDecisionState.Rejected, "PriorRequestClosed");
            }
            if (reason.Length != 0) return new DetentionRequestChoice(state, reason);
            if (!f.PolicyAvailable) return new DetentionRequestChoice(DetentionDecisionState.Waiting, "DetentionPolicyUnavailable");
            if (f.HasActiveAuthorization && f.ActiveAuthorizationExpires > f.Tick)
                return new DetentionRequestChoice(DetentionDecisionState.Approved, "ExistingAuthorization", DetentionAuthorizationChoice.ReuseActive, f.ActiveAuthorizationId, f.ActiveAuthorizationExpires);
            if (!ThresholdMet(f.QualifyingCount, f.Threshold)) return new DetentionRequestChoice(DetentionDecisionState.Rejected, "ThresholdNotMet");
            var subject = UniqueSubject(f.Registry, f.SubjectAccountId);
            if (subject == null) return new DetentionRequestChoice(DetentionDecisionState.Waiting, "AmbiguousSubjectIdentity");
            return new DetentionRequestChoice(DetentionDecisionState.Approved, "Approved", DetentionAuthorizationChoice.IssueNew, subjectActorId: subject.ActorId);
        }
        // A supervisor who read an accepted case reply decides whether to ask for detention.
        public static bool ShouldRequestDetention(CaseDecisionPayload? decision, PublicPersonnelPolicy? personnel, string role) =>
            decision != null && decision.Accepted && personnel != null && role == personnel.SupervisorRole && ThresholdMet(decision.QualifyingCount, personnel.DetentionThreshold);
    }
}
