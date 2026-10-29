using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class KnownNavigation
    {
        private sealed class Node : IComparable<Node>
        {
            internal int Index; internal long Cost; internal long Sequence;
            public int CompareTo(Node? other) { if (other == null) return 1; int c = Cost.CompareTo(other.Cost); return c == 0 ? Sequence.CompareTo(other.Sequence) : c; }
        }
        public static long? Estimate(IActorView view, Coord from, Coord target)
            => new Distances(view, from).To(target);

        // One immutable actor view, one source, many candidate destinations. The
        // frontier is reused only inside this planning request, never across knowledge changes.
        public sealed class Distances
        {
            private readonly Coord[] positions = Array.Empty<Coord>();
            private readonly long[] traversal = Array.Empty<long>();
            private readonly SortedSet<Node> open = new SortedSet<Node>();
            private readonly long[] best = Array.Empty<long>();
            private readonly bool[] reached = Array.Empty<bool>();
            private readonly bool[] settled = Array.Empty<bool>();
            private long sequence;
            private Node? pendingExpansion;
            public Distances(IActorView view, Coord from)
            {
                if (view.PublicKnowledge == null) return;
                var known = new SortedDictionary<Coord, long>();
                var overrides = new SortedDictionary<long, long>();
                foreach (var fact in view.Knowledge.Where(f => f.Key.SubjectKind == SubjectKind.Tile && f.Key.Attribute == AttributeKind.TraversalTicks && f.Value.Kind == FactValueKind.Number && f.Value.Number > 0)) overrides[fact.Key.SubjectId] = fact.Value.Number;
                foreach (var tile in view.PublicKnowledge.Layout) known.Add(tile.Position, overrides.TryGetValue(tile.Id.Value, out long cost) ? cost : tile.TraversalTicks);
                foreach (var fact in view.Knowledge.Where(f => f.Key.SubjectKind == SubjectKind.Tile && f.Key.Attribute == AttributeKind.Location && f.Value.Kind == FactValueKind.Coordinate))
                    if (overrides.TryGetValue(fact.Key.SubjectId, out long cost)) known[fact.Value.Coordinate] = cost;
                // Compact, sorted coordinates preserve deterministic lookup while
                // avoiding tree-node allocations for every visited distance.
                positions = known.Keys.ToArray(); traversal = known.Values.ToArray();
                best = new long[positions.Length]; reached = new bool[positions.Length]; settled = new bool[positions.Length];
                int source = Array.BinarySearch(positions, from); if (source < 0) return;
                reached[source] = true; open.Add(new Node { Index = source, Sequence = sequence++ });
            }
            public long? To(Coord target)
            {
                int targetIndex = Array.BinarySearch(positions, target); if (targetIndex < 0) return null;
                if (settled[targetIndex]) return best[targetIndex];
                if (pendingExpansion != null) { Expand(pendingExpansion); pendingExpansion = null; }
                while (open.Count > 0)
                {
                    var node = open.Min!; open.Remove(node); if (best[node.Index] != node.Cost) continue;
                    settled[node.Index] = true;
                    // Match a single-target search: do not inspect edges beyond its
                    // answer (which might overflow). Expand only if another target needs it.
                    if (node.Index == targetIndex) { pendingExpansion = node; return node.Cost; }
                    Expand(node);
                }
                return null;
            }
            private void Expand(Node node)
            {
                foreach (var next in positions[node.Index].Neighbors())
                {
                    int index = Array.BinarySearch(positions, next); if (index < 0) continue;
                    long sum = checked(traversal[node.Index] + traversal[index]); long cost = checked(node.Cost + sum / 2 + sum % 2);
                    if (reached[index] && best[index] <= cost) continue;
                    best[index] = cost; reached[index] = true; open.Add(new Node { Index = index, Cost = cost, Sequence = sequence++ });
                }
            }
        }
    }
}
