
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

// A workload generator, never a second simulation. All responses use World policies.
public static partial class StationBaseline
{
    public const long Day = 36000;
    public static LoadedScenario Create(int population, int days = 2, ulong seed = 42, bool shortage = false)
    {
        if (population < 6 || population > 4096 || days < 1 || days > 7) throw new ArgumentException("Baseline needs 6..4096 citizens and 1..7 days.");
        var catalog = new CatalogData { SchemaVersion = 1, Id = "integrated-station-v1",
            Tiles = Enum.GetValues<TileFamily>().Select(f => new DefinitionData { Id = f.ToString(), Family = f, TraversalTicks = 10, Capabilities = new() { "Cleanable" } }).ToList(),
            Resources = new() { new() { Id = "card" }, new() { Id = "meal" } } };
        var s = new InitialStateData { SchemaVersion = 1, Id = "integrated-station-" + population, Catalog = "catalog.json", Seed = seed, Horizon = days * Day,
            Information = new(), Work = new(), JobSystem = new(), Routines = new(), Needs = new(), Authority = new(),
            Cleanliness = new() { MeanSoilingTicks = 12000, CleanUnitTicks = 300, Access = new() { Roles = new() { "cleaner" } } } };
        s.Routines.Configuration = new() { DayTicks = Day, DutyTicks = Day / 3, CheckInWindow = 750, PeriodicCheckInTicks = 1500 };
        // A 1-hour configured day represents 24 hours: preserve need points/day and
        // scale finite life activities by 24. Work and terminal times are declared below.
        s.Needs.Rules = new NeedConfig().Rules.Select(r => new NeedRuleData { Kind = r.Kind, AwakeRate = r.AwakeRate * 24, WorkingRate = r.WorkingRate * 24,
            SleepingRate = r.SleepingRate * 24, RestingRate = r.RestingRate * 24, Seek = r.Seek, Urgent = r.Urgent, Critical = r.Critical }).ToList();
        s.Needs.Conversations.DurationTicks = 75;
        int districts = (population + 15) / 16;
        CoordinateData At(int x) => new() { X = x };
        for (int d = 0; d < districts; d++)
        {
            int x = d * 3, size = Math.Min(16, population - d * 16);
            for (int t = 0; t < 3; t++) s.Tiles.Add(new() { Id = x + t + 1, Definition = catalog.Tiles[(x + t) % catalog.Tiles.Count].Id, Position = At(x + t) });
            s.Information.Terminals.Add(new() { Id = d + 1, Position = At(x + 2), Capacity = 2, PermittedRoles = new() { "cleaner" } });
            s.Needs.Services.Add(new() { Id = d * 3 + 1, Kind = NeedServiceKind.Cafeteria, Position = At(x), Capacity = 8, DurationTicks = 375 });
            s.Needs.Services.Add(new() { Id = d * 3 + 2, Kind = NeedServiceKind.Toilet, Position = At(x), Capacity = 4, DurationTicks = 75 });
            s.Needs.Services.Add(new() { Id = d * 3 + 3, Kind = NeedServiceKind.RestSpot, Position = At(x), Capacity = 8, DurationTicks = 250 });
            s.Items.Add(new() { Id = population + d + 1, Resource = "meal", Quantity = shortage ? 1 : size * 20, LocationKind = LocationKind.TileContainer, Owner = x + 1 });
            s.Cleanliness.Dirt.Add(new() { TileId = x + 2, Units = 10000 });
        }
        for (int i = 1; i <= population; i++)
        {
            int x = ((i - 1) / 16) * 3;
            s.Actors.Add(new() { Id = i, Name = "Citizen " + i, Role = "cleaner", Position = At(x + 2) });
            s.Items.Add(new() { Id = i, Resource = "card", Quantity = 1, LocationKind = LocationKind.ActorInventory, Owner = i });
            s.Information.Credentials.Add(new() { Id = i, Holder = i, Account = i, CardItem = i });
            s.Routines.Assignments.Add(new() { ActorId = i, Cohort = (i - 1) % 3, RestTile = At(x) });
            s.Needs.Services.Add(new() { Id = districts * 3 + i, Kind = NeedServiceKind.Bed, Position = At(x), AssignedActorId = i, DurationTicks = 12000 });
            // Initial physiological state, not a scheduled response. Social pressure
            // ensures the first day exercises consent as well as threshold recurrence.
            s.Needs.Initial.Add(new() { ActorId = i, Kind = NeedKind.Social, Q = 400 * NeedAccumulator.HourMilliseconds });
            s.JobSystem.Jobs.Add(new() { Id = i, Kind = WorkKind.CleanTile, TileId = x + 2, ReportedUnits = 10000 });
            s.JobSystem.Assignments.Add(new() { Key = "initial/" + i, JobId = i, ActorId = i, Expires = s.Horizon + Day });
            if (days >= 2) s.ResponseChainRoles.Add(new() { Name = "citizen-" + i, ActorId = i, TriggerKind = ResponseTriggerKind.InitialCondition,
                RequiredEvents = new() { new() { Type = "ActionCompleted", ActorId = i, Detail = "ReadInbox" }, new() { Type = "ActionCompleted", ActorId = i, Detail = "Clean" } } });
        }
        return new LoadedScenario(JsonData.Write(new ScenarioData { InitialState = s }), JsonData.Write(catalog), "generated integrated baseline");
    }

}
