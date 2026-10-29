using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum RoutineOpportunityKind { PreDutyMeal, DutyMeal, AfterDutyMeal, ScheduledRest, Sleep }
    public readonly struct RoutineOpportunity
    {
        public RoutineOpportunityKind Kind { get; }
        public long Opens { get; }
        public long Closes { get; }
        public NeedKind Need => Kind == RoutineOpportunityKind.Sleep ? NeedKind.SleepPressure : Kind == RoutineOpportunityKind.ScheduledRest ? NeedKind.WorkStrain : NeedKind.Hunger;
        public RoutineOpportunity(RoutineOpportunityKind kind, long opens, long closes) { Kind = kind; Opens = opens; Closes = closes; }
    }
    // Deterministic opportunities derived exclusively from the known roster.
    // Durations here scale with the declared day; physical action durations do not.
    public static class DailyRoutine
    {
        public static IReadOnlyList<RoutineOpportunity> Around(RosterPayload roster, long tick)
        {
            if (tick < 0 || roster.DayTicks < 24 || roster.DutyTicks < 4) throw new ArgumentException("Daily life requires a day of at least 24 ticks and duty of at least 4 ticks.");
            long hour = roster.DayTicks / 24, sleep = Math.Min(roster.DayTicks / 3, roster.DayTicks - roster.DutyTicks - hour);
            if (sleep < 1) throw new ArgumentException("Daily life requires a sleep and pre-duty opportunity outside duty.");
            long sleepStart = roster.DayTicks - hour - sleep;
            long stagger = roster.CheckInOffset % Math.Max(1, roster.DutyTicks / 8);
            var templates = new[] {
                new RoutineOpportunity(RoutineOpportunityKind.PreDutyMeal, roster.DayTicks - hour, roster.DayTicks),
                new RoutineOpportunity(RoutineOpportunityKind.DutyMeal, roster.DutyTicks / 2 + stagger, Math.Min(roster.DutyTicks, roster.DutyTicks / 2 + stagger + Math.Max(1, roster.DutyTicks / 8))),
                new RoutineOpportunity(RoutineOpportunityKind.AfterDutyMeal, roster.DutyTicks, Math.Min(sleepStart, roster.DutyTicks + 2 * hour)),
                new RoutineOpportunity(RoutineOpportunityKind.ScheduledRest, roster.DutyTicks * 3 / 4 + stagger, roster.DutyTicks),
                new RoutineOpportunity(RoutineOpportunityKind.Sleep, sleepStart, roster.DayTicks - hour) };
            long cycle = tick / roster.DayTicks; var result = new List<RoutineOpportunity>();
            for (long day = cycle - 1; day <= cycle + 1; day++)
            foreach (var t in templates.Where(t => t.Closes > t.Opens))
            {
                long start = checked(day * roster.DayTicks + roster.ShiftStart);
                result.Add(new RoutineOpportunity(t.Kind, checked(start + t.Opens), checked(start + t.Closes)));
            }
            return result.OrderBy(t => t.Opens).ThenBy(t => t.Kind).ToArray();
        }
    }
}
