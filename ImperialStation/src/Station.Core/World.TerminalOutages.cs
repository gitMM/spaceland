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
        // Indexed by citizen, then terminal: every lookup is one citizen's own memory.
        private readonly SortedDictionary<long, SortedDictionary<long, TerminalOutageSnapshot>> terminalOutages = new SortedDictionary<long, SortedDictionary<long, TerminalOutageSnapshot>>();
        private IEnumerable<TerminalOutageSnapshot> OwnOutages(long actor) => terminalOutages.TryGetValue(actor, out var own) ? own.Values : (IEnumerable<TerminalOutageSnapshot>)Array.Empty<TerminalOutageSnapshot>();
        private IEnumerable<TerminalOutageSnapshot> AllOutages => terminalOutages.Values.SelectMany(o => o.Values);
        private void AddOutage(TerminalOutageSnapshot outage)
        {
            if (!terminalOutages.TryGetValue(outage.ActorId, out var own)) terminalOutages.Add(outage.ActorId, own = new SortedDictionary<long, TerminalOutageSnapshot>());
            own.Add(outage.TerminalId, outage);
        }
        public IReadOnlyList<TerminalOutageSnapshot> InspectTerminalOutages(Id<ActorTag>? actor = null) =>
            (actor == null ? AllOutages : OwnOutages(actor.Value.Value)).Select(o => o.Copy()).ToArray();
        private void ObserveTerminalCondition(Actor actor, Terminal terminal)
        {
            if (actor.Position != terminal.Definition.Position || actor.Action?.Kind == ActionKind.Move) return;
            terminalOutages.TryGetValue(actor.Id.Value, out var own);
            TerminalOutageSnapshot? outage = null; own?.TryGetValue(terminal.Definition.Id, out outage);
            if (terminal.Enabled && terminal.Connected)
            {
                if (outage != null)
                {
                    own!.Remove(outage.TerminalId); if (own.Count == 0) terminalOutages.Remove(actor.Id.Value);
                    Emit("TerminalRestorationObserved", actor, terminalId: outage.TerminalId, detail: "since=" + outage.FirstObservedTick + "; observations=" + outage.Observations);
                }
                return;
            }
            if (outage == null)
            {
                outage = new TerminalOutageSnapshot { ActorId = actor.Id.Value, TerminalId = terminal.Definition.Id, FirstObservedTick = NeedNow, Backoff = planner.InitialBackoff };
                AddOutage(outage);
            }
            else if (outage.LastObservedTick == NeedNow) return;
            // A recheck at or after the retry time that finds the outage again backs off further.
            else if (NeedNow >= outage.RetryAt) outage.Backoff = NextWorkBackoff(outage.Backoff);
            outage.LastObservedTick = NeedNow; outage.Observations++; outage.RetryAt = checked(NeedNow + outage.Backoff);
            // A confirmed outage on recheck also defers the citizen's other remembered
            // outages, so rechecks do not alternate between known-down terminals.
            // Only outages still inside their own wait are deferred; one already due may be rechecked.
            if (outage.Observations > 1)
                foreach (var other in OwnOutages(actor.Id.Value).Where(o => o.TerminalId != outage.TerminalId && o.RetryAt > NeedNow)) other.RetryAt = Math.Max(other.RetryAt, outage.RetryAt);
            Emit("TerminalOutageLearned", actor, terminalId: outage.TerminalId, detail: "observations=" + outage.Observations + "; retry=" + outage.RetryAt);
        }
        // A failure that just taught the citizen about an outage: replan at once
        // so an eligible alternative is tried before waiting for a recheck.
        private bool JustLearnedTerminalOutage(Id<ActorTag> actor) => actors[actor].LastFailure == FailureReason.RequiredServiceNotPresent &&
            OwnOutages(actor.Value).Any(o => o.LastObservedTick == NeedNow && o.Observations == 1);
        // Outages still inside their wait are avoided. Of those due for a recheck, only the
        // least-confirmed one (smallest backoff, then oldest observation) stays eligible.
        private IReadOnlyList<long> KnownTerminalOutages(Id<ActorTag> actor)
        {
            if (!terminalOutages.TryGetValue(actor.Value, out var own)) return Array.Empty<long>();
            var recheck = own.Values.Where(o => o.RetryAt <= NeedNow).OrderBy(o => o.Backoff).ThenBy(o => o.LastObservedTick).ThenBy(o => o.TerminalId).FirstOrDefault();
            return own.Values.Where(o => o != recheck).Select(o => o.TerminalId).ToArray();
        }
        // A query bound to one terminal has no alternative to alternate with: it avoids that
        // terminal only while the remembered outage is inside its own wait, whatever other
        // outages the citizen remembers, and otherwise rechecks it.
        private long BoundTerminalRetry(Id<ActorTag> actor, long terminal) =>
            terminalOutages.TryGetValue(actor.Value, out var own) && own.TryGetValue(terminal, out var o) && o.RetryAt > NeedNow ? o.RetryAt : -1;
        // An agent left without any eligible terminal waits for its own backoff and
        // for the earliest remembered outage to become due for a bounded recheck.
        private long NextTerminalRecheck(Id<ActorTag> actor, long backoffRetry)
        {
            long next = long.MaxValue;
            foreach (var o in OwnOutages(actor.Value).Where(o => o.RetryAt > NeedNow)) next = Math.Min(next, o.RetryAt);
            return next == long.MaxValue ? backoffRetry : Math.Max(backoffRetry, next);
        }
    }
}
