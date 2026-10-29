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
            long terminal = recordAccess.Values.Where(g => g.Actor == actor.Id && g.RequiredRole == actor.Role && g.Kind == RecordKind.WorkAttempt &&
                recordAccess.TryGetValue((actor.Id.Value, g.TerminalId, RecordKind.WorkReport), out var reports) && reports.RequiredRole == actor.Role).Select(g => g.TerminalId).FirstOrDefault();
            var audit = new AuditSnapshot { VerificationAttemptId = inspection.AttemptId, ActorId = actor.Id.Value, TerminalId = terminal, Backoff = planner.InitialBackoff };
            audits.Add(audit.VerificationAttemptId, audit);
            Emit("DiscrepancyAuditOpened", actor, attemptId: audit.VerificationAttemptId, terminalId: terminal, detail: "ResolveJobDiscrepancy");
            if (terminal == 0) FinishAudit(audit, "NoAssignedRecordsTerminal"); else RequestAudit(audit, processingTick);
        }
        private void FinishAudit(AuditSnapshot audit, string reason, AccountabilityFinding? finding = null)
        {
            audit.Finding = finding; audit.Reason = reason; audit.Status = finding?.Attributable == true ? AuditStatus.ReadyToFile : AuditStatus.NeedsReview;
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
        private void WakeAudits(Actor actor, bool failed = false)
        {
            if (actor.Action != null) return;
            foreach (var audit in audits.Values.Where(a => a.ActorId == actor.Id.Value && a.Pending)) RequestAudit(audit, failed ? checked(processingTick + audit.Backoff) : processingTick);
        }
        private RecordQuery AuditQuery(AuditSnapshot audit) => new RecordQuery(audit.Status == AuditStatus.QueryingAttempts ? RecordKind.WorkAttempt : RecordKind.WorkReport, after: audit.Cursor, limit: 1, history: true, machineId: verificationInspections[audit.VerificationAttemptId].MachineId);
        private void EvaluateAudit(long id)
        {
            var audit = audits[id]; audit.NextDecision = -1; if (!audit.Pending) return;
            var actor = actors[new Id<ActorTag>(audit.ActorId)];
            if (EscortControls(actor.Id) || actor.Action != null || DeferToNeed(actor) || DeferToDueGoal(actor) || !KnownOnDuty(actor.Id) || audits.Values.Any(a => a.ActorId == actor.Id.Value && a.Pending && a.VerificationAttemptId < id) ||
                !needAgents.ContainsKey(actor.Id.Value) && (reportAgents.TryGetValue(actor.Id, out var report) && report.Pending.Count > 0 || checkIns.TryGetValue(actor.Id, out var checkIn) && (checkIn.Due || checkIn.DueTick == processingTick)) ||
                localOrders.Values.Any(o => o.ActorId == actor.Id.Value && o.Status is LocalOrderStatus.Read or LocalOrderStatus.AcknowledgmentPending or LocalOrderStatus.Working)) return;
            if (!SpendPlanningRequest()) { RequestAudit(audit, checked(processingTick + 1)); return; }
            var plan = ReportPlanning.PlanRecords(ActorView(actor.Id), AuditQuery(audit), audit.TerminalId, Information!.LoginTicks, Information.QueryTicks, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += plan.Expansions;
            if (plan.Outcome == PlanOutcome.PlanFound && plan.Steps.Count > 0)
            {
                PlansFound++; audit.Backoff = planner.InitialBackoff; audit.Reason = "";
                long cause = Emit("PlanSelected", actor, attemptId: id, terminalId: audit.TerminalId, detail: "audit-records/v1; " + string.Join(" -> ", plan.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), Intent = plan.Steps[0].Intent, CauseId = cause }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; audit.Reason = "PlanBudgetExhausted"; }
            else { PlansUnavailable++; audit.Reason = "NoKnownRecordsRouteOrCredential"; }
            RequestAudit(audit, checked(processingTick + audit.Backoff)); audit.Backoff = NextWorkBackoff(audit.Backoff);
        }
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
            // Read only facts actually learned by this completed action. CapturedRecords is a service
            // implementation detail and is deliberately unavailable to this citizen decision.
            var groups = actor.Knowledge.Entries.Values.Select(e => e.Fact).Where(f => f.Key.SubjectKind == SubjectKind.Record && f.SourceKind == EvidenceKind.RecordQuery && f.SourceId == action.Id.Value).GroupBy(f => f.Key.SubjectId).ToArray();
            if (groups.Length != receipt.DisclosedRows) { FinishAudit(audit, "IncompleteDisclosure"); return; }
            foreach (var group in groups)
            {
                FactValue? Field(AttributeKind key) => group.Where(f => f.Key.Attribute == key).Select(f => (FactValue?)f.Value).SingleOrDefault();
                long Num(AttributeKind key) => Field(key) is FactValue f && f.Kind == FactValueKind.Number ? f.Number : -1;
                var metadata = new[] { AttributeKind.RecordType, AttributeKind.RecordEntityId, AttributeKind.RecordVersion, AttributeKind.AccountId, AttributeKind.RecordFieldCount };
                var fields = group.Where(f => !metadata.Contains(f.Key.Attribute)).Select(f => new RecordField(f.Key.Attribute, f.Value)).ToArray();
                if (Num(AttributeKind.RecordVersion) != group.Key || Num(AttributeKind.RecordEntityId) < 1 || Num(AttributeKind.AccountId) < 1 || Num(AttributeKind.RecordFieldCount) != fields.Length ||
                    Field(AttributeKind.RecordType)?.Text != query.Kind.ToString() || group.Select(f => f.ObservedTick).Distinct().Count() != 1)
                { FinishAudit(audit, "IncompleteDisclosure"); return; }
                if (group.Key > cutoff) continue;
                var row = new RecordEntry(query.Kind, Num(AttributeKind.RecordEntityId), Num(AttributeKind.AccountId), group.Key, group.First().ObservedTick, "disclosed/" + group.Key, fields);
                var inspection = verificationInspections[audit.VerificationAttemptId];
                bool relevant = row.Kind == RecordKind.WorkAttempt ? fields.Any(f => f.Attribute == AttributeKind.MachineId && f.Value.Equals(FactValue.Integer(inspection.MachineId))) && fields.Any(f => f.Attribute == AttributeKind.TaskKind && f.Value.Text == "RepairMachine") :
                    audit.Evidence.Any(r => r.Kind == RecordKind.WorkAttempt && fields.Any(f => f.Attribute == AttributeKind.AttemptId && f.Value.Equals(FactValue.Integer(r.EntityId))));
                if (!relevant) continue;
                if (audit.Evidence.Count >= AuditEvidenceLimit) { FinishAudit(audit, "EvidenceCapacityExceeded"); return; }
                audit.Evidence = audit.Evidence.Concat(new[] { row }).ToArray(); audit.QueryActionIds = audit.QueryActionIds.Concat(new[] { action.Id.Value }).ToArray();
            }
            audit.Cursor = Math.Min(receipt.NextCursor, cutoff);
            if (receipt.HasMore && receipt.NextCursor < cutoff)
            { if (audit.Pages >= AuditPageLimit) FinishAudit(audit, "HistoryPageLimitExceeded"); return; }
            if (audit.Status == AuditStatus.QueryingAttempts) { audit.Status = AuditStatus.QueryingReports; audit.Cursor = 0; }
            else
            {
                var v = verificationInspections[audit.VerificationAttemptId];
                var finding = AccountabilityPolicy.Assess(v.MachineId, v.RequiredResource, v.InstalledResource, v.Tick, audit.Evidence, true);
                FinishAudit(audit, finding.Reason, finding);
            }
            if (audit.Pending && audit.Pages >= AuditPageLimit) FinishAudit(audit, "HistoryPageLimitExceeded");
        }
    }
}
