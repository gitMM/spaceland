using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<long, DetentionFacilitySnapshot> detentionFacilities = new SortedDictionary<long, DetentionFacilitySnapshot>();
        private readonly SortedDictionary<long, DetentionStaySnapshot> detentionStays = new SortedDictionary<long, DetentionStaySnapshot>();
        private readonly SortedDictionary<long, long> releaseOrderMessages = new SortedDictionary<long, long>();
        private bool ReleaseWasReceived(long authorization, long tick, long version) => records.History.Values.Any(e => e.Kind == RecordKind.DetentionRelease && e.EntityId == authorization && e.Tick <= tick && e.Version <= version);
        public IReadOnlyList<DetentionStaySnapshot> InspectDetentionStays() => detentionStays.Values.Select(s => s.Copy()).ToArray();
        public IReadOnlyList<DetentionFacilitySnapshot> InspectDetentionFacilities() => detentionFacilities.Values.Select(f => {
            var copy = f.Copy(); copy.Reservations = detentionStays.Values.Count(s => s.FacilityId == f.Definition.Id && s.Status == DetentionStayStatus.Reserved);
            copy.Occupants = detentionStays.Values.Count(s => s.FacilityId == f.Definition.Id && s.Status is DetentionStayStatus.Detained or DetentionStayStatus.Releasing);
            copy.Operations = FacilityOperations(f.Definition.Id); return copy; }).ToArray();
        private int FacilityOperations(long id) => detentionStays.Values.Count(s => s.FacilityId == id && s.Status is DetentionStayStatus.Reserved or DetentionStayStatus.Releasing);
        public void ScheduleDetentionServiceChange(long tick, long facility, bool powered, bool connected)
        {
            ValidateFuture(tick); if (!detentionFacilities.ContainsKey(facility)) throw new ArgumentException("Unknown detention facility.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.DetentionServiceChange, ActionId = facility, Value = (powered ? 1 : 0) | (connected ? 2 : 0) });
        }
        private void ChangeDetentionService(long id, long flags)
        {
            var facility = detentionFacilities[id]; facility.Powered = (flags & 1) != 0; facility.Connected = (flags & 2) != 0;
            foreach (var stay in detentionStays.Values.Where(s => s.FacilityId == id).ToArray())
            {
                if ((!facility.Powered || !facility.Connected) && stay.Status is DetentionStayStatus.Reserved or DetentionStayStatus.Releasing)
                    CancelRunning(actors[new Id<ActorTag>(stay.Status == DetentionStayStatus.Reserved ? stay.LeaderId : stay.ActorId)]);
                if (stay.Status == DetentionStayStatus.Detained && stay.ReleaseDue <= processingTick) ScheduleDetentionRelease(stay, checked(processingTick + 1));
            }
            WakeWaitingIntakes(id);
            Emit("DetentionServiceChanged", detail: "facility=" + id + "; powered=" + facility.Powered + "; connected=" + facility.Connected);
        }
        private void PlanDetentionIntake(Actor actor, LocalGuardOrderSnapshot local, EscortSnapshot group)
        {
            if (actor.Id.Value != local.Payload.Leader || group.Status != EscortStatus.AwaitingIntake) return;
            // A local refusal remains known until this service changes. Routine
            // wakeups must not turn a full facility into a two-tick retry loop.
            if (local.Reason is "Intake/FacilityFull" or "Intake/ServiceBusy" or "Intake/NoPower" or "Intake/Disconnected") return;
            if (Detention == null || !Detention.Facilities.Any(f => f.Id == local.Payload.Dispatch.FacilityId)) { local.Reason = "DetentionServiceNotDeclared"; return; }
            if (!SpendPlanningRequest()) { RequestGuardDecision(local, checked(processingTick + 1)); return; }
            // The copied destination and facility identity choose the action; current
            // power, capacity and authorization are disclosed only by the local service.
            var plan = Station.Mind.GuardSearchPlanning.Intake(ActorView(actor.Id), local.Payload, planner.MaximumDepth, planner.NodeBudget);
            ExecuteGuardPlan(actor, local, plan, "register-intake/v1");
        }
        private void WakeWaitingIntakes(long facilityId)
        {
            foreach (var group in escorts.Values.Where(g => g.Status == EscortStatus.AwaitingIntake && EscortPayload(g).Dispatch.FacilityId == facilityId))
            {
                var leader = actors[new Id<ActorTag>(EscortPayload(group).Leader)];
                if (leader.Position != detentionFacilities[facilityId].Definition.Position) continue;
                var local = localGuardOrders[group.Members.Single(m => m.ActorId == leader.Id.Value).OrderMessageId];
                local.Reason = "Intake/ServiceChanged";
                RequestGuardDecision(local, checked(processingTick + 1));
            }
        }
        private FailureReason ValidateIntake(Actor leader, ActiveAction action)
        {
            if (!localGuardOrders.TryGetValue(action.GuardOrderId, out var local) || local.ActorId != leader.Id.Value || local.Payload.Leader != leader.Id.Value ||
                !escorts.TryGetValue(local.Payload.DispatchId, out var group) || group.Status != EscortStatus.AwaitingIntake) return FailureReason.UnknownRequiredFact;
            var p = local.Payload;
            if (!detentionFacilities.TryGetValue(action.ServiceId, out var facility) || action.ServiceId != p.Dispatch.FacilityId || facility.Definition.Position != leader.Position || leader.Position != p.Dispatch.Destination)
                return FailureReason.RequiredServiceNotPresent;
            if (!facility.Powered || !facility.Connected) return FailureReason.ServiceBusy;
            if (!detentionAuthorizations.TryGetValue(p.AuthorizationId, out var authorization) || authorization.Status != DetentionAuthorizationStatus.Authorized || authorization.Expires <= processingTick ||
                authorization.ActorId != p.SubjectActorId || authorization.AccountId != p.SubjectAccountId) return FailureReason.UnknownRequiredFact;
            foreach (var member in group.Members)
            {
                var actor = actors[new Id<ActorTag>(member.ActorId)];
                if (actor.Position != facility.Definition.Position || actor.Action != null && actor.Action != action) return FailureReason.NotAtRequiredTile;
                if (member.OrderMessageId > 0 && (!PhysicallyEquipped(actor, p.Dispatch.RequiredEquipment) || localGuardOrders[member.OrderMessageId].Payload.Expires <= processingTick)) return FailureReason.MissingTool;
            }
            int otherSlots = detentionStays.Values.Count(s => s.FacilityId == action.ServiceId && s.AuthorizationId != p.AuthorizationId && s.Status is DetentionStayStatus.Reserved or DetentionStayStatus.Detained or DetentionStayStatus.Releasing);
            int otherOperations = detentionStays.Values.Count(s => s.FacilityId == action.ServiceId && s.AuthorizationId != p.AuthorizationId && s.Status is DetentionStayStatus.Reserved or DetentionStayStatus.Releasing);
            return otherSlots >= facility.Definition.Capacity || otherOperations >= facility.Definition.ServiceCapacity ? FailureReason.ServiceBusy : FailureReason.None;
        }
        private void StartDetentionIntake(Actor leader, ActiveAction action)
        {
            var failure = ValidateIntake(leader, action);
            if (failure != FailureReason.None)
            {
                if (localGuardOrders.TryGetValue(action.GuardOrderId, out var order))
                {
                    order.Reason = "Intake/" + failure;
                    if (failure == FailureReason.ServiceBusy && detentionFacilities.TryGetValue(action.ServiceId, out var unavailable))
                        order.Reason = !unavailable.Powered ? "Intake/NoPower" : !unavailable.Connected ? "Intake/Disconnected" :
                            detentionStays.Values.Count(s => s.FacilityId == action.ServiceId && s.Status is DetentionStayStatus.Reserved or DetentionStayStatus.Detained or DetentionStayStatus.Releasing) >= unavailable.Definition.Capacity ? "Intake/FacilityFull" : "Intake/ServiceBusy";
                }
                ObserveRevokedIntake(leader, action); Fail(leader, action.Id, failure); return;
            }
            var p = localGuardOrders[action.GuardOrderId].Payload;
            if (detentionStays.TryGetValue(p.AuthorizationId, out var old) && old.Status != DetentionStayStatus.Cancelled) { Fail(leader, action.Id, FailureReason.ResourceClaimed); return; }
            action.Completes = checked(processingTick + detentionFacilities[action.ServiceId].Definition.IntakeTicks);
            detentionStays[p.AuthorizationId] = new DetentionStaySnapshot { AuthorizationId = p.AuthorizationId, FacilityId = action.ServiceId, ActorId = p.SubjectActorId, AccountId = p.SubjectAccountId,
                LeaderId = leader.Id.Value, IntakeActionId = action.Id.Value, IntakeStarted = processingTick, Status = DetentionStayStatus.Reserved };
            leader.Action = action; leader.LastStatus = ActionStatus.Running; leader.LastFailure = FailureReason.None;
            Emit("DetentionSlotReserved", leader, action.Id, target: leader.Position, detail: "facility=" + action.ServiceId + "; authorization=" + p.AuthorizationId, causeId: action.CauseId);
            ScheduleCompletion(leader, action.Completes);
        }
        private void CompleteDetentionIntake(Actor leader, ActiveAction action)
        {
            var p = localGuardOrders[action.GuardOrderId].Payload; var stay = detentionStays[p.AuthorizationId]; var group = escorts[p.DispatchId];
            var failure = ValidateIntake(leader, action);
            if (failure != FailureReason.None) { stay.Status = DetentionStayStatus.Cancelled; stay.Reason = failure.ToString(); leader.Action = null; ObserveRevokedIntake(leader, action); Fail(leader, action.Id, failure); return; }
            long due = checked(processingTick + p.SentenceTicks);
            stay.Status = DetentionStayStatus.Detained; stay.IntakeTick = processingTick; stay.ReleaseDue = due; group.Status = EscortStatus.Detained;
            WakeWaitingIntakes(stay.FacilityId);
            long cause = Emit("Detained", leader, action.Id, target: leader.Position, detail: "authorization=" + p.AuthorizationId + "; facility=" + stay.FacilityId, causeId: action.CauseId);
            stay.IntakeEventId = cause;
            foreach (var order in localOrders.Values.Where(o => o.ActorId == stay.ActorId && o.Status == LocalOrderStatus.Suspended)) order.Reason = "DetentionAwaitingCurrentOrders";
            stay.IntakeReceiptId = PostDetentionServiceMessage(stay, MessageKind.DetentionIntakeReceipt, RecipientKind.Authority, "Detained", cause);
            ScheduleDetentionRelease(stay, due);
            foreach (var member in group.Members.Where(m => m.OrderMessageId > 0))
            { var l = localGuardOrders[member.OrderMessageId]; l.Status = GuardOrderStatus.TransferredToFacility; l.Reason = "LocalIntakeCompleted"; RemoveGuardDecision(l); }
            Finish(leader);
            foreach (var member in group.Members.Where(m => m.OrderMessageId > 0 && m.ActorId != leader.Id.Value)) OnActionFailed(actors[new Id<ActorTag>(member.ActorId)]);
        }
        private void ScheduleDetentionRelease(DetentionStaySnapshot stay, long tick)
        {
            if (stay.NextRelease >= 0 && stay.NextRelease <= tick) return;
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.DetentionReleaseDue && e.Value == stay.AuthorizationId).ToArray()) scheduled.Remove(e);
            stay.NextRelease = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.DetentionReleaseDue, Value = stay.AuthorizationId });
        }
        private void BeginDetentionRelease(long authorization)
        {
            var stay = detentionStays[authorization]; stay.NextRelease = -1;
            if (stay.Status != DetentionStayStatus.Detained) throw new InvalidOperationException("Release timer does not own a detained occupant.");
            var f = detentionFacilities[stay.FacilityId]; var target = actors[new Id<ActorTag>(stay.ActorId)];
            if (processingTick < stay.ReleaseDue) throw new InvalidOperationException("Premature detention release.");
            if (target.Action != null && IsNeedAction(target.Action.Kind) && f.Powered && f.Connected && FacilityOperations(stay.FacilityId) < f.Definition.ServiceCapacity) CancelRunning(target);
            if (!f.Powered || !f.Connected || FacilityOperations(stay.FacilityId) >= f.Definition.ServiceCapacity || target.Action != null || target.Position != f.Definition.Position)
            { stay.Reason = "ReleaseServiceUnavailable"; ScheduleDetentionRelease(stay, checked(processingTick + planner.InitialBackoff)); return; }
            var action = new ActiveAction { Id = new Id<ActionTag>(nextAction++), Kind = ActionKind.ReleaseDetention, Target = f.Definition.Position, ServiceId = stay.FacilityId,
                Started = processingTick, Completes = checked(processingTick + f.Definition.ReleaseTicks) };
            action.CauseId = Emit("DetentionReleaseDue", target, detail: "authorization=" + authorization + "; due=" + stay.ReleaseDue, causeId: stay.IntakeEventId);
            stay.Status = DetentionStayStatus.Releasing; stay.ReleaseActionId = action.Id.Value; stay.ReleaseStarted = processingTick; stay.Reason = "";
            target.Action = action; target.LastStatus = ActionStatus.Running; target.LastFailure = FailureReason.None;
            Emit("ActionRequested", target, action.Id, target: action.Target, detail: "ReleaseDetention", causeId: action.CauseId, origin: ActionOrigin.SystemProtocol); ScheduleCompletion(target, action.Completes);
        }
        private void CompleteDetentionRelease(Actor target, ActiveAction action)
        {
            var stay = detentionStays.Values.Single(s => s.Status == DetentionStayStatus.Releasing && s.ReleaseActionId == action.Id.Value);
            var f = detentionFacilities[stay.FacilityId];
            if (!f.Powered || !f.Connected || target.Position != f.Definition.Position) { CancelRunning(target); return; }
            stay.Status = DetentionStayStatus.Released; stay.ReleaseTick = processingTick; escorts.Values.Single(g => g.AuthorizationId == stay.AuthorizationId && g.Status == EscortStatus.Detained).Status = EscortStatus.Released;
            WakeWaitingIntakes(stay.FacilityId);
            long cause = Emit("DetentionReleased", target, action.Id, target: target.Position, detail: "authorization=" + stay.AuthorizationId, causeId: action.CauseId);
            stay.ReleaseReceiptId = PostDetentionServiceMessage(stay, MessageKind.DetentionReleaseReceipt, RecipientKind.Authority, "Released", cause);
            stay.ReleaseNoticeId = PostDetentionServiceMessage(stay, MessageKind.DetentionReleaseNotice, RecipientKind.Account, "Released", cause);
            Finish(target);
        }
        private void CancelDetentionOperation(ActiveAction action)
        {
            if (action.Kind == ActionKind.RegisterIntake)
            { var s = detentionStays.Values.Single(s => s.Status == DetentionStayStatus.Reserved && s.IntakeActionId == action.Id.Value); s.Status = DetentionStayStatus.Cancelled; s.Reason = "IntakeCancelled"; WakeWaitingIntakes(s.FacilityId); }
            else if (action.Kind == ActionKind.ReleaseDetention)
            {
                var s = detentionStays.Values.Single(s => s.Status == DetentionStayStatus.Releasing && s.ReleaseActionId == action.Id.Value);
                s.Status = DetentionStayStatus.Detained; s.ReleaseActionId = 0; s.ReleaseStarted = -1; s.Reason = "ReleaseCancelled"; ScheduleDetentionRelease(s, checked(processingTick + planner.InitialBackoff));
                WakeWaitingIntakes(s.FacilityId);
            }
        }
        private long PostDetentionServiceMessage(DetentionStaySnapshot stay, MessageKind kind, RecipientKind recipient, string status, long parentCause)
        {
            var draft = new MessageDraft(kind, recipient, recipient == RecipientKind.Account ? new Id<AccountTag>(stay.AccountId) : default,
                new FactKey(SubjectKind.Order, stay.AuthorizationId, AttributeKind.DetentionStatus), new FactValue(FactValueKind.Text, text: status), processingTick,
                operationKey: "detention-service/" + stay.FacilityId + "/" + stay.AuthorizationId + "/" + kind);
            var message = new MessageSnapshot { Id = nextMessage++, Draft = draft, SubmittedTick = processingTick, DeliveryTick = checked(processingTick + Information!.LatencyTicks), Stage = MessageStage.Posted };
            messages.Add(message.Id, message); long cause = Emit("DetentionServiceMessagePosted", messageId: message.Id, detail: draft.OperationKey, causeId: parentCause);
            Schedule(new ScheduledEvent { Tick = message.DeliveryTick, Phase = EventPhase.Delivery, Kind = ScheduledKind.MessageDelivery, Value = message.Id, CauseId = cause }); return message.Id;
        }
        private bool ReceiveDetentionServiceReceipt(MessageSnapshot message)
        {
            if (!(message.Draft.Kind is MessageKind.DetentionIntakeReceipt or MessageKind.DetentionReleaseReceipt)) return true;
            bool intake = message.Draft.Kind == MessageKind.DetentionIntakeReceipt;
            // Formal service-issued receipt identity is the authority's evidence.
            // It does not inspect the subject's current position, actions or inventory.
            var stay = detentionStays.Values.SingleOrDefault(s => (intake ? s.IntakeReceiptId : s.ReleaseReceiptId) == message.Id);
            if (stay == null || message.SenderAccount.Value != 0 || message.Seeded) throw new InvalidOperationException("Unregistered detention service receipt.");
            if (intake) stay.IntakeReceivedTick = processingTick; else stay.ReleaseReceivedTick = processingTick;
            var dispatch = escortDispatches[stay.AuthorizationId]; dispatch.Status = intake ? EscortDispatchStatus.Detained : EscortDispatchStatus.Released; dispatch.Reason = intake ? "IntakeReceipt" : "ReleaseReceipt";
            records.Append(intake ? RecordKind.DetentionIntake : RecordKind.DetentionRelease, stay.AuthorizationId, stay.AccountId, processingTick, message.Draft.OperationKey,
                new[] { Number(AttributeKind.ActorId, stay.ActorId), Number(AttributeKind.AuthorizationId, stay.AuthorizationId), Number(AttributeKind.MessageId, message.Id),
                    Number(AttributeKind.FacilityId, stay.FacilityId), Number(AttributeKind.ObservedTick, message.Draft.ObservedTick), Text(AttributeKind.DetentionStatus, intake ? "Detained" : "Released") });
            if (!intake)
            {
                if (activeDetentionAuthorizations.TryGetValue(stay.AccountId, out long id) && id == stay.AuthorizationId) activeDetentionAuthorizations.Remove(stay.AccountId);
                foreach (var attempt in workAttempts.Values.Where(a => a.ActorId == stay.ActorId && a.Assignment == AssignmentStatus.Active && a.Expires > processingTick))
                {
                    var original = messages[attempt.OrderMessageId].Draft;
                    long refresh = PostAuthorityWork(new MessageDraft(MessageKind.Order, RecipientKind.Account, new Id<AccountTag>(stay.AccountId), original.Subject, original.Claim, processingTick,
                        original.TaskId, original.AttemptId, workOrder: original.WorkOrder, operationKey: "post-detention/" + stay.AuthorizationId + "/" + attempt.Id));
                    releaseOrderMessages.Add(refresh, stay.AuthorizationId);
                }
            }
            if (!intake) RequestEscortReview(processingTick);
            RequestIncidentReview(processingTick); return false;
        }
        private bool ReadPostDetentionOrder(Actor actor, MessageSnapshot message)
        {
            if (!releaseOrderMessages.TryGetValue(message.Id, out long id) || detentionStays[id].ActorId != actor.Id.Value ||
                !localOrders.TryGetValue(message.Draft.AttemptId, out var copy) || copy.Status != LocalOrderStatus.Suspended || copy.Reason != "DetentionAwaitingCurrentOrders") return false;
            if (copy.Expires <= processingTick) { copy.Status = LocalOrderStatus.Expired; copy.Reason = "KnownOrderExpired"; return true; }
            copy.Status = LocalOrderStatus.Read; copy.Reason = "CurrentOrdersRead"; RequestOrderDecision(copy, processingTick); return true;
        }
    }
}
