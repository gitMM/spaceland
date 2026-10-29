using Station.Contracts;
using Station.Core;

namespace Station.Runner;

// Classifies every missed daily routine window from the trace and final goals.
// Measurement only; the categories explain timing, they never excuse a defect.
public static class RoutineWindows
{
    public sealed record Miss(long GoalId, long ActorId, string Kind, long Opens, long Closes, long ResolvedTick, string Cause);
    public sealed class Report
    {
        public SortedDictionary<string, int> Causes { get; set; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, int> Outcomes { get; set; } = new(StringComparer.Ordinal);
        // A finite activity admitted while its window was open must still count.
        public int RetentionViolations { get; set; }
        // Cafeteria reconsiderations: total, and the most by one citizen before one meal.
        public int CafeteriaSwitches { get; set; }
        public int MaximumSwitchesPerMeal { get; set; }
        public List<Miss> Misses { get; set; } = new();
    }
    public sealed class Tracker
    {
        private sealed class ActorState
        {
            internal string Action = ""; internal long ActionId; internal long Since;
            internal long HungerSeek = -1; internal bool Custody;
            internal string LastCritical = ""; internal long LastCriticalTick = -1;
        }
        private sealed class GoalState
        {
            internal long Actor; internal string Kind = ""; internal bool Planned, Deferred, Blocked; internal long Claimed = -1;
            internal string Interrupted = ""; internal long Resolved = -1; internal string ActionAtResolution = ""; internal bool CustodyAtResolution; internal long HungerSeek = -1;
        }
        private readonly Dictionary<long, ActorState> actors = new();
        private readonly Dictionary<long, GoalState> goals = new();
        private readonly Dictionary<long, long> plans = new();
        private readonly Dictionary<long, long> actionGoals = new();
        private readonly Dictionary<long, long> subjects = new();
        private readonly Dictionary<long, int> mealSwitches = new();
        private int switches, maximumSwitches;
        private static long Number(string detail, string name)
        {
            int at = detail.IndexOf(name + "=", StringComparison.Ordinal); if (at < 0) return 0;
            int start = at + name.Length + 1, end = start; while (end < detail.Length && char.IsDigit(detail[end])) end++;
            return end == start ? 0 : long.Parse(detail.AsSpan(start, end - start), System.Globalization.CultureInfo.InvariantCulture);
        }
        private ActorState Actor(long id) { if (!actors.TryGetValue(id, out var a)) actors[id] = a = new ActorState(); return a; }
        private static long Goal(string detail) => detail.StartsWith("goal=", StringComparison.Ordinal) ? long.Parse(detail.AsSpan(5, detail.IndexOf(';') - 5), System.Globalization.CultureInfo.InvariantCulture) : 0;
        public void Observe(TraceEvent e)
        {
            switch (e.Type)
            {
                case "DailyGoalDue": goals[Goal(e.Detail)] = new GoalState { Actor = e.ActorId, Kind = e.Detail.Split(';')[1].Trim() }; break;
                case "DailyGoalDeferred" when goals.TryGetValue(Goal(e.Detail), out var deferred): deferred.Deferred = true; break;
                case "DailyGoalBlocked" when goals.TryGetValue(Goal(e.Detail), out var blocked): blocked.Blocked = true; break;
                case "PlanSelected" when e.Detail.StartsWith("daily-routine/v1; goal=", StringComparison.Ordinal):
                    {
                        long id = Goal(e.Detail["daily-routine/v1; ".Length..]);
                        if (goals.TryGetValue(id, out var planned)) { planned.Planned = true; plans[e.Id] = id; }
                        break;
                    }
                case "ActionRequested":
                    {
                        var a = Actor(e.ActorId); a.Action = e.Detail; a.ActionId = e.ActionId; a.Since = e.Tick;
                        if (plans.Remove(e.CauseId, out long goal)) actionGoals[e.ActionId] = goal;
                        break;
                    }
                case "ActionCompleted" or "ActionFailed" or "ActionCancelled":
                    {
                        var a = Actor(e.ActorId);
                        if (e.Type == "ActionCancelled" && e.Detail == "Sleep" && a.LastCriticalTick == e.Tick)
                            foreach (var g in goals.Values.Where(g => g.Actor == e.ActorId && g.Kind == "Sleep" && g.Resolved < 0)) g.Interrupted = a.LastCritical;
                        if (a.ActionId == e.ActionId || e.ActionId == 0) { a.Action = ""; a.ActionId = 0; }
                        actionGoals.Remove(e.ActionId);
                        break;
                    }
                case "NeedServiceClaimed" when actionGoals.TryGetValue(e.ActionId, out long claimed) && goals.TryGetValue(claimed, out var c) && c.Claimed < 0: c.Claimed = e.Tick; break;
                case "NeedThresholdReached":
                    {
                        var a = Actor(e.ActorId);
                        if (e.Detail.StartsWith("Hunger/Seek", StringComparison.Ordinal)) a.HungerSeek = e.Tick;
                        if (e.Detail.Contains("/Critical", StringComparison.Ordinal)) { a.LastCritical = e.Detail.Split('/')[0]; a.LastCriticalTick = e.Tick; }
                        break;
                    }
                case "DetentionAuthorized": subjects[Number(e.Detail, "authorization")] = Number(e.Detail, "known-actor"); break;
                case "CustodyBegan" when subjects.TryGetValue(Number(e.Detail, "authorization"), out long subject): Actor(subject).Custody = true; break;
                case "DetentionReleased": Actor(e.ActorId).Custody = false; break;
                case "CafeteriaReconsidered":
                    switches++; mealSwitches[e.ActorId] = mealSwitches.GetValueOrDefault(e.ActorId) + 1; maximumSwitches = Math.Max(maximumSwitches, mealSwitches[e.ActorId]); break;
                case "MealConsumed": mealSwitches.Remove(e.ActorId); break;
                case "DailyGoalResolved" when goals.TryGetValue(Goal(e.Detail), out var resolved):
                    {
                        var a = Actor(resolved.Actor);
                        resolved.Resolved = e.Tick; resolved.ActionAtResolution = a.Action; resolved.CustodyAtResolution = a.Custody; resolved.HungerSeek = a.HungerSeek;
                        break;
                    }
            }
        }
        public Report Finish(World w)
        {
            var report = new Report { CafeteriaSwitches = switches, MaximumSwitchesPerMeal = maximumSwitches };
            foreach (var g in w.InspectDailyGoals())
            {
                report.Outcomes[g.Kind + "/" + g.Status] = report.Outcomes.GetValueOrDefault(g.Kind + "/" + g.Status) + 1;
                if (g.Status != DailyGoalStatus.MissedWindow || !goals.TryGetValue(g.Id, out var s)) continue;
                if (s.Claimed >= 0 && s.Claimed < g.Closes) report.RetentionViolations++;
                string cause = g.Kind == RoutineOpportunityKind.Sleep ? SleepCause(g, s) : MealCause(g, s);
                report.Causes[g.Kind + "/" + cause] = report.Causes.GetValueOrDefault(g.Kind + "/" + cause) + 1;
                report.Misses.Add(new Miss(g.Id, g.ActorId, g.Kind.ToString(), g.Opens, g.Closes, s.Resolved, cause));
            }
            return report;
        }
        private static string SleepCause(DailyGoalSnapshot g, GoalState s)
        {
            if (s.CustodyAtResolution) return "Custody";
            if (s.Interrupted.Length > 0) return "CriticalInterruption/" + s.Interrupted;
            if (s.Resolved > g.Closes) return "TravelAcrossBoundary";
            if (s.ActionAtResolution.Length > 0) return "CommittedAction/" + s.ActionAtResolution;
            return s.Blocked ? "Blocked/" + g.Reason : s.Planned ? "StartedTooLate" : "HigherPriorityGoal";
        }
        private static string MealCause(DailyGoalSnapshot g, GoalState s)
        {
            if (s.CustodyAtResolution) return "Custody";
            if (s.Resolved > g.Closes) return "TravelAcrossBoundary";
            // Hunger reached seek too late to travel, queue and eat before closing.
            if (s.HungerSeek >= g.Opens && g.Closes - s.HungerSeek < 1000) return "LateEligibility";
            if (s.ActionAtResolution.Length > 0) return "CommittedAction/" + s.ActionAtResolution;
            return s.Blocked ? "Blocked/" + g.Reason : s.Planned ? "StartedTooLate" : "HigherPriorityGoal";
        }
    }
}
