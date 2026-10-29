using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class CleanlinessTests
{
    private static FactKey Dirt(long tile = 1) => new(SubjectKind.Tile, tile, AttributeKind.Dirt);
    private static World Make(long units = 1, int mean = 0, bool tools = false, ulong seed = 42, bool trace = true, PerceptionCatalog? perception = null)
    {
        var catalog = new[] { new TileDefinition("floor", TileFamily.Corridor, 2, capabilities: new[] { "Cleanable" }), new TileDefinition("hull", TileFamily.Hull, 2) };
        var population = new[] { new ActorSeed(new(1), "First", "cleaner", Fixtures.A, new(1)), new ActorSeed(new(2), "Second", "cleaner", Fixtures.A, new(2)), new ActorSeed(new(3), "Remote", "observer", Fixtures.C, new(3)) };
        var work = tools ? new WorkSeed(new[] { new ResourceDefinition("mop") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(), Array.Empty<MachineDefinition>()) : null;
        return new World("cleaning", seed, new WorldConfig(), catalog,
            new[] { new Tile(new(1), "floor", Fixtures.A), new Tile(new(2), "floor", Fixtures.B), new Tile(new(3), "hull", Fixtures.C) }, population,
            tools ? new[] { new Item(new(1), "mop", 1, new(LocationKind.ActorInventory, 1)) } : Array.Empty<Item>(), trace,
            new InformationSeed(new InformationConfig(inspectTicks: 2), Array.Empty<TerminalSeed>(), Array.Empty<CredentialSeed>(), perception: perception), work,
            cleanliness: new CleanlinessSeed(new[] { new DirtSeed(new(1), units) }, cleanUnitTicks: 3, meanSoilingTicks: mean, access: new WorkAccess(new[] { "cleaner" }, tools ? "mop" : "")));
    }

    [Fact]
    public void FiniteConcurrentCleaningRechecksCompletionAndNeverDuplicatesAnEffect()
    {
        var w = Make();
        w.Submit(1, new(new(1), ActionKind.Clean, Fixtures.A)); w.Submit(1, new(new(2), ActionKind.Clean, Fixtures.A));
        w.AdvanceTo(3); Assert.Equal(1, w.InspectDirt()[0].Units); Assert.Equal(2, w.InspectActors().Count(a => a.Action == ActionKind.Clean));
        var bytes = w.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
        w.AdvanceTo(4); copy.AdvanceTo(4); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(0, w.InspectDirt()[0].Units); Assert.Equal(1, w.InspectDirt()[0].RemovedUnits);
        Assert.Equal(new long[] { 0, 1 }, w.Trace.Where(e => e.Type == "CleaningCompleted").Select(e => e.Quantity).OrderBy(x => x));
        Assert.Single(w.Trace, e => e.Type == "CleaningCompleted" && e.Detail == "NoWorkNeeded");
        Assert.All(new long[] { 1, 2 }, id => Assert.Equal(FactValue.Integer(0), w.InspectKnowledge(new(id)).Single(f => f.Key.Equals(Dirt())).Value));
        Assert.Empty(w.InspectBeliefs()); w.AssertAccounting();
        w.Submit(5, new(new(1), ActionKind.Clean, Fixtures.A)); w.AdvanceTo(8);
        Assert.Equal(1, w.InspectDirt()[0].RemovedUnits); Assert.Contains(w.Trace, e => e.Tick == 5 && e.Type == "CleaningArrivalChecked" && e.Detail == "NoWorkNeeded");
    }

    [Fact]
    public void SameTickSpillPrecedesCompletionAndOnlyOneUnitIsCleaned()
    {
        var w = Make(units: 0); w.Submit(1, new(new(1), ActionKind.Clean, Fixtures.A)); w.ScheduleSpill(4, Fixtures.A);
        w.AdvanceTo(4); var state = w.InspectDirt()[0]; Assert.Equal(4, state.AddedUnits); Assert.Equal(1, state.RemovedUnits); Assert.Equal(3, state.Units);
        Assert.Equal(FactValue.Integer(3), w.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Dirt())).Value);
        Assert.DoesNotContain(w.InspectKnowledge(new(3)), f => f.Key.Equals(Dirt())); Assert.Empty(w.InspectBeliefs()); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void CancellationReleasesToolWithoutCleaningOrConsumingIt()
    {
        var w = Make(tools: true); w.Submit(1, new(new(1), ActionKind.Clean, Fixtures.A));
        w.Submit(2, new(new(1), ActionKind.Cancel, Fixtures.A)); w.Submit(1, new(new(2), ActionKind.Clean, Fixtures.A));
        w.AdvanceTo(1); Assert.Single(w.InspectResourceClaims()); Assert.Equal(FailureReason.MissingTool, w.InspectActor(new(2)).LastFailure);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        w.AdvanceTo(4); Assert.Empty(w.InspectResourceClaims()); Assert.Equal(1, w.InspectDirt()[0].Units); Assert.Equal(0, w.InspectDirt()[0].RemovedUnits);
        Assert.DoesNotContain(w.Trace, e => e.Type == "CleaningCompleted"); Assert.Equal(1, w.InspectItems()[0].Quantity); Assert.Empty(w.InspectResourceTransactions());
        w.Submit(5, new(new(1), ActionKind.Clean, Fixtures.A)); w.AdvanceTo(8); Assert.Equal(0, w.InspectDirt()[0].Units); Assert.Equal(1, w.InspectItems()[0].Quantity); w.AssertAccounting();
    }

    [Fact]
    public void CleaningRequiresPhysicalArrivalQualifiedRoleAndCleanableTile()
    {
        var w = Make(); w.Submit(1, new(new(3), ActionKind.Clean, Fixtures.A)); w.AdvanceTo(1);
        Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(new(3)).LastFailure);
        w.Submit(2, new(new(3), ActionKind.Clean, Fixtures.C)); w.AdvanceTo(2);
        Assert.Equal(FailureReason.RequiredServiceNotPresent, w.InspectActor(new(3)).LastFailure);
        w.Submit(3, new(new(3), ActionKind.Move, Fixtures.A)); w.Submit(4, new(new(3), ActionKind.Clean, Fixtures.A)); w.AdvanceTo(4);
        Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(new(3)).LastFailure);
        w.Submit(7, new(new(3), ActionKind.Clean, Fixtures.A)); w.AdvanceTo(7);
        Assert.Equal(FailureReason.NotQualified, w.InspectActor(new(3)).LastFailure); Assert.Equal(1, w.InspectDirt()[0].Units);
    }

    [Fact]
    public void DirtObservationIsLocalAndContentCanRequireFiniteTileInspection()
    {
        var w = Make(); w.AdvanceTo(0); Assert.Equal(FactValue.Integer(1), w.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Dirt())).Value);
        Assert.DoesNotContain(w.InspectKnowledge(new(3)), f => f.Key.Attribute == AttributeKind.Dirt);
        w.Submit(1, new(new(1), ActionKind.Move, Fixtures.B)); w.ScheduleSpill(2, Fixtures.A, 2); w.AdvanceTo(4);
        Assert.Equal(FactValue.Integer(1), w.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Dirt())).Value);
        Assert.Equal(FactValue.Integer(3), w.InspectKnowledge(new(2)).Single(f => f.Key.Equals(Dirt())).Value);
        var privateDirt = Make(perception: new PerceptionCatalog(new[] { new PerceptionRule(SubjectKind.Tile, AttributeKind.Dirt, PerceptionMode.InspectionOnly) }));
        privateDirt.Submit(1, new(new(1), ActionKind.InspectTile, Fixtures.A)); privateDirt.AdvanceTo(2);
        Assert.DoesNotContain(privateDirt.InspectKnowledge(new(1)), f => f.Key.Equals(Dirt()));
        privateDirt.AdvanceTo(3); Assert.Equal(FactValue.Integer(1), privateDirt.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Dirt())).Value);
    }

    [Fact]
    public void InvalidConfigurationAndSpillsAreRejectedWithoutMutation()
    {
        Assert.Throws<ArgumentException>(() => new CleanlinessSeed(meanSoilingTicks: 1));
        Assert.Throws<ArgumentException>(() => new CleanlinessSeed(cleanUnitTicks: 0));
        Assert.Throws<ArgumentException>(() => new CleanlinessSeed(spillUnits: 0));
        Assert.Throws<ArgumentException>(() => new DirtSeed(new(1), -1));
        Assert.Throws<ArgumentException>(() => new CleanlinessSeed(new[] { new DirtSeed(new(1), 1), new DirtSeed(new(1), 2) }));
        var w = Make(); var before = w.Serialize();
        Assert.Throws<ArgumentException>(() => w.ScheduleSpill(1, Fixtures.C));
        Assert.Throws<ArgumentException>(() => w.ScheduleSpill(1, Fixtures.A, 0));
        Assert.Equal(before, w.Serialize());
    }

    [Fact]
    public void RestoreRejectsNegativeOrUnbalancedDirtAndMismatchedTimers()
    {
        var w = Make(mean: 10); w.AdvanceTo(2); var bytes = w.Serialize();
        // Locate the actual dirt block; later subsystems may append snapshot data.
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
            foreach (var state in w.InspectDirt().OrderBy(d => d.TileId))
            { writer.Write(state.TileId); writer.Write(state.Units); writer.Write(state.InitialUnits); writer.Write(state.AddedUnits); writer.Write(state.RemovedUnits); writer.Write(state.NextSoilingTick); }
        var pattern = buffer.ToArray(); int first = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var change in new[] { (Offset: first + 8, Value: -1L), (Offset: first + 24, Value: 123L), (Offset: first + 40, Value: -1L) })
        {
            var bad = bytes.ToArray(); BitConverter.GetBytes(change.Value).CopyTo(bad, change.Offset);
            Assert.Throws<ArgumentException>(() => World.Restore(bad));
        }
        Assert.Equal(bytes, World.Restore(bytes).Serialize());
    }

    [Fact]
    public void OverflowLeavesAnInspectableFailedWorldWithoutPartialDirtMutation()
    {
        var w = Make(units: long.MaxValue); w.ScheduleSpill(1, Fixtures.A, 1);
        Assert.Throws<OverflowException>(() => w.AdvanceTo(1)); Assert.NotNull(w.RuntimeError);
        Assert.Equal(long.MaxValue, w.InspectDirt()[0].Units); Assert.Equal(0, w.InspectDirt()[0].AddedUnits); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void SoilingUsesBoundedIndependentIntervalsAndGettersDoNotDrawRandomness()
    {
        var w = Make(mean: 10); w.AdvanceTo(100);
        foreach (var tile in new[] { Fixtures.A, Fixtures.B })
        {
            long prior = 0; var events = w.Trace.Where(e => e.Type == "DirtAdded" && e.Detail == "Soiling" && e.Target == tile).ToArray(); Assert.NotEmpty(events);
            foreach (var e in events) { Assert.InRange(e.Tick - prior, 5, 15); prior = e.Tick; Assert.Equal(1, e.Quantity); }
            Assert.InRange(w.InspectDirt().Single(d => d.Position == tile).NextSoilingTick - prior, 5, 15);
        }
        Assert.Equal(w.Trace.Count(e => e.Type == "DirtAdded"), w.InspectDirt().Sum(d => d.AddedUnits));
        var before = w.Serialize(); for (int i = 0; i < 50; i++) { w.InspectDirt(); w.ActorView(new(1)); w.InspectBeliefs(); } Assert.Equal(before, w.Serialize());
        var changed = Make(mean: 10); changed.Submit(1, new(new(1), ActionKind.Clean, Fixtures.A)); changed.Submit(1, new(new(2), ActionKind.Clean, Fixtures.A)); changed.ScheduleSpill(2, Fixtures.A); changed.AdvanceTo(100);
        Assert.Equal(w.Trace.Where(e => e.Type == "DirtAdded" && e.Detail == "Soiling").Select(e => (e.Tick, e.Target)), changed.Trace.Where(e => e.Type == "DirtAdded" && e.Detail == "Soiling").Select(e => (e.Tick, e.Target)));
    }

    [Fact]
    public void GeneratedSoilingCleaningAndSpillContinuationsAreByteAndTraceStable()
    {
        World Variant(int i, bool trace = true)
        {
            var w = Make(units: i % 4, mean: 4 + i % 13, tools: i % 2 == 0, seed: (ulong)i, trace: trace);
            for (int t = 1; t < 60; t += 5) w.Submit(t, new(new(1), ActionKind.Clean, Fixtures.A));
            w.Submit(2, new(new(2), ActionKind.Clean, Fixtures.A)); w.ScheduleSpill(9, Fixtures.A, 2 + i % 5); w.ScheduleSpill(17, Fixtures.B);
            return w;
        }
        var expected = new string[20];
        for (int i = 0; i < 20; i++)
        {
            var baseline = Variant(i); baseline.AdvanceTo(100); expected[i] = baseline.Digest(); baseline.AssertAccounting();
            foreach (long tick in new long[] { -1, 0, 1, 2, 3, 4, 9, 17, 39, 60 })
            {
                var prefix = Variant(i); if (tick >= 0) prefix.AdvanceTo(tick); var bytes = prefix.Serialize(); var restored = World.Restore(bytes, true);
                Assert.Equal(bytes, restored.Serialize()); restored.AdvanceTo(100); Assert.Equal(baseline.Serialize(), restored.Serialize());
                Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
            }
            var chunked = Variant(i, false); for (int tick = 0; tick <= 100; tick++) { chunked.AdvanceTo(tick); chunked.InspectDirt(); chunked.ActorView(new(1)); }
            Assert.Equal(expected[i], chunked.Digest());
        }
        foreach (int parallel in new[] { 2, 4, 8 }) Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        { var w = Variant(i); w.AdvanceTo(100); Assert.Equal(expected[i], w.Digest()); });
    }

    [Fact]
    public void RunnerFixtureAndDirtAssertionsExecuteAndValidateStrictly()
    {
        var scenario = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "cleanup_physics.json"));
        var run = Runs.Execute(scenario, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(2, run.Result.Metrics["dirtUnitsRemoved"]); Assert.Equal(4, run.Result.Metrics["dirtUnitsAdded"]);
        var json = JsonNode.Parse(scenario.ScenarioJson)!.AsObject(); json["disturbances"]![0]!["dirtUnits"] = 0;
        Assert.Throws<ScenarioException>(() => new LoadedScenario(json.ToJsonString(), scenario.CatalogJson, "bad-spill"));
    }
}
