using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public enum EscortDispatchStatus { AwaitingGuards, AwaitingReaders, TeamForming, Blocked, Expired, Detained, Released, Cancelled, NeedsAttention }
    public enum GuardOrderStatus { AcknowledgmentPending, EquipmentPending, Rejected, Expired, AtRendezvous, WaitingTeam, Searching, TargetLocated, SearchExhausted, EscortInterrupted, TransferredToFacility, Revoked, SearchAbandoned }
    public sealed class EscortDispatchSnapshot
    {
        public long AuthorizationId { get; internal set; }
        public long DispatchedTick { get; internal set; } = -1;
        public EscortDispatchStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "AwaitingGuardTeam";
        public EscortOrderPayload? Payload { get; internal set; }
        public IReadOnlyList<long> OrderMessageIds { get; internal set; } = Array.Empty<long>();
        internal EscortDispatchSnapshot Copy() { var copy = (EscortDispatchSnapshot)MemberwiseClone(); copy.OrderMessageIds = OrderMessageIds.ToArray(); return copy; }
    }
    public sealed class GuardAssignmentSnapshot
    {
        public long OrderMessageId { get; internal set; }
        public long ActorId { get; internal set; }
        public long AuthorizationId { get; internal set; }
        public long AcknowledgmentMessageId { get; internal set; }
        public string Response { get; internal set; } = "AwaitingAcknowledgment";
        internal GuardAssignmentSnapshot Copy() => (GuardAssignmentSnapshot)MemberwiseClone();
    }
    public sealed class LocalGuardOrderSnapshot
    {
        public long OrderMessageId { get; internal set; }
        public long ActorId { get; internal set; }
        public EscortOrderPayload Payload { get; internal set; } = null!;
        public long ReadTick { get; internal set; }
        public long AcknowledgmentMessageId { get; internal set; }
        public string Response { get; internal set; } = "Accepted";
        public GuardOrderStatus Status { get; internal set; }
        public long NextDecision { get; internal set; } = -1;
        public long Backoff { get; internal set; }
        public long ReadyTick { get; internal set; } = -1;
        public long ReadyItemId { get; internal set; }
        public string Reason { get; internal set; } = "";
        public long JoinTick { get; internal set; } = -1;
        public long JoinActionId { get; internal set; }
        public long FormedTick { get; internal set; } = -1;
        public int CandidateIndex { get; internal set; }
        public IReadOnlyList<PatrolObservation> SearchObservations { get; internal set; } = Array.Empty<PatrolObservation>();
        internal LocalGuardOrderSnapshot Copy() { var copy = (LocalGuardOrderSnapshot)MemberwiseClone(); copy.SearchObservations = SearchObservations.ToArray(); return copy; }
    }
}
