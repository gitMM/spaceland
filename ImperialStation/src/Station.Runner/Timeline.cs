using System.Globalization;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

// Viewer projection of one complete run for tools/viewer: per-citizen movement,
// action and knowledge tracks plus periodic state samples for scrubbing. It reads
// the ordinary trace and inspection views while the world advances and never
// feeds anything back into the simulation. The format is documented in docs/formats.md.
public static class Timeline
{
    public const int SchemaVersion = 1;
    public sealed class Summary
    {
        public int SchemaVersion { get; set; } = Timeline.SchemaVersion;
        public string Status { get; set; } = "completed";
        public string Scenario { get; set; } = "";
        public ulong Seed { get; set; }
        public string Path { get; set; } = "";
        public int Citizens { get; set; }
        public long TraceEvents { get; set; }
        public int KeptEvents { get; set; }
        public int Frames { get; set; }
        public long Bytes { get; set; }
        public string FinalDigest { get; set; } = "";
        public double Seconds { get; set; }
    }

    public static int Run(LoadedScenario loaded, Func<string, string?> option)
    {
        ulong seed = option("--seed") is string text ? ulong.Parse(text, CultureInfo.InvariantCulture) : loaded.Scenario.InitialState.Seed;
        long every = option("--every") is string e ? long.Parse(e, CultureInfo.InvariantCulture) : 300;
        long day = loaded.Scenario.InitialState.Routines?.Configuration.DayTicks ?? StationBaseline.Day;
        var summary = Export(loaded, seed, option("--out") ?? "artifacts/timeline-" + loaded.Scenario.InitialState.Id, every,
            tick => { if (tick % day == 0) Console.Error.WriteLine("Timeline tick " + tick + "/" + loaded.Scenario.InitialState.Horizon); });
        Console.WriteLine(JsonData.Write(summary));
        return 0;
    }

