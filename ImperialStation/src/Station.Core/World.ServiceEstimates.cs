using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public enum ServiceEstimateSource { ObservedQueue, ExperiencedWait, ObservedStock, ServiceFailure }
    // A cafeteria estimate one citizen learned in person: the queue seen on arrival, the
    // wait actually experienced, or meals seen remaining. It records when and how it was
    // learned and goes stale; citizens never read live remote queues or stock.
    public sealed class ServiceEstimateSnapshot
    {
        public long ActorId { get; internal set; }
        public long ServiceId { get; internal set; }
        public long WaitObservedTick { get; internal set; } = -1;
        public long WaitTicks { get; internal set; }
        public ServiceEstimateSource WaitSource { get; internal set; }
        public long StockObservedTick { get; internal set; } = -1;
        public long MealsSeen { get; internal set; } = -1;
        public ServiceEstimateSource StockSource { get; internal set; }
        internal ServiceEstimateSnapshot Copy() => (ServiceEstimateSnapshot)MemberwiseClone();
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<(long Actor, long Service), ServiceEstimateSnapshot> serviceEstimates = new SortedDictionary<(long Actor, long Service), ServiceEstimateSnapshot>();
        // Estimates older than this are ignored rather than trusted indefinitely.
        private long EstimateStaleTicks => checked(planner.MaximumBackoff * 6);
        public IReadOnlyList<ServiceEstimateSnapshot> InspectServiceEstimates(Id<ActorTag> actor) =>
            serviceEstimates.Values.Where(e => e.ActorId == actor.Value).Select(e => e.Copy()).ToArray();
        private ServiceEstimateSnapshot Estimate(Actor actor, NeedService service)
        {
            var key = (actor.Id.Value, service.Definition.Id);
            if (!serviceEstimates.TryGetValue(key, out var estimate)) { estimate = new ServiceEstimateSnapshot { ActorId = actor.Id.Value, ServiceId = service.Definition.Id }; serviceEstimates.Add(key, estimate); }
            return estimate;
        }
        private void LearnServiceEstimate(Actor actor, NeedService service, ServiceEstimateSource source, long wait = -1, long meals = -1)
        {
            if (service.Definition.Kind != NeedServiceKind.Cafeteria || service.Definition.DetentionFacilityId > 0 || processingTick < 0 || actor.Position != service.Definition.Position) return;
            var estimate = Estimate(actor, service);
            if (wait >= 0) { estimate.WaitTicks = wait; estimate.WaitObservedTick = processingTick; estimate.WaitSource = source; }
            if (meals >= 0) { estimate.MealsSeen = meals; estimate.StockObservedTick = processingTick; estimate.StockSource = source; }
            Emit("ServiceEstimateLearned", actor, detail: "service=" + service.Definition.Id + "; " + source + "; wait=" + estimate.WaitTicks + "; meals=" + estimate.MealsSeen);
        }
        // Whole service rounds needed before a newcomer's turn, as seen from the service's tile.
        private static long VisibleWait(NeedService service)
        {
            long ahead = service.Queue.Count + service.Occupants.Count, capacity = Math.Max(1, service.Definition.Capacity);
            return checked(ahead < capacity ? 0 : (ahead - capacity) / capacity + 1) * service.Definition.DurationTicks;
        }
        // Queue seen on arrival.
        private void ObserveServiceQueue(Actor actor, NeedService service) => LearnServiceEstimate(actor, service, ServiceEstimateSource.ObservedQueue, wait: VisibleWait(service));
        private long MealsVisible(NeedService service) => AvailableItemsAt(service.Definition.Position).Where(i => i.Resource == service.Definition.MealResource)
            .Sum(i => Math.Max(0, i.Quantity - (mealReservations.TryGetValue(i.Id.Value, out var claims) ? claims.Count : 0)));
        // Fresh expected extra waits per service, for this citizen's planning only. A learned
        // cafeteria wait shorter than one service duration is ignored so small queues cause
        // no switching.
        private SortedDictionary<long, long> KnownServiceWaits(Id<ActorTag> actor)
        {
            var waits = new SortedDictionary<long, long>();
            foreach (var e in serviceEstimates.Values.Where(e => e.ActorId == actor.Value && needServices.ContainsKey(e.ServiceId)))
            {
                bool freshWait = e.WaitObservedTick >= 0 && NeedNow - e.WaitObservedTick <= EstimateStaleTicks, freshStock = e.StockObservedTick >= 0 && NeedNow - e.StockObservedTick <= EstimateStaleTicks;
                // A cafeteria last seen empty is the least preferred, not forbidden: it is
                // still chosen when nothing else is known, so restocking can be discovered.
                long extra = (freshStock && e.MealsSeen == 0 ? EstimateStaleTicks : 0) + (freshWait ? Math.Max(0, e.WaitTicks - needServices[e.ServiceId].Definition.DurationTicks) : 0);
                if (extra > 0) waits[e.ServiceId] = extra;
            }
            // Identical services this citizen may use on its own tile (for example a
            // detention facility's beds) are compared by the queues it can see there now,
            // so it does not queue at one while another stands free.
            var position = actors[actor].Position;
            var here = needServices.Values.Where(s => s.Definition.Position == position && s.Enabled && (s.Definition.Kind != NeedServiceKind.Bed || s.AssignedActorId == 0 || s.AssignedActorId == actor.Value)).ToArray();
            foreach (var s in here.Where(s => here.Count(o => o.Definition.Kind == s.Definition.Kind && o.Definition.DetentionFacilityId == s.Definition.DetentionFacilityId) > 1))
            {
                long visible = VisibleWait(s);
                if (visible > 0) waits[s.Definition.Id] = visible; else waits.Remove(s.Definition.Id);
            }
            return waits;
        }
        private void WriteServiceEstimates(BinaryWriter w)
        {
            w.Write(serviceEstimates.Count);
            foreach (var e in serviceEstimates.Values)
            { w.Write(e.ActorId); w.Write(e.ServiceId); w.Write(e.WaitObservedTick); w.Write(e.WaitTicks); w.Write((int)e.WaitSource); w.Write(e.StockObservedTick); w.Write(e.MealsSeen); w.Write((int)e.StockSource); }
        }
        private void ReadServiceEstimates(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var e = new ServiceEstimateSnapshot { ActorId = r.ReadInt64(), ServiceId = r.ReadInt64(), WaitObservedTick = r.ReadInt64(), WaitTicks = r.ReadInt64(), WaitSource = ReadEnum<ServiceEstimateSource>(r),
                    StockObservedTick = r.ReadInt64(), MealsSeen = r.ReadInt64(), StockSource = ReadEnum<ServiceEstimateSource>(r) };
                bool Valid(long tick) => tick >= -1 && (RuntimeError != null || tick <= LastProcessedTick);
                if (!actors.ContainsKey(new Id<ActorTag>(e.ActorId)) || !needServices.TryGetValue(e.ServiceId, out var service) || service.Definition.Kind != NeedServiceKind.Cafeteria ||
                    !Valid(e.WaitObservedTick) || !Valid(e.StockObservedTick) || e.WaitObservedTick < 0 && e.StockObservedTick < 0 || e.WaitTicks < 0 || e.MealsSeen < -1 || (e.StockObservedTick < 0) != (e.MealsSeen < 0) ||
                    !serviceEstimates.TryAdd((e.ActorId, e.ServiceId), e))
                    throw new ArgumentException("Invalid learned service estimate.");
            }
        }
    }
}
