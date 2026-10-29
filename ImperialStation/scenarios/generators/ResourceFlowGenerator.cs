using Station.Contracts;
using Station.Core;
using Station.Runner;

namespace Station.Scenarios;

public static class ResourceFlowGenerator
{
    public static LoadedScenario Generate(string scenarioJson, string catalogJson, ulong seed)
    {
        var data = JsonData.Read<ScenarioData>(scenarioJson, "resource flow"); var catalog = JsonData.Read<CatalogData>(catalogJson, "resource catalog");
        var s = data.InitialState; var rng = Pcg32.Create(seed, "resource-flow-generator");
        s.Id = "resource_flow_variant_" + seed; s.Seed = seed;
        long actor = 100 + rng.Next(100), item = 1000 + rng.Next(100), stash = 200 + rng.Next(100), service = 300 + rng.Next(100);
        long initial = 1 + rng.Next(1000), supplied = 1 + rng.Next(100), split = 1 + rng.Next((int)(initial + supplied - 1)), consumed = 1 + rng.Next((int)(initial + supplied - 1));
        long itemTicks = 1 + rng.Next(5), travelTicks = 1 + rng.Next(5), supplyTicks = 1 + rng.Next(9), consumeTicks = 1 + rng.Next(9);
        int dx = rng.Next(101) - 50, dy = rng.Next(11) - 5, dz = rng.Next(101) - 50, axis = rng.Next(3), sign = rng.Next(2) == 0 ? -1 : 1;
        CoordinateData Transform(CoordinateData c) => axis switch {
            0 => new() { X = dx + sign * c.X, Y = dy + c.Y, Z = dz + c.Z },
            1 => new() { X = dx + c.Z, Y = dy + sign * c.X, Z = dz + c.Y },
            _ => new() { X = dx + c.Y, Y = dy + c.Z, Z = dz + sign * c.X } };
        s.Configuration.ItemActionTicks = itemTicks;
        foreach (var tile in s.Tiles) { tile.Position = Transform(tile.Position); tile.TraversalOverride = travelTicks; }
        s.Actors[0].Id = actor; s.Actors[0].Position = Transform(s.Actors[0].Position);
        s.Items[0].Id = item; s.Items[0].Owner = actor; s.Items[0].Quantity = initial;
        s.Work!.Stashes[0].Id = stash; s.Work.Stashes[0].ProjectId = 400 + rng.Next(100); s.Work.Stashes[0].Position = Transform(s.Work.Stashes[0].Position);
        s.Work.Workstations[0].Id = service; s.Work.Workstations[0].Position = Transform(s.Work.Workstations[0].Position);
        var source = catalog.Recipes.Single(r => r.Id == 2); source.Duration = supplyTicks; source.Outputs[0].Quantity = supplied;
        var sink = catalog.Recipes.Single(r => r.Id == 3); sink.Duration = consumeTicks; sink.Inputs[0].Quantity = consumed;
        data.ScriptedActors[0].ActorId = actor; long tick = 0;
        foreach (var command in data.ScriptedActors[0].Commands)
        {
            command.Tick = tick; command.Target = Transform(command.Target);
            if (command.ItemId > 0) command.ItemId = item + command.ItemId - 1;
            if (command.OtherItemId > 0) command.OtherItemId = item + command.OtherItemId - 1;
            if (command.Kind == ActionKind.StoreInStash) command.ServiceId = stash;
            else if (command.ServiceId > 0) command.ServiceId = service;
            if (command.Kind == ActionKind.SplitStack) command.Quantity = split;
            tick += command.Kind == ActionKind.ProduceRecipe ? (command.RecipeId == 2 ? supplyTicks : consumeTicks) : command.Kind == ActionKind.Move ? travelTicks : itemTicks;
        }
        s.Horizon = tick;
        data.Assertions = new() {
            new() { Tick = tick, Kind = "ItemState", ItemId = item, Quantity = initial + supplied - consumed, LocationKind = LocationKind.ActorInventory, Owner = actor },
            new() { Tick = tick, Kind = "ItemAbsent", ItemId = item + 1 }, new() { Tick = tick, Kind = "ItemAbsent", ItemId = item + 2 }, new() { Tick = tick, Kind = "Accounting" } };
        return new LoadedScenario(JsonData.Write(data), JsonData.Write(catalog), "generated resource flow " + seed);
    }
}
