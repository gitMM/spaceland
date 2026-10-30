using Station.Contracts;
using Station.Core;

namespace Station.Runner;

// Response-time measurement for the pressure workload. It reads only the recorded
// trace and final institutional snapshots after the fact; nothing here feeds back
// into simulation decisions. Limits are fixture-specific assessment targets for the
// shortened 36,000-tick day (one configured hour = 1,500 ticks), declared before
// any fix is judged. Missing a target is reported with its cause; it is not a gate.
public static partial class PressureStation
{
    public const long ConfiguredHour = StationBaseline.Day / 24;
    public static readonly IReadOnlyDictionary<string, long> ResponseTargets = new SortedDictionary<string, long>(StringComparer.Ordinal)
    {
        ["case/Detection"] = 3 * ConfiguredHour, ["case/Decision"] = ConfiguredHour,
        ["credential/Detection"] = ConfiguredHour, ["credential/Resolution"] = 2 * ConfiguredHour,
        ["escort/Dispatch"] = ConfiguredHour, ["escort/Acknowledgment"] = 2 * ConfiguredHour, ["escort/Arrival"] = 2 * ConfiguredHour, ["escort/Resolution"] = ConfiguredHour,
        ["facility/Detection"] = 2 * ConfiguredHour, ["facility/Reporting"] = ConfiguredHour, ["facility/Dispatch"] = ConfiguredHour,
        ["facility/Acknowledgment"] = 2 * ConfiguredHour, ["facility/Arrival"] = ConfiguredHour, ["facility/Resolution"] = 2 * ConfiguredHour,
        ["patrol/Detection"] = 2 * ConfiguredHour, ["patrol/Dispatch"] = ConfiguredHour, ["patrol/Acknowledgment"] = 2 * ConfiguredHour,
        ["patrol/Arrival"] = ConfiguredHour, ["patrol/Resolution"] = 2 * ConfiguredHour,
    };
    // Detection and reporting are separated from the institutional response (dispatch onward).
    private static readonly HashSet<string> DetectionStages = new(StringComparer.Ordinal) { "Detection", "Reporting" };
    public sealed class StageStatistics
    {
        public string Chain { get; set; } = "";
        public string Stage { get; set; } = "";
        public string Kind { get; set; } = "";
        public long Target { get; set; } = -1;
        public int Completed { get; set; }
        public long Median { get; set; } = -1;
        public long P95 { get; set; } = -1;
        public long Maximum { get; set; } = -1;
        public int Unresolved { get; set; }
        public long OldestUnresolvedAge { get; set; } = -1;
        public int MissedTarget { get; set; }
        public SortedDictionary<string, int> MissCauses { get; set; } = new(StringComparer.Ordinal);
    }
    public sealed class ResponseStage
    {
        public string Stage { get; set; } = "";
        public long Start { get; set; } = -1;
        public long End { get; set; } = -1;
        public long Duration => Start < 0 || End < 0 ? -1 : End - Start;
        public string Cause { get; set; } = "";
    }
    public sealed class ResponseChain
    {
        public string Chain { get; set; } = "";
        public string Key { get; set; } = "";
        public long Incident { get; set; } = -1;
        public int IncidentCohort { get; set; } = -1;
        public IReadOnlyList<long> Responders { get; set; } = Array.Empty<long>();
        public int Reassignments { get; set; }
        public List<ResponseStage> Stages { get; set; } = new();
    }
    public sealed class ResponseReport
    {
        public IReadOnlyDictionary<string, long> Targets { get; set; } = ResponseTargets;
        public List<StageStatistics> Stages { get; set; } = new();
        public int Chains { get; set; }
        public int MissedTargets { get; set; }
        public int UnresolvedStages { get; set; }
        public SortedDictionary<string, int> MissCauses { get; set; } = new(StringComparer.Ordinal);
        // Every chain with a missed target or unresolved stage, then a bounded sample.
        public List<ResponseChain> Exceptions { get; set; } = new();
        public int ExceptionsOmitted { get; set; }
    }

