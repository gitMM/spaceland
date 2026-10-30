using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public const int AuditPageLimit = 64;
        public const int AuditEvidenceLimit = 32;
        private readonly SortedDictionary<long, AuditSnapshot> audits = new SortedDictionary<long, AuditSnapshot>();
        public IReadOnlyList<AuditSnapshot> InspectAudits(Id<ActorTag> actor) => audits.Values.Where(a => a.ActorId == actor.Value).Select(a => a.Copy()).ToArray();
        private void BeginAudit(VerificationInspectionSnapshot inspection)
        {
            if (!inspection.Discrepancy || audits.ContainsKey(inspection.AttemptId)) return;
            var actor = actors[new Id<ActorTag>(inspection.ActorId)];
            long terminal = AuditPlanning.ChooseTerminal(recordAccess.Values.Where(g => g.Actor == actor.Id).Select(g => (g.TerminalId, g.Kind, g.RequiredRole)), actor.Role);
            var audit = new AuditSnapshot { VerificationAttemptId = inspection.AttemptId, ActorId = actor.Id.Value, TerminalId = terminal, Backoff = planner.InitialBackoff };
            audits.Add(audit.VerificationAttemptId, audit);
            Emit("DiscrepancyAuditOpened", actor, attemptId: audit.VerificationAttemptId, terminalId: terminal, detail: "ResolveJobDiscrepancy");
            if (terminal == 0) FinishAudit(audit, "NoAssignedRecordsTerminal"); else RequestAudit(audit, processingTick);
        }
        private void FinishAudit(AuditSnapshot audit, string reason, AccountabilityFinding? finding = null)
        {
            audit.Finding = finding; audit.Reason = reason; audit.Status = AuditPlanning.ReadyToFile(finding) ? AuditStatus.ReadyToFile : AuditStatus.NeedsReview;
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateAudit && e.Value == audit.VerificationAttemptId).ToArray()) scheduled.Remove(e);
            audit.NextDecision = -1;
            QueueCaseFiling(audit);
            if (audit.TerminalId > 0) Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.ReleaseIdleTerminal,
                Intent = new ActionIntent(new Id<ActorTag>(audit.ActorId), ActionKind.Logout, default) });
            Emit("DiscrepancyAuditResolved", actors[new Id<ActorTag>(audit.ActorId)], attemptId: audit.VerificationAttemptId, terminalId: audit.TerminalId,
                detail: audit.Status + "; " + reason + "; policy=" + AccountabilityPolicy.Version);
        }
        private void RequestAudit(AuditSnapshot audit, long tick)
        {
            if (!audit.Pending || audit.NextDecision >= 0) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            audit.NextDecision = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateAudit, Value = audit.VerificationAttemptId });
        }
        private void WakeAudits(Actor actor, bool failed = false, ActiveAction? completed = null)
        {
            if (actor.Action != null) return;
            foreach (var audit in audits.Values.Where(a => a.ActorId == actor.Id.Value && a.Pending))
            {
                if (completed != null && AuditPlanning.ProgressResetsBackoff(completed.Kind, completed.ServiceId, audit.TerminalId))
                    audit.Backoff = planner.InitialBackoff;
                RequestAudit(audit, failed ? checked(processingTick + audit.Backoff) : processingTick);
                if (failed) audit.Backoff = NextWorkBackoff(audit.Backoff);
            }
        }
        private RecordQuery AuditQuery(AuditSnapshot audit) => new RecordQuery(audit.Status == AuditStatus.QueryingAttempts ? RecordKind.WorkAttempt : RecordKind.WorkReport, after: audit.Cursor, limit: 1, history: true, machineId: verificationInspections[audit.VerificationAttemptId].MachineId);
        private void EvaluateAudit(long id)
        {
            var audit = audits[id]; audit.NextDecision = -1; if (!audit.Pending) return;
            var actor = actors[new Id<ActorTag>(audit.ActorId)];
            if (EscortControls(actor.Id) || actor.Action != null || DeferToNeed(actor) || DeferToDueGoal(actor) || !KnownOnDuty(actor.Id) || AuditDefers(actor, id)) return;
            if (!SpendPlanningRequest()) { RequestAudit(audit, checked(processingTick + 1)); return; }
            var plan = ReportPlanning.PlanRecords(ActorView(actor.Id), AuditQuery(audit), audit.TerminalId, Information!.LoginTicks, Information.QueryTicks, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += plan.Expansions;
            if (plan.Outcome == PlanOutcome.PlanFound && plan.Steps.Count > 0)
            {
                // Finding the same plan is not evidence that an offline service
                // recovered. Reset the retry delay only after physical progress.
                PlansFound++; audit.Reason = "";
                long cause = Emit("PlanSelected", actor, attemptId: id, terminalId: audit.TerminalId, detail: "audit-records/v1; " + string.Join(" -> ", plan.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), Intent = plan.Steps[0].Intent, CauseId = cause }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
            audit.Reason = AuditPlanning.PlanFailureReason(plan.Outcome);
            RequestAudit(audit, checked(processingTick + audit.Backoff)); audit.Backoff = NextWorkBackoff(audit.Backoff);
        }
        private bool AuditDefers(Actor actor, long id) => AuditPlanning.DefersToOtherWork(
            audits.Values.Any(a => a.ActorId == actor.Id.Value && a.Pending && a.VerificationAttemptId < id), needAgents.ContainsKey(actor.Id.Value),
            reportAgents.TryGetValue(actor.Id, out var report) && report.Pending.Count > 0, checkIns.TryGetValue(actor.Id, out var checkIn) && (checkIn.Due || checkIn.DueTick == processingTick),
            localOrders.Values.Any(o => o.ActorId == actor.Id.Value && o.Status is LocalOrderStatus.Read or LocalOrderStatus.AcknowledgmentPending or LocalOrderStatus.Working));
        private void AuditQueryCompleted(Actor actor, ActiveAction action)
        {
            var audit = audits.Values.FirstOrDefault(a => a.ActorId == actor.Id.Value && a.Pending);
            if (audit == null || action.ServiceId != audit.TerminalId || action.Query == null) return;
            var query = AuditQuery(audit);
            if (action.Query.Kind != query.Kind || action.Query.EntityId != 0 || action.Query.After != query.After || action.Query.Limit != 1 || !action.Query.History || action.Query.MachineId != query.MachineId) return;
            var receipt = recordReceipts[actor.Id]; audit.Pages++;
            if (audit.Status == AuditStatus.QueryingAttempts && audit.AttemptVersion < 0) audit.AttemptVersion = receipt.Version;
            if (audit.Status == AuditStatus.QueryingReports && audit.ReportVersion < 0) audit.ReportVersion = receipt.Version;
            long cutoff = audit.Status == AuditStatus.QueryingAttempts ? audit.AttemptVersion : audit.ReportVersion;
            audit.RecordVersion = Math.Max(audit.RecordVersion, cutoff);
            // Only facts this completed action taught are offered to the audit decision.
            var learned = actor.Knowledge.Entries.Values.Select(e => e.Fact).Where(f => f.Key.SubjectKind == SubjectKind.Record && f.SourceKind == EvidenceKind.RecordQuery && f.SourceId == action.Id.Value).ToArray();
            var v = verificationInspections[audit.VerificationAttemptId];
            var page = AuditPlanning.ReadPage(new AuditPageInput(audit.Status == AuditStatus.QueryingAttempts, query.Kind, cutoff, audit.Pages, AuditPageLimit, AuditEvidenceLimit, receipt, action.Id.Value, learned,
                audit.Evidence, audit.QueryActionIds, v.MachineId, v.RequiredResource, v.InstalledResource, v.Tick));
            audit.Evidence = page.Evidence; audit.QueryActionIds = page.QueryActionIds;
            if (page.Cursor.HasValue) audit.Cursor = page.Cursor.Value;
            if (page.AdvanceToReports) audit.Status = AuditStatus.QueryingReports;
            if (page.FinishReason != null) FinishAudit(audit, page.FinishReason, page.Finding);
        }
    }
}
