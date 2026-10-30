using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // How a citizen weighs need services by its own learned estimates and by the
    // queues it can see on its own tile.
    public static class ServicePreference
    {
        // Estimates older than this are ignored rather than trusted indefinitely.
        public static long StaleTicks(long maximumBackoff) => checked(maximumBackoff * 6);
        // Whole service rounds needed before a newcomer's turn, as seen from the service's tile.
        public static long VisibleWait(int queued, int occupants, int capacity, long durationTicks)
        {
            long ahead = queued + occupants, slots = Math.Max(1, capacity);
            return checked(ahead < slots ? 0 : (ahead - slots) / slots + 1) * durationTicks;
        }
        // Expected extra wait from one learned cafeteria estimate. A learned wait shorter
        // than one service duration is ignored so small queues cause no switching. A
        // cafeteria last seen empty is the least preferred, not forbidden: it is still
        // chosen when nothing else is known, so restocking can be discovered.
        public static long LearnedExtraWait(long now, long staleTicks, long waitObservedTick, long waitTicks, long stockObservedTick, long mealsSeen, long durationTicks)
        {
            bool freshWait = waitObservedTick >= 0 && now - waitObservedTick <= staleTicks, freshStock = stockObservedTick >= 0 && now - stockObservedTick <= staleTicks;
            return (freshStock && mealsSeen == 0 ? staleTicks : 0) + (freshWait ? Math.Max(0, waitTicks - durationTicks) : 0);
        }
        // Identical services this citizen may use on its own tile (for example a detention
        // facility's beds) are compared by the queues it can see there now, so it does not
        // queue at one while another stands free. Updates the expected extra waits.
        public static void CompareVisibleQueues(SortedDictionary<long, long> waits, IReadOnlyList<(long Id, NeedServiceKind Kind, long DetentionFacilityId, Func<long> VisibleWait)> usableHere)
        {
            foreach (var s in usableHere.Where(s => usableHere.Count(o => o.Kind == s.Kind && o.DetentionFacilityId == s.DetentionFacilityId) > 1))
            {
                long visible = s.VisibleWait();
                if (visible > 0) waits[s.Id] = visible; else waits.Remove(s.Id);
            }
        }
        // Arriving at a service that visibly has someone ahead, a citizen may use an
        // identical service on the same tile that stands visibly free.
        public static bool SeeksIdentical(long chosenVisibleWait) => chosenVisibleWait != 0;
        public static bool Identical(NeedServiceDefinition a, NeedServiceDefinition b) =>
            a.Position == b.Position && a.Kind == b.Kind && a.DetentionFacilityId == b.DetentionFacilityId && a.DurationTicks == b.DurationTicks;
        public static bool VisiblyFree(long visibleWait) => visibleWait == 0;
    }
}
