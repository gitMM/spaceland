using System;

namespace Station.Contracts
{
    public sealed class QualityAssuranceConfig
    {
        public int SamplePermille { get; }
        public long DelayTicks { get; }
        public long OrderTicks { get; }
        public string SupervisorRole { get; }
        public QualityAssuranceConfig(int samplePermille = 250, long delayTicks = 18000, long orderTicks = 1728000, string supervisorRole = "supervisor")
        {
            if (samplePermille < 0 || samplePermille > 1000 || delayTicks < 0 || orderTicks < 1 || string.IsNullOrWhiteSpace(supervisorRole)) throw new ArgumentException("Invalid QA policy.");
            SamplePermille = samplePermille; DelayTicks = delayTicks; OrderTicks = orderTicks; SupervisorRole = supervisorRole;
        }
    }
}
