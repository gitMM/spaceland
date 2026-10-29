using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class ConstructionPlanning
    {
        private sealed class BindingLimit : Exception { }
        private sealed class Part
        { internal long Id; internal string Resource = ""; internal long Quantity; internal Coord At; internal bool Held; internal long Cost; internal ulong Bit; }
        public static bool KnownCompleted(IActorView view, ConstructionOrder order) => view.Knowledge.Any(f => f.Key.Equals(new FactKey(SubjectKind.Tile, order.TileId, AttributeKind.Location)) && f.Value.Equals(FactValue.At(order.Site)));
        public static PlanResult Plan(IActorView view, ConstructionOrder order, int candidates = 3, int depth = 8, int budget = 512)
        {
            if (candidates < 1 || candidates > 30) throw new ArgumentException("Invalid construction candidate bound.");
            if (!view.CurrentTile.HasValue || view.PublicKnowledge?.Work == null || !order.Roles.Contains(view.Role, StringComparer.Ordinal)) return new PlanResult(PlanOutcome.NoPlan);
            if (KnownCompleted(view, order)) return new PlanResult(PlanOutcome.PlanFound);
            try { return Build(view, order, candidates, depth, budget); } catch (BindingLimit) { return new PlanResult(PlanOutcome.BudgetExhausted); }
        }
        private static PlanResult Build(IActorView view, ConstructionOrder order, int k, int depth, int budget)
        {
            var names = new List<string> { "ConstructionCompleted" }; var actions = new List<SymbolicAction>();
            ulong Bit(string name) { if (names.Count == 64) throw new BindingLimit(); ulong b = 1UL << names.Count; names.Add(name); return b; }
            var locations = new SortedDictionary<Coord, ulong>();
            ulong At(Coord c) { if (!locations.TryGetValue(c, out ulong b)) { b = Bit("At:" + c); locations.Add(c, b); } return b; }
            var known = new SortedDictionary<FactKey, FactValue>(); foreach (var f in view.Knowledge) known.Add(f.Key, f.Value);
            FactValue? Fact(long id, AttributeKind attribute) => known.TryGetValue(new FactKey(SubjectKind.Item, id, attribute), out var value) ? value : (FactValue?)null;
            var standing = order.Site.Neighbors().Select(c => (At: c, Cost: KnownNavigation.Estimate(view, view.CurrentTile!.Value, c))).Where(c => c.Cost.HasValue)
                .OrderBy(c => c.Cost).ThenBy(c => c.At).Take(k).Select(c => c.At).ToArray();
            if (standing.Length == 0) return new PlanResult(PlanOutcome.NoPlan);
            var required = new SortedSet<string>(order.Recipe.Inputs.Select(m => m.Resource), StringComparer.Ordinal); if (order.ToolResource.Length > 0) required.Add(order.ToolResource);
            var parts = new List<Part>();
            foreach (var f in view.Knowledge.Where(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.Attribute == AttributeKind.Resource && f.Value.Kind == FactValueKind.Text && required.Contains(f.Value.Text)))
            {
                long id = f.Key.SubjectId; bool held = view.Inventory.Any(i => i.Value == id);
                var amount = Fact(id, AttributeKind.Quantity); var at = Fact(id, AttributeKind.Location); var kind = Fact(id, AttributeKind.LocationKind);
                if (amount?.Kind != FactValueKind.Number || amount.Value.Number < 1 || Fact(id, AttributeKind.Present)?.Equals(FactValue.Boolean(false)) == true ||
                    view.Knowledge.Any(c => c.Key.SubjectKind == SubjectKind.Credential && c.Key.Attribute == AttributeKind.CredentialCard && c.Value.Equals(FactValue.Integer(id)))) continue;
                if (!held && (at?.Kind != FactValueKind.Coordinate || kind?.Kind != FactValueKind.Number || !(kind.Value.Number == (long)LocationKind.TileContainer || kind.Value.Number == (long)LocationKind.ServiceOutput || kind.Value.Number == (long)LocationKind.ProjectStash))) continue;
                Coord position = held ? view.CurrentTile!.Value : at!.Value.Coordinate;
                long? cost = KnownNavigation.Estimate(view, view.CurrentTile!.Value, position);
                var onward = standing.Select(c => KnownNavigation.Estimate(view, position, c)).Where(c => c.HasValue).ToArray();
                if (!cost.HasValue || onward.Length == 0) continue;
                parts.Add(new Part { Id = id, Resource = f.Value.Text, Quantity = amount.Value.Number, At = position, Held = held, Cost = checked(cost.Value + onward.Min()!.Value + (held ? 0 : view.PublicKnowledge!.Work!.PickupTicks)) });
            }
            parts = parts.GroupBy(p => p.Resource).OrderBy(g => g.Key, StringComparer.Ordinal).SelectMany(g => g.OrderBy(p => p.Cost).ThenBy(p => p.Id).Take(k)).ToList();
            ulong initial = At(view.CurrentTile!.Value);
            foreach (var p in parts)
            {
                p.Bit = Bit("Holds:" + p.Id); if (p.Held) initial |= p.Bit;
                else actions.Add(new SymbolicAction("Collect:" + p.Id, At(p.At), p.Bit, p.Bit, 0, view.PublicKnowledge!.Work!.PickupTicks, new ActionIntent(view.Actor, ActionKind.PickUp, p.At, new Id<ItemTag>(p.Id))));
            }
            var tools = order.ToolResource.Length == 0 ? new Part?[] { null } : parts.Where(p => p.Resource == order.ToolResource).Cast<Part?>().ToArray();
            foreach (var tool in tools)
            {
                var bundles = new List<ulong> { tool?.Bit ?? 0 };
                foreach (var input in order.Recipe.Inputs)
                {
                    var pool = parts.Where(p => p.Resource == input.Resource && p != tool).OrderBy(p => p.Held ? 0 : 1).ThenBy(p => p.Cost).ThenBy(p => p.Id).ToArray();
                    var alternatives = new SortedSet<ulong>();
                    for (int first = 0; first < pool.Length; first++)
                    {
                        long left = input.Quantity; ulong bits = 0;
                        foreach (var p in new[] { pool[first] }.Concat(pool.Where((p, i) => i != first))) { bits |= p.Bit; left -= Math.Min(left, p.Quantity); if (left == 0) break; }
                        if (left == 0) alternatives.Add(bits);
                    }
                    bundles = bundles.SelectMany(b => alternatives.Select(a => b | a)).Distinct().Take(k).ToList();
                }
                foreach (var site in standing) foreach (ulong bundle in bundles)
                    actions.Add(new SymbolicAction("Construct:" + order.ProjectId + "@" + site + "/" + bundle, At(site) | bundle, 1, 1, 0, order.Recipe.Duration,
                        new ActionIntent(view.Actor, ActionKind.Construct, site, serviceId: order.ProjectId, recipeId: order.Recipe.Id)));
            }
            ulong allLocations = locations.Values.Aggregate(0UL, (a, b) => a | b);
            foreach (var from in locations) foreach (var to in locations)
            {
                if (from.Key == to.Key) continue; long? cost = KnownNavigation.Estimate(view, from.Key, to.Key); if (!cost.HasValue) continue;
                actions.Add(new SymbolicAction("Travel:" + from.Key + "->" + to.Key, from.Value, to.Value, to.Value, allLocations & ~to.Value, cost.Value, new ActionIntent(view.Actor, ActionKind.Move, to.Key)));
            }
            return GoapPlanner.Search(new PlanningProblem(initial, 1, names, actions), depth, budget);
        }
    }
}