    // Escort-team coverage per 64-citizen group, sampled every 100 ticks from published
    // rosters plus recorded need activities and escort/patrol commitments.
    public sealed class GuardCoverage
    {
        public int Group { get; set; }
        public SortedDictionary<int, int> GuardsPerCohort { get; set; } = new();
        public int Samples { get; set; }
        public int MinimumOnDuty { get; set; } = int.MaxValue;
        public int MinimumAvailable { get; set; } = int.MaxValue;
        public int SamplesWithoutTeam { get; set; }
        public int SamplesWithoutTeamFromRoster { get; set; }
        public int SamplesWithoutTeamFromNeeds { get; set; }
        public int SamplesWithoutTeamFromCommitments { get; set; }
    }
    internal sealed class ResponseTracker
    {
        private readonly long horizon, day, duty, shift; private readonly int guardCount;
        private readonly Dictionary<long, int> cohort = new();
        private readonly Dictionary<long, string> role = new();
        private readonly Dictionary<long, int> district = new();
        private readonly Dictionary<long, Coord> tilePosition = new();
        private readonly HashSet<long> publicTerminals;
        // Message transport.
        private readonly Dictionary<long, long> posted = new(), delivered = new(), read = new();
        // Work attempts.
        private readonly Dictionary<long, long> orderTick = new(), orderMessage = new(), ackPosted = new(), ackReceived = new(), workStart = new(), workDone = new();
        private readonly Dictionary<long, List<long>> jobAttempts = new();
        private readonly Dictionary<long, long> facilityCreated = new();
        // Facility subjects: effective incidents and queued local reports.
        private readonly Dictionary<string, List<long>> incidents = new(StringComparer.Ordinal), reports = new(StringComparer.Ordinal);
        // Escort authorizations.
        private readonly Dictionary<long, long> authorized = new(), dispatched = new(), custody = new(), detained = new();
        private readonly Dictionary<long, int> dispatchCount = new();
        private readonly Dictionary<long, long[]> teams = new();
        private readonly Dictionary<long, List<long>> teamAcks = new();
        private readonly Dictionary<long, long> orderAuthorization = new();
        // Patrols and overdue work.
        private readonly Dictionary<long, long> incidentOpened = new(), incidentAttempt = new(), patrolInspected = new();
        private readonly Dictionary<long, List<long>> expectationDeadlines = new();
        // Cases and credentials.
        private readonly Dictionary<long, long> inspected = new(), decided = new(), misconduct = new();
        private readonly List<(long Actor, long Lost, long Needed, long Completed)> credentials = new();
        // Responder state used only to classify missed targets.
        private readonly Dictionary<long, List<(long Tick, bool Asleep)>> sleep = new();
        private readonly Dictionary<long, List<(long Tick, bool Connected)>> terminals = new();
        private readonly Dictionary<long, List<(long Start, long End)>> guardNeeds = new(), guardCommitments = new();
        private readonly Dictionary<long, long> needSince = new(), patrolSince = new();

        internal ResponseTracker(LoadedScenario scenario)
        {
            var s = scenario.Scenario.InitialState; horizon = s.Horizon;
            var routines = s.Routines!.Configuration; day = routines.DayTicks; duty = routines.DutyTicks; shift = routines.DayTicks / routines.Cohorts;
            guardCount = s.Detention?.Escort?.GuardCount ?? 2;
            foreach (var r in s.Routines.Assignments) cohort[r.ActorId] = r.Cohort;
            foreach (var a in s.Actors) { role[a.Id] = a.Role; district[a.Id] = a.Position.X / 3; }
            foreach (var t in s.Tiles) tilePosition[t.Id] = t.Position.ToCoord();
            publicTerminals = s.Information!.Terminals.Where(t => t.Id < 10000).Select(t => t.Id).ToHashSet();
            foreach (var c in scenario.Scenario.ScriptedActors.SelectMany(a => a.Commands).Where(c => c.Kind == ActionKind.Install)) misconduct.TryAdd(c.ServiceId, c.Tick);
        }
        private static void First(Dictionary<long, long> map, long key, long tick) { if (key != 0) map.TryAdd(key, tick); }
        private static void Add<TKey>(Dictionary<TKey, List<long>> map, TKey key, long value) where TKey : notnull
        { if (!map.TryGetValue(key, out var list)) map[key] = list = new(); list.Add(value); }
        private static long Field(string detail, string name)
        {
            foreach (var part in detail.Split(';', StringSplitOptions.TrimEntries))
                if (part.StartsWith(name + "=", StringComparison.Ordinal) && long.TryParse(part.AsSpan(name.Length + 1), out long value)) return value;
            return 0;
        }
        private bool IsGuard(long actor) => role.TryGetValue(actor, out var r) && r == "guard";
        private static void AddInterval(Dictionary<long, List<(long, long)>> map, long actor, long start, long end)
        { if (!map.TryGetValue(actor, out var list)) map[actor] = list = new(); list.Add((start, end)); }
        private static string Subject(FactKey? key) => key.HasValue ? key.Value.SubjectKind + ":" + key.Value.SubjectId : "";

