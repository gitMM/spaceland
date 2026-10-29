using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteCleanliness(BinaryWriter w)
        {
            w.Write(cleanliness != null); if (cleanliness == null) return;
            w.Write(cleanliness.CleanUnitTicks); w.Write(cleanliness.MeanSoilingTicks); w.Write(cleanliness.SpillUnits); WriteAccess(w, cleanliness.Access);
            w.Write(dirt.Count);
            foreach (var state in dirt.Values)
            { w.Write(state.TileId); w.Write(state.Units); w.Write(state.InitialUnits); w.Write(state.AddedUnits); w.Write(state.RemovedUnits); w.Write(state.NextSoilingTick); }
        }
        private void ReadCleanliness(BinaryReader r)
        {
            if (r.ReadBoolean())
            {
                long duration = r.ReadInt64(); int mean = r.ReadInt32(); long spill = r.ReadInt64();
                cleanliness = new CleanlinessSeed(cleanUnitTicks: duration, meanSoilingTicks: mean, spillUnits: spill, access: ReadAccess(r));
                int count = Count(r);
                for (int i = 0; i < count; i++)
                {
                    var state = new DirtSnapshot { TileId = r.ReadInt64(), Units = r.ReadInt64(), InitialUnits = r.ReadInt64(), AddedUnits = r.ReadInt64(), RemovedUnits = r.ReadInt64(), NextSoilingTick = r.ReadInt64() };
                    if (!Topology.ById.TryGetValue(new Id<TileTag>(state.TileId), out var tile) || !tile.Completed || !Topology.Definitions[tile.DefinitionId].Capabilities.Contains("Cleanable", StringComparer.Ordinal))
                        throw new ArgumentException("Dirt state requires a completed Cleanable tile.");
                    state.Position = tile.Position; ValidateWorkAccess(state.Position, cleanliness.Access); dirt.Add(state.TileId, state);
                    if (state.Units < 0 || state.InitialUnits < 0 || state.AddedUnits < 0 || state.RemovedUnits < 0 || (decimal)state.Units != (decimal)state.InitialUnits + state.AddedUnits - state.RemovedUnits)
                        throw new ArgumentException("Invalid dirt balance.");
                    var timers = scheduled.Where(e => e.Kind == ScheduledKind.Soiling && e.Value == state.TileId).ToArray();
                    bool expected = mean > 0;
                    if (state.NextSoilingTick < -1 || !expected && state.NextSoilingTick != -1 || expected && RuntimeError == null && state.NextSoilingTick <= LastProcessedTick ||
                        timers.Length != (state.NextSoilingTick < 0 ? 0 : 1) || timers.Any(e => e.Tick != state.NextSoilingTick || e.Phase != EventPhase.Disturbance))
                        throw new ArgumentException("Invalid soiling timer.");
                }
                if (dirt.Count != Topology.ById.Values.Count(t => t.Completed && Topology.Definitions[t.DefinitionId].Capabilities.Contains("Cleanable", StringComparer.Ordinal)))
                    throw new ArgumentException("Incomplete Cleanable tile registry.");
            }
            if (streams.ContainsKey("soiling") != (cleanliness != null && cleanliness.MeanSoilingTicks > 0 && dirt.Count > 0)) throw new ArgumentException("Invalid soiling stream.");
            if (JobsEnabled && !WorkEnabled && cleanliness == null || jobs.Values.Any(j => j.Kind == WorkKind.CleanTile && !dirt.ContainsKey(j.TileId))) throw new ArgumentException("Cleanup jobs require physical dirt state.");
            foreach (var e in scheduled.Where(e => e.Kind is ScheduledKind.Soiling or ScheduledKind.Spill))
                if (!dirt.ContainsKey(e.Value) || e.Phase != EventPhase.Disturbance || e.Kind == ScheduledKind.Spill && e.ActionId <= 0)
                    throw new ArgumentException("Invalid dirt disturbance.");
            foreach (var actor in actors.Values.Where(a => a.Action?.Kind == ActionKind.Clean))
            {
                var action = actor.Action!;
                bool Has(ClaimKind kind, long id) => resourceClaims.Any(c => c.Kind == kind && c.ResourceId == id && c.ActionId == action.Id.Value && c.ActorId == actor.Id.Value);
                if (cleanliness == null || ValidateCleaning(actor, action, false) != FailureReason.None || action.Completes != checked(action.Started + cleanliness.CleanUnitTicks) ||
                    action.Materials.Count != (cleanliness.Access.ToolResource.Length == 0 ? 0 : 1) || action.Materials.Any(b => b.Quantity != 0 || !Has(ClaimKind.Item, b.Item.Value)) ||
                    cleanliness.Access.Staff.Value > 0 && !Has(ClaimKind.Staff, cleanliness.Access.Staff.Value) ||
                    RuntimeError == null && scheduled.Count(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == action.Id.Value && e.Tick == action.Completes && e.Intent.Actor == actor.Id) != 1)
                    throw new ArgumentException("Invalid active cleaning action or claims.");
            }
        }
    }
}
