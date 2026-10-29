using Station.Contracts;

namespace Station.Mind
{
    public static class PatrolPlanning
    {
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
