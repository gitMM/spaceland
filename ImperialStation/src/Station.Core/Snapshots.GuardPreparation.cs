using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteGuardPreparation(BinaryWriter w)
        {
            w.Write(equippedItems.Count);
            foreach (var e in equippedItems.Values) { w.Write(e.ActorId); w.Write(e.ItemId); w.Write(e.EquippedTick); w.Write(e.ActionId); }
        }
        private void ReadGuardPreparation(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var e = new EquipmentSnapshot { ActorId = r.ReadInt64(), ItemId = r.ReadInt64(), EquippedTick = r.ReadInt64(), ActionId = r.ReadInt64() };
                if (!actors.TryGetValue(new Id<ActorTag>(e.ActorId), out var actor) || !items.TryGetValue(new Id<ItemTag>(e.ItemId), out var item) || !HeldBy(item, actor) || item.Quantity < 1 ||
                    e.EquippedTick < 0 || e.EquippedTick > LastProcessedTick || e.ActionId < 1 || e.ActionId >= nextAction || credentials.Values.Any(c => c.Definition.Card == item.Id)) throw new ArgumentException("Invalid equipped inventory.");
                equippedItems.Add(e.ActorId, e);
            }
            foreach (var l in localGuardOrders.Values)
            {
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.EvaluateGuard && e.Value == l.OrderMessageId).ToArray();
                if (l.Backoff < planner.InitialBackoff || l.Backoff > planner.MaximumBackoff || l.NextDecision < -1 || timers.Length != (l.NextDecision < 0 ? 0 : 1) ||
                    timers.Any(e => e.Phase != EventPhase.Decision || e.Tick != l.NextDecision) || l.NextDecision >= 0 && (!GuardPreparing(l) || RuntimeError == null && l.NextDecision <= LastProcessedTick) ||
                    l.ReadyTick < -1 || l.ReadyTick > LastProcessedTick || l.ReadyTick >= 0 && (l.ReadyTick < l.ReadTick || l.ReadyTick >= l.Payload.Expires || l.ReadyItemId < 1 || l.ReadyItemId >= nextItem) ||
                    l.ReadyTick == -1 && l.ReadyItemId != 0 || (l.Status is GuardOrderStatus.AtRendezvous or GuardOrderStatus.WaitingTeam) && l.ReadyTick < 0) throw new ArgumentException("Invalid guard preparation state.");
                if ((l.Status is GuardOrderStatus.AtRendezvous or GuardOrderStatus.WaitingTeam) && (!equippedItems.TryGetValue(l.ActorId, out var equipment) || items[new Id<ItemTag>(equipment.ItemId)].Resource != l.Payload.Dispatch.RequiredEquipment ||
                    actors[new Id<ActorTag>(l.ActorId)].Position != l.Payload.Dispatch.Rendezvous || actors[new Id<ActorTag>(l.ActorId)].Action?.Kind == ActionKind.Move)) throw new ArgumentException("Guard readiness has no physical equipment or rendezvous presence.");
            }
            if (scheduled.Any(e => e.Kind == ScheduledKind.EvaluateGuard && !localGuardOrders.ContainsKey(e.Value))) throw new ArgumentException("Unknown guard preparation timer owner.");
            foreach (var actor in actors.Values.Where(a => a.Action != null))
            {
                var action = actor.Action!;
                if (action.GuardOrderId > 0 && (!localGuardOrders.TryGetValue(action.GuardOrderId, out var l) || l.ActorId != actor.Id.Value || l.Response != "Accepted" || l.AcknowledgmentMessageId == 0 ||
                    l.ReadTick > action.Started || l.Payload.Expires <= action.Started || !(action.Kind is ActionKind.Move or ActionKind.PickUp or ActionKind.Equip or ActionKind.JoinGuardTeam or ActionKind.InspectActor or ActionKind.BeginEscort or ActionKind.RegisterIntake) ||
                    l.Payload.Expires <= LastProcessedTick && action.Kind == ActionKind.Move && !action.CancelAtBoundary)) throw new ArgumentException("Guard action lacks a valid read copy.");
                if (action.Kind == ActionKind.Equip && (action.Completes != checked(action.Started + EquipTicks) || ValidateEquip(actor, action) != FailureReason.None ||
                    !resourceClaims.Any(c => c.Kind == ClaimKind.Item && c.ResourceId == action.Item.Value && c.ActorId == actor.Id.Value && c.ActionId == action.Id.Value))) throw new ArgumentException("Invalid active equipment action.");
            }
        }
    }
}
