using Station.Contracts;

namespace Station.Runner;

public sealed class AuthorityData
{
    public long SlackMinimum { get; set; } = 1500;
    public long SlackMargin { get; set; } = 250;
    public long SlackMaximum { get; set; } = 4000;
    public long GraceTicks { get; set; } = 3000;
    public long EmaNumerator { get; set; } = 1;
    public long EmaDenominator { get; set; } = 8;
    public long UnacknowledgedTicks { get; set; } = 36000;
    public long IncidentAdmissionPermille { get; set; } = 50;
    public int MaximumReplacementDepth { get; set; } = 2;
    public long ResponseOrderTicks { get; set; } = 1728000;
    public string PatrolRole { get; set; } = "guard";
    public AuthorityConfig ToConfig() => new(SlackMinimum, SlackMargin, SlackMaximum, GraceTicks, EmaNumerator, EmaDenominator, UnacknowledgedTicks, IncidentAdmissionPermille, MaximumReplacementDepth, ResponseOrderTicks, PatrolRole);
}
