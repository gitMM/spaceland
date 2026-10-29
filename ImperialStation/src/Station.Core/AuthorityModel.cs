using Station.Contracts;

namespace Station.Core
{
    public enum ExpectationStatus { AwaitingResult, Satisfied, Overdue, Cancelled, Unestimated }
    public enum AcknowledgmentStatus { Awaiting, Acknowledged, Rejected, Overdue, Cancelled }
    public enum IncidentWorkResponse { Pending, NoEligibleResponder, EscalationDeferred, Dispatched, DepthLimited, Cancelled }
    public sealed class ReportExpectationSnapshot
    {
        public long AttemptId { get; internal set; }
        public long JobId { get; internal set; }
        public long ActorId { get; internal set; }
        public string Role { get; internal set; } = "";
        public string TaskKind { get; internal set; } = "RepairMachine";
        public long AcknowledgmentMessageId { get; internal set; }
        public long AcknowledgmentTick { get; internal set; }
        public long OriginTerminalId { get; internal set; }
        public long ReturnTerminalId { get; internal set; }
        public long UnscaledEstimate { get; internal set; }
        public long RatioAtCreation { get; internal set; }
        public long Slack { get; internal set; }
        public long BaseDeadline { get; internal set; } = -1;
        public long OffDutyExtension { get; internal set; }
        public long Deadline { get; internal set; } = -1;
        public long RosterVersion { get; internal set; }
        public ExpectationStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "";
        public long ResultMessageId { get; internal set; }
        public long ResultTick { get; internal set; } = -1;
        internal ReportExpectationSnapshot Copy() => (ReportExpectationSnapshot)MemberwiseClone();
    }
    public sealed class AcknowledgmentWatchSnapshot
    {
        public long AttemptId { get; internal set; }
        public long OrderMessageId { get; internal set; }
        public long PostedTick { get; internal set; }
        public long Deadline { get; internal set; }
        public long RosterVersion { get; internal set; }
        public long OffDutyExtension { get; internal set; }
        public AcknowledgmentStatus Status { get; internal set; }
        public long ResponseMessageId { get; internal set; }
        internal AcknowledgmentWatchSnapshot Copy() => (AcknowledgmentWatchSnapshot)MemberwiseClone();
    }
    public sealed class MissingReportIncident
    {
        public long Id { get; internal set; }
        public long AttemptId { get; internal set; }
        public long JobId { get; internal set; }
        public long ActorId { get; internal set; }
        public int Cohort { get; internal set; } = -1;
        public long OpenedTick { get; internal set; }
        public long OpeningSequence { get; internal set; }
        public long OpeningEventId { get; internal set; }
        public string Kind { get; internal set; } = "MissingWorkReport";
        public long ResponseMessageId { get; internal set; }
        internal MissingReportIncident Copy() => (MissingReportIncident)MemberwiseClone();
        public long AdmissionBucket { get; internal set; } = -1;
        public long AdmissionTick { get; internal set; } = -1;
        public long ReplacementAttemptId { get; internal set; }
        public long ReplacementResultMessageId { get; internal set; }
        public string WorkResolution { get; internal set; } = "Unverified";
        public int ReplacementDepth { get; internal set; }
        public IncidentWorkResponse WorkResponse { get; internal set; }
        public string WorkerStatusResponse { get; internal set; } = "PendingPatrol";
        public long PatrolOrderMessageId { get; internal set; }
        public long PatrolResultMessageId { get; internal set; }
        public string PatrolReason { get; internal set; } = "";
        public string ResponseReason { get; internal set; } = "";
    }
    public sealed class IncidentAdmissionBucket
    {
        public long Bucket { get; internal set; }
        public int Cohort { get; internal set; }
        public long OpenedTick { get; internal set; }
        public long KnownOnDutyHeadcount { get; internal set; }
        public long Budget { get; internal set; }
        public long Used { get; internal set; }
        internal IncidentAdmissionBucket Copy() => (IncidentAdmissionBucket)MemberwiseClone();
    }
    public sealed class LearnedDurationSnapshot
    {
        public string Role { get; internal set; } = "";
        public string TaskKind { get; internal set; } = "";
        public long Ratio { get; internal set; }
        public long Samples { get; internal set; }
        internal LearnedDurationSnapshot Copy() => (LearnedDurationSnapshot)MemberwiseClone();
    }
}
