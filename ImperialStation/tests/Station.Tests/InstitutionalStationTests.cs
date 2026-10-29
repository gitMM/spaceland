using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class InstitutionalStationTests
{
    [Theory]
    [InlineData("normal")]
    [InlineData("correct")]
    [InlineData("single-case")]
    [InlineData("guards-unavailable")]
    [InlineData("terminal-outage")]
    [InlineData("intake-outage")]
    [InlineData("full-capacity")]
    [InlineData("interrupted-escort")]
    public void SpecializedStationProvesDailyLifeInstitutionalControlsAndReplay(string variant)
    {
        var result = InstitutionalStation.Execute(InstitutionalStation.Create(32, variant: variant), variant);
        Assert.True(result.Status == "completed", string.Join("\n", result.Failures));
        Assert.True(result.CheckpointMatches);
        Assert.True(result.Participation.CausalRolesVerified >= 6);
        Assert.Equal(variant == "single-case" ? 4 : variant == "full-capacity" ? 16 : 8, result.Participation.ExternalActions);
        Assert.All(result.Participation.Citizens, c => Assert.True(c.Conversations > 0 && c.Meals.All(n => n > 0) && c.SleepTicks.All(n => n > 0)));
    }

    [Fact]
    public void LateOrderReadRemembersOwnCompletionWithoutInventingPhysicalSuccess()
    {
        var scenario = InstitutionalStation.Create(32); var w = scenario.CreateWorld(trace: true); w.AdvanceTo(2000);
        var reported = w.InspectLocalOrders(new(1)).Where(o => o.JobId is 10001 or 10002).ToArray();
        Assert.Equal(2, reported.Length);
        Assert.All(reported, o => { Assert.Equal(LocalOrderStatus.Completed, o.Status); Assert.Equal("OwnCompletionAlreadyPosted", o.Reason); });
        Assert.All(w.InspectMachines(), m => Assert.False(m.Operational));
        Assert.Equal(2, w.Trace.Count(e => e.Type == "ItemInstalled"));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
}
