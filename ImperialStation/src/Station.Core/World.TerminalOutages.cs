using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    // A terminal outage one citizen observed in person. It is private memory, not live
    // terminal state: travel and unrelated actions never clear it. Only a later
    // successful login at that terminal (a new local observation) proves restoration.
    public sealed class TerminalOutageSnapshot
    {
        public long ActorId { get; internal set; }
        public long TerminalId { get; internal set; }
        public long FirstObservedTick { get; internal set; }
        public long ObservedTick { get; internal set; }
        public long RecheckAt { get; internal set; }
        public long RecheckDelay { get; internal set; }
        public long Failures { get; internal set; }
        internal TerminalOutageSnapshot Copy() => (TerminalOutageSnapshot)MemberwiseClone();
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<(long Actor, long Terminal), TerminalOutageSnapshot> terminalOutages = new SortedDictionary<(long Actor, long Terminal), TerminalOutageSnapshot>();
        // A learned outage redirects planning to eligible alternatives until its recheck.
        // The delay starts at the planner's maximum retry delay and doubles to a bound.
        private long OutageRecheckLimit => checked(planner.MaximumBackoff * 8);
        public IReadOnlyList<TerminalOutageSnapshot> InspectTerminalOutages(Id<ActorTag> actor) =>
            terminalOutages.Values.Where(o => o.ActorId == actor.Value).Select(o => o.Copy()).ToArray();
        private void LearnTerminalOutage(Actor actor, Terminal terminal)
        {
            if (processingTick < 0 || actor.Position != terminal.Definition.Position) return;
            var key = (actor.Id.Value, terminal.Definition.Id);
            if (!terminalOutages.TryGetValue(key, out var outage))
            {
                outage = new TerminalOutageSnapshot { ActorId = actor.Id.Value, TerminalId = terminal.Definition.Id, FirstObservedTick = processingTick, RecheckDelay = planner.MaximumBackoff };
                terminalOutages.Add(key, outage);
            }
            else if (outage.ObservedTick == processingTick) return;
            else outage.RecheckDelay = Math.Min(OutageRecheckLimit, checked(outage.RecheckDelay * 2));
            outage.Failures++; outage.ObservedTick = processingTick; outage.RecheckAt = checked(processingTick + outage.RecheckDelay);
            Emit("TerminalOutageLearned", actor, terminalId: terminal.Definition.Id, detail: "failures=" + outage.Failures + "; recheck=" + outage.RecheckAt);
        }
        private void ObserveTerminalRestored(Actor actor, long terminalId)
        {
            if (terminalOutages.Remove((actor.Id.Value, terminalId), out var outage))
                Emit("TerminalOutageCleared", actor, terminalId: terminalId, detail: "since=" + outage.FirstObservedTick + "; failures=" + outage.Failures);
        }
        // Terminals this citizen should not plan to use while an eligible alternative exists.
        // After RecheckAt the terminal is an ordinary candidate again. With no alternative,
        // planning falls back to all terminals and the agent's own bounded retry backoff.
        // Ordered by recheck time so a fallback rechecks the oldest observation first.
        private long[] AvoidedTerminals(Id<ActorTag> actor) => terminalOutages.Values.Where(o => o.ActorId == actor.Value && o.RecheckAt > NeedNow)
            .OrderBy(o => o.RecheckAt).ThenBy(o => o.TerminalId).Select(o => o.TerminalId).ToArray();
        private void WriteTerminalOutages(BinaryWriter w)
        {
            w.Write(terminalOutages.Count);
            foreach (var o in terminalOutages.Values)
            { w.Write(o.ActorId); w.Write(o.TerminalId); w.Write(o.FirstObservedTick); w.Write(o.ObservedTick); w.Write(o.RecheckAt); w.Write(o.RecheckDelay); w.Write(o.Failures); }
        }
        private void ReadTerminalOutages(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var o = new TerminalOutageSnapshot { ActorId = r.ReadInt64(), TerminalId = r.ReadInt64(), FirstObservedTick = r.ReadInt64(), ObservedTick = r.ReadInt64(), RecheckAt = r.ReadInt64(), RecheckDelay = r.ReadInt64(), Failures = r.ReadInt64() };
                if (!actors.ContainsKey(new Id<ActorTag>(o.ActorId)) || !terminals.ContainsKey(o.TerminalId) || o.Failures < 1 || o.FirstObservedTick < 0 || o.ObservedTick < o.FirstObservedTick ||
                    (RuntimeError == null && o.ObservedTick > LastProcessedTick) || o.RecheckDelay < planner.MaximumBackoff || o.RecheckDelay > OutageRecheckLimit || o.RecheckAt != checked(o.ObservedTick + o.RecheckDelay) ||
                    !terminalOutages.TryAdd((o.ActorId, o.TerminalId), o)) throw new ArgumentException("Invalid learned terminal outage.");
            }
        }
    }
}
