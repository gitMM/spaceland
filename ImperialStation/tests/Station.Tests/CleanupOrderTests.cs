using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class CleanupOrderTests
{
    private static Coord Site => new(2, 0, 0);
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "missing_cleanup_report.json")))!.AsObject();
        change?.Invoke(json);
        // Transformed fixtures have explicit assertions in their tests; the canonical
        // Runner timeline applies only to the unmodified scenario.
        if (change != null) json["assertions"] = new JsonArray();
        return new LoadedScenario(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "cleanup-work.json")), "cleanup-test");
    }
    private static LocalWorkOrderSnapshot Local(World w, long actor) => Assert.Single(w.InspectLocalOrders(new(actor)));

    [Fact]
    public void CompletedCleanupQueuesItsReportBeforeLeavingForAnOffDutyNeed()
    {
        var w = Load(j =>
        {
            var s = j["initialState"]!; s.AsObject().Remove("authority");
            s["tiles"]![1]!["traversalOverride"] = 1;
            s["routines"]!["configuration"]!["dayTicks"] = 60;
            s["routines"]!["configuration"]!["dutyTicks"] = 20;
            s["cleanliness"]!["cleanUnitTicks"] = 10;
            s["cleanliness"]!["dirt"]![0]!["units"] = 1;
            s["needs"] = new JsonObject {
                ["initial"] = new JsonArray(new JsonObject { ["actorId"] = 1, ["kind"] = "Bladder", ["q"] = 500 * NeedAccumulator.HourMilliseconds - 22 * 10000 }),
                ["services"] = new JsonArray(new JsonObject { ["id"] = 1, ["kind"] = "Toilet", ["position"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }, ["durationTicks"] = 2 }) };
        }).CreateWorld(42, true);
        w.AdvanceTo(24);
        Assert.Equal(0, w.InspectDirt()[0].Units);
        Assert.Equal(LocalOrderStatus.Completed, Local(w, 1).Status);
        var copy = World.Restore(w.Serialize(), true);
        w.ClearRecordedTrace(); copy.ClearRecordedTrace();
        w.AdvanceTo(59); copy.AdvanceTo(59);
        var report = Assert.Single(w.InspectMessages(), m => m.Draft.Kind == MessageKind.WorkCompleted && m.Draft.AttemptId == 1);
        Assert.True(report.DeliveryTick < 60);
        Assert.Equal("WorkCompleted", Assert.Single(w.InspectJobs()).AuthorityStatus);
        Assert.Equal(w.Digest(), copy.Digest());
        Assert.Equal(w.Trace.Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        w.AssertAccounting();
    }

    [Fact]
    public void T19_AutonomousCleanupKeepsDeliveryAcceptancePhysicalWorkAndReportsSeparate()
    {
        var loaded = Load(); Assert.Empty(loaded.Scenario.ScriptedActors); var w = loaded.CreateWorld(42, true);
        w.AdvanceTo(3); Assert.Empty(w.InspectLocalOrders(new(1))); Assert.Empty(w.InspectExpectations());
        w.AdvanceTo(4); Assert.Equal(LocalOrderStatus.AcknowledgmentPending, Local(w, 1).Status); Assert.Empty(w.InspectExpectations());
        w.AdvanceTo(6); Assert.Equal(LocalOrderStatus.Working, Local(w, 1).Status); Assert.Empty(w.InspectExpectations());
        w.AdvanceTo(8); var expected = Assert.Single(w.InspectExpectations()); Assert.Equal("CleanTile", expected.TaskKind); Assert.Equal(18, expected.UnscaledEstimate); Assert.Equal(31, expected.Deadline);
        w.AdvanceTo(30); Assert.Empty(w.InspectMissingReports()); Assert.Single(w.InspectWorkAttempts()); Assert.Empty(w.InspectPatrolOrders());
        w.AdvanceTo(31); Assert.Single(w.InspectMissingReports()); Assert.Single(w.InspectWorkAttempts());
        w.AdvanceTo(34); var incident = Assert.Single(w.InspectMissingReports()); Assert.Equal(2, incident.ReplacementAttemptId); Assert.True(incident.PatrolOrderMessageId > 0);
        Assert.Empty(w.InspectLocalOrders(new(2))); Assert.Empty(w.InspectLocalPatrols(new(3))); Assert.Equal(LocalOrderStatus.Working, Local(w, 1).Status);
        Assert.Equal(1, w.InspectAdmissionBuckets().Sum(b => b.Used));
        w.AdvanceTo(52); Assert.Equal(LocalOrderStatus.AcknowledgmentPending, Local(w, 2).Status); Assert.Equal(2, w.InspectDirt()[0].Units);
        w.AdvanceTo(63); Assert.Equal(0, w.InspectDirt()[0].Units); Assert.Equal(2, Local(w, 2).CleanedUnits); Assert.Equal("Unverified", w.InspectMissingReports()[0].WorkResolution);
        w.AdvanceTo(71); Assert.Equal("ReportedComplete", w.InspectMissingReports()[0].WorkResolution); Assert.Equal("PatrolPosted", w.InspectMissingReports()[0].WorkerStatusResponse);
        w.AdvanceTo(110); Assert.Equal(LocalOrderStatus.Completed, Local(w, 1).Status); Assert.Equal("NoWorkNeeded", Local(w, 1).Reason); Assert.Equal(0, Local(w, 1).CleanedUnits);
        Assert.DoesNotContain(w.Trace, e => e.ActorId == 1 && e.Type == "CleaningCompleted"); Assert.Equal(2, w.InspectDirt()[0].RemovedUnits);
        w.AdvanceTo(173); Assert.Equal("ReportedNotLocated", w.InspectMissingReports()[0].WorkerStatusResponse); long childResult = w.InspectMissingReports()[0].ReplacementResultMessageId;
        w.AdvanceTo(217); Assert.Equal(0, w.InspectMissingReports()[0].ResponseMessageId);
        w.AdvanceTo(218); Assert.Equal(LocalOrderStatus.Cancelled, Local(w, 1).Status); incident = w.InspectMissingReports()[0]; Assert.True(incident.ResponseMessageId > 0); Assert.Equal(childResult, incident.ReplacementResultMessageId);
        var originalResult = w.InspectMessages().Single(m => m.Id == incident.ResponseMessageId);
        Assert.Equal(1, originalResult.Draft.AttemptId); Assert.Equal("NoWorkNeeded", originalResult.Draft.Claim.Text); Assert.Equal(1, originalResult.TerminalId);
        Assert.Equal(2, w.InspectMessages().Single(m => m.Id == childResult).TerminalId);
        Assert.DoesNotContain(w.InspectTerminals(), t => t.Position == Site); Assert.Equal(0, w.ActionsFailed); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
    }

    [Fact]
    public void ReplacementPhysicallyInspectsAlreadyCleanTileAndReportsNoWorkNeeded()
    {
        var loaded = Load(json => json["initialState"]!["tiles"]![1]!["traversalOverride"] = 1); var w = loaded.CreateWorld(42, true);
        w.ScheduleTerminalChange(10, 1, false, false); w.ScheduleTerminalChange(80, 1, true, true); w.AdvanceTo(300);
        var replacement = Local(w, 2); Assert.Equal(LocalOrderStatus.Completed, replacement.Status); Assert.Equal("NoWorkNeeded", replacement.Reason); Assert.Equal(0, replacement.CleanedUnits);
        Assert.Equal(2, w.InspectDirt()[0].RemovedUnits); Assert.Equal(2, Local(w, 1).CleanedUnits);
        Assert.Contains(w.Trace, e => e.ActorId == 2 && e.Type == "ActionCompleted" && e.Detail == "InspectTile" && e.Target == Site);
        Assert.DoesNotContain(w.Trace, e => e.ActorId == 2 && e.Type == "CleaningCompleted");
        var report = w.InspectMessages().Single(m => m.Draft.AttemptId == 2 && m.Draft.Kind == MessageKind.WorkCompleted);
        Assert.Equal("NoWorkNeeded", report.Draft.Claim.Text); Assert.Equal(2, report.TerminalId); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void EitherCleanupResponseBranchProceedsWhenTheOtherRoleIsOffDuty(int unavailable)
    {
        var w = Load(json => json["initialState"]!["routines"]!["assignments"]![unavailable - 1]!["cohort"] = 1).CreateWorld(42);
        w.AdvanceTo(80); var incident = Assert.Single(w.InspectMissingReports()); Assert.Equal(1, w.InspectAdmissionBuckets().Sum(b => b.Used));
        if (unavailable == 2) { Assert.Equal(0, incident.ReplacementAttemptId); Assert.True(incident.PatrolOrderMessageId > 0); Assert.Equal(2, w.InspectDirt()[0].Units); }
        else { Assert.Equal(0, incident.PatrolOrderMessageId); Assert.True(incident.ReplacementAttemptId > 0); Assert.Equal("ReportedComplete", incident.WorkResolution); Assert.Equal(0, w.InspectDirt()[0].Units); }
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void HiddenTraversalAndDirtChangesDoNotChangeInitialDeadlineOrResponseDestinations()
    {
        var loaded = Load(json => json["initialState"]!["tiles"]![1]!["traversalOverride"] = 1);
        var baseline = loaded.CreateWorld(42); var faster = loaded.CreateWorld(42); var dirtier = loaded.CreateWorld(42);
        // Disturbances change physical routing after the identical publication is made.
        baseline.ScheduleTraversalChange(0, Fixtures.B, 101); faster.ScheduleTraversalChange(0, Fixtures.B, 31); dirtier.ScheduleTraversalChange(0, Fixtures.B, 101); dirtier.ScheduleSpill(9, Site, 7);
        foreach (var w in new[] { baseline, faster, dirtier }) w.AdvanceTo(34);
        Assert.NotEqual(baseline.InspectActor(new(1)).Source, faster.InspectActor(new(1)).Source);
        Assert.NotEqual(baseline.InspectDirt()[0].Units, dirtier.InspectDirt()[0].Units);
        foreach (var other in new[] { faster, dirtier })
        {
            Assert.Equal(JsonData.Write(baseline.AuthorityView()), JsonData.Write(other.AuthorityView()));
            Assert.Equal(baseline.InspectExpectations()[0].Deadline, other.InspectExpectations()[0].Deadline);
            Assert.Equal(baseline.InspectExpectations()[0].UnscaledEstimate, other.InspectExpectations()[0].UnscaledEstimate);
            Assert.Equal(baseline.InspectPatrolOrders()[0].Payload.Candidates, other.InspectPatrolOrders()[0].Payload.Candidates);
            Assert.Equal(baseline.InspectWorkAttempts().Select(a => (a.Id, a.ActorId, a.DispatchTick)), other.InspectWorkAttempts().Select(a => (a.Id, a.ActorId, a.DispatchTick)));
        }
    }

    [Fact]
    public void SuspendedCleanupRetainsItsToolAndReinspectsBeforeResuming()
    {
        var w = Load(json => { json["initialState"]!.AsObject().Remove("authority"); json["initialState"]!["tiles"]![1]!["traversalOverride"] = 1; }).CreateWorld(42, true);
        w.Submit(11, new(new(1), ActionKind.SuspendWork, Site, attemptId: 1)); w.Submit(20, new(new(1), ActionKind.ResumeWork, Site, attemptId: 1));
        w.AdvanceTo(12); Assert.Equal(LocalOrderStatus.Suspended, Local(w, 1).Status); Assert.Equal(2, w.InspectDirt()[0].Units); Assert.Contains(new Id<ItemTag>(8), w.InspectActor(new(1)).Inventory);
        Assert.Empty(w.InspectResourceClaims()); var bytes = w.Serialize(); var resumed = World.Restore(bytes, true); Assert.Equal(bytes, resumed.Serialize());
        w.AdvanceTo(70); resumed.AdvanceTo(70); Assert.Equal(w.Serialize(), resumed.Serialize()); Assert.Equal(LocalOrderStatus.Completed, Local(w, 1).Status);
        Assert.Equal(0, w.InspectDirt()[0].Units); Assert.Equal(2, Local(w, 1).CleanedUnits); Assert.Equal(1, w.InspectItems().Single(i => i.Id.Value == 8).Quantity);
        Assert.Contains(w.Trace, e => e.ActorId == 1 && e.Tick > 20 && e.Type == "ActionCompleted" && e.Detail == "InspectTile"); w.AssertAccounting();
    }

    [Fact]
    public void CleanupLeaseExpiryStopsTravelAtItsNextBoundaryAndNeverCleans()
    {
        var w = Load(json => { json["initialState"]!.AsObject().Remove("authority"); json["initialState"]!["jobSystem"]!["assignments"]![0]!["expires"] = 10; }).CreateWorld(42, true);
        w.AdvanceTo(10); Assert.Equal(LocalOrderStatus.Expired, Local(w, 1).Status); Assert.True(w.InspectActor(new(1)).InTransit);
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(150); Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.Kind == MessageKind.WorkBlocked);
        w.AdvanceTo(170); copy.AdvanceTo(170); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(2, w.InspectDirt()[0].Units); Assert.DoesNotContain(w.Trace, e => e.Type == "CleaningCompleted");
        Assert.Contains(w.InspectMessages(), m => m.Draft.AttemptId == 1 && m.Draft.Kind == MessageKind.WorkBlocked && m.Draft.Claim.Text == "KnownOrderExpired");
    }

    [Fact]
    public void CleanupPlannerCollectsAKnownLocalToolWithoutMaterialConsumption()
    {
        var w = Load(json => { json["initialState"]!.AsObject().Remove("authority"); var tool = json["initialState"]!["items"]!.AsArray().Single(i => i!["id"]!.GetValue<int>() == 8)!; tool["locationKind"] = "TileContainer"; tool["owner"] = 1; }).CreateWorld(42, true);
        w.AdvanceTo(300); Assert.Equal(LocalOrderStatus.Completed, Local(w, 1).Status); Assert.Equal(2, Local(w, 1).CleanedUnits);
        Assert.Contains(w.Trace, e => e.ActorId == 1 && e.Type == "ActionCompleted" && e.Detail == "PickUp" && e.ItemId == 8);
        Assert.Equal(1, w.InspectItems().Single(i => i.Id.Value == 8).Quantity); Assert.Empty(w.InspectResourceTransactions()); w.AssertAccounting();
    }

    [Fact]
    public void CleanupResultReceivedExactlyAtDeadlineSuppressesTheIncident()
    {
        var calibration = Load(json => json["initialState"]!["authority"]!["graceTicks"] = 1000).CreateWorld(42); calibration.AdvanceTo(300);
        var completed = Assert.Single(calibration.InspectMessages(), m => m.Draft.AttemptId == 1 && m.Draft.Kind == MessageKind.WorkCompleted);
        var expectation = Assert.Single(calibration.InspectExpectations()); long grace = completed.DeliveryTick - expectation.AcknowledgmentTick - expectation.UnscaledEstimate;
        Assert.True(grace > 0);
        var w = Load(json => json["initialState"]!["authority"]!["graceTicks"] = grace).CreateWorld(42);
        w.AdvanceTo(completed.DeliveryTick - 1); Assert.Empty(w.InspectMissingReports()); Assert.Equal(ExpectationStatus.AwaitingResult, w.InspectExpectations()[0].Status);
        w.AdvanceTo(completed.DeliveryTick); Assert.Equal(completed.DeliveryTick, w.InspectExpectations()[0].Deadline); Assert.Equal(ExpectationStatus.Satisfied, w.InspectExpectations()[0].Status);
        Assert.Empty(w.InspectMissingReports()); Assert.Empty(w.InspectPatrolOrders()); Assert.Single(w.InspectWorkAttempts());
    }

    [Fact]
    public void UnreadCleanupOrderNeverBecomesAnAcceptedWorkExpectation()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(0, 1, false, false); w.AdvanceTo(300);
        Assert.Empty(w.InspectLocalOrders(new(1))); Assert.Empty(w.InspectExpectations()); Assert.Empty(w.InspectMissingReports()); Assert.Empty(w.InspectPatrolOrders());
        Assert.Equal(AcknowledgmentStatus.Overdue, Assert.Single(w.InspectAcknowledgmentWatches()).Status); Assert.Equal(2, w.InspectDirt()[0].Units);
    }

    [Fact]
    public void KnownOffDutyExtensionAllowsRetainedCleanupAndItsTerminalReport()
    {
        var w = Load(json => {
            json["initialState"]!["tiles"]![1]!["traversalOverride"] = 1;
            json["initialState"]!["routines"]!["configuration"]!["dayTicks"] = 120;
            json["initialState"]!["routines"]!["configuration"]!["dutyTicks"] = 20;
            json["initialState"]!["cleanliness"]!["cleanUnitTicks"] = 10;
            json["initialState"]!["authority"]!["graceTicks"] = 70;
        }).CreateWorld(42, true);
        w.AdvanceTo(30); Assert.Equal(1, w.InspectDirt()[0].Units); Assert.Equal(LocalOrderStatus.Working, Local(w, 1).Status);
        var e = Assert.Single(w.InspectExpectations()); Assert.Equal(110, e.BaseDeadline); Assert.Equal(101, e.OffDutyExtension); Assert.Equal(211, e.Deadline);
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(200); copy.AdvanceTo(200); Assert.Equal(w.Serialize(), copy.Serialize());
        e = Assert.Single(w.InspectExpectations()); Assert.Equal(ExpectationStatus.Satisfied, e.Status); Assert.True(e.ResultTick > e.BaseDeadline && e.ResultTick < e.Deadline);
        Assert.Empty(w.InspectMissingReports()); Assert.DoesNotContain(w.Trace, t => t.Type == "ActionStarted" && t.ActorId == 1 && t.Detail == "Clean" && t.Tick >= 20 && t.Tick < 120);
    }

    [Fact]
    public void MultipleRetainedCleanupOrdersDoNotIssueCompetingIntents()
    {
        var w = Load(json => {
            var state = json["initialState"]!; state.AsObject().Remove("authority"); state["tiles"]![1]!["traversalOverride"] = 1; state["tiles"]![0]!["definition"] = "corridor";
            state["cleanliness"]!["dirt"]!.AsArray().Add(new JsonObject { ["tileId"] = 1, ["units"] = 1 });
            state["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 2, ["kind"] = "CleanTile", ["tileId"] = 1, ["reportedUnits"] = 1 });
            state["jobSystem"]!["assignments"]!.AsArray().Add(new JsonObject { ["key"] = "second-cleanup", ["jobId"] = 2, ["actorId"] = 1, ["expires"] = 900 });
        }).CreateWorld(42);
        w.AdvanceTo(100); Assert.Equal(0, w.ActionsFailed); Assert.Equal(2, w.InspectLocalOrders(new(1)).Count);
        Assert.All(w.InspectLocalOrders(new(1)), o => Assert.Equal(LocalOrderStatus.Completed, o.Status)); Assert.Equal(3, w.InspectDirt().Sum(d => d.RemovedUnits));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void WrongAttemptWrongAccountUnrelatedAndDuplicateAcknowledgmentDoNotExtendCleanupDeadline()
    {
        var w = Load(json => {
            JsonObject Claim(string kind, long sender, long attempt, long delivery, long related = 0) => new() {
                ["senderAccount"] = sender, ["deliveryTick"] = delivery,
                ["message"] = new JsonObject { ["kind"] = kind, ["recipientKind"] = "Authority", ["taskId"] = 1, ["attemptId"] = attempt,
                    ["relatedMessageId"] = related, ["observedTick"] = 0,
                    ["subject"] = new JsonObject { ["subjectKind"] = "Attempt", ["subjectId"] = attempt, ["attribute"] = "WorkStatus" },
                    ["claim"] = new JsonObject { ["kind"] = "Text", ["text"] = "ClaimedComplete" } }
            };
            json["initialState"]!["information"]!["messages"] = new JsonArray(
                Claim("WorkCompleted", 1, 999, 12), Claim("WorkCompleted", 2, 1, 15), Claim("Observation", 1, 1, 20), Claim("OrderAccepted", 1, 1, 25, 5));
        }).CreateWorld(42, true);
        w.AdvanceTo(30); var e = Assert.Single(w.InspectExpectations()); Assert.Equal(8, e.AcknowledgmentTick); Assert.Equal(31, e.Deadline); Assert.Equal(0, e.ResultMessageId);
        Assert.Equal(2, w.InspectDirt()[0].Units); Assert.Empty(w.InspectMissingReports()); Assert.Equal(3, w.Trace.Count(t => t.Type == "WorkReportRejected"));
        w.AdvanceTo(31); Assert.Single(w.InspectMissingReports()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void RenamedEntitiesAndTranslatedRotatedGeometryPreserveTheCleanupWorkflow()
    {
        var original = Load().CreateWorld(42); original.AdvanceTo(300);
        var changed = Load(json => {
            var state = json["initialState"]!; state["id"] = "renamed-rotated-cleanup";
            foreach (var actor in state["actors"]!.AsArray()) { actor!["id"] = actor["id"]!.GetValue<int>() + 100; actor["name"] = "Renamed citizen"; }
            foreach (var item in state["items"]!.AsArray()) { item!["id"] = item["id"]!.GetValue<int>() + 200; item["owner"] = item["owner"]!.GetValue<int>() + 100; }
            foreach (var tile in state["tiles"]!.AsArray()) tile!["id"] = tile["id"]!.GetValue<int>() + 300;
            foreach (var entry in state["cleanliness"]!["dirt"]!.AsArray()) entry!["tileId"] = entry["tileId"]!.GetValue<int>() + 300;
            foreach (var job in state["jobSystem"]!["jobs"]!.AsArray()) { job!["id"] = job["id"]!.GetValue<int>() + 400; job["tileId"] = job["tileId"]!.GetValue<int>() + 300; }
            foreach (var assignment in state["jobSystem"]!["assignments"]!.AsArray()) { assignment!["actorId"] = assignment["actorId"]!.GetValue<int>() + 100; assignment["jobId"] = assignment["jobId"]!.GetValue<int>() + 400; }
            foreach (var entry in state["jobSystem"]!["checkIns"]!.AsArray()) entry!["actorId"] = entry["actorId"]!.GetValue<int>() + 100;
            foreach (var entry in state["routines"]!["assignments"]!.AsArray()) entry!["actorId"] = entry["actorId"]!.GetValue<int>() + 100;
            foreach (var terminal in state["information"]!["terminals"]!.AsArray()) terminal!["id"] = terminal["id"]!.GetValue<int>() + 500;
            foreach (var credential in state["information"]!["credentials"]!.AsArray()) {
                credential!["id"] = credential["id"]!.GetValue<int>() + 600; credential["holder"] = credential["holder"]!.GetValue<int>() + 100;
                credential["account"] = credential["account"]!.GetValue<int>() + 100; credential["cardItem"] = credential["cardItem"]!.GetValue<int>() + 200;
            }
            void Transform(JsonNode? node)
            {
                if (node is JsonObject obj) {
                    if (obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z")) { int x = obj["x"]!.GetValue<int>(), y = obj["y"]!.GetValue<int>(); obj["x"] = 17 - y; obj["y"] = -23 + x; obj["z"] = obj["z"]!.GetValue<int>() + 5; }
                    else foreach (var value in obj.Select(p => p.Value).ToArray()) Transform(value);
                }
                else if (node is JsonArray array) foreach (var value in array) Transform(value);
            }
            Transform(state);
        }).CreateWorld(42);
        changed.AdvanceTo(300); Assert.Equal(0, changed.ActionsFailed);
        Assert.Equal(original.InspectDirt().Select(d => (d.Units, d.RemovedUnits)), changed.InspectDirt().Select(d => (d.Units, d.RemovedUnits)));
        Assert.Equal(original.InspectExpectations().Select(e => (e.ActorId, e.Deadline, e.ResultTick, e.Status)), changed.InspectExpectations().Select(e => (e.ActorId - 100, e.Deadline, e.ResultTick, e.Status)));
        var before = Assert.Single(original.InspectMissingReports()); var after = Assert.Single(changed.InspectMissingReports());
        Assert.Equal((before.OpenedTick, before.AdmissionTick, before.WorkResolution, before.WorkerStatusResponse), (after.OpenedTick, after.AdmissionTick, after.WorkResolution, after.WorkerStatusResponse));
        Assert.Equal(1, changed.InspectAdmissionBuckets().Sum(b => b.Used)); Assert.Equal(changed.Serialize(), World.Restore(changed.Serialize()).Serialize());
    }

    [Fact]
    public void GeneratedCleanupOrderContinuationsPreserveReadsToolsDeadlinesAndSeparateResults()
    {
        LoadedScenario Variant(int i) => Load(json => { json["initialState"]!["tiles"]![1]!["traversalOverride"] = 81 + i * 2; json["initialState"]!["cleanliness"]!["cleanUnitTicks"] = 2 + i % 4; json["initialState"]!["information"]!["configuration"]!["inspectTicks"] = 1 + i % 3; });
        var expected = new string[20];
        for (int i = 0; i < 20; i++)
        {
            var loaded = Variant(i); var direct = loaded.CreateWorld((ulong)i, true); direct.AdvanceTo(300); expected[i] = direct.Digest(); direct.AssertAccounting();
            foreach (long tick in new long[] { -1, 3, 4, 6, 8, 31, 34, 52, 58, 63, 110, 218 })
            {
                var prefix = loaded.CreateWorld((ulong)i, true); if (tick >= 0) prefix.AdvanceTo(tick); var bytes = prefix.Serialize(); var restored = World.Restore(bytes, true);
                Assert.Equal(bytes, restored.Serialize()); restored.AdvanceTo(300); Assert.Equal(direct.Serialize(), restored.Serialize());
                Assert.Equal(direct.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
            }
            var chunked = loaded.CreateWorld((ulong)i); for (int tick = 0; tick <= 300; tick++) { chunked.AdvanceTo(tick); chunked.InspectLocalOrders(new(1)); chunked.InspectDirt(); chunked.AuthorityView(); }
            Assert.Equal(expected[i], chunked.Digest());
        }
        foreach (int parallel in new[] { 2, 4, 8 }) Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        { var w = Variant(i).CreateWorld((ulong)i); w.AdvanceTo(300); Assert.Equal(expected[i], w.Digest()); });
    }
}