        internal void Observe(TraceEvent e)
        {
            switch (e.Type)
            {
                case "AuthorityOrderPosted":
                    First(posted, e.MessageId, e.Tick);
                    if (e.Detail.StartsWith("Order;", StringComparison.Ordinal) && e.AttemptId > 0)
                    { orderTick.TryAdd(e.AttemptId, e.Tick); orderMessage.TryAdd(e.AttemptId, e.MessageId); if (!jobAttempts.TryGetValue(e.TaskId, out var l) || !l.Contains(e.AttemptId)) Add(jobAttempts, e.TaskId, e.AttemptId); }
                    break;
                case "MessagePosted":
                    First(posted, e.MessageId, e.Tick);
                    if (e.Detail is "PatrolFound" or "PatrolNotLocated" or "PatrolBlocked" && patrolSince.Remove(e.ActorId, out long patrol)) AddInterval(guardCommitments, e.ActorId, patrol, e.Tick);
                    break;
                case "MailboxDelivered": First(delivered, e.MessageId, e.Tick); break;
                case "MessageRead":
                    First(read, e.MessageId, e.Tick);
                    if (e.Detail == "PatrolOrder" && IsGuard(e.ActorId)) patrolSince.TryAdd(e.ActorId, e.Tick);
                    break;
                case "OrderAcknowledged": First(ackPosted, e.AttemptId, e.Tick); break;
                case "WorkReportReceived": First(ackReceived, e.AttemptId, e.Tick); break;
                case "ActionStarted":
                    if (e.AttemptId > 0 && e.Detail is not ("Move" or "Login" or "ReadInbox" or "PostMessage" or "QueryRecords")) First(workStart, e.AttemptId, e.Tick);
                    break;
                case "WorkExecutionCompletedPhysically": First(workDone, e.AttemptId, e.Tick); break;
                case "FacilityTaskCreated": First(facilityCreated, Field(e.Detail, "job"), e.Tick); break;
                case "FacilityReportQueued": Add(reports, Subject(e.FactKey), e.Tick); break;
                case "MachineFaulted": if (e.Quantity > 0) Add(incidents, Subject(e.FactKey), e.Tick); break;
                case "DirtAdded":
                    if (e.Detail == "Spill") foreach (var t in tilePosition.Where(t => t.Value == e.Target)) Add(incidents, "Tile:" + t.Key, e.Tick);
                    break;
                case "DetentionAuthorized": First(authorized, Field(e.Detail, "authorization"), e.Tick); break;
                case "EscortTeamDispatched":
                {
                    long auth = Field(e.Detail, "authorization"); First(dispatched, auth, e.Tick); dispatchCount[auth] = dispatchCount.GetValueOrDefault(auth) + 1;
                    var guards = e.Detail.Split(';', StringSplitOptions.TrimEntries).FirstOrDefault(p => p.StartsWith("guards=", StringComparison.Ordinal));
                    if (guards != null && !teams.ContainsKey(auth)) teams[auth] = guards["guards=".Length..].Split(',').Select(long.Parse).ToArray();
                    break;
                }
                case "GuardOrderRead": orderAuthorization.TryAdd(e.MessageId, Field(e.Detail, "authorization")); break;
                case "GuardAcknowledgmentReceived": if (e.Detail.EndsWith("Accepted", StringComparison.Ordinal)) Add(teamAcks, Field(e.Detail, "authorization"), e.Tick); break;
                case "CustodyBegan": First(custody, Field(e.Detail, "authorization"), e.Tick); break;
                case "Detained": First(detained, Field(e.Detail, "authorization"), e.Tick); break;
                case "ReportExpectationCreated": { long deadline = Field(e.Detail, "deadline"); if (deadline > 0) Add(expectationDeadlines, e.AttemptId, deadline); break; }
                case "MissingWorkReportOpened": { long id = Field(e.Detail, "incident"); First(incidentOpened, id, e.Tick); First(incidentAttempt, id, e.AttemptId); break; }
                case "PatrolCandidateInspected": First(patrolInspected, e.MessageId, e.Tick); break;
                case "VerificationInspected": First(inspected, e.AttemptId, e.Tick); break;
                case "ReprimandAccepted": First(decided, e.AttemptId, e.Tick); break;
                case "CardLost": credentials.Add((e.ActorId, e.Tick, -1, -1)); break;
                case "CredentialRecoveryNeeded":
                case "CredentialRecoveryCompleted":
                {
                    int i = credentials.FindLastIndex(c => c.Actor == e.ActorId);
                    if (i < 0) break; var c = credentials[i];
                    if (e.Type == "CredentialRecoveryNeeded" && c.Needed < 0) credentials[i] = c with { Needed = e.Tick };
                    if (e.Type == "CredentialRecoveryCompleted" && c.Completed < 0) credentials[i] = c with { Completed = e.Tick };
                    break;
                }
                case "NeedModeChanged":
                {
                    bool asleep = e.Detail == "Sleeping";
                    if (!sleep.TryGetValue(e.ActorId, out var list)) sleep[e.ActorId] = list = new();
                    if (list.Count == 0 ? asleep : list[^1].Asleep != asleep) list.Add((e.Tick, asleep));
                    break;
                }
                case "NeedServiceClaimed": if (IsGuard(e.ActorId)) needSince[e.ActorId] = e.Tick; break;
                case "NeedServiceReleased":
                    if (IsGuard(e.ActorId) && needSince.Remove(e.ActorId, out long since)) AddInterval(guardNeeds, e.ActorId, since, e.Tick);
                    break;
                case "TerminalStateChanged":
                    if (!terminals.TryGetValue(e.TerminalId, out var states)) terminals[e.TerminalId] = states = new();
                    states.Add((e.Tick, e.Detail.Contains("connected=True", StringComparison.Ordinal)));
                    break;
            }
        }

