using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class PatrolTests
{
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "patrol_response.json")))!.AsObject();
        change?.Invoke(json);
        return new LoadedScenario(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "patrol-test");
    }
    private static World InspectWorld(Coord target, long traversal = 1, PerceptionCatalog? perception = null)
        => new("local-person-inspection", 42, new WorldConfig(), Fixtures.Catalog,
            new[] { new Tile(new(1), "Command", Fixtures.A, traversalOverride: traversal), new Tile(new(2), "Hull", Fixtures.B, traversalOverride: traversal) },
            new[] { new ActorSeed(new(1), "Observer", "guard", Fixtures.A), new ActorSeed(new(2), "Subject", "worker", target) }, Array.Empty<Item>(), true,
            new InformationSeed(new InformationConfig(inspectTicks: 3), Array.Empty<TerminalSeed>(), Array.Empty<CredentialSeed>(), perception: perception));
    private static FactKey Location(long actor) => new(SubjectKind.Actor, actor, AttributeKind.Location);

    [Fact]
    public void ActorLocationIsInspectionOnlyUnlessContentEnablesLocalPassiveObservation()
    {
        var ordinary = InspectWorld(Fixtures.A); ordinary.AdvanceTo(0); Assert.DoesNotContain(ordinary.InspectKnowledge(new(1)), f => f.Key.Equals(Location(2)));
        var rule = new PerceptionCatalog(new[] { new PerceptionRule(SubjectKind.Actor, AttributeKind.Location, PerceptionMode.Passive) });
        var local = InspectWorld(Fixtures.A, perception: rule); local.AdvanceTo(0); Assert.Equal(FactValue.At(Fixtures.A), local.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Location(2))).Value);
        var remote = InspectWorld(Fixtures.B, perception: rule); remote.AdvanceTo(0); Assert.DoesNotContain(remote.InspectKnowledge(new(1)), f => f.Key.Equals(Location(2)));
        Assert.Equal(local.Serialize(), World.Restore(local.Serialize()).Serialize());
    }

    [Fact]
    public void FiniteInspectionTeachesOnlyLocalPresenceAtCompletionAndCancellationTeachesNothing()
    {
        var remote = InspectWorld(Fixtures.B); remote.Submit(1, new(new(1), ActionKind.InspectActor, Fixtures.A, targetActor: new(2)));
        remote.AdvanceTo(3); Assert.DoesNotContain(remote.InspectKnowledge(new(1)), f => f.Key.Equals(Location(2)));
        var resumed = World.Restore(remote.Serialize()); remote.AdvanceTo(4); resumed.AdvanceTo(4); Assert.Equal(remote.Digest(), resumed.Digest());
        Assert.Equal(FactValue.Absent(Fixtures.A), remote.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Location(2))).Value);
        Assert.Empty(remote.InspectBeliefs());
        var arrives = InspectWorld(Fixtures.B); arrives.Submit(1, new(new(1), ActionKind.InspectActor, Fixtures.A, targetActor: new(2))); arrives.Submit(2, new(new(2), ActionKind.Move, Fixtures.A)); arrives.AdvanceTo(4);
        Assert.Equal(FactValue.At(Fixtures.A), arrives.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Location(2))).Value);
        var transit = InspectWorld(Fixtures.A, 10); transit.Submit(1, new(new(1), ActionKind.InspectActor, Fixtures.A, targetActor: new(2))); transit.Submit(2, new(new(2), ActionKind.Move, Fixtures.B)); transit.AdvanceTo(4);
        Assert.Equal(FactValue.Absent(Fixtures.A), transit.InspectKnowledge(new(1)).Single(f => f.Key.Equals(Location(2))).Value);
        var cancelled = InspectWorld(Fixtures.A); cancelled.Submit(1, new(new(1), ActionKind.InspectActor, Fixtures.A, targetActor: new(2))); cancelled.Submit(2, new(new(1), ActionKind.Cancel, Fixtures.A)); cancelled.AdvanceTo(4);
        Assert.DoesNotContain(cancelled.InspectKnowledge(new(1)), f => f.Key.Equals(Location(2)));
    }

    [Fact]
    public void AutonomousPatrolReadsAcceptsSearchesAndReportsIndependentOfRepair()
    {
        var loaded = Load(); Assert.Equal(1, Assert.Single(loaded.Scenario.ScriptedActors).ActorId);
        var world = loaded.CreateWorld(42, true); world.AdvanceTo(27);
        var order = Assert.Single(world.InspectPatrolOrders()); Assert.Equal("Posted", order.Status);
        Assert.Empty(world.InspectLocalPatrols(new(3))); Assert.DoesNotContain(world.InspectKnowledge(new(3)), f => f.Key.Equals(Location(1)));
        world.AdvanceTo(39); Assert.Equal(LocalPatrolStatus.AcceptancePending, Assert.Single(world.InspectLocalPatrols(new(3))).Status);
        Assert.Empty(world.InspectLocalPatrols(new(3))[0].Observations);
        world.AdvanceTo(48); var search = Assert.Single(world.InspectLocalPatrols(new(3)));
        Assert.Equal(new[] { Fixtures.A, Fixtures.B, Fixtures.C }, search.Observations.Select(o => o.Tile));
        Assert.Equal(new[] { false, false, true }, search.Observations.Select(o => o.Found));
        Assert.DoesNotContain(world.InspectBeliefs(), f => f.Key.Equals(Location(1)));
        world.AdvanceTo(53); var incident = Assert.Single(world.InspectMissingReports());
        Assert.Equal("ReportedComplete", incident.WorkResolution); Assert.Equal("PatrolPosted", incident.WorkerStatusResponse);
        world.AdvanceTo(54); incident = Assert.Single(world.InspectMissingReports()); Assert.Equal("ReportedFound", incident.WorkerStatusResponse);
        Assert.Equal(FactValue.At(Fixtures.C), world.InspectBeliefs().Single(f => f.Key.Equals(Location(1))).Value);
        Assert.Equal(1, world.InspectAdmissionBuckets().Sum(b => b.Used)); Assert.True(incident.ReplacementAttemptId > 0); Assert.True(incident.PatrolOrderMessageId > 0);
        Assert.Equal(LocalPatrolStatus.Completed, world.InspectLocalPatrols(new(3))[0].Status); Assert.Equal(0, world.ActionsFailed);
        Assert.Equal(world.Serialize(), World.Restore(world.Serialize()).Serialize());
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(1, run.Result.Metrics["incidentsResolvedByReports"]);
        var received = Assert.Single(run.World.Trace, e => e.Type == "PatrolResultReceived"); var chain = TraceChains.Select(run.World.Trace, received.Id);
        Assert.Contains(chain, e => e.Type == "MissingWorkReportOpened"); Assert.Contains(chain, e => e.Type == "PatrolCandidateInspected");
        Assert.Contains(chain, e => e.Type == "MessageRead" && e.ActorId == 3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void EitherBranchCanProceedAndTheLaterBranchReusesItsOriginalAdmission(int delayedActor)
    {
        var loaded = Load(json => json["initialState"]!["routines"]!["assignments"]![delayedActor - 1]!["cohort"] = 1);
        var world = loaded.CreateWorld(42); world.AdvanceTo(55);
        var incident = Assert.Single(world.InspectMissingReports()); Assert.Equal(25, incident.AdmissionTick); Assert.Equal(1, world.InspectAdmissionBuckets().Sum(b => b.Used));
        if (delayedActor == 2) { Assert.Equal(0, incident.ReplacementAttemptId); Assert.Equal("ReportedFound", incident.WorkerStatusResponse); Assert.False(world.InspectMachines()[0].Operational); }
        else { Assert.Equal(0, incident.PatrolOrderMessageId); Assert.Equal("ReportedComplete", incident.WorkResolution); }
        world.ScheduleRosterChange(60, new(new(delayedActor), 0, Fixtures.A), "become-available"); world.AdvanceTo(60);
        incident = Assert.Single(world.InspectMissingReports()); Assert.True(incident.PatrolOrderMessageId > 0); Assert.True(incident.ReplacementAttemptId > 0);
        Assert.Equal(25, incident.AdmissionTick); Assert.Equal(1, world.InspectAdmissionBuckets().Sum(b => b.Used));
        Assert.Equal(world.Serialize(), World.Restore(world.Serialize()).Serialize());
    }

    [Fact]
    public void ExhaustedSearchReportsLocalNegativeEvidenceWithoutLocatingOrCondemningTarget()
    {
        var loaded = Load(json => json["scriptedActors"]![0]!["commands"]!.AsArray().Add(JsonNode.Parse("{\"tick\":43,\"kind\":\"Move\",\"target\":{\"x\":0,\"y\":0,\"z\":0}}")));
        var world = loaded.CreateWorld(42); world.AdvanceTo(70);
        Assert.Equal(Fixtures.A, world.InspectActor(new(1)).Position);
        var local = Assert.Single(world.InspectLocalPatrols(new(3))); Assert.Equal(3, local.Observations.Count); Assert.All(local.Observations, o => Assert.False(o.Found));
        Assert.Equal("ReportedNotLocated", Assert.Single(world.InspectMissingReports()).WorkerStatusResponse);
        Assert.Equal(FactValue.Absent(Fixtures.C), world.InspectBeliefs().Single(f => f.Key.Equals(Location(1))).Value);
        Assert.Equal(world.Serialize(), World.Restore(world.Serialize()).Serialize());
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("credential")]
    [InlineData("terminal")]
    public void FailedOrCancelledReadDoesNotGrantPatrolAwareness(string failure)
    {
        var world = Load(json => { json["initialState"]!["information"]!["configuration"]!["readBaseTicks"] = 4; json["initialState"]!["information"]!["configuration"]!["readMaximumTicks"] = 4; }).CreateWorld(42);
        long started = 0;
        for (long tick = 27; tick < 80; tick++) { world.AdvanceTo(tick); if (world.InspectActor(new(3)).Action == ActionKind.ReadInbox && world.InspectActor(new(3)).ActionStart >= 27) { started = tick; break; } }
        Assert.True(started > 0);
        if (failure == "cancel") world.Submit(started + 1, new(new(3), ActionKind.Cancel, Fixtures.A));
        else if (failure == "credential") world.ScheduleCredentialRevocation(started + 1, 3);
        else world.ScheduleTerminalChange(started + 1, 3, false, false);
        var resumed = World.Restore(world.Serialize()); world.AdvanceTo(started + 4); resumed.AdvanceTo(started + 4);
        Assert.Equal(world.Digest(), resumed.Digest()); Assert.Empty(world.InspectLocalPatrols(new(3)));
        Assert.Equal("Posted", Assert.Single(world.InspectPatrolOrders()).Status);
        Assert.Null(world.InspectMessages().Single(m => m.Draft.Kind == MessageKind.PatrolOrder).FirstReadTick);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public void KnownExpiryProducesBlockedReportAndStopsSearch(long lease)
    {
        var world = Load(json => json["initialState"]!["authority"]!["responseOrderTicks"] = lease).CreateWorld(42); world.AdvanceTo(80);
        var local = Assert.Single(world.InspectLocalPatrols(new(3))); Assert.Equal(LocalPatrolStatus.Completed, local.Status);
        Assert.Equal("KnownOrderExpired", local.Reason); Assert.All(local.Observations, o => Assert.False(o.Found));
        Assert.Equal("ReportedBlocked", Assert.Single(world.InspectMissingReports()).WorkerStatusResponse);
        Assert.Equal(world.Serialize(), World.Restore(world.Serialize()).Serialize());
    }

    [Fact]
    public void RepeatedResultAndWrongAccountDoNotOverwriteFirstPatrolEvidence()
    {
        var world = Load().CreateWorld(42); world.AdvanceTo(199); var order = Assert.Single(world.InspectPatrolOrders());
        long first = order.ResultMessageId; int beliefs = world.InspectBeliefHistory().Count;
        void Post(long actor, long service, long tick)
        {
            world.Submit(tick, new(new(actor), ActionKind.Login, Fixtures.A, serviceId: service));
            world.Submit(tick + 1, new(new(actor), ActionKind.PostMessage, Fixtures.A, serviceId: service, message:
                new MessageDraft(MessageKind.PatrolFound, RecipientKind.Authority, default, Location(1), FactValue.At(Fixtures.B), tick, relatedMessageId: order.OrderMessageId, operationKey: "repeat-" + actor)));
        }
        Post(3, 3, 200); Post(2, 2, 210); world.AdvanceTo(220);
        Assert.Equal(first, world.InspectPatrolOrders()[0].ResultMessageId); Assert.Equal(beliefs, world.InspectBeliefHistory().Count);
        Assert.Equal(FactValue.At(Fixtures.C), world.InspectBeliefs().Single(f => f.Key.Equals(Location(1))).Value);
        Assert.Equal(world.Serialize(), World.Restore(world.Serialize()).Serialize());
    }

    [Fact]
    public void FiniteInspectionFinishesAcrossShiftEndAndSearchResumesOnKnownDuty()
    {
        var world = Load(json => json["initialState"]!["information"]!["configuration"]!["inspectTicks"] = 70).CreateWorld(42, true);
        world.AdvanceTo(160); var local = Assert.Single(world.InspectLocalPatrols(new(3)));
        Assert.Equal(LocalPatrolStatus.Searching, local.Status); Assert.Single(local.Observations); Assert.False(local.Observations[0].Found);
        Assert.True(local.Observations[0].Tick > 100);
        var resumed = World.Restore(world.Serialize(), true); world.AdvanceTo(500); resumed.AdvanceTo(500);
        Assert.Equal(world.Digest(), resumed.Digest()); Assert.Equal("ReportedFound", Assert.Single(world.InspectMissingReports()).WorkerStatusResponse);
        Assert.Equal(3, world.InspectLocalPatrols(new(3))[0].Observations.Count);
        Assert.DoesNotContain(world.Trace, e => e.ActorId == 3 && e.Type == "ActionRequested" && e.Detail == "InspectActor" && e.Tick > 100 && e.Tick < 300);
    }

    [Fact]
    public void GeneratedPatrolContinuationsTraceModesAndConcurrentWorldsMatch()
    {
        var digests = new string[20];
        LoadedScenario Variant(int i) => Load(json => { json["initialState"]!["information"]!["configuration"]!["inspectTicks"] = 1 + i % 3; json["initialState"]!["information"]!["configuration"]!["latencyTicks"] = 1 + i % 3; });
        for (int i = 0; i < 20; i++)
        {
            var scenario = Variant(i); var direct = scenario.CreateWorld((ulong)i, true); direct.AdvanceTo(110); digests[i] = direct.Digest();
            Assert.Single(direct.InspectPatrolOrders()); Assert.Equal("ReportedFound", Assert.Single(direct.InspectMissingReports()).WorkerStatusResponse);
            foreach (long tick in new long[] { -1, 24, 25, 27, 37, 38, 40, 41, 44, 47, 53, 100 })
            {
                var prefix = scenario.CreateWorld((ulong)i, true); if (tick >= 0) prefix.AdvanceTo(tick);
                var bytes = prefix.Serialize(); var resumed = World.Restore(bytes, true); Assert.Equal(bytes, resumed.Serialize()); resumed.AdvanceTo(110);
                Assert.Equal(digests[i], resumed.Digest()); Assert.Equal(direct.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), resumed.Trace.Select(Runs.EventJson));
            }
            var chunked = scenario.CreateWorld((ulong)i, false); for (int tick = 0; tick <= 110; tick++) { chunked.AdvanceTo(tick); chunked.InspectPatrolOrders(); chunked.InspectLocalPatrols(new(3)); }
            Assert.Equal(digests[i], chunked.Digest());
        }
        foreach (int concurrency in new[] { 2, 4, 8 }) Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
        { var world = Variant(i).CreateWorld((ulong)i); world.AdvanceTo(110); Assert.Equal(digests[i], world.Digest()); });
    }
}
