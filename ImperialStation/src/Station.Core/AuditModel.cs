using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public enum AuditStatus { QueryingAttempts, QueryingReports, ReadyToFile, NeedsReview }
    public sealed class AuditSnapshot
    {
        public long VerificationAttemptId { get; internal set; }
        public long ActorId { get; internal set; }
        public long TerminalId { get; internal set; }
        public AuditStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "";
        public long NextDecision { get; internal set; } = -1;
        public long Backoff { get; internal set; }
        public long Cursor { get; internal set; }
        public long RecordVersion { get; internal set; } = -1;
        public long AttemptVersion { get; internal set; } = -1;
        public long ReportVersion { get; internal set; } = -1;
        public int Pages { get; internal set; }
        public AccountabilityFinding? Finding { get; internal set; }
        public IReadOnlyList<RecordEntry> Evidence { get; internal set; } = Array.Empty<RecordEntry>();
        public IReadOnlyList<long> QueryActionIds { get; internal set; } = Array.Empty<long>();
        internal AuditSnapshot Copy() { var result = (AuditSnapshot)MemberwiseClone(); result.Evidence = Evidence.ToArray(); result.QueryActionIds = QueryActionIds.ToArray(); return result; }
        internal bool Pending => Status is AuditStatus.QueryingAttempts or AuditStatus.QueryingReports;
    }
}
