using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum NeedKind { Hunger, Bladder, SleepPressure, WorkStrain, Social }
    public enum NeedMode { Awake, Working, Sleeping, Resting }
    public enum NeedBand { None, Seek, Urgent, Critical }

    // Exact rate segment. Querying a segment never changes its anchor or remainder.
    public readonly struct NeedAccumulator
    {
        public const long HourMilliseconds = 3600000;
        public const long MaximumQ = 1000 * HourMilliseconds;
        public long AnchorQ { get; }
        public long AnchorTick { get; }
        public int RatePerHour { get; }
        public NeedAccumulator(long anchorQ, long anchorTick, int ratePerHour)
        {
            if (anchorQ < 0 || anchorQ > MaximumQ || anchorTick < 0) throw new ArgumentOutOfRangeException(nameof(anchorQ));
            AnchorQ = anchorQ; AnchorTick = anchorTick; RatePerHour = ratePerHour;
        }
        public long At(long tick, long tickDurationMs)
        {
            if (tick < AnchorTick || tickDurationMs < 1) throw new ArgumentOutOfRangeException(nameof(tick));
            long q = checked(AnchorQ + checked(checked((long)RatePerHour * (tick - AnchorTick)) * tickDurationMs));
            return Math.Max(0, Math.Min(MaximumQ, q));
        }
        public int Displayed(long tick, long tickDurationMs) => (int)(At(tick, tickDurationMs) / HourMilliseconds);
        public NeedAccumulator ChangeRate(long tick, long tickDurationMs, int rate)
        { long q = At(tick, tickDurationMs); return rate == RatePerHour ? this : new NeedAccumulator(q, tick, rate); }
        public NeedAccumulator Relieve(long tick, long tickDurationMs, int points)
        {
            if (points < 0 || points > 1000) throw new ArgumentOutOfRangeException(nameof(points));
            return new NeedAccumulator(Math.Max(0, At(tick, tickDurationMs) - checked(points * HourMilliseconds)), tick, RatePerHour);
        }
        public NeedAccumulator Reset(long tick, long tickDurationMs) { _ = At(tick, tickDurationMs); return new NeedAccumulator(0, tick, RatePerHour); }
        public long? FirstAtLeast(long fromTick, long tickDurationMs, int threshold)
        {
            if (threshold < 0 || threshold > 1000) throw new ArgumentOutOfRangeException(nameof(threshold));
            long delta = checked(threshold * HourMilliseconds - At(fromTick, tickDurationMs));
            if (delta <= 0) return fromTick;
            if (RatePerHour <= 0) return null;
            long step = checked(RatePerHour * tickDurationMs);
            long ticks = delta / step + (delta % step == 0 ? 0 : 1);
            return checked(fromTick + ticks);
        }
        public long? FirstBelow(long fromTick, long tickDurationMs, int threshold)
        {
            if (threshold < 1 || threshold > 1000) throw new ArgumentOutOfRangeException(nameof(threshold));
            long delta = checked(At(fromTick, tickDurationMs) - threshold * HourMilliseconds + 1);
            if (delta <= 0) return fromTick;
            if (RatePerHour >= 0) return null;
            long step = checked(-(long)RatePerHour * tickDurationMs);
            return checked(fromTick + delta / step + (delta % step == 0 ? 0 : 1));
        }
    }
    public sealed class NeedRule
    {
        public NeedKind Kind { get; }
        public int AwakeRate { get; }
        public int WorkingRate { get; }
        public int SleepingRate { get; }
        public int RestingRate { get; }
        public int Seek { get; }
        public int Urgent { get; }
        public int Critical { get; }
        public NeedRule(NeedKind kind, int awakeRate, int workingRate, int sleepingRate, int restingRate, int seek = 0, int urgent = 0, int critical = 0)
        {
            if (!Enum.IsDefined(typeof(NeedKind), kind) || new[] { awakeRate, workingRate, sleepingRate, restingRate }.Any(r => r < -1000000 || r > 1000000) ||
                new[] { seek, urgent, critical }.Any(t => t < 0 || t > 1000) || urgent > 0 && urgent < seek || critical > 0 && critical < Math.Max(seek, urgent)) throw new ArgumentException("Invalid need rate or threshold order.");
            Kind = kind; AwakeRate = awakeRate; WorkingRate = workingRate; SleepingRate = sleepingRate; RestingRate = restingRate; Seek = seek; Urgent = urgent; Critical = critical;
        }
        public int Rate(NeedMode mode) => mode switch { NeedMode.Awake => AwakeRate, NeedMode.Working => WorkingRate, NeedMode.Sleeping => SleepingRate, NeedMode.Resting => RestingRate, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
        public NeedBand Band(long q) => Critical > 0 && q >= Critical * NeedAccumulator.HourMilliseconds ? NeedBand.Critical : Urgent > 0 && q >= Urgent * NeedAccumulator.HourMilliseconds ? NeedBand.Urgent : Seek > 0 && q >= Seek * NeedAccumulator.HourMilliseconds ? NeedBand.Seek : NeedBand.None;
    }
    public sealed class NeedConfig
    {
        public IReadOnlyList<NeedRule> Rules { get; }
        public NeedConfig(IEnumerable<NeedRule>? rules = null)
        {
            var all = (rules ?? Defaults()).OrderBy(r => r.Kind).ToArray();
            if (all.Length != 5 || all.Select(r => r.Kind).Distinct().Count() != 5) throw new ArgumentException("Needs require exactly one rule per need kind.");
            Rules = Array.AsReadOnly(all);
        }
        private static IEnumerable<NeedRule> Defaults() => new[] {
            new NeedRule(NeedKind.Hunger, 60, 60, 60, 60, 300, 700, 900), new NeedRule(NeedKind.Bladder, 100, 100, 40, 100, 500, 800, 950),
            new NeedRule(NeedKind.SleepPressure, 50, 50, -100, 50, urgent: 850, critical: 1000), new NeedRule(NeedKind.WorkStrain, 0, 60, -60, -120, 500), new NeedRule(NeedKind.Social, 20, 20, 20, 20, 400) };
    }
    public sealed class NeedInitialValue
    {
        public Id<ActorTag> Actor { get; }
        public NeedKind Kind { get; }
        public long Q { get; }
        public NeedInitialValue(Id<ActorTag> actor, NeedKind kind, long q)
        { if (actor.Value < 1 || !Enum.IsDefined(typeof(NeedKind), kind) || q < 0 || q > NeedAccumulator.MaximumQ) throw new ArgumentException("Invalid initial need."); Actor = actor; Kind = kind; Q = q; }
    }
    public sealed class NeedSeed
    {
        public NeedConfig Config { get; } public ConversationConfig Conversations { get; }
        public IReadOnlyList<NeedInitialValue> Initial { get; }
        public IReadOnlyList<NeedServiceDefinition> Services { get; }
        public IReadOnlyList<long>? AutonomousActors { get; }
        public NeedSeed(NeedConfig? config = null, IEnumerable<NeedInitialValue>? initial = null, IEnumerable<NeedServiceDefinition>? services = null, IEnumerable<long>? autonomousActors = null, ConversationConfig? conversations = null)
        { Conversations = conversations ?? new ConversationConfig(); AutonomousActors = autonomousActors == null ? null : Array.AsReadOnly(autonomousActors.OrderBy(id => id).ToArray()); Config = config ?? new NeedConfig(); Initial = Array.AsReadOnly((initial ?? Array.Empty<NeedInitialValue>()).ToArray()); Services = Array.AsReadOnly((services ?? Array.Empty<NeedServiceDefinition>()).OrderBy(s => s.Id).ToArray()); }
    }
    public sealed class NeedValue
    {
        public NeedKind Kind { get; }
        public long Q { get; }
        public int Displayed => (int)(Q / NeedAccumulator.HourMilliseconds);
        public NeedBand Band { get; }
        public NeedValue(NeedKind kind, long q, NeedBand band) { Kind = kind; Q = q; Band = band; }
    }
}
