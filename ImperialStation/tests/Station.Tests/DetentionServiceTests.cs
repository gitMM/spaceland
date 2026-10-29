using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class DetentionServiceTests
{
    private static JsonObject Scenario()
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "escort_movement.json")))!.AsObject(); j["assertions"] = new JsonArray();
        j["initialState"]!["detention"]!["facilities"] = new JsonArray(new JsonObject { ["id"] = 1, ["position"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 }, ["capacity"] = 1 }); return j;
    }
    private static LoadedScenario Load(Action<JsonObject>? change = null) { var j = Scenario(); change?.Invoke(j); return Loaded(j); }
    private static LoadedScenario Loaded(JsonObject j) => new(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "detention-service-test");
    private static DetentionStaySnapshot Stay(World w) => Assert.Single(w.InspectDetentionStays());
    private static DetentionFacilitySnapshot Facility(World w) => Assert.Single(w.InspectDetentionFacilities());
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void FiniteIntakeReservationBecomesOccupancyOnlyOnSuccessfulLocalCompletion()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(494); Assert.Empty(w.InspectDetentionStays());
        w.AdvanceTo(495); Assert.Equal(DetentionStayStatus.Reserved, Stay(w).Status); Assert.Equal(1, Facility(w).Reservations); Assert.Equal(0, Facility(w).Occupants); Assert.Equal(1, Facility(w).Operations);
        Assert.Equal(ActionKind.RegisterIntake, w.InspectActor(new(3)).Action); Assert.Equal(795, w.InspectActor(new(3)).ActionCompletion); RoundTrip(w);
        w.AdvanceTo(794); Assert.Equal(0, Facility(w).Occupants); Assert.Equal(EscortDispatchStatus.TeamForming, Assert.Single(w.InspectEscortDispatches()).Status);
        w.AdvanceTo(795); Assert.Equal(DetentionStayStatus.Detained, Stay(w).Status); Assert.Equal(0, Facility(w).Reservations); Assert.Equal(1, Facility(w).Occupants); Assert.Equal(0, Facility(w).Operations);
        Assert.Equal(795, Stay(w).IntakeTick); Assert.Equal(1795, Stay(w).ReleaseDue); Assert.Equal(EscortStatus.Detained, Assert.Single(w.InspectEscorts()).Status);
        Assert.Equal(EscortDispatchStatus.TeamForming, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Equal(-1, Stay(w).IntakeReceivedTick); RoundTrip(w);
        w.AdvanceTo(796); Assert.Equal(EscortDispatchStatus.Detained, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Equal(796, Stay(w).IntakeReceivedTick); RoundTrip(w);
    }
    [Fact]
    public void SentenceEndsThroughFiniteReleaseAndDelayedReceiptAndMailboxNotice()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(1794); Assert.Equal(DetentionStayStatus.Detained, Stay(w).Status);
        w.AdvanceTo(1795); Assert.Equal(DetentionStayStatus.Releasing, Stay(w).Status); Assert.Equal(ActionKind.ReleaseDetention, w.InspectActor(new(1)).Action); Assert.Equal(1, Facility(w).Occupants); RoundTrip(w);
        w.AdvanceTo(2094); Assert.Equal(1, Facility(w).Occupants); Assert.Equal(2, w.QualifyingReprimandCount(1, 2160));
        w.AdvanceTo(2095); Assert.Equal(DetentionStayStatus.Released, Stay(w).Status); Assert.Equal(0, Facility(w).Occupants); Assert.Equal(EscortStatus.Released, Assert.Single(w.InspectEscorts()).Status);
        Assert.Equal(EscortDispatchStatus.Detained, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Equal(MessageStage.Posted, w.InspectMessages().Single(m => m.Id == Stay(w).ReleaseNoticeId).Stage); RoundTrip(w);
        w.AdvanceTo(2096); Assert.Equal(EscortDispatchStatus.Released, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Equal(MessageStage.DeliveredToMailbox, w.InspectMessages().Single(m => m.Id == Stay(w).ReleaseNoticeId).Stage);
        Assert.Equal(0, w.QualifyingReprimandCount(1, 2162)); Assert.Equal(2, w.QualifyingReprimandCount(1, 179)); Assert.Equal(2, w.InspectReprimands().Count); RoundTrip(w);
        w.AdvanceTo(2134); Assert.Equal(EscortDispatchStatus.Released, Assert.Single(w.InspectEscortDispatches()).Status); RoundTrip(w);
    }
    [Fact]
    public void CancellationDuringIntakeReleasesReservationWithoutCreatingOccupancy()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(534); w.Submit(535, new(new(4), ActionKind.Cancel, new(2, 0, 0))); w.AdvanceTo(535);
        Assert.Equal(DetentionStayStatus.Cancelled, Stay(w).Status); Assert.Equal(0, Facility(w).Reservations); Assert.Equal(0, Facility(w).Occupants); Assert.Equal(0, Stay(w).IntakeReceiptId); RoundTrip(w);
        w.AdvanceTo(934); Assert.Equal(0, Facility(w).Occupants); Assert.Equal(0, Stay(w).IntakeReceiptId); RoundTrip(w);
    }
    [Fact]
    public void KnownExpiryDuringIntakeCannotBecomeACompletedDetention()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 500).CreateWorld(42); w.AdvanceTo(613);
        Assert.Equal(DetentionStayStatus.Cancelled, Stay(w).Status); Assert.Equal(EscortStatus.Interrupted, Assert.Single(w.InspectEscorts()).Status); Assert.Equal(0, Facility(w).Occupants); RoundTrip(w);
    }
    [Fact]
    public void IntakeCompletionExactlyAtAuthorizationExpiryIsRejected()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 682).CreateWorld(42); w.AdvanceTo(795);
        Assert.Equal(DetentionStayStatus.Cancelled, Stay(w).Status); Assert.Equal(0, Stay(w).IntakeReceiptId); Assert.Equal(0, Facility(w).Occupants); RoundTrip(w);
    }
    [Fact]
    public void AuthorizationExpiryAfterIntakeDoesNotEraseTheSentence()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 700).CreateWorld(42); w.AdvanceTo(813);
        Assert.Equal(DetentionStayStatus.Detained, Stay(w).Status); Assert.Equal(DetentionAuthorizationStatus.Expired, Assert.Single(w.InspectDetentionAuthorizations()).Status); Assert.Equal(EscortDispatchStatus.Detained, Assert.Single(w.InspectEscortDispatches()).Status); RoundTrip(w);
        w.AdvanceTo(2096); Assert.Equal(DetentionStayStatus.Released, Stay(w).Status); RoundTrip(w);
    }
    [Fact]
    public void MissingAndWrongLocationServicesCannotPerformRemoteIntake()
    {
        foreach (bool absent in new[] { false, true })
        {
            var w = Load(j => { if (absent) j["initialState"]!["detention"]!["facilities"] = new JsonArray(); else j["initialState"]!["detention"]!["facilities"]![0]!["position"]!["x"] = 0; }).CreateWorld(42); w.AdvanceTo(934);
            Assert.Empty(w.InspectDetentionStays()); Assert.Equal(EscortStatus.AwaitingIntake, Assert.Single(w.InspectEscorts()).Status); RoundTrip(w);
        }
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ServiceOutageCancelsIntakeAndRequiresANewFullOperation(bool powered, bool connected)
    {
        var w = Load().CreateWorld(42); w.ScheduleDetentionServiceChange(534, 1, powered, connected); w.AdvanceTo(534);
        Assert.Equal(DetentionStayStatus.Cancelled, Stay(w).Status); Assert.Equal(0, Facility(w).Reservations); RoundTrip(w);
        w.ScheduleDetentionServiceChange(634, 1, true, true); w.AdvanceTo(635); Assert.Equal(DetentionStayStatus.Reserved, Stay(w).Status); Assert.Equal(635, Stay(w).IntakeStarted); RoundTrip(w);
        w.AdvanceTo(934); Assert.Equal(0, Facility(w).Occupants); w.AdvanceTo(935); Assert.Equal(1, Facility(w).Occupants); RoundTrip(w);
    }
    [Fact]
    public void ReleaseOutageKeepsOccupancyAndRetriesWithoutEarlyFreedom()
    {
        var w = Load().CreateWorld(42); w.ScheduleDetentionServiceChange(1834, 1, false, true); w.AdvanceTo(1834);
        Assert.Equal(DetentionStayStatus.Detained, Stay(w).Status); Assert.Equal(1, Facility(w).Occupants); Assert.Equal(0, Stay(w).ReleaseReceiptId); RoundTrip(w);
        w.ScheduleDetentionServiceChange(1934, 1, true, true); w.AdvanceTo(1935); Assert.Equal(DetentionStayStatus.Releasing, Stay(w).Status); Assert.True(Stay(w).ReleaseStarted >= 1934); RoundTrip(w);
        w.AdvanceTo(2334); Assert.Equal(DetentionStayStatus.Released, Stay(w).Status); Assert.True(Stay(w).ReleaseTick >= 2234); RoundTrip(w);
    }
    [Fact]
    public void DetainedTargetCannotCancelSentenceOrUseOrdinaryTerminals()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(834);
        foreach (var kind in new[] { ActionKind.Move, ActionKind.Cancel, ActionKind.Login, ActionKind.ReleaseDetention, ActionKind.ResumeWork })
        { long tick = w.LastProcessedTick + 1; w.Submit(tick, new(new(1), kind, new(2, 0, 0), serviceId: 1)); w.AdvanceTo(tick); Assert.Equal(FailureReason.CustodyRestricted, w.InspectActor(new(1)).LastFailure); }
        Assert.Equal(DetentionStayStatus.Detained, Stay(w).Status); RoundTrip(w);
    }
    [Fact]
    public void GuardsAreReleasedLocallyAtIntakeWhileTargetRemainsAtTheFacility()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(834); Assert.Equal(new Coord(2, 0, 0), w.ActorView(new(1)).CurrentTile);
        Assert.All(new long[] { 3, 4 }, id => Assert.Equal(GuardOrderStatus.TransferredToFacility, Assert.Single(w.InspectLocalGuardOrders(new(id))).Status)); RoundTrip(w);
        w.Submit(835, new(new(4), ActionKind.Move, new(0, 0, 0))); w.AdvanceTo(837); Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(4)).CurrentTile); Assert.Equal(new Coord(2, 0, 0), w.ActorView(new(1)).CurrentTile); RoundTrip(w);
    }
    [Fact]
    public void RepeatedAdvanceAndRestoredCustodyPreserveReleaseMessagesAndHistory()
    {
        foreach (int tick in new[] { 494, 495, 534, 794, 795, 796, 934, 1794, 1795, 2094, 2095, 2096, 2113 })
        {
            var w = Load().CreateWorld(42, true); w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); RoundTrip(w);
            w.AdvanceTo(2434); copy.AdvanceTo(2434); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
            Assert.Equal(3, w.InspectMessages().Count(m => m.Draft.Kind is MessageKind.DetentionIntakeReceipt or MessageKind.DetentionReleaseReceipt or MessageKind.DetentionReleaseNotice));
        }
    }
    private static JsonObject TwoSubjects(int capacity, int operations)
    {
        var j = Scenario(); var s = j["initialState"]!;
        s["detention"]!["sentenceTicks"] = 100; s["detention"]!["orderTicks"] = 10000;
        s["detention"]!["facilities"]![0]!["capacity"] = capacity; s["detention"]!["facilities"]![0]!["serviceCapacity"] = operations;
        void AddCopies(JsonArray rows, Action<JsonObject> edit) { foreach (var row in rows.ToArray()) { var copy = row!.DeepClone().AsObject(); edit(copy); rows.Add(copy); } }
        void Bump(JsonObject row, string key, int offset = 100) { if (row[key] != null && row[key]!.GetValue<long>() > 0) row[key] = row[key]!.GetValue<long>() + offset; }
        AddCopies(s["actors"]!.AsArray(), a => { Bump(a, "id"); Bump(a, "account"); });
        AddCopies(s["items"]!.AsArray(), a => { Bump(a, "id"); if (a["locationKind"]!.GetValue<string>() != "TileContainer") Bump(a, "owner"); });
        AddCopies(s["information"]!["facts"]!.AsArray(), a => { Bump(a, "actorId"); var key = a["key"]!.AsObject(); if (key["subjectKind"]!.GetValue<string>() != "Tile") Bump(key, "subjectId"); });
        AddCopies(s["information"]!["terminals"]!.AsArray(), a => Bump(a, "id"));
        AddCopies(s["information"]!["credentials"]!.AsArray(), a => { foreach (string field in new[] { "id", "holder", "account", "cardItem" }) Bump(a, field); });
        AddCopies(s["information"]!["recordAccess"]!.AsArray(), a => { Bump(a, "actorId"); Bump(a, "terminalId"); });
        AddCopies(s["work"]!["machines"]!.AsArray(), a => Bump(a, "id")); AddCopies(s["work"]!["workstations"]!.AsArray(), a => Bump(a, "id"));
        AddCopies(s["jobSystem"]!["jobs"]!.AsArray(), a => { Bump(a, "id"); Bump(a, "machineId"); });
        AddCopies(s["jobSystem"]!["assignments"]!.AsArray(), a => { Bump(a, "actorId"); Bump(a, "jobId"); a["key"] = "second/" + a["key"]!.GetValue<string>(); });
        AddCopies(s["jobSystem"]!["checkIns"]!.AsArray(), a => Bump(a, "actorId")); AddCopies(s["routines"]!["assignments"]!.AsArray(), a => Bump(a, "actorId"));
        AddCopies(j["scriptedActors"]!.AsArray(), a => {
            Bump(a, "actorId"); foreach (var command in a["commands"]!.AsArray())
            {
                var c = command!.AsObject(); Bump(c, "serviceId"); Bump(c, "itemId");
                if (c["message"] is JsonObject m) { Bump(m, "attemptId", 2); Bump(m, "taskId"); Bump(m, "relatedMessageId", 2); var subject = m["subject"]!.AsObject(); Bump(subject, "subjectId", subject["subjectKind"]!.GetValue<string>() == "Attempt" ? 2 : 100); }
            }
        }); return j;
    }
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void ConcurrentSubjectsRespectSeparateBedAndOperationCapacities(int capacity, int operations)
    {
        var w = Loaded(TwoSubjects(capacity, operations)).CreateWorld(42, true);
        for (int tick = 0; tick <= 3000; tick += 17)
        {
            w.AdvanceTo(tick); var f = Facility(w); Assert.InRange(f.Reservations + f.Occupants, 0, capacity); Assert.InRange(f.Operations, 0, operations); RoundTrip(w);
        }
        Assert.True(w.InspectDetentionStays().Count == 2, JsonData.Write(w.InspectDetentionStays()) + JsonData.Write(w.InspectEscortDispatches()));
        Assert.All(w.InspectDetentionStays(), s => Assert.Equal(DetentionStayStatus.Released, s.Status)); Assert.Equal(0, Facility(w).Occupants);
        if (capacity == 1) { var stays = w.InspectDetentionStays().OrderBy(s => s.IntakeTick).ToArray(); Assert.True(stays[1].IntakeStarted >= stays[0].ReleaseTick); }
    }
    [Fact]
    public void FullIntakeWaitsForLocalCapacityChangeAndSurvivesCheckpoint()
    {
        var scenario = TwoSubjects(1, 1);
        scenario["initialState"]!["detention"]!["sentenceTicks"] = 2000;
        var w = Loaded(scenario).CreateWorld(42, true);
        w.AdvanceTo(1000);
        Assert.Contains(w.InspectEscorts(), e => e.Status == EscortStatus.AwaitingIntake);
        var restored = World.Restore(w.Serialize(), true);
        w.ClearRecordedTrace(); restored.ClearRecordedTrace();
        w.AdvanceTo(6000); restored.AdvanceTo(6000);
        Assert.All(w.InspectDetentionStays(), s => Assert.Equal(DetentionStayStatus.Released, s.Status));
        Assert.Equal(2, w.InspectDetentionStays().Count);
        Assert.InRange(w.Trace.Count(e => e.Type == "ActionFailed" && e.Detail == "ServiceBusy"), 0, 4);
        Assert.Equal(w.Digest(), restored.Digest());
        Assert.Equal(w.Trace.Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        w.AssertAccounting();
    }
    [Fact]
    public void ReleasedWorkerMustReadCurrentOrdersBeforeSuspendedWorkCanResume()
    {
        var w = Load(j => { j["initialState"]!["detention"]!["sentenceTicks"] = 100; j["initialState"]!["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 100, ["machineId"] = 1 }); foreach (var a in j["initialState"]!["jobSystem"]!["assignments"]!.AsArray()) a!["expires"] = 10000; }).CreateWorld(42);
        long unfinished = w.ScheduleWorkAssignment(300, new("unfinished-at-intake", 100, new(1), 10000));
        w.AdvanceTo(414); w.Submit(487, new(new(1), ActionKind.Logout, new(1, 0, 0))); w.Submit(489, new(new(1), ActionKind.Login, new(1, 0, 0), serviceId: 1)); w.Submit(492, new(new(1), ActionKind.ReadInbox, new(1, 0, 0), serviceId: 1));
        w.AdvanceTo(494); Assert.All(w.InspectLocalOrders(new(1)).Where(o => o.AttemptId == unfinished), o => Assert.Equal(LocalOrderStatus.Suspended, o.Status));
        w.AdvanceTo(1195); Assert.Equal(DetentionStayStatus.Released, Stay(w).Status); RoundTrip(w);
        w.Submit(1196, new(new(1), ActionKind.ResumeWork, new(2, 0, 0), attemptId: unfinished)); w.AdvanceTo(1196); Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(1)).LastFailure); RoundTrip(w);
        w.AdvanceTo(1197); Assert.All(w.InspectLocalOrders(new(1)).Where(o => o.AttemptId == unfinished), o => Assert.Equal(LocalOrderStatus.Suspended, o.Status));
        var refreshes = w.InspectMessages().Where(m => m.Draft.OperationKey.StartsWith("post-detention/", StringComparison.Ordinal)).ToArray(); Assert.Equal(3, refreshes.Length);
        Assert.Single(refreshes, m => m.Draft.AttemptId == unfinished);
        w.Submit(1198, new(new(1), ActionKind.Move, new(1, 0, 0))); w.Submit(1200, new(new(1), ActionKind.Login, new(1, 0, 0), serviceId: 1)); w.Submit(1202, new(new(1), ActionKind.ReadInbox, new(1, 0, 0), serviceId: 1)); w.AdvanceTo(1203);
        Assert.All(w.InspectLocalOrders(new(1)).Where(o => o.AttemptId == unfinished), o => { Assert.NotEqual(LocalOrderStatus.Suspended, o.Status); Assert.Equal("CurrentOrdersRead", o.Reason); });
        Assert.All(refreshes, m => Assert.Equal(MessageStage.Read, w.InspectMessages().Single(x => x.Id == m.Id).Stage)); RoundTrip(w);
    }
    [Fact]
    public void CompletedSentenceClosesOldRequestsAndPreventsReusingEarlierReprimands()
    {
        var w = Load(j => { j["initialState"]!["detention"]!["sentenceTicks"] = 100; j["initialState"]!["routines"]!["configuration"]!["dayTicks"] = 9000; j["initialState"]!["routines"]!["configuration"]!["dutyTicks"] = 3000; }).CreateWorld(42); w.AdvanceTo(1234);
        var original = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft;
        void Post(MessageDraft draft, long tick)
        { w.Submit(tick, new(new(2), ActionKind.Login, new(0, 0, 0), serviceId: 2)); w.Submit(tick + 2, new(new(2), ActionKind.PostMessage, new(0, 0, 0), serviceId: 2, message: draft)); w.AdvanceTo(tick + 4); }
        Post(original, 1301); Assert.Equal("PriorRequestClosed", w.InspectDetentionRequests().Last().Decision.Reason); RoundTrip(w);
        var fresh = new MessageDraft(MessageKind.RequestDetention, RecipientKind.Authority, default, original.Subject, original.Claim, 1305, relatedMessageId: original.RelatedMessageId, operationKey: "after-release", detentionRequest: original.DetentionRequest);
        Post(fresh, 1310); Assert.Equal("ThresholdNotMet", w.InspectDetentionRequests().Last().Decision.Reason); Assert.Equal(0, w.InspectDetentionRequests().Last().Decision.QualifyingCount); Assert.Single(w.InspectDetentionAuthorizations()); RoundTrip(w);
    }
    [Fact]
    public void CorruptSentenceAndReceiptIdentityCannotRestoreAsCompletedCustody()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(795); var stay = Stay(w); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var b = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { b.Write(stay.AuthorizationId); b.Write(stay.FacilityId); b.Write(stay.ActorId); b.Write(stay.AccountId); b.Write(stay.LeaderId); b.Write((int)stay.Status); b.Write(stay.IntakeActionId); b.Write(stay.IntakeStarted); b.Write(stay.IntakeTick); b.Write(stay.ReleaseDue); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var edit in new[] { (Offset: 8, Value: 999L), (Offset: 16, Value: 3L), (Offset: 44, Value: 0L), (Offset: 60, Value: 860L), (Offset: 68, Value: 1000L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(edit.Value).CopyTo(bad, start + edit.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }
    [Fact]
    public void ANewAcceptedCaseAfterReleaseStartsANewQualifyingWindow()
    {
        var w = Load(j => {
            var s = j["initialState"]!; s["detention"]!["sentenceTicks"] = 100; s["routines"]!["configuration"]!["dayTicks"] = 9000; s["routines"]!["configuration"]!["dutyTicks"] = 3000;
            var machine = s["work"]!["machines"]![0]!.DeepClone(); machine["id"] = 3; s["work"]!["machines"]!.AsArray().Add(machine);
            s["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 3, ["machineId"] = 3 });
            var item = s["items"]!.AsArray().Single(i => i!["id"]!.GetValue<int>() == 8)!.DeepClone(); item["id"] = 10; s["items"]!.AsArray().Add(item);
            var fourth = machine.DeepClone(); fourth["id"] = 4; s["work"]!["machines"]!.AsArray().Add(fourth);
            s["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 4, ["machineId"] = 4 });
            var another = item.DeepClone(); another["id"] = 11; s["items"]!.AsArray().Add(another);
        }).CreateWorld(42, true); w.AdvanceTo(1300); Assert.Equal(0, w.QualifyingReprimandCount(1, 1300));
        long attempt = w.ScheduleWorkAssignment(1301, new("after-release-repair", 3, new(1), 10000));
        w.Submit(1301, new(new(1), ActionKind.Move, new(1, 0, 0))); w.AdvanceTo(1302); long order = w.InspectWorkAttempts().Single(a => a.Id == attempt).OrderMessageId;
        w.Submit(1303, new(new(1), ActionKind.Login, new(1, 0, 0), serviceId: 1)); w.Submit(1304, new(new(1), ActionKind.Install, new(1, 0, 0), new(10), serviceId: 3));
        w.Submit(1307, new(new(1), ActionKind.PostMessage, new(1, 0, 0), serviceId: 1, message: new(MessageKind.OrderAccepted, RecipientKind.Authority, default,
            new(SubjectKind.Attempt, attempt, AttributeKind.WorkStatus), new(FactValueKind.Text, text: "Accepted"), 1307, 3, attempt, order, operationKey: "after-release/accept")));
        w.Submit(1308, new(new(1), ActionKind.PostMessage, new(1, 0, 0), serviceId: 1, message: new(MessageKind.WorkCompleted, RecipientKind.Authority, default,
            new(SubjectKind.Machine, 3, AttributeKind.Operational), FactValue.Boolean(true), 1308, 3, attempt, operationKey: "after-release/complete")));
        w.AdvanceTo(1800); Assert.Equal(3, w.InspectReprimands().Count); Assert.Equal(1, w.QualifyingReprimandCount(1, 1800)); Assert.Equal(2, w.QualifyingReprimandCount(1, 179));
        Assert.Equal(1, w.InspectMessages().Last(m => m.Draft.Kind == MessageKind.CaseDecision).Draft.CaseDecision!.QualifyingCount); Assert.Single(w.InspectDetentionAuthorizations()); RoundTrip(w);
        long next = w.ScheduleWorkAssignment(1801, new("after-release-second-repair", 4, new(1), 10000)); w.AdvanceTo(1801); long nextOrder = w.InspectWorkAttempts().Single(a => a.Id == next).OrderMessageId;
        w.Submit(1802, new(new(1), ActionKind.Login, new(1, 0, 0), serviceId: 1)); w.Submit(1803, new(new(1), ActionKind.Install, new(1, 0, 0), new(11), serviceId: 4));
        w.Submit(1806, new(new(1), ActionKind.PostMessage, new(1, 0, 0), serviceId: 1, message: new(MessageKind.OrderAccepted, RecipientKind.Authority, default,
            new(SubjectKind.Attempt, next, AttributeKind.WorkStatus), new(FactValueKind.Text, text: "Accepted"), 1806, 4, next, nextOrder, operationKey: "after-release-second/accept")));
        w.Submit(1807, new(new(1), ActionKind.PostMessage, new(1, 0, 0), serviceId: 1, message: new(MessageKind.WorkCompleted, RecipientKind.Authority, default,
            new(SubjectKind.Machine, 4, AttributeKind.Operational), FactValue.Boolean(true), 1807, 4, next, operationKey: "after-release-second/complete")));
        w.AdvanceTo(3500); Assert.Equal(2, w.InspectDetentionAuthorizations().Count); Assert.Equal(new long[] { 3, 4 }, w.InspectDetentionAuthorizations()[1].JustifyingCases);
        Assert.Equal(2, w.InspectDetentionStays().Count); Assert.All(w.InspectDetentionStays(), s => Assert.Equal(DetentionStayStatus.Released, s.Status)); Assert.Equal(0, w.QualifyingReprimandCount(1, 3500)); RoundTrip(w);
    }
    [Theory]
    [InlineData(MessageKind.DetentionIntakeReceipt)]
    [InlineData(MessageKind.DetentionReleaseReceipt)]
    [InlineData(MessageKind.DetentionReleaseNotice)]
    public void CitizensCannotForgeFacilityReceiptsOrReleaseNotices(MessageKind kind)
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(734);
        var draft = new MessageDraft(kind, kind == MessageKind.DetentionReleaseNotice ? RecipientKind.Account : RecipientKind.Authority, kind == MessageKind.DetentionReleaseNotice ? new(1) : default,
            new(SubjectKind.Order, 1, AttributeKind.DetentionStatus), new(FactValueKind.Text, text: "Released"), 800, operationKey: "forged-service-receipt");
        w.Submit(735, new(new(2), ActionKind.Login, new(0, 0, 0), serviceId: 2)); w.Submit(736, new(new(2), ActionKind.PostMessage, new(0, 0, 0), serviceId: 2, message: draft)); w.AdvanceTo(739);
        Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.OperationKey == draft.OperationKey); Assert.Contains(w.Trace, e => e.Type == "ActionFailed" && e.ActorId == 2 && e.Detail == "InvalidMessage"); Assert.Equal(DetentionStayStatus.Reserved, Stay(w).Status); RoundTrip(w);
    }
    [Theory]
    [InlineData("id", 0)]
    [InlineData("capacity", 0)]
    [InlineData("serviceCapacity", 0)]
    [InlineData("intakeTicks", 0)]
    [InlineData("releaseTicks", 0)]
    public void InvalidDetentionFacilityDeclarationsFailBeforeSimulation(string field, int value)
        => Assert.Throws<ScenarioException>(() => Load(j => j["initialState"]!["detention"]!["facilities"]![0]![field] = value).CreateWorld(42));
    [Fact]
    public void GoldenDetentionLifecyclePassesItsMilestoneAssertions()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "detention_release.json"));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(0, run.World.ActionsFailed); RoundTrip(run.World);
    }
}
