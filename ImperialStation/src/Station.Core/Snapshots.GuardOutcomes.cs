using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteGuardOutcomes(BinaryWriter w)
        {
            w.Write(detentionRevocations.Count);
            foreach (var v in detentionRevocations.Values)
            {
                w.Write(v.AuthorizationId); w.Write(v.Tick); w.Write(v.OperationKey); w.Write(v.Applied);
                w.Write(v.Notices.Count); foreach (long id in v.Notices) w.Write(id);
            }
            w.Write(knownGuardRevocations.Count);
            foreach (var v in knownGuardRevocations.Values) { w.Write(v.OrderMessageId); w.Write(v.LearnedTick); w.Write(v.MessageId); w.Write(v.ServiceActionId); w.Write(v.FacilityId); }
            foreach (var outcomes in new[] { localGuardOutcomes, receivedGuardOutcomes })
            {
                w.Write(outcomes.Count);
                foreach (var o in outcomes.Values) { w.Write(o.OrderMessageId); w.Write(o.ActorId); w.Write(o.Outcome); w.Write(o.ObservedTick); w.Write(o.MessageId); w.Write(o.ReceivedTick); }
            }
        }
        private void ReadGuardOutcomes(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var v = new DetentionRevocationSnapshot { AuthorizationId = r.ReadInt64(), Tick = r.ReadInt64(), OperationKey = r.ReadString(), Applied = r.ReadBoolean() };
                var notices = new long[Count(r)]; for (int j = 0; j < notices.Length; j++) notices[j] = r.ReadInt64(); v.Notices = notices;
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.RevokeDetention && e.Value == v.AuthorizationId).ToArray();
                if (v.AuthorizationId < 1 || v.Tick < 0 || string.IsNullOrWhiteSpace(v.OperationKey) || v.Applied != (v.Tick <= LastProcessedTick) ||
                    timers.Length != (v.Applied ? 0 : 1) || timers.Any(e => e.Tick != v.Tick || e.Phase != EventPhase.Disturbance) || notices.Distinct().Count() != notices.Length) throw new ArgumentException("Invalid revocation command or timer.");
                if (v.Applied)
                {
                    if (!detentionAuthorizations.TryGetValue(v.AuthorizationId, out var authorization)) throw new ArgumentException("Revocation has no issued authorization.");
                    bool revoked = authorization.Status == DetentionAuthorizationStatus.Revoked;
                    if (revoked ? authorization.RevokedTick != v.Tick : authorization.Expires > v.Tick) throw new ArgumentException("Revocation disagrees with authorization history.");
                    long[] orders = revoked && escortDispatches.TryGetValue(v.AuthorizationId, out var dispatch) ? dispatch.OrderMessageIds.ToArray() : Array.Empty<long>();
                    if (notices.Length != orders.Length) throw new ArgumentException("Missing individual revocation notices.");
                    for (int j = 0; j < notices.Length; j++)
                    {
                        if (!messages.TryGetValue(notices[j], out var m) || m.Draft.Kind != MessageKind.EscortRevoked || !ValidDraft(m.Draft) || m.Seeded || m.SenderAccount.Value != 0 || m.PhysicalSender.Value != 0 ||
                            m.SubmittedTick != v.Tick || m.Draft.ObservedTick != v.Tick || m.Draft.RelatedMessageId != orders[j] || m.Draft.Recipient != messages[orders[j]].Draft.Recipient) throw new ArgumentException("Invalid revocation notice transport.");
                    }
                }
                else if (notices.Length != 0) throw new ArgumentException("Unapplied revocation already has notices.");
                detentionRevocations.Add(v.AuthorizationId, v); revocationKeys.Add(v.OperationKey, v.AuthorizationId);
            }
            if (scheduled.Any(e => e.Kind == ScheduledKind.RevokeDetention && !detentionRevocations.ContainsKey(e.Value)) ||
                detentionAuthorizations.Values.Any(a => a.Status == DetentionAuthorizationStatus.Revoked && !detentionRevocations.ContainsKey(a.Id)) ||
                messages.Values.Any(m => m.Draft.Kind == MessageKind.EscortRevoked && !detentionRevocations.Values.Any(v => v.Notices.Contains(m.Id)))) throw new ArgumentException("Unregistered revocation state.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var v = new KnownGuardRevocationSnapshot { OrderMessageId = r.ReadInt64(), LearnedTick = r.ReadInt64(), MessageId = r.ReadInt64(), ServiceActionId = r.ReadInt64(), FacilityId = r.ReadInt64() };
                if (!localGuardOrders.TryGetValue(v.OrderMessageId, out var local) || local.Status != GuardOrderStatus.Revoked || local.Reason != "KnownAuthorizationRevoked" || v.LearnedTick < local.ReadTick || v.LearnedTick > LastProcessedTick ||
                    !detentionAuthorizations.TryGetValue(local.Payload.AuthorizationId, out var authorization) || authorization.Status != DetentionAuthorizationStatus.Revoked || v.LearnedTick < authorization.RevokedTick) throw new ArgumentException("Invalid private revocation proof.");
                if (v.MessageId != 0)
                {
                    if (v.ServiceActionId != 0 || v.FacilityId != 0 || !messages.TryGetValue(v.MessageId, out var notice) || notice.Draft.Kind != MessageKind.EscortRevoked || notice.Draft.RelatedMessageId != local.OrderMessageId ||
                        notice.Stage != MessageStage.Read || notice.FirstReadTick != v.LearnedTick || notice.Draft.Recipient.Value != registrations[local.ActorId].AccountId) throw new ArgumentException("Private revocation lacks its own completed read.");
                }
                else if (v.ServiceActionId < 1 || v.ServiceActionId >= nextAction || v.FacilityId != local.Payload.Dispatch.FacilityId || !detentionFacilities.TryGetValue(v.FacilityId, out var facility) ||
                    facility.Definition.Position != local.Payload.Dispatch.Destination || !escorts.TryGetValue(local.Payload.DispatchId, out var group) || group.StartedTick > v.LearnedTick || local.Payload.Leader != local.ActorId)
                    throw new ArgumentException("Private revocation lacks a local intake service observation.");
                knownGuardRevocations.Add(v.OrderMessageId, v);
            }
            if (localGuardOrders.Values.Any(l => l.Status == GuardOrderStatus.Revoked && !knownGuardRevocations.ContainsKey(l.OrderMessageId))) throw new ArgumentException("Private revocation has no learning event.");
            foreach (bool received in new[] { false, true })
            {
                count = Count(r);
                for (int i = 0; i < count; i++)
                {
                    var o = new GuardOutcomeSnapshot { OrderMessageId = r.ReadInt64(), ActorId = r.ReadInt64(), Outcome = r.ReadString(), ObservedTick = r.ReadInt64(), MessageId = r.ReadInt64(), ReceivedTick = r.ReadInt64() };
                    if (!guardAssignments.TryGetValue(o.OrderMessageId, out var assignment) || assignment.ActorId != o.ActorId || !ValidGuardOutcome(o.Outcome) || o.ObservedTick < messages[o.OrderMessageId].SubmittedTick || o.ObservedTick > LastProcessedTick) throw new ArgumentException("Invalid guard outcome identity.");
                    MessageSnapshot? posted = null;
                    if (o.MessageId > 0)
                    {
                        if (!messages.TryGetValue(o.MessageId, out posted) || !ValidDraft(posted.Draft) || posted.Draft.Kind != MessageKind.EscortOutcome || posted.Draft.RelatedMessageId != o.OrderMessageId || posted.Draft.Claim.Text != o.Outcome ||
                            posted.Draft.ObservedTick != o.ObservedTick || posted.SubmittedTick < o.ObservedTick || posted.SubmittedTick < messages[o.OrderMessageId].DeliveryTick || posted.PhysicalSender.Value != o.ActorId ||
                            posted.SenderAccount.Value != registrations[o.ActorId].AccountId || posted.TerminalId < 1 || posted.SessionId < 1) throw new ArgumentException("Invalid guard outcome transport.");
                    }
                    if (received)
                    {
                        if (posted == null || posted.Stage != MessageStage.Accepted || o.ReceivedTick != posted.DeliveryTick || o.ReceivedTick > LastProcessedTick) throw new ArgumentException("Guard outcome was not received.");
                        var row = records.History.Values.SingleOrDefault(e => e.Kind == RecordKind.GuardOutcome && e.EntityId == o.MessageId);
                        if (row == null || row.Tick != o.ReceivedTick || row.AccountId != posted.SenderAccount.Value || RecordNumber(row, AttributeKind.ActorId) != o.ActorId || RecordNumber(row, AttributeKind.AuthorizationId) != assignment.AuthorizationId ||
                            RecordNumber(row, AttributeKind.GuardOrderMessageId) != o.OrderMessageId || RecordNumber(row, AttributeKind.MessageId) != o.MessageId || RecordNumber(row, AttributeKind.ObservedTick) != o.ObservedTick || RecordText(row, AttributeKind.WorkStatus) != o.Outcome)
                            throw new ArgumentException("Guard outcome receipt lacks its formal record.");
                    }
                    else
                    {
                        if (o.ReceivedTick != -1 || !localGuardOrders.TryGetValue(o.OrderMessageId, out var local) || o.ObservedTick < local.ReadTick) throw new ArgumentException("Invalid private outcome owner.");
                        if (posted == null && (!reportAgents.TryGetValue(new Id<ActorTag>(o.ActorId), out var agent) || !agent.Pending.Any(d => d.Kind == MessageKind.EscortOutcome && ValidDraft(d) && d.RelatedMessageId == o.OrderMessageId && d.Claim.Text == o.Outcome && d.ObservedTick == o.ObservedTick))) throw new ArgumentException("Missing queued guard report.");
                        if (o.Outcome == "AuthorizationRevoked" && (!knownGuardRevocations.TryGetValue(o.OrderMessageId, out var known) || known.LearnedTick != o.ObservedTick) ||
                            o.Outcome == "KnownOrderExpired" && o.ObservedTick != Math.Max(local.ReadTick, local.Payload.Expires) ||
                            o.Outcome == "TargetNotLocated" && (local.SearchObservations.Count != local.Payload.Candidates.Count || local.SearchObservations.Any(s => s.Found) || local.SearchObservations.Last().Tick != o.ObservedTick) ||
                            o.Outcome == "EscortInterrupted" && (!escorts.TryGetValue(local.Payload.DispatchId, out var escort) || escort.Status != EscortStatus.Interrupted || escort.BoundaryTick != o.ObservedTick)) throw new ArgumentException("Private guard report contradicts its local evidence.");
                    }
                    (received ? receivedGuardOutcomes : localGuardOutcomes).Add((o.OrderMessageId, o.Outcome), o);
                }
            }
            if (records.History.Values.Any(e => e.Kind == RecordKind.GuardOutcome && !receivedGuardOutcomes.Values.Any(o => o.MessageId == e.EntityId)) ||
                messages.Values.Any(m => m.Draft.Kind == MessageKind.EscortOutcome && !ValidDraft(m.Draft))) throw new ArgumentException("Unregistered guard outcome record or invalid envelope.");
        }
    }
}
