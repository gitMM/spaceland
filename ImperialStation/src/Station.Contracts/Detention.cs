using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public sealed class DetentionPolicy
    {
        public long SentenceTicks { get; }
        public long OrderTicks { get; }
        public EscortDispatchPolicy? Escort { get; }
        public IReadOnlyList<DetentionFacility> Facilities { get; }
        public DetentionPolicy(long sentenceTicks = 864000, long orderTicks = 1728000, EscortDispatchPolicy? escort = null, IEnumerable<DetentionFacility>? facilities = null)
        {
            if (sentenceTicks < 1 || orderTicks < 1) throw new ArgumentException("Invalid detention policy durations.");
            var list = (facilities ?? Array.Empty<DetentionFacility>()).ToArray();
            if (list.Any(f => f == null) || list.Select(f => f.Id).Distinct().Count() != list.Length) throw new ArgumentException("Duplicate detention facility.");
            SentenceTicks = sentenceTicks; OrderTicks = orderTicks; Escort = escort; Facilities = Array.AsReadOnly(list.OrderBy(f => f.Id).ToArray());
        }
    }
    public enum DetentionDecisionState { Waiting, Approved, Rejected }
    public sealed class DetentionRequestPayload
    {
        public long AccountId { get; }
        public long CaseId { get; }
        public long CaseReplyMessageId { get; }
        public DetentionRequestPayload(long accountId, long caseId, long caseReplyMessageId)
        { if (accountId < 1 || caseId < 1 || caseReplyMessageId < 1) throw new ArgumentException("Invalid detention request."); AccountId = accountId; CaseId = caseId; CaseReplyMessageId = caseReplyMessageId; }
        public bool Same(DetentionRequestPayload other) => AccountId == other.AccountId && CaseId == other.CaseId && CaseReplyMessageId == other.CaseReplyMessageId;
    }
    public sealed class DetentionDecisionPayload
    {
        public long RequestMessageId { get; }
        public long AccountId { get; }
        public long AuthorizationId { get; }
        public DetentionDecisionState State { get; }
        public string Reason { get; }
        public int QualifyingCount { get; }
        public long Expires { get; }
        public long CountThroughCaseId { get; }
        public DetentionDecisionPayload(long requestMessageId, long accountId, long authorizationId, DetentionDecisionState state, string reason, int qualifyingCount, long expires = 0, long countThroughCaseId = 0)
        {
            if (requestMessageId < 1 || accountId < 1 || authorizationId < 0 || !Enum.IsDefined(typeof(DetentionDecisionState), state) || string.IsNullOrWhiteSpace(reason) || qualifyingCount < 0 || countThroughCaseId < 0 ||
                (state == DetentionDecisionState.Approved ? authorizationId < 1 || expires < 1 : authorizationId != 0 || expires != 0)) throw new ArgumentException("Invalid detention decision.");
            RequestMessageId = requestMessageId; AccountId = accountId; AuthorizationId = authorizationId; State = state; Reason = reason; QualifyingCount = qualifyingCount; Expires = expires;
            CountThroughCaseId = countThroughCaseId;
        }
        public bool Same(DetentionDecisionPayload other) => RequestMessageId == other.RequestMessageId && AccountId == other.AccountId && AuthorizationId == other.AuthorizationId && State == other.State && Reason == other.Reason && QualifyingCount == other.QualifyingCount && Expires == other.Expires && CountThroughCaseId == other.CountThroughCaseId;
    }
}
