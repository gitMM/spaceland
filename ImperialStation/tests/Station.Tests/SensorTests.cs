using Station.Contracts;
using Station.Core;
using Station.Runner;
using System.Text.Json.Nodes;
using Xunit;

namespace Station.Tests;

public sealed class SensorTests
{
    private static FactKey Status => new(SubjectKind.Machine, 1, AttributeKind.Operational);
    private static World Make(IEnumerable<MachineSensorSeed>? sensors = null, PerceptionCatalog? perception = null,
        bool local = false, bool installed = true, IEnumerable<InitialMessage>? messages = null, ulong seed = 42, int eventCap = 1000000)
    {
        var access = new WorkAccess(new[] { "worker" });
        return new World("sensors", seed, new WorldConfig(itemActionTicks: 1, maxEventsPerTick: eventCap), Fixtures.Catalog,
            new[] { new Tile(new(1), "Power", Fixtures.A, traversalOverride: 2), new Tile(new(2), "Storage", Fixtures.B, traversalOverride: 2) },
            new[] { new ActorSeed(new(1), "Engineer", "worker", local ? Fixtures.A : Fixtures.B) },
            new[] { new Item(new(1), "correct", 1, new ItemLocation(installed ? LocationKind.MachineSlot : LocationKind.ActorInventory, 1)),
                new Item(new(2), "metal", 7, new ItemLocation(LocationKind.TileContainer, 1)) }, true,
            new InformationSeed(new InformationConfig(inspectTicks: 3), messages: messages, perception: perception,
                sensors: sensors ?? new[] { new MachineSensorSeed(1, 1, 0, 10, 5) }),
            new WorkSeed(new[] { new ResourceDefinition("correct", "cartridge"), new ResourceDefinition("metal") },
                Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
                new[] { new MachineDefinition(1, Fixtures.A, "cartridge", "correct", access, installTicks: 3, inspectTicks: 3),
                    new MachineDefinition(2, Fixtures.B, "cartridge", "correct", access) }));
    }

