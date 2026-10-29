using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class RecoveryStationTests
{
    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
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
        Assert.True(result.Events["PatrolResultReceived"] > 0);
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

    [Fact]
    public void RecoveryGateRejectsASeedThatDoesNotExerciseOverdueReports()
    {
        var result = RecoveryStation.Execute(RecoveryStation.Create(32, 91));
        Assert.Equal("assertion_failed", result.Status);
        Assert.Equal("No overdue work reports were exercised.", Assert.Single(result.Failures));
        Assert.True(result.CheckpointMatches);
    }
}
