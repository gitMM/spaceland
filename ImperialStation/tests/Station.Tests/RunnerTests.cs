using System.Text.Json.Nodes;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void AutonomousReportScenarioAndFocusedCausalTraceHaveRealTransitions()
    {
        var loaded=LoadedScenario.Load(Path.Combine(Fixtures.Root,"scenarios","autonomous_report.json"));var run=Runs.Execute(loaded,42,true);
        Assert.Equal("completed",run.Result.Status);Assert.Empty(loaded.Scenario.ScriptedActors);Assert.Equal(1,Assert.Single(run.World.InspectAgents()).CompletedReports);
        var changed=Assert.Single(run.World.Trace,e=>e.Type=="BeliefChanged");var chain=TraceChains.Select(run.World.Trace,changed.Id);
        Assert.Contains(chain,e=>e.Type=="MessagePosted");Assert.Contains(chain,e=>e.Type=="MailboxDelivered");Assert.Contains(chain,e=>e.Type=="PlanSelected");
        Assert.All(chain,e=>Assert.True(e.Id<=changed.Id));
    }
    [Fact]
    public void T07_CanonicalAndTwentyGeneratedTerminalScenariosRunThroughStrictLoader()
    {
        string source=File.ReadAllText(Path.Combine(Fixtures.Root,"scenarios","false_delivery.json"));
        var canonical=LoadedScenario.Load(Path.Combine(Fixtures.Root,"scenarios","false_delivery.json"));
        Assert.Equal("completed",Runs.Execute(canonical,42,true).Result.Status);
        for(ulong seed=0;seed<20;seed++)
        {
            var variant=Station.Scenarios.FalseDeliveryGenerator.Generate(source,CatalogJson,seed);
            var run=Runs.Execute(variant,seed,true);Assert.Equal("completed",run.Result.Status);Assert.Empty(run.Result.AssertionFailures);
            var rng=Pcg32.Create(seed,"generated-terminal-snapshots");
            for(int i=0;i<5;i++)
            {
                var w=variant.CreateWorld(seed);w.AdvanceTo(rng.Next((int)variant.Scenario.InitialState.Horizon));byte[] bytes=w.Serialize();var restored=World.Restore(bytes);
                Assert.Equal(bytes,restored.Serialize());restored.AdvanceTo(variant.Scenario.InitialState.Horizon);Assert.Equal(run.World.Digest(),restored.Digest());
            }
        }
    }
    private static string ScenarioPath => Path.Combine(Fixtures.Root, "scenarios", "movement_exact.json");
    private static string CatalogJson => File.ReadAllText(Path.Combine(Fixtures.Root, "content", "tiles.json"));
    private static JsonObject Scenario() => JsonNode.Parse(File.ReadAllText(ScenarioPath))!.AsObject();
    [Fact]
    public void ScenarioRunsAssertionsAndSelfContainedReplay()
    {
        var loaded = LoadedScenario.Load(ScenarioPath); string directory = Path.Combine(Fixtures.Root, "artifacts", "test-replay");
        var run = Runs.Execute(loaded, 42, true, directory); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        var bundle = JsonData.Read<ReplayBundle>(File.ReadAllText(Path.Combine(directory, "replay.json")), "replay");
        var embedded = new LoadedScenario(bundle.ScenarioJson, bundle.CatalogJson, "embedded");
        var replay = Runs.Execute(embedded, 42, true, startingWorld: bundle.Start.Restore(true));
        Assert.Equal(bundle.ExpectedDigest, replay.Result.Digest); Assert.Equal(run.World.Trace.Select(Runs.EventJson), replay.World.Trace.Select(Runs.EventJson));
        var checkpoint = JsonData.Read<Checkpoint>(File.ReadAllText(Path.Combine(directory,"checkpoint.json")), "checkpoint");
        Assert.Equal(checkpoint.Digest, checkpoint.Restore().Digest()); Assert.Equal(JsonData.Write(checkpoint), JsonData.Write(Checkpoint.Capture(checkpoint.Restore())));
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("knowledgeInjection")]
    [InlineData("recordMutations")]
    public void UnknownScenarioSectionsAreRejected(string key)
    {
        var json = Scenario(); json[key] = new JsonArray(); var error = Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "fixture.json"));
        Assert.Contains("fixture.json", error.Message); Assert.Contains(key, error.Message);
    }
    [Fact]
    public void SchemaRejectsUnknownFieldsEnumsCoordinatesDurationsAndIds()
    {
        var json = Scenario(); json["initialState"]!["tiles"]![0]!["traversalOverride"] = 0;
        Assert.Contains("traversalOverride", Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "bad.json")).Message);
        json = Scenario(); json["initialState"]!["tiles"]![0]!["definition"] = "does_not_exist";
        Assert.Contains("unknown definition", Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "bad.json")).Message);
        json = Scenario(); json["initialState"]!["tiles"]![0]!["position"]!["x"] = 0.5;
        Assert.Contains("position.x", Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "bad.json")).Message);
        json = Scenario(); json["scriptedActors"]![0]!["commands"]![0]!["kind"] = "Teleport";
        Assert.Contains("kind", Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "bad.json")).Message);
        json = Scenario(); json["scriptedActors"]![0]!["commands"]![0]!["kind"] = 0;
        Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "bad.json"));
        Assert.Throws<ScenarioException>(() => new LoadedScenario("{\"initialState\":{},\"initialState\":{}}", CatalogJson, "bad.json"));
    }
    [Fact]
    public void UnscriptedResponsesAndUnknownDisturbancesAreRejected()
    {
        var json = Scenario(); json["initialState"]!["actors"]![0]!["scripted"] = false;
        Assert.Contains("post-tick-0", Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "bad.json")).Message);
        json = Scenario(); json["disturbances"] = JsonNode.Parse("[{\"type\":\"SetBelief\",\"tick\":1,\"tile\":{\"x\":0,\"y\":0,\"z\":0},\"traversalTicks\":1}]");
        Assert.Contains("unknown disturbance", Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), CatalogJson, "bad.json")).Message);
    }
    [Fact]
    public void T25_CanonicalFixtureFiveCheckpointsForTwentySeeds()
    {
        var loaded = LoadedScenario.Load(ScenarioPath);
        for (ulong seed = 0; seed < 20; seed++)
        {
            var direct = loaded.CreateWorld(seed); direct.AdvanceTo(20); var rng = Pcg32.Create(seed, "checkpoint-selection");
            for (int i = 0; i < 5; i++)
            {
                var prefix = loaded.CreateWorld(seed); prefix.AdvanceTo(rng.Next(20)); byte[] data = prefix.Serialize(); var restored = World.Restore(data);
                Assert.Equal(data, restored.Serialize()); restored.AdvanceTo(20); Assert.Equal(direct.Digest(), restored.Digest());
            }
        }
    }
}
