using System.Text.Json.Serialization;
using Station.Core;
using Station.Contracts;

namespace Station.Runner;

public sealed class JobData
{
    [JsonRequired] public long Id { get; set; }
    public long MachineId { get; set; }
    public WorkKind Kind { get; set; }
    public long TileId { get; set; }
    public long ReportedUnits { get; set; } = 1;
    public JobSeed ToSeed()
    {
        if ((Kind is WorkKind.RepairMachine or WorkKind.VerifyMachine) && TileId == 0) return new(Id, MachineId, Kind);
        if (Kind == WorkKind.CleanTile && MachineId == 0) return new(Id, new Id<TileTag>(TileId), ReportedUnits);
        throw new ArgumentException("Job kind and target fields disagree.");
    }
}
public sealed class AssignmentData
{
    [JsonRequired] public string Key { get; set; } = "";
    [JsonRequired] public long JobId { get; set; }
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public long Expires { get; set; }
}
public sealed class CheckInData
{
    [JsonRequired] public long ActorId { get; set; }
    public long FirstTick { get; set; }
    public long Interval { get; set; } = 36000;
}
public sealed class JobSystemData
{
    public List<JobData> Jobs { get; set; } = new();
    public List<AssignmentData> Assignments { get; set; } = new();
    public List<CheckInData> CheckIns { get; set; } = new();
    public JobSystemSeed ToSeed() => new(Jobs.Select(j => j.ToSeed()), Assignments.Select(a => new AssignmentSeed(a.Key, a.JobId, new(a.ActorId), a.Expires)), CheckIns.Select(c => new CheckInSeed(new(c.ActorId), c.FirstTick, c.Interval)));
}
