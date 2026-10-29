using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private static bool GuardPreparing(LocalGuardOrderSnapshot local) => local.Status is GuardOrderStatus.EquipmentPending or GuardOrderStatus.AtRendezvous or GuardOrderStatus.WaitingTeam or GuardOrderStatus.Searching or GuardOrderStatus.TargetLocated;
        private bool GuardHasPriority(Id<ActorTag> actor) => !HasCriticalNeed(actor) && KnownOnDuty(actor) && localGuardOrders.Values.Any(l => l.ActorId == actor.Value && GuardPreparing(l) &&
            l.Payload.Expires > processingTick && !(l.Reason is "NoKnownAvailableEquipmentRoute" or "NoKnownRendezvousRoute" or "NoKnownSearchRoute" or "PlanBudgetExhausted")) && !GuardJourneyNeedsRelief(actor);
        private bool GuardJourneyNeedsRelief(Id<ActorTag> id)
        {
            if (Needs == null || EscortControls(id) || !needAgents.TryGetValue(id.Value, out var agent) || agent.UrgentRetryAt > NeedNow) return false;
            var local = localGuardOrders.Values.FirstOrDefault(l => l.ActorId == id.Value &&
                (l.Status == GuardOrderStatus.TargetLocated || l.Status == GuardOrderStatus.Searching && l.Reason == "PreparingForEscort") && l.Payload.Expires > NeedNow);
            if (local == null) return false;
            var urgent = NeedValues(id.Value).Where(n => Needs.Rules[(int)n.Kind].Critical > 0 &&
                (n.Band >= NeedBand.Urgent || n.Kind == NeedKind.Bladder && n.Band >= NeedBand.Seek)).ToArray();
            if (urgent.Length == 0 || urgent.All(n => n.Band < NeedBand.Urgent) && agent.RetryAt > NeedNow) return false;
            // Keep an accepted preparation goal through its ordinary travel and
            // relief actions. Leaving the target invalidates the sighting, not
            // the reason for seeking relief. Failure backoff above still yields.
            if (local.Reason == "PreparingForEscort") return true;
            var view = ActorView(id); if (!view.CurrentTile.HasValue) return false;
            var travel = new KnownNavigation.Distances(view, view.CurrentTile.Value).To(local.Payload.Dispatch.Destination);
            if (!travel.HasValue) return false;
            long duration = checked(travel.Value + Information!.InspectTicks + (Detention!.Facilities.SingleOrDefault(f => f.Id == local.Payload.Dispatch.FacilityId)?.IntakeTicks ?? 0));
            // Forecast only this guard's own needs against the known route and
            // published intake duration. No partner state or live queue is read.
            return urgent.Any(n => view.NeedServices.Any(s => s.DetentionFacilityId == 0 && NeedPlanning.Serves(s.Kind, n.Kind)) &&
                new NeedAccumulator(n.Q, NeedNow, Needs.Rules.Single(r => r.Kind == n.Kind).AwakeRate).At(checked(NeedNow + duration), Config.TickDurationMs) >=
                    Needs.Rules.Single(r => r.Kind == n.Kind).Critical * NeedAccumulator.HourMilliseconds);
        }
        private void RefreshGuardReadiness(Actor actor)
        {
            if (!EscortControls(actor.Id))
                foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value && l.Status == GuardOrderStatus.TargetLocated))
                    if (actor.Action?.Kind == ActionKind.Move || actor.Position != local.SearchObservations.Last().Tile)
                        ReinspectGuardTarget(local);
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
        private void ReinspectGuardTarget(LocalGuardOrderSnapshot local)
        {
            if (local.Status != GuardOrderStatus.TargetLocated) return;
            local.SearchObservations = local.SearchObservations.Take(local.SearchObservations.Count - 1).ToArray();
            local.CandidateIndex--; local.Status = GuardOrderStatus.Searching;
            if (local.Reason != "PreparingForEscort") local.Reason = "TargetObservationNeedsRefresh";
            RequestGuardDecision(local, checked(processingTick + 1));
        }
        private long GuardTargetWaitDeadline(LocalGuardOrderSnapshot local) => System.Math.Min(local.Payload.Expires,
            // A late-shift formation needs the next duty interval after sleep.
            // One complete configured roster cycle is still a finite local bound.
            checked(local.FormedTick + System.Math.Max(checked(Information!.InspectTicks * 2), Routines?.DayTicks ?? planner.MaximumBackoff)));
        private void RequestGuardDecision(LocalGuardOrderSnapshot local, long tick)
        {
            if (!GuardPreparing(local)) return;
            // Ordinary action wakeups must not bypass the retained delay after
            // a local custody handoff found an unmet team precondition.
            if (local.Reason == "AwaitingLocalHandoff" && local.NextDecision > processingTick && tick < local.NextDecision) return;
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
            if (local.Reason == "AwaitingLocalHandoff") local.Reason = "RetryLocalHandoff";
            if (!GuardPreparing(local)) return;
            if ((local.Status is GuardOrderStatus.Searching or GuardOrderStatus.TargetLocated) && !EscortControls(actor.Id))
            {
                long deadline = GuardTargetWaitDeadline(local);
                if (processingTick >= deadline && processingTick < local.Payload.Expires)
                {
                    local.Status = GuardOrderStatus.SearchAbandoned; local.Reason = "TeamNotReady";
                    QueueGuardOutcome(local, "TeamNotReady", processingTick); return;
                }
                // Bound the locally formed team's search, including need breaks.
                // A partner's remote progress is never read to decide this timeout.
                RequestGuardDecision(local, deadline);
            }
            if (HasCriticalNeed(actor.Id)) { WithdrawGuardJoin(local); if (local.Status == GuardOrderStatus.WaitingTeam) local.Status = GuardOrderStatus.EquipmentPending; local.Reason = "CriticalNeed"; return; } if (processingTick >= local.Payload.Expires) { ExpireGuardOrder(id); return; }
            if (GuardJourneyNeedsRelief(actor.Id)) { RequestNeedDecision(needAgents[actor.Id.Value], processingTick); return; }
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
