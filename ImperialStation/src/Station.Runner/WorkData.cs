using System.Text.Json.Serialization;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public sealed class ResourceData
{
    [JsonRequired] public string Id { get; set; } = "";
    public string FitClass { get; set; } = "";
    public int Rating { get; set; }
    public ResourceDefinition ToDefinition() => new(Id, FitClass, Rating);
}
public sealed class MaterialData
{
    [JsonRequired] public string Resource { get; set; } = "";
    [JsonRequired] public long Quantity { get; set; }
    public MaterialAmount ToAmount() => new(Resource, Quantity);
}
public sealed class RecipeData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public string Capability { get; set; } = "";
    public long Duration { get; set; } = 1200;
    [JsonRequired] public List<MaterialData> Inputs { get; set; } = new();
    [JsonRequired] public List<MaterialData> Outputs { get; set; } = new();
    public RecipeDefinition ToDefinition() => new(Id, Capability, Inputs.Select(x => x.ToAmount()), Outputs.Select(x => x.ToAmount()), Duration);
}
public sealed class WorkAccessData
{
    [JsonRequired] public List<string> Roles { get; set; } = new();
    public string ToolResource { get; set; } = "";
    public long StaffId { get; set; }
    public bool RequiresCredential { get; set; }
    public WorkAccess ToAccess() => new(Roles, ToolResource, new(StaffId), RequiresCredential);
}
public sealed class WorkstationData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public CoordinateData Position { get; set; } = new();
    [JsonRequired] public string Capability { get; set; } = "";
    public int Capacity { get; set; } = 1;
    [JsonRequired] public WorkAccessData Access { get; set; } = new();
    public WorkstationDefinition ToDefinition() => new(Id, Position.ToCoord(), Capability, Access.ToAccess(), Capacity);
}
public sealed class MachineData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public CoordinateData Position { get; set; } = new();
    [JsonRequired] public string FitClass { get; set; } = "";
    [JsonRequired] public string RequiredResource { get; set; } = "";
    public int RequiredRating { get; set; }
    public long InstallTicks { get; set; } = 300;
    public long InspectTicks { get; set; } = 100;
    public long TestTicks { get; set; } = 100;
    [JsonRequired] public WorkAccessData Access { get; set; } = new();
    public MachineDefinition ToDefinition() => new(Id, Position.ToCoord(), FitClass, RequiredResource, Access.ToAccess(), RequiredRating, InstallTicks, InspectTicks, TestTicks);
}
public sealed class WorkData
{
    public List<ProjectStashData> Stashes { get; set; } = new();
    public List<WorkstationData> Workstations { get; set; } = new();
    public List<MachineData> Machines { get; set; } = new();
    public WorkSeed ToSeed(CatalogData catalog) => new(catalog.Resources.Select(x => x.ToDefinition()), catalog.Recipes.Select(x => x.ToDefinition()), Workstations.Select(x => x.ToDefinition()), Machines.Select(x => x.ToDefinition()), Stashes.Select(x => new ProjectStashDefinition(x.Id, x.ProjectId, x.Position.ToCoord())));
}
public sealed class ProjectStashData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public long ProjectId { get; set; }
    [JsonRequired] public CoordinateData Position { get; set; } = new();
}
