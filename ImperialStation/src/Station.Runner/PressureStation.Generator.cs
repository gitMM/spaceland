using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public static partial class PressureStation
{
    public const long PressureEnd = 84000;
    public const int GuardsPerCohort = 3;
    // The population workload: four 64-citizen groups. Larger counts remain possible
    // through --count, but 256 is the population gate and test size (2026-10-30).
    public const int PopulationCount = 256;
    // Variants: "" (staggered guard shifts with a mixed wave schedule), "single-cohort-guards"
    // (the same schedule with the earlier four guards per group, all in cohort 0; a
    // comparison control only), "daytime" (staggered guards, the earlier all-daytime waves)
    // and "overnight" (staggered guards, every wave in the day cohort's night).
    public static readonly IReadOnlyList<string> Variants = new[] { "", "single-cohort-guards", "daytime", "overnight" };
    // Wave start offsets within each configured day. The mixed default keeps the first
    // wave during day duty, starts the second at the 24,000 shift boundary while the day
    // cohort sleeps, and puts the third in the evening after day duty has ended.
    public static IReadOnlyList<long> WaveOffsets(string variant) => variant switch
    { "daytime" => new long[] { 6000, 6000, 6000 }, "overnight" => new long[] { 24000, 24000, 24000 }, _ => new long[] { 6000, 24000, 15000 } };
    // Declared staffing redistribution: five existing cleaners per group (b+21..b+25,
    // never card-loss or misconduct actors) become guards, giving three guards per
    // cohort so each interval keeps a two-guard escort team plus one guard for patrol,
    // breaks or an ongoing custody commitment. Population and duty lengths are unchanged.
    public static IReadOnlyList<long> ConvertedGuards(long groupBase) => Enumerable.Range(21, 5).Select(i => groupBase + i).ToArray();
    public static LoadedScenario Create(int population = 32, ulong seed = 42, string variant = "")
    {
        if (population < 32 || population % 16 != 0) throw new ArgumentException("Pressure population must be a multiple of 16, at least 32.");
        if (!Variants.Contains(variant)) throw new ArgumentException("Unknown pressure variant: " + variant);
        bool staggered = variant != "single-cohort-guards";
        var waves = WaveOffsets(variant);
        if (staggered && population % 64 != 0 && population % 64 < 32) throw new ArgumentException("Staggered guard shifts need every 64-citizen group to have at least 32 citizens.");
        var baseline = StationBaseline.Create(population, days: 9, seed: seed);
        var scenario = JsonData.Read<ScenarioData>(baseline.ScenarioJson, "pressure base");
        var catalog = JsonData.Read<CatalogData>(baseline.CatalogJson, "pressure catalog");
        var s = scenario.InitialState; s.Id = "pressure-station-" + population + (variant.Length > 0 ? "-" + variant : "");
        s.ResponseChainRoles.Clear();
        CoordinateData At(int x) => new() { X = x };
        var roles = new List<string> { "cleaner", "worker", "supervisor", "guard" };
        int groups = (population + 63) / 64;
        var subjects = new List<long>();
        var supervisors = new List<long>();
        var guards = new List<long>();
        for (int b = 0; b < population; b += 64)
        {
            subjects.Add(b + 1); subjects.Add(b + 7); supervisors.Add(b + 2);
            guards.AddRange(Enumerable.Range(b + 3, 4).Select(i => (long)i));
            if (staggered) guards.AddRange(ConvertedGuards(b));
        }
        foreach (var a in s.Actors)
        {
            if (subjects.Contains(a.Id)) a.Role = "worker";
            if (supervisors.Contains(a.Id)) a.Role = "supervisor";
            if (guards.Contains(a.Id)) a.Role = "guard";
        }
        var nonCleaners = supervisors.Concat(guards).ToHashSet();
        s.JobSystem!.Jobs.RemoveAll(j => nonCleaners.Contains(j.Id));
        s.JobSystem.Assignments.RemoveAll(a => nonCleaners.Contains(a.ActorId));
        foreach (var r in s.Routines!.Assignments.Where(r => s.Actors.Single(a => a.Id == r.ActorId).Role != "cleaner")) r.Cohort = 0;
        // Staggered guard duty: within each group, guards rotate through the three
        // cohorts in ID order. Subjects and supervisors remain on the day cohort.
        if (staggered)
            foreach (var group in guards.GroupBy(g => (g - 1) / 64))
                foreach (var (guard, index) in group.OrderBy(g => g).Select((g, i) => (g, i)))
                    s.Routines.Assignments.Single(r => r.ActorId == guard).Cohort = index % s.Routines.Configuration.Cohorts;
        s.Cleanliness!.MeanSoilingTicks = 0;
        s.Cleanliness.Access.Roles.Add("worker"); s.Cleanliness.Access.RequiresCredential = true;
        foreach (var d in s.Cleanliness.Dirt) d.Units = 400;
        foreach (var j in s.JobSystem.Jobs) j.ReportedUnits = 400;
        // Finite nine-day provisioning with margin for six meals per citizen/day.
        // A food-shortage variant is separate from the declared disruption waves.
        foreach (var meal in s.Items.Where(i => i.Resource == "meal")) meal.Quantity = 16 * 60;
        // Assigned bedrooms open onto each district's maintenance corridor (x+1), where
        // spills land. At x+2 they sat one tile from the next district's services, so
        // nearest-service travel bypassed the corridor and it went unobserved for days.
        // Faults and dirt are still discovered only by local perception.
        foreach (var bed in s.Needs!.Services.Where(n => n.Kind == NeedServiceKind.Bed)) bed.Position.X += 1;
        foreach (var roster in s.Routines.Assignments) roster.RestTile!.X += 1;
        s.Facility = new() { ReportingRoles = new() { "cleaner", "supervisor", "guard" }, RepeatTicks = StationBaseline.Day, OrderTicks = s.Horizon };
        s.Authority!.PatrolRole = "guard"; s.Authority.GraceTicks = 2000;
        s.QualityAssurance = new() { SamplePermille = 1000, DelayTicks = 50, OrderTicks = s.Horizon };
        s.Personnel = new();
        s.Detention = new() { SentenceTicks = 15000, OrderTicks = s.Horizon,
            Escort = new() { FacilityId = 1, Destination = At(5), Rendezvous = At(0), RequiredEquipment = "guard_equipment", EquipTicks = 50 },
            Facilities = new() { new() { Id = 1, Position = At(5), Capacity = groups, IntakeTicks = 100, ReleaseTicks = 100 } } };
        catalog.Resources.AddRange(new[] { new ResourceData { Id = "correct", FitClass = "cartridge", Rating = 2 }, new ResourceData { Id = "wrong", FitClass = "cartridge", Rating = 1 }, new ResourceData { Id = "guard_equipment" }, new ResourceData { Id = "blank" } });
        catalog.Recipes.Add(new() { Id = 1, Capability = "CredentialIssuance", Duration = 50, Inputs = new() { new() { Resource = "blank", Quantity = 1 } }, Outputs = new() { new() { Resource = "card", Quantity = 1 } } });
        s.Configuration.ItemActionTicks = 1;
        var info = s.Information!;
        info.Configuration.LoginTicks = 1; info.Configuration.PostTicks = 1;
        info.Configuration.ReadBaseTicks = 5; info.Configuration.ReadPerMessageTicks = 0; info.Configuration.ReadMaximumTicks = 5; info.Configuration.QueryTicks = 10;
        info.Recovery = new() { IdentityCheckTicks = 50, Capabilities = s.Actors.Select(a => new RecoveryCapabilityData { ActorId = a.Id, AccountId = a.Id }).ToList() };
        long item = 1000000;
        foreach (var a in s.Actors.Where(a => a.Role is "worker" or "cleaner"))
            s.Items.Add(new() { Id = item++, Resource = "correct", Quantity = 4, LocationKind = LocationKind.ActorInventory, Owner = a.Id });
        // The same public terminals serve ordinary reports, recovery and records.
        foreach (var terminal in info.Terminals)
        {
            int d = (int)terminal.Id - 1, x = d * 3;
            terminal.PermittedRoles = new(roles);
            terminal.Capabilities |= TerminalCapability.Records | TerminalCapability.CredentialRecovery;
            // Bind each supervisor to the existing public desk in their home
            // district. Granting every desk implicitly assigned every audit to
            // terminal 1 (the audit policy's lowest-ID rule), creating a global
            // commute that grew with population despite scaled infrastructure.
            foreach (long supervisor in supervisors.Where(id => (id - 1) / 16 + 1 == terminal.Id))
                foreach (var kind in new[] { RecordKind.WorkAttempt, RecordKind.WorkReport })
                    info.RecordAccess.Add(new() { ActorId = supervisor, TerminalId = terminal.Id, RequiredRole = "supervisor", Kind = kind });
            s.Work!.Workstations.Add(new() { Id = terminal.Id, Position = At(x + 2), Capability = "CredentialIssuance", Access = new() { Roles = new(roles) } });
            info.Recovery.Desks.Add(new() { TerminalId = terminal.Id, WorkstationId = terminal.Id, RecipeId = 1 });
            s.Items.Add(new() { Id = item++, Resource = "blank", Quantity = 6, LocationKind = LocationKind.TileContainer, Owner = x + 3 });
            long machine = 1000 + terminal.Id;
            s.Work.Machines.Add(new() { Id = machine, Position = At(x + 2), FitClass = "cartridge", RequiredResource = "correct", RequiredRating = 2,
                InstallTicks = 1500, Access = new() { Roles = new() { "worker", "cleaner" }, RequiresCredential = true } });
            s.Items.Add(new() { Id = item++, Resource = "correct", Quantity = 1, LocationKind = LocationKind.MachineSlot, Owner = machine });
            for (int wave = 0; wave < 3; wave++)
            {
                long start = waves[wave] + wave * StationBaseline.Day;
                long actor = d * 16 + 2 + wave;
                scenario.Disturbances.Add(new() { Type = "CardLoss", Tick = start, ActorId = actor, ItemId = actor });
                scenario.Disturbances.Add(new() { Type = "MachineFault", Tick = start, WorkServiceId = machine });
                scenario.Disturbances.Add(new() { Type = "Spill", Tick = start + 200, Tile = At(x + 1), DirtUnits = 100 });
                scenario.Disturbances.Add(new() { Type = "TerminalChange", Tick = start + 500, TerminalId = terminal.Id, Enabled = true, Connected = false });
                scenario.Disturbances.Add(new() { Type = "TerminalChange", Tick = start + 6000, TerminalId = terminal.Id, Enabled = true, Connected = true });
            }
        }
        long machineId = 0;
        foreach (long subject in subjects)
        {
            int x = (int)((subject - 1) / 16) * 3 + 1;
            long terminal = 10000 + subject;
            // Isolate only the finite misconduct trigger from unrelated initial login traffic.
            // Subsequent audit, recovery and ordinary reports contend at public terminals.
            info.Terminals.Add(new() { Id = terminal, Position = At(x), Capacity = 1, PermittedRoles = new() { "worker" } });
            var actor = s.Actors.Single(a => a.Id == subject); actor.Position = At(x); actor.Scripted = true; actor.AutonomousNeeds = true;
            s.Needs!.Initial.RemoveAll(n => n.ActorId == subject);
            var script = new ScriptedActorData { ActorId = subject }; scenario.ScriptedActors.Add(script);
            for (int k = 0; k < 2; k++)
            {
                long m = ++machineId, wrong = item++, installed = item++;
                s.Work!.Machines.Add(new() { Id = m, Position = At(x), FitClass = "cartridge", RequiredResource = "correct", RequiredRating = 2, InstallTicks = 3, InspectTicks = 2, TestTicks = 2, Access = new() { Roles = new() { "worker" } } });
                s.Items.Add(new() { Id = wrong, Resource = "wrong", Quantity = 1, LocationKind = LocationKind.ActorInventory, Owner = subject });
                s.Items.Add(new() { Id = installed, Resource = "correct", Quantity = 1, LocationKind = LocationKind.MachineSlot, Owner = m });
                long job = 10000 + m;
                s.JobSystem.Jobs.Add(new() { Id = job, MachineId = m });
                s.JobSystem.Assignments.Add(new() { Key = "claimed-repair/" + m, JobId = job, ActorId = subject, Expires = s.Horizon + StationBaseline.Day });
                // Keep the finite trigger contiguous: a free tick between the
                // two claims lets a legitimate inbox read interrupt the next install.
                long attempt = s.JobSystem.Assignments.Count, start = (subject % 64 == 1 ? 0 : 20) + k * 6;
                script.Commands.Add(new() { Tick = start, Kind = ActionKind.Login, Target = At(x), ServiceId = terminal });
                script.Commands.Add(new() { Tick = start + 1, Kind = ActionKind.Install, Target = At(x), ServiceId = m, ItemId = wrong });
                script.Commands.Add(new() { Tick = start + 4, Kind = ActionKind.PostMessage, Target = At(x), ServiceId = terminal, Message = new() { Kind = MessageKind.OrderAccepted, RecipientKind = RecipientKind.Authority,
                    Subject = new() { SubjectKind = SubjectKind.Attempt, SubjectId = attempt, Attribute = AttributeKind.WorkStatus }, Claim = new() { Kind = FactValueKind.Text, Text = "Accepted" }, ObservedTick = start + 4, TaskId = job, AttemptId = attempt, RelatedMessageId = attempt, OperationKey = "accept/" + m } });
                script.Commands.Add(new() { Tick = start + 5, Kind = ActionKind.PostMessage, Target = At(x), ServiceId = terminal, Message = new() { Kind = MessageKind.WorkCompleted, RecipientKind = RecipientKind.Authority,
                    Subject = new() { SubjectKind = SubjectKind.Machine, SubjectId = m, Attribute = AttributeKind.Operational }, Claim = new() { Kind = FactValueKind.Boolean, Number = 1 }, ObservedTick = start + 5, TaskId = job, AttemptId = attempt, OperationKey = "false-completion/" + m } });
            }
        }
        foreach (long guard in guards)
        {
            long id = item++;
            // Guards start with issued physical kits, not permanently remembered
            // remote stock. Equipping still requires an ordinary finite action.
            s.Items.Add(new() { Id = id, Resource = "guard_equipment", Quantity = 1, LocationKind = LocationKind.ActorInventory, Owner = guard });
        }
        foreach (var kind in new[] { NeedServiceKind.Cafeteria, NeedServiceKind.Toilet, NeedServiceKind.RestSpot, NeedServiceKind.Bed })
            s.Needs!.Services.Add(new() { Id = 10000 + (int)kind, Kind = kind, Position = At(5), DetentionFacilityId = 1, Capacity = kind == NeedServiceKind.Bed ? 1 : groups,
                DurationTicks = kind == NeedServiceKind.Bed ? 12000 : kind == NeedServiceKind.Cafeteria ? 375 : kind == NeedServiceKind.RestSpot ? 250 : 75 });
        for (int bed = 1; bed < groups; bed++)
            s.Needs!.Services.Add(new() { Id = 10100 + bed, Kind = NeedServiceKind.Bed, Position = At(5), DetentionFacilityId = 1, Capacity = 1, DurationTicks = 12000 });
        s.Items.Add(new() { Id = item++, Resource = "meal", Quantity = groups * 60, LocationKind = LocationKind.TileContainer, Owner = 6 });
        return new LoadedScenario(JsonData.Write(scenario), JsonData.Write(catalog), "generated fixed-station pressure");
    }
}
