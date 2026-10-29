using Station.Contracts;
using Station.Core;

namespace Station.Runner;

// Measurement only: reads trace events and final inspections, never World state
// that a citizen or the authority could not have produced. Stage boundaries:
// incident -> discovered (local observation) -> reported (authority receipt) ->
// ordered (authority posting) -> acknowledged (authority receipt) -> arrived
// (responder physically at the target) -> resolved (physical outcome).
public static class ResponseTimes
{
    public static readonly string[] Stages = { "Detection", "Reporting", "Ordering", "Acknowledgment", "Arrival", "Resolution", "Total" };
    // Declared before assessing the staffing/routine fixes. One configured hour is
    // Day / 24 ticks; limits are stated in configured hours of the shortened day.
    public sealed class Limits
    {
        public long Hour { get; set; }
        public Dictionary<string, Dictionary<string, long>> Categories { get; set; } = new(StringComparer.Ordinal);
        public static Limits Pressure(long day)
        {
            long h = day / 24;
            Dictionary<string, long> L(long detection, long reporting, long ordering, long ack, long arrival, long resolution, long total) => new(StringComparer.Ordinal)
            { ["Detection"] = detection * h, ["Reporting"] = reporting * h, ["Ordering"] = ordering * h, ["Acknowledgment"] = ack * h, ["Arrival"] = arrival * h, ["Resolution"] = resolution * h, ["Total"] = total * h };
            return new Limits { Hour = h, Categories = new(StringComparer.Ordinal) {
                // Faults and spills are found by ordinary travel, not sensors.
                ["MachineFault"] = L(8, 2, 2, 4, 2, 4, 16),
                ["Spill"] = L(8, 2, 2, 4, 2, 8, 24),
                // An authorized detention needs a team in every shift.
                ["Custody"] = L(1, 1, 1, 2, 2, 1, 6),
                ["CardLoss"] = L(4, 2, 2, 2, 2, 2, 8),
                ["MissingReport"] = L(0, 1, 1, 2, 2, 4, 8) } };
        }
    }
    public sealed class Incident
    {
        public string Category { get; set; } = "";
        public string Subject { get; set; } = "";
        public long Tick { get; set; }
        public string Shift { get; set; } = "";
        public bool NearShiftBoundary { get; set; }
        public long Discovered { get; set; } = -1;
        public long Reported { get; set; } = -1;
        public long Ordered { get; set; } = -1;
        public long Delivered { get; set; } = -1;
        public long Read { get; set; } = -1;
        public long Acknowledged { get; set; } = -1;
        public long Arrived { get; set; } = -1;
        public long Resolved { get; set; } = -1;
        // A spill is resolved when its tile is back to its dirt level just before
        // the spill: the spilled units were removed, not the tile's older backlog.
        public long RestoredLevel { get; set; } = -1;
        public long Responder { get; set; }
        public long AttemptId { get; set; }
        public long OrderMessageId { get; set; }
        public int Dispatches { get; set; }
        public SortedDictionary<string, long> Waits { get; set; } = new(StringComparer.Ordinal);
        public List<string> Missed { get; set; } = new();
        internal long Key;
        internal readonly List<long> Responders = new();
    }
    public sealed record StageStats(string Category, string Stage, int Count, long Median, long P95, long Maximum, long Limit, int Missed);
    public sealed record Unresolved(string Category, int Count, long MaximumAge, long MedianAge);
    public sealed record ShiftStats(string Category, string Shift, int Count, int NearBoundary, long MedianTotal, long MaximumTotal, int Missed, int Unresolved);
    public sealed record Coverage(long Tick, int OnDuty, int Available, int Escorting, int Patrolling, int Breaks);
    public sealed class Report
    {
        public Limits Limits { get; set; } = new();
        public List<StageStats> Stages { get; set; } = new();
        public List<Unresolved> Unresolved { get; set; } = new();
        public List<ShiftStats> Shifts { get; set; } = new();
        public SortedDictionary<string, int> MissCauses { get; set; } = new(StringComparer.Ordinal);
        public List<Coverage> GuardCoverage { get; set; } = new();
        // Samples where on-duty guards not already escorting could not form a team.
        // Patrols and breaks do not excuse a gap; an active escort team does.
        public int GuardTeamGapSamples { get; set; }
        public int Incidents { get; set; }
        public int MissedIncidents { get; set; }
        public List<Incident> Detail { get; set; } = new();
    }

