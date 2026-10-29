using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class EscortMovementTests
{
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "guard_search.json")))!.AsObject();
        j["assertions"] = new JsonArray(); change?.Invoke(j);
        return new(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "escort-movement-test");
    }
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    private static EscortSnapshot Group(World w) => Assert.Single(w.InspectEscorts());
    private static World Moving(Action<JsonObject>? change = null)
    {
        var w = Load(change).CreateWorld(42, true); w.ScheduleTraversalChange(484, new(2, 0, 0), 20); w.AdvanceTo(494); return w;
    }
    [Fact]
    public void FiniteLocalBeginPrecedesCustodyAndSharedDeparture()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(394);
        Assert.Empty(w.InspectEscorts()); Assert.Equal(ActionKind.BeginEscort, w.InspectActor(new(3)).Action); Assert.Equal(494, w.InspectActor(new(3)).ActionCompletion); RoundTrip(w);
        w.AdvanceTo(493); Assert.Empty(w.InspectEscorts());
        w.AdvanceTo(494); Assert.Equal(EscortStatus.Escorting, Group(w).Status); Assert.Equal(new long[] { 1, 3, 4 }, Group(w).Members.Select(m => m.ActorId));
        foreach (long id in new long[] { 1, 3, 4 }) { var actor = w.InspectActor(new(id)); Assert.True(actor.InTransit); Assert.Equal(494, actor.Departure); Assert.Equal(495, actor.Arrival); Assert.Null(w.ActorView(new(id)).CurrentTile); }
        Assert.Equal(EscortDispatchStatus.TeamForming, Assert.Single(w.InspectEscortDispatches()).Status); RoundTrip(w);
        w.AdvanceTo(495); Assert.Equal(EscortStatus.AwaitingIntake, Group(w).Status);
        foreach (long id in new long[] { 1, 3, 4 }) Assert.Equal(new Coord(2, 0, 0), w.ActorView(new(id)).CurrentTile);
        Assert.DoesNotContain(w.Trace, e => e.Type == "Detained"); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void FrozenEdgeTimingSurvivesTraversalChangesForEveryMember()
    {
        var w = Moving(); w.ScheduleTraversalChange(499, new(2, 0, 0), 100); w.AdvanceTo(504);
        foreach (long id in new long[] { 1, 3, 4 }) { var a = w.InspectActor(new(id)); Assert.Equal(505, a.Arrival); Assert.Equal(10, a.ProgressNumerator); Assert.Equal(11, a.ProgressDenominator); } RoundTrip(w);
        w.AdvanceTo(505); Assert.Equal(EscortStatus.AwaitingIntake, Group(w).Status); RoundTrip(w);
    }
    [Theory]
    [InlineData(ActionKind.Move)]
    [InlineData(ActionKind.Cancel)]
    [InlineData(ActionKind.Login)]
    [InlineData(ActionKind.Logout)]
    [InlineData(ActionKind.ResumeWork)]
    public void CooperativeTargetCannotEscapeOrUseOrdinaryInputs(ActionKind kind)
    {
        var w = Moving(); w.Submit(495, new(new(1), kind, new(1, 0, 0), serviceId: 1)); w.AdvanceTo(495);
        Assert.Equal(FailureReason.CustodyRestricted, w.InspectActor(new(1)).LastFailure); Assert.Equal(EscortStatus.Escorting, Group(w).Status); RoundTrip(w);
        w.AdvanceTo(505); w.Submit(506, new(new(1), kind, new(2, 0, 0), serviceId: 1)); w.AdvanceTo(506);
        Assert.Equal(FailureReason.CustodyRestricted, w.InspectActor(new(1)).LastFailure); Assert.Equal(new Coord(2, 0, 0), w.ActorView(new(1)).CurrentTile); RoundTrip(w);
    }
    [Fact]
    public void GuardCancellationStopsTheWholeGroupAtTheSafeBoundary()
    {
        var w = Moving(); w.Submit(495, new(new(4), ActionKind.Cancel, new(1, 0, 0))); w.AdvanceTo(504);
        Assert.Equal("MemberCancelled", Group(w).Interruption); Assert.All(Group(w).Members, m => Assert.True(w.InspectActor(new(m.ActorId)).InTransit)); RoundTrip(w);
        w.AdvanceTo(505); Assert.Equal(EscortStatus.Interrupted, Group(w).Status); Assert.All(Group(w).Members, m => Assert.Equal(new Coord(2, 0, 0), w.InspectActor(new(m.ActorId)).Position)); Assert.Equal(2, w.InspectGuardOutcomes().Count(o => o.Outcome == "EscortInterrupted")); RoundTrip(w);
        w.Submit(506, new(new(1), ActionKind.Move, new(1, 0, 0))); w.AdvanceTo(517); Assert.Equal(new Coord(1, 0, 0), w.ActorView(new(1)).CurrentTile); RoundTrip(w);
    }
    [Fact]
    public void KnownExpiryWaitsForSharedArrivalThenEndsCustody()
    {
        var w = Moving(j => j["initialState"]!["detention"]!["orderTicks"] = 387); w.AdvanceTo(500);
        Assert.Equal("KnownOrderExpired", Group(w).Interruption); Assert.Equal(EscortStatus.Escorting, Group(w).Status); RoundTrip(w);
        w.AdvanceTo(505); Assert.Equal(EscortStatus.Interrupted, Group(w).Status); Assert.False(w.InspectActor(new(1)).InTransit); Assert.All(Group(w).Members, m => Assert.Equal(new Coord(2, 0, 0), w.InspectActor(new(m.ActorId)).Position)); Assert.Equal(2, w.InspectGuardOutcomes().Count(o => o.Outcome == "EscortInterrupted")); RoundTrip(w);
    }
    [Fact]
    public void TargetDepartureDuringBeginCannotBeReplacedByOldIdentification()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(394); w.Submit(395, new(new(1), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(494);
        Assert.Empty(w.InspectEscorts()); Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(new(3)).LastFailure); Assert.Equal(new Coord(2, 0, 0), w.ActorView(new(1)).CurrentTile); RoundTrip(w);
    }
    [Fact]
    public void PartnerLosingEquipmentDuringBeginPreventsCustody()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(394); w.Submit(395, new(new(4), ActionKind.PutDown, new(1, 0, 0), new(22))); w.AdvanceTo(494);
        Assert.Empty(w.InspectEscorts()); Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(3)).LastFailure); RoundTrip(w);
    }
    [Fact]
    public void OfflineCopiesAuthorizePhysicalEscortWithoutAuthorityProgressPush()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(140, 3, false, false); w.ScheduleTerminalChange(140, 4, false, false); w.AdvanceTo(734);
        Assert.Equal(EscortStatus.AwaitingIntake, Group(w).Status); Assert.Equal(EscortDispatchStatus.TeamForming, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void CheckpointResumptionPreservesAtomicMovementAndCancellation()
    {
        foreach (int tick in new[] { 394, 493, 494, 499, 500, 504, 505, 734 }) foreach (bool expire in new[] { false, true })
        {
            var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = expire ? 387 : 2000).CreateWorld(42, true);
            w.ScheduleTraversalChange(484, new(2, 0, 0), 20); w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); RoundTrip(w);
            w.AdvanceTo(2134); copy.AdvanceTo(2134); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }
    [Fact]
    public void GroupInspectionDoesNotExposeMutableMembership()
    {
        var w = Moving(); var members = (EscortMemberSnapshot[])Group(w).Members; members[0] = members[1];
        Assert.Equal(1, Group(w).Members[0].ActorId); RoundTrip(w);
    }
    [Fact]
    public void MultipleEdgesDepartAndArriveTogetherUsingTheNextEdgesCurrentCost()
    {
        var w = Load(j => {
            var tile = j["initialState"]!["tiles"]![2]!.DeepClone(); tile["id"] = 4; tile["position"]!["x"] = 3;
            j["initialState"]!["tiles"]!.AsArray().Add(tile); j["initialState"]!["detention"]!["escort"]!["destination"]!["x"] = 3;
        }).CreateWorld(42, true);
        w.ScheduleTraversalChange(484, new(2, 0, 0), 20); w.ScheduleTraversalChange(499, new(3, 0, 0), 40); w.AdvanceTo(505);
        foreach (long id in new long[] { 1, 3, 4 }) { var a = w.InspectActor(new(id)); Assert.True(a.InTransit); Assert.Equal(new Coord(2, 0, 0), a.Source); Assert.Equal(505, a.Departure); Assert.Equal(535, a.Arrival); } RoundTrip(w);
        w.AdvanceTo(535); Assert.Equal(EscortStatus.AwaitingIntake, Group(w).Status); Assert.All(Group(w).Members, m => Assert.Equal(new Coord(3, 0, 0), w.ActorView(new(m.ActorId)).CurrentTile)); RoundTrip(w);
    }
    [Fact]
    public void CustodyCancelsTargetsFiniteActionWithoutConsumingEquipmentOrLeavingClaims()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(464); w.Submit(465, new(new(1), ActionKind.Equip, new(1, 0, 0), new(2))); w.AdvanceTo(493);
        Assert.Equal(ActionKind.Equip, w.InspectActor(new(1)).Action); Assert.Contains(w.InspectResourceClaims(), c => c.ActorId == 1); RoundTrip(w);
        w.AdvanceTo(494); Assert.Equal(EscortStatus.Escorting, Group(w).Status); Assert.Equal(0, w.ActorView(new(1)).EquippedItem.Value);
        Assert.Contains(new Id<ItemTag>(2), w.ActorView(new(1)).Inventory); Assert.DoesNotContain(w.InspectResourceClaims(), c => c.ActorId == 1); RoundTrip(w);
        w.AdvanceTo(515); Assert.Equal(0, w.ActorView(new(1)).EquippedItem.Value); w.AssertAccounting(); RoundTrip(w);
    }
    [Fact]
    public void CustodySuspendsReadWorkWithoutCancellingTheInstitutionalAssignment()
    {
        var w = Load(j => j["initialState"]!["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 100, ["machineId"] = 1 })).CreateWorld(42, true);
        w.ScheduleWorkAssignment(300, new("unfinished-at-intake", 100, new(1), 10000)); w.AdvanceTo(414); w.Submit(487, new(new(1), ActionKind.Logout, new(1, 0, 0)));
        w.Submit(489, new(new(1), ActionKind.Login, new(1, 0, 0), serviceId: 1)); w.Submit(492, new(new(1), ActionKind.ReadInbox, new(1, 0, 0), serviceId: 1)); w.AdvanceTo(493);
        var working = w.InspectLocalOrders(new(1)).Where(o => o.Status is LocalOrderStatus.Read or LocalOrderStatus.AcknowledgmentPending or LocalOrderStatus.Working).Select(o => o.AttemptId).ToArray();
        Assert.True(working.Length > 0, JsonData.Write(w.InspectLocalOrders(new(1))) + JsonData.Write(w.InspectActor(new(1)))); w.AdvanceTo(494); Assert.Equal(EscortStatus.Escorting, Group(w).Status);
        foreach (long id in working)
        {
            Assert.Equal(LocalOrderStatus.Suspended, w.InspectLocalOrders(new(1)).Single(o => o.AttemptId == id).Status);
            Assert.Equal(AssignmentStatus.Active, w.InspectWorkAttempts().Single(a => a.Id == id).Assignment);
            Assert.Equal(WorkExecution.Waiting, w.InspectWorkAttempts().Single(a => a.Id == id).Execution);
        }
        RoundTrip(w);
    }
    [Fact]
    public void CorruptedMembershipAndMovementIdentityAreRejectedOnRestore()
    {
        var w = Moving(); var g = Group(w); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var b = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        {
            b.Write(g.AuthorizationId); b.Write(g.DispatchId); b.Write(g.BeginActionId); b.Write(g.StartedTick); b.Write(g.BoundaryTick); b.Write((int)g.Status); b.Write(g.Interruption); b.Write(g.Members.Count);
            foreach (var m in g.Members) { b.Write(m.ActorId); b.Write(m.OrderMessageId); b.Write(m.MovementActionId); }
        }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var edit in new[] { (Offset: 16, Value: 0L), (Offset: 24, Value: 0L), (Offset: 32, Value: 1000L), (Offset: 49, Value: 3L), (Offset: 65, Value: 0L) })
        { var corrupt = bytes.ToArray(); BitConverter.GetBytes(edit.Value).CopyTo(corrupt, start + edit.Offset); Assert.Throws<ArgumentException>(() => World.Restore(corrupt)); }
    }
    [Fact]
    public void DutyBoundaryCannotSplitAnUrgentEscortOrIssueTargetResistance()
    {
        var w = Moving(j => j["initialState"]!["routines"]!["configuration"]!["dutyTicks"] = 570); w.AdvanceTo(504);
        Assert.Equal("", Group(w).Interruption); Assert.All(Group(w).Members, m => Assert.True(w.InspectActor(new(m.ActorId)).InTransit)); RoundTrip(w);
        w.AdvanceTo(505); Assert.Equal(EscortStatus.AwaitingIntake, Group(w).Status); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void BeginningAtTheDestinationCreatesCustodyWithoutInventedTravelOrIntake()
    {
        var w = Load(j => j["initialState"]!["detention"]!["escort"]!["destination"]!["x"] = 1).CreateWorld(42); w.AdvanceTo(494);
        Assert.Equal(EscortStatus.AwaitingIntake, Group(w).Status); Assert.All(Group(w).Members, m => Assert.Equal(0, m.MovementActionId));
        Assert.All(Group(w).Members, m => Assert.False(w.InspectActor(new(m.ActorId)).InTransit)); Assert.DoesNotContain(w.InspectSessions(), s => s.ActorId == 1); RoundTrip(w);
        w.AdvanceTo(2113); Assert.Equal(EscortStatus.Interrupted, Group(w).Status); RoundTrip(w);
    }
    [Fact]
    public void ExpiryDuringFiniteHandoffCannotEstablishCustody()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 350).CreateWorld(42); w.AdvanceTo(463);
        Assert.Empty(w.InspectEscorts()); Assert.Equal(ActionKind.BeginEscort, w.InspectActor(new(3)).Action); RoundTrip(w);
        w.AdvanceTo(494); Assert.Empty(w.InspectEscorts()); Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(3)).LastFailure); RoundTrip(w);
    }
}
