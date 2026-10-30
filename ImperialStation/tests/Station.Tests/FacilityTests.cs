using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class FacilityTests
{
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "facility_cleanup.json")))!.AsObject();
        change?.Invoke(json);
        if (change != null) json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "cleanup-work.json")), "facility-test");
    }
    private static JsonObject Receipt(long sender, long observed, long delivered, string key, long units = 2) => new()
    {
        ["senderAccount"] = sender, ["deliveryTick"] = delivered,
        ["message"] = new JsonObject { ["kind"] = "FacilityCondition", ["recipientKind"] = "Authority", ["observedTick"] = observed, ["operationKey"] = key,
            ["subject"] = new JsonObject { ["subjectKind"] = "Tile", ["subjectId"] = 3, ["attribute"] = "Dirt" },
            ["claim"] = new JsonObject { ["kind"] = "Number", ["number"] = units } }
    };
    private static void Messages(JsonObject json, params JsonObject[] entries) => json["initialState"]!["information"]!["messages"] = new JsonArray(entries.Cast<JsonNode>().ToArray());

    [Fact]
    public void ObservedDirtBecomesWorkOnlyAfterPhysicalPostAndReceipt()
    {
        var loaded = Load(); Assert.Empty(loaded.Scenario.ScriptedActors); var w = loaded.CreateWorld(42, true);
        w.AdvanceTo(0); Assert.Single(w.InspectFacilityObservations(new(3))); Assert.Empty(w.InspectJobs()); Assert.Empty(w.InspectMessages());
        w.AdvanceTo(5); Assert.Single(w.InspectMessages()); Assert.Empty(w.InspectJobs());
        w.AdvanceTo(7); var task = Assert.Single(w.InspectFacilityTasks()); Assert.Equal(7, task.CreatedTick); Assert.Equal(FacilityTaskStatus.Assigned, task.Status);
        Assert.Equal(2, Assert.Single(w.InspectWorkAttempts()).ActorId); Assert.Empty(w.InspectLocalOrders(new(2)));
        w.AdvanceTo(39); Assert.Equal(0, w.InspectDirt()[0].Units); Assert.Equal(FacilityTaskStatus.Assigned, w.InspectFacilityTasks()[0].Status);
        w.AdvanceTo(47); Assert.Equal(FacilityTaskStatus.ReportedComplete, w.InspectFacilityTasks()[0].Status); Assert.Equal(2, w.InspectDirt()[0].RemovedUnits);
        Assert.Equal(0, w.ActionsFailed); Assert.Empty(w.InspectMissingReports()); Assert.Equal(0, w.InspectAdmissionBuckets().Sum(b => b.Used)); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenConditionAndSeededBeliefsDoNotGenerateReports(bool seedKnowledge)
    {
        var w = Load(j => {
            j["initialState"]!["actors"]![2]!["position"]!["x"] = 0;
            if (seedKnowledge) j["initialState"]!["information"]!["facts"] = new JsonArray(new JsonObject {
                ["actorId"] = 3, ["key"] = Receipt(3, 0, 0, "seed")["message"]!["subject"]!.DeepClone(),
                ["value"] = new JsonObject { ["kind"] = "Number", ["number"] = 2 } });
        }).CreateWorld(42);
        w.AdvanceTo(300); Assert.Empty(w.InspectFacilityTasks()); Assert.Empty(w.InspectJobs()); Assert.Empty(w.InspectFacilityObservations(new(3)));
        Assert.Equal(2, w.InspectDirt()[0].Units);
    }

    [Fact]
    public void UnacknowledgedCleanupOrderIsReassignedAndTheLateReplyChangesNothing()
    {
        // The assigned cleaner's terminal is offline from tick 6 to 400, so it cannot read
        // the order posted at 7. After two configured hours (day 3,000 / 12 = 250 ticks)
        // the order goes to the other eligible cleaner through the ordinary supersede.
        var w = Load(j => { j["initialState"]!["horizon"] = 2000; j["initialState"]!["jobSystem"]!["checkIns"]![0]!["interval"] = 20; }).CreateWorld(42, true);
        w.ScheduleTerminalChange(6, 2, true, false); w.ScheduleTerminalChange(400, 2, true, true);
        w.AdvanceTo(256);
        var first = Assert.Single(w.InspectWorkAttempts()); Assert.Equal(2, first.ActorId); Assert.Null(first.AcknowledgmentReceivedTick);
        w.AdvanceTo(258);
        var reassigned = Assert.Single(w.Trace, e => e.Type == "FacilityOrderUnacknowledged"); Assert.Equal(257, reassigned.Tick);
        var attempts = w.InspectWorkAttempts().OrderBy(a => a.Id).ToArray();
        Assert.Equal(AssignmentStatus.Superseded, attempts[0].Assignment); Assert.Equal(1, attempts[1].ActorId);
        Assert.Equal(attempts[1].Id, w.InspectFacilityTasks()[0].AttemptId);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        var copy = World.Restore(w.Serialize());
        w.AdvanceTo(1000); copy.AdvanceTo(1000); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(0, w.InspectDirt()[0].Units);
        Assert.Equal(FacilityTaskStatus.ReportedComplete, w.InspectFacilityTasks()[0].Status);
        Assert.Contains(w.Trace, e => e.Type == "CleaningCompleted" && e.ActorId == 1 && e.Quantity == 1);
        Assert.DoesNotContain(w.Trace, e => e.Type == "CleaningCompleted" && e.ActorId == 2 && e.Quantity == 1);
        // The silent cleaner later reads the cancellation; its attempt never becomes current again.
        Assert.Equal(AssignmentStatus.Superseded, w.InspectWorkAttempts().Single(a => a.Id == attempts[0].Id).Assignment);
        Assert.Equal(1, w.InspectWorkAttempts().Count(a => a.Assignment == AssignmentStatus.Active));
        w.AssertAccounting();
    }

    [Fact]
    public void NoTerminalAccessRetainsReportWithoutAuthorityKnowledge()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(0, 1, false, false); w.ScheduleTerminalChange(0, 2, false, false);
        w.AdvanceTo(300); Assert.Empty(w.InspectFacilityTasks()); Assert.Empty(w.InspectMessages());
        Assert.Equal(1, Assert.Single(w.InspectFacilityObservations(new(3))).Generation);
        var restored = World.Restore(w.Serialize()); Assert.Equal(w.Serialize(), restored.Serialize());
        w.ScheduleTerminalChange(301, 2, true, true); restored.ScheduleTerminalChange(301, 2, true, true);
        w.AdvanceTo(900); restored.AdvanceTo(900); Assert.Equal(w.Serialize(), restored.Serialize()); Assert.Single(w.InspectFacilityTasks());
    }

    [Fact]
    public void ClaimsAreDeduplicatedAndMergedWithoutConsultingPhysicalDirt()
    {
        var w = Load(j => {
            j["initialState"]!["actors"]![2]!["position"]!["x"] = 0;
            j["initialState"]!["cleanliness"]!["dirt"]![0]!["units"] = 0;
            Messages(j, Receipt(3, 0, 1, "a"), Receipt(3, 0, 2, "a"), Receipt(3, 0, 3, "a", 9), Receipt(3, 1, 4, "b"));
        }).CreateWorld(42, true);
        w.AdvanceTo(5); var task = Assert.Single(w.InspectFacilityTasks()); Assert.Equal(2, task.ReportsReceived); Assert.Single(w.InspectWorkAttempts()); Assert.Equal(0, w.InspectDirt()[0].Units);
        Assert.Contains(w.Trace, e => e.Type == "FacilityReportRejected" && e.Detail == "DuplicateOperation");
        Assert.Contains(w.Trace, e => e.Type == "FacilityReportRejected" && e.Detail == "IdempotencyConflict");
        w.AdvanceTo(100); Assert.Equal(FacilityTaskStatus.ReportedComplete, w.InspectFacilityTasks()[0].Status); Assert.Equal(0, w.InspectDirt()[0].RemovedUnits);
        Assert.Equal("NoWorkNeeded", Assert.Single(w.InspectLocalOrders(new(2))).Reason);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 1)]
    public void ReceiptRequiresReporterDutyAndThreshold(long sender, long units)
    {
        var w = Load(j => { j["initialState"]!["actors"]![2]!["position"]!["x"] = 0; Messages(j, Receipt(sender, 0, 1, "invalid", units)); }).CreateWorld(42, true);
        w.AdvanceTo(20); Assert.Empty(w.InspectFacilityTasks()); Assert.Contains(w.Trace, e => e.Type == "FacilityReportRejected");
        Assert.DoesNotContain(w.InspectBeliefs(), b => b.Key.Equals(new FactKey(SubjectKind.Tile, 3, AttributeKind.Dirt)));
    }

    [Fact]
    public void LateOldObservationCannotReopenCompletedWorkButFreshObservationCan()
    {
        var w = Load(j => Messages(j, Receipt(3, 39, 80, "old"), Receipt(3, 100, 100, "new"))).CreateWorld(42, true);
        w.AdvanceTo(80); Assert.Single(w.InspectFacilityTasks()); Assert.Contains(w.Trace, e => e.Type == "FacilityReportStale");
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(200); copy.AdvanceTo(200); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(new long[] { 1, 2 }, w.InspectJobs().Select(j => j.Id)); Assert.Equal(2, w.InspectFacilityTasks().Count);
        Assert.All(w.InspectFacilityTasks(), t => Assert.Equal(FacilityTaskStatus.ReportedComplete, t.Status));
        Assert.Equal(2, w.InspectDirt()[0].RemovedUnits); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void NoEligibleDutyWorkerLeavesTaskPendingAcrossSnapshots()
    {
        var w = Load(j => {
            j["initialState"]!["routines"]!["assignments"]![0]!["cohort"] = 1;
            j["initialState"]!["routines"]!["assignments"]![1]!["cohort"] = 1;
        }).CreateWorld(42);
        w.AdvanceTo(300); Assert.Equal(FacilityTaskStatus.Pending, Assert.Single(w.InspectFacilityTasks()).Status); Assert.Empty(w.InspectWorkAttempts());
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(1100); copy.AdvanceTo(1100); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.NotEmpty(w.InspectWorkAttempts());
    }

    [Fact]
    public void CompletionOlderThanReceivedConditionCreatesAFollowUpAttempt()
    {
        var w = Load(j => Messages(j, Receipt(3, 44, 44, "after-cleaning"))).CreateWorld(42);
        w.AdvanceTo(47); Assert.Equal(2, w.InspectWorkAttempts().Count); Assert.Equal(FacilityTaskStatus.Assigned, Assert.Single(w.InspectFacilityTasks()).Status);
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(200); copy.AdvanceTo(200); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(FacilityTaskStatus.ReportedComplete, w.InspectFacilityTasks()[0].Status); Assert.Equal(2, w.InspectFacilityTasks()[0].AttemptId);
        Assert.Equal(2, w.InspectDirt()[0].RemovedUnits);
    }

    [Fact]
    public void RejectedWorkNeedsFreshConditionEvidenceBeforeRetry()
    {
        var w = Load(j => {
            var items = j["initialState"]!["items"]!.AsArray(); items.Remove(items.Single(i => i!["id"]!.GetValue<int>() == 9));
            Messages(j, Receipt(3, 200, 200, "fresh-after-rejection"));
        }).CreateWorld(42);
        w.AdvanceTo(150); Assert.Equal(FacilityTaskStatus.NeedsAttention, Assert.Single(w.InspectFacilityTasks()).Status); Assert.Single(w.InspectWorkAttempts());
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(300); copy.AdvanceTo(300); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(2, w.InspectWorkAttempts().Count); Assert.Equal(FacilityTaskStatus.NeedsAttention, w.InspectFacilityTasks()[0].Status); Assert.Equal(2, w.InspectDirt()[0].Units);
    }

    [Fact]
    public void FutureDatedClaimsDoNotCreateTasks()
    {
        var w = Load(j => { j["initialState"]!["actors"]![2]!["position"]!["x"] = 0; Messages(j, Receipt(3, 100, 1, "future")); }).CreateWorld(42, true);
        w.AdvanceTo(20); Assert.Empty(w.InspectFacilityTasks()); Assert.Contains(w.Trace, e => e.Type == "FacilityReportRejected");
    }

    [Fact]
    public void SeparateObserversMergeIntoOneTaskAndOneAssignment()
    {
        var w = Load(j => {
            j["initialState"]!["facility"]!["reportingRoles"] = new JsonArray("guard", "cleaner");
            j["initialState"]!["actors"]![1]!["position"]!["y"] = 0;
        }).CreateWorld(42);
        w.AdvanceTo(200); var task = Assert.Single(w.InspectFacilityTasks()); Assert.True(task.ReportsReceived >= 2);
        Assert.Single(w.InspectWorkAttempts()); Assert.Equal(FacilityTaskStatus.ReportedComplete, task.Status);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void CompletedObservationResetsCooldownAndNewDirtQueuesAnotherGeneration()
    {
        var w = Load(j => j["initialState"]!["jobSystem"]!["checkIns"]![2]!["interval"] = 1000).CreateWorld(42, true);
        w.AdvanceTo(47); w.Submit(50, new(new(3), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(51);
        Assert.False(Assert.Single(w.InspectFacilityObservations(new(3))).Adverse);
        w.ScheduleSpill(52, new(2, 0, 0), 2); w.AdvanceTo(200);
        Assert.Equal(2, w.InspectFacilityTasks().Count); Assert.Equal(2, Assert.Single(w.InspectFacilityObservations(new(3))).Generation);
        Assert.Equal(4, w.InspectDirt()[0].RemovedUnits); Assert.All(w.InspectFacilityTasks(), t => Assert.Equal(FacilityTaskStatus.ReportedComplete, t.Status));
    }

    [Fact]
    public void ObservedMachineFaultCreatesAnAutonomousRepairTask()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "work_orders.json")))!.AsObject();
        var state = json["initialState"]!; json["assertions"] = new JsonArray(); state["horizon"] = 500;
        state["actors"]![0]!["position"]!["x"] = 1;
        state["jobSystem"]!["jobs"] = new JsonArray(); state["jobSystem"]!["assignments"] = new JsonArray();
        state["authority"] = new JsonObject { ["graceTicks"] = 1000 };
        state["facility"] = new JsonObject { ["reportingRoles"] = new JsonArray("worker") };
        state["routines"] = new JsonObject { ["configuration"] = new JsonObject { ["dayTicks"] = 3000, ["cohorts"] = 3, ["dutyTicks"] = 1000, ["checkInWindow"] = 1, ["periodicCheckInTicks"] = 20 },
            ["assignments"] = new JsonArray(new JsonObject { ["actorId"] = 1, ["cohort"] = 0, ["restTile"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 } }) };
        var loaded = new LoadedScenario(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "facility-repair");
        var w = loaded.CreateWorld(42, true); w.AdvanceTo(500);
        var task = Assert.Single(w.InspectFacilityTasks()); Assert.Equal(SubjectKind.Machine, task.Subject.SubjectKind);
        Assert.Equal(FacilityTaskStatus.ReportedComplete, task.Status); Assert.True(Assert.Single(w.InspectMachines()).Operational);
        Assert.Equal(WorkKind.RepairMachine, Assert.Single(w.InspectJobs()).Kind); Assert.Equal(0, w.ActionsFailed); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void GeneratedCheckpointsTracingGettersAndConcurrentWorldsAreNeutral()
    {
        string Run(int seed, bool observe)
        {
            var loaded = Load(j => { j["initialState"]!["cleanliness"]!["dirt"]![0]!["units"] = 2 + seed % 5; j["initialState"]!["cleanliness"]!["meanSoilingTicks"] = 60 + seed; });
            var direct = loaded.CreateWorld((ulong)seed); direct.AdvanceTo(300);
            if (!observe) return direct.Digest();
            foreach (int checkpoint in new[] { 0, 4, 5, 6, 7, 9, 28, 30, 39, 47, 100, 250 })
            {
                var prefix = loaded.CreateWorld((ulong)seed, true); prefix.AdvanceTo(checkpoint); byte[] bytes = prefix.Serialize(); var resumed = World.Restore(bytes, true);
                Assert.Equal(bytes, resumed.Serialize());
                for (int t = checkpoint + 1; t < 300; t += 17) { resumed.AdvanceTo(t); resumed.InspectFacilityTasks(); resumed.InspectFacilityObservations(new(3)); resumed.AuthorityView(); }
                resumed.AdvanceTo(300); prefix.AdvanceTo(300); Assert.Equal(direct.Serialize(), resumed.Serialize());
                Assert.Equal(prefix.Trace.Where(e => e.Tick > checkpoint).Select(Runs.EventJson), resumed.Trace.Select(Runs.EventJson)); resumed.AssertAccounting();
            }
            return direct.Digest();
        }
        var expected = Enumerable.Range(0, 20).Select(s => Run(s, true)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[20]; Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, false)); Assert.Equal(expected, actual); }
    }
}