    public static Summary Export(LoadedScenario loaded, ulong seed, string outputDirectory, long every = 300, Action<long>? progress = null)
    {
        if (every < 1 || every > 1000000) throw new ArgumentException("--every must be 1..1000000 ticks.");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var world = loaded.CreateWorld(seed, trace: true);
        long horizon = loaded.Scenario.InitialState.Horizon;
        var builder = new Builder(world, loaded);
        for (long tick = 0; ; tick = Math.Min(horizon, tick + every))
        {
            world.AdvanceTo(tick);
            builder.Consume(world.Trace); world.ClearRecordedTrace();
            builder.Sample(tick);
            progress?.Invoke(tick);
            if (tick >= horizon) break;
        }
        builder.Finish();
        string directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "timeline.json.gz");
        string digest = world.Digest();
        using (var file = File.Create(path))
        using (var zip = new GZipStream(file, CompressionLevel.Optimal))
        using (var json = new Utf8JsonWriter(zip, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            builder.Write(json, seed, every, horizon, digest);
        return new Summary { Scenario = loaded.Scenario.InitialState.Id, Seed = seed, Path = path, Citizens = builder.CitizenCount, TraceEvents = builder.TraceEvents,
            KeptEvents = builder.KeptEvents, Frames = builder.FrameCount, Bytes = new FileInfo(path).Length, FinalDigest = digest, Seconds = timer.Elapsed.TotalSeconds };
    }

    // Knowledge values as the viewer shows them; tests compare reconstructed knowledge with this.
    public static string Format(FactValue v) => v.Kind switch
    {
        FactValueKind.Boolean => v.Number != 0 ? "true" : "false",
        FactValueKind.Coordinate => "at " + Coordinate(v.Coordinate),
        FactValueKind.AbsentAt => "absent at " + Coordinate(v.Coordinate),
        FactValueKind.Text => v.Text,
        _ => v.Number.ToString(CultureInfo.InvariantCulture)
    };
    private static string Coordinate(Coord c) => "(" + c.X + ", " + c.Y + ", " + c.Z + ")";

    private sealed class Act
    {
        public long Requested, Began = -1, End = -1, RequestEvent, Cause, Attempt, Terminal, Service, Partner;
        public int Kind, Outcome, Origin, Failure, Queue;
        public bool Waiting;
        // Rarely used fields last so the writer can trim trailing zeros.
        public long[] Row() => new[] { Requested, Began, End, Kind, Outcome, RequestEvent, Cause, Origin, Attempt, Failure, Queue, Terminal, Service, Partner };
    }
    private sealed class PlaceRef
    {
        public string Kind = ""; public long Id; public Coord Position; public int Capacity; public long Assigned; public long Detention; public string Label = "";
        public readonly List<long> Busy = new(), Queued = new(), Up = new();
    }

    private sealed class Builder
    {
        // Events folded into the per-citizen tracks instead of the shared event list.
        private static readonly HashSet<string> Tracked = new(StringComparer.Ordinal) { "ObservationRecorded", "KnowledgeEvicted", "MovementStarted", "TileArrived", "ActionRequested", "ActionStarted", "ActionCompleted", "ActionCancelled" };
        // Events whose target is meaningful even when it is the (0,0,0) tile.
        private static readonly HashSet<string> Located = new(StringComparer.Ordinal) { "CardLost", "MachineFaulted", "DirtAdded", "ItemMoved", "ItemInstalled", "EquipmentEquipped", "CustodyBegan", "Detained",
            "DetentionReleased", "CleaningArrivalChecked", "CleaningCompleted", "MachineTested", "VerificationInspected", "PatrolCandidateInspected", "GuardSearchCandidateInspected", "FaultPartConsumed" };
        private readonly World world;
        private readonly LoadedScenario loaded;
        private readonly List<string> strings = new();
        private readonly Dictionary<string, int> stringIds = new(StringComparer.Ordinal);
        private readonly ActorSnapshot[] citizens;
        private readonly long[] accounts;
        private readonly Dictionary<long, int> citizenIndex = new();
        private readonly List<long[]>[] moves, learned;
        private readonly List<Act>[] acts;
        private readonly List<long[]>[] initialKnowledge;
        private readonly Dictionary<(long Actor, long Action), Act> actions = new();
        private readonly Dictionary<(long Actor, long Action), Act> open = new();
        private readonly Dictionary<(int Citizen, string Key), string> known = new();
        private readonly List<long[]> events = new();
        private readonly Dictionary<Coord, long> tiles = new();
        private readonly List<PlaceRef> places = new();
        private readonly List<long> frameTicks = new();
        private readonly List<long>[] needs, modes, activities, inventories, custody;
        private readonly SortedDictionary<long, List<long>> dirt = new();
        private readonly List<long[]>[] rosters;
        private readonly NeedRule[] rules;
        public long TraceEvents { get; private set; }
        public int KeptEvents => events.Count;
        public int FrameCount => frameTicks.Count;
        public int CitizenCount => citizens.Length;

        public Builder(World world, LoadedScenario loaded)
        {
            this.world = world; this.loaded = loaded;
            S(""); // Index 0 always means "none".
            citizens = world.InspectActors().OrderBy(a => a.Id.Value).ToArray();
            int n = citizens.Length;
            accounts = citizens.Select(a => world.ActorView(a.Id).Account.Value).ToArray();
            for (int i = 0; i < n; i++) citizenIndex.Add(citizens[i].Id.Value, i);
            List<T>[] PerCitizen<T>() => Enumerable.Range(0, n).Select(_ => new List<T>()).ToArray();
            moves = PerCitizen<long[]>(); learned = PerCitizen<long[]>(); acts = PerCitizen<Act>(); initialKnowledge = PerCitizen<long[]>(); rosters = PerCitizen<long[]>();
            needs = PerCitizen<long>(); modes = PerCitizen<long>(); activities = PerCitizen<long>(); inventories = PerCitizen<long>(); custody = PerCitizen<long>();
            rules = world.Needs?.Rules.ToArray() ?? Array.Empty<NeedRule>();
            for (int i = 0; i < n; i++)
                foreach (var fact in world.InspectKnowledge(citizens[i].Id).OrderBy(f => f.Key))
                {
                    string key = fact.Key.ToString(), value = Format(fact.Value);
                    known[(i, key)] = value;
                    initialKnowledge[i].Add(new long[] { S(key), S(value), S(fact.SourceKind.ToString()), fact.ObservedTick, fact.SourceActorId });
                }
            foreach (var tile in world.Topology.InspectTiles()) tiles[tile.Position] = tile.Id.Value;
            foreach (var s in world.InspectNeedServices().OrderBy(s => s.Definition.Id))
            {
                var d = s.Definition;
                places.Add(new PlaceRef { Kind = d.Kind.ToString(), Id = d.Id, Position = d.Position, Capacity = d.Capacity, Assigned = d.AssignedActorId, Detention = d.DetentionFacilityId,
                    Label = d.Kind + (d.AssignedActorId > 0 ? " of " + d.AssignedActorId : "") + (d.DetentionFacilityId > 0 ? " (detention)" : "") });
            }
            foreach (var t in world.InspectTerminals().OrderBy(t => t.Id))
                places.Add(new PlaceRef { Kind = "Terminal", Id = t.Id, Position = t.Position, Capacity = t.Capacity, Label = "Terminal " + t.Id });
            foreach (var m in world.InspectMachines().OrderBy(m => m.Id))
                places.Add(new PlaceRef { Kind = "Machine", Id = m.Id, Position = m.Position, Capacity = 1, Label = "Machine " + m.Id + " (" + m.RequiredResource + ")" });
            foreach (var w in (loaded.Scenario.InitialState.Work?.Workstations ?? new()).OrderBy(w => w.Id))
                places.Add(new PlaceRef { Kind = "Workstation", Id = w.Id, Position = w.Position.ToCoord(), Capacity = w.Capacity, Label = w.Capability + " " + w.Id });
            foreach (var f in world.InspectDetentionFacilities().OrderBy(f => f.Definition.Id))
                places.Add(new PlaceRef { Kind = "Detention", Id = f.Definition.Id, Position = f.Definition.Position, Capacity = f.Definition.Capacity, Label = "Detention " + f.Definition.Id });
        }

        private int S(string value)
        {
            if (!stringIds.TryGetValue(value, out int id)) { id = strings.Count; strings.Add(value); stringIds.Add(value, id); }
            return id;
        }
        private long TileAt(Coord c) => tiles.TryGetValue(c, out long id) ? id : 0;

        public void Consume(IReadOnlyList<TraceEvent> trace)
        {
            foreach (var e in trace)
            {
                TraceEvents++;
                int c = citizenIndex.TryGetValue(e.ActorId, out int index) ? index : -1;
                if (c >= 0) Track(c, e);
                if (Tracked.Contains(e.Type)) continue;
                long tile = Located.Contains(e.Type) || !e.Target.Equals(default(Coord)) ? TileAt(e.Target) : 0;
                events.Add(new[] { e.Id, e.Tick, S(e.Type), e.ActorId, S(e.Detail), tile, e.CauseId, e.MessageId, e.TerminalId, e.AttemptId, e.TaskId, e.ItemId, e.Quantity });
            }
        }

        private void Track(int c, TraceEvent e)
        {
            var key = (e.ActorId, e.ActionId);
            open.TryGetValue(key, out var act);
            switch (e.Type)
            {
                case "ObservationRecorded" when e.FactKey is FactKey fact && e.FactValue is FactValue value:
                {
                    string k = fact.ToString(), v = Format(value);
                    if (known.TryGetValue((c, k), out var previous) && previous == v) return;
                    known[(c, k)] = v;
                    int at = e.Detail.LastIndexOf("; source=", StringComparison.Ordinal);
                    learned[c].Add(new long[] { e.Tick, S(k), S(v), S(at >= 0 ? e.Detail.Substring(at + 9) : ""), e.Id, e.MessageId });
                    return;
                }
                case "KnowledgeEvicted" when e.FactKey is FactKey gone:
                    known.Remove((c, gone.ToString()));
                    learned[c].Add(new long[] { e.Tick, S(gone.ToString()), -1, 0, e.Id });
                    return;
                case "MovementStarted":
                    moves[c].Add(new long[] { e.Departure, e.Arrival, e.Source.X, e.Source.Z, e.Target.X, e.Target.Z, e.Source.Y, e.Target.Y });
                    return;
                case "TileArrived":
                    // Keep the segment honest if an edge ever ends earlier than scheduled.
                    if (moves[c].Count > 0 && moves[c][^1][1] != e.Tick && moves[c][^1][0] <= e.Tick)
                    { var m = moves[c][^1]; m[1] = e.Tick; m[4] = e.Target.X; m[5] = e.Target.Z; m[7] = e.Target.Y; }
                    return;
                case "ActionRequested":
                    act = new Act { Requested = e.Tick, Kind = S(e.Detail), RequestEvent = e.Id, Cause = e.CauseId, Origin = S(e.Origin.ToString()), Attempt = e.AttemptId, Terminal = e.TerminalId };
                    open[key] = act; actions[key] = act; acts[c].Add(act);
                    return;
            }
            if (act == null) return;
            switch (e.Type)
            {
                case "NeedServiceQueued": act.Queue = 1; act.Waiting = true; act.Began = -1; break;
                case "TerminalQueued": act.Queue = 2; act.Waiting = true; act.Began = -1; if (e.TerminalId != 0) act.Terminal = e.TerminalId; break;
                case "NeedServiceClaimed": case "TerminalQueueGranted": act.Waiting = false; act.Began = e.Tick; if (e.TerminalId != 0) act.Terminal = e.TerminalId; break;
                case "ActionStarted": if (!act.Waiting && act.Began < 0) act.Began = e.Tick; if (e.TerminalId != 0) act.Terminal = e.TerminalId; break;
                case "ConversationStarted": case "ConversationConsented":
                    int p = e.Detail.IndexOf("partner=", StringComparison.Ordinal);
                    if (p >= 0 && long.TryParse(new string(e.Detail.Skip(p + 8).TakeWhile(char.IsDigit).ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out long partner)) act.Partner = partner;
                    break;
                case "ActionCompleted": Close(act, key, e, 1); break;
                case "ActionFailed": act.Failure = S(e.Detail); Close(act, key, e, 2); break;
                case "ActionCancelled": Close(act, key, e, 3); break;
            }
        }
        private void Close(Act act, (long, long) key, TraceEvent e, int outcome)
        {
            act.End = e.Tick; act.Outcome = outcome; act.Waiting = false;
            if (act.Began < 0 && act.Queue == 0) act.Began = act.Requested;
            if (e.TerminalId != 0 && act.Terminal == 0) act.Terminal = e.TerminalId;
            open.Remove(key);
        }

        public void Sample(long tick)
        {
            frameTicks.Add(tick);
            var needState = world.InspectNeeds().ToDictionary(x => x.ActorId);
            var routines = world.InspectRoutines().ToDictionary(r => r.ActorId);
            var items = world.InspectItems();
            var equipped = world.InspectEquipment().ToDictionary(x => x.ActorId, x => x.ItemId);
            var carried = items.Where(i => i.Location.Kind == LocationKind.ActorInventory).GroupBy(i => i.Location.Owner)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.OrderBy(i => i.Resource, StringComparer.Ordinal).ThenBy(i => i.Id.Value)
                    .Select(i => i.Resource + (i.Quantity > 1 ? " ×" + i.Quantity.ToString(CultureInfo.InvariantCulture) : "") + (equipped.TryGetValue(g.Key, out long eq) && eq == i.Id.Value ? " (equipped)" : ""))));
            var held = new Dictionary<long, string>();
            var escorts = world.InspectEscorts().ToDictionary(x => x.AuthorizationId);
            foreach (var stay in world.InspectDetentionStays().OrderBy(x => x.AuthorizationId))
            {
                if (stay.Status is DetentionStayStatus.Cancelled or DetentionStayStatus.Released) continue;
                escorts.TryGetValue(stay.AuthorizationId, out var escort);
                bool moving = escort != null && escort.Status is EscortStatus.Escorting or EscortStatus.AwaitingIntake;
                held[stay.ActorId] = stay.Status == DetentionStayStatus.Reserved ? moving ? "Escorted (" + escort!.Status + ")" : "Awaiting escort" : stay.Status.ToString();
                if (moving) foreach (var member in escort!.Members) if (member.ActorId != stay.ActorId) held[member.ActorId] = "Escorting " + stay.ActorId;
            }
            for (int i = 0; i < citizens.Length; i++)
            {
                long id = citizens[i].Id.Value;
                if (needState.TryGetValue(id, out var n))
                {
                    for (int k = 0; k < rules.Length; k++) needs[i].Add(n.Values.FirstOrDefault(v => v.Kind == rules[k].Kind)?.Displayed ?? 0);
                    modes[i].Add(S(n.Mode.ToString()));
                }
                else { for (int k = 0; k < rules.Length; k++) needs[i].Add(0); modes[i].Add(0); }
                activities[i].Add(routines.TryGetValue(id, out var r) ? S(r.Activity) : 0);
                inventories[i].Add(carried.TryGetValue(id, out var inv) ? S(inv) : 0);
                custody[i].Add(held.TryGetValue(id, out var h) ? S(h) : 0);
            }
            var services = world.InspectNeedServices().ToDictionary(s => s.Definition.Id);
            var terminals = world.InspectTerminals().ToDictionary(t => t.Id);
            var machines = world.InspectMachines().ToDictionary(m => m.Id);
            var facilities = world.InspectDetentionFacilities().ToDictionary(f => f.Definition.Id);
            foreach (var p in places)
            {
                (long busy, long queued, long up) = p.Kind switch
                {
                    "Terminal" when terminals.TryGetValue(p.Id, out var t) => ((long)t.Claims.Count, (long)t.Queue.Count, !t.Enabled ? 0L : t.Connected ? 2L : 1L),
                    "Machine" when machines.TryGetValue(p.Id, out var m) => (0L, 0L, m.Operational ? 2L : 0L),
                    "Detention" when facilities.TryGetValue(p.Id, out var f) => ((long)f.Occupants, (long)f.Reservations, !f.Powered ? 0L : f.Connected ? 2L : 1L),
                    "Workstation" => (0L, 0L, 2L),
                    _ when services.TryGetValue(p.Id, out var s) => ((long)s.OccupantActionIds.Count, (long)s.QueuedActionIds.Count, s.Enabled ? 2L : 0L),
                    _ => (0L, 0L, 0L)
                };
                p.Busy.Add(busy); p.Queued.Add(queued); p.Up.Add(up);
            }
            foreach (var d in world.InspectDirt())
            {
                if (!dirt.TryGetValue(d.TileId, out var units)) { units = Enumerable.Repeat(0L, frameTicks.Count - 1).ToList(); dirt.Add(d.TileId, units); }
                units.Add(d.Units);
            }
            foreach (var units in dirt.Values) while (units.Count < frameTicks.Count) units.Add(0);
        }

        public void Finish()
        {
            foreach (var a in world.InspectNeedActivities())
                if (actions.TryGetValue((a.ActorId, a.ActionId), out var act)) act.Service = a.ServiceId;
            foreach (var r in world.InspectRosters().OrderBy(r => r.PublicationTick).ThenBy(r => r.Roster.Version))
                if (citizenIndex.TryGetValue(r.Roster.ActorId, out int c))
                    rosters[c].Add(new[] { r.PublicationTick, r.Roster.Version, r.Roster.Cohort, r.Roster.ShiftStart, r.Roster.DutyTicks, r.Roster.DayTicks });
            foreach (var tile in world.Topology.InspectTiles()) tiles[tile.Position] = tile.Id.Value;
        }

        public void Write(Utf8JsonWriter j, ulong seed, long every, long horizon, string digest)
        {
            var s = loaded.Scenario.InitialState;
            var definitions = world.Topology.InspectDefinitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
            j.WriteStartObject();
            j.WriteNumber("schemaVersion", SchemaVersion); j.WriteString("kind", "station_timeline");
            j.WriteString("scenario", s.Id); j.WriteString("source", loaded.Source); j.WriteNumber("seed", seed);
            j.WriteString("contentDigest", loaded.ContentDigest); j.WriteString("buildId", Runs.BuildId()); j.WriteString("finalDigest", digest);
            j.WriteNumber("tickMs", world.Config.TickDurationMs);
            j.WriteNumber("dayTicks", world.Routines?.DayTicks ?? 0); j.WriteNumber("dutyTicks", world.Routines?.DutyTicks ?? 0); j.WriteNumber("cohorts", world.Routines?.Cohorts ?? 0);
            j.WriteNumber("startTick", 0); j.WriteNumber("horizon", horizon); j.WriteNumber("every", every);
            j.WriteStartArray("strings"); foreach (var value in strings) j.WriteStringValue(value); j.WriteEndArray();
            j.WriteStartArray("rooms");
            foreach (var tile in world.Topology.InspectTiles().OrderBy(t => t.Id.Value))
            {
                j.WriteStartObject(); j.WriteNumber("id", tile.Id.Value); j.WriteString("definition", tile.DefinitionId);
                j.WriteString("family", definitions.TryGetValue(tile.DefinitionId, out var d) ? d.Family.ToString() : "");
                Position(j, "position", tile.Position); j.WriteNumber("traversalTicks", world.Topology.TraversalTicks(tile.Position)); j.WriteBoolean("completed", tile.Completed);
                j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteStartArray("places");
            foreach (var p in places)
            {
                j.WriteStartObject(); j.WriteString("kind", p.Kind); j.WriteNumber("id", p.Id); Position(j, "position", p.Position); j.WriteNumber("capacity", p.Capacity);
                j.WriteNumber("assignedActorId", p.Assigned); j.WriteNumber("detentionFacilityId", p.Detention); j.WriteString("label", p.Label);
                j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteStartArray("needs");
            foreach (var r in rules) { j.WriteStartObject(); j.WriteString("kind", r.Kind.ToString()); j.WriteNumber("seek", r.Seek); j.WriteNumber("urgent", r.Urgent); j.WriteNumber("critical", r.Critical); j.WriteEndObject(); }
            j.WriteEndArray();
            // Exact custody intervals; frame samples alone miss short escorts.
            var escorts = world.InspectEscorts().ToDictionary(e => e.AuthorizationId);
            j.WriteStartArray("custody");
            foreach (var stay in world.InspectDetentionStays().OrderBy(x => x.AuthorizationId))
            {
                escorts.TryGetValue(stay.AuthorizationId, out var escort);
                j.WriteStartObject(); j.WriteNumber("authorizationId", stay.AuthorizationId); j.WriteNumber("actorId", stay.ActorId); j.WriteNumber("leaderId", stay.LeaderId);
                j.WriteNumber("facilityId", stay.FacilityId); j.WriteString("status", stay.Status.ToString());
                j.WriteNumber("escortStarted", escort?.StartedTick ?? -1); j.WriteString("escortStatus", escort?.Status.ToString() ?? "");
                j.WriteStartArray("escortMembers"); foreach (var m in escort?.Members ?? Array.Empty<EscortMemberSnapshot>()) j.WriteNumberValue(m.ActorId); j.WriteEndArray();
                j.WriteNumber("intakeTick", stay.IntakeTick); j.WriteNumber("releaseTick", stay.ReleaseTick);
                j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteStartArray("citizens");
            for (int i = 0; i < citizens.Length; i++)
            {
                var a = citizens[i];
                j.WriteStartObject(); j.WriteNumber("id", a.Id.Value); j.WriteString("name", a.Name); j.WriteString("role", a.Role); j.WriteNumber("account", accounts[i]);
                Position(j, "position", a.Position);
                Rows(j, "rosters", rosters[i]); Rows(j, "knowledge", initialKnowledge[i]); Rows(j, "moves", moves[i]);
                Rows(j, "acts", acts[i].Select(x => x.Row())); Rows(j, "learned", learned[i]);
                j.WriteEndObject();
            }
            j.WriteEndArray();
            Rows(j, "events", events);
            j.WriteStartObject("frames");
            Numbers(j, "ticks", frameTicks);
            j.WriteStartArray("citizens");
            for (int i = 0; i < citizens.Length; i++)
            {
                j.WriteStartObject(); Numbers(j, "needs", needs[i]); Numbers(j, "mode", modes[i]); Numbers(j, "activity", activities[i]);
                Numbers(j, "inventory", inventories[i]); Numbers(j, "custody", custody[i]); j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteStartArray("places");
            foreach (var p in places) { j.WriteStartObject(); Numbers(j, "busy", p.Busy); Numbers(j, "queue", p.Queued); Numbers(j, "up", p.Up); j.WriteEndObject(); }
            j.WriteEndArray();
            j.WriteStartArray("dirt");
            foreach (var (tile, units) in dirt) { j.WriteStartObject(); j.WriteNumber("tileId", tile); Numbers(j, "units", units); j.WriteEndObject(); }
            j.WriteEndArray();
            j.WriteEndObject();
            j.WriteEndObject();
        }
        private static void Position(Utf8JsonWriter j, string name, Coord c) { j.WriteStartArray(name); j.WriteNumberValue(c.X); j.WriteNumberValue(c.Y); j.WriteNumberValue(c.Z); j.WriteEndArray(); }
        private static void Numbers(Utf8JsonWriter j, string name, List<long> values) { j.WriteStartArray(name); foreach (long v in values) j.WriteNumberValue(v); j.WriteEndArray(); }
        private static void Rows(Utf8JsonWriter j, string name, IEnumerable<long[]> rows)
        {
            j.WriteStartArray(name);
            foreach (var row in rows)
            {
                int n = row.Length; while (n > 0 && row[n - 1] == 0) n--;
                j.WriteStartArray(); for (int i = 0; i < n; i++) j.WriteNumberValue(row[i]); j.WriteEndArray();
            }
            j.WriteEndArray();
        }
    }
}