        private bool OnDuty(long actor, long tick) => cohort.TryGetValue(actor, out int c) && ((tick - c * shift) % day + day) % day < duty;
        private int CohortAt(long tick) => (int)(((tick % day) + day) % day / shift);
        private long Overlap(long start, long end, Func<long, bool> predicate, long step = 100)
        { long n = 0; for (long t = start; t < end; t += step) if (predicate(t)) n++; return n * step; }
        private bool Asleep(long actor, long tick)
        {
            if (!sleep.TryGetValue(actor, out var list)) return false;
            int lo = 0, hi = list.Count - 1, found = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (list[mid].Tick <= tick) { found = mid; lo = mid + 1; } else hi = mid - 1; }
            return found >= 0 && list[found].Asleep;
        }
        private List<(long Start, long End)>? outages;
        private bool PublicOutage(long tick)
        {
            outages ??= terminals.Where(t => publicTerminals.Contains(t.Key)).SelectMany(t => t.Value.Select((s, i) => (s, i, t.Value)))
                .Where(x => !x.s.Connected).Select(x => (x.s.Tick, x.i + 1 < x.Value.Count ? x.Value[x.i + 1].Tick : horizon)).ToList();
            return outages.Any(o => o.Start <= tick && tick < o.End);
        }
        private string Classify(string chain, ResponseStage stage, IReadOnlyList<long> responders, int incidentDistrict)
        {
            long start = stage.Start, end = stage.End >= 0 ? stage.End : horizon, span = Math.Max(1, end - start);
            string Boundary(string cause) => end - start > 0 && CohortAt(start) != CohortAt(end - 1) || end - start >= shift ? cause + "/AcrossShiftBoundary" : cause;
            if (stage.Stage == "Detection")
            {
                if (chain == "credential") return Boundary(responders.Count > 0 && Asleep(responders[0], start) ? "LossHolderAsleep" : responders.Count > 0 && !OnDuty(responders[0], start) ? "LossHolderOffDuty" : "LossNotYetNoticed");
                if (chain is "case" or "patrol") return Boundary("InstitutionalReviewDelay");
                bool observer = district.Any(d => d.Value == incidentDistrict && OnDuty(d.Key, start) && !Asleep(d.Key, start));
                return Boundary(observer ? "NoOnDutyObserverVisit" : "NoOnDutyObserverInDistrict");
            }
            if (stage.Stage == "Dispatch" && chain is "escort" or "patrol")
            {
                // Rosters are published, so the on-duty count uses no hidden state.
                int onDuty = role.Count(r => r.Value == "guard" && OnDuty(r.Key, start));
                return Boundary(onDuty < (chain == "escort" ? guardCount : 1) ? (chain == "escort" ? "NoOnDutyEscortTeam" : "NoOnDutyGuard") : "GuardsCommittedOrUnavailable");
            }
            // Facility dispatch selects only known on-duty, uncommitted qualified responders.
            if (stage.Stage == "Dispatch") return Boundary("NoUncommittedOnDutyResponder");
            if (responders.Count > 0)
            {
                long offDuty = responders.Max(r => Overlap(start, end, t => !OnDuty(r, t)));
                long asleep = responders.Max(r => Overlap(start, end, t => Asleep(r, t)));
                if (offDuty * 2 >= span) return Boundary("ResponderOffDuty");
                if (asleep * 2 >= span) return Boundary("ResponderAsleep");
            }
            if (stage.Stage is "Reporting" or "Acknowledgment" && Overlap(start, end, PublicOutage) * 4 >= span) return Boundary("TerminalOutage");
            return Boundary(stage.Stage == "Acknowledgment" ? "ResponderOccupiedOrUnread" : stage.Stage == "Resolution" ? "WorkOrCustodyDuration" : "ResponderOccupied");
        }

