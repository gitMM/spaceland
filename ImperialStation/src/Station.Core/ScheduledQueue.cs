using System;
using System.Collections;
using System.Collections.Generic;

namespace Station.Core
{
    // The canonical queue still orders by tick/phase/sequence. This derived actor
    // index only avoids scanning unrelated timers when replacing an actor's event.
    internal sealed class ScheduledQueue : IEnumerable<ScheduledEvent>
    {
        private readonly SortedSet<ScheduledEvent> ordered = new SortedSet<ScheduledEvent>();
        private readonly SortedDictionary<long, SortedSet<ScheduledEvent>> byActor = new SortedDictionary<long, SortedSet<ScheduledEvent>>();
        internal int Count => ordered.Count;
        internal ScheduledEvent? Min => ordered.Min;
        internal IEnumerable<ScheduledEvent> ForActor(long id) => byActor.TryGetValue(id, out var events) ? events : (IEnumerable<ScheduledEvent>)Array.Empty<ScheduledEvent>();
        internal bool Add(ScheduledEvent e)
        {
            if (!ordered.Add(e)) return false;
            long actor = e.Intent.Actor.Value;
            if (!byActor.TryGetValue(actor, out var events)) { events = new SortedSet<ScheduledEvent>(); byActor.Add(actor, events); }
            events.Add(e); return true;
        }
        internal bool Remove(ScheduledEvent e)
        {
            if (!ordered.Remove(e)) return false;
            var events = byActor[e.Intent.Actor.Value]; events.Remove(e);
            if (events.Count == 0) byActor.Remove(e.Intent.Actor.Value);
            return true;
        }
        public IEnumerator<ScheduledEvent> GetEnumerator() => ordered.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
