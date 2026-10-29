using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class GuardPreparationTests
{
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "guard_preparation.json")))!.AsObject(); change?.Invoke(j);
        if (change != null) j["assertions"] = new JsonArray();
        return new(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "guard-preparation-test");
    }
    private static LocalGuardOrderSnapshot Local(World w, long actor = 3) => Assert.Single(w.InspectLocalGuardOrders(new(actor)));
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    private static World HoldingPart()
    {
        var w = Fixtures.Line(); w.Submit(0, new(Fixtures.Actor, ActionKind.Move, Fixtures.C)); w.Submit(13, new(Fixtures.Actor, ActionKind.PickUp, Fixtures.C, new(1))); w.AdvanceTo(14); return w;
    }

    [Fact]
    public void GuardsPhysicallyCollectEquipAndTravelWithoutChangingAuthorityKnowledge()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(134); Assert.Empty(w.InspectEquipment());
        Assert.DoesNotContain(new Id<ItemTag>(21), w.ActorView(new(3)).Inventory);
        w.AdvanceTo(138); Assert.Contains(new Id<ItemTag>(21), w.ActorView(new(3)).Inventory); Assert.Equal(ActionKind.Equip, w.InspectActor(new(3)).Action);
        Assert.Equal(188, w.InspectActor(new(3)).ActionCompletion); Assert.Equal(0, w.ActorView(new(3)).EquippedItem.Value); RoundTrip(w);
        w.AdvanceTo(187); Assert.Equal(0, w.ActorView(new(3)).EquippedItem.Value);
        w.AdvanceTo(188); Assert.Equal(21, w.ActorView(new(3)).EquippedItem.Value); Assert.True(w.InspectActor(new(3)).InTransit); RoundTrip(w);
        w.AdvanceTo(234);
        foreach (long id in new long[] { 3, 4 }) { Assert.Equal(GuardOrderStatus.AtRendezvous, Local(w, id).Status); Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(id)).CurrentTile); }
        Assert.Equal(EscortDispatchStatus.TeamForming, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Empty(w.InspectResourceTransactions()); RoundTrip(w);
        var run = Runs.Execute(Load(), 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(0, run.World.ActionsFailed);
    }

    [Fact]
    public void SharedEquipActionIsFiniteAndPreservesInventoryAccounting()
    {
        var w = HoldingPart(); w.Submit(15, new(Fixtures.Actor, ActionKind.Equip, Fixtures.C, new(1))); w.AdvanceTo(64);
        Assert.Equal(0, w.ActorView(Fixtures.Actor).EquippedItem.Value); Assert.Equal(ActionKind.Equip, w.InspectActor(Fixtures.Actor).Action); RoundTrip(w);
        w.AdvanceTo(65); Assert.Equal(1, w.ActorView(Fixtures.Actor).EquippedItem.Value); Assert.Equal(3, Assert.Single(w.InspectItems()).Quantity); Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting(); RoundTrip(w);
        w.Submit(66, new(Fixtures.Actor, ActionKind.PutDown, Fixtures.C, new(1))); w.AdvanceTo(67);
        Assert.Equal(0, w.ActorView(Fixtures.Actor).EquippedItem.Value); Assert.Empty(w.InspectEquipment()); RoundTrip(w);
    }

    [Fact]
    public void CancelledEquipCannotProduceEquippedStateOrLeakClaims()
    {
        var w = HoldingPart(); w.Submit(15, new(Fixtures.Actor, ActionKind.Equip, Fixtures.C, new(1))); w.Submit(16, new(Fixtures.Actor, ActionKind.Cancel, Fixtures.C)); w.AdvanceTo(100);
        Assert.Empty(w.InspectEquipment()); Assert.Empty(w.InspectResourceClaims()); Assert.Contains(new Id<ItemTag>(1), w.ActorView(Fixtures.Actor).Inventory); RoundTrip(w);
    }

    [Fact]
    public void UnheldRemoteAndCredentialItemsCannotBeEquipped()
    {
        var w = Fixtures.Line(); w.Submit(0, new(Fixtures.Actor, ActionKind.Equip, Fixtures.C, new(1))); w.AdvanceTo(0); Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(Fixtures.Actor).LastFailure);
        w.Submit(1, new(Fixtures.Actor, ActionKind.Equip, Fixtures.A, new(1))); w.AdvanceTo(1); Assert.Equal(FailureReason.NotHeld, w.InspectActor(Fixtures.Actor).LastFailure); Assert.Empty(w.InspectEquipment());
        var guard = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 200).CreateWorld(42); guard.AdvanceTo(734); guard.Submit(735, new(new(1), ActionKind.Equip, new(1, 0, 0), new(5))); guard.AdvanceTo(735);
        Assert.Equal(FailureReason.ProtectedItem, guard.InspectActor(new(1)).LastFailure); Assert.Equal(0, guard.ActorView(new(1)).EquippedItem.Value); RoundTrip(guard);
    }

    [Fact]
    public void ConsumingTheEquippedStackClearsPhysicalEquipment()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 200).CreateWorld(42); w.AdvanceTo(734);
        w.Submit(735, new(new(1), ActionKind.Equip, new(1, 0, 0), new(2))); w.AdvanceTo(785); Assert.Equal(2, w.ActorView(new(1)).EquippedItem.Value);
        w.Submit(786, new(new(1), ActionKind.Move, new(0, 0, 0))); w.Submit(787, new(new(1), ActionKind.ProduceRecipe, new(0, 0, 0), serviceId: 1, recipeId: 1)); w.AdvanceTo(794);
        Assert.DoesNotContain(w.InspectItems(), i => i.Id.Value == 2); Assert.Equal(0, w.ActorView(new(1)).EquippedItem.Value); w.AssertAccounting(); RoundTrip(w);
    }

    [Fact]
    public void MergingAwayEquippedItemIdentityClearsEquipment()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 200).CreateWorld(42); w.AdvanceTo(734);
        w.Submit(735, new(new(1), ActionKind.SplitStack, new(1, 0, 0), new(1), quantity: 1)); w.AdvanceTo(736); long split = w.InspectStackChanges().Last().DestinationItem;
        w.Submit(737, new(new(1), ActionKind.Equip, new(1, 0, 0), new(split))); w.AdvanceTo(787); Assert.Equal(split, w.ActorView(new(1)).EquippedItem.Value);
        w.Submit(788, new(new(1), ActionKind.MergeStacks, new(1, 0, 0), new(1), otherItem: new(split))); w.AdvanceTo(789);
        Assert.Equal(0, w.ActorView(new(1)).EquippedItem.Value); Assert.DoesNotContain(w.InspectItems(), i => i.Id.Value == split); w.AssertAccounting(); RoundTrip(w);
    }

    [Fact]
    public void NoKnownEquipmentLeavesExplainedPendingInsteadOfReadingRemoteStock()
    {
        var w = Load(j => {
            var facts = j["initialState"]!["information"]!["facts"]!.AsArray();
            foreach (var f in facts.Where(f => f!["actorId"]!.GetValue<int>() >= 3).ToArray()) facts.Remove(f);
        }).CreateWorld(42); w.AdvanceTo(734);
        Assert.Empty(w.InspectEquipment()); Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w).Status); Assert.Equal("NoKnownAvailableEquipmentRoute", Local(w).Reason);
        Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(3)).CurrentTile); Assert.True(w.PlannerRequests < 1000); RoundTrip(w);
    }

    [Fact]
    public void StaleWarehouseKnowledgeCannotTakeEquipmentFromAnotherActorsInventory()
    {
        var w = Load(j => { var item = j["initialState"]!["items"]!.AsArray().Single(i => i!["id"]!.GetValue<int>() == 21)!; item["locationKind"] = "ActorInventory"; item["owner"] = 1; }).CreateWorld(42, true);
        w.AdvanceTo(734); Assert.Contains(new Id<ItemTag>(21), w.ActorView(new(1)).Inventory); Assert.Single(w.InspectEquipment());
        Assert.Contains(w.Trace, e => e.Type == "ActionFailed" && e.Detail == "MissingItem"); Assert.Contains(new long[] { 3, 4 }, id => Local(w, id).Status == GuardOrderStatus.EquipmentPending); RoundTrip(w);
    }

    [Fact]
    public void GuardsContinueOnReadCopiesWhenTerminalsGoOffline()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(140, 3, false, false); w.ScheduleTerminalChange(140, 4, false, false); w.AdvanceTo(234);
        Assert.Equal(GuardOrderStatus.AtRendezvous, Local(w, 3).Status); Assert.Equal(GuardOrderStatus.AtRendezvous, Local(w, 4).Status); Assert.Equal(2, w.InspectEquipment().Count);
        Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }

    [Fact]
    public void ExpiryDuringFiniteEquipRetainsThePhysicalItemButCannotMarkReady()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 50).CreateWorld(42); w.AdvanceTo(163);
        Assert.Equal(GuardOrderStatus.Expired, Local(w).Status); Assert.Equal(ActionKind.Equip, w.InspectActor(new(3)).Action); Assert.Empty(w.InspectEquipment()); RoundTrip(w);
        w.AdvanceTo(234); Assert.Equal(21, w.ActorView(new(3)).EquippedItem.Value); Assert.Equal(GuardOrderStatus.Expired, Local(w).Status); Assert.Equal(-1, Local(w).ReadyTick); RoundTrip(w);
    }

    [Fact]
    public void GuardActionTagRequiresThatActorsOwnReadAndAcknowledgedCopy()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); var orders = Assert.Single(w.InspectEscortDispatches()).OrderMessageIds;
        w.Submit(115, new(new(3), ActionKind.Move, new(2, 0, 0), guardOrderId: orders[0])); w.AdvanceTo(115);
        Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(3)).LastFailure); Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(3)).CurrentTile);
        w.AdvanceTo(234); w.Submit(235, new(new(1), ActionKind.Move, new(0, 0, 0), guardOrderId: orders[0])); w.AdvanceTo(235);
        Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(1)).LastFailure); RoundTrip(w);
    }

    [Fact]
    public void KnownExpiryStopsPreparationTravelAtTheFrozenEdgeBoundary()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 25).CreateWorld(42, true);
        w.ScheduleTraversalChange(134, new(1, 0, 0), 20); w.AdvanceTo(138);
        Assert.Equal(GuardOrderStatus.Expired, Local(w).Status); Assert.True(w.InspectActor(new(3)).InTransit); Assert.Equal(146, w.InspectActor(new(3)).Arrival); RoundTrip(w);
        w.AdvanceTo(146); Assert.False(w.InspectActor(new(3)).InTransit); Assert.Equal(new Coord(1, 0, 0), w.ActorView(new(3)).CurrentTile); Assert.Empty(w.InspectEquipment());
        Assert.DoesNotContain(w.Trace, e => e.Type == "MovementStarted" && e.ActorId == 3 && e.Tick == 212 && e.Target == new Coord(2, 0, 0)); RoundTrip(w);
    }

    [Fact]
    public void LosingEquipmentInvalidatesReadinessAndRequiresAnotherFiniteEquip()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(234); Assert.Equal(GuardOrderStatus.AtRendezvous, Local(w).Status);
        w.Submit(235, new(new(3), ActionKind.Cancel, new(0, 0, 0)));
        w.Submit(236, new(new(3), ActionKind.PutDown, new(0, 0, 0), new(21))); w.AdvanceTo(237);
        Assert.Equal(0, w.ActorView(new(3)).EquippedItem.Value); Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w).Status); RoundTrip(w);
        w.AdvanceTo(288); Assert.Equal(21, w.ActorView(new(3)).EquippedItem.Value); Assert.Equal(GuardOrderStatus.AtRendezvous, Local(w).Status); Assert.True(Local(w).ReadyTick > 236); RoundTrip(w);
    }

    [Fact]
    public void DepartureImmediatelyInvalidatesRendezvousReadiness()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(234);
        w.Submit(235, new(new(3), ActionKind.Cancel, new(0, 0, 0)));
        w.Submit(236, new(new(3), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(236);
        Assert.True(w.InspectActor(new(3)).InTransit); Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w).Status); RoundTrip(w);
        w.AdvanceTo(244); Assert.Equal(GuardOrderStatus.AtRendezvous, Local(w).Status); Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(3)).CurrentTile); RoundTrip(w);
    }

    [Fact]
    public void CheckpointContinuationsCoverPickupEquipTravelExpiryAndReady()
    {
        foreach (int seed in Enumerable.Range(0, 8)) foreach (int tick in new[] { 113, 134, 135, 136, 137, 138, 141, 163, 187, 188, 189, 190, 191, 193, 234 })
        {
            var w = Load(j => { j["initialState"]!["detention"]!["escort"]!["equipTicks"] = 40 + seed * 3; j["initialState"]!["detention"]!["orderTicks"] = seed % 2 == 0 ? 50 : 2000; }).CreateWorld((ulong)seed, true);
            w.AdvanceTo(tick); var bytes = w.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
            w.AdvanceTo(734); copy.AdvanceTo(734); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void CorruptEquippedOwnershipCannotBeRestored()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(234); var e = w.InspectEquipment().First(); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) { writer.Write(e.ActorId); writer.Write(e.ItemId); writer.Write(e.EquippedTick); writer.Write(e.ActionId); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var edit in new[] { (Offset: 0, Value: 1L), (Offset: 8, Value: 13L), (Offset: 16, Value: 301L), (Offset: 24, Value: 0L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(edit.Value).CopyTo(bad, start + edit.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }

    [Fact]
    public void ConcurrentChunkedPreparationHasTheSameDigest()
    {
        string Run(int seed, bool chunked)
        {
            var w = Load(j => j["initialState"]!["detention"]!["escort"]!["equipTicks"] = 1 + seed * 7).CreateWorld((ulong)seed);
            if (chunked) for (int tick = 0; tick < 800; tick += 11) { w.AdvanceTo(tick); _ = w.InspectEquipment(); _ = w.ActorView(new(3)); _ = w.InspectLocalGuardOrders(new(3)); }
            w.AdvanceTo(800); return w.Digest();
        }
        var expected = Enumerable.Range(0, 10).Select(s => Run(s, false)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[10]; Parallel.For(0, 10, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, true)); Assert.Equal(expected, actual); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void PreparationHonorsSupportedCandidateBoundsAndSearchBudget(int candidates)
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(134); var view = w.ActorView(new(3)); var order = Local(w).Payload;
        var plan = GuardPreparation.Plan(view, order, candidates); Assert.Equal(PlanOutcome.PlanFound, plan.Outcome); Assert.Equal(ActionKind.Move, plan.Steps[0].Intent.Kind);
        Assert.Equal(PlanOutcome.BudgetExhausted, GuardPreparation.Plan(view, order, candidates, budget: 0).Outcome);
    }
}
