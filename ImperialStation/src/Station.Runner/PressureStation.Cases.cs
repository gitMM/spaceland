using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public static partial class PressureStation
{
    public enum CaseOutcomeKind { Pending, Accepted, VerifiedClear, NeedsReview }
    public sealed record CaseOutcome(long RepairAttemptId, long MachineId, long ActorId,
        CaseOutcomeKind Outcome, string Reason, long VerificationAttemptId = 0,
        long InspectionReportId = 0, long CaseId = 0, long DecisionMessageId = 0);

    // Completion follows institutional evidence, not the scripted trigger's privileged truth.
    // Missing permissions and invalid provenance remain failures, rather than being
    // disguised as acceptable uncertainty about the original repair.
    private static bool EvidenceNeedsReview(string reason) => reason is
        "AmbiguousHandoff" or "NoAttributablePartMismatch" or "StaleWorkRecords" or
        "AmbiguousCompletion" or "IncompleteHistory" or "IncompleteDisclosure" or
        "HistoryPageLimitExceeded" or "EvidenceCapacityExceeded" or "AssignmentAmended" or
        "ConflictingAmendment" or "MissingAssignment" or "IncompleteAssignment" or "MissingSubmission" ||
        reason.StartsWith("AccountabilityPolicyRejected:", StringComparison.Ordinal) &&
        EvidenceNeedsReview(reason["AccountabilityPolicyRejected:".Length..]);

    public static IReadOnlyList<CaseOutcome> InspectCaseOutcomes(World world, IEnumerable<long> repairAttemptIds)
    {
        var attempts = world.InspectWorkAttempts().ToDictionary(a => a.Id);
        var jobs = world.InspectJobs().ToDictionary(j => j.Id);
        var quality = world.InspectQualityAssurance().ToDictionary(q => q.RepairAttemptId);
        var actorIds = attempts.Values.Where(a => jobs[a.JobId].Kind == WorkKind.VerifyMachine).Select(a => a.ActorId).Distinct().ToArray();
        var audits = actorIds.SelectMany(id => world.InspectAudits(new(id))).ToDictionary(a => a.VerificationAttemptId);
        var filings = actorIds.SelectMany(id => world.InspectCaseFilings(new(id))).ToDictionary(f => f.VerificationAttemptId);
        var cases = world.InspectCases(); var reprimands = world.InspectReprimands();
        var result = new List<CaseOutcome>();
        foreach (long id in repairAttemptIds.Distinct().Order())
        {
            if (!attempts.TryGetValue(id, out var attempt) || !jobs.TryGetValue(attempt.JobId, out var job))
            { result.Add(new(id, 0, 0, CaseOutcomeKind.Pending, "MissingRepairAttempt")); continue; }
            CaseOutcome Outcome(CaseOutcomeKind kind, string reason, long verification = 0, long report = 0, long caseId = 0, long decision = 0)
                => new(id, job.MachineId, attempt.ActorId, kind, reason, verification, report, caseId, decision);
            if (!quality.TryGetValue(id, out var q) || q.ResultMessageId == 0)
            { result.Add(Outcome(CaseOutcomeKind.Pending, "AwaitingVerificationReport")); continue; }
            var accepted = cases.Where(c => c.Submission.AttemptId == id && c.Submission.MachineId == job.MachineId && c.Submission.AccountId == attempt.AccountId).ToArray();
            if (accepted.Length == 1 && reprimands.Count(r => r.CaseId == accepted[0].Id && r.AccountId == attempt.AccountId) == 1 &&
                filings.Values.Any(f => f.CaseId == accepted[0].Id && f.Status == CaseFilingStatus.Accepted && f.ReadTick >= 0))
            {
                var filing = filings.Values.First(f => f.CaseId == accepted[0].Id && f.Status == CaseFilingStatus.Accepted && f.ReadTick >= 0);
                result.Add(Outcome(CaseOutcomeKind.Accepted, "Accepted", filing.VerificationAttemptId, filing.InspectionReportId, accepted[0].Id, filing.ReplyMessageId)); continue;
            }
            if (accepted.Length > 0)
            { result.Add(Outcome(CaseOutcomeKind.Pending, "AcceptedCaseReceiptIncomplete", q.VerificationAttemptId, q.ResultMessageId)); continue; }
            if (q.Status == QualityAssuranceStatus.ReportedClear)
            { result.Add(Outcome(CaseOutcomeKind.VerifiedClear, "VerificationReportedClear", q.VerificationAttemptId, q.ResultMessageId)); continue; }
            if (q.Status != QualityAssuranceStatus.ReportedDiscrepancy || !audits.TryGetValue(q.VerificationAttemptId, out var audit))
            { result.Add(Outcome(CaseOutcomeKind.Pending, "AwaitingDiscrepancyAudit", q.VerificationAttemptId, q.ResultMessageId)); continue; }
            if (audit.Status == AuditStatus.NeedsReview && EvidenceNeedsReview(audit.Reason))
            { result.Add(Outcome(CaseOutcomeKind.NeedsReview, audit.Reason, q.VerificationAttemptId, q.ResultMessageId)); continue; }
            if (filings.TryGetValue(q.VerificationAttemptId, out var review) && review.Status == CaseFilingStatus.NeedsReview &&
                review.ReplyMessageId > 0 && review.ReadTick >= 0 && EvidenceNeedsReview(review.Reason))
            { result.Add(Outcome(CaseOutcomeKind.NeedsReview, review.Reason, q.VerificationAttemptId, q.ResultMessageId, decision: review.ReplyMessageId)); continue; }
            result.Add(Outcome(CaseOutcomeKind.Pending, string.IsNullOrEmpty(audit.Reason) ? "AuditOrFilingUnfinished" : audit.Reason,
                q.VerificationAttemptId, q.ResultMessageId));
        }
        return result;
    }
}
