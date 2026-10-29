using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class VerificationPlanning
    {
        public static PlanResult Plan(IActorView view, long machineId, int depth = 8, int budget = 512)
        {
            var machine = view.PublicKnowledge?.Work?.Equipment.SingleOrDefault(m => m.Id == machineId);
            if (machine == null || !view.CurrentTile.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            long? travel = KnownNavigation.Estimate(view, view.CurrentTile.Value, machine.Position);
            if (!travel.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            // Prior status/specification knowledge never substitutes for this order's inspection.
            var actions = new[] {
                new SymbolicAction("TravelToVerification", 0, 1, 1, 0, travel.Value, new ActionIntent(view.Actor, ActionKind.Move, machine.Position)),
                new SymbolicAction("InspectVerificationMachine", 1, 2, 2, 0, machine.InspectTicks, new ActionIntent(view.Actor, ActionKind.InspectMachine, machine.Position, serviceId: machineId)) };
            return GoapPlanner.Search(new PlanningProblem(view.CurrentTile.Value == machine.Position ? 1UL : 0UL, 2, new[] { "AtVerification", "InspectedForOrder" }, actions), depth, budget);
        }
    }
}
