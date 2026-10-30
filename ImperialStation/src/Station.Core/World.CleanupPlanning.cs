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
            // Later cleanup assignments wait in stable attempt order (Mind rule).
            if (CleanupPolicy.WaitsForEarlierCleanup(order.AttemptId, OwnWorkOrderBriefs(order.ActorId))) return;
            var view = ActorView(actor.Id);
            bool knownDone = CleanupPolicy.KnownDone(view, order.TileId, order.CleanupChecked);
            if (!KnownOnDuty(actor.Id) && !knownDone) return;
            if (WorkReportsFirst(actor.Id)) return;
            if (!SpendPlanningRequest()) { RequestOrderDecision(order, checked(processingTick + 1)); return; }
            var result = CleanupPlanning.Plan(view, new Id<TileTag>(order.TileId), order.CleanupChecked, Information!.InspectTicks, planner.Candidates, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += result.Expansions;
            var step = CleanupPolicy.Classify(result);
            if (step != WorkGoalStep.Blocked)
            {
                PlansFound++; order.Backoff = planner.InitialBackoff;
                if (step == WorkGoalStep.Completed)
                {
                    CompleteKnownCleanup(actor, order); return;
                }
                var intent = CleanupPolicy.FirstIntent(result, order.AttemptId);
                long cause = Emit("PlanSelected", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId,
                    detail: "clean-known-tile/v1; " + string.Join(" -> ", result.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), CauseId = cause,
                    Intent = intent });
                return;
            }
            if (result.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
            order.Reason = CleanupPolicy.BlockedReason(result.Outcome);
            SetAttemptExecution(order.AttemptId, WorkExecution.Blocked);
            if (!order.BlockedReported) { order.BlockedReported = true; QueueOrderReport(order, MessageKind.WorkBlocked, order.Reason); TriggerWorkCheckIn(actor.Id); }
            Emit("PlanUnavailable", actor, attemptId: order.AttemptId, detail: "clean-known-tile/v1; " + order.Reason);
            RequestOrderDecision(order, checked(processingTick + order.Backoff)); order.Backoff = NextWorkBackoff(order.Backoff);
        }
        private void CompleteKnownCleanup(Actor actor, LocalWorkOrderSnapshot order)
        {
            order.Status = LocalOrderStatus.Completed; order.Reason = CleanupPolicy.CompletionReason(order.CleanedUnits);
            foreach (var e in scheduled.ForActor(actor.Id.Value).Where(e => e.Kind == ScheduledKind.EvaluateWorkOrder && e.Value == order.AttemptId).ToArray()) scheduled.Remove(e);
            order.NextDecision = -1;
            QueueOrderReport(order, MessageKind.WorkCompleted, order.Reason); UnpinOrder(actor, order.AttemptId); TriggerWorkCheckIn(actor.Id);
            Emit("CleanupGoalCompleted", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId, detail: order.Reason);
        }
        private void CleanupActionCompleted(Actor actor, ActiveAction action)
        {
            if (CleanupPolicy.InvalidatesCheck(action.Kind))
                foreach (var local in localOrders.Values.Where(o => o.ActorId == actor.Id.Value && o.Kind == WorkKind.CleanTile)) local.CleanupChecked = false;
            if (!CleanupPolicy.ChecksTile(action.Kind) || !localOrders.TryGetValue(action.AttemptId, out var order) || order.Kind != WorkKind.CleanTile) return;
            var observed = actor.Knowledge.Entries.TryGetValue(new FactKey(SubjectKind.Tile, order.TileId, AttributeKind.Dirt), out var entry) ? entry.Fact : null;
            order.CleanupChecked = CleanupPolicy.ObservationCurrent(observed, processingTick);
            // Preserve this completed local observation before another due goal
            // moves the worker away. Posting still uses the ordinary report queue
            // and terminal transport; no authority state is updated here.
            if (order.ActorId == actor.Id.Value && CleanupPolicy.ObservedClean(KnownWorkStatus(order.Status), order.Expires, processingTick, order.CleanupChecked, observed)) CompleteKnownCleanup(actor, order);
        }
    }
}
