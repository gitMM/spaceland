using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class GuardSearchPlanning
    {
        public static PlanResult Intake(IActorView view, EscortOrderPayload order, int depth = 8, int budget = 512)
        {
            if (view.CurrentTile != order.Dispatch.Destination || view.Actor.Value != order.Leader || view.Tick >= order.Expires) return new PlanResult(PlanOutcome.NoPlan);
            var step = new SymbolicAction("RegisterLocalIntake", 0, 1, 1, 0, 300, new ActionIntent(view.Actor, ActionKind.RegisterIntake, order.Dispatch.Destination, serviceId: order.Dispatch.FacilityId));
            return GoapPlanner.Search(new PlanningProblem(0, 1, new[] { "IntakeRegistered" }, new[] { step }), depth, budget);
        }
        public static PlanResult Begin(IActorView view, EscortOrderPayload order, long duration, int depth = 8, int budget = 512)
        {
            if (!view.CurrentTile.HasValue || view.Actor.Value != order.Leader || view.Tick >= order.Expires || !GuardPreparation.Equipped(view, order.Dispatch.RequiredEquipment)) return new PlanResult(PlanOutcome.NoPlan);
            var location = view.Knowledge.FirstOrDefault(f => f.Key.Equals(new FactKey(SubjectKind.Actor, order.SubjectActorId, AttributeKind.Location)));
            if (location == null || location.SourceKind != EvidenceKind.Inspection || (location.Value.Kind != FactValueKind.Coordinate || location.Value.Coordinate != view.CurrentTile.Value)) return new PlanResult(PlanOutcome.NoPlan);
            var step = new SymbolicAction("EstablishLocalCustody", 0, 1, 1, 0, duration, new ActionIntent(view.Actor, ActionKind.BeginEscort, view.CurrentTile.Value, targetActor: new Id<ActorTag>(order.SubjectActorId)));
            return GoapPlanner.Search(new PlanningProblem(0, 1, new[] { "LocalCustody" }, new[] { step }), depth, budget);
        }
        public static PlanResult Join(IActorView view, EscortOrderPayload order, long duration, int depth = 8, int budget = 512)
        {
            if (view.CurrentTile != order.Dispatch.Rendezvous || !GuardPreparation.Equipped(view, order.Dispatch.RequiredEquipment) || view.Tick >= order.Expires ||
                !order.Guards.Contains(view.Actor.Value) || view.Role != order.Dispatch.GuardRole) return new PlanResult(PlanOutcome.NoPlan);
            var step = new SymbolicAction("JoinReadGuardTeam", 0, 1, 1, 0, duration, new ActionIntent(view.Actor, ActionKind.JoinGuardTeam, order.Dispatch.Rendezvous));
            return GoapPlanner.Search(new PlanningProblem(0, 1, new[] { "LocalJoinCompleted" }, new[] { step }), depth, budget);
        }
    }
}
