using Station.Contracts;
using Station.Core;
using Station.Mind;

namespace Station.Runner;

public static partial class RecoveryStation
{
    public sealed record Phase(long Tick, long Dirt, int BrokenMachines, int CompletedJobs, int OpenFacilityTasks, int MissingReports, int ResolvedReports);
    public sealed class Result
    {
        public string Status { get; set; } = "completed";
        public StationBaseline.Result Participation { get; set; } = new();
        public List<Phase> Phases { get; set; } = new();
        public SortedDictionary<string, long> Events { get; set; } = new(StringComparer.Ordinal);
        public bool CheckpointMatches { get; set; }
        public double CheckpointVerificationSeconds { get; set; }
        public long ActionsWithoutDeclaredCause { get; set; }
        public List<string> Failures { get; set; } = new();
    }
    public static Result Execute(LoadedScenario scenario, string? output = null)
    {
        var result = new Result(); World? last = null; byte[]? continuation = null;
        if (output != null) Directory.CreateDirectory(output);
        using var trace = output != null && scenario.Scenario.InitialState.Actors.Count <= 32 ? new StreamWriter(Path.Combine(output, "trace.jsonl")) : null;
        using var institutionalTrace = output != null ? new StreamWriter(Path.Combine(output, "institution-events.jsonl")) : null;
        result.Participation = StationBaseline.Execute(scenario, output, requireDaily: false, observe: (w, tick) =>
        {
            last = w;
            if (tick == BurstEnd) continuation = w.Serialize();
            foreach (var e in w.Trace)
            {
                result.Events[e.Type] = result.Events.GetValueOrDefault(e.Type) + 1;
                if (e.Type == "ActionRequested" && (!(e.Origin is ActionOrigin.Autonomous or ActionOrigin.SystemProtocol) || e.CauseId <= 0 || e.CauseId >= e.Id)) result.ActionsWithoutDeclaredCause++;
                if (e.Type is "CardLost" or "MachineFaulted" or "FacilityTaskCreated" or "FacilityTaskResponse" or "CredentialRecoveryCompleted" or "ReplacementCardIssued" or "MissingWorkReportOpened" or "IncidentResponseChanged" or "PatrolResultReceived" or "ReportExpectationSatisfied") institutionalTrace?.WriteLine(Runs.EventJson(e));
                trace?.WriteLine(Runs.EventJson(e));
            }
            // Also sample the first observation after the burst, so a quickly repaired
            // fault is still measured as a disruption phase.
            if (tick % 6000 != 0 && tick != scenario.Scenario.InitialState.Horizon && !(tick >= BurstStart && tick - 1000 < BurstStart)) return;
            var incidents = w.InspectMissingReports();
            var phase = new Phase(tick, w.InspectDirt().Sum(d => d.Units), w.InspectMachines().Count(m => !m.Operational),
                w.InspectJobs().Count(j => j.PhysicalStatus == WorkExecution.CompletedPhysically),
                w.InspectFacilityTasks().Count(t => t.Status != FacilityTaskStatus.ReportedComplete), incidents.Count,
                incidents.Count(i => i.ResponseMessageId > 0 || i.ReplacementResultMessageId > 0));
            result.Phases.Add(phase);
            Console.Error.WriteLine(JsonData.Write(phase));
        });
        var world = last!; var final = result.Phases.Last();
        result.Failures.AddRange(result.Participation.Failures);
        void Require(bool condition, string failure) { if (!condition) result.Failures.Add(failure); }
        Require(final.Dirt == 0, "Finite dirt backlog did not drain.");
        Require(final.BrokenMachines == 0, "Faulted machines remain broken.");
        Require(final.OpenFacilityTasks == 0, "Facility tasks lack completed reports.");
        Require(result.Events.GetValueOrDefault("MissingWorkReportOpened") > 0, "No overdue work reports were exercised.");
        Require(final.MissingReports == final.ResolvedReports, "Missing report incidents lack a delivered response.");
        Require(world.InspectMissingReports().All(i => i.PatrolResultMessageId > 0 || i.ResponseMessageId > 0 && i.WorkerStatusResponse == "ResponseReceived"), "Missing workers lack delivered patrol outcomes or original replies closing an unposted search.");
        Require(world.InspectJobs().All(j => j.PhysicalStatus == WorkExecution.CompletedPhysically && j.AuthorityStatus == "WorkCompleted"), "Work backlog lacks physical completion and received completion reports.");
        Require(result.Events.GetValueOrDefault("CredentialRecoveryCompleted") == scenario.Scenario.Disturbances.Count(d => d.Type == "CardLoss"), "Lost credentials were not all recovered.");
        Require(result.Events.GetValueOrDefault("CardLost") == scenario.Scenario.Disturbances.Count(d => d.Type == "CardLoss"), "Card loss burst did not execute.");
        Require(scenario.Scenario.InitialState.Actors.All(a => CredentialPlanning.KnownUsableCredential(world.ActorView(new(a.Id)))), "Citizens lack usable physical credentials after recovery.");
        Require(result.ActionsWithoutDeclaredCause == 0, "Actions lack declared autonomous/protocol provenance.");
        Require(result.Events.GetValueOrDefault("MachineFaulted") == scenario.Scenario.Disturbances.Count(d => d.Type == "MachineFault"), "Fault burst did not execute.");
        Require(result.Participation.ScheduledRestsOverdue == 0, "Scheduled rests remain overdue.");
        Require(result.Events.GetValueOrDefault("ActionFailed") < result.Participation.Population * 100L, "Retry storm: more than 100 failed actions per citizen.");
        Require(result.Phases.Any(p => p.Tick < BurstStart && p.Dirt < result.Phases[0].Dirt), "No normal work progress before disruptions.");
        Require(result.Phases.Any(p => p.Tick >= BurstStart && p.BrokenMachines > 0), "No measured disruption phase.");
        foreach (var p in result.Participation.Citizens)
            Require(p.Meals.All(n => n > 0) && p.SleepTicks.All(n => n > 0) && p.Toilets > 0 && p.Rests > 0, "Citizen " + p.Actor + " lacks daily meals/sleep/toilet/rest.");
        if (output != null && continuation != null) File.WriteAllBytes(Path.Combine(output, "burst-end.bin"), continuation);
        if (continuation != null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var restored = World.Restore(continuation);
            long nextProgress = BurstEnd;
            for (long tick = BurstEnd + 997; tick < result.Participation.Horizon; tick += 997)
            {
                restored.AdvanceTo(tick);
                if (tick >= nextProgress)
                { Console.Error.WriteLine("Checkpoint continuation tick " + tick + "/" + result.Participation.Horizon); nextProgress = tick + 6000; }
            }
            restored.AdvanceTo(result.Participation.Horizon);
            result.CheckpointMatches = restored.Digest() == result.Participation.Digest;
            result.CheckpointVerificationSeconds = timer.Elapsed.TotalSeconds;
            Require(result.CheckpointMatches, "Checkpoint continuation differs from the original run.");
        }
        if (result.Failures.Count > 0) result.Status = "assertion_failed";
        if (output != null)
        {
            File.WriteAllText(Path.Combine(output, "recovery-result.json"), JsonData.Write(result));
            File.WriteAllText(Path.Combine(output, "institutions.json"), JsonData.Write(new { jobs = world.InspectJobs(), attempts = world.InspectWorkAttempts(), incidents = world.InspectMissingReports(), facility = world.InspectFacilityTasks(), recoveries = world.InspectCredentialRecoveryCases(), expectations = world.InspectExpectations() }));
        }
        return result;
    }
}
