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
                    RemainsOnDuty(c.Person.Roster, view.Tick, policy.MinimumRemainingDutyTicks) && !c.Committed && c.KnownOrigin.HasValue)
                .Select(c => (Actor: c.Person.ActorId, Cost: TravelEstimates.Authority(c.KnownOrigin!.Value, policy.Rendezvous, view.NominalTravelTicks)))
                .OrderBy(c => c.Cost).ThenBy(c => c.Actor).Select(c => c.Actor).Distinct().Take(policy.GuardCount).ToArray();
            return team.Length == policy.GuardCount ? team : Array.Empty<long>();
        }
        // Uses only the published roster: a responder about to go off duty would read
        // the order at their post-duty visit and act only in a later shift. Ordinary
        // responders need one configured hour of known duty left; escort teams use
        // their policy's declared minimum.
        public static bool RemainsOnDuty(RosterPayload roster, long tick, long minimum) => roster.OnDuty(tick) && roster.NextBoundary(tick) - tick >= Math.Max(1, minimum);
        public static long CleanupReplacement(IAuthorityView view, Id<TileTag> tile, long excludedActor, IEnumerable<DispatchCandidate> candidates)
        {
            var job = view.Publication.Work?.Cleanable.SingleOrDefault(c => c.Tile == tile);
            if (job == null) return 0;
            return candidates.Where(c => c.Person.ActorId != excludedActor && !c.Committed && c.KnownOrigin.HasValue &&
                    c.Person.Roster != null && RemainsOnDuty(c.Person.Roster, view.Tick, c.Person.Roster.DayTicks / 24) && job.Roles.Contains(c.Person.Role, StringComparer.Ordinal))
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
        // With preemptCleanup, an uncommitted responder is still preferred; otherwise
        // the nearest one committed only to cleanup may be ordered onto the repair.
        public static long Replacement(IAuthorityView view, long machineId, long excludedActor, IEnumerable<DispatchCandidate> candidates, bool preemptCleanup = false)
        {
            var job = view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == machineId);
            if (job == null) return 0;
            var eligible = candidates.Where(c => c.Person.ActorId != excludedActor && c.KnownOrigin.HasValue &&
                    c.Person.Roster != null && RemainsOnDuty(c.Person.Roster, view.Tick, c.Person.Roster.DayTicks / 24) && job.Roles.Contains(c.Person.Role, StringComparer.Ordinal)).ToArray();
            long Nearest(IEnumerable<DispatchCandidate> from) => from
                .Select(c => (Actor: c.Person.ActorId, Cost: TravelEstimates.Authority(c.KnownOrigin!.Value, job.Position, view.NominalTravelTicks)))
                .OrderBy(c => c.Cost).ThenBy(c => c.Actor).Select(c => c.Actor).FirstOrDefault();
            long selected = Nearest(eligible.Where(c => !c.Committed));
            return selected != 0 || !preemptCleanup ? selected : Nearest(eligible.Where(c => c.CleanupOnly));
        }
    }
}
