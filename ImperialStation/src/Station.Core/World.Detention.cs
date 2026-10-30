using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public DetentionPolicy? Detention { get; private set; }
        private readonly SortedDictionary<long, LocalDetentionRequestSnapshot> localDetentionRequests = new SortedDictionary<long, LocalDetentionRequestSnapshot>();
        private readonly SortedDictionary<long, DetentionRequestSnapshot> detentionRequests = new SortedDictionary<long, DetentionRequestSnapshot>();
        private readonly SortedDictionary<string, long> detentionRequestKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<long, DetentionAuthorizationSnapshot> detentionAuthorizations = new SortedDictionary<long, DetentionAuthorizationSnapshot>();
        private readonly SortedDictionary<long, long> activeDetentionAuthorizations = new SortedDictionary<long, long>();
        private long nextDetentionAuthorization = 1;
        public IReadOnlyList<LocalDetentionRequestSnapshot> InspectLocalDetentionRequests(Id<ActorTag> actor) => localDetentionRequests.Values.Where(r => r.ActorId == actor.Value).Select(r => r.Copy()).ToArray();
        public IReadOnlyList<DetentionRequestSnapshot> InspectDetentionRequests() => detentionRequests.Values.Select(r => r.Copy()).ToArray();
        public IReadOnlyList<DetentionAuthorizationSnapshot> InspectDetentionAuthorizations() => detentionAuthorizations.Values.Select(o => o.Copy()).ToArray();
        private void InitializeDetention(DetentionPolicy policy)
        {
            if (Personnel == null || Authority == null) throw new ArgumentException("Detention approval requires personnel and authority services.");
            if (policy.Escort != null && (!Topology.Contains(policy.Escort.Rendezvous) || !Topology.Contains(policy.Escort.Destination) || !resources.ContainsKey(policy.Escort.RequiredEquipment)))
                throw new ArgumentException("Escort dispatch requires declared destination tiles and equipment.");
            Detention = policy;
            foreach (var definition in policy.Facilities)
            {
                if (!Topology.Contains(definition.Position)) throw new ArgumentException("Detention facility requires a completed physical tile.");
                detentionFacilities.Add(definition.Id, new DetentionFacilitySnapshot { Definition = definition, Powered = definition.Powered, Connected = definition.Connected });
            }
        }
        private void ReadDetentionProtocol(Actor actor, MessageSnapshot message)
        {
            if (message.SenderAccount.Value != 0) return;
            var decision = message.Draft.DetentionDecision;
            if (message.Draft.Kind == MessageKind.DetentionDecision && decision != null)
            {
                var local = localDetentionRequests.Values.FirstOrDefault(r => r.ActorId == actor.Id.Value && r.RequestMessageId == decision.RequestMessageId && r.Status == DetentionRequestStatus.Posted);
                if (local == null) return;
                local.ReplyMessageId = message.Id; local.ReadTick = processingTick; local.AuthorizationId = decision.AuthorizationId; local.Expires = decision.Expires; local.Reason = decision.Reason;
                local.Status = decision.State == DetentionDecisionState.Approved ? DetentionRequestStatus.Approved : decision.State == DetentionDecisionState.Waiting ? DetentionRequestStatus.Waiting : DetentionRequestStatus.Rejected;
                Emit("DetentionDecisionRead", actor, messageId: message.Id, detail: decision.State + "; " + decision.Reason); return;
            }
            var c = message.Draft.CaseDecision;
            if (message.Draft.Kind != MessageKind.CaseDecision || c == null || !DetentionDecisions.ShouldRequestDetention(c, publicKnowledge?.Personnel, actor.Role) || localDetentionRequests.ContainsKey(message.Id)) return;
            var request = new LocalDetentionRequestSnapshot { CaseReplyMessageId = message.Id, ActorId = actor.Id.Value, AccountId = c.AccountId, CaseId = c.CaseId };
            localDetentionRequests.Add(message.Id, request);
            var payload = new DetentionRequestPayload(c.AccountId, c.CaseId, message.Id);
            var draft = new MessageDraft(MessageKind.RequestDetention, RecipientKind.Authority, default, new FactKey(SubjectKind.Order, c.CaseId, AttributeKind.DetentionStatus),
                new FactValue(FactValueKind.Text, text: "RequestDetention"), processingTick, relatedMessageId: message.Id, operationKey: "request-detention/" + message.Id, detentionRequest: payload);
            if (!reportAgents.TryGetValue(actor.Id, out var agent)) { agent = new ReportAgent { Actor = actor.Id, Backoff = planner.InitialBackoff }; reportAgents.Add(actor.Id, agent); }
            QueueReport(agent, draft); RequestReportDecision(agent, processingTick);
            Emit("DetentionRequestQueued", actor, messageId: message.Id, detail: "case=" + c.CaseId + "; read-count=" + c.QualifyingCount);
        }
        private void DetentionMessagePosted(Actor actor, MessageSnapshot message)
        {
            var payload = message.Draft.DetentionRequest;
            if (message.Draft.Kind != MessageKind.RequestDetention || payload == null || !localDetentionRequests.TryGetValue(payload.CaseReplyMessageId, out var local) || local.ActorId != actor.Id.Value || local.Status != DetentionRequestStatus.Queued) return;
            local.Status = DetentionRequestStatus.Posted; local.RequestMessageId = message.Id;
        }
        private string ValidateDetentionRequest(MessageSnapshot message)
        {
            var p = message.Draft.DetentionRequest!;
            if (Personnel == null) return "PersonnelUnavailable";
            if (RegisteredSender(message)?.Role != Personnel.SupervisorRole || message.SubmittedTick < 0 || message.TerminalId < 1 || message.SessionId < 1) return "RequesterPermissionDenied";
            if (!messages.TryGetValue(p.CaseReplyMessageId, out var source) || source.Draft.Kind != MessageKind.CaseDecision || source.SenderAccount.Value != 0 || source.Draft.Recipient != message.SenderAccount ||
                source.DeliveryTick > message.SubmittedTick || source.Draft.CaseDecision?.Accepted != true || source.Draft.CaseDecision.CaseId != p.CaseId || source.Draft.CaseDecision.AccountId != p.AccountId ||
                !caseReplies.TryGetValue(source.Draft.CaseDecision.FilingMessageId, out long issuedReply) || issuedReply != source.Id ||
                !cases.TryGetValue(p.CaseId, out var c) || c.Submission.AccountId != p.AccountId || c.AcceptedTick > message.SubmittedTick) return "InvalidAcceptedCaseEvidence";
            return "";
        }
        private bool ReceiveDetentionRequest(MessageSnapshot message, long cause)
        {
            if (message.Draft.Kind != MessageKind.RequestDetention) return true;
            var p = message.Draft.DetentionRequest!; string key = message.SenderAccount.Value + "/" + message.Draft.OperationKey;
            string reason = ValidateDetentionRequest(message); var state = DetentionDecisionState.Rejected; long authorization = 0, expires = 0;
            long validationVersion = records.Version; int count = QualifyingReprimandCount(p.AccountId, processingTick);
            DetentionDecisionPayload? prior = null; bool priorSame = false, priorReleased = false, priorRevoked = false; long priorExpires = 0;
            if (detentionRequestKeys.TryGetValue(key, out long priorId))
            {
                prior = detentionRequests[priorId].Decision; priorSame = SameDraft(messages[priorId].Draft, message.Draft);
                if (prior.State == DetentionDecisionState.Approved)
                {
                    priorExpires = detentionAuthorizations[prior.AuthorizationId].Expires;
                    priorReleased = ReleaseWasReceived(prior.AuthorizationId, processingTick, records.Version); priorRevoked = RevocationWasKnownToAuthority(prior.AuthorizationId, processingTick, records.Version);
                }
            }
            bool hasActive = activeDetentionAuthorizations.TryGetValue(p.AccountId, out long active);
            var choice = DetentionDecisions.Decide(new DetentionRequestFacts(processingTick, p.AccountId, reason, count, Personnel?.DetentionThreshold ?? 0, prior, priorSame, priorExpires, priorReleased, priorRevoked,
                Detention != null && Authority != null, hasActive, active, hasActive ? detentionAuthorizations[active].Expires : 0, registrations.Values));
            state = choice.State; reason = choice.Reason;
            if (choice.Authorization is DetentionAuthorizationChoice.ReusePrior or DetentionAuthorizationChoice.ReuseActive) { authorization = choice.AuthorizationId; expires = choice.Expires; }
            else if (choice.Authorization == DetentionAuthorizationChoice.IssueNew)
            {
                expires = checked(processingTick + Detention!.OrderTicks); long followingId = checked(nextDetentionAuthorization + 1);
                authorization = nextDetentionAuthorization; nextDetentionAuthorization = followingId;
                var order = new DetentionAuthorizationSnapshot { Id = authorization, AccountId = p.AccountId, ActorId = choice.SubjectActorId, RequestMessageId = message.Id, CaseId = p.CaseId,
                    IssuedTick = processingTick, Expires = expires, SentenceTicks = Detention.SentenceTicks, CountThroughCaseId = nextCase - 1, CountThroughRecordVersion = validationVersion, JustifyingCases = QualifyingReprimandCases(p.AccountId, processingTick) };
                detentionAuthorizations.Add(order.Id, order); activeDetentionAuthorizations[p.AccountId] = order.Id; RecordDetentionAuthorization(order);
                Schedule(new ScheduledEvent { Tick = expires, Phase = EventPhase.Decision, Kind = ScheduledKind.ExpireDetentionAuthorization, Value = order.Id });
                OpenEscortDispatch(order.Id);
                Emit("DetentionAuthorized", messageId: message.Id, causeId: cause, detail: "authorization=" + order.Id + "; account=" + p.AccountId + "; known-actor=" + order.ActorId);
            }
            if (!detentionRequestKeys.ContainsKey(key)) detentionRequestKeys.Add(key, message.Id);
            var result = new DetentionDecisionPayload(message.Id, p.AccountId, authorization, state, reason, count, expires, nextCase - 1);
            var request = new DetentionRequestSnapshot { MessageId = message.Id, ReceivedTick = processingTick, ValidationRecordVersion = validationVersion, Decision = result }; detentionRequests.Add(message.Id, request);
            bool registered = RegisteredAccount(message.SenderAccount);
            if (registered)
                request.ReplyMessageId = PostAuthorityWork(new MessageDraft(MessageKind.DetentionDecision, RecipientKind.Account, message.SenderAccount, message.Draft.Subject,
                    new FactValue(FactValueKind.Text, text: reason), processingTick, relatedMessageId: message.Id, operationKey: "detention-reply/" + message.Id, detentionDecision: result), cause);
            if (message.SenderAccount.Value > 0 && registered)
                records.Append(RecordKind.DetentionRequest, message.Id, message.SenderAccount.Value, processingTick, "detention-request/" + message.Id, new[] {
                    Number(AttributeKind.ActorId, message.PhysicalSender.Value), Number(AttributeKind.SubjectAccountId, p.AccountId), Number(AttributeKind.CaseId, p.CaseId), Number(AttributeKind.AuthorizationId, authorization),
                    Number(AttributeKind.QualifyingCount, count), Text(AttributeKind.DetentionStatus, state.ToString()), Text(AttributeKind.WorkStatus, reason) });
            Emit("DetentionRequestDecided", messageId: message.Id, causeId: cause, detail: state + "; " + reason + "; count=" + count); return false;
        }
        private void RecordDetentionAuthorization(DetentionAuthorizationSnapshot order)
        {
            records.Append(RecordKind.DetentionAuthorization, order.Id, order.AccountId, processingTick, "detention-authorization/" + order.Id + "/" + order.Status,
                new[] { Number(AttributeKind.ActorId, order.ActorId), Number(AttributeKind.MessageId, order.RequestMessageId), Number(AttributeKind.CaseId, order.CaseId), Number(AttributeKind.Expires, order.Expires),
                    Number(AttributeKind.SentenceTicks, order.SentenceTicks), Text(AttributeKind.DetentionStatus, order.Status.ToString()), Text(AttributeKind.JustifyingCases, string.Join(",", order.JustifyingCases)) });
        }
        private void ExpireDetentionAuthorization(long id)
        {
            var order = detentionAuthorizations[id]; if (order.Status != DetentionAuthorizationStatus.Authorized) return;
            order.Status = DetentionAuthorizationStatus.Expired;
            if (activeDetentionAuthorizations.TryGetValue(order.AccountId, out long active) && active == id) activeDetentionAuthorizations.Remove(order.AccountId);
            RecordDetentionAuthorization(order); Emit("DetentionAuthorizationExpired", messageId: order.RequestMessageId, detail: "authorization=" + id);
            ExpireEscortDispatch(id);
        }
        private static bool SameDetentionPayloads(MessageDraft a, MessageDraft b) =>
            (a.DetentionRequest == null ? b.DetentionRequest == null : b.DetentionRequest != null && a.DetentionRequest.Same(b.DetentionRequest)) &&
            (a.DetentionDecision == null ? b.DetentionDecision == null : b.DetentionDecision != null && a.DetentionDecision.Same(b.DetentionDecision));
        private static bool ValidDetentionDraft(MessageDraft d)
        {
            if (d.DetentionRequest != null && d.Kind != MessageKind.RequestDetention || d.DetentionDecision != null && d.Kind != MessageKind.DetentionDecision) return false;
            if (!(d.Kind is MessageKind.RequestDetention or MessageKind.DetentionDecision)) return true;
            if (d.WorkOrder != null || d.Roster != null || d.PatrolOrder != null || d.InspectionReport != null || d.CaseSubmission != null || d.CaseDecision != null || d.TaskId != 0 || d.AttemptId != 0 || string.IsNullOrWhiteSpace(d.OperationKey)) return false;
            if (d.Kind == MessageKind.RequestDetention)
                return d.DetentionRequest != null && d.RecipientKind == RecipientKind.Authority && d.RelatedMessageId == d.DetentionRequest.CaseReplyMessageId &&
                    d.Subject.Equals(new FactKey(SubjectKind.Order, d.DetentionRequest.CaseId, AttributeKind.DetentionStatus)) && d.Claim.Equals(new FactValue(FactValueKind.Text, text: "RequestDetention"));
            return d.DetentionDecision != null && d.RecipientKind == RecipientKind.Account && d.RelatedMessageId == d.DetentionDecision.RequestMessageId && d.Subject.Attribute == AttributeKind.DetentionStatus && d.Claim.Equals(new FactValue(FactValueKind.Text, text: d.DetentionDecision.Reason));
        }
    }
}
