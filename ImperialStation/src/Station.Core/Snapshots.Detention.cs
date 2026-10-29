using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteDetentionRequestPayload(BinaryWriter w, DetentionRequestPayload? p)
        { w.Write(p != null); if (p == null) return; w.Write(p.AccountId); w.Write(p.CaseId); w.Write(p.CaseReplyMessageId); }
        private static DetentionRequestPayload? ReadDetentionRequestPayload(BinaryReader r) => r.ReadBoolean() ? new DetentionRequestPayload(r.ReadInt64(), r.ReadInt64(), r.ReadInt64()) : null;
        private static void WriteDetentionDecisionPayload(BinaryWriter w, DetentionDecisionPayload? p)
        { w.Write(p != null); if (p == null) return; w.Write(p.RequestMessageId); w.Write(p.AccountId); w.Write(p.AuthorizationId); w.Write((int)p.State); w.Write(p.Reason); w.Write(p.QualifyingCount); w.Write(p.Expires); w.Write(p.CountThroughCaseId); }
        private static DetentionDecisionPayload? ReadDetentionDecisionPayload(BinaryReader r) => r.ReadBoolean() ? new DetentionDecisionPayload(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), ReadEnum<DetentionDecisionState>(r), r.ReadString(), r.ReadInt32(), r.ReadInt64(), r.ReadInt64()) : null;
        private void WriteDetention(BinaryWriter w)
        {
            w.Write(Detention != null); if (Detention != null) { w.Write(Detention.SentenceTicks); w.Write(Detention.OrderTicks); WriteEscortPolicy(w, Detention.Escort); WriteDetentionFacilityDefinitions(w, Detention.Facilities); }
            w.Write(nextDetentionAuthorization); w.Write(detentionAuthorizations.Count);
            foreach (var o in detentionAuthorizations.Values)
            {
                w.Write(o.Id); w.Write(o.AccountId); w.Write(o.ActorId); w.Write(o.RequestMessageId); w.Write(o.CaseId); w.Write(o.IssuedTick); w.Write(o.Expires); w.Write(o.SentenceTicks); w.Write(o.CountThroughCaseId); w.Write(o.CountThroughRecordVersion); w.Write((int)o.Status); w.Write(o.RevokedTick);
                w.Write(o.JustifyingCases.Count); foreach (long id in o.JustifyingCases) w.Write(id);
            }
            w.Write(detentionRequestKeys.Count); foreach (var p in detentionRequestKeys) { w.Write(p.Key); w.Write(p.Value); }
            w.Write(detentionRequests.Count); foreach (var request in detentionRequests.Values) { w.Write(request.MessageId); w.Write(request.ReceivedTick); w.Write(request.ValidationRecordVersion); w.Write(request.ReplyMessageId); WriteDetentionDecisionPayload(w, request.Decision); }
            w.Write(localDetentionRequests.Count);
            foreach (var l in localDetentionRequests.Values)
            { w.Write(l.CaseReplyMessageId); w.Write(l.ActorId); w.Write(l.AccountId); w.Write(l.CaseId); w.Write((int)l.Status); w.Write(l.RequestMessageId); w.Write(l.ReplyMessageId); w.Write(l.ReadTick); w.Write(l.AuthorizationId); w.Write(l.Expires); w.Write(l.Reason); }
        }
        private void ReadDetention(BinaryReader r)
        {
            if (r.ReadBoolean()) InitializeDetention(new DetentionPolicy(r.ReadInt64(), r.ReadInt64(), ReadEscortPolicy(r), ReadDetentionFacilityDefinitions(r)));
            nextDetentionAuthorization = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var o = new DetentionAuthorizationSnapshot { Id = r.ReadInt64(), AccountId = r.ReadInt64(), ActorId = r.ReadInt64(), RequestMessageId = r.ReadInt64(), CaseId = r.ReadInt64(),
                    IssuedTick = r.ReadInt64(), Expires = r.ReadInt64(), SentenceTicks = r.ReadInt64(), CountThroughCaseId = r.ReadInt64(), CountThroughRecordVersion = r.ReadInt64(), Status = ReadEnum<DetentionAuthorizationStatus>(r), RevokedTick = r.ReadInt64() };
                int n = Count(r); var justifications = new long[n]; for (int j = 0; j < n; j++) justifications[j] = r.ReadInt64(); o.JustifyingCases = justifications;
                if (Detention == null || Personnel == null || o.Id < 1 || o.Id >= nextDetentionAuthorization || o.IssuedTick < 0 || o.IssuedTick > LastProcessedTick || o.Expires != checked(o.IssuedTick + Detention.OrderTicks) || o.SentenceTicks != Detention.SentenceTicks ||
                    o.CountThroughRecordVersion < 0 || o.CountThroughRecordVersion > records.Version || o.CountThroughCaseId < 1 || o.CountThroughCaseId >= nextCase || !justifications.SequenceEqual(QualifyingReprimandCases(o.AccountId, o.IssuedTick, o.CountThroughCaseId, o.CountThroughRecordVersion)) || justifications.Length < Personnel.DetentionThreshold ||
                    !registrations.TryGetValue(o.ActorId, out var subject) || subject.AccountId != o.AccountId || registrations.Values.Count(p => p.AccountId == o.AccountId) != 1 ||
                    !messages.TryGetValue(o.RequestMessageId, out var request) || request.Draft.Kind != MessageKind.RequestDetention || request.Stage != MessageStage.Accepted || request.DeliveryTick != o.IssuedTick ||
                    request.Draft.DetentionRequest?.AccountId != o.AccountId || request.Draft.DetentionRequest.CaseId != o.CaseId || ValidateDetentionRequest(request).Length != 0) throw new ArgumentException("Invalid detention authorization evidence.");
                var history = records.History.Values.Where(e => e.Kind == RecordKind.DetentionAuthorization && e.EntityId == o.Id).ToArray();
                int expected = o.Status == DetentionAuthorizationStatus.Authorized ? 1 : 2;
                if (history.Length != expected || history.Any(e => e.AccountId != o.AccountId || RecordNumber(e, AttributeKind.ActorId) != o.ActorId || RecordNumber(e, AttributeKind.MessageId) != o.RequestMessageId || RecordNumber(e, AttributeKind.CaseId) != o.CaseId ||
                    RecordNumber(e, AttributeKind.Expires) != o.Expires || RecordNumber(e, AttributeKind.SentenceTicks) != o.SentenceTicks || RecordText(e, AttributeKind.JustifyingCases) != string.Join(",", justifications)) ||
                    history[0].Tick != o.IssuedTick || RecordText(history[0], AttributeKind.DetentionStatus) != "Authorized" || expected == 2 && (history[1].Tick != (o.Status == DetentionAuthorizationStatus.Revoked ? o.RevokedTick : o.Expires) || RecordText(history[1], AttributeKind.DetentionStatus) != o.Status.ToString())) throw new ArgumentException("Invalid authorization history.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.ExpireDetentionAuthorization && e.Value == o.Id).ToArray();
                if (timers.Length != (o.Status == DetentionAuthorizationStatus.Authorized ? 1 : 0) || timers.Any(e => e.Phase != EventPhase.Decision || e.Tick != o.Expires) ||
                    RuntimeError == null && (o.Status == DetentionAuthorizationStatus.Authorized && o.Expires <= LastProcessedTick || o.Status == DetentionAuthorizationStatus.Expired && o.Expires > LastProcessedTick) || (o.Status == DetentionAuthorizationStatus.Revoked ? o.RevokedTick < o.IssuedTick || o.RevokedTick >= o.Expires || o.RevokedTick > LastProcessedTick : o.RevokedTick != -1)) throw new ArgumentException("Invalid authorization expiry timer.");
                if (o.Status == DetentionAuthorizationStatus.Authorized && !records.History.Values.Any(e => e.Kind == RecordKind.DetentionRelease && e.EntityId == o.Id)) activeDetentionAuthorizations.Add(o.AccountId, o.Id);
                detentionAuthorizations.Add(o.Id, o);
            }
            if (nextDetentionAuthorization != detentionAuthorizations.Count + 1 || scheduled.Any(e => e.Kind == ScheduledKind.ExpireDetentionAuthorization && !detentionAuthorizations.ContainsKey(e.Value))) throw new ArgumentException("Invalid authorization sequence.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                string key = r.ReadString(); long id = r.ReadInt64();
                if (!messages.TryGetValue(id, out var message) || message.Draft.Kind != MessageKind.RequestDetention || message.Stage != MessageStage.Accepted || key != message.SenderAccount.Value + "/" + message.Draft.OperationKey) throw new ArgumentException("Invalid detention operation key.");
                detentionRequestKeys.Add(key, id);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var request = new DetentionRequestSnapshot { MessageId = r.ReadInt64(), ReceivedTick = r.ReadInt64(), ValidationRecordVersion = r.ReadInt64(), ReplyMessageId = r.ReadInt64(), Decision = ReadDetentionDecisionPayload(r) ?? throw new ArgumentException("Missing detention decision.") }; var d = request.Decision;
                if (!messages.TryGetValue(request.MessageId, out var posted) || posted.Draft.Kind != MessageKind.RequestDetention || posted.Stage != MessageStage.Accepted || request.ReceivedTick != posted.DeliveryTick || request.ReceivedTick > LastProcessedTick ||
                    request.ValidationRecordVersion < 0 || request.ValidationRecordVersion > records.Version || d.RequestMessageId != request.MessageId || d.AccountId != posted.Draft.DetentionRequest!.AccountId || d.CountThroughCaseId >= nextCase || d.QualifyingCount != QualifyingReprimandCases(d.AccountId, request.ReceivedTick, d.CountThroughCaseId, request.ValidationRecordVersion).Length ||
                    !detentionRequestKeys.ContainsKey(posted.SenderAccount.Value + "/" + posted.Draft.OperationKey)) throw new ArgumentException("Invalid detention request decision.");
                bool replyExpected = actors.Values.Any(a => a.Account == posted.SenderAccount);
                if (replyExpected)
                {
                    if (!messages.TryGetValue(request.ReplyMessageId, out var reply) || reply.SenderAccount.Value != 0 || reply.Draft.Kind != MessageKind.DetentionDecision || reply.Draft.Recipient != posted.SenderAccount || reply.Draft.RelatedMessageId != posted.Id ||
                        reply.SubmittedTick != request.ReceivedTick || reply.Draft.DetentionDecision == null || !d.Same(reply.Draft.DetentionDecision)) throw new ArgumentException("Invalid detention reply provenance.");
                    var row = records.History.Values.SingleOrDefault(e => e.Kind == RecordKind.DetentionRequest && e.EntityId == request.MessageId);
                    if (row == null || row.AccountId != posted.SenderAccount.Value || row.Tick != request.ReceivedTick || RecordNumber(row, AttributeKind.ActorId) != posted.PhysicalSender.Value || RecordNumber(row, AttributeKind.SubjectAccountId) != d.AccountId ||
                        RecordNumber(row, AttributeKind.CaseId) != posted.Draft.DetentionRequest.CaseId || RecordNumber(row, AttributeKind.AuthorizationId) != d.AuthorizationId || RecordNumber(row, AttributeKind.QualifyingCount) != d.QualifyingCount ||
                        RecordText(row, AttributeKind.DetentionStatus) != d.State.ToString() || RecordText(row, AttributeKind.WorkStatus) != d.Reason) throw new ArgumentException("Invalid detention decision record.");
                }
                else if (request.ReplyMessageId != 0) throw new ArgumentException("Detention reply has no recipient.");
                detentionRequests.Add(request.MessageId, request);
            }
            if (detentionRequestKeys.Values.Any(id => !detentionRequests.ContainsKey(id)) || detentionAuthorizations.Values.Any(o => !detentionRequests.TryGetValue(o.RequestMessageId, out var q) || q.Decision.AuthorizationId != o.Id || q.Decision.Reason != "Approved") ||
                messages.Values.Any(m => m.Draft.Kind == MessageKind.DetentionDecision && !detentionRequests.Values.Any(q => q.ReplyMessageId == m.Id) || m.Draft.Kind == MessageKind.RequestDetention && m.Stage == MessageStage.Accepted && !detentionRequests.ContainsKey(m.Id)) ||
                records.History.Values.Any(e => e.Kind == RecordKind.DetentionAuthorization && !detentionAuthorizations.ContainsKey(e.EntityId) || e.Kind == RecordKind.DetentionRequest && !detentionRequests.ContainsKey(e.EntityId))) throw new ArgumentException("Unregistered detention decision.");
            foreach (var request in detentionRequests.Values) ValidateSavedDetentionDecision(messages[request.MessageId], request);
            if (messages.Values.Any(m => (m.Draft.Kind is MessageKind.FileCase or MessageKind.CaseDecision or MessageKind.RequestDetention or MessageKind.DetentionDecision || m.Draft.InspectionReport != null) && !ValidDraft(m.Draft))) throw new ArgumentException("Invalid saved institutional envelope.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var l = new LocalDetentionRequestSnapshot { CaseReplyMessageId = r.ReadInt64(), ActorId = r.ReadInt64(), AccountId = r.ReadInt64(), CaseId = r.ReadInt64(), Status = ReadEnum<DetentionRequestStatus>(r), RequestMessageId = r.ReadInt64(), ReplyMessageId = r.ReadInt64(),
                    ReadTick = r.ReadInt64(), AuthorizationId = r.ReadInt64(), Expires = r.ReadInt64(), Reason = r.ReadString() };
                if (Personnel == null || !actors.TryGetValue(new Id<ActorTag>(l.ActorId), out var actor) || !messages.TryGetValue(l.CaseReplyMessageId, out var source) || source.Stage != MessageStage.Read || source.Draft.Recipient != actor.Account ||
                    source.Draft.CaseDecision?.Accepted != true || source.Draft.CaseDecision.AccountId != l.AccountId || source.Draft.CaseDecision.CaseId != l.CaseId || source.Draft.CaseDecision.QualifyingCount < Personnel.DetentionThreshold) throw new ArgumentException("Invalid private detention request source.");
                if (l.Status == DetentionRequestStatus.Queued)
                {
                    if (l.RequestMessageId != 0 || !reportAgents.TryGetValue(actor.Id, out var agent) || !agent.Pending.Any(d => d.Kind == MessageKind.RequestDetention && d.DetentionRequest?.CaseReplyMessageId == source.Id)) throw new ArgumentException("Missing queued detention request.");
                }
                else if (!messages.TryGetValue(l.RequestMessageId, out var posted) || posted.PhysicalSender != actor.Id || posted.Draft.DetentionRequest?.CaseReplyMessageId != source.Id) throw new ArgumentException("Missing posted detention request.");
                if (l.Status is DetentionRequestStatus.Approved or DetentionRequestStatus.Waiting or DetentionRequestStatus.Rejected)
                {
                    if (!messages.TryGetValue(l.ReplyMessageId, out var reply) || reply.Stage != MessageStage.Read || reply.FirstReadTick != l.ReadTick || reply.Draft.DetentionDecision == null || reply.Draft.DetentionDecision.RequestMessageId != l.RequestMessageId ||
                        reply.Draft.DetentionDecision.AuthorizationId != l.AuthorizationId || reply.Draft.DetentionDecision.Expires != l.Expires || reply.Draft.DetentionDecision.Reason != l.Reason || reply.Draft.DetentionDecision.State.ToString() != l.Status.ToString()) throw new ArgumentException("Invalid private detention decision read.");
                }
                else if (l.ReplyMessageId != 0 || l.ReadTick != -1 || l.AuthorizationId != 0 || l.Expires != 0 || l.Reason.Length != 0) throw new ArgumentException("Unread detention decision leaked into private state.");
                localDetentionRequests.Add(l.CaseReplyMessageId, l);
            }
        }
        private void ValidateSavedDetentionDecision(MessageSnapshot message, DetentionRequestSnapshot request)
        {
            var d = request.Decision; string validation = ValidateDetentionRequest(message);
            long first = detentionRequestKeys[message.SenderAccount.Value + "/" + message.Draft.OperationKey];
            if (d.Reason == "IdempotencyConflict") { if (d.State != DetentionDecisionState.Rejected || first == message.Id || SameDraft(messages[first].Draft, message.Draft)) throw new ArgumentException("Invalid detention retry conflict."); return; }
            if (validation.Length > 0)
            { if (d.Reason != validation || d.State != (validation == "PersonnelUnavailable" ? DetentionDecisionState.Waiting : DetentionDecisionState.Rejected)) throw new ArgumentException("Invalid detention evidence rejection."); return; }
            if (d.State == DetentionDecisionState.Approved)
            {
                if (!detentionAuthorizations.TryGetValue(d.AuthorizationId, out var order) || order.AccountId != d.AccountId || order.IssuedTick > request.ReceivedTick || order.Expires <= request.ReceivedTick || order.Expires != d.Expires || RevocationWasKnownToAuthority(order.Id, request.ReceivedTick, request.ValidationRecordVersion) ||
                    order.IssuedTick == request.ReceivedTick && order.RequestMessageId > request.MessageId || !(d.Reason is "Approved" or "DuplicateRequest" or "ExistingAuthorization") ||
                    d.Reason == "Approved" && (order.RequestMessageId != message.Id || order.CountThroughCaseId != d.CountThroughCaseId || order.CountThroughRecordVersion != request.ValidationRecordVersion) || d.Reason != "Approved" && order.RequestMessageId == message.Id ||
                    d.Reason == "DuplicateRequest" && (first == message.Id || !SameDraft(messages[first].Draft, message.Draft) || detentionRequests[first].Decision.AuthorizationId != d.AuthorizationId) ||
                    d.Reason == "ExistingAuthorization" && first != message.Id) throw new ArgumentException("Invalid approved detention decision.");
                return;
            }
            bool valid = d.Reason switch {
                "DetentionPolicyUnavailable" => d.State == DetentionDecisionState.Waiting && Detention == null,
                "AmbiguousSubjectIdentity" => d.State == DetentionDecisionState.Waiting && registrations.Values.Count(p => p.AccountId == d.AccountId) != 1,
                "ThresholdNotMet" => d.State == DetentionDecisionState.Rejected && Personnel != null && d.QualifyingCount < Personnel.DetentionThreshold,
                "PriorRequestClosed" => d.State == DetentionDecisionState.Rejected && first != message.Id && SameDraft(messages[first].Draft, message.Draft) &&
                    (detentionRequests[first].Decision.State == DetentionDecisionState.Rejected || detentionRequests[first].Decision.State == DetentionDecisionState.Approved && (detentionRequests[first].Decision.Expires <= request.ReceivedTick || ReleaseWasReceived(detentionRequests[first].Decision.AuthorizationId, request.ReceivedTick, request.ValidationRecordVersion) || RevocationWasKnownToAuthority(detentionRequests[first].Decision.AuthorizationId, request.ReceivedTick, request.ValidationRecordVersion))),
                _ => false };
            if (!valid) throw new ArgumentException("Invalid detention decision reason.");
        }
    }
}
