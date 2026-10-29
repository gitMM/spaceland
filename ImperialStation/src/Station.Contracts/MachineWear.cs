using System;

namespace Station.Contracts
{
    public sealed class MachineWearConfig
    {
        public int MeanIntervalTicks { get; }
        public MachineWearConfig(int meanIntervalTicks = 2592000)
        {
            if (meanIntervalTicks != 0 && (meanIntervalTicks < 2 || meanIntervalTicks > 1000000000))
                throw new ArgumentException("Wear mean must be zero (disabled) or 2..1000000000 ticks.");
            MeanIntervalTicks = meanIntervalTicks;
        }
    }
}
