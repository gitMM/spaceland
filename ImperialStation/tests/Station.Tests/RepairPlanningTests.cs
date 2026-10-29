using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Station.Scenarios;
using Xunit;

namespace Station.Tests;

public sealed class RepairPlanningTests
{
    private static Coord At(int x) => new(x, 0, 0);
    private static FactSeed Fact(long item, AttributeKind attribute, FactValue value) => new(new(1), new(SubjectKind.Item, item, attribute), value);
    internal static World Make(int knownPart = 2, int actualPart = 2, bool inputs = true, bool specKnown = true, long fabricationTicks = 5,
        ulong seed = 42, bool goal = true, PlannerConfig? planner = null, long travelTicks = 1, string actualRequired = "correct", bool trace = true, PublicKnowledge? publication = null)
    {
        var resources = new[] { new ResourceDefinition("tool"), new ResourceDefinition("metal"), new ResourceDefinition("wire"), new ResourceDefinition("correct", "cartridge"), new ResourceDefinition("other", "cartridge") };
        var recipe = new RecipeDefinition(1, "fabricate", new[] { new MaterialAmount("metal", 2), new MaterialAmount("wire", 1) }, new[] { new MaterialAmount("correct", 1) }, fabricationTicks);
        var access = new WorkAccess(new[] { "worker" }, "tool");
        var work = new WorkSeed(resources, new[] { recipe }, new[] { new WorkstationDefinition(1, At(0), "fabricate", access) },
            new[] { new MachineDefinition(1, At(1), "cartridge", actualRequired, access, installTicks: 3, inspectTicks: 2) });
        var items = new List<Item> { new(new(1), "tool", 1, new(LocationKind.ActorInventory, 1)) };
        if (inputs) { items.Add(new(new(2), "metal", 2, new(LocationKind.ActorInventory, 1))); items.Add(new(new(3), "wire", 1, new(LocationKind.ActorInventory, 1))); }
        if (actualPart >= 0) items.Add(new(new(4), "correct", 1, new(LocationKind.TileContainer, actualPart + 1)));
        var facts = new List<FactSeed>();
        if (knownPart >= 0)
        {
            facts.Add(Fact(4, AttributeKind.Location, FactValue.At(At(knownPart)))); facts.Add(Fact(4, AttributeKind.Resource, new(FactValueKind.Text, text: "correct")));
            facts.Add(Fact(4, AttributeKind.Quantity, FactValue.Integer(1)));
        }
        if (specKnown) facts.Add(new(new(1), new(SubjectKind.Machine, 1, AttributeKind.RequiredResource), new(FactValueKind.Text, text: "correct")));
        var info = new InformationSeed(facts: facts, planner: planner, publication: publication, repairGoals: goal ? new[] { new RepairGoalSeed(new(1), 1) } : null);
        return new World("repair-planning", seed, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            Enumerable.Range(0, 21).Select(x => new Tile(new(x + 1), "Industrial", At(x), traversalOverride: travelTicks)),
            new[] { new ActorSeed(new(1), "Engineer", "worker", At(0)) }, items, trace, info, work);
    }
    private static string Decisions(World w) => string.Join("\n", w.Trace.Where(e => e.Type == "PlanSelected").Select(e => e.Tick + ":" + e.Detail));

