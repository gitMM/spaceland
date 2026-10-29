using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public static partial class RecoveryStation
{
    public const long BurstStart = StationBaseline.Day + 2000;
    public const long BurstEnd = StationBaseline.Day + 12000;
    public static LoadedScenario Create(int population = 32, ulong seed = 42)
    {
        var baseline = StationBaseline.Create(population, days: 5, seed: seed);
        var scenario = JsonData.Read<ScenarioData>(baseline.ScenarioJson, "recovery base");
        var catalog = JsonData.Read<CatalogData>(baseline.CatalogJson, "recovery catalog");
        var s = scenario.InitialState; s.Id = "recovery-station-" + population;
        s.ResponseChainRoles.Clear();
        s.Cleanliness!.MeanSoilingTicks = 0; s.Cleanliness.Access.RequiresCredential = true;
        foreach (var dirt in s.Cleanliness.Dirt) dirt.Units = 400;
        foreach (var job in s.JobSystem!.Jobs) job.ReportedUnits = 400;
        s.Facility = new() { ReportingRoles = new() { "cleaner" }, RepeatTicks = StationBaseline.Day, OrderTicks = s.Horizon };
        s.Authority!.PatrolRole = "cleaner";
        catalog.Resources.Add(new() { Id = "blank" });
        catalog.Resources.Add(new() { Id = "part", FitClass = "cartridge" });
        catalog.Recipes.Add(new() { Id = 1, Capability = "CredentialIssuance", Duration = 50,
            Inputs = new() { new() { Resource = "blank", Quantity = 1 } }, Outputs = new() { new() { Resource = "card", Quantity = 1 } } });
        s.Information!.Recovery = new() { IdentityCheckTicks = 50,
            Capabilities = s.Actors.Select(a => new RecoveryCapabilityData { ActorId = a.Id, AccountId = a.Id }).ToList() };
        long item = s.Items.Max(i => i.Id) + 1;
        foreach (var actor in s.Actors)
            s.Items.Add(new() { Id = item++, Resource = "part", Quantity = 1, LocationKind = LocationKind.ActorInventory, Owner = actor.Id });
        foreach (var terminal in s.Information.Terminals)
        {
            long district = terminal.Id - 1, x = district * 3;
            terminal.Capabilities |= TerminalCapability.CredentialRecovery;
            s.Work!.Workstations.Add(new() { Id = terminal.Id, Position = new() { X = (int)x + 2 }, Capability = "CredentialIssuance", Access = new() { Roles = new() { "cleaner" } } });
            s.Information.Recovery.Desks.Add(new() { TerminalId = terminal.Id, WorkstationId = terminal.Id, RecipeId = 1 });
            s.Items.Add(new() { Id = item++, Resource = "blank", Quantity = 2, LocationKind = LocationKind.TileContainer, Owner = x + 3 });
            s.Work.Machines.Add(new() { Id = terminal.Id, Position = new() { X = (int)x + 2 }, FitClass = "cartridge", RequiredResource = "part",
                InstallTicks = 1500, Access = new() { Roles = new() { "cleaner" }, RequiresCredential = true } });
            s.Items.Add(new() { Id = item++, Resource = "part", Quantity = 1, LocationKind = LocationKind.MachineSlot, Owner = terminal.Id });
            foreach (long actor in new[] { district * 16 + 1, district * 16 + 4 }.Where(id => id <= population))
                scenario.Disturbances.Add(new() { Type = "CardLoss", Tick = BurstStart, ActorId = actor, ItemId = actor });
            scenario.Disturbances.Add(new() { Type = "MachineFault", Tick = BurstStart, WorkServiceId = terminal.Id });
            scenario.Disturbances.Add(new() { Type = "Spill", Tick = BurstStart + 200, Tile = new() { X = (int)x + 1 }, DirtUnits = 100 });
            scenario.Disturbances.Add(new() { Type = "TerminalChange", Tick = BurstStart + 1500, TerminalId = terminal.Id, Enabled = true, Connected = false });
            scenario.Disturbances.Add(new() { Type = "TerminalChange", Tick = BurstEnd, TerminalId = terminal.Id, Enabled = true, Connected = true });
        }
        return new LoadedScenario(JsonData.Write(scenario), JsonData.Write(catalog), "generated finite station recovery");
    }
}
