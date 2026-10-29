using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteTerminalOutages(BinaryWriter w)
        {
            var all = AllOutages.ToArray(); w.Write(all.Length);
            foreach (var o in all)
            { w.Write(o.ActorId); w.Write(o.TerminalId); w.Write(o.FirstObservedTick); w.Write(o.LastObservedTick); w.Write(o.Observations); w.Write(o.Backoff); w.Write(o.RetryAt); }
        }
        private void ReadTerminalOutages(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var o = new TerminalOutageSnapshot { ActorId = r.ReadInt64(), TerminalId = r.ReadInt64(), FirstObservedTick = r.ReadInt64(), LastObservedTick = r.ReadInt64(), Observations = r.ReadInt64(), Backoff = r.ReadInt64(), RetryAt = r.ReadInt64() };
                if (!actors.ContainsKey(new Id<ActorTag>(o.ActorId)) || !terminals.ContainsKey(o.TerminalId) || o.FirstObservedTick < 0 || o.LastObservedTick < o.FirstObservedTick || o.LastObservedTick > LastProcessedTick ||
                    o.Observations < 1 || o.Backoff < 1 || o.RetryAt < checked(o.LastObservedTick + o.Backoff) || OwnOutages(o.ActorId).Any(x => x.TerminalId == o.TerminalId)) throw new ArgumentException("Invalid remembered terminal outage.");
                AddOutage(o);
            }
        }
    }
}
