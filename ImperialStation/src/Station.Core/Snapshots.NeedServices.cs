using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteNeedServices(BinaryWriter w)
        {
            w.Write(needServices.Count);
            foreach (var s in needServices.Values)
            {
                var d = s.Definition; w.Write(d.Id); w.Write((int)d.Kind); WriteCoord(w, d.Position); w.Write(d.Capacity); w.Write(d.DurationTicks); w.Write(d.MealResource); w.Write(d.AssignedActorId); w.Write(d.DetentionFacilityId); w.Write(d.Enabled); w.Write(d.AllocationTicks); w.Write(d.AllocationRequiresCredential);
                w.Write(s.Enabled); w.Write(s.AssignedActorId); w.Write(s.NextGrant); w.Write(s.Queue.Count); foreach (long id in s.Queue) w.Write(id); w.Write(s.Occupants.Count); foreach (long id in s.Occupants) w.Write(id);
            }
            w.Write(needActivities.Count);
            foreach (var a in needActivities.Values)
            { w.Write(a.ActionId); w.Write(a.ActorId); w.Write(a.ServiceId); w.Write((int)a.Kind); w.Write(a.RequestedTick); w.Write(a.StartedTick); w.Write(a.EndedTick); w.Write(a.MealItemId); w.Write((int)a.Status); w.Write(a.Reason); }
            w.Write(mealSinks.Count); foreach (var sink in mealSinks) { w.Write(sink.Key); w.Write(sink.Value); }
        }
        private void ReadNeedServices(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var d = new NeedServiceDefinition(r.ReadInt64(), ReadEnum<NeedServiceKind>(r), ReadCoord(r), r.ReadInt32(), r.ReadInt64(), r.ReadString(), r.ReadInt64(), r.ReadInt64(), r.ReadBoolean(), r.ReadInt64(), r.ReadBoolean());
                if (Needs == null && (d.Kind != NeedServiceKind.Bed || !constructedBerths.Contains(d.Id))) throw new ArgumentException("Only physical construction berths may exist without needs."); InitializeNeedServices(new[] { d }); var s = needServices[d.Id]; s.Enabled = r.ReadBoolean(); s.AssignedActorId = r.ReadInt64(); s.NextGrant = r.ReadInt64();
                int n = Count(r); for (int j = 0; j < n; j++) s.Queue.Add(r.ReadInt64()); n = Count(r); for (int j = 0; j < n; j++) if (!s.Occupants.Add(r.ReadInt64())) throw new ArgumentException("Duplicate need occupancy claim.");
                if (s.Queue.Distinct().Count() != s.Queue.Count || !s.Queue.SequenceEqual(s.Queue.OrderBy(id => id)) || s.Occupants.Count > d.Capacity || !s.Enabled && s.Occupants.Count > 0) throw new ArgumentException("Invalid need service queue or capacity.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.GrantNeedService && e.Value == d.Id).ToArray();
                bool ready = s.Enabled && s.Queue.Count > 0 && s.Occupants.Count < d.Capacity;
                if (s.NextGrant < -1 || timers.Length != (s.NextGrant < 0 ? 0 : 1) || timers.Any(e => e.Tick != s.NextGrant || e.Phase != EventPhase.Decision) || ready != (s.NextGrant >= 0) || s.NextGrant >= 0 && s.NextGrant <= LastProcessedTick) throw new ArgumentException("Invalid need service grant timer.");
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new NeedActivitySnapshot { ActionId = r.ReadInt64(), ActorId = r.ReadInt64(), ServiceId = r.ReadInt64(), Kind = ReadEnum<ActionKind>(r), RequestedTick = r.ReadInt64(), StartedTick = r.ReadInt64(), EndedTick = r.ReadInt64(), MealItemId = r.ReadInt64(), Status = ReadEnum<NeedActivityStatus>(r), Reason = r.ReadString() };
                if (!actors.TryGetValue(new Id<ActorTag>(a.ActorId), out var actor) || !needServices.TryGetValue(a.ServiceId, out var service) || service.Definition.Action != a.Kind || a.ActionId < 1 || a.ActionId >= nextAction ||
                    a.RequestedTick < 0 || a.RequestedTick > LastProcessedTick || a.StartedTick < -1 || a.StartedTick >= 0 && (a.StartedTick < a.RequestedTick || a.StartedTick > LastProcessedTick) || a.MealItemId < 0 ||
                    (a.Kind == ActionKind.Eat && a.StartedTick >= 0) != (a.MealItemId > 0)) throw new ArgumentException("Invalid need activity identity or timeline.");
                bool pending = a.Status is NeedActivityStatus.Queued or NeedActivityStatus.Active;
                if (pending ? a.EndedTick != -1 || a.Reason.Length != 0 : a.EndedTick < a.RequestedTick || a.EndedTick > LastProcessedTick || a.StartedTick > a.EndedTick) throw new ArgumentException("Invalid need activity termination.");
                if (a.Status == NeedActivityStatus.Completed && (a.StartedTick < 0 || (a.Kind == ActionKind.Sleep ? a.EndedTick <= a.StartedTick || a.EndedTick > checked(a.StartedTick + service.Definition.DurationTicks) : a.EndedTick != checked(a.StartedTick + service.Definition.DurationTicks)) || a.Reason.Length != 0) ||
                    a.Status == NeedActivityStatus.Cancelled && a.Reason != "Cancelled" || a.Status == NeedActivityStatus.Rejected && (a.StartedTick != -1 || !Enum.TryParse<FailureReason>(a.Reason, out var failure) || failure == FailureReason.None || !Enum.IsDefined(typeof(FailureReason), failure))) throw new ArgumentException("Invalid need completion or cancellation.");
                if ((a.Status == NeedActivityStatus.Active) != service.Occupants.Contains(a.ActionId) || (a.Status == NeedActivityStatus.Queued) != service.Queue.Contains(a.ActionId)) throw new ArgumentException("Need activity disagrees with capacity claims.");
                if (pending)
                {
                    var action = actor.Action;
                    if (action == null || action.Id.Value != a.ActionId || action.Kind != a.Kind || action.ServiceId != a.ServiceId || action.Target != service.Definition.Position || actor.Position != action.Target || action.Waiting != (a.Status == NeedActivityStatus.Queued) ||
                        a.Status == NeedActivityStatus.Queued && (a.StartedTick != -1 || action.Started != a.RequestedTick) || a.Status == NeedActivityStatus.Active && (action.Started != a.StartedTick || action.Completes != checked(a.StartedTick + NeedDuration(action, service)) || NeedServiceAccess(actor, action, service) != FailureReason.None)) throw new ArgumentException("Need claim lacks its physical action.");
                    var completions = scheduled.Where(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == a.ActionId).ToArray();
                    if (completions.Length != (a.Status == NeedActivityStatus.Active ? 1 : 0) || completions.Any(e => e.Tick != action.Completes || e.Intent.Actor != actor.Id || e.Phase != EventPhase.PhysicalCompletion)) throw new ArgumentException("Invalid finite need completion timer.");
                    if (a.Status == NeedActivityStatus.Active && a.Kind == ActionKind.Eat && action.Item.Value != a.MealItemId) throw new ArgumentException("Meal reservation disagrees with action.");
                }
                needActivities.Add(a.ActionId, a); IndexSleep(a);
                if (a.Status == NeedActivityStatus.Active) ReserveMeal(a);
            }
            foreach (var s in needServices.Values)
                if (s.Queue.Concat(s.Occupants).Any(id => !needActivities.TryGetValue(id, out var a) || a.ServiceId != s.Definition.Id)) throw new ArgumentException("Unregistered need claim.");
            foreach (var actor in actors.Values.Where(a => a.Action != null && IsNeedAction(a.Action.Kind)))
                if (!needActivities.TryGetValue(actor.Action!.Id.Value, out var a) || a.ActorId != actor.Id.Value || !(a.Status is NeedActivityStatus.Active or NeedActivityStatus.Queued)) throw new ArgumentException("Unregistered need action.");
            foreach (var activity in needActivities.Values.Where(a => a.Status == NeedActivityStatus.Active && a.Kind == ActionKind.Eat))
                if (AvailableMeal(actors[new Id<ActorTag>(activity.ActorId)], actors[new Id<ActorTag>(activity.ActorId)].Action!, needServices[activity.ServiceId]) == null) throw new ArgumentException("Meal reservations exceed real accessible stock.");
            count = Count(r); for (int i = 0; i < count; i++) { string resource = r.ReadString(); long amount = r.ReadInt64(); if (string.IsNullOrWhiteSpace(resource) || amount < 1) throw new ArgumentException("Invalid meal sink."); mealSinks.Add(resource, amount); }
            var consumed = needActivities.Values.Where(a => a.Status == NeedActivityStatus.Completed && a.Kind == ActionKind.Eat).GroupBy(a => needServices[a.ServiceId].Definition.MealResource).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, Count: (long)g.Count()));
            if (!mealSinks.Select(p => (p.Key, p.Value)).SequenceEqual(consumed)) throw new ArgumentException("Meal sink does not match completed eating.");
            if (scheduled.Any(e => e.Kind == ScheduledKind.GrantNeedService && !needServices.ContainsKey(e.Value) || e.Kind == ScheduledKind.NeedServiceChange && !needServices.ContainsKey(e.ActionId))) throw new ArgumentException("Unknown need service event.");
        }
    }
}
