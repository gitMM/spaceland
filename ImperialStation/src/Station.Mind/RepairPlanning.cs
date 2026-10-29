using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    /// <summary>Grounds a bounded repair domain from the citizen's evidence and published catalog.</summary>
    public static class RepairPlanning
    {
        private sealed class BindingLimit : Exception { }
        private sealed class Part
        {
            internal long Id;
            internal string Resource = "";
            internal long Quantity;
            internal Coord At;
            internal bool Held;
            internal long EstimatedCost;
            internal ulong AvailableBit;
            internal ulong HeldBit;
            internal ulong SingleBit;
        }
        private sealed class Domain
        {
            internal readonly List<string> Predicates = new List<string>();
            internal readonly List<SymbolicAction> Actions = new List<SymbolicAction>();
            internal readonly SortedDictionary<Coord, ulong> Locations = new SortedDictionary<Coord, ulong>();
            internal ulong Initial;
            internal ulong Bit(string name)
            { if (Predicates.Count == 64) throw new BindingLimit(); ulong bit = 1UL << Predicates.Count; Predicates.Add(name); return bit; }
            internal ulong Location(Coord at)
            { if (!Locations.TryGetValue(at, out ulong bit)) { bit = Bit("At:" + at); Locations.Add(at, bit); } return bit; }
        }
        public static PlanResult Plan(IActorView view, long machineId, int candidates = 3, int maxDepth = 8, int nodeBudget = 512)
        {
            if (candidates < 1 || candidates > 30) throw new ArgumentException("Invalid repair candidate limit.");
            if (view.CurrentTile == null || view.PublicKnowledge?.Work == null) return new PlanResult(PlanOutcome.NoPlan);
            try { return Build(view, machineId, candidates, maxDepth, nodeBudget); }
            catch (BindingLimit) { return new PlanResult(PlanOutcome.BudgetExhausted); }
        }
        private static PlanResult Build(IActorView view, long machineId, int k, int depth, int budget)
        {
            var catalog = view.PublicKnowledge!.Work!;
            var machine = catalog.Equipment.SingleOrDefault(m => m.Id == machineId);
            if (machine == null) return new PlanResult(PlanOutcome.NoPlan);
            var known = new SortedDictionary<FactKey, FactValue>(); foreach (var fact in view.Knowledge) known.Add(fact.Key, fact.Value);
            FactValue? Fact(SubjectKind subject, long id, AttributeKind attribute) => known.TryGetValue(new FactKey(subject, id, attribute), out var value) ? value : (FactValue?)null;
            if (Fact(SubjectKind.Machine, machineId, AttributeKind.Operational)?.Equals(FactValue.Boolean(true)) == true)
                return new PlanResult(PlanOutcome.PlanFound);
            var d = new Domain(); ulong goal = d.Bit("MachineOperational:" + machineId);
            d.Initial |= d.Location(view.CurrentTile!.Value); ulong atMachine = d.Location(machine.Position);
            var required = Fact(SubjectKind.Machine, machineId, AttributeKind.RequiredResource);
            if (required?.Kind != FactValueKind.Text || string.IsNullOrWhiteSpace(required.Value.Text))
            {
                // This request's goal is learning the specification. The actual observation drives
                // the next request; no hypothetical part type is obtained from physical truth.
                d.Actions.Add(new SymbolicAction("InspectSpecification:" + machineId, atMachine, goal, goal, 0, machine.InspectTicks,
                    new ActionIntent(view.Actor, ActionKind.InspectMachine, machine.Position, serviceId: machineId)));
                AddTravel(view, d); return GoapPlanner.Search(new PlanningProblem(d.Initial, goal, d.Predicates, d.Actions), depth, budget);
            }
            if (!machine.Roles.Contains(view.Role, StringComparer.Ordinal)) return new PlanResult(PlanOutcome.NoPlan);
            string wanted = required.Value.Text;
            var manufacturing = catalog.Recipes.Where(r => r.Outputs.Any(o => o.Resource == wanted))
                .SelectMany(r => catalog.Workstations.Where(s => s.Capability == r.Capability && s.Roles.Contains(view.Role, StringComparer.Ordinal))
                    .Select(s => (Recipe: r, Station: s, Travel: KnownNavigation.Estimate(view, view.CurrentTile.Value, s.Position)))
                    .Where(x => x.Travel.HasValue).OrderBy(x => x.Travel!.Value).ThenBy(x => x.Station.Id).Take(k))
                .OrderBy(x => checked(x.Recipe.Duration + x.Travel!.Value)).ThenBy(x => x.Recipe.Id).ThenBy(x => x.Station.Id)
                .GroupBy(x => x.Recipe.Id).Take(k).SelectMany(g => g).ToArray();
            var needed = new SortedSet<string>(StringComparer.Ordinal) { wanted };
            if (machine.ToolResource.Length > 0) needed.Add(machine.ToolResource);
            foreach (var option in manufacturing)
            { foreach (var input in option.Recipe.Inputs) needed.Add(input.Resource); if (option.Station.ToolResource.Length > 0) needed.Add(option.Station.ToolResource); }

            var allKnown = new List<Part>();
            foreach (var resourceFact in view.Knowledge.Where(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.Attribute == AttributeKind.Resource && f.Value.Kind == FactValueKind.Text && needed.Contains(f.Value.Text)))
            {
                long id = resourceFact.Key.SubjectId; bool held = view.Inventory.Any(i => i.Value == id);
                var quantity = Fact(SubjectKind.Item, id, AttributeKind.Quantity); var location = Fact(SubjectKind.Item, id, AttributeKind.Location);
                var kind = Fact(SubjectKind.Item, id, AttributeKind.LocationKind);
                if (quantity?.Kind != FactValueKind.Number || quantity.Value.Number < 1 || Fact(SubjectKind.Item, id, AttributeKind.Present)?.Equals(FactValue.Boolean(false)) == true) continue;
                if (!held && (location?.Kind != FactValueKind.Coordinate || (kind.HasValue && kind.Value.Number != (long)LocationKind.TileContainer && kind.Value.Number != (long)LocationKind.ServiceOutput && kind.Value.Number != (long)LocationKind.ProjectStash))) continue;
                Coord at = held ? view.CurrentTile.Value : location!.Value.Coordinate;
                long? travel = KnownNavigation.Estimate(view, view.CurrentTile.Value, at), onward = KnownNavigation.Estimate(view, at, machine.Position);
                if (!travel.HasValue || !onward.HasValue) continue;
                allKnown.Add(new Part { Id = id, Resource = resourceFact.Value.Text, Quantity = quantity.Value.Number, At = at, Held = held,
                    EstimatedCost = checked(travel.Value + onward.Value + (held ? 0 : catalog.PickupTicks) + (resourceFact.Value.Text == wanted && quantity.Value.Number > 1 ? catalog.PickupTicks : 0)) });
            }
            var parts = allKnown.GroupBy(p => p.Resource).OrderBy(g => g.Key, StringComparer.Ordinal)
                .SelectMany(g => g.OrderBy(p => p.EstimatedCost).ThenBy(p => p.Id).Take(k)).ToList();
            foreach (var part in parts) BindPart(view, d, part, "Item:" + part.Id, catalog.PickupTicks, false);
            var targets = parts.Where(p => p.Resource == wanted).ToList();
            ulong fabricated = d.Bit("FabricatedForThisRepair");
            foreach (var option in manufacturing)
            {
                var bundles = MaterialBundles(option.Recipe, parts, k);
                if (bundles.Count == 0) continue;
                var tools = ToolOptions(parts, option.Station.ToolResource).ToArray(); if (tools.Length == 0) continue;
                var output = new Part { Resource = wanted, Quantity = option.Recipe.Outputs.Single(o => o.Resource == wanted).Quantity, At = option.Station.Position };
                BindPart(view, d, output, "FutureOutput:" + option.Recipe.Id + ":" + option.Station.Id, catalog.PickupTicks, true);
                targets.Add(output);
                foreach (var bundle in bundles) foreach (var tool in tools)
                {
                    if (tool != null && bundle.Any(b => b.Part.Id == tool.Id)) continue;
                    ulong pre = d.Location(option.Station.Position) | (tool?.HeldBit ?? 0), remove = 0;
                    foreach (var b in bundle) { pre |= b.Part.HeldBit; if (b.Amount == b.Part.Quantity) remove |= b.Part.HeldBit; }
                    d.Actions.Add(new SymbolicAction("Fabricate:" + option.Recipe.Id + "@" + option.Station.Id, pre, fabricated,
                        output.AvailableBit | fabricated, remove, option.Recipe.Duration,
                        new ActionIntent(view.Actor, ActionKind.ProduceRecipe, option.Station.Position, serviceId: option.Station.Id, recipeId: option.Recipe.Id)));
                }
            }
            foreach (var part in targets)
            {
                if (part.Quantity > 1)
                {
                    part.SingleBit = d.Bit("SingleCartridge:" + part.Id + ":" + part.HeldBit);
                    // Keep one unit on the bound ID; the remainder receives a real ID at completion.
                    d.Actions.Add(new SymbolicAction("SeparateCartridge:" + part.Id, d.Location(part.At) | part.HeldBit, part.SingleBit, part.SingleBit, 0, catalog.PickupTicks,
                        new ActionIntent(view.Actor, ActionKind.SplitStack, part.At, new Id<ItemTag>(part.Id), quantity: part.Quantity - 1)));
                }
                foreach (var tool in ToolOptions(parts, machine.ToolResource))
                {
                    if (tool != null && tool == part) continue;
                    d.Actions.Add(new SymbolicAction("Install:" + (part.Id == 0 ? "future-output" : part.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "@" + machineId,
                        atMachine | part.HeldBit | part.SingleBit | (tool?.HeldBit ?? 0), goal, goal, part.HeldBit, machine.InstallTicks,
                        new ActionIntent(view.Actor, ActionKind.Install, machine.Position, new Id<ItemTag>(part.Id), machineId)));
                }
            }
            AddTravel(view, d);
            return GoapPlanner.Search(new PlanningProblem(d.Initial, goal, d.Predicates, d.Actions), depth, budget);
        }
        private static IEnumerable<Part?> ToolOptions(List<Part> parts, string resource)
            => resource.Length == 0 ? new Part?[] { null } : parts.Where(p => p.Resource == resource).Cast<Part?>();
        private static void BindPart(IActorView view, Domain d, Part part, string name, long pickupTicks, bool hypothetical)
        {
            part.AvailableBit = d.Bit("Available:" + name); part.HeldBit = d.Bit("Holds:" + name);
            if (part.Held) d.Initial |= part.HeldBit; else if (!hypothetical) d.Initial |= part.AvailableBit;
            ulong at = d.Location(part.At);
            d.Actions.Add(new SymbolicAction("PickUp:" + name, at | part.AvailableBit, part.HeldBit, part.HeldBit, part.AvailableBit, pickupTicks,
                new ActionIntent(view.Actor, ActionKind.PickUp, part.At, new Id<ItemTag>(part.Id))));
        }
        private static void AddTravel(IActorView view, Domain d)
        {
            ulong all = d.Locations.Values.Aggregate(0UL, (a, b) => a | b);
            foreach (var from in d.Locations) foreach (var to in d.Locations)
            {
                if (from.Key == to.Key) continue;
                long? cost = KnownNavigation.Estimate(view, from.Key, to.Key); if (!cost.HasValue) continue;
                d.Actions.Add(new SymbolicAction("Travel:" + from.Key + "->" + to.Key, from.Value, to.Value, to.Value, all & ~to.Value, cost.Value,
                    new ActionIntent(view.Actor, ActionKind.Move, to.Key)));
            }
        }
        private sealed class Use { internal Part Part = null!; internal long Amount; }
        private static List<List<Use>> MaterialBundles(RecipeDefinition recipe, List<Part> parts, int k)
        {
            var bundles = new List<List<Use>> { new List<Use>() };
            foreach (var input in recipe.Inputs)
            {
                var candidates = parts.Where(p => p.Resource == input.Resource).OrderBy(p => p.Held ? 0 : 1).ThenBy(p => p.EstimatedCost).ThenBy(p => p.Id).ToArray();
                var alternatives = new List<List<Use>>(); var seen = new SortedSet<string>(StringComparer.Ordinal);
                // At most K ways to assemble each prerequisite, each from at most K known stacks.
                for (int first = 0; first < candidates.Length; first++)
                {
                    long left = input.Quantity; var use = new List<Use>();
                    foreach (var part in new[] { candidates[first] }.Concat(candidates.Where((p, i) => i != first)))
                    { long amount = Math.Min(left, part.Quantity); use.Add(new Use { Part = part, Amount = amount }); left -= amount; if (left == 0) break; }
                    string key = string.Join(",", use.OrderBy(u => u.Part.Id).Select(u => u.Part.Id + ":" + u.Amount));
                    if (left == 0 && seen.Add(key)) alternatives.Add(use);
                }
                bundles = bundles.SelectMany(b => alternatives.Select(a => b.Concat(a).ToList())).Take(k).ToList();
                if (bundles.Count == 0) break;
            }
            return bundles;
        }
    }
}
