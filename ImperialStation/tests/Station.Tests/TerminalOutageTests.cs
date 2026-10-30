using Station.Contracts;
using Station.Core;
using Xunit;

namespace Station.Tests;

public sealed class TerminalOutageTests
{
    // One reporter at x=0 between terminal 1 (x=-2, chosen on ties) and terminal 2 (x=2).
    private static World Reporting(params (long Terminal, long Reconnect)[] outages)
    {
        var tiles = Enumerable.Range(0, 5).Select(i => new Tile(new(i + 1), "Corridor", new Coord(i - 2, 0, 0), traversalOverride: 2)).ToArray();
        var report = new MessageDraft(MessageKind.Observation, RecipientKind.Authority, default, new FactKey(SubjectKind.Item, 1, AttributeKind.Location), FactValue.At(new(-2, 0, 0)), 0);
        var w = new World("terminal-outage", 1, new WorldConfig(), Fixtures.Catalog, tiles, new[] { new ActorSeed(new(1), "Reporter", "worker", new(0, 0, 0)) },
            new[] { new Item(new(101), "keycard", 1, new ItemLocation(LocationKind.ActorInventory, 1)), new Item(new(1), "component", 1, new ItemLocation(LocationKind.TileContainer, 1)) }, true,
            new InformationSeed(TerminalFixtures.Tiny(), new[] { new TerminalSeed(1, new(-2, 0, 0), new[] { "worker" }), new TerminalSeed(2, new(2, 0, 0), new[] { "worker" }) },
                new[] { new CredentialSeed(1, new(1), new(1), new(101)) }, reportGoals: new[] { new ReportGoalSeed(new(1), report) }));
        foreach (var (terminal, reconnect) in outages)
        { w.ScheduleTerminalChange(0, terminal, true, false); w.ScheduleTerminalChange(reconnect, terminal, true, true); }
        return w;
    }
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    private static TraceEvent[] FailedLogins(World w) => w.Trace.Where(e => e.Type == "ActionFailed" && w.Trace.Any(r => r.Type == "ActionRequested" && r.ActionId == e.ActionId && r.Detail == "Login")).ToArray();

    [Fact]
    public void LearnedOutageSendsTheReportToAnEligibleAlternativeTerminal()
    {
        var w = Reporting((1, 50000)); w.AdvanceTo(200);
        var learned = Assert.Single(w.Trace, e => e.Type == "TerminalOutageLearned");
        Assert.Equal(1, learned.TerminalId);
        Assert.Single(FailedLogins(w));
        var posted = Assert.Single(w.InspectMessages());
        Assert.Equal(2, posted.TerminalId);
        Assert.Contains(w.Trace, e => e.Type == "TerminalLoginSucceeded" && e.TerminalId == 2);
        // Travel and the later successful login elsewhere do not erase the observation.
        var outage = Assert.Single(w.InspectTerminalOutages(new(1)));
        Assert.Equal(1, outage.TerminalId); Assert.Equal(learned.Tick + 1000, outage.RecheckAt);
        Assert.Equal(new Coord(2, 0, 0), w.InspectActor(new(1)).Position);
        RoundTrip(w);
    }

    [Fact]
    public void StationWideOutageKeepsBoundedRetriesAndClearsOnRestoredLogin()
    {
        // With every eligible terminal out there is no alternative: the reporter keeps
        // the agent's bounded retry backoff, while its learned recheck delays grow.
        var w = Reporting((1, 9000), (2, 9000)); w.AdvanceTo(8999);
        Assert.Empty(w.InspectMessages());
        var failed = FailedLogins(w);
        Assert.InRange(failed.Length, 2, 16);
        Assert.Equal(new long[] { 1, 2 }, w.InspectTerminalOutages(new(1)).Select(o => o.TerminalId));
        Assert.Contains(w.InspectTerminalOutages(new(1)), o => o.Failures > 1 && o.RecheckDelay > 1000);
        Assert.All(w.InspectTerminalOutages(new(1)), o => Assert.InRange(o.RecheckDelay, 1000, 8000));
        RoundTrip(w);
        var restored = World.Restore(w.Serialize(), traceEnabled: true);
        w.AdvanceTo(30000); restored.AdvanceTo(30000);
        Assert.Equal(w.Digest(), restored.Digest());
        var posted = Assert.Single(w.InspectMessages()); Assert.True(posted.SubmittedTick >= 9000);
        var cleared = Assert.Single(w.Trace, e => e.Type == "TerminalOutageCleared");
        Assert.Equal(posted.TerminalId, cleared.TerminalId);
        Assert.DoesNotContain(w.InspectTerminalOutages(new(1)), o => o.TerminalId == cleared.TerminalId);
        Assert.InRange(FailedLogins(w).Length, failed.Length, failed.Length + 2);
    }
}
