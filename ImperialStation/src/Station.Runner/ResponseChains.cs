using System.Text.Json.Serialization;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public enum ResponseTriggerKind { InitialCondition, Event }
public sealed class ResponseEventData
{
    [JsonRequired] public string Type { get; set; } = "";
    public long? Tick { get; set; }
    public long? ActorId { get; set; }
    public long? ItemId { get; set; }
    public string? Detail { get; set; }
    public bool Matches(TraceEvent e) => e.Type == Type && (!Tick.HasValue || e.Tick == Tick) &&
        (!ActorId.HasValue || e.ActorId == ActorId) && (!ItemId.HasValue || e.ItemId == ItemId) && (Detail == null || e.Detail == Detail);
}
public sealed class ResponseTriggerCommandData
{
    [JsonRequired] public long Tick { get; set; }
    [JsonRequired] public ActionKind Kind { get; set; }
}
public sealed class ResponseChainRoleData
{
    [JsonRequired] public string Name { get; set; } = "";
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public ResponseTriggerKind TriggerKind { get; set; }
    public ResponseEventData? Trigger { get; set; }
    public List<ResponseTriggerCommandData> AllowedTriggerCommands { get; set; } = new();
    [JsonRequired] public List<ResponseEventData> RequiredEvents { get; set; } = new();
}
public sealed class ResponseChainResult
{
    public string Role { get; set; } = "";
    public long ActorId { get; set; }
    public long RootEventId { get; set; }
    public long ActionsChecked { get; set; }
    public List<long> TransitionEventIds { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public bool Passed => Failures.Count == 0;
}
public static class ResponseChains
{
    public static void Validate(ScenarioData scenario, string source)
    {
        var initial = scenario.InitialState; var roles = initial.ResponseChainRoles;
        void Require(bool ok, string path, string reason) { if (!ok) throw new ScenarioException(source, "$.initialState.responseChainRoles" + path, reason); }
        Require(roles.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count() == roles.Count, "", "role names must be unique");
        for (int i = 0; i < roles.Count; i++)
        {
            var r = roles[i]; string path = "[" + i + "]";
            Require(!string.IsNullOrWhiteSpace(r.Name) && initial.Actors.Any(a => a.Id == r.ActorId), path, "named role must identify a registered actor");
            Require(Enum.IsDefined(r.TriggerKind) && (r.TriggerKind == ResponseTriggerKind.Event) == (r.Trigger != null), path, "event triggers require a selector; initial conditions do not");
            Require(r.RequiredEvents.Count > 0, path, "at least one required transition prevents a vacuous proof");
            foreach (var e in r.RequiredEvents.Concat(r.Trigger == null ? Array.Empty<ResponseEventData>() : new[] { r.Trigger }))
                Require(!string.IsNullOrWhiteSpace(e.Type) && (!e.Tick.HasValue || e.Tick >= 0 && e.Tick <= initial.Horizon) &&
                    (!e.ActorId.HasValue || e.ActorId == 0 || initial.Actors.Any(a => a.Id == e.ActorId)) && (!e.ItemId.HasValue || e.ItemId >= 0), path, "invalid event selector");
            Require(r.AllowedTriggerCommands.Select(c => (c.Tick, c.Kind)).Distinct().Count() == r.AllowedTriggerCommands.Count, path, "duplicate trigger command allowance");
            foreach (var c in r.AllowedTriggerCommands)
            {
                long triggerTick = r.TriggerKind == ResponseTriggerKind.InitialCondition ? 0 : r.Trigger!.Tick ?? -1;
                Require(c.Tick >= 0 && c.Tick <= triggerTick && Enum.IsDefined(c.Kind), path, "allowed commands must be bounded by an explicit trigger tick");
                Require(scenario.ScriptedActors.Where(a => a.ActorId == r.ActorId).SelectMany(a => a.Commands).Count(a => a.Tick == c.Tick && a.Kind == c.Kind) == 1,
                    path, "allowance must identify exactly one declared trigger command");
            }
        }
    }

