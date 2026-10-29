using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private static bool GuardPreparing(LocalGuardOrderSnapshot local) => local.Status is GuardOrderStatus.EquipmentPending or GuardOrderStatus.AtRendezvous or GuardOrderStatus.WaitingTeam or GuardOrderStatus.Searching or GuardOrderStatus.TargetLocated;
        private bool GuardHasPriority(Id<ActorTag> actor) => !HasCriticalNeed(actor) && KnownOnDuty(actor) && localGuardOrders.Values.Any(l => l.ActorId == actor.Value && GuardPreparing(l) &&
            l.Payload.Expires > processingTick && !(l.Reason is "NoKnownAvailableEquipmentRoute" or "NoKnownRendezvousRoute" or "NoKnownSearchRoute" or "PlanBudgetExhausted"));
        private void RefreshGuardReadiness(Actor actor)
        {
            foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value && (l.Status is GuardOrderStatus.AtRendezvous or GuardOrderStatus.WaitingTeam)))
                if (actor.Action?.Kind == ActionKind.Move || actor.Position != local.Payload.Dispatch.Rendezvous || !GuardPreparation.Equipped(ActorView(actor.Id), local.Payload.Dispatch.RequiredEquipment))
                { WithdrawGuardJoin(local); local.Status = GuardOrderStatus.EquipmentPending; local.Reason = "ReadinessLost"; }
            foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value && (l.Status is GuardOrderStatus.Searching or GuardOrderStatus.TargetLocated)))
                if (!GuardPreparation.Equipped(ActorView(actor.Id), local.Payload.Dispatch.RequiredEquipment))
                {
                    if (local.Status == GuardOrderStatus.TargetLocated) { local.SearchObservations = local.SearchObservations.Take(local.SearchObservations.Count - 1).ToArray(); local.CandidateIndex--; }
                    local.Status = GuardOrderStatus.EquipmentPending; local.Reason = "EquipmentLost";
                }
        }
        private void RequestGuardDecision(LocalGuardOrderSnapshot local, long tick)
        {
            if (!GuardPreparing(local)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (local.NextDecision >= 0 && local.NextDecision <= tick) return;
            RemoveGuardDecision(local); local.NextDecision = tick;
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateGuard, Value = local.OrderMessageId });
        }
        private void RemoveGuardDecision(LocalGuardOrderSnapshot local)
        {
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateGuard && e.Value == local.OrderMessageId).ToArray()) scheduled.Remove(e);
            local.NextDecision = -1;
        }
        private void WakeGuard(Actor actor, bool failed = false)
        {
            if (actor.Action != null) return;
            foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value)) RequestGuardDecision(local, failed ? checked(processingTick + local.Backoff) : processingTick);
        }
        private void EvaluateGuard(long id)
        {
            var local = localGuardOrders[id]; local.NextDecision = -1; var actor = actors[new Id<ActorTag>(local.ActorId)];
            if (!GuardPreparing(local)) return;
            if (HasCriticalNeed(actor.Id)) { WithdrawGuardJoin(local); if (local.Status == GuardOrderStatus.WaitingTeam) local.Status = GuardOrderStatus.EquipmentPending; local.Reason = "CriticalNeed"; return; } if (processingTick >= local.Payload.Expires) { ExpireGuardOrder(id); return; }
            var group = ActiveEscort(actor.Id); if (group?.Status == EscortStatus.AwaitingIntake && actor.Action == null) { PlanDetentionIntake(actor, local, group); return; }
            if (actor.Action != null || !KnownOnDuty(actor.Id) || reportAgents.TryGetValue(actor.Id, out var reports) && reports.Pending.Count > 0) return;
            if (EscortControls(actor.Id) || local.Status == GuardOrderStatus.WaitingTeam) return;
            if (local.Status == GuardOrderStatus.TargetLocated) { PlanBeginEscort(actor, local); return; }
            if (local.Status == GuardOrderStatus.Searching) { EvaluateGuardSearch(actor, local); return; }
            var view = ActorView(actor.Id);
            bool equipped = GuardPreparation.Equipped(view, local.Payload.Dispatch.RequiredEquipment);
            if (equipped && view.CurrentTile == local.Payload.Dispatch.Rendezvous)
            {
                if (local.Status != GuardOrderStatus.AtRendezvous)
                {
                    local.Status = GuardOrderStatus.AtRendezvous; local.ReadyTick = processingTick; local.ReadyItemId = view.EquippedItem.Value; local.Reason = "WaitingForTeam";
                    Emit("GuardAtRendezvous", actor, messageId: id, item: view.EquippedItem, target: local.Payload.Dispatch.Rendezvous);
                }
                PlanGuardJoin(actor, local); return;
            }
            local.Status = GuardOrderStatus.EquipmentPending; local.Reason = equipped ? "TravellingToRendezvous" : "AcquiringEquipment";
            if (!SpendPlanningRequest()) { RequestGuardDecision(local, checked(processingTick + 1)); return; }
            var plan = GuardPreparation.Plan(view, local.Payload, planner.Candidates, planner.MaximumDepth, planner.NodeBudget); PlannerExpansions += plan.Expansions;
            if (plan.Outcome != PlanOutcome.PlanFound || plan.Steps.Count == 0)
            {
                if (plan.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; local.Reason = "PlanBudgetExhausted"; }
                else { PlansUnavailable++; local.Reason = equipped ? "NoKnownRendezvousRoute" : "NoKnownAvailableEquipmentRoute"; QueueGuardOutcome(local, equipped ? "RendezvousUnreachable" : "EquipmentUnavailable", processingTick); }
                if (checkIns.TryGetValue(actor.Id, out var checkIn)) RequestCheckInDecision(checkIn, processingTick);
                RequestGuardDecision(local, checked(processingTick + local.Backoff)); local.Backoff = NextWorkBackoff(local.Backoff); return;
            }
            PlansFound++; local.Backoff = planner.InitialBackoff; var step = plan.Steps[0].Intent;
            long cause = Emit("PlanSelected", actor, messageId: id, detail: "guard-preparation/v1; cost=" + plan.Cost);
            Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, CauseId = cause,
                Intent = new ActionIntent(actor.Id, step.Kind, step.Target, step.Item, guardOrderId: id) });
        }
        private bool CanExecuteGuard(Actor actor, ActiveAction action)
        {
            if (HasCriticalNeed(actor.Id) || !localGuardOrders.TryGetValue(action.GuardOrderId, out var local) || local.ActorId != actor.Id.Value || processingTick >= local.Payload.Expires || local.Response != "Accepted" || local.AcknowledgmentMessageId == 0) return false;
            if (action.Kind == ActionKind.RegisterIntake) return local.Status == GuardOrderStatus.TargetLocated && local.Payload.Leader == actor.Id.Value && ActiveEscort(actor.Id)?.Status == EscortStatus.AwaitingIntake;
            if (action.Kind == ActionKind.BeginEscort) return local.Status == GuardOrderStatus.TargetLocated && local.Payload.Leader == actor.Id.Value;
            if (action.Kind == ActionKind.JoinGuardTeam) return local.Status == GuardOrderStatus.AtRendezvous && local.FormedTick < 0;
            if (local.Status == GuardOrderStatus.Searching) return (action.Kind is ActionKind.Move or ActionKind.InspectActor) && local.CandidateIndex < local.Payload.Candidates.Count &&
                action.Target == local.Payload.Candidates[local.CandidateIndex] && (action.Kind != ActionKind.InspectActor || action.TargetActor.Value == local.Payload.SubjectActorId);
            return local.Status == GuardOrderStatus.EquipmentPending && (action.Kind is ActionKind.Move or ActionKind.PickUp or ActionKind.Equip);
        }
    }
}
