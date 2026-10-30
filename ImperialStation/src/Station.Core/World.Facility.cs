using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public FacilityConfig? Facility { get; private set; }
        private readonly SortedDictionary<(long Actor, FactKey Subject), FacilityObservationSnapshot> facilityObservations = new SortedDictionary<(long, FactKey), FacilityObservationSnapshot>();
        private readonly SortedDictionary<long, FacilityTaskSnapshot> facilityTasks = new SortedDictionary<long, FacilityTaskSnapshot>();
        private readonly SortedDictionary<FactKey, long> facilityTargets = new SortedDictionary<FactKey, long>();
        private readonly SortedDictionary<string, long> facilityReportKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private long nextFacilityReview = -1;
        public IReadOnlyList<FacilityTaskSnapshot> InspectFacilityTasks() => facilityTasks.Values.Select(t => t.Copy()).ToArray();
        public IReadOnlyList<FacilityObservationSnapshot> InspectFacilityObservations(Id<ActorTag> actor) => facilityObservations.Values.Where(o => o.ActorId == actor.Value).Select(o => o.Copy()).ToArray();
        private void InitializeFacility(FacilityConfig config)
        {
            if (Authority == null || !JobsEnabled || publicKnowledge?.Work == null) throw new ArgumentException("Facility reporting requires authority, jobs and published work rules.");
            // Validate declared identities at setup, never mutable condition/position during dispatch.
            if (publicKnowledge.Work.Equipment.Any(m => !machines.ContainsKey(m.Id)) || publicKnowledge.Work.Cleanable.Any(c => !dirt.ContainsKey(c.Tile.Value)))
                throw new ArgumentException("Facility publication contains unregistered target identities.");
            Facility = config;
        }
        private static bool FacilitySubject(FactKey key) => FacilityPolicy.Subject(key);
        private bool AdverseFacilityFact(FactKey key, FactValue value) => Facility != null && FacilityPolicy.Adverse(Facility, key, value);
        private void FacilityObserved(Actor actor, KnowledgeFact fact, long cause)
        {
            if (Facility == null || !FacilityPolicy.Observes(Facility, fact.SourceKind, fact.Key, actor.Role)) return;
            var key = (actor.Id.Value, fact.Key);
            if (!facilityObservations.TryGetValue(key, out var state))
            { state = new FacilityObservationSnapshot { ActorId = actor.Id.Value, Subject = fact.Key }; facilityObservations.Add(key, state); }
            bool wasAdverse = state.Adverse; state.Adverse = AdverseFacilityFact(fact.Key, fact.Value); state.LastObservedTick = fact.ObservedTick;
            if (!FacilityPolicy.ShouldReport(state.Adverse, wasAdverse, processingTick, state.NextRepeatTick, fact.Key,
                reportAgents.TryGetValue(actor.Id, out var pending) ? pending.Pending : (IEnumerable<MessageDraft>)Array.Empty<MessageDraft>())) return;
            long generation = checked(state.Generation + 1), next = checked(processingTick + Facility.RepeatTicks);
            string operation = "facility/" + actor.Id.Value + "/" + fact.Key + "/" + generation;
            var draft = new MessageDraft(MessageKind.FacilityCondition, RecipientKind.Authority, default, fact.Key, fact.Value, fact.ObservedTick, operationKey: operation);
            if (!reportAgents.TryGetValue(actor.Id, out var agent)) { agent = new ReportAgent { Actor = actor.Id, Backoff = planner.InitialBackoff }; reportAgents.Add(actor.Id, agent); }
            QueueReport(agent, draft); state.Generation = generation; state.LastQueuedTick = processingTick; state.NextRepeatTick = next; state.OperationKey = operation; state.LastMessageId = 0;
            Emit("FacilityReportQueued", actor, factKey: fact.Key, factValue: fact.Value, detail: operation, causeId: cause);
            RequestReportDecision(agent, processingTick);
        }
        private void FacilityMessagePosted(Actor actor, MessageSnapshot message)
        {
            if (message.Draft.Kind == MessageKind.FacilityCondition && facilityObservations.TryGetValue((actor.Id.Value, message.Draft.Subject), out var state) && state.OperationKey == message.Draft.OperationKey)
                state.LastMessageId = message.Id;
        }
        private bool FacilityPublished(FactKey key) => FacilityPolicy.Published(publicKnowledge?.Work, key);
        private static bool JobMatches(JobSnapshot job, FactKey key) => key.SubjectKind == SubjectKind.Tile ? job.Kind == WorkKind.CleanTile && job.TileId == key.SubjectId : job.Kind == WorkKind.RepairMachine && job.MachineId == key.SubjectId;
        private bool ReceiveFacilityReport(MessageSnapshot message, long cause)
        {
            var draft = message.Draft; if (draft.Kind != MessageKind.FacilityCondition) return true;
            var person = registrations.Values.SingleOrDefault(p => p.AccountId == message.SenderAccount.Value);
            if (!FacilityPolicy.AcceptsReport(Facility, person, draft.ObservedTick, processingTick, draft.Subject, draft.Claim, publicKnowledge?.Work))
            { Emit("FacilityReportRejected", messageId: message.Id, causeId: cause, detail: "UnknownReporterDutyOrPublishedCondition"); return false; }
            string operation = message.SenderAccount.Value + "/" + draft.OperationKey;
            if (facilityReportKeys.TryGetValue(operation, out long prior))
            { Emit("FacilityReportRejected", messageId: message.Id, causeId: cause, detail: SameDraft(messages[prior].Draft, draft) ? "DuplicateOperation" : "IdempotencyConflict"); return false; }
            facilityReportKeys.Add(operation, message.Id);
            FacilityTaskSnapshot? task = facilityTargets.TryGetValue(draft.Subject, out long previousJob) ? facilityTasks[previousJob] : null;
            var disposition = FacilityPolicy.Disposition(task != null, task?.Status == FacilityTaskStatus.ReportedComplete, task?.ResolvedObservedTick ?? 0, draft.ObservedTick);
            if (disposition == FacilityReportDisposition.Stale)
            { Emit("FacilityReportStale", messageId: message.Id, causeId: cause, detail: "PredatesReportedCompletion; job=" + task!.JobId); return false; }
            if (disposition == FacilityReportDisposition.NewTask)
            {
                var existing = jobs.Values.FirstOrDefault(j => !facilityTasks.ContainsKey(j.Id) && JobMatches(j, draft.Subject) && !(j.AuthorityStatus is "WorkCompleted" or "Cancelled"));
                if (existing == null)
                {
                    long id = nextJob; nextJob = checked(nextJob + 1);
                    existing = new JobSnapshot { Id = id, Kind = draft.Subject.SubjectKind == SubjectKind.Tile ? WorkKind.CleanTile : WorkKind.RepairMachine,
                        TileId = draft.Subject.SubjectKind == SubjectKind.Tile ? draft.Subject.SubjectId : 0, MachineId = draft.Subject.SubjectKind == SubjectKind.Machine ? draft.Subject.SubjectId : 0,
                        ReportedUnits = draft.Subject.SubjectKind == SubjectKind.Tile ? draft.Claim.Number : 0, PhysicalStatus = WorkExecution.Pending };
                    jobs.Add(id, existing); // No physical condition is consulted or changed by a claim.
                }
                task = new FacilityTaskSnapshot { JobId = existing.Id, Subject = draft.Subject, CreatedTick = processingTick, FirstMessageId = message.Id, LatestMessageId = message.Id,
                    LatestObservedTick = draft.ObservedTick, ReportsReceived = 1, AttemptId = existing.CurrentAttemptId, Status = existing.CurrentAttemptId == 0 ? FacilityTaskStatus.Pending : FacilityTaskStatus.Assigned };
                task.CauseId = Emit("FacilityTaskCreated", messageId: message.Id, causeId: cause, factKey: draft.Subject, detail: "job=" + task.JobId);
                facilityTasks.Add(task.JobId, task); facilityTargets[draft.Subject] = task.JobId;
            }
            else
            {
                bool reopen = FacilityPolicy.Reopens(task!.Status == FacilityTaskStatus.NeedsAttention, draft.ObservedTick, task.LatestObservedTick);
                task.LatestMessageId = message.Id; task.LatestObservedTick = Math.Max(task.LatestObservedTick, draft.ObservedTick); task.ReportsReceived = checked(task.ReportsReceived + 1);
                if (reopen) { task.Status = FacilityTaskStatus.Pending; task.Reason = "FreshConditionReport"; }
                Emit("FacilityReportMerged", messageId: message.Id, causeId: cause, detail: "job=" + task.JobId);
            }
            RequestFacilityReview(processingTick); return true;
        }
        private void FacilityWorkReported(MessageSnapshot message, WorkAttemptSnapshot attempt)
        {
            if (!facilityTasks.TryGetValue(attempt.JobId, out var task) || jobs[attempt.JobId].CurrentAttemptId != attempt.Id || task.Status == FacilityTaskStatus.ReportedComplete) return;
            task.AttemptId = attempt.Id;
            var response = FacilityPolicy.WorkResponse(message.Draft.Kind, message.Draft.ObservedTick, task.LatestObservedTick);
            if (response == FacilityWorkResponse.Reopen)
            { task.Status = FacilityTaskStatus.Pending; task.Reason = "CompletionPredatesLatestCondition"; }
            else if (response == FacilityWorkResponse.Complete)
            { task.Status = FacilityTaskStatus.ReportedComplete; task.CompletionMessageId = message.Id; task.ResolvedObservedTick = message.Draft.ObservedTick; task.Reason = "CompletionClaimReceived"; }
            else if (response == FacilityWorkResponse.NeedsAttention)
            { task.Status = FacilityTaskStatus.NeedsAttention; task.Reason = message.Draft.Claim.Text; }
            else return;
            Emit("FacilityTaskResponse", messageId: message.Id, attemptId: attempt.Id, detail: task.Status + "; " + task.Reason);
            RequestFacilityReview(processingTick);
        }
        // Evidence hold: the authority's own pending quality verification of a machine's
        // claimed repair must inspect it before another repair can change the evidence. A
        // reported discrepancy keeps the hold until a case decision for that repair is posted,
        // bounded to one configured day after the report so a missing filing cannot freeze it.
        private bool AwaitingQualityVerification(JobSnapshot job) => FacilityPolicy.EvidenceHold(job.Kind == WorkKind.RepairMachine,
            qualityChecks.Values.Where(q => q.MachineId == job.MachineId).Select(q => new QualityHoldEvidence(QualityStage(q.Status), q.RepairAttemptId,
                q.Status == QualityAssuranceStatus.ReportedDiscrepancy && messages.TryGetValue(q.ResultMessageId, out var result) ? result.DeliveryTick : (long?)null)),
            processingTick, Routines?.DayTicks ?? Authority?.UnacknowledgedTicks ?? 0, repair => caseReplies.Values.Any(reply => messages[reply].Draft.AttemptId == repair));
        // Urgent facility orders are bounded like escorts: two configured hours (day / 12).
        private long FacilityAcknowledgmentTicks => FacilityPolicy.UrgentAcknowledgmentTicks(Routines?.DayTicks, Authority?.UnacknowledgedTicks);
        // The current facility order, while it is still unacknowledged by its assignee.
        private WorkAttemptSnapshot? UnacknowledgedFacilityOrder(FacilityTaskSnapshot task) =>
            task.Status == FacilityTaskStatus.Assigned && workAttempts.TryGetValue(task.AttemptId, out var a) && a.Assignment == AssignmentStatus.Active &&
            !a.AcknowledgmentReceivedTick.HasValue && a.DispatchKey.StartsWith("facility/", StringComparison.Ordinal) ? a : null;
        private long FacilityAcknowledgmentDeadline(FacilityTaskSnapshot task) => UnacknowledgedFacilityOrder(task) is WorkAttemptSnapshot a ? checked(a.DispatchTick + FacilityAcknowledgmentTicks) : -1;
        private void RequestFacilityReview(long tick)
        {
            if (Facility == null || !facilityTasks.Values.Any(t => t.Status == FacilityTaskStatus.Pending || FacilityAcknowledgmentDeadline(t) >= 0 || CleanupHelpersWanted(t) > 0)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (nextFacilityReview >= 0 && nextFacilityReview <= tick) return;
            foreach (var old in scheduled.Where(e => e.Kind == ScheduledKind.ReviewFacilityTasks).ToArray()) scheduled.Remove(old);
            nextFacilityReview = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.ReviewFacilityTasks });
        }
        // An order nobody acknowledged within the deadline goes to another on-duty, eligible
        // responder through the ordinary superseding dispatch. A late reply to the old order
        // reaches only that superseded attempt and changes neither the job nor the task.
        private void ReassignUnacknowledgedFacilityOrders(IAuthorityView view)
        {
            DispatchCandidate[]? candidates = null;
            foreach (var task in facilityTasks.Values.OrderBy(t => t.CreatedTick).ThenBy(t => t.JobId).ToArray())
            {
                var silent = UnacknowledgedFacilityOrder(task); if (silent == null || !FacilityPolicy.AcknowledgmentOverdue(silent.DispatchTick, FacilityAcknowledgmentTicks, processingTick)) continue;
                var job = jobs[task.JobId]; if (AwaitingQualityVerification(job)) continue;
                candidates ??= Candidates(view).ToArray();
                long selected = FacilityPolicy.Responder(view, job.Kind, job.TileId, job.MachineId, silent.ActorId, candidates);
                if (selected == 0) { task.Reason = "UnacknowledgedNoEligibleReplacement"; continue; }
                Emit("FacilityOrderUnacknowledged", attemptId: silent.Id, messageId: silent.OrderMessageId, detail: "job=" + task.JobId + "; from=" + silent.ActorId + "; to=" + selected);
                var attempt = CreateWorkAttempt(processingTick, new AssignmentSeed("facility/" + task.JobId + "/unacknowledged/" + silent.Id, task.JobId, new Id<ActorTag>(selected), checked(processingTick + Facility!.OrderTicks)));
                task.AttemptId = attempt.Id; task.Reason = "ReassignedUnacknowledged"; DispatchWork(attempt.Id, task.CauseId); candidates = null;
            }
        }
        // A reported spill larger than one cleaner clears in a known duty period gets up to
        // two additional cleanup orders for the same tile. Each helper is a separate job;
        // the tile's last removed unit completes them all. Uses only received reports and
        // published durations, never live dirt.
        private long ReportedUnits(FacilityTaskSnapshot task) => messages.TryGetValue(task.LatestMessageId, out var latest) && latest.Draft.Claim.Kind == FactValueKind.Number ? latest.Draft.Claim.Number : 0;
        private static bool OpenAttempt(WorkAttemptSnapshot a, long tick) => a.Assignment == AssignmentStatus.Active && a.Expires > tick && !(a.AuthorityStatus is "WorkCompleted" or "WorkBlocked" or "UnableToContinue" or "OrderRejected");
        // Additional cleaners still wanted for an assigned cleanup task, within the lifetime bound.
        private long CleanupHelpersWanted(FacilityTaskSnapshot task)
        {
            if (cleanliness == null || Routines == null || !FacilityPolicy.CleanupHelpersApply(cleanliness.CleanUnitTicks, task.Status == FacilityTaskStatus.Assigned, task.Subject.SubjectKind == SubjectKind.Tile)) return 0;
            long units = ReportedUnits(task);
            string prefix = "facility-assist/" + task.JobId + "/";
            var helpers = workAttempts.Values.Where(a => a.DispatchKey.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            return FacilityPolicy.CleanupHelpersWanted(Routines.DutyTicks, cleanliness.CleanUnitTicks, units, helpers.Length, helpers.Count(a => OpenAttempt(a, processingTick)));
        }
        private void AddCleanupHelpers(IAuthorityView view)
        {
            DispatchCandidate[]? candidates = null;
            foreach (var task in facilityTasks.Values.Where(t => CleanupHelpersWanted(t) > 0).OrderBy(t => t.CreatedTick).ThenBy(t => t.JobId).ToArray())
            {
                long units = ReportedUnits(task);
                string prefix = "facility-assist/" + task.JobId + "/";
                var helpers = workAttempts.Values.Where(a => a.DispatchKey.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                // Bounded: at most twice the helper limit over the task's whole life.
                for (int n = helpers.Length; CleanupHelpersWanted(task) > 0; n++)
                {
                    candidates ??= Candidates(view).ToArray();
                    long selected = FacilityPolicy.CleanupHelper(view, task.Subject.SubjectId, workAttempts[task.AttemptId].ActorId, candidates, helpers.Where(h => h.Assignment == AssignmentStatus.Active).Select(h => h.ActorId));
                    if (selected == 0) break;
                    long id = nextJob; nextJob = checked(nextJob + 1);
                    jobs.Add(id, new JobSnapshot { Id = id, Kind = WorkKind.CleanTile, TileId = task.Subject.SubjectId, ReportedUnits = units, PhysicalStatus = WorkExecution.Pending });
                    var attempt = CreateWorkAttempt(processingTick, new AssignmentSeed(prefix + n, id, new Id<ActorTag>(selected), checked(processingTick + Facility!.OrderTicks)));
                    Emit("CleanupHelperDispatched", attemptId: attempt.Id, detail: "job=" + task.JobId + "; helper-job=" + id + "; units=" + units);
                    DispatchWork(attempt.Id, task.CauseId); candidates = null;
                    helpers = workAttempts.Values.Where(a => a.DispatchKey.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                }
            }
        }
        private void ReviewFacilityTasks()
        {
            nextFacilityReview = -1; var view = AuthorityView();
            DispatchCandidate[]? candidates = null;
            foreach (var task in facilityTasks.Values.Where(t => t.Status == FacilityTaskStatus.Pending).OrderBy(t => t.CreatedTick).ThenBy(t => t.JobId).ToArray())
            {
                var job = jobs[task.JobId];
                if (AwaitingQualityVerification(job)) { task.Reason = "AwaitingQualityVerification"; continue; }
                candidates ??= Candidates(view).ToArray();
                long selected = FacilityPolicy.Responder(view, job.Kind, job.TileId, job.MachineId, 0, candidates);
                if (selected == 0) { task.Reason = "NoKnownQualifiedOnDutyUncommittedResponderWithLocation"; continue; }
                var assignment = new AssignmentSeed("facility/" + task.JobId + "/after/" + job.CurrentAttemptId, task.JobId, new Id<ActorTag>(selected), checked(processingTick + Facility!.OrderTicks));
                var attempt = CreateWorkAttempt(processingTick, assignment); task.AttemptId = attempt.Id; task.Status = FacilityTaskStatus.Assigned; task.Reason = "OrderPosted";
                DispatchWork(attempt.Id, task.CauseId);
                // A dispatch can supersede another assignment. Rebuild before the
                // next selection; failed selections cannot change commitments.
                candidates = null;
            }
            ReassignUnacknowledgedFacilityOrders(view);
            AddCleanupHelpers(view);
            var waiting = facilityTasks.Values.Select(FacilityAcknowledgmentDeadline).Where(t => t > processingTick).DefaultIfEmpty(-1).Min();
            if (!facilityTasks.Values.Any(t => t.Status == FacilityTaskStatus.Pending)) { if (waiting > 0) RequestFacilityReview(waiting); return; }
            long next = NextHourTick(HourBucket(processingTick));
            if (waiting > 0) next = Math.Min(next, waiting);
            foreach (var person in view.Personnel.Where(p => p.Roster != null)) next = Math.Min(next, person.Roster!.NextBoundary(processingTick));
            foreach (var attempt in workAttempts.Values.Where(a => a.Expires > processingTick)) next = Math.Min(next, attempt.Expires);
            foreach (var patrol in patrolOrders.Values.Where(p => p.Payload.Expires > processingTick)) next = Math.Min(next, patrol.Payload.Expires);
            RequestFacilityReview(next);
        }
    }
}
