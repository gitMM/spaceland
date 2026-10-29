using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class NeedTests
{
    [Fact]
    public void CriticalEscortGoldenScenarioCompletesItsMilestoneAssertions()
    {
        var scenario = new LoadedScenario(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "critical_escort.json")), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "critical-escort-golden");
        var run = Runs.Execute(scenario, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(14, scenario.Scenario.Assertions.Count); RoundTrip(run.World);
    }
    [Fact]
    public void ShortAlternatingModesRetainEveryFractionSpecifiedByT27()
    {
        var s = new NeedAccumulator(500 * NeedAccumulator.HourMilliseconds, 0, -100);
        for (int i = 0; i < 100; i++) { s = s.ChangeRate(i * 2 + 1, 100, 50); s = s.ChangeRate(i * 2 + 2, 100, -100); }
        Assert.Equal(1799500000, s.At(200, 100)); Assert.Equal(499, s.Displayed(200, 100));
    }
    [Theory]
    [InlineData(100)]
    [InlineData(137)]
    [InlineData(1001)]
    public void SameRatePartitionsPreserveAnchorValueAndThreshold(long clock)
    {
        var initial = new NeedAccumulator(123456789, 0, 60); var partitioned = initial;
        for (int tick = 1; tick <= 1000; tick++) partitioned = partitioned.ChangeRate(tick, clock, 60);
        Assert.Equal(initial, partitioned); Assert.Equal(initial.At(1000, clock), partitioned.At(1000, clock));
        Assert.Equal(initial.FirstAtLeast(1000, clock, 700), partitioned.FirstAtLeast(1000, clock, 700));
    }
    [Fact]
    public void CeilingThresholdIsFirstSatisfiedTickIncludingNonDivisibleHours()
    {
        var s = new NeedAccumulator(0, 0, 60); Assert.Equal(600, s.FirstAtLeast(0, 100, 1)); Assert.Equal(0, s.Displayed(599, 100)); Assert.Equal(1, s.Displayed(600, 100));
        long t = s.FirstAtLeast(0, 137, 1)!.Value; Assert.True(s.At(t - 1, 137) < NeedAccumulator.HourMilliseconds); Assert.True(s.At(t, 137) >= NeedAccumulator.HourMilliseconds);
        Assert.Equal(t, s.FirstAtLeast(t, 137, 1)); Assert.Null(s.ChangeRate(1, 137, -100).FirstAtLeast(1, 137, 1));
    }
    [Fact]
    public void ReliefAndClampingDoNotDiscardRemaindersOrRetainHiddenOvershoot()
    {
        var s = new NeedAccumulator(700 * NeedAccumulator.HourMilliseconds + 123, 0, 60).Relieve(1, 100, 500);
        Assert.Equal(200 * NeedAccumulator.HourMilliseconds + 6123, s.AnchorQ);
        Assert.Equal(0, s.Reset(2, 100).AnchorQ);
        var upper = new NeedAccumulator(NeedAccumulator.MaximumQ - 1, 0, 100).ChangeRate(100, 100, -100);
        Assert.Equal(NeedAccumulator.MaximumQ - 10000, upper.At(101, 100));
        var lower = new NeedAccumulator(1, 0, -100).ChangeRate(100, 100, 100); Assert.Equal(10000, lower.At(101, 100));
    }
    [Fact]
    public void ArithmeticOverflowAndInvalidTimeAreExplicitFailures()
    {
        var s = new NeedAccumulator(0, 1, int.MaxValue);
        Assert.Throws<OverflowException>(() => s.At(long.MaxValue, 100)); Assert.Throws<ArgumentOutOfRangeException>(() => s.At(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.At(1, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => s.Relieve(1, 100, -1));
        Assert.Throws<ArgumentException>(() => new NeedRule(NeedKind.Hunger, 60, 60, 60, 60, 700, 300, 900));
    }
    private static World Line(NeedSeed? seed = null, int clock = 100) => new("needs", 42, new WorldConfig(tickDurationMs: clock, itemActionTicks: 1000), Fixtures.Catalog,
        new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 20), new Tile(new(2), "Hull", Fixtures.B, traversalOverride: 20), new Tile(new(3), "Industrial", Fixtures.C, traversalOverride: 20) },
        new[] { new ActorSeed(new(1), "worker", "worker", Fixtures.A) }, new[] { new Item(new(1), "part", 1, new(LocationKind.TileContainer, 1)) }, true, needs: seed ?? new NeedSeed());
    private static NeedConfig FastHunger() => new(new NeedConfig().Rules.Select(r => r.Kind == NeedKind.Hunger ? new NeedRule(r.Kind, 60, 60, 60, 60, 1, 2, 3) : r));
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void WorldThresholdsAreExactOneShotEventsAndInspectionDoesNotMutateAnchors()
    {
        var w = Line(new NeedSeed(FastHunger())); w.AdvanceTo(599); Assert.Equal(0, w.NeedThresholdsReached); Assert.Equal(600, Assert.Single(w.InspectNeeds()).NextThreshold);
        var before = w.Serialize(); _ = w.InspectNeeds(); _ = w.ActorView(new(1)).Needs; _ = w.Digest(); Assert.Equal(before, w.Serialize()); RoundTrip(w);
        w.AdvanceTo(600); Assert.Equal(1, w.NeedThresholdsReached); Assert.Equal(1200, Assert.Single(w.InspectNeeds()).NextThreshold); RoundTrip(w);
        w.AdvanceTo(1800); Assert.Equal(3, w.NeedThresholdsReached); Assert.Equal("NoKnownReliefService", Assert.Single(w.InspectNeeds()).UnmetReason);
        w.AdvanceTo(1801); Assert.Equal(3, w.NeedThresholdsReached); Assert.All(Assert.Single(w.InspectNeeds()).Segments, s => Assert.Equal(0, s.AnchorTick)); RoundTrip(w);
    }
    [Fact]
    public void CriticalMovementCompletesItsCommittedEdgeBeforeStopping()
    {
        var seed = new NeedSeed(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, 900 * NeedAccumulator.HourMilliseconds - 5 * 6000) });
        var w = Line(seed); w.Submit(0, new(new(1), ActionKind.Move, Fixtures.C)); w.AdvanceTo(5);
        Assert.True(w.InspectActor(new(1)).InTransit); Assert.Equal(1, w.CriticalNeedInterruptions); RoundTrip(w);
        w.AdvanceTo(20); Assert.False(w.InspectActor(new(1)).InTransit); Assert.Equal(Fixtures.B, w.ActorView(new(1)).CurrentTile); Assert.Equal(ActionStatus.Cancelled, w.InspectActor(new(1)).LastStatus); RoundTrip(w);
    }
    [Fact]
    public void CriticalInterruptionDoesNotGrantAnUnfinishedTransfer()
    {
        var w = Line(new NeedSeed(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, 900 * NeedAccumulator.HourMilliseconds - 5 * 6000) }));
        w.Submit(0, new(new(1), ActionKind.PickUp, Fixtures.A, new(1))); w.AdvanceTo(5);
        Assert.Equal(LocationKind.TileContainer, Assert.Single(w.InspectItems()).Location.Kind); Assert.Equal(ActionStatus.Cancelled, w.InspectActor(new(1)).LastStatus); Assert.Empty(w.InspectResourceClaims()); RoundTrip(w);
        w.AdvanceTo(1001); Assert.Equal(0, w.ActionsCompleted); RoundTrip(w);
    }
    private static World Escort(long critical, long actor = 3)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "detention_release.json")))!.AsObject(); j["assertions"] = new JsonArray();
        j["initialState"]!["needs"] = new JsonObject { ["initial"] = new JsonArray(new JsonObject { ["actorId"] = actor, ["kind"] = "Hunger", ["q"] = 900 * NeedAccumulator.HourMilliseconds - critical * 6000 }) };
        return new LoadedScenario(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "need-escort").CreateWorld(42, true);
    }
    [Fact]
    public void CriticalGuardStopsTheWholeEscortAtItsSharedBoundaryThenReports()
    {
        var w = Escort(500); w.ScheduleTraversalChange(484, new(2, 0, 0), 20); w.AdvanceTo(500);
        var group = Assert.Single(w.InspectEscorts()); Assert.Equal(EscortStatus.Escorting, group.Status); Assert.Equal("CriticalNeed/Hunger", group.Interruption);
        Assert.All(group.Members, m => Assert.True(w.InspectActor(new(m.ActorId)).InTransit)); Assert.Empty(w.InspectGuardOutcomes(true)); RoundTrip(w);
        w.AdvanceTo(505); Assert.Equal(EscortStatus.Interrupted, Assert.Single(w.InspectEscorts()).Status); Assert.Equal(new Coord(2, 0, 0), w.InspectActor(new(1)).Position);
        Assert.Empty(w.InspectDetentionStays()); RoundTrip(w);
        w.AdvanceTo(800); Assert.Equal(2, w.InspectGuardOutcomes(true).Count(o => o.Outcome == "EscortInterrupted")); Assert.DoesNotContain(w.InspectBeliefs(), b => b.Key.SubjectKind == SubjectKind.Actor); RoundTrip(w);
    }
    [Fact]
    public void DetainedTargetKeepsNeedsWithoutCancellingItsSentence()
    {
        var w = Escort(900, 1); w.AdvanceTo(1000); Assert.Equal(DetentionStayStatus.Detained, Assert.Single(w.InspectDetentionStays()).Status);
        Assert.Equal(NeedBand.Critical, w.ActorView(new(1)).Needs.Single(n => n.Kind == NeedKind.Hunger).Band); Assert.Equal(0, w.CriticalNeedInterruptions); RoundTrip(w);
        w.AdvanceTo(2200); Assert.Equal(DetentionStayStatus.Released, Assert.Single(w.InspectDetentionStays()).Status); RoundTrip(w);
    }
    [Fact]
    public void FractionalWorldCheckpointsAndChunkedAdvancementPreserveThresholds()
    {
        var w = Line(new NeedSeed(FastHunger()), 137); w.AdvanceTo(437); var copy = World.Restore(w.Serialize(), true); RoundTrip(w);
        foreach (long tick in new long[] { 438, 499, 600, 876, 1314, 1900 }) copy.AdvanceTo(tick);
        w.AdvanceTo(1900); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Digest(), copy.Digest());
        Assert.True(w.ActorView(new(1)).Needs.Single(n => n.Kind == NeedKind.Hunger).Q % NeedAccumulator.HourMilliseconds != 0);
    }
    [Fact]
    public void CompletedAndCancelledPhysicalWorkAccumulateOnlyActualWorkingTime()
    {
        var work = new WorkSeed(new[] { new ResourceDefinition("metal"), new ResourceDefinition("part") },
            new[] { new RecipeDefinition(1, "make", new[] { new MaterialAmount("metal", 1) }, new[] { new MaterialAmount("part", 1) }, 5) },
            new[] { new WorkstationDefinition(1, Fixtures.A, "make", new WorkAccess(new[] { "worker" })) }, Array.Empty<MachineDefinition>());
        var w = new World("need-work", 42, new WorldConfig(tickDurationMs: 137), Fixtures.Catalog, new[] { new Tile(new(1), "Industrial", Fixtures.A) },
            new[] { new ActorSeed(new(1), "worker", "worker", Fixtures.A) }, new[] { new Item(new(1), "metal", 2, new(LocationKind.ActorInventory, 1)) }, true, work: work, needs: new NeedSeed());
        w.Submit(1, new(new(1), ActionKind.ProduceRecipe, Fixtures.A, serviceId: 1, recipeId: 1)); w.AdvanceTo(3);
        Assert.Equal(NeedMode.Working, Assert.Single(w.InspectNeeds()).Mode); Assert.Equal(2 * 60 * 137, w.ActorView(new(1)).Needs.Single(n => n.Kind == NeedKind.WorkStrain).Q); RoundTrip(w);
        w.AdvanceTo(6); Assert.Equal(NeedMode.Awake, Assert.Single(w.InspectNeeds()).Mode); Assert.Equal(5 * 60 * 137, w.ActorView(new(1)).Needs.Single(n => n.Kind == NeedKind.WorkStrain).Q); RoundTrip(w);
        w.Submit(7, new(new(1), ActionKind.ProduceRecipe, Fixtures.A, serviceId: 1, recipeId: 1)); w.Submit(9, new(new(1), ActionKind.Cancel, Fixtures.A)); w.AdvanceTo(20);
        Assert.Equal(NeedMode.Awake, Assert.Single(w.InspectNeeds()).Mode); Assert.Equal(7 * 60 * 137, w.ActorView(new(1)).Needs.Single(n => n.Kind == NeedKind.WorkStrain).Q);
        Assert.Equal(1, w.ActionsCompleted); Assert.Equal(1, w.InspectItems().Single(i => i.Resource == "metal").Quantity); RoundTrip(w);
    }
    [Fact]
    public void CriticalPartnerDuringFiniteHandoffCannotBeSilentlyPulledIntoCustody()
    {
        var w = Escort(434, 4); w.AdvanceTo(600); Assert.Empty(w.InspectEscorts()); Assert.Equal(0, w.CriticalNeedInterruptions);
        Assert.Equal("CriticalNeed", Assert.Single(w.InspectLocalGuardOrders(new(4))).Reason); RoundTrip(w);
    }
    [Fact]
    public void NeedSnapshotsRejectCorruptAccumulatorRateAndClock()
    {
        var w = Line(new NeedSeed(FastHunger(), new[] { new NeedInitialValue(new(1), NeedKind.Social, 123456789) })); w.AdvanceTo(500); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) { writer.Write(123456789L); writer.Write(0L); writer.Write(20); writer.Write((int)NeedBand.None); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        var outside = bytes.ToArray(); BitConverter.GetBytes(NeedAccumulator.MaximumQ + 1).CopyTo(outside, start); Assert.Throws<ArgumentOutOfRangeException>(() => World.Restore(outside));
        var future = bytes.ToArray(); BitConverter.GetBytes(501L).CopyTo(future, start + 8); Assert.Throws<ArgumentException>(() => World.Restore(future));
        var rate = bytes.ToArray(); BitConverter.GetBytes(21).CopyTo(rate, start + 16); Assert.Throws<ArgumentException>(() => World.Restore(rate));
    }
}
