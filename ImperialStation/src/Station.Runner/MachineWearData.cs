using Station.Contracts;

namespace Station.Runner;

public sealed class MachineWearData
{
    public int MeanIntervalTicks { get; set; } = 2592000;
    public MachineWearConfig ToConfig() => new(MeanIntervalTicks);
}
