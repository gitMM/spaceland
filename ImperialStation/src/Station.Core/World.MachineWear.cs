using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        public MachineWearConfig? MachineWear { get; private set; }
        private readonly SortedDictionary<long, MachineWearSnapshot> machineWear = new SortedDictionary<long, MachineWearSnapshot>();
        private readonly SortedDictionary<string, long> faultSinks = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public IReadOnlyList<MachineWearSnapshot> InspectMachineWear() => machineWear.Values.Select(m => m.Copy()).ToArray();
        public IReadOnlyList<FaultSinkSnapshot> InspectFaultSinks() => faultSinks.Select(p => new FaultSinkSnapshot(p.Key, p.Value)).ToArray();
        private void InitializeMachineWear(MachineWearConfig? config)
        {
            if (config != null && !WorkEnabled) throw new ArgumentException("Machine wear requires physical work definitions.");
            MachineWear = config;
            foreach (long id in machines.Keys) machineWear.Add(id, new MachineWearSnapshot { MachineId = id });
            if (config == null || config.MeanIntervalTicks == 0 || machineWear.Count == 0) return;
            streams.Add("wear", Pcg32.Create(Seed, "wear"));
            foreach (var state in machineWear.Values) ScheduleWear(state, 0);
        }
        private void ScheduleWear(MachineWearSnapshot state, long from)
        {
            int mean = MachineWear!.MeanIntervalTicks, low = mean / 2, high = (int)(2L * mean - low);
            state.NextWearTick = checked(from + low + streams["wear"].Next(high - low + 1));
            Schedule(new ScheduledEvent { Tick = state.NextWearTick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.MachineWear, Value = state.MachineId });
        }
        public void ScheduleMachineFault(long tick, long machineId)
        {
            ValidateFuture(tick);
            if (!machines.ContainsKey(machineId)) throw new ArgumentException("MachineFault requires a known machine.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.MachineFault, Value = machineId });
        }
        private void WearMachine(long id)
        {
            var state = machineWear[id]; state.NextWearTick = -1;
            FaultMachine(id, "Wear"); ScheduleWear(state, processingTick);
        }
        private void FaultMachine(long id, string reason)
        {
            var machine = machines[id]; var state = machineWear[id]; var part = InstalledItem(id);
            long events = checked(state.FaultEvents + 1), consumed = checked(state.ConsumedParts + (part == null ? 0 : 1));
            long total = 0;
            if (part != null) { faultSinks.TryGetValue(part.Resource, out total); total = checked(total + 1); }
            state.FaultEvents = events; state.ConsumedParts = consumed; state.LastFaultTick = processingTick;
            // Declared destructive sink: only the installed, single-unit cartridge is
            // exhausted. Inventory, pending installation inputs and tools are untouched.
            if (part != null)
            {
                RemoveItem(part.Id); faultSinks[part.Resource] = total; state.LastConsumedItemId = part.Id.Value;
            }
            long cause = Emit("MachineFaulted", item: part?.Id ?? default, target: machine.Position, quantity: part == null ? 0 : 1,
                detail: "Machine:" + id + "; " + reason, factKey: new FactKey(SubjectKind.Machine, id, AttributeKind.Operational), factValue: FactValue.Boolean(false));
            if (part != null) Emit("FaultPartConsumed", item: part.Id, target: machine.Position, quantity: 1, causeId: cause, detail: part.Resource);
            foreach (var actor in actors.Values.Where(a => a.Position == machine.Position && a.Action?.Kind != ActionKind.Move)) ScheduleSensing(actor, cause);
            AssertAccounting();
        }
    }
}
