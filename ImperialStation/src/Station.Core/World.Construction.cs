using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<long, ConstructionProjectSnapshot> constructionProjects = new SortedDictionary<long, ConstructionProjectSnapshot>();
        private readonly SortedDictionary<Coord, long> constructionSites = new SortedDictionary<Coord, long>();
        private readonly SortedSet<long> constructedBerths = new SortedSet<long>();
        public IReadOnlyList<ConstructionProjectSnapshot> InspectConstructionProjects() => constructionProjects.Values.Select(p => p.Copy()).ToArray();
        private void ValidateConstructionDefinitions()
        {
            foreach (var definition in Topology.Definitions.Values.Where(d => d.Construction != null))
            {
                var c = definition.Construction!;
                if (!WorkEnabled || !recipes.TryGetValue(c.RecipeId, out var recipe) || recipe.Inputs.Count == 0 || recipe.Outputs.Count != 0 ||
                    c.Access.ToolResource.Length > 0 && !resources.ContainsKey(c.Access.ToolResource) || c.Access.Staff.Value > 0 && !actors.ContainsKey(c.Access.Staff))
                    throw new ArgumentException("Construction requires a declared material-consuming recipe, no item outputs, and valid access dependencies.");
            }
        }
        private void InitializeConstruction(ConstructionSeed? seed)
        {
            if (seed == null) return;
            foreach (var p in seed.Projects)
            {
                if (!Topology.Definitions.TryGetValue(p.DefinitionId, out var definition) || definition.Construction == null || Topology.Tiles.ContainsKey(p.Site) ||
                    Topology.ById.ContainsKey(new Id<TileTag>(p.TileId)) || !p.Site.Neighbors().Any(Topology.Contains) || constructionProjects.Values.Any(c => c.Site == p.Site || c.TileId == p.TileId))
                    throw new ArgumentException("Construction reservation requires a unique empty adjacent site, unused tile ID and buildable definition.");
                constructionProjects.Add(p.Id, new ConstructionProjectSnapshot { Id = p.Id, DefinitionId = p.DefinitionId, Site = p.Site, TileId = p.TileId });
                constructionSites.Add(p.Site, p.Id);
            }
        }
        private FailureReason ValidateConstruction(Actor actor, ActiveAction action, bool bind)
        {
            if (action.AttemptId > 0 && (!localOrders.TryGetValue(action.AttemptId, out var order) || order.Kind != WorkKind.ConstructHabitat || order.ProjectId != action.ServiceId)) return FailureReason.UnknownRequiredFact; if (!constructionProjects.TryGetValue(action.ServiceId, out var project)) return FailureReason.RequiredServiceNotPresent;
            if (project.Status == ConstructionStatus.Completed || Topology.Tiles.ContainsKey(project.Site) || !Topology.Contains(actor.Position) || actor.Position.Manhattan(project.Site) != 1)
                return FailureReason.InvalidConstructionSite;
            if (project.ActiveActionId != 0 && project.ActiveActionId != action.Id.Value || ClaimedByOther(ClaimKind.ConstructionSite, project.Id, action.Id.Value)) return FailureReason.ServiceBusy;
            var definition = Topology.Definitions[project.DefinitionId].Construction!;
            if (action.RecipeId != 0 && action.RecipeId != definition.RecipeId) return FailureReason.InvalidConstructionSite;
            if (bind) action.RecipeId = definition.RecipeId;
            else if (action.RecipeId != definition.RecipeId) return FailureReason.InvalidConstructionSite;
            var failure = CheckWorkAccess(actor, action, actor.Position, definition.Access, bind); if (failure != FailureReason.None) return failure;
            var recipe = recipes[definition.RecipeId];
            if (bind)
            {
                foreach (var input in recipe.Inputs)
                {
                    long remaining = input.Quantity; bool claimed = false;
                    foreach (var item in items.Values.Where(i => i.Resource == input.Resource && HeldBy(i, actor)))
                    {
                        if (credentials.Values.Any(c => c.Definition.Card == item.Id) || action.Materials.Any(b => b.Item == item.Id)) continue;
                        if (ClaimedByOther(ClaimKind.Item, item.Id.Value, action.Id.Value)) { claimed = true; continue; }
                        long amount = Math.Min(remaining, item.Quantity); action.Materials.Add(new MaterialBinding { Item = item.Id, Quantity = amount }); remaining -= amount;
                        if (remaining == 0) break;
                    }
                    if (remaining > 0) return claimed ? FailureReason.ResourceClaimed : FailureReason.InsufficientMaterials;
                }
            }
            else if (project.Status != ConstructionStatus.Building || project.ActiveActionId != action.Id.Value ||
                recipe.Inputs.Any(input => action.Materials.Where(b => b.Quantity > 0 && items.TryGetValue(b.Item, out var item) && item.Resource == input.Resource).Sum(b => b.Quantity) != input.Quantity))
                return FailureReason.InsufficientMaterials;
            foreach (var b in action.Materials)
                if (!items.TryGetValue(b.Item, out var item) || !HeldBy(item, actor) || item.Quantity < b.Quantity || ClaimedByOther(ClaimKind.Item, b.Item.Value, action.Id.Value)) return FailureReason.ResourceClaimed;
            return FailureReason.None;
        }
        private void StartConstruction(Actor actor, ActiveAction action)
        {
            var failure = ValidateConstruction(actor, action, true); if (failure != FailureReason.None) { ConstructionWorkFailed(action, failure); Fail(actor, action.Id, failure); return; }
            var project = constructionProjects[action.ServiceId]; var definition = Topology.Definitions[project.DefinitionId].Construction!;
            long finish = checked(processingTick + recipes[definition.RecipeId].Duration);
            AddClaim(ClaimKind.ConstructionSite, project.Id, actor, action);
            foreach (var b in action.Materials) AddClaim(ClaimKind.Item, b.Item.Value, actor, action);
            if (definition.Access.Staff.Value > 0) AddClaim(ClaimKind.Staff, definition.Access.Staff.Value, actor, action);
            project.Status = ConstructionStatus.Building; project.ActiveActionId = action.Id.Value;
            action.Completes = finish; actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, target: project.Site, arrival: finish, detail: "Construct", causeId: action.CauseId);
            ScheduleCompletion(actor, finish);
        }
        private void ConstructionInterrupted(ActiveAction action)
        {
            if (action.Kind != ActionKind.Construct || !constructionProjects.TryGetValue(action.ServiceId, out var p) || p.ActiveActionId != action.Id.Value) return;
            p.Status = ConstructionStatus.Reserved; p.ActiveActionId = 0;
        }
        private void CompleteConstruction(Actor actor, ActiveAction action)
        {
            var failure = ValidateConstruction(actor, action, false);
            if (failure != FailureReason.None) { ConstructionInterrupted(action); ConstructionWorkFailed(action, failure); ReleaseResourceClaims(action.Id.Value); actor.Action = null; Fail(actor, action.Id, failure); return; }
            var project = constructionProjects[action.ServiceId]; var definition = Topology.Definitions[project.DefinitionId].Construction!; var recipe = recipes[definition.RecipeId];
            ValidateRecipeCommit(recipe); _ = checked(Topology.Version + 1);
            long firstBerth = needServices.Count == 0 ? 1 : checked(needServices.Keys.Last() + 1); _ = checked(firstBerth + definition.Berths);
            if (Topology.ById.ContainsKey(new Id<TileTag>(project.TileId))) throw new InvalidOperationException("Reserved construction tile ID is already used.");
            var beds = Enumerable.Range(0, definition.Berths).Select(i => new NeedServiceDefinition(firstBerth + i, NeedServiceKind.Bed, project.Site,
                durationTicks: definition.SleepTicks, allocationTicks: definition.AllocationTicks, allocationRequiresCredential: definition.AllocationRequiresCredential)).ToArray();
            foreach (var binding in action.Materials.Where(b => b.Quantity > 0))
            {
                var item = items[binding.Item]; item.Quantity -= binding.Quantity;
                Emit("ItemConsumed", actor, action.Id, item.Id, detail: item.Resource + ":" + binding.Quantity, causeId: action.CauseId);
                if (item.Quantity == 0) RemoveItem(item.Id); ObserveItemDescription(actor, item, action.CauseId);
            }
            foreach (var input in recipe.Inputs) RecordRecipeDelta(actor, action, input.Resource, -input.Quantity);
            Topology.AddCompleted(new Tile(new Id<TileTag>(project.TileId), project.DefinitionId, project.Site));
            InitializeNeedServices(beds); foreach (var bed in beds) constructedBerths.Add(bed.Id);
            project.Status = ConstructionStatus.Completed; project.ActiveActionId = 0; project.CompletedTick = processingTick;
            project.CompletedActionId = action.Id.Value; project.CompletedActorId = actor.Id.Value; project.BerthIds = Array.AsReadOnly(beds.Select(b => b.Id).ToArray());
            project.CompletionEventId = Emit("ConstructionCompleted", actor, action.Id, target: project.Site, detail: "project=" + project.Id + "; tile=" + project.TileId + "; berths=" + definition.Berths, causeId: action.CauseId);
            ObserveConstructedTile(actor, project, project.CompletionEventId);
            Finish(actor); AssertAccounting();
        }
        private void ObserveConstructedTile(Actor actor, ConstructionProjectSnapshot project, long cause)
        {
            if (Information == null) return;
            Learn(actor, new FactKey(SubjectKind.Tile, project.TileId, AttributeKind.Location), FactValue.At(project.Site), EvidenceKind.Direct, project.TileId, processingTick, cause);
            Learn(actor, new FactKey(SubjectKind.Tile, project.TileId, AttributeKind.TraversalTicks), FactValue.Integer(Topology.TraversalTicks(project.Site)), EvidenceKind.Direct, project.TileId, processingTick, cause);
            foreach (long bed in project.BerthIds) Learn(actor, new FactKey(SubjectKind.Service, bed, AttributeKind.Location), FactValue.At(project.Site), EvidenceKind.Direct, project.TileId, processingTick, cause);
        }
        private void ObserveConstructedServices(Actor actor, long cause)
        {
            if (constructionSites.TryGetValue(actor.Position, out long id) && constructionProjects[id].Status == ConstructionStatus.Completed) ObserveConstructedTile(actor, constructionProjects[id], cause);
        }
        private IReadOnlyList<NeedServiceDefinition>? publishedNeedServices;
        private IReadOnlyList<NeedServiceDefinition> KnownNeedServices(Actor actor)
        {
            // Seeded definitions are immutable public data shared by every view.
            // Constructed berths still require each actor's own observed location.
            if (constructedBerths.Count == 0)
            {
                if (publishedNeedServices == null || publishedNeedServices.Count != needServices.Count)
                    publishedNeedServices = Array.AsReadOnly(needServices.Values.Select(s => s.Definition).ToArray());
                return publishedNeedServices;
            }
            return Array.AsReadOnly(needServices.Values.Where(s => !constructedBerths.Contains(s.Definition.Id) ||
                actor.Knowledge.Entries.TryGetValue(new FactKey(SubjectKind.Service, s.Definition.Id, AttributeKind.Location), out var fact) && fact.Fact.Value.Equals(FactValue.At(s.Definition.Position))).Select(s => s.Definition).ToArray());
        }
    }
}
