using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private bool GuardIntakeCommunication(EscortSnapshot escort, Id<ActorTag> actor, ActionKind kind) => escort.Status == EscortStatus.AwaitingIntake &&
            EscortPayload(escort).SubjectActorId != actor.Value && (kind is ActionKind.Login or ActionKind.ReadInbox or ActionKind.Logout);
        private readonly SortedDictionary<long, DetentionRevocationSnapshot> detentionRevocations = new SortedDictionary<long, DetentionRevocationSnapshot>();
        private readonly SortedDictionary<string, long> revocationKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<long, KnownGuardRevocationSnapshot> knownGuardRevocations = new SortedDictionary<long, KnownGuardRevocationSnapshot>();
        private static readonly IComparer<(long Order, string Outcome)> guardOutcomeComparer = Comparer<(long Order, string Outcome)>.Create((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : StringComparer.Ordinal.Compare(a.Outcome, b.Outcome));
        private readonly SortedDictionary<(long Order, string Outcome), GuardOutcomeSnapshot> localGuardOutcomes = new SortedDictionary<(long, string), GuardOutcomeSnapshot>(guardOutcomeComparer);
        private readonly SortedDictionary<(long Order, string Outcome), GuardOutcomeSnapshot> receivedGuardOutcomes = new SortedDictionary<(long, string), GuardOutcomeSnapshot>(guardOutcomeComparer);
        public IReadOnlyList<DetentionRevocationSnapshot> InspectDetentionRevocations() => detentionRevocations.Values.Select(r => r.Copy()).ToArray();
        public IReadOnlyList<KnownGuardRevocationSnapshot> InspectKnownGuardRevocations() => knownGuardRevocations.Values.Select(r => r.Copy()).ToArray();
        public IReadOnlyList<GuardOutcomeSnapshot> InspectGuardOutcomes(bool received = false) => (received ? receivedGuardOutcomes : localGuardOutcomes).Values.Select(r => r.Copy()).ToArray();
        public void ScheduleDetentionRevocation(long tick, long authorization, string operationKey)
        {
            if (revocationKeys.TryGetValue(operationKey, out long prior)) { if (prior != authorization) throw new ArgumentException("Revocation key conflicts with an existing authorization."); return; }
            ValidateFuture(tick);
            if (authorization < 1 || string.IsNullOrWhiteSpace(operationKey)) throw new ArgumentException("Revocation requires an authorization ID and operation key.");
            if (detentionRevocations.ContainsKey(authorization)) throw new ArgumentException("Authorization already has a scheduled or applied revocation.");
            revocationKeys.Add(operationKey, authorization);
            detentionRevocations.Add(authorization, new DetentionRevocationSnapshot { AuthorizationId = authorization, Tick = tick, OperationKey = operationKey });
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.RevokeDetention, Value = authorization });
        }
        private void RevokeDetention(long id)
        {
            var revocation = detentionRevocations[id]; revocation.Applied = true;
            if (!detentionAuthorizations.TryGetValue(id, out var authorization)) throw new InvalidOperationException("Revocation refers to an authorization that has not been issued.");
            if (authorization.Status != DetentionAuthorizationStatus.Authorized || authorization.Expires <= processingTick) return;
            authorization.Status = DetentionAuthorizationStatus.Revoked; authorization.RevokedTick = processingTick;
            if (activeDetentionAuthorizations.TryGetValue(authorization.AccountId, out long active) && active == id) activeDetentionAuthorizations.Remove(authorization.AccountId);
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ExpireDetentionAuthorization && e.Value == id).ToArray()) scheduled.Remove(e);
            RecordDetentionAuthorization(authorization);
            long cause = Emit("DetentionAuthorizationRevoked", detail: "authorization=" + id + "; key=" + revocation.OperationKey);
            if (escortDispatches.TryGetValue(id, out var dispatch))
            {
                if (!(dispatch.Status is EscortDispatchStatus.Detained or EscortDispatchStatus.Released)) { dispatch.Status = EscortDispatchStatus.Cancelled; dispatch.Reason = "AuthorizationRevoked"; }
                var notices = new List<long>();
                foreach (long orderId in dispatch.OrderMessageIds)
                {
                    var original = messages[orderId];
                    notices.Add(PostAuthorityWork(new MessageDraft(MessageKind.EscortRevoked, RecipientKind.Account, original.Draft.Recipient, original.Draft.Subject,
                        new FactValue(FactValueKind.Text, text: "AuthorizationRevoked"), processingTick, relatedMessageId: orderId,
                        operationKey: "escort-revoked/" + orderId), cause));
                }
                revocation.Notices = notices.ToArray();
            }
            if (!escortDispatches.Values.Any(d => d.Status == EscortDispatchStatus.AwaitingGuards))
            { foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ReviewEscorts).ToArray()) scheduled.Remove(e); nextEscortReview = -1; }
            RequestIncidentReview(processingTick);
            // No private copy, physical action or custody state is changed here.
        }
        private bool RevocationWasKnownToAuthority(long authorization, long tick, long version) => records.History.Values.Any(e => e.Kind == RecordKind.DetentionAuthorization && e.EntityId == authorization &&
            e.Tick <= tick && e.Version <= version && RecordText(e, AttributeKind.DetentionStatus) == "Revoked");
        private void ReadGuardRevocation(Actor actor, MessageSnapshot message)
        {
            if (message.Draft.Kind != MessageKind.EscortRevoked || message.SenderAccount.Value != 0 || message.Draft.Recipient != actor.Account ||
                !localGuardOrders.TryGetValue(message.Draft.RelatedMessageId, out var local) || local.ActorId != actor.Id.Value) return;
            LearnGuardRevocation(local, message.Id, 0, 0, GuardOutcomePolicy.RevocationReason(message.Draft.Claim.Text));
        }
        private void LearnGuardRevocation(LocalGuardOrderSnapshot local, long message, long serviceAction, long facility, string reason = "KnownAuthorizationRevoked")
        {
            if (knownGuardRevocations.ContainsKey(local.OrderMessageId)) return;
            knownGuardRevocations.Add(local.OrderMessageId, new KnownGuardRevocationSnapshot { OrderMessageId = local.OrderMessageId, LearnedTick = processingTick, MessageId = message, ServiceActionId = serviceAction, FacilityId = facility });
            local.Status = GuardOrderStatus.Revoked; local.Reason = reason; RemoveGuardDecision(local); WithdrawGuardJoin(local);
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ExpireGuardOrder && e.Value == local.OrderMessageId).ToArray()) scheduled.Remove(e);
            var actor = actors[new Id<ActorTag>(local.ActorId)];
            var key = new FactKey(SubjectKind.Order, local.OrderMessageId, AttributeKind.DetentionStatus);
            if (actor.Knowledge.Entries.TryGetValue(key, out var entry))
            { var fact = entry.Fact; entry.Fact = new KnowledgeFact(fact.Id, fact.Key, fact.Value, fact.SourceKind, fact.SourceId, fact.ObservedTick, fact.LearnedTick, false, fact.SourceActorId); }
            if (actor.Action?.GuardOrderId == local.OrderMessageId && actor.Action.Kind == ActionKind.Move) actor.Action.CancelAtBoundary = true;
            QueueGuardOutcome(local, "AuthorizationRevoked", processingTick);
            Emit("GuardRevocationLearned", actor, messageId: message, detail: "order=" + local.OrderMessageId + "; service-action=" + serviceAction);
        }
        private void ApplyLearnedGuardRevocation(Actor actor)
        {
            foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value && l.Status == GuardOrderStatus.Revoked))
            {
                var group = ActiveEscort(actor.Id);
                if (group != null && group.AuthorizationId == local.Payload.AuthorizationId && group.Status is EscortStatus.Escorting or EscortStatus.AwaitingIntake)
                    InterruptEscort(group, "LearnedRevocation");
            }
        }
        private void ObserveRevokedIntake(Actor actor, ActiveAction action)
        {
            if (!localGuardOrders.TryGetValue(action.GuardOrderId, out var local) || local.ActorId != actor.Id.Value || local.Payload.Leader != actor.Id.Value || !escorts.TryGetValue(local.Payload.DispatchId, out var escort) || escort.Status != EscortStatus.AwaitingIntake ||
                !detentionAuthorizations.TryGetValue(local.Payload.AuthorizationId, out var authorization) || authorization.Status != DetentionAuthorizationStatus.Revoked ||
                !detentionFacilities.TryGetValue(action.ServiceId, out var facility) || !facility.Powered || !facility.Connected || actor.Position != facility.Definition.Position || action.ServiceId != local.Payload.Dispatch.FacilityId) return;
            LearnGuardRevocation(local, 0, action.Id.Value, action.ServiceId); ApplyLearnedGuardRevocation(actor);
        }
        private static bool GuardOutcomeTerminal(string outcome) => GuardOutcomePolicy.Terminal(outcome);
        private static bool ValidGuardOutcome(string outcome) => GuardOutcomePolicy.Valid(outcome);
        private static EscortDispatchProgress DispatchProgress(EscortDispatchStatus status) => status switch
        {
            EscortDispatchStatus.AwaitingGuards => EscortDispatchProgress.AwaitingGuards, EscortDispatchStatus.AwaitingReaders => EscortDispatchProgress.AwaitingReaders,
            EscortDispatchStatus.TeamForming => EscortDispatchProgress.TeamForming, EscortDispatchStatus.Blocked => EscortDispatchProgress.Blocked, EscortDispatchStatus.Expired => EscortDispatchProgress.Expired,
            EscortDispatchStatus.Detained => EscortDispatchProgress.Detained, EscortDispatchStatus.Released => EscortDispatchProgress.Released, EscortDispatchStatus.Cancelled => EscortDispatchProgress.Cancelled,
            EscortDispatchStatus.NeedsAttention => EscortDispatchProgress.NeedsAttention,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        private void QueueGuardOutcome(LocalGuardOrderSnapshot local, string outcome, long observed)
        {
            var key = (local.OrderMessageId, outcome); if (localGuardOutcomes.ContainsKey(key)) return;
            var result = new GuardOutcomeSnapshot { OrderMessageId = local.OrderMessageId, ActorId = local.ActorId, Outcome = outcome, ObservedTick = observed };
            localGuardOutcomes.Add(key, result); var actor = new Id<ActorTag>(local.ActorId);
            if (!reportAgents.TryGetValue(actor, out var reports)) { reports = new ReportAgent { Actor = actor, Backoff = planner.InitialBackoff }; reportAgents.Add(actor, reports); }
            QueueReport(reports, new MessageDraft(MessageKind.EscortOutcome, RecipientKind.Authority, default, new FactKey(SubjectKind.Order, local.OrderMessageId, AttributeKind.DetentionStatus),
                new FactValue(FactValueKind.Text, text: outcome), observed, relatedMessageId: local.OrderMessageId, operationKey: "escort-result/" + local.OrderMessageId + "/" + outcome));
            RequestReportDecision(reports, processingTick);
            Emit("GuardOutcomeQueued", actors[actor], messageId: local.OrderMessageId, detail: outcome);
        }
        private void GuardOutcomePosted(Actor actor, MessageSnapshot message)
        {
            var d = message.Draft;
            if (d.Kind != MessageKind.EscortOutcome || !localGuardOutcomes.TryGetValue((d.RelatedMessageId, d.Claim.Text), out var local) || local.ActorId != actor.Id.Value || local.MessageId != 0 ||
                local.ObservedTick != d.ObservedTick || d.OperationKey != "escort-result/" + local.OrderMessageId + "/" + local.Outcome) return;
            local.MessageId = message.Id;
        }
        private bool ReceiveGuardOutcome(MessageSnapshot message)
        {
            var d = message.Draft; if (d.Kind != MessageKind.EscortOutcome) return true;
            if (!guardAssignments.TryGetValue(d.RelatedMessageId, out var assignment) || message.PhysicalSender.Value != assignment.ActorId || message.SenderAccount.Value != registrations[assignment.ActorId].AccountId ||
                message.TerminalId < 1 || message.SessionId < 1 || message.SubmittedTick < messages[d.RelatedMessageId].DeliveryTick || d.ObservedTick < messages[d.RelatedMessageId].SubmittedTick ||
                d.ObservedTick > message.SubmittedTick || !d.Subject.Equals(messages[d.RelatedMessageId].Draft.Subject))
            { Emit("GuardOutcomeRejected", messageId: message.Id, detail: "OrderAccountOrTransportMismatch"); return false; }
            var key = (d.RelatedMessageId, d.Claim.Text); if (receivedGuardOutcomes.ContainsKey(key)) return false;
            var outcome = new GuardOutcomeSnapshot { OrderMessageId = assignment.OrderMessageId, ActorId = assignment.ActorId, Outcome = d.Claim.Text, ObservedTick = d.ObservedTick, MessageId = message.Id, ReceivedTick = processingTick };
            receivedGuardOutcomes.Add(key, outcome);
            records.Append(RecordKind.GuardOutcome, message.Id, message.SenderAccount.Value, processingTick, d.OperationKey, new[] { Number(AttributeKind.ActorId, assignment.ActorId),
                Number(AttributeKind.AuthorizationId, assignment.AuthorizationId), Number(AttributeKind.GuardOrderMessageId, assignment.OrderMessageId), Number(AttributeKind.MessageId, message.Id), Number(AttributeKind.ObservedTick, d.ObservedTick), Text(AttributeKind.WorkStatus, d.Claim.Text) });
            var dispatch = escortDispatches[assignment.AuthorizationId];
            if (GuardOutcomePolicy.NeedsAttention(dispatch.OrderMessageIds.Contains(assignment.OrderMessageId), DispatchProgress(dispatch.Status)))
            { dispatch.Status = EscortDispatchStatus.NeedsAttention; dispatch.Reason = "GuardReported/" + d.Claim.Text; }
            Emit("GuardOutcomeReceived", messageId: message.Id, detail: "authorization=" + assignment.AuthorizationId + "; " + d.Claim.Text);
            var authorization = detentionAuthorizations[assignment.AuthorizationId];
            if (GuardOutcomePolicy.RequestReplacementTeam(DispatchProgress(dispatch.Status), dispatch.OrderMessageIds.Select(id => receivedGuardOutcomes.Values.Where(o => o.OrderMessageId == id).Select(o => o.Outcome)),
                authorization.Status == DetentionAuthorizationStatus.Authorized && authorization.Expires > processingTick))
            {
                dispatch.Status = EscortDispatchStatus.AwaitingGuards; dispatch.Reason = "AwaitingReplacementTeam";
                Emit("EscortReplacementRequested", messageId: message.Id, detail: "authorization=" + assignment.AuthorizationId);
                RequestEscortReview(processingTick);
            }
            RequestIncidentReview(processingTick); return false;
        }
        private bool GuardReportedFinished(long actor, long authorization, long tick) => records.HistoryOf(RecordKind.GuardOutcome).Any(e => e.Tick <= tick &&
            RecordNumber(e, AttributeKind.ActorId) == actor && RecordNumber(e, AttributeKind.AuthorizationId) == authorization && GuardOutcomeTerminal(RecordText(e, AttributeKind.WorkStatus)));
        private static bool ValidGuardOutcomeDraft(MessageDraft d)
        {
            if (!(d.Kind is MessageKind.EscortOutcome or MessageKind.EscortRevoked)) return true;
            if (d.WorkOrder != null || d.EscortOrder != null || d.Roster != null || d.PatrolOrder != null || d.InspectionReport != null || d.CaseSubmission != null || d.CaseDecision != null || d.DetentionRequest != null || d.DetentionDecision != null ||
                d.TaskId != 0 || d.AttemptId != 0 || d.RelatedMessageId < 1 || d.Claim.Kind != FactValueKind.Text || !d.Subject.Equals(new FactKey(SubjectKind.Order, d.RelatedMessageId, AttributeKind.DetentionStatus))) return false;
            return d.Kind == MessageKind.EscortOutcome ? d.RecipientKind == RecipientKind.Authority && ValidGuardOutcome(d.Claim.Text) && d.OperationKey == "escort-result/" + d.RelatedMessageId + "/" + d.Claim.Text :
                d.RecipientKind == RecipientKind.Account && d.Claim.Text == "AuthorizationRevoked" && d.OperationKey == "escort-revoked/" + d.RelatedMessageId;
        }
    }
}
