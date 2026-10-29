using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public sealed class PersonnelConfig
    {
        public string SupervisorRole { get; }
        public long QualifyingWindow { get; }
        public int DetentionThreshold { get; }
        public PersonnelConfig(string supervisorRole = "supervisor", long qualifyingWindow = 6048000, int detentionThreshold = 2)
        {
            if (string.IsNullOrWhiteSpace(supervisorRole) || qualifyingWindow < 1 || detentionThreshold < 1) throw new ArgumentException("Invalid personnel policy.");
            SupervisorRole = supervisorRole; QualifyingWindow = qualifyingWindow; DetentionThreshold = detentionThreshold;
        }
    }
    public sealed class InspectionReportPayload
    {
        public long ActionId { get; }
        public string RequiredResource { get; }
        public string InstalledResource { get; }
        public InspectionReportPayload(long actionId, string requiredResource, string installedResource)
        {
            if (actionId < 1 || string.IsNullOrWhiteSpace(requiredResource) || installedResource == null) throw new ArgumentException("Invalid inspection submission.");
            ActionId = actionId; RequiredResource = requiredResource; InstalledResource = installedResource;
        }
        public bool Same(InspectionReportPayload other) => ActionId == other.ActionId && RequiredResource == other.RequiredResource && InstalledResource == other.InstalledResource;
    }
    public readonly struct CaseEvidenceReference
    {
        public long RecordVersion { get; }
        public long QueryActionId { get; }
        public CaseEvidenceReference(long recordVersion, long queryActionId)
        { if (recordVersion < 1 || queryActionId < 1) throw new ArgumentException("Invalid case evidence reference."); RecordVersion = recordVersion; QueryActionId = queryActionId; }
    }
    public sealed class CaseSubmissionPayload
    {
        public long JobId { get; }
        public long AttemptId { get; }
        public long MachineId { get; }
        public long AccountId { get; }
        public string Violation { get; }
        public string Policy { get; }
        public long InspectionReportId { get; }
        public long AttemptVersion { get; }
        public long ReportVersion { get; }
        public IReadOnlyList<CaseEvidenceReference> Evidence { get; }
        public string CanonicalKey => JobId + "/" + AttemptId + "/" + MachineId + "/" + Violation;
        public CaseSubmissionPayload(long jobId, long attemptId, long machineId, long accountId, string violation, string policy, long inspectionReportId, long attemptVersion, long reportVersion, IEnumerable<CaseEvidenceReference> evidence)
        {
            var refs = evidence.OrderBy(e => e.RecordVersion).ToArray();
            if (jobId < 1 || attemptId < 1 || machineId < 1 || accountId < 1 || string.IsNullOrWhiteSpace(violation) || string.IsNullOrWhiteSpace(policy) || inspectionReportId < 1 || attemptVersion < 1 || reportVersion < 1 ||
                refs.Length < 1 || refs.Length > 32 || refs.Select(e => e.RecordVersion).Distinct().Count() != refs.Length || refs.Any(e => e.RecordVersion < 1 || e.QueryActionId < 1)) throw new ArgumentException("Invalid case submission.");
            JobId = jobId; AttemptId = attemptId; MachineId = machineId; AccountId = accountId; Violation = violation; Policy = policy; InspectionReportId = inspectionReportId;
            AttemptVersion = attemptVersion; ReportVersion = reportVersion; Evidence = Array.AsReadOnly(refs);
        }
        public bool Same(CaseSubmissionPayload other) => CanonicalKey == other.CanonicalKey && AccountId == other.AccountId && Policy == other.Policy && InspectionReportId == other.InspectionReportId && AttemptVersion == other.AttemptVersion && ReportVersion == other.ReportVersion && Evidence.SequenceEqual(other.Evidence);
    }
    public sealed class CaseDecisionPayload
    {
        public long FilingMessageId { get; }
        public long CaseId { get; }
        public long AccountId { get; }
        public int QualifyingCount { get; }
        public long CountThroughCaseId { get; }
        public long ValidationRecordVersion { get; }
        public string Reason { get; }
        public bool Accepted => CaseId > 0;
        public CaseDecisionPayload(long filingMessageId, long caseId, long accountId, int qualifyingCount, string reason, long countThroughCaseId = 0, long validationRecordVersion = 0)
        {
            if (filingMessageId < 1 || caseId < 0 || accountId < 1 || qualifyingCount < 0 || countThroughCaseId < 0 || validationRecordVersion < 0 || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Invalid case decision.");
            FilingMessageId = filingMessageId; CaseId = caseId; AccountId = accountId; QualifyingCount = qualifyingCount; Reason = reason;
            CountThroughCaseId = countThroughCaseId;
            ValidationRecordVersion = validationRecordVersion;
        }
        public bool Same(CaseDecisionPayload other) => FilingMessageId == other.FilingMessageId && CaseId == other.CaseId && AccountId == other.AccountId && QualifyingCount == other.QualifyingCount && Reason == other.Reason && CountThroughCaseId == other.CountThroughCaseId && ValidationRecordVersion == other.ValidationRecordVersion;
    }
}
