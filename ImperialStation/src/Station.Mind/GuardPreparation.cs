using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class GuardPreparation
    {
        public static bool Equipped(IActorView view, string resource) => view.EquippedItem.Value > 0 && view.Inventory.Contains(view.EquippedItem) &&
            view.Knowledge.Any(f => f.Key.Equals(new FactKey(SubjectKind.Item, view.EquippedItem.Value, AttributeKind.Resource)) && f.Value.Kind == FactValueKind.Text && f.Value.Text == resource);

        public static PlanResult Plan(IActorView view, EscortOrderPayload order, int candidates = 3, int depth = 8, int budget = 512)
        {
            if (candidates < 1 || candidates > 30) throw new ArgumentException("Invalid equipment planning candidate bound.");
            if (!view.CurrentTile.HasValue || view.Role != order.Dispatch.GuardRole || view.Tick >= order.Expires || !order.Guards.Contains(view.Actor.Value) || view.PublicKnowledge?.Work == null)
                return new PlanResult(PlanOutcome.NoPlan);
            var names = new List<string> { "Equipped" };
            var locations = new SortedDictionary<Coord, ulong>();
            ulong At(Coord at) { if (!locations.TryGetValue(at, out ulong bit)) { bit = 1UL << names.Count; names.Add("At:" + at); locations.Add(at, bit); } return bit; }
            ulong initial = At(view.CurrentTile.Value), atRendezvous = At(order.Dispatch.Rendezvous);
            var steps = new List<SymbolicAction>();
            if (Equipped(view, order.Dispatch.RequiredEquipment)) initial |= 1;
            else
            {
                var tools = new List<(long Id, Coord At, bool Held, long Cost)>();
                foreach (var f in view.Knowledge.Where(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.Attribute == AttributeKind.Resource && f.Value.Kind == FactValueKind.Text && f.Value.Text == order.Dispatch.RequiredEquipment))
                {
                    long id = f.Key.SubjectId; bool held = view.Inventory.Any(i => i.Value == id);
                    KnowledgeFact? Fact(AttributeKind attribute) => view.Knowledge.SingleOrDefault(k => k.Key.Equals(new FactKey(SubjectKind.Item, id, attribute)));
                    var location = Fact(AttributeKind.Location); var quantity = Fact(AttributeKind.Quantity); var ownerKind = Fact(AttributeKind.LocationKind);
                    if (quantity?.Value.Kind != FactValueKind.Number || quantity.Value.Number < 1 || Fact(AttributeKind.Present)?.Value.Equals(FactValue.Boolean(false)) == true) continue;
                    if (!held && (location?.Value.Kind != FactValueKind.Coordinate || ownerKind?.Value.Kind != FactValueKind.Number ||
                        !(ownerKind.Value.Number == (long)LocationKind.TileContainer || ownerKind.Value.Number == (long)LocationKind.ServiceOutput || ownerKind.Value.Number == (long)LocationKind.ProjectStash))) continue;
                    Coord at = held ? view.CurrentTile.Value : location!.Value.Coordinate;
                    long? travel = KnownNavigation.Estimate(view, view.CurrentTile.Value, at), onward = KnownNavigation.Estimate(view, at, order.Dispatch.Rendezvous);
                    if (travel.HasValue && onward.HasValue) tools.Add((id, at, held, checked(travel.Value + onward.Value + order.Dispatch.EquipTicks + (held ? 0 : view.PublicKnowledge.Work.PickupTicks))));
                }
                foreach (var tool in tools.OrderBy(t => t.Cost).ThenBy(t => t.Id).Take(candidates))
                {
                    ulong at = At(tool.At), held = 1UL << names.Count; names.Add("Held:" + tool.Id);
                    if (tool.Held) initial |= held;
                    else steps.Add(new SymbolicAction("CollectGuardEquipment:" + tool.Id, at, held, held, 0, view.PublicKnowledge.Work.PickupTicks,
                        new ActionIntent(view.Actor, ActionKind.PickUp, tool.At, new Id<ItemTag>(tool.Id))));
                    // Equip must name the tile occupied when the action executes.
                    steps.Add(new SymbolicAction("EquipGuardEquipment:" + tool.Id, at | held, 1, 1, 0, order.Dispatch.EquipTicks,
                        new ActionIntent(view.Actor, ActionKind.Equip, tool.At, new Id<ItemTag>(tool.Id))));
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
            // Reaching the tile is verified from the next actor view, not a synthetic physical action.
            return GoapPlanner.Search(new PlanningProblem(initial, atRendezvous | 1, names, steps), depth, budget);
        }
    }
}
