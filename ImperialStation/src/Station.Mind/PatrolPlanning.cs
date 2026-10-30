using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class PatrolPlanning
    {
        public const int MaximumSearchSites = 8;
        // Authority search order for a silent worker: last terminal visit, job site, rest tile.
        public static Coord[] SearchSites(Coord? knownOrigin, Coord? jobSite, Coord? restTile)
        {
            var result = new List<Coord>();
            if (knownOrigin.HasValue) result.Add(knownOrigin.Value);
            if (jobSite.HasValue) result.Add(jobSite.Value);
            if (restTile is Coord rest) result.Add(rest);
            return result.Distinct().Take(MaximumSearchSites).ToArray();
        }
        // A read patrol order: refuse it without the required role, otherwise accept it
        // once, then search.
        public static PatrolOrderGate OrderGate(IActorView view, PatrolOrderPayload payload, bool awaitingAcceptance) =>
            view.Role != payload.RequiredRole ? PatrolOrderGate.NotQualified : awaitingAcceptance ? PatrolOrderGate.Accept : PatrolOrderGate.Search;
        public static bool Found(FactValue inspection) => inspection.Kind == FactValueKind.Coordinate;
        // Stop at the first sighting, or report not located after the last candidate.
        public static MessageKind? SearchResult(bool found, int inspected, int candidates) =>
            found ? MessageKind.PatrolFound : inspected == candidates ? MessageKind.PatrolNotLocated : (MessageKind?)null;
        public static PlanResult InspectCandidate(IActorView view, Coord candidate, Id<ActorTag> target, long inspectTicks, int depth = 8, int budget = 512)
        {
            if (!view.CurrentTile.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            long? travel = KnownNavigation.Estimate(view, view.CurrentTile.Value, candidate);
            if (!travel.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            var actions = new[] {
                new SymbolicAction("TravelToSearchCandidate", 0, 1, 1, 0, travel.Value, new ActionIntent(view.Actor, ActionKind.Move, candidate)),
                new SymbolicAction("InspectForKnownActor", 1, 2, 2, 0, inspectTicks, new ActionIntent(view.Actor, ActionKind.InspectActor, candidate, targetActor: target)) };
            return GoapPlanner.Search(new PlanningProblem(view.CurrentTile.Value == candidate ? 1UL : 0UL, 2, new[] { "AtSearchCandidate", "CandidateInspected" }, actions), depth, budget);
        }
    }
}
