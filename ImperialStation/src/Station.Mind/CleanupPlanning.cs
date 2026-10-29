using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class CleanupPlanning
    {
        // Plan one verified observation or cleaning step, then replan from local evidence.
        public static PlanResult Plan(IActorView view, Id<TileTag> tile, bool checkedHere, long inspectTicks, int candidates = 3, int depth = 8, int budget = 512)
        {
            if (candidates < 1 || candidates > 30 || inspectTicks < 1) throw new ArgumentException("Invalid cleanup planning bounds.");
            var site = view.PublicKnowledge?.Work?.Cleanable.SingleOrDefault(c => c.Tile == tile);
            if (!view.CurrentTile.HasValue || site == null || !site.Roles.Contains(view.Role, StringComparer.Ordinal)) return new PlanResult(PlanOutcome.NoPlan);
            var dirt = view.Knowledge.SingleOrDefault(f => f.Key.Equals(new FactKey(SubjectKind.Tile, tile.Value, AttributeKind.Dirt)));
            bool verified = checkedHere && view.CurrentTile.Value == site.Position && dirt != null && dirt.Value.Kind == FactValueKind.Number && dirt.Value.Number >= 0;
            if (verified && dirt!.Value.Number == 0) return new PlanResult(PlanOutcome.PlanFound);
            var names = new List<string> { "CleanupStepVerified", "ToolHeld" };
            var locations = new SortedDictionary<Coord, ulong>();
            ulong At(Coord c) { if (!locations.TryGetValue(c, out var bit)) { bit = 1UL << names.Count; names.Add("At:" + c); locations.Add(c, bit); } return bit; }
            ulong initial = At(view.CurrentTile.Value), atSite = At(site.Position);
            var steps = new List<SymbolicAction>();
            if (site.ToolResource.Length == 0) initial |= 2;
            else
            {
                var tools = new List<(long Id, Coord At, bool Held, long Cost)>();
                foreach (var f in view.Knowledge.Where(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.Attribute == AttributeKind.Resource && f.Value.Kind == FactValueKind.Text && f.Value.Text == site.ToolResource))
                {
                    long id = f.Key.SubjectId; bool held = view.Inventory.Any(i => i.Value == id);
                    KnowledgeFact? Fact(AttributeKind attribute) => view.Knowledge.SingleOrDefault(k => k.Key.Equals(new FactKey(SubjectKind.Item, id, attribute)));
                    var position = Fact(AttributeKind.Location); var quantity = Fact(AttributeKind.Quantity); var ownerKind = Fact(AttributeKind.LocationKind);
                    if (quantity?.Value.Kind != FactValueKind.Number || quantity.Value.Number < 1 || Fact(AttributeKind.Present)?.Value.Equals(FactValue.Boolean(false)) == true) continue;
                    if (!held && (position?.Value.Kind != FactValueKind.Coordinate || ownerKind?.Value.Kind != FactValueKind.Number ||
                        !(ownerKind.Value.Number == (long)LocationKind.TileContainer || ownerKind.Value.Number == (long)LocationKind.ServiceOutput || ownerKind.Value.Number == (long)LocationKind.ProjectStash))) continue;
                    var at = held ? view.CurrentTile.Value : position!.Value.Coordinate;
                    long? travel = KnownNavigation.Estimate(view, view.CurrentTile.Value, at), onward = KnownNavigation.Estimate(view, at, site.Position);
                    if (travel.HasValue && onward.HasValue) tools.Add((id, at, held, checked(travel.Value + onward.Value + (held ? 0 : view.PublicKnowledge!.Work!.PickupTicks))));
                }
                foreach (var tool in tools.OrderBy(t => t.Cost).ThenBy(t => t.Id).Take(candidates))
                {
                    if (tool.Held) initial |= 2;
                    else steps.Add(new SymbolicAction("CollectCleanupTool:" + tool.Id, At(tool.At), 2, 2, 0, view.PublicKnowledge!.Work!.PickupTicks,
                        new ActionIntent(view.Actor, ActionKind.PickUp, tool.At, new Id<ItemTag>(tool.Id))));
                }
            }
            foreach (var from in locations)
            foreach (var to in locations)
            {
                if (from.Key == to.Key) continue;
                long? cost = KnownNavigation.Estimate(view, from.Key, to.Key);
                if (cost.HasValue) steps.Add(new SymbolicAction("Travel:" + from.Key + ":" + to.Key, from.Value, to.Value, to.Value, from.Value, cost.Value,
                    new ActionIntent(view.Actor, ActionKind.Move, to.Key)));
            }
            steps.Add(new SymbolicAction(verified ? "CleanOneUnit" : "InspectCleanupTile", atSite | 2, 1, 1, 0, verified ? site.UnitTicks : inspectTicks,
                new ActionIntent(view.Actor, verified ? ActionKind.Clean : ActionKind.InspectTile, site.Position)));
            return GoapPlanner.Search(new PlanningProblem(initial, 1, names, steps), depth, budget);
        }
    }
}
