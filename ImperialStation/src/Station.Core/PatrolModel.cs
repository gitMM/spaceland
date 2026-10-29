using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public enum LocalPatrolStatus { Read, AcceptancePending, Searching, ResultPending, Completed }
    public sealed class PatrolOrderSnapshot
    {
        public long OrderMessageId { get; internal set; }
        public long ActorId { get; internal set; }
        public PatrolOrderPayload Payload { get; internal set; } = null!;
        public long AcknowledgmentMessageId { get; internal set; }
        public long ResultMessageId { get; internal set; }
        public string Status { get; internal set; } = "Posted";
        internal PatrolOrderSnapshot Copy() => (PatrolOrderSnapshot)MemberwiseClone();
    }
    public sealed class PatrolObservation
    {
        public long Tick { get; }
        public long ActionId { get; }
        public Coord Tile { get; }
        public bool Found { get; }
        public PatrolObservation(long tick, long actionId, Coord tile, bool found)
        { Tick = tick; ActionId = actionId; Tile = tile; Found = found; }
    }
    public sealed class LocalPatrolSnapshot
    {
        public long OrderMessageId { get; internal set; }
        public long ActorId { get; internal set; }
        public PatrolOrderPayload Payload { get; internal set; } = null!;
        public long ReadTick { get; internal set; }
        public LocalPatrolStatus Status { get; internal set; }
        public int CandidateIndex { get; internal set; }
        public long NextDecision { get; internal set; } = -1;
        public long Backoff { get; internal set; }
        public string Reason { get; internal set; } = "";
        public IReadOnlyList<PatrolObservation> Observations { get; internal set; } = Array.Empty<PatrolObservation>();
        internal LocalPatrolSnapshot Copy() { var copy = (LocalPatrolSnapshot)MemberwiseClone(); copy.Observations = Observations.ToArray(); return copy; }
    }
}
