using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteSensors(BinaryWriter w)
        {
            w.Write(perception.Rules.Count); foreach (var rule in perception.Rules) { w.Write((int)rule.Subject); w.Write((int)rule.Attribute); w.Write((int)rule.Mode); }
            w.Write(nextSensorReading); w.Write(sensors.Count);
            foreach (var s in sensors.Values)
            { w.Write(s.Id); w.Write(s.MachineId); w.Write(s.FirstTick); w.Write(s.Interval); w.Write(s.Latency); w.Write(s.Enabled); w.Write((int)s.Channel); w.Write(s.NextSampleTick); }
            w.Write(sensorReadings.Count);
            foreach (var reading in sensorReadings.Values)
            { w.Write(reading.Id); w.Write(reading.SensorId); WriteKey(w, reading.Key); WriteValue(w, reading.Value); w.Write(reading.ObservedTick); w.Write(reading.DeliveryTick); w.Write(reading.Delivered); }
        }
        private void ReadSensors(BinaryReader r)
        {
            int count = Count(r); var rules = new List<PerceptionRule>();
            for (int i = 0; i < count; i++) rules.Add(new PerceptionRule(ReadEnum<SubjectKind>(r), ReadEnum<AttributeKind>(r), ReadEnum<PerceptionMode>(r)));
            perception = new PerceptionCatalog(rules); if (count != perception.Rules.Count) throw new ArgumentException("Snapshot perception catalog is incomplete.");
            nextSensorReading = r.ReadInt64(); count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var s = new SensorSnapshot { Id = r.ReadInt64(), MachineId = r.ReadInt64(), FirstTick = r.ReadInt64(), Interval = r.ReadInt64(), Latency = r.ReadInt64(), Enabled = r.ReadBoolean(), Channel = ReadEnum<SensorChannel>(r), NextSampleTick = r.ReadInt64() };
                _ = new MachineSensorSeed(s.Id, s.MachineId, s.FirstTick, s.Interval, s.Latency, s.Enabled, s.Channel);
                if (Information == null || !machines.ContainsKey(s.MachineId) || (s.Enabled ? (RuntimeError == null && s.NextSampleTick <= LastProcessedTick) || s.NextSampleTick < s.FirstTick || (s.NextSampleTick - s.FirstTick) % s.Interval != 0 : s.NextSampleTick != -1))
                    throw new ArgumentException("Invalid sensor scope or next sample.");
                sensors.Add(s.Id, s);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var reading = new SensorReading { Id = r.ReadInt64(), SensorId = r.ReadInt64(), Key = ReadKey(r), Value = ReadValue(r), ObservedTick = r.ReadInt64(), DeliveryTick = r.ReadInt64(), Delivered = r.ReadBoolean() };
                if (reading.Id != i + 1L || !sensors.TryGetValue(reading.SensorId, out var sensor) || reading.Key.SubjectKind != SubjectKind.Machine || reading.Key.SubjectId != sensor.MachineId || reading.Key.Attribute != AttributeKind.Operational ||
                    reading.Value.Kind != FactValueKind.Boolean || !(reading.Value.Number is 0 or 1) || reading.ObservedTick < sensor.FirstTick || (RuntimeError == null && reading.ObservedTick > LastProcessedTick) || (reading.ObservedTick - sensor.FirstTick) % sensor.Interval != 0 ||
                    reading.DeliveryTick <= reading.ObservedTick || reading.DeliveryTick - reading.ObservedTick != sensor.Latency || (RuntimeError == null && reading.Delivered != (reading.DeliveryTick <= LastProcessedTick))) throw new ArgumentException("Invalid captured sensor reading.");
                sensorReadings.Add(reading.Id, reading);
            }
            if (nextSensorReading != sensorReadings.Count + 1L) throw new ArgumentException("Invalid sensor reading sequence.");
            foreach (var sensor in sensors.Values)
            {
                var pending = scheduled.Where(e => e.Kind == ScheduledKind.SensorSample && e.Value == sensor.Id).ToArray();
                // Failed worlds are diagnostic checkpoints: an overflowing event may
                // already have been consumed. They cannot resume simulation.
                if ((RuntimeError == null && pending.Length != (sensor.Enabled ? 1 : 0)) || pending.Length > (sensor.Enabled ? 1 : 0) || pending.Any(e => e.Tick != sensor.NextSampleTick || e.Phase != EventPhase.Observation)) throw new ArgumentException("Missing or duplicate sensor watchdog.");
            }
            foreach (var reading in sensorReadings.Values)
            {
                var pending = scheduled.Where(e => e.Kind == ScheduledKind.SensorDelivery && e.Value == reading.Id).ToArray();
                if (pending.Length != (reading.Delivered ? 0 : 1) || pending.Any(e => e.Tick != reading.DeliveryTick || e.Phase != EventPhase.Delivery)) throw new ArgumentException("Invalid in-flight sensor delivery.");
            }
            foreach (var e in scheduled.Where(e => e.Kind is ScheduledKind.SensorChange or ScheduledKind.SensorSample or ScheduledKind.SensorDelivery))
                if (e.Kind == ScheduledKind.SensorDelivery ? !sensorReadings.ContainsKey(e.Value) : !sensors.ContainsKey(e.Kind == ScheduledKind.SensorChange ? e.ActionId : e.Value)) throw new ArgumentException("Unknown sensor event target.");
            foreach (var belief in beliefHistory.Concat(beliefs.Values))
            {
                if (belief.Source == "Sensor")
                {
                    if (belief.MessageId != 0 || !sensorReadings.TryGetValue(belief.ReadingId, out var reading) || !reading.Delivered || belief.SensorId != reading.SensorId || !belief.Key.Equals(reading.Key) || !belief.Value.Equals(reading.Value) || belief.ObservedTick != reading.ObservedTick || belief.ReceivedTick != reading.DeliveryTick)
                        throw new ArgumentException("Sensor belief has invalid evidence provenance.");
                }
                else if (belief.Source != "Reported" || belief.SensorId != 0 || belief.ReadingId != 0) throw new ArgumentException("Invalid belief source.");
            }
        }
    }
}