    [Fact]
    public void T10ExistingComponentCompletesRealAutonomousRepair()
    {
        var w = Make(inputs: false); w.AdvanceTo(20);
        Assert.True(Assert.Single(w.InspectMachines()).Operational); Assert.Equal(1, Assert.Single(w.InspectRepairAgents()).CompletedRepairs);
        Assert.Empty(w.InspectResourceTransactions()); Assert.Equal(4, w.ActionsCompleted);
        Assert.Contains("PickUp:Item:4", Decisions(w)); Assert.DoesNotContain("Fabricate:", Decisions(w));
        Assert.All(w.Trace.Where(e => e.Type == "ActionRequested"), e => Assert.True(e.CauseId > 0));
        Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting();
    }
    [Fact]
    public void T10FabricationCompletesWithoutAnySeededFinishedComponent()
    {
        var w = Make(knownPart: -1, actualPart: -1); w.AdvanceTo(20);
        Assert.True(w.InspectMachines()[0].Operational); Assert.Equal(1, w.InspectRepairAgents()[0].CompletedRepairs);
        Assert.Equal(3, w.InspectResourceTransactions().Count); Assert.Equal(4, w.ActionsCompleted);
        Assert.Contains("Fabricate:1@1", Decisions(w));
        Assert.All(w.Trace.Where(e => e.Type == "ActionRequested" && (e.Detail == "PickUp" || e.Detail == "Install")), e => Assert.True(e.ItemId > 0));
        Assert.DoesNotContain(w.InspectItems(), i => i.Resource == "metal" || i.Resource == "wire"); w.AssertAccounting();
    }
    [Fact]
    public void T10MissingKnownPartProducesLocalFailureAndFabricationReplan()
    {
        var w = Make(actualPart: 20); w.AdvanceTo(30);
        Assert.True(w.InspectMachines()[0].Operational); Assert.Equal(1, w.InspectRepairAgents()[0].CompletedRepairs);
        Assert.Contains(w.Trace, e => e.Type == "ActionFailed" && e.Detail == "MissingItem");
        var missing = w.InspectKnowledge(new(1)).Single(f => f.Key.Equals(new FactKey(SubjectKind.Item, 4, AttributeKind.Location)));
        Assert.Equal(FactValue.Absent(At(2)), missing.Value);
        Assert.Equal(21, w.InspectItems().Single(i => i.Id.Value == 4).Location.Owner);
        Assert.Equal(3, w.InspectResourceTransactions().Count); Assert.Contains("Fabricate:1@1", Decisions(w)); w.AssertAccounting();
    }
    [Fact]
    public void T10NoPrerequisitesProducesExplainedNoPlanAndNoResources()
    {
        var w = Make(knownPart: -1, actualPart: -1, inputs: false); w.AdvanceTo(20);
        Assert.False(w.InspectMachines()[0].Operational); Assert.Single(w.InspectItems()); Assert.Empty(w.InspectResourceTransactions());
        Assert.Equal(PlanOutcome.NoPlan, w.InspectRepairAgents()[0].LastOutcome);
        Assert.Equal("NoKnownRepairPrerequisitesOrRoute", w.InspectRepairAgents()[0].BlockedReason); Assert.Equal(50, w.InspectRepairAgents()[0].NextDecisionTick);
        Assert.Equal(0, w.ActionsCompleted); w.AssertAccounting();
    }
    [Fact]
    public void UnknownSpecificationRequiresFiniteInspectionBeforeChoosingPart()
    {
        var w = Make(knownPart: -1, actualPart: -1, specKnown: false); w.AdvanceTo(2);
        Assert.Empty(w.InspectResourceTransactions()); Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.RequiredResource);
        Assert.Equal(ActionKind.InspectMachine, w.InspectActor(new(1)).Action);
        w.AdvanceTo(30); Assert.True(w.InspectMachines()[0].Operational);
        Assert.Contains(w.Trace, e => e.Type == "ActionCompleted" && e.Detail == "InspectMachine" && e.Tick == 3);
        Assert.Equal(1, w.InspectRepairAgents()[0].CompletedRepairs);
    }
    [Fact]
    public void T23KnownTravelAndRecipeCostsFlipTheChosenAlternativeAtCrossover()
    {
        var near = Make(goal: false); near.AdvanceTo(0); var nearPlan = RepairPlanning.Plan(near.ActorView(new(1)), 1);
        Assert.Equal(7, nearPlan.Cost); Assert.DoesNotContain(nearPlan.Steps, s => s.Intent.Kind == ActionKind.ProduceRecipe);
        var far = Make(knownPart: 10, actualPart: 10, goal: false); far.AdvanceTo(0); var farPlan = RepairPlanning.Plan(far.ActorView(new(1)), 1);
        Assert.Equal(10, farPlan.Cost); Assert.Contains(farPlan.Steps, s => s.Intent.Kind == ActionKind.ProduceRecipe);
        var expensiveRecipe = Make(knownPart: 10, actualPart: 10, fabricationTicks: 30, goal: false); expensiveRecipe.AdvanceTo(0);
        var expensivePlan = RepairPlanning.Plan(expensiveRecipe.ActorView(new(1)), 1);
        Assert.Equal(23, expensivePlan.Cost); Assert.DoesNotContain(expensivePlan.Steps, s => s.Intent.Kind == ActionKind.ProduceRecipe);
        // Existing: 2*d + 3 ticks; fabrication: 10. The crossover is between d=3 and d=4.
        for (int distance = 2; distance <= 5; distance++)
        {
            var w = Make(knownPart: distance, actualPart: distance, goal: false); w.AdvanceTo(0);
            var plan = RepairPlanning.Plan(w.ActorView(new(1)), 1);
            Assert.Equal(Math.Min(2 * distance + 3, 10), plan.Cost);
            Assert.Equal(distance >= 4, plan.Steps.Any(s => s.Intent.Kind == ActionKind.ProduceRecipe));
        }
    }
    [Fact]
    public void HiddenItemAndMachineDifferencesDoNotChangeInitialDecision()
    {
        var original = Make(actualPart: 2); var hidden = Make(actualPart: 20, actualRequired: "other");
        original.AdvanceTo(0); hidden.AdvanceTo(0); Assert.Equal(Decisions(original), Decisions(hidden));
        Assert.Equal(original.ActorView(new(1)).Knowledge.Select(f => (f.Key, f.Value)), hidden.ActorView(new(1)).Knowledge.Select(f => (f.Key, f.Value)));
        Assert.Equal(original.InspectActor(new(1)).Destination, hidden.InspectActor(new(1)).Destination);
        // The public catalog carries no machine requirement or installed item.
        Assert.Equal(original.ActorView(new(1)).PublicKnowledge!.Work!.Equipment[0].FitClass, hidden.ActorView(new(1)).PublicKnowledge!.Work!.Equipment[0].FitClass);
    }
    [Fact]
    public void BudgetExhaustionBackoffAndSnapshotsDoNotCreateMaterials()
    {
        var w = Make(knownPart: -1, actualPart: -1, planner: new PlannerConfig(nodeBudget: 0, initialBackoff: 2, maximumBackoff: 8));
        w.AdvanceTo(5); var restored = World.Restore(w.Serialize(), true); w.AdvanceTo(15); restored.AdvanceTo(15);
        Assert.Equal(w.Digest(), restored.Digest()); Assert.Equal(new long[] { 0, 2, 6, 14 }, w.Trace.Where(e => e.Type == "PlanUnavailable").Select(e => e.Tick));
        Assert.Equal(22, w.InspectRepairAgents()[0].NextDecisionTick); Assert.Empty(w.InspectResourceTransactions()); Assert.Equal(0, w.ActionsCompleted);
    }
    [Fact]
    public void AutonomousRepairContinuesAcrossTwentySeedsAndFiveCheckpoints()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            World Create() => Make(knownPart: seed % 2 == 0 ? -1 : 2, actualPart: seed % 3 == 0 ? 20 : -1, fabricationTicks: 3 + (long)seed % 5, seed: seed);
            var original = Create(); original.AdvanceTo(100); Assert.True(original.InspectMachines()[0].Operational);
            foreach (long tick in new long[] { 0, 2, 4, 7, 12 })
            {
                var w = Create(); w.AdvanceTo(tick); var data = w.Serialize(); var restored = World.Restore(data, true);
                Assert.Equal(data, restored.Serialize()); restored.AdvanceTo(100); Assert.Equal(original.Digest(), restored.Digest()); restored.AssertAccounting();
                Assert.DoesNotContain(restored.InspectKnowledge(new(1)), f => f.Pinned);
            }
        }
    }
    [Fact]
    public void UnobservedTraversalChangeAffectsExecutionWithoutChangingPlan()
    {
        var original = Make(inputs: false); var changed = Make(inputs: false);
        changed.ScheduleTraversalChange(0, At(1), 100); original.AdvanceTo(0); changed.AdvanceTo(0);
        Assert.Equal(Decisions(original), Decisions(changed));
        Assert.NotEqual(original.InspectActor(new(1)).Arrival, changed.InspectActor(new(1)).Arrival);
        Assert.Equal(7, original.InspectRepairAgents()[0].LastPlanCost); Assert.Equal(7, changed.InspectRepairAgents()[0].LastPlanCost);
    }
    [Fact]
    public void PublishedCatalogDivergenceSurvivesSnapshotWithoutTruthRefresh()
    {
        var known = Make(goal: false).ActorView(new(1)).PublicKnowledge!; var work = known.Work!;
        var fastRecipe = new RecipeDefinition(1, "fabricate", work.Recipes[0].Inputs, work.Recipes[0].Outputs, 1);
        var publication = new PublicKnowledge(7, known.Layout, known.Services, new PublicWorkCatalog(work.Resources, new[] { fastRecipe }, work.Workstations, work.Equipment, work.PickupTicks));
        var w = Make(knownPart: -1, actualPart: -1, publication: publication, goal: false); w.AdvanceTo(0); var restored = World.Restore(w.Serialize());
        Assert.Equal(7, restored.ActorView(new(1)).PublicKnowledge!.Version);
        Assert.Equal(1, restored.ActorView(new(1)).PublicKnowledge!.Work!.Recipes[0].Duration);
        var plan = RepairPlanning.Plan(restored.ActorView(new(1)), 1); Assert.Equal(6, plan.Cost);
        restored.Submit(1, plan.Steps[0].Intent); restored.AdvanceTo(2); Assert.Empty(restored.InspectResourceTransactions());
        restored.AdvanceTo(6); Assert.Equal(3, restored.InspectResourceTransactions().Count); // actual recipe still takes five ticks
    }
    [Fact]
    public void GeneratedRepairVariantsUseOrdinaryPolicyAndResumeAtFiveCheckpoints()
    {
        var canonical = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "alternative_planning.json"));
        for (ulong seed = 0; seed < 20; seed++) foreach (var variant in Enum.GetValues<RepairVariant>())
        {
            var loaded = RepairGenerator.Generate(canonical.ScenarioJson, canonical.CatalogJson, seed, variant);
            Assert.Empty(loaded.Scenario.ScriptedActors);
            var run = Runs.Execute(loaded, seed, true); Assert.Equal("completed", run.Result.Status);
            Assert.Equal(variant == RepairVariant.NoPrerequisites ? 0 : 1, run.Result.Metrics["repairGoalsCompleted"]);
            if (variant == RepairVariant.Existing) Assert.Empty(run.World.InspectResourceTransactions());
            if (variant is RepairVariant.Fabrication or RepairVariant.MissingKnown or RepairVariant.UnknownSpecification) Assert.Equal(3, run.World.InspectResourceTransactions().Count);
            if (variant == RepairVariant.MissingKnown) Assert.Contains(run.World.Trace, e => e.Type == "ActionFailed" && e.Detail == "MissingItem");
            if (variant is RepairVariant.StackedExisting or RepairVariant.StackedFabrication or RepairVariant.StackedStash)
            {
                Assert.Single(run.World.InspectStackChanges()); Assert.Equal(ActionKind.SplitStack, run.World.InspectStackChanges()[0].Kind);
                Assert.Equal(1, run.World.InspectItems().Single(i => i.Location.Kind == LocationKind.MachineSlot).Quantity);
                Assert.Contains(run.World.InspectItems(), i => i.Resource == "correct" && i.Location.Kind == LocationKind.ActorInventory && i.Quantity >= 1);
                Assert.Equal(0, run.World.ActionsFailed);
            }
            foreach (long tick in new long[] { 0, 2, 5, 9, 17 })
            {
                var w = loaded.CreateWorld(); w.AdvanceTo(tick); var data = w.Serialize(); var restored = World.Restore(data);
                Assert.Equal(data, restored.Serialize()); var continuation = Runs.Execute(loaded, seed, false, startingWorld: restored);
                Assert.Equal(run.Result.Digest, continuation.Result.Digest); Assert.Equal("completed", continuation.Result.Status);
            }
        }
    }
    [Fact]
    public void RepairWorldsMatchSequentialRunsAtConcurrencyTwoFourAndEight()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "planner_costs.json"));
        var expected = Enumerable.Range(0, 24).Select(i => Runs.Execute(loaded, (ulong)i, true).Result).ToArray();
        Assert.All(expected, r => Assert.Equal("completed", r.Status));
        foreach (int concurrency in new[] { 2, 4, 8 })
        {
            var actual = new RunResult[24]; Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i => actual[i] = Runs.Execute(loaded, (ulong)i, false).Result);
            for (int i = 0; i < expected.Length; i++) { Assert.Equal(expected[i].Digest, actual[i].Digest); Assert.Equal(expected[i].Metrics, actual[i].Metrics); }
        }
    }
    [Fact]
    public void RawMaterialsOnTheFloorRequirePlannedPickupsBeforeProduction()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "alternative_planning.json"));
        var data = loaded.Scenario; data.InitialState.Items.RemoveAll(i => i.Id == 4);
        data.InitialState.Information!.Facts.RemoveAll(f => f.Key.SubjectKind == SubjectKind.Item);
        foreach (var item in data.InitialState.Items.Where(i => i.Resource is "metal" or "wire")) { item.LocationKind = LocationKind.TileContainer; item.Owner = 1; }
        var scenario = new LoadedScenario(JsonData.Write(data), loaded.CatalogJson, "floor inputs");
        var run = Runs.Execute(scenario, 42, true); Assert.Equal("completed", run.Result.Status);
        Assert.True(run.World.InspectMachines()[0].Operational); Assert.Equal(6, run.World.ActionsCompleted);
        Assert.Equal(new[] { "PickUp", "PickUp", "ProduceRecipe", "PickUp", "Move", "Install" }, run.World.Trace.Where(e => e.Type == "ActionCompleted").Select(e => e.Detail));
    }
    [Fact]
    public void InspectionDoesNotRevealWhetherAnAbsentItemExistsRemotely()
    {
        var remote = Make(actualPart: 20, goal: false); var missing = Make(actualPart: -1, goal: false);
        foreach (var w in new[] { remote, missing })
        { w.Submit(0, new(new(1), ActionKind.Inspect, At(0), new(4))); w.AdvanceTo(0); Assert.Equal(ActionKind.Inspect, w.InspectActor(new(1)).Action); w.AdvanceTo(100); }
        Assert.Equal(remote.InspectKnowledge(new(1)).Select(f => (f.Key, f.Value)), missing.InspectKnowledge(new(1)).Select(f => (f.Key, f.Value)));
        Assert.Equal(FactValue.Absent(At(0)), remote.InspectKnowledge(new(1)).Single(f => f.Key.Equals(new FactKey(SubjectKind.Item, 4, AttributeKind.Location))).Value);
    }
    [Fact]
    public void OwnInventoryFailuresDoNotRevealWhetherAnUnheldItemExistsRemotely()
    {
        foreach (int actual in new[] { -1, 20 })
        {
            var w = Make(actualPart: actual, goal: false);
            w.Submit(0, new(new(1), ActionKind.PutDown, At(0), new(4))); w.AdvanceTo(0); Assert.Equal(FailureReason.NotHeld, w.InspectActor(new(1)).LastFailure);
            w.Submit(1, new(new(1), ActionKind.Move, At(1))); w.Submit(2, new(new(1), ActionKind.Install, At(1), new(4), serviceId: 1));
            w.AdvanceTo(2); Assert.Equal(FailureReason.NotHeld, w.InspectActor(new(1)).LastFailure);
        }
    }
    [Fact]
    public void PendingReportAndRepairSharePlannerBudgetWithoutCompetingIntents()
    {
        var source = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "alternative_planning.json"));
        var data = source.Scenario; var catalog = source.Catalog; var info = data.InitialState.Information!;
        catalog.Resources.Add(new ResourceData { Id = "card" });
        data.InitialState.Items.Add(new ItemData { Id = 100, Resource = "card", Quantity = 1, LocationKind = LocationKind.ActorInventory, Owner = 1 });
        info.Terminals.Add(new TerminalData { Id = 1, Position = new(), PermittedRoles = new() { "worker" } });
        info.Credentials.Add(new CredentialData { Id = 1, Holder = 1, Account = 1, CardItem = 100 });
        info.Configuration.LoginTicks = 1; info.Configuration.PostTicks = 1; info.Configuration.LatencyTicks = 1;
        info.Planner.RequestsPerTick = 1;
        info.ReportGoals.Add(new ReportGoalData { ActorId = 1, Report = new MessageData { Kind = MessageKind.Observation, RecipientKind = RecipientKind.Authority,
            Subject = new() { SubjectKind = SubjectKind.Machine, SubjectId = 1, Attribute = AttributeKind.Operational }, Claim = new() { Kind = FactValueKind.Boolean, Number = 0 }, ObservedTick = 0 } });
        var loaded = new LoadedScenario(JsonData.Write(data), JsonData.Write(catalog), "report and repair");
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status);
        Assert.Equal(1, run.World.InspectAgents()[0].CompletedReports); Assert.Equal(1, run.World.InspectRepairAgents()[0].CompletedRepairs);
        Assert.Equal(new[] { "Login", "PostMessage" }, run.World.Trace.Where(e => e.Type == "ActionCompleted").Take(2).Select(e => e.Detail));
        Assert.DoesNotContain(run.World.Trace, e => e.Type == "ActionFailed" && e.Detail == "ActorBusy");
        Assert.All(run.World.Trace.Where(e => e.Type is "PlanSelected" or "PlanUnavailable" or "RepairGoalCompleted").GroupBy(e => e.Tick), g => Assert.Single(g));
        var w = loaded.CreateWorld(); w.AdvanceTo(1); var restored = World.Restore(w.Serialize());
        Assert.Equal(run.Result.Digest, Runs.Execute(loaded, 42, false, startingWorld: restored).Result.Digest);
    }
}
