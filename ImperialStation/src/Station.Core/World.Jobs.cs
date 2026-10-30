using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<long, JobSnapshot> jobs = new SortedDictionary<long, JobSnapshot>();
        private readonly SortedDictionary<long, WorkAttemptSnapshot> workAttempts = new SortedDictionary<long, WorkAttemptSnapshot>();
        private readonly SortedDictionary<string, long> assignmentKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> cancellationKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> workMessageKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<long, LocalWorkOrderSnapshot> localOrders = new SortedDictionary<long, LocalWorkOrderSnapshot>();
        private readonly List<WorkHistoryEntry> workHistory = new List<WorkHistoryEntry>();
        private long nextAttempt = 1;
        private long nextJob = 1;
        private long workVersion;
        public bool JobsEnabled { get; private set; }
        public IReadOnlyList<JobSnapshot> InspectJobs() => jobs.Values.Select(j => j.Copy()).ToArray();
        public IReadOnlyList<WorkAttemptSnapshot> InspectWorkAttempts() => workAttempts.Values.Select(a => a.Copy()).ToArray();
        public IReadOnlyList<LocalWorkOrderSnapshot> InspectLocalOrders(Id<ActorTag> actor) => localOrders.Values.Where(o => o.ActorId == actor.Value).Select(o => o.Copy()).ToArray();
        public IReadOnlyList<WorkHistoryEntry> InspectWorkHistory() => workHistory.Select(e => e.Copy()).ToArray();
        private void InitializeJobs(JobSystemSeed seed)
        {
            if (Information == null || !WorkEnabled && cleanliness == null) throw new ArgumentException("Jobs require information and a physical work system.");
            JobsEnabled = true;
            foreach (var job in seed.Jobs)
            {
                if (job.Kind != WorkKind.CleanTile ? !machines.ContainsKey(job.MachineId) : !dirt.ContainsKey(job.Tile.Value)) throw new ArgumentException("Unknown job target.");
                bool complete = job.Kind != WorkKind.VerifyMachine && (job.Kind == WorkKind.RepairMachine ? MachineOperational(machines[job.MachineId]) : dirt[job.Tile.Value].Units == 0);
                jobs.Add(job.Id, new JobSnapshot { Id = job.Id, Kind = job.Kind, MachineId = job.MachineId, TileId = job.Tile.Value, ReportedUnits = job.ReportedUnits, PhysicalStatus = complete ? WorkExecution.CompletedPhysically : WorkExecution.Pending });
            }
            nextJob = jobs.Count == 0 ? 1 : checked(jobs.Keys.Last() + 1);
            foreach (var assignment in seed.Assignments) ScheduleWorkAssignment(0, assignment);
            InitializeCheckIns(seed.CheckIns);
        }
        public long ScheduleWorkAssignment(long tick, AssignmentSeed assignment)
        {
            if (assignmentKeys.TryGetValue(assignment.Key, out long prior))
            {
                var old = workAttempts[prior];
                if (old.JobId != assignment.JobId || old.ActorId != assignment.Actor.Value || old.Expires != assignment.Expires) throw new ArgumentException("Assignment idempotency key conflicts with prior command.");
                return prior;
            }
            ValidateFuture(tick);
            var attempt = CreateWorkAttempt(tick, assignment);
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Delivery, Kind = ScheduledKind.DispatchWork, Value = attempt.Id });
            return attempt.Id;
        }
        private WorkAttemptSnapshot CreateWorkAttempt(long tick, AssignmentSeed assignment)
        {
            if (!JobsEnabled || !jobs.ContainsKey(assignment.JobId) || !actors.TryGetValue(assignment.Actor, out var actor) || assignment.Expires <= tick)
                throw new ArgumentException("Invalid assignment job, actor or expiry.");
            long id = nextAttempt; nextAttempt = checked(nextAttempt + 1);
            var attempt = new WorkAttemptSnapshot { Id = id, JobId = assignment.JobId, ActorId = assignment.Actor.Value, AccountId = actor.Account.Value,
                DispatchKey = assignment.Key, DispatchTick = tick, Expires = assignment.Expires };
            workAttempts.Add(id, attempt);
            assignmentKeys.Add(assignment.Key, id);
            return attempt;
        }
        public void ScheduleWorkCancellation(long tick, long attemptId, string key)
        {
            if (cancellationKeys.TryGetValue(key, out long prior)) { if (prior != attemptId) throw new ArgumentException("Cancellation key conflicts."); return; }
            ValidateFuture(tick);
            if (string.IsNullOrWhiteSpace(key) || !workAttempts.ContainsKey(attemptId)) throw new ArgumentException("Invalid cancellation.");
            cancellationKeys.Add(key, attemptId);
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Delivery, Kind = ScheduledKind.CancelAssignment, Value = attemptId });
        }
        private void WorkHistory(string kind, WorkAttemptSnapshot attempt, long message = 0)
        {
            workHistory.Add(new WorkHistoryEntry { Version = ++workVersion, Tick = processingTick, JobId = attempt.JobId, AttemptId = attempt.Id, MessageId = message, Kind = kind });
            Emit(kind, detail: "job=" + attempt.JobId + "; attempt=" + attempt.Id, messageId: message, attemptId: attempt.Id);
        }
        private long PostAuthorityWork(MessageDraft draft, long causeId = 0)
        {
            var message = new MessageSnapshot { Id = nextMessage++, Draft = draft, SubmittedTick = processingTick,
                DeliveryTick = checked(processingTick + Information!.LatencyTicks), Stage = MessageStage.Posted };
            messages.Add(message.Id, message);
            long cause = Emit(draft.Kind == MessageKind.RecoveryReply ? "RecoveryReplyPosted" : "AuthorityOrderPosted", messageId: message.Id, detail: draft.Kind + "; attempt=" + draft.AttemptId, causeId: causeId);
            Schedule(new ScheduledEvent { Tick = message.DeliveryTick, Phase = EventPhase.Delivery, Kind = ScheduledKind.MessageDelivery, Value = message.Id, CauseId = cause });
            return message.Id;
        }
        private void DispatchWork(long id, long causeId = 0)
        {
            var attempt = workAttempts[id]; if (attempt.Assignment != AssignmentStatus.PendingDispatch) return;
            var job = jobs[attempt.JobId];
            if (job.CurrentAttemptId > 0) CancelAssignment(job.CurrentAttemptId, "Superseded");
            attempt.Assignment = AssignmentStatus.Active; attempt.Execution = WorkExecution.Assigned;
            job.CurrentAttemptId = id; job.AuthorityStatus = "Assigned";
            RecalculateJobExecution(job.Id);
            attempt.OrderMessageId = PostAuthorityWork(new MessageDraft(MessageKind.Order, RecipientKind.Account, new Id<AccountTag>(attempt.AccountId),
                new FactKey(SubjectKind.Attempt, id, AttributeKind.OrderStatus), new FactValue(FactValueKind.Text, text: job.Kind.ToString()), processingTick,
                job.Id, id, workOrder: job.Kind == WorkKind.ConstructHabitat ? new WorkOrderPayload(ConstructionSpecification(job.ProjectId), attempt.Expires) : job.Kind != WorkKind.CleanTile ? new WorkOrderPayload(job.MachineId, attempt.Expires, job.Kind) : new WorkOrderPayload(new Id<TileTag>(job.TileId), attempt.Expires), operationKey: attempt.DispatchKey), causeId);
            WorkHistory("WorkAssigned", attempt, attempt.OrderMessageId);
            RecordWorkAttempt(attempt, "work/dispatch/" + attempt.Id);
            WatchAcknowledgment(attempt);
        }
        private void CancelAssignment(long id, string reason)
        {
            var attempt = workAttempts[id];
            if (attempt.Assignment is AssignmentStatus.Cancelled or AssignmentStatus.Superseded) return;
            attempt.Assignment = reason == "Superseded" ? AssignmentStatus.Superseded : AssignmentStatus.Cancelled;
            long message = attempt.OrderMessageId == 0 ? 0 : PostAuthorityWork(new MessageDraft(MessageKind.OrderCancelled, RecipientKind.Account, new Id<AccountTag>(attempt.AccountId),
                new FactKey(SubjectKind.Attempt, id, AttributeKind.OrderStatus), new FactValue(FactValueKind.Text, text: reason), processingTick,
                attempt.JobId, id, attempt.OrderMessageId, operationKey: "cancel/" + id));
            if (jobs[attempt.JobId].CurrentAttemptId == id) jobs[attempt.JobId].AuthorityStatus = reason;
            WorkHistory("Assignment" + reason, attempt, message);
            RecordWorkAttempt(attempt, "work/cancel/" + attempt.Id);
            CancelAuthorityExpectation(attempt.Id, reason);
            // No citizen state, active action, carried item or physical execution status changes here.
        }
        private void ReadWorkOrder(Actor actor, MessageSnapshot message)
        {
            if (!JobsEnabled || message.SenderAccount.Value != 0 || message.Draft.AttemptId < 1) return;
            var draft = message.Draft;
            if (draft.Kind == MessageKind.OrderCancelled)
            {
                if (localOrders.TryGetValue(draft.AttemptId, out var copy) && copy.ActorId == actor.Id.Value && copy.JobId == draft.TaskId)
                { copy.Status = LocalOrderStatus.Cancelled; copy.Reason = "CancellationRead"; CancelPendingAcknowledgment(copy); RemoveOrderGoal(copy); SetAttemptExecution(copy.AttemptId, WorkExecution.Cancelled); UnpinOrder(actor, copy.AttemptId); }
                return;
            }
            if (ReadPostDetentionOrder(actor, message)) return;
            if (draft.Kind != MessageKind.Order || draft.WorkOrder == null || draft.TaskId < 1 || localOrders.ContainsKey(draft.AttemptId)) return;
            // Everything in this private order copy came from the completed authenticated read.
            var order = new LocalWorkOrderSnapshot { ActorId = actor.Id.Value, AttemptId = draft.AttemptId, JobId = draft.TaskId, MachineId = draft.WorkOrder.MachineId,
                Kind = draft.WorkOrder.Kind, TileId = draft.WorkOrder.Tile.Value, ProjectId = draft.WorkOrder.Construction?.ProjectId ?? 0,
                OrderMessageId = message.Id, ReadTick = processingTick, Expires = draft.WorkOrder.Expires, Status = LocalOrderStatus.Read, Backoff = planner.InitialBackoff };
            localOrders.Add(order.AttemptId, order);
            // Belief revision on read is a Mind rule; Core only supplies the private fact.
            var claimed = WorkOrderPolicy.ClaimedCondition(order.Kind, order.MachineId);
            if (claimed.HasValue)
            {
                var condition = claimed.Value;
                if (WorkOrderPolicy.OrderSupersedesBelief(actor.Knowledge.Entries.TryGetValue(condition, out var known) ? known.Fact : null, draft.ObservedTick))
                    Learn(actor, condition, FactValue.Boolean(false), EvidenceKind.MessageRead, message.Id, draft.ObservedTick, 0);
            }
            // Memory of the citizen's own authenticated submission, not the
            // authority's assessment, physical truth or the message store.
            bool ownCompletionPosted = ownCompletionPosts.Contains((actor.Id.Value, order.JobId, order.AttemptId));
            switch (WorkOrderPolicy.OnRead(ownCompletionPosted, order.Expires, processingTick))
            {
                case WorkOrderReadOutcome.OwnCompletionAlreadyPosted:
                    order.Status = LocalOrderStatus.Completed; order.Reason = WorkOrderPolicy.OwnCompletionReason; UnpinOrder(actor, order.AttemptId); return;
                case WorkOrderReadOutcome.Expired:
                    order.Status = LocalOrderStatus.Expired; order.Reason = WorkOrderPolicy.ExpiredReason; QueueOrderReport(order, MessageKind.OrderRejected, order.Reason); UnpinOrder(actor, order.AttemptId); return;
            }
            Schedule(new ScheduledEvent { Tick = order.Expires, Phase = EventPhase.Decision, Kind = ScheduledKind.ExpireWorkOrder, Value = order.AttemptId });
            RequestOrderDecision(order, processingTick);
        }
        private void RequestOrderDecision(LocalWorkOrderSnapshot order, long tick)
        {
            if (!WorkOrderPolicy.AwaitsDecision(KnownWorkStatus(order.Status), order.Kind)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (order.NextDecision >= 0) return;
            order.NextDecision = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateWorkOrder, Value = order.AttemptId });
        }
        private void EvaluateWorkOrder(long id)
        {
            var order = localOrders[id]; order.NextDecision = -1;
            if (EscortControls(new Id<ActorTag>(order.ActorId)) || DeferToNeed(actors[new Id<ActorTag>(order.ActorId)]) || DeferToDueGoal(actors[new Id<ActorTag>(order.ActorId)])) return;
            switch (WorkOrderPolicy.Route(KnownWorkStatus(order.Status), order.Kind))
            {
                case WorkOrderRoute.Verification: EvaluateVerificationOrder(order); return;
                case WorkOrderRoute.Construction: EvaluateConstructionOrder(order); return;
                case WorkOrderRoute.Cleanup: EvaluateCleanupOrder(order); return;
                case WorkOrderRoute.None: return;
            }
            var actor = actors[new Id<ActorTag>(order.ActorId)];
            if (actor.Action != null || !KnownOnDuty(actor.Id) || PrepareCredentialGoal(actor)) return; // Action/duty boundaries wake this retained order.
            if (!SpendPlanningRequest()) { RequestOrderDecision(order, checked(processingTick + 1)); return; }
            var result = WorkOrderPolicy.PlanAcceptance(ActorView(actor.Id), order.Kind, order.MachineId, order.TileId, order.Kind == WorkKind.ConstructHabitat ? ReadConstructionSpecification(order) : null,
                Information!.InspectTicks, planner.Candidates, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += result.Expansions;
            var acceptance = WorkOrderPolicy.Accept(result.Outcome, order.Kind);
            if (acceptance.Verdict == WorkOrderVerdict.Retry) { PlansExhausted++; RequestOrderDecision(order, checked(processingTick + order.Backoff)); order.Backoff = NextWorkBackoff(order.Backoff); return; }
            if (acceptance.Verdict == WorkOrderVerdict.Reject)
            { PlansUnavailable++; order.Status = LocalOrderStatus.Rejected; order.Reason = acceptance.Reason; QueueOrderReport(order, MessageKind.OrderRejected, order.Reason); UnpinOrder(actor, id); }
            else
            { PlansFound++; order.Status = LocalOrderStatus.AcknowledgmentPending; QueueOrderReport(order, MessageKind.OrderAccepted, acceptance.Reason); }
            Emit("WorkOrderEvaluated", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId, detail: "attempt=" + id + "; " + order.Status);
        }
        private bool SpendPlanningRequest()
        {
            if (planningBudgetTick != processingTick) { planningBudgetTick = processingTick; planningRequestsThisTick = 0; }
            if (planningRequestsThisTick >= planner.RequestsPerTick) return false;
            planningRequestsThisTick++; PlannerRequests++; return true;
        }
        private long NextWorkBackoff(long value) => WorkBackoff.Next(value, planner.MaximumBackoff);
        private static KnownWorkOrderStatus KnownWorkStatus(LocalOrderStatus status) => (KnownWorkOrderStatus)(int)status;
        private static WorkOrderBrief WorkOrderBriefOf(LocalWorkOrderSnapshot order) => new WorkOrderBrief(order.AttemptId, order.Kind, KnownWorkStatus(order.Status), order.MachineId, order.TileId);
        // The citizen's own private order copies, in attempt order.
        private IEnumerable<WorkOrderBrief> OwnWorkOrderBriefs(long actorId) => localOrders.Values.Where(o => o.ActorId == actorId).Select(WorkOrderBriefOf);
        private bool WorkReportsFirst(Id<ActorTag> actor) => WorkOrderPolicy.ReportsFirst(needAgents.ContainsKey(actor.Value), reportAgents.TryGetValue(actor, out var report) ? report.Pending.Count : 0,
            checkIns.TryGetValue(actor, out var checkIn) && (checkIn.Due || checkIn.DueTick == processingTick));
        private void WakeOrderDecisions(Actor actor, bool failed = false)
        {
            if (actor.Action != null) return;
            foreach (var order in localOrders.Values.Where(o => o.ActorId == actor.Id.Value && WorkOrderPolicy.AwaitsDecision(KnownWorkStatus(o.Status), o.Kind) && o.NextDecision < 0))
                RequestOrderDecision(order, WorkBackoff.RetryTick(processingTick, order.Backoff, failed));
        }
        private void QueueOrderReport(LocalWorkOrderSnapshot order, MessageKind kind, string reason)
        {
            var draft = new MessageDraft(kind, RecipientKind.Authority, default,
                new FactKey(SubjectKind.Attempt, order.AttemptId, AttributeKind.WorkStatus), new FactValue(FactValueKind.Text, text: reason), processingTick,
                order.JobId, order.AttemptId, order.OrderMessageId, operationKey: kind + "/" + order.AttemptId);
            var actorId = new Id<ActorTag>(order.ActorId);
            if (!reportAgents.TryGetValue(actorId, out var agent)) { agent = new ReportAgent { Actor = actorId, Backoff = planner.InitialBackoff }; reportAgents.Add(actorId, agent); }
            if (!agent.Pending.Any(p => p.OperationKey == draft.OperationKey)) QueueReport(agent, draft);
            RequestReportDecision(agent, processingTick);
        }
        // Each citizen remembers the work completions it posted itself, by (job, attempt).
        private readonly SortedSet<(long Actor, long Job, long Attempt)> ownCompletionPosts = new SortedSet<(long, long, long)>();
        private void WorkMessagePosted(Actor actor, MessageSnapshot message)
        {
            if (message.Draft.Kind == MessageKind.WorkCompleted) ownCompletionPosts.Add((actor.Id.Value, message.Draft.TaskId, message.Draft.AttemptId));
            if (!localOrders.TryGetValue(message.Draft.AttemptId, out var order) || order.ActorId != actor.Id.Value || message.Draft.TaskId != order.JobId || message.Draft.RelatedMessageId != order.OrderMessageId) return;
            if (message.Draft.Kind == MessageKind.OrderAccepted && order.Status == LocalOrderStatus.AcknowledgmentPending)
            {
                order.Status = LocalOrderStatus.Working; AddOrderGoal(order);
                Emit("OrderAcknowledged", actor, messageId: message.Id, detail: "attempt=" + order.AttemptId + "; posted");
            }
        }
        private bool ReceiveWorkReport(MessageSnapshot message)
        {
            var draft = message.Draft;
            if (!JobsEnabled || !(draft.Kind is MessageKind.OrderAccepted or MessageKind.OrderRejected or MessageKind.WorkCompleted or MessageKind.WorkBlocked or MessageKind.UnableToContinue)) return true;
            if (!workAttempts.TryGetValue(draft.AttemptId, out var attempt) || attempt.JobId != draft.TaskId || attempt.AccountId != message.SenderAccount.Value || attempt.OrderMessageId == 0 ||
                (draft.Kind is MessageKind.OrderAccepted or MessageKind.OrderRejected && draft.RelatedMessageId != attempt.OrderMessageId))
            { Emit("WorkReportRejected", messageId: message.Id, detail: "AttemptOrAccountMismatch"); return false; }
            if (jobs[attempt.JobId].Kind == WorkKind.VerifyMachine && draft.Kind == MessageKind.WorkCompleted &&
                (!draft.Subject.Equals(new FactKey(SubjectKind.Machine, jobs[attempt.JobId].MachineId, AttributeKind.Operational)) || draft.Claim.Kind != FactValueKind.Boolean || draft.Claim.Number is not (0 or 1) || draft.ObservedTick < attempt.DispatchTick || draft.ObservedTick > processingTick))
            { Emit("WorkReportRejected", messageId: message.Id, detail: "InvalidVerificationClaim"); return false; }
            if (draft.InspectionReport != null && jobs[attempt.JobId].Kind != WorkKind.VerifyMachine)
            { Emit("WorkReportRejected", messageId: message.Id, detail: "InspectionRequiresVerificationOrder"); return false; }
            if (!ValidConstructionReport(message, attempt)) { Emit("WorkReportRejected", messageId: message.Id, detail: "InvalidConstructionClaim"); return false; } string key = message.SenderAccount.Value + "/" + (draft.OperationKey.Length == 0 ? "message/" + message.Id : draft.OperationKey);
            if (workMessageKeys.TryGetValue(key, out long prior))
            { if (!SameDraft(messages[prior].Draft, draft)) Emit("WorkReportRejected", messageId: message.Id, detail: "IdempotencyConflict"); return false; }
            if (draft.Kind is MessageKind.OrderAccepted or MessageKind.OrderRejected && (attempt.AcknowledgmentReceivedTick.HasValue || attempt.AuthorityStatus == "OrderRejected"))
            { Emit("WorkReportRejected", messageId: message.Id, detail: "AlreadyAcknowledged"); return false; }
            workMessageKeys.Add(key, message.Id);
            attempt.ReportMessageIds = attempt.ReportMessageIds.Concat(new[] { message.Id }).ToArray();
            if (draft.Kind == MessageKind.OrderAccepted) { attempt.AcknowledgmentReceivedTick ??= processingTick; attempt.AuthorityStatus = "Accepted"; }
            else attempt.AuthorityStatus = draft.Kind.ToString();
            if (jobs[attempt.JobId].CurrentAttemptId == attempt.Id && attempt.Assignment == AssignmentStatus.Active) jobs[attempt.JobId].AuthorityStatus = attempt.AuthorityStatus;
            WorkHistory("WorkReportReceived", attempt, message.Id);
            RecordWorkReport(message);
            RecordWorkAttempt(attempt, "work/receive/" + message.Id);
            ReceiveAuthorityReport(message, attempt);
            FacilityWorkReported(message, attempt);
            QualityWorkReported(message, attempt); ReceiveConstructionReport(message, attempt);
            return true;
        }
        private bool CanExecuteKnownOrder(Actor actor, long attempt) => localOrders.TryGetValue(attempt, out var order) && order.ActorId == actor.Id.Value && order.Status == LocalOrderStatus.Working && processingTick < order.Expires;
        private void AddOrderGoal(LocalWorkOrderSnapshot order)
        {
            if (order.Kind is WorkKind.VerifyMachine or WorkKind.ConstructHabitat) { RequestOrderDecision(order, processingTick); return; }
            if (order.Kind == WorkKind.CleanTile)
            {
                foreach (long priorId in WorkOrderPolicy.SupersededCleanups(WorkOrderBriefOf(order), OwnWorkOrderBriefs(order.ActorId)))
                { var priorCleanup = localOrders[priorId]; priorCleanup.Status = LocalOrderStatus.Cancelled; priorCleanup.Reason = WorkOrderPolicy.SupersededReason; RemoveOrderGoal(priorCleanup); SetAttemptExecution(priorCleanup.AttemptId, WorkExecution.Cancelled); UnpinOrder(actors[new Id<ActorTag>(priorCleanup.ActorId)], priorCleanup.AttemptId); }
                order.CleanupChecked = false; RequestOrderDecision(order, processingTick); return;
            }
            var actorId = new Id<ActorTag>(order.ActorId);
            if (!repairAgents.TryGetValue(actorId, out var agent)) { agent = new RepairAgent { Actor = actorId, Backoff = planner.InitialBackoff }; repairAgents.Add(actorId, agent); }
            bool retained = agent.Attempts.TryGetValue(order.MachineId, out long oldId);
            if (WorkOrderPolicy.SupersedesRepair(retained ? oldId : (long?)null, order.AttemptId) && localOrders.TryGetValue(oldId, out var old))
            { old.Status = LocalOrderStatus.Cancelled; old.Reason = WorkOrderPolicy.SupersededReason; SetAttemptExecution(oldId, WorkExecution.Cancelled); UnpinOrder(actors[actorId], oldId); }
            agent.Attempts[order.MachineId] = order.AttemptId;
            if (!agent.Pending.Contains(order.MachineId)) agent.Pending.Add(order.MachineId);
            RequestRepairDecision(agent, processingTick);
        }
        private void RemoveOrderGoal(LocalWorkOrderSnapshot order)
        {
            var actor = actors[new Id<ActorTag>(order.ActorId)];
            if (order.Kind != WorkKind.RepairMachine)
            {
                foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateWorkOrder && e.Value == order.AttemptId).ToArray()) scheduled.Remove(e);
                order.NextDecision = -1; order.CleanupChecked = false;
                if (actor.Action?.AttemptId == order.AttemptId) { if (actor.Action.Kind == ActionKind.Move) actor.Action.CancelAtBoundary = true; else CancelRunning(actor); }
                return;
            }
            if (!repairAgents.TryGetValue(actor.Id, out var agent) || !agent.Attempts.TryGetValue(order.MachineId, out long current) || current != order.AttemptId) return;
            agent.Pending.Remove(order.MachineId); agent.Attempts.Remove(order.MachineId); ReleaseRepairPins(actor, agent);
            if (actor.Action?.AttemptId == order.AttemptId)
            { if (actor.Action.Kind == ActionKind.Move) actor.Action.CancelAtBoundary = true; else CancelRunning(actor); }
        }
        private void ExpireWorkOrder(long id)
        {
            var order = localOrders[id];
            if (WorkOrderPolicy.Settled(KnownWorkStatus(order.Status))) return;
            order.Status = LocalOrderStatus.Expired; order.Reason = WorkOrderPolicy.ExpiredReason; CancelPendingAcknowledgment(order); RemoveOrderGoal(order); UnpinOrder(actors[new Id<ActorTag>(order.ActorId)], id);
            SetAttemptExecution(id, WorkExecution.Cancelled); QueueOrderReport(order, MessageKind.WorkBlocked, order.Reason);
        }
        private void UnpinOrder(Actor actor, long id)
        {
            var key = new FactKey(SubjectKind.Attempt, id, AttributeKind.OrderStatus);
            if (actor.Knowledge.Entries.TryGetValue(key, out var entry)) { var f = entry.Fact; entry.Fact = new KnowledgeFact(f.Id, f.Key, f.Value, f.SourceKind, f.SourceId, f.ObservedTick, f.LearnedTick, false, f.SourceActorId); }
        }
        private void SetAttemptExecution(long id, WorkExecution status)
        {
            if (!workAttempts.TryGetValue(id, out var attempt) || attempt.Execution == status || attempt.Execution == WorkExecution.CompletedPhysically) return;
            attempt.Execution = status; WorkHistory("WorkExecution" + status, attempt); RecalculateJobExecution(attempt.JobId);
        }
        private void RecalculateJobExecution(long jobId)
        {
            var job = jobs[jobId]; if (job.PhysicalStatus == WorkExecution.CompletedPhysically) return;
            var states = workAttempts.Values.Where(a => a.JobId == jobId).Select(a => a.Execution).ToArray();
            foreach (var state in new[] { WorkExecution.CompletedPhysically, WorkExecution.InProgress, WorkExecution.Assigned, WorkExecution.Waiting, WorkExecution.Blocked, WorkExecution.Failed, WorkExecution.Cancelled })
                if (states.Contains(state)) { job.PhysicalStatus = state; return; }
            job.PhysicalStatus = WorkExecution.Pending;
        }
        private void CancelPendingAcknowledgment(LocalWorkOrderSnapshot order)
        {
            var actorId = new Id<ActorTag>(order.ActorId);
            if (reportAgents.TryGetValue(actorId, out var report)) RemoveReports(report, p => p.Kind == MessageKind.OrderAccepted && p.AttemptId == order.AttemptId);
            var actor = actors[actorId];
            if (actor.Action?.Kind == ActionKind.PostMessage && actor.Action.Message?.Kind == MessageKind.OrderAccepted && actor.Action.Message.AttemptId == order.AttemptId) CancelRunning(actor);
        }
        private void MarkWorkInProgress(ActiveAction action) { if (action.AttemptId > 0) SetAttemptExecution(action.AttemptId, WorkExecution.InProgress); }
        private void RecordPhysicalWorkCompletion(Actor actor, ActiveAction action)
        {
            if (action.AttemptId == 0 || !workAttempts.TryGetValue(action.AttemptId, out var attempt)) return;
            var job = jobs[attempt.JobId];
            if (job.Kind == WorkKind.ConstructHabitat) { ConstructionOrderActionCompleted(actor, action, attempt, job); return; } if (job.Kind == WorkKind.VerifyMachine)
            {
                if (action.Kind != ActionKind.InspectMachine || action.ServiceId != job.MachineId) return;
                CaptureVerification(actor, action); SetAttemptExecution(attempt.Id, WorkExecution.CompletedPhysically); return;
            }
            if (job.Kind == WorkKind.CleanTile)
            {
                if (!(action.Kind is ActionKind.Clean or ActionKind.InspectTile) || dirt[job.TileId].Position != actor.Position || dirt[job.TileId].Units != 0) return;
                SetAttemptExecution(attempt.Id, WorkExecution.CompletedPhysically); job.PhysicalStatus = WorkExecution.CompletedPhysically; return;
            }
            if (action.Kind != ActionKind.Install) return;
            if (job.MachineId != action.ServiceId || !MachineOperational(machines[job.MachineId])) return;
            SetAttemptExecution(attempt.Id, WorkExecution.CompletedPhysically); job.PhysicalStatus = WorkExecution.CompletedPhysically;
        }
        private void RepairOrderSatisfied(RepairAgent agent, long machine)
        {
            TriggerWorkCheckIn(agent.Actor);
            if (!agent.Attempts.TryGetValue(machine, out long id)) return;
            var order = localOrders[id]; order.Status = LocalOrderStatus.Completed; agent.Attempts.Remove(machine);
            QueueOrderReport(order, MessageKind.WorkCompleted, "ReportedComplete"); UnpinOrder(actors[agent.Actor], id);
        }
        private void RepairOrderBlocked(RepairAgent agent, string reason)
        {
            if (agent.Pending.Count == 0 || !agent.Attempts.TryGetValue(agent.Pending[0], out long id)) return;
            SetAttemptExecution(id, WorkExecution.Blocked); var order = localOrders[id];
            if (!order.BlockedReported) { order.BlockedReported = true; QueueOrderReport(order, MessageKind.WorkBlocked, reason); TriggerWorkCheckIn(agent.Actor); }
        }
        private void ChangeWorkSuspension(Actor actor, Id<ActionTag> action, long id, bool resume)
        {
            if (!localOrders.TryGetValue(id, out var order) || order.ActorId != actor.Id.Value ||
                (resume ? order.Status != LocalOrderStatus.Suspended || order.Reason == "DetentionAwaitingCurrentOrders" || actor.Action != null || processingTick >= order.Expires : order.Status != LocalOrderStatus.Working))
            { Fail(actor, action, FailureReason.UnknownRequiredFact); return; }
            if (resume) { order.Status = LocalOrderStatus.Working; AddOrderGoal(order); }
            else { order.Status = LocalOrderStatus.Suspended; RemoveOrderGoal(order); SetAttemptExecution(id, WorkExecution.Waiting); }
            ActionsCompleted++; Emit("ActionCompleted", actor, action, detail: resume ? "ResumeWork" : "SuspendWork");
        }
    }
}
