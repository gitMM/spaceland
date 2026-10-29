using Station.Contracts;

namespace Station.Mind
{
    public static class RoutinePlanning
    {
        public static PlanResult PlanRest(IActorView view, Coord restTile, int depth = 8, int budget = 512)
        {
            if (!view.CurrentTile.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            if (view.CurrentTile.Value == restTile) return new PlanResult(PlanOutcome.PlanFound);
            long? cost = KnownNavigation.Estimate(view, view.CurrentTile.Value, restTile);
            if (!cost.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            return GoapPlanner.Search(new PlanningProblem(0, 1, new[] { "AtKnownRestTile" }, new[] {
                new SymbolicAction("GoToKnownRestTile", 0, 1, 1, 0, cost.Value, new ActionIntent(view.Actor, ActionKind.Move, restTile)) }), depth, budget);
        }
    }
}
