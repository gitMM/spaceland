using System.Reflection;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class ConstructionTests
{
    [Fact]
    public void RunnerFixtureUsesDefaultHabitatRecipeWithNeedsAndPhysicalMaterialCollection()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "physical_construction.json"));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(13, loaded.Scenario.Assertions.Count); Assert.Equal(12, run.World.InspectActors().Count); Assert.Equal(12, run.World.InspectNeeds().Count);
        Assert.Equal(18000, loaded.Catalog.Recipes[0].Duration); Assert.Equal(4, loaded.Catalog.Recipes[0].Inputs[0].Quantity);
        Assert.Equal(18011, Assert.Single(run.World.InspectConstructionProjects()).CompletedTick); Assert.Equal(16, run.World.UsableHousingBerths);
        Assert.Equal(4, run.World.ActionsCompleted); Assert.Equal(0, run.World.ActionsFailed); Assert.Empty(run.World.InspectBeliefs());
        Assert.Equal(run.World.Digest(), World.Restore(run.World.Serialize()).Digest());
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("detached")]
    [InlineData("duplicate")]
    [InlineData("duration")]
    [InlineData("recipe")]
    public void ConstructionLoaderRejectsInvalidReservationsAndDefinitions(string fault)
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "physical_construction.json"));
        var data = JsonData.Read<ScenarioData>(loaded.ScenarioJson, "construction"); var catalog = JsonData.Read<CatalogData>(loaded.CatalogJson, "catalog");
        var p = data.InitialState.Construction!.Projects[0];
        if (fault == "occupied") p.Site.X = 1;
        if (fault == "detached") p.Site.X = 5;
        if (fault == "duplicate") data.InitialState.Construction.Projects.Add(p);
        if (fault == "duration") catalog.Tiles.Single(t => t.Construction != null).Construction!.SleepTicks = 0;
        if (fault == "recipe") catalog.Tiles.Single(t => t.Construction != null).Construction!.RecipeId = 99;
        var failure = Assert.Throws<ScenarioException>(() => new LoadedScenario(JsonData.Write(data), JsonData.Write(catalog), "construction-invalid"));
        Assert.Contains("construction", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Coord Site = new(2, 0, 0), Standing = new(1, 0, 0);
    private static World Make(long material = 6, bool tool = true, string role = "builder", bool credential = false, bool needs = true, long duration = 5, ulong seed = 42, bool secondWorker = false, long? hunger = null)
    {
        var blueprint = new ConstructionDefinition(1, new WorkAccess(new[] { "builder" }, "tool", requiresCredential: credential), 8, sleepTicks: 10, allocationTicks: 2, allocationRequiresCredential: false);
        var catalog = Fixtures.Catalog.Select(d => new TileDefinition(d.Id, d.Family, 2, d.VisualKey, d.Capabilities, d.Family == TileFamily.Habitat ? blueprint : null));
        var items = new List<Item>(); if (material > 0) items.Add(new(new(1), "material", material, new(LocationKind.ActorInventory, 1)));
        if (tool) items.Add(new(new(2), "tool", 1, new(LocationKind.ActorInventory, 1)));
        if (credential) items.Add(new(new(3), "card", 1, new(LocationKind.ActorInventory, 1)));
        if (secondWorker) { items.Add(new(new(4), "material", 4, new(LocationKind.ActorInventory, 2))); items.Add(new(new(5), "tool", 1, new(LocationKind.ActorInventory, 2))); }
        var work = new WorkSeed(new[] { new ResourceDefinition("material"), new ResourceDefinition("tool"), new ResourceDefinition("card") },
            new[] { new RecipeDefinition(1, "HabitatConstruction", new[] { new MaterialAmount("material", 4) }, Array.Empty<MaterialAmount>(), duration) }, Array.Empty<WorkstationDefinition>(), Array.Empty<MachineDefinition>());
        var information = new InformationSeed(new InformationConfig(), Array.Empty<TerminalSeed>(), credential ? new[] { new CredentialSeed(1, new(1), new(1), new(3)) } : Array.Empty<CredentialSeed>());
        return new World("physical-construction", seed, new WorldConfig(itemActionTicks: 1), catalog,
            new[] { new Tile(new(1), "Storage", new(0, 0, 0), traversalOverride: 1), new Tile(new(2), "Corridor", Standing, traversalOverride: 1) },
            new[] { new ActorSeed(new(1), "builder", role, Standing), new ActorSeed(new(2), "observer", "builder", secondWorker ? Standing : new(0, 0, 0)) }, items, true, information, work,
            needs: needs ? new NeedSeed(autonomousActors: Array.Empty<long>(), initial: hunger.HasValue ? new[] { new NeedInitialValue(new(1), NeedKind.Hunger, hunger.Value) } : null) : null,
            construction: new ConstructionSeed(new[] { new ConstructionProjectSeed(1, "Habitat", Site, 3) }));
    }
    private static void Build(World world, long tick = 0, long actor = 1) => world.Submit(tick, new(new(actor), ActionKind.Construct, Standing, serviceId: 1));

    [Fact]
    public void ReservationCreatesNoTileOrCapacityAndFiniteWorkCommitsRealInputsOnce()
    {
        var w = Make(); Assert.False(w.Topology.Contains(Site)); Assert.Equal(0, w.UsableHousingBerths); Assert.Empty(w.InspectResourceClaims());
        Build(w); w.AdvanceTo(4); Assert.Equal(ConstructionStatus.Building, Assert.Single(w.InspectConstructionProjects()).Status);
        Assert.Equal(6, Assert.Single(w.InspectItems(), i => i.Resource == "material").Quantity); Assert.Equal(NeedMode.Working, Assert.Single(w.InspectNeeds(), n => n.ActorId == 1).Mode);
        Assert.False(w.Topology.Contains(Site)); Assert.Equal(0, w.UsableHousingBerths); Assert.Empty(w.InspectResourceTransactions()); Assert.Equal(3, w.InspectResourceClaims().Count);
        w.AdvanceTo(5); var project = Assert.Single(w.InspectConstructionProjects()); Assert.Equal(ConstructionStatus.Completed, project.Status);
        Assert.Equal(5, project.CompletedTick); Assert.Equal(1, w.Topology.Version); Assert.True(w.Topology.Contains(Site)); Assert.Equal(8, w.UsableHousingBerths); Assert.Equal(8, w.VacantHousingBerths);
        Assert.Equal(2, Assert.Single(w.InspectItems(), i => i.Resource == "material").Quantity); Assert.Equal(-4, Assert.Single(w.InspectResourceTransactions()).Delta);
        Assert.Empty(w.InspectResourceClaims()); Assert.Equal(8, w.ActorView(new(1)).NeedServices.Count); Assert.Empty(w.ActorView(new(2)).NeedServices);
        Assert.DoesNotContain(w.ActorView(new(1)).PublicKnowledge!.Layout, t => t.Position == Site); Assert.Empty(w.InspectBeliefs());
        Build(w, 6); w.AdvanceTo(6); Assert.Equal(FailureReason.InvalidConstructionSite, w.InspectActor(new(1)).LastFailure);
        Assert.Single(w.InspectResourceTransactions()); Assert.Equal(8, w.UsableHousingBerths); w.AssertAccounting();
    }

    [Fact]
    public void NewTileIsReachableOnlyAfterCompletionAndServicesAreLearnedLocally()
    {
        var w = Make(); Build(w); w.Submit(4, new(new(2), ActionKind.Move, Site)); w.AdvanceTo(4);
        Assert.Equal(FailureReason.InvalidDestination, w.InspectActor(new(2)).LastFailure);
        w.AdvanceTo(5); Assert.Equal(PathOutcome.PathFound, w.Topology.FindPath(new(0, 0, 0), Site).Outcome);
        w.Submit(6, new(new(2), ActionKind.Move, Site)); w.AdvanceTo(9); Assert.Equal(Site, w.InspectActor(new(2)).Position); Assert.Equal(8, w.ActorView(new(2)).NeedServices.Count);
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }

    [Fact]
    public void CancellationReleasesClaimsAndRetainsMaterialsForAFullRetry()
    {
        var w = Make(); Build(w); w.Submit(2, new(new(1), ActionKind.Cancel, Standing)); w.AdvanceTo(2);
        Assert.Equal(ConstructionStatus.Reserved, Assert.Single(w.InspectConstructionProjects()).Status); Assert.Empty(w.InspectResourceClaims()); Assert.Empty(w.InspectResourceTransactions());
        Assert.Equal(6, Assert.Single(w.InspectItems(), i => i.Resource == "material").Quantity); Assert.False(w.Topology.Contains(Site));
        Build(w, 3); w.AdvanceTo(7); Assert.False(w.Topology.Contains(Site)); w.AdvanceTo(8); Assert.True(w.Topology.Contains(Site)); Assert.Single(w.InspectResourceTransactions()); w.AssertAccounting();
    }

    [Fact]
    public void CriticalNeedInterruptsConstructionWithoutConsumingMaterialOrCreatingCapacity()
    {
        var w = Make(hunger: 900 * NeedAccumulator.HourMilliseconds - 12000); Build(w); w.AdvanceTo(2);
        Assert.Equal(1, w.CriticalNeedInterruptions);
        Assert.Equal(ConstructionStatus.Reserved, Assert.Single(w.InspectConstructionProjects()).Status); Assert.Empty(w.InspectResourceClaims());
        Assert.Empty(w.InspectResourceTransactions()); Assert.False(w.Topology.Contains(Site)); Assert.Equal(0, w.UsableHousingBerths);
        Assert.Equal(6, Assert.Single(w.InspectItems(), i => i.Resource == "material").Quantity); Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }

    [Fact]
    public void ConstructedBerthCanBeAllocatedAndUsedForPhysicalSleep()
    {
        var w = Make(); Build(w); w.AdvanceTo(5); long berth = Assert.Single(w.InspectConstructionProjects()).BerthIds[0];
        w.Submit(6, new(new(1), ActionKind.Move, Site)); w.Submit(8, new(new(1), ActionKind.AllocateBerth, Site, serviceId: berth));
        w.Submit(10, new(new(1), ActionKind.Sleep, Site, serviceId: berth)); w.AdvanceTo(21);
        Assert.Equal(1, Assert.Single(w.InspectBerthAllocations()).ActorId); Assert.Equal(NeedActivityStatus.Completed, Assert.Single(w.InspectNeedActivities()).Status);
        Assert.Equal(7, w.VacantHousingBerths); Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }

    [Theory]
    [InlineData("materials", FailureReason.InsufficientMaterials)]
    [InlineData("tool", FailureReason.MissingTool)]
    [InlineData("role", FailureReason.NotQualified)]
    [InlineData("position", FailureReason.InvalidConstructionSite)]
    [InlineData("recipe", FailureReason.InvalidConstructionSite)]
    public void FailedPrerequisitesNeverCreateClaimsOrOutputs(string missing, FailureReason expected)
    {
        var w = Make(material: missing == "materials" ? 3 : 6, tool: missing != "tool", role: missing == "role" ? "visitor" : "builder");
        if (missing == "position") w.Submit(0, new(new(2), ActionKind.Construct, new(0, 0, 0), serviceId: 1));
        else w.Submit(0, new(new(1), ActionKind.Construct, Standing, serviceId: 1, recipeId: missing == "recipe" ? 99 : 0));
        w.AdvanceTo(10); Assert.Equal(expected, w.InspectActor(new(missing == "position" ? 2 : 1)).LastFailure);
        Assert.Empty(w.InspectResourceClaims()); Assert.Empty(w.InspectResourceTransactions()); Assert.False(w.Topology.Contains(Site)); Assert.Equal(0, w.UsableHousingBerths);
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }

    [Fact]
    public void CredentialLossDuringWorkIsRestorableAndFailsCompletionWithoutConsumption()
    {
        var w = Make(credential: true); Build(w); w.ScheduleCardLoss(2, new(1), new(3)); w.AdvanceTo(3);
        var copy = World.Restore(w.Serialize(), true); Assert.Equal(w.Digest(), copy.Digest()); w.AdvanceTo(5); copy.AdvanceTo(5);
        Assert.Equal(w.Digest(), copy.Digest()); Assert.Equal(FailureReason.MissingCredential, w.InspectActor(new(1)).LastFailure);
        Assert.Empty(w.InspectResourceClaims()); Assert.Empty(w.InspectResourceTransactions()); Assert.False(w.Topology.Contains(Site)); Assert.Equal(6, Assert.Single(w.InspectItems(), i => i.Resource == "material").Quantity);
    }

    [Fact]
    public void ConcurrentBuildersCannotDoubleConsumeOrDuplicateCapacity()
    {
        var w = Make(secondWorker: true); Build(w); Build(w, actor: 2); w.AdvanceTo(0);
        Assert.Single(w.InspectActors(), a => a.Action == ActionKind.Construct); Assert.Single(w.InspectResourceClaims(), c => c.Kind == ClaimKind.ConstructionSite);
        w.AdvanceTo(5); Assert.Single(w.InspectResourceTransactions()); Assert.Equal(8, w.UsableHousingBerths); Assert.Equal(6, w.InspectItems().Where(i => i.Resource == "material").Sum(i => i.Quantity)); w.AssertAccounting();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryConstructionBoundaryRestoresWithIdenticalRemainingTrace(bool needs)
    {
        for (int tick = 0; tick <= 8; tick++)
        {
            var w = Make(needs: needs); Build(w); w.AdvanceTo(tick); var restored = World.Restore(w.Serialize(), true); Assert.Equal(w.Serialize(), restored.Serialize());
            w.AdvanceTo(20); restored.AdvanceTo(20); Assert.Equal(w.Digest(), restored.Digest());
            Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void SnapshotRejectsForgedConstructionTimingAndOutputs()
    {
        foreach (string name in new[] { "ActiveActionId", "CompletedTick" })
        {
            var w = Make(); Build(w); w.AdvanceTo(2);
            var map = typeof(World).GetField("constructionProjects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            var p = map.GetType().GetProperty("Item")!.GetValue(map, new object[] { 1L })!;
            p.GetType().GetProperty(name)!.SetValue(p, name == "CompletedTick" ? 0L : 999L);
            Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
        }
        var complete = Make(); Build(complete); complete.AdvanceTo(5);
        var state = typeof(World).GetField("constructionProjects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(complete)!;
        var done = state.GetType().GetProperty("Item")!.GetValue(state, new object[] { 1L })!;
        done.GetType().GetProperty("TileId")!.SetValue(done, 999L); Assert.Throws<ArgumentException>(() => World.Restore(complete.Serialize()));
    }
}
