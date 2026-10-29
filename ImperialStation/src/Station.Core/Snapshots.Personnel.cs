using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteInspectionPayload(BinaryWriter w, InspectionReportPayload? p)
        { w.Write(p != null); if (p == null) return; w.Write(p.ActionId); w.Write(p.RequiredResource); w.Write(p.InstalledResource); }
        private static InspectionReportPayload? ReadInspectionPayload(BinaryReader r) => r.ReadBoolean() ? new InspectionReportPayload(r.ReadInt64(), r.ReadString(), r.ReadString()) : null;
        private static void WriteCasePayload(BinaryWriter w, CaseSubmissionPayload? p)
        {
            w.Write(p != null); if (p == null) return;
            w.Write(p.JobId); w.Write(p.AttemptId); w.Write(p.MachineId); w.Write(p.AccountId); w.Write(p.Violation); w.Write(p.Policy); w.Write(p.InspectionReportId); w.Write(p.AttemptVersion); w.Write(p.ReportVersion);
            w.Write(p.Evidence.Count); foreach (var e in p.Evidence) { w.Write(e.RecordVersion); w.Write(e.QueryActionId); }
        }
        private static CaseSubmissionPayload? ReadCasePayload(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            long job = r.ReadInt64(), attempt = r.ReadInt64(), machine = r.ReadInt64(), account = r.ReadInt64(); string violation = r.ReadString(), policy = r.ReadString();
            long inspection = r.ReadInt64(), attempts = r.ReadInt64(), reports = r.ReadInt64(); int count = Count(r); if (count > 32) throw new ArgumentException("Case evidence limit exceeded.");
            var evidence = new CaseEvidenceReference[count]; for (int i = 0; i < count; i++) evidence[i] = new CaseEvidenceReference(r.ReadInt64(), r.ReadInt64());
            return new CaseSubmissionPayload(job, attempt, machine, account, violation, policy, inspection, attempts, reports, evidence);
        }
        private static void WriteCaseDecisionPayload(BinaryWriter w, CaseDecisionPayload? p)
        { w.Write(p != null); if (p == null) return; w.Write(p.FilingMessageId); w.Write(p.CaseId); w.Write(p.AccountId); w.Write(p.QualifyingCount); w.Write(p.Reason); w.Write(p.CountThroughCaseId); w.Write(p.ValidationRecordVersion); }
        private static CaseDecisionPayload? ReadCaseDecisionPayload(BinaryReader r) => r.ReadBoolean() ? new CaseDecisionPayload(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadString(), r.ReadInt64(), r.ReadInt64()) : null;
        private void WritePersonnel(BinaryWriter w)
        {
            w.Write(Personnel != null); if (Personnel != null) { w.Write(Personnel.SupervisorRole); w.Write(Personnel.QualifyingWindow); w.Write(Personnel.DetentionThreshold); }
            w.Write(nextCase); w.Write(disclosures.Count);
            foreach (var d in disclosures.Values) { w.Write(d.ActionId); w.Write(d.ActorId); w.Write(d.AccountId); w.Write(d.TerminalId); w.Write(d.RecordVersion); w.Write(d.CapturedVersion); w.Write(d.CompletedTick); }
            w.Write(verificationReportIds.Count); foreach (var pair in verificationReportIds) { w.Write(pair.Key); w.Write(pair.Value); }
            w.Write(cases.Count); foreach (var c in cases.Values) { w.Write(c.Id); w.Write(c.FilingMessageId); w.Write(c.AcceptedTick); WriteCasePayload(w, c.Submission); w.Write(c.ValidationRecordVersion); }
            w.Write(reprimands.Count); foreach (var p in reprimands.Values) { w.Write(p.CaseId); w.Write(p.AccountId); w.Write(p.AcceptedTick); w.Write(p.Policy); }
            w.Write(filingKeys.Count); foreach (var p in filingKeys) { w.Write(p.Key); w.Write(p.Value); }
            w.Write(caseReplies.Count); foreach (var p in caseReplies) { w.Write(p.Key); w.Write(p.Value); }
            w.Write(caseFilings.Count);
            foreach (var f in caseFilings.Values)
            {
                w.Write(f.VerificationAttemptId); w.Write(f.ActorId); w.Write(f.InspectionReportId); w.Write(f.FilingMessageId); w.Write(f.ReplyMessageId); w.Write(f.ReadTick); w.Write(f.CaseId); w.Write(f.QualifyingCount); w.Write((int)f.Status); w.Write(f.Reason);
            }
        }
        private void ReadPersonnel(BinaryReader r)
        {
            if (r.ReadBoolean()) InitializePersonnel(new PersonnelConfig(r.ReadString(), r.ReadInt64(), r.ReadInt32()));
            nextCase = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var d = new QueryDisclosureSnapshot { ActionId = r.ReadInt64(), ActorId = r.ReadInt64(), AccountId = r.ReadInt64(), TerminalId = r.ReadInt64(), RecordVersion = r.ReadInt64(), CapturedVersion = r.ReadInt64(), CompletedTick = r.ReadInt64() };
                if (Personnel == null || d.ActionId < 1 || d.ActionId >= nextAction || !actors.TryGetValue(new Id<ActorTag>(d.ActorId), out var actor) || actor.Account.Value != d.AccountId || !terminals.ContainsKey(d.TerminalId) ||
                    !records.History.TryGetValue(d.RecordVersion, out var row) || !(row.Kind is RecordKind.WorkAttempt or RecordKind.WorkReport) || row.Version > d.CapturedVersion || d.CapturedVersion > records.Version ||
                    d.CompletedTick < row.Tick || d.CompletedTick > LastProcessedTick ||
                    !records.History.Values.Any(e => e.Kind == RecordKind.QueryDisclosure && e.AccountId == d.AccountId && e.Tick == d.CompletedTick && e.OperationKey == "query-disclosure/" + d.ActionId + "/" + d.RecordVersion &&
                        RecordNumber(e, AttributeKind.ActorId) == d.ActorId && RecordNumber(e, AttributeKind.TerminalId) == d.TerminalId && RecordNumber(e, AttributeKind.QueryActionId) == d.ActionId && RecordNumber(e, AttributeKind.EvidenceVersion) == d.RecordVersion && RecordNumber(e, AttributeKind.QueriedVersion) == d.CapturedVersion)) throw new ArgumentException("Invalid query disclosure provenance.");
                disclosures.Add((d.ActionId, d.RecordVersion), d);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long attempt = r.ReadInt64(), messageId = r.ReadInt64();
                if (Personnel == null || !localOrders.TryGetValue(attempt, out var order) || order.Kind != WorkKind.VerifyMachine || !messages.TryGetValue(messageId, out var message) || message.Draft.Kind != MessageKind.WorkCompleted ||
                    message.Draft.InspectionReport == null || message.PhysicalSender.Value != order.ActorId || message.Draft.AttemptId != attempt || message.Draft.TaskId != order.JobId) throw new ArgumentException("Invalid locally posted inspection reference.");
                verificationReportIds.Add(attempt, messageId);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var c = new DisciplinaryCaseSnapshot { Id = r.ReadInt64(), FilingMessageId = r.ReadInt64(), AcceptedTick = r.ReadInt64(), Submission = ReadCasePayload(r) ?? throw new ArgumentException("Missing case submission."), ValidationRecordVersion = r.ReadInt64() };
                if (Personnel == null || c.Id < 1 || c.Id >= nextCase || c.AcceptedTick < 0 || c.AcceptedTick > LastProcessedTick || !messages.TryGetValue(c.FilingMessageId, out var filing) || filing.Draft.Kind != MessageKind.FileCase ||
                    filing.Stage != MessageStage.Accepted || filing.DeliveryTick != c.AcceptedTick || filing.Draft.CaseSubmission == null || !c.Submission.Same(filing.Draft.CaseSubmission) || c.ValidationRecordVersion < 1 || c.ValidationRecordVersion > records.Version || ValidateCaseSubmission(filing, c.AcceptedTick, c.ValidationRecordVersion).Length > 0 ||
                    !records.History.Values.Any(e => e.Kind == RecordKind.Case && e.EntityId == c.Id && e.AccountId == c.Submission.AccountId && e.Tick == c.AcceptedTick && RecordNumber(e, AttributeKind.MessageId) == c.FilingMessageId &&
                        RecordNumber(e, AttributeKind.TaskId) == c.Submission.JobId && RecordNumber(e, AttributeKind.AttemptId) == c.Submission.AttemptId && RecordNumber(e, AttributeKind.MachineId) == c.Submission.MachineId &&
                        RecordText(e, AttributeKind.Policy) == c.Submission.Policy && RecordText(e, AttributeKind.Violation) == c.Submission.Violation)) throw new ArgumentException("Invalid accepted case.");
                cases.Add(c.Id, c); caseKeys.Add(c.Submission.CanonicalKey, c.Id);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var p = new ReprimandSnapshot { CaseId = r.ReadInt64(), AccountId = r.ReadInt64(), AcceptedTick = r.ReadInt64(), Policy = r.ReadString() };
                if (!cases.TryGetValue(p.CaseId, out var c) || c.Submission.AccountId != p.AccountId || c.AcceptedTick != p.AcceptedTick || c.Submission.Policy != p.Policy ||
                    !records.History.Values.Any(e => e.Kind == RecordKind.Reprimand && e.EntityId == p.CaseId && e.AccountId == p.AccountId && e.Tick == p.AcceptedTick && RecordNumber(e, AttributeKind.CaseId) == p.CaseId && RecordText(e, AttributeKind.Policy) == p.Policy)) throw new ArgumentException("Invalid unique reprimand.");
                reprimands.Add(p.CaseId, p);
            }
            if (nextCase != cases.Count + 1 || reprimands.Count != cases.Count) throw new ArgumentException("Invalid case/reprimand cardinality.");
            if (records.History.Values.Count(e => e.Kind == RecordKind.Case) != cases.Count || records.History.Values.Count(e => e.Kind == RecordKind.Reprimand) != reprimands.Count ||
                records.History.Values.Count(e => e.Kind == RecordKind.QueryDisclosure) != disclosures.Count) throw new ArgumentException("Unregistered personnel record.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                string key = r.ReadString(); long id = r.ReadInt64();
                if (!messages.TryGetValue(id, out var m) || m.Stage != MessageStage.Accepted || m.Draft.Kind != MessageKind.FileCase || key != m.SenderAccount.Value + "/" + m.Draft.OperationKey) throw new ArgumentException("Invalid filing idempotency key.");
                filingKeys.Add(key, id);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long filingId = r.ReadInt64(), replyId = r.ReadInt64();
                if (!messages.TryGetValue(filingId, out var filing) || filing.Draft.Kind != MessageKind.FileCase || filing.Stage != MessageStage.Accepted || !messages.TryGetValue(replyId, out var reply) ||
                    reply.Draft.Kind != MessageKind.CaseDecision || reply.SenderAccount.Value != 0 || reply.Draft.Recipient != filing.SenderAccount || reply.Draft.RelatedMessageId != filingId || reply.SubmittedTick != filing.DeliveryTick ||
                    reply.Draft.CaseDecision == null || reply.Draft.CaseDecision.FilingMessageId != filingId || reply.Draft.CaseDecision.AccountId != filing.Draft.CaseSubmission!.AccountId) throw new ArgumentException("Invalid case service reply.");
                var decision = reply.Draft.CaseDecision;
                string expectedReason = ValidateCaseSubmission(filing, reply.SubmittedTick, decision.ValidationRecordVersion);
                string operation = filing.SenderAccount.Value + "/" + filing.Draft.OperationKey;
                if (!filingKeys.TryGetValue(operation, out long original)) throw new ArgumentException("Missing case operation key.");
                if (!SameDraft(messages[original].Draft, filing.Draft)) expectedReason = "IdempotencyConflict";
                if (expectedReason.Length == 0)
                {
                    if (!caseKeys.TryGetValue(filing.Draft.CaseSubmission!.CanonicalKey, out long accepted)) throw new ArgumentException("Missing accepted canonical case.");
                    expectedReason = cases[accepted].Submission.AccountId != decision.AccountId ? "CanonicalCaseAccountConflict" : cases[accepted].FilingMessageId == filingId ? "Accepted" : "AlreadyAccepted";
                }
                if (decision.Reason != expectedReason || decision.Accepted != (expectedReason is "Accepted" or "AlreadyAccepted")) throw new ArgumentException("Invalid personnel decision reason.");
                if (decision.ValidationRecordVersion < 0 || decision.ValidationRecordVersion > records.Version || decision.CountThroughCaseId >= nextCase || decision.CountThroughCaseId < decision.CaseId || (decision.Accepted ? !cases.TryGetValue(decision.CaseId, out var c) || c.Submission.CanonicalKey != filing.Draft.CaseSubmission!.CanonicalKey || c.Submission.AccountId != decision.AccountId || decision.QualifyingCount != QualifyingReprimandCases(decision.AccountId, reply.SubmittedTick, decision.CountThroughCaseId, decision.ValidationRecordVersion).Length : decision.QualifyingCount != 0))
                    throw new ArgumentException("Invalid case count reply.");
                caseReplies.Add(filingId, replyId);
            }
            if (messages.Values.Any(m => m.Draft.Kind == MessageKind.CaseDecision && !caseReplies.Values.Contains(m.Id))) throw new ArgumentException("Unregistered personnel reply.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var f = new CaseFilingSnapshot { VerificationAttemptId = r.ReadInt64(), ActorId = r.ReadInt64(), InspectionReportId = r.ReadInt64(), FilingMessageId = r.ReadInt64(), ReplyMessageId = r.ReadInt64(), ReadTick = r.ReadInt64(), CaseId = r.ReadInt64(), QualifyingCount = r.ReadInt32(), Status = ReadEnum<CaseFilingStatus>(r), Reason = r.ReadString() };
                if (!audits.TryGetValue(f.VerificationAttemptId, out var audit) || audit.ActorId != f.ActorId || audit.Finding?.Attributable != true || !verificationReportIds.TryGetValue(f.VerificationAttemptId, out long source) || source != f.InspectionReportId) throw new ArgumentException("Invalid private case goal.");
                if (f.Status == CaseFilingStatus.Queued)
                {
                    if (f.FilingMessageId != 0 || !reportAgents.TryGetValue(new Id<ActorTag>(f.ActorId), out var agent) || !agent.Pending.Any(d => d.Kind == MessageKind.FileCase && d.CaseSubmission?.InspectionReportId == source)) throw new ArgumentException("Missing queued case report.");
                }
                else if (!messages.TryGetValue(f.FilingMessageId, out var posted) || posted.PhysicalSender.Value != f.ActorId || posted.Draft.Kind != MessageKind.FileCase || posted.Draft.CaseSubmission?.InspectionReportId != source) throw new ArgumentException("Missing posted case report.");
                if (f.Status is CaseFilingStatus.Accepted or CaseFilingStatus.NeedsReview)
                {
                    if (!messages.TryGetValue(f.ReplyMessageId, out var reply) || reply.Stage != MessageStage.Read || reply.FirstReadTick != f.ReadTick || reply.Draft.CaseDecision == null || reply.Draft.CaseDecision.FilingMessageId != f.FilingMessageId ||
                        reply.Draft.CaseDecision.CaseId != f.CaseId || reply.Draft.CaseDecision.QualifyingCount != f.QualifyingCount || reply.Draft.CaseDecision.Reason != f.Reason || (f.CaseId > 0) != (f.Status == CaseFilingStatus.Accepted)) throw new ArgumentException("Invalid read case decision.");
                }
                else if (f.ReplyMessageId != 0 || f.ReadTick != -1 || f.CaseId != 0 || f.QualifyingCount != 0 || f.Reason.Length != 0) throw new ArgumentException("Unread case decision leaked into private goal.");
                caseFilings.Add(f.VerificationAttemptId, f);
            }
        }
    }
}
