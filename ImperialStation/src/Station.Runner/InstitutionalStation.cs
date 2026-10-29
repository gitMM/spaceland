using System.Diagnostics;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public static partial class InstitutionalStation
{
    public sealed class Result
    {
        public string Status { get; set; } = "completed";
        public string Variant { get; set; } = "";
        public StationBaseline.Result Participation { get; set; } = new();
        public SortedDictionary<string, long> Events { get; set; } = new(StringComparer.Ordinal);
        public long InvalidActionCauses { get; set; }
        public long TraceGaps { get; set; }
        public int Reprimands { get; set; }
        public int Authorizations { get; set; }
        public int Released { get; set; }
        public long TargetWorkAfterRelease { get; set; }
        public long CustodyNeedCompletions { get; set; }
        public bool WaitedForCapacity { get; set; }
        public bool WaitedForGuards { get; set; }
        public long QueriesDuringOutage { get; set; }
        public long CheckpointTick { get; set; }
        public bool CheckpointMatches { get; set; }
        public double ReplaySeconds { get; set; }
        public List<string> Failures { get; set; } = new();
    }
    public static Result Execute(LoadedScenario scenario, string variant = "normal", string? output = null)
    {
        var result = new Result { Variant = variant }; World? final = null; byte[]? checkpoint = null;
        long previous = 0; var targetEvents = new List<TraceEvent>();
        var protocolCauses = new Dictionary<long, string>();
        var triggers = scenario.Scenario.ScriptedActors.SelectMany(a => a.Commands.Select(c => (Actor: a.ActorId, c.Tick, Detail: c.Kind.ToString()))).ToHashSet();
        bool IsTrigger(TraceEvent e) => e.Origin == ActionOrigin.ExternalCommand && triggers.Contains((e.ActorId, e.Tick, e.Detail));
        if (output != null) Directory.CreateDirectory(output);
        using var trace = output == null ? null : new StreamWriter(Path.Combine(output, "institutional-trace.jsonl"));
        result.Participation = StationBaseline.Execute(scenario, output, requireDaily: true, requiresPhysicalWork: id => id > 6, permittedExternalTrigger: IsTrigger, observe: (w, tick) =>
        {
            final = w;
            foreach (var e in w.Trace)
            {
                if (e.Id != previous + 1) result.TraceGaps++; previous = e.Id;
                result.Events[e.Type] = result.Events.GetValueOrDefault(e.Type) + 1;
                if (e.Type is "CustodyBegan" or "DetentionReleaseDue" or "ConversationConsented") protocolCauses.Add(e.Id, e.Type);
                if (e.Type == "ActionRequested" && !IsTrigger(e) && (e.CauseId <= 0 || e.CauseId >= e.Id || e.Origin is not (ActionOrigin.Autonomous or ActionOrigin.SystemProtocol))) result.InvalidActionCauses++;
                if (e.Type == "ActionRequested" && e.Origin == ActionOrigin.SystemProtocol && (!protocolCauses.TryGetValue(e.CauseId, out var protocol) ||
                    protocol != (e.Detail switch { "Move" => "CustodyBegan", "ReleaseDetention" => "DetentionReleaseDue", "Converse" => "ConversationConsented", _ => "" }))) result.InvalidActionCauses++;
                if (variant == "terminal-outage" && e.Type == "RecordsQueried" && e.Tick < 8000) result.QueriesDuringOutage++;
                if (e.ActorId == 1) targetEvents.Add(e);
                if (e.ActorId <= 6) trace?.WriteLine(Runs.EventJson(e));
            }
            var stays = w.InspectDetentionStays();
            if (w.InspectDetentionFacilities().Any(f => f.Occupants == f.Definition.Capacity) && w.InspectEscorts().Any(e => e.Status == EscortStatus.AwaitingIntake)) result.WaitedForCapacity = true;
            if (w.InspectEscortDispatches().Any(d => d.Status == EscortDispatchStatus.AwaitingGuards && d.Reason == "InsufficientKnownOnDutyUncommittedGuards")) result.WaitedForGuards = true;
            if (checkpoint == null && (variant == "interrupted-escort" ? w.InspectEscorts().Any(e => e.Status == EscortStatus.Interrupted) : stays.Any(s => s.Status == DetentionStayStatus.Detained) || tick == 36000))
            { checkpoint = w.Serialize(); result.CheckpointTick = tick; }
            if (tick % 6000 == 0) Console.Error.WriteLine(JsonData.Write(new { tick, reprimands = w.InspectReprimands().Count, audits = w.InspectAudits(new(2)).Select(a => a.Status), dispatches = w.InspectEscortDispatches().Select(d => new { d.Status, d.Reason }), stays = stays.Select(s => s.Status) }));
        });
        var world = final!; result.Failures.AddRange(result.Participation.Failures);
        void Require(bool condition, string failure) { if (!condition) result.Failures.Add(failure); }
        result.Reprimands = world.InspectReprimands().Count;
        result.Authorizations = world.InspectDetentionAuthorizations().Count;
        var stays = world.InspectDetentionStays(); result.Released = stays.Count(s => s.Status == DetentionStayStatus.Released);
        var release = stays.Where(s => s.ActorId == 1 && s.Status == DetentionStayStatus.Released).Select(s => s.ReleaseTick).DefaultIfEmpty(-1).Max();
        result.TargetWorkAfterRelease = release < 0 ? 0 : targetEvents.Count(e => e.Tick > release && e.Type == "CleaningCompleted" && e.Quantity > 0);
        result.CustodyNeedCompletions = world.InspectNeedActivities().Count(a => a.ActorId == 1 && a.Status == NeedActivityStatus.Completed && stays.Any(s => s.ActorId == 1 && a.StartedTick >= s.IntakeTick && s.IntakeTick >= 0 && a.EndedTick <= s.ReleaseTick));
        int cases = variant == "correct" ? 0 : variant == "single-case" ? 1 : variant == "full-capacity" ? 4 : 2;
        Require(result.Reprimands == cases, "Expected exactly " + cases + " distinct reprimands.");
        Require(result.InvalidActionCauses == 0 && result.TraceGaps == 0, "Response provenance or trace continuity failed.");
        Require(result.Participation.ExternalActions == triggers.Count, "Misconduct trigger command count differs from its declaration.");
        if (cases < 2) Require(result.Authorizations == 0 && stays.Count == 0, "Negative control created detention.");
        else
        {
            Require(result.Authorizations == cases / 2 && result.Released == cases / 2, "Institutional chain did not reach the expected authorized releases.");
            Require(stays.Select(s => s.ActorId).OrderBy(id => id).SequenceEqual(variant == "full-capacity" ? new long[] { 1, 7 } : new long[] { 1 }), "Custody subjects differ from the attributable workers.");
            Require(world.InspectEscortDispatches().All(d => d.Status == EscortDispatchStatus.Released), "Release receipt did not reach the authority.");
            Require(result.TargetWorkAfterRelease > 0, "Autonomous target did not resume physical work after release.");
            Require(result.CustodyNeedCompletions > 0, "No real need service completed during custody.");
            Require(world.InspectAudits(new(2)).Count(a => a.Evidence.Count > 0) == cases, "Supervisor lacks the expected audits with terminal-disclosed evidence.");
            Require(world.InspectDetentionFacilities().All(f => f.Occupants == 0 && f.Reservations == 0), "Detention capacity was not released.");
        }
        if (variant == "interrupted-escort")
        {
            Require(result.Events.GetValueOrDefault("EscortInterrupted") > 0 && result.Events.GetValueOrDefault("EscortReplacementRequested") > 0, "Interruption did not produce received reports and replacement.");
            Require(world.InspectGuardAssignments().Select(a => a.ActorId).Distinct().Count() == 4, "Replacement did not use a different guard team.");
        }
        if (variant == "full-capacity")
        {
            Require(result.WaitedForCapacity, "Full-capacity control never waited behind a real occupant.");
            var admitted = stays.OrderBy(s => s.IntakeTick).ToArray();
            Require(admitted.Length == 2 && admitted[1].IntakeTick >= admitted[0].ReleaseTick, "Full facility admitted overlapping occupants.");
        }
        if (variant == "guards-unavailable") Require(result.WaitedForGuards, "Unavailable-guard control never recorded the staffing wait.");
        if (variant == "terminal-outage") Require(result.QueriesDuringOutage == 0, "An offline records terminal disclosed audit evidence.");
        if (variant == "intake-outage") Require(stays.All(s => s.IntakeStarted >= 8000 && s.IntakeTick >= s.IntakeStarted + 100), "Intake bypassed service restoration or its finite duration.");
        if (checkpoint != null)
        {
            var timer = Stopwatch.StartNew(); var restored = World.Restore(checkpoint);
            for (long tick = result.CheckpointTick + 997; tick < scenario.Scenario.InitialState.Horizon; tick += 997) restored.AdvanceTo(tick);
            restored.AdvanceTo(scenario.Scenario.InitialState.Horizon); result.CheckpointMatches = restored.Digest() == world.Digest(); result.ReplaySeconds = timer.Elapsed.TotalSeconds;
            Require(result.CheckpointMatches, "Institutional checkpoint continuation differs.");
        }
        else Require(false, "No checkpoint captured.");
        if (result.Failures.Count > 0) result.Status = "assertion_failed";
        if (output != null)
        {
            if (checkpoint != null) File.WriteAllBytes(Path.Combine(output, "institutional-checkpoint.bin"), checkpoint);
            File.WriteAllText(Path.Combine(output, "institutional-result.json"), JsonData.Write(result));
            File.WriteAllText(Path.Combine(output, "institutions.json"), JsonData.Write(new { audits = world.InspectAudits(new(2)), reprimands = world.InspectReprimands(), authorizations = world.InspectDetentionAuthorizations(), dispatches = world.InspectEscortDispatches(), stays,
                guards = world.InspectGuardAssignments(), outcomes = world.InspectGuardOutcomes(true), activities = world.InspectNeedActivities().Where(a => a.ActorId <= 6) }));
        }
        return result;
    }
}
