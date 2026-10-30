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
                bool searchedSinceVisit = origin.HasValue && latestAuthorityVisit.TryGetValue(subject.AccountId, out var visit) &&
                    receivedGuardOutcomes.Values.Any(o => o.Outcome == "TargetNotLocated" && o.ObservedTick >= visit.SubmittedTick &&
                        messages[o.OrderMessageId].Draft.EscortOrder is EscortOrderPayload old && old.SubjectActorId == subject.ActorId && old.Candidates.Contains(origin.Value));
                if (origin.HasValue && !searchedSinceVisit) candidates.Add(origin.Value);
                // Received terminal outcomes and administrative cancellation mean
                // an old assignment is no longer a current work destination.
                // The last reported terminal visit remains independent evidence.
                foreach (var attempt in workAttempts.Values.Where(a => a.ActorId == subject.ActorId && a.Expires > processingTick &&
                    a.Assignment == AssignmentStatus.Active && !(a.AuthorityStatus is "WorkCompleted" or "WorkBlocked" or "UnableToContinue" or "OrderRejected")).OrderByDescending(a => a.Id))
                {
                    var job = jobs[attempt.JobId]; Coord? site = job.Kind == WorkKind.ConstructHabitat ? ConstructionSpecification(job.ProjectId).Site : job.Kind == WorkKind.CleanTile ? view.Publication.Work?.Cleanable.SingleOrDefault(c => c.Tile.Value == job.TileId)?.Position : view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == job.MachineId)?.Position;
                    if (site.HasValue) candidates.Add(site.Value);
                }
                if (subject.Roster?.RestTile is Coord rest) candidates.Add(rest);
                // A received negative search lowers an older terminal clue's
                // priority. Keep it as a possibility; absence was only local.
                // A newer authenticated visit restores its original priority.
                if (origin.HasValue && searchedSinceVisit) candidates.Add(origin.Value);
                var sites = candidates.Distinct().Take(8).ToArray();
                if (sites.Length == 0) { dispatch.Reason = "NoKnownSubjectSearchLocation"; continue; }
                // An interrupted team is not silently reused. Received terminal outcomes
                // release its commitments; a replacement uses other registered guards.
                // Match the policy's cheap eligibility filters before reading work
                // commitments. Pending cases otherwise rescan every citizen's work
                // history even when the entire guard roster is off duty.
                var guards = AuthorityDispatch.EscortTeam(view, subject.ActorId, policy, view.Personnel.Where(p =>
                    p.ActorId != subject.ActorId && p.Role == policy.GuardRole && p.Roster != null && p.Roster.OnDuty(view.Tick) &&
                    !guardAssignments.Values.Any(a => a.AuthorizationId == authorization.Id && a.ActorId == p.ActorId)).Select(Candidate));
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
                dispatch.AcknowledgmentDeadline = checked(processingTick + EscortAcknowledgmentTicks);
                Schedule(new ScheduledEvent { Tick = dispatch.AcknowledgmentDeadline, Phase = EventPhase.Decision, Kind = ScheduledKind.EscortAcknowledgmentDue, Value = authorization.Id, ActionId = payload.DispatchId });
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
        // An urgent order is bounded: two configured hours (the station day / 12), or the
        // authority's ordinary acknowledgment interval without a roster.
        private long EscortAcknowledgmentTicks => Routines != null ? Math.Max(1, Routines.DayTicks / 12) : Authority?.UnacknowledgedTicks ?? 1;
        // Escalate only when no team member has acknowledged: a partly acknowledged team may
        // already be moving, and its partner wait is bounded by the TeamNotReady deadline.
        // Every order is revoked with a recorded reason; silent guards are released and
        // excluded from the replacement. The authority never reads private guard state.
        private void EscortAcknowledgmentDue(long authorization, long dispatchId)
        {
            if (!escortDispatches.TryGetValue(authorization, out var dispatch) || dispatch.Payload?.DispatchId != dispatchId || dispatch.Status != EscortDispatchStatus.AwaitingReaders) return;
            var team = dispatch.OrderMessageIds.Select(id => guardAssignments[id]).ToArray();
            if (team.Any(a => a.AcknowledgmentMessageId > 0) || detentionAuthorizations[authorization].Status != DetentionAuthorizationStatus.Authorized || detentionAuthorizations[authorization].Expires <= processingTick) return;
            long cause = Emit("EscortAcknowledgmentOverdue", detail: "authorization=" + authorization + "; guards=" + string.Join(",", team.Select(a => a.ActorId)));
            foreach (var assignment in team)
            {
                var order = messages[assignment.OrderMessageId];
                PostAuthorityWork(new MessageDraft(MessageKind.EscortRevoked, RecipientKind.Account, order.Draft.Recipient, order.Draft.Subject,
                    new FactValue(FactValueKind.Text, text: "AcknowledgmentOverdue"), processingTick, relatedMessageId: order.Id, operationKey: "escort-unacknowledged/" + order.Id), cause);
                assignment.Response = "Unacknowledged"; RecordGuardAssignment(assignment);
            }
            dispatch.Status = EscortDispatchStatus.AwaitingGuards; dispatch.Reason = "AcknowledgmentOverdue"; dispatch.Reassignments++; dispatch.AcknowledgmentDeadline = -1;
            RequestEscortReview(processingTick); RequestIncidentReview(processingTick);
        }
        private bool EscortCommitted(long actor) => guardAssignments.Values.Any(a => a.ActorId == actor && a.Response != "Unacknowledged" && detentionAuthorizations[a.AuthorizationId].Expires > processingTick &&
            !(escortDispatches[a.AuthorizationId].Status is EscortDispatchStatus.Detained or EscortDispatchStatus.Released) && !GuardReportedFinished(actor, a.AuthorizationId, processingTick));
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
            // A reply after reassignment is retained as evidence but restores no commitment.
            if (assignment.Response == "Unacknowledged") { Emit("GuardAcknowledgmentLate", messageId: message.Id, detail: "authorization=" + assignment.AuthorizationId + "; " + d.Claim.Text); return false; }
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
                "guard-assignment/" + assignment.OrderMessageId + "/" + assignment.AcknowledgmentMessageId + (assignment.Response == "Unacknowledged" ? "/unacknowledged" : ""),
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
