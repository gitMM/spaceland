using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class EquipmentSnapshot
    {
        public long ActorId { get; internal set; }
        public long ItemId { get; internal set; }
        public long EquippedTick { get; internal set; }
        public long ActionId { get; internal set; }
        internal EquipmentSnapshot Copy() => (EquipmentSnapshot)MemberwiseClone();
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, EquipmentSnapshot> equippedItems = new SortedDictionary<long, EquipmentSnapshot>();
        public IReadOnlyList<EquipmentSnapshot> InspectEquipment() => equippedItems.Values.Select(e => e.Copy()).ToArray();
        private Id<ItemTag> EquippedItem(Id<ActorTag> actor) => equippedItems.TryGetValue(actor.Value, out var e) ? new Id<ItemTag>(e.ItemId) : default;
        private long EquipTicks => Detention?.Escort?.EquipTicks ?? 50;
        private FailureReason ValidateEquip(Actor actor, ActiveAction action)
        {
            if (actor.Position != action.Target) return FailureReason.NotAtRequiredTile;
            if (!items.TryGetValue(action.Item, out var item) || !HeldBy(item, actor) || item.Quantity < 1) return FailureReason.NotHeld;
            if (credentials.Values.Any(c => c.Definition.Card == item.Id)) return FailureReason.ProtectedItem;
            return ClaimedByOther(ClaimKind.Item, item.Id.Value, action.Id.Value) ? FailureReason.ResourceClaimed : FailureReason.None;
        }
        private void StartEquip(Actor actor, ActiveAction action)
        {
            var failure = ValidateEquip(actor, action); if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            action.Completes = checked(processingTick + EquipTicks);
            AddClaim(ClaimKind.Item, action.Item.Value, actor, action); actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, action.Item, target: action.Target, arrival: action.Completes, detail: "Equip", causeId: action.CauseId); ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteEquip(Actor actor, ActiveAction action)
        {
            var failure = ValidateEquip(actor, action);
            if (failure != FailureReason.None) { ReleaseResourceClaims(action.Id.Value); actor.Action = null; Fail(actor, action.Id, failure); return; }
            equippedItems[actor.Id.Value] = new EquipmentSnapshot { ActorId = actor.Id.Value, ItemId = action.Item.Value, EquippedTick = processingTick, ActionId = action.Id.Value };
            ObserveOwnTransfer(actor, items[action.Item], action.CauseId);
            Emit("EquipmentEquipped", actor, action.Id, action.Item, target: actor.Position, detail: items[action.Item].Resource, causeId: action.CauseId); Finish(actor);
        }
        private void RefreshEquipment(Actor actor)
        {
            if (!equippedItems.TryGetValue(actor.Id.Value, out var e) || items.TryGetValue(new Id<ItemTag>(e.ItemId), out var item) && HeldBy(item, actor) && item.Quantity > 0) return;
            equippedItems.Remove(actor.Id.Value); Emit("EquipmentRemoved", actor, item: new Id<ItemTag>(e.ItemId), detail: "ItemNoLongerHeld");
        }
    }
}
