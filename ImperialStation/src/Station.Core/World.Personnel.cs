using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public PersonnelConfig? Personnel { get; private set; }
        private readonly SortedDictionary<(long Action, long Version), QueryDisclosureSnapshot> disclosures = new SortedDictionary<(long, long), QueryDisclosureSnapshot>();
        private readonly SortedDictionary<long, long> verificationReportIds = new SortedDictionary<long, long>();
        private readonly SortedDictionary<long, CaseFilingSnapshot> caseFilings = new SortedDictionary<long, CaseFilingSnapshot>();
        private readonly SortedDictionary<long, DisciplinaryCaseSnapshot> cases = new SortedDictionary<long, DisciplinaryCaseSnapshot>();
        private readonly SortedDictionary<string, long> caseKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<long, ReprimandSnapshot> reprimands = new SortedDictionary<long, ReprimandSnapshot>();
        private readonly SortedDictionary<string, long> filingKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<long, long> caseReplies = new SortedDictionary<long, long>();
        private long nextCase = 1;
        public IReadOnlyList<QueryDisclosureSnapshot> InspectQueryDisclosures() => disclosures.Values.Select(d => d.Copy()).ToArray();
        public IReadOnlyList<DisciplinaryCaseSnapshot> InspectCases() => cases.Values.Select(c => c.Copy()).ToArray();
        public IReadOnlyList<ReprimandSnapshot> InspectReprimands() => reprimands.Values.Select(c => c.Copy()).ToArray();
        public IReadOnlyList<CaseFilingSnapshot> InspectCaseFilings(Id<ActorTag> actor) => caseFilings.Values.Where(f => f.ActorId == actor.Value).Select(f => f.Copy()).ToArray();
        private void InitializePersonnel(PersonnelConfig config)
        { if (Information == null || !JobsEnabled || Authority == null) throw new ArgumentException("Personnel requires information, jobs and the authority's registrations."); Personnel = config; }
        private void RecordQueryDisclosure(Actor actor, ActiveAction action, RecordEntry entry, long capturedVersion)
        {
            if (Personnel == null || !(entry.Kind is RecordKind.WorkAttempt or RecordKind.WorkReport)) return;
            var d = new QueryDisclosureSnapshot { ActionId = action.Id.Value, ActorId = actor.Id.Value, AccountId = actor.Account.Value, TerminalId = action.ServiceId,
                RecordVersion = entry.Version, CapturedVersion = capturedVersion, CompletedTick = processingTick };
            disclosures.Add((d.ActionId, d.RecordVersion), d);
            records.Append(RecordKind.QueryDisclosure, disclosures.Count, d.AccountId, processingTick, "query-disclosure/" + d.ActionId + "/" + d.RecordVersion,
                new[] { Number(AttributeKind.ActorId, d.ActorId), Number(AttributeKind.TerminalId, d.TerminalId), Number(AttributeKind.QueryActionId, d.ActionId),
                    Number(AttributeKind.QueriedVersion, d.CapturedVersion), Number(AttributeKind.EvidenceVersion, d.RecordVersion) });
        }
        private void QueueCaseFiling(AuditSnapshot audit)
        {
            if (Personnel == null || audit.Finding?.Attributable != true || caseFilings.ContainsKey(audit.VerificationAttemptId) || !verificationReportIds.TryGetValue(audit.VerificationAttemptId, out long source)) return;
            var f = audit.Finding; var actor = actors[new Id<ActorTag>(audit.ActorId)];
            var payload = new CaseSubmissionPayload(f.JobId, f.AttemptId, f.MachineId, f.AccountId, f.Reason, AccountabilityPolicy.Version, source, audit.AttemptVersion, audit.ReportVersion,
                audit.Evidence.Select((row, i) => new CaseEvidenceReference(row.Version, audit.QueryActionIds[i])));
            var filing = new CaseFilingSnapshot { VerificationAttemptId = audit.VerificationAttemptId, ActorId = audit.ActorId, InspectionReportId = source };
            caseFilings.Add(filing.VerificationAttemptId, filing);
            var draft = new MessageDraft(MessageKind.FileCase, RecipientKind.Authority, default, new FactKey(SubjectKind.Attempt, f.AttemptId, AttributeKind.CaseStatus),
                new FactValue(FactValueKind.Text, text: "FileCase"), processingTick, f.JobId, f.AttemptId, source, operationKey: "file-case/" + audit.VerificationAttemptId, caseSubmission: payload);
            if (!reportAgents.TryGetValue(actor.Id, out var agent)) { agent = new ReportAgent { Actor = actor.Id, Backoff = planner.InitialBackoff }; reportAgents.Add(actor.Id, agent); }
            QueueReport(agent, draft); RequestReportDecision(agent, processingTick);
            Emit("CaseFilingQueued", actor, attemptId: audit.VerificationAttemptId, detail: payload.CanonicalKey);
        }
        private void PersonnelMessagePosted(Actor actor, MessageSnapshot message)
        {
            if (Personnel == null) return;
            var draft = message.Draft;
            if (draft.Kind == MessageKind.WorkCompleted && draft.InspectionReport != null && localOrders.TryGetValue(draft.AttemptId, out var order) && order.ActorId == actor.Id.Value && order.Kind == WorkKind.VerifyMachine && draft.TaskId == order.JobId)
            {
                if (!verificationReportIds.ContainsKey(order.AttemptId)) verificationReportIds.Add(order.AttemptId, message.Id);
                if (audits.TryGetValue(order.AttemptId, out var audit)) QueueCaseFiling(audit);
            }
            if (draft.Kind == MessageKind.FileCase && draft.CaseSubmission != null)
            {
                var filing = caseFilings.Values.FirstOrDefault(f => f.ActorId == actor.Id.Value && f.InspectionReportId == draft.CaseSubmission.InspectionReportId && f.Status == CaseFilingStatus.Queued);
                if (filing != null) { filing.FilingMessageId = message.Id; filing.Status = CaseFilingStatus.Posted; }
            }
        }
        private static long RecordNumber(RecordEntry row, AttributeKind key) => row.Fields.Where(f => f.Attribute == key && f.Value.Kind == FactValueKind.Number).Select(f => f.Value.Number).DefaultIfEmpty(-1).Single();
        private static string RecordText(RecordEntry row, AttributeKind key) => row.Fields.Where(f => f.Attribute == key && f.Value.Kind == FactValueKind.Text).Select(f => f.Value.Text).DefaultIfEmpty("").Single();
        private RecordEntry[] CaseAttemptHistory(long machine) => records.History.Values.Where(r => r.Kind == RecordKind.WorkAttempt && RecordNumber(r, AttributeKind.MachineId) == machine && RecordText(r, AttributeKind.TaskKind) == "RepairMachine").ToArray();
        private string ValidateCaseSubmission(MessageSnapshot message, long? historicalTick = null, long throughRecordVersion = long.MaxValue)
        {
            var p = message.Draft.CaseSubmission!;
            long at = historicalTick ?? processingTick;
            if (Personnel == null) return "PersonnelUnavailable";
            if (RegisteredSender(message)?.Role != Personnel.SupervisorRole || message.TerminalId < 1 || message.SessionId < 1 || message.SubmittedTick < 0) return "RequesterPermissionDenied";
            if (!CasePolicy.SupportedPolicy(p)) return "UnsupportedAccountabilityPolicy";
            if (!messages.TryGetValue(p.InspectionReportId, out var inspection) || inspection.PhysicalSender != message.PhysicalSender || inspection.SenderAccount != message.SenderAccount ||
                inspection.Draft.Kind != MessageKind.WorkCompleted || inspection.Draft.InspectionReport == null || inspection.TerminalId < 1 || inspection.SessionId < 1 || inspection.SubmittedTick < 0 || inspection.SubmittedTick > message.SubmittedTick ||
                !inspection.Draft.Subject.Equals(new FactKey(SubjectKind.Machine, p.MachineId, AttributeKind.Operational)) || !inspection.Draft.Claim.Equals(FactValue.Boolean(false)) ||
                !records.History.Values.Any(r => r.Kind == RecordKind.WorkReport && r.EntityId == inspection.Id && r.Tick <= at && r.Version <= throughRecordVersion) ||
                !records.History.Values.Any(r => r.Kind == RecordKind.WorkAttempt && r.EntityId == inspection.Draft.AttemptId && r.AccountId == message.SenderAccount.Value && RecordText(r, AttributeKind.TaskKind) == "VerifyMachine" && RecordNumber(r, AttributeKind.MachineId) == p.MachineId)) return "InvalidInspectionProvenance";
            var rows = new List<RecordEntry>();
            long assignedTerminal = 0;
            foreach (var reference in p.Evidence)
            {
                if (!records.History.TryGetValue(reference.RecordVersion, out var row) || !(row.Kind is RecordKind.WorkAttempt or RecordKind.WorkReport) ||
                    !disclosures.TryGetValue((reference.QueryActionId, reference.RecordVersion), out var receipt) || receipt.ActorId != message.PhysicalSender.Value || receipt.AccountId != message.SenderAccount.Value || receipt.CompletedTick > message.SubmittedTick ||
                    receipt.RecordVersion > (row.Kind == RecordKind.WorkAttempt ? p.AttemptVersion : p.ReportVersion) || receipt.CapturedVersion < (row.Kind == RecordKind.WorkAttempt ? p.AttemptVersion : p.ReportVersion) ||
                    !recordAccess.TryGetValue((receipt.ActorId, receipt.TerminalId, row.Kind), out var grant) || grant.RequiredRole != Personnel.SupervisorRole) return "InvalidQueryProvenance";
                if (assignedTerminal != 0 && assignedTerminal != receipt.TerminalId) return "MixedRecordsTerminals";
                assignedTerminal = receipt.TerminalId; rows.Add(row);
            }
            var specification = inspection.Draft.InspectionReport;
            if (publicKnowledge?.Work == null || !publicKnowledge.Work.Resources.Any(r => r.Id == specification.RequiredResource) ||
                !publicKnowledge.Work.Resources.Any(r => r.Id == specification.InstalledResource)) return "InvalidInspectionEvidenceShape";
            var attempts = CaseAttemptHistory(p.MachineId).Where(r => r.Tick <= at && r.Version <= throughRecordVersion).ToArray();
            var reports = records.History.Values.Where(r => r.Kind == RecordKind.WorkReport && r.Tick <= at && r.Version <= throughRecordVersion && attempts.Any(a => a.EntityId == RecordNumber(r, AttributeKind.AttemptId))).ToArray();
            return CasePolicy.EvidenceStandard(p, inspection.Draft.InspectionReport, inspection.Draft.ObservedTick, attempts, reports, rows);
        }
        public int QualifyingReprimandCount(long account, long tick)
            => QualifyingReprimandCases(account, tick).Length;
        private long[] QualifyingReprimandCases(long account, long tick, long throughCaseId = long.MaxValue, long throughRecordVersion = long.MaxValue) =>
            ReprimandPolicy.QualifyingCases(Personnel, account, tick, records.History.Values, reprimands.Values.Select(r => new ReprimandEntry(r.CaseId, r.AccountId, r.AcceptedTick)), throughCaseId, throughRecordVersion);
        private bool ReceiveCaseSubmission(MessageSnapshot message, long cause)
        {
            if (message.Draft.Kind != MessageKind.FileCase) return true;
            var p = message.Draft.CaseSubmission!; string key = message.SenderAccount.Value + "/" + message.Draft.OperationKey;
            long validationVersion = records.Version;
            string validation = ValidateCaseSubmission(message); long caseId = 0;
            bool conflict = filingKeys.TryGetValue(key, out long previous) && !SameDraft(messages[previous].Draft, message.Draft);
            bool known = caseKeys.TryGetValue(p.CanonicalKey, out long existing); var canonical = known ? cases[existing] : null;
            var choice = CasePolicy.Decide(validation, conflict, known, canonical?.Submission.AccountId ?? 0, canonical?.FilingMessageId ?? 0, p.AccountId, message.Id);
            string reason = choice.Reason;
            if (choice.Outcome == CaseAcceptance.AlreadyAccepted) caseId = existing;
            else if (choice.Outcome == CaseAcceptance.OpenCase)
            {
                caseId = nextCase++;
                cases.Add(caseId, new DisciplinaryCaseSnapshot { Id = caseId, FilingMessageId = message.Id, AcceptedTick = processingTick, Submission = p, ValidationRecordVersion = validationVersion }); caseKeys.Add(p.CanonicalKey, caseId);
                reprimands.Add(caseId, new ReprimandSnapshot { CaseId = caseId, AccountId = p.AccountId, AcceptedTick = processingTick, Policy = p.Policy });
                var fields = new[] { Number(AttributeKind.TaskId, p.JobId), Number(AttributeKind.AttemptId, p.AttemptId), Number(AttributeKind.MachineId, p.MachineId), Number(AttributeKind.MessageId, message.Id), Text(AttributeKind.Violation, p.Violation), Text(AttributeKind.Policy, p.Policy) };
                records.Append(RecordKind.Case, caseId, p.AccountId, processingTick, "case/" + p.CanonicalKey, fields);
                records.Append(RecordKind.Reprimand, caseId, p.AccountId, processingTick, "reprimand/" + caseId, new[] { Number(AttributeKind.CaseId, caseId), Text(AttributeKind.Policy, p.Policy) });
                Emit("ReprimandAccepted", messageId: message.Id, causeId: cause, detail: "case=" + caseId + "; account=" + p.AccountId + "; " + p.CanonicalKey);
            }
            if (!filingKeys.ContainsKey(key)) filingKeys.Add(key, message.Id);
            // A reply travels through the mailbox. This does not update a supervisor's private goal.
            if (RegisteredAccount(message.SenderAccount))
            {
                var decision = new CaseDecisionPayload(message.Id, caseId, p.AccountId, caseId == 0 ? 0 : QualifyingReprimandCount(p.AccountId, processingTick), reason, nextCase - 1, validationVersion);
                long reply = PostAuthorityWork(new MessageDraft(MessageKind.CaseDecision, RecipientKind.Account, message.SenderAccount, message.Draft.Subject,
                    new FactValue(FactValueKind.Text, text: reason), processingTick, p.JobId, p.AttemptId, message.Id, operationKey: "case-reply/" + message.Id, caseDecision: decision), cause);
                caseReplies.Add(message.Id, reply);
            }
            Emit("CaseSubmissionDecided", messageId: message.Id, causeId: cause, detail: reason + "; case=" + caseId); return false;
        }
        private void ReadCaseDecision(Actor actor, MessageSnapshot message)
        {
            var d = message.Draft.CaseDecision;
            if (message.Draft.Kind != MessageKind.CaseDecision || message.SenderAccount.Value != 0 || d == null) return;
            var filing = caseFilings.Values.FirstOrDefault(f => f.ActorId == actor.Id.Value && f.FilingMessageId == d.FilingMessageId && f.Status == CaseFilingStatus.Posted);
            if (filing == null) return;
            filing.ReplyMessageId = message.Id; filing.ReadTick = processingTick; filing.CaseId = d.CaseId; filing.QualifyingCount = d.QualifyingCount; filing.Reason = d.Reason;
            filing.Status = d.Accepted ? CaseFilingStatus.Accepted : CaseFilingStatus.NeedsReview;
            Emit("CaseDecisionRead", actor, messageId: message.Id, detail: d.Reason + "; count=" + d.QualifyingCount);
        }
        private static bool SamePersonnelPayloads(MessageDraft a, MessageDraft b) =>
            (a.InspectionReport == null ? b.InspectionReport == null : b.InspectionReport != null && a.InspectionReport.Same(b.InspectionReport)) &&
            (a.CaseSubmission == null ? b.CaseSubmission == null : b.CaseSubmission != null && a.CaseSubmission.Same(b.CaseSubmission)) &&
            (a.CaseDecision == null ? b.CaseDecision == null : b.CaseDecision != null && a.CaseDecision.Same(b.CaseDecision));
        private static bool ValidPersonnelDraft(MessageDraft draft)
        {
            if (draft.CaseSubmission != null && draft.Kind != MessageKind.FileCase || draft.CaseDecision != null && draft.Kind != MessageKind.CaseDecision) return false;
            if (draft.InspectionReport != null && (draft.Kind != MessageKind.WorkCompleted || draft.RecipientKind != RecipientKind.Authority || draft.Subject.SubjectKind != SubjectKind.Machine || draft.Subject.Attribute != AttributeKind.Operational || draft.Claim.Kind != FactValueKind.Boolean)) return false;
            if (draft.Kind == MessageKind.FileCase)
            {
                var p = draft.CaseSubmission;
                return p != null && draft.RecipientKind == RecipientKind.Authority && !string.IsNullOrWhiteSpace(draft.OperationKey) && draft.TaskId == p.JobId && draft.AttemptId == p.AttemptId && draft.RelatedMessageId == p.InspectionReportId &&
                    draft.Subject.Equals(new FactKey(SubjectKind.Attempt, p.AttemptId, AttributeKind.CaseStatus)) && draft.Claim.Equals(new FactValue(FactValueKind.Text, text: "FileCase")) && draft.WorkOrder == null && draft.Roster == null && draft.PatrolOrder == null;
            }
            if (draft.Kind == MessageKind.CaseDecision)
                return draft.CaseDecision != null && draft.RecipientKind == RecipientKind.Account && draft.RelatedMessageId == draft.CaseDecision.FilingMessageId && draft.Claim.Equals(new FactValue(FactValueKind.Text, text: draft.CaseDecision.Reason)) &&
                    draft.WorkOrder == null && draft.Roster == null && draft.PatrolOrder == null && draft.Subject.Attribute == AttributeKind.CaseStatus;
            return true;
        }
    }
}
