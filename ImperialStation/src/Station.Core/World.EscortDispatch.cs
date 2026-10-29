using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<long, EscortDispatchSnapshot> escortDispatches = new SortedDictionary<long, EscortDispatchSnapshot>();
        private readonly SortedDictionary<long, GuardAssignmentSnapshot> guardAssignments = new SortedDictionary<long, GuardAssignmentSnapshot>();
        private readonly SortedDictionary<long, LocalGuardOrderSnapshot> localGuardOrders = new SortedDictionary<long, LocalGuardOrderSnapshot>();
        private long nextEscortReview = -1;
        public IReadOnlyList<EscortDispatchSnapshot> InspectEscortDispatches() => escortDispatches.Values.Select(o => o.Copy()).ToArray();
        public IReadOnlyList<GuardAssignmentSnapshot> InspectGuardAssignments() => guardAssignments.Values.Select(o => o.Copy()).ToArray();
        public IReadOnlyList<LocalGuardOrderSnapshot> InspectLocalGuardOrders(Id<ActorTag> actor) => localGuardOrders.Values.Where(o => o.ActorId == actor.Value).Select(o => o.Copy()).ToArray();
        private void OpenEscortDispatch(long authorization)
        {
            if (Detention?.Escort == null) return;
            escortDispatches.Add(authorization, new EscortDispatchSnapshot { AuthorizationId = authorization });
            RequestEscortReview(processingTick);
        }
        private void RequestEscortReview(long tick)
        {
            if (!escortDispatches.Values.Any(o => o.Status == EscortDispatchStatus.AwaitingGuards)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (nextEscortReview >= 0 && nextEscortReview <= tick) return;
            foreach (var old in scheduled.Where(e => e.Kind == ScheduledKind.ReviewEscorts).ToArray()) scheduled.Remove(old);
            nextEscortReview = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.ReviewEscorts });
        }
        private void ReviewEscorts()
        {
            nextEscortReview = -1; var view = AuthorityView(); var policy = Detention!.Escort!;
            foreach (var dispatch in escortDispatches.Values.Where(o => o.Status == EscortDispatchStatus.AwaitingGuards))
            {
                var authorization = detentionAuthorizations[dispatch.AuthorizationId];
                if (authorization.Status != DetentionAuthorizationStatus.Authorized || authorization.Expires <= processingTick) continue;
                var subject = view.Personnel.Single(p => p.ActorId == authorization.ActorId);
                var candidates = new List<Coord>(); var origin = Candidate(subject).KnownOrigin;
                if (origin.HasValue) candidates.Add(origin.Value);
                foreach (var attempt in workAttempts.Values.Where(a => a.ActorId == subject.ActorId && a.Expires > processingTick).OrderByDescending(a => a.Id))
                {
                    var job = jobs[attempt.JobId]; Coord? site = job.Kind == WorkKind.ConstructHabitat ? ConstructionSpecification(job.ProjectId).Site : job.Kind == WorkKind.CleanTile ? view.Publication.Work?.Cleanable.SingleOrDefault(c => c.Tile.Value == job.TileId)?.Position : view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == job.MachineId)?.Position;
                    if (site.HasValue) candidates.Add(site.Value);
                }
                if (subject.Roster?.RestTile is Coord rest) candidates.Add(rest);
                var sites = candidates.Distinct().Take(8).ToArray();
                if (sites.Length == 0) { dispatch.Reason = "NoKnownSubjectSearchLocation"; continue; }
                // An interrupted team is not silently reused. Received terminal outcomes
                // release its commitments; a replacement uses other registered guards.
                var guards = AuthorityDispatch.EscortTeam(view, subject.ActorId, policy, view.Personnel.Where(p => !guardAssignments.Values.Any(a => a.AuthorizationId == authorization.Id && a.ActorId == p.ActorId)).Select(Candidate));
                if (guards.Count != policy.GuardCount) { dispatch.Reason = "InsufficientKnownOnDutyUncommittedGuards"; continue; }
                var payload = new EscortOrderPayload(authorization.Id, authorization.AccountId, authorization.ActorId, authorization.Expires, authorization.SentenceTicks, policy, authorization.JustifyingCases, guards, sites, nextMessage);
                var orders = new List<long>();
                foreach (long guard in guards)
                {
                    long id = nextMessage;
                    long posted = PostAuthorityWork(new MessageDraft(MessageKind.EscortOrder, RecipientKind.Account, new Id<AccountTag>(registrations[guard].AccountId),
                        new FactKey(SubjectKind.Order, id, AttributeKind.DetentionStatus), new FactValue(FactValueKind.Text, text: "LocateAndEscort"), processingTick,
                        operationKey: "escort/" + authorization.Id + "/dispatch/" + payload.DispatchId + "/guard/" + guard, escortOrder: payload), 0);
                    var assignment = new GuardAssignmentSnapshot { OrderMessageId = posted, ActorId = guard, AuthorizationId = authorization.Id };
                    guardAssignments.Add(posted, assignment); orders.Add(posted); RecordGuardAssignment(assignment);
                }
                dispatch.Payload = payload; dispatch.OrderMessageIds = orders.ToArray(); dispatch.DispatchedTick = processingTick;
                dispatch.Status = EscortDispatchStatus.AwaitingReaders; dispatch.Reason = "AwaitingIndividualAcknowledgments";
                Emit("EscortTeamDispatched", detail: "authorization=" + authorization.Id + "; guards=" + string.Join(",", guards));
            }
            var pending = escortDispatches.Values.Where(o => o.Status == EscortDispatchStatus.AwaitingGuards && detentionAuthorizations[o.AuthorizationId].Expires > processingTick).ToArray();
            if (pending.Length == 0) return;
            long next = pending.Min(o => detentionAuthorizations[o.AuthorizationId].Expires);
            foreach (var p in view.Personnel.Where(p => p.Roster != null)) next = Math.Min(next, p.Roster!.NextBoundary(processingTick));
            foreach (var a in workAttempts.Values.Where(a => a.Expires > processingTick)) next = Math.Min(next, a.Expires);
            foreach (var p in patrolOrders.Values.Where(p => p.Payload.Expires > processingTick)) next = Math.Min(next, p.Payload.Expires);
            foreach (var a in detentionAuthorizations.Values.Where(a => a.Expires > processingTick)) next = Math.Min(next, a.Expires);
            RequestEscortReview(next);
        }
        private bool EscortCommitted(long actor) => guardAssignments.Values.Any(a => a.ActorId == actor && detentionAuthorizations[a.AuthorizationId].Expires > processingTick && !GuardReportedFinished(actor, a.AuthorizationId, processingTick) && !(escortDispatches[a.AuthorizationId].Status is EscortDispatchStatus.Detained or EscortDispatchStatus.Released));
        private void ReadGuardOrder(Actor actor, MessageSnapshot message)
        {
            var payload = message.Draft.EscortOrder;
            if (message.Draft.Kind != MessageKind.EscortOrder || message.SenderAccount.Value != 0 || payload == null || message.Draft.Recipient != actor.Account ||
                !payload.Guards.Contains(actor.Id.Value) || localGuardOrders.ContainsKey(message.Id)) return;
            string response = payload.Expires <= processingTick ? "KnownOrderExpired" : actor.Role != payload.Dispatch.GuardRole ? "NotQualified" : "Accepted";
            var local = new LocalGuardOrderSnapshot { OrderMessageId = message.Id, ActorId = actor.Id.Value, Payload = payload, ReadTick = processingTick, Response = response, Backoff = planner.InitialBackoff,
                Status = response == "KnownOrderExpired" ? GuardOrderStatus.Expired : response == "Accepted" ? GuardOrderStatus.AcknowledgmentPending : GuardOrderStatus.Rejected };
            localGuardOrders.Add(message.Id, local);
            if (payload.Expires > processingTick) Schedule(new ScheduledEvent { Tick = payload.Expires, Phase = EventPhase.Decision, Kind = ScheduledKind.ExpireGuardOrder, Value = message.Id });
            else ExpireGuardOrder(message.Id);
            if (!reportAgents.TryGetValue(actor.Id, out var agent)) { agent = new ReportAgent { Actor = actor.Id, Backoff = planner.InitialBackoff }; reportAgents.Add(actor.Id, agent); }
            QueueReport(agent, new MessageDraft(MessageKind.EscortAcknowledged, RecipientKind.Authority, default, message.Draft.Subject, new FactValue(FactValueKind.Text, text: response), processingTick,
                relatedMessageId: message.Id, operationKey: "escort-ack/" + message.Id));
            RequestReportDecision(agent, processingTick);
            Emit("GuardOrderRead", actor, messageId: message.Id, detail: "authorization=" + payload.AuthorizationId + "; " + response);
        }
        private void GuardMessagePosted(Actor actor, MessageSnapshot message)
        {
            GuardOutcomePosted(actor, message); if (message.Draft.Kind != MessageKind.EscortAcknowledged || !localGuardOrders.TryGetValue(message.Draft.RelatedMessageId, out var local) || local.ActorId != actor.Id.Value ||
                local.AcknowledgmentMessageId != 0 || message.Draft.Claim.Text != local.Response || message.Draft.ObservedTick != local.ReadTick || message.Draft.OperationKey != "escort-ack/" + local.OrderMessageId) return;
            local.AcknowledgmentMessageId = message.Id;
            if (local.Status == GuardOrderStatus.AcknowledgmentPending) { local.Status = GuardOrderStatus.EquipmentPending; RequestGuardDecision(local, processingTick); }
        }
        private bool ReceiveGuardAcknowledgment(MessageSnapshot message)
        {
            var d = message.Draft; if (d.Kind != MessageKind.EscortAcknowledged) return true;
            if (!guardAssignments.TryGetValue(d.RelatedMessageId, out var assignment) || assignment.AcknowledgmentMessageId != 0 ||
                message.SenderAccount.Value != registrations[assignment.ActorId].AccountId || message.PhysicalSender.Value != assignment.ActorId || message.TerminalId < 1 || message.SessionId < 1 ||
                message.SubmittedTick < messages[assignment.OrderMessageId].DeliveryTick || !d.Subject.Equals(messages[assignment.OrderMessageId].Draft.Subject))
            { Emit("GuardAcknowledgmentRejected", messageId: message.Id, detail: "OrderAccountOrTransportMismatch"); return false; }
            // This is an authenticated claim. The authority never consults the guard's private read state.
            if (detentionAuthorizations[assignment.AuthorizationId].Status != DetentionAuthorizationStatus.Authorized || detentionAuthorizations[assignment.AuthorizationId].Expires <= processingTick)
            { Emit("GuardAcknowledgmentRejected", messageId: message.Id, detail: "AuthorizationExpired"); return false; }
            assignment.AcknowledgmentMessageId = message.Id; assignment.Response = d.Claim.Text; RecordGuardAssignment(assignment);
            var dispatch = escortDispatches[assignment.AuthorizationId];
            if (!dispatch.OrderMessageIds.Contains(assignment.OrderMessageId) || dispatch.Status == EscortDispatchStatus.AwaitingGuards) return false;
            if (dispatch.Status is EscortDispatchStatus.Cancelled or EscortDispatchStatus.NeedsAttention or EscortDispatchStatus.Detained or EscortDispatchStatus.Released) return false; if (assignment.Response != "Accepted") { dispatch.Status = EscortDispatchStatus.Blocked; dispatch.Reason = "GuardReported/" + assignment.Response; }
            else if (dispatch.Status != EscortDispatchStatus.Blocked && dispatch.OrderMessageIds.All(id => guardAssignments[id].Response == "Accepted"))
            { dispatch.Status = EscortDispatchStatus.TeamForming; dispatch.Reason = "EquipmentAndRendezvousPending"; }
            Emit("GuardAcknowledgmentReceived", messageId: message.Id, detail: "authorization=" + assignment.AuthorizationId + "; " + assignment.Response); return false;
        }
        private void RecordGuardAssignment(GuardAssignmentSnapshot assignment)
        {
            var authorization = detentionAuthorizations[assignment.AuthorizationId];
            records.Append(RecordKind.GuardAssignment, assignment.OrderMessageId, registrations[assignment.ActorId].AccountId, processingTick,
                "guard-assignment/" + assignment.OrderMessageId + "/" + assignment.AcknowledgmentMessageId,
                new[] { Number(AttributeKind.ActorId, assignment.ActorId), Number(AttributeKind.AuthorizationId, assignment.AuthorizationId), Number(AttributeKind.SubjectAccountId, authorization.AccountId),
                    Number(AttributeKind.MessageId, assignment.AcknowledgmentMessageId), Number(AttributeKind.Expires, authorization.Expires), Text(AttributeKind.WorkStatus, assignment.Response) });
        }
        private void ExpireEscortDispatch(long authorization)
        {
            if (!escortDispatches.TryGetValue(authorization, out var dispatch)) return;
            if (!(dispatch.Status is EscortDispatchStatus.Detained or EscortDispatchStatus.Released)) { dispatch.Status = EscortDispatchStatus.Expired; dispatch.Reason = "AuthorizationExpired"; }
            if (!escortDispatches.Values.Any(o => o.Status == EscortDispatchStatus.AwaitingGuards))
            { foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ReviewEscorts).ToArray()) scheduled.Remove(e); nextEscortReview = -1; }
            RequestIncidentReview(processingTick);
        }
        private void ExpireGuardOrder(long id)
        {
            var local = localGuardOrders[id]; if (local.Status == GuardOrderStatus.Revoked) return; if (local.Status != GuardOrderStatus.TransferredToFacility) QueueGuardOutcome(local, "KnownOrderExpired", processingTick); local.Status = GuardOrderStatus.Expired;
            var escort = ActiveEscort(new Id<ActorTag>(local.ActorId)); if (escort != null && escort.AuthorizationId == local.Payload.AuthorizationId && (escort.Status is EscortStatus.Escorting or EscortStatus.AwaitingIntake)) InterruptEscort(escort, "KnownOrderExpired");
            WithdrawGuardJoin(local);
            local.Reason = "KnownOrderExpired"; RemoveGuardDecision(local);
            var actor = actors[new Id<ActorTag>(local.ActorId)]; var key = new FactKey(SubjectKind.Order, id, AttributeKind.DetentionStatus);
            if (actor.Action?.GuardOrderId == id && actor.Action.Kind == ActionKind.Move) actor.Action.CancelAtBoundary = true;
            if (checkIns.TryGetValue(actor.Id, out var checkIn)) RequestCheckInDecision(checkIn, processingTick);
            if (actor.Knowledge.Entries.TryGetValue(key, out var entry)) { var f = entry.Fact; entry.Fact = new KnowledgeFact(f.Id, f.Key, f.Value, f.SourceKind, f.SourceId, f.ObservedTick, f.LearnedTick, false, f.SourceActorId); }
            Emit("GuardOrderExpired", actor, messageId: id, detail: "LocalCopyExpired");
        }
        private static bool ValidEscortDraft(MessageDraft d)
        {
            if (d.EscortOrder != null && d.Kind != MessageKind.EscortOrder) return false;
            if (!(d.Kind is MessageKind.EscortOrder or MessageKind.EscortAcknowledged)) return true;
            if (d.WorkOrder != null || d.Roster != null || d.PatrolOrder != null || d.InspectionReport != null || d.CaseSubmission != null || d.CaseDecision != null || d.DetentionRequest != null || d.DetentionDecision != null ||
                d.TaskId != 0 || d.AttemptId != 0 || string.IsNullOrWhiteSpace(d.OperationKey) || d.Subject.SubjectKind != SubjectKind.Order || d.Subject.Attribute != AttributeKind.DetentionStatus || d.Claim.Kind != FactValueKind.Text) return false;
            return d.Kind == MessageKind.EscortOrder ? d.EscortOrder != null && d.RecipientKind == RecipientKind.Account && d.RelatedMessageId == 0 && d.Claim.Text == "LocateAndEscort" :
                d.RecipientKind == RecipientKind.Authority && d.RelatedMessageId > 0 && d.Subject.SubjectId == d.RelatedMessageId && (d.Claim.Text is "Accepted" or "NotQualified" or "KnownOrderExpired");
        }
    }
}
