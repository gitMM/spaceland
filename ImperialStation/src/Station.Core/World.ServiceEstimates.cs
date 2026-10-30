using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

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
        private long EstimateStaleTicks => ServicePreference.StaleTicks(planner.MaximumBackoff);
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
        private static long VisibleWait(NeedService service) => ServicePreference.VisibleWait(service.Queue.Count, service.Occupants.Count, service.Definition.Capacity, service.Definition.DurationTicks);
        // Queue seen on arrival.
        private void ObserveServiceQueue(Actor actor, NeedService service) => LearnServiceEstimate(actor, service, ServiceEstimateSource.ObservedQueue, wait: VisibleWait(service));
        private long MealsVisible(NeedService service) => AvailableItemsAt(service.Definition.Position).Where(i => i.Resource == service.Definition.MealResource)
            .Sum(i => Math.Max(0, i.Quantity - (mealReservations.TryGetValue(i.Id.Value, out var claims) ? claims.Count : 0)));
        // Gathers this citizen's learned estimates and what it sees of the services on its own
        // tile; Mind turns them into expected extra waits for its planning only.
        private SortedDictionary<long, long> KnownServiceWaits(Id<ActorTag> actor)
        {
            var waits = new SortedDictionary<long, long>();
            foreach (var e in serviceEstimates.Values.Where(e => e.ActorId == actor.Value && needServices.ContainsKey(e.ServiceId)))
            {
                long extra = ServicePreference.LearnedExtraWait(NeedNow, EstimateStaleTicks, e.WaitObservedTick, e.WaitTicks, e.StockObservedTick, e.MealsSeen, needServices[e.ServiceId].Definition.DurationTicks);
                if (extra > 0) waits[e.ServiceId] = extra;
            }
            if (seenServiceWaits.TryGetValue(actor.Value, out var seen))
            {
                var here = seen.Select(p => (p.Key, needServices[p.Key].Definition.Kind, needServices[p.Key].Definition.DetentionFacilityId, (Func<long>)(() => p.Value))).ToArray();
                ServicePreference.CompareVisibleQueues(waits, here);
            }
            return waits;
        }
        // Local perception of the services on a citizen's own tile: for each one it may use
        // (in service, and not a bed labelled for someone else), the visible wait. A citizen
        // sees them on arrival and sees every later queue, seat, label or power change while
        // it stands there; deciders read only this, never the live queues.
        private readonly SortedDictionary<long, SortedDictionary<long, long>> seenServiceWaits = new SortedDictionary<long, SortedDictionary<long, long>>();
        private readonly SortedDictionary<Coord, List<NeedService>> servicesAt = new SortedDictionary<Coord, List<NeedService>>();
        private void IndexNeedService(NeedService service)
        { if (!servicesAt.TryGetValue(service.Definition.Position, out var list)) { list = new List<NeedService>(); servicesAt.Add(service.Definition.Position, list); } list.Add(service); }
        private void PerceiveLocalServices(Actor actor)
        {
            seenServiceWaits.Remove(actor.Id.Value);
            if (servicesAt.TryGetValue(actor.Position, out var here)) foreach (var service in here) SeeService(actor.Id.Value, service);
        }
        private void SeeService(long actor, NeedService service)
        {
            bool usable = service.Enabled && (service.Definition.Kind != NeedServiceKind.Bed || service.AssignedActorId == 0 || service.AssignedActorId == actor);
            if (!seenServiceWaits.TryGetValue(actor, out var seen)) { if (!usable) return; seen = new SortedDictionary<long, long>(); seenServiceWaits.Add(actor, seen); }
            if (usable) seen[service.Definition.Id] = VisibleWait(service); else { seen.Remove(service.Definition.Id); if (seen.Count == 0) seenServiceWaits.Remove(actor); }
        }
        // Everyone standing on the service's tile sees the change.
        private void ServiceVisiblyChanged(NeedService service)
        { if (localActors.TryGetValue(service.Definition.Position, out var bucket)) foreach (long id in bucket) SeeService(id, service); }
        private long SeenWait(Actor actor, NeedService service) => seenServiceWaits.TryGetValue(actor.Id.Value, out var seen) && seen.TryGetValue(service.Definition.Id, out long wait) ? wait : 0;
        private void WriteServiceEstimates(BinaryWriter w)
        {
            w.Write(serviceEstimates.Count);
            foreach (var e in serviceEstimates.Values)
            { w.Write(e.ActorId); w.Write(e.ServiceId); w.Write(e.WaitObservedTick); w.Write(e.WaitTicks); w.Write((int)e.WaitSource); w.Write(e.StockObservedTick); w.Write(e.MealsSeen); w.Write((int)e.StockSource); }
            w.Write(seenServiceWaits.Count);
            foreach (var actor in seenServiceWaits) { w.Write(actor.Key); w.Write(actor.Value.Count); foreach (var seen in actor.Value) { w.Write(seen.Key); w.Write(seen.Value); } }
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
            seenServiceWaits.Clear(); count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long actor = r.ReadInt64(); int n = Count(r); var seen = new SortedDictionary<long, long>();
                if (n == 0 || !actors.TryGetValue(new Id<ActorTag>(actor), out var who) || !seenServiceWaits.TryAdd(actor, seen)) throw new ArgumentException("Invalid seen service waits.");
                for (int j = 0; j < n; j++)
                {
                    long service = r.ReadInt64(), wait = r.ReadInt64();
                    if (!needServices.TryGetValue(service, out var s) || s.Definition.Position != who.Position || wait < 0 || !seen.TryAdd(service, wait)) throw new ArgumentException("Invalid seen service waits.");
                }
            }
        }
    }
}
