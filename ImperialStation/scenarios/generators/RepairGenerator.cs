using Station.Contracts;
using Station.Core;
using Station.Runner;

namespace Station.Scenarios;

public enum RepairVariant { Existing, Fabrication, MissingKnown, NoPrerequisites, UnknownSpecification, StackedExisting, StackedFabrication, StackedStash }

public static class RepairGenerator
{
    public static LoadedScenario Generate(string canonicalJson, string catalogJson, ulong seed, RepairVariant variant)
    {
        var rng = Pcg32.Create(seed, "repair-generator");
        var scenario = JsonData.Read<ScenarioData>(canonicalJson, "canonical repair");
        var catalog = JsonData.Read<CatalogData>(catalogJson, "repair catalog");
        var initial = scenario.InitialState; var information = initial.Information!;
        initial.Id = "repair_variant_" + variant + "_" + seed; initial.Seed = seed; initial.Horizon = 400;
        scenario.ScriptedActors.Clear();
        foreach (var actor in initial.Actors) actor.Scripted = false;
        if (variant is RepairVariant.Fabrication or RepairVariant.NoPrerequisites or RepairVariant.UnknownSpecification or RepairVariant.StackedFabrication)
        { initial.Items.RemoveAll(i => i.Id == 4); information.Facts.RemoveAll(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.SubjectId == 4); }
        if (variant is RepairVariant.Existing or RepairVariant.StackedExisting or RepairVariant.StackedStash) initial.Items.RemoveAll(i => i.Id is 1 or 2);
        long batch = 2 + rng.Next(7);
        if (variant is RepairVariant.StackedExisting or RepairVariant.StackedStash)
        {
            initial.Items.Single(i => i.Id == 4).Quantity = batch;
            information.Facts.Single(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.SubjectId == 4 && f.Key.Attribute == AttributeKind.Quantity).Value.Number = batch;
        }
        if (variant == RepairVariant.StackedFabrication) catalog.Recipes[0].Outputs.Single(o => o.Resource == "correct").Quantity = batch;
        if (variant == RepairVariant.StackedStash)
        {
            long stash = 500 + rng.Next(100); initial.Work!.Stashes.Add(new ProjectStashData { Id = stash, ProjectId = 1000 + rng.Next(100), Position = new() { X = 2 } });
            var item = initial.Items.Single(i => i.Id == 4); item.LocationKind = LocationKind.ProjectStash; item.Owner = stash;
            information.Facts.Add(new InitialFactData { ActorId = 1, Key = new() { SubjectKind = SubjectKind.Item, SubjectId = 4, Attribute = AttributeKind.LocationKind }, Value = new() { Kind = FactValueKind.Number, Number = (long)LocationKind.ProjectStash } });
        }
        if (variant == RepairVariant.NoPrerequisites) initial.Items.RemoveAll(i => i.Resource != "tool");
        if (variant == RepairVariant.UnknownSpecification) information.Facts.RemoveAll(f => f.Key.Attribute == AttributeKind.RequiredResource);
        if (variant == RepairVariant.MissingKnown)
        {
            // Keep stale location evidence at x=2; place the physical part at a distinct remote tile.
            for (int x = 3; x <= 8; x++) initial.Tiles.Add(new TileData { Id = x + 1, Definition = "corridor", Position = new() { X = x } });
            initial.Items.Single(i => i.Id == 4).Owner = 9;
        }
        long travel = 1 + rng.Next(4); foreach (var tile in initial.Tiles) tile.TraversalOverride = travel;
        catalog.Recipes[0].Duration = 3 * travel + 2 + rng.Next(5); // Stale/known existing route initially cheaper.
        initial.Work!.Machines[0].InstallTicks = 2 + rng.Next(5);
        initial.Work.Machines[0].InspectTicks = 1 + rng.Next(3);
        int dx = rng.Next(61) - 30, dy = rng.Next(7) - 3, dz = rng.Next(61) - 30, axis = rng.Next(3), sign = rng.Next(2) == 0 ? -1 : 1;
        CoordinateData Transform(CoordinateData p)
        {
            int x = p.X * sign;
            return axis switch { 0 => new() { X = dx + x, Y = dy + p.Y, Z = dz + p.Z }, 1 => new() { X = dx + p.Z, Y = dy + x, Z = dz + p.Y }, _ => new() { X = dx + p.Y, Y = dy + p.Z, Z = dz + x } };
        }
        foreach (var tile in initial.Tiles) tile.Position = Transform(tile.Position);
        foreach (var actor in initial.Actors) actor.Position = Transform(actor.Position);
        foreach (var station in initial.Work.Workstations) station.Position = Transform(station.Position);
        foreach (var machine in initial.Work.Machines) machine.Position = Transform(machine.Position);
        foreach (var stash in initial.Work.Stashes) stash.Position = Transform(stash.Position);
        foreach (var fact in information.Facts.Where(f => f.Value.Coordinate != null)) fact.Value.Coordinate = Transform(fact.Value.Coordinate!);
        long actorId = 10 + rng.Next(100), machineId = 20 + rng.Next(100), stationId = 30 + rng.Next(100);
        initial.Actors[0].Id = actorId;
        foreach (var item in initial.Items.Where(i => i.LocationKind == LocationKind.ActorInventory)) item.Owner = actorId;
        foreach (var fact in information.Facts) { fact.ActorId = actorId; if (fact.Key.SubjectKind == SubjectKind.Machine) fact.Key.SubjectId = machineId; }
        information.RepairGoals[0].ActorId = actorId; information.RepairGoals[0].MachineId = machineId;
        initial.Work.Machines[0].Id = machineId; initial.Work.Workstations[0].Id = stationId;
        scenario.Assertions = new() { new() { Tick = initial.Horizon, Kind = "MachineOperational", MachineId = machineId, Operational = variant != RepairVariant.NoPrerequisites }, new() { Tick = initial.Horizon, Kind = "Accounting" } };
        return new LoadedScenario(JsonData.Write(scenario), JsonData.Write(catalog), "generated repair " + seed);
    }
}
