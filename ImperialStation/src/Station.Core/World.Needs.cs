using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    internal sealed class NeedState
    {
        internal NeedAccumulator Segment;
        internal NeedBand Announced;
    }
    internal sealed class ActorNeeds
    {
        internal long ActorId;
        internal NeedMode Mode;
        internal long NextThreshold = -1;
        internal long LastCriticalTick = -1;
        internal string UnmetReason = "";
        internal NeedState[] Values = Array.Empty<NeedState>();
    }
    public sealed class ActorNeedsSnapshot
    {
        public long ActorId { get; internal set; }
        public NeedMode Mode { get; internal set; }
        public long NextThreshold { get; internal set; }
        public long LastCriticalTick { get; internal set; }
        public string UnmetReason { get; internal set; } = "";
        public IReadOnlyList<NeedValue> Values { get; internal set; } = Array.Empty<NeedValue>();
        public IReadOnlyList<NeedAccumulator> Segments { get; internal set; } = Array.Empty<NeedAccumulator>();
    }
    public sealed partial class World
    {
        public NeedConfig? Needs { get; private set; }
        private readonly SortedDictionary<long, ActorNeeds> actorNeeds = new SortedDictionary<long, ActorNeeds>();
        public long NeedThresholdsReached { get; private set; }
        public long CriticalNeedInterruptions { get; private set; }
        private long NeedNow => Math.Max(0, processingTick < 0 ? LastProcessedTick : processingTick);
        public IReadOnlyList<ActorNeedsSnapshot> InspectNeeds() => actorNeeds.Values.Select(n => new ActorNeedsSnapshot {
            ActorId = n.ActorId, Mode = n.Mode, NextThreshold = n.NextThreshold, LastCriticalTick = n.LastCriticalTick, UnmetReason = n.UnmetReason,
            Values = NeedValues(n.ActorId), Segments = Array.AsReadOnly(n.Values.Select(v => v.Segment).ToArray()) }).ToArray();
        public IReadOnlyList<NeedValue> InspectOwnNeeds(Id<ActorTag> actor) => NeedValues(actor.Value);
        private IReadOnlyList<NeedValue> NeedValues(long actor)
        {
            if (!actorNeeds.TryGetValue(actor, out var state)) return Array.Empty<NeedValue>();
            return Array.AsReadOnly(state.Values.Select((v, i) => { long q = v.Segment.At(NeedNow, Config.TickDurationMs); return new NeedValue(Needs!.Rules[i].Kind, q, Needs.Rules[i].Band(q)); }).ToArray());
        }
        private void InitializeNeeds(NeedSeed seed)
        {
            Needs = seed.Config; InitializeNeedServices(seed.Services); foreach (var actor in actors.Values) PerceiveLocalServices(actor);
            var initial = new SortedDictionary<(long Actor, NeedKind Kind), long>();
            foreach (var value in seed.Initial)
            { if (!actors.ContainsKey(value.Actor)) throw new ArgumentException("Initial need refers to an unknown actor."); initial.Add((value.Actor.Value, value.Kind), value.Q); }
            foreach (var actor in actors.Values)
            {
                var n = new ActorNeeds { ActorId = actor.Id.Value, Values = Needs.Rules.Select(r => new NeedState { Segment = new NeedAccumulator(initial.TryGetValue((actor.Id.Value, r.Kind), out long q) ? q : 0, 0, r.AwakeRate) }).ToArray() };
                actorNeeds.Add(actor.Id.Value, n); ScheduleNeedThreshold(n);
            }
        }
        private NeedMode ActualNeedMode(Actor actor) => actor.Action?.Waiting == false && actor.Action.Kind == ActionKind.Sleep ? NeedMode.Sleeping : actor.Action?.Waiting == false && actor.Action.Kind == ActionKind.Rest ? NeedMode.Resting : actor.Action != null && (IsWorkAction(actor.Action.Kind) || actor.Action.Kind == ActionKind.Clean || actor.Action.Kind == ActionKind.Construct) ? NeedMode.Working : NeedMode.Awake;
        private void RefreshNeedMode(Actor actor)
        {
            if (!actorNeeds.TryGetValue(actor.Id.Value, out var state)) return;
            var mode = ActualNeedMode(actor); if (mode == state.Mode) return;
            for (int i = 0; i < state.Values.Length; i++)
            {
                var value = state.Values[i]; value.Segment = value.Segment.ChangeRate(NeedNow, Config.TickDurationMs, Needs!.Rules[i].Rate(mode));
                var band = Needs.Rules[i].Band(value.Segment.At(NeedNow, Config.TickDurationMs)); if (band < value.Announced) value.Announced = band;
            }
            state.Mode = mode;
            if (state.Values.Select((v, i) => Needs!.Rules[i].Band(v.Segment.At(NeedNow, Config.TickDurationMs))).All(b => b == NeedBand.None)) state.UnmetReason = "";
            ScheduleNeedThreshold(state);
            Emit("NeedModeChanged", actor, detail: mode.ToString());
        }
        private long NextNeedThreshold(ActorNeeds state, long now)
        {
            long next = long.MaxValue;
            for (int i = 0; i < state.Values.Length; i++)
            {
                var value = state.Values[i]; var rule = Needs!.Rules[i]; long q = value.Segment.At(now, Config.TickDurationMs);
                if (rule.Band(q) > value.Announced) return now;
                foreach (int threshold in new[] { rule.Seek, rule.Urgent, rule.Critical })
                    if (threshold > 0 && q < threshold * NeedAccumulator.HourMilliseconds)
                    { long? crossing = value.Segment.FirstAtLeast(now, Config.TickDurationMs, threshold); if (crossing.HasValue) next = Math.Min(next, crossing.Value); }
                    else if (threshold > 0)
                    { long? crossing = value.Segment.FirstBelow(now, Config.TickDurationMs, threshold); if (crossing.HasValue) next = Math.Min(next, crossing.Value); }
            }
            return next == long.MaxValue ? -1 : next;
        }
        private void ScheduleNeedThreshold(ActorNeeds state)
        {
            foreach (var e in scheduled.ForActor(state.ActorId).Where(e => e.Kind == ScheduledKind.NeedThreshold).ToArray()) scheduled.Remove(e);
            long next = NextNeedThreshold(state, NeedNow);
            if (next >= 0 && processingTick >= 0 && next <= processingTick && processingPhase >= EventPhase.Decision) next = checked(processingTick + 1);
            state.NextThreshold = next;
            if (next >= 0) Schedule(new ScheduledEvent { Tick = next, Phase = EventPhase.Decision, Kind = ScheduledKind.NeedThreshold, Intent = new ActionIntent(new Id<ActorTag>(state.ActorId), ActionKind.Inspect, default) });
        }
        private void ReachNeedThreshold(Id<ActorTag> actorId)
        {
            var state = actorNeeds[actorId.Value]; state.NextThreshold = -1; var actor = actors[actorId]; NeedKind? critical = null;
            for (int i = 0; i < state.Values.Length; i++)
            {
                var value = state.Values[i]; var rule = Needs!.Rules[i]; long q = value.Segment.At(processingTick, Config.TickDurationMs); var band = rule.Band(q);
                if (band > value.Announced)
                {
                    NeedThresholdsReached++; Emit("NeedThresholdReached", actor, detail: rule.Kind + "/" + band + "; q=" + q);
                    if (band == NeedBand.Critical && !critical.HasValue) critical = rule.Kind;
                    state.UnmetReason = "NoKnownReliefService";
                }
                value.Announced = band;
            }
            if (critical.HasValue)
            {
                state.LastCriticalTick = processingTick;
                var group = ActiveEscort(actorId); var kind = critical.Value;
                switch (NeedPriority.CriticalResponse(group != null, group != null && EscortPayload(group).SubjectActorId == actorId.Value, group != null && (group.Status is EscortStatus.Escorting or EscortStatus.AwaitingIntake),
                    actor.Action?.Kind, kind, () => TravellingForNeed(actor, kind)))
                {
                    case CriticalPreemption.InterruptEscort: CriticalNeedInterruptions++; InterruptEscort(group!, "CriticalNeed/" + kind); break;
                    case CriticalPreemption.StopAtBoundary: CriticalNeedInterruptions++; actor.Action!.CancelAtBoundary = true; break;
                    case CriticalPreemption.CancelNow: CriticalNeedInterruptions++; CancelRunning(actor); break;
                }
                if (group == null) WakeGuard(actor);
            }
            ScheduleNeedThreshold(state); WakeNeed(actor, true); WakeDaily(actor);
        }
        private bool HasCriticalNeed(Id<ActorTag> actor) => actorNeeds.TryGetValue(actor.Value, out var n) && n.Values.Where((v, i) => Needs!.Rules[i].Band(v.Segment.At(NeedNow, Config.TickDurationMs)) == NeedBand.Critical).Any();
    }
}
