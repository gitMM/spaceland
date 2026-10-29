using Station.Contracts;
using Station.Core;
using Station.Mind;
using Xunit;

namespace Station.Tests;

// Remembered terminal outages shared by reporting, check-ins, audits and recovery.
public sealed class TerminalOutageTests
{
    private static readonly Id<ActorTag> Worker = new(1);
    private static readonly Coord Home = new(0, 0, 0), Far = new(3, 0, 0);
    // Two public terminals: the worker's nearest at Home and an eligible alternative three tiles away.
    private static World Make(ulong seed = 42, bool alternative = true)
    {
        var access = new WorkAccess(new[] { "worker" }, "tool");
        var work = new WorkSeed(new[] { new ResourceDefinition("card"), new ResourceDefinition("tool"), new ResourceDefinition("part", "slot") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
            new[] { new MachineDefinition(1, new(1, 0, 0), "slot", "part", access, installTicks: 3, inspectTicks: 2) });
        var items = new List<Item> { new(new(1), "card", 1, new(LocationKind.ActorInventory, 1)), new(new(2), "tool", 1, new(LocationKind.ActorInventory, 1)), new(new(3), "part", 1, new(LocationKind.ActorInventory, 1)) };
        var facts = new List<FactSeed> { new(Worker, new(SubjectKind.Machine, 1, AttributeKind.RequiredResource), new(FactValueKind.Text, text: "part")) };
        const TerminalCapability all = TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.Records;
        var terminals = new List<TerminalSeed> { new(1, Home, new[] { "worker" }, capabilities: all) };
        if (alternative) terminals.Add(new(2, Far, new[] { "worker" }, capabilities: all));
        var info = new InformationSeed(new InformationConfig(loginTicks: 1, readBaseTicks: 1, readPerMessageTicks: 0, readMaximumTicks: 1, postTicks: 1, latencyTicks: 1, sessionTicks: 500),
            terminals, new[] { new CredentialSeed(1, Worker, new(1), new(1)) }, facts: facts);
        var jobs = new JobSystemSeed(new[] { new JobSeed(1, 1) }, new[] { new AssignmentSeed("initial", 1, Worker, 2000) }, new[] { new CheckInSeed(Worker, 0, 100) });
        return new World("outages", seed, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            Enumerable.Range(0, 4).Select(i => new Tile(new(i + 1), "Industrial", new(i, 0, 0), traversalOverride: 2)),
            new[] { new ActorSeed(Worker, "Worker", "worker", Home) }, items, true, info, work, jobs);
    }
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());

    [Fact]
    public void LocalOutageIsRememberedAndAnEligibleAlternativeCarriesTheReport()
    {
        var w = Make(); w.ScheduleTerminalChange(3, 1, true, false); w.AdvanceTo(40);
        var outage = Assert.Single(w.InspectTerminalOutages(Worker));
        Assert.Equal(1, outage.TerminalId); Assert.Equal(3, outage.FirstObservedTick);
        // The acceptance and completion were posted at the alternative terminal, never at the offline one.
        var posted = w.InspectMessages().Where(m => m.PhysicalSender == Worker && m.Draft.Kind is MessageKind.OrderAccepted or MessageKind.WorkCompleted).ToArray();
        Assert.NotEmpty(posted); Assert.All(posted, m => Assert.Equal(2, m.TerminalId));
        Assert.Equal(1, w.InspectTerminalOutages(Worker).Single().Observations);
        Assert.Equal(WorkExecution.CompletedPhysically, w.InspectJobs()[0].PhysicalStatus);
        RoundTrip(w);
    }

    [Fact]
    public void TravelDoesNotEraseAnObservedOutageAndRestorationNeedsALocalRecheck()
    {
        var w = Make(); w.ScheduleTerminalChange(3, 1, true, false); w.ScheduleTerminalChange(60, 1, true, true);
        w.AdvanceTo(59);
        // The worker moved away (repair site, alternative terminal) and still remembers the outage.
        Assert.Contains(w.Trace, e => e.Type == "TileArrived" && e.ActorId == Worker.Value && e.Tick > 3);
        Assert.Single(w.InspectTerminalOutages(Worker), o => o.TerminalId == 1);
        // Reconnection is a physical change; the citizen learns it only by observing the terminal.
        w.AdvanceTo(150); Assert.Single(w.InspectTerminalOutages(Worker), o => o.TerminalId == 1);
        // With the alternative now down, the next check-in rechecks the remembered terminal.
        w.ScheduleTerminalChange(151, 2, true, false); w.AdvanceTo(600);
        Assert.Contains(w.Trace, e => e.Type == "TerminalRestorationObserved" && e.ActorId == Worker.Value && e.TerminalId == 1 && e.Tick > 151);
        Assert.DoesNotContain(w.InspectTerminalOutages(Worker), o => o.TerminalId == 1);
        Assert.Contains(w.Trace, e => e.Type == "ActionCompleted" && e.Detail == "ReadInbox" && e.ActorId == Worker.Value && e.Tick > 151);
        RoundTrip(w);
    }

    [Fact]
    public void WithoutAnEligibleAlternativeRechecksBackOffAndFailuresStayBounded()
    {
        var w = Make(alternative: false); w.ScheduleTerminalChange(3, 1, true, false); w.AdvanceTo(3000);
        var learned = w.Trace.Where(e => e.Type == "TerminalOutageLearned" && e.ActorId == Worker.Value).Select(e => e.Tick).ToArray();
        Assert.True(learned.Length >= 4);
        var gaps = learned.Zip(learned.Skip(1), (a, b) => b - a).ToArray();
        // Each failed recheck waits at least as long as the previous one, up to the planner's cap.
        Assert.All(gaps.Zip(gaps.Skip(1)), p => Assert.True(p.Second >= p.First || p.Second >= 1000));
        Assert.True(w.ActionsFailed <= learned.Length + 2);
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(4000); copy.AdvanceTo(4000);
        Assert.Equal(w.Serialize(), copy.Serialize());
    }

    [Fact]
    public void RecordsQueriesRespectTheirBoundTerminalEvenWhenAnotherTerminalIsUp()
    {
        // Audits are bound to the terminal that grants the records permission; a
        // remembered outage there yields no plan rather than an unpermitted alternative.
        var w = Make(); var view = w.ActorView(Worker);
        var query = new RecordQuery(RecordKind.WorkAttempt, 1);
        Assert.Equal(PlanOutcome.PlanFound, ReportPlanning.PlanRecords(view, query, 1, 1, 1).Outcome);
        Assert.Equal(PlanOutcome.NoPlan, ReportPlanning.PlanRecords(view, query, 1, 1, 1, avoided: new long[] { 1 }).Outcome);
        Assert.Equal(PlanOutcome.PlanFound, ReportPlanning.Plan(view, TerminalFixtures.Draft(1), 1, 1, avoided: new long[] { 1 }).Outcome);
    }
}
