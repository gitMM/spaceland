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
        private static bool FacilitySubject(FactKey key) => key.SubjectKind == SubjectKind.Tile && key.Attribute == AttributeKind.Dirt || key.SubjectKind == SubjectKind.Machine && key.Attribute == AttributeKind.Operational;
        private bool AdverseFacilityFact(FactKey key, FactValue value) => Facility != null && (key.SubjectKind == SubjectKind.Tile && key.Attribute == AttributeKind.Dirt && value.Kind == FactValueKind.Number && value.Number >= Facility.DirtThreshold ||
            key.SubjectKind == SubjectKind.Machine && key.Attribute == AttributeKind.Operational && value.Equals(FactValue.Boolean(false)));
        private void FacilityObserved(Actor actor, KnowledgeFact fact, long cause)
        {
            if (Facility == null || !(fact.SourceKind is EvidenceKind.Direct or EvidenceKind.Inspection) || !FacilitySubject(fact.Key) || !Facility.ReportingRoles.Contains(actor.Role, StringComparer.Ordinal)) return;
            var key = (actor.Id.Value, fact.Key);
            if (!facilityObservations.TryGetValue(key, out var state))
            { state = new FacilityObservationSnapshot { ActorId = actor.Id.Value, Subject = fact.Key }; facilityObservations.Add(key, state); }
            bool wasAdverse = state.Adverse; state.Adverse = AdverseFacilityFact(fact.Key, fact.Value); state.LastObservedTick = fact.ObservedTick;
            if (!state.Adverse || wasAdverse && processingTick < state.NextRepeatTick) return;
            if (reportAgents.TryGetValue(actor.Id, out var pending) && pending.Pending.Any(p => p.Kind == MessageKind.FacilityCondition && p.Subject.Equals(fact.Key))) return;
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
        private bool FacilityPublished(FactKey key) => publicKnowledge?.Work != null && (key.SubjectKind == SubjectKind.Tile ? publicKnowledge.Work.Cleanable.Any(c => c.Tile.Value == key.SubjectId) :
            key.SubjectKind == SubjectKind.Machine && publicKnowledge.Work.Equipment.Any(m => m.Id == key.SubjectId));
        private static bool JobMatches(JobSnapshot job, FactKey key) => key.SubjectKind == SubjectKind.Tile ? job.Kind == WorkKind.CleanTile && job.TileId == key.SubjectId : job.Kind == WorkKind.RepairMachine && job.MachineId == key.SubjectId;
        private bool ReceiveFacilityReport(MessageSnapshot message, long cause)
        {
            var draft = message.Draft; if (draft.Kind != MessageKind.FacilityCondition) return true;
            var person = registrations.Values.SingleOrDefault(p => p.AccountId == message.SenderAccount.Value);
            if (Facility == null || person == null || draft.ObservedTick < 0 || draft.ObservedTick > processingTick || !Facility.ReportingRoles.Contains(person.Role, StringComparer.Ordinal) || !AdverseFacilityFact(draft.Subject, draft.Claim) || !FacilityPublished(draft.Subject))
            { Emit("FacilityReportRejected", messageId: message.Id, causeId: cause, detail: "UnknownReporterDutyOrPublishedCondition"); return false; }
            string operation = message.SenderAccount.Value + "/" + draft.OperationKey;
            if (facilityReportKeys.TryGetValue(operation, out long prior))
            { Emit("FacilityReportRejected", messageId: message.Id, causeId: cause, detail: SameDraft(messages[prior].Draft, draft) ? "DuplicateOperation" : "IdempotencyConflict"); return false; }
            facilityReportKeys.Add(operation, message.Id);
            FacilityTaskSnapshot? task = facilityTargets.TryGetValue(draft.Subject, out long previousJob) ? facilityTasks[previousJob] : null;
            if (task?.Status == FacilityTaskStatus.ReportedComplete && draft.ObservedTick <= task.ResolvedObservedTick)
            { Emit("FacilityReportStale", messageId: message.Id, causeId: cause, detail: "PredatesReportedCompletion; job=" + task.JobId); return false; }
            if (task == null || task.Status == FacilityTaskStatus.ReportedComplete)
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
                bool fresh = draft.ObservedTick > task.LatestObservedTick;
                task.LatestMessageId = message.Id; task.LatestObservedTick = Math.Max(task.LatestObservedTick, draft.ObservedTick); task.ReportsReceived = checked(task.ReportsReceived + 1);
                if (fresh && task.Status == FacilityTaskStatus.NeedsAttention) { task.Status = FacilityTaskStatus.Pending; task.Reason = "FreshConditionReport"; }
                Emit("FacilityReportMerged", messageId: message.Id, causeId: cause, detail: "job=" + task.JobId);
            }
            RequestFacilityReview(processingTick); return true;
        }
        private void FacilityWorkReported(MessageSnapshot message, WorkAttemptSnapshot attempt)
        {
            if (!facilityTasks.TryGetValue(attempt.JobId, out var task) || jobs[attempt.JobId].CurrentAttemptId != attempt.Id || task.Status == FacilityTaskStatus.ReportedComplete) return;
            task.AttemptId = attempt.Id;
            if (message.Draft.Kind == MessageKind.WorkCompleted)
            {
                if (message.Draft.ObservedTick < task.LatestObservedTick)
                { task.Status = FacilityTaskStatus.Pending; task.Reason = "CompletionPredatesLatestCondition"; }
                else { task.Status = FacilityTaskStatus.ReportedComplete; task.CompletionMessageId = message.Id; task.ResolvedObservedTick = message.Draft.ObservedTick; task.Reason = "CompletionClaimReceived"; }
            }
            else if (message.Draft.Kind is MessageKind.OrderRejected or MessageKind.WorkBlocked or MessageKind.UnableToContinue)
            { task.Status = FacilityTaskStatus.NeedsAttention; task.Reason = message.Draft.Claim.Text; }
            else return;
            Emit("FacilityTaskResponse", messageId: message.Id, attemptId: attempt.Id, detail: task.Status + "; " + task.Reason);
            RequestFacilityReview(processingTick);
        }
        private void RequestFacilityReview(long tick)
        {
            if (Facility == null || !facilityTasks.Values.Any(t => t.Status == FacilityTaskStatus.Pending)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (nextFacilityReview >= 0 && nextFacilityReview <= tick) return;
            foreach (var old in scheduled.Where(e => e.Kind == ScheduledKind.ReviewFacilityTasks).ToArray()) scheduled.Remove(old);
            nextFacilityReview = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.ReviewFacilityTasks });
        }
        private void ReviewFacilityTasks()
        {
            nextFacilityReview = -1; var view = AuthorityView();
            DispatchCandidate[]? candidates = null;
            foreach (var task in facilityTasks.Values.Where(t => t.Status == FacilityTaskStatus.Pending).OrderBy(t => t.CreatedTick).ThenBy(t => t.JobId).ToArray())
            {
                var job = jobs[task.JobId];
                candidates ??= Candidates(view).ToArray();
                long selected = job.Kind == WorkKind.CleanTile ? AuthorityDispatch.CleanupReplacement(view, new Id<TileTag>(job.TileId), 0, candidates) :
                    AuthorityDispatch.Replacement(view, job.MachineId, 0, candidates);
                if (selected == 0) { task.Reason = "NoKnownQualifiedOnDutyUncommittedResponderWithLocation"; continue; }
                var assignment = new AssignmentSeed("facility/" + task.JobId + "/after/" + job.CurrentAttemptId, task.JobId, new Id<ActorTag>(selected), checked(processingTick + Facility!.OrderTicks));
                var attempt = CreateWorkAttempt(processingTick, assignment); task.AttemptId = attempt.Id; task.Status = FacilityTaskStatus.Assigned; task.Reason = "OrderPosted";
                DispatchWork(attempt.Id, task.CauseId);
                // A dispatch can supersede another assignment. Rebuild before the
                // next selection; failed selections cannot change commitments.
                candidates = null;
            }
            if (!facilityTasks.Values.Any(t => t.Status == FacilityTaskStatus.Pending)) return;
            long next = NextHourTick(HourBucket(processingTick));
            foreach (var person in view.Personnel.Where(p => p.Roster != null)) next = Math.Min(next, person.Roster!.NextBoundary(processingTick));
            foreach (var attempt in workAttempts.Values.Where(a => a.Expires > processingTick)) next = Math.Min(next, attempt.Expires);
            foreach (var patrol in patrolOrders.Values.Where(p => p.Payload.Expires > processingTick)) next = Math.Min(next, patrol.Payload.Expires);
            RequestFacilityReview(next);
        }
    }
}
