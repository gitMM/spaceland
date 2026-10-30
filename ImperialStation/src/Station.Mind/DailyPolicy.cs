using System;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Daily-routine goal policy over the citizen's known roster and own needs.
    public static class DailyPolicy
    {
        // A meal below seek is still needed when the known roster offers no later meal
        // opportunity before own hunger, at its configured rates, would become critical
        // (typically across sleep). Uses only own needs and the known calendar.
        public static bool MealNeeded(NeedRule hunger, Func<long> q, RosterPayload? knownRoster, long goalRosterVersion, long goalCloses, long now, long tickDurationMs)
        {
            if (hunger.Seek <= 0) return false;
            long hungerQ = q();
            if (hungerQ >= hunger.Seek * NeedAccumulator.HourMilliseconds) return true;
            if (hunger.Critical <= 0 || knownRoster == null || knownRoster.Version != goalRosterVersion) return false;
            long next = DailyRoutine.Around(knownRoster, goalCloses).Where(w => w.Need == NeedKind.Hunger && w.Opens >= goalCloses).Select(w => w.Opens).DefaultIfEmpty(-1).Min();
            return next > now && new NeedAccumulator(hungerQ, now, Math.Max(hunger.AwakeRate, hunger.SleepingRate)).At(next, tickDurationMs) >= hunger.Critical * NeedAccumulator.HourMilliseconds;
        }
        // Routine protection for the short in-duty meal window (R-LIF-11): the tick at which
        // own hunger, at its configured awake/working rate, reaches seek, when that falls
        // before the open window closes; otherwise -1. Ordinary work then waits for the
        // meal instead of starting indivisible work that would run past the close.
        public static long DutyMealComingDue(NeedRule hunger, long hungerQ, long goalCloses, long now, long tickDurationMs)
        {
            if (hunger.Seek <= 0 || hungerQ >= hunger.Seek * NeedAccumulator.HourMilliseconds || goalCloses <= now) return -1;
            long? due = new NeedAccumulator(hungerQ, now, Math.Max(hunger.AwakeRate, hunger.WorkingRate)).FirstAtLeast(now, tickDurationMs, hunger.Seek);
            return due.HasValue && due.Value < goalCloses ? due.Value : -1;
        }
        // A pending goal stays open until its window closes (scheduled rest stays open
        // until done); a meal goal is open only while the meal is needed.
        public static bool Open(bool pending, RoutineOpportunityKind kind, NeedKind need, long closes, long now, Func<bool> mealNeeded) =>
            pending && (kind == RoutineOpportunityKind.ScheduledRest || closes > now) && (need != NeedKind.Hunger || mealNeeded());
        // An open goal competes once due and past its own retry delay.
        public static bool Eligible(bool pending, RoutineOpportunityKind kind, NeedKind need, long due, long retryAt, long closes, long now, Func<bool> mealNeeded) =>
            pending && due <= now && retryAt <= now && (kind == RoutineOpportunityKind.ScheduledRest || closes > now) && (need != NeedKind.Hunger || mealNeeded());
        // A meal window that closes while the meal was not needed was unnecessary, not missed.
        public static bool MealUnnecessary(NeedKind need, Func<bool> mealNeeded) => need == NeedKind.Hunger && !mealNeeded();
        // How long to sleep, from own knowledge only: the bed's published duration, less
        // sleep already taken in this sleep window (an interrupted sleep keeps its pro-rata
        // relief, so only the rest is still needed), and never past the next pre-duty meal
        // opening of the known roster, which closes the sleep window and precedes duty. A
        // shorter sleep is a finite action of that length; relief stays pro rata by elapsed
        // ticks (R-LIF-08, R-LIF-12).
        public static long SleepLength(RosterPayload? knownRoster, long now, long bedDuration, long sleptThisWindow)
        {
            long length = Math.Max(1, bedDuration - Math.Max(0, sleptThisWindow));
            if (knownRoster == null) return length;
            long wake = DailyRoutine.Around(knownRoster, now).Where(w => w.Kind == RoutineOpportunityKind.PreDutyMeal && w.Opens > now).Select(w => w.Opens).DefaultIfEmpty(long.MaxValue).Min();
            return wake == long.MaxValue ? length : Math.Max(1, Math.Min(length, wake - now));
        }
        // Opening of the known sleep window that contains now, or null outside one.
        public static long? SleepWindowOpened(RosterPayload? knownRoster, long now) =>
            knownRoster == null ? null : DailyRoutine.Around(knownRoster, now).Where(w => w.Kind == RoutineOpportunityKind.Sleep && w.Opens <= now && now < w.Closes).Select(w => (long?)w.Opens).FirstOrDefault();
        // Sleep yields to pre-sleep relief of a need forecast to become critical in bed.
        public static bool YieldsToPreSleepRelief(RoutineOpportunityKind kind, Func<NeedKind?> preSleepRelief) => kind == RoutineOpportunityKind.Sleep && preSleepRelief().HasValue;
    }
}
