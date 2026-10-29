using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class PressureStationTests
{
    [Fact]
    public void CombinedPressureRecoversWithDailyLifeConservationAndExactContinuation()
    {
        var result = PressureStation.Execute(PressureStation.Create());
        Assert.True(result.Status == "completed", string.Join("\n", result.Failures));
        Assert.Equal(4, result.Reprimands);
        Assert.Equal(2, result.Released);
        Assert.True(result.OverlappingPressure && result.SharedGuardDuties);
        Assert.True(result.CheckpointMatches && result.ContinuationTraceMatches);
        Assert.True(result.RecoveryTick >= PressureStation.PressureEnd);
        Assert.True(result.Participation.Horizon - result.RecoveryTick >= StationBaseline.Day);
    }

    [Fact]
    public void PopulationGeneratorCreatesRealIndependentBedsAndScaledMisconduct()
    {
        var scenario = PressureStation.Create(2048);
        Assert.Equal(64, scenario.Scenario.ScriptedActors.Count);
        Assert.Equal(512, scenario.Scenario.ScriptedActors.Sum(a => a.Commands.Count));
        Assert.Equal(384, scenario.Scenario.Disturbances.Count(d => d.Type == "CardLoss"));
        var state = scenario.Scenario.InitialState;
        foreach (var supervisor in state.Actors.Where(a => a.Role == "supervisor"))
        {
            var grants = state.Information!.RecordAccess.Where(g => g.ActorId == supervisor.Id).ToArray();
            Assert.Equal(2, grants.Length);
            Assert.All(grants, g => Assert.Equal((supervisor.Id - 1) / 16 + 1, g.TerminalId));
            Assert.Equal(new[] { Station.Contracts.RecordKind.WorkAttempt, Station.Contracts.RecordKind.WorkReport }, grants.Select(g => g.Kind).Order());
            var desk = Assert.Single(state.Information.Terminals, t => t.Id == grants[0].TerminalId);
            Assert.Contains("worker", desk.PermittedRoles);
            Assert.Contains("guard", desk.PermittedRoles);
            Assert.Contains(state.Information.Recovery!.Desks, d => d.TerminalId == desk.Id);
        }
        var w = scenario.CreateWorld();
        Assert.Equal(32, w.InspectNeedServices().Count(n => n.Definition.Kind == Station.Contracts.NeedServiceKind.Bed && n.Definition.DetentionFacilityId == 1));
        Assert.Empty(w.InspectEquipment());
        Assert.All(scenario.Scenario.InitialState.Actors.Where(a => a.Role == "guard"), a =>
            Assert.Single(scenario.Scenario.InitialState.Items, i => i.Resource == "guard_equipment" &&
                i.LocationKind == Station.Contracts.LocationKind.ActorInventory && i.Owner == a.Id && i.Quantity == 1));
        w.AdvanceTo(50);
        Assert.Equal(128, w.InspectMachines().Count(m => m.Id <= 128 && !m.Operational));
        w.AssertAccounting();
    }
}
