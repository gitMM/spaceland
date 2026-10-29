using Station.Contracts;
using Station.Core;
using Xunit;

namespace Station.Tests;

public sealed class ReproducibilityTests
{
    [Fact]
    public void T12_MidSegmentContinuationAndByteStableSnapshot()
    {
        var w = Fixtures.Line(); w.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.C));
        byte[] initial = w.Serialize(); Assert.Equal(initial, World.Restore(initial).Serialize());
        w.AdvanceTo(7); byte[] snapshot = w.Serialize(); var resumed = World.Restore(snapshot, true); Assert.Equal(snapshot, resumed.Serialize());
        w.AdvanceTo(20); resumed.AdvanceTo(20); Assert.Equal(w.Digest(), resumed.Digest());
        Assert.Equal(w.Trace.Where(e => e.Tick > 7).Select(e => (e.Id,e.Tick,e.Type)), resumed.Trace.Select(e => (e.Id,e.Tick,e.Type)));
    }
    [Fact]
    public void T13_ChunkingTracingAndInspectionAreNeutral()
    {
        var direct = Fixtures.Generated(61); var observed = Fixtures.Generated(61, true); direct.AdvanceTo(1100);
        for (int t = 0; t <= 1100; t++)
        {
            observed.AdvanceTo(t); observed.AdvanceTo(t); observed.InspectActors(); observed.InspectItems(); observed.ActorView(Fixtures.Actor);
            observed.Topology.InspectTiles(); observed.Digest(); observed.Serialize();
        }
        Assert.Equal(direct.Digest(), observed.Digest()); Assert.Equal(direct.Serialize(), observed.Serialize());
    }
    [Fact]
    public void T14_ConcurrentWorldsMatchSequentialResults()
    {
        string Run(int seed) { var w = Fixtures.Generated((ulong)seed); w.AdvanceTo(1100); return w.Digest(); }
        var expected = Enumerable.Range(0, 24).Select(Run).ToArray();
        foreach (int concurrency in new[] { 2, 4, 8 })
        {
            var results = new string[24]; Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i => results[i] = Run(i)); Assert.Equal(expected, results);
        }
    }
    [Fact]
    public void T25_TwentyGeneratedSeedsAtFiveCheckpointTicks()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var direct = Fixtures.Generated(seed); direct.AdvanceTo(1100); var rng = Pcg32.Create(seed, "snapshot-fuzz");
            for (int checkpoint = 0; checkpoint < 5; checkpoint++)
            {
                int tick = rng.Next(1100); var prefix = Fixtures.Generated(seed); prefix.AdvanceTo(tick); byte[] bytes = prefix.Serialize();
                var resumed = World.Restore(bytes); Assert.Equal(bytes, resumed.Serialize());
                for (int t = tick + 1; t < 1100; t += 13) resumed.AdvanceTo(t); resumed.AdvanceTo(1100);
                Assert.Equal(direct.Digest(), resumed.Digest()); resumed.AssertAccounting();
            }
        }
    }
    [Fact]
    public void GeneratedRoutesMatchDijkstraForOneThousandSeeds()
    {
        for (ulong seed = 0; seed < 1000; seed++)
        {
            var w = Fixtures.Generated(seed); var tiles = w.Topology.InspectTiles(); var from = tiles[0].Position; var target = tiles[^1].Position;
            var path = w.Topology.FindPath(from, target); var oracle = w.Topology.FindPath(from, target, dijkstra: true);
            Assert.Equal(oracle.Cost, path.Cost); Assert.Equal(PathOutcome.PathFound, path.Outcome);
            for (int i = 1; i < path.Route.Count; i++) Assert.Equal(1, path.Route[i-1].Manhattan(path.Route[i]));
            w.AdvanceTo(1100); w.AssertAccounting(); Assert.Equal(0, w.ActionsFailed);
        }
    }
    [Fact]
    public void PcgHasStableVectorsAndIndependentStreams()
    {
        var known = Pcg32.Restore(42, 55);
        Assert.Equal(new uint[] { 0, 210066564u, 1160386676u, 309281181u, 2968221517u }, Enumerable.Range(0, 5).Select(_ => known.Next()));
        var a = Pcg32.Create(17,"actor/1"); var b = Pcg32.Create(17,"actor/2"); var reference = Pcg32.Create(17,"actor/2");
        for (int i = 0; i < 100; i++) { a.Next(); Assert.Equal(reference.Next(), b.Next()); }
        var restored = Pcg32.Restore(a.State, a.Increment); Assert.Equal(a.Next(), restored.Next());
    }
    [Fact]
    public void PresentationMappingsDoNotAffectDigest()
    {
        World Make(string visual) => new("visual", 1, new WorldConfig(), new[] { new TileDefinition("tile", TileFamily.Hull, 4, visual) },
            new[] { new Tile(new(1), "tile", Fixtures.A) }, Array.Empty<ActorSeed>(), Array.Empty<Item>());
        Assert.Equal(Make("red").Digest(), Make("blue").Digest()); Assert.NotEqual(Make("red").Serialize(), Make("blue").Serialize());
    }
}
