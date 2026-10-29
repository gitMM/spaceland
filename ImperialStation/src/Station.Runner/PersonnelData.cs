using System.Text.Json.Serialization;
using Station.Contracts;

namespace Station.Runner;

public sealed class PersonnelData
{
    public string SupervisorRole { get; set; } = "supervisor";
    public long QualifyingWindow { get; set; } = 6048000;
    public int DetentionThreshold { get; set; } = 2;
    public PersonnelConfig ToConfig() => new(SupervisorRole, QualifyingWindow, DetentionThreshold);
}
public sealed class InspectionReportData
{
    [JsonRequired] public long ActionId { get; set; }
    [JsonRequired] public string RequiredResource { get; set; } = "";
    [JsonRequired] public string InstalledResource { get; set; } = "";
    public InspectionReportPayload ToPayload() => new(ActionId, RequiredResource, InstalledResource);
}
public sealed class CaseEvidenceData
{
    [JsonRequired] public long RecordVersion { get; set; }
    [JsonRequired] public long QueryActionId { get; set; }
}
public sealed class CaseSubmissionData
{
    [JsonRequired] public long JobId { get; set; }
    [JsonRequired] public long AttemptId { get; set; }
    [JsonRequired] public long MachineId { get; set; }
    [JsonRequired] public long AccountId { get; set; }
    [JsonRequired] public string Violation { get; set; } = "";
    [JsonRequired] public string Policy { get; set; } = "";
    [JsonRequired] public long InspectionReportId { get; set; }
    [JsonRequired] public long AttemptVersion { get; set; }
    [JsonRequired] public long ReportVersion { get; set; }
    [JsonRequired] public List<CaseEvidenceData> Evidence { get; set; } = new();
    public CaseSubmissionPayload ToPayload() => new(JobId, AttemptId, MachineId, AccountId, Violation, Policy, InspectionReportId, AttemptVersion, ReportVersion,
        Evidence.Select(e => new CaseEvidenceReference(e.RecordVersion, e.QueryActionId)));
}
