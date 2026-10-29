using System.Text.Json.Nodes;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class PressureCaseOutcomeTests
{
    private static World Create(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "personnel_case.json")))!.AsObject();
        json["assertions"] = new JsonArray(); change?.Invoke(json);
        return new LoadedScenario(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "pressure-case-control").CreateWorld(42, true);
    }
    private static PressureStation.CaseOutcome Outcome(World world) => Assert.Single(PressureStation.InspectCaseOutcomes(world, new[] { 1L }));

    [Fact]
    public void PendingAuditAndUnreadAcceptanceAreNotCompletedCases()
    {
        var w = Create(); w.AdvanceTo(29);
        Assert.Equal(PressureStation.CaseOutcomeKind.Pending, Outcome(w).Outcome);
        w.AdvanceTo(67); Assert.Single(w.InspectCases());
        Assert.Equal(PressureStation.CaseOutcomeKind.Pending, Outcome(w).Outcome);
        w.AdvanceTo(78); var outcome = Outcome(w);
        Assert.Equal(PressureStation.CaseOutcomeKind.Accepted, outcome.Outcome);
        Assert.True(outcome.CaseId > 0 && outcome.DecisionMessageId > 0 && outcome.InspectionReportId > 0);
        Assert.Equal(outcome, Outcome(World.Restore(w.Serialize())));
    }

    [Fact]
    public void StaleEvidenceIsAnExplicitOutcomeOnlyAfterTheReviewReplyIsRead()
    {
        var w = Create(); w.ScheduleWorkCancellation(66, 1, "late-cancel"); w.AdvanceTo(68);
        Assert.Equal(PressureStation.CaseOutcomeKind.Pending, Outcome(w).Outcome);
        w.AdvanceTo(300); var outcome = Outcome(w);
        Assert.Equal(PressureStation.CaseOutcomeKind.NeedsReview, outcome.Outcome);
        Assert.Equal("StaleWorkRecords", outcome.Reason);
        Assert.True(outcome.DecisionMessageId > 0); Assert.Equal(0, outcome.CaseId);
        Assert.Empty(w.InspectReprimands());
        Assert.Equal(outcome, Outcome(World.Restore(w.Serialize())));
    }

    [Fact]
    public void VerifiedClearDoesNotRequireAnInventedReprimand()
    {
        var w = Create(j => j["initialState"]!["items"]!.AsArray().Single(i => i!["id"]!.GetValue<long>() == 8)!["resource"] = "correct");
        w.AdvanceTo(300); var outcome = Outcome(w);
        Assert.Equal(PressureStation.CaseOutcomeKind.VerifiedClear, outcome.Outcome);
        Assert.True(outcome.InspectionReportId > 0); Assert.Empty(w.InspectReprimands());
    }

    [Fact]
    public void MissingAuditPermissionsDoNotMasqueradeAsEvidenceUncertainty()
    {
        var w = Create(j => j["initialState"]!["information"]!["recordAccess"] = new JsonArray());
        w.AdvanceTo(300);
        Assert.Equal(PressureStation.CaseOutcomeKind.Pending, Outcome(w).Outcome);
        Assert.Equal("NoAssignedRecordsTerminal", Outcome(w).Reason);
        Assert.Empty(w.InspectReprimands());
    }
}
