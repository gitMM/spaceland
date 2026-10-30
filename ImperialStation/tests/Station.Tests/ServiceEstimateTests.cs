using Station.Contracts;
using Station.Core;
using Xunit;

namespace Station.Tests;

public sealed class ServiceEstimateTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    // Five tiles of 4 ticks each. Cafeteria 1 (x=1) seats one; cafeteria 2 (x=4) seats
    // three; meals take 60 ticks. One tick is one minute, so hunger rises one point per tick.
    private static World Make()
    {
        var tiles = Enumerable.Range(0, 5).Select(i => new Tile(new(i + 1), "Habitat", new Coord(i, 0, 0), traversalOverride: 4)).ToArray();
        var actors = Enumerable.Range(1, 3).Select(i => new ActorSeed(new(i), "Diner " + i, "worker", new Coord(0, 0, 0))).ToArray();
        var items = new List<Item> { new(new(10), "meal", 20, new(LocationKind.TileContainer, 2)), new(new(11), "meal", 20, new(LocationKind.TileContainer, 5)) };
        items.AddRange(actors.Select(a => new Item(new(a.Id.Value), "card", 1, new(LocationKind.ActorInventory, a.Id.Value))));
        return new World("service-estimates", 42, new WorldConfig(tickDurationMs: 60000, itemActionTicks: 1), Fixtures.Catalog, tiles, actors, items, true,
            information: new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, new Coord(0, 0, 0), new[] { "worker" }) },
                credentials: actors.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(a.Id.Value)))),
            needs: new NeedSeed(initial: actors.Select(a => new NeedInitialValue(a.Id, NeedKind.Hunger, 750 * H)),
                services: new[] { new NeedServiceDefinition(1, NeedServiceKind.Cafeteria, new Coord(1, 0, 0), capacity: 1, durationTicks: 60),
                    new NeedServiceDefinition(2, NeedServiceKind.Cafeteria, new Coord(4, 0, 0), capacity: 3, durationTicks: 60),
                    new NeedServiceDefinition(3, NeedServiceKind.Toilet, new Coord(0, 0, 0), capacity: 3, durationTicks: 2) }));
    }
    private static NeedActivitySnapshot[] Meals(World w, long actor) => w.InspectNeedActivities().Where(a => a.ActorId == actor && a.Kind == ActionKind.Eat && a.Status == NeedActivityStatus.Completed).OrderBy(a => a.StartedTick).ToArray();

    [Fact]
    public void ExperiencedQueueSendsOnlyTheDelayedDinerToTheLessBusyCafeteria()
    {
        var w = Make(); w.AdvanceTo(250);
        // Everyone first chooses the nearest cafeteria; with one seat someone waits two rounds.
        Assert.All(new long[] { 1, 2, 3 }, a => Assert.Equal(1, Meals(w, a)[0].ServiceId));
        var estimates = new long[] { 1, 2, 3 }.ToDictionary(a => a, a => Assert.Single(w.InspectServiceEstimates(new(a))));
        Assert.All(estimates.Values, e => Assert.Equal(ServiceEstimateSource.ExperiencedWait, e.WaitSource));
        // Meals seen after eating are a separate, later observation of local stock.
        Assert.All(estimates.Values, e => { Assert.Equal(ServiceEstimateSource.ObservedStock, e.StockSource); Assert.True(e.WaitObservedTick < e.StockObservedTick); });
        long delayed = estimates.OrderByDescending(e => e.Value.WaitTicks).First().Key;
        Assert.True(estimates[delayed].WaitTicks > 100, "With one seat the last diner waits about two rounds.");
        w.AdvanceTo(1200);
        // Hungry again with fresh estimates: only the diner whose extra wait outweighs the
        // longer walk takes the farther cafeteria; a one-round wait causes no switching.
        Assert.Equal(2, Meals(w, delayed)[1].ServiceId);
        Assert.All(estimates.Keys.Where(a => a != delayed), a => Assert.Equal(1, Meals(w, a)[1].ServiceId));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    // Two identical one-seat cafeterias share the diners' tile.
    private static World SameTile(long secondHunger)
    {
        var tiles = new[] { new Tile(new(1), "Habitat", new Coord(0, 0, 0)) };
        var actors = Enumerable.Range(1, 2).Select(i => new ActorSeed(new(i), "Diner " + i, "worker", new Coord(0, 0, 0))).ToArray();
        var items = new List<Item> { new(new(10), "meal", 20, new(LocationKind.TileContainer, 1)) };
        items.AddRange(actors.Select(a => new Item(new(a.Id.Value), "card", 1, new(LocationKind.ActorInventory, a.Id.Value))));
        return new World("identical-services", 42, new WorldConfig(tickDurationMs: 60000, itemActionTicks: 1), Fixtures.Catalog, tiles, actors, items, true,
            information: new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, new Coord(0, 0, 0), new[] { "worker" }) },
                credentials: actors.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(a.Id.Value)))),
            needs: new NeedSeed(initial: new[] { new NeedInitialValue(new(1), NeedKind.Hunger, 699 * H), new NeedInitialValue(new(2), NeedKind.Hunger, secondHunger * H) },
                services: new[] { new NeedServiceDefinition(1, NeedServiceKind.Cafeteria, new Coord(0, 0, 0), capacity: 1, durationTicks: 60),
                    new NeedServiceDefinition(2, NeedServiceKind.Cafeteria, new Coord(0, 0, 0), capacity: 1, durationTicks: 60) }));
    }

    // The second diner gets hungry while the lower-numbered seat is occupied and plans
    // for the free one it can see.
    [Fact]
    public void DinerTakesAVisiblyFreeIdenticalServiceOnTheSameTile()
    {
        var w = SameTile(689); w.AdvanceTo(120);
        var first = Meals(w, 1)[0]; var second = Meals(w, 2)[0];
        Assert.Equal(1, first.ServiceId);
        Assert.True(second.RequestedTick > first.StartedTick && second.RequestedTick < first.EndedTick, "The second diner decides while the first seat is taken.");
        Assert.Equal(2, second.ServiceId);
        Assert.True(second.StartedTick <= second.RequestedTick + 1, "No wait beyond the ordinary one-tick grant.");
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    // Both diners plan in the same tick and pick the same seat; the one who finds it
    // taken on arrival uses the free identical seat instead of queueing a full round.
    [Fact]
    public void SameTickChoosersSpreadOverIdenticalServicesOnArrival()
    {
        var w = SameTile(699); w.AdvanceTo(120);
        var first = Meals(w, 1)[0]; var second = Meals(w, 2)[0];
        Assert.Equal(first.RequestedTick, second.RequestedTick);
        Assert.NotEqual(first.ServiceId, second.ServiceId);
        Assert.All(new[] { first, second }, m => Assert.True(m.StartedTick <= m.RequestedTick + 1));
        Assert.Single(w.Trace, e => e.Type == "NeedServiceSwitched");
        var copy = World.Restore(w.Serialize()); Assert.Equal(w.Serialize(), copy.Serialize());
        w.AdvanceTo(200); copy.AdvanceTo(200); Assert.Equal(w.Serialize(), copy.Serialize());
    }
}
