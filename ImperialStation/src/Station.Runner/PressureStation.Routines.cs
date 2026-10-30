using Station.Contracts;
using Station.Core;

namespace Station.Runner;

// Explains every missed ordinary routine window after the run, from the recorded trace
// and final snapshots. It never influences decisions. Each miss gets exactly one
// category, checked in a fixed order; "Unexplained" is kept so nothing is hidden.
public static partial class PressureStation
{
    // Allowed delays follow declared priorities (custody, urgent orders, credential
    // recovery, an unchanged fixture start, a need that only arose late). The others
    // point at routine timing worth fixing.
    public static readonly IReadOnlyDictionary<string, bool> MissCategoryAllowed = new SortedDictionary<string, bool>(StringComparer.Ordinal)
    {
        ["InitialSleepWindow"] = true, ["InCustody"] = true, ["EscortOrCustodyDuty"] = true, ["CredentialRecovery"] = true,
        ["LateEligibility"] = true, ["SleepOverran"] = false, ["ContinuousWork"] = false, ["Unexplained"] = false,
    };
    public sealed class MissedWindow
    {
        public long Actor { get; set; }
        public string Kind { get; set; } = "";
        public long Opens { get; set; }
        public long Closes { get; set; }
        public string Reason { get; set; } = "";
        public string Category { get; set; } = "";
    }
    public sealed class MissedWindowReport
    {
        public int Total { get; set; }
        public int Allowed { get; set; }
        public int NeedsAttention { get; set; }
        public SortedDictionary<string, int> Categories { get; set; } = new(StringComparer.Ordinal);
        public List<MissedWindow> Windows { get; set; } = new();
        public int WindowsOmitted { get; set; }
    }

    // On-duty time between completed inbox reads (check-ins), per citizen, after removing
    // declared terminal outages, custody, escort/custody duty, credential recovery and
    // sleep. The bound is two periodic check-in intervals: sustained work, meals or
    // cleaning may delay a check-in but must not starve it.
    public sealed class CheckInGap
    {
        public long Actor { get; set; }
        public long From { get; set; }
        public long To { get; set; }
        public long OnDutyTicks { get; set; }
        public long MeasuredTicks { get; set; }
    }
    public sealed class CheckInReport
    {
        public int Actors { get; set; }
        public long Reads { get; set; }
        public long BoundTicks { get; set; }
        public long MaximumGap { get; set; }
        public long MedianWorstGap { get; set; }
        public int GapsOverBound { get; set; }
        // Excluded on-duty ticks per category; categories may overlap.
        public SortedDictionary<string, long> ExcludedOnDutyTicks { get; set; } = new(StringComparer.Ordinal);
        public List<CheckInGap> Worst { get; set; } = new();
    }
    // A published roster change: the citizen must keep its old roster until it reads the
    // new one at a terminal; every duty boundary is checked against the roster it knew.
    public sealed class RosterChangeCheck
    {
        public long Actor { get; set; }
        public long Published { get; set; }
        public long Read { get; set; } = -1;
        public int OldCohort { get; set; }
        public int NewCohort { get; set; }
        public int BoundariesBeforeRead { get; set; }
        public int BoundariesAfterRead { get; set; }
        // Duty boundaries and pre-shift check-ins between publication and read that only
        // the old roster schedules; the unread new roster would not have placed them.
        public int OldRosterOnlyEvents { get; set; }
        public int Inconsistent { get; set; }
    }

