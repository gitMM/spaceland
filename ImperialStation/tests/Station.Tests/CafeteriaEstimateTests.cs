using Station.Contracts;
using Station.Core;
using Station.Mind;
using Xunit;

namespace Station.Tests;

// Cafeteria choice from the citizen's own remembered queue and stock estimates.
public sealed class CafeteriaEstimateTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    // Cafeteria 1 is one tile from the resident; cafeteria 2 is two tiles away.
    private static World Make(IEnumerable<FactSeed>? facts = null, Coord? start = null, long hunger = 700)
    {
        var tiles = new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 4), new Tile(new(3), "Habitat", Fixtures.C, traversalOverride: 4) };
        var services = new[] { new NeedServiceDefinition(1, NeedServiceKind.Cafeteria, Fixtures.B, durationTicks: 5), new NeedServiceDefinition(2, NeedServiceKind.Cafeteria, Fixtures.C, durationTicks: 5) };
        var items = new[] { new Item(new(1), "meal", 2, new(LocationKind.TileContainer, 2)), new Item(new(2), "meal", 2, new(LocationKind.TileContainer, 3)) };
        return new World("cafeteria-estimates", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog, tiles,
            new[] { new ActorSeed(new(1), "Resident", "worker", start ?? Fixtures.A) }, items, true,
            information: new InformationSeed(publication: new PublicKnowledge(1, tiles.Select(t => new KnownTile(t.Id, t.Position, 4)), Array.Empty<PublicService>()), facts: facts),
            needs: new NeedSeed(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, hunger * H) }, services: services));
    }
    private static FactSeed Fact(long service, AttributeKind attribute, long value) => new(new(1), new(SubjectKind.Service, service, attribute), FactValue.Integer(value));
    private static long Chosen(World w) => NeedPlanning.Plan(w.ActorView(new(1)), NeedKind.Hunger).Steps.Last().Intent.ServiceId;

    [Fact]
    public void NearestCafeteriaWinsWithoutEstimatesAndLiveQueuesAreNotRead()
    {
        Assert.Equal(1, Chosen(Make()));
        // A physically disabled nearest cafeteria is invisible to planning until visited.
        var w = Make(hunger: 0); w.ScheduleNeedServiceChange(0, 1, false); w.AdvanceTo(0);
        Assert.Equal(1, Chosen(w));
    }

    [Fact]
    public void RememberedLongQueueSelectsAUsefulAlternative()
    {
        var w = Make(new[] { Fact(1, AttributeKind.EstimateTicks, 200) });
        Assert.Equal(2, Chosen(w));
        w.AdvanceTo(30);
        Assert.Equal(1, w.MealsConsumed);
        Assert.Equal(2, Assert.Single(w.InspectNeedActivities(), a => a.Status == NeedActivityStatus.Completed).ServiceId);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void EstimatesBecomeStaleAfterTheirDeclaredAge()
    {
        // A low hunger keeps the resident idle while the remembered queue ages past one configured hour.
        var w = Make(new[] { Fact(1, AttributeKind.EstimateTicks, 200) }, hunger: 0);
        Assert.Equal(2, Chosen(w));
        w.AdvanceTo(1498); Assert.Equal(2, Chosen(w));
        w.AdvanceTo(1500); Assert.Equal(1, Chosen(w));
    }

    [Fact]
    public void RememberedEmptyCounterIsAvoidedUntilStale()
    {
        var w = Make(new[] { Fact(1, AttributeKind.Quantity, 0) });
        Assert.Equal(2, Chosen(w));
        var idle = Make(new[] { Fact(1, AttributeKind.Quantity, 0) }, hunger: 0);
        idle.AdvanceTo(6 * 1500 + 1); Assert.Equal(1, Chosen(idle));
    }

    [Fact]
    public void ObservedQueueIsNotLeftForAnUnknownAlternativeOrASmallGain()
    {
        // At cafeteria 1 with a short observed wait: the unknown alternative is assumed no
        // better, and a known-empty queue elsewhere must beat this one by a whole meal.
        var here = Make(new[] { Fact(1, AttributeKind.EstimateTicks, 3) }, Fixtures.B);
        Assert.Equal(1, Chosen(here));
        var small = Make(new[] { Fact(1, AttributeKind.EstimateTicks, 6), Fact(2, AttributeKind.EstimateTicks, 0) }, Fixtures.B);
        Assert.Equal(1, Chosen(small));
        var large = Make(new[] { Fact(1, AttributeKind.EstimateTicks, 400), Fact(2, AttributeKind.EstimateTicks, 0) }, Fixtures.B);
        Assert.Equal(2, Chosen(large));
    }

    [Fact]
    public void ArrivalRecordsALocalEstimateThatConversationMayShareWithProvenance()
    {
        var w = Make(); w.AdvanceTo(10);
        var view = w.ActorView(new(1));
        var wait = Assert.Single(view.Knowledge, f => f.Key.Equals(new FactKey(SubjectKind.Service, 1, AttributeKind.EstimateTicks)));
        Assert.Equal(EvidenceKind.Direct, wait.SourceKind); Assert.True(wait.ObservedTick >= 4);
        Assert.Contains(view.Knowledge, f => f.Key.Equals(new FactKey(SubjectKind.Service, 1, AttributeKind.Quantity)) && f.Value.Number >= 1);
        // Only a witnessed empty counter or a queue is shared; the listener keeps its age.
        var queue = new KnowledgeFact(1, new(SubjectKind.Service, 1, AttributeKind.EstimateTicks), FactValue.Integer(375), EvidenceKind.Direct, 1, 100, 100);
        var none = new KnowledgeFact(2, new(SubjectKind.Service, 1, AttributeKind.EstimateTicks), FactValue.Integer(0), EvidenceKind.Direct, 1, 100, 100);
        var empty = new KnowledgeFact(3, new(SubjectKind.Service, 1, AttributeKind.Quantity), FactValue.Integer(0), EvidenceKind.Direct, 1, 100, 100);
        Assert.True(ConversationPolicy.MayShare(1, ConversationTopic.WitnessedProblem, queue));
        Assert.False(ConversationPolicy.MayShare(1, ConversationTopic.WitnessedProblem, none));
        Assert.True(ConversationPolicy.MayShare(1, ConversationTopic.WitnessedProblem, empty));
    }
}
