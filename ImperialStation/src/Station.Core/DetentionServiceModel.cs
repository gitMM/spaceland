using Station.Contracts;

namespace Station.Core
{
    public enum DetentionStayStatus { Reserved, Cancelled, Detained, Releasing, Released }
    public sealed class DetentionFacilitySnapshot
    {
        public DetentionFacility Definition { get; internal set; } = null!;
        public bool Powered { get; internal set; }
        public bool Connected { get; internal set; }
        public int Reservations { get; internal set; }
        public int Occupants { get; internal set; }
        public int Operations { get; internal set; }
        internal DetentionFacilitySnapshot Copy() => (DetentionFacilitySnapshot)MemberwiseClone();
    }
    public sealed class DetentionStaySnapshot
    {
        public long AuthorizationId { get; internal set; }
        public long FacilityId { get; internal set; }
        public long ActorId { get; internal set; }
        public long AccountId { get; internal set; }
        public long LeaderId { get; internal set; }
        public DetentionStayStatus Status { get; internal set; }
        public long IntakeActionId { get; internal set; }
        public long IntakeStarted { get; internal set; }
        public long IntakeTick { get; internal set; } = -1;
        public long ReleaseDue { get; internal set; } = -1;
        public long ReleaseActionId { get; internal set; }
        public long ReleaseStarted { get; internal set; } = -1;
        public long ReleaseTick { get; internal set; } = -1;
        public long NextRelease { get; internal set; } = -1;
        public string Reason { get; internal set; } = "";
        public long IntakeReceiptId { get; internal set; }
        public long IntakeReceivedTick { get; internal set; } = -1;
        public long IntakeEventId { get; internal set; }
        public long ReleaseReceiptId { get; internal set; }
        public long ReleaseReceivedTick { get; internal set; } = -1;
        public long ReleaseNoticeId { get; internal set; }
        internal DetentionStaySnapshot Copy() => (DetentionStaySnapshot)MemberwiseClone();
    }
}
