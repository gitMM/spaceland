using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed class GuardJoinSnapshot
    {
        public long ActorId { get; internal set; }
        public long OrderMessageId { get; internal set; }
        public long Tick { get; internal set; }
        public long ActionId { get; internal set; }
        internal GuardJoinSnapshot Copy() => (GuardJoinSnapshot)MemberwiseClone();
    }
    // Physical local cooperation, distinct from the authority's reported team progress.
    public sealed class LocalGuardTeamSnapshot
    {
        public long AuthorizationId { get; internal set; }
        public long DispatchId { get; internal set; }
        public long FormedTick { get; internal set; } = -1;
        public IReadOnlyList<GuardJoinSnapshot> Members { get; internal set; } = Array.Empty<GuardJoinSnapshot>();
        internal LocalGuardTeamSnapshot Copy() { var copy = (LocalGuardTeamSnapshot)MemberwiseClone(); copy.Members = Members.Select(m => m.Copy()).ToArray(); return copy; }
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, LocalGuardTeamSnapshot> localGuardTeams = new SortedDictionary<long, LocalGuardTeamSnapshot>();
        public IReadOnlyList<LocalGuardTeamSnapshot> InspectLocalGuardTeams() => localGuardTeams.Values.Select(t => t.Copy()).ToArray();
        private bool PhysicallyEquipped(Actor actor, string resource) => equippedItems.TryGetValue(actor.Id.Value, out var e) &&
            items.TryGetValue(new Id<ItemTag>(e.ItemId), out var item) && HeldBy(item, actor) && item.Quantity > 0 && item.Resource == resource;
        private FailureReason ValidateGuardJoin(Actor actor, ActiveAction action)
        {
            if (HasCriticalNeed(actor.Id) || !localGuardOrders.TryGetValue(action.GuardOrderId, out var local) || local.ActorId != actor.Id.Value || local.Status != GuardOrderStatus.AtRendezvous ||
                local.AcknowledgmentMessageId == 0 || local.Response != "Accepted" || local.Payload.Expires <= processingTick || local.FormedTick >= 0) return FailureReason.UnknownRequiredFact;
            if (actor.Position != local.Payload.Dispatch.Rendezvous || action.Target != actor.Position) return FailureReason.NotAtRequiredTile;
            return PhysicallyEquipped(actor, local.Payload.Dispatch.RequiredEquipment) ? FailureReason.None : FailureReason.MissingTool;
        }
        private void StartGuardJoin(Actor actor, ActiveAction action)
        {
            var failure = ValidateGuardJoin(actor, action); if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            action.Completes = checked(processingTick + Information!.InspectTicks); actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, target: action.Target, arrival: action.Completes, detail: "JoinGuardTeam", causeId: action.CauseId); ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteGuardJoin(Actor actor, ActiveAction action)
        {
            var failure = ValidateGuardJoin(actor, action);
            if (failure != FailureReason.None) { actor.Action = null; Fail(actor, action.Id, failure); return; }
            var local = localGuardOrders[action.GuardOrderId];
            if (!localGuardTeams.TryGetValue(local.Payload.DispatchId, out var team))
            { team = new LocalGuardTeamSnapshot { AuthorizationId = local.Payload.AuthorizationId, DispatchId = local.Payload.DispatchId }; localGuardTeams.Add(team.DispatchId, team); }
            if (team.FormedTick >= 0) throw new InvalidOperationException("A formed team cannot accept another formation join.");
            var member = new GuardJoinSnapshot { ActorId = actor.Id.Value, OrderMessageId = local.OrderMessageId, Tick = processingTick, ActionId = action.Id.Value };
            team.Members = team.Members.Where(m => m.ActorId != actor.Id.Value).Concat(new[] { member }).OrderBy(m => m.ActorId).ToArray();
            local.JoinTick = processingTick; local.JoinActionId = action.Id.Value; local.Status = GuardOrderStatus.WaitingTeam; local.Reason = "AwaitingLocalTeamMembers";
            long cause = Emit("GuardJoinedTeam", actor, action.Id, messageId: local.OrderMessageId, target: actor.Position, detail: "authorization=" + team.AuthorizationId);
            TryFormGuardTeam(team, local.Payload, cause); Finish(actor);
        }
        private void TryFormGuardTeam(LocalGuardTeamSnapshot team, EscortOrderPayload payload, long cause)
        {
            if (!team.Members.Select(m => m.ActorId).OrderBy(id => id).SequenceEqual(payload.Guards.OrderBy(id => id))) return;
            foreach (var member in team.Members)
            {
                var actor = actors[new Id<ActorTag>(member.ActorId)]; var copy = localGuardOrders[member.OrderMessageId];
                if (HasCriticalNeed(actor.Id) || actor.Action?.Kind == ActionKind.Move || actor.Position != payload.Dispatch.Rendezvous || !PhysicallyEquipped(actor, payload.Dispatch.RequiredEquipment) ||
                    copy.Status != GuardOrderStatus.WaitingTeam || copy.JoinTick != member.Tick || copy.JoinActionId != member.ActionId || !copy.Payload.Same(payload) || copy.Payload.Expires <= processingTick) return;
            }
            team.FormedTick = processingTick;
            // All participants already completed their own local join. This notification
            // is part of the co-located handshake, never a remote institutional update.
            foreach (var member in team.Members)
            {
                var copy = localGuardOrders[member.OrderMessageId]; copy.FormedTick = processingTick; copy.Status = GuardOrderStatus.Searching; copy.Reason = "SearchKnownCandidates";
                Emit("GuardTeamFormedLocally", actors[new Id<ActorTag>(member.ActorId)], messageId: member.OrderMessageId, target: payload.Dispatch.Rendezvous, detail: "authorization=" + team.AuthorizationId, causeId: cause);
                RequestGuardDecision(copy, processingTick);
            }
        }
        private void WithdrawGuardJoin(LocalGuardOrderSnapshot local)
        {
            if (local.FormedTick >= 0) return;
            if (localGuardTeams.TryGetValue(local.Payload.DispatchId, out var team)) team.Members = team.Members.Where(m => m.OrderMessageId != local.OrderMessageId).ToArray();
            local.JoinTick = -1; local.JoinActionId = 0;
        }
        private void PlanGuardJoin(Actor actor, LocalGuardOrderSnapshot local)
        {
            if (local.FormedTick >= 0) { local.Status = GuardOrderStatus.Searching; local.Reason = "SearchKnownCandidates"; RequestGuardDecision(local, processingTick); return; }
            if (!SpendPlanningRequest()) { RequestGuardDecision(local, checked(processingTick + 1)); return; }
            var plan = GuardSearchPlanning.Join(ActorView(actor.Id), local.Payload, Information!.InspectTicks, planner.MaximumDepth, planner.NodeBudget);
            ExecuteGuardPlan(actor, local, plan, "guard-join/v1");
        }
        private void EvaluateGuardSearch(Actor actor, LocalGuardOrderSnapshot local)
        {
            if (!SpendPlanningRequest()) { RequestGuardDecision(local, checked(processingTick + 1)); return; }
            var plan = PatrolPlanning.InspectCandidate(ActorView(actor.Id), local.Payload.Candidates[local.CandidateIndex], new Id<ActorTag>(local.Payload.SubjectActorId), Information!.InspectTicks, planner.MaximumDepth, planner.NodeBudget);
            ExecuteGuardPlan(actor, local, plan, "guard-search/v1");
        }
        private void ExecuteGuardPlan(Actor actor, LocalGuardOrderSnapshot local, PlanResult plan, string policy)
        {
            PlannerExpansions += plan.Expansions;
            if (plan.Outcome != PlanOutcome.PlanFound || plan.Steps.Count == 0)
            {
                if (plan.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; local.Reason = "PlanBudgetExhausted"; }
                else { PlansUnavailable++; local.Reason = "NoKnownSearchRoute"; QueueGuardOutcome(local, "SearchUnreachable", processingTick); }
                RequestGuardDecision(local, checked(processingTick + local.Backoff)); local.Backoff = NextWorkBackoff(local.Backoff);
                if (checkIns.TryGetValue(actor.Id, out var checkIn)) RequestCheckInDecision(checkIn, processingTick); return;
            }
            PlansFound++; local.Backoff = planner.InitialBackoff; if (local.Status != GuardOrderStatus.TargetLocated) local.Reason = policy; var step = plan.Steps[0].Intent;
            long cause = Emit("PlanSelected", actor, messageId: local.OrderMessageId, detail: policy + "; candidate=" + local.CandidateIndex + "; cost=" + plan.Cost);
            Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, CauseId = cause,
                Intent = new ActionIntent(actor.Id, step.Kind, step.Target, step.Item, serviceId: step.ServiceId, targetActor: step.TargetActor, guardOrderId: local.OrderMessageId) });
        }
        private void GuardTargetObserved(Actor actor, ActiveAction action, FactValue value)
        {
            if (action.GuardOrderId == 0 || !localGuardOrders.TryGetValue(action.GuardOrderId, out var local) || local.ActorId != actor.Id.Value || local.Status != GuardOrderStatus.Searching ||
                local.Payload.Expires <= processingTick || action.TargetActor.Value != local.Payload.SubjectActorId || action.Target != local.Payload.Candidates[local.CandidateIndex]) return;
            bool found = value.Kind == FactValueKind.Coordinate;
            local.SearchObservations = local.SearchObservations.Concat(new[] { new PatrolObservation(processingTick, action.Id.Value, action.Target, found) }).ToArray(); local.CandidateIndex++;
            if (found) { local.Status = GuardOrderStatus.TargetLocated; local.Reason = "LocallyIdentifiedTarget"; RemoveGuardDecision(local); }
            else if (local.CandidateIndex == local.Payload.Candidates.Count) { local.Status = GuardOrderStatus.SearchExhausted; local.Reason = "TargetNotLocated"; RemoveGuardDecision(local); QueueGuardOutcome(local, "TargetNotLocated", processingTick); }
            Emit("GuardSearchCandidateInspected", actor, action.Id, messageId: local.OrderMessageId, target: action.Target, detail: found ? "FoundHere" : "AbsentHere", causeId: action.CauseId);
        }
    }
}
