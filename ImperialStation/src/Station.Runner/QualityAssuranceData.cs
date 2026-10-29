using Station.Contracts;

namespace Station.Runner;

public sealed class QualityAssuranceData
{
    public int SamplePermille { get; set; } = 250;
    public long DelayTicks { get; set; } = 18000;
    public long OrderTicks { get; set; } = 1728000;
    public string SupervisorRole { get; set; } = "supervisor";
    public QualityAssuranceConfig ToConfig() => new(SamplePermille, DelayTicks, OrderTicks, SupervisorRole);
}
