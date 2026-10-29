using Station.Contracts;
using Station.Core;
using Station.Runner;

namespace Station.Scenarios;

public enum RecoveryVariant { Replacement, KnownRetrieval, RediscoveredCard, NoStock, NoDesk, NoCapability, ValidCard }

public sealed record GeneratedRecovery(LoadedScenario Scenario, RecoveryVariant Variant, long ActorId, long CardId,
    long PartId, long Stock, long LossTick, long TravelTicks, int HeadquartersIndex, int BackgroundStaff);

public static class RecoveryGenerator
{
    public static GeneratedRecovery Generate(string canonicalJson, string catalogJson, ulong seed, RecoveryVariant variant)
    {
        var data = JsonData.Read<ScenarioData>(canonicalJson, "canonical recovery");
        var catalog = JsonData.Read<CatalogData>(catalogJson, "recovery catalog");
        var rng = Pcg32.Create(seed, "recovery-generator"); var initial = data.InitialState;
        var info = initial.Information!; var work = initial.Work!; var jobs = initial.JobSystem!;
        initial.Id = "recovery_variant_" + variant + "_" + seed; initial.Seed = seed; initial.Horizon = 800;
        data.Assertions.Clear(); data.Disturbances.Clear(); data.ScriptedActors.Clear();
        var chain = initial.ResponseChainRoles.Single(); initial.ResponseChainRoles.Clear();
        long actor = 10 + rng.Next(100), account = 1000 + rng.Next(100), itemOffset = 200 + rng.Next(100);
        long machine = 400 + rng.Next(100), workstation = 600 + rng.Next(100), terminal = 800 + rng.Next(100);
        long credential = 1200 + rng.Next(100), job = 1400 + rng.Next(100), tileOffset = 1600 + rng.Next(100), recipe = 1800 + rng.Next(100);
        long travel = 2 + rng.Next(5), stock = 1 + rng.Next(4);
        // Keep the HQ route away from the dropped card. A route through x=0 would
        // legitimately discover and retrieve it, invalidating a replacement/no-stock contract.
        int hq = rng.Next(2) == 0 ? 2 : 4 + rng.Next(6), background = rng.Next(4);
        if (variant == RecoveryVariant.RediscoveredCard) hq = -1;
        int dx = rng.Next(81) - 40, dy = rng.Next(11) - 5, dz = rng.Next(81) - 40, axis = rng.Next(3), sign = rng.Next(2) == 0 ? -1 : 1;
        CoordinateData At(int x) => axis switch
        {
            0 => new() { X = dx + sign * x, Y = dy, Z = dz },
            1 => new() { X = dx, Y = dy + sign * x, Z = dz },
            _ => new() { X = dx, Y = dy, Z = dz + sign * x }
        };
        int first = Math.Min(0, hq), last = Math.Max(3, hq);
        long TileId(int x) => tileOffset + x - first;
        initial.Tiles = Enumerable.Range(first, last - first + 1).Select(x => new TileData
        { Id = TileId(x), Definition = x == hq ? "command" : "corridor", Position = At(x), TraversalOverride = travel }).ToList();
        initial.Actors[0].Id = actor; initial.Actors[0].AccountId = account; initial.Actors[0].Position = At(0); initial.Actors[0].Scripted = false;
        for (int i = 0; i < background; i++) initial.Actors.Add(new ActorData
        { Id = actor + 100 + i, AccountId = account + 100 + i, Role = "clerk", Name = "Background staff " + i, Position = At(hq) });
        // Background staffing varies population/identity isolation, not a hidden printer dependency.
        foreach (var item in initial.Items)
        { item.Id += itemOffset; item.Owner = item.LocationKind == LocationKind.ActorInventory ? actor : TileId(hq); }
        initial.Items.Single(i => i.Resource == "blank").Quantity = stock;
        if (variant == RecoveryVariant.NoStock) initial.Items.RemoveAll(i => i.Resource == "blank");
        work.Machines[0].Id = machine; work.Machines[0].Position = At(3); work.Machines[0].InstallTicks = 2 + rng.Next(8);
        work.Machines[0].InspectTicks = 1 + rng.Next(4);
        work.Workstations[0].Id = workstation; work.Workstations[0].Position = At(hq); work.Workstations[0].Capacity = 1 + rng.Next(2);
        catalog.Recipes[0].Id = recipe; catalog.Recipes[0].Duration = 7 + rng.Next(14);
        info.Terminals[0].Id = terminal; info.Terminals[0].Position = At(0);
        info.Terminals[1].Id = terminal + 1; info.Terminals[1].Position = At(hq); info.Terminals[1].Capacity = 1 + rng.Next(2);
        info.Credentials[0].Id = credential; info.Credentials[0].Holder = actor; info.Credentials[0].Account = account; info.Credentials[0].CardItem += itemOffset;
        info.Facts[0].ActorId = actor; info.Facts[0].Key.SubjectId = machine;
        if (variant == RecoveryVariant.KnownRetrieval) info.Facts.Add(new InitialFactData
        { ActorId = actor, Key = new() { SubjectKind = SubjectKind.Item, SubjectId = 1 + itemOffset, Attribute = AttributeKind.Location }, Value = new() { Kind = FactValueKind.Coordinate, Coordinate = At(0) } });
        info.Configuration.LoginTicks = 1 + rng.Next(3); info.Configuration.ReadBaseTicks = info.Configuration.ReadMaximumTicks = 1 + rng.Next(3);
        info.Configuration.PostTicks = 1 + rng.Next(3); info.Configuration.LatencyTicks = 1 + rng.Next(4); info.Configuration.SessionTicks = 2000;
        info.Recovery!.IdentityCheckTicks = 4 + rng.Next(7);
        info.Recovery.Capabilities[0].ActorId = actor; info.Recovery.Capabilities[0].AccountId = account;
        info.Recovery.Capabilities[0].Retained = variant != RecoveryVariant.NoCapability;
        info.Recovery.Desks[0].TerminalId = terminal + 1; info.Recovery.Desks[0].WorkstationId = workstation; info.Recovery.Desks[0].RecipeId = recipe;
        if (variant == RecoveryVariant.NoDesk) info.Recovery.Desks.Clear();
        jobs.Jobs[0].Id = job; jobs.Jobs[0].MachineId = machine;
        jobs.Assignments[0].JobId = job; jobs.Assignments[0].ActorId = actor; jobs.Assignments[0].Expires = 5000;
        jobs.CheckIns[0].ActorId = actor; jobs.CheckIns[0].FirstTick = info.Configuration.LatencyTicks; jobs.CheckIns[0].Interval = 2000;
        initial.Configuration.ItemActionTicks = 1 + rng.Next(3);
        string content = JsonData.Write(catalog);

        // Derive only the trigger from an undisturbed autonomous prefix. No response
        // action or outcome is copied into the fixture. Validate again after loss.
        var undisturbed = new LoadedScenario(JsonData.Write(data), content, "recovery preflight").CreateWorld(trace: true);
        undisturbed.AdvanceTo(200);
        var departure = undisturbed.Trace.First(e => e.Type == "ActionRequested" && e.ActorId == actor && e.Detail == "Move");
        long loss = departure.Tick + 1;
        chain.ActorId = actor;
        chain.TriggerKind = variant == RecoveryVariant.ValidCard ? ResponseTriggerKind.InitialCondition : ResponseTriggerKind.Event;
        chain.Trigger = variant == RecoveryVariant.ValidCard ? null : new() { Type = "CardLost", Tick = loss, ActorId = actor, ItemId = 1 + itemOffset };
        foreach (var required in chain.RequiredEvents.Where(e => e.ActorId.HasValue)) required.ActorId = actor;
        if (variant != RecoveryVariant.Replacement)
        {
            chain.RequiredEvents.Clear();
            if (variant != RecoveryVariant.ValidCard) chain.RequiredEvents.Add(new() { Type = "CredentialRecoveryNeeded", ActorId = actor });
            if (variant is RecoveryVariant.KnownRetrieval or RecoveryVariant.RediscoveredCard)
            {
                chain.RequiredEvents.Add(new() { Type = "ActionCompleted", ActorId = actor, Detail = "PickUp" });
                chain.RequiredEvents.Add(new() { Type = "CredentialRecoveryCompleted", ActorId = actor });
            }
            chain.RequiredEvents.Add(new() { Type = variant is RecoveryVariant.KnownRetrieval or RecoveryVariant.RediscoveredCard or RecoveryVariant.ValidCard ? "ActionStarted" : "CredentialRecoveryWaiting", ActorId = actor,
                Detail = variant is RecoveryVariant.KnownRetrieval or RecoveryVariant.RediscoveredCard or RecoveryVariant.ValidCard ? "Install" : null });
        }
        initial.ResponseChainRoles.Add(chain);
        if (variant != RecoveryVariant.ValidCard) data.Disturbances.Add(new DisturbanceData { Type = "CardLoss", Tick = loss, ActorId = actor, ItemId = 1 + itemOffset });
        bool success = variant is RecoveryVariant.Replacement or RecoveryVariant.KnownRetrieval or RecoveryVariant.RediscoveredCard or RecoveryVariant.ValidCard;
        data.Assertions.Add(new() { Tick = loss, Kind = "ActorInTransit", ActorId = actor });
        data.Assertions.Add(new() { Tick = loss, Kind = "LocalOrder", ActorId = actor, AttemptId = 1, State = "Working" });
        data.Assertions.Add(new() { Tick = loss, Kind = "CredentialRecoveryCases", State = "Authorized", Quantity = 0 });
        data.Assertions.Add(new() { Tick = loss, Kind = "ItemHeld", ActorId = actor, ItemId = 3 + itemOffset });
        if (variant is not RecoveryVariant.KnownRetrieval and not RecoveryVariant.ValidCard)
            data.Assertions.Add(new() { Tick = loss, Kind = "KnowledgeAbsent", ActorId = actor, Fact = new() { SubjectKind = SubjectKind.Item, SubjectId = 1 + itemOffset, Attribute = AttributeKind.Location } });
        data.Assertions.Add(new() { Tick = 800, Kind = "MachineOperational", MachineId = machine, Operational = success });
        data.Assertions.Add(new() { Tick = 800, Kind = "RecoveryAgent", ActorId = actor, State = success ? "Completed" : "Pending", Quantity = success && variant != RecoveryVariant.ValidCard ? 1 : 0 });
        data.Assertions.Add(new() { Tick = 800, Kind = "Accounting" });
        if (success) data.Assertions.Add(new() { Tick = 800, Kind = "JobAuthority", JobId = job, State = "WorkCompleted" });
        var loaded = new LoadedScenario(JsonData.Write(data), content, "generated recovery " + seed);
        var prefix = loaded.CreateWorld(trace: true); prefix.AdvanceTo(loss);
        if (!prefix.InspectActor(new(actor)).InTransit || prefix.InspectLocalOrders(new(actor)).Single().Status != LocalOrderStatus.Working ||
            prefix.InspectCredentialRecoveryCases().Count != 0 || prefix.InspectRecoveryAgents().Single(a => a.ActorId == actor).Pending)
            throw new InvalidOperationException("Generated recovery does not preserve the accepted-work/transit/undiscovered-loss precondition.");
        if (variant != RecoveryVariant.ValidCard && !prefix.InspectItems().Single(i => i.Id.Value == 1 + itemOffset).Location.Equals(new ItemLocation(LocationKind.TileContainer, TileId(0))))
            throw new InvalidOperationException("Generated loss must leave the old card on the committed departure tile.");
        return new(loaded, variant, actor, 1 + itemOffset, 3 + itemOffset, variant == RecoveryVariant.NoStock ? 0 : stock, loss, travel, hq, background);
    }
}