    public static IReadOnlyList<ResponseChainResult> Check(IReadOnlyList<TraceEvent> events, IReadOnlyList<ResponseChainRoleData> roles, long startTick)
    {
        var results = roles.Select(r => new ResponseChainResult { Role = r.Name, ActorId = r.ActorId }).ToArray();
        if (roles.Count == 0) return results;
        if (startTick >= 0)
        { foreach (var r in results) r.Failures.Add("Complete causal history is unavailable from a continuation checkpoint; replay from the initial state."); return results; }
        var ordered = events.OrderBy(e => e.Id).ToArray();
        if (ordered.Any(e => e.Id <= 0 || !Enum.IsDefined(e.Origin)) || ordered.Select(e => e.Id).Distinct().Count() != ordered.Length)
        { foreach (var r in results) r.Failures.Add("Trace event identities are invalid or duplicated."); return results; }
        var byId = ordered.ToDictionary(e => e.Id); var parents = ordered.ToDictionary(e => e.Id, _ => new SortedSet<long>());
        var lastAction = new Dictionary<long, long>(); var lastMessage = new Dictionary<long, long>();
        foreach (var e in ordered)
        {
            if (e.CauseId < 0 || e.CauseId >= e.Id || e.CauseId > 0 && !byId.ContainsKey(e.CauseId))
            { foreach (var r in results) r.Failures.Add("Invalid or missing cause for event " + e.Id); }
            else if (e.CauseId > 0) parents[e.Id].Add(e.CauseId);
            if (e.ActionId > 0) { if (lastAction.TryGetValue(e.ActionId, out long prior)) parents[e.Id].Add(prior); lastAction[e.ActionId] = e.Id; }
            if (e.MessageId > 0) { if (lastMessage.TryGetValue(e.MessageId, out long prior)) parents[e.Id].Add(prior); lastMessage[e.MessageId] = e.Id; }
        }
        if (results.Any(r => !r.Passed)) return results;
        bool PermittedTrigger(TraceEvent e) => roles.Any(r => r.ActorId == e.ActorId && r.AllowedTriggerCommands.Any(c => c.Tick == e.Tick && c.Kind.ToString() == e.Detail));
        // Propagate forbidden external roots once in causal order. This also catches
        // scripted helpers outside the declared response role when they cause it.
        var contamination = new Dictionary<long, long>();
        foreach (var e in ordered)
        {
            long bad = e.Type == "ActionRequested" && e.Origin == ActionOrigin.ExternalCommand && !PermittedTrigger(e) ? e.Id : 0;
            if (bad == 0) bad = parents[e.Id].Select(p => contamination[p]).FirstOrDefault(p => p > 0);
            contamination[e.Id] = bad;
        }
        for (int i = 0; i < roles.Count; i++)
        {
            var role = roles[i]; var result = results[i]; long root = 0;
            foreach (var allowance in role.AllowedTriggerCommands)
                if (ordered.Count(e => e.Type == "ActionRequested" && e.Origin == ActionOrigin.ExternalCommand && e.ActorId == role.ActorId && e.Tick == allowance.Tick && e.Detail == allowance.Kind.ToString()) != 1)
                    result.Failures.Add("Trigger allowance must match exactly one executed command at tick " + allowance.Tick);
            if (role.TriggerKind == ResponseTriggerKind.Event)
            {
                var roots = ordered.Where(role.Trigger!.Matches).ToArray();
                if (roots.Length != 1) { result.Failures.Add("Trigger must match exactly one event; matched " + roots.Length); continue; }
                root = roots[0].Id; result.RootEventId = root;
            }
            var reached = new HashSet<long>();
            foreach (var e in ordered) if (root == 0 || e.Id == root || parents[e.Id].Any(reached.Contains)) reached.Add(e.Id);
            foreach (var action in ordered.Where(e => e.Type == "ActionRequested" && e.ActorId == role.ActorId))
            {
                result.ActionsChecked++;
                if (action.Origin == ActionOrigin.None || action.Origin != ActionOrigin.ExternalCommand && action.CauseId == 0)
                    result.Failures.Add("Unproven action origin at event " + action.Id);
                if (contamination[action.Id] > 0) result.Failures.Add("Scripted response action/ancestor " + contamination[action.Id] + " reaches action " + action.Id);
                if (action.Origin == ActionOrigin.SystemProtocol)
                {
                    string expected = action.Detail switch { "Move" => "CustodyBegan", "ReleaseDetention" => "DetentionReleaseDue", "Converse" => "ConversationConsented", _ => "" };
                    if (expected.Length == 0 || !byId.TryGetValue(action.CauseId, out var protocol) || protocol.Type != expected)
                        result.Failures.Add("Missing system protocol cause at action " + action.Id);
                }
            }
            var requested = ordered.Where(e => e.Type == "ActionRequested" && e.ActorId == role.ActorId).Select(e => e.ActionId).ToHashSet();
            foreach (var execution in ordered.Where(e => e.ActorId == role.ActorId && e.Type is "ActionStarted" or "ActionCompleted" or "ActionFailed"))
                if (execution.ActionId <= 0 || !requested.Contains(execution.ActionId)) result.Failures.Add("Execution lacks an origin request at event " + execution.Id);
            long previous = root;
            foreach (var selector in role.RequiredEvents)
            {
                var found = ordered.FirstOrDefault(e => e.Id > previous && reached.Contains(e.Id) && selector.Matches(e));
                if (found == null) { result.Failures.Add("Missing ordered causal transition: " + selector.Type + (selector.Detail == null ? "" : "/" + selector.Detail)); continue; }
                result.TransitionEventIds.Add(found.Id); previous = found.Id;
                if (contamination[found.Id] > 0) result.Failures.Add("Scripted ancestor " + contamination[found.Id] + " reaches transition " + found.Id);
            }
        }
        return results;
    }
}
