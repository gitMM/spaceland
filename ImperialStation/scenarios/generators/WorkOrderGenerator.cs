using Station.Contracts;
using Station.Core;
using Station.Runner;

namespace Station.Scenarios;

public static class WorkOrderGenerator
{
    public static LoadedScenario Generate(string canonicalJson, string catalogJson, ulong seed)
    {
        var data = JsonData.Read<ScenarioData>(canonicalJson, "canonical work order"); var initial = data.InitialState;
        var rng = Pcg32.Create(seed, "work-order-generator"); var info = initial.Information!; var work = initial.Work!; var jobs = initial.JobSystem!;
        initial.Id = "work_order_variant_" + seed; initial.Seed = seed; initial.Horizon = 300;
        long actor = 10 + rng.Next(100), account = 1000 + rng.Next(100), machine = 200 + rng.Next(100), station = 300 + rng.Next(100), terminal = 400 + rng.Next(100), job = 500 + rng.Next(100), itemOffset = 100 + rng.Next(100);
        int dx = rng.Next(81) - 40, dy = rng.Next(9) - 4, dz = rng.Next(81) - 40, axis = rng.Next(3), sign = rng.Next(2) == 0 ? -1 : 1;
        CoordinateData Transform(CoordinateData p)
        {
            int x = sign * p.X;
            return axis switch { 0 => new() { X = dx + x, Y = dy + p.Y, Z = dz + p.Z }, 1 => new() { X = dx + p.Z, Y = dy + x, Z = dz + p.Y }, _ => new() { X = dx + p.Y, Y = dy + p.Z, Z = dz + x } };
        }
        foreach (var tile in initial.Tiles) { tile.Position = Transform(tile.Position); tile.TraversalOverride = 1 + rng.Next(5); }
        initial.Actors[0].Id = actor; initial.Actors[0].AccountId = account; initial.Actors[0].Position = Transform(initial.Actors[0].Position);
        foreach (var item in initial.Items) { item.Id += itemOffset; if (item.LocationKind == LocationKind.ActorInventory) item.Owner = actor; }
        foreach (var fact in info.Facts)
        {
            fact.ActorId = actor;
            if (fact.Key.SubjectKind == SubjectKind.Item) fact.Key.SubjectId += itemOffset;
            if (fact.Key.SubjectKind == SubjectKind.Machine) fact.Key.SubjectId = machine;
            if (fact.Value.Coordinate != null) fact.Value.Coordinate = Transform(fact.Value.Coordinate);
        }
        work.Machines[0].Id = machine; work.Machines[0].Position = Transform(work.Machines[0].Position); work.Machines[0].InstallTicks = 2 + rng.Next(6);
        work.Workstations[0].Id = station; work.Workstations[0].Position = Transform(work.Workstations[0].Position);
        info.Terminals[0].Id = terminal; info.Terminals[0].Position = Transform(info.Terminals[0].Position);
        info.Credentials[0].Holder = actor; info.Credentials[0].Account = account; info.Credentials[0].CardItem += itemOffset;
        info.Configuration.LoginTicks = 1 + rng.Next(3); info.Configuration.ReadBaseTicks = info.Configuration.ReadMaximumTicks = 1 + rng.Next(3);
        info.Configuration.PostTicks = 1 + rng.Next(3); info.Configuration.LatencyTicks = 1 + rng.Next(4);
        jobs.Jobs[0].Id = job; jobs.Jobs[0].MachineId = machine;
        jobs.Assignments[0].JobId = job; jobs.Assignments[0].ActorId = actor; jobs.Assignments[0].Expires = 1000;
        jobs.CheckIns[0].ActorId = actor; jobs.CheckIns[0].FirstTick = rng.Next(7); jobs.CheckIns[0].Interval = 10 + rng.Next(16);
        data.Assertions = new() { new() { Tick = 300, Kind = "JobPhysical", JobId = job, State = "CompletedPhysically" }, new() { Tick = 300, Kind = "JobAuthority", JobId = job, State = "WorkCompleted" }, new() { Tick = 300, Kind = "Accounting" } };
        return new LoadedScenario(JsonData.Write(data), catalogJson, "generated work order " + seed);
    }
}
