using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        public const int SnapshotVersion = 51;
        public const string MovementModel = "orthogonal-mean-ceiling-frozen-v1";
        public const string RandomModel = "pcg-xsh-rr64-32-fnv1a-splitmix-v1";
        // Explicit in-memory encoding. File access belongs to Runner.
        public byte[] Serialize() => Encode(true);
        public string Digest() => Hash(Encode(false));
        public static string Hash(byte[] bytes)
        { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))); }
        private byte[] Encode(bool presentation)
        {
            using var buffer = new MemoryStream(); using var w = new BinaryWriter(buffer, new UTF8Encoding(false, true), true);
            w.Write("ImperialStation"); w.Write(SnapshotVersion); w.Write(MovementModel); w.Write(RandomModel);
            w.Write(Identity); w.Write(Seed); w.Write(Config.TickDurationMs); w.Write(Config.ItemActionTicks); w.Write(Config.PathBudget); w.Write(Config.MaxEventsPerTick); w.Write(Config.AuthorityNominalTicks);
            w.Write(LastProcessedTick); w.Write(nextSequence); w.Write(nextAction); w.Write(nextTrace); w.Write(Topology.Version);
            w.Write(PathRequests); w.Write(PathExpansions); w.Write(ActionsCompleted); w.Write(ActionsFailed); w.Write(RuntimeError ?? "");
            w.Write(Topology.Definitions.Count);
            foreach (var d in Topology.Definitions.Values)
            { w.Write(d.Id); w.Write((int)d.Family); w.Write(d.TraversalTicks); w.Write(presentation ? d.VisualKey : ""); w.Write(d.Capabilities.Count); foreach (string c in d.Capabilities) w.Write(c); WriteConstructionDefinition(w, d.Construction); }
            w.Write(Topology.ById.Count);
            foreach (var t in Topology.ById.Values)
            { w.Write(t.Id.Value); w.Write(t.DefinitionId); WriteCoord(w, t.Position); w.Write(t.Completed); w.Write(t.TraversalOverride.HasValue); if (t.TraversalOverride.HasValue) w.Write(t.TraversalOverride.Value); }
            WriteWorkDefinitions(w);
            w.Write(actors.Count);
            foreach (var a in actors.Values)
            {
                w.Write(a.Id.Value); w.Write(presentation ? a.Name : ""); w.Write(a.Role); WriteCoord(w, a.Position); w.Write(a.Account.Value); w.Write((int)a.LastStatus); w.Write((int)a.LastFailure); w.Write(a.Action != null);
                if (a.Action != null)
                {
                    var x = a.Action; w.Write(x.Id.Value); w.Write((int)x.Kind); WriteCoord(w, x.Target); w.Write(x.Item.Value); w.Write(x.Started); w.Write(x.Completes);
                    w.Write(x.Route.Count); foreach (var c in x.Route) WriteCoord(w, c); w.Write(x.RouteIndex); WriteCoord(w, x.Source); WriteCoord(w, x.Destination); w.Write(x.Departure); w.Write(x.Arrival);
                    w.Write(x.ServiceId); w.Write(x.SessionId); w.Write(x.CredentialId); w.Write(x.CauseId); w.Write(x.Waiting); w.Write(x.CancelAtBoundary); WriteDraft(w, x.Message);
                    w.Write(x.CapturedMessages.Count); foreach (long id in x.CapturedMessages) w.Write(id);
                    w.Write(x.RecipeId); w.Write(x.AttemptId); w.Write(x.Materials.Count); foreach (var b in x.Materials) { w.Write(b.Item.Value); w.Write(b.Quantity); }
                    WriteQuery(w, x.Query); WriteRecordPage(w, x.CapturedRecords);
                    w.Write(x.Quantity); w.Write(x.OtherItem.Value); w.Write(x.TargetActor.Value); w.Write(x.PatrolOrderId); w.Write(x.GuardOrderId); w.Write((int)x.ConversationTopic);
                }
            }
            w.Write(items.Count);
            foreach (var i in items.Values) { w.Write(i.Id.Value); w.Write(i.Resource); w.Write(i.Quantity); w.Write((int)i.Location.Kind); w.Write(i.Location.Owner); }
            w.Write(initialBalances.Count); foreach (var kv in initialBalances) { w.Write(kv.Key); w.Write(kv.Value); }
            w.Write(streams.Count); foreach (var kv in streams) { w.Write(kv.Key); w.Write(kv.Value.State); w.Write(kv.Value.Increment); }
            w.Write(scheduled.Count);
            foreach (var e in scheduled)
            { w.Write(e.Tick); w.Write((int)e.Phase); w.Write(e.Sequence); w.Write((int)e.Kind); w.Write(e.Intent.Actor.Value); w.Write((int)e.Intent.Kind); WriteCoord(w, e.Intent.Target); w.Write(e.Intent.Item.Value); w.Write(e.Intent.ServiceId); WriteDraft(w, e.Intent.Message); w.Write(e.Intent.RecipeId); w.Write(e.Intent.AttemptId); WriteQuery(w, e.Intent.Query); w.Write(e.Intent.Quantity); w.Write(e.Intent.OtherItem.Value); w.Write(e.Intent.TargetActor.Value); w.Write(e.Intent.PatrolOrderId); w.Write(e.Intent.GuardOrderId); w.Write((int)e.Intent.ConversationTopic); w.Write(e.ActionId); w.Write(e.Value); w.Write(e.CauseId); }
            WriteInformation(w);
            WriteConstruction(w); WriteWorkState(w);
            WriteJobs(w);
            WriteRecords(w);
            WriteStacks(w);
            WriteSensors(w);
            WriteRoutines(w);
            WriteAuthority(w);
            WriteQualityAssurance(w);
            WriteAudits(w);
            WritePersonnel(w);
            WriteDetention(w);
            WriteEscortDispatch(w);
            WriteGuardPreparation(w);
            WriteGuardSearch(w);
            WriteEscorts(w);
            WriteDetentionServices(w); WriteGuardOutcomes(w);
            WriteMachineWear(w);
            WriteCleanliness(w);
            WriteFacility(w); WriteNeeds(w); WriteNeedServices(w); WriteBerths(w); WriteConversations(w); WriteNeedAgents(w); WriteDailyGoals(w); WriteGrowth(w); WriteTerminalOutages(w); WriteServiceEstimates(w);
            w.Flush(); return buffer.ToArray();
        }
        private static void WriteCoord(BinaryWriter w, Coord c) { w.Write(c.X); w.Write(c.Y); w.Write(c.Z); }
        private static Coord ReadCoord(BinaryReader r) => new Coord(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        private static int Count(BinaryReader r)
        { int n = r.ReadInt32(); if (n < 0 || n > 10000000) throw new ArgumentException("Invalid snapshot collection length."); return n; }
        private static T ReadEnum<T>(BinaryReader r) where T : struct, Enum
        { int value = r.ReadInt32(); if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentException($"Invalid snapshot {typeof(T).Name}."); return (T)Enum.ToObject(typeof(T), value); }
        public static World Restore(byte[] data, bool traceEnabled = false)
        {
            using var buffer = new MemoryStream(data, false); using var r = new BinaryReader(buffer, new UTF8Encoding(false, true));
            if (r.ReadString() != "ImperialStation" || r.ReadInt32() != SnapshotVersion || r.ReadString() != MovementModel || r.ReadString() != RandomModel)
                throw new ArgumentException("Unsupported snapshot format, movement model or RNG version.");
            string identity = r.ReadString(); ulong seed = r.ReadUInt64(); var config = new WorldConfig(r.ReadInt32(), r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64());
            long last = r.ReadInt64(), seq = r.ReadInt64(), actionSeq = r.ReadInt64(), traceSeq = r.ReadInt64(), topologyVersion = r.ReadInt64();
            long paths = r.ReadInt64(), expansions = r.ReadInt64(), completed = r.ReadInt64(), failed = r.ReadInt64(); string error = r.ReadString();
            var definitions = new List<TileDefinition>(); int count = Count(r);
            for (int i = 0; i < count; i++)
            { string id = r.ReadString(); var family = ReadEnum<TileFamily>(r); long ticks = r.ReadInt64(); string visual = r.ReadString(); int n = Count(r); var caps = new List<string>(); for (int j = 0; j < n; j++) caps.Add(r.ReadString()); definitions.Add(new TileDefinition(id, family, ticks, visual, caps, ReadConstructionDefinition(r))); }
            var tiles = new List<Tile>(); count = Count(r);
            for (int i = 0; i < count; i++) { var id = new Id<TileTag>(r.ReadInt64()); string def = r.ReadString(); var c = ReadCoord(r); bool built = r.ReadBoolean(); long? cost = r.ReadBoolean() ? r.ReadInt64() : (long?)null; tiles.Add(new Tile(id, def, c, built, cost)); }
            var work = ReadWorkDefinitions(r);
            var population = new List<Actor>(); count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new Actor { Id = new Id<ActorTag>(r.ReadInt64()), Name = r.ReadString(), Role = r.ReadString(), Position = ReadCoord(r), Account = new Id<AccountTag>(r.ReadInt64()), LastStatus = ReadEnum<ActionStatus>(r), LastFailure = ReadEnum<FailureReason>(r) };
                if (r.ReadBoolean())
                {
                    var x = new ActiveAction { Id = new Id<ActionTag>(r.ReadInt64()), Kind = ReadEnum<ActionKind>(r), Target = ReadCoord(r), Item = new Id<ItemTag>(r.ReadInt64()), Started = r.ReadInt64(), Completes = r.ReadInt64() };
                    int n = Count(r); for (int j = 0; j < n; j++) x.Route.Add(ReadCoord(r)); x.RouteIndex = r.ReadInt32(); x.Source = ReadCoord(r); x.Destination = ReadCoord(r); x.Departure = r.ReadInt64(); x.Arrival = r.ReadInt64();
                    x.ServiceId = r.ReadInt64(); x.SessionId = r.ReadInt64(); x.CredentialId = r.ReadInt64(); x.CauseId = r.ReadInt64(); x.Waiting = r.ReadBoolean(); x.CancelAtBoundary = r.ReadBoolean(); x.Message = ReadDraft(r);
                    n = Count(r); for (int j = 0; j < n; j++) x.CapturedMessages.Add(r.ReadInt64()); a.Action = x;
                    x.RecipeId = r.ReadInt64(); x.AttemptId = r.ReadInt64(); n = Count(r); for (int j = 0; j < n; j++) x.Materials.Add(new MaterialBinding { Item = new Id<ItemTag>(r.ReadInt64()), Quantity = r.ReadInt64() });
                    x.Query = ReadQuery(r); x.CapturedRecords = ReadRecordPage(r);
                    x.Quantity = r.ReadInt64(); x.OtherItem = new Id<ItemTag>(r.ReadInt64()); x.TargetActor = new Id<ActorTag>(r.ReadInt64()); x.PatrolOrderId = r.ReadInt64(); x.GuardOrderId = r.ReadInt64(); x.ConversationTopic = ReadEnum<ConversationTopic>(r);
                }
                population.Add(a);
            }
            var inventory = new List<Item>(); count = Count(r);
            for (int i = 0; i < count; i++) inventory.Add(new Item(new Id<ItemTag>(r.ReadInt64()), r.ReadString(), r.ReadInt64(), new ItemLocation(ReadEnum<LocationKind>(r), r.ReadInt64())));
            var world = new World(identity, seed, config, definitions, tiles, population.Select(a => new ActorSeed(a.Id, a.Name, a.Role, a.Position, a.Account)), inventory, traceEnabled, work: work)
            { LastProcessedTick = last, nextSequence = seq, nextAction = actionSeq, nextTrace = traceSeq, PathRequests = paths, PathExpansions = expansions, ActionsCompleted = completed, ActionsFailed = failed, RuntimeError = error.Length == 0 ? null : error };
            world.Topology.Version = topologyVersion; world.actors.Clear(); foreach (var a in population) world.actors.Add(a.Id, a);
            world.initialBalances.Clear(); count = Count(r); for (int i = 0; i < count; i++) world.initialBalances.Add(r.ReadString(), r.ReadInt64());
            world.streams.Clear(); count = Count(r); for (int i = 0; i < count; i++) world.streams.Add(r.ReadString(), Pcg32.Restore(r.ReadUInt64(), r.ReadUInt64()));
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var e = new ScheduledEvent { Tick = r.ReadInt64(), Phase = ReadEnum<EventPhase>(r), Sequence = r.ReadInt64(), Kind = ReadEnum<ScheduledKind>(r) };
                e.Intent = new ActionIntent(new Id<ActorTag>(r.ReadInt64()), ReadEnum<ActionKind>(r), ReadCoord(r), new Id<ItemTag>(r.ReadInt64()), r.ReadInt64(), ReadDraft(r), r.ReadInt64(), r.ReadInt64(), ReadQuery(r), r.ReadInt64(), new Id<ItemTag>(r.ReadInt64()), new Id<ActorTag>(r.ReadInt64()), r.ReadInt64(), r.ReadInt64(), ReadEnum<ConversationTopic>(r)); e.ActionId = r.ReadInt64(); e.Value = r.ReadInt64(); e.CauseId = r.ReadInt64();
                if ((e.Tick <= last && error.Length == 0) || !world.scheduled.Add(e)) throw new ArgumentException("Duplicate or already processed snapshot event.");
            }
            world.ReadInformation(r);
            world.ReadConstruction(r); world.ReadWorkState(r);
            world.ReadJobs(r);
            world.ReadRecords(r);
            world.ReadStacks(r);
            world.ReadSensors(r);
            world.ReadRoutines(r);
            world.ReadAuthority(r);
            world.ReadQualityAssurance(r);
            world.ReadAudits(r);
            world.ReadPersonnel(r);
            world.ReadDetention(r);
            world.ReadEscortDispatch(r);
            world.ReadGuardPreparation(r);
            world.ReadGuardSearch(r);
            world.ReadEscorts(r);
            world.ReadDetentionServices(r); world.ReadGuardOutcomes(r);
            world.ReadMachineWear(r);
            world.ReadCleanliness(r);
            world.ReadFacility(r); world.ReadNeeds(r); world.ReadNeedServices(r); world.ReadBerths(r); world.ReadConversations(r); world.ReadNeedAgents(r); world.ReadDailyGoals(r); world.ReadGrowth(r); world.ReadTerminalOutages(r); world.ReadServiceEstimates(r);
            if (buffer.Position != buffer.Length) throw new ArgumentException("Trailing snapshot data.");
            if (last < -1 || seq < 1 || actionSeq < 1 || traceSeq < 1) throw new ArgumentException("Invalid snapshot counters.");
            world.ValidateRecoverySessions(); world.ValidateConstructionState(); world.ValidateGrowthState(); world.AssertAccounting(); return world;
        }
    }
}
