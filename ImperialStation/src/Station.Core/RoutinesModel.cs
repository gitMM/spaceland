using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class RoutineConfig
    {
        public long DayTicks { get; }
        public int Cohorts { get; }
        public long DutyTicks { get; }
        public int CheckInWindow { get; }
        public long PeriodicCheckInTicks { get; }
        public RoutineConfig(long dayTicks = 864000, int cohorts = 3, long dutyTicks = 288000, int checkInWindow = 18000, long periodicCheckInTicks = 36000)
        {
            if (dayTicks < 2 || cohorts < 1 || dayTicks % cohorts != 0 || dutyTicks < 1 || dutyTicks >= dayTicks || dutyTicks > dayTicks / cohorts || checkInWindow < 1 || checkInWindow > dutyTicks || periodicCheckInTicks < 1)
                throw new ArgumentException("Invalid station day, cohort duty or check-in windows.");
            DayTicks = dayTicks; Cohorts = cohorts; DutyTicks = dutyTicks; CheckInWindow = checkInWindow; PeriodicCheckInTicks = periodicCheckInTicks;
        }
    }
    public sealed class RosterAssignmentSeed
    {
        public Id<ActorTag> Actor { get; }
        public int Cohort { get; }
        public Coord? RestTile { get; }
        public bool InitiallyKnown { get; }
        public RosterAssignmentSeed(Id<ActorTag> actor, int cohort, Coord? restTile = null, bool initiallyKnown = true)
        { if (actor.Value < 1 || cohort < 0) throw new ArgumentException("Invalid roster actor/cohort."); Actor = actor; Cohort = cohort; RestTile = restTile; InitiallyKnown = initiallyKnown; }
    }
    public sealed class RoutineSeed
    {
        public RoutineConfig Config { get; }
        public IReadOnlyList<RosterAssignmentSeed> Assignments { get; }
        public RoutineSeed(RoutineConfig? config = null, IEnumerable<RosterAssignmentSeed>? assignments = null)
        { Config = config ?? new RoutineConfig(); Assignments = (assignments ?? Array.Empty<RosterAssignmentSeed>()).OrderBy(a => a.Actor).ToArray(); }
    }
    public sealed class RosterSnapshot
    {
        public RosterPayload Roster { get; internal set; } = null!;
        public string OperationKey { get; internal set; } = "";
        public long PublicationTick { get; internal set; }
        public bool Published { get; internal set; }
        public long MessageId { get; internal set; }
        internal RosterSnapshot Copy() => (RosterSnapshot)MemberwiseClone();
    }
    public sealed class RoutineSnapshot
    {
        public long ActorId { get; internal set; }
        public RosterPayload? KnownRoster { get; internal set; }
        public bool OnDuty { get; internal set; }
        public string Activity { get; internal set; } = "UnknownRoster";
        public long NextBoundary { get; internal set; } = -1;
        public long NextShiftCheckIn { get; internal set; } = -1;
        public long NextDecision { get; internal set; } = -1;
        public bool EndShiftCheckIn { get; internal set; }
        internal RoutineSnapshot Copy() => (RoutineSnapshot)MemberwiseClone();
    }
}
