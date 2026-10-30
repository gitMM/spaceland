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
        // A pending goal stays open until its window closes (scheduled rest stays open
        // until done); a meal goal is open only while the meal is needed.
        public static bool Open(bool pending, RoutineOpportunityKind kind, NeedKind need, long closes, long now, Func<bool> mealNeeded) =>
            pending && (kind == RoutineOpportunityKind.ScheduledRest || closes > now) && (need != NeedKind.Hunger || mealNeeded());
        // An open goal competes once due and past its own retry delay.
        public static bool Eligible(bool pending, RoutineOpportunityKind kind, NeedKind need, long due, long retryAt, long closes, long now, Func<bool> mealNeeded) =>
            pending && due <= now && retryAt <= now && (kind == RoutineOpportunityKind.ScheduledRest || closes > now) && (need != NeedKind.Hunger || mealNeeded());
        // A meal window that closes while the meal was not needed was unnecessary, not missed.
        public static bool MealUnnecessary(NeedKind need, Func<bool> mealNeeded) => need == NeedKind.Hunger && !mealNeeded();
        // Sleep yields to pre-sleep relief of a need forecast to become critical in bed.
        public static bool YieldsToPreSleepRelief(RoutineOpportunityKind kind, Func<NeedKind?> preSleepRelief) => kind == RoutineOpportunityKind.Sleep && preSleepRelief().HasValue;
    }
}
