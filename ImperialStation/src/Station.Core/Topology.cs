using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class PathResult
    {
        public PathOutcome Outcome { get; }
        public IReadOnlyList<Coord> Route { get; }
        public long Cost { get; }
        public int Expansions { get; }
        internal PathResult(PathOutcome outcome, IEnumerable<Coord>? route = null, long cost = 0, int expansions = 0)
        { Outcome = outcome; Route = Array.AsReadOnly((route ?? Array.Empty<Coord>()).ToArray()); Cost = cost; Expansions = expansions; }
    }

    public sealed class Topology
    {
        internal readonly SortedDictionary<string, TileDefinition> Definitions = new SortedDictionary<string, TileDefinition>(StringComparer.Ordinal);
        internal readonly SortedDictionary<Coord, Tile> Tiles = new SortedDictionary<Coord, Tile>();
        internal readonly SortedDictionary<Id<TileTag>, Tile> ById = new SortedDictionary<Id<TileTag>, Tile>();
        public long Version { get; internal set; }
        public long MinEdgeCost { get; private set; }
        public Topology(IEnumerable<TileDefinition> definitions, IEnumerable<Tile> tiles)
        {
            foreach (var d in definitions) Definitions.Add(d.Id, d);
            foreach (var t in tiles)
            {
                if (!Definitions.ContainsKey(t.DefinitionId)) throw new ArgumentException($"Unknown definition {t.DefinitionId} at {t.Position}.");
                ValidateCost(t.TraversalOverride);
                if (t.Id.Value == 0) throw new ArgumentException("Tile ID must be positive.");
                var copy = t.Copy(); Tiles.Add(copy.Position, copy); ById.Add(copy.Id, copy);
            }
            RecomputeMinimum();
        }
        internal static void ValidateCost(long? ticks)
        { if (ticks.HasValue && (ticks < 1 || ticks > long.MaxValue / 2)) throw new ArgumentException("Traversal ticks must be in [1, Int64.MaxValue/2]."); }
        internal void ChangeCost(Coord at, long ticks)
        {
            ValidateCost(ticks);
            if (!Contains(at)) throw new ArgumentException($"Unknown completed tile {at}.");
            Tiles[at].TraversalOverride = ticks; Version++; RecomputeMinimum();
        }
        private void RecomputeMinimum()
        { MinEdgeCost = Tiles.Values.Where(t => t.Completed).Select(Cost).DefaultIfEmpty(1).Min(); }
        internal void AddCompleted(Tile tile)
        {
            if (!tile.Completed || tile.Id.Value < 1 || Tiles.ContainsKey(tile.Position) || ById.ContainsKey(tile.Id) || !Definitions.ContainsKey(tile.DefinitionId) || !tile.Position.Neighbors().Any(Contains))
                throw new InvalidOperationException("Construction must add one new completed tile adjacent to the station.");
            long version = checked(Version + 1); var copy = tile.Copy(); Tiles.Add(copy.Position, copy); ById.Add(copy.Id, copy); Version = version; RecomputeMinimum();
        }
        private long Cost(Tile t) => t.TraversalOverride ?? Definitions[t.DefinitionId].TraversalTicks;
        public long TraversalTicks(Coord p) => Cost(Tiles[p]);
        public bool Contains(Coord p) => Tiles.TryGetValue(p, out var t) && t.Completed;
        public long EdgeTicks(Coord a, Coord b)
        {
            if (!Contains(a) || !Contains(b) || a.Manhattan(b) != 1) throw new ArgumentException($"Not a completed adjacent edge: {a} -> {b}.");
            long sum = checked(TraversalTicks(a) + TraversalTicks(b));
            return sum / 2 + sum % 2;
        }
        public IReadOnlyList<Tile> InspectTiles() => Tiles.Values.Select(t => t.Copy()).ToArray();
        public IReadOnlyList<TileDefinition> InspectDefinitions() => Definitions.Values.ToArray();
        public void ValidateConnected(bool populated)
        {
            var remaining = new SortedSet<Coord>(Tiles.Values.Where(t => t.Completed).Select(t => t.Position));
            if (remaining.Count == 0) { if (populated) throw new ArgumentException("Populated station has no completed tiles."); return; }
            var components = new List<string>();
            while (remaining.Count > 0)
            {
                var start = remaining.Min; var queue = new Queue<Coord>(); queue.Enqueue(start); remaining.Remove(start); int size = 0;
                while (queue.Count > 0)
                { var p = queue.Dequeue(); size++; foreach (var n in p.Neighbors()) if (remaining.Remove(n)) queue.Enqueue(n); }
                components.Add($"component {components.Count + 1}: {start}, {size} tile(s)");
            }
            if (components.Count > 1) throw new ArgumentException("Disconnected station: " + string.Join("; ", components));
        }

        private sealed class Node : IComparable<Node>
        {
            internal Coord Position; internal long G; internal long F; internal long Sequence;
            public int CompareTo(Node? other) { if (other == null) return 1; int c = F.CompareTo(other.F); return c == 0 ? Sequence.CompareTo(other.Sequence) : c; }
        }
        public PathResult FindPath(Coord start, Coord target, int budget = 100000, bool dijkstra = false)
        {
            if (!Contains(start) || !Contains(target)) return new PathResult(PathOutcome.InvalidDestination);
            if (start == target) return new PathResult(PathOutcome.PathFound, new[] { start });
            var open = new SortedSet<Node>(); var distances = new SortedDictionary<Coord, long>();
            var parents = new SortedDictionary<Coord, Coord>(); long sequence = 0; int expansions = 0;
            long Heuristic(Coord p) => dijkstra ? 0 : checked(p.Manhattan(target) * MinEdgeCost);
            distances.Add(start, 0); open.Add(new Node { Position = start, G = 0, F = Heuristic(start), Sequence = sequence++ });
            while (open.Count > 0)
            {
                var node = open.Min!; open.Remove(node);
                if (distances[node.Position] != node.G) continue;
                if (node.Position == target)
                {
                    var route = new List<Coord> { target }; var p = target;
                    while (p != start) { p = parents[p]; route.Add(p); }
                    route.Reverse(); return new PathResult(PathOutcome.PathFound, route, node.G, expansions);
                }
                if (expansions >= budget) return new PathResult(PathOutcome.BudgetExhausted, expansions: expansions);
                expansions++;
                foreach (var next in node.Position.Neighbors())
                {
                    if (!Contains(next)) continue;
                    long g = checked(node.G + EdgeTicks(node.Position, next));
                    if (distances.TryGetValue(next, out long previous) && g >= previous) continue;
                    distances[next] = g; parents[next] = node.Position;
                    open.Add(new Node { Position = next, G = g, F = checked(g + Heuristic(next)), Sequence = sequence++ });
                }
            }
            return new PathResult(PathOutcome.NoRoute, expansions: expansions);
        }
    }
}
