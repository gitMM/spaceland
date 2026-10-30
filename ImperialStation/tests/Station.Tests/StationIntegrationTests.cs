using Station.Contracts;
using Station.Core;
using Station.Runner;
using Station.Mind;
using Xunit;

namespace Station.Tests;

public sealed class StationIntegrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(47)]
    [InlineData(99)]
    public void SharedKnownDistancesMatchPhysicalWeightedPathsInArbitraryTargetOrder(int source)
    {
        var tiles = Enumerable.Range(0, 100).Select(i => new Tile(new(i + 1), "Corridor", new(i % 10 - 5, i / 10 - 5, 0), traversalOverride: 1 + i * 17 % 13)).ToArray();
        var from = tiles[source].Position;
        var world = new World("weighted-known-grid", 1, new WorldConfig(), new[] { new TileDefinition("Corridor", TileFamily.Corridor, 1) }, tiles,
            new[] { new ActorSeed(new(1), "Actor", "worker", from) }, Array.Empty<Item>(), information: new InformationSeed());
        var distances = new KnownNavigation.Distances(world.ActorView(new(1)), from);
        Assert.Null(distances.To(new(int.MaxValue, 0, 0)));
        foreach (var target in tiles.OrderBy(t => t.Id.Value * 37 % 101))
        {
            var physical = world.Topology.FindPath(from, target.Position, 1000);
            Assert.Equal(PathOutcome.PathFound, physical.Outcome);
            Assert.Equal(physical.Cost, distances.To(target.Position));
            Assert.Equal(physical.Cost, distances.To(target.Position));
        }
        Assert.Null(distances.To(new(int.MinValue, 0, 0)));
    }

    [Fact]
    public void SharedKnownSearchReturnsBeforeExploringBeyondTheRequestedAnswer()
    {
        var definitions = new[] { new TileDefinition("Hull", TileFamily.Hull, long.MaxValue / 2) };
        var tiles = Enumerable.Range(0, 4).Select(i => new Tile(new(i + 1), "Hull", new(i, 0, 0))).ToArray();
        var world = new World("large-known-cost", 1, new WorldConfig(), definitions, tiles,
            new[] { new ActorSeed(new(1), "Actor", "worker", default) }, Array.Empty<Item>(), information: new InformationSeed(new InformationConfig(), Array.Empty<TerminalSeed>()));
        var routes = new KnownNavigation.Distances(world.ActorView(new(1)), default);
        Assert.Equal(0, routes.To(default));
        Assert.Equal(long.MaxValue / 2, routes.To(new(1, 0, 0)));
        Assert.Equal(long.MaxValue - 1, routes.To(new(2, 0, 0)));
        Assert.Equal(long.MaxValue / 2, routes.To(new(1, 0, 0)));
    }

    [Fact]
    public void PopulationParticipationDoesNotApplyTheTwoDayConversationGate()
    {
        var measured = StationBaseline.Execute(StationBaseline.Create(128, days: 1), requireDaily: false);
        Assert.Equal("completed", measured.Status); Assert.Equal(128, measured.ActiveWorkers);
        Assert.Equal(0, measured.ScheduledRestsOverdue); Assert.Equal(128, measured.ScheduledRestsCompleted);
        // Schema 46 budgets an hour before sleep, relieves needs forecast to become
        // critical during sleep and forecasts meal need across sleep, so daily timing
        // (not only the schema byte) changes. Status, workers and rests are unchanged.
        // Schema 49 (escort acknowledgment deadlines) follows 48 (learned terminal outages,
        // cafeteria estimates). Workers, rests and the 422 meals are unchanged.
        // Schema 50 saves each citizen's own record-grant notices (T08 decision inputs).
        // Conversation initiators no longer read partners' private consent, so who is asked changes.
        Assert.Equal("f24696251f02c5a49b700110f83bdee210a7aadec15653dd35bc53b4b1683992", measured.Digest);
        Assert.All(measured.Citizens, p => Assert.True(p.Meals[0] > 0 && p.SleepTicks[0] > 0 && p.Toilets > 0));
    }
    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    [InlineData(91UL)]
    public void TwoDaysCombineOrdinaryWorkAndEveryCitizensRealDailyActivities(ulong seed)
    {
        var scenario = StationBaseline.Create(12, seed: seed);
        Assert.Empty(scenario.Scenario.ScriptedActors);
        var result = StationBaseline.Execute(scenario);
        Assert.True(result.Status == "completed", string.Join("\n", result.Failures));
        Assert.Equal(12, result.ActiveWorkers);
        Assert.Equal(12, result.CausalRolesVerified);
        Assert.Equal(24, result.ScheduledRestsCompleted);
        Assert.Equal(result.MealsConsumed, result.Citizens.Sum(p => p.Meals.Sum()));
        Assert.All(result.Citizens, p => Assert.All(p.WorkPerDay, n => Assert.True(n > 0)));
    }

    [Fact]
    public void FoodShortageCannotGrantMealsAndReportsPhysicalFailure()
    {
        var result = StationBaseline.Execute(StationBaseline.Create(12, shortage: true), requireDaily: false);
        Assert.Equal(1, result.MealsConsumed);
        Assert.True(result.RejectedActivities > 0);
        // Rejected meals stop waiting when rejected, not at the run horizon.
        Assert.InRange(result.MaxNeedWaitTicks, 0, 375); // at most one cafeteria serving interval in this fixture
        Assert.True(result.CriticalInterruptions > 0);
        Assert.All(result.Citizens, p => Assert.True(p.Toilets > 0 && p.SleepTicks.Sum() > 0));
    }

    [Fact]
    public void IntegratedCheckpointsMidSleepAndConversationMatchChunkedContinuation()
    {
        var scenario = StationBaseline.Create(12);
        var full = scenario.CreateWorld(trace: true); full.AdvanceTo(scenario.Scenario.InitialState.Horizon);
        long sleep = full.Trace.First(e => e.Type == "NeedModeChanged" && e.Detail == "Sleeping").Tick + 1;
        long conversation = full.Trace.First(e => e.Type == "ConversationStarted").Tick + 1;
        foreach (long tick in new[] { sleep, conversation, StationBaseline.Day - 1 })
        {
            var w = scenario.CreateWorld(trace: true); w.AdvanceTo(tick);
            byte[] state = w.Serialize(); var restored = World.Restore(state, true); Assert.Equal(state, restored.Serialize());
            for (long t = tick + 731; t < scenario.Scenario.InitialState.Horizon; t += 731)
            { restored.AdvanceTo(t); restored.ClearRecordedTrace(); _ = restored.InspectNeeds(); }
            restored.AdvanceTo(scenario.Scenario.InitialState.Horizon);
            Assert.Equal(full.Digest(), restored.Digest());
        }
    }

    [Fact]
    public void TraceRetentionCannotChangeStateOrSubsequentEventIds()
    {
        var scenario = StationBaseline.Create(6);
        var full = scenario.CreateWorld(trace: true); var drained = scenario.CreateWorld(trace: true);
        full.AdvanceTo(500); drained.AdvanceTo(500); drained.ClearRecordedTrace();
        Assert.Equal(full.Digest(), drained.Digest());
        full.AdvanceTo(1000); drained.AdvanceTo(1000);
        Assert.Equal(full.Digest(), drained.Digest());
        Assert.Equal(full.Trace.Where(e => e.Tick > 500).Select(Runs.EventJson), drained.Trace.Select(Runs.EventJson));
    }

    [Fact]
    public void LostCardDuringDailyWorkUsesPhysicalRecoveryAndThenResumesWork()
    {
        var original = StationBaseline.Create(12);
        var scenario = JsonData.Read<ScenarioData>(original.ScenarioJson, "integration recovery");
        var catalog = JsonData.Read<CatalogData>(original.CatalogJson, "integration recovery content");
        var s = scenario.InitialState;
        catalog.Resources.Add(new() { Id = "blank" });
        catalog.Recipes.Add(new() { Id = 1, Capability = "CredentialIssuance", Duration = 50, Inputs = new() { new() { Resource = "blank", Quantity = 1 } }, Outputs = new() { new() { Resource = "card", Quantity = 1 } } });
        s.Work!.Workstations.Add(new() { Id = 1, Position = new() { X = 2 }, Capability = "CredentialIssuance", Access = new() { Roles = new() { "cleaner" } } });
        s.Items.Add(new() { Id = 99, Resource = "blank", Quantity = 3, LocationKind = LocationKind.TileContainer, Owner = 3 });
        s.Information!.Terminals[0].Capabilities |= TerminalCapability.CredentialRecovery;
        s.Information.Recovery = new() { IdentityCheckTicks = 50, Capabilities = s.Actors.Select(a => new RecoveryCapabilityData { ActorId = a.Id, AccountId = a.Id }).ToList(), Desks = new() { new() { TerminalId = 1, WorkstationId = 1, RecipeId = 1 } } };
        s.Cleanliness!.Access.RequiresCredential = true;
        scenario.Disturbances.Add(new() { Type = "CardLoss", Tick = 2000, ActorId = 1, ItemId = 1 });
        var integrated = new LoadedScenario(JsonData.Write(scenario), JsonData.Write(catalog), "integrated recovery");
        var world = integrated.CreateWorld(trace: true); world.AdvanceTo(s.Horizon); world.AssertAccounting();
        var completed = Assert.Single(world.Trace, e => e.Type == "CredentialRecoveryCompleted" && e.ActorId == 1);
        Assert.Contains(world.Trace, e => e.Tick > completed.Tick && e.Type == "CleaningCompleted" && e.ActorId == 1 && e.Quantity > 0);
        Assert.Single(world.Trace, e => e.Type == "ReplacementCardIssued");
        Assert.All(ResponseChains.Check(world.Trace, s.ResponseChainRoles, -1), p => Assert.True(p.Passed, string.Join("\n", p.Failures)));
        Assert.Equal(world.Serialize(), World.Restore(world.Serialize()).Serialize());
    }
}
