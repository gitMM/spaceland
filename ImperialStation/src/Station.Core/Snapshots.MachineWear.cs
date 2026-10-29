using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteMachineWear(BinaryWriter w)
        {
            w.Write(MachineWear != null); if (MachineWear != null) w.Write(MachineWear.MeanIntervalTicks);
            w.Write(machineWear.Count);
            foreach (var s in machineWear.Values) { w.Write(s.MachineId); w.Write(s.FaultEvents); w.Write(s.ConsumedParts); w.Write(s.LastFaultTick); w.Write(s.LastConsumedItemId); w.Write(s.NextWearTick); }
            w.Write(faultSinks.Count); foreach (var p in faultSinks) { w.Write(p.Key); w.Write(p.Value); }
        }
        private void ReadMachineWear(BinaryReader r)
        {
            MachineWear = r.ReadBoolean() ? new MachineWearConfig(r.ReadInt32()) : null;
            if (MachineWear != null && !WorkEnabled) throw new ArgumentException("Saved wear requires work definitions.");
            machineWear.Clear(); int count = Count(r); bool enabled = MachineWear?.MeanIntervalTicks > 0;
            for (int i = 0; i < count; i++)
            {
                var s = new MachineWearSnapshot { MachineId = r.ReadInt64(), FaultEvents = r.ReadInt64(), ConsumedParts = r.ReadInt64(), LastFaultTick = r.ReadInt64(), LastConsumedItemId = r.ReadInt64(), NextWearTick = r.ReadInt64() };
                if (!machines.ContainsKey(s.MachineId) || s.FaultEvents < 0 || s.ConsumedParts < 0 || s.ConsumedParts > s.FaultEvents ||
                    (s.FaultEvents == 0 ? s.LastFaultTick != -1 : s.LastFaultTick < 0 || s.LastFaultTick > LastProcessedTick) ||
                    (s.ConsumedParts == 0 ? s.LastConsumedItemId != 0 : s.LastConsumedItemId <= 0 || s.LastConsumedItemId >= nextItem || items.ContainsKey(new Id<ItemTag>(s.LastConsumedItemId))))
                    throw new ArgumentException("Invalid machine fault counters.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.MachineWear && e.Value == s.MachineId).ToArray();
                if (s.NextWearTick < -1 || !enabled && s.NextWearTick != -1 || enabled && RuntimeError == null && s.NextWearTick <= LastProcessedTick ||
                    timers.Length != (s.NextWearTick < 0 ? 0 : 1) || timers.Any(e => e.Tick != s.NextWearTick || e.Phase != EventPhase.Disturbance)) throw new ArgumentException("Invalid wear timer.");
                machineWear.Add(s.MachineId, s);
            }
            if (!machineWear.Keys.SequenceEqual(machines.Keys) || streams.ContainsKey("wear") != (enabled && machines.Count > 0)) throw new ArgumentException("Invalid machine wear registry/stream.");
            if (credentials.Values.Any(c => items.TryGetValue(c.Definition.Card, out var card) && card.Location.Kind == LocationKind.MachineSlot)) throw new ArgumentException("Credential card occupies consumable machine slot.");
            count = Count(r);
            for (int i = 0; i < count; i++) { string resource = r.ReadString(); long units = r.ReadInt64(); if (!resources.ContainsKey(resource) || units <= 0) throw new ArgumentException("Invalid fault resource sink."); faultSinks.Add(resource, units); }
            if (faultSinks.Values.Sum(v => (decimal)v) != machineWear.Values.Sum(s => (decimal)s.ConsumedParts)) throw new ArgumentException("Fault sink count disagrees with failed parts.");
            foreach (var e in scheduled.Where(e => e.Kind is ScheduledKind.MachineWear or ScheduledKind.MachineFault))
                if (!machines.ContainsKey(e.Value) || e.Phase != EventPhase.Disturbance) throw new ArgumentException("Invalid machine fault event.");
        }
    }
}
