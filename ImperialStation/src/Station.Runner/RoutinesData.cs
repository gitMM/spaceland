using System.Text.Json.Serialization;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public sealed class RoutineConfigurationData
{
    public long DayTicks { get; set; } = 864000;
    public int Cohorts { get; set; } = 3;
    public long DutyTicks { get; set; } = 288000;
    public int CheckInWindow { get; set; } = 18000;
    public long PeriodicCheckInTicks { get; set; } = 36000;
    public RoutineConfig ToConfig() => new(DayTicks, Cohorts, DutyTicks, CheckInWindow, PeriodicCheckInTicks);
}
public sealed class RosterAssignmentData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public int Cohort { get; set; }
    public CoordinateData? RestTile { get; set; }
    public bool InitiallyKnown { get; set; } = true;
    public RosterAssignmentSeed ToSeed() => new(new Id<ActorTag>(ActorId), Cohort, RestTile?.ToCoord(), InitiallyKnown);
}
public sealed class RoutineData
{
    public RoutineConfigurationData Configuration { get; set; } = new();
    public List<RosterAssignmentData> Assignments { get; set; } = new();
    public RoutineSeed ToSeed() => new(Configuration.ToConfig(), Assignments.Select(a => a.ToSeed()));
}
