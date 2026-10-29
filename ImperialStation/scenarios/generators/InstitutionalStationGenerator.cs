using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public static partial class InstitutionalStation
{
    public static LoadedScenario Create(int population = 32, ulong seed = 42, string variant = "normal")
    {
        if (population < 32) throw new ArgumentException("Institutional station requires at least 32 citizens.");
        if (variant is not ("normal" or "correct" or "single-case" or "guards-unavailable" or "terminal-outage" or "intake-outage" or "full-capacity" or "interrupted-escort")) throw new ArgumentException("Unknown institutional variant.");
        var baseline = StationBaseline.Create(population, days: variant == "interrupted-escort" ? 3 : 2, seed: seed);
        var scenario = JsonData.Read<ScenarioData>(baseline.ScenarioJson, "baseline");
        var catalog = JsonData.Read<CatalogData>(baseline.CatalogJson, "catalog");
        var s = scenario.InitialState; s.Id = "institutional-station-" + population + "-" + variant;
        s.ResponseChainRoles.Clear();
        CoordinateData At(int x) => new() { X = x };
        catalog.Resources.AddRange(new[] { new ResourceData { Id = "correct", FitClass = "cartridge", Rating = 2 }, new ResourceData { Id = "wrong", FitClass = "cartridge", Rating = 1 }, new ResourceData { Id = "guard_equipment" } });
        s.Actors[0].Role = "worker"; s.Actors[1].Role = "supervisor";
        for (int i = 2; i < 6; i++) s.Actors[i].Role = "guard";
        foreach (var terminal in s.Information!.Terminals) terminal.PermittedRoles = new() { "cleaner", "worker", "supervisor", "guard" };
        s.Cleanliness!.Access.Roles.Add("worker");
        s.JobSystem!.Jobs.RemoveAll(j => j.Id >= 2 && j.Id <= 6);
        s.JobSystem.Assignments.RemoveAll(a => a.ActorId >= 2 && a.ActorId <= 6);
        foreach (var roster in s.Routines!.Assignments.Where(a => a.ActorId <= 6)) roster.Cohort = variant == "guards-unavailable" && roster.ActorId >= 3 ? 1 : 0;
        // Dedicated, physically visited records desk. Ordinary terminals cannot disclose audit records.
        s.Information.Terminals.Add(new() { Id = 10001, Position = At(0), Capacity = 1, PermittedRoles = new() { "supervisor" }, Capabilities = TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.Records });
        foreach (var kind in new[] { RecordKind.WorkAttempt, RecordKind.WorkReport }) s.Information.RecordAccess.Add(new() { ActorId = 2, TerminalId = 10001, RequiredRole = "supervisor", Kind = kind });
        s.QualityAssurance = new() { SamplePermille = 1000, DelayTicks = 50, OrderTicks = s.Horizon };
        s.Personnel = new();
        s.Detention = new() { SentenceTicks = 15000, OrderTicks = s.Horizon, Escort = new() { FacilityId = 1, Destination = At(5), Rendezvous = At(0), RequiredEquipment = "guard_equipment", EquipTicks = 50 },
            Facilities = new() { new() { Id = 1, Position = At(5), Capacity = 1, IntakeTicks = 100, ReleaseTicks = 100 } } };
        s.Authority!.GraceTicks = 2000;
        s.Configuration.ItemActionTicks = 1;
        s.Information.Configuration.LoginTicks = 1; s.Information.Configuration.PostTicks = 1;
        s.Information.Configuration.ReadBaseTicks = 5; s.Information.Configuration.ReadPerMessageTicks = 0; s.Information.Configuration.ReadMaximumTicks = 5;
        s.Information.Configuration.QueryTicks = 10;
        s.Actors[0].Position = At(1); s.Actors[0].Scripted = true; s.Actors[0].AutonomousNeeds = true;
        s.Needs!.Initial.RemoveAll(n => n.ActorId == 1);
        s.Information.Terminals.Add(new() { Id = 10000, Position = At(1), Capacity = 1, PermittedRoles = new() { "worker" } });
        var trigger = new ScriptedActorData { ActorId = 1 }; scenario.ScriptedActors.Add(trigger);
        // Only the misconduct is scripted. Shared actions install the part and post
        // authenticated claims; needs, all responses and subsequent work use World policies.
        for (int m = 1; m <= (variant == "single-case" ? 1 : variant == "full-capacity" ? 4 : 2); m++)
        {
            long subject = m <= 2 ? 1 : 7, terminal = m <= 2 ? 10000 : 10002;
            if (m == 3)
            {
                s.Actors[6].Role = "worker"; s.Actors[6].Position = At(1); s.Actors[6].Scripted = true; s.Actors[6].AutonomousNeeds = true;
                s.Routines.Assignments.Single(r => r.ActorId == 7).Cohort = 0;
                s.Needs.Initial.RemoveAll(n => n.ActorId == 7);
                s.Information.Terminals.Add(new() { Id = terminal, Position = At(1), Capacity = 1, PermittedRoles = new() { "worker" } });
                trigger = new() { ActorId = 7 }; scenario.ScriptedActors.Add(trigger);
            }
            s.Work!.Machines.Add(new() { Id = m, Position = At(1), FitClass = "cartridge", RequiredResource = "correct", RequiredRating = 2, InstallTicks = 3, InspectTicks = 2, TestTicks = 2, Access = new() { Roles = new() { "worker" } } });
            s.Items.Add(new() { Id = 100000 + m, Resource = variant == "correct" ? "correct" : "wrong", Quantity = 1, LocationKind = LocationKind.ActorInventory, Owner = subject });
            s.Items.Add(new() { Id = 100010 + m, Resource = "correct", Quantity = 1, LocationKind = LocationKind.MachineSlot, Owner = m });
            s.JobSystem.Jobs.Add(new() { Id = 10000 + m, MachineId = m });
            s.JobSystem.Assignments.Add(new() { Key = "initial-claimed-repair/" + m, JobId = 10000 + m, ActorId = subject, Expires = s.Horizon + StationBaseline.Day });
            long attempt = s.JobSystem.Assignments.Count;
            long start = (m <= 2 ? 0 : 20) + ((m - 1) % 2) * 7;
            trigger.Commands.Add(new() { Tick = start, Kind = ActionKind.Login, Target = At(1), ServiceId = terminal });
            trigger.Commands.Add(new() { Tick = start + 1, Kind = ActionKind.Install, Target = At(1), ServiceId = m, ItemId = 100000 + m });
            trigger.Commands.Add(new() { Tick = start + 4, Kind = ActionKind.PostMessage, Target = At(1), ServiceId = terminal, Message = new() { Kind = MessageKind.OrderAccepted, RecipientKind = RecipientKind.Authority,
                Subject = new() { SubjectKind = SubjectKind.Attempt, SubjectId = attempt, Attribute = AttributeKind.WorkStatus }, Claim = new() { Kind = FactValueKind.Text, Text = "Accepted" },
                ObservedTick = start + 4, TaskId = 10000 + m, AttemptId = attempt, RelatedMessageId = attempt, OperationKey = "accept-repair/" + m } });
            trigger.Commands.Add(new() { Tick = start + 5, Kind = ActionKind.PostMessage, Target = At(1), ServiceId = terminal, Message = new() { Kind = MessageKind.WorkCompleted, RecipientKind = RecipientKind.Authority,
                Subject = new() { SubjectKind = SubjectKind.Machine, SubjectId = m, Attribute = AttributeKind.Operational }, Claim = new() { Kind = FactValueKind.Boolean, Number = 1 },
                ObservedTick = start + 5, TaskId = 10000 + m, AttemptId = attempt, OperationKey = "false-completion/" + m } });
        }
        for (int actor = 3; actor <= 6; actor++)
        {
            long item = 100100 + actor;
            s.Items.Add(new() { Id = item, Resource = "guard_equipment", Quantity = 1, LocationKind = LocationKind.TileContainer, Owner = 5 });
            foreach (var pair in new[] { (AttributeKind.Resource, new FactValueData { Kind = FactValueKind.Text, Text = "guard_equipment" }), (AttributeKind.Quantity, new FactValueData { Kind = FactValueKind.Number, Number = 1 }),
                (AttributeKind.LocationKind, new FactValueData { Kind = FactValueKind.Number, Number = (long)LocationKind.TileContainer }), (AttributeKind.Location, new FactValueData { Kind = FactValueKind.Coordinate, Coordinate = At(4) }) })
                s.Information.Facts.Add(new() { ActorId = actor, Key = new() { SubjectKind = SubjectKind.Item, SubjectId = item, Attribute = pair.Item1 }, Value = pair.Item2 });
        }
        foreach (var kind in new[] { NeedServiceKind.Cafeteria, NeedServiceKind.Toilet, NeedServiceKind.RestSpot, NeedServiceKind.Bed })
            s.Needs!.Services.Add(new() { Id = 10000 + (int)kind, Kind = kind, Position = At(5), DetentionFacilityId = 1, Capacity = 1,
                DurationTicks = kind == NeedServiceKind.Bed ? 12000 : kind == NeedServiceKind.Cafeteria ? 375 : kind == NeedServiceKind.RestSpot ? 250 : 75 });
        s.Items.Add(new() { Id = 100200, Resource = "meal", Quantity = 20, LocationKind = LocationKind.TileContainer, Owner = 6 });
        if (variant == "terminal-outage")
        {
            scenario.Disturbances.Add(new() { Type = "TerminalChange", Tick = 1, TerminalId = 10001, Enabled = true, Connected = false });
            scenario.Disturbances.Add(new() { Type = "TerminalChange", Tick = 8000, TerminalId = 10001, Enabled = true, Connected = true });
        }
        if (variant == "intake-outage")
        {
            scenario.Disturbances.Add(new() { Type = "DetentionServiceChange", Tick = 1, DetentionFacilityId = 1, Enabled = false, Connected = false });
            scenario.Disturbances.Add(new() { Type = "DetentionServiceChange", Tick = 8000, DetentionFacilityId = 1, Enabled = true, Connected = true });
        }
        if (variant == "interrupted-escort")
        {
            // A temporary slow segment freezes the first escort's travel time. Needs
            // continue accumulating in transit and interrupt the group at its next boundary.
            scenario.Disturbances.Add(new() { Type = "TraversalChange", Tick = 5400, Tile = At(5), TraversalTicks = 60000 });
            scenario.Disturbances.Add(new() { Type = "TraversalChange", Tick = 6000, Tile = At(5), TraversalTicks = 10 });
        }
        // Retain the full causal graph for the small proof. Population runs use the
        // same world and streaming checks without keeping millions of trace objects.
        if (population <= 64)
        {
            foreach (var script in scenario.ScriptedActors)
            {
                var last = script.Commands.Last();
                s.ResponseChainRoles.Add(new() { Name = "trigger-and-resumed-worker-" + script.ActorId, ActorId = script.ActorId, TriggerKind = ResponseTriggerKind.Event,
                    Trigger = new() { Type = "ActionRequested", ActorId = script.ActorId, Tick = last.Tick, Detail = last.Kind.ToString() },
                    AllowedTriggerCommands = script.Commands.Select(c => new ResponseTriggerCommandData { Tick = c.Tick, Kind = c.Kind }).ToList(),
                    RequiredEvents = new() { new() { Type = "ActionCompleted", ActorId = script.ActorId, Tick = last.Tick + 1, Detail = "PostMessage" } } });
            }
            var supervisor = new ResponseChainRoleData { Name = "supervisor", ActorId = 2, TriggerKind = ResponseTriggerKind.InitialCondition,
                RequiredEvents = new() { new() { Type = "VerificationInspected", ActorId = 2 } } };
            if (variant != "correct") supervisor.RequiredEvents.AddRange(new[] { new ResponseEventData { Type = "RecordsQueried", ActorId = 2 }, new() { Type = "DiscrepancyAuditResolved", ActorId = 2 }, new() { Type = "ReprimandAccepted" } });
            s.ResponseChainRoles.Add(supervisor);
            for (int guard = 3; guard <= 6; guard++) s.ResponseChainRoles.Add(new() { Name = "guard-" + guard, ActorId = guard, TriggerKind = ResponseTriggerKind.InitialCondition,
                RequiredEvents = new() { new() { Type = "ActionCompleted", ActorId = guard, Detail = "ReadInbox" } } });
        }
        return new LoadedScenario(JsonData.Write(scenario), JsonData.Write(catalog), "generated institutional station");
    }
}
