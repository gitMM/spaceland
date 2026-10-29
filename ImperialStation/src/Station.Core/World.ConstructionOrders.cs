using System;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private ConstructionOrder ConstructionSpecification(long id)
        {
            var p = constructionProjects[id]; var c = Topology.Definitions[p.DefinitionId].Construction!;
            return new ConstructionOrder(p.Id, p.TileId, p.DefinitionId, p.Site, recipes[c.RecipeId], c.Access.Roles, c.Access.ToolResource, c.Access.RequiresCredential, c.Berths);
        }
        // Read only the payload of this citizen's completed authenticated read.
        private ConstructionOrder ReadConstructionSpecification(LocalWorkOrderSnapshot order) => messages[order.OrderMessageId].Draft.WorkOrder!.Construction!;
        private void EvaluateConstructionOrder(LocalWorkOrderSnapshot order)
        {
            var actor = actors[new Id<ActorTag>(order.ActorId)];
            if (actor.Action != null || processingTick >= order.Expires || !KnownOnDuty(actor.Id) || PrepareCredentialGoal(actor)) return;
            if (localOrders.Values.Any(o => o.ActorId == order.ActorId && o.Kind == WorkKind.ConstructHabitat && o.Status == LocalOrderStatus.Working && o.AttemptId < order.AttemptId)) return;
            if (!needAgents.ContainsKey(actor.Id.Value) && (reportAgents.TryGetValue(actor.Id, out var reports) && reports.Pending.Count > 0 || checkIns.TryGetValue(actor.Id, out var checkIn) && checkIn.Due)) return;
            if (!SpendPlanningRequest()) { RequestOrderDecision(order, checked(processingTick + 1)); return; }
            var result = ConstructionPlanning.Plan(ActorView(actor.Id), ReadConstructionSpecification(order), planner.Candidates, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += result.Expansions;
            if (result.Outcome == PlanOutcome.PlanFound)
            {
                PlansFound++; order.Backoff = planner.InitialBackoff;
                if (result.Steps.Count == 0) { ConstructionOrderSatisfied(actor, order, "KnownCompleted"); return; }
                var first = result.Steps[0].Intent;
                long cause = Emit("PlanSelected", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId,
                    detail: "construct-read-project/v1; " + string.Join(" -> ", result.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), CauseId = cause,
                    Intent = new ActionIntent(first.Actor, first.Kind, first.Target, first.Item, first.ServiceId, recipeId: first.RecipeId, attemptId: order.AttemptId) }); return;
            }
            if (result.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; order.Reason = "PlanBudgetExhausted"; }
            else { PlansUnavailable++; order.Reason = "NoKnownConstructionPrerequisitesOrRoute"; }
            SetAttemptExecution(order.AttemptId, WorkExecution.Blocked);
            if (!order.BlockedReported) { order.BlockedReported = true; QueueOrderReport(order, MessageKind.WorkBlocked, order.Reason); TriggerWorkCheckIn(actor.Id); }
            Emit("PlanUnavailable", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId, detail: "construct-read-project/v1; " + order.Reason);
            RequestOrderDecision(order, checked(processingTick + order.Backoff)); order.Backoff = NextWorkBackoff(order.Backoff);
        }
        private void ConstructionOrderSatisfied(Actor actor, LocalWorkOrderSnapshot order, string reason, long cause = 0)
        {
            order.Status = LocalOrderStatus.Completed; order.Reason = reason;
            order.CompletionObservedTick = processingTick;
            order.CompletionCauseId = Emit("ConstructionGoalCompleted", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId, detail: "project=" + order.ProjectId + "; " + reason, causeId: cause);
            QueueOrderReport(order, MessageKind.WorkCompleted, reason); UnpinOrder(actor, order.AttemptId); TriggerWorkCheckIn(actor.Id);
        }
        private void ConstructionOrderActionCompleted(Actor actor, ActiveAction action, WorkAttemptSnapshot attempt, JobSnapshot job)
        {
            if (action.Kind != ActionKind.Construct || action.ServiceId != job.ProjectId || !localOrders.TryGetValue(attempt.Id, out var order)) return;
            SetAttemptExecution(attempt.Id, WorkExecution.CompletedPhysically); job.PhysicalStatus = WorkExecution.CompletedPhysically;
            ConstructionOrderSatisfied(actor, order, "ReportedComplete", constructionProjects[job.ProjectId].CompletionEventId);
        }
        private long ConstructionReportCause(MessageDraft draft) => draft.Kind == MessageKind.ConstructionSurvey ? ConstructionSurveyCause(draft) : draft.Kind == MessageKind.WorkCompleted && localOrders.TryGetValue(draft.AttemptId, out var order) &&
            order.Kind == WorkKind.ConstructHabitat && order.OrderMessageId == draft.RelatedMessageId ? order.CompletionCauseId : 0;
        private void ConstructionWorkFailed(ActiveAction action, FailureReason reason)
        {
            if (!localOrders.TryGetValue(action.AttemptId, out var order) || order.Kind != WorkKind.ConstructHabitat || order.Status != LocalOrderStatus.Working) return;
            order.Reason = reason.ToString(); SetAttemptExecution(order.AttemptId, WorkExecution.Blocked);
            if (!order.BlockedReported) { order.BlockedReported = true; QueueOrderReport(order, MessageKind.WorkBlocked, order.Reason); }
        }
        private bool ValidConstructionReport(MessageSnapshot message, WorkAttemptSnapshot attempt)
        {
            if (jobs[attempt.JobId].Kind != WorkKind.ConstructHabitat) return true;
            var d = message.Draft;
            return d.RelatedMessageId == attempt.OrderMessageId && d.Subject.Equals(new FactKey(SubjectKind.Attempt, attempt.Id, AttributeKind.WorkStatus)) &&
                d.Claim.Kind == FactValueKind.Text && d.ObservedTick >= attempt.DispatchTick && d.ObservedTick <= message.DeliveryTick &&
                (d.Kind != MessageKind.WorkCompleted || d.Claim.Text is "ReportedComplete" or "KnownCompleted");
        }
        private void ReceiveConstructionReport(MessageSnapshot message, WorkAttemptSnapshot attempt)
        {
            var job = jobs[attempt.JobId];
            if (job.Kind != WorkKind.ConstructHabitat || message.Draft.Kind != MessageKind.WorkCompleted || !growthCommitments.TryGetValue(job.ProjectId, out var c)) return;
            if (c.AcceptedReportId > 0) { Emit("ConstructionReportDuplicate", messageId: message.Id, attemptId: attempt.Id, detail: "project=" + c.ProjectId); return; }
            // This is the authority's acceptance of a claim, never a physical completion check.
            long capacity = checked(growthState!.BelievedCompletedBerths + c.Berths);
            c.BelievedBerths = c.Berths; c.CapacityEvidenceMessageId = message.Id; c.AcceptedReportId = message.Id; c.AcceptedTick = processingTick; growthState.BelievedCompletedBerths = capacity;
            Emit("ConstructionCapacityAccepted", messageId: message.Id, attemptId: attempt.Id, detail: "project=" + c.ProjectId + "; believed=" + capacity + "; retired=" + c.Berths);
            RequestGrowthReview(processingTick);
        }
        private long SelectConstructionWorker(IAuthorityView view, long project, long excluded = 0)
        {
            var spec = ConstructionSpecification(project);
            return view.Personnel.Select(Candidate).Where(c => c.Person.ActorId != excluded && !c.Committed && c.KnownOrigin.HasValue && c.Person.Roster != null && c.Person.Roster.OnDuty(view.Tick) && spec.Roles.Contains(c.Person.Role, StringComparer.Ordinal))
                .Select(c => (Id: c.Person.ActorId, Cost: TravelEstimates.Authority(c.KnownOrigin!.Value, spec.Site, view.NominalTravelTicks)))
                .OrderBy(c => c.Cost).ThenBy(c => c.Id).Select(c => c.Id).FirstOrDefault();
        }
        private bool DispatchGrowthProjects()
        {
            bool pending = false;
            foreach (var c in growthCommitments.Values.Where(c => c.AcceptedReportId == 0))
            {
                var job = jobs[c.JobId]; if (job.CurrentAttemptId > 0) continue;
                long actor = SelectConstructionWorker(AuthorityView(), c.ProjectId);
                if (actor == 0) { pending = true; continue; }
                var attempt = CreateWorkAttempt(processingTick, new AssignmentSeed("growth/" + c.ProjectId + "/initial", job.Id, new Id<ActorTag>(actor), checked(processingTick + Authority!.ResponseOrderTicks)));
                DispatchWork(attempt.Id, c.CauseId);
            }
            return pending;
        }
    }
}
