using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteConstructionDefinition(BinaryWriter w, ConstructionDefinition? d)
        { w.Write(d != null); if (d == null) return; w.Write(d.RecipeId); WriteAccess(w, d.Access); w.Write(d.Berths); w.Write(d.SleepTicks); w.Write(d.AllocationTicks); w.Write(d.AllocationRequiresCredential); }
        private static ConstructionDefinition? ReadConstructionDefinition(BinaryReader r) => r.ReadBoolean() ? new ConstructionDefinition(r.ReadInt64(), ReadAccess(r), r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadBoolean()) : null;
        private void WriteConstruction(BinaryWriter w)
        {
            w.Write(constructionProjects.Count);
            foreach (var p in constructionProjects.Values)
            {
                w.Write(p.Id); w.Write(p.DefinitionId); WriteCoord(w, p.Site); w.Write(p.TileId); w.Write(p.AuthorityManaged); w.Write((int)p.Status); w.Write(p.ActiveActionId);
                w.Write(p.CompletedTick); w.Write(p.CompletedActionId); w.Write(p.CompletedActorId); w.Write(p.CompletionEventId);
                w.Write(p.BerthIds.Count); foreach (long id in p.BerthIds) w.Write(id);
            }
        }
        private void ReadConstruction(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var p = new ConstructionProjectSnapshot { Id = r.ReadInt64(), DefinitionId = r.ReadString(), Site = ReadCoord(r), TileId = r.ReadInt64(), AuthorityManaged = r.ReadBoolean(), Status = ReadEnum<ConstructionStatus>(r), ActiveActionId = r.ReadInt64(),
                    CompletedTick = r.ReadInt64(), CompletedActionId = r.ReadInt64(), CompletedActorId = r.ReadInt64(), CompletionEventId = r.ReadInt64() };
                int n = Count(r); var berths = new long[n]; for (int j = 0; j < n; j++) { berths[j] = r.ReadInt64(); if (!constructedBerths.Add(berths[j])) throw new ArgumentException("Duplicate constructed berth."); }
                p.BerthIds = Array.AsReadOnly(berths); constructionProjects.Add(p.Id, p); constructionSites.Add(p.Site, p.Id);
            }
        }
        private void ValidateConstructionState()
        {
            if (constructionProjects.Values.Select(p => p.Site).Distinct().Count() != constructionProjects.Count || constructionProjects.Values.Select(p => p.TileId).Distinct().Count() != constructionProjects.Count)
                throw new ArgumentException("Duplicate construction site or output tile.");
            var completed = constructionProjects.Values.Where(p => p.Status == ConstructionStatus.Completed).ToArray();
            if (completed.Select(p => p.CompletedActionId).Distinct().Count() != completed.Length || completed.Select(p => p.CompletionEventId).Distinct().Count() != completed.Length || Topology.Version < completed.Length)
                throw new ArgumentException("Construction completions must have distinct actions/events and topology changes.");
            foreach (var p in constructionProjects.Values)
            {
                if (p.Id < 1 || p.TileId < 1 || !Topology.Definitions.TryGetValue(p.DefinitionId, out var tileDefinition) || tileDefinition.Construction == null) throw new ArgumentException("Invalid construction identity or definition.");
                var definition = tileDefinition.Construction; var recipe = recipes[definition.RecipeId];
                if (p.Status == ConstructionStatus.Completed)
                {
                    if (p.ActiveActionId != 0 || p.CompletedTick < 0 || p.CompletedTick > LastProcessedTick || p.CompletedActionId < 1 || p.CompletedActionId >= nextAction || p.CompletionEventId < 1 || p.CompletionEventId >= nextTrace ||
                        !actors.ContainsKey(new Id<ActorTag>(p.CompletedActorId)) || !Topology.ById.TryGetValue(new Id<TileTag>(p.TileId), out var tile) || !tile.Completed || tile.Position != p.Site || tile.DefinitionId != p.DefinitionId || p.BerthIds.Count != definition.Berths)
                        throw new ArgumentException("Completed construction lacks its physical tile, timing or output capacity.");
                    foreach (long id in p.BerthIds)
                        if (!needServices.TryGetValue(id, out var bed) || bed.Definition.Kind != NeedServiceKind.Bed || bed.Definition.Position != p.Site || bed.Definition.DetentionFacilityId != 0 || bed.Definition.DurationTicks != definition.SleepTicks ||
                            bed.Definition.AllocationTicks != definition.AllocationTicks || bed.Definition.AllocationRequiresCredential != definition.AllocationRequiresCredential) throw new ArgumentException("Construction output is not the declared ordinary berth.");
                    var transactions = resourceTransactions.Where(t => t.ActionId == p.CompletedActionId).ToArray();
                    if (transactions.Length != recipe.Inputs.Count || transactions.Any(t => t.RecipeId != recipe.Id || t.Tick != p.CompletedTick || t.ActorId != p.CompletedActorId)) throw new ArgumentException("Construction completion lacks conserved recipe inputs.");
                }
                else
                {
                    if (((!p.AuthorityManaged || p.Status == ConstructionStatus.Building) && (Topology.Tiles.ContainsKey(p.Site) || !p.Site.Neighbors().Any(Topology.Contains))) || Topology.ById.ContainsKey(new Id<TileTag>(p.TileId)) || p.CompletedTick != -1 || p.CompletedActionId != 0 || p.CompletedActorId != 0 || p.CompletionEventId != 0 || p.BerthIds.Count != 0)
                        throw new ArgumentException("Unbuilt construction has physical output or invalid site.");
                    var actor = actors.Values.SingleOrDefault(a => a.Action?.Id.Value == p.ActiveActionId);
                    if (p.Status == ConstructionStatus.Reserved ? p.ActiveActionId != 0 : actor?.Action?.Kind != ActionKind.Construct || actor.Action.ServiceId != p.Id)
                        throw new ArgumentException("Construction state disagrees with active work.");
                }
            }
            foreach (var actor in actors.Values.Where(a => a.Action?.Kind == ActionKind.Construct))
            {
                var a = actor.Action!;
                if (!constructionProjects.TryGetValue(a.ServiceId, out var project) || project.Status != ConstructionStatus.Building || project.ActiveActionId != a.Id.Value || a.Materials.Select(b => b.Item).Distinct().Count() != a.Materials.Count ||
                    a.Materials.Any(b => b.Quantity < 0 || !resourceClaims.Any(c => c.Kind == ClaimKind.Item && c.ResourceId == b.Item.Value && c.ActionId == a.Id.Value)) ||
                    !resourceClaims.Any(c => c.Kind == ClaimKind.ConstructionSite && c.ResourceId == project.Id && c.ActionId == a.Id.Value) || a.RecipeId != Topology.Definitions[project.DefinitionId].Construction!.RecipeId || a.Target != actor.Position || actor.Position.Manhattan(project.Site) != 1 ||
                    a.Completes != checked(a.Started + recipes[a.RecipeId].Duration)) throw new ArgumentException("Invalid construction action, materials or claims.");
                var recipe = recipes[a.RecipeId]; var access = Topology.Definitions[project.DefinitionId].Construction!.Access;
                if (recipe.Inputs.Any(input => a.Materials.Where(b => b.Quantity > 0 && items[b.Item].Resource == input.Resource).Sum(b => b.Quantity) != input.Quantity) ||
                    a.Materials.Any(b => b.Quantity > 0 && (!recipe.Inputs.Any(input => input.Resource == items[b.Item].Resource) || credentials.Values.Any(c => c.Definition.Card == b.Item)) || b.Quantity == 0 && items[b.Item].Resource != access.ToolResource) ||
                    a.Materials.Count(b => b.Quantity == 0) != (access.ToolResource.Length > 0 ? 1 : 0)) throw new ArgumentException("Construction bindings disagree with the declared recipe/tool.");
                var staff = Topology.Definitions[project.DefinitionId].Construction!.Access.Staff;
                if (staff.Value > 0 && !resourceClaims.Any(c => c.Kind == ClaimKind.Staff && c.ResourceId == staff.Value && c.ActionId == a.Id.Value)) throw new ArgumentException("Construction lacks its staff claim.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == a.Id.Value).ToArray();
                if (timers.Length != 1 || timers[0].Tick != a.Completes || timers[0].Phase != EventPhase.PhysicalCompletion || timers[0].Intent.Actor != actor.Id) throw new ArgumentException("Invalid construction completion timer.");
            }
            foreach (var claim in resourceClaims.Where(c => c.Kind == ClaimKind.ConstructionSite))
                if (!constructionProjects.TryGetValue(claim.ResourceId, out var p) || p.Status != ConstructionStatus.Building || p.ActiveActionId != claim.ActionId) throw new ArgumentException("Unowned construction site claim.");
        }
    }
}
