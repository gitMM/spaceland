using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteAccess(BinaryWriter w, WorkAccess a)
        { w.Write(a.Roles.Count); foreach (string role in a.Roles) w.Write(role); w.Write(a.ToolResource); w.Write(a.Staff.Value); w.Write(a.RequiresCredential); }
        private static WorkAccess ReadAccess(BinaryReader r)
        { int count = Count(r); var roles = new List<string>(); for (int i = 0; i < count; i++) roles.Add(r.ReadString()); return new WorkAccess(roles, r.ReadString(), new Id<ActorTag>(r.ReadInt64()), r.ReadBoolean()); }
        private static void WriteAmounts(BinaryWriter w, IReadOnlyList<MaterialAmount> amounts)
        { w.Write(amounts.Count); foreach (var a in amounts) { w.Write(a.Resource); w.Write(a.Quantity); } }
        private static List<MaterialAmount> ReadAmounts(BinaryReader r)
        { int count = Count(r); var amounts = new List<MaterialAmount>(); for (int i = 0; i < count; i++) amounts.Add(new MaterialAmount(r.ReadString(), r.ReadInt64())); return amounts; }
        private void WriteWorkDefinitions(BinaryWriter w)
        {
            w.Write(WorkEnabled); if (!WorkEnabled) return;
            w.Write(resources.Count); foreach (var x in resources.Values) { w.Write(x.Id); w.Write(x.FitClass); w.Write(x.Rating); }
            w.Write(recipes.Count);
            foreach (var x in recipes.Values) { w.Write(x.Id); w.Write(x.Capability); w.Write(x.Duration); WriteAmounts(w, x.Inputs); WriteAmounts(w, x.Outputs); }
            w.Write(workstations.Count);
            foreach (var x in workstations.Values) { w.Write(x.Id); WriteCoord(w, x.Position); w.Write(x.Capability); w.Write(x.Capacity); WriteAccess(w, x.Access); }
            w.Write(machines.Count);
            foreach (var x in machines.Values)
            { w.Write(x.Id); WriteCoord(w, x.Position); w.Write(x.FitClass); w.Write(x.RequiredResource); w.Write(x.RequiredRating); w.Write(x.InstallTicks); w.Write(x.InspectTicks); w.Write(x.TestTicks); WriteAccess(w, x.Access); }
            w.Write(stashes.Count); foreach (var x in stashes.Values) { w.Write(x.Id); w.Write(x.ProjectId); WriteCoord(w, x.Position); }
        }
        private static WorkSeed? ReadWorkDefinitions(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            var resources = new List<ResourceDefinition>(); int count = Count(r);
            for (int i = 0; i < count; i++) resources.Add(new ResourceDefinition(r.ReadString(), r.ReadString(), r.ReadInt32()));
            var recipes = new List<RecipeDefinition>(); count = Count(r);
            for (int i = 0; i < count; i++) { long id = r.ReadInt64(); string cap = r.ReadString(); long duration = r.ReadInt64(); recipes.Add(new RecipeDefinition(id, cap, ReadAmounts(r), ReadAmounts(r), duration)); }
            var stations = new List<WorkstationDefinition>(); count = Count(r);
            for (int i = 0; i < count; i++) { long id = r.ReadInt64(); var at = ReadCoord(r); string cap = r.ReadString(); int capacity = r.ReadInt32(); stations.Add(new WorkstationDefinition(id, at, cap, ReadAccess(r), capacity)); }
            var machines = new List<MachineDefinition>(); count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long id = r.ReadInt64(); var at = ReadCoord(r); string fit = r.ReadString(), required = r.ReadString(); int rating = r.ReadInt32();
                long install = r.ReadInt64(), inspect = r.ReadInt64(), test = r.ReadInt64();
                machines.Add(new MachineDefinition(id, at, fit, required, ReadAccess(r), rating, install, inspect, test));
            }
            count = Count(r); var stashes = new List<ProjectStashDefinition>();
            for (int i = 0; i < count; i++) stashes.Add(new ProjectStashDefinition(r.ReadInt64(), r.ReadInt64(), ReadCoord(r)));
            return new WorkSeed(resources, recipes, stations, machines, stashes);
        }
        private void WriteWorkState(BinaryWriter w)
        {
            w.Write(nextItem); w.Write(nextTransaction);
            w.Write(disabledWorkstations.Count); foreach (long id in disabledWorkstations) w.Write(id);
            w.Write(disabledMachines.Count); foreach (long id in disabledMachines) w.Write(id);
            w.Write(resourceClaims.Count);
            foreach (var c in resourceClaims) { w.Write((int)c.Kind); w.Write(c.ResourceId); w.Write(c.ActorId); w.Write(c.ActionId); w.Write(c.RequestedTick); }
            w.Write(resourceTransactions.Count);
            foreach (var t in resourceTransactions) { w.Write(t.Id); w.Write(t.Tick); w.Write(t.ActorId); w.Write(t.ActionId); w.Write(t.RecipeId); w.Write(t.Resource); w.Write(t.Delta); }
        }
        private void ReadWorkState(BinaryReader r)
        {
            nextItem = r.ReadInt64(); nextTransaction = r.ReadInt64();
            int count = Count(r); for (int i = 0; i < count; i++) if (!disabledWorkstations.Add(r.ReadInt64())) throw new ArgumentException("Duplicate disabled workstation.");
            count = Count(r); for (int i = 0; i < count; i++) if (!disabledMachines.Add(r.ReadInt64())) throw new ArgumentException("Duplicate disabled machine.");
            count = Count(r);
            for (int i = 0; i < count; i++) resourceClaims.Add(new ResourceClaim { Kind = ReadEnum<ClaimKind>(r), ResourceId = r.ReadInt64(), ActorId = r.ReadInt64(), ActionId = r.ReadInt64(), RequestedTick = r.ReadInt64() });
            count = Count(r);
            for (int i = 0; i < count; i++) resourceTransactions.Add(new ResourceTransaction { Id = r.ReadInt64(), Tick = r.ReadInt64(), ActorId = r.ReadInt64(), ActionId = r.ReadInt64(), RecipeId = r.ReadInt64(), Resource = r.ReadString(), Delta = r.ReadInt64() });
            if (nextItem < 1 || items.Keys.Any(i => i.Value >= nextItem) || nextTransaction < 1 || resourceTransactions.Any(t => t.Id >= nextTransaction || t.Id < 1 || !resources.ContainsKey(t.Resource) || !recipes.ContainsKey(t.RecipeId) || !actors.ContainsKey(new Id<ActorTag>(t.ActorId))))
                throw new ArgumentException("Invalid resource counters or transactions.");
            if (disabledWorkstations.Any(id => !workstations.ContainsKey(id)) || disabledMachines.Any(id => !machines.ContainsKey(id))) throw new ArgumentException("Unknown disabled work service.");
            foreach (var c in resourceClaims)
            {
                if (!actors.TryGetValue(new Id<ActorTag>(c.ActorId), out var actor) || actor.Action?.Id.Value != c.ActionId || c.RequestedTick != actor.Action.Started)
                    throw new ArgumentException("Resource claim does not own an active action.");
                bool exists = c.Kind == ClaimKind.Item ? items.ContainsKey(new Id<ItemTag>(c.ResourceId)) : c.Kind == ClaimKind.Workstation ? workstations.ContainsKey(c.ResourceId) : c.Kind == ClaimKind.Machine ? machines.ContainsKey(c.ResourceId) : c.Kind == ClaimKind.ConstructionSite ? constructionProjects.ContainsKey(c.ResourceId) : actors.ContainsKey(new Id<ActorTag>(c.ResourceId));
                if (!exists) throw new ArgumentException("Claim references an unknown resource.");
            }
            foreach (var g in resourceClaims.GroupBy(c => (c.Kind, c.ResourceId)))
            {
                int capacity = g.Key.Kind == ClaimKind.Workstation ? workstations[g.Key.ResourceId].Capacity : 1;
                if (g.Count() > capacity || g.Select(c => c.ActionId).Distinct().Count() != g.Count()) throw new ArgumentException("Invalid resource claim capacity.");
            }
            foreach (var actor in actors.Values.Where(a => a.Action != null))
            {
                var action = actor.Action!;
                bool Has(ClaimKind kind, long id) => resourceClaims.Any(c => c.Kind == kind && c.ResourceId == id && c.ActorId == actor.Id.Value && c.ActionId == action.Id.Value);
                if ((action.Kind == ActionKind.PickUp || action.Kind == ActionKind.PutDown || action.Kind == ActionKind.Install || IsStackAction(action.Kind)) && !Has(ClaimKind.Item, action.Item.Value))
                    throw new ArgumentException("Active item operation has no claim.");
                if (action.Kind == ActionKind.MergeStacks && !Has(ClaimKind.Item, action.OtherItem.Value)) throw new ArgumentException("Active merge has no source claim.");
                if (IsWorkAction(action.Kind))
                {
                    if (!Has(action.Kind == ActionKind.ProduceRecipe ? ClaimKind.Workstation : ClaimKind.Machine, action.ServiceId) || action.Materials.Any(b => b.Quantity < 0 || !Has(ClaimKind.Item, b.Item.Value)))
                        throw new ArgumentException("Active work has invalid resource bindings.");
                    if (action.Materials.Select(b => b.Item).Distinct().Count() != action.Materials.Count) throw new ArgumentException("Duplicate work binding.");
                }
            }
            if (resourceTransactions.Select(t => t.Id).Distinct().Count() != resourceTransactions.Count) throw new ArgumentException("Duplicate transaction ID.");
            foreach (var group in resourceTransactions.GroupBy(t => t.ActionId))
            {
                var first = group.First(); var recipe = recipes[first.RecipeId];
                var declared = recipe.Inputs.Select(i => (i.Resource, Delta: -i.Quantity)).Concat(recipe.Outputs.Select(o => (o.Resource, Delta: o.Quantity))).OrderBy(x => x.Resource, StringComparer.Ordinal).ThenBy(x => x.Delta);
                var actual = group.Select(t => (t.Resource, t.Delta)).OrderBy(x => x.Resource, StringComparer.Ordinal).ThenBy(x => x.Delta);
                if (group.Any(t => t.RecipeId != first.RecipeId || t.Tick != first.Tick || t.ActorId != first.ActorId) || !declared.SequenceEqual(actual))
                    throw new ArgumentException("Resource transaction does not match its declared recipe.");
            }
        }
    }
}
