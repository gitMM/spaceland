using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class ConstructionDefinition
    {
        public long RecipeId { get; }
        public WorkAccess Access { get; }
        public int Berths { get; }
        public long SleepTicks { get; }
        public long AllocationTicks { get; }
        public bool AllocationRequiresCredential { get; }
        public ConstructionDefinition(long recipeId, WorkAccess access, int berths = 0, long sleepTicks = 288000, long allocationTicks = 100, bool allocationRequiresCredential = true)
        {
            if (recipeId < 1 || access == null || berths < 0 || sleepTicks < 1 || allocationTicks < 1) throw new ArgumentException("Invalid construction recipe, capacity or service timing.");
            RecipeId = recipeId; Access = access; Berths = berths; SleepTicks = sleepTicks; AllocationTicks = allocationTicks; AllocationRequiresCredential = allocationRequiresCredential;
        }
    }
    public sealed class ConstructionProjectSeed
    {
        public long Id { get; }
        public string DefinitionId { get; }
        public Coord Site { get; }
        public long TileId { get; }
        public ConstructionProjectSeed(long id, string definitionId, Coord site, long tileId)
        { if (id < 1 || tileId < 1 || string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("Construction project requires positive IDs and a tile definition."); Id = id; DefinitionId = definitionId; Site = site; TileId = tileId; }
    }
    public sealed class ConstructionSeed
    {
        public IReadOnlyList<ConstructionProjectSeed> Projects { get; }
        public ConstructionSeed(IEnumerable<ConstructionProjectSeed> projects) => Projects = Array.AsReadOnly(projects.OrderBy(p => p.Id).ToArray());
    }
    public enum ConstructionStatus { Reserved, Building, Completed }
    public sealed class ConstructionProjectSnapshot
    {
        public long Id { get; internal set; }
        public string DefinitionId { get; internal set; } = "";
        public Coord Site { get; internal set; }
        public long TileId { get; internal set; }
        public ConstructionStatus Status { get; internal set; }
        public bool AuthorityManaged { get; internal set; }
        public long ActiveActionId { get; internal set; }
        public long CompletedTick { get; internal set; } = -1;
        public long CompletedActionId { get; internal set; }
        public long CompletedActorId { get; internal set; }
        public long CompletionEventId { get; internal set; }
        public IReadOnlyList<long> BerthIds { get; internal set; } = Array.Empty<long>();
        internal ConstructionProjectSnapshot Copy() { var copy = (ConstructionProjectSnapshot)MemberwiseClone(); copy.BerthIds = Array.AsReadOnly(BerthIds.ToArray()); return copy; }
    }
}
