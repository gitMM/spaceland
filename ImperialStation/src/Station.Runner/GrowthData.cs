using System.Text.Json.Serialization;
using Station.Core;

namespace Station.Runner;

public sealed class GrowthData
{
    [JsonRequired] public long BelievedCompletedBerths { get; set; }
    [JsonRequired] public CoordinateData Minimum { get; set; } = new();
    [JsonRequired] public CoordinateData Maximum { get; set; } = new();
    [JsonRequired] public List<MaterialData> KnownMaterials { get; set; } = new();
    public long ReservePermille { get; set; } = 50;
    public int Candidates { get; set; } = 3;
    public GrowthSeed ToSeed() => new(BelievedCompletedBerths, Minimum.ToCoord(), Maximum.ToCoord(), KnownMaterials.Select(m => m.ToAmount()), ReservePermille, Candidates);
}
