using Station.Contracts;
using Station.Core;
using Station.Runner;
using System.Text.Json.Nodes;
using Xunit;

namespace Station.Tests;

public sealed class WorkTests
{
    private static readonly WorkAccess Worker = new(new[] { "worker" }, "tool");
    private static WorkSeed Definitions(int capacity = 1, long staff = 0) => new(
        new[] { new ResourceDefinition("metal"), new ResourceDefinition("wire"), new ResourceDefinition("tool"), new ResourceDefinition("correct", "cartridge", 2), new ResourceDefinition("wrong", "cartridge", 1), new ResourceDefinition("incompatible", "large") },
        new[] { new RecipeDefinition(1, "fabricate", new[] { new MaterialAmount("metal", 2), new MaterialAmount("wire", 1) }, new[] { new MaterialAmount("correct", 1) }, 5) },
        new[] { new WorkstationDefinition(1, Fixtures.A, "fabricate", new WorkAccess(new[] { "worker" }, "tool", new(staff)), capacity) },
        new[] { new MachineDefinition(1, Fixtures.B, "cartridge", "correct", Worker, requiredRating: 2, installTicks: 3, inspectTicks: 2, testTicks: 2) });
    private static Item Held(long id, string resource, long quantity = 1, long owner = 1) => new(new(id), resource, quantity, new ItemLocation(LocationKind.ActorInventory, owner));
    private static Item[] Materials => new[] { Held(1, "metal", 3), Held(2, "wire"), Held(3, "tool") };
    private static World Make(IEnumerable<Item>? items = null, ulong seed = 42, int population = 2, WorkSeed? definitions = null, bool information = true, bool trace = true, long itemTicks = 1)
        => new("work", seed, new WorldConfig(itemActionTicks: itemTicks), Fixtures.Catalog,
            new[] { new Tile(new(1), "Industrial", Fixtures.A, traversalOverride: 2), new Tile(new(2), "Power", Fixtures.B, traversalOverride: 2) },
            Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "Worker" + i, "worker", Fixtures.A)), items ?? Materials,
            trace, information ? new InformationSeed() : null, definitions ?? Definitions());
    private static void Produce(World w, long tick = 0, long actor = 1) => w.Submit(tick, new(new(actor), ActionKind.ProduceRecipe, Fixtures.A, serviceId: 1, recipeId: 1));
    private static void FullChain(World w)
    {
        Produce(w);
        w.Submit(5, new(new(1), ActionKind.PickUp, Fixtures.A, new(4)));
        w.Submit(6, new(new(1), ActionKind.Move, Fixtures.B));
        w.Submit(8, new(new(1), ActionKind.Install, Fixtures.B, new(4), serviceId: 1));
        w.Submit(11, new(new(1), ActionKind.InspectMachine, Fixtures.B, serviceId: 1));
    }

    [Fact]
    public void RecipeConsumesOnlyAtCompletionAndOutputRequiresRealPickup()
    {
        var w = Make(); FullChain(w); w.AdvanceTo(4);
        Assert.Equal(3, w.InspectItems().Single(i => i.Id.Value == 1).Quantity);
        Assert.Empty(w.InspectResourceTransactions()); Assert.Equal(4, w.InspectResourceClaims().Count);
        w.AdvanceTo(5);
        Assert.Equal(1, w.InspectItems().Single(i => i.Id.Value == 1).Quantity);
        Assert.DoesNotContain(w.InspectItems(), i => i.Id.Value == 2);
        var output = w.InspectItems().Single(i => i.Id.Value == 4);
        Assert.Equal(LocationKind.ServiceOutput, output.Location.Kind); Assert.Equal(1, output.Location.Owner);
        Assert.DoesNotContain(new Id<ItemTag>(4), w.InspectActor(new(1)).Inventory);
        Assert.Equal(3, w.InspectResourceTransactions().Count); w.AssertAccounting();
        w.AdvanceTo(6); Assert.Contains(new Id<ItemTag>(4), w.InspectActor(new(1)).Inventory);
        w.AdvanceTo(10); Assert.False(Assert.Single(w.InspectMachines()).Operational);
        w.AdvanceTo(13); Assert.True(Assert.Single(w.InspectMachines()).Operational);
        Assert.Equal(LocationKind.MachineSlot, w.InspectItems().Single(i => i.Id.Value == 4).Location.Kind);
        Assert.Empty(w.InspectResourceClaims()); Assert.Equal(1, w.InspectItems().Single(i => i.Resource == "tool").Quantity); w.AssertAccounting();
    }
    [Fact]
    public void FittingWrongCartridgeInstallsAndReplacementPreservesIt()
    {
        var w = Make(new[] { Held(1, "wrong"), Held(2, "correct"), Held(3, "tool") });
        w.Submit(0, new(new(1), ActionKind.Move, Fixtures.B));
        w.Submit(2, new(new(1), ActionKind.Install, Fixtures.B, new(1), serviceId: 1));
        w.Submit(5, new(new(1), ActionKind.FunctionalTest, Fixtures.B, serviceId: 1));
        w.Submit(7, new(new(1), ActionKind.Install, Fixtures.B, new(2), serviceId: 1));
        w.AdvanceTo(5); Assert.Equal(1, Assert.Single(w.InspectMachines()).InstalledItemId); Assert.False(w.InspectMachines()[0].Operational);
        w.AdvanceTo(7); Assert.Equal(FailureReason.None, w.InspectActor(new(1)).LastFailure);
        w.AdvanceTo(10); Assert.True(w.InspectMachines()[0].Operational);
        var wrong = w.InspectItems().Single(i => i.Id.Value == 1);
        Assert.Equal(LocationKind.TileContainer, wrong.Location.Kind); Assert.Equal(2, wrong.Location.Owner);
        Assert.Empty(w.InspectResourceTransactions()); w.AssertAccounting();
    }
    [Fact]
    public void WrongShapeAndMissingToolsFailWithoutMovingOrConsumingParts()
    {
        var w = Make(new[] { Held(1, "incompatible"), Held(2, "correct"), Held(3, "tool") });
        w.Submit(0, new(new(1), ActionKind.Move, Fixtures.B));
        w.Submit(2, new(new(1), ActionKind.Install, Fixtures.B, new(1), serviceId: 1));
        w.Submit(3, new(new(1), ActionKind.PutDown, Fixtures.B, new(3)));
        w.Submit(4, new(new(1), ActionKind.Install, Fixtures.B, new(2), serviceId: 1));
        w.AdvanceTo(2); Assert.Equal(FailureReason.IncompatiblePart, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectResourceClaims());
        w.AdvanceTo(4); Assert.Equal(FailureReason.MissingTool, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectResourceClaims());
        Assert.Equal(0, w.InspectMachines()[0].InstalledItemId); w.AssertAccounting();
    }
    [Fact]
    public void RecipeCancellationReleasesAllClaimsAndRetainsCarriedInputs()
    {
        var w = Make(); Produce(w); w.Submit(2, new(new(1), ActionKind.Cancel, Fixtures.A)); Produce(w, 3);
        w.AdvanceTo(2); Assert.Empty(w.InspectResourceClaims()); Assert.Empty(w.InspectResourceTransactions());
        Assert.Equal(ActionStatus.Cancelled, w.InspectActor(new(1)).LastStatus); Assert.Equal(3, w.InspectActor(new(1)).Inventory.Count);
        var restored = World.Restore(w.Serialize()); w.AdvanceTo(8); restored.AdvanceTo(8);
        Assert.Equal(w.Digest(), restored.Digest()); Assert.Equal(3, w.InspectResourceTransactions().Count); w.AssertAccounting();
    }
    [Fact]
    public void UnavailableWorkstationAtCompletionFailsAtomically()
    {
        var w = Make(); Produce(w); w.ScheduleWorkServiceChange(5, 1, false, false); w.AdvanceTo(5);
        Assert.Equal(FailureReason.RequiredServiceNotPresent, w.InspectActor(new(1)).LastFailure);
        Assert.Empty(w.InspectResourceTransactions()); Assert.Empty(w.InspectResourceClaims()); Assert.Equal(3, w.InspectItems().Count);
        Assert.Equal(3, w.InspectItems().Single(i => i.Id.Value == 1).Quantity); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Fact]
    public void InsufficientInputsCannotPartiallyClaimOrConsume()
    {
        var w = Make(new[] { Held(1, "metal", 3), Held(3, "tool") }); Produce(w); w.AdvanceTo(5);
        Assert.Equal(FailureReason.InsufficientMaterials, w.InspectActor(new(1)).LastFailure);
        Assert.Empty(w.InspectResourceClaims()); Assert.Empty(w.InspectResourceTransactions()); w.AssertAccounting();
    }
    [Fact]
    public void WorkCapacityIsAtomicAndPriorTickClaimsHavePriority()
    {
        var inventory = Materials.Concat(new[] { Held(4, "metal", 2, 2), Held(5, "wire", owner: 2), Held(6, "tool", owner: 2) });
        var w = Make(inventory); Produce(w, 0, 2); Produce(w, 1, 1); w.AdvanceTo(1);
        Assert.Equal(FailureReason.ServiceBusy, w.InspectActor(new(1)).LastFailure);
        Assert.All(w.InspectResourceClaims(), c => Assert.Equal(2, c.ActorId));
        w.Submit(2, new(new(2), ActionKind.Cancel, Fixtures.A)); Produce(w, 3, 1); w.AdvanceTo(8);
        Assert.Equal(3, w.InspectResourceTransactions().Count); Assert.All(w.InspectResourceTransactions(), t => Assert.Equal(1, t.ActorId)); w.AssertAccounting();
        var parallel = Make(inventory, definitions: Definitions(capacity: 2)); Produce(parallel, actor: 1); Produce(parallel, actor: 2); parallel.AdvanceTo(5);
        Assert.Equal(6, parallel.InspectResourceTransactions().Count); Assert.Equal(2, parallel.InspectItems().Count(i => i.Resource == "correct")); parallel.AssertAccounting();
    }
    [Fact]
    public void StaffPresenceAndActivityAreRealAndReservedForTheOperation()
    {
        var w = Make(definitions: Definitions(staff: 2)); Produce(w);
        w.Submit(1, new(new(2), ActionKind.Move, Fixtures.B)); w.AdvanceTo(1);
        Assert.Equal(FailureReason.ActorBusy, w.InspectActor(new(2)).LastFailure); Assert.False(w.InspectActor(new(2)).InTransit);
        w.Submit(5, new(new(2), ActionKind.Move, Fixtures.B)); w.AdvanceTo(7); Assert.Equal(Fixtures.B, w.InspectActor(new(2)).Position);
        var absent = Make(definitions: Definitions(staff: 2)); absent.Submit(0, new(new(2), ActionKind.Move, Fixtures.B)); Produce(absent, 1); absent.AdvanceTo(1);
        Assert.Equal(FailureReason.StaffUnavailable, absent.InspectActor(new(1)).LastFailure); Assert.Empty(absent.InspectResourceClaims());
    }
    [Fact]
    public void RequiredSpecificationIsInspectionOnlyWhileStatusIsPassive()
    {
        var w = Make(); w.Submit(0, new(new(1), ActionKind.Move, Fixtures.B));
        w.Submit(2, new(new(1), ActionKind.InspectMachine, Fixtures.B, serviceId: 1)); w.AdvanceTo(3);
        Assert.Contains(w.InspectKnowledge(new(1)), f => f.Key.SubjectKind == SubjectKind.Machine && f.Key.Attribute == AttributeKind.Operational);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.RequiredResource);
        Assert.DoesNotContain(w.InspectKnowledge(new(2)), f => f.Key.SubjectKind == SubjectKind.Machine);
        w.AdvanceTo(4);
        Assert.Equal("correct", w.InspectKnowledge(new(1)).Single(f => f.Key.Attribute == AttributeKind.RequiredResource).Value.Text);
        Assert.DoesNotContain(w.InspectKnowledge(new(2)), f => f.Key.SubjectKind == SubjectKind.Machine);
    }
    [Fact]
    public void InspectionOfAnItemConsumedWhileReadingItReportsAbsence()
    {
        var w = Make(); Produce(w); w.Submit(1, new(new(2), ActionKind.Inspect, Fixtures.A, new(2))); w.AdvanceTo(101);
        Assert.Equal(FactValue.Absent(Fixtures.A), w.InspectKnowledge(new(2)).Single(f => f.Key.Equals(new FactKey(SubjectKind.Item, 2, AttributeKind.Location))).Value);
        Assert.Null(w.RuntimeError);
    }
    [Fact]
    public void ProductionAndInstallSnapshotsContinueAcrossEveryActionBoundary()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var original = Make(seed: seed); FullChain(original); original.AdvanceTo(20); string expected = original.Digest();
            foreach (long tick in new long[] { 0, 4, 5, 8, 11 })
            {
                var w = Make(seed: seed); FullChain(w); w.AdvanceTo(tick); byte[] checkpoint = w.Serialize();
                var restored = World.Restore(checkpoint, true); Assert.Equal(checkpoint, restored.Serialize());
                restored.AdvanceTo(20); Assert.Equal(expected, restored.Digest()); restored.AssertAccounting();
            }
        }
    }
    [Fact]
    public void ControlledArbitrationDrawsCoverEveryPermutationExactlyOnce()
    {
        var permutations = new HashSet<string>();
        for (int first = 0; first < 3; first++) for (int second = 0; second < 2; second++)
        {
            var draws = new Queue<int>(new[] { first, second }); var order = new[] { 1, 2, 3 };
            ClaimArbitration.Permute(order, bound => { int n = draws.Dequeue(); Assert.InRange(n, 0, bound - 1); return n; });
            Assert.Equal(new[] { 1, 2, 3 }, order.OrderBy(x => x)); Assert.True(permutations.Add(string.Join(",", order)));
        }
        Assert.Equal(6, permutations.Count);
    }
    [Fact]
    public void ExclusiveItemCompetitionReportsFairnessFor64ActorsAcross1000Seeds()
    {
        var wins = new int[64];
        for (ulong seed = 0; seed < 1000; seed++)
        {
            var w = Make(new[] { new Item(new(1), "correct", 1, new ItemLocation(LocationKind.TileContainer, 1)) }, seed, 64, information: false);
            for (long actor = 1; actor <= 64; actor++) w.Submit(0, new(new(actor), ActionKind.PickUp, Fixtures.A, new(1)));
            var replay = World.Restore(w.Serialize()); w.AdvanceTo(0); Assert.Single(w.InspectResourceClaims());
            long[] order = w.Trace.Where(e => e.Type == "ActionRequested").Select(e => e.ActorId).ToArray();
            Assert.Equal(Enumerable.Range(1, 64).Select(i => (long)i), order.OrderBy(i => i));
            w.AdvanceTo(1); replay.AdvanceTo(1); Assert.Equal(w.Digest(), replay.Digest());
            var winner = Assert.Single(w.InspectActors(), a => a.Inventory.Count == 1); wins[winner.Id.Value - 1]++;
            Assert.Equal(63, w.InspectActors().Count(a => a.LastFailure == FailureReason.ResourceClaimed));
            Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting();
        }
        Assert.True(wins[0] > 0 && wins[63] > 0);
        double expected = 1000.0 / 64, chiSquare = wins.Sum(n => (n - expected) * (n - expected) / expected);
        var dir = Path.Combine(Fixtures.Root, "artifacts", "claim-fairness"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "result.json"), JsonData.Write(new { seedStart = 0, seedCount = 1000, actors = 64, wins, chiSquare, degreesOfFreedom = 63,
            pValue = GammaQ(31.5, chiSquare / 2), note = "Diagnostic only; no statistical cutoff is a correctness gate." }));
    }
    private static double GammaQ(double a, double x)
    {
        // log Gamma(31.5) from Gamma(0.5)=sqrt(pi); deterministic diagnostic arithmetic.
        double logGamma = Math.Log(Math.PI) / 2;
        for (double n = 0.5; n < a; n++) logGamma += Math.Log(n);
        double factor = Math.Exp(-x + a * Math.Log(x) - logGamma);
        if (x < a + 1)
        {
            double sum = 1 / a, term = sum;
            for (int n = 1; n < 10000; n++) { term *= x / (a + n); sum += term; if (Math.Abs(term) < Math.Abs(sum) * 1e-14) break; }
            return 1 - sum * factor;
        }
        double b = x + 1 - a, c = 1e300, d = 1 / b, h = d;
        for (int n = 1; n < 10000; n++)
        {
            double an = -n * (n - a); b += 2; d = an * d + b; if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = b + an / c; if (Math.Abs(c) < 1e-300) c = 1e-300; d = 1 / d; double delta = d * c; h *= delta;
            if (Math.Abs(delta - 1) < 1e-14) break;
        }
        return factor * h;
    }

    [Fact]
    public void WorkScenarioLoadsRunsAndReplaysWithPhysicalAssertions()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "work_actions.json"));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status);
        Assert.Equal(7, run.World.ActionsCompleted); Assert.Equal(0, run.World.ActionsFailed);
        Assert.True(Assert.Single(run.World.InspectMachines()).Operational);
        var restored = World.Restore(loaded.CreateWorld().Serialize()); var replay = Runs.Execute(loaded, 42, false, startingWorld: restored);
        Assert.Equal(run.Result.Digest, replay.Result.Digest); Assert.Equal("completed", replay.Result.Status);
        foreach (int concurrency in new[] { 2, 4, 8 })
        {
            var results = new string[24]; Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i => results[i] = Runs.Execute(loaded, (ulong)i, false).Result.Digest);
            for (int i = 0; i < results.Length; i++) Assert.Equal(Runs.Execute(loaded, (ulong)i, true).Result.Digest, results[i]);
        }
    }
    [Fact]
    public void WorkDefinitionsRejectInvalidRecipeResourcesAndDuplicateSlots()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "work_actions.json"));
        var catalog = JsonNode.Parse(loaded.CatalogJson)!; catalog["recipes"]![0]!["inputs"]![0]!["resource"] = "missing";
        Assert.Throws<ScenarioException>(() => new LoadedScenario(loaded.ScenarioJson, catalog.ToJsonString(), "invalid"));
        Assert.Throws<ArgumentException>(() => Make(new[] {
            new Item(new(1), "wrong", 1, new ItemLocation(LocationKind.MachineSlot, 1)),
            new Item(new(2), "correct", 1, new ItemLocation(LocationKind.MachineSlot, 1)) }));
    }
    [Fact]
    public void RecipeCannotOverflowAccountingAfterConsumingItsInputs()
    {
        var work = new WorkSeed(Definitions().Resources,
            new[] { new RecipeDefinition(1, "fabricate", new[] { new MaterialAmount("metal", 1) }, new[] { new MaterialAmount("wire", long.MaxValue) }, 1) },
            Definitions().Workstations, Definitions().Machines);
        var w = Make(definitions: work); Produce(w);
        Assert.Throws<OverflowException>(() => w.AdvanceTo(1));
        Assert.NotNull(w.RuntimeError); Assert.Equal(3, w.InspectItems().Single(i => i.Resource == "metal").Quantity);
        Assert.Empty(w.InspectResourceTransactions()); w.AssertAccounting();
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }
    [Fact]
    public void CompetingPickupCancellationLetsLaterActorAcquireTheSamePhysicalItem()
    {
        var w = Make(new[] { new Item(new(1), "correct", 1, new ItemLocation(LocationKind.TileContainer, 1)) }, information: false);
        w.Submit(0, new(new(1), ActionKind.PickUp, Fixtures.A, new(1)));
        w.Submit(0, new(new(1), ActionKind.Cancel, Fixtures.A));
        w.Submit(0, new(new(2), ActionKind.PickUp, Fixtures.A, new(1)));
        // The permutation may put actor 2 first, but cancellation must only release actor 1's claim.
        w.AdvanceTo(0);
        if (w.InspectActor(new(2)).Action == null) w.Submit(1, new(new(2), ActionKind.PickUp, Fixtures.A, new(1)));
        w.AdvanceTo(2); Assert.Contains(new Id<ItemTag>(1), w.InspectActor(new(2)).Inventory);
        Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting();
    }
    [Fact]
    public void RemoteItemClaimsCannotBeDiscoveredThroughLocalPickupFailure()
    {
        var w = Make(new[] { new Item(new(1), "correct", 1, new ItemLocation(LocationKind.TileContainer, 2)) }, itemTicks: 3);
        w.Submit(0, new(new(2), ActionKind.Move, Fixtures.B));
        w.Submit(2, new(new(2), ActionKind.PickUp, Fixtures.B, new(1))); w.AdvanceTo(2);
        // Actor 1 stays at A while actor 2 owns an exclusive claim at B.
        w.Submit(3, new(new(1), ActionKind.PickUp, Fixtures.A, new(1))); w.AdvanceTo(3);
        Assert.Equal(2, Assert.Single(w.InspectResourceClaims()).ActorId);
        Assert.Equal(FailureReason.MissingItem, w.InspectActor(new(1)).LastFailure);
        Assert.Equal(FactValue.Absent(Fixtures.A), w.InspectKnowledge(new(1)).Single(f => f.Key.Equals(new FactKey(SubjectKind.Item, 1, AttributeKind.Location))).Value);
    }
    [Fact]
    public void CompletionReportChangesBeliefWithoutRepairingMachineOrConsumingMaterials()
    {
        var definitions = Definitions();
        var work = new WorkSeed(definitions.Resources.Concat(new[] { new ResourceDefinition("card") }), definitions.Recipes, definitions.Workstations, definitions.Machines);
        var info = new InformationSeed(new InformationConfig(loginTicks: 1, postTicks: 1, latencyTicks: 1),
            new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }) },
            new[] { new CredentialSeed(1, new(1), new(1), new(4)) });
        var w = new World("false-repair", 42, new WorldConfig(), Fixtures.Catalog,
            new[] { new Tile(new(1), "Industrial", Fixtures.A), new Tile(new(2), "Power", Fixtures.B) },
            new[] { new ActorSeed(new(1), "Reporter", "worker", Fixtures.A) }, Materials.Concat(new[] { Held(4, "card") }), true, info, work);
        var key = new FactKey(SubjectKind.Machine, 1, AttributeKind.Operational);
        w.Submit(0, new(new(1), ActionKind.Login, Fixtures.A, serviceId: 1));
        w.Submit(1, new(new(1), ActionKind.PostMessage, Fixtures.A, serviceId: 1,
            message: new MessageDraft(MessageKind.WorkCompleted, RecipientKind.Authority, default, key, FactValue.Boolean(true), 0, taskId: 7, attemptId: 9)));
        w.AdvanceTo(3);
        Assert.True(Assert.Single(w.InspectBeliefs()).Value.Equals(FactValue.Boolean(true)));
        Assert.False(Assert.Single(w.InspectMachines()).Operational); Assert.Empty(w.InspectResourceTransactions());
        Assert.Equal(3, w.InspectItems().Single(i => i.Resource == "metal").Quantity); w.AssertAccounting();
    }
}
