using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Core
{
    public sealed class DetentionRevocationSnapshot
    {
        public long AuthorizationId { get; internal set; }
        public long Tick { get; internal set; }
        public string OperationKey { get; internal set; } = "";
        public bool Applied { get; internal set; }
        public IReadOnlyList<long> Notices { get; internal set; } = Array.Empty<long>();
        internal DetentionRevocationSnapshot Copy() { var c = (DetentionRevocationSnapshot)MemberwiseClone(); c.Notices = Notices.ToArray(); return c; }
    }
    public sealed class KnownGuardRevocationSnapshot
    {
        public long OrderMessageId { get; internal set; }
        public long LearnedTick { get; internal set; }
        public long MessageId { get; internal set; }
        public long ServiceActionId { get; internal set; }
        public long FacilityId { get; internal set; }
        internal KnownGuardRevocationSnapshot Copy() => (KnownGuardRevocationSnapshot)MemberwiseClone();
    }
    public sealed class GuardOutcomeSnapshot
    {
        public long OrderMessageId { get; internal set; }
        public long ActorId { get; internal set; }
        public string Outcome { get; internal set; } = "";
        public long ObservedTick { get; internal set; }
        public long MessageId { get; internal set; }
        public long ReceivedTick { get; internal set; } = -1;
        internal GuardOutcomeSnapshot Copy() => (GuardOutcomeSnapshot)MemberwiseClone();
    }
}