    // Streams every event once; retains only per-incident timestamps and the
    // bounded per-actor sleep intervals needed to explain a delayed responder.
    public sealed class Tracker
    {
        private readonly long day, duty, horizon; private readonly int cohorts;
        private readonly Dictionary<long, int> cohort = new();
        private readonly Dictionary<long, string> roles = new();
        private readonly Dictionary<long, Coord> machineSites = new();
        private readonly Dictionary<long, Coord> tileSites = new();
        private readonly List<Incident> incidents = new();
        private readonly Dictionary<string, List<Incident>> open = new(StringComparer.Ordinal);
        private readonly Dictionary<long, string> jobSubjects = new();
        private readonly Dictionary<long, long> actionAttempts = new();
        private readonly Dictionary<long, (long Tick, long Message, long Job)> orders = new();
        private readonly Dictionary<long, long> acknowledgments = new();
        private readonly Dictionary<long, long> completions = new();
        private readonly Dictionary<long, List<(long Tick, long Message)>> receipts = new();
        private readonly Dictionary<long, List<(long Tick, Coord At)>> arrivals = new();
        private readonly Dictionary<long, Incident> custody = new();
        private readonly Dictionary<long, Incident> cards = new();
        private readonly Dictionary<long, Incident> missing = new();
        private readonly Dictionary<long, List<(long Start, long End)>> sleep = new();
        private readonly Dictionary<long, long> asleepSince = new();
        private readonly List<Coverage> coverage = new();
        private readonly Dictionary<long, long> credentialHolders = new();
        private readonly Dictionary<long, SortedDictionary<string, int>> dispatchReasons = new();
        private readonly Dictionary<long, string> replacedBecause = new();
        public Tracker(ScenarioData scenario)
        {
            var s = scenario.InitialState; horizon = s.Horizon;
            day = s.Routines?.Configuration.DayTicks ?? StationBaseline.Day; duty = s.Routines?.Configuration.DutyTicks ?? day / 3; cohorts = Math.Max(1, s.Routines?.Configuration.Cohorts ?? 3);
            foreach (var a in s.Actors) roles[a.Id] = a.Role;
            foreach (var r in s.Routines?.Assignments ?? new()) cohort[r.ActorId] = r.Cohort;
            foreach (var m in s.Work?.Machines ?? new()) machineSites[m.Id] = m.Position.ToCoord();
            foreach (var t in s.Tiles) tileSites[t.Id] = t.Position.ToCoord();
            foreach (var c in s.Information?.Credentials ?? new()) credentialHolders[c.Id] = c.Holder;
        }
        private string ShiftOf(long tick) => "shift-" + (tick % day) / (day / cohorts);
        private bool NearBoundary(long tick) { long r = tick % (day / cohorts); return r < day / 48 || day / cohorts - r <= day / 48; }
        public bool OnDuty(long actor, long tick)
        {
            if (!cohort.TryGetValue(actor, out int c)) return false;
            long start = c * (day / cohorts), time = tick % day, rel = time >= start ? time - start : day - (start - time);
            return rel < duty;
        }
        private Incident Open(string category, string subject, long tick, long key = 0)
        {
            var i = new Incident { Category = category, Subject = subject, Tick = tick, Shift = ShiftOf(tick), NearShiftBoundary = NearBoundary(tick), Key = key };
            incidents.Add(i);
            if (!open.TryGetValue(subject, out var list)) open[subject] = list = new();
            list.Add(i); return i;
        }
        private static string Subject(FactKey? key) => key == null ? "" : key.Value.SubjectKind + ":" + key.Value.SubjectId;
        private static long JobOf(string detail)
        {
            int at = detail.IndexOf("job=", StringComparison.Ordinal); if (at < 0) return 0;
            int end = at + 4; while (end < detail.Length && char.IsDigit(detail[end])) end++;
            return long.Parse(detail.AsSpan(at + 4, end - at - 4), System.Globalization.CultureInfo.InvariantCulture);
        }
        private static long Number(string detail, string name)
        {
            int at = detail.IndexOf(name + "=", StringComparison.Ordinal); if (at < 0) return 0;
            int start = at + name.Length + 1, end = start; while (end < detail.Length && char.IsDigit(detail[end])) end++;
            return end == start ? 0 : long.Parse(detail.AsSpan(start, end - start), System.Globalization.CultureInfo.InvariantCulture);
        }
        public void Observe(TraceEvent e)
        {
            switch (e.Type)
            {
                case "MachineFaulted": Open("MachineFault", Subject(e.FactKey), e.Tick); break;
                case "DirtAdded" when e.Detail == "Spill" && e.FactValue is { } level: Open("Spill", Subject(e.FactKey), e.Tick).RestoredLevel = level.Number - e.Quantity; break;
                case "CardLost": cards[e.ActorId] = Open("CardLoss", "Actor:" + e.ActorId, e.Tick); cards[e.ActorId].Responder = e.ActorId; break;
                case "CredentialRecoveryNeeded" when cards.TryGetValue(e.ActorId, out var card) && card.Discovered < 0: card.Discovered = e.Tick; break;
                case "LostCardReportAccepted":
                    if (credentialHolders.TryGetValue(Number(e.Detail, "credential"), out long holder) && cards.TryGetValue(holder, out var reported) && reported.Reported < 0) reported.Reported = reported.Ordered = e.Tick;
                    break;
                case "ReplacementCardIssued" when cards.TryGetValue(e.ActorId, out var issued) && issued.Arrived < 0: issued.Arrived = e.Tick; break;
                case "CredentialRecoveryCompleted" when cards.TryGetValue(e.ActorId, out var done) && done.Resolved < 0: done.Resolved = e.Tick; cards.Remove(e.ActorId); break;
                case "FacilityReportQueued":
                    if (open.TryGetValue(Subject(e.FactKey), out var observed))
                        foreach (var i in observed.Where(i => i.Discovered < 0 && i.Tick <= e.Tick)) i.Discovered = e.Tick;
                    break;
                case "FacilityTaskCreated": jobSubjects[JobOf(e.Detail)] = Subject(e.FactKey); Receipt(JobOf(e.Detail), e); break;
                case "FacilityReportMerged": Receipt(JobOf(e.Detail), e); break;
                case "ActionRequested" when e.AttemptId > 0: actionAttempts[e.ActionId] = e.AttemptId; break;
                case "WorkAssigned": orders[e.AttemptId] = (e.Tick, e.MessageId, e.TaskId); break;
                case "WorkReportReceived" when e.AttemptId > 0 && !acknowledgments.ContainsKey(e.AttemptId): acknowledgments[e.AttemptId] = e.Tick; break;
                case "WorkExecutionCompletedPhysically" when e.AttemptId > 0: completions.TryAdd(e.AttemptId, e.Tick); break;
                case "TileArrived":
                    if (!arrivals.TryGetValue(e.ActorId, out var trips)) arrivals[e.ActorId] = trips = new();
                    if (trips.Count == 0 || trips[^1].At != e.Target) trips.Add((e.Tick, e.Target));
                    break;
                case "MachineTested" when e.Detail.EndsWith("operational=True", StringComparison.Ordinal): Resolve(e.Detail[..e.Detail.IndexOf(';')], e); break;
                case "CleaningCompleted" when e.FactValue is { } dirtLeft: Resolve(Subject(e.FactKey), e, dirtLeft.Number); break;
                case "DetentionAuthorized":
                    {
                        long id = Number(e.Detail, "authorization");
                        var i = Open("Custody", "Authorization:" + id, e.Tick, id); i.Discovered = e.Tick; i.Reported = e.Tick; custody[id] = i; break;
                    }
                case "EscortTeamDispatched" when custody.TryGetValue(Number(e.Detail, "authorization"), out var dispatch):
                    if (dispatch.Ordered < 0) dispatch.Ordered = e.Tick; dispatch.Dispatches++; dispatch.Acknowledged = -1; dispatch.Arrived = -1; dispatch.Responders.Clear();
                    int at = e.Detail.IndexOf("guards=", StringComparison.Ordinal);
                    if (at >= 0) dispatch.Responders.AddRange(e.Detail[(at + 7)..].Split(',').Select(g => long.Parse(g, System.Globalization.CultureInfo.InvariantCulture)));
                    dispatch.Responder = dispatch.Responders.FirstOrDefault(); acks[dispatch.Key] = 0;
                    break;
                case "GuardOrderRead" when custody.TryGetValue(Number(e.Detail, "authorization"), out var read):
                    if (read.Responders.Contains(e.ActorId)) read.Read = Math.Max(read.Read, e.Tick);
                    break;
                case "GuardAcknowledgmentReceived" when custody.TryGetValue(Number(e.Detail, "authorization"), out var acked):
                    acks[acked.Key] = acks.GetValueOrDefault(acked.Key) + 1;
                    if (acks[acked.Key] >= Math.Max(1, acked.Responders.Count) && acked.Acknowledged < 0) acked.Acknowledged = e.Tick;
                    break;
                case "GuardSearchCandidateInspected" when e.Detail.StartsWith("FoundHere", StringComparison.Ordinal):
                    foreach (var c in custody.Values.Where(c => c.Resolved < 0 && c.Arrived < 0 && c.Acknowledged >= 0 && c.Responders.Contains(e.ActorId))) c.Arrived = e.Tick;
                    break;
                case "Detained" when custody.TryGetValue(Number(e.Detail, "authorization"), out var detained): detained.Resolved = e.Tick; break;
                case "EscortAcknowledgmentOverdue" when custody.ContainsKey(Number(e.Detail, "authorization")):
                    replacedBecause[Number(e.Detail, "authorization")] = "AcknowledgmentOverdue"; break;
                case "GuardOutcomeReceived" when custody.ContainsKey(Number(e.Detail, "authorization")):
                    replacedBecause[Number(e.Detail, "authorization")] = e.Detail[(e.Detail.IndexOf(';') + 2)..];
                    break;
                case "MissingWorkReportOpened":
                    {
                        long id = Number(e.Detail, "incident");
                        var i = Open("MissingReport", "Incident:" + id, e.Tick, id); i.Discovered = e.Tick; i.Reported = e.Tick; missing[id] = i; break;
                    }
                case "NeedModeChanged":
                    if (e.Detail == "Sleeping") asleepSince[e.ActorId] = e.Tick;
                    else if (asleepSince.Remove(e.ActorId, out long since)) { if (!sleep.TryGetValue(e.ActorId, out var spans)) sleep[e.ActorId] = spans = new(); spans.Add((since, e.Tick)); }
                    break;
            }
        }
        private readonly Dictionary<long, int> acks = new();
        private void Receipt(long job, TraceEvent e)
        { if (job <= 0) return; if (!receipts.TryGetValue(job, out var list)) receipts[job] = list = new(); list.Add((e.Tick, e.MessageId)); }
        private void Resolve(string subject, TraceEvent e, long dirtLeft = 0)
        {
            if (!open.TryGetValue(subject, out var list)) return;
            long attempt = e.AttemptId > 0 ? e.AttemptId : actionAttempts.GetValueOrDefault(e.ActionId);
            foreach (var i in list.Where(i => i.Resolved < 0 && i.Tick <= e.Tick && dirtLeft <= Math.Max(0, i.RestoredLevel))) { i.Resolved = e.Tick; i.Responder = e.ActorId; i.AttemptId = attempt; }
            list.RemoveAll(i => i.Resolved >= 0);
        }
        // Coverage samples use the published roster and authority-visible commitments
        // only to explain gaps; nothing here feeds a simulated decision.
        public void Sample(World w, long tick)
        {
            foreach (var d in w.InspectEscortDispatches().Where(d => d.Status == EscortDispatchStatus.AwaitingGuards))
            {
                if (!dispatchReasons.TryGetValue(d.AuthorizationId, out var reasons)) dispatchReasons[d.AuthorizationId] = reasons = new(StringComparer.Ordinal);
                reasons[d.Reason] = reasons.GetValueOrDefault(d.Reason) + 1;
            }
            var guards = roles.Where(r => r.Value == "guard").Select(r => r.Key).ToArray();
            if (guards.Length == 0) return;
            var onDuty = guards.Where(g => OnDuty(g, tick)).ToArray();
            var escorts = w.InspectGuardAssignments(); var dispatches = w.InspectEscortDispatches().ToDictionary(d => d.AuthorizationId);
            var outcomes = w.InspectGuardOutcomes(received: true);
            var patrols = w.InspectPatrolOrders();
            bool Escorting(long g) => escorts.Any(a => a.ActorId == g && dispatches.TryGetValue(a.AuthorizationId, out var d) && !(d.Status is EscortDispatchStatus.Detained or EscortDispatchStatus.Released or EscortDispatchStatus.Expired or EscortDispatchStatus.Cancelled) &&
                !outcomes.Any(o => o.OrderMessageId == a.OrderMessageId));
            bool Patrolling(long g) => !Escorting(g) && patrols.Any(p => p.ActorId == g && p.ResultMessageId == 0 && p.Payload.Expires > tick);
            int escorting = onDuty.Count(Escorting), patrolling = onDuty.Count(Patrolling);
            int breaks = onDuty.Count(g => !Escorting(g) && !Patrolling(g) && w.InspectActor(new(g)).Action is ActionKind.Eat or ActionKind.Rest or ActionKind.UseToilet or ActionKind.Sleep);
            coverage.Add(new Coverage(tick, onDuty.Length, onDuty.Length - escorting - patrolling, escorting, patrolling, breaks));
        }
        public Report Finish(World w, Limits limits, int teamSize)
        {
            var messages = w.InspectMessages().ToDictionary(m => m.Id);
            var attempts = w.InspectWorkAttempts().ToDictionary(a => a.Id);
            foreach (var i in incidents.Where(i => i.Category is "MachineFault" or "Spill"))
            {
                long job = jobSubjects.Where(j => j.Value == i.Subject).Select(j => j.Key).Where(j => receipts.TryGetValue(j, out var r) && r.Any(x => x.Tick >= i.Tick)).DefaultIfEmpty().Min();
                if (job > 0)
                    foreach (var r in receipts[job].Where(r => r.Tick >= i.Tick))
                        if (messages.TryGetValue(r.Message, out var m) && m.Draft.ObservedTick >= i.Tick) { i.Reported = r.Tick; break; }
                // The responding order is the one active when the report arrived,
                // else the first order posted afterward. The physical resolver may
                // be a different worker who finished the same shared target.
                long anchor = i.Reported >= 0 ? i.Reported : i.Tick;
                var candidates = orders.Where(o => o.Value.Job == job && job > 0).OrderBy(o => o.Value.Tick).ThenBy(o => o.Key).ToArray();
                var chosen = candidates.Where(o => o.Value.Tick <= anchor && (!completions.TryGetValue(o.Key, out long done) || done >= anchor)).Select(o => o.Key).LastOrDefault();
                if (chosen == 0) chosen = candidates.Where(o => o.Value.Tick >= anchor).Select(o => o.Key).FirstOrDefault();
                if (chosen == 0 && i.AttemptId > 0 && orders.ContainsKey(i.AttemptId)) chosen = i.AttemptId;
                if (chosen > 0)
                {
                    var order = orders[chosen]; i.AttemptId = chosen;
                    if (attempts.TryGetValue(chosen, out var a)) i.Responder = a.ActorId;
                    i.OrderMessageId = order.Message; i.Ordered = Math.Max(order.Tick, anchor);
                    if (messages.TryGetValue(order.Message, out var m)) { i.Delivered = Math.Max(m.DeliveryTick, i.Ordered); if (m.FirstReadTick is long read) i.Read = Math.Max(read, i.Ordered); }
                    if (acknowledgments.TryGetValue(chosen, out long ack)) i.Acknowledged = Math.Max(ack, i.Ordered);
                    Coord site = i.Subject.StartsWith("Machine:", StringComparison.Ordinal) ? machineSites.GetValueOrDefault(long.Parse(i.Subject[8..], System.Globalization.CultureInfo.InvariantCulture)) : tileSites.GetValueOrDefault(long.Parse(i.Subject[5..], System.Globalization.CultureInfo.InvariantCulture));
                    long since = Math.Max(i.Acknowledged, i.Ordered);
                    if (i.Acknowledged >= 0 && arrivals.TryGetValue(i.Responder, out var trips))
                        foreach (var t in trips) if (t.Tick >= since && (i.Resolved < 0 || t.Tick <= i.Resolved) && t.At == site) { i.Arrived = t.Tick; break; }
                    // Already at the site when acknowledging, or resolved by a co-worker.
                    if (i.Arrived < 0 && i.Acknowledged >= 0 && i.Resolved >= 0) i.Arrived = Math.Min(Math.Max(since, i.Acknowledged), i.Resolved);
                }
                else if (i.Resolved >= 0)
                {
                    // Resolved in passing before any order existed for this incident.
                    long previous = Math.Max(i.Discovered, i.Reported);
                    i.Ordered = i.Acknowledged = i.Arrived = Math.Min(i.Resolved, Math.Max(i.Tick, previous));
                }
                if (i.Responder > 0) i.Responders.Add(i.Responder);
            }
            foreach (var i in missing.Values)
            {
                var incident = w.InspectMissingReports().SingleOrDefault(m => m.Id == i.Key); if (incident == null) continue;
                long patrol = incident.PatrolOrderMessageId, replacement = incident.ReplacementAttemptId;
                if (replacement > 0 && orders.TryGetValue(replacement, out var order)) { i.Ordered = order.Tick; i.OrderMessageId = order.Message; if (acknowledgments.TryGetValue(replacement, out long a)) i.Acknowledged = a; if (attempts.TryGetValue(replacement, out var r)) { i.Responder = r.ActorId; i.Responders.Add(r.ActorId); } }
                else if (patrol > 0 && messages.TryGetValue(patrol, out var p)) { i.Ordered = p.SubmittedTick; i.OrderMessageId = patrol; }
                if (messages.TryGetValue(i.OrderMessageId, out var om)) { i.Delivered = om.DeliveryTick; if (om.FirstReadTick is long read) i.Read = read; }
                long result = new[] { incident.ResponseMessageId, incident.ReplacementResultMessageId, incident.PatrolResultMessageId }.Where(id => id > 0 && messages.ContainsKey(id)).Select(id => messages[id].DeliveryTick).DefaultIfEmpty(-1).Min();
                if (i.Acknowledged < 0 && result >= 0) i.Acknowledged = Math.Max(i.Ordered, result);
                i.Arrived = i.Acknowledged; i.Resolved = result;
            }
            var report = new Report { Limits = limits };
            foreach (var i in incidents)
            {
                long end = i.Resolved >= 0 ? i.Resolved : horizon;
                void Wait(string stage, long from, long to) { if (from >= 0 && to >= 0) i.Waits[stage] = Math.Max(0, to - from); }
                Wait("Detection", i.Tick, i.Discovered); Wait("Reporting", i.Discovered, i.Reported); Wait("Ordering", i.Reported, i.Ordered);
                Wait("Acknowledgment", i.Ordered, i.Acknowledged); Wait("Arrival", i.Acknowledged, i.Arrived); Wait("Resolution", i.Arrived, i.Resolved);
                if (i.Resolved >= 0) Wait("Total", i.Tick, i.Resolved);
                if (!limits.Categories.TryGetValue(i.Category, out var stageLimits)) continue;
                foreach (var stage in Stages)
                {
                    long wait = i.Waits.TryGetValue(stage, out long v) ? v : -1;
                    bool reached = wait >= 0;
                    // An unfinished stage counts from its start until the horizon.
                    long pending = !reached ? PendingWait(i, stage) : -1;
                    if (reached && wait > stageLimits[stage] || pending > stageLimits[stage]) i.Missed.Add(stage + ":" + Cause(w, i, stage, end));
                }
            }
            foreach (var group in incidents.GroupBy(i => i.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                foreach (var stage in Stages)
                {
                    var values = group.Where(i => i.Waits.ContainsKey(stage)).Select(i => i.Waits[stage]).OrderBy(v => v).ToArray();
                    long limit = limits.Categories.TryGetValue(group.Key, out var l) ? l[stage] : -1;
                    report.Stages.Add(new StageStats(group.Key, stage, values.Length, Percentile(values, 500), Percentile(values, 950), values.DefaultIfEmpty().Max(), limit,
                        group.Count(i => i.Missed.Any(m => m.StartsWith(stage + ":", StringComparison.Ordinal)))));
                }
                var ages = group.Where(i => i.Resolved < 0).Select(i => horizon - i.Tick).OrderBy(v => v).ToArray();
                report.Unresolved.Add(new Unresolved(group.Key, ages.Length, ages.DefaultIfEmpty().Max(), Percentile(ages, 500)));
            }
            foreach (var group in incidents.GroupBy(i => (i.Category, i.Shift)).OrderBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.Shift, StringComparer.Ordinal))
            {
                var totals = group.Where(i => i.Resolved >= 0).Select(i => i.Resolved - i.Tick).OrderBy(v => v).ToArray();
                report.Shifts.Add(new ShiftStats(group.Key.Category, group.Key.Shift, group.Count(), group.Count(i => i.NearShiftBoundary), Percentile(totals, 500), totals.DefaultIfEmpty().Max(),
                    group.Count(i => i.Missed.Count > 0), group.Count(i => i.Resolved < 0)));
            }
            foreach (var miss in incidents.SelectMany(i => i.Missed.Select(m => i.Category + "/" + m)))
                report.MissCauses[miss] = report.MissCauses.GetValueOrDefault(miss) + 1;
            report.Incidents = incidents.Count; report.MissedIncidents = incidents.Count(i => i.Missed.Count > 0);
            report.GuardCoverage = coverage; report.GuardTeamGapSamples = coverage.Count(c => c.Escorting == 0 && c.OnDuty - c.Patrolling < teamSize);
            report.Detail = incidents;
            return report;
        }
        private long PendingWait(Incident i, string stage)
        {
            long[] marks = { i.Tick, i.Discovered, i.Reported, i.Ordered, i.Acknowledged, i.Arrived, i.Resolved };
            int index = Array.IndexOf(Stages, stage);
            if (i.Resolved >= 0) return -1;
            if (stage == "Total") return horizon - i.Tick;
            if (marks[index + 1] >= 0) return -1;
            long start = marks[index]; return start >= 0 ? horizon - start : -1;
        }
        // Explains the dominant condition of the responders during a late stage.
        private string Cause(World w, Incident i, string stage, long end)
        {
            long from = stage switch { "Detection" => i.Tick, "Reporting" => i.Discovered, "Ordering" => i.Reported, "Acknowledgment" => i.Ordered, "Arrival" => i.Acknowledged, "Resolution" => i.Arrived, _ => i.Tick };
            long to = stage switch { "Detection" => i.Discovered, "Reporting" => i.Reported, "Ordering" => i.Ordered, "Acknowledgment" => i.Acknowledged, "Arrival" => i.Arrived, "Resolution" => i.Resolved, _ => i.Resolved };
            if (from < 0) from = i.Tick; if (to < 0) to = end;
            if (stage == "Total") return i.Missed.Count > 0 ? "LateStage/" + i.Missed[0].Split(':')[0] : "Accumulated";
            if (stage == "Detection") return "NoAuthorizedObserverAtSite";
            if (stage == "Reporting") return "ReportTransport/TerminalAvailability";
            if (stage == "Ordering")
            {
                if (i.Category == "Custody") return dispatchReasons.TryGetValue(i.Key, out var reasons) ? reasons.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal).First().Key : "AwaitingGuardTeam";
                return "NoOnDutyUncommittedQualifiedResponder";
            }
            if (i.Responders.Count == 0) return "UnknownResponder";
            if (i.Category == "Custody" && i.Dispatches > 1) return "ReplacementTeam/" + replacedBecause.GetValueOrDefault(i.Key, "Unknown");
            long span = Math.Max(1, to - from); long offDuty = 0, asleep = 0;
            foreach (long r in i.Responders)
            {
                for (long t = from; t < to; t += Math.Max(1, span / 64)) if (!OnDuty(r, t)) offDuty += Math.Max(1, span / 64);
                if (sleep.TryGetValue(r, out var spans)) foreach (var s in spans) asleep += Math.Max(0, Math.Min(to, s.End) - Math.Max(from, s.Start));
                if (asleepSince.TryGetValue(r, out long since)) asleep += Math.Max(0, to - Math.Max(from, since));
            }
            span *= i.Responders.Count;
            if (asleep * 2 > span) return "ResponderAsleep";
            if (offDuty * 2 > span) return "ResponderOffDuty";
            if (stage == "Acknowledgment") return i.Read >= 0 && i.Read - from <= span / i.Responders.Count / 2 ? "ReadButNotAcknowledged/BusyOrNeeds" : i.Category == "Custody" && i.Dispatches > 1 ? "ReplacementTeamNeeded" : "OrderNotRead/OnDutyBusy";
            if (stage == "Arrival") return i.Category == "Custody" ? "SearchOrTeamFormation" : "TravelOrPriorWork";
            return i.Category == "Custody" ? "CustodyHandoffOrIntake" : "PhysicalWorkDuration";
        }
        private static long Percentile(long[] sorted, int permille)
        {
            if (sorted.Length == 0) return 0;
            int index = (int)Math.Min(sorted.Length - 1, (sorted.Length * (long)permille + 999) / 1000 - 1);
            return sorted[Math.Max(0, index)];
        }
    }
}
