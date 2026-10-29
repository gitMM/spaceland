using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteNeeds(BinaryWriter w)
        {
            w.Write(Needs != null); if (Needs == null) return;
            foreach (var r in Needs.Rules) { w.Write((int)r.Kind); w.Write(r.AwakeRate); w.Write(r.WorkingRate); w.Write(r.SleepingRate); w.Write(r.RestingRate); w.Write(r.Seek); w.Write(r.Urgent); w.Write(r.Critical); }
            w.Write(NeedThresholdsReached); w.Write(CriticalNeedInterruptions); w.Write(actorNeeds.Count);
            foreach (var n in actorNeeds.Values)
            {
                w.Write(n.ActorId); w.Write((int)n.Mode); w.Write(n.NextThreshold); w.Write(n.LastCriticalTick); w.Write(n.UnmetReason);
                foreach (var v in n.Values) { w.Write(v.Segment.AnchorQ); w.Write(v.Segment.AnchorTick); w.Write(v.Segment.RatePerHour); w.Write((int)v.Announced); }
            }
        }
        private void ReadNeeds(BinaryReader r)
        {
            if (!r.ReadBoolean()) { if (scheduled.Any(e => e.Kind == ScheduledKind.NeedThreshold)) throw new ArgumentException("Need timer has no need configuration."); return; }
            var rules = new NeedRule[5];
            for (int i = 0; i < rules.Length; i++) rules[i] = new NeedRule(ReadEnum<NeedKind>(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            if (!rules.Select(x => x.Kind).SequenceEqual(Enum.GetValues(typeof(NeedKind)).Cast<NeedKind>())) throw new ArgumentException("Invalid need rule ordering.");
            Needs = new NeedConfig(rules); NeedThresholdsReached = r.ReadInt64(); CriticalNeedInterruptions = r.ReadInt64();
            int count = Count(r); if (count != actors.Count || NeedThresholdsReached < 0 || CriticalNeedInterruptions < 0 || CriticalNeedInterruptions > NeedThresholdsReached) throw new ArgumentException("Invalid need population or counters.");
            for (int i = 0; i < count; i++)
            {
                var n = new ActorNeeds { ActorId = r.ReadInt64(), Mode = ReadEnum<NeedMode>(r), NextThreshold = r.ReadInt64(), LastCriticalTick = r.ReadInt64(), UnmetReason = r.ReadString(), Values = new NeedState[5] };
                if (!actors.TryGetValue(new Id<ActorTag>(n.ActorId), out var actor) || n.Mode != ActualNeedMode(actor) || n.LastCriticalTick < -1 || n.LastCriticalTick > LastProcessedTick || !(n.UnmetReason is "" or "NoKnownReliefService")) throw new ArgumentException("Invalid need owner, mode or history.");
                for (int j = 0; j < n.Values.Length; j++)
                {
                    var v = new NeedState { Segment = new NeedAccumulator(r.ReadInt64(), r.ReadInt64(), r.ReadInt32()), Announced = ReadEnum<NeedBand>(r) }; n.Values[j] = v;
                    if (v.Segment.AnchorTick > NeedNow || v.Segment.RatePerHour != rules[j].Rate(n.Mode) || v.Announced > rules[j].Band(v.Segment.At(NeedNow, Config.TickDurationMs))) throw new ArgumentException("Invalid exact need segment or announced threshold.");
                }
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.NeedThreshold && e.Intent.Actor.Value == n.ActorId).ToArray();
                long expected = NextNeedThreshold(n, NeedNow);
                if (n.NextThreshold < -1 || timers.Length != (n.NextThreshold < 0 ? 0 : 1) || timers.Any(e => e.Tick != n.NextThreshold || e.Phase != EventPhase.Decision) ||
                    n.NextThreshold != expected && !(expected == LastProcessedTick && n.NextThreshold == checked(LastProcessedTick + 1)) || n.NextThreshold >= 0 && n.NextThreshold <= LastProcessedTick) throw new ArgumentException("Invalid need threshold timer.");
                actorNeeds.Add(n.ActorId, n);
            }
            if (scheduled.Any(e => e.Kind == ScheduledKind.NeedThreshold && !actorNeeds.ContainsKey(e.Intent.Actor.Value))) throw new ArgumentException("Unregistered need timer.");
        }
    }
}
