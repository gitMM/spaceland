using Station.Contracts;

namespace Station.Runner;

public sealed class FacilityData
{
    public List<string> ReportingRoles { get; set; } = new() { "cleaner", "supervisor", "guard", "patrol" };
    public long DirtThreshold { get; set; } = 2;
    public long RepeatTicks { get; set; } = 36000;
    public long OrderTicks { get; set; } = 1728000;
    public bool RepairPreemptsCleanup { get; set; }
    public FacilityConfig ToConfig() => new(ReportingRoles, DirtThreshold, RepeatTicks, OrderTicks, RepairPreemptsCleanup);
}
