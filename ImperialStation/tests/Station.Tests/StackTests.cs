using Station.Contracts;
using Station.Core;
using Station.Runner;
using Station.Scenarios;
using Xunit;

namespace Station.Tests;

public sealed class StackTests
{
    private static readonly Coord A = new(0, 0, 0), B = new(1, 0, 0);
    private static Item Stack(long id, long quantity = 10, string resource = "metal", LocationKind location = LocationKind.ActorInventory, long owner = 1)
        => new(new(id), resource, quantity, new(location, owner));
    private static World Make(IEnumerable<Item>? items = null, ulong seed = 42, long ticks = 3, bool protectedCard = false)
    {
        var work = new WorkSeed(new[] { new ResourceDefinition("metal"), new ResourceDefinition("wire"), new ResourceDefinition("card") },
            new[] {
                new RecipeDefinition(1, "supply", Array.Empty<MaterialAmount>(), new[] { new MaterialAmount("metal", 5) }, 4),
                new RecipeDefinition(2, "supply", new[] { new MaterialAmount("metal", 2) }, Array.Empty<MaterialAmount>(), 4) },
            new[] { new WorkstationDefinition(1, A, "supply", new WorkAccess(new[] { "worker" })) }, Array.Empty<MachineDefinition>(),
            new[] { new ProjectStashDefinition(1, 42, B) });
        return new World("stack_operations", seed, new WorldConfig(itemActionTicks: ticks), Fixtures.Catalog,
            new[] { new Tile(new(1), "Industrial", A, traversalOverride: 1), new Tile(new(2), "Storage", B, traversalOverride: 1) },
            new[] { new ActorSeed(new(1), "Worker", "worker", A), new ActorSeed(new(2), "Worker", "worker", A) }, items ?? new[] { Stack(1) }, true,
            new InformationSeed(credentials: protectedCard ? new[] { new CredentialSeed(1, new(1), new(1), new(1)) } : null), work);
    }
    private static void Split(World w, long tick = 0, long item = 1, long quantity = 4, long actor = 1, Coord? at = null)
        => w.Submit(tick, new(new(actor), ActionKind.SplitStack, at ?? A, new(item), quantity: quantity));
    private static void Merge(World w, long tick, long keep = 1, long remove = 2, long actor = 1)
        => w.Submit(tick, new(new(actor), ActionKind.MergeStacks, A, new(keep), otherItem: new(remove)));
    private static void Chain(World w)
    {
        Split(w); Merge(w, 3); w.Submit(6, new(new(1), ActionKind.Move, B));
        w.Submit(7, new(new(1), ActionKind.StoreInStash, B, new(1), serviceId: 1));
        w.Submit(10, new(new(1), ActionKind.PickUp, B, new(1)));
    }
    [Fact]
    public void SplitMergeStashAndPickupAreFinitePhysicalOperations()
    {
        var w = Make(); Chain(w); w.AdvanceTo(2);
        Assert.Equal(10, Assert.Single(w.InspectItems()).Quantity); Assert.Empty(w.InspectStackChanges());
        w.AdvanceTo(3); Assert.Equal(new long[] { 6, 4 }, w.InspectItems().Select(i => i.Quantity));
        Assert.All(w.InspectItems(), i => { Assert.Equal(LocationKind.ActorInventory, i.Location.Kind); Assert.Equal(1, i.Location.Owner); });
        w.AdvanceTo(6); Assert.Equal(10, Assert.Single(w.InspectItems()).Quantity); Assert.Equal(1, w.InspectItems()[0].Id.Value);
        w.AdvanceTo(9); Assert.Equal(LocationKind.ActorInventory, w.InspectItems()[0].Location.Kind);
        w.AdvanceTo(10); Assert.Equal(LocationKind.ProjectStash, w.InspectItems()[0].Location.Kind); Assert.Empty(w.InspectActor(new(1)).Inventory);
        w.AdvanceTo(13); Assert.Equal(LocationKind.ActorInventory, w.InspectItems()[0].Location.Kind); Assert.Empty(w.InspectResourceClaims());
        Assert.Equal(2, w.InspectStackChanges().Count); Assert.Empty(w.InspectResourceTransactions());
        Assert.Equal(4, w.InspectStackChanges()[0].Quantity); Assert.Equal(4, w.InspectStackChanges()[1].Quantity); w.AssertAccounting();
        Assert.Contains(w.InspectKnowledge(new(1)), f => f.Key.SubjectId == 2 && f.Key.SubjectKind == SubjectKind.Item && f.Key.Attribute == AttributeKind.Present && f.Value.Equals(FactValue.Boolean(false)));
    }
    [Theory]
    [InlineData(ActionKind.SplitStack)]
    [InlineData(ActionKind.MergeStacks)]
    [InlineData(ActionKind.StoreInStash)]
    public void CancellationReleasesClaimsWithoutChangingStacksOrLocation(ActionKind kind)
    {
        var w = Make(new[] { Stack(1), Stack(2, 5) }); long start = kind == ActionKind.StoreInStash ? 1 : 0;
        if (kind == ActionKind.StoreInStash) w.Submit(0, new(new(1), ActionKind.Move, B));
        w.Submit(start, new(new(1), kind, kind == ActionKind.StoreInStash ? B : A, new(1), serviceId: 1, quantity: 4, otherItem: new(2)));
        w.Submit(start + 1, new(new(1), ActionKind.Cancel, kind == ActionKind.StoreInStash ? B : A)); w.AdvanceTo(5);
        Assert.Equal(new long[] { 10, 5 }, w.InspectItems().Select(i => i.Quantity)); Assert.All(w.InspectItems(), i => Assert.Equal(LocationKind.ActorInventory, i.Location.Kind));
        Assert.Empty(w.InspectResourceClaims()); Assert.Empty(w.InspectStackChanges()); w.AssertAccounting();
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(11)]
    public void InvalidSplitsAreExplainedAndCannotAlterTheSource(long quantity)
    {
        var w = Make(); Split(w, quantity: quantity); w.AdvanceTo(4);
        Assert.Equal(FailureReason.InvalidQuantity, w.InspectActor(new(1)).LastFailure); Assert.Equal(10, Assert.Single(w.InspectItems()).Quantity);
        Assert.Empty(w.InspectResourceClaims()); Assert.Empty(w.InspectStackChanges());
    }
    [Fact]
    public void MergeRequiresMatchingResourceAndOneSharedPhysicalContainer()
    {
        foreach (var second in new[] { Stack(2, resource: "wire"), Stack(2, location: LocationKind.TileContainer), Stack(2, owner: 2) })
        {
            var w = Make(new[] { Stack(1), second }); Merge(w, 0); w.AdvanceTo(3);
            Assert.Equal(2, w.InspectItems().Count); Assert.Empty(w.InspectStackChanges()); Assert.Empty(w.InspectResourceClaims());
            Assert.Contains(w.InspectActor(new(1)).LastFailure, new[] { FailureReason.IncompatibleStack, FailureReason.NotAtRequiredTile }); w.AssertAccounting();
        }
        var same = Make(); Merge(same, 0, remove: 1); same.AdvanceTo(3); Assert.Equal(FailureReason.IncompatibleStack, same.InspectActor(new(1)).LastFailure);
    }
    [Fact]
    public void CredentialIdentityCannotBeSplitOrMergedButCanBePhysicallyStored()
    {
        var w = Make(new[] { Stack(1, 2, "card"), Stack(2, 1, "card") }, protectedCard: true);
        Split(w, quantity: 1); Merge(w, 1); w.AdvanceTo(4);
        Assert.Equal(FailureReason.ProtectedItem, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectStackChanges());
        w.Submit(5, new(new(1), ActionKind.Move, B)); w.Submit(6, new(new(1), ActionKind.StoreInStash, B, new(1), serviceId: 1)); w.AdvanceTo(9);
        Assert.Equal(LocationKind.ProjectStash, w.InspectItems()[0].Location.Kind); Assert.Equal(2, w.InspectItems()[0].Quantity); w.AssertAccounting();
    }
    [Fact]
    public void StashNeedsRealCompletedLocationAndDepositDoesNotWorkRemotely()
    {
        var w = Make(); w.Submit(0, new(new(1), ActionKind.StoreInStash, A, new(1), serviceId: 1)); w.AdvanceTo(3);
        Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(new(1)).LastFailure); Assert.Equal(LocationKind.ActorInventory, w.InspectItems()[0].Location.Kind);
        Assert.Throws<ArgumentException>(() => Make(new[] { Stack(1, location: LocationKind.ProjectStash, owner: 99) }));
        var seeded = Make(new[] { Stack(1, location: LocationKind.ProjectStash) }); seeded.Submit(0, new(new(1), ActionKind.PickUp, A, new(1))); seeded.AdvanceTo(3);
        Assert.Equal(FailureReason.MissingItem, seeded.InspectActor(new(1)).LastFailure);
        seeded.Submit(4, new(new(1), ActionKind.Move, B)); seeded.Submit(5, new(new(1), ActionKind.PickUp, B, new(1))); seeded.AdvanceTo(8);
        Assert.Contains(new Id<ItemTag>(1), seeded.InspectActor(new(1)).Inventory); seeded.AssertAccounting();
    }
    [Fact]
    public void SameTickOverlappingMergesAcquireBothClaimsAtomically()
    {
        var winners = new HashSet<long>();
        for (ulong seed = 0; seed < 64; seed++)
        {
            var w = Make(new[] { Stack(1, location: LocationKind.TileContainer), Stack(2, location: LocationKind.TileContainer), Stack(3, location: LocationKind.TileContainer) }, seed);
            Merge(w, 0, 1, 2, 1); Merge(w, 0, 3, 2, 2); w.AdvanceTo(0);
            Assert.Equal(2, w.InspectResourceClaims().Count); long winner = w.InspectResourceClaims()[0].ActorId; winners.Add(winner);
            Assert.All(w.InspectResourceClaims(), c => Assert.Equal(winner, c.ActorId));
            var copy = World.Restore(w.Serialize()); w.AdvanceTo(3); copy.AdvanceTo(3); Assert.Equal(w.Digest(), copy.Digest());
            Assert.Single(w.InspectStackChanges()); Assert.Equal(30, w.InspectItems().Sum(i => i.Quantity)); Assert.Equal(2, w.InspectItems().Count); w.AssertAccounting();
        }
        Assert.Equal(2, winners.Count);
    }
    [Fact]
    public void SplittingAClaimedLocalStackCannotRaceAPickup()
    {
        var w = Make(new[] { Stack(1, location: LocationKind.TileContainer) }); Split(w, actor: 2);
        w.Submit(1, new(new(1), ActionKind.PickUp, A, new(1))); w.AdvanceTo(1);
        Assert.Equal(FailureReason.ResourceClaimed, w.InspectActor(new(1)).LastFailure);
        Assert.All(w.InspectResourceClaims(), c => Assert.Equal(2, c.ActorId)); w.AdvanceTo(3);
        Assert.All(w.InspectItems(), i => Assert.Equal(LocationKind.TileContainer, i.Location.Kind)); w.AssertAccounting();
    }
    [Fact]
    public void DeclaredSourceAndSinkUseNormalServiceTimingAndAccounting()
    {
        var w = Make(Array.Empty<Item>());
        w.Submit(0, new(new(1), ActionKind.ProduceRecipe, A, serviceId: 1, recipeId: 1));
        w.Submit(4, new(new(1), ActionKind.PickUp, A, new(1)));
        w.Submit(7, new(new(1), ActionKind.ProduceRecipe, A, serviceId: 1, recipeId: 2));
        w.AdvanceTo(3); Assert.Empty(w.InspectItems()); Assert.Empty(w.InspectResourceTransactions());
        w.AdvanceTo(4); Assert.Equal(5, Assert.Single(w.InspectItems()).Quantity); Assert.Equal(LocationKind.ServiceOutput, w.InspectItems()[0].Location.Kind);
        w.AdvanceTo(10); Assert.Equal(5, w.InspectItems()[0].Quantity);
        w.AdvanceTo(11); Assert.Equal(3, w.InspectItems()[0].Quantity); Assert.Equal(new long[] { 5, -2 }, w.InspectResourceTransactions().Select(t => t.Delta));
        w.AssertAccounting(); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        Assert.Throws<ArgumentException>(() => new RecipeDefinition(3, "void", Array.Empty<MaterialAmount>(), Array.Empty<MaterialAmount>()));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CancelledSourceOrSinkCannotCreateOrConsumeResources(long recipe)
    {
        var w = Make(); w.Submit(0, new(new(1), ActionKind.ProduceRecipe, A, serviceId: 1, recipeId: recipe)); w.Submit(2, new(new(1), ActionKind.Cancel, A)); w.AdvanceTo(6);
        Assert.Equal(10, Assert.Single(w.InspectItems()).Quantity); Assert.Empty(w.InspectResourceTransactions()); Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting();
    }
    [Fact]
    public void StackChainContinuationPreservesClaimsIdsQuantitiesAndLedger()
    {
        for (ulong seed = 0; seed < 20; seed++)
        foreach (long tick in new long[] { -1, 0, 2, 3, 5, 7, 9, 10, 12, 13 })
        {
            var w = Make(seed: seed); Chain(w); if (tick >= 0) w.AdvanceTo(tick);
            var bytes = w.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
            w.AdvanceTo(20); copy.AdvanceTo(20); Assert.Equal(w.Digest(), copy.Digest()); w.AssertAccounting(); copy.AssertAccounting();
        }
    }
    [Fact]
    public void LargestValidStackSplitsAndMergesWithoutRoundingOrOverflow()
    {
        var w = Make(new[] { Stack(1, long.MaxValue) }); Split(w, quantity: long.MaxValue - 1); Merge(w, 3); w.AdvanceTo(6);
        Assert.Equal(long.MaxValue, Assert.Single(w.InspectItems()).Quantity); Assert.Equal(2, w.InspectStackChanges().Count); w.AssertAccounting();
    }
    [Fact]
    public void AutonomousOrderBindsStackSeparationToOriginalAttemptAndResumes()
    {
        var canonical = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "work_orders.json"));
        for (ulong seed = 0; seed < 20; seed++)
        {
            var generated = WorkOrderGenerator.Generate(canonical.ScenarioJson, canonical.CatalogJson, seed);
            var data = JsonData.Read<ScenarioData>(generated.ScenarioJson, "stack order"); var initial = data.InitialState;
            initial.Items.RemoveAll(i => i.Resource is "metal" or "wire");
            var stack = initial.Items.Single(i => i.Resource == "correct"); stack.Quantity = 2 + (long)(seed % 8);
            initial.Information!.Facts.Single(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.SubjectId == stack.Id && f.Key.Attribute == AttributeKind.Quantity).Value.Number = stack.Quantity;
            var loaded = new LoadedScenario(JsonData.Write(data), generated.CatalogJson, "stack order"); var run = Runs.Execute(loaded, seed, true);
            Assert.Equal("completed", run.Result.Status); Assert.Equal(0, run.World.ActionsFailed);
            var start = Assert.Single(run.World.Trace, e => e.Type == "ActionStarted" && e.Detail == "SplitStack");
            Assert.Equal(1, start.AttemptId); Assert.Equal(initial.JobSystem!.Jobs[0].Id, start.TaskId);
            Assert.Single(run.World.InspectStackChanges()); Assert.Equal(stack.Quantity - 1, run.World.InspectStackChanges()[0].Quantity);
            var pending = loaded.CreateWorld(); pending.AdvanceTo(start.Tick); var restored = World.Restore(pending.Serialize());
            var continued = Runs.Execute(loaded, seed, false, startingWorld: restored); Assert.Equal(run.Result.Digest, continued.Result.Digest);
        }
    }
    [Fact]
    public void CanonicalResourceAndStackedOrderScenariosRunThroughStrictLoader()
    {
        foreach (string name in new[] { "resource_flow", "stacked_order" })
        {
            var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", name + ".json")); var run = Runs.Execute(loaded, 42, true);
            Assert.Equal("completed", run.Result.Status); Assert.Equal(0, run.World.ActionsFailed);
            Assert.Equal(name == "resource_flow" ? 3 : 1, run.World.InspectStackChanges().Count);
            if (name == "stacked_order") { Assert.Empty(loaded.Scenario.ScriptedActors); Assert.Empty(loaded.Scenario.InitialState.Information!.RepairGoals); }
        }
    }
    [Fact]
    public void GeneratedResourceFlowsConserveQuantitiesAndContinueAcrossOperations()
    {
        var canonical = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "resource_flow.json"));
        var cases = Enumerable.Range(0, 20).Select(i => ResourceFlowGenerator.Generate(canonical.ScenarioJson, canonical.CatalogJson, (ulong)i)).ToArray();
        var results = cases.Select(s => Runs.Execute(s, s.Scenario.InitialState.Seed, true).Result).ToArray(); Assert.All(results, r => Assert.Equal("completed", r.Status));
        for (int i = 0; i < cases.Length; i++)
        {
            var s = cases[i];
            foreach (var command in s.Scenario.ScriptedActors[0].Commands)
            {
                var w = s.CreateWorld(); w.AdvanceTo(command.Tick); var bytes = w.Serialize(); var copy = World.Restore(bytes);
                Assert.Equal(bytes, copy.Serialize()); Assert.Equal(results[i].Digest, Runs.Execute(s, s.Scenario.InitialState.Seed, false, startingWorld: copy).Result.Digest);
            }
        }
        foreach (int concurrency in new[] { 2, 4, 8 })
        {
            var parallel = new RunResult[cases.Length]; Parallel.For(0, cases.Length, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i => parallel[i] = Runs.Execute(cases[i], cases[i].Scenario.InitialState.Seed, false).Result);
            for (int i = 0; i < parallel.Length; i++) { Assert.Equal(results[i].Digest, parallel[i].Digest); Assert.Equal(results[i].Metrics, parallel[i].Metrics); }
        }
    }
}
