using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private CleanlinessSeed? cleanliness;
        private readonly SortedDictionary<long, DirtSnapshot> dirt = new SortedDictionary<long, DirtSnapshot>();
        public IReadOnlyList<DirtSnapshot> InspectDirt() => dirt.Values.Select(d => d.Copy()).ToArray();
        private void InitializeCleanliness(CleanlinessSeed seed)
        {
            cleanliness = seed;
            foreach (var tile in Topology.ById.Values.Where(t => t.Completed && Topology.Definitions[t.DefinitionId].Capabilities.Contains("Cleanable", StringComparer.Ordinal)))
            {
                ValidateWorkAccess(tile.Position, seed.Access);
                dirt.Add(tile.Id.Value, new DirtSnapshot { TileId = tile.Id.Value, Position = tile.Position });
            }
            foreach (var entry in seed.Dirt)
            {
                if (!dirt.TryGetValue(entry.Tile.Value, out var state)) throw new ArgumentException("Initial dirt requires a completed Cleanable tile.");
                state.Units = state.InitialUnits = entry.Units;
            }
            if (seed.MeanSoilingTicks == 0 || dirt.Count == 0) return;
            streams.Add("soiling", Pcg32.Create(Seed, "soiling"));
            foreach (var state in dirt.Values) ScheduleSoiling(state, 0);
        }
        private void ScheduleSoiling(DirtSnapshot state, long from)
        {
            int mean = cleanliness!.MeanSoilingTicks, low = mean / 2, high = (int)(3L * mean / 2);
            state.NextSoilingTick = checked(from + low + streams["soiling"].Next(high - low + 1));
            Schedule(new ScheduledEvent { Tick = state.NextSoilingTick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.Soiling, Value = state.TileId });
        }
        private void Soil(long tileId)
        {
            var state = dirt[tileId]; state.NextSoilingTick = -1;
            AddDirt(state, 1, "Soiling"); ScheduleSoiling(state, processingTick);
        }
        public void ScheduleSpill(long tick, Coord tile, long? units = null)
        {
            ValidateFuture(tick);
            if (cleanliness == null || !Topology.Tiles.TryGetValue(tile, out var definition) || !dirt.ContainsKey(definition.Id.Value) || units <= 0)
                throw new ArgumentException("Spill requires positive units and a completed Cleanable tile.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.Spill,
                Value = definition.Id.Value, ActionId = units ?? cleanliness.SpillUnits });
        }
        private void AddDirt(DirtSnapshot state, long units, string reason)
        {
            long next = checked(state.Units + units), total = checked(state.AddedUnits + units);
            state.Units = next; state.AddedUnits = total;
            long cause = Emit("DirtAdded", target: state.Position, quantity: units, detail: reason,
                factKey: new FactKey(SubjectKind.Tile, state.TileId, AttributeKind.Dirt), factValue: FactValue.Integer(next));
            SenseDirtLocally(state.Position, cause);
        }
        private void SenseDirtLocally(Coord tile, long cause)
        {
            foreach (var actor in actors.Values.Where(a => a.Position == tile && a.Action?.Kind != ActionKind.Move)) ScheduleSensing(actor, cause);
        }
        private void ObserveDirt(Actor actor, bool passive, long sourceId, long cause)
        {
            if (Information == null || !dirt.TryGetValue(Topology.Tiles[actor.Position].Id.Value, out var state)) return;
            ObservePerceived(actor, new FactKey(SubjectKind.Tile, state.TileId, AttributeKind.Dirt), FactValue.Integer(state.Units),
                passive ? EvidenceKind.Direct : EvidenceKind.Inspection, sourceId, cause, passive);
        }
        private FailureReason ValidateCleaning(Actor actor, ActiveAction action, bool bind)
        {
            if (cleanliness == null || !Topology.Tiles.TryGetValue(action.Target, out var tile) || !dirt.ContainsKey(tile.Id.Value)) return FailureReason.RequiredServiceNotPresent;
            return CheckWorkAccess(actor, action, tile.Position, cleanliness.Access, bind);
        }
        private void StartCleaning(Actor actor, ActiveAction action)
        {
            var failure = ValidateCleaning(actor, action, true);
            if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            action.Completes = checked(processingTick + cleanliness!.CleanUnitTicks);
            // Tiles are not exclusively claimed: two workers may arrive with stale orders.
            // Each completion removes at most one unit from the then-current state.
            foreach (var material in action.Materials) AddClaim(ClaimKind.Item, material.Item.Value, actor, action);
            if (cleanliness.Access.Staff.Value > 0) AddClaim(ClaimKind.Staff, cleanliness.Access.Staff.Value, actor, action);
            actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            ObserveDirt(actor, false, action.Id.Value, action.CauseId);
            var state = dirt[Topology.Tiles[action.Target].Id.Value];
            Emit("CleaningArrivalChecked", actor, action.Id, target: action.Target, quantity: state.Units, causeId: action.CauseId,
                detail: state.Units == 0 ? "NoWorkNeeded" : "DirtPresent");
            Emit("ActionStarted", actor, action.Id, target: action.Target, arrival: action.Completes, detail: "Clean");
            ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteCleaning(Actor actor, ActiveAction action)
        {
            var failure = ValidateCleaning(actor, action, false);
            if (failure != FailureReason.None) { ReleaseResourceClaims(action.Id.Value); actor.Action = null; Fail(actor, action.Id, failure); return; }
            var state = dirt[Topology.Tiles[action.Target].Id.Value]; long removed = state.Units > 0 ? 1 : 0;
            long total = checked(state.RemovedUnits + removed); state.Units -= removed; state.RemovedUnits = total;
            // Physical target completion belongs to every job for this tile.
            // Concurrent workers may observe the last unit removed by somebody
            // else and finish their orders without another cleaning action.
            // This changes no institutional report or individual work credit.
            if (state.Units == 0)
                foreach (var job in jobs.Values.Where(j => j.Kind == WorkKind.CleanTile && j.TileId == state.TileId))
                    job.PhysicalStatus = WorkExecution.CompletedPhysically;
            if (localOrders.TryGetValue(action.AttemptId, out var order) && order.ActorId == actor.Id.Value && order.Kind == WorkKind.CleanTile && order.TileId == state.TileId)
                order.CleanedUnits = checked(order.CleanedUnits + removed);
            long cause = Emit("CleaningCompleted", actor, action.Id, target: state.Position, quantity: removed, causeId: action.CauseId,
                detail: removed == 0 ? "NoWorkNeeded" : "DirtRemoved", factKey: new FactKey(SubjectKind.Tile, state.TileId, AttributeKind.Dirt), factValue: FactValue.Integer(state.Units));
            ObserveDirt(actor, false, action.Id.Value, cause); SenseDirtLocally(state.Position, cause); Finish(actor);
        }
        private void AssertDirtAccounting()
        {
            foreach (var state in dirt.Values)
                if (state.Units < 0 || state.InitialUnits < 0 || state.AddedUnits < 0 || state.RemovedUnits < 0 ||
                    (decimal)state.Units != (decimal)state.InitialUnits + state.AddedUnits - state.RemovedUnits) throw new InvalidOperationException("Dirt accounting mismatch.");
        }
    }
}
