using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Disciplinary case acceptance at the authority desk. Core authenticates the filing and
    // gathers the record rows; this service applies the evidence standard and chooses the answer.
    public static class CasePolicy
    {
        public const string SupportedViolation = "WrongInstalledPart";
        public static bool SupportedPolicy(CaseSubmissionPayload p) => p.Policy == AccountabilityPolicy.Version && p.Violation == SupportedViolation;
        // Empty when the gathered history fully and exactly supports the filed finding.
        public static string EvidenceStandard(CaseSubmissionPayload p, InspectionReportPayload inspection, long observedTick, IReadOnlyList<RecordEntry> attempts, IReadOnlyList<RecordEntry> reports, IEnumerable<RecordEntry> disclosed)
        {
            if (attempts.Any(r => r.Version > p.AttemptVersion) || reports.Any(r => r.Version > p.ReportVersion)) return "StaleWorkRecords";
            if (!attempts.Concat(reports).Select(r => r.Version).OrderBy(v => v).SequenceEqual(p.Evidence.Select(e => e.RecordVersion))) return "IncompleteCaseHistory";
            var finding = AccountabilityPolicy.Assess(p.MachineId, inspection.RequiredResource, inspection.InstalledResource, observedTick, disclosed, true);
            return finding.Attributable && finding.JobId == p.JobId && finding.AttemptId == p.AttemptId && finding.AccountId == p.AccountId && finding.Reason == p.Violation ? "" : "AccountabilityPolicyRejected:" + finding.Reason;
        }
        public static bool Accepts(string reason) => reason is "Accepted" or "AlreadyAccepted";
        // canonicalFilingMessageId identifies the filing that opened an existing canonical case;
        // a filing that opened it itself is answered "Accepted".
        public static CaseAcceptanceChoice Decide(string validationReason, bool idempotencyConflict, bool canonicalExists, long canonicalAccountId, long canonicalFilingMessageId, long accountId, long filingMessageId)
        {
            string reason = idempotencyConflict ? "IdempotencyConflict" : validationReason;
            if (reason.Length != 0) return new CaseAcceptanceChoice(CaseAcceptance.Rejected, reason);
            if (!canonicalExists) return new CaseAcceptanceChoice(CaseAcceptance.OpenCase, "Accepted");
            if (canonicalAccountId != accountId) return new CaseAcceptanceChoice(CaseAcceptance.Rejected, "CanonicalCaseAccountConflict");
            return canonicalFilingMessageId == filingMessageId ? new CaseAcceptanceChoice(CaseAcceptance.OpenCase, "Accepted") : new CaseAcceptanceChoice(CaseAcceptance.AlreadyAccepted, "AlreadyAccepted");
        }
    }
}
