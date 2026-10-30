using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

// A sleep is a finite action sized from the citizen's own knowledge: its bed's duration,
// less sleep already taken in this window, and never past the next pre-duty meal opening
// of its known roster (R-LIF-03/08/11/12).
public sealed class SleepTimingTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    private static NeedActivitySnapshot[] Sleeps(World w, long actor) => w.InspectNeedActivities().Where(a => a.ActorId == actor && a.Kind == ActionKind.Sleep && a.StartedTick >= 0).OrderBy(a => a.StartedTick).ToArray();

    // A 2,400-tick day with 800-tick duty from tick 0 and an 800-tick bed: the sleep window
    // is [1400, 2300), the pre-duty meal [2300, 2400) and duty starts at 2400. The citizen
    // rests off duty on tile A; its berth is on tile B, which takes 600 ticks to enter, so it
    // can only lie down well into the window.
    private static World LateBed()
    {
        var services = new[] { new NeedServiceDefinition(3, NeedServiceKind.Bed, Fixtures.B, durationTicks: 800, assignedActorId: 1) };
        var w = new World("late-bed", 42, new WorldConfig(tickDurationMs: 6000, itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 600) },
            new[] { new ActorSeed(new(1), "Citizen", "worker", Fixtures.A) }, new[] { new Item(new(1), "card", 1, new(LocationKind.ActorInventory, 1)) }, true,
            information: new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }) }, credentials: new[] { new CredentialSeed(1, new(1), new(1), new(1)) }),
            routines: new RoutineSeed(new RoutineConfig(dayTicks: 2400, dutyTicks: 800, checkInWindow: 10, periodicCheckInTicks: 300), new[] { new RosterAssignmentSeed(new(1), 0, Fixtures.A) }),
            needs: new NeedSeed(services: services));
        return w;
    }

    [Fact]
    public void SleepStartedLateEndsBeforeTheKnownPreDutyMealInsteadOfRunningIntoDuty()
    {
        var w = LateBed(); w.AdvanceTo(1800); RoundTrip(w);
        w.AdvanceTo(2400);
        var sleep = Sleeps(w, 1).Last();
        Assert.True(sleep.StartedTick > 1600 && sleep.StartedTick + 800 > 2400, "A full bed started this late would run into duty at 2400.");
        // A full 800-tick bed would end at or after duty start; the sleep ends when the
        // known pre-duty meal opens, completes its daily goal and leaves the citizen awake.
        Assert.Equal(NeedActivityStatus.Completed, sleep.Status);
        // Sized at the decision tick to end at the 2300 opening (the bed is granted at the next tick).
        Assert.Equal(2300 - sleep.RequestedTick, sleep.EndedTick - sleep.StartedTick);
        Assert.True(sleep.EndedTick < 2400 && sleep.EndedTick - sleep.StartedTick < 800);
        Assert.Contains(w.InspectDailyGoals(), g => g.Kind == RoutineOpportunityKind.Sleep && g.Status == DailyGoalStatus.Completed && g.CompletedActionId == sleep.ActionId);
        Assert.NotEqual(ActionKind.Sleep, w.InspectActor(new(1)).Action);
        RoundTrip(w);
    }

    // The institutional station with the guards-unavailable control: actor 1 is detained,
    // falls asleep in its cell during its sleep window and the release falls due while it
    // sleeps. It then finishes its night in its own berth.
    [Fact]
    public void CitizenReleasedMidSleepSleepsOnlyWhatRemainsOfItsBedDuration()
    {
        var w = InstitutionalStation.Create(32, variant: "guards-unavailable").CreateWorld(trace: true); w.AdvanceTo(36000);
        var due = w.Trace.Single(e => e.Type == "DetentionReleaseDue" && e.ActorId == 1);
        Assert.Contains(w.Trace, e => e.Tick == due.Tick && e.ActorId == 1 && e.Type == "ActionCancelled" && e.Detail == "Sleep");
        var custody = w.InspectNeedActivities().Single(a => a.ActorId == 1 && a.Kind == ActionKind.Sleep && a.EndedTick == due.Tick);
        Assert.Equal(NeedActivityStatus.Cancelled, custody.Status);
        long slept = custody.EndedTick - custody.StartedTick, bed = w.InspectNeedServices().Single(s => s.Definition.Kind == NeedServiceKind.Bed && s.Definition.AssignedActorId == 1).Definition.DurationTicks;
        Assert.True(slept > 0 && slept < bed, "Release interrupts a sleep already under way.");
        var rest = w.InspectNeedActivities().Where(a => a.ActorId == 1 && a.Kind == ActionKind.Sleep && a.StartedTick > due.Tick).OrderBy(a => a.StartedTick).First();
        Assert.Equal(NeedActivityStatus.Completed, rest.Status);
        // The interrupted sleep kept its pro-rata relief; only the remainder of the bed's
        // duration is slept, not a fresh full night that would run past the pre-duty meal.
        Assert.Equal(bed - slept, rest.EndedTick - rest.StartedTick);
        var roster = w.ActorView(new(1)).KnownRoster!;
        Assert.True(rest.EndedTick <= DailyRoutine.Around(roster, rest.StartedTick).Where(o => o.Kind == RoutineOpportunityKind.PreDutyMeal && o.Opens > rest.StartedTick).Min(o => o.Opens));
        Assert.Contains(w.InspectDailyGoals(), g => g.ActorId == 1 && g.Kind == RoutineOpportunityKind.Sleep && g.Status == DailyGoalStatus.Completed && g.CompletedActionId == rest.ActionId);
        RoundTrip(w);
    }

    [Fact]
    public void SleepLengthUsesOnlyOwnBedDurationSleepTakenAndKnownRoster()
    {
        var roster = new RosterPayload(1, 1, 0, 2400, 0, 800, 0, Fixtures.B);
        Assert.Equal(800, DailyPolicy.SleepLength(null, 2000, 800, 0));
        Assert.Equal(800, DailyPolicy.SleepLength(roster, 1400, 800, 0));
        Assert.Equal(300, DailyPolicy.SleepLength(roster, 2000, 800, 0));
        Assert.Equal(200, DailyPolicy.SleepLength(roster, 1500, 800, 600));
        Assert.Equal(1400, DailyPolicy.SleepWindowOpened(roster, 2000)); Assert.Null(DailyPolicy.SleepWindowOpened(roster, 2350));
    }

    [Fact]
    public void OrdinaryWorkWaitsForADutyMealOwnHungerWillNeedBeforeItCloses()
    {
        var hunger = new NeedRule(NeedKind.Hunger, 60, 60, 60, 60, 300, 700, 900);
        // One tick is one minute here: hunger rises one point per tick.
        Assert.Equal(450, DailyPolicy.DutyMealComingDue(hunger, 250 * H, 500, 400, 60000));
        Assert.Equal(-1, DailyPolicy.DutyMealComingDue(hunger, 150 * H, 500, 400, 60000));
        Assert.Equal(-1, DailyPolicy.DutyMealComingDue(hunger, 300 * H, 500, 400, 60000));
        Assert.Equal(-1, DailyPolicy.DutyMealComingDue(hunger, 250 * H, 400, 400, 60000));
    }
}
