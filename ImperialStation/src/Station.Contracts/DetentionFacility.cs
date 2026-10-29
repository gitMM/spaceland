using System;

namespace Station.Contracts
{
    public sealed class DetentionFacility
    {
        public long Id { get; }
        public Coord Position { get; }
        public int Capacity { get; }
        public int ServiceCapacity { get; }
        public long IntakeTicks { get; }
        public long ReleaseTicks { get; }
        public bool Powered { get; }
        public bool Connected { get; }
        public DetentionFacility(long id, Coord position, int capacity, int serviceCapacity = 1, long intakeTicks = 300, long releaseTicks = 300, bool powered = true, bool connected = true)
        {
            if (id < 1 || capacity < 1 || serviceCapacity < 1 || intakeTicks < 1 || releaseTicks < 1) throw new ArgumentException("Invalid detention facility capacity or duration.");
            Id = id; Position = position; Capacity = capacity; ServiceCapacity = serviceCapacity; IntakeTicks = intakeTicks; ReleaseTicks = releaseTicks; Powered = powered; Connected = connected;
        }
    }
}
