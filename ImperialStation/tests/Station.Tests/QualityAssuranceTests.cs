using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class QualityAssuranceTests
{
    private static readonly Coord Site = new(1, 0, 0);
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "quality_verification.json")))!.AsObject(); change?.Invoke(json);
        if (change != null) json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "qa-test");
    }
    private static World ClaimFixture(bool wrong = true, int rate = 1000, ulong seed = 42)
    {
        var w = Load(j => {
            var s = j["initialState"]!; s.AsObject().Remove("facility"); j["disturbances"] = new JsonArray();
            s["qualityAssurance"]!["samplePermille"] = rate;
            s["routines"]!["assignments"]!.AsArray().RemoveAt(0); s["jobSystem"]!["checkIns"]!.AsArray().RemoveAt(0);
            s["jobSystem"]!["jobs"] = new JsonArray(new JsonObject { ["id"] = 1, ["machineId"] = 1 });
            s["jobSystem"]!["assignments"] = new JsonArray(new JsonObject { ["key"] = "repair", ["jobId"] = 1, ["actorId"] = 1, ["expires"] = 1000 });
            s["items"]!.AsArray().Add(new JsonObject { ["id"] = 8, ["resource"] = wrong ? "wrong" : "correct", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = 1 });
        }).CreateWorld(seed, true);
        w.Submit(0, new(new(1), ActionKind.Login, Site, serviceId: 1));
        w.Submit(1, new(new(1), ActionKind.Install, Site, new(8), serviceId: 1));
        w.Submit(4, new(new(1), ActionKind.PostMessage, Site, serviceId: 1, message: new MessageDraft(MessageKind.OrderAccepted, RecipientKind.Authority, default,
            new(SubjectKind.Attempt, 1, AttributeKind.WorkStatus), new(FactValueKind.Text, text: "Accepted"), 4, 1, 1, 1, operationKey: "accept")));
        w.Submit(5, new(new(1), ActionKind.PostMessage, Site, serviceId: 1, message: Completion(5, "complete")));
        return w;
    }
    private static MessageDraft Completion(long observed, string key) => new(MessageKind.WorkCompleted, RecipientKind.Authority, default,
        new(SubjectKind.Machine, 1, AttributeKind.Operational), FactValue.Boolean(true), observed, 1, 1, operationKey: key);

    [Fact]
    public void QAFollowsReceiptDelayOrderReadInspectionAndTerminalResult()
    {
        var loaded = Load(); var w = loaded.CreateWorld(42, true); Assert.Empty(loaded.Scenario.ScriptedActors);
        w.AdvanceTo(117); Assert.Empty(w.InspectQualityAssurance()); w.AdvanceTo(118);
        var q = Assert.Single(w.InspectQualityAssurance()); Assert.Equal(QualityAssuranceStatus.Scheduled, q.Status); Assert.Equal(123, q.DueTick);
        w.AdvanceTo(123); Assert.Empty(w.InspectLocalOrders(new(2))); Assert.Single(w.InspectWorkAttempts());
        w.AdvanceTo(125); Assert.Equal(QualityAssuranceStatus.Assigned, w.InspectQualityAssurance()[0].Status); Assert.Empty(w.InspectLocalOrders(new(2)));
        w.AdvanceTo(134); Assert.Equal(LocalOrderStatus.AcknowledgmentPending, Assert.Single(w.InspectLocalOrders(new(2))).Status); Assert.Empty(w.InspectVerifications(new(2)));
        w.AdvanceTo(139); var observed = Assert.Single(w.InspectVerifications(new(2))); Assert.False(observed.Discrepancy); Assert.Equal("correct", observed.RequiredResource);
        Assert.Equal(QualityAssuranceStatus.Assigned, w.InspectQualityAssurance()[0].Status);
        w.AdvanceTo(144); Assert.Equal(QualityAssuranceStatus.ReportedClear, w.InspectQualityAssurance()[0].Status); Assert.Single(w.InspectQualityAssurance());
        Assert.Equal("VerifyMachine", w.InspectExpectations().Single(e => e.AttemptId == 2).TaskKind); Assert.Equal(0, w.ActionsFailed);
        Assert.DoesNotContain(w.Trace, e => e.ActorId == 2 && e.Type == "ItemInstalled"); w.AssertAccounting();
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
    }

    [Fact]
    public void FalseCompletionTriggersVerificationAndOnlyLocalInspectionFindsMismatch()
    {
        var falseWorld = ClaimFixture(); var trueWorld = ClaimFixture(false);
        foreach (var w in new[] { falseWorld, trueWorld }) w.AdvanceTo(15);
        Assert.False(falseWorld.InspectMachines()[0].Operational); Assert.True(trueWorld.InspectMachines()[0].Operational);
        var bad = Assert.Single(falseWorld.InspectQualityAssurance()); var good = Assert.Single(trueWorld.InspectQualityAssurance());
        Assert.Equal((bad.SampleRoll, bad.ReceivedTick, bad.DueTick, bad.VerificationJobId, bad.VerificationAttemptId), (good.SampleRoll, good.ReceivedTick, good.DueTick, good.VerificationJobId, good.VerificationAttemptId));
        Assert.Empty(falseWorld.InspectVerifications(new(2))); Assert.DoesNotContain(falseWorld.InspectKnowledge(new(2)), f => f.Key.Attribute == AttributeKind.InstalledResource);
        foreach (var w in new[] { falseWorld, trueWorld }) w.AdvanceTo(100);
        Assert.Equal(QualityAssuranceStatus.ReportedDiscrepancy, falseWorld.InspectQualityAssurance()[0].Status); Assert.Equal(QualityAssuranceStatus.ReportedClear, trueWorld.InspectQualityAssurance()[0].Status);
        var v = Assert.Single(falseWorld.InspectVerifications(new(2))); Assert.Equal("correct", v.RequiredResource); Assert.Equal("wrong", v.InstalledResource);
        Assert.Equal("ResolveJobDiscrepancy", Assert.Single(falseWorld.InspectLocalOrders(new(2))).Reason);
        Assert.DoesNotContain(falseWorld.InspectKnowledge(new(2)), f => f.Key.SubjectKind == SubjectKind.Record);
        Assert.Equal(falseWorld.Serialize(), World.Restore(falseWorld.Serialize()).Serialize());
    }

    [Fact]
    public void RepeatedCompletionIdsCannotResampleTheSameAttempt()
    {
        var w = ClaimFixture();
        w.Submit(30, new(new(1), ActionKind.PostMessage, Site, serviceId: 1, message: Completion(5, "complete")));
        w.Submit(40, new(new(1), ActionKind.PostMessage, Site, serviceId: 1, message: Completion(40, "second-completion")));
        w.AdvanceTo(100); Assert.Single(w.InspectQualityAssurance()); Assert.Single(w.Trace, e => e.Type == "QualitySampled");
        Assert.Equal(2, w.InspectWorkAttempts().Count); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void SamplingMatchesIndependentStreamAndRateBoundaries()
    {
        Assert.Equal(250, new QualityAssuranceConfig().SamplePermille); Assert.Equal(18000, new QualityAssuranceConfig().DelayTicks);
        foreach (int invalid in new[] { -1, 1001 }) Assert.Throws<ArgumentException>(() => new QualityAssuranceConfig(invalid));
        Assert.Throws<ArgumentException>(() => new QualityAssuranceConfig(delayTicks: -1));
        foreach (int rate in new[] { 0, 250, 1000 }) for (ulong seed = 0; seed < 20; seed++)
        {
            var w = ClaimFixture(rate: rate, seed: seed); w.AdvanceTo(7); var q = Assert.Single(w.InspectQualityAssurance());
            int roll = Pcg32.Create(seed, "qa").Next(1000); Assert.Equal(roll, q.SampleRoll);
            Assert.Equal(roll < rate ? QualityAssuranceStatus.Scheduled : QualityAssuranceStatus.NotSampled, q.Status);
            var copy = World.Restore(w.Serialize()); w.AdvanceTo(100); copy.AdvanceTo(100); Assert.Equal(w.Serialize(), copy.Serialize());
        }
    }

    [Fact]
    public void DisabledQAAndPhysicalRepairAloneCannotCreateVerification()
    {
        var w = Load(j => j["initialState"]!.AsObject().Remove("qualityAssurance")).CreateWorld(42); w.AdvanceTo(300); Assert.Empty(w.InspectQualityAssurance()); Assert.Single(w.InspectJobs());
        var delayed = Load().CreateWorld(42); delayed.AdvanceTo(114); Assert.True(delayed.InspectMachines()[0].Operational); Assert.Empty(delayed.InspectQualityAssurance());
    }

    [Fact]
    public void DeliveredUnreadVerificationDoesNotWakeOrInspectSupervisor()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(0, 2, false, false); w.AdvanceTo(300);
        Assert.Equal(QualityAssuranceStatus.Assigned, Assert.Single(w.InspectQualityAssurance()).Status); Assert.Empty(w.InspectLocalOrders(new(2))); Assert.Empty(w.InspectVerifications(new(2)));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void OffDutySupervisorWaitsUntilKnownDutyBoundary()
    {
        var w = Load(j => j["initialState"]!["routines"]!["assignments"]![1]!["cohort"] = 1).CreateWorld(42);
        w.AdvanceTo(300); Assert.Equal(QualityAssuranceStatus.PendingSupervisor, Assert.Single(w.InspectQualityAssurance()).Status); Assert.Single(w.InspectWorkAttempts());
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(1100); copy.AdvanceTo(1100); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(QualityAssuranceStatus.ReportedClear, w.InspectQualityAssurance()[0].Status);
    }

    [Fact]
    public void CapturedInspectionSurvivesLaterPhysicalFaultAndReturnTravel()
    {
        var w = Load().CreateWorld(42); w.ScheduleMachineFault(140, 1); w.AdvanceTo(144);
        Assert.False(w.InspectMachines()[0].Operational); Assert.True(Assert.Single(w.InspectVerifications(new(2))).Operational);
        Assert.Equal(QualityAssuranceStatus.ReportedClear, Assert.Single(w.InspectQualityAssurance()).Status);
        var result = w.InspectMessages().Single(m => m.Id == w.InspectQualityAssurance()[0].ResultMessageId); Assert.Equal(139, result.Draft.ObservedTick);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void CancelledInspectionHasNoResultAndResumeRequiresFiniteInspection()
    {
        var w = Load().CreateWorld(42, true); w.Submit(138, new(new(2), ActionKind.SuspendWork, Site, attemptId: 2));
        w.AdvanceTo(140); Assert.Empty(w.InspectVerifications(new(2))); Assert.Equal(LocalOrderStatus.Suspended, Assert.Single(w.InspectLocalOrders(new(2))).Status);
        w.Submit(145, new(new(2), ActionKind.ResumeWork, Site, attemptId: 2)); var copy = World.Restore(w.Serialize()); w.AdvanceTo(180); copy.AdvanceTo(180);
        Assert.Equal(w.Serialize(), copy.Serialize()); Assert.True(Assert.Single(w.InspectVerifications(new(2))).Tick >= 147); Assert.Equal(QualityAssuranceStatus.ReportedClear, w.InspectQualityAssurance()[0].Status);
    }

    [Fact]
    public void VerificationLeaseExpiryDoesNotFabricateAResult()
    {
        var w = Load(j => j["initialState"]!["qualityAssurance"]!["orderTicks"] = 12).CreateWorld(42); w.AdvanceTo(300);
        Assert.Empty(w.InspectVerifications(new(2))); Assert.Equal(QualityAssuranceStatus.NeedsAttention, Assert.Single(w.InspectQualityAssurance()).Status);
        Assert.Equal(0, w.InspectQualityAssurance()[0].ResultMessageId); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void VerificationPlannerUsesPublishedRouteAndAlwaysInspects()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(0); var view = w.ActorView(new(2));
        var plan = VerificationPlanning.Plan(view, 1); Assert.Equal(PlanOutcome.PlanFound, plan.Outcome);
        Assert.Equal(new[] { ActionKind.Move, ActionKind.InspectMachine }, plan.Steps.Select(s => s.Intent.Kind));
        Assert.Equal(PlanOutcome.BudgetExhausted, VerificationPlanning.Plan(view, 1, budget: 1).Outcome);
        Assert.Equal(PlanOutcome.NoPlan, VerificationPlanning.Plan(view, 999).Outcome);
    }

    [Fact]
    public void CleanupCompletionDoesNotEnterRepairQASampling()
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "facility_cleanup.json")))!.AsObject();
        j["initialState"]!["qualityAssurance"] = new JsonObject { ["samplePermille"] = 1000, ["delayTicks"] = 0 };
        var loaded = new LoadedScenario(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "cleanup-work.json")), "cleanup-with-qa");
        var w = loaded.CreateWorld(42); w.AdvanceTo(300); Assert.Empty(w.InspectQualityAssurance()); Assert.Single(w.InspectJobs());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void OverdueVerificationCanAssignAnotherSupervisorWithoutRepairingTheMachine()
    {
        var w = Load(j => {
            var s = j["initialState"]!; s["configuration"]!["authorityNominalTicks"] = 1;
            s["authority"]!["graceTicks"] = 0; s["authority"]!["slackMinimum"] = 1000; s["authority"]!["slackMargin"] = 0;
            s["actors"]!.AsArray().Add(new JsonObject { ["id"] = 3, ["name"] = "Other supervisor", ["role"] = "supervisor", ["position"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 } });
            s["items"]!.AsArray().Add(new JsonObject { ["id"] = 8, ["resource"] = "card", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = 3 });
            s["information"]!["credentials"]!.AsArray().Add(new JsonObject { ["id"] = 3, ["holder"] = 3, ["account"] = 3, ["cardItem"] = 8 });
            s["information"]!["terminals"]!.AsArray().Add(new JsonObject { ["id"] = 3, ["position"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 }, ["permittedRoles"] = new JsonArray("supervisor"), ["capabilities"] = "Inbox, Post" });
            s["routines"]!["assignments"]!.AsArray().Add(new JsonObject { ["actorId"] = 3, ["cohort"] = 0, ["restTile"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 } });
            s["jobSystem"]!["checkIns"]!.AsArray().Add(new JsonObject { ["actorId"] = 3, ["firstTick"] = 0, ["interval"] = 20 });
        }).CreateWorld(42, true);
        w.ScheduleTerminalChange(140, 2, false, false); w.ScheduleTerminalChange(250, 2, true, true); w.AdvanceTo(800);
        var q = Assert.Single(w.InspectQualityAssurance()); Assert.Equal(QualityAssuranceStatus.ReportedClear, q.Status); Assert.True(q.VerificationAttemptId > 2);
        Assert.True(w.InspectMissingReports().Single(i => i.AttemptId == 2).ReplacementAttemptId > 0);
        Assert.Equal(q.VerificationAttemptId, w.InspectMessages().Single(m => m.Id == q.ResultMessageId).Draft.AttemptId);
        Assert.DoesNotContain(w.Trace, e => e.ActorId != 1 && e.Type == "ItemInstalled"); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void SnapshotRejectsForgedQAReceiptSamplingAndDueTime()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(118); var q = Assert.Single(w.InspectQualityAssurance()); byte[] bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { writer.Write(q.RepairAttemptId); writer.Write(q.RepairJobId); writer.Write(q.MachineId); writer.Write(q.CompletionMessageId); writer.Write(q.ReceivedTick); writer.Write(q.SampleRoll); }
        byte[] pattern = buffer.ToArray(); int index = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var mutation in new[] { (Offset: index + 24, Value: 0L), (Offset: index + 32, Value: 117L), (Offset: index + 44, Value: 124L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Value).CopyTo(bad, mutation.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
        var invalidRoll = bytes.ToArray(); BitConverter.GetBytes(1000).CopyTo(invalidRoll, index + 40); Assert.Throws<ArgumentException>(() => World.Restore(invalidRoll));
    }

    [Fact]
    public void GeneratedCheckpointsAndConcurrentWorldsRemainDeterministic()
    {
        string Run(int seed, bool verify)
        {
            var loaded = Load(j => { j["initialState"]!["qualityAssurance"]!["delayTicks"] = seed % 8; j["initialState"]!["qualityAssurance"]!["samplePermille"] = 250 + seed * 35; });
            var direct = loaded.CreateWorld((ulong)seed); direct.AdvanceTo(300);
            if (verify) foreach (int tick in new[] { 0, 117, 118, 120, 123, 124, 125, 134, 136, 138, 139, 143, 144, 160 })
            {
                var prefix = loaded.CreateWorld((ulong)seed, true); prefix.AdvanceTo(tick); var bytes = prefix.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
                for (int t = tick + 1; t < 300; t += 13) { copy.AdvanceTo(t); copy.InspectQualityAssurance(); copy.InspectVerifications(new(2)); copy.AuthorityView(); }
                copy.AdvanceTo(300); prefix.AdvanceTo(300); Assert.Equal(direct.Serialize(), copy.Serialize()); copy.AssertAccounting();
                Assert.Equal(prefix.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
            }
            return direct.Digest();
        }
        var expected = Enumerable.Range(0, 20).Select(s => Run(s, true)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[20]; Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, false)); Assert.Equal(expected, actual); }
    }
}
