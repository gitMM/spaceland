using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private static KnownGuardOrderStatus KnownGuardStatus(GuardOrderStatus status) => (KnownGuardOrderStatus)(int)status;
        private static GuardOrderStatus CoreGuardStatus(KnownGuardOrderStatus status) => (GuardOrderStatus)(int)status;
        private static bool GuardPreparing(LocalGuardOrderSnapshot local) => GuardPolicy.Preparing(KnownGuardStatus(local.Status));
        private bool GuardHasPriority(Id<ActorTag> actor) => !HasCriticalNeed(actor) && KnownOnDuty(actor) && localGuardOrders.Values.Any(l => l.ActorId == actor.Value &&
            GuardPolicy.HoldsPriority(KnownGuardStatus(l.Status), l.Reason, l.Payload.Expires, processingTick)) && !GuardJourneyNeedsRelief(actor);
        private bool GuardJourneyNeedsRelief(Id<ActorTag> id)
        {
            if (Needs == null || EscortControls(id) || !needAgents.TryGetValue(id.Value, out var agent) || agent.UrgentRetryAt > NeedNow) return false;
            var local = localGuardOrders.Values.FirstOrDefault(l => l.ActorId == id.Value && GuardPolicy.ForecastsJourney(KnownGuardStatus(l.Status), l.Reason, l.Payload.Expires, NeedNow));
            if (local == null) return false;
            // Own needs, own view and the published intake duration only (Mind forecast).
            return GuardPolicy.JourneyNeedsRelief(local.Reason, local.Payload.Dispatch.Destination, NeedValues(id.Value), Needs.Rules, agent.RetryAt, NeedNow, Config.TickDurationMs,
                Information!.InspectTicks, () => Detention!.Facilities.SingleOrDefault(f => f.Id == local.Payload.Dispatch.FacilityId)?.IntakeTicks ?? 0, () => ActorView(id));
        }
        private void RefreshGuardReadiness(Actor actor)
        {
            if (!EscortControls(actor.Id))
                foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value && l.Status == GuardOrderStatus.TargetLocated))
                    if (GuardPolicy.SightingStale(actor.Action?.Kind == ActionKind.Move, actor.Position, local.SearchObservations.Last().Tile))
                        ReinspectGuardTarget(local);
            foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value && (l.Status is GuardOrderStatus.AtRendezvous or GuardOrderStatus.WaitingTeam)))
                if (GuardPolicy.ReadinessLost(actor.Action?.Kind == ActionKind.Move, actor.Position, local.Payload.Dispatch.Rendezvous, () => GuardPreparation.Equipped(ActorView(actor.Id), local.Payload.Dispatch.RequiredEquipment)))
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
            local.Reason = GuardPolicy.ReinspectReason(local.Reason);
            RequestGuardDecision(local, checked(processingTick + 1));
        }
        private long GuardTargetWaitDeadline(LocalGuardOrderSnapshot local) =>
            GuardPolicy.TargetWaitDeadline(local.Payload.Expires, local.FormedTick, Information!.InspectTicks, Routines?.DayTicks, planner.MaximumBackoff);
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
            foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value)) RequestGuardDecision(local, WorkBackoff.RetryTick(processingTick, local.Backoff, failed));
        }
        // Side-effect-free observations for one guard decision, evaluated only when Mind asks.
        private sealed class GuardDecisionInputs : IGuardPreparationInputs
        {
            private readonly World world; private readonly Actor actor;
            internal GuardDecisionInputs(World world, Actor actor) { this.world = world; this.actor = actor; }
            public bool CriticalNeed() => world.HasCriticalNeed(actor.Id);
            public bool JourneyNeedsRelief() => world.GuardJourneyNeedsRelief(actor.Id);
            public bool AwaitingIntake() => world.ActiveEscort(actor.Id)?.Status == EscortStatus.AwaitingIntake && actor.Action == null;
            public bool Busy() => actor.Action != null;
            public bool KnownOnDuty() => world.KnownOnDuty(actor.Id);
            public bool ReportsPending() => world.reportAgents.TryGetValue(actor.Id, out var reports) && reports.Pending.Count > 0;
            public bool EscortControls() => world.EscortControls(actor.Id);
        }
        private void EvaluateGuard(long id)
        {
            var local = localGuardOrders[id]; local.NextDecision = -1; var actor = actors[new Id<ActorTag>(local.ActorId)];
            local.Reason = GuardPolicy.ReasonOnDecision(local.Reason);
            var status = KnownGuardStatus(local.Status);
            if (!GuardPolicy.Preparing(status)) return;
            if (GuardPolicy.BoundsTeamWait(status) && !EscortControls(actor.Id))
            {
                long deadline = GuardTargetWaitDeadline(local);
                if (GuardPolicy.TeamWaitExpired(processingTick, deadline, local.Payload.Expires))
                {
                    local.Status = GuardOrderStatus.SearchAbandoned; local.Reason = GuardPolicy.TeamNotReady;
                    QueueGuardOutcome(local, GuardPolicy.TeamNotReady, processingTick); return;
                }
                // Bound the locally formed team's search, including need breaks.
                // A partner's remote progress is never read to decide this timeout.
                RequestGuardDecision(local, deadline);
            }
            switch (GuardPolicy.Next(status, processingTick, local.Payload.Expires, new GuardDecisionInputs(this, actor)))
            {
                case GuardStep.WithdrawForCriticalNeed:
                    WithdrawGuardJoin(local); local.Status = CoreGuardStatus(GuardPolicy.AfterCriticalNeed(KnownGuardStatus(local.Status))); local.Reason = GuardPolicy.CriticalNeedReason; return;
                case GuardStep.Expire: ExpireGuardOrder(id); return;
                case GuardStep.SeekRelief: RequestNeedDecision(needAgents[actor.Id.Value], processingTick); return;
                case GuardStep.RegisterIntake: PlanDetentionIntake(actor, local, ActiveEscort(actor.Id)!); return;
                case GuardStep.BeginEscort: PlanBeginEscort(actor, local); return;
                case GuardStep.Search: EvaluateGuardSearch(actor, local); return;
                case GuardStep.Wait: return;
            }
            var view = ActorView(actor.Id);
            bool equipped = GuardPreparation.Equipped(view, local.Payload.Dispatch.RequiredEquipment);
            var preparation = GuardPolicy.Prepare(status, equipped, view.CurrentTile == local.Payload.Dispatch.Rendezvous);
            if (preparation != GuardPreparationStep.Plan)
            {
                if (preparation == GuardPreparationStep.ArriveAtRendezvous)
                {
                    local.Status = GuardOrderStatus.AtRendezvous; local.ReadyTick = processingTick; local.ReadyItemId = view.EquippedItem.Value; local.Reason = "WaitingForTeam";
                    Emit("GuardAtRendezvous", actor, messageId: id, item: view.EquippedItem, target: local.Payload.Dispatch.Rendezvous);
                }
                PlanGuardJoin(actor, local); return;
            }
            local.Status = GuardOrderStatus.EquipmentPending; local.Reason = GuardPolicy.PreparationReason(equipped);
            if (!SpendPlanningRequest()) { RequestGuardDecision(local, checked(processingTick + 1)); return; }
            var plan = GuardPreparation.Plan(view, local.Payload, planner.Candidates, planner.MaximumDepth, planner.NodeBudget); PlannerExpansions += plan.Expansions;
            if (plan.Outcome != PlanOutcome.PlanFound || plan.Steps.Count == 0)
            {
                var failure = GuardPolicy.PreparationFailure(plan.Outcome, equipped);
                if (plan.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
                local.Reason = failure.Reason;
                if (failure.Outcome != null) QueueGuardOutcome(local, failure.Outcome, processingTick);
                if (checkIns.TryGetValue(actor.Id, out var checkIn)) RequestCheckInDecision(checkIn, processingTick);
                RequestGuardDecision(local, checked(processingTick + local.Backoff)); local.Backoff = NextWorkBackoff(local.Backoff); return;
            }
            PlansFound++; local.Backoff = planner.InitialBackoff;
            long cause = Emit("PlanSelected", actor, messageId: id, detail: "guard-preparation/v1; cost=" + plan.Cost);
            Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, CauseId = cause,
                Intent = GuardPolicy.PreparationIntent(plan, actor.Id, id) });
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
