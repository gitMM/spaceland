using Station.Contracts;

namespace Station.Core
{
    public enum FacilityTaskStatus { Pending, Assigned, NeedsAttention, ReportedComplete }
    public sealed class FacilityObservationSnapshot
    {
        public long ActorId { get; internal set; }
        public FactKey Subject { get; internal set; }
        public bool Adverse { get; internal set; }
        public long Generation { get; internal set; }
        public long LastObservedTick { get; internal set; } = -1;
        public long LastQueuedTick { get; internal set; } = -1;
        public long NextRepeatTick { get; internal set; } = -1;
        public string OperationKey { get; internal set; } = "";
        public long LastMessageId { get; internal set; }
        internal FacilityObservationSnapshot Copy() => (FacilityObservationSnapshot)MemberwiseClone();
    }
    public sealed class FacilityTaskSnapshot
    {
        public long JobId { get; internal set; }
        public FactKey Subject { get; internal set; }
        public long CreatedTick { get; internal set; }
        public long FirstMessageId { get; internal set; }
        public long LatestMessageId { get; internal set; }
        public long LatestObservedTick { get; internal set; }
        public long ReportsReceived { get; internal set; }
        public long AttemptId { get; internal set; }
        public FacilityTaskStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "";
        public long CompletionMessageId { get; internal set; }
        public long ResolvedObservedTick { get; internal set; } = -1;
        public long CauseId { get; internal set; }
        internal FacilityTaskSnapshot Copy() => (FacilityTaskSnapshot)MemberwiseClone();
    }
}
