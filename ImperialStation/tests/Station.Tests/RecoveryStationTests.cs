using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class RecoveryStationTests
{
    [Theory]
    [InlineData(42UL)]
    public void FiniteDisruptionsRecoverThroughOrdinaryActionsAndDailyLifeContinues(ulong seed)
    {
        var scenario = RecoveryStation.Create(32, seed);
        Assert.Empty(scenario.Scenario.ScriptedActors);
        Assert.All(scenario.Scenario.InitialState.Actors, a => Assert.False(a.Scripted));
        var result = RecoveryStation.Execute(scenario);
        Assert.True(result.Status == "completed", string.Join("\n", result.Failures));
        Assert.True(result.CheckpointMatches);
        Assert.Equal(32, result.Participation.ActiveWorkers);
        Assert.Equal(4, result.Events["CredentialRecoveryCompleted"]);
        // An authenticated original reply can now close an unposted search.
        // The gate requires either that reply or the dispatched patrol's result.
        Assert.Equal(result.Phases[^1].MissingReports, result.Phases[^1].ResolvedReports);
        Assert.Equal(0, result.Participation.ExternalActions);
    }

    [Fact]
    public void RecoveryUsesAnotherDeskAfterLocallyDiscoveringExhaustedBlankStock()
    {
        // Empty nearest desk, supplied alternative: neither choice may use
        // privileged stock truth before the citizen tries the physical service.
        var w = AutonomousRecoveryTests.Station(stock: 0, secondDesk: true);
        w.AdvanceTo(300); w.AssertAccounting();
        Assert.Contains(w.Trace, e => e.Type == "ActionFailed" && e.Detail == "InsufficientMaterials");
        Assert.Equal(1, w.InspectRecoveryAgents().Sum(a => a.Completed));
        Assert.Equal(3, Assert.Single(w.InspectCredentialRecoveryCases()).IssueTerminalId);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(91UL)]
    public void SeedsThatNowExerciseOverdueReportsPassTheRecoveryGate(ulong seed)
    {
        // Pre-sleep relief and the budgeted sleep window changed daily timing; these
        // seeds, previously rejected for lacking overdue reports, now exercise them.
        var result = RecoveryStation.Execute(RecoveryStation.Create(32, seed));
        Assert.True(result.Status == "completed", string.Join("\n", result.Failures));
        Assert.True(result.Events["MissingWorkReportOpened"] > 0);
        Assert.True(result.CheckpointMatches);
    }

    [Fact]
    public void RecoveryGateRejectsARunThatDoesNotExerciseOverdueReports()
    {
        // Keep the scenario coverage requirement by construction: with a report grace
        // longer than the horizon nothing can become overdue, so the gate must reject.
        var source = RecoveryStation.Create(32, 7);
        var data = JsonData.Read<ScenarioData>(source.ScenarioJson, "recovery");
        data.InitialState.Authority!.GraceTicks = data.InitialState.Horizon * 2;
        var result = RecoveryStation.Execute(new LoadedScenario(JsonData.Write(data), source.CatalogJson, "recovery without overdue reports"));
        Assert.Equal("assertion_failed", result.Status);
        Assert.Contains("No overdue work reports were exercised.", result.Failures);
        Assert.True(result.CheckpointMatches);
    }
}