    [Fact]
    public void WiredReadingsCaptureAtSampleAndNeverLeakToCitizenOrOtherFactTypes()
    {
        var w = Make(); w.ScheduleWorkServiceChange(3, 1, true, false); w.AdvanceTo(4);
        Assert.Empty(w.InspectBeliefs()); var reading = Assert.Single(w.InspectSensorReadings());
        Assert.Equal(FactValue.Boolean(true), reading.Value); Assert.Equal(0, reading.ObservedTick); Assert.False(reading.Delivered);
        w.AdvanceTo(5); var old = Assert.Single(w.InspectBeliefs());
        Assert.Equal(FactValue.Boolean(true), old.Value); Assert.False(w.InspectMachines()[0].Operational);
        Assert.Equal("Sensor", old.Source); Assert.Equal(1, old.SensorId); Assert.Equal(1, old.ReadingId); Assert.Equal(0, old.MessageId);
        Assert.Equal(0, old.ObservedTick); Assert.Equal(5, old.ReceivedTick);
        w.AdvanceTo(15); Assert.Equal(FactValue.Boolean(false), Assert.Single(w.InspectBeliefs()).Value);
        Assert.True(w.InspectBeliefs()[0].ContradictsPrevious); Assert.Equal(2, w.InspectBeliefHistory().Count);
        Assert.All(w.InspectSensorReadings(), r => Assert.Equal(Status, r.Key));
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.SubjectKind == SubjectKind.Machine && f.Key.SubjectId == 1);
        Assert.Empty(w.InspectMailbox(new(1))); Assert.Empty(w.InspectMessages());
    }

    [Fact]
    public void DisablePrecedesSamplingButDoesNotRetractInFlightPacketAndEnableKeepsLattice()
    {
        var w = Make(); w.ScheduleSensorChange(2, 1, false); w.ScheduleWorkServiceChange(3, 1, true, false);
        w.ScheduleSensorChange(13, 1, true); w.ScheduleSensorChange(30, 1, false);
        w.AdvanceTo(19); Assert.Single(w.InspectSensorReadings()); Assert.Single(w.InspectBeliefs());
        Assert.Equal(20, Assert.Single(w.InspectSensors()).NextSampleTick);
        w.AdvanceTo(35); Assert.Equal(new long[] { 0, 20 }, w.InspectSensorReadings().Select(r => r.ObservedTick));
        Assert.Equal(-1, w.InspectSensors()[0].NextSampleTick); Assert.False(w.InspectSensors()[0].Enabled);
        Assert.Equal(FactValue.Boolean(false), w.InspectBeliefs()[0].Value);
    }

    [Fact]
    public void InitiallyDisabledSensorHasNoEvidenceUntilExplicitEnable()
    {
        var w = Make(new[] { new MachineSensorSeed(7, 1, 2, 7, 1, false) });
        w.AdvanceTo(20); Assert.Empty(w.InspectSensorReadings()); Assert.Empty(w.InspectBeliefs());
        w.ScheduleSensorChange(23, 7, true); w.AdvanceTo(24);
        var r = Assert.Single(w.InspectSensorReadings()); Assert.Equal(23, r.ObservedTick); Assert.True(r.Delivered);
    }

    [Fact]
    public void PhysicalCompletionPrecedesSameTickSensorObservation()
    {
        var w = Make(new[] { new MachineSensorSeed(1, 1, 3, 10, 1) }, local: true, installed: false);
        w.Submit(0, new(new(1), ActionKind.Install, Fixtures.A, new(1), serviceId: 1)); w.AdvanceTo(4);
        Assert.Equal(FactValue.Boolean(true), Assert.Single(w.InspectBeliefs()).Value);
        Assert.Equal(3, w.InspectBeliefs()[0].ObservedTick); Assert.Equal(1, w.ActionsCompleted);
    }

    [Fact]
    public void HumanReportAndSensorKeepDistinctProvenanceAndArrivalOrder()
    {
        var claim = new InitialMessage(new MessageDraft(MessageKind.Observation, RecipientKind.Authority, default, Status, FactValue.Boolean(false), 0), new(1), 5);
        var w = Make(messages: new[] { claim }); w.AdvanceTo(5);
        var history = w.InspectBeliefHistory(); Assert.Equal(2, history.Count);
        Assert.Equal("Reported", history[0].Source); Assert.Equal(0, history[0].SensorId); Assert.Equal(1, history[0].MessageId);
        Assert.Equal("Sensor", history[1].Source); Assert.True(history[1].ContradictsPrevious);
        Assert.Equal(FactValue.Boolean(true), w.InspectBeliefs()[0].Value);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void InspectionOnlyFactsWaitForCompletionAndCancellationDisclosesNothing()
    {
        var rules = new PerceptionCatalog(new[] { new PerceptionRule(SubjectKind.Machine, AttributeKind.Operational, PerceptionMode.InspectionOnly),
            new PerceptionRule(SubjectKind.Item, AttributeKind.Quantity, PerceptionMode.InspectionOnly) });
        var w = Make(Array.Empty<MachineSensorSeed>(), rules, local: true);
        w.Submit(0, new(new(1), ActionKind.InspectMachine, Fixtures.A, serviceId: 1));
        w.Submit(1, new(new(1), ActionKind.Cancel, Fixtures.A));
        w.Submit(2, new(new(1), ActionKind.InspectMachine, Fixtures.A, serviceId: 1));
        w.Submit(5, new(new(1), ActionKind.Inspect, Fixtures.A, new(2))); w.AdvanceTo(4);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.Attribute is AttributeKind.Operational or AttributeKind.RequiredResource or AttributeKind.Quantity);
        w.AdvanceTo(7); var machine = w.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Status));
        Assert.Equal(5, machine.LearnedTick); Assert.Equal(EvidenceKind.Inspection, machine.SourceKind);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.Quantity);
        w.AdvanceTo(8); var quantity = w.InspectKnowledge(new(1)).Single(f => f.Key.Attribute == AttributeKind.Quantity);
        Assert.Equal(7, quantity.Value.Number); Assert.Equal(EvidenceKind.Inspection, quantity.SourceKind); Assert.Equal(8, quantity.LearnedTick);
        Assert.Empty(w.InspectBeliefs());
    }

    [Fact]
    public void TileInspectionReadsCurrentLocalCostAtCompletion()
    {
        var w = Make(Array.Empty<MachineSensorSeed>(), new PerceptionCatalog(new[] { new PerceptionRule(SubjectKind.Tile, AttributeKind.TraversalTicks, PerceptionMode.InspectionOnly) }));
        w.Submit(0, new(new(1), ActionKind.InspectTile, Fixtures.B)); w.ScheduleTraversalChange(2, Fixtures.B, 9); w.AdvanceTo(2);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.TraversalTicks);
        var resumed = World.Restore(w.Serialize()); w.AdvanceTo(3); resumed.AdvanceTo(3); Assert.Equal(w.Digest(), resumed.Digest());
        var fact = w.InspectKnowledge(new(1)).Single(f => f.Key.Attribute == AttributeKind.TraversalTicks);
        Assert.Equal(2, fact.Key.SubjectId); Assert.Equal(9, fact.Value.Number); Assert.Equal(EvidenceKind.Inspection, fact.SourceKind);
    }

    [Fact]
    public void PassiveSpecificationOverrideRemainsLocalAndGettersRemainPure()
    {
        var w = Make(perception: new PerceptionCatalog(new[] { new PerceptionRule(SubjectKind.Machine, AttributeKind.RequiredResource, PerceptionMode.Passive) }));
        w.AdvanceTo(0); var fact = Assert.Single(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.RequiredResource);
        Assert.Equal(2, fact.Key.SubjectId); Assert.Equal(EvidenceKind.Direct, fact.SourceKind);
        var bytes = w.Serialize();
        for (int i = 0; i < 10; i++) { w.InspectSensors(); w.InspectSensorReadings(); w.InspectPerceptionRules(); w.InspectBeliefs(); w.InspectMachines(); w.ActorView(new(1)); }
        Assert.Equal(bytes, w.Serialize());
    }

    [Fact]
    public void GeneratedSchedulesReplayAcrossPendingPacketsTogglesAndConcurrentWorlds()
    {
        World Variant(int i)
        {
            var w = Make(new[] { new MachineSensorSeed(10 + i, 1, i % 4, 3 + i % 7, 1 + i % 9), new MachineSensorSeed(100 + i, 2, 2, 13, 4, false) }, seed: (ulong)i);
            w.ScheduleSensorChange(11, 10 + i, false); w.ScheduleSensorChange(23, 10 + i, true);
            w.ScheduleSensorChange(15, 100 + i, true); w.ScheduleWorkServiceChange(6, 1, true, false); w.ScheduleWorkServiceChange(32, 1, true, true);
            return w;
        }
        var expected = Enumerable.Range(0, 20).Select(i => { var w = Variant(i); w.AdvanceTo(60); return w.Digest(); }).ToArray();
        for (int i = 0; i < 20; i++) foreach (long tick in new long[] { -1, 0, 5, 11, 15, 22, 23, 32, 59 })
        {
            var w = Variant(i); if (tick >= 0) w.AdvanceTo(tick); var bytes = w.Serialize(); var restored = World.Restore(bytes, true);
            Assert.Equal(bytes, restored.Serialize()); w.AdvanceTo(60); restored.AdvanceTo(60);
            Assert.Equal(expected[i], restored.Digest()); Assert.Equal(w.Trace.SkipWhile(e => e.Tick <= tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        }
        foreach (int concurrency in new[] { 2, 4, 8 }) Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
        { var w = Variant(i); w.AdvanceTo(60); Assert.Equal(expected[i], w.Digest()); });
    }

    [Fact]
    public void InvalidScopesSchedulesAndFactRulesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Make(new[] { new MachineSensorSeed(1, 99, 0, 10) }));
        Assert.Throws<ArgumentException>(() => new MachineSensorSeed(1, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => new MachineSensorSeed(1, 1, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => new MachineSensorSeed(1, 1, 0, 1, channel: (SensorChannel)99));
        var rule = new PerceptionRule(SubjectKind.Item, AttributeKind.Quantity, PerceptionMode.InspectionOnly);
        Assert.Throws<ArgumentException>(() => new PerceptionCatalog(new[] { rule, rule }));
        Assert.Throws<ArgumentException>(() => new PerceptionCatalog(new[] { new PerceptionRule(SubjectKind.Job, AttributeKind.Quantity, PerceptionMode.Passive) }));
        Assert.Throws<ArgumentException>(() => Make(new[] { new MachineSensorSeed(1, 1, 0, 10), new MachineSensorSeed(1, 2, 0, 10) }));
        var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "content", "tiles.json")))!;
        catalog["perception"] = JsonNode.Parse("[{\"subject\":\"Job\",\"attribute\":\"Quantity\",\"mode\":\"Passive\"}]");
        var scenario = File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "movement_exact.json"));
        Assert.Contains("catalog.perception", Assert.Throws<ScenarioException>(() => new LoadedScenario(scenario, catalog.ToJsonString(), "invalid")).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedWorldSensorStateRemainsInspectableAndCannotResume(bool enabling)
    {
        var w = Make(new[] { new MachineSensorSeed(1, 1, enabling ? 1 : long.MaxValue, 7, 1, !enabling) });
        if (enabling) w.ScheduleSensorChange(long.MaxValue, 1, true);
        Assert.Throws<OverflowException>(() => w.AdvanceTo(long.MaxValue)); Assert.NotNull(w.RuntimeError);
        var restored = World.Restore(w.Serialize()); Assert.Equal(w.Serialize(), restored.Serialize());
        Assert.Throws<InvalidOperationException>(() => restored.AdvanceTo(long.MaxValue)); Assert.Empty(restored.InspectSensorReadings());
    }

    [Fact]
    public void MidTickEventCapRetainsDiagnosticPendingSamples()
    {
        var w = Make(new[] { new MachineSensorSeed(1, 1, 1, 7, 1), new MachineSensorSeed(2, 2, 1, 7, 1) }, eventCap: 1);
        Assert.Throws<InvalidOperationException>(() => w.AdvanceTo(10)); Assert.Single(w.InspectSensorReadings());
        var restored = World.Restore(w.Serialize()); Assert.Equal(w.Serialize(), restored.Serialize());
        Assert.Contains("Event cap", restored.RuntimeError); Assert.Throws<InvalidOperationException>(() => restored.AdvanceTo(10));
    }

    [Fact]
    public void CanonicalScenarioReplaysAndRejectsClaimedSensorProvenance()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "sensor_status.json"));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(3, run.Result.Metrics["sensorReadings"]); Assert.Equal(3, run.Result.Metrics["sensorDeliveries"]);
        Assert.Equal(new long[] { 0, 10, 30 }, run.World.InspectSensorReadings().Select(r => r.ObservedTick));
        var initial = loaded.CreateWorld(42); var replay = Runs.Execute(loaded, 42, true, startingWorld: World.Restore(initial.Serialize(), true));
        Assert.Equal(run.Result.Digest, replay.Result.Digest); Assert.Equal(run.World.Trace.Select(Runs.EventJson), replay.World.Trace.Select(Runs.EventJson));
        var forged = JsonNode.Parse(loaded.ScenarioJson)!;
        forged["initialState"]!["information"]!["messages"]![0]!["message"]!["source"] = "Sensor";
        Assert.Contains("source", Assert.Throws<ScenarioException>(() => new LoadedScenario(forged.ToJsonString(), loaded.CatalogJson, "forged")).Message);
    }
}
