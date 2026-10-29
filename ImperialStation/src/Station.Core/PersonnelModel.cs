using Station.Contracts;

namespace Station.Core
{
    public enum CaseFilingStatus { Queued, Posted, Accepted, NeedsReview }
    public sealed class CaseFilingSnapshot
    {
        public long VerificationAttemptId { get; internal set; }
        public long ActorId { get; internal set; }
        public long InspectionReportId { get; internal set; }
        public long FilingMessageId { get; internal set; }
        public long ReplyMessageId { get; internal set; }
        public long ReadTick { get; internal set; } = -1;
        public long CaseId { get; internal set; }
        public int QualifyingCount { get; internal set; }
        public CaseFilingStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "";
        internal CaseFilingSnapshot Copy() => (CaseFilingSnapshot)MemberwiseClone();
    }
    public sealed class DisciplinaryCaseSnapshot
    {
        public long Id { get; internal set; }
        public long FilingMessageId { get; internal set; }
        public long AcceptedTick { get; internal set; }
        public long ValidationRecordVersion { get; internal set; }
        public CaseSubmissionPayload Submission { get; internal set; } = null!;
        internal DisciplinaryCaseSnapshot Copy() => (DisciplinaryCaseSnapshot)MemberwiseClone();
    }
    public sealed class ReprimandSnapshot
    {
        public long CaseId { get; internal set; }
        public long AccountId { get; internal set; }
        public long AcceptedTick { get; internal set; }
        public string Policy { get; internal set; } = "";
        internal ReprimandSnapshot Copy() => (ReprimandSnapshot)MemberwiseClone();
    }
    public sealed class QueryDisclosureSnapshot
    {
        public long ActionId { get; internal set; }
        public long ActorId { get; internal set; }
        public long AccountId { get; internal set; }
        public long TerminalId { get; internal set; }
        public long RecordVersion { get; internal set; }
        public long CapturedVersion { get; internal set; }
        public long CompletedTick { get; internal set; }
        internal QueryDisclosureSnapshot Copy() => (QueryDisclosureSnapshot)MemberwiseClone();
    }
}
