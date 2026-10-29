using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class GrowthTests
{
    private static MaterialAmount[] Stock(long quantity) => quantity == 0 ? Array.Empty<MaterialAmount>() : new[] { new MaterialAmount("material", quantity) };
    private static ExpansionCandidate Candidate(long id, long berths, long cost, long materials = 4, Coord? site = null) => new(id, "Habitat", site ?? new((int)id, 0, 0), berths, cost, Stock(materials));
    [Theory]
    [InlineData(12, 0, 12)]
    [InlineData(20, 50, 21)]
    [InlineData(1, 50, 2)]
    [InlineData(0, 50, 0)]
    [InlineData(1001, 50, 1052)]
    public void TargetUsesPopulationAndRoundedUpReserve(long population, long reserve, long expected) => Assert.Equal(expected, ExpansionPlanning.CapacityTarget(population, reserve));

    [Fact]
    public void GenuineSearchComparesCombinedCapacityAndCosts()
    {
        var choices = new[] { Candidate(1, 4, 6), Candidate(2, 4, 6), Candidate(3, 8, 15) };
        var result = ExpansionPlanning.Plan(8, choices, Stock(8));
        Assert.Equal(PlanOutcome.PlanFound, result.Plan.Outcome); Assert.Equal(12, result.Plan.Cost); Assert.Equal(8, result.BatchTarget);
        Assert.Equal(new long[] { 1, 2 }, result.Plan.Steps.Select(s => s.Intent.ServiceId)); Assert.True(result.Plan.Expansions > 1);
        var tight = ExpansionPlanning.Plan(8, choices, Stock(4)); Assert.Equal(15, tight.Plan.Cost); Assert.Equal(3, Assert.Single(tight.Plan.Steps).Intent.ServiceId);
    }
    [Fact]
    public void SearchEnforcesSharedMaterialsAndOneProjectPerSite()
    {
        var choices = new[] { Candidate(1, 4, 1, site: default(Coord)), Candidate(2, 4, 1, site: default(Coord)), Candidate(3, 8, 5) };
        var result = ExpansionPlanning.Plan(8, choices, Stock(8));
        Assert.Equal(3, Assert.Single(result.Plan.Steps).Intent.ServiceId);
        Assert.Equal(PlanOutcome.NoPlan, ExpansionPlanning.Plan(8, choices, Stock(3)).Plan.Outcome);
        Assert.Equal(PlanOutcome.BudgetExhausted, ExpansionPlanning.Plan(8, choices, Stock(8), nodeBudget: 0).Plan.Outcome);
        Assert.Equal(PlanOutcome.NoPlan, ExpansionPlanning.Plan(8, new[] { Candidate(1, 4, 1), Candidate(2, 4, 1) }, Stock(8), maximumDepth: 1).Plan.Outcome);
    }
    [Fact]
    public void BatchTargetIsBoundedAndCandidateOrderDoesNotAffectPlan()
    {
        var candidates = new[] { Candidate(3, 8, 3), Candidate(1, 8, 3), Candidate(2, 8, 3) };
        var first = ExpansionPlanning.Plan(100, candidates, Stock(8)); var second = ExpansionPlanning.Plan(100, candidates.Reverse(), Stock(8));
        Assert.Equal(16, first.BatchTarget); Assert.Equal(2, first.Plan.Steps.Count);
        Assert.Equal(first.Plan.Steps.Select(s => s.Intent.ServiceId), second.Plan.Steps.Select(s => s.Intent.ServiceId));
        Assert.Throws<ArgumentException>(() => ExpansionPlanning.Plan(8, Enumerable.Range(1, 6).Select(i => Candidate(i, 8, 1)), Stock(24)));
        Assert.Empty(ExpansionPlanning.Plan(0, candidates, Stock(0)).Plan.Steps);
    }
    private static World Make(int population = 12, int actualBeds = 8, long knownBeds = 8, long knownMaterial = 4, long actualMaterial = 4,
        long reserve = 0, bool occupiedSite = false, bool falseLayout = false, int budget = 512, string role = "builder", bool trace = true, bool reverse = false, Coord? minimum = null, Coord? maximum = null)
    {
        var definition = new ConstructionDefinition(1, new WorkAccess(new[] { "builder" }), 8);
        var catalog = Fixtures.Catalog.Select(d => new TileDefinition(d.Id, d.Family, 1, construction: d.Family == TileFamily.Habitat ? definition : null));
        var resources = new[] { new ResourceDefinition("material") };
        var recipes = new[] { new RecipeDefinition(1, "HabitatConstruction", Stock(4), Array.Empty<MaterialAmount>(), 5) };
        var tiles = new List<Tile> { new(new(1), "Habitat", default) }; if (occupiedSite) tiles.Add(new(new(2), "Corridor", new(1, 0, 0)));
        var layout = falseLayout ? new[] { new KnownTile(new(10), new(10, 0, 0), 1) } : new[] { new KnownTile(new(1), default, 1) };
        var publication = new PublicKnowledge(1, layout, Array.Empty<PublicService>(), new PublicWorkCatalog(resources, recipes, Array.Empty<PublicWorkstation>(), Array.Empty<PublicEquipment>(), 1));
        var people = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "Citizen " + i, i == 1 ? role : "citizen", default));
        return new World("growth", 42, new WorldConfig(), reverse ? catalog.Reverse() : catalog, tiles, reverse ? people.Reverse() : people,
            actualMaterial == 0 ? Array.Empty<Item>() : new[] { new Item(new(1), "material", actualMaterial, new(LocationKind.ActorInventory, 1)) }, trace,
            new InformationSeed(publication: publication, planner: new PlannerConfig(nodeBudget: budget, initialBackoff: 2, maximumBackoff: 8)),
            new WorkSeed(resources, recipes, Array.Empty<WorkstationDefinition>(), Array.Empty<MachineDefinition>()), new JobSystemSeed(Array.Empty<JobSeed>()), authority: new AuthorityConfig(),
            needs: new NeedSeed(services: Enumerable.Range(1, actualBeds).Select(i => new NeedServiceDefinition(i, NeedServiceKind.Bed, default, assignedActorId: i <= population ? i : 0)), autonomousActors: Array.Empty<long>()),
            growth: new GrowthSeed(knownBeds, minimum ?? new(1, 0, 0), maximum ?? new(11, 0, 0), Stock(knownMaterial), reserve));
    }
    [Fact]
    public void ShortfallCommitsExactlyOneHabitatWithoutChangingPhysicalCapacityOrInventory()
    {
        var w = Make(); Assert.Empty(w.InspectConstructionProjects()); w.AdvanceTo(100);
        var s = w.InspectGrowth()!; Assert.Equal(12, s.Target); Assert.Equal(8, s.BelievedCompletedBerths); Assert.Equal(8, s.CommittedPendingBerths);
        Assert.Equal("CapacityCommittedAwaitingEvidence", s.Reason); Assert.True(s.NextReview > 100); Assert.Empty(s.AvailableMaterials);
        var p = Assert.Single(w.InspectConstructionProjects()); Assert.True(p.AuthorityManaged); Assert.Equal(new Coord(1, 0, 0), p.Site); Assert.Equal(ConstructionStatus.Reserved, p.Status);
        Assert.Equal(8, w.UsableHousingBerths); Assert.Single(w.Topology.InspectTiles()); Assert.Equal(4, Assert.Single(w.InspectItems()).Quantity); Assert.Empty(w.InspectResourceClaims());
        Assert.Single(w.Trace, e => e.Type == "GrowthProjectCommitted"); Assert.Equal(1, w.PlannerRequests);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Fact]
    public void VacantCapacityCountsAndAssignmentsDoNotCauseExpansion()
    {
        var w = Make(population: 20, actualBeds: 24, knownBeds: 24, reserve: 50); w.AdvanceTo(100);
        Assert.Equal(21, w.InspectGrowth()!.Target); Assert.Equal(4, w.VacantHousingBerths); Assert.Equal(20, w.InspectBerthAllocations().Count);
        Assert.Empty(w.InspectConstructionProjects()); Assert.Equal(0, w.PlannerRequests);
    }
    [Fact]
    public void PhysicalCompletionKeepsCommitmentUntilAReportCanBeAccepted()
    {
        var w = Make(); w.AdvanceTo(0); w.Submit(1, new(new(1), ActionKind.Construct, default, serviceId: 1)); w.AdvanceTo(100);
        Assert.Equal(ConstructionStatus.Completed, Assert.Single(w.InspectConstructionProjects()).Status); Assert.Equal(16, w.UsableHousingBerths);
        Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(8, w.InspectGrowth()!.CommittedPendingBerths); Assert.Empty(w.InspectGrowth()!.AvailableMaterials);
        Assert.Single(w.Trace, e => e.Type == "GrowthProjectCommitted"); Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }
    [Theory]
    [InlineData(0, 4, true)]
    [InlineData(4, 0, false)]
    public void PlannerUsesKnownMaterialBudgetNotPhysicalInventory(long actual, long known, bool commits)
    {
        var w = Make(actualMaterial: actual, knownMaterial: known); w.AdvanceTo(0);
        Assert.Equal(commits ? 1 : 0, w.InspectConstructionProjects().Count);
        if (commits) { w.Submit(1, new(new(1), ActionKind.Construct, default, serviceId: 1)); w.AdvanceTo(1); Assert.Equal(FailureReason.InsufficientMaterials, w.InspectActor(new(1)).LastFailure); }
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FalseLayoutCanReserveAnInvalidSiteButCannotBuildIt(bool detached)
    {
        var w = Make(occupiedSite: !detached, falseLayout: detached); w.AdvanceTo(0);
        Assert.Equal(new Coord(detached ? 9 : 1, 0, 0), Assert.Single(w.InspectConstructionProjects()).Site);
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
        w.Submit(1, new(new(1), ActionKind.Construct, default, serviceId: 1)); w.AdvanceTo(10);
        Assert.Equal(FailureReason.InvalidConstructionSite, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectResourceTransactions()); Assert.Equal(8, w.UsableHousingBerths);
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }
    [Fact]
    public void BeliefsCanBeWrongAboutCapacityWithoutGettingCorrectedByHiddenState()
    {
        var falselyEnough = Make(actualBeds: 0, knownBeds: 24); falselyEnough.AdvanceTo(10); Assert.Empty(falselyEnough.InspectConstructionProjects());
        var falselyShort = Make(actualBeds: 24, knownBeds: 8); falselyShort.AdvanceTo(10); Assert.Single(falselyShort.InspectConstructionProjects());
    }
    [Theory]
    [InlineData(0, "builder", PlanOutcome.BudgetExhausted)]
    [InlineData(512, "citizen", PlanOutcome.NoPlan)]
    public void FailedPlanningBacksOffWithoutCreatingProjects(int budget, string role, PlanOutcome outcome)
    {
        var w = Make(budget: budget, role: role); w.AdvanceTo(6); var s = w.InspectGrowth()!;
        Assert.Equal(outcome, s.LastOutcome); Assert.Equal(14, s.NextReview); Assert.Equal(8, s.Backoff); Assert.Equal(3, w.PlannerRequests); Assert.Empty(w.InspectConstructionProjects());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryCheckpointsTraceAndInputOrderPreserveReplay(bool build)
    {
        World Start(bool trace = true, bool reverse = false)
        { var w = Make(trace: trace, reverse: reverse); if (build) w.Submit(1, new(new(1), ActionKind.Construct, default, serviceId: 1)); return w; }
        var direct = Start(); direct.AdvanceTo(100); var quiet = Start(false, true); quiet.AdvanceTo(100); Assert.Equal(direct.Digest(), quiet.Digest());
        foreach (long boundary in new long[] { -1, 0, 1, 2, 5, 6, 7, 100 })
        {
            var w = Start(); if (boundary >= 0) w.AdvanceTo(boundary); var restored = World.Restore(w.Serialize(), true);
            Assert.Equal(w.Serialize(), restored.Serialize()); restored.AdvanceTo(100); Assert.Equal(direct.Digest(), restored.Digest());
            Assert.Equal(JsonData.Write(direct.Trace.Where(e => e.Tick > boundary).ToArray()), JsonData.Write(restored.Trace));
        }
    }
    [Fact]
    public void MultipleProjectsReserveDistinctBoundedSitesAndStopAtCombinedCapacity()
    {
        var w = Make(population: 24, knownMaterial: 12, minimum: new(-1, -1, 0), maximum: new(1, 1, 0)); w.AdvanceTo(100);
        Assert.Equal(new[] { new Coord(-1, 0, 0), new Coord(0, -1, 0) }, w.InspectConstructionProjects().Select(p => p.Site));
        Assert.Equal(16, w.InspectGrowth()!.CommittedPendingBerths); Assert.Equal(4, Assert.Single(w.InspectGrowth()!.AvailableMaterials).Quantity);
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
        var tight = Make(population: 24, knownMaterial: 4, minimum: new(-1, -1, 0), maximum: new(1, 1, 0)); tight.AdvanceTo(20);
        Assert.Single(tight.InspectGrowth()!.Commitments); Assert.Equal(PlanOutcome.NoPlan, tight.InspectGrowth()!.LastOutcome); Assert.True(tight.InspectGrowth()!.NextReview > 20);
        var blocked = Make(minimum: default(Coord), maximum: default(Coord)); blocked.AdvanceTo(20); Assert.Empty(blocked.InspectConstructionProjects());
    }
    [Fact]
    public void SnapshotRejectsForgedCapacityBudgetAndMissingReview()
    {
        foreach (string field in new[] { "Target", "BelievedCompletedBerths", "NextReview", "Backoff" })
        {
            var w = Make(knownMaterial: 0); w.AdvanceTo(0);
            var state = typeof(World).GetField("growthState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
            state.GetType().GetProperty(field)!.SetValue(state, field == "NextReview" ? -1L : 9999L);
            Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
        }
        var committed = Make(); committed.AdvanceTo(0);
        var map = typeof(World).GetField("growthCommitments", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(committed)!;
        var c = map.GetType().GetProperty("Item")!.GetValue(map, new object[] { 1L })!;
        c.GetType().GetProperty("Materials")!.SetValue(c, Stock(5)); Assert.Throws<ArgumentException>(() => World.Restore(committed.Serialize()));
    }
    [Fact]
    public void RunnableFixtureUsesUnscriptedAuthorityAndKeepsThreeCapacityQuantitiesSeparate()
    {
        var fixture = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "capacity_commitment.json"));
        Assert.Empty(fixture.Scenario.ScriptedActors); Assert.DoesNotContain(fixture.Scenario.InitialState.Actors, a => a.Scripted);
        var run = Runs.Execute(fixture, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(8, run.World.UsableHousingBerths); Assert.Equal(8, run.World.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(8, run.World.InspectGrowth()!.CommittedPendingBerths);
    }
    [Theory]
    [InlineData("bounds")]
    [InlineData("candidates")]
    [InlineData("materials")]
    [InlineData("authority")]
    [InlineData("capacity")]
    public void LoaderRejectsInvalidGrowthPolicy(string fault)
    {
        var fixture = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "capacity_commitment.json"));
        var data = JsonData.Read<ScenarioData>(fixture.ScenarioJson, "growth"); var g = data.InitialState.Growth!;
        if (fault == "bounds") g.Minimum.X = 4; if (fault == "candidates") g.Candidates = 6; if (fault == "materials") g.KnownMaterials[0].Resource = "unknown";
        if (fault == "authority") data.InitialState.Authority = null; if (fault == "capacity") g.BelievedCompletedBerths = -1;
        var error = Assert.Throws<ScenarioException>(() => new LoadedScenario(JsonData.Write(data), fixture.CatalogJson, "bad-growth")); Assert.Contains("growth", error.Message);
    }
}
