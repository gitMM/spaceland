using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteDetentionFacilityDefinitions(BinaryWriter w, IReadOnlyList<DetentionFacility> facilities)
        {
            w.Write(facilities.Count);
            foreach (var f in facilities) { w.Write(f.Id); WriteCoord(w, f.Position); w.Write(f.Capacity); w.Write(f.ServiceCapacity); w.Write(f.IntakeTicks); w.Write(f.ReleaseTicks); w.Write(f.Powered); w.Write(f.Connected); }
        }
        private static DetentionFacility[] ReadDetentionFacilityDefinitions(BinaryReader r)
        {
            var facilities = new DetentionFacility[Count(r)];
            for (int i = 0; i < facilities.Length; i++) facilities[i] = new DetentionFacility(r.ReadInt64(), ReadCoord(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadBoolean(), r.ReadBoolean());
            return facilities;
        }
        private void WriteDetentionServices(BinaryWriter w)
        {
            w.Write(detentionFacilities.Count); foreach (var f in detentionFacilities.Values) { w.Write(f.Definition.Id); w.Write(f.Powered); w.Write(f.Connected); }
            w.Write(detentionStays.Count);
            foreach (var s in detentionStays.Values)
            {
                w.Write(s.AuthorizationId); w.Write(s.FacilityId); w.Write(s.ActorId); w.Write(s.AccountId); w.Write(s.LeaderId); w.Write((int)s.Status);
                w.Write(s.IntakeActionId); w.Write(s.IntakeStarted); w.Write(s.IntakeTick); w.Write(s.ReleaseDue); w.Write(s.ReleaseActionId); w.Write(s.ReleaseStarted); w.Write(s.ReleaseTick); w.Write(s.NextRelease); w.Write(s.Reason);
                w.Write(s.IntakeReceiptId); w.Write(s.IntakeReceivedTick); w.Write(s.ReleaseReceiptId); w.Write(s.ReleaseReceivedTick); w.Write(s.ReleaseNoticeId); w.Write(s.IntakeEventId);
            }
            w.Write(releaseOrderMessages.Count); foreach (var e in releaseOrderMessages) { w.Write(e.Key); w.Write(e.Value); }
        }
        private void ReadDetentionServices(BinaryReader r)
        {
            int count = Count(r); if (count != detentionFacilities.Count) throw new ArgumentException("Missing detention service state.");
            var seen = new SortedSet<long>();
            for (int i = 0; i < count; i++)
            { long id = r.ReadInt64(); if (!seen.Add(id) || !detentionFacilities.TryGetValue(id, out var f)) throw new ArgumentException("Unknown detention service."); f.Powered = r.ReadBoolean(); f.Connected = r.ReadBoolean(); }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var s = new DetentionStaySnapshot { AuthorizationId = r.ReadInt64(), FacilityId = r.ReadInt64(), ActorId = r.ReadInt64(), AccountId = r.ReadInt64(), LeaderId = r.ReadInt64(), Status = ReadEnum<DetentionStayStatus>(r),
                    IntakeActionId = r.ReadInt64(), IntakeStarted = r.ReadInt64(), IntakeTick = r.ReadInt64(), ReleaseDue = r.ReadInt64(), ReleaseActionId = r.ReadInt64(), ReleaseStarted = r.ReadInt64(), ReleaseTick = r.ReadInt64(), NextRelease = r.ReadInt64(), Reason = r.ReadString(),
                    IntakeReceiptId = r.ReadInt64(), IntakeReceivedTick = r.ReadInt64(), ReleaseReceiptId = r.ReadInt64(), ReleaseReceivedTick = r.ReadInt64(), ReleaseNoticeId = r.ReadInt64(), IntakeEventId = r.ReadInt64() };
                var group = escorts.Values.Where(g => g.AuthorizationId == s.AuthorizationId && g.StartedTick <= s.IntakeStarted).OrderByDescending(g => g.StartedTick).FirstOrDefault();
                if (group == null || !detentionFacilities.TryGetValue(s.FacilityId, out var f)) throw new ArgumentException("Detention stay lacks its physical service or escort.");
                var p = EscortPayload(group);
                if (s.FacilityId != p.Dispatch.FacilityId || f.Definition.Position != p.Dispatch.Destination || s.ActorId != p.SubjectActorId || s.AccountId != p.SubjectAccountId || s.LeaderId != p.Leader ||
                    s.IntakeActionId <= group.BeginActionId || s.IntakeActionId >= nextAction || s.IntakeStarted < group.StartedTick || s.IntakeStarted >= p.Expires || s.IntakeStarted > LastProcessedTick ||
                    s.IntakeReceivedTick < -1 || s.ReleaseReceivedTick < -1 || s.ReleaseTick < -1 || s.NextRelease < -1) throw new ArgumentException("Invalid detention stay identity or timeline.");
                bool admitted = s.Status is DetentionStayStatus.Detained or DetentionStayStatus.Releasing or DetentionStayStatus.Released;
                if (admitted ? s.IntakeEventId < 1 || s.IntakeEventId >= nextTrace : s.IntakeEventId != 0) throw new ArgumentException("Invalid intake event cause.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.DetentionReleaseDue && e.Value == s.AuthorizationId).ToArray();
                if (timers.Length != (s.NextRelease < 0 ? 0 : 1) || timers.Any(e => e.Tick != s.NextRelease || e.Phase != EventPhase.Decision) ||
                    s.Status == DetentionStayStatus.Detained != (s.NextRelease >= 0) || s.NextRelease >= 0 && (s.NextRelease < s.ReleaseDue || s.NextRelease <= LastProcessedTick)) throw new ArgumentException("Invalid release timer.");
                if (!admitted)
                {
                    if (s.IntakeTick != -1 || s.ReleaseDue != -1 || s.IntakeReceiptId != 0 || s.ReleaseActionId != 0 || s.ReleaseStarted != -1 || s.ReleaseTick != -1 || s.ReleaseReceiptId != 0 || s.ReleaseNoticeId != 0 || s.IntakeReceivedTick != -1 || s.ReleaseReceivedTick != -1)
                        throw new ArgumentException("Reservation incorrectly became occupancy.");
                    if (s.Status == DetentionStayStatus.Reserved)
                    {
                        if (group.Status != EscortStatus.AwaitingIntake || !f.Powered || !f.Connected || p.Expires <= LastProcessedTick) throw new ArgumentException("Invalid active intake reservation.");
                        ValidateDetentionOperation(s.LeaderId, s.IntakeActionId, ActionKind.RegisterIntake, s.FacilityId, s.IntakeStarted, checked(s.IntakeStarted + f.Definition.IntakeTicks));
                    }
                }
                else
                {
                    if (s.IntakeTick != checked(s.IntakeStarted + f.Definition.IntakeTicks) || s.IntakeTick >= p.Expires || detentionAuthorizations[s.AuthorizationId].RevokedTick >= 0 && s.IntakeTick >= detentionAuthorizations[s.AuthorizationId].RevokedTick || s.IntakeTick > LastProcessedTick || s.ReleaseDue != checked(s.IntakeTick + p.SentenceTicks) ||
                        group.Status != (s.Status == DetentionStayStatus.Released ? EscortStatus.Released : EscortStatus.Detained)) throw new ArgumentException("Invalid intake completion or sentence.");
                    ValidateDetentionServiceMessage(s, s.IntakeReceiptId, MessageKind.DetentionIntakeReceipt, s.IntakeTick, s.IntakeReceivedTick);
                    if (s.Status == DetentionStayStatus.Released)
                    {
                        if (s.ReleaseStarted < s.ReleaseDue || s.ReleaseTick != checked(s.ReleaseStarted + f.Definition.ReleaseTicks) || s.ReleaseTick > LastProcessedTick || s.ReleaseActionId <= s.IntakeActionId || s.ReleaseActionId >= nextAction)
                            throw new ArgumentException("Invalid finite release completion.");
                        ValidateDetentionServiceMessage(s, s.ReleaseReceiptId, MessageKind.DetentionReleaseReceipt, s.ReleaseTick, s.ReleaseReceivedTick);
                        ValidateDetentionServiceMessage(s, s.ReleaseNoticeId, MessageKind.DetentionReleaseNotice, s.ReleaseTick, -1);
                    }
                    else
                    {
                        if (s.ReleaseTick != -1 || s.ReleaseReceiptId != 0 || s.ReleaseNoticeId != 0 || s.ReleaseReceivedTick != -1) throw new ArgumentException("Premature release receipt.");
                        if (s.Status == DetentionStayStatus.Releasing)
                        {
                            if (!f.Powered || !f.Connected || s.ReleaseStarted < s.ReleaseDue || s.ReleaseActionId <= s.IntakeActionId) throw new ArgumentException("Invalid release service operation.");
                            ValidateDetentionOperation(s.ActorId, s.ReleaseActionId, ActionKind.ReleaseDetention, s.FacilityId, s.ReleaseStarted, checked(s.ReleaseStarted + f.Definition.ReleaseTicks));
                        }
                        else if (s.ReleaseActionId != 0 || s.ReleaseStarted != -1) throw new ArgumentException("Inactive release retains an operation.");
                    }
                }
                detentionStays.Add(s.AuthorizationId, s);
                if (s.ReleaseReceivedTick >= 0 && escortDispatches[s.AuthorizationId].Status != EscortDispatchStatus.Released || s.ReleaseReceivedTick < 0 && s.IntakeReceivedTick >= 0 && escortDispatches[s.AuthorizationId].Status != EscortDispatchStatus.Detained)
                    throw new ArgumentException("Institutional custody status contradicts received service evidence.");
            }
            foreach (var f in InspectDetentionFacilities())
                if (f.Occupants + f.Reservations > f.Definition.Capacity || f.Operations > f.Definition.ServiceCapacity) throw new ArgumentException("Detention capacity exceeded.");
            foreach (var group in escorts.Values.Where(g => g.Status is EscortStatus.Detained or EscortStatus.Released))
                if (!detentionStays.ContainsKey(group.AuthorizationId)) throw new ArgumentException("Custody lacks physical intake.");
            foreach (var local in localGuardOrders.Values.Where(l => l.Status == GuardOrderStatus.TransferredToFacility))
                if (!detentionStays.TryGetValue(local.Payload.AuthorizationId, out var stay) || stay.IntakeTick < 0 || local.Reason != "LocalIntakeCompleted") throw new ArgumentException("Guard transfer lacks completed local intake.");
            if (records.History.Values.Any(e => (e.Kind is RecordKind.DetentionIntake or RecordKind.DetentionRelease) && !detentionStays.ContainsKey(e.EntityId))) throw new ArgumentException("Unregistered detention receipt history.");
            foreach (var actor in actors.Values.Where(a => a.Action?.Kind is ActionKind.RegisterIntake or ActionKind.ReleaseDetention))
                if (!detentionStays.Values.Any(s => s.Status == DetentionStayStatus.Reserved && s.IntakeActionId == actor.Action!.Id.Value && s.LeaderId == actor.Id.Value ||
                    s.Status == DetentionStayStatus.Releasing && s.ReleaseActionId == actor.Action!.Id.Value && s.ActorId == actor.Id.Value)) throw new ArgumentException("Orphaned detention service operation.");
            if (scheduled.Any(e => e.Kind == ScheduledKind.DetentionReleaseDue && !detentionStays.ContainsKey(e.Value) || e.Kind == ScheduledKind.DetentionServiceChange && (!detentionFacilities.ContainsKey(e.ActionId) || e.Value < 0 || e.Value > 3))) throw new ArgumentException("Invalid detention service event.");
            if (messages.Values.Any(m => (m.Draft.Kind is MessageKind.DetentionIntakeReceipt or MessageKind.DetentionReleaseReceipt or MessageKind.DetentionReleaseNotice) &&
                !detentionStays.Values.Any(s => s.IntakeReceiptId == m.Id || s.ReleaseReceiptId == m.Id || s.ReleaseNoticeId == m.Id))) throw new ArgumentException("Unregistered detention service message.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long messageId = r.ReadInt64(), authorization = r.ReadInt64();
                if (!detentionStays.TryGetValue(authorization, out var s) || s.ReleaseReceivedTick < 0 || !messages.TryGetValue(messageId, out var m) || m.SenderAccount.Value != 0 || m.Seeded ||
                    m.Draft.Kind != MessageKind.Order || m.Draft.Recipient.Value != s.AccountId || m.SubmittedTick != s.ReleaseReceivedTick ||
                    !workAttempts.TryGetValue(m.Draft.AttemptId, out var attempt) || attempt.ActorId != s.ActorId || m.Draft.TaskId != attempt.JobId ||
                    m.Draft.OperationKey != "post-detention/" + authorization + "/" + attempt.Id || m.Draft.WorkOrder == null || m.Draft.WorkOrder.Expires != attempt.Expires || !ValidDraft(m.Draft) ||
                    m.Draft.WorkOrder.Kind != messages[attempt.OrderMessageId].Draft.WorkOrder!.Kind || m.Draft.WorkOrder.MachineId != messages[attempt.OrderMessageId].Draft.WorkOrder!.MachineId ||
                    m.Draft.WorkOrder.Tile != messages[attempt.OrderMessageId].Draft.WorkOrder!.Tile) throw new ArgumentException("Invalid current-order refresh after release.");
                releaseOrderMessages.Add(messageId, authorization);
            }
        }
        private void ValidateDetentionOperation(long actorId, long actionId, ActionKind kind, long facility, long start, long end)
        {
            var actor = actors[new Id<ActorTag>(actorId)]; var a = actor.Action;
            if (a == null || a.Id.Value != actionId || a.Kind != kind || a.ServiceId != facility || a.Started != start || a.Completes != end || a.Target != detentionFacilities[facility].Definition.Position ||
                actor.Position != a.Target || start > LastProcessedTick || end <= LastProcessedTick ||
                scheduled.Count(e => e.Kind == ScheduledKind.ActionCompletion && e.Intent.Actor == actor.Id && e.ActionId == actionId && e.Tick == end && e.Phase == EventPhase.PhysicalCompletion) != 1)
                throw new ArgumentException("Invalid finite detention service operation.");
        }
        private void ValidateDetentionServiceMessage(DetentionStaySnapshot s, long messageId, MessageKind kind, long tick, long received)
        {
            bool notice = kind == MessageKind.DetentionReleaseNotice; bool intake = kind == MessageKind.DetentionIntakeReceipt;
            if (!messages.TryGetValue(messageId, out var m) || !ValidDraft(m.Draft) || m.Seeded || m.SenderAccount.Value != 0 || m.PhysicalSender.Value != 0 || m.Draft.Kind != kind || m.SubmittedTick != tick || m.Draft.ObservedTick != tick ||
                m.DeliveryTick != checked(tick + Information!.LatencyTicks) || m.Draft.RecipientKind != (notice ? RecipientKind.Account : RecipientKind.Authority) || m.Draft.Recipient.Value != (notice ? s.AccountId : 0) ||
                !m.Draft.Subject.Equals(new FactKey(SubjectKind.Order, s.AuthorizationId, AttributeKind.DetentionStatus)) || m.Draft.Claim.Kind != FactValueKind.Text || m.Draft.Claim.Text != (intake ? "Detained" : "Released") ||
                m.Draft.OperationKey != "detention-service/" + s.FacilityId + "/" + s.AuthorizationId + "/" + kind ||
                !notice && (received != (m.DeliveryTick <= LastProcessedTick ? m.DeliveryTick : -1) || m.Stage != (received < 0 ? MessageStage.Posted : MessageStage.Accepted))) throw new ArgumentException("Invalid authenticated detention service receipt.");
            if (notice) return;
            var rows = records.History.Values.Where(e => e.Kind == (intake ? RecordKind.DetentionIntake : RecordKind.DetentionRelease) && e.EntityId == s.AuthorizationId).ToArray();
            if (rows.Length != (received < 0 ? 0 : 1) || rows.Any(e => e.Tick != received || e.AccountId != s.AccountId || RecordNumber(e, AttributeKind.ActorId) != s.ActorId || RecordNumber(e, AttributeKind.MessageId) != messageId ||
                RecordNumber(e, AttributeKind.ObservedTick) != tick || RecordNumber(e, AttributeKind.FacilityId) != s.FacilityId || RecordText(e, AttributeKind.DetentionStatus) != (intake ? "Detained" : "Released"))) throw new ArgumentException("Invalid institutional detention receipt history.");
        }
    }
}
