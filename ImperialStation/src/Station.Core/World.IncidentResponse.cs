using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<(long Bucket, int Cohort), IncidentAdmissionBucket> admissionBuckets = new SortedDictionary<(long, int), IncidentAdmissionBucket>();
        private long nextAdmissionBucketTick = 0;
        private long nextIncidentReview = -1;
        private readonly SortedDictionary<long, MessageSnapshot> latestAuthorityVisit = new SortedDictionary<long, MessageSnapshot>();
        private void IndexAuthorityVisit(MessageSnapshot message)
        {
            if (message.Draft.RecipientKind == RecipientKind.Authority && message.Stage == MessageStage.Accepted && message.TerminalId > 0 &&
                (!latestAuthorityVisit.TryGetValue(message.SenderAccount.Value, out var previous) || message.SubmittedTick > previous.SubmittedTick || message.SubmittedTick == previous.SubmittedTick && message.Id > previous.Id))
                latestAuthorityVisit[message.SenderAccount.Value] = message;
        }
        public IReadOnlyList<IncidentAdmissionBucket> InspectAdmissionBuckets() => admissionBuckets.Values.Select(b => b.Copy()).ToArray();
        private long HourBucket(long tick) => checked(tick * Config.TickDurationMs) / 3600000;
        private long NextHourTick(long bucket)
        {
            long ms = checked((bucket + 1) * 3600000);
            return checked(ms / Config.TickDurationMs + (ms % Config.TickDurationMs == 0 ? 0 : 1));
        }
        private void OpenAdmissionBucket()
        {
            long bucket = HourBucket(processingTick); var view = AuthorityView();
            for (int cohort = -1; cohort < (Routines?.Cohorts ?? 0); cohort++)
            {
                long count = view.Personnel.LongCount(p => p.Roster != null && p.Roster.Cohort == cohort && p.Roster.OnDuty(processingTick));
                var entry = new IncidentAdmissionBucket { Bucket = bucket, Cohort = cohort, OpenedTick = processingTick, KnownOnDutyHeadcount = count,
                    Budget = Math.Max(1, checked(count * Authority!.IncidentAdmissionPermille) / 1000) };
                admissionBuckets.Add((bucket, cohort), entry);
                Emit("IncidentBudgetOpened", detail: "bucket=" + bucket + "; cohort=" + cohort + "; headcount=" + count + "; budget=" + entry.Budget);
            }
            nextAdmissionBucketTick = NextHourTick(bucket);
            Schedule(new ScheduledEvent { Tick = nextAdmissionBucketTick, Phase = EventPhase.Decision, Kind = ScheduledKind.OpenAdmissionBucket });
            RequestIncidentReview(processingTick);
        }
        private static bool NeedsWorkResponse(MissingReportIncident incident) => incident.WorkResponse is IncidentWorkResponse.Pending or IncidentWorkResponse.NoEligibleResponder or IncidentWorkResponse.EscalationDeferred;
        private static bool NeedsPatrolResponse(MissingReportIncident incident) => incident.PatrolOrderMessageId == 0 && incident.WorkerStatusResponse == "PendingPatrol";
        private static bool NeedsIncidentResponse(MissingReportIncident incident) => NeedsWorkResponse(incident) || NeedsPatrolResponse(incident);
        private void RequestIncidentReview(long tick)
        {
            RequestEscortReview(tick);
            RequestFacilityReview(tick);
            RequestQualityReview(tick);
            if (Authority == null || !missingReports.Values.Any(NeedsIncidentResponse)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (nextIncidentReview >= 0 && nextIncidentReview <= tick) return;
            foreach (var old in scheduled.Where(e => e.Kind == ScheduledKind.ReviewIncidents).ToArray()) scheduled.Remove(old);
            nextIncidentReview = tick;
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.ReviewIncidents });
        }
        private bool KnownCommitted(long actor)
        {
            return EscortCommitted(actor) || patrolOrders.Values.Any(p => p.ActorId == actor && p.Payload.Expires > processingTick && p.ResultMessageId == 0) || workAttempts.Values.Any(a => a.ActorId == actor && a.Expires > processingTick &&
                (a.Assignment == AssignmentStatus.PendingDispatch || a.Assignment == AssignmentStatus.Active &&
                    !(a.AuthorityStatus is "WorkCompleted" or "WorkBlocked" or "UnableToContinue" or "OrderRejected")));
        }
        private DispatchCandidate Candidate(KnownPersonnel person)
        {
            // Transport metadata is evidence of a previous terminal visit. No current
            // position, held credential, private read or action state is consulted.
            latestAuthorityVisit.TryGetValue(person.AccountId, out var latest);
            Coord? origin = latest == null ? null : publicKnowledge!.Services.SingleOrDefault(t => t.Id == latest.TerminalId)?.Position;
            return new DispatchCandidate(person, origin ?? person.Roster?.RestTile, KnownCommitted(person.ActorId));
        }
        private IEnumerable<DispatchCandidate> Candidates(IAuthorityView view)
        {
            // Receipt metadata is indexed when accepted and rebuilt on restore.
            // Commitments are reread after every dispatch, including within a review.
            var committed = new SortedSet<long>(workAttempts.Values.Where(a => a.Expires > processingTick &&
                (a.Assignment == AssignmentStatus.PendingDispatch || a.Assignment == AssignmentStatus.Active && !(a.AuthorityStatus is "WorkCompleted" or "WorkBlocked" or "UnableToContinue" or "OrderRejected"))).Select(a => a.ActorId));
            // Commitments other than routine cleanup; those cannot be interrupted by a repair.
            var binding = new SortedSet<long>(workAttempts.Values.Where(a => a.Expires > processingTick && jobs[a.JobId].Kind != WorkKind.CleanTile &&
                (a.Assignment == AssignmentStatus.PendingDispatch || a.Assignment == AssignmentStatus.Active && !(a.AuthorityStatus is "WorkCompleted" or "WorkBlocked" or "UnableToContinue" or "OrderRejected"))).Select(a => a.ActorId));
            committed.UnionWith(patrolOrders.Values.Where(p => p.Payload.Expires > processingTick && p.ResultMessageId == 0).Select(p => p.ActorId));
            binding.UnionWith(patrolOrders.Values.Where(p => p.Payload.Expires > processingTick && p.ResultMessageId == 0).Select(p => p.ActorId));
            var services = new SortedDictionary<long, Coord>();
            foreach (var service in view.Publication.Services) services.Add(service.Id, service.Position);
            foreach (var person in view.Personnel)
            {
                Coord? origin = latestAuthorityVisit.TryGetValue(person.AccountId, out var message) && services.TryGetValue(message.TerminalId, out var position) ? position : (Coord?)null;
                bool escort = EscortCommitted(person.ActorId);
                yield return new DispatchCandidate(person, origin ?? person.Roster?.RestTile, committed.Contains(person.ActorId) || escort, !binding.Contains(person.ActorId) && !escort);
            }
        }
        private void SetIncidentResponse(MissingReportIncident incident, IncidentWorkResponse state, string reason)
        {
            if (incident.WorkResponse == state && incident.ResponseReason == reason) return;
            incident.WorkResponse = state; incident.ResponseReason = reason;
            RecordIncidentResponse(incident);
            Emit("IncidentResponseChanged", attemptId: incident.AttemptId, detail: "incident=" + incident.Id + "; work=" + state + "; " + reason);
        }
        private void ReviewIncidents()
        {
            nextIncidentReview = -1;
            var view = AuthorityView(); long bucket = HourBucket(processingTick);
            DispatchCandidate[]? available = null;
            IEnumerable<DispatchCandidate> ReviewCandidates()
            {
                // Waiting cases share the same received evidence. Reuse the
                // expensive commitment scan until this review posts an assignment.
                foreach (var candidate in available ??= Candidates(view).ToArray()) yield return candidate;
            }
            foreach (var incident in missingReports.Values.Where(NeedsIncidentResponse).OrderBy(i => i.OpenedTick).ThenBy(i => i.OpeningSequence).ToArray())
            {
                if (NeedsWorkResponse(incident) && incident.ReplacementDepth >= Authority!.MaximumReplacementDepth)
                    SetIncidentResponse(incident, IncidentWorkResponse.DepthLimited, "MaximumReplacementDepth");
                var job = jobs[incident.JobId];
                long selected = !NeedsWorkResponse(incident) ? 0 : job.Kind == WorkKind.ConstructHabitat ? SelectConstructionWorker(view, job.ProjectId, incident.ActorId) : job.Kind == WorkKind.VerifyMachine ? SelectVerification(view, job.MachineId, incident.ActorId) : job.Kind == WorkKind.RepairMachine ? AuthorityDispatch.Replacement(view, job.MachineId, incident.ActorId, ReviewCandidates()) :
                    AuthorityDispatch.CleanupReplacement(view, new Id<TileTag>(job.TileId), incident.ActorId, ReviewCandidates());
                var locations = PatrolCandidates(incident, view);
                long guard = NeedsPatrolResponse(incident) ? SelectPatrol(incident, view, locations, ReviewCandidates()) : 0;
                if (NeedsWorkResponse(incident) && selected == 0) SetIncidentResponse(incident, IncidentWorkResponse.NoEligibleResponder, "NoKnownQualifiedOnDutyUncommittedResponderWithLocation");
                if (NeedsPatrolResponse(incident)) incident.PatrolReason = guard == 0 ? "NoKnownEligiblePatrolOrSearchLocation" : "";
                if (selected == 0 && guard == 0) continue;
                var budget = admissionBuckets[(bucket, incident.Cohort)];
                if (incident.AdmissionBucket < 0 && budget.Used >= budget.Budget)
                {
                    if (selected != 0) SetIncidentResponse(incident, IncidentWorkResponse.EscalationDeferred, "CohortHourBudgetExhausted");
                    if (guard != 0) incident.PatrolReason = "EscalationDeferred";
                    continue;
                }
                bool committed = false;
                if (selected != 0)
                {
                    long expires = checked(processingTick + Authority!.ResponseOrderTicks);
                    var assignment = new AssignmentSeed("incident/" + incident.Id + "/replacement", incident.JobId, new Id<ActorTag>(selected), expires);
                    var attempt = CreateWorkAttempt(processingTick, assignment);
                    incident.ReplacementAttemptId = attempt.Id;
                    DispatchWork(attempt.Id, incident.OpeningEventId); committed = true;
                    available = null;
                    SetIncidentResponse(incident, IncidentWorkResponse.Dispatched, "ReplacementOrderPosted");
                }
                // Re-evaluate commitments after posting the replacement. A dual-role
                // responder cannot be assigned both branches in the same decision.
                guard = NeedsPatrolResponse(incident) ? SelectPatrol(incident, view, locations, ReviewCandidates()) : 0;
                if (guard != 0) { DispatchPatrol(incident, guard, locations); committed = true; available = null; }
                if (committed && incident.AdmissionBucket < 0)
                {
                    budget.Used = checked(budget.Used + 1); incident.AdmissionBucket = bucket; incident.AdmissionTick = processingTick;
                    Emit("IncidentAdmitted", attemptId: incident.AttemptId, detail: "incident=" + incident.Id + "; bucket=" + bucket + "; cohort=" + incident.Cohort + "; used=" + budget.Used);
                }
                RecordIncidentResponse(incident);
            }
            if (!missingReports.Values.Any(NeedsIncidentResponse)) return;
            // Re-evaluate at public availability boundaries, not on private action or
            // sleep transitions. Messages and publications can wake the queue sooner.
            long next = nextAdmissionBucketTick;
            foreach (var person in view.Personnel.Where(p => p.Roster != null)) next = Math.Min(next, person.Roster!.NextBoundary(processingTick));
            foreach (var commitment in workAttempts.Values.Where(a => a.Expires > processingTick)) next = Math.Min(next, commitment.Expires);
            foreach (var patrol in patrolOrders.Values.Where(p => p.Payload.Expires > processingTick)) next = Math.Min(next, patrol.Payload.Expires);
            RequestIncidentReview(next);
        }
        private void RecordIncidentResponse(MissingReportIncident incident)
        {
            records.Append(RecordKind.IncidentResponse, incident.Id, workAttempts[incident.AttemptId].AccountId, processingTick,
                "incident-response/" + incident.Id + "/" + processingTick + "/" + incident.WorkResponse + "/" + incident.ReplacementAttemptId + "/" + incident.ReplacementResultMessageId + "/" + incident.PatrolOrderMessageId + "/" + incident.PatrolResultMessageId + "/" + incident.AdmissionBucket,
                new[] { Number(AttributeKind.AttemptId, incident.AttemptId), Number(AttributeKind.TaskId, incident.JobId), Number(AttributeKind.Cohort, incident.Cohort),
                    Number(AttributeKind.AdmissionBucket, incident.AdmissionBucket), Number(AttributeKind.ReplacementAttemptId, incident.ReplacementAttemptId),
                    Number(AttributeKind.ReplacementDepth, incident.ReplacementDepth), Number(AttributeKind.ResultMessageId, incident.ReplacementResultMessageId),
                    Text(AttributeKind.WorkResolution, incident.WorkResolution), Text(AttributeKind.WorkStatus, incident.WorkResponse.ToString()),
                    Number(AttributeKind.PatrolOrderMessageId, incident.PatrolOrderMessageId), Number(AttributeKind.PatrolResultMessageId, incident.PatrolResultMessageId), Text(AttributeKind.WorkerStatus, incident.WorkerStatusResponse) });
        }
    }
}
