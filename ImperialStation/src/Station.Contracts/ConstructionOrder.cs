using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public sealed class ConstructionOrder
    {
        public long ProjectId { get; }
        public long TileId { get; }
        public string DefinitionId { get; }
        public Coord Site { get; }
        public RecipeDefinition Recipe { get; }
        public IReadOnlyList<string> Roles { get; }
        public string ToolResource { get; }
        public bool RequiresCredential { get; }
        public long Berths { get; }
        public bool Same(ConstructionOrder other) => ProjectId == other.ProjectId && TileId == other.TileId && DefinitionId == other.DefinitionId && Site == other.Site &&
            Recipe.Id == other.Recipe.Id && Recipe.Capability == other.Recipe.Capability && Recipe.Duration == other.Recipe.Duration &&
            Recipe.Inputs.Count == other.Recipe.Inputs.Count && Recipe.Inputs.Zip(other.Recipe.Inputs, (a, b) => a.Resource == b.Resource && a.Quantity == b.Quantity).All(x => x) &&
            Roles.SequenceEqual(other.Roles) && ToolResource == other.ToolResource && RequiresCredential == other.RequiresCredential && Berths == other.Berths;
        public ConstructionOrder(long projectId, long tileId, string definitionId, Coord site, RecipeDefinition recipe, IEnumerable<string> roles, string toolResource, bool requiresCredential, long berths)
        {
            var allowed = roles.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (projectId < 1 || tileId < 1 || string.IsNullOrWhiteSpace(definitionId) || recipe.Inputs.Count == 0 || recipe.Outputs.Count != 0 ||
                allowed.Length == 0 || allowed.Any(string.IsNullOrWhiteSpace) || toolResource == null || berths < 1) throw new ArgumentException("Invalid construction order specification.");
            ProjectId = projectId; TileId = tileId; DefinitionId = definitionId; Site = site; Recipe = recipe; Roles = Array.AsReadOnly(allowed); ToolResource = toolResource; RequiresCredential = requiresCredential; Berths = berths;
        }
    }
}
