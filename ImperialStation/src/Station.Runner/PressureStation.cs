using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Station.Contracts;
using Station.Core;
using Station.Mind;

namespace Station.Runner;

public static partial class PressureStation
{
    public sealed record Phase(long Tick, long Dirt, int BrokenMachines, int OpenFacilityTasks, int UnfinishedJobs,
        int MissingResponses, int ActiveDispatches, int Occupants, int TerminalQueue, int NeedQueue, long FailedActions, int PendingCases = 0);
    public sealed class Result
    {
        public string Status { get; set; } = "completed";
        public StationBaseline.Result Participation { get; set; } = new();
        public List<Phase> Phases { get; set; } = new();
        public SortedDictionary<string, long> Events { get; set; } = new(StringComparer.Ordinal);
        public long InvalidCauses { get; set; }
        public long TraceGaps { get; set; }
        public bool OverlappingPressure { get; set; }
        public bool SharedGuardDuties { get; set; }
        public bool CapacityWait { get; set; }
        public int Reprimands { get; set; }
        public int Released { get; set; }
        public IReadOnlyList<CaseOutcome> CaseOutcomes { get; set; } = Array.Empty<CaseOutcome>();
        public int SubjectsRequiringCustody { get; set; }
        public int ReleasedSubjectsResumingWork { get; set; }
        public int ReleasedSubjectsReturningIdle { get; set; }
        public long RecoveryTick { get; set; } = -1;
        public bool CheckpointMatches { get; set; }
        public bool ContinuationTraceMatches { get; set; }
        public string ContinuationTraceHash { get; set; } = "";
        public bool TerminalQueuesRecovered { get; set; }
        public bool NeedQueuesRecovered { get; set; }
        public int AccountingChecks { get; set; }
        public long DuplicateActiveAssignments { get; set; }
        public int CitizensWithContinuingShifts { get; set; }
        public double ReplaySeconds { get; set; }
        public ResponseTimes.Report? ResponseTimes { get; set; }
        public List<string> Failures { get; set; } = new();
    }
    public static Result Execute(LoadedScenario scenario, string? output = null)
    {
        const long checkpointTick = 12000;
        var result = new Result(); World? final = null; byte[]? checkpoint = null;
        var s = scenario.Scenario.InitialState;
        var roles = s.Actors.ToDictionary(a => a.Id, a => a.Role);
        var subjects = scenario.Scenario.ScriptedActors.Select(a => a.ActorId).ToHashSet();
        var repairTriggers = scenario.Scenario.ScriptedActors.SelectMany(a => a.Commands)
            .Where(c => c.Kind == ActionKind.PostMessage && c.Message?.Kind == MessageKind.WorkCompleted).Select(c => c.Message!.AttemptId).Distinct().ToArray();
        var triggers = scenario.Scenario.ScriptedActors.SelectMany(a => a.Commands.Select(c => (Actor: a.ActorId, c.Tick, Detail: c.Kind.ToString()))).ToHashSet();
        bool IsTrigger(TraceEvent e) => e.Origin == ActionOrigin.ExternalCommand && triggers.Contains((e.ActorId, e.Tick, e.Detail));
        var lastWork = new Dictionary<long, long>();
        var lastCheckIn = new Dictionary<long, long>();
        var lastOnDuty = new Dictionary<long, long>();
        var terminalLastEmpty = new Dictionary<long, long>();
        var needLastEmpty = new Dictionary<long, long>();
        var onDuty = new Dictionary<long, int>();
        var offDuty = new Dictionary<long, int>();
        long previous = 0, failures = 0;
        var protocolCauses = new Dictionary<long, string>();
        using var originalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Hash(IncrementalHash hash, TraceEvent e) => hash.AppendData(Encoding.UTF8.GetBytes(Runs.EventJson(e) + "\n"));
        if (output != null) Directory.CreateDirectory(output);
        using var trace = output == null ? null : new StreamWriter(Path.Combine(output, "pressure-trace.jsonl"));
        var responses = new ResponseTimes.Tracker(scenario.Scenario);
        result.Participation = StationBaseline.Execute(scenario, output, requireDaily: false,
            requiresPhysicalWork: id => roles[id] is "cleaner" or "worker", permittedExternalTrigger: IsTrigger,
            observe: (w, tick) =>
            {
                final = w;
                // StationBaseline calls AssertAccounting immediately before this observer.
                result.AccountingChecks++;
                foreach (var terminal in w.InspectTerminals())
                {
                    terminalLastEmpty.TryAdd(terminal.Id, -1);
                    if (terminal.Queue.Count == 0) terminalLastEmpty[terminal.Id] = tick;
                }
                foreach (var service in w.InspectNeedServices())
                {
                    needLastEmpty.TryAdd(service.Definition.Id, -1);
                    if (service.QueuedActionIds.Count == 0) needLastEmpty[service.Definition.Id] = tick;
                }
                foreach (var e in w.Trace)
                {
                    responses.Observe(e);
                    if (e.Id != previous + 1) result.TraceGaps++; previous = e.Id;
                    result.Events[e.Type] = result.Events.GetValueOrDefault(e.Type) + 1;
                    if (e.Type is "CustodyBegan" or "DetentionReleaseDue" or "ConversationConsented") protocolCauses.Add(e.Id, e.Type);
                    if (e.Type == "ActionRequested" && !IsTrigger(e) && (e.CauseId <= 0 || e.CauseId >= e.Id || e.Origin is not (ActionOrigin.Autonomous or ActionOrigin.SystemProtocol))) result.InvalidCauses++;
                    if (e.Type == "ActionRequested" && e.Origin == ActionOrigin.SystemProtocol && (!protocolCauses.TryGetValue(e.CauseId, out var p) ||
                        p != (e.Detail switch { "Move" => "CustodyBegan", "ReleaseDetention" => "DetentionReleaseDue", "Converse" => "ConversationConsented", _ => "" }))) result.InvalidCauses++;
                    if (e.Type == "ActionFailed") failures++;
                    if (e.Type == "DutyBoundary" && e.Detail == "OnDuty") { onDuty[e.ActorId] = onDuty.GetValueOrDefault(e.ActorId) + 1; lastOnDuty[e.ActorId] = e.Tick; }
                    if (e.Type == "ActionCompleted" && e.Detail == "ReadInbox") lastCheckIn[e.ActorId] = e.Tick;
                    if (e.Type == "DutyBoundary" && e.Detail == "OffDuty") offDuty[e.ActorId] = offDuty.GetValueOrDefault(e.ActorId) + 1;
                    if (e.Type is "CleaningCompleted" or "ItemInstalled" or "CleanupGoalCompleted" or "RepairGoalCompleted") lastWork[e.ActorId] = e.Tick;
                    if (e.Tick > checkpointTick) Hash(originalHash, e);
                    if (s.Actors.Count <= 32 || e.Type is "CardLost" or "MachineFaulted" or "ReprimandAccepted" or "DetentionReleased" or "FacilityTaskResponse") trace?.WriteLine(Runs.EventJson(e));
                }
                responses.Sample(w, tick);
                if (tick == checkpointTick)
                {
                    checkpoint = w.Serialize();
                    if (output != null) File.WriteAllBytes(Path.Combine(output, "pressure-checkpoint.bin"), checkpoint);
                }
                var dispatches = w.InspectEscortDispatches();
                var machines = w.InspectMachines();
                var stays = w.InspectDetentionStays();
                int activeCases = w.InspectReprimands().Select(r => r.AccountId).Distinct()
                    .Count(account => !stays.Any(st => st.AccountId == account && st.Status == DetentionStayStatus.Released));
                if (machines.Any(m => !m.Operational) && activeCases >= 2 &&
                    w.InspectRecoveryAgents().Any(a => !CredentialPlanning.KnownUsableCredential(w.ActorView(new(a.ActorId)))) ) result.OverlappingPressure = true;
                if (w.InspectDetentionFacilities().Any(f => f.Occupants == f.Definition.Capacity) && w.InspectEscorts().Any(e => e.Status == EscortStatus.AwaitingIntake)) result.CapacityWait = true;
                if (tick % 6000 != 0 && tick != s.Horizon) return;
                result.DuplicateActiveAssignments += w.InspectWorkAttempts().Where(a => a.Assignment == AssignmentStatus.Active)
                    .GroupBy(a => a.JobId).Sum(g => Math.Max(0, g.Count() - 1));
                var phase = new Phase(tick, w.InspectDirt().Sum(d => d.Units), machines.Count(m => !m.Operational),
                    w.InspectFacilityTasks().Count(t => t.Status != FacilityTaskStatus.ReportedComplete),
                    w.InspectJobs().Count(j => j.PhysicalStatus != WorkExecution.CompletedPhysically || j.AuthorityStatus != "WorkCompleted"),
                    w.InspectMissingReports().Count(i => (i.ResponseMessageId == 0 && i.ReplacementResultMessageId == 0) || i.PatrolResultMessageId == 0 && i.WorkerStatusResponse != "ResponseReceived"),
                    dispatches.Count(d => d.Status != EscortDispatchStatus.Released), stays.Count(st => st.Status != DetentionStayStatus.Released),
                    w.InspectTerminals().Sum(t => t.Queue.Count), w.InspectNeedServices().Sum(n => n.QueuedActionIds.Count), failures,
                    InspectCaseOutcomes(w, repairTriggers).Count(c => c.Outcome == CaseOutcomeKind.Pending));
                result.Phases.Add(phase); Console.Error.WriteLine(JsonData.Write(phase));
            });
        var world = final!;
        result.ResponseTimes = responses.Finish(world, ResponseTimes.Limits.Pressure(StationBaseline.Day), world.Detention?.Escort?.GuardCount ?? 2);
        result.Failures.AddRange(result.Participation.Failures);
        void Require(bool ok, string reason) { if (!ok) result.Failures.Add(reason); }
        result.Reprimands = world.InspectReprimands().Count;
        var stays = world.InspectDetentionStays(); result.Released = stays.Count(st => st.Status == DetentionStayStatus.Released);
        result.CaseOutcomes = InspectCaseOutcomes(world, repairTriggers);
        Require(repairTriggers.Length == subjects.Count * 2 && result.CaseOutcomes.All(c => c.Outcome != CaseOutcomeKind.Pending),
            "Every declared case must have a completed verification and an accepted or explicit evidence-based outcome.");
        Require(result.Reprimands == result.CaseOutcomes.Count(c => c.Outcome == CaseOutcomeKind.Accepted) &&
            world.InspectCases().All(c => repairTriggers.Contains(c.Submission.AttemptId)), "Unexpected or duplicate reprimands/cases.");
        var custodySubjects = result.CaseOutcomes.Where(c => c.Outcome == CaseOutcomeKind.Accepted).GroupBy(c => c.ActorId)
            .Where(g => g.Count() >= world.Personnel!.DetentionThreshold).Select(g => g.Key).ToHashSet();
        result.SubjectsRequiringCustody = custodySubjects.Count;
        Require(custodySubjects.Count >= 2, "Multiple attributable custody chains were not exercised.");
        Require(result.Released == custodySubjects.Count && stays.Select(st => st.ActorId).Order().SequenceEqual(custodySubjects.Order()), "All and only qualifying subjects must complete custody and release.");
        Require(world.InspectDetentionAuthorizations().Count == custodySubjects.Count &&
            world.InspectDetentionAuthorizations().Select(a => a.ActorId).Order().SequenceEqual(custodySubjects.Order()), "Duplicate, unexpected or missing detention authorizations.");
        Require(result.TraceGaps == 0 && result.InvalidCauses == 0 && result.Participation.ExternalActions == triggers.Count, "Trace or autonomous response provenance failed.");
        Require(result.OverlappingPressure, "Recovery and multiple institutional chains did not overlap.");
        Require(result.Participation.MaxTerminalQueue > 0, "Shared terminal contention was not exercised.");
        // Capacity waiting is reported, not required: shared guards may serialize
        // arrivals before intake. Staffing contention is measured independently.
        Require(result.Events.GetValueOrDefault("CardLost") == scenario.Scenario.Disturbances.Count(d => d.Type == "CardLoss"), "Some declared card losses did not execute.");
        Require(result.Events.GetValueOrDefault("CredentialRecoveryCompleted") == result.Events.GetValueOrDefault("CardLost"), "Lost credentials did not all recover.");
        Require(s.Actors.All(a => CredentialPlanning.KnownUsableCredential(world.ActorView(new(a.Id)))), "Final usable credential missing.");
        Require(result.Events.GetValueOrDefault("MachineFaulted") == scenario.Scenario.Disturbances.Count(d => d.Type == "MachineFault"), "Some declared fault disturbances did not execute.");
        Require(result.Events.GetValueOrDefault("MissingWorkReportOpened") > 0, "Overdue work was not exercised.");
        result.SharedGuardDuties = world.InspectPatrolOrders().Any(p => world.InspectGuardAssignments().Any(g => g.ActorId == p.ActorId));
        Require(result.SharedGuardDuties, "No guard performed both patrol and escort duties.");
        foreach (long subject in custodySubjects)
        {
            long release = stays.Where(st => st.ActorId == subject && st.Status == DetentionStayStatus.Released).Select(st => st.ReleaseTick).DefaultIfEmpty(long.MaxValue).Max();
            bool worked = lastWork.GetValueOrDefault(subject) > release;
            bool returnedIdle = lastCheckIn.GetValueOrDefault(subject) > release && lastOnDuty.GetValueOrDefault(subject) > release &&
                world.InspectLocalOrders(new(subject)).All(o => o.Status is LocalOrderStatus.Completed or LocalOrderStatus.Cancelled or LocalOrderStatus.Expired or LocalOrderStatus.Rejected);
            if (worked) result.ReleasedSubjectsResumingWork++;
            else if (returnedIdle) result.ReleasedSubjectsReturningIdle++;
            Require(worked || returnedIdle, "Subject " + subject + " did not return to ordinary work/duty after release.");
        }
        Require(result.ReleasedSubjectsResumingWork > 0, "No released subject resumed an available work task.");
        foreach (var c in result.Participation.Citizens)
        {
            Require(c.Meals.All(n => n > 0) && c.SleepTicks.All(n => n > 0) && c.Rests > 0 && c.Toilets > 0, "Citizen " + c.Actor + " missed daily meals/sleep or basic routines.");
            Require(c.FailedActions < 100, "Citizen " + c.Actor + " exceeded the individual retry bound (100 failed actions).");
        }
        Require(result.Participation.ScheduledRestsOverdue == 0, "Scheduled rests remained overdue.");
        int days = (int)(s.Horizon / StationBaseline.Day);
        result.CitizensWithContinuingShifts = s.Actors.Count(a => onDuty.GetValueOrDefault(a.Id) >= days && offDuty.GetValueOrDefault(a.Id) >= days);
        Require(result.CitizensWithContinuingShifts == s.Actors.Count, "Some citizens stopped cycling through known duty and off-duty periods.");
        Require(result.DuplicateActiveAssignments == 0, "A job had duplicate active assignments.");
        Require(failures < s.Actors.Count * 100L, "Retry storm exceeded 100 failed actions per citizen.");
        bool Drained(Phase p) => p.Dirt == 0 && p.BrokenMachines == 0 && p.OpenFacilityTasks == 0 && p.UnfinishedJobs == 0 && p.MissingResponses == 0 && p.ActiveDispatches == 0 && p.Occupants == 0 && p.PendingCases == 0;
        var recovered = result.Phases.FindIndex(p => p.Tick >= PressureEnd && Drained(p));
        if (recovered >= 0 && result.Phases.Skip(recovered).All(Drained)) result.RecoveryTick = result.Phases[recovered].Tick;
        Require(result.RecoveryTick >= 0 && s.Horizon - result.RecoveryTick >= StationBaseline.Day, "Backlogs did not drain and remain drained for a configured day.");
        Require(world.InspectDetentionFacilities().All(f => f.Occupants == 0 && f.Reservations == 0), "Custody capacity leaked.");
        Require(world.InspectRepairAgents().All(a => a.PendingMachines.Count == 0), "A repair agent retained unfinished work after recovery.");
        result.TerminalQueuesRecovered = terminalLastEmpty.Values.All(t => t >= s.Horizon - StationBaseline.Day);
        result.NeedQueuesRecovered = needLastEmpty.Values.All(t => t >= s.Horizon - StationBaseline.Day);
        Require(result.TerminalQueuesRecovered && result.NeedQueuesRecovered, "A service queue never drained during the final observation day.");
        Require(world.InspectConstructionProjects().Count == 0 && world.Topology.InspectTiles().Count == s.Tiles.Count, "Fixed layout changed.");
        result.ContinuationTraceHash = Convert.ToHexString(originalHash.GetHashAndReset()).ToLowerInvariant();
        if (output != null)
            File.WriteAllText(Path.Combine(output, "pressure-primary.json"), JsonData.Write(new {
                status = result.Failures.Count == 0 ? "awaiting_replay" : "assertion_failed",
                result.Failures, result.Reprimands, result.Released, result.RecoveryTick, result.CaseOutcomes, result.SubjectsRequiringCustody,
                stateDigest = result.Participation.Digest, result.ContinuationTraceHash
            }));
        if (checkpoint != null)
        {
            if (output != null) File.WriteAllBytes(Path.Combine(output, "pressure-checkpoint.bin"), checkpoint);
            var timer = Stopwatch.StartNew(); var restored = World.Restore(checkpoint, traceEnabled: true); restored.ClearRecordedTrace();
            using var replayHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long nextReplayProgress = checkpointTick + 6000;
            for (long tick = Math.Min(checkpointTick + 997, s.Horizon); ; tick = Math.Min(tick + 997, s.Horizon))
            {
                restored.AdvanceTo(tick); restored.AssertAccounting();
                foreach (var e in restored.Trace) Hash(replayHash, e);
                restored.ClearRecordedTrace();
                if (tick >= nextReplayProgress || tick == s.Horizon)
                {
                    Console.Error.WriteLine(JsonData.Write(new { replayTick = tick, horizon = s.Horizon }));
                    nextReplayProgress += 6000;
                }
                if (tick == s.Horizon) break;
            }
            result.CheckpointMatches = restored.Digest() == world.Digest();
            result.ContinuationTraceMatches = Convert.ToHexString(replayHash.GetHashAndReset()).ToLowerInvariant() == result.ContinuationTraceHash;
            result.ReplaySeconds = timer.Elapsed.TotalSeconds;
        }
        Require(result.CheckpointMatches && result.ContinuationTraceMatches, "Checkpoint state or exact continuation trace differs.");
        if (result.Failures.Count > 0) result.Status = "assertion_failed";
        if (output != null)
        {
            File.WriteAllText(Path.Combine(output, "response-times.json"), JsonData.Write(result.ResponseTimes));
            var summary = result.ResponseTimes; result.ResponseTimes = new ResponseTimes.Report { Limits = summary!.Limits, Stages = summary.Stages, Unresolved = summary.Unresolved, MissCauses = summary.MissCauses,
                GuardTeamGapSamples = summary.GuardTeamGapSamples, Shifts = summary.Shifts, Incidents = summary.Incidents, MissedIncidents = summary.MissedIncidents };
            File.WriteAllText(Path.Combine(output, "pressure-result.json"), JsonData.Write(result));
            File.WriteAllText(Path.Combine(output, "institutions.json"), JsonData.Write(new { jobs = world.InspectJobs(), attempts = world.InspectWorkAttempts(), facility = world.InspectFacilityTasks(), incidents = world.InspectMissingReports(), patrols = world.InspectPatrolOrders(),
                reprimands = world.InspectReprimands(), authorizations = world.InspectDetentionAuthorizations(), dispatches = world.InspectEscortDispatches(), stays, guards = world.InspectGuardAssignments(), recoveries = world.InspectCredentialRecoveryCases(),
                quality = world.InspectQualityAssurance(), cases = world.InspectCases(),
                filings = s.Actors.Where(a => a.Role == "supervisor").SelectMany(a => world.InspectCaseFilings(new(a.Id))),
                audits = s.Actors.Where(a => a.Role == "supervisor").SelectMany(a => world.InspectAudits(new(a.Id))), repairs = world.InspectRepairAgents(), agents = world.InspectAgents() }));
        }
        return result;
    }
}
