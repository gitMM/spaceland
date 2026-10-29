using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class GuardSearchTests
{
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "guard_search.json")))!.AsObject(); change?.Invoke(j);
        if (change != null) j["assertions"] = new JsonArray();
        return new(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "guard-search-test");
    }
    private static LocalGuardOrderSnapshot Local(World w, long actor = 3) => Assert.Single(w.InspectLocalGuardOrders(new(actor)));
    private static LocalGuardTeamSnapshot Team(World w) => Assert.Single(w.InspectLocalGuardTeams());
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());

    [Fact]
    public void IndividualFiniteJoinsPrecedeLocalFormationAndTargetInspection()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(234); Assert.Empty(w.InspectLocalGuardTeams()); Assert.Equal(ActionKind.JoinGuardTeam, w.InspectActor(new(3)).Action); RoundTrip(w);
        w.AdvanceTo(290); Assert.Equal(GuardOrderStatus.WaitingTeam, Local(w).Status); Assert.Single(Team(w).Members); Assert.Equal(-1, Team(w).FormedTick); Assert.Equal(-1, Local(w, 4).JoinTick); RoundTrip(w);
        w.AdvanceTo(293); Assert.Equal(293, Team(w).FormedTick); Assert.Equal(2, Team(w).Members.Count); Assert.Equal(GuardOrderStatus.Searching, Local(w).Status);
        Assert.True(w.InspectActor(new(3)).InTransit); Assert.True(w.InspectActor(new(4)).InTransit); Assert.Empty(Local(w).SearchObservations); RoundTrip(w);
        w.AdvanceTo(294); Assert.Equal(ActionKind.InspectActor, w.InspectActor(new(3)).Action); Assert.Empty(Local(w).SearchObservations);
        w.AdvanceTo(393); Assert.Empty(Local(w).SearchObservations); RoundTrip(w);
        w.AdvanceTo(394); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); var found = Assert.Single(Local(w).SearchObservations); Assert.True(found.Found); Assert.Equal(394, found.Tick); Assert.Equal(new Coord(1, 0, 0), found.Tile); RoundTrip(w);
        Assert.Equal(EscortDispatchStatus.TeamForming, Assert.Single(w.InspectEscortDispatches()).Status);
        var run = Runs.Execute(Load(), 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(0, run.World.ActionsFailed);
    }

    [Fact]
    public void MissingArmedPartnerLeavesAnIndividualWaitingWithoutRemotePull()
    {
        var w = Load(j => { var item = j["initialState"]!["items"]!.AsArray().Single(i => i!["id"]!.GetValue<int>() == 22)!; item["locationKind"] = "ActorInventory"; item["owner"] = 1; }).CreateWorld(42); w.AdvanceTo(734);
        Assert.Equal(-1, Team(w).FormedTick); Assert.Single(Team(w).Members); Assert.Equal(GuardOrderStatus.WaitingTeam, Local(w).Status); Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w, 4).Status);
        Assert.Empty(Local(w).SearchObservations); Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(3)).CurrentTile); RoundTrip(w);
    }

    [Fact]
    public void CancellationCannotFabricateACompletedJoin()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(234); w.Submit(235, new(new(4), ActionKind.Cancel, new(0, 0, 0))); w.AdvanceTo(293);
        Assert.Equal(-1, Team(w).FormedTick); Assert.Single(Team(w).Members); Assert.Equal(-1, Local(w, 4).JoinTick); Assert.Empty(Local(w).SearchObservations); RoundTrip(w);
        w.AdvanceTo(534); Assert.True(Team(w).FormedTick > 359); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); RoundTrip(w);
    }

    [Fact]
    public void DepartureWithdrawsAnUnformedJoinUntilTheGuardPhysicallyReturns()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(290); w.Submit(291, new(new(3), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(291);
        Assert.Empty(Team(w).Members); Assert.Equal(-1, Local(w).JoinTick); Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w).Status); RoundTrip(w);
        w.AdvanceTo(293); Assert.Single(Team(w).Members); Assert.Equal(4, Team(w).Members[0].ActorId); Assert.Equal(-1, Team(w).FormedTick); RoundTrip(w);
        w.AdvanceTo(734); Assert.True(Team(w).FormedTick > 359); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); RoundTrip(w);
    }

    [Fact]
    public void EquipmentLossWithdrawsAPendingJoinAndRequiresReequipping()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(290); w.Submit(291, new(new(3), ActionKind.PutDown, new(0, 0, 0), new(21))); w.AdvanceTo(292);
        Assert.Equal(0, w.ActorView(new(3)).EquippedItem.Value); Assert.Empty(Team(w).Members); Assert.Equal(-1, Local(w).JoinTick); RoundTrip(w);
        w.AdvanceTo(734); Assert.True(Team(w).FormedTick >= 443); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); RoundTrip(w);
    }

    [Fact]
    public void UnreadGuardCannotJoinEvenWithARequestedActionTag()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); long order = Assert.Single(w.InspectEscortDispatches()).OrderMessageIds[0];
        w.Submit(115, new(new(3), ActionKind.JoinGuardTeam, new(0, 0, 0), guardOrderId: order)); w.AdvanceTo(115);
        Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(3)).LastFailure); Assert.Empty(w.InspectLocalGuardTeams()); Assert.Empty(w.InspectLocalGuardOrders(new(3))); RoundTrip(w);
    }

    [Fact]
    public void ExpiryDuringJoinCannotCompleteFormation()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 179).CreateWorld(42); w.AdvanceTo(290); Assert.Single(Team(w).Members);
        w.AdvanceTo(292); Assert.Equal(GuardOrderStatus.Expired, Local(w).Status); Assert.Empty(Team(w).Members); RoundTrip(w);
        w.AdvanceTo(334); Assert.Equal(-1, Team(w).FormedTick); Assert.Empty(Local(w).SearchObservations); Assert.Equal(GuardOrderStatus.Expired, Local(w, 4).Status); RoundTrip(w);
    }

    [Fact]
    public void ExpiryDuringInspectionKeepsTheOrderExpiredWithoutAProtocolResult()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 200).CreateWorld(42); w.AdvanceTo(313);
        Assert.Equal(GuardOrderStatus.Expired, Local(w).Status); Assert.Equal(ActionKind.InspectActor, w.InspectActor(new(3)).Action); RoundTrip(w);
        w.AdvanceTo(394); Assert.Empty(Local(w).SearchObservations); Assert.Equal(GuardOrderStatus.Expired, Local(w).Status);
        Assert.Contains(w.InspectKnowledge(new(3)), f => f.Key.Equals(new FactKey(SubjectKind.Actor, 1, AttributeKind.Location)) && f.SourceKind == EvidenceKind.Inspection); RoundTrip(w);
    }

    [Fact]
    public void OfflineGuardsFormAndSearchUsingTheirReadCopies()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(140, 3, false, false); w.ScheduleTerminalChange(140, 4, false, false); w.AdvanceTo(734);
        Assert.Equal(293, Team(w).FormedTick); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w, 4).Status); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }

    [Fact]
    public void StaleCandidateProducesLocalAbsenceWithoutFindingTheDistantTarget()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); w.Submit(115, new(new(1), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(734);
        foreach (long guard in new long[] { 3, 4 }) { var local = Local(w, guard); Assert.Equal(GuardOrderStatus.SearchExhausted, local.Status); Assert.False(Assert.Single(local.SearchObservations).Found); Assert.Equal("TargetNotLocated", local.Reason); }
        Assert.Equal(new Coord(2, 0, 0), w.ActorView(new(1)).CurrentTile); Assert.Equal(EscortDispatchStatus.AwaitingGuards, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Equal(2, w.InspectGuardOutcomes(true).Count(o => o.Outcome == "TargetNotLocated")); RoundTrip(w);
    }

    [Fact]
    public void TargetLeavingDuringInspectionIsAbsentAtCompletion()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(334); w.Submit(335, new(new(1), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(394);
        Assert.False(Assert.Single(Local(w).SearchObservations).Found); Assert.Equal(GuardOrderStatus.SearchExhausted, Local(w).Status); RoundTrip(w);
    }

    [Fact]
    public void AnotherKnownCandidateIsSearchedThroughPhysicalMovementAndInspection()
    {
        var w = Load(j => {
            var machine = j["initialState"]!["work"]!["machines"]![0]!.DeepClone(); machine["id"] = 3; machine["position"]!["x"] = 2;
            j["initialState"]!["work"]!["machines"]!.AsArray().Add(machine);
            j["initialState"]!["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 3, ["machineId"] = 3 });
        }).CreateWorld(42); w.AdvanceTo(103); w.ScheduleWorkAssignment(104, new("known-other-site", 3, new(1), 1000)); w.AdvanceTo(114);
        Assert.Equal(new[] { new Coord(1, 0, 0), new Coord(2, 0, 0) }, Assert.Single(w.InspectEscortDispatches()).Payload!.Candidates);
        w.Submit(115, new(new(1), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(734);
        foreach (long guard in new long[] { 3, 4 }) { var observations = Local(w, guard).SearchObservations; Assert.Equal(2, observations.Count); Assert.False(observations[0].Found); Assert.True(observations[1].Found); Assert.True(observations[1].Tick >= observations[0].Tick + 101); }
        RoundTrip(w);
    }

    [Fact]
    public void LocatedTargetRemainsFreeAndItsLaterDepartureDoesNotRewriteCapturedEvidence()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(394); var observation = Assert.Single(Local(w).SearchObservations);
        w.Submit(395, new(new(1), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(396);
        Assert.Equal(new Coord(2, 0, 0), w.ActorView(new(1)).CurrentTile); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); Assert.Equal(observation.Tick, Assert.Single(Local(w).SearchObservations).Tick);
        Assert.Equal(new Coord(1, 0, 0), w.ActorView(new(3)).CurrentTile); RoundTrip(w);
    }

    [Fact]
    public void PriorLocationKnowledgeCannotReplaceTheFiniteIdentificationAction()
    {
        var w = Load(j => j["initialState"]!["information"]!["facts"]!.AsArray().Add(new JsonObject {
            ["actorId"] = 3, ["key"] = new JsonObject { ["subjectKind"] = "Actor", ["subjectId"] = 1, ["attribute"] = "Location" },
            ["value"] = new JsonObject { ["kind"] = "Coordinate", ["coordinate"] = new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 } } })).CreateWorld(42);
        w.AdvanceTo(393); Assert.Empty(Local(w).SearchObservations); Assert.Equal(GuardOrderStatus.Searching, Local(w).Status);
        var key = new FactKey(SubjectKind.Actor, 1, AttributeKind.Location); Assert.Equal(EvidenceKind.Seeded, w.InspectKnowledge(new(3)).Single(f => f.Key.Equals(key)).SourceKind);
        w.AdvanceTo(394); var fact = w.InspectKnowledge(new(3)).Single(f => f.Key.Equals(key)); Assert.Equal(EvidenceKind.Inspection, fact.SourceKind); Assert.Equal(Assert.Single(Local(w).SearchObservations).ActionId, fact.SourceId); RoundTrip(w);
    }

    [Fact]
    public void ACoLocatedBystanderCannotBeMistakenForTheNamedTarget()
    {
        var w = Load(j => j["initialState"]!["actors"]!.AsArray().Add(new JsonObject { ["id"] = 5, ["name"] = "Bystander", ["role"] = "visitor", ["position"] = new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 } })).CreateWorld(42);
        w.AdvanceTo(114); w.Submit(115, new(new(1), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(394);
        Assert.Equal(new Coord(1, 0, 0), w.ActorView(new(5)).CurrentTile); Assert.Equal(GuardOrderStatus.SearchExhausted, Local(w).Status); Assert.False(Assert.Single(Local(w).SearchObservations).Found); RoundTrip(w);
    }

    [Fact]
    public void EquipmentLossAfterFindingTargetRequiresAnotherSearchObservation()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(394); w.Submit(395, new(new(3), ActionKind.Cancel, new(1, 0, 0))); w.Submit(396, new(new(3), ActionKind.PutDown, new(1, 0, 0), new(21))); w.AdvanceTo(397);
        Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w).Status); Assert.Empty(Local(w).SearchObservations); Assert.Equal(0, Local(w).CandidateIndex); RoundTrip(w);
        w.AdvanceTo(734); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); Assert.True(Assert.Single(Local(w).SearchObservations).Tick > 460); Assert.Equal(293, Team(w).FormedTick); RoundTrip(w);
    }

    [Fact]
    public void SnapshotContinuationIncludesPendingJoinFormationSearchAndExpiry()
    {
        foreach (int seed in Enumerable.Range(0, 8)) foreach (int tick in new[] { 190, 234, 289, 290, 292, 293, 294, 313, 393, 394, 534 })
        {
            var w = Load(j => { j["initialState"]!["information"]!["configuration"]!["inspectTicks"] = 80 + seed * 5; j["initialState"]!["detention"]!["orderTicks"] = seed % 3 == 0 ? 200 : 2000; }).CreateWorld((ulong)seed, true);
            w.AdvanceTo(tick); var bytes = w.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
            w.AdvanceTo(734); copy.AdvanceTo(734); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void CorruptedLocalJoinProofCannotRestoreAFormedTeam()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(734); var m = Team(w).Members[0]; var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) { writer.Write(m.ActorId); writer.Write(m.OrderMessageId); writer.Write(m.Tick); writer.Write(m.ActionId); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var edit in new[] { (Offset: 0, Value: 1L), (Offset: 8, Value: 0L), (Offset: 16, Value: m.Tick + 1), (Offset: 24, Value: 0L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(edit.Value).CopyTo(bad, start + edit.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
        var exposed = (GuardJoinSnapshot[])Team(w).Members; exposed[0] = exposed[1]; Assert.Equal(3, Team(w).Members[0].ActorId);
    }

    [Fact]
    public void ConcurrentAndChunkedLocalSearchIsDeterministic()
    {
        string Run(int seed, bool chunked)
        {
            var w = Load(j => j["initialState"]!["information"]!["configuration"]!["inspectTicks"] = 20 + seed * 11).CreateWorld((ulong)seed);
            if (chunked) for (int tick = 0; tick < 800; tick += 13) { w.AdvanceTo(tick); _ = w.InspectLocalGuardTeams(); _ = w.InspectLocalGuardOrders(new(3)); }
            w.AdvanceTo(800); return w.Digest();
        }
        var expected = Enumerable.Range(0, 10).Select(s => Run(s, false)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[10]; Parallel.For(0, 10, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, true)); Assert.Equal(expected, actual); }
    }
}
