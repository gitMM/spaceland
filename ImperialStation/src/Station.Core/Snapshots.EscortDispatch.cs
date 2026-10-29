using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteEscortPolicy(BinaryWriter w, EscortDispatchPolicy? p)
        { w.Write(p != null); if (p == null) return; w.Write(p.FacilityId); WriteCoord(w, p.Destination); WriteCoord(w, p.Rendezvous); w.Write(p.RequiredEquipment); w.Write(p.GuardRole); w.Write(p.GuardCount); w.Write(p.EquipTicks); }
        private static EscortDispatchPolicy? ReadEscortPolicy(BinaryReader r) => r.ReadBoolean() ? new EscortDispatchPolicy(r.ReadInt64(), ReadCoord(r), ReadCoord(r), r.ReadString(), r.ReadString(), r.ReadInt32(), r.ReadInt64()) : null;
        private static void WriteEscortPayload(BinaryWriter w, EscortOrderPayload? p)
        {
            w.Write(p != null); if (p == null) return;
            w.Write(p.AuthorizationId); w.Write(p.DispatchId); w.Write(p.SubjectAccountId); w.Write(p.SubjectActorId); w.Write(p.Expires); w.Write(p.SentenceTicks); WriteEscortPolicy(w, p.Dispatch);
            w.Write(p.JustifyingCases.Count); foreach (long id in p.JustifyingCases) w.Write(id);
            w.Write(p.Guards.Count); foreach (long id in p.Guards) w.Write(id);
            w.Write(p.Candidates.Count); foreach (var site in p.Candidates) WriteCoord(w, site);
        }
        private static EscortOrderPayload? ReadEscortPayload(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            long id = r.ReadInt64(), dispatchId = r.ReadInt64(), account = r.ReadInt64(), actor = r.ReadInt64(), expires = r.ReadInt64(), sentence = r.ReadInt64();
            var policy = ReadEscortPolicy(r) ?? throw new ArgumentException("Missing escort dispatch policy.");
            var cases = new long[Count(r)]; for (int i = 0; i < cases.Length; i++) cases[i] = r.ReadInt64();
            var guards = new long[Count(r)]; for (int i = 0; i < guards.Length; i++) guards[i] = r.ReadInt64();
            var sites = new Coord[Count(r)]; for (int i = 0; i < sites.Length; i++) sites[i] = ReadCoord(r);
            return new EscortOrderPayload(id, account, actor, expires, sentence, policy, cases, guards, sites, dispatchId);
        }
        private void WriteEscortDispatch(BinaryWriter w)
        {
            w.Write(nextEscortReview); w.Write(escortDispatches.Count);
            foreach (var d in escortDispatches.Values)
            {
                w.Write(d.AuthorizationId); w.Write(d.DispatchedTick); w.Write((int)d.Status); w.Write(d.Reason); WriteEscortPayload(w, d.Payload);
                w.Write(d.OrderMessageIds.Count); foreach (long id in d.OrderMessageIds) w.Write(id);
            }
            w.Write(guardAssignments.Count);
            foreach (var a in guardAssignments.Values) { w.Write(a.OrderMessageId); w.Write(a.ActorId); w.Write(a.AuthorizationId); w.Write(a.AcknowledgmentMessageId); w.Write(a.Response); }
            w.Write(localGuardOrders.Count);
            foreach (var l in localGuardOrders.Values) { w.Write(l.OrderMessageId); w.Write(l.ActorId); w.Write(l.ReadTick); w.Write(l.AcknowledgmentMessageId); w.Write(l.Response); w.Write((int)l.Status); w.Write(l.NextDecision); w.Write(l.Backoff); w.Write(l.ReadyTick); w.Write(l.ReadyItemId); w.Write(l.Reason); }
        }
        private void ReadEscortDispatch(BinaryReader r)
        {
            nextEscortReview = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var d = new EscortDispatchSnapshot { AuthorizationId = r.ReadInt64(), DispatchedTick = r.ReadInt64(), Status = ReadEnum<EscortDispatchStatus>(r), Reason = r.ReadString(), Payload = ReadEscortPayload(r) };
                var ids = new long[Count(r)]; for (int j = 0; j < ids.Length; j++) ids[j] = r.ReadInt64(); d.OrderMessageIds = ids;
                if (Detention?.Escort == null || !detentionAuthorizations.TryGetValue(d.AuthorizationId, out var authorization)) throw new ArgumentException("Unknown escort authorization.");
                if (d.Payload == null)
                {
                    if (ids.Length != 0 || d.DispatchedTick != -1 || !(d.Status is EscortDispatchStatus.AwaitingGuards or EscortDispatchStatus.Expired or EscortDispatchStatus.Cancelled) ||
                        d.Status == EscortDispatchStatus.AwaitingGuards && !(d.Reason is "AwaitingGuardTeam" or "NoKnownSubjectSearchLocation" or "InsufficientKnownOnDutyUncommittedGuards")) throw new ArgumentException("Invalid pending escort dispatch.");
                }
                else
                {
                    var p = d.Payload;
                    var expected = new EscortOrderPayload(authorization.Id, authorization.AccountId, authorization.ActorId, authorization.Expires, authorization.SentenceTicks, Detention.Escort, authorization.JustifyingCases, p.Guards, p.Candidates, p.DispatchId);
                    if (!p.Same(expected) || ids.Length != p.Guards.Count || ids.Distinct().Count() != ids.Length || d.DispatchedTick < authorization.IssuedTick || d.DispatchedTick >= authorization.Expires || d.DispatchedTick > LastProcessedTick ||
                        p.Candidates.Any(c => !Topology.Contains(c)) || p.Guards.Any(g => !registrations.TryGetValue(g, out var person) || person.Role != p.Dispatch.GuardRole)) throw new ArgumentException("Invalid escort team or destinations.");
                    for (int j = 0; j < ids.Length; j++)
                    {
                        if (!messages.TryGetValue(ids[j], out var m) || m.SenderAccount.Value != 0 || m.Seeded || m.Draft.Kind != MessageKind.EscortOrder || !ValidDraft(m.Draft) ||
                            m.Draft.EscortOrder == null || !p.Same(m.Draft.EscortOrder) || m.SubmittedTick != d.DispatchedTick || m.Draft.Recipient.Value != registrations[p.Guards[j]].AccountId ||
                            m.Draft.Subject.SubjectId != m.Id || m.Draft.OperationKey != "escort/" + authorization.Id + "/dispatch/" + p.DispatchId + "/guard/" + p.Guards[j]) throw new ArgumentException("Invalid guard order transport.");
                    }
                }
                if (d.Status == EscortDispatchStatus.Expired && (authorization.Status != DetentionAuthorizationStatus.Expired || d.Reason != "AuthorizationExpired") ||
                    authorization.Status == DetentionAuthorizationStatus.Expired && !(d.Status is EscortDispatchStatus.Expired or EscortDispatchStatus.Detained or EscortDispatchStatus.Released)) throw new ArgumentException("Invalid escort expiration state.");
                if (d.Status == EscortDispatchStatus.Cancelled && (authorization.Status != DetentionAuthorizationStatus.Revoked || d.Reason != "AuthorizationRevoked") || authorization.Status == DetentionAuthorizationStatus.Revoked && !(d.Status is EscortDispatchStatus.Cancelled or EscortDispatchStatus.Detained or EscortDispatchStatus.Released)) throw new ArgumentException("Invalid revoked escort dispatch."); escortDispatches.Add(d.AuthorizationId, d);
            }
            if (Detention?.Escort != null && escortDispatches.Count != detentionAuthorizations.Count) throw new ArgumentException("Missing escort dispatch.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new GuardAssignmentSnapshot { OrderMessageId = r.ReadInt64(), ActorId = r.ReadInt64(), AuthorizationId = r.ReadInt64(), AcknowledgmentMessageId = r.ReadInt64(), Response = r.ReadString() };
                if (!escortDispatches.TryGetValue(a.AuthorizationId, out var dispatch) || dispatch.Payload == null ||
                    !messages.TryGetValue(a.OrderMessageId, out var order) || !registrations.TryGetValue(a.ActorId, out var person) || order.Draft.Recipient.Value != person.AccountId || order.Draft.EscortOrder == null || !order.Draft.EscortOrder.Guards.Contains(a.ActorId)) throw new ArgumentException("Invalid guard assignment.");
                var payload = order.Draft.EscortOrder; var authorization = detentionAuthorizations[a.AuthorizationId];
                var expectedPayload = new EscortOrderPayload(authorization.Id, authorization.AccountId, authorization.ActorId, authorization.Expires, authorization.SentenceTicks, Detention!.Escort!, authorization.JustifyingCases, payload.Guards, payload.Candidates, payload.DispatchId);
                if (!payload.Same(expectedPayload) || order.SenderAccount.Value != 0 || order.Seeded || order.Draft.Kind != MessageKind.EscortOrder || !ValidDraft(order.Draft) ||
                    order.SubmittedTick < authorization.IssuedTick || order.SubmittedTick >= authorization.Expires || order.Draft.Subject.SubjectId != order.Id ||
                    order.Draft.OperationKey != "escort/" + authorization.Id + "/dispatch/" + payload.DispatchId + "/guard/" + a.ActorId ||
                    !messages.TryGetValue(payload.DispatchId, out var first) || first.Draft.EscortOrder == null || !first.Draft.EscortOrder.Same(payload) || first.SubmittedTick != order.SubmittedTick)
                    throw new ArgumentException("Invalid historical guard transport.");
                if (a.AcknowledgmentMessageId == 0) { if (a.Response != "AwaitingAcknowledgment") throw new ArgumentException("Unread guard acknowledgment leaked."); }
                else if (!messages.TryGetValue(a.AcknowledgmentMessageId, out var ack) || ack.Draft.Kind != MessageKind.EscortAcknowledged || !ValidDraft(ack.Draft) || ack.Draft.RelatedMessageId != order.Id ||
                    ack.Stage != MessageStage.Accepted || ack.PhysicalSender.Value != a.ActorId || ack.SenderAccount.Value != person.AccountId || ack.TerminalId < 1 || ack.SessionId < 1 || ack.SubmittedTick < order.DeliveryTick ||
                    ack.DeliveryTick >= dispatch.Payload.Expires || RevocationWasKnownToAuthority(a.AuthorizationId, ack.DeliveryTick, long.MaxValue) || ack.Draft.Claim.Text != a.Response) throw new ArgumentException("Invalid guard acknowledgment receipt.");
                var history = records.History.Values.Where(e => e.Kind == RecordKind.GuardAssignment && e.EntityId == a.OrderMessageId).ToArray();
                if (history.Length != (a.AcknowledgmentMessageId == 0 ? 1 : 2) || history.Any(e => e.AccountId != person.AccountId || RecordNumber(e, AttributeKind.ActorId) != a.ActorId ||
                    RecordNumber(e, AttributeKind.AuthorizationId) != a.AuthorizationId || RecordNumber(e, AttributeKind.SubjectAccountId) != dispatch.Payload.SubjectAccountId || RecordNumber(e, AttributeKind.Expires) != dispatch.Payload.Expires) ||
                    history[0].Tick != order.SubmittedTick || RecordNumber(history[0], AttributeKind.MessageId) != 0 || RecordText(history[0], AttributeKind.WorkStatus) != "AwaitingAcknowledgment" ||
                    history.Length == 2 && (history[1].Tick != messages[a.AcknowledgmentMessageId].DeliveryTick || RecordNumber(history[1], AttributeKind.MessageId) != a.AcknowledgmentMessageId || RecordText(history[1], AttributeKind.WorkStatus) != a.Response)) throw new ArgumentException("Invalid guard assignment history.");
                guardAssignments.Add(a.OrderMessageId, a);
            }
            foreach (var d in escortDispatches.Values.Where(d => d.Payload != null))
            {
                if (d.OrderMessageIds.Any(id => !guardAssignments.ContainsKey(id))) throw new ArgumentException("Missing guard assignment.");
                var generations = guardAssignments.Values.Where(a => a.AuthorizationId == d.AuthorizationId).GroupBy(a => messages[a.OrderMessageId].Draft.EscortOrder!.DispatchId).OrderBy(g => g.Key).ToArray();
                if (generations.Length == 0 || generations.Last().Key != d.Payload!.DispatchId) throw new ArgumentException("Missing current escort dispatch.");
                for (int generation = 0; generation < generations.Length; generation++)
                {
                    var group = generations[generation]; var payload = messages[group.Key].Draft.EscortOrder!;
                    if (!group.Select(a => a.ActorId).OrderBy(id => id).SequenceEqual(payload.Guards.OrderBy(id => id))) throw new ArgumentException("Incomplete historical guard team.");
                    if (generation > 0 && generations.Take(generation).SelectMany(g => g).Any(a => payload.Guards.Contains(a.ActorId) ||
                        !records.History.Values.Any(e => e.Kind == RecordKind.GuardOutcome && RecordNumber(e, AttributeKind.GuardOrderMessageId) == a.OrderMessageId && GuardOutcomeTerminal(RecordText(e, AttributeKind.WorkStatus)) && e.Tick <= messages[group.Key].SubmittedTick)))
                        throw new ArgumentException("Replacement lacks received terminal reports or reuses a failed guard.");
                }
                if (d.Status == EscortDispatchStatus.AwaitingGuards)
                {
                    if (!d.OrderMessageIds.All(id => records.History.Values.Any(e => e.Kind == RecordKind.GuardOutcome && RecordNumber(e, AttributeKind.GuardOrderMessageId) == id && GuardOutcomeTerminal(RecordText(e, AttributeKind.WorkStatus)))) ||
                        !(d.Reason is "AwaitingReplacementTeam" or "NoKnownSubjectSearchLocation" or "InsufficientKnownOnDutyUncommittedGuards")) throw new ArgumentException("Replacement wait lacks reports.");
                    continue;
                }
                if (d.Status is EscortDispatchStatus.Expired or EscortDispatchStatus.Cancelled) continue; if (d.Status == EscortDispatchStatus.NeedsAttention) { if (!records.History.Values.Any(e => e.Kind == RecordKind.GuardOutcome && RecordNumber(e, AttributeKind.AuthorizationId) == d.AuthorizationId && d.Reason == "GuardReported/" + RecordText(e, AttributeKind.WorkStatus))) throw new ArgumentException("Missing guard outcome receipt."); continue; }
                if (d.Status is EscortDispatchStatus.Detained or EscortDispatchStatus.Released)
                {
                    var kind = d.Status == EscortDispatchStatus.Detained ? RecordKind.DetentionIntake : RecordKind.DetentionRelease;
                    if (!records.History.Values.Any(e => e.Kind == kind && e.EntityId == d.AuthorizationId) || d.Reason != (d.Status == EscortDispatchStatus.Detained ? "IntakeReceipt" : "ReleaseReceipt")) throw new ArgumentException("Escort custody belief lacks a received service receipt.");
                    continue;
                }
                var denied = d.OrderMessageIds.Select(id => guardAssignments[id]).Where(a => a.AcknowledgmentMessageId != 0 && a.Response != "Accepted").OrderByDescending(a => messages[a.AcknowledgmentMessageId].DeliveryTick).ThenByDescending(a => a.AcknowledgmentMessageId).FirstOrDefault();
                var expected = denied != null ? EscortDispatchStatus.Blocked : d.OrderMessageIds.All(id => guardAssignments[id].Response == "Accepted") ? EscortDispatchStatus.TeamForming : EscortDispatchStatus.AwaitingReaders;
                string reason = denied != null ? "GuardReported/" + denied.Response : expected == EscortDispatchStatus.TeamForming ? "EquipmentAndRendezvousPending" : "AwaitingIndividualAcknowledgments";
                if (d.Status != expected || d.Reason != reason) throw new ArgumentException("Escort progress has no received acknowledgments.");
            }
            if (messages.Values.Any(m => m.Draft.Kind == MessageKind.EscortOrder && !guardAssignments.ContainsKey(m.Id)) || records.History.Values.Any(e => e.Kind == RecordKind.GuardAssignment && !guardAssignments.ContainsKey(e.EntityId))) throw new ArgumentException("Unregistered guard order.");
            foreach (var team in escortDispatches.Values.Where(d => d.Payload != null))
                if (escortDispatches.Values.Any(other => other.Payload != null && (other.DispatchedTick < team.DispatchedTick || other.DispatchedTick == team.DispatchedTick && other.AuthorizationId < team.AuthorizationId) &&
                    other.Payload.Expires > team.DispatchedTick && !records.History.Values.Any(e => e.Kind == RecordKind.DetentionIntake && e.EntityId == other.AuthorizationId && e.Tick <= team.DispatchedTick) && other.Payload.Guards.Intersect(team.Payload!.Guards).Any(g => !GuardReportedFinished(g, other.AuthorizationId, team.DispatchedTick)))) throw new ArgumentException("Overlapping guard commitments.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var l = new LocalGuardOrderSnapshot { OrderMessageId = r.ReadInt64(), ActorId = r.ReadInt64(), ReadTick = r.ReadInt64(), AcknowledgmentMessageId = r.ReadInt64(), Response = r.ReadString(), Status = ReadEnum<GuardOrderStatus>(r), NextDecision = r.ReadInt64(), Backoff = r.ReadInt64(), ReadyTick = r.ReadInt64(), ReadyItemId = r.ReadInt64(), Reason = r.ReadString() };
                if (!guardAssignments.TryGetValue(l.OrderMessageId, out var assignment) || assignment.ActorId != l.ActorId || !messages.TryGetValue(l.OrderMessageId, out var order) || order.Stage != MessageStage.Read ||
                    order.FirstReadTick != l.ReadTick || order.Draft.EscortOrder == null) throw new ArgumentException("Invalid private guard order source.");
                l.Payload = order.Draft.EscortOrder;
                string response = l.Payload.Expires <= l.ReadTick ? "KnownOrderExpired" : actors[new Id<ActorTag>(l.ActorId)].Role != l.Payload.Dispatch.GuardRole ? "NotQualified" : "Accepted";
                if (l.Response != response) throw new ArgumentException("Invalid private guard decision.");
                if (l.AcknowledgmentMessageId == 0)
                {
                    if (!reportAgents.TryGetValue(new Id<ActorTag>(l.ActorId), out var reports) || !reports.Pending.Any(d => d.Kind == MessageKind.EscortAcknowledged && ValidDraft(d) && d.RelatedMessageId == l.OrderMessageId && d.Claim.Text == response && d.ObservedTick == l.ReadTick && d.OperationKey == "escort-ack/" + l.OrderMessageId)) throw new ArgumentException("Missing guard acknowledgment goal.");
                }
                else if (!messages.TryGetValue(l.AcknowledgmentMessageId, out var ack) || ack.PhysicalSender.Value != l.ActorId || ack.Draft.Kind != MessageKind.EscortAcknowledged || !ValidDraft(ack.Draft) || ack.Draft.RelatedMessageId != l.OrderMessageId || ack.Draft.Claim.Text != response || ack.Draft.ObservedTick != l.ReadTick || ack.Draft.OperationKey != "escort-ack/" + l.OrderMessageId) throw new ArgumentException("Invalid private posted acknowledgment.");
                var expected = l.Payload.Expires <= LastProcessedTick ? GuardOrderStatus.Expired : response != "Accepted" ? GuardOrderStatus.Rejected : l.AcknowledgmentMessageId == 0 ? GuardOrderStatus.AcknowledgmentPending : GuardOrderStatus.EquipmentPending;
                if (l.Status != GuardOrderStatus.Revoked && l.Status != expected && !(expected == GuardOrderStatus.EquipmentPending && (l.Status is GuardOrderStatus.AtRendezvous or GuardOrderStatus.WaitingTeam or GuardOrderStatus.Searching or GuardOrderStatus.TargetLocated or GuardOrderStatus.SearchExhausted or GuardOrderStatus.EscortInterrupted or GuardOrderStatus.TransferredToFacility)) && RuntimeError == null) throw new ArgumentException("Invalid private guard order progress.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.ExpireGuardOrder && e.Value == l.OrderMessageId).ToArray();
                if (timers.Length != (l.Status != GuardOrderStatus.Revoked && l.Payload.Expires > LastProcessedTick ? 1 : 0) || timers.Any(e => e.Phase != EventPhase.Decision || e.Tick != l.Payload.Expires)) throw new ArgumentException("Invalid local guard expiry timer.");
                localGuardOrders.Add(l.OrderMessageId, l);
            }
            if (messages.Values.Any(m => m.Draft.Kind == MessageKind.EscortOrder && m.Stage == MessageStage.Read && !localGuardOrders.ContainsKey(m.Id)) || scheduled.Any(e => e.Kind == ScheduledKind.ExpireGuardOrder && !localGuardOrders.ContainsKey(e.Value))) throw new ArgumentException("Missing local guard order.");
            var reviews = scheduled.Where(e => e.Kind == ScheduledKind.ReviewEscorts).ToArray();
            bool pending = escortDispatches.Values.Any(d => d.Status == EscortDispatchStatus.AwaitingGuards);
            if (nextEscortReview < -1 || reviews.Length != (nextEscortReview < 0 ? 0 : 1) || reviews.Any(e => e.Tick != nextEscortReview || e.Phase != EventPhase.Decision) ||
                RuntimeError == null && (pending != (nextEscortReview >= 0) || nextEscortReview >= 0 && nextEscortReview <= LastProcessedTick)) throw new ArgumentException("Invalid escort dispatch timer.");
        }
    }
}
