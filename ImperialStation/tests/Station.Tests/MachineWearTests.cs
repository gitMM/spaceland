using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class MachineWearTests
{
    private static readonly Coord Site = new(1, 0, 0);
    private static World Make(int? mean = null, ulong seed = 42, bool local = true, bool sensor = false, bool trace = true)
    {
        var work = new WorkSeed(new[] { new ResourceDefinition("part", "slot"), new ResourceDefinition("tool") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
            new[] { new MachineDefinition(7, Site, "slot", "part", new WorkAccess(new[] { "worker" }, "tool"), installTicks: 3, inspectTicks: 2, testTicks: 2) });
        return new World("wear", seed, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Industrial", Fixtures.A, traversalOverride: 1), new Tile(new(2), "Power", Site, traversalOverride: 1) },
            new[] { new ActorSeed(new(1), "Engineer", "worker", local ? Site : Fixtures.A) },
            new[] { new Item(new(1), "part", 1, new(LocationKind.MachineSlot, 7)), new Item(new(2), "part", 1, new(LocationKind.ActorInventory, 1)), new Item(new(3), "tool", 1, new(LocationKind.ActorInventory, 1)) },
            trace, new InformationSeed(sensors: sensor ? new[] { new MachineSensorSeed(1, 7, 1, 100, 2) } : null), work,
            machineWear: mean.HasValue ? new MachineWearConfig(mean.Value) : null);
    }
    private static bool Operational(World w) => Assert.Single(w.InspectMachines()).Operational;
    private static LoadedScenario Scenario(Action<JsonObject>? change = null)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "machine_faults.json")))!.AsObject(); change?.Invoke(j);
        if (change != null) j["assertions"] = new JsonArray();
        return new(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "fault-tests");
    }

    [Fact]
    public void FaultConsumesOnlyInstalledCartridgeAndLocalInspectionRevealsSpecification()
    {
        var w = Make(); w.AdvanceTo(0); Assert.True(Operational(w)); w.ScheduleMachineFault(1, 7); w.AdvanceTo(1);
        Assert.False(Operational(w)); Assert.DoesNotContain(w.InspectItems(), i => i.Id.Value == 1);
        Assert.Equal(1, w.InspectItems().Single(i => i.Id.Value == 2).Quantity); Assert.Equal(1, w.InspectItems().Single(i => i.Id.Value == 3).Quantity);
        Assert.Contains(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.Operational && f.Value.Equals(FactValue.Boolean(false)));
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.RequiredResource);
        Assert.Empty(w.InspectBeliefs()); Assert.Empty(w.InspectMessages()); Assert.Empty(w.InspectJobs());
        long fault = Assert.Single(w.Trace, e => e.Type == "MachineFaulted").Id;
        Assert.Contains(w.Trace, e => e.Tick == 1 && e.Type == "ObservationRecorded" && e.FactKey?.Attribute == AttributeKind.Operational && e.CauseId == fault);
        Assert.Equal(1, Assert.Single(w.InspectFaultSinks()).ConsumedUnits); w.AssertAccounting();
        w.Submit(2, new(new(1), ActionKind.InspectMachine, Site, serviceId: 7)); w.AdvanceTo(4);
        Assert.Contains(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.RequiredResource && f.Value.Text == "part" && f.SourceKind == EvidenceKind.Inspection);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void RemoteFaultDoesNotChangeCitizenKnowledgeAndSensorDeliveryRetainsLatency()
    {
        var w = Make(local: false, sensor: true); w.ScheduleMachineFault(1, 7); w.AdvanceTo(2);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.SubjectKind == SubjectKind.Machine); Assert.Empty(w.InspectBeliefs());
        w.AdvanceTo(3); Assert.Equal(FactValue.Boolean(false), Assert.Single(w.InspectBeliefs()).Value);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.SubjectKind == SubjectKind.Machine); Assert.Empty(w.InspectJobs());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void RepeatedFaultsAtAnEmptySlotDoNotDuplicateConsumption()
    {
        var w = Make(); w.ScheduleMachineFault(1, 7); w.ScheduleMachineFault(1, 7); w.ScheduleMachineFault(2, 7); w.AdvanceTo(2);
        var state = Assert.Single(w.InspectMachineWear()); Assert.Equal(3, state.FaultEvents); Assert.Equal(1, state.ConsumedParts);
        Assert.Equal(1, Assert.Single(w.InspectFaultSinks()).ConsumedUnits); Assert.Equal(2, w.InspectItems().Count); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void PhaseOneFaultThenPhaseTwoInstallationLeavesNewCartridgeWorking()
    {
        var w = Make(); w.Submit(0, new(new(1), ActionKind.Install, Site, new(2), serviceId: 7)); w.ScheduleMachineFault(3, 7);
        w.AdvanceTo(2); Assert.True(Operational(w)); var copy = World.Restore(w.Serialize()); w.AdvanceTo(3); copy.AdvanceTo(3);
        Assert.True(Operational(w)); Assert.Equal(2, w.InspectMachines()[0].InstalledItemId); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(0, w.ActionsFailed); Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting();
        w.ScheduleMachineFault(4, 7); w.AdvanceTo(4); Assert.False(Operational(w)); Assert.Equal(2, w.InspectFaultSinks()[0].ConsumedUnits);
        Assert.Single(w.InspectItems()); w.AssertAccounting();
    }

    [Fact]
    public void CancelledInstallationDoesNotRepairOrConsumeReplacement()
    {
        var w = Make(); w.ScheduleMachineFault(0, 7); w.Submit(0, new(new(1), ActionKind.Install, Site, new(2), serviceId: 7));
        w.Submit(1, new(new(1), ActionKind.Cancel, Site)); w.AdvanceTo(4);
        Assert.False(Operational(w)); Assert.Contains(new Id<ItemTag>(2), w.InspectActor(new(1)).Inventory); Assert.Equal(1, w.InspectFaultSinks()[0].ConsumedUnits);
        Assert.Empty(w.InspectResourceClaims()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void DefaultAndDisabledWearAreExplicitAndManualFaultDoesNotAdvanceWearStream()
    {
        Assert.Equal(2592000, new MachineWearConfig().MeanIntervalTicks);
        foreach (int invalid in new[] { -1, 1, 1000000001 }) Assert.Throws<ArgumentException>(() => new MachineWearConfig(invalid));
        var disabled = Make(0); disabled.AdvanceTo(1000); Assert.True(Operational(disabled)); Assert.Equal(-1, disabled.InspectMachineWear()[0].NextWearTick);
        var normal = Make(20); var disturbed = Make(20); disturbed.ScheduleMachineFault(0, 7);
        var rng = Pcg32.Create(42, "wear"); long tick = 10 + rng.Next(21);
        Assert.Equal(tick, normal.InspectMachineWear()[0].NextWearTick);
        while (tick < 300)
        {
            normal.AdvanceTo(tick); disturbed.AdvanceTo(tick); tick += 10 + rng.Next(21);
            Assert.Equal(tick, normal.InspectMachineWear()[0].NextWearTick); Assert.Equal(tick, disturbed.InspectMachineWear()[0].NextWearTick);
        }
        Assert.Equal(normal.InspectMachineWear()[0].FaultEvents + 1, disturbed.InspectMachineWear()[0].FaultEvents);
        Assert.Equal(1, normal.InspectMachineWear()[0].ConsumedParts); Assert.Equal(1, disturbed.InspectMachineWear()[0].ConsumedParts);
    }

    [Fact]
    public void LiveFailureProducesReportOrderInspectionReplacementAndCompletion()
    {
        var loaded = Scenario(); Assert.Empty(loaded.Scenario.ScriptedActors); var w = loaded.CreateWorld(42, true);
        w.AdvanceTo(9); Assert.True(Operational(w)); Assert.Empty(w.InspectFacilityTasks());
        w.AdvanceTo(10); Assert.False(Operational(w)); Assert.Empty(w.InspectFacilityTasks());
        w.AdvanceTo(13); Assert.Equal(FacilityTaskStatus.Assigned, Assert.Single(w.InspectFacilityTasks()).Status);
        w.AdvanceTo(108); Assert.Contains(w.Trace, e => e.Type == "ActionCompleted" && e.Detail == "InspectMachine");
        w.AdvanceTo(114); Assert.True(Operational(w)); Assert.Equal(FacilityTaskStatus.Assigned, w.InspectFacilityTasks()[0].Status);
        w.AdvanceTo(118); Assert.Equal(FacilityTaskStatus.ReportedComplete, w.InspectFacilityTasks()[0].Status); Assert.Equal(1, w.InspectFaultSinks()[0].ConsumedUnits);
        Assert.Equal(0, w.ActionsFailed); w.AssertAccounting(); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
    }

    [Fact]
    public void AutomaticWearCanGenerateRepairThroughOrdinaryObservationAndReports()
    {
        var w = Scenario(j => { j["disturbances"] = new JsonArray(); j["initialState"]!["machineWear"]!["meanIntervalTicks"] = 300; }).CreateWorld(42, true);
        w.AdvanceTo(600); Assert.True(w.InspectMachineWear()[0].FaultEvents > 0); Assert.NotEmpty(w.InspectFacilityTasks());
        Assert.Contains(w.Trace, e => e.Type == "MachineFaulted" && e.Detail.EndsWith("Wear"));
        Assert.Contains(w.Trace, e => e.Type == "ItemInstalled"); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void InvalidSnapshotCountersAndTimersAreRejected()
    {
        var w = Make(20); byte[] bytes = w.Serialize();
        // Find this fixture's exact wear block; later snapshot sections may grow.
        var wear = Assert.Single(w.InspectMachineWear());
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        {
            writer.Write(true); writer.Write(20); writer.Write(1);
            writer.Write(wear.MachineId); writer.Write(wear.FaultEvents); writer.Write(wear.ConsumedParts); writer.Write(wear.LastFaultTick); writer.Write(wear.LastConsumedItemId); writer.Write(wear.NextWearTick); writer.Write(0);
        }
        var pattern = buffer.ToArray(); int record = 9 + Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var mutation in new[] { (record + 8, -1L), (record + 16, 1L), (record + 24, 0L), (record + 40, -1L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Item2).CopyTo(bad, mutation.Item1); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }

    [Fact]
    public void FaultSinkAndSubsequentProductionReconcileWithoutIntermediateOverflow()
    {
        var work = new WorkSeed(new[] { new ResourceDefinition("part", "slot"), new ResourceDefinition("metal") },
            new[] { new RecipeDefinition(1, "fabricate", new[] { new MaterialAmount("metal", 1) }, new[] { new MaterialAmount("part", 1) }, 1) },
            new[] { new WorkstationDefinition(1, Site, "fabricate", new WorkAccess(new[] { "worker" })) },
            new[] { new MachineDefinition(7, Site, "slot", "part", new WorkAccess(new[] { "worker" })) });
        var w = new World("wear-balance", 42, new WorldConfig(), Fixtures.Catalog, new[] { new Tile(new(1), "Power", Site) },
            new[] { new ActorSeed(new(1), "Engineer", "worker", Site) },
            new[] { new Item(new(1), "part", 1, new(LocationKind.MachineSlot, 7)), new Item(new(2), "part", long.MaxValue - 1, new(LocationKind.ActorInventory, 1)), new Item(new(3), "metal", 1, new(LocationKind.ActorInventory, 1)) }, work: work);
        w.ScheduleMachineFault(0, 7); w.Submit(1, new(new(1), ActionKind.ProduceRecipe, Site, serviceId: 1, recipeId: 1)); w.AdvanceTo(2);
        Assert.Equal(long.MaxValue, w.InspectItems().Where(i => i.Resource == "part").Sum(i => i.Quantity)); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void SoilingDrawsDoNotChangeTheWearSchedule()
    {
        World Create(int soilMean)
        {
            var work = new WorkSeed(new[] { new ResourceDefinition("part", "slot") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
                new[] { new MachineDefinition(7, Site, "slot", "part", new WorkAccess(new[] { "worker" })) });
            return new World("streams", 42, new WorldConfig(), new[] { new TileDefinition("floor", TileFamily.Power, 1, capabilities: new[] { "Cleanable" }) },
                new[] { new Tile(new(1), "floor", Site) }, Array.Empty<ActorSeed>(), new[] { new Item(new(1), "part", 1, new(LocationKind.MachineSlot, 7)) }, true, work: work,
                cleanliness: new CleanlinessSeed(meanSoilingTicks: soilMean), machineWear: new MachineWearConfig(20));
        }
        var quiet = Create(0); var busy = Create(2); quiet.AdvanceTo(200); busy.AdvanceTo(200);
        Assert.Equal(0, quiet.InspectDirt()[0].Units); Assert.True(busy.InspectDirt()[0].Units > 0);
        Assert.Equal(quiet.Trace.Where(e => e.Type == "MachineFaulted").Select(e => e.Tick), busy.Trace.Where(e => e.Type == "MachineFaulted").Select(e => e.Tick));
        Assert.Equal(quiet.InspectMachineWear()[0].NextWearTick, busy.InspectMachineWear()[0].NextWearTick);
        Assert.Equal(busy.Serialize(), World.Restore(busy.Serialize()).Serialize());
    }

    [Fact]
    public void RegisteredCredentialCannotBeInstalledIntoAConsumableSlot()
    {
        var work = new WorkSeed(new[] { new ResourceDefinition("card", "slot") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
            new[] { new MachineDefinition(7, Site, "slot", "card", new WorkAccess(new[] { "worker" })) });
        var info = new InformationSeed(credentials: new[] { new CredentialSeed(1, new(1), new(1), new(1)) });
        World Create(LocationKind location) => new("card", 42, new WorldConfig(), Fixtures.Catalog, new[] { new Tile(new(1), "Power", Site) },
            new[] { new ActorSeed(new(1), "Engineer", "worker", Site) }, new[] { new Item(new(1), "card", 1, new(location, location == LocationKind.MachineSlot ? 7 : 1)) }, information: info, work: work);
        Assert.Throws<ArgumentException>(() => Create(LocationKind.MachineSlot));
        var w = Create(LocationKind.ActorInventory); w.Submit(0, new(new(1), ActionKind.Install, Site, new(1), serviceId: 7)); w.AdvanceTo(0);
        Assert.Equal(FailureReason.IncompatiblePart, w.InspectActor(new(1)).LastFailure); Assert.Contains(new Id<ItemTag>(1), w.InspectActor(new(1)).Inventory);
    }

    [Fact]
    public void GeneratedWearContinuationsMatchAcrossTracingAndConcurrentWorlds()
    {
        string Run(int seed, bool verify)
        {
            World Create(bool trace) { var w = Make(10 + seed, (ulong)seed, trace: trace); w.ScheduleMachineFault(3, 7); w.Submit(30, new(new(1), ActionKind.Install, Site, new(2), serviceId: 7)); return w; }
            var direct = Create(false); direct.AdvanceTo(200);
            if (verify) foreach (int checkpoint in new[] { 0, 2, 3, 5, 10, 20, 30, 31, 33, 34, 80, 150 })
            {
                var prefix = Create(true); prefix.AdvanceTo(checkpoint); byte[] bytes = prefix.Serialize(); var resumed = World.Restore(bytes, true); Assert.Equal(bytes, resumed.Serialize());
                for (int t = checkpoint + 1; t < 200; t += 7) { resumed.AdvanceTo(t); resumed.InspectFaultSinks(); resumed.InspectMachineWear(); resumed.InspectMachines(); }
                resumed.AdvanceTo(200); prefix.AdvanceTo(200); Assert.Equal(direct.Serialize(), resumed.Serialize()); resumed.AssertAccounting();
                Assert.Equal(prefix.Trace.Where(e => e.Tick > checkpoint).Select(Runs.EventJson), resumed.Trace.Select(Runs.EventJson));
            }
            return direct.Digest();
        }
        var expected = Enumerable.Range(0, 20).Select(s => Run(s, true)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[20]; Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, false)); Assert.Equal(expected, actual); }
    }
}
