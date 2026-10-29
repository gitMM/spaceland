using System;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private void CaptureVerification(Actor actor, ActiveAction action)
        {
            if (verificationInspections.ContainsKey(action.AttemptId)) return;
            KnowledgeFact? Fact(AttributeKind attribute) => actor.Knowledge.Entries.TryGetValue(new FactKey(SubjectKind.Machine, action.ServiceId, attribute), out var f) &&
                f.Fact.SourceKind == EvidenceKind.Inspection && f.Fact.SourceId == action.Id.Value && f.Fact.ObservedTick == processingTick ? f.Fact : null;
            var operational = Fact(AttributeKind.Operational); var required = Fact(AttributeKind.RequiredResource); var installed = Fact(AttributeKind.InstalledResource);
            if (operational?.Value.Kind != FactValueKind.Boolean || required?.Value.Kind != FactValueKind.Text || installed?.Value.Kind != FactValueKind.Text) return;
            var result = new VerificationInspectionSnapshot { ActorId = actor.Id.Value, AttemptId = action.AttemptId, MachineId = action.ServiceId, ActionId = action.Id.Value,
                Tick = processingTick, Operational = operational.Value.Number != 0, RequiredResource = required.Value.Text, InstalledResource = installed.Value.Text };
            verificationInspections.Add(action.AttemptId, result);
            Emit("VerificationInspected", actor, action.Id, attemptId: action.AttemptId, detail: result.Discrepancy ? "ResolveJobDiscrepancy" : "NoDiscrepancy", causeId: action.CauseId);
        }
        private void EvaluateVerificationOrder(LocalWorkOrderSnapshot order)
        {
            var actor = actors[new Id<ActorTag>(order.ActorId)]; if (actor.Action != null || processingTick >= order.Expires) return;
            if (localOrders.Values.Any(o => o.ActorId == order.ActorId && o.Status == LocalOrderStatus.Working && o.Kind != WorkKind.RepairMachine && o.AttemptId < order.AttemptId)) return;
            if (verificationInspections.TryGetValue(order.AttemptId, out var result))
            {
                order.Status = LocalOrderStatus.Completed; order.Reason = result.Discrepancy ? "ResolveJobDiscrepancy" : "VerifiedClear";
                var draft = new MessageDraft(MessageKind.WorkCompleted, RecipientKind.Authority, default, new FactKey(SubjectKind.Machine, order.MachineId, AttributeKind.Operational),
                    FactValue.Boolean(result.Operational), result.Tick, order.JobId, order.AttemptId, order.OrderMessageId, operationKey: "verification/" + order.AttemptId,
                    inspectionReport: Personnel == null ? null : new InspectionReportPayload(result.ActionId, result.RequiredResource, result.InstalledResource));
                if (!reportAgents.TryGetValue(actor.Id, out var agent)) { agent = new ReportAgent { Actor = actor.Id, Backoff = planner.InitialBackoff }; reportAgents.Add(actor.Id, agent); }
                QueueReport(agent, draft); RequestReportDecision(agent, processingTick); UnpinOrder(actor, order.AttemptId); TriggerWorkCheckIn(actor.Id); BeginAudit(result); return;
            }
            if (!KnownOnDuty(actor.Id) || !needAgents.ContainsKey(actor.Id.Value) && (reportAgents.TryGetValue(actor.Id, out var report) && report.Pending.Count > 0 || checkIns.TryGetValue(actor.Id, out var checkIn) && (checkIn.Due || checkIn.DueTick == processingTick))) return;
            if (!SpendPlanningRequest()) { RequestOrderDecision(order, checked(processingTick + 1)); return; }
            var plan = VerificationPlanning.Plan(ActorView(actor.Id), order.MachineId, planner.MaximumDepth, planner.NodeBudget); PlannerExpansions += plan.Expansions;
            if (plan.Outcome == PlanOutcome.PlanFound && plan.Steps.Count > 0)
            {
                PlansFound++; order.Backoff = planner.InitialBackoff; var first = plan.Steps[0].Intent;
                long cause = Emit("PlanSelected", actor, attemptId: order.AttemptId, messageId: order.OrderMessageId, detail: "verify-known-machine/v1; " + string.Join(" -> ", plan.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), CauseId = cause,
                    Intent = new ActionIntent(first.Actor, first.Kind, first.Target, first.Item, serviceId: first.ServiceId, attemptId: order.AttemptId) }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; order.Reason = "PlanBudgetExhausted"; }
            else { PlansUnavailable++; order.Reason = "NoKnownVerificationRoute"; }
            SetAttemptExecution(order.AttemptId, WorkExecution.Blocked);
            if (!order.BlockedReported) { order.BlockedReported = true; QueueOrderReport(order, MessageKind.WorkBlocked, order.Reason); TriggerWorkCheckIn(actor.Id); }
            RequestOrderDecision(order, checked(processingTick + order.Backoff)); order.Backoff = NextWorkBackoff(order.Backoff);
        }
    }
}
