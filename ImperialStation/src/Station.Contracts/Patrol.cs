using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public sealed class PatrolOrderPayload
    {
        public long IncidentId { get; }
        public Id<ActorTag> TargetActor { get; }
        public long Expires { get; }
        public string RequiredRole { get; }
        public IReadOnlyList<Coord> Candidates { get; }
        public PatrolOrderPayload(long incidentId, Id<ActorTag> targetActor, long expires, string requiredRole, IEnumerable<Coord> candidates)
        {
            var locations = candidates.ToArray();
            if (incidentId < 1 || targetActor.Value < 1 || expires < 1 || string.IsNullOrWhiteSpace(requiredRole) || locations.Length < 1 || locations.Length > 8 || locations.Distinct().Count() != locations.Length)
                throw new ArgumentException("Invalid patrol order or search candidates.");
            IncidentId = incidentId; TargetActor = targetActor; Expires = expires; RequiredRole = requiredRole; Candidates = Array.AsReadOnly(locations);
        }
    }
}
