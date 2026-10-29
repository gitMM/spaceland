using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    // Declared dispatch destinations and equipment requirements, never occupancy or inventory.
    public sealed class EscortDispatchPolicy
    {
        public long FacilityId { get; }
        public Coord Destination { get; }
        public Coord Rendezvous { get; }
        public string RequiredEquipment { get; }
        public string GuardRole { get; }
        public int GuardCount { get; }
        public long EquipTicks { get; }
        public EscortDispatchPolicy(long facilityId, Coord destination, Coord rendezvous, string requiredEquipment, string guardRole = "guard", int guardCount = 2, long equipTicks = 50)
        {
            if (facilityId < 1 || string.IsNullOrWhiteSpace(requiredEquipment) || string.IsNullOrWhiteSpace(guardRole) || guardCount < 1 || guardCount > 8 || equipTicks < 1) throw new ArgumentException("Invalid escort dispatch policy.");
            FacilityId = facilityId; Destination = destination; Rendezvous = rendezvous; RequiredEquipment = requiredEquipment; GuardRole = guardRole; GuardCount = guardCount; EquipTicks = equipTicks;
        }
    }
    public sealed class EscortOrderPayload
    {
        public long AuthorizationId { get; }
        public long DispatchId { get; }
        public long SubjectAccountId { get; }
        public long SubjectActorId { get; }
        public long Expires { get; }
        public long SentenceTicks { get; }
        public EscortDispatchPolicy Dispatch { get; }
        public IReadOnlyList<long> JustifyingCases { get; }
        public IReadOnlyList<long> Guards { get; }
        public IReadOnlyList<Coord> Candidates { get; }
        public long Leader => Guards[0];
        public EscortOrderPayload(long authorizationId, long subjectAccountId, long subjectActorId, long expires, long sentenceTicks, EscortDispatchPolicy dispatch,
            IEnumerable<long> justifyingCases, IEnumerable<long> guards, IEnumerable<Coord> candidates, long dispatchId = 0)
        {
            var cases = justifyingCases.ToArray(); var team = guards.ToArray(); var sites = candidates.ToArray();
            if (authorizationId < 1 || subjectAccountId < 1 || subjectActorId < 1 || expires < 1 || sentenceTicks < 1 || dispatch == null ||
                cases.Length < 1 || cases.Any(c => c < 1) || !cases.SequenceEqual(cases.Distinct().OrderBy(c => c)) || team.Length != dispatch.GuardCount ||
                team.Any(g => g < 1 || g == subjectActorId) || team.Distinct().Count() != team.Length || sites.Length < 1 || sites.Length > 8 || sites.Distinct().Count() != sites.Length)
                throw new ArgumentException("Invalid escort order.");
            if (dispatchId < 0) throw new ArgumentException("Invalid escort dispatch identity.");
            DispatchId = dispatchId == 0 ? authorizationId : dispatchId;
            AuthorizationId = authorizationId; SubjectAccountId = subjectAccountId; SubjectActorId = subjectActorId; Expires = expires; SentenceTicks = sentenceTicks; Dispatch = dispatch;
            JustifyingCases = Array.AsReadOnly(cases); Guards = Array.AsReadOnly(team); Candidates = Array.AsReadOnly(sites);
        }
        public bool Same(EscortOrderPayload p) => DispatchId == p.DispatchId && AuthorizationId == p.AuthorizationId && SubjectAccountId == p.SubjectAccountId && SubjectActorId == p.SubjectActorId && Expires == p.Expires &&
            SentenceTicks == p.SentenceTicks && Dispatch.FacilityId == p.Dispatch.FacilityId && Dispatch.Destination.Equals(p.Dispatch.Destination) && Dispatch.Rendezvous.Equals(p.Dispatch.Rendezvous) &&
            Dispatch.RequiredEquipment == p.Dispatch.RequiredEquipment && Dispatch.GuardRole == p.Dispatch.GuardRole && Dispatch.GuardCount == p.Dispatch.GuardCount && Dispatch.EquipTicks == p.Dispatch.EquipTicks &&
            JustifyingCases.SequenceEqual(p.JustifyingCases) && Guards.SequenceEqual(p.Guards) && Candidates.SequenceEqual(p.Candidates);
    }
}
