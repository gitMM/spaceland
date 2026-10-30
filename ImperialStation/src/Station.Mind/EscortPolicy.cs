using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Escort dispatch: subject search sites, guard team, acknowledgment escalation.
    public static class EscortPolicy
    {
        public const int MaximumSearchSites = 8;
        // A received negative search at the last terminal visit, made after that visit.
        public static bool SearchedSinceVisit(long subjectActor, Coord origin, long visitTick, IEnumerable<GuardSearchEvidence> outcomes) =>
            outcomes.Any(o => o.Outcome == "TargetNotLocated" && o.ObservedTick >= visitTick && o.Order is EscortOrderPayload old && old.SubjectActorId == subjectActor && old.Candidates.Contains(origin));
        // Last terminal visit unless already searched since, then open assignment sites
        // newest first, then the rostered rest tile, then the demoted visit.
        public static Coord[] SearchSites(Coord? origin, bool searchedSinceVisit, IEnumerable<KnownAssignmentSite> assignments, Coord? rest)
        {
            var candidates = new List<Coord>();
            if (origin.HasValue && !searchedSinceVisit) candidates.Add(origin.Value);
            // Received terminal outcomes and administrative cancellation mean
            // an old assignment is no longer a current work destination.
            // The last reported terminal visit remains independent evidence.
            foreach (var assignment in assignments.OrderByDescending(a => a.AttemptId))
                if (assignment.Site.HasValue) candidates.Add(assignment.Site.Value);
            if (rest is Coord tile) candidates.Add(tile);
            // A received negative search lowers an older terminal clue's
            // priority. Keep it as a possibility; absence was only local.
            // A newer authenticated visit restores its original priority.
            if (origin.HasValue && searchedSinceVisit) candidates.Add(origin.Value);
            return candidates.Distinct().Take(MaximumSearchSites).ToArray();
        }
        // An interrupted team is not silently reused: guards already assigned to this
        // authorization are excluded. Cheap roster filters run before gathering commitments.
        public static IReadOnlyList<long> Team(IAuthorityView view, long subjectActor, EscortDispatchPolicy policy, IEnumerable<long> priorGuards, Func<KnownPersonnel, DispatchCandidate> gather)
        {
            var prior = new SortedSet<long>(priorGuards);
            return AuthorityDispatch.EscortTeam(view, subjectActor, policy, view.Personnel.Where(p =>
                p.ActorId != subjectActor && p.Role == policy.GuardRole && p.Roster != null && p.Roster.OnDuty(view.Tick) && !prior.Contains(p.ActorId)).Select(gather));
        }
        // An urgent order is bounded: two configured hours (the station day / 12), or the
        // authority's ordinary acknowledgment interval without a roster.
        public static long UrgentAcknowledgmentTicks(long? dayTicks, long? unacknowledgedTicks) => dayTicks.HasValue ? Math.Max(1, dayTicks.Value / 12) : unacknowledgedTicks ?? 1;
        // Escalate only when no team member has acknowledged and the authorization holds.
        public static bool ReassignUnacknowledgedTeam(IEnumerable<long> acknowledgmentMessageIds, bool authorizationValid) => !acknowledgmentMessageIds.Any(id => id > 0) && authorizationValid;
        public static EscortTeamResponse TeamResponse(string response, bool blocked, IEnumerable<string> teamResponses) =>
            response != "Accepted" ? EscortTeamResponse.Blocked : !blocked && teamResponses.All(r => r == "Accepted") ? EscortTeamResponse.TeamForming : EscortTeamResponse.Unchanged;
        // A guard's reply to a read escort order.
        public static string GuardOrderResponse(EscortOrderPayload payload, string role, long tick) =>
            payload.Expires <= tick ? "KnownOrderExpired" : role != payload.Dispatch.GuardRole ? "NotQualified" : "Accepted";
    }
}
