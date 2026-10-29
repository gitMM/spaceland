using System.Diagnostics;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public static partial class StationBaseline
{
    public sealed class Participation
    {
        public long Actor { get; set; }
        public long[] Meals { get; set; } = Array.Empty<long>();
        public long[] SleepTicks { get; set; } = Array.Empty<long>();
        public long Rests { get; set; }
        public long Toilets { get; set; }
        public long Conversations { get; set; }
        public long Plans { get; set; }
        public long Arrivals { get; set; }
        public long Work { get; set; }
        public long[] WorkPerDay { get; set; } = Array.Empty<long>();
        public long Reads { get; set; }
        public long Posts { get; set; }
        public long FailedActions { get; set; }
        internal long ModeSince;
        internal bool Sleeping;
    }
    public sealed class Result
    {
        public string Status { get; set; } = "completed";
        public string Scope { get; set; } = "";
        public string Build { get; set; } = "";
        public string Content { get; set; } = "";
        public string Scenario { get; set; } = "";
        public ulong Seed { get; set; }
        public string Digest { get; set; } = "";
        public string Runtime { get; set; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        public string OS { get; set; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        public int LogicalProcessors { get; set; } = Environment.ProcessorCount;
        public int Population { get; set; }
        public long Horizon { get; set; }
        public long DayTicks { get; set; } = Day;
        public int TickMilliseconds { get; set; } = 100;
        public double InitializationSeconds { get; set; }
        public double ExecutionSeconds { get; set; }
        public long AllocatedBytes { get; set; }
        public long PeakWorkingSetBytes { get; set; }
        public long Events { get; set; }
        public long MaxTerminalQueue { get; set; }
        public long MaxNeedWaitTicks { get; set; }
        public long MealsConsumed { get; set; }
        public int ActiveWorkers { get; set; }
        public long PlannerExpansions { get; set; }
        public long PathExpansions { get; set; }
        public long PendingDailyGoals { get; set; }
        public long MissedDailyGoals { get; set; }
        public long CriticalInterruptions { get; set; }
        public long RejectedActivities { get; set; }
        public long ScheduledRestsCompleted { get; set; }
        public long ScheduledRestsOverdue { get; set; }
        public long CompletedJobs { get; set; }
        public long BlockedAttempts { get; set; }
        public int CausalRolesVerified { get; set; }
        public long ExternalActions { get; set; }
        public List<string> Failures { get; set; } = new();
        public List<Participation> Citizens { get; set; } = new();
    }
    public static Result Execute(LoadedScenario scenario, string? output = null, bool requireDaily = true, Action<long>? progress = null, Action<World, long>? observe = null, Func<long, bool>? requiresPhysicalWork = null, Func<TraceEvent, bool>? permittedExternalTrigger = null)
    {
        var config = scenario.Scenario.InitialState;
        if (output != null)
        { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "scenario.json"), scenario.ScenarioJson); File.WriteAllText(Path.Combine(output, "catalog.json"), scenario.CatalogJson); }
        var clock = Stopwatch.StartNew(); var w = scenario.CreateWorld(trace: true);
        var result = new Result { Scope = requireDaily ? "Integrated daily participation; not full T22 variant closure" : "Population work/needs baseline; T15 institutional chains remain separate", Build = Runs.BuildId(), Content = scenario.ContentDigest,
            Scenario = World.Hash(System.Text.Encoding.UTF8.GetBytes(scenario.ScenarioJson)), Seed = config.Seed, Population = config.Actors.Count, Horizon = config.Horizon, InitializationSeconds = clock.Elapsed.TotalSeconds };
        var proof = config.ResponseChainRoles.Count > 0 ? new List<TraceEvent>() : null;
        int days = (int)(config.Horizon / Day);
        var people = config.Actors.ToDictionary(a => a.Id, a => new Participation { Actor = a.Id, Meals = new long[days], SleepTicks = new long[days], WorkPerDay = new long[days] });
        void SleepThrough(Participation p, long end)
        {
            if (p.Sleeping) for (int d = 0; d < days; d++) p.SleepTicks[d] += Math.Max(0, Math.Min(end, (d + 1) * Day) - Math.Max(p.ModeSince, d * Day));
            p.ModeSince = end;
        }
        long unexpectedExternal = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread(); clock.Restart();
        for (long tick = 0; tick <= config.Horizon; tick = Math.Min(config.Horizon, tick + 1000))
        {
            w.AdvanceTo(tick); w.AssertAccounting();
            observe?.Invoke(w, tick);
            foreach (var e in w.Trace)
            {
                result.Events++;
                if (e.Type == "ActionRequested" && e.Origin == ActionOrigin.ExternalCommand) { result.ExternalActions++; if (!(permittedExternalTrigger?.Invoke(e) ?? false)) unexpectedExternal++; }
                if (!people.TryGetValue(e.ActorId, out var p)) continue;
                int day = (int)(e.Tick / Day);
                if (e.Type == "NeedModeChanged") { SleepThrough(p, e.Tick); p.Sleeping = e.Detail == "Sleeping"; }
                if (e.Type == "MealConsumed" && day < days) p.Meals[day]++;
                if (e.Type == "PlanSelected") p.Plans++;
                if (e.Type == "TileArrived") p.Arrivals++;
                if (e.Type == "CleaningCompleted" && e.Quantity > 0) { p.Work++; if (day < days) p.WorkPerDay[day]++; }
                if (e.Type == "ActionFailed") p.FailedActions++;
                if (e.Type == "ActionCompleted")
                {
                    if (e.Detail == "Rest") p.Rests++; if (e.Detail == "UseToilet") p.Toilets++;
                    if (e.Detail == "Converse") p.Conversations++; if (e.Detail == "ReadInbox") p.Reads++; if (e.Detail == "PostMessage") p.Posts++;
                }
            }
            proof?.AddRange(w.Trace); w.ClearRecordedTrace();
            progress?.Invoke(tick);
            foreach (var service in w.InspectNeedServices())
                if (service.OccupantActionIds.Count > service.Definition.Capacity) throw new InvalidOperationException("Service capacity exceeded.");
            if (tick == config.Horizon) break;
        }
        result.ExecutionSeconds = clock.Elapsed.TotalSeconds; result.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        result.PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
        foreach (var p in people.Values) SleepThrough(p, config.Horizon);
        result.Citizens = people.Values.OrderBy(p => p.Actor).ToList();
        result.ActiveWorkers = people.Values.Count(p => p.Plans > 0 && p.Arrivals > 0 && p.Work > 0);
        result.Digest = w.Digest(); result.MaxTerminalQueue = w.MaximumTerminalQueue; result.MealsConsumed = w.MealsConsumed;
        result.PlannerExpansions = w.PlannerExpansions; result.PathExpansions = w.PathExpansions;
        result.MaxNeedWaitTicks = w.InspectNeedActivities().Select(a =>
            (a.StartedTick >= 0 ? a.StartedTick : a.EndedTick >= 0 ? a.EndedTick : config.Horizon) - a.RequestedTick).DefaultIfEmpty().Max();
        result.PendingDailyGoals = w.InspectDailyGoals().Count(g => g.Status == DailyGoalStatus.Pending);
        result.MissedDailyGoals = w.InspectDailyGoals().Count(g => g.Status == DailyGoalStatus.MissedWindow);
        result.CriticalInterruptions = w.CriticalNeedInterruptions;
        result.RejectedActivities = w.InspectNeedActivities().Count(a => a.Status == NeedActivityStatus.Rejected);
        result.ScheduledRestsCompleted = w.InspectDailyGoals().Count(g => g.Kind == RoutineOpportunityKind.ScheduledRest && g.Status == DailyGoalStatus.Completed);
        result.ScheduledRestsOverdue = w.InspectDailyGoals().Count(g => g.Kind == RoutineOpportunityKind.ScheduledRest && g.Closes < config.Horizon && g.Status != DailyGoalStatus.Completed);
        result.CompletedJobs = w.InspectJobs().Count(j => j.PhysicalStatus == WorkExecution.CompletedPhysically);
        result.BlockedAttempts = w.InspectWorkAttempts().Count(a => a.Execution == WorkExecution.Blocked);
        if (proof != null)
        {
            var chains = ResponseChains.Check(proof, config.ResponseChainRoles, -1);
            result.CausalRolesVerified = chains.Count(c => c.Passed);
            foreach (var chain in chains) result.Failures.AddRange(chain.Failures.Select(f => chain.Role + ": " + f));
            if (output != null) File.WriteAllText(Path.Combine(output, "response-chains.json"), JsonData.Write(chains));
        }
        if (unexpectedExternal > 0) result.Failures.Add("Autonomous workload contains undeclared external action commands.");
        foreach (var p in result.Citizens)
        {
            if ((requiresPhysicalWork?.Invoke(p.Actor) ?? true) && (p.Work == 0 || p.Posts == 0) || p.Plans == 0 || p.Arrivals == 0 || p.Reads == 0) result.Failures.Add("Citizen " + p.Actor + " lacks role-appropriate work/planning/travel/read/post participation.");
            if (requireDaily && (p.Meals.Any(n => n == 0) || p.SleepTicks.Any(n => n == 0) || p.Rests == 0 || p.Toilets == 0 || p.Conversations == 0)) result.Failures.Add("Citizen " + p.Actor + " lacks required daily activity.");
            if (requireDaily && (requiresPhysicalWork?.Invoke(p.Actor) ?? true) && p.WorkPerDay.Any(n => n == 0)) result.Failures.Add("Citizen " + p.Actor + " did not work on every configured day.");
        }
        if (requireDaily && result.ScheduledRestsOverdue > 0) result.Failures.Add("Scheduled rests remained overdue despite adequate services.");
        if (result.Failures.Count > 0) result.Status = "assertion_failed";
        if (output != null) { File.WriteAllText(Path.Combine(output, "result.json"), JsonData.Write(result)); Checkpoint.SaveBinary(w, Path.Combine(output, "checkpoint.json")); }
        return result;
    }
}