    internal sealed class RoutineMissTracker
    {
        private readonly Dictionary<long, (long Actor, string Kind, long Opens)> goals = new();
        private readonly List<(long Goal, long Tick, string Reason)> missed = new();
        private readonly Dictionary<long, List<(long Tick, string Mode)>> modes = new();
        private readonly Dictionary<long, List<long>> hungerSeek = new(), meals = new();
        private readonly Dictionary<long, List<(long Start, long End)>> recovery = new(), guardDuty = new();
        private readonly Dictionary<long, long> recoveryStart = new(), guardRead = new();
        private readonly Dictionary<long, long> authorizationGuardRead = new();
        private readonly Dictionary<long, List<long>> authorizationGuards = new();
        private readonly Dictionary<long, long> firstSleepStart = new(), custodyBegan = new();
        private readonly Dictionary<long, List<long>> reads = new();
        private readonly Dictionary<long, List<(long Tick, long Version)>> rosterReads = new();
        private readonly Dictionary<long, List<(long Tick, bool OnDuty)>> boundaries = new();
        private readonly Dictionary<long, List<long>> shiftCheckIns = new();
        private readonly long horizon;
        private readonly LoadedScenario scenario;
        internal RoutineMissTracker(LoadedScenario scenario) { this.scenario = scenario; horizon = scenario.Scenario.InitialState.Horizon; }
        private static long Field(string detail, string name)
        {
            foreach (var part in detail.Split(';', StringSplitOptions.TrimEntries))
                if (part.StartsWith(name + "=", StringComparison.Ordinal) && long.TryParse(part.AsSpan(name.Length + 1), out long value)) return value;
            return 0;
        }
        private static void Add<T>(Dictionary<long, List<T>> map, long key, T value) { if (!map.TryGetValue(key, out var list)) map[key] = list = new(); list.Add(value); }
        internal void Observe(TraceEvent e)
        {
            switch (e.Type)
            {
                case "DailyGoalDue":
                {
                    var parts = e.Detail.Split("; "); goals[Field(e.Detail, "goal")] = (e.ActorId, parts.Length > 1 ? parts[1] : "", Math.Max(0, e.Tick)); break;
                }
                case "DailyGoalResolved":
                    if (e.Detail.Contains("; MissedWindow; ", StringComparison.Ordinal)) missed.Add((Field(e.Detail, "goal"), e.Tick, e.Detail[(e.Detail.IndexOf("MissedWindow; ", StringComparison.Ordinal) + 14)..]));
                    break;
                case "NeedModeChanged":
                    Add(modes, e.ActorId, (e.Tick, e.Detail));
                    if (e.Detail == "Sleeping") firstSleepStart.TryAdd(e.ActorId, e.Tick);
                    break;
                case "ActionCompleted": if (e.Detail == "ReadInbox") Add(reads, e.ActorId, e.Tick); break;
                case "MealConsumed": Add(meals, e.ActorId, e.Tick); break;
                case "RosterRead": Add(rosterReads, e.ActorId, (e.Tick, Field(e.Detail, "version"))); break;
                case "DutyBoundary": Add(boundaries, e.ActorId, (e.Tick, e.Detail == "OnDuty")); break;
                case "CheckInDue": if (e.Detail == "known-roster/v1") Add(shiftCheckIns, e.ActorId, e.Tick); break;
                case "NeedThresholdReached": if (e.Detail.StartsWith("Hunger/Seek", StringComparison.Ordinal)) Add(hungerSeek, e.ActorId, e.Tick); break;
                case "CredentialRecoveryNeeded": recoveryStart.TryAdd(e.ActorId, e.Tick); break;
                case "CredentialRecoveryCompleted": if (recoveryStart.Remove(e.ActorId, out long start)) Add(recovery, e.ActorId, (start, e.Tick)); break;
                case "GuardOrderRead":
                {
                    long auth = Field(e.Detail, "authorization"); Add(authorizationGuards, auth, e.ActorId);
                    guardRead.TryAdd(e.ActorId * 1_000_000 + auth, e.Tick); break;
                }
                case "CustodyBegan": custodyBegan.TryAdd(Field(e.Detail, "authorization"), e.Tick); break;
                case "Detained":
                {
                    long auth = Field(e.Detail, "authorization");
                    foreach (long guard in authorizationGuards.GetValueOrDefault(auth) ?? new List<long>())
                        if (guardRead.Remove(guard * 1_000_000 + auth, out long read)) Add(guardDuty, guard, (read, e.Tick));
                    break;
                }
            }
        }
        // Ticks of [start, end) spent in a mode, from the recorded mode changes.
        private long TimeIn(long actor, long start, long end, string mode)
        {
            if (!modes.TryGetValue(actor, out var list)) return mode == "Awake" ? end - start : 0;
            long total = 0; string current = "Awake"; long at = start;
            foreach (var (tick, m) in list)
            {
                if (tick <= start) { current = m; continue; }
                if (tick >= end) break;
                if (current == mode) total += tick - at; current = m; at = tick;
            }
            if (current == mode) total += end - at;
            return total;
        }
        private List<(long Start, long End)> ModeSpans(long actor, string mode, long end)
        {
            var spans = new List<(long, long)>(); long? since = null;
            foreach (var (tick, m) in modes.GetValueOrDefault(actor) ?? new())
            {
                if (m == mode && since == null) since = tick;
                else if (m != mode && since != null) { spans.Add((since.Value, tick)); since = null; }
            }
            if (since != null) spans.Add((since.Value, end));
            return spans;
        }
        // The roster this citizen knew at each tick: the initial one, then each one read.
        private List<(long From, RosterPayload Roster)> KnownRosters(World world, long actor)
        {
            var all = world.InspectRosters().Where(r => r.Roster.ActorId == actor).OrderBy(r => r.Roster.Version).ToArray();
            var known = new List<(long, RosterPayload)>();
            if (all.Length == 0) return known;
            known.Add((0, all[0].Roster));
            foreach (var (tick, version) in rosterReads.GetValueOrDefault(actor) ?? new())
                if (all.FirstOrDefault(r => r.Roster.Version == version) is { } read) known.Add((tick, read.Roster));
            return known;
        }
        private static long OnDutyTicks(List<(long From, RosterPayload Roster)> known, long start, long end)
        {
            long total = 0;
            for (int i = 0; i < known.Count; i++)
            {
                long from = Math.Max(start, known[i].From), to = Math.Min(end, i + 1 < known.Count ? known[i + 1].From : long.MaxValue);
                for (long t = from; t < to;)
                {
                    long next = Math.Min(known[i].Roster.NextBoundary(t), to);
                    if (known[i].Roster.OnDuty(t)) total += next - t;
                    t = next;
                }
            }
            return total;
        }
        private static List<(long Start, long End)> Merge(IEnumerable<(long Start, long End)> spans)
        {
            var merged = new List<(long Start, long End)>();
            foreach (var s in spans.Where(s => s.End > s.Start).OrderBy(s => s.Start))
                if (merged.Count > 0 && s.Start <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, s.End)); else merged.Add(s);
            return merged;
        }
        internal CheckInReport FinishCheckIns(World world, long boundTicks)
        {
            var report = new CheckInReport { BoundTicks = boundTicks };
            // Declared outage windows of every terminal, from disconnection to reconnection.
            var outages = new List<(long, long)>();
            foreach (var down in scenario.Scenario.Disturbances.Where(d => d.Type == "TerminalChange" && d.Connected == false))
                outages.Add((down.Tick, scenario.Scenario.Disturbances.Where(d => d.Type == "TerminalChange" && d.TerminalId == down.TerminalId && d.Connected == true && d.Tick > down.Tick)
                    .Select(d => d.Tick).DefaultIfEmpty(horizon).Min()));
            var gaps = new List<CheckInGap>(); var worst = new List<long>();
            foreach (var routine in world.InspectRosters().Select(r => r.Roster.ActorId).Distinct().Order())
            {
                var known = KnownRosters(world, routine);
                var excluded = new SortedDictionary<string, List<(long Start, long End)>>(StringComparer.Ordinal)
                {
                    ["TerminalOutage"] = outages,
                    ["InCustody"] = CustodySpans(world, routine),
                    ["EscortOrCustodyDuty"] = guardDuty.GetValueOrDefault(routine) ?? new(),
                    ["CredentialRecovery"] = (recovery.GetValueOrDefault(routine) ?? new()).Concat(recoveryStart.TryGetValue(routine, out long open) ? new[] { (open, horizon) } : Array.Empty<(long, long)>()).ToList(),
                    ["Sleeping"] = ModeSpans(routine, "Sleeping", horizon),
                };
                foreach (var (category, spans) in excluded)
                    report.ExcludedOnDutyTicks[category] = report.ExcludedOnDutyTicks.GetValueOrDefault(category) + Merge(spans).Sum(s => OnDutyTicks(known, s.Start, s.End));
                var merged = Merge(excluded.Values.SelectMany(s => s));
                var times = (reads.GetValueOrDefault(routine) ?? new()).Distinct().Order().ToList();
                report.Actors++; report.Reads += times.Count;
                long actorWorst = 0, previous = 0;
                foreach (long next in times.Append(horizon))
                {
                    long duty = OnDutyTicks(known, previous, next);
                    long measured = duty - merged.Where(s => s.Start < next && s.End > previous).Sum(s => OnDutyTicks(known, Math.Max(previous, s.Start), Math.Min(next, s.End)));
                    gaps.Add(new CheckInGap { Actor = routine, From = previous, To = next, OnDutyTicks = duty, MeasuredTicks = measured });
                    actorWorst = Math.Max(actorWorst, measured); previous = next;
                }
                worst.Add(actorWorst);
            }
            report.MaximumGap = worst.DefaultIfEmpty(0).Max();
            report.MedianWorstGap = worst.Count == 0 ? 0 : worst.Order().ElementAt(worst.Count / 2);
            report.GapsOverBound = gaps.Count(g => g.MeasuredTicks > boundTicks);
            report.Worst = gaps.OrderByDescending(g => g.MeasuredTicks).ThenBy(g => g.Actor).Take(20).ToList();
            return report;
        }
        internal List<RosterChangeCheck> FinishRosterChanges(World world)
        {
            var checks = new List<RosterChangeCheck>();
            foreach (var change in scenario.Scenario.Disturbances.Where(d => d.Type == "RosterChange").OrderBy(d => d.Roster!.ActorId))
            {
                long actor = change.Roster!.ActorId;
                var known = KnownRosters(world, actor);
                var published = world.InspectRosters().Where(r => r.OperationKey == change.OperationKey).Select(r => r.Roster).Single();
                var check = new RosterChangeCheck { Actor = actor, Published = change.Tick, OldCohort = known[0].Roster.Cohort, NewCohort = published.Cohort,
                    Read = (rosterReads.GetValueOrDefault(actor) ?? new()).Where(r => r.Version == published.Version).Select(r => r.Tick).DefaultIfEmpty(-1).Min() };
                foreach (var (tick, onDuty) in boundaries.GetValueOrDefault(actor) ?? new())
                {
                    var roster = known.Last(k => k.From <= tick).Roster;
                    if (roster.OnDuty(tick) != onDuty) check.Inconsistent++;
                    if (check.Read < 0 || tick < check.Read) check.BoundariesBeforeRead++; else check.BoundariesAfterRead++;
                    // The boundary follows the old roster where the new one would not have had it.
                    if (tick >= change.Tick && (check.Read < 0 || tick < check.Read) && (published.OnDuty(tick) != onDuty || published.OnDuty(tick - 1) == onDuty)) check.OldRosterOnlyEvents++;
                }
                // Check-ins the old roster schedules (in-shift offset, or the pre-duty meal
                // window opening) that the new roster would not; off-duty ones share a boundary.
                bool Scheduled(RosterPayload r, long tick) => r.NextCheckIn(tick, true) == tick || DailyRoutine.Around(r, tick).Any(w => w.Kind == RoutineOpportunityKind.PreDutyMeal && w.Opens == tick);
                foreach (long tick in shiftCheckIns.GetValueOrDefault(actor) ?? new())
                    if (tick >= change.Tick && (check.Read < 0 || tick < check.Read) && Scheduled(known[0].Roster, tick) && !Scheduled(published, tick)) check.OldRosterOnlyEvents++;
                checks.Add(check);
            }
            return checks;
        }
        // Custody runs from the escort taking the subject (before any intake wait) to release.
        private List<(long Start, long End)> CustodySpans(World world, long actor)
        {
            var stays = world.InspectDetentionStays();
            return world.InspectDetentionAuthorizations().Where(x => x.ActorId == actor && custodyBegan.ContainsKey(x.Id))
                .Select(x => (custodyBegan[x.Id], stays.Where(s => s.ActorId == actor && s.ReleaseTick >= custodyBegan[x.Id]).Select(s => s.ReleaseTick).DefaultIfEmpty(horizon).Min()))
                .Concat(stays.Where(s => s.ActorId == actor && s.IntakeTick >= 0).Select(s => (s.IntakeTick, s.ReleaseTick < 0 ? horizon : s.ReleaseTick))).ToList();
        }
        private bool HungerAtSeek(long actor, long tick)
        {
            long seek = (hungerSeek.GetValueOrDefault(actor) ?? new()).Where(t => t <= tick).DefaultIfEmpty(-1).Max();
            return seek >= 0 && (meals.GetValueOrDefault(actor) ?? new()).Where(t => t <= tick).DefaultIfEmpty(-1).Max() < seek;
        }
        private static bool Overlaps(IEnumerable<(long Start, long End)>? spans, long start, long end) => spans != null && spans.Any(s => s.Start < end && s.End > start);
        internal MissedWindowReport Finish(World world)
        {
            var report = new MissedWindowReport();
            foreach (var (gid, closedTick, reason) in missed)
            {
                if (!goals.TryGetValue(gid, out var goal)) continue;
                var g = world.InspectDailyGoals().SingleOrDefault(x => x.Id == gid);
                long opens = g?.Opens ?? goal.Opens, closes = g?.Closes ?? closedTick, span = Math.Max(1, closes - Math.Max(0, opens));
                long a = goal.Actor, from = Math.Max(0, opens);
                var custody = CustodySpans(world, a);
                string category;
                if (TimeIn(a, from, closes, "Sleeping") * 2 >= span && firstSleepStart.TryGetValue(a, out long first) && first < 100 && closes <= StationBaseline.Day && TimeIn(a, 0, from, "Awake") <= 100)
                    category = "InitialSleepWindow";
                else if (Overlaps(custody, from, closes))
                    category = "InCustody";
                else if (Overlaps(guardDuty.GetValueOrDefault(a), from, closes))
                    category = "EscortOrCustodyDuty";
                else if (Overlaps(recovery.GetValueOrDefault(a), from, closes) || recoveryStart.TryGetValue(a, out long open) && open < closes)
                    category = "CredentialRecovery";
                else if (TimeIn(a, from, closes, "Sleeping") * 2 >= span)
                    category = "SleepOverran";
                // A meal that became needed (hunger at seek, with no meal since) only in the
                // last service-plus-travel slack before the window closed.
                else if (goal.Kind.EndsWith("Meal", StringComparison.Ordinal) && !HungerAtSeek(a, closes - 500))
                    category = "LateEligibility";
                else if (TimeIn(a, from, closes, "Working") * 2 >= span)
                    category = "ContinuousWork";
                else category = "Unexplained";
                report.Total++; report.Categories[category] = report.Categories.GetValueOrDefault(category) + 1;
                if (MissCategoryAllowed[category]) report.Allowed++; else report.NeedsAttention++;
                if (report.Windows.Count < 300) report.Windows.Add(new MissedWindow { Actor = a, Kind = goal.Kind, Opens = opens, Closes = closes, Reason = reason, Category = category });
                else report.WindowsOmitted++;
            }
            return report;
        }
    }
}
