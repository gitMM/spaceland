using System;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private void EvaluateCleanupOrder(LocalWorkOrderSnapshot order)
        {
            var actor = actors[new Id<ActorTag>(order.ActorId)];
            if (actor.Action != null || processingTick >= order.Expires) return;
            // One retained cleanup goal may issue an intent for this citizen. Later
            // assignments wait in stable attempt order and wake after the report.
            if (localOrders.Values.Any(o => o.ActorId == order.ActorId && o.Kind == WorkKind.CleanTile && o.Status == LocalOrderStatus.Working && o.AttemptId < order.AttemptId)) return;
            var view = ActorView(actor.Id);
            var site = view.PublicKnowledge?.Work?.Cleanable.SingleOrDefault(c => c.Tile.Value == order.TileId);
            bool knownDone = order.CleanupChecked && site != null && view.CurrentTile == site.Position &&
                view.Knowledge.Any(f => f.Key.Equals(new FactKey(SubjectKind.Tile, order.TileId, AttributeKind.Dirt)) && f.Value.Equals(FactValue.Integer(0)));
            if (!KnownOnDuty(actor.Id) && !knownDone) return;
            if (!needAgents.ContainsKey(actor.Id.Value) && (reportAgents.TryGetValue(actor.Id, out var report) && report.Pending.Count > 0 ||
                checkIns.TryGetValue(actor.Id, out var checkIn) && (checkIn.Due || checkIn.DueTick == processingTick))) return;
            if (!SpendPlanningRequest()) { RequestOrderDecision(order, checked(processingTick + 1)); return; }
            var result = CleanupPlanning.Plan(view, new Id<TileTag>(order.TileId), order.CleanupChecked, Information!.InspectTicks, planner.Candidates, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += result.Expansions;
            if (result.Outcome == PlanOutcome.PlanFound)
            {
                PlansFound++; order.Backoff = planner.InitialBackoff;
                if (result.Steps.Count == 0)
                {
                    order.Status = LocalOrderStatus.Completed; order.Reason = order.CleanedUnits == 0 ? "NoWorkNeeded" : "ReportedComplete";
                    QueueOrderReport(order, MessageKind.WorkCompleted, order.Reason); UnpinOrder(actor, order.AttemptId); TriggerWorkCheckIn(actor.Id);
                    Emit("CleanupGoalCompleted", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId, detail: order.Reason); return;
                }
                var first = result.Steps[0].Intent;
                long cause = Emit("PlanSelected", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId,
                    detail: "clean-known-tile/v1; " + string.Join(" -> ", result.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), CauseId = cause,
                    Intent = new ActionIntent(first.Actor, first.Kind, first.Target, first.Item, attemptId: order.AttemptId) });
                return;
            }
            if (result.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; order.Reason = "PlanBudgetExhausted"; }
            else { PlansUnavailable++; order.Reason = "NoKnownCleanupPrerequisitesOrRoute"; }
            SetAttemptExecution(order.AttemptId, WorkExecution.Blocked);
            if (!order.BlockedReported) { order.BlockedReported = true; QueueOrderReport(order, MessageKind.WorkBlocked, order.Reason); TriggerWorkCheckIn(actor.Id); }
            Emit("PlanUnavailable", actor, attemptId: order.AttemptId, detail: "clean-known-tile/v1; " + order.Reason);
            RequestOrderDecision(order, checked(processingTick + order.Backoff)); order.Backoff = NextWorkBackoff(order.Backoff);
        }
        private void CleanupActionCompleted(Actor actor, ActiveAction action)
        {
            if (action.Kind == ActionKind.Move)
                foreach (var local in localOrders.Values.Where(o => o.ActorId == actor.Id.Value && o.Kind == WorkKind.CleanTile)) local.CleanupChecked = false;
            if (!(action.Kind is ActionKind.InspectTile or ActionKind.Clean) || !localOrders.TryGetValue(action.AttemptId, out var order) || order.Kind != WorkKind.CleanTile) return;
            var observed = actor.Knowledge.Entries.TryGetValue(new FactKey(SubjectKind.Tile, order.TileId, AttributeKind.Dirt), out var entry) ? entry.Fact : null;
            order.CleanupChecked = observed != null && observed.ObservedTick == processingTick;
        }
    }
}
