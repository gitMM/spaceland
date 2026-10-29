using Station.Contracts;
using Station.Core;
using Station.Mind;
using Xunit;

namespace Station.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void T01_AllTwelveFamiliesAndVerticalEdgesAreTraversable()
    {
        var tiles = Fixtures.Catalog.Select((d, i) => new Tile(new(i + 1), d.Id, new Coord(i < 6 ? i : 5, i < 6 ? 0 : i - 5, 0))).ToArray();
        var topology = new Topology(Fixtures.Catalog, tiles); topology.ValidateConnected(true);
        foreach (var tile in tiles) Assert.Equal(PathOutcome.PathFound, topology.FindPath(tiles[0].Position, tile.Position).Outcome);
        Assert.Throws<ArgumentException>(() => topology.EdgeTicks(new(4, 0, 0), new(5, 1, 0)));
        Assert.Equal(PathOutcome.InvalidDestination, topology.FindPath(tiles[0].Position, new(4, 1, 0)).Outcome);
        var missing = new Topology(Fixtures.Catalog, new[] { tiles[0], new Tile(new(100), "Truss", new(2, 0, 0)) });
        Assert.Equal(PathOutcome.NoRoute, missing.FindPath(new(0, 0, 0), new(2, 0, 0)).Outcome);
    }
    [Fact]
    public void T02_ConnectivityNamesComponentsAndRejectsEmptyPopulatedWorld()
    {
        var topology = new Topology(Fixtures.Catalog, new[] { new Tile(new(1), "Hull", new(0, 0, 0)), new Tile(new(2), "Hull", new(2, 0, 0)) });
        string error = Assert.Throws<ArgumentException>(() => topology.ValidateConnected(true)).Message;
        Assert.Contains("component 1: (0,0,0)", error); Assert.Contains("component 2: (2,0,0)", error);
        Assert.Throws<ArgumentException>(() => new Topology(Fixtures.Catalog, Array.Empty<Tile>()).ValidateConnected(true));
        new Topology(Fixtures.Catalog, Array.Empty<Tile>()).ValidateConnected(false);
    }
    [Fact]
    public void T03_ExactArrivalLocalityAndTickZero()
    {
        var w = Fixtures.Line(); w.Submit(0, new ActionIntent(Fixtures.Actor, ActionKind.Move, Fixtures.C));
        w.Submit(11, new ActionIntent(Fixtures.Actor, ActionKind.PickUp, Fixtures.C, new(1)));
        Assert.Equal(-1, w.LastProcessedTick); Assert.Empty(w.Trace); Assert.False(w.InspectActor(Fixtures.Actor).InTransit);
        w.AdvanceTo(0); Assert.True(w.InspectActor(Fixtures.Actor).InTransit);
        byte[] zero = w.Serialize(); w.AdvanceTo(0); Assert.Equal(zero, w.Serialize());
        Assert.Throws<ArgumentException>(() => w.Submit(0, new ActionIntent(Fixtures.Actor, ActionKind.Move, Fixtures.A)));
        w.AdvanceTo(3); var a = w.InspectActor(Fixtures.Actor); Assert.Equal(3, a.ProgressNumerator); Assert.Equal(4, a.ProgressDenominator); Assert.Equal(Fixtures.A, a.Position);
        w.AdvanceTo(4); Assert.Equal(Fixtures.B, w.InspectActor(Fixtures.Actor).Position);
        w.AdvanceTo(11); a = w.InspectActor(Fixtures.Actor); Assert.Equal(FailureReason.NotAtRequiredTile, a.LastFailure); Assert.True(a.InTransit); Assert.Null(w.ActorView(Fixtures.Actor).CurrentTile);
        w.AdvanceTo(12); Assert.False(w.InspectActor(Fixtures.Actor).InTransit); Assert.Equal(Fixtures.C, w.ActorView(Fixtures.Actor).CurrentTile);
        Assert.Equal(new long[] { 4, 12 }, w.Trace.Where(e => e.Type == "TileArrived").Select(e => e.Tick));
        Assert.Equal(4, w.Topology.EdgeTicks(Fixtures.A, Fixtures.B)); Assert.Equal(4, w.Topology.EdgeTicks(Fixtures.B, Fixtures.A));
        Assert.Equal(0, w.Topology.FindPath(Fixtures.A, Fixtures.A).Cost);
        var same = Fixtures.Line(); same.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.A)); same.AdvanceTo(0); Assert.Equal(1, same.ActionsCompleted);
    }
    [Fact]
    public void T04_CheaperDetourAndLoweredMinimumRemainOptimal()
    {
        var positions = new[] { new Coord(0, 0, 0), new Coord(1, 0, 0), new Coord(2, 0, 0), new Coord(0, 0, 1), new Coord(1, 0, 1), new Coord(2, 0, 1) };
        var tiles = positions.Select((p, i) => new Tile(new(i + 1), "Corridor", p, traversalOverride: i == 1 ? 100 : 2));
        var w = new World("detour_fixture", 1, new WorldConfig(), Fixtures.Catalog, tiles, Array.Empty<ActorSeed>(), Array.Empty<Item>());
        var route = w.Topology.FindPath(positions[0], positions[2]); Assert.Equal(8, route.Cost); Assert.Equal(new[] { positions[0], positions[3], positions[4], positions[5], positions[2] }, route.Route);
        Assert.Equal(w.Topology.FindPath(positions[0], positions[2], dijkstra: true).Cost, route.Cost);
        w.ScheduleTraversalChange(0, positions[1], 1); w.AdvanceTo(0); Assert.Equal(1, w.Topology.MinEdgeCost); Assert.Equal(1, w.Topology.Version);
        route = w.Topology.FindPath(positions[0], positions[2]); Assert.Equal(4, route.Cost);
        Assert.Equal(w.Topology.FindPath(positions[0], positions[2], dijkstra: true).Cost, route.Cost);
    }
    [Fact]
    public void T04_TiesFollowNeighborInsertionAndBudgetIsDistinct()
    {
        var tiles = new[] { new Coord(0,0,0), new Coord(1,0,0), new Coord(0,0,1), new Coord(1,0,1) }.Select((p,i) => new Tile(new(i+1), "Corridor", p, traversalOverride: 1));
        var topology = new Topology(Fixtures.Catalog, tiles);
        var route = topology.FindPath(new(0,0,0), new(1,0,1)); Assert.Equal(new Coord(1,0,0), route.Route[1]);
        Assert.Equal(PathOutcome.BudgetExhausted, topology.FindPath(new(0,0,0), new(1,0,1), 0).Outcome);
    }
    [Fact]
    public void T05_AuthorityUnderestimateDoesNotAffectExecution()
    {
        Assert.Equal(4, TravelEstimates.Authority(Fixtures.A, Fixtures.C, 2));
        var w = Fixtures.Line(); w.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.C)); w.AdvanceTo(4);
        Assert.True(w.InspectActor(Fixtures.Actor).InTransit); w.AdvanceTo(12); Assert.Equal(Fixtures.C, w.InspectActor(Fixtures.Actor).Position);
    }
    [Fact]
    public void T06_CostChangeFreezesCurrentSegmentAcrossRestore()
    {
        var w = Fixtures.Line(); w.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.C)); w.ScheduleTraversalChange(2, Fixtures.B, 20); w.AdvanceTo(3);
        var restored = World.Restore(w.Serialize(), true); Assert.Equal(4, restored.InspectActor(Fixtures.Actor).Arrival);
        w.AdvanceTo(4); restored.AdvanceTo(4); Assert.Equal(19, restored.InspectActor(Fixtures.Actor).Arrival);
        w.AdvanceTo(19); restored.AdvanceTo(19); Assert.Equal(w.Digest(), restored.Digest()); Assert.False(restored.InspectActor(Fixtures.Actor).InTransit);
    }
    [Fact]
    public void ItemsRequireFinitePhysicalTransferAndConserveQuantity()
    {
        var w = Fixtures.Line(); w.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.C)); w.Submit(12, new(Fixtures.Actor, ActionKind.PickUp, Fixtures.C, new(1)));
        w.Submit(14, new(Fixtures.Actor, ActionKind.PutDown, Fixtures.C, new(1))); w.AdvanceTo(12);
        Assert.Empty(w.InspectActor(Fixtures.Actor).Inventory); w.AdvanceTo(13); Assert.Single(w.InspectActor(Fixtures.Actor).Inventory);
        var checkpoint = World.Restore(w.Serialize()); w.AdvanceTo(15); checkpoint.AdvanceTo(15); Assert.Equal(w.Digest(), checkpoint.Digest());
        Assert.Empty(w.InspectActor(Fixtures.Actor).Inventory); Assert.Equal(3, Assert.Single(w.InspectItems()).Quantity); w.AssertAccounting();
    }
    [Fact]
    public void AtomicClaimsPreventDuplicatePickup()
    {
        var w = new World("transfer_fixture", 1, new WorldConfig(itemActionTicks: 2), Fixtures.Catalog, new[] { new Tile(new(1), "Storage", Fixtures.A) },
            new[] { new ActorSeed(new(1), "First", "worker", Fixtures.A), new ActorSeed(new(2), "Second", "worker", Fixtures.A) },
            new[] { new Item(new(1), "part", 1, new ItemLocation(LocationKind.TileContainer, 1)) });
        w.Submit(0, new(new(1), ActionKind.PickUp, Fixtures.A, new(1))); w.Submit(0, new(new(2), ActionKind.PickUp, Fixtures.A, new(1))); w.AdvanceTo(2);
        Assert.Equal(1, w.ActionsCompleted); Assert.Equal(1, w.ActionsFailed);
        Assert.Single(w.InspectActors(), a => a.LastFailure == FailureReason.ResourceClaimed);
        Assert.Single(w.InspectActors(), a => a.Inventory.Count == 1); w.AssertAccounting();
    }
    [Fact]
    public void EventCapFailsExplicitlyAndArithmeticIsChecked()
    {
        var w = Fixtures.Line(config: new WorldConfig(maxEventsPerTick: 1));
        w.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.A)); w.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.C));
        Assert.Throws<InvalidOperationException>(() => w.AdvanceTo(0)); Assert.Contains("Event cap", w.RuntimeError);
        var failed = World.Restore(w.Serialize()); Assert.Equal(w.Serialize(), failed.Serialize()); Assert.Throws<InvalidOperationException>(() => failed.AdvanceTo(1));
        Assert.Throws<OverflowException>(() => TravelEstimates.Authority(new(int.MinValue,0,0), new(int.MaxValue,0,0), long.MaxValue));
        Assert.Equal(3, new Coord(int.MaxValue, int.MaxValue, int.MaxValue).Neighbors().Count());
    }
}
