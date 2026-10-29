namespace Station.Core
{
    public sealed class MachineWearSnapshot
    {
        public long MachineId { get; internal set; }
        public long FaultEvents { get; internal set; }
        public long ConsumedParts { get; internal set; }
        public long LastFaultTick { get; internal set; } = -1;
        public long LastConsumedItemId { get; internal set; }
        public long NextWearTick { get; internal set; } = -1;
        internal MachineWearSnapshot Copy() => (MachineWearSnapshot)MemberwiseClone();
    }
    public sealed class FaultSinkSnapshot
    {
        public string Resource { get; }
        public long ConsumedUnits { get; }
        internal FaultSinkSnapshot(string resource, long units) { Resource = resource; ConsumedUnits = units; }
    }
}