        private ResponseStage Stage(string name, long start, long end) => new() { Stage = name, Start = start, End = start < 0 ? -1 : end };
        private long Get(Dictionary<long, long> map, long key) => map.TryGetValue(key, out long v) ? v : -1;

        internal IReadOnlyList<GuardCoverage> Coverage()
        {
            const long step = 100; int samples = (int)(horizon / step) + 1;
            foreach (var (actor, since) in needSince) AddInterval(guardNeeds, actor, since, horizon);
            foreach (var (actor, since) in patrolSince) AddInterval(guardCommitments, actor, since, horizon);
            foreach (var (auth, start) in dispatched)
                foreach (long guard in teams.GetValueOrDefault(auth) ?? Array.Empty<long>()) AddInterval(guardCommitments, guard, start, Get(detained, auth) is var end && end >= 0 ? end : horizon);
            bool[] Timeline(Dictionary<long, List<(long Start, long End)>> map, long actor)
            {
                var busy = new bool[samples];
                foreach (var (start, end) in map.GetValueOrDefault(actor) ?? new List<(long, long)>())
                    for (long t = (start + step - 1) / step; t < samples && t * step < end; t++) busy[t] = true;
                return busy;
            }
            var result = new List<GuardCoverage>();
            foreach (var group in role.Where(r => r.Value == "guard").Select(r => r.Key).GroupBy(id => (int)((id - 1) / 64)).OrderBy(g => g.Key))
            {
                var guards = group.OrderBy(id => id).ToArray();
                var needs = guards.Select(g => Timeline(guardNeeds, g)).ToArray(); var commitments = guards.Select(g => Timeline(guardCommitments, g)).ToArray();
                var coverage = new GuardCoverage { Group = group.Key, Samples = samples };
                foreach (long g in guards) coverage.GuardsPerCohort[cohort.GetValueOrDefault(g)] = coverage.GuardsPerCohort.GetValueOrDefault(cohort.GetValueOrDefault(g)) + 1;
                for (int i = 0; i < samples; i++)
                {
                    int onDuty = 0, rested = 0, available = 0;
                    for (int k = 0; k < guards.Length; k++)
                    {
                        if (!OnDuty(guards[k], i * step)) continue; onDuty++;
                        if (needs[k][i]) continue; rested++;
                        if (!commitments[k][i]) available++;
                    }
                    coverage.MinimumOnDuty = Math.Min(coverage.MinimumOnDuty, onDuty); coverage.MinimumAvailable = Math.Min(coverage.MinimumAvailable, available);
                    if (available >= guardCount) continue;
                    coverage.SamplesWithoutTeam++;
                    if (onDuty < guardCount) coverage.SamplesWithoutTeamFromRoster++;
                    else if (rested < guardCount) coverage.SamplesWithoutTeamFromNeeds++;
                    else coverage.SamplesWithoutTeamFromCommitments++;
                }
                result.Add(coverage);
            }
            return result;
        }
        internal ResponseReport Finish(World world, IReadOnlyList<CaseOutcome> cases)
        {
            var chains = new List<ResponseChain>();
            var attempts = world.InspectWorkAttempts().ToDictionary(a => a.Id);
            long Resolution(long job) => jobAttempts.TryGetValue(job, out var list) ? list.Select(a => Get(workDone, a)).Where(t => t >= 0).DefaultIfEmpty(-1).Min() : -1;
            // Facility tasks, keyed by subject; earlier tasks bound the incident window.
            var lastResolved = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var task in world.InspectFacilityTasks().OrderBy(t => t.CreatedTick).ThenBy(t => t.JobId))
            {
                string subject = task.Subject.SubjectKind + ":" + task.Subject.SubjectId;
                long after = lastResolved.GetValueOrDefault(subject, -1), created = Get(facilityCreated, task.JobId) is var c && c >= 0 ? c : task.CreatedTick;
                long incident = incidents.TryGetValue(subject, out var hits) ? hits.Where(t => t > after && t <= created).DefaultIfEmpty(-1).Min() : -1;
                long discovery = reports.TryGetValue(subject, out var seen) ? seen.Where(t => t >= Math.Max(incident, after)).DefaultIfEmpty(-1).Min() : -1;
                var list = jobAttempts.GetValueOrDefault(task.JobId) ?? new List<long>();
                long first = list.Count > 0 ? list[0] : 0, msg = Get(orderMessage, first);
                long order = Get(orderTick, first), ack = Get(ackReceived, first);
                long arrival = list.Select(a => Get(workStart, a)).Where(t => t >= 0 && t >= order).DefaultIfEmpty(-1).Min();
                long resolved = task.ResolvedObservedTick >= 0 ? task.ResolvedObservedTick : Resolution(task.JobId);
                if (resolved >= 0) lastResolved[subject] = resolved;
                var chain = new ResponseChain { Chain = "facility", Key = "job=" + task.JobId + "; " + subject + (incident < 0 ? "; initial-condition" : ""), Incident = incident,
                    IncidentCohort = incident >= 0 ? CohortAt(incident) : -1, Reassignments = Math.Max(0, list.Count - 1),
                    Responders = list.Where(attempts.ContainsKey).Select(a => attempts[a].ActorId).Distinct().ToArray() };
                if (incident >= 0) chain.Stages.Add(Stage("Detection", incident, discovery));
                chain.Stages.Add(Stage("Reporting", discovery, created));
                chain.Stages.Add(Stage("Dispatch", created, order));
                chain.Stages.Add(Stage("Delivery", order, Get(delivered, msg)));
                chain.Stages.Add(Stage("Reading", Get(delivered, msg), Get(read, msg)));
                chain.Stages.Add(Stage("Acknowledgment", order, ack));
                chain.Stages.Add(Stage("Arrival", ack, arrival >= 0 ? Math.Max(arrival, ack) : -1));
                chain.Stages.Add(Stage("Resolution", arrival >= 0 ? Math.Max(arrival, ack) : -1, resolved));
                chains.Add(chain);
            }
            foreach (var (auth, authorizedTick) in authorized.OrderBy(a => a.Key))
            {
                var team = teams.GetValueOrDefault(auth) ?? Array.Empty<long>();
                long order = Get(dispatched, auth);
                var acks = teamAcks.GetValueOrDefault(auth) ?? new List<long>();
                long ack = acks.Count >= guardCount ? acks.OrderBy(t => t).ElementAt(guardCount - 1) : -1;
                var messages = orderAuthorization.Where(o => o.Value == auth).Select(o => o.Key).ToArray();
                long delivery = messages.Select(m => Get(delivered, m)).Where(t => t >= 0).DefaultIfEmpty(-1).Max();
                long reading = messages.Length >= guardCount ? messages.Select(m => Get(read, m)).Where(t => t >= 0).OrderBy(t => t).ElementAtOrDefault(guardCount - 1) : -1;
                var chain = new ResponseChain { Chain = "escort", Key = "authorization=" + auth, Incident = authorizedTick, IncidentCohort = CohortAt(authorizedTick),
                    Responders = team, Reassignments = Math.Max(0, dispatchCount.GetValueOrDefault(auth) - 1) };
                chain.Stages.Add(Stage("Dispatch", authorizedTick, order));
                chain.Stages.Add(Stage("Delivery", order, delivery));
                chain.Stages.Add(Stage("Reading", delivery, reading > 0 ? reading : -1));
                chain.Stages.Add(Stage("Acknowledgment", order, ack));
                chain.Stages.Add(Stage("Arrival", ack, Get(custody, auth)));
                chain.Stages.Add(Stage("Resolution", Get(custody, auth), Get(detained, auth)));
                chains.Add(chain);
            }
            var patrols = world.InspectPatrolOrders().ToDictionary(p => p.OrderMessageId);
            foreach (var incident in world.InspectMissingReports().OrderBy(i => i.Id))
            {
                long opened = Get(incidentOpened, incident.Id) is var o && o >= 0 ? o : incident.OpenedTick;
                long deadline = expectationDeadlines.TryGetValue(incident.AttemptId, out var deadlines) ? deadlines.Where(t => t <= opened).DefaultIfEmpty(-1).Max() : -1;
                long msg = incident.PatrolOrderMessageId; patrols.TryGetValue(msg, out var patrol);
                long order = msg > 0 ? Get(posted, msg) : -1, ack = patrol != null && patrol.AcknowledgmentMessageId > 0 ? Get(delivered, patrol.AcknowledgmentMessageId) : -1;
                long arrival = msg > 0 ? Get(patrolInspected, msg) : -1;
                long result = incident.PatrolResultMessageId > 0 ? Get(delivered, incident.PatrolResultMessageId) : -1;
                var chain = new ResponseChain { Chain = "patrol", Key = "incident=" + incident.Id + "; attempt=" + incident.AttemptId, Incident = deadline, IncidentCohort = deadline >= 0 ? CohortAt(deadline) : -1,
                    Responders = patrol == null ? Array.Empty<long>() : new[] { patrol.ActorId } };
                if (deadline >= 0) chain.Stages.Add(Stage("Detection", deadline, opened));
                // A received original reply settles worker status; no patrol is then required.
                if (msg == 0 && incident.WorkerStatusResponse == "ResponseReceived") { chain.Key += "; settled-by-reply"; chains.Add(chain); continue; }
                chain.Stages.Add(Stage("Dispatch", opened, order));
                chain.Stages.Add(Stage("Delivery", order, msg > 0 ? Get(delivered, msg) : -1));
                chain.Stages.Add(Stage("Reading", msg > 0 ? Get(delivered, msg) : -1, msg > 0 ? Get(read, msg) : -1));
                chain.Stages.Add(Stage("Acknowledgment", order, ack));
                chain.Stages.Add(Stage("Arrival", ack, arrival >= 0 ? Math.Max(arrival, ack) : -1));
                chain.Stages.Add(Stage("Resolution", arrival >= 0 ? Math.Max(arrival, ack) : -1, result));
                chains.Add(chain);
            }
            foreach (var c in cases)
            {
                long incident = misconduct.TryGetValue(c.MachineId, out long m) ? m : -1;
                var chain = new ResponseChain { Chain = "case", Key = "attempt=" + c.RepairAttemptId + "; outcome=" + c.Outcome, Incident = incident, IncidentCohort = incident >= 0 ? CohortAt(incident) : -1 };
                long inspection = c.VerificationAttemptId > 0 ? Get(inspected, c.VerificationAttemptId) : -1;
                chain.Stages.Add(Stage("Detection", incident, inspection));
                // Evidence-based review outcomes close without a reprimand; only accepted cases expect one.
                if (c.Outcome == CaseOutcomeKind.Accepted || c.Outcome == CaseOutcomeKind.Pending) chain.Stages.Add(Stage("Decision", inspection, Get(decided, c.RepairAttemptId)));
                chains.Add(chain);
            }
            foreach (var c in credentials)
            {
                var chain = new ResponseChain { Chain = "credential", Key = "actor=" + c.Actor + "; lost=" + c.Lost, Incident = c.Lost, IncidentCohort = CohortAt(c.Lost), Responders = new[] { c.Actor } };
                chain.Stages.Add(Stage("Detection", c.Lost, c.Needed));
                chain.Stages.Add(Stage("Resolution", c.Needed, c.Completed));
                chains.Add(chain);
            }
            var report = new ResponseReport { Chains = chains.Count };
            var exceptions = new List<ResponseChain>();
            foreach (var chain in chains)
            {
                int incidentDistrict = -1;
                if (chain.Chain == "facility")
                {
                    string subject = chain.Key.Split(';', StringSplitOptions.TrimEntries)[1];
                    if (subject.StartsWith("Tile:", StringComparison.Ordinal) && tilePosition.TryGetValue(long.Parse(subject[5..]), out var p)) incidentDistrict = p.X / 3;
                    if (subject.StartsWith("Machine:", StringComparison.Ordinal))
                        incidentDistrict = world.InspectMachines().Where(m => m.Id == long.Parse(subject[8..])).Select(m => m.Position.X / 3).DefaultIfEmpty(-1).First();
                }
                bool exceptional = false;
                foreach (var stage in chain.Stages.Where(s => s.Start >= 0))
                {
                    long target = ResponseTargets.GetValueOrDefault(chain.Chain + "/" + stage.Stage, -1);
                    if (target < 0 || stage.End >= 0 && stage.Duration <= target) continue;
                    stage.Cause = (stage.End < 0 ? "Unresolved:" : "") + Classify(chain.Chain, stage, chain.Responders, incidentDistrict);
                    exceptional = true;
                }
                if (exceptional) exceptions.Add(chain);
            }
            foreach (var group in chains.SelectMany(c => c.Stages.Select(s => (c.Chain, Stage: s))).GroupBy(x => (x.Chain, x.Stage.Stage)).OrderBy(g => g.Key.Chain, StringComparer.Ordinal).ThenBy(g => g.Key.Stage, StringComparer.Ordinal))
            {
                var started = group.Where(x => x.Stage.Start >= 0).Select(x => x.Stage).ToArray();
                var done = started.Where(s => s.End >= 0).Select(s => s.Duration).OrderBy(d => d).ToArray();
                long Percentile(int p) => done.Length == 0 ? -1 : done[Math.Max(0, (int)Math.Ceiling(done.Length * p / 100.0) - 1)];
                var stats = new StageStatistics { Chain = group.Key.Chain, Stage = group.Key.Stage, Kind = DetectionStages.Contains(group.Key.Stage) ? "detection" : "institutional",
                    Target = ResponseTargets.GetValueOrDefault(group.Key.Chain + "/" + group.Key.Stage, -1), Completed = done.Length,
                    Median = Percentile(50), P95 = Percentile(95), Maximum = done.Length == 0 ? -1 : done[^1], Unresolved = started.Count(s => s.End < 0),
                    OldestUnresolvedAge = started.Where(s => s.End < 0).Select(s => horizon - s.Start).DefaultIfEmpty(-1).Max() };
                foreach (var s in started.Where(s => s.Cause.Length > 0))
                {
                    stats.MissedTarget++; stats.MissCauses[s.Cause] = stats.MissCauses.GetValueOrDefault(s.Cause) + 1;
                    string key = group.Key.Chain + "/" + group.Key.Stage + ": " + s.Cause; report.MissCauses[key] = report.MissCauses.GetValueOrDefault(key) + 1;
                }
                report.MissedTargets += stats.MissedTarget; report.UnresolvedStages += stats.Unresolved;
                report.Stages.Add(stats);
            }
            const int maximumExceptions = 400;
            report.Exceptions = exceptions.Take(maximumExceptions).ToList(); report.ExceptionsOmitted = Math.Max(0, exceptions.Count - maximumExceptions);
            return report;
        }
    }
}
