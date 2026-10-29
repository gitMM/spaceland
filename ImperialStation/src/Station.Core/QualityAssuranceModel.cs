namespace Station.Core
{
    public enum QualityAssuranceStatus { NotSampled, Scheduled, PendingSupervisor, Assigned, ReportedClear, ReportedDiscrepancy, NeedsAttention }
    public sealed class QualityAssuranceSnapshot
    {
        public long RepairAttemptId { get; internal set; }
        public long RepairJobId { get; internal set; }
        public long MachineId { get; internal set; }
        public long CompletionMessageId { get; internal set; }
        public long ReceivedTick { get; internal set; }
        public int SampleRoll { get; internal set; }
        public long DueTick { get; internal set; } = -1;
        public QualityAssuranceStatus Status { get; internal set; }
        public long VerificationJobId { get; internal set; }
        public long VerificationAttemptId { get; internal set; }
        public long ResultMessageId { get; internal set; }
        public string Reason { get; internal set; } = "";
        public long CauseId { get; internal set; }
        internal QualityAssuranceSnapshot Copy() => (QualityAssuranceSnapshot)MemberwiseClone();
    }
    public sealed class VerificationInspectionSnapshot
    {
        public long AttemptId { get; internal set; }
        public long ActorId { get; internal set; }
        public long MachineId { get; internal set; }
        public long Tick { get; internal set; }
        public long ActionId { get; internal set; }
        public bool Operational { get; internal set; }
        public string RequiredResource { get; internal set; } = "";
        public string InstalledResource { get; internal set; } = "";
        public bool Discrepancy => !Operational || RequiredResource != InstalledResource;
        internal VerificationInspectionSnapshot Copy() => (VerificationInspectionSnapshot)MemberwiseClone();
    }
}
