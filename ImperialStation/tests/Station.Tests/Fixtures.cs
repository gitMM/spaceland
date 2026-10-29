using Station.Contracts;
using Station.Core;

namespace Station.Tests;

internal static class Fixtures
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ImperialStation.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Cannot find standalone solution.");
        }
    }
    internal static Id<ActorTag> Actor => new(1);
    internal static Coord A => new(0, 0, 0);
    internal static Coord B => new(1, 0, 0);
    internal static Coord C => new(2, 0, 0);
    internal static TileDefinition[] Catalog => Enum.GetValues<TileFamily>().Select(f => new TileDefinition(f.ToString(), f, 40)).ToArray();
    internal static World Line(bool trace = true, ulong seed = 42, WorldConfig? config = null)
    {
        var tiles = new[] { new Tile(new(1), "Storage", A, traversalOverride: 2), new Tile(new(2), "Hull", B, traversalOverride: 6), new Tile(new(3), "Industrial", C, traversalOverride: 10) };
        return new World("exact_fixture", seed, config ?? new WorldConfig(itemActionTicks: 1, authorityNominalTicks: 2), Catalog, tiles,
            new[] { new ActorSeed(Actor, "FixtureCourier", "worker", A) }, new[] { new Item(new(1), "part", 3, new ItemLocation(LocationKind.TileContainer, 3)) }, trace);
    }
    internal static World Generated(ulong seed, bool trace = false)
    {
        var rng = Pcg32.Create(seed, "fixture-generation"); int width = 3 + rng.Next(5), depth = 3 + rng.Next(4);
        var tiles = new List<Tile>(); long id = 1; int offset = rng.Next(1000) - 500;
        for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
            tiles.Add(new Tile(new(id++), ((TileFamily)rng.Next(12)).ToString(), new Coord(offset + x, 1, z), traversalOverride: 1 + rng.Next(30)));
        var from = tiles[0].Position; var to = tiles[^1].Position;
        var world = new World("generated_grid", seed, new WorldConfig(itemActionTicks: 3), Catalog, tiles,
            new[] { new ActorSeed(Actor, "GeneratedCourier", "worker", from) }, new[] { new Item(new(1), "part", 7, new ItemLocation(LocationKind.TileContainer, tiles[^1].Id.Value)) }, trace);
        world.Submit(0, new ActionIntent(Actor, ActionKind.Move, to));
        world.ScheduleTraversalChange(2, tiles[1].Position, 1);
        world.Submit(500, new ActionIntent(Actor, ActionKind.PickUp, to, new(1)));
        world.Submit(510, new ActionIntent(Actor, ActionKind.Move, from));
        world.Submit(1000, new ActionIntent(Actor, ActionKind.PutDown, from, new(1)));
        return world;
    }
}
