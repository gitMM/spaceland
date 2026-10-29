using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public enum DetentionRequestStatus { Queued, Posted, Approved, Waiting, Rejected }
    public enum DetentionAuthorizationStatus { Authorized, Expired, Revoked }
    public sealed class LocalDetentionRequestSnapshot
    {
        public long CaseReplyMessageId { get; internal set; }
        public long ActorId { get; internal set; }
        public long AccountId { get; internal set; }
        public long CaseId { get; internal set; }
        public DetentionRequestStatus Status { get; internal set; }
        public long RequestMessageId { get; internal set; }
        public long ReplyMessageId { get; internal set; }
        public long ReadTick { get; internal set; } = -1;
        public long AuthorizationId { get; internal set; }
        public long Expires { get; internal set; }
        public string Reason { get; internal set; } = "";
        internal LocalDetentionRequestSnapshot Copy() => (LocalDetentionRequestSnapshot)MemberwiseClone();
    }
    public sealed class DetentionRequestSnapshot
    {
        public long MessageId { get; internal set; }
        public long ReceivedTick { get; internal set; }
        public long ValidationRecordVersion { get; internal set; }
        public long ReplyMessageId { get; internal set; }
        public DetentionDecisionPayload Decision { get; internal set; } = null!;
        internal DetentionRequestSnapshot Copy() => (DetentionRequestSnapshot)MemberwiseClone();
    }
    // Administrative authorization. It does not itself constitute a guard team or custody.
    public sealed class DetentionAuthorizationSnapshot
    {
        public long Id { get; internal set; }
        public long AccountId { get; internal set; }
        public long ActorId { get; internal set; }
        public long RequestMessageId { get; internal set; }
        public long CaseId { get; internal set; }
        public long IssuedTick { get; internal set; }
        public long RevokedTick { get; internal set; } = -1; public long Expires { get; internal set; }
        public long SentenceTicks { get; internal set; }
        public long CountThroughCaseId { get; internal set; }
        public long CountThroughRecordVersion { get; internal set; }
        public DetentionAuthorizationStatus Status { get; internal set; }
        public IReadOnlyList<long> JustifyingCases { get; internal set; } = Array.Empty<long>();
        internal DetentionAuthorizationSnapshot Copy() { var copy = (DetentionAuthorizationSnapshot)MemberwiseClone(); copy.JustifyingCases = JustifyingCases.ToArray(); return copy; }
    }
}
