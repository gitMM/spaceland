using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    // A citizen's own memory of a terminal found disabled or disconnected while
    // standing at it. Reporting, check-ins, audits and credential recovery share
    // it: travel never erases it, a failed recheck doubles the retry delay, and
    // only a later local observation of a working terminal clears it.
    public sealed class TerminalOutageSnapshot
    {
        public long ActorId { get; internal set; }
        public long TerminalId { get; internal set; }
        public long FirstObservedTick { get; internal set; }
        public long LastObservedTick { get; internal set; }
        public long Observations { get; internal set; }
        public long Backoff { get; internal set; }
        public long RetryAt { get; internal set; }
        internal TerminalOutageSnapshot Copy() => (TerminalOutageSnapshot)MemberwiseClone();
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<(long Actor, long Terminal), TerminalOutageSnapshot> terminalOutages = new SortedDictionary<(long, long), TerminalOutageSnapshot>();
        public IReadOnlyList<TerminalOutageSnapshot> InspectTerminalOutages(Id<ActorTag>? actor = null) =>
            terminalOutages.Values.Where(o => actor == null || o.ActorId == actor.Value.Value).Select(o => o.Copy()).ToArray();
        private void ObserveTerminalCondition(Actor actor, Terminal terminal)
        {
            if (actor.Position != terminal.Definition.Position || actor.Action?.Kind == ActionKind.Move) return;
            var key = (actor.Id.Value, terminal.Definition.Id);
            if (terminal.Enabled && terminal.Connected)
            {
                if (terminalOutages.TryGetValue(key, out var known) && terminalOutages.Remove(key))
                    Emit("TerminalRestorationObserved", actor, terminalId: known.TerminalId, detail: "since=" + known.FirstObservedTick + "; observations=" + known.Observations);
                return;
            }
            if (!terminalOutages.TryGetValue(key, out var outage))
            {
                outage = new TerminalOutageSnapshot { ActorId = actor.Id.Value, TerminalId = terminal.Definition.Id, FirstObservedTick = NeedNow, Backoff = planner.InitialBackoff };
                terminalOutages.Add(key, outage);
            }
            else if (outage.LastObservedTick == NeedNow) return;
            // A recheck at or after the retry time that finds the outage again backs off further.
            else if (NeedNow >= outage.RetryAt) outage.Backoff = NextWorkBackoff(outage.Backoff);
            outage.LastObservedTick = NeedNow; outage.Observations++; outage.RetryAt = checked(NeedNow + outage.Backoff);
            // A confirmed outage on recheck also defers the citizen's other remembered
            // outages, so rechecks do not alternate between known-down terminals.
            // Only outages still inside their own wait are deferred; one already due may be rechecked.
            if (outage.Observations > 1)
                foreach (var other in terminalOutages.Values.Where(o => o.ActorId == outage.ActorId && o.TerminalId != outage.TerminalId && o.RetryAt > NeedNow)) other.RetryAt = Math.Max(other.RetryAt, outage.RetryAt);
            Emit("TerminalOutageLearned", actor, terminalId: outage.TerminalId, detail: "observations=" + outage.Observations + "; retry=" + outage.RetryAt);
        }
        // A failure that just taught the citizen about an outage: replan at once
        // so an eligible alternative is tried before waiting for a recheck.
        private bool JustLearnedTerminalOutage(Id<ActorTag> actor) => terminalOutages.Count > 0 && actors[actor].LastFailure == FailureReason.RequiredServiceNotPresent &&
            terminalOutages.Values.Any(o => o.ActorId == actor.Value && o.LastObservedTick == NeedNow && o.Observations == 1);
        // Outages still inside their wait are avoided. Of those due for a recheck, only the
        // least-confirmed one (smallest backoff, then oldest observation) stays eligible.
        private IReadOnlyList<long> KnownTerminalOutages(Id<ActorTag> actor)
        {
            if (terminalOutages.Count == 0) return Array.Empty<long>();
            var own = terminalOutages.Values.Where(o => o.ActorId == actor.Value).ToArray();
            var recheck = own.Where(o => o.RetryAt <= NeedNow).OrderBy(o => o.Backoff).ThenBy(o => o.LastObservedTick).ThenBy(o => o.TerminalId).FirstOrDefault();
            return own.Where(o => o != recheck).Select(o => o.TerminalId).ToArray();
        }
        // An agent left without any eligible terminal waits for its own backoff and
        // for the earliest remembered outage to become due for a bounded recheck.
        private long NextTerminalRecheck(Id<ActorTag> actor, long backoffRetry)
        {
            long next = long.MaxValue;
            foreach (var o in terminalOutages.Values.Where(o => o.ActorId == actor.Value && o.RetryAt > NeedNow)) next = Math.Min(next, o.RetryAt);
            return next == long.MaxValue ? backoffRetry : Math.Max(backoffRetry, next);
        }
    }
}
