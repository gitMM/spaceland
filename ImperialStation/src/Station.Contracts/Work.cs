using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum WorkKind { RepairMachine, CleanTile, VerifyMachine, ConstructHabitat }
    public sealed class PublicCleanable
    {
        public Id<TileTag> Tile { get; }
        public Coord Position { get; }
        public long UnitTicks { get; }
        public string ToolResource { get; }
        public IReadOnlyList<string> Roles { get; }
        public PublicCleanable(Id<TileTag> tile, Coord position, long unitTicks, string toolResource, IEnumerable<string> roles)
        {
            if (tile.Value == 0 || unitTicks < 1 || toolResource == null) throw new ArgumentException("Invalid public cleanup definition.");
            Tile = tile; Position = position; UnitTicks = unitTicks; ToolResource = toolResource;
            Roles = Array.AsReadOnly(roles.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            if (Roles.Count == 0 || Roles.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Cleanup requires published roles.");
        }
    }
    public sealed class PublicWorkstation
    {
        public long Id { get; }
        public Coord Position { get; }
        public string Capability { get; }
        public string ToolResource { get; }
        public IReadOnlyList<string> Roles { get; }
        public bool RequiresCredential { get; }
        public PublicWorkstation(long id, Coord position, string capability, string toolResource, IEnumerable<string> roles, bool requiresCredential = false)
        { Id = id; Position = position; Capability = capability; ToolResource = toolResource; Roles = Array.AsReadOnly(roles.OrderBy(x => x, StringComparer.Ordinal).ToArray()); RequiresCredential = requiresCredential; }
    }
    public sealed class PublicEquipment
    {
        public long Id { get; }
        public Coord Position { get; }
        public string FitClass { get; }
        public string ToolResource { get; }
        public IReadOnlyList<string> Roles { get; }
        public long InstallTicks { get; }
        public long InspectTicks { get; }
        public bool RequiresCredential { get; }
        public PublicEquipment(long id, Coord position, string fitClass, string toolResource, IEnumerable<string> roles, long installTicks, long inspectTicks, bool requiresCredential = false)
        { Id = id; Position = position; FitClass = fitClass; ToolResource = toolResource; Roles = Array.AsReadOnly(roles.OrderBy(x => x, StringComparer.Ordinal).ToArray()); InstallTicks = installTicks; InspectTicks = inspectTicks; RequiresCredential = requiresCredential; }
    }
    public sealed class PublicWorkCatalog
    {
        public IReadOnlyList<ResourceDefinition> Resources { get; }
        public IReadOnlyList<RecipeDefinition> Recipes { get; }
        public IReadOnlyList<PublicWorkstation> Workstations { get; }
        public IReadOnlyList<PublicEquipment> Equipment { get; }
        public long PickupTicks { get; }
        public IReadOnlyList<PublicCleanable> Cleanable { get; }
        public PublicWorkCatalog(IEnumerable<ResourceDefinition> resources, IEnumerable<RecipeDefinition> recipes,
            IEnumerable<PublicWorkstation> workstations, IEnumerable<PublicEquipment> equipment, long pickupTicks, IEnumerable<PublicCleanable>? cleanable = null)
        {
            if (pickupTicks < 1) throw new ArgumentException("Invalid published pickup duration.");
            Resources = Array.AsReadOnly(resources.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
            Recipes = Array.AsReadOnly(recipes.OrderBy(x => x.Id).ToArray());
            Workstations = Array.AsReadOnly(workstations.OrderBy(x => x.Id).ToArray());
            Equipment = Array.AsReadOnly(equipment.OrderBy(x => x.Id).ToArray()); PickupTicks = pickupTicks;
            Cleanable = Array.AsReadOnly((cleanable ?? Array.Empty<PublicCleanable>()).OrderBy(x => x.Tile).ToArray());
            if (Cleanable.Select(x => x.Tile).Distinct().Count() != Cleanable.Count) throw new ArgumentException("Duplicate public cleanup tile.");
        }
    }
    public sealed class ResourceDefinition
    {
        public string Id { get; }
        public string FitClass { get; }
        public int Rating { get; }
        public ResourceDefinition(string id, string fitClass = "", int rating = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || fitClass == null || rating < 0) throw new ArgumentException("Invalid resource definition.");
            Id = id; FitClass = fitClass; Rating = rating;
        }
    }
    public sealed class MaterialAmount
    {
        public string Resource { get; }
        public long Quantity { get; }
        public MaterialAmount(string resource, long quantity)
        {
            if (string.IsNullOrWhiteSpace(resource) || quantity < 1) throw new ArgumentException("Invalid recipe material.");
            Resource = resource; Quantity = quantity;
        }
    }
    public sealed class RecipeDefinition
    {
        public long Id { get; }
        public string Capability { get; }
        public long Duration { get; }
        public IReadOnlyList<MaterialAmount> Inputs { get; }
        public IReadOnlyList<MaterialAmount> Outputs { get; }
        public RecipeDefinition(long id, string capability, IEnumerable<MaterialAmount> inputs, IEnumerable<MaterialAmount> outputs, long duration = 1200)
        {
            if (id < 1 || string.IsNullOrWhiteSpace(capability) || duration < 1) throw new ArgumentException("Invalid recipe definition.");
            Id = id; Capability = capability; Duration = duration;
            Inputs = Array.AsReadOnly(inputs.OrderBy(x => x.Resource, StringComparer.Ordinal).ToArray());
            Outputs = Array.AsReadOnly(outputs.OrderBy(x => x.Resource, StringComparer.Ordinal).ToArray());
            if (Inputs.Count + Outputs.Count == 0 || Inputs.Select(x => x.Resource).Distinct().Count() != Inputs.Count || Outputs.Select(x => x.Resource).Distinct().Count() != Outputs.Count)
                throw new ArgumentException("Recipe requires a declared source, sink or transformation with unique positive material entries.");
        }
    }
}
