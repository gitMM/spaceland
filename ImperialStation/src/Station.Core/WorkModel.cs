using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class WorkAccess
    {
        public IReadOnlyList<string> Roles { get; }
        public string ToolResource { get; }
        public Id<ActorTag> Staff { get; }
        public bool RequiresCredential { get; }
        public WorkAccess(IEnumerable<string> roles, string toolResource = "", Id<ActorTag> staff = default, bool requiresCredential = false)
        {
            Roles = Array.AsReadOnly(roles.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
            if (Roles.Count == 0 || Roles.Any(string.IsNullOrWhiteSpace) || toolResource == null) throw new ArgumentException("Work access requires roles.");
            ToolResource = toolResource; Staff = staff; RequiresCredential = requiresCredential;
        }
    }
    public sealed class WorkstationDefinition
    {
        public long Id { get; }
        public Coord Position { get; }
        public string Capability { get; }
        public int Capacity { get; }
        public WorkAccess Access { get; }
        public WorkstationDefinition(long id, Coord position, string capability, WorkAccess access, int capacity = 1)
        {
            if (id < 1 || string.IsNullOrWhiteSpace(capability) || capacity < 1) throw new ArgumentException("Invalid workstation.");
            Id = id; Position = position; Capability = capability; Access = access; Capacity = capacity;
        }
    }
    public sealed class MachineDefinition
    {
        public long Id { get; }
        public Coord Position { get; }
        public string FitClass { get; }
        public string RequiredResource { get; }
        public int RequiredRating { get; }
        public WorkAccess Access { get; }
        public long InstallTicks { get; }
        public long InspectTicks { get; }
        public long TestTicks { get; }
        public MachineDefinition(long id, Coord position, string fitClass, string requiredResource, WorkAccess access,
            int requiredRating = 0, long installTicks = 300, long inspectTicks = 100, long testTicks = 100)
        {
            if (id < 1 || string.IsNullOrWhiteSpace(fitClass) || string.IsNullOrWhiteSpace(requiredResource) || requiredRating < 0 || installTicks < 1 || inspectTicks < 1 || testTicks < 1)
                throw new ArgumentException("Invalid machine definition.");
            Id = id; Position = position; FitClass = fitClass; RequiredResource = requiredResource; Access = access;
            RequiredRating = requiredRating; InstallTicks = installTicks; InspectTicks = inspectTicks; TestTicks = testTicks;
        }
    }
    public sealed class WorkSeed
    {
        public IReadOnlyList<ResourceDefinition> Resources { get; }
        public IReadOnlyList<RecipeDefinition> Recipes { get; }
        public IReadOnlyList<WorkstationDefinition> Workstations { get; }
        public IReadOnlyList<MachineDefinition> Machines { get; }
        public IReadOnlyList<ProjectStashDefinition> Stashes { get; }
        public WorkSeed(IEnumerable<ResourceDefinition> resources, IEnumerable<RecipeDefinition> recipes,
            IEnumerable<WorkstationDefinition> workstations, IEnumerable<MachineDefinition> machines, IEnumerable<ProjectStashDefinition>? stashes = null)
        {
            Resources = Array.AsReadOnly(resources.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
            Recipes = Array.AsReadOnly(recipes.OrderBy(x => x.Id).ToArray());
            Workstations = Array.AsReadOnly(workstations.OrderBy(x => x.Id).ToArray());
            Machines = Array.AsReadOnly(machines.OrderBy(x => x.Id).ToArray());
            Stashes = Array.AsReadOnly((stashes ?? Array.Empty<ProjectStashDefinition>()).OrderBy(x => x.Id).ToArray());
        }
    }
    public enum ClaimKind { Item, Workstation, Machine, Staff, ConstructionSite }
    public sealed class ResourceClaim
    {
        public ClaimKind Kind { get; internal set; }
        public long ResourceId { get; internal set; }
        public long ActorId { get; internal set; }
        public long ActionId { get; internal set; }
        public long RequestedTick { get; internal set; }
        internal ResourceClaim Copy() => (ResourceClaim)MemberwiseClone();
    }
    internal sealed class MaterialBinding
    {
        internal Id<ItemTag> Item;
        internal long Quantity; // Zero reserves a tool without consuming it.
    }
    public sealed class ResourceTransaction
    {
        public long Id { get; internal set; }
        public long Tick { get; internal set; }
        public long ActorId { get; internal set; }
        public long ActionId { get; internal set; }
        public long RecipeId { get; internal set; }
        public string Resource { get; internal set; } = "";
        public long Delta { get; internal set; }
        internal ResourceTransaction Copy() => (ResourceTransaction)MemberwiseClone();
    }
    public sealed class MachineSnapshot
    {
        public long Id { get; internal set; }
        public Coord Position { get; internal set; }
        public long InstalledItemId { get; internal set; }
        public string RequiredResource { get; internal set; } = "";
        public bool Operational { get; internal set; }
    }
}
