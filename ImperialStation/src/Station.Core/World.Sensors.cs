using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public enum SensorChannel { Authority }
    public sealed class MachineSensorSeed
    {
        public long Id { get; }
        public long MachineId { get; }
        public long FirstTick { get; }
        public long Interval { get; }
        public long Latency { get; }
        public bool Enabled { get; }
        public SensorChannel Channel { get; }
        public MachineSensorSeed(long id, long machineId, long firstTick, long interval, long latency = 2, bool enabled = true, SensorChannel channel = SensorChannel.Authority)
        {
            if (id < 1 || machineId < 1 || firstTick < 0 || interval < 1 || latency < 1 || !Enum.IsDefined(typeof(SensorChannel), channel)) throw new ArgumentException("Invalid machine sensor scope, schedule or channel.");
            Id = id; MachineId = machineId; FirstTick = firstTick; Interval = interval; Latency = latency; Enabled = enabled; Channel = channel;
        }
    }
    public sealed class SensorSnapshot
    {
        public long Id { get; internal set; }
        public long MachineId { get; internal set; }
        public long FirstTick { get; internal set; }
        public long Interval { get; internal set; }
        public long Latency { get; internal set; }
        public bool Enabled { get; internal set; }
        public SensorChannel Channel { get; internal set; }
        public long NextSampleTick { get; internal set; }
        internal SensorSnapshot Copy() => (SensorSnapshot)MemberwiseClone();
    }
    public sealed class SensorReading
    {
        public long Id { get; internal set; }
        public long SensorId { get; internal set; }
        public FactKey Key { get; internal set; }
        public FactValue Value { get; internal set; }
        public long ObservedTick { get; internal set; }
        public long DeliveryTick { get; internal set; }
        public bool Delivered { get; internal set; }
        internal SensorReading Copy() => (SensorReading)MemberwiseClone();
    }
    public sealed partial class World
    {
        private PerceptionCatalog perception = new PerceptionCatalog();
        private readonly SortedDictionary<long, SensorSnapshot> sensors = new SortedDictionary<long, SensorSnapshot>();
        private readonly SortedDictionary<long, SensorReading> sensorReadings = new SortedDictionary<long, SensorReading>();
        private long nextSensorReading = 1;
        public IReadOnlyList<PerceptionRule> InspectPerceptionRules() => perception.Rules;
        public IReadOnlyList<SensorSnapshot> InspectSensors() => sensors.Values.Select(s => s.Copy()).ToArray();
        public IReadOnlyList<SensorReading> InspectSensorReadings() => sensorReadings.Values.Select(s => s.Copy()).ToArray();
        private void InitializeSensors(IEnumerable<MachineSensorSeed> seeds)
        {
            foreach (var seed in seeds)
            {
                if (!machines.ContainsKey(seed.MachineId)) throw new ArgumentException("Machine sensor scope must name a real machine.");
                var sensor = new SensorSnapshot { Id = seed.Id, MachineId = seed.MachineId, FirstTick = seed.FirstTick, Interval = seed.Interval, Latency = seed.Latency, Enabled = seed.Enabled, Channel = seed.Channel, NextSampleTick = -1 };
                sensors.Add(seed.Id, sensor); if (sensor.Enabled) ScheduleSensorSample(sensor, sensor.FirstTick);
            }
        }
        private void ScheduleSensorSample(SensorSnapshot sensor, long tick)
        {
            sensor.NextSampleTick = tick;
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Observation, Kind = ScheduledKind.SensorSample, Value = sensor.Id });
        }
        public void ScheduleSensorChange(long tick, long sensorId, bool enabled)
        {
            ValidateFuture(tick); if (!sensors.ContainsKey(sensorId)) throw new ArgumentException("Unknown sensor.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.SensorChange, ActionId = sensorId, Value = enabled ? 1 : 0 });
        }
        private void ChangeSensor(long id, bool enabled)
        {
            var sensor = sensors[id]; if (sensor.Enabled == enabled) return;
            long tick = sensor.FirstTick;
            if (enabled && tick < processingTick)
            { long delta = processingTick - tick; long periods = delta / sensor.Interval + (delta % sensor.Interval == 0 ? 0 : 1); tick = checked(tick + periods * sensor.Interval); }
            sensor.Enabled = enabled;
            foreach (var pending in scheduled.Where(e => e.Kind == ScheduledKind.SensorSample && e.Value == id).ToArray()) scheduled.Remove(pending);
            sensor.NextSampleTick = -1;
            if (enabled)
            {
                ScheduleSensorSample(sensor, tick);
            }
            Emit("SensorStateChanged", sensorId: id, detail: "enabled=" + enabled);
        }
        private void SampleSensor(long id)
        {
            var sensor = sensors[id]; if (!sensor.Enabled || sensor.NextSampleTick != processingTick) throw new InvalidOperationException("Invalid sensor schedule.");
            long delivery = checked(processingTick + sensor.Latency), next = checked(processingTick + sensor.Interval);
            _ = checked(nextSensorReading + 1);
            // This registered capability measures one machine status. No inventory, people,
            // installed specification or citizen knowledge enters its payload.
            var reading = new SensorReading { Id = nextSensorReading++, SensorId = id, Key = new FactKey(SubjectKind.Machine, sensor.MachineId, AttributeKind.Operational),
                Value = FactValue.Boolean(MachineOperational(machines[sensor.MachineId])), ObservedTick = processingTick, DeliveryTick = delivery };
            sensorReadings.Add(reading.Id, reading);
            long cause = Emit("SensorObserved", sensorId: id, readingId: reading.Id, factKey: reading.Key, factValue: reading.Value);
            Schedule(new ScheduledEvent { Tick = delivery, Phase = EventPhase.Delivery, Kind = ScheduledKind.SensorDelivery, Value = reading.Id, CauseId = cause });
            ScheduleSensorSample(sensor, next);
        }
        private void DeliverSensor(long id, long cause)
        {
            var reading = sensorReadings[id]; if (reading.Delivered) return;
            reading.Delivered = true;
            long delivered = Emit("SensorDelivered", sensorId: reading.SensorId, readingId: id, causeId: cause, factKey: reading.Key, factValue: reading.Value);
            ReceiveBelief(reading.Key, reading.Value, reading.ObservedTick, 0, reading.SensorId, reading.Id, delivered);
        }
        private void ReceiveBelief(FactKey key, FactValue value, long observed, long messageId, long sensorId, long readingId, long cause)
        {
            var belief = new BeliefSnapshot { Key = key, Value = value, MessageId = messageId, SensorId = sensorId, ReadingId = readingId,
                Source = sensorId == 0 ? "Reported" : "Sensor", ObservedTick = observed, ReceivedTick = processingTick,
                ContradictsPrevious = beliefs.TryGetValue(key, out var prior) && !prior.Value.Equals(value) };
            beliefHistory.Add(belief); beliefs[key] = belief;
            Emit("BeliefChanged", messageId: messageId, sensorId: sensorId, readingId: readingId, causeId: cause, detail: key + " = " + value, factKey: key, factValue: value);
        }
        private void ObservePerceived(Actor actor, FactKey key, FactValue value, EvidenceKind source, long sourceId, long cause, bool passive)
        {
            if (passive && perception.Mode(key.SubjectKind, key.Attribute) != PerceptionMode.Passive) return;
            Learn(actor, key, value, source, sourceId, processingTick, cause);
        }
    }
}
