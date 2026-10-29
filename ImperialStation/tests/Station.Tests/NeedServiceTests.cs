using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class NeedServiceTests
{
    [Fact]
    public void PhysicalServiceGoldenScenarioCompletesAllAssertions()
    {
        var scenario = new LoadedScenario(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "need_services.json")), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "tiles.json")), "need-service-golden");
        var run = Runs.Execute(scenario, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(15, scenario.Scenario.Assertions.Count); Assert.Equal(0, run.World.ActionsFailed); RoundTrip(run.World);
    }
    private const long H = NeedAccumulator.HourMilliseconds;
    private static World Make(NeedServiceKind kind, int capacity = 1, long duration = 5, int population = 1, long meals = 2, long assigned = 1)
    {
        var service = new NeedServiceDefinition(1, kind, Fixtures.A, capacity, duration, assignedActorId: kind == NeedServiceKind.Bed ? assigned : 0);
        var initial = Enumerable.Range(1, population).SelectMany(id => new[] { new NeedInitialValue(new(id), NeedKind.Hunger, 700 * H), new NeedInitialValue(new(id), NeedKind.Bladder, 500 * H), new NeedInitialValue(new(id), NeedKind.SleepPressure, 500 * H), new NeedInitialValue(new(id), NeedKind.WorkStrain, 500 * H) });
        return new World("need-service", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 1), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 1) },
            Enumerable.Range(1, population).Select(id => new ActorSeed(new(id), "citizen", "worker", Fixtures.A)), meals > 0 ? new[] { new Item(new(1), "meal", meals, new(LocationKind.TileContainer, 1)) } : Array.Empty<Item>(), true,
            needs: new NeedSeed(autonomousActors: Array.Empty<long>(), initial: initial, services: new[] { service }));
    }
    private static void Start(World w, ActionKind kind, long tick = 0, long actor = 1) => w.Submit(tick, new(new(actor), kind, Fixtures.A, serviceId: 1));
    private static long Q(World w, NeedKind kind, long actor = 1) => w.InspectOwnNeeds(new(actor)).Single(n => n.Kind == kind).Q;
    private static NeedServiceSnapshot Service(World w) => Assert.Single(w.InspectNeedServices());
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void EatingClaimsASeatAndRealMealThenConsumesAndRelievesOnlyAtCompletion()
    {
        var w = Make(NeedServiceKind.Cafeteria); Start(w, ActionKind.Eat); w.AdvanceTo(0); Assert.Single(Service(w).QueuedActionIds); Assert.Empty(Service(w).OccupantActionIds); RoundTrip(w);
        w.AdvanceTo(1); Assert.Single(Service(w).OccupantActionIds); Assert.Empty(Service(w).QueuedActionIds); Assert.Equal(2, Assert.Single(w.InspectItems()).Quantity); RoundTrip(w);
        w.AdvanceTo(5); Assert.Equal(700 * H + 30000, Q(w, NeedKind.Hunger)); Assert.Equal(0, w.MealsConsumed);
        w.AdvanceTo(6); Assert.Equal(200 * H + 36000, Q(w, NeedKind.Hunger)); Assert.Equal(1, w.MealsConsumed); Assert.Equal(1, Assert.Single(w.InspectItems()).Quantity);
        Assert.Empty(Service(w).OccupantActionIds); Assert.Equal(NeedActivityStatus.Completed, Assert.Single(w.InspectNeedActivities()).Status); w.AssertAccounting(); RoundTrip(w);
    }
    [Fact]
    public void EmptyCafeteriaCannotFabricateFoodOrRelief()
    {
        var w = Make(NeedServiceKind.Cafeteria, meals: 0); Start(w, ActionKind.Eat); w.AdvanceTo(6);
        Assert.Equal(FailureReason.InsufficientMaterials, w.InspectActor(new(1)).LastFailure); Assert.Equal(700 * H + 36000, Q(w, NeedKind.Hunger)); Assert.Empty(Service(w).OccupantActionIds); Assert.Equal(0, w.MealsConsumed); RoundTrip(w);
    }
    [Fact]
    public void ConcurrentMealsReserveSeparateUnitsOfOneStackWithoutOverbooking()
    {
        var w = Make(NeedServiceKind.Cafeteria, capacity: 2, population: 3, meals: 2); Start(w, ActionKind.Eat, actor: 1); Start(w, ActionKind.Eat, actor: 2); Start(w, ActionKind.Eat, actor: 3); w.AdvanceTo(1);
        Assert.Equal(2, Service(w).OccupantActionIds.Count); Assert.Single(Service(w).QueuedActionIds); RoundTrip(w);
        w.AdvanceTo(12); Assert.Equal(2, w.MealsConsumed); Assert.Empty(w.InspectItems()); Assert.Equal(2, w.InspectNeedActivities().Count(a => a.Status == NeedActivityStatus.Completed)); Assert.Single(w.InspectNeedActivities(), a => a.Status == NeedActivityStatus.Rejected); w.AssertAccounting(); RoundTrip(w);
    }
    [Fact]
    public void ReservedMealStackCannotBeMovedByAnotherActor()
    {
        var w = Make(NeedServiceKind.Cafeteria, population: 2); Start(w, ActionKind.Eat); w.AdvanceTo(1);
        w.Submit(2, new(new(2), ActionKind.PickUp, Fixtures.A, new(1))); w.AdvanceTo(2); Assert.Equal(FailureReason.ResourceClaimed, w.InspectActor(new(2)).LastFailure); RoundTrip(w);
        w.AdvanceTo(6); w.Submit(7, new(new(2), ActionKind.PickUp, Fixtures.A, new(1))); w.AdvanceTo(8); Assert.Contains(new Id<ItemTag>(1), w.InspectActor(new(2)).Inventory); RoundTrip(w);
    }
    [Fact]
    public void CancellingEatingReleasesTheSeatAndMealWithoutAResult()
    {
        var w = Make(NeedServiceKind.Cafeteria); Start(w, ActionKind.Eat); w.Submit(3, new(new(1), ActionKind.Cancel, Fixtures.A)); w.AdvanceTo(6);
        Assert.Equal(2, Assert.Single(w.InspectItems()).Quantity); Assert.Equal(0, w.MealsConsumed); Assert.Equal(700 * H + 36000, Q(w, NeedKind.Hunger)); Assert.Empty(Service(w).OccupantActionIds); RoundTrip(w);
    }
    [Fact]
    public void ToiletIsFiniteCardlessAndResetsTheExactAccumulator()
    {
        var w = Make(NeedServiceKind.Toilet); Start(w, ActionKind.UseToilet); w.AdvanceTo(5); Assert.Equal(500 * H + 50000, Q(w, NeedKind.Bladder));
        w.AdvanceTo(6); Assert.Equal(0, Q(w, NeedKind.Bladder)); w.AdvanceTo(7); Assert.Equal(10000, Q(w, NeedKind.Bladder)); RoundTrip(w);
    }
    [Theory]
    [InlineData(NeedServiceKind.RestSpot, ActionKind.Rest, NeedMode.Resting)]
    [InlineData(NeedServiceKind.Bed, ActionKind.Sleep, NeedMode.Sleeping)]
    public void RestAndSleepGiveOnlyElapsedBenefitEvenWhenCancelled(NeedServiceKind kind, ActionKind action, NeedMode mode)
    {
        var w = Make(kind); Start(w, action); w.AdvanceTo(1); Assert.Equal(mode, Assert.Single(w.InspectNeeds()).Mode); RoundTrip(w);
        w.Submit(3, new(new(1), ActionKind.Cancel, Fixtures.A)); w.AdvanceTo(3);
        Assert.Equal(NeedMode.Awake, Assert.Single(w.InspectNeeds()).Mode); Assert.Equal(500 * H - (kind == NeedServiceKind.Bed ? 12000 : 24000), Q(w, NeedKind.WorkStrain));
        Assert.Equal(kind == NeedServiceKind.Bed ? 500 * H - 15000 : 500 * H + 15000, Q(w, NeedKind.SleepPressure)); RoundTrip(w);
    }
    [Fact]
    public void PhysicalOneTickSleepCyclesProduceTheExactT27Accumulator()
    {
        var w = Make(NeedServiceKind.Bed, duration: 1);
        for (int i = 0; i < 100; i++) Start(w, ActionKind.Sleep, tick: i * 2);
        w.AdvanceTo(101); var copy = World.Restore(w.Serialize()); RoundTrip(w); w.AdvanceTo(200); copy.AdvanceTo(200);
        Assert.Equal(1799500000, Q(w, NeedKind.SleepPressure)); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(100, w.InspectNeedActivities().Count(a => a.Status == NeedActivityStatus.Completed)); RoundTrip(w);
    }
    [Fact]
    public void AFreeBedStillRequiresItsAssignmentAndVacantCapacityDoesNotGrantPermission()
    {
        foreach (long assigned in new long[] { 0, 2 })
        {
            var w = Make(NeedServiceKind.Bed, population: 2, assigned: assigned); Start(w, ActionKind.Sleep); w.AdvanceTo(2);
            Assert.Equal(FailureReason.NotQualified, w.InspectActor(new(1)).LastFailure); Assert.Empty(Service(w).OccupantActionIds); Assert.Equal(NeedMode.Awake, w.InspectNeeds().First().Mode); RoundTrip(w);
        }
    }
    [Fact]
    public void ServiceOutageCancelsActiveUseAndRetainsTheFifoQueueForReopening()
    {
        var w = Make(NeedServiceKind.Toilet, population: 2); Start(w, ActionKind.UseToilet, actor: 1); Start(w, ActionKind.UseToilet, actor: 2); w.ScheduleNeedServiceChange(3, 1, false); w.AdvanceTo(3);
        Assert.Empty(Service(w).OccupantActionIds); Assert.Single(Service(w).QueuedActionIds); Assert.Single(w.InspectNeedActivities(), a => a.Status == NeedActivityStatus.Cancelled); RoundTrip(w);
        long waiting = w.InspectNeedActivities().Single(a => a.Status == NeedActivityStatus.Queued).ActorId;
        w.ScheduleNeedServiceChange(10, 1, true); w.AdvanceTo(15); Assert.Equal(0, Q(w, NeedKind.Bladder, waiting)); Assert.Single(w.InspectNeedActivities(), a => a.Status == NeedActivityStatus.Completed); RoundTrip(w);
    }
    [Fact]
    public void CancellingTheOnlyQueuedRequestRemovesItsGrantTimer()
    {
        var w = Make(NeedServiceKind.Toilet); Start(w, ActionKind.UseToilet); w.Submit(0, new(new(1), ActionKind.Cancel, Fixtures.A)); w.AdvanceTo(0);
        Assert.Empty(Service(w).QueuedActionIds); Assert.Equal(-1, Service(w).NextGrant); RoundTrip(w); w.AdvanceTo(6); Assert.Equal(0, w.ActionsCompleted);
    }
    [Fact]
    public void RestHasNoSleepReliefAndNeedServiceCannotRunAtADistance()
    {
        var w = Make(NeedServiceKind.RestSpot); w.Submit(0, new(new(1), ActionKind.Rest, Fixtures.B, serviceId: 1)); w.AdvanceTo(0); Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(new(1)).LastFailure);
        Start(w, ActionKind.Rest, 1); w.AdvanceTo(7); Assert.Equal(500 * H + 35000, Q(w, NeedKind.SleepPressure)); Assert.True(Q(w, NeedKind.WorkStrain) < 500 * H); RoundTrip(w);
    }
    [Fact]
    public void CheckpointsPreserveQueuesMealClaimsAndProRataRates()
    {
        foreach (var kind in Enum.GetValues<NeedServiceKind>())
        foreach (long tick in new long[] { 0, 1, 3, 5, 6, 7, 11 })
        {
            var w = Make(kind, population: 2); Start(w, Service(w).Definition.Action); if (kind != NeedServiceKind.Bed) Start(w, Service(w).Definition.Action, actor: 2);
            w.AdvanceTo(tick); var copy = World.Restore(w.Serialize()); w.AdvanceTo(20); copy.AdvanceTo(20); Assert.Equal(w.Serialize(), copy.Serialize()); RoundTrip(w);
        }
    }
    private static World Detained(NeedServiceKind kind, long duration = 5)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "detention_release.json")))!.AsObject(); j["assertions"] = new JsonArray();
        j["initialState"]!["actors"]!.AsArray().Add(new JsonObject { ["id"] = 5, ["name"] = "Visitor", ["role"] = "visitor", ["position"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 } });
        j["initialState"]!["needs"] = new JsonObject {
            ["initial"] = new JsonArray(new JsonObject { ["actorId"] = 1, ["kind"] = "SleepPressure", ["q"] = 500 * H }),
            ["services"] = new JsonArray(new JsonObject { ["id"] = 1, ["kind"] = kind.ToString(), ["position"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 }, ["detentionFacilityId"] = 1, ["durationTicks"] = duration }) };
        var w = new LoadedScenario(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "detention-needs").CreateWorld(42); w.AdvanceTo(900); return w;
    }
    [Fact]
    public void DetaineeCanUseAndCancelLocalReliefWithoutCancellingCustody()
    {
        var w = Detained(NeedServiceKind.Toilet); w.Submit(901, new(new(1), ActionKind.UseToilet, new(2, 0, 0), serviceId: 1)); w.AdvanceTo(902); Assert.Single(Service(w).OccupantActionIds); RoundTrip(w);
        w.Submit(903, new(new(1), ActionKind.Cancel, new(2, 0, 0))); w.AdvanceTo(903); Assert.Empty(Service(w).OccupantActionIds); Assert.Equal(DetentionStayStatus.Detained, Assert.Single(w.InspectDetentionStays()).Status); RoundTrip(w);
        w.Submit(904, new(new(1), ActionKind.UseToilet, new(2, 0, 0), serviceId: 1)); w.AdvanceTo(910); Assert.Equal(0, Q(w, NeedKind.Bladder)); Assert.Equal(EscortStatus.Detained, Assert.Single(w.InspectEscorts()).Status); RoundTrip(w);
        w.Submit(911, new(new(1), ActionKind.Move, new(1, 0, 0))); w.AdvanceTo(911); Assert.Equal(FailureReason.CustodyRestricted, w.InspectActor(new(1)).LastFailure); RoundTrip(w);
    }
    [Fact]
    public void ReleaseDueInterruptsCustodySleepWithElapsedBenefitAndNoSentenceExtension()
    {
        var w = Detained(NeedServiceKind.Bed, 2000); w.Submit(901, new(new(1), ActionKind.Sleep, new(2, 0, 0), serviceId: 1)); w.AdvanceTo(902);
        Assert.Equal(NeedMode.Sleeping, w.InspectNeeds().Single(n => n.ActorId == 1).Mode); RoundTrip(w);
        w.AdvanceTo(1795); Assert.Equal(ActionKind.ReleaseDetention, w.InspectActor(new(1)).Action); Assert.Equal(NeedActivityStatus.Cancelled, Assert.Single(w.InspectNeedActivities()).Status);
        Assert.Equal(500 * H + 902 * 5000 - (1795 - 902) * 10000, Q(w, NeedKind.SleepPressure)); Assert.Empty(Service(w).OccupantActionIds); RoundTrip(w);
        w.AdvanceTo(2162); Assert.Equal(DetentionStayStatus.Released, Assert.Single(w.InspectDetentionStays()).Status); RoundTrip(w);
    }
    [Fact]
    public void OrdinaryCitizenCannotUseACustodyOnlyService()
    {
        var w = Detained(NeedServiceKind.Toilet);
        w.Submit(901, new(new(5), ActionKind.UseToilet, new(2, 0, 0), serviceId: 1)); w.AdvanceTo(902); Assert.Equal(FailureReason.CustodyRestricted, w.InspectActor(new(5)).LastFailure); Assert.Empty(w.InspectNeedActivities()); RoundTrip(w);
    }
    [Fact]
    public void ThresholdsFallAtTheFirstExactTickDuringRealSleep()
    {
        var w = new World("sleep-crossing", 42, new WorldConfig(), Fixtures.Catalog, new[] { new Tile(new(1), "Habitat", Fixtures.A) }, new[] { new ActorSeed(new(1), "sleeper", "worker", Fixtures.A) }, Array.Empty<Item>(), true,
            needs: new NeedSeed(autonomousActors: Array.Empty<long>(), initial: new[] { new NeedInitialValue(new(1), NeedKind.SleepPressure, 1000 * H) }, services: new[] { new NeedServiceDefinition(1, NeedServiceKind.Bed, Fixtures.A, durationTicks: 10, assignedActorId: 1) }));
        Start(w, ActionKind.Sleep); w.AdvanceTo(1); Assert.Equal(NeedBand.Critical, w.ActorView(new(1)).Needs.Single(n => n.Kind == NeedKind.SleepPressure).Band); Assert.Equal(2, Assert.Single(w.InspectNeeds()).NextThreshold); RoundTrip(w);
        w.AdvanceTo(2); Assert.Equal(NeedBand.Urgent, w.ActorView(new(1)).Needs.Single(n => n.Kind == NeedKind.SleepPressure).Band); Assert.Equal(ActionKind.Sleep, w.InspectActor(new(1)).Action); RoundTrip(w);
    }
    [Fact]
    public void MailDeliveryDoesNotWakeASleepingActor()
    {
        var w = new World("sleep-inbox", 42, new WorldConfig(), Fixtures.Catalog, new[] { new Tile(new(1), "Habitat", Fixtures.A) }, new[] { new ActorSeed(new(1), "sleeper", "worker", Fixtures.A) }, Array.Empty<Item>(), true,
            information: new InformationSeed(messages: new[] { new InitialMessage(TerminalFixtures.Draft(1), new(1), 3) }), needs: new NeedSeed(autonomousActors: Array.Empty<long>(), services: new[] { new NeedServiceDefinition(1, NeedServiceKind.Bed, Fixtures.A, durationTicks: 10, assignedActorId: 1) }));
        Start(w, ActionKind.Sleep); w.AdvanceTo(3); Assert.Equal(ActionKind.Sleep, w.InspectActor(new(1)).Action); Assert.Equal(NeedMode.Sleeping, Assert.Single(w.InspectNeeds()).Mode);
        Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(w.InspectMessages()).Stage); Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.SourceKind == EvidenceKind.MessageRead); RoundTrip(w);
    }
    [Fact]
    public void RestoreRejectsChangedMealReservationAndPrematureCompletion()
    {
        var w = Make(NeedServiceKind.Cafeteria); Start(w, ActionKind.Eat); w.AdvanceTo(3); var a = Assert.Single(w.InspectNeedActivities()); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { writer.Write(a.ActionId); writer.Write(a.ActorId); writer.Write(a.ServiceId); writer.Write((int)a.Kind); writer.Write(a.RequestedTick); writer.Write(a.StartedTick); writer.Write(a.EndedTick); writer.Write(a.MealItemId); writer.Write((int)a.Status); writer.Write(a.Reason); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var edit in new[] { (Offset: 36, Value: 2L), (Offset: 44, Value: 3L), (Offset: 52, Value: 999L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(edit.Value).CopyTo(bad, start + edit.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }
    [Fact]
    public void PublicServiceCatalogDoesNotRevealCurrentOutagesOrCapacityClaims()
    {
        var w = Make(NeedServiceKind.Toilet); var known = Assert.Single(w.ActorView(new(1)).NeedServices); w.ScheduleNeedServiceChange(1, 1, false); w.AdvanceTo(1);
        Assert.False(Service(w).Enabled); Assert.True(known.Enabled); Assert.Same(known, Assert.Single(w.ActorView(new(1)).NeedServices)); RoundTrip(w);
    }
    [Fact]
    public void InvalidBerthCapacityAndDuplicateAssignmentsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new NeedServiceDefinition(1, NeedServiceKind.Bed, Fixtures.A, capacity: 2));
        Assert.Throws<ArgumentException>(() => new World("duplicate-berth", 42, new WorldConfig(), Fixtures.Catalog, new[] { new Tile(new(1), "Habitat", Fixtures.A) },
            new[] { new ActorSeed(new(1), "resident", "worker", Fixtures.A) }, Array.Empty<Item>(), needs: new NeedSeed(autonomousActors: Array.Empty<long>(), services: new[] {
                new NeedServiceDefinition(1, NeedServiceKind.Bed, Fixtures.A, assignedActorId: 1), new NeedServiceDefinition(2, NeedServiceKind.Bed, Fixtures.A, assignedActorId: 1) })));
    }
}
