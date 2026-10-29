using System.Text.Json.Serialization;
using Station.Core;

namespace Station.Runner;

public sealed class DirtData
{
    [JsonRequired] public long TileId { get; set; }
    [JsonRequired] public long Units { get; set; }
}
public sealed class CleanlinessData
{
    public long CleanUnitTicks { get; set; } = 300;
    public int MeanSoilingTicks { get; set; } = 288000;
    public long SpillUnits { get; set; } = 4;
    public WorkAccessData Access { get; set; } = new() { Roles = new() { "cleaner" } };
    public List<DirtData> Dirt { get; set; } = new();
    public CleanlinessSeed ToSeed() => new(Dirt.Select(d => new DirtSeed(new(d.TileId), d.Units)), CleanUnitTicks, MeanSoilingTicks, SpillUnits, Access.ToAccess());
}
