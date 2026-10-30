using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class AuthorityDispatch
    {
        public static IReadOnlyList<long> EscortTeam(IAuthorityView view, long excludedActor, EscortDispatchPolicy policy, IEnumerable<DispatchCandidate> candidates)
        {
            var team = candidates.Where(c => c.Person.ActorId != excludedActor && c.Person.Role == policy.GuardRole && c.Person.Roster != null &&
                    c.Person.Roster.OnDuty(view.Tick) && !c.Committed && c.KnownOrigin.HasValue)
                .Select(c => (Actor: c.Person.ActorId, Cost: TravelEstimates.Authority(c.KnownOrigin!.Value, policy.Rendezvous, view.NominalTravelTicks)))
                .OrderBy(c => c.Cost).ThenBy(c => c.Actor).Select(c => c.Actor).Distinct().Take(policy.GuardCount).ToArray();
            return team.Length == policy.GuardCount ? team : Array.Empty<long>();
        }
        public static long CleanupReplacement(IAuthorityView view, Id<TileTag> tile, long excludedActor, IEnumerable<DispatchCandidate> candidates)
        {
            var job = view.Publication.Work?.Cleanable.SingleOrDefault(c => c.Tile == tile);
            if (job == null) return 0;
            return candidates.Where(c => c.Person.ActorId != excludedActor && !c.Committed && c.KnownOrigin.HasValue &&
                    c.Person.Roster != null && c.Person.Roster.OnDuty(view.Tick) && job.Roles.Contains(c.Person.Role, StringComparer.Ordinal))
                .Select(c => (Actor: c.Person.ActorId, Cost: TravelEstimates.Authority(c.KnownOrigin!.Value, job.Position, view.NominalTravelTicks)))
                .OrderBy(c => c.Cost).ThenBy(c => c.Actor).Select(c => c.Actor).FirstOrDefault();
        }
        public static long Patrol(IAuthorityView view, long excludedActor, string requiredRole, IReadOnlyList<Coord> locations, IEnumerable<DispatchCandidate> candidates)
        {
            if (locations.Count == 0) return 0;
            return candidates.Where(c => c.Person.ActorId != excludedActor && c.Person.Role == requiredRole && c.Person.Roster != null &&
                    c.Person.Roster.OnDuty(view.Tick) && !c.Committed && c.KnownOrigin.HasValue)
                .Select(c => (Actor: c.Person.ActorId, Cost: TravelEstimates.Authority(c.KnownOrigin!.Value, locations[0], view.NominalTravelTicks)))
                .OrderBy(c => c.Cost).ThenBy(c => c.Actor).Select(c => c.Actor).FirstOrDefault();
        }
        public static long Replacement(IAuthorityView view, long machineId, long excludedActor, IEnumerable<DispatchCandidate> candidates)
        {
            var job = view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == machineId);
            if (job == null) return 0;
            // A broken machine outranks routine cleanup: a responder committed only to
            // cleaning may be chosen, but an uncommitted one is always preferred.
            return candidates.Where(c => c.Person.ActorId != excludedActor && (!c.Committed || c.RoutineOnly) && c.KnownOrigin.HasValue &&
                    c.Person.Roster != null && c.Person.Roster.OnDuty(view.Tick) && job.Roles.Contains(c.Person.Role, StringComparer.Ordinal))
                .Select(c => (Actor: c.Person.ActorId, c.Committed, Cost: TravelEstimates.Authority(c.KnownOrigin!.Value, job.Position, view.NominalTravelTicks)))
                .OrderBy(c => c.Committed).ThenBy(c => c.Cost).ThenBy(c => c.Actor).Select(c => c.Actor).FirstOrDefault();
        }
    }
}
