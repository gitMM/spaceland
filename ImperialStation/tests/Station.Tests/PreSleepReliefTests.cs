using Station.Contracts;
using Station.Core;
using Xunit;

namespace Station.Tests;

public sealed class PreSleepReliefTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    // A 2,400-tick day with 800-tick duty and an 800-tick bed. One tick is one minute,
    // so hunger rises one point per tick awake or asleep and a full sleep adds 800.
    private static World Make(bool cafeteria = true, long sleep = 800)
    {
        var services = new List<NeedServiceDefinition> {
            new(2, NeedServiceKind.RestSpot, Fixtures.B, durationTicks: 5), new(3, NeedServiceKind.Bed, Fixtures.B, durationTicks: sleep, assignedActorId: 1),
            new(4, NeedServiceKind.Toilet, Fixtures.B, durationTicks: 2) };
        if (cafeteria) services.Add(new(1, NeedServiceKind.Cafeteria, Fixtures.B, durationTicks: 5));
        return new World("pre-sleep", 42, new WorldConfig(tickDurationMs: 60000, itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 4) },
            new[] { new ActorSeed(new(1), "Citizen", "worker", Fixtures.A) },
            new[] { new Item(new(2), "meal", 20, new(LocationKind.TileContainer, 2)), new Item(new(1), "card", 1, new(LocationKind.ActorInventory, 1)) }, true,
            information: new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }) }, credentials: new[] { new CredentialSeed(1, new(1), new(1), new(1)) }),
            routines: new RoutineSeed(new RoutineConfig(dayTicks: 2400, dutyTicks: 800, checkInWindow: 10, periodicCheckInTicks: 300), new[] { new RosterAssignmentSeed(new(1), 0, Fixtures.B) }),
            needs: new NeedSeed(services: services));
    }
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    private static bool CriticalWhileAsleep(World w)
    {
        bool asleep = false;
        foreach (var e in w.Trace)
        {
            if (e.Type == "NeedModeChanged") asleep = e.Detail == "Sleeping";
            if (e.Type == "NeedThresholdReached" && e.Detail.Contains("/Critical") && asleep) return true;
        }
        return false;
    }

    [Fact]
    public void HungerForecastToBecomeCriticalDuringSleepIsRelievedBeforeBed()
    {
        // Sleep pressure becomes urgent before the roster window here, so this also covers
        // the urgent-sleep path: hunger is still below seek but would pass 900 in bed.
        var w = Make(); w.AdvanceTo(1020); RoundTrip(w);
        w.AdvanceTo(2400);
        var relief = w.Trace.First(e => e.Type == "PlanSelected" && e.Detail.StartsWith("pre-sleep-relief/v1; need=Hunger", StringComparison.Ordinal));
        var bed = w.InspectNeedActivities().First(a => a.Kind == ActionKind.Sleep && a.RequestedTick >= relief.Tick);
        Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.Eat && a.Status == NeedActivityStatus.Completed && a.StartedTick > relief.Tick && a.EndedTick <= bed.StartedTick);
        Assert.Equal(NeedActivityStatus.Completed, bed.Status);
        Assert.Equal(0, w.CriticalNeedInterruptions); Assert.False(CriticalWhileAsleep(w));
        RoundTrip(w);
    }

    [Fact]
    public void UnavailableReliefDoesNotKeepTheCitizenAwake()
    {
        // Without any known cafeteria the forecast cannot be acted upon; sleep proceeds.
        var w = Make(cafeteria: false); w.AdvanceTo(2400);
        Assert.DoesNotContain(w.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("pre-sleep-relief/v1", StringComparison.Ordinal));
        var bed = w.InspectNeedActivities().First(a => a.Kind == ActionKind.Sleep);
        Assert.True(bed.StartedTick <= 1030, "Urgent sleep must not wait for an unavailable meal.");
        RoundTrip(w);
    }

    [Fact]
    public void RestoredCheckpointContinuesPreSleepReliefIdentically()
    {
        var w = Make(); w.AdvanceTo(1021);
        var restored = World.Restore(w.Serialize(), traceEnabled: true);
        w.AdvanceTo(2400); restored.AdvanceTo(2400);
        Assert.Equal(w.Digest(), restored.Digest());
    }
}
