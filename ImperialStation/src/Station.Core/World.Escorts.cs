using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public enum EscortStatus { Escorting, AwaitingIntake, Interrupted, Detained, Released }
    public sealed class EscortMemberSnapshot
    {
        public long ActorId { get; internal set; }
        public long OrderMessageId { get; internal set; }
        public long MovementActionId { get; internal set; }
        internal EscortMemberSnapshot Copy() => (EscortMemberSnapshot)MemberwiseClone();
    }
    public sealed class EscortSnapshot
    {
        public long AuthorizationId { get; internal set; }
        public long DispatchId { get; internal set; }
        public long BeginActionId { get; internal set; }
        public long StartedTick { get; internal set; }
        public long BoundaryTick { get; internal set; }
        public EscortStatus Status { get; internal set; }
        public string Interruption { get; internal set; } = "";
        public IReadOnlyList<EscortMemberSnapshot> Members { get; internal set; } = Array.Empty<EscortMemberSnapshot>();
        internal EscortSnapshot Copy() { var c = (EscortSnapshot)MemberwiseClone(); c.Members = Members.Select(m => m.Copy()).ToArray(); return c; }
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, EscortSnapshot> escorts = new SortedDictionary<long, EscortSnapshot>();
        public IReadOnlyList<EscortSnapshot> InspectEscorts() => escorts.Values.Select(e => e.Copy()).ToArray();
        private EscortSnapshot? ActiveEscort(Id<ActorTag> actor) => escorts.Values.FirstOrDefault(e => (e.Status is EscortStatus.Escorting or EscortStatus.AwaitingIntake) && e.Members.Any(m => m.ActorId == actor.Value) || e.Status == EscortStatus.Detained && EscortPayload(e).SubjectActorId == actor.Value);
        private EscortOrderPayload EscortPayload(EscortSnapshot group) => localGuardOrders[group.Members.First(m => m.OrderMessageId > 0).OrderMessageId].Payload;
        private bool EscortControls(Id<ActorTag> actor) => ActiveEscort(actor) != null;
        private void PlanBeginEscort(Actor actor, LocalGuardOrderSnapshot local)
        {
            if (actor.Id.Value != local.Payload.Leader || escorts.ContainsKey(local.Payload.DispatchId)) return;
            if (!SpendPlanningRequest()) { RequestGuardDecision(local, checked(processingTick + 1)); return; }
            var plan = GuardSearchPlanning.Begin(ActorView(actor.Id), local.Payload, Information!.InspectTicks, planner.MaximumDepth, planner.NodeBudget);
            ExecuteGuardPlan(actor, local, plan, "begin-escort/v1");
        }
        private FailureReason ValidateBeginEscort(Actor leader, ActiveAction action)
        {
            if (!localGuardOrders.TryGetValue(action.GuardOrderId, out var order) || order.ActorId != leader.Id.Value || order.Status != GuardOrderStatus.TargetLocated ||
                order.Payload.Leader != leader.Id.Value || escorts.ContainsKey(order.Payload.DispatchId) || order.Payload.Expires <= processingTick) return FailureReason.UnknownRequiredFact;
            var p = order.Payload;
            if (!localGuardTeams.TryGetValue(p.DispatchId, out var team) || team.FormedTick < 0) return FailureReason.UnknownRequiredFact;
            foreach (long id in p.Guards)
            {
                var guard = actors[new Id<ActorTag>(id)];
                var copy = localGuardOrders.Values.SingleOrDefault(l => l.ActorId == id && l.Payload.DispatchId == p.DispatchId);
                if (copy == null || !copy.Payload.Same(p) || copy.Status != GuardOrderStatus.TargetLocated || copy.Response != "Accepted" || copy.AcknowledgmentMessageId == 0 ||
                    copy.FormedTick != team.FormedTick || copy.SearchObservations.LastOrDefault()?.Found != true || copy.SearchObservations.Last().Tile != leader.Position) return FailureReason.UnknownRequiredFact;
                if (guard.Position != leader.Position || guard.Action != null && guard.Action != action || resourceClaims.Any(c => c.Kind == ClaimKind.Staff && c.ResourceId == id)) return FailureReason.ActorBusy;
                if (HasCriticalNeed(guard.Id) || EscortControls(guard.Id) || !PhysicallyEquipped(guard, p.Dispatch.RequiredEquipment)) return FailureReason.MissingTool;
            }
            var target = actors[new Id<ActorTag>(p.SubjectActorId)];
            if (target.Position != leader.Position || target.Action?.Kind == ActionKind.Move) return FailureReason.NotAtRequiredTile;
            if (target.Account.Value != p.SubjectAccountId || EscortControls(target.Id) || resourceClaims.Any(c => c.Kind == ClaimKind.Staff && c.ResourceId == target.Id.Value)) return FailureReason.ActorBusy;
            return FailureReason.None;
        }
        private void StartBeginEscort(Actor actor, ActiveAction action)
        {
            var failure = ValidateBeginEscort(actor, action);
            if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            action.Completes = checked(processingTick + Information!.InspectTicks); actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, target: actor.Position, arrival: action.Completes, detail: "BeginEscort"); ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteBeginEscort(Actor leader, ActiveAction action)
        {
            var failure = ValidateBeginEscort(leader, action);
            if (failure != FailureReason.None) { leader.Action = null; Fail(leader, action.Id, failure); return; }
            var p = localGuardOrders[action.GuardOrderId].Payload;
            PathRequests++; var path = Topology.FindPath(leader.Position, p.Dispatch.Destination, Config.PathBudget); PathExpansions += path.Expansions;
            if (path.Outcome != PathOutcome.PathFound) { leader.Action = null; Fail(leader, action.Id, path.Outcome == PathOutcome.BudgetExhausted ? FailureReason.PathBudgetExhausted : FailureReason.NoRoute); return; }
            // Publish local membership before cancelling the target's finite action, so its wakeups cannot escape custody.
            var group = new EscortSnapshot { AuthorizationId = p.AuthorizationId, DispatchId = p.DispatchId, BeginActionId = action.Id.Value, StartedTick = processingTick, BoundaryTick = processingTick,
                Members = p.Guards.Select(id => new EscortMemberSnapshot { ActorId = id, OrderMessageId = localGuardOrders.Values.Single(l => l.ActorId == id && l.Payload.DispatchId == p.DispatchId).OrderMessageId })
                    .Concat(new[] { new EscortMemberSnapshot { ActorId = p.SubjectActorId } }).OrderBy(m => m.ActorId).ToArray() };
            escorts.Add(group.DispatchId, group);
            var target = actors[new Id<ActorTag>(p.SubjectActorId)]; CancelRunning(target);
            foreach (var member in group.Members) EndSession(new Id<ActorTag>(member.ActorId), "CustodyBegan");
            foreach (var order in localOrders.Values.Where(o => o.ActorId == target.Id.Value && o.Status is LocalOrderStatus.Read or LocalOrderStatus.AcknowledgmentPending or LocalOrderStatus.Working))
            { order.Status = LocalOrderStatus.Suspended; order.Reason = "CustodySuspended"; CancelPendingAcknowledgment(order); RemoveOrderGoal(order); SetAttemptExecution(order.AttemptId, WorkExecution.Waiting); }
            long cause = Emit("CustodyBegan", leader, action.Id, target: leader.Position, messageId: action.GuardOrderId, detail: "authorization=" + p.AuthorizationId);
            Finish(leader);
            if (path.Route.Count == 1) { group.Status = EscortStatus.AwaitingIntake; Emit("EscortAwaitingIntake", leader, causeId: cause, target: leader.Position); return; }
            foreach (var member in group.Members)
            {
                var actor = actors[new Id<ActorTag>(member.ActorId)];
                var move = new ActiveAction { Id = new Id<ActionTag>(nextAction++), Kind = ActionKind.Move, Target = p.Dispatch.Destination, Started = processingTick, CauseId = cause, GuardOrderId = member.OrderMessageId };
                move.Route.AddRange(path.Route); actor.Action = move; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None; member.MovementActionId = move.Id.Value;
                Emit("ActionRequested", actor, move.Id, target: move.Target, detail: "Move", causeId: cause, origin: ActionOrigin.SystemProtocol);
            }
            StartEscortSegment(group);
        }
        private void StartEscortSegment(EscortSnapshot group)
        {
            // One frozen timing calculation is shared by every participant, including the target.
            var first = actors[new Id<ActorTag>(group.Members[0].ActorId)].Action!;
            var source = first.Route[first.RouteIndex]; var destination = first.Route[first.RouteIndex + 1];
            long arrival = checked(processingTick + Topology.EdgeTicks(source, destination));
            foreach (var member in group.Members)
            {
                var actor = actors[new Id<ActorTag>(member.ActorId)]; var a = actor.Action!;
                a.Source = source; a.Destination = destination; a.Departure = processingTick; a.Arrival = arrival;
                Emit("MovementStarted", actor, a.Id, source: source, target: destination, departure: processingTick, arrival: arrival, causeId: a.CauseId); ScheduleCompletion(actor, arrival);
            }
        }
        private void CompleteEscortSegment(EscortSnapshot group)
        {
            foreach (var member in group.Members)
            {
                var actor = actors[new Id<ActorTag>(member.ActorId)]; var a = actor.Action;
                if (a == null || a.Kind != ActionKind.Move || a.Id.Value != member.MovementActionId || a.Arrival != processingTick) throw new InvalidOperationException("Escort lost a coupled movement member.");
            }
            foreach (var member in group.Members)
            {
                var actor = actors[new Id<ActorTag>(member.ActorId)]; var a = actor.Action!;
                foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == a.Id.Value).ToArray()) scheduled.Remove(e);
                RelocateActor(actor, a.Destination); a.RouteIndex++; group.BoundaryTick = processingTick;
                Emit("TileArrived", actor, a.Id, source: a.Source, target: actor.Position, departure: a.Departure, arrival: a.Arrival); ScheduleSensing(actor, a.CauseId);
            }
            if (group.Interruption.Length > 0 || group.Members.Any(m => actors[new Id<ActorTag>(m.ActorId)].Action!.CancelAtBoundary)) { StopEscort(group, group.Interruption.Length > 0 ? group.Interruption : "MemberCancelled"); return; }
            var first = actors[new Id<ActorTag>(group.Members[0].ActorId)].Action!;
            if (first.RouteIndex < first.Route.Count - 1) { StartEscortSegment(group); return; }
            group.Status = EscortStatus.AwaitingIntake;
            foreach (var member in group.Members) Finish(actors[new Id<ActorTag>(member.ActorId)]);
            Emit("EscortAwaitingIntake", actors[new Id<ActorTag>(EscortPayload(group).Leader)], target: first.Target, detail: "authorization=" + group.AuthorizationId);
        }
        private void InterruptEscort(EscortSnapshot group, string reason)
        {
            if (group.Interruption.Length == 0) group.Interruption = reason;
            if (group.Status == EscortStatus.AwaitingIntake) { StopEscort(group, reason); return; }
            foreach (var member in group.Members) actors[new Id<ActorTag>(member.ActorId)].Action!.CancelAtBoundary = true;
        }
        private void StopEscort(EscortSnapshot group, string reason)
        {
            group.Status = EscortStatus.Interrupted; group.Interruption = reason; group.BoundaryTick = processingTick;
            foreach (var member in group.Members.Where(m => m.OrderMessageId > 0))
            {
                var local = localGuardOrders[member.OrderMessageId];
                if (!(local.Status is GuardOrderStatus.Expired or GuardOrderStatus.Revoked)) { local.Status = GuardOrderStatus.EscortInterrupted; local.Reason = reason; }
                RemoveGuardDecision(local); QueueGuardOutcome(local, "EscortInterrupted", processingTick);
            }
            foreach (var member in group.Members) { var actor = actors[new Id<ActorTag>(member.ActorId)]; if (actor.Action != null) CancelRunning(actor); else OnActionFailed(actor); }
            Emit("EscortInterrupted", detail: "authorization=" + group.AuthorizationId + "; " + reason);
        }
    }
}
