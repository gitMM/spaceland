using System.Text.Json.Serialization;
using Station.Core;
using Station.Contracts;

namespace Station.Runner;

public sealed class ConstructionDefinitionData
{
    [JsonRequired] public long RecipeId { get; set; }
    [JsonRequired] public WorkAccessData Access { get; set; } = new();
    public int Berths { get; set; }
    public long SleepTicks { get; set; } = 288000;
    public long AllocationTicks { get; set; } = 100;
    public bool AllocationRequiresCredential { get; set; } = true;
    public ConstructionDefinition ToDefinition() => new(RecipeId, Access.ToAccess(), Berths, SleepTicks, AllocationTicks, AllocationRequiresCredential);
}
public sealed class ConstructionProjectData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public string DefinitionId { get; set; } = "";
    [JsonRequired] public CoordinateData Site { get; set; } = new();
    [JsonRequired] public long TileId { get; set; }
}
public sealed class ConstructionData
{
    [JsonRequired] public List<ConstructionProjectData> Projects { get; set; } = new();
    public ConstructionSeed ToSeed() => new(Projects.Select(p => new ConstructionProjectSeed(p.Id, p.DefinitionId, p.Site.ToCoord(), p.TileId)));
}
public sealed class ConstructionOrderData
{
    [JsonRequired] public long ProjectId { get; set; }
    [JsonRequired] public long TileId { get; set; }
    [JsonRequired] public string DefinitionId { get; set; } = "";
    [JsonRequired] public CoordinateData Site { get; set; } = new();
    [JsonRequired] public RecipeData Recipe { get; set; } = new();
    [JsonRequired] public List<string> Roles { get; set; } = new();
    public string ToolResource { get; set; } = "";
    public bool RequiresCredential { get; set; }
    [JsonRequired] public long Berths { get; set; }
    public ConstructionOrder ToOrder() => new(ProjectId, TileId, DefinitionId, Site.ToCoord(), Recipe.ToDefinition(), Roles, ToolResource, RequiresCredential, Berths);
}
