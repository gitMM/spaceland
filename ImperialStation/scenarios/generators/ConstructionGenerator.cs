using Station.Contracts;
using Station.Core;
using Station.Runner;

namespace Station.Scenarios;

public enum ConstructionVariant { Expansion, DelayedReport, NeedInterruption, NoStock, NoQualifiedWorker, NoLegalSite, VacantCapacity }
public sealed record GeneratedConstruction(LoadedScenario Scenario, ConstructionVariant Variant, long WorkerId, string MaterialResource,
    Coord Site, Coord Depot, Coord Terminal, int Builders, long WorkTicks, long TravelTicks, long Latency, int Axis, int Sign);

public static class ConstructionGenerator
{
    public static GeneratedConstruction Generate(string canonicalJson, string catalogJson, ulong seed, ConstructionVariant variant)
    {
        var data = JsonData.Read<ScenarioData>(canonicalJson, "canonical construction"); var catalog = JsonData.Read<CatalogData>(catalogJson, "construction catalog");
        var initial = data.InitialState; var rng = Pcg32.Create(seed, "construction-generator");
        if (!Enum.IsDefined(variant) || initial.Growth == null || initial.Information == null || initial.Routines == null || initial.Needs == null || initial.Work == null ||
            catalog.Recipes.Count != 1 || catalog.Tiles.Count(t => t.Construction != null) != 1 || !catalog.Resources.Any(r => r.Id == "material") || !catalog.Resources.Any(r => r.Id == "card"))
            throw new ArgumentException("Construction generation requires the canonical construction template, material/card catalog and a supported variant.");
        initial.Id = "construction_variant_" + variant + "_" + seed; initial.Seed = seed; initial.Horizon = 2000;
        data.Assertions.Clear(); data.Disturbances.Clear(); data.ScriptedActors.Clear(); initial.ResponseChainRoles.Clear();
        int axis = rng.Next(3), sign = rng.Next(2) == 0 ? -1 : 1, length = 2 + rng.Next(6), depot = rng.Next(3), terminal = rng.Next(length + 1);
        int dx = rng.Next(101) - 50, dy = rng.Next(31) - 15, dz = rng.Next(101) - 50;
        CoordinateData At(int x) => axis switch { 0 => new() { X = dx + sign * x, Y = dy, Z = dz }, 1 => new() { X = dx, Y = dy + sign * x, Z = dz }, _ => new() { X = dx, Y = dy, Z = dz + sign * x } };
        long actorOffset = 100 + rng.Next(100), accountOffset = 1000 + rng.Next(100), itemOffset = 2000 + rng.Next(100), tileOffset = 4000 + rng.Next(100);
        long terminalId = 6000 + rng.Next(100), recipeId = 8000 + rng.Next(100), credentialOffset = 10000 + rng.Next(100), serviceOffset = 12000 + rng.Next(100);
        int builders = 1 + rng.Next(3), population = variant == ConstructionVariant.VacantCapacity ? 20 : 12, berths = variant == ConstructionVariant.VacantCapacity ? 24 : 8;
        var actorIds = Enumerable.Range(0, population).Select(i => actorOffset + 7 * i).ToArray();
        for (int i = actorIds.Length - 1; i > 0; i--) { int other = rng.Next(i + 1); (actorIds[i], actorIds[other]) = (actorIds[other], actorIds[i]); }
        long worker = actorIds.Take(builders).Min(); string role = "builder_role_" + rng.Next(100), material = "material_" + rng.Next(100);
        long travel = 1 + rng.Next(5), work = variant == ConstructionVariant.NeedInterruption ? 150 + rng.Next(20) : 20 + rng.Next(81);
        long latency = variant == ConstructionVariant.DelayedReport ? 100 + rng.Next(101) : 1 + rng.Next(8);
        initial.Configuration.ItemActionTicks = 1 + rng.Next(4); initial.Configuration.AuthorityNominalTicks = travel;
        long TileId(int index) => tileOffset + 13 * index;
        initial.Tiles = Enumerable.Range(0, length + 1).Select(i => new TileData { Id = TileId(i), Definition = i == 0 ? "habitat" : "corridor", Position = At(i), TraversalOverride = travel }).ToList();
        initial.Actors = Enumerable.Range(0, population).Select(i => new ActorData { Id = actorIds[i], AccountId = accountOffset + i * 11,
            Name = "Construction resident " + i, Role = i < builders && variant != ConstructionVariant.NoQualifiedWorker ? role : "citizen", Position = At(i < builders ? depot : 0) }).ToList();
        initial.Items.Clear();
        if (variant != ConstructionVariant.NoStock)
        {
            bool split = rng.Next(2) == 0;
            initial.Items.Add(new ItemData { Id = itemOffset, Resource = material, Quantity = split ? 2 : 4, LocationKind = LocationKind.TileContainer, Owner = TileId(depot) });
            if (split) initial.Items.Add(new ItemData { Id = itemOffset + 1, Resource = material, Quantity = 2, LocationKind = LocationKind.TileContainer, Owner = TileId(depot) });
        }
        var info = initial.Information!; info.Credentials.Clear(); info.Facts.Clear(); info.Messages.Clear(); info.ReportGoals.Clear(); info.RepairGoals.Clear();
        info.Terminals = new() { new TerminalData { Id = terminalId, Position = At(terminal), PermittedRoles = new() { role }, Capabilities = TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.Records, Capacity = 1 + rng.Next(2) } };
        info.Configuration.LoginTicks = 1 + rng.Next(4); info.Configuration.ReadBaseTicks = 1 + rng.Next(3); info.Configuration.ReadPerMessageTicks = 1;
        info.Configuration.ReadMaximumTicks = 10; info.Configuration.PostTicks = 1 + rng.Next(4); info.Configuration.LatencyTicks = latency; info.Configuration.SessionTicks = 500;
        var routine = initial.Routines!; routine.Assignments.Clear(); routine.Configuration.CheckInWindow = 1; routine.Configuration.PeriodicCheckInTicks = 30 + rng.Next(31);
        for (int i = 0; i < builders; i++)
        {
            long card = itemOffset + 10 + i;
            initial.Items.Add(new ItemData { Id = card, Resource = "card", Quantity = 1, LocationKind = LocationKind.ActorInventory, Owner = actorIds[i] });
            info.Credentials.Add(new CredentialData { Id = credentialOffset + i, Holder = actorIds[i], Account = initial.Actors[i].AccountId, CardItem = card });
            routine.Assignments.Add(new RosterAssignmentData { ActorId = actorIds[i], Cohort = 0, RestTile = At(depot) });
        }
        catalog.Resources.Single(r => r.Id == "material").Id = material;
        var recipe = catalog.Recipes.Single(); recipe.Id = recipeId; recipe.Duration = work; recipe.Inputs.Single().Resource = material;
        catalog.Tiles.Single(t => t.Construction != null).Construction!.RecipeId = recipeId;
        catalog.Tiles.Single(t => t.Construction != null).Construction!.Access.Roles = new() { role };
        initial.Needs!.Services = Enumerable.Range(0, berths).Select(i => new NeedServiceData { Id = serviceOffset + i, Kind = NeedServiceKind.Bed,
            Position = At(0), AssignedActorId = i < Math.Min(berths, population) ? actorIds[i] : 0 }).ToList();
        initial.Needs.Initial.Clear(); initial.Needs.Rules = null;
        if (variant == ConstructionVariant.NeedInterruption)
        {
            // A declared stress variant: the hunger rate rises only while actually working.
            // Starting below urgent, the first build reaches critical; one meal leaves enough
            // headroom for a full restarted recipe. No command creates the response.
            initial.Needs.Rules = new NeedConfig().Rules.Select(r => new NeedRuleData { Kind = r.Kind, AwakeRate = r.AwakeRate, WorkingRate = r.Kind == NeedKind.Hunger ? 100000 : r.WorkingRate,
                SleepingRate = r.SleepingRate, RestingRate = r.RestingRate, Seek = r.Seek, Urgent = r.Urgent, Critical = r.Critical }).ToList();
            initial.Needs.Initial.Add(new NeedInitialData { ActorId = worker, Kind = NeedKind.Hunger, Q = 600 * NeedAccumulator.HourMilliseconds });
            catalog.Resources.Add(new ResourceData { Id = "meal" });
            initial.Items.Add(new ItemData { Id = itemOffset + 100, Resource = "meal", Quantity = 3, LocationKind = LocationKind.TileContainer, Owner = TileId(depot) });
            initial.Needs.Services.Add(new NeedServiceData { Id = serviceOffset + 100, Kind = NeedServiceKind.Cafeteria, Position = At(depot), DurationTicks = 5 + rng.Next(6) });
        }
        var growth = initial.Growth!; growth.BelievedCompletedBerths = berths; growth.ReservePermille = variant == ConstructionVariant.VacantCapacity ? 50 : 0;
        growth.KnownMaterials = new() { new MaterialData { Resource = material, Quantity = 4 } };
        var end = At(variant == ConstructionVariant.NoLegalSite ? length : length + 1); var start = At(0);
        growth.Minimum = new() { X = Math.Min(start.X, end.X), Y = Math.Min(start.Y, end.Y), Z = Math.Min(start.Z, end.Z) };
        growth.Maximum = new() { X = Math.Max(start.X, end.X), Y = Math.Max(start.Y, end.Y), Z = Math.Max(start.Z, end.Z) };
        bool positive = variant is ConstructionVariant.Expansion or ConstructionVariant.DelayedReport or ConstructionVariant.NeedInterruption;
        bool commits = positive || variant == ConstructionVariant.NoStock;
        void Assertion(string kind, long quantity) => data.Assertions.Add(new AssertionData { Kind = kind, Tick = initial.Horizon, Quantity = quantity });
        Assertion("HousingBerths", positive ? 16 : berths); Assertion("GrowthBelievedBerths", positive ? 16 : berths);
        Assertion("GrowthPendingBerths", variant == ConstructionVariant.NoStock ? 8 : 0); Assertion("GrowthCommitments", commits ? 1 : 0);
        Assertion("GrowthTarget", variant == ConstructionVariant.VacantCapacity ? 21 : 12);
        data.Assertions.Add(new AssertionData { Kind = "Accounting", Tick = initial.Horizon });
        if (commits) data.Assertions.Add(new AssertionData { Kind = "Construction", Tick = initial.Horizon, EntityId = 1, State = positive ? "Completed" : "Reserved" });
        data.Assertions.Add(new AssertionData { Kind = "TileCompleted", Tick = initial.Horizon, Position = At(length + 1), State = positive ? "Completed" : "Absent" });
        var events = positive ? new List<ResponseEventData> { new() { Type = "AuthorityOrderPosted", ActorId = 0 }, new() { Type = "MessageRead", ActorId = worker }, new() { Type = "OrderAcknowledged", ActorId = worker },
            new() { Type = "ActionCompleted", ActorId = worker, Detail = "PickUp" }, new() { Type = "ConstructionCompleted", ActorId = worker }, new() { Type = "ConstructionCapacityAccepted", ActorId = 0 } } :
            commits ? new() { new() { Type = "AuthorityOrderPosted", ActorId = 0 }, new() { Type = "WorkOrderEvaluated", ActorId = worker, Detail = "attempt=1; Rejected" }, new() { Type = "WorkReportReceived", ActorId = 0 } } :
            new List<ResponseEventData> { new() { Type = variant == ConstructionVariant.VacantCapacity ? "GrowthCapacityCovered" : "GrowthPlanEvaluated", ActorId = 0 } };
        initial.ResponseChainRoles.Add(new ResponseChainRoleData { Name = "generated-habitat-builder", ActorId = worker, TriggerKind = commits ? ResponseTriggerKind.Event : ResponseTriggerKind.InitialCondition,
            Trigger = commits ? new ResponseEventData { Type = "GrowthProjectCommitted", ActorId = 0, Tick = 0 } : null, RequiredEvents = events });
        // These are input checks, not outcome filtering. A broken positive case must fail tests.
        if (recipe.Inputs.Single().Quantity != 4 || initial.Items.Where(i => i.Resource == material).Sum(i => i.Quantity) != (variant == ConstructionVariant.NoStock ? 0 : 4) ||
            info.Configuration.LatencyTicks <= 0 || initial.Actors.Any(a => a.Scripted) || data.ScriptedActors.Count > 0 || initial.Needs.Rules != null && variant != ConstructionVariant.NeedInterruption)
            throw new InvalidOperationException("Generated construction prerequisites were not preserved.");
        return new GeneratedConstruction(new LoadedScenario(JsonData.Write(data), JsonData.Write(catalog), "generated-construction"), variant, worker, material,
            At(length + 1).ToCoord(), At(depot).ToCoord(), At(terminal).ToCoord(), builders, work, travel, latency, axis, sign);
    }
}
