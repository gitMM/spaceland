using System;

namespace Station.Contracts
{
    // Immutable institutional payload. Citizens obtain it only through initial
    // knowledge or a completed read; it contains no live availability or needs.
    public sealed class RosterPayload
    {
        public long Version { get; }
        public long ActorId { get; }
        public int Cohort { get; }
        public long DayTicks { get; }
        public long ShiftStart { get; }
        public long DutyTicks { get; }
        public int CheckInOffset { get; }
        public Coord? RestTile { get; }
        public RosterPayload(long version, long actorId, int cohort, long dayTicks, long shiftStart, long dutyTicks, int checkInOffset, Coord? restTile)
        {
            if (version < 1 || actorId < 1 || cohort < 0 || dayTicks < 2 || shiftStart < 0 || shiftStart >= dayTicks || dutyTicks < 1 || dutyTicks >= dayTicks || checkInOffset < 0 || checkInOffset >= dutyTicks)
                throw new ArgumentException("Invalid roster identity or daily schedule.");
            Version = version; ActorId = actorId; Cohort = cohort; DayTicks = dayTicks; ShiftStart = shiftStart; DutyTicks = dutyTicks; CheckInOffset = checkInOffset; RestTile = restTile;
        }
        private long Relative(long tick)
        { if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick)); long time = tick % DayTicks; return time >= ShiftStart ? time - ShiftStart : DayTicks - (ShiftStart - time); }
        public bool OnDuty(long tick) => Relative(tick) < DutyTicks;
        public long NextBoundary(long tick)
        { long relative = Relative(tick); return checked(tick + (relative < DutyTicks ? DutyTicks - relative : DayTicks - relative)); }
        public long NextCheckIn(long tick, bool inclusive)
        {
            long relative = Relative(tick);
            long delta = relative <= CheckInOffset ? CheckInOffset - relative : DayTicks - relative + CheckInOffset;
            if (delta == 0 && !inclusive) delta = DayTicks;
            return checked(tick + delta);
        }
    }
}
