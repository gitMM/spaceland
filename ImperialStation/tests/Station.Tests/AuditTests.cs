using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class AuditTests
{
    private static readonly Id<ActorTag> Supervisor = new(2);
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "discrepancy_audit.json")))!.AsObject(); change?.Invoke(json);
        if (change != null) json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "audit-test");
    }
    private static AuditSnapshot Audit(World world) => Assert.Single(world.InspectAudits(Supervisor));
    private static World Finished()
    { var w = Load().CreateWorld(42, true); w.AdvanceTo(300); return w; }

    [Fact]
    public void OfflineAuditDeskBacksOffUntilRealProgressAndRestoresExactly()
    {
        var w = Load().CreateWorld(42, true);
        w.ScheduleTerminalChange(30, 2, false, false);
        w.ScheduleTerminalChange(4030, 2, true, true);
        w.AdvanceTo(2000);
        Assert.True(Audit(w).Status is AuditStatus.QueryingAttempts or AuditStatus.QueryingReports);
        Assert.InRange(w.Trace.Count(e => e.ActorId == 2 && e.Type == "ActionFailed" && e.Detail == "RequiredServiceNotPresent"), 1, 20);
        var restored = World.Restore(w.Serialize(), true);
        w.ClearRecordedTrace(); restored.ClearRecordedTrace();
        w.AdvanceTo(8000); restored.AdvanceTo(8000);
        Assert.Equal(AuditStatus.ReadyToFile, Audit(w).Status);
        Assert.Equal(w.Digest(), restored.Digest());
        Assert.Equal(w.Trace.Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void BoundAuditDeskIsRecheckedDespiteAnUnvisitedRememberedOutage()
    {
        // The supervisor learns that a far desk (3) is down, then posts at a nearer working
        // one (4) and never revisits it. That less-confirmed outage stays due forever; the
        // alternatives rule rechecked only it, so the audit never retried its own desk (2).
        // The 256-citizen pressure run left one case pending this way.
        var w = Load(j => {
            var info = j["initialState"]!["information"]!;
            var far = info["terminals"]![1]!.DeepClone(); far["id"] = 3; far["capabilities"] = "Inbox, Post"; far["position"]!["x"] = 2; info["terminals"]!.AsArray().Add(far);
            var near = info["terminals"]![1]!.DeepClone(); near["id"] = 4; near["capabilities"] = "Inbox, Post"; near["position"]!["x"] = 1; info["terminals"]!.AsArray().Add(near);
            foreach (var actor in j["initialState"]!["actors"]!.AsArray()) if ((long)actor!["id"]! == Supervisor.Value) actor["position"]!["x"] = 2;
        }).CreateWorld(42, true);
        w.ScheduleTerminalChange(0, 3, false, false); w.ScheduleTerminalChange(0, 2, false, false); w.ScheduleTerminalChange(4030, 2, true, true);
        w.AdvanceTo(8000);
        Assert.Equal(AuditStatus.ReadyToFile, Audit(w).Status);
        Assert.Contains(w.InspectTerminalOutages(Supervisor), o => o.TerminalId == 3 && o.Observations == 1);
        // The bound desk's rechecks still back off; restoration is learned only on arrival.
        var learned = w.Trace.Where(e => e.Type == "TerminalOutageLearned" && e.TerminalId == 2).Select(e => e.Tick).ToArray();
        Assert.True(learned.Length >= 4); Assert.All(learned.Zip(learned.Skip(1)), p => Assert.True(p.Second > p.First));
        Assert.Contains(w.Trace, e => e.Type == "TerminalRestorationObserved" && e.TerminalId == 2 && e.Tick > 4030);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void UnrelatedMachineHistoryCannotExhaustTheAuditPageBudget()
    {
        var w = Load(j => {
            var s = j["initialState"]!; var machine = s["work"]!["machines"]![0]!.DeepClone(); machine["id"] = 2; s["work"]!["machines"]!.AsArray().Add(machine);
            for (int id = 100; id < 180; id++)
            {
                s["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = id, ["machineId"] = 2 });
                s["jobSystem"]!["assignments"]!.AsArray().Add(new JsonObject { ["key"] = "unrelated/" + id, ["jobId"] = id, ["actorId"] = 1, ["expires"] = 1000 });
            }
        }).CreateWorld(42, true);
        w.AdvanceTo(40); var copy = World.Restore(w.Serialize()); w.AdvanceTo(300); copy.AdvanceTo(300);
        Assert.Equal(AuditStatus.ReadyToFile, Audit(w).Status); Assert.Equal(10, Audit(w).Pages);
        Assert.Equal(5, Audit(w).Evidence.Count); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.All(Audit(w).Evidence, e => Assert.Contains(e.Fields, f => f.Attribute == AttributeKind.MachineId && f.Value.Number == 1));
    }

    [Fact]
    public void InspectionCreatesGoalButOnlyCompletedAssignedDeskQueriesEstablishResponsibility()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(28); Assert.Empty(w.InspectAudits(Supervisor));
        w.AdvanceTo(29); Assert.Equal(AuditStatus.QueryingAttempts, Audit(w).Status); Assert.Null(Audit(w).Finding); Assert.Empty(Audit(w).Evidence);
        Assert.DoesNotContain(w.InspectKnowledge(Supervisor), f => f.Key.SubjectKind == SubjectKind.Record);
        w.AdvanceTo(300); var audit = Audit(w); Assert.Equal(AuditStatus.ReadyToFile, audit.Status); Assert.Equal(2, audit.TerminalId);
        var finding = audit.Finding!; Assert.Equal((1L, 1L, 1L, 1L), (finding.JobId, finding.AttemptId, finding.MachineId, finding.AccountId)); Assert.Equal("WrongInstalledPart", finding.Reason);
        Assert.Equal(5, audit.Evidence.Count); Assert.Equal(10, audit.Pages); Assert.Equal(0, w.ActionsFailed);
        Assert.All(audit.QueryActionIds, id => Assert.Contains(w.Trace, e => e.Type == "RecordsQueried" && e.ActionId == id && e.ActorId == 2 && e.TerminalId == 2));
        Assert.All(audit.Evidence, row => Assert.DoesNotContain(row.Fields, f => f.Attribute is AttributeKind.Operational or AttributeKind.InstalledResource));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize()); w.AssertAccounting();
        var run = Runs.Execute(Load(), 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
    }

    [Theory]
    [InlineData("no-grants")]
    [InlineData("missing-report-grant")]
    [InlineData("split-desks")]
    public void MissingCompleteAssignedDeskPermissionIsReviewNotAccountAttribution(string kind)
    {
        var w = Load(j => {
            var info = j["initialState"]!["information"]!;
            if (kind == "no-grants") info["recordAccess"] = new JsonArray();
            else if (kind == "missing-report-grant") info["recordAccess"]!.AsArray().RemoveAt(1);
            else { var desk = info["terminals"]![1]!.DeepClone(); desk["id"] = 3; info["terminals"]!.AsArray().Add(desk); info["recordAccess"]![1]!["terminalId"] = 3; }
        }).CreateWorld(42, true); w.AdvanceTo(300);
        Assert.Equal(AuditStatus.NeedsReview, Audit(w).Status); Assert.Equal("NoAssignedRecordsTerminal", Audit(w).Reason); Assert.Null(Audit(w).Finding); Assert.Empty(Audit(w).Evidence);
        Assert.DoesNotContain(w.Trace, e => e.Type == "RecordsQueried"); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void AssignedDeskIsNotReplacedByAnUnassignedNearbyDesk()
    {
        var w = Load(j => {
            var info = j["initialState"]!["information"]!; var desk = info["terminals"]![1]!.DeepClone(); desk["id"] = 3; info["terminals"]!.AsArray().Add(desk);
            foreach (var grant in info["recordAccess"]!.AsArray()) grant!["terminalId"] = 3;
        }).CreateWorld(42, true);
        w.ScheduleTerminalChange(0, 3, false, false); w.AdvanceTo(150); Assert.Equal(3, Audit(w).TerminalId); Assert.Null(Audit(w).Finding); Assert.Empty(Audit(w).Evidence);
        Assert.DoesNotContain(w.Trace, e => e.Type == "RecordsQueried");
        w.ScheduleTerminalChange(151, 3, true, true); w.AdvanceTo(600); Assert.Equal(AuditStatus.ReadyToFile, Audit(w).Status);
        Assert.All(w.Trace.Where(e => e.Type == "RecordsQueried"), e => Assert.Equal(3, e.TerminalId));
    }

    [Fact]
    public void PhysicalChangeAfterInspectionCannotRewriteAuditEvidence()
    {
        var w = Load().CreateWorld(42, true); w.ScheduleMachineFault(30, 1); w.AdvanceTo(300);
        Assert.Equal(0, w.InspectMachines()[0].InstalledItemId);
        Assert.Equal("wrong", Assert.Single(w.InspectVerifications(Supervisor)).InstalledResource); Assert.Equal(AuditStatus.ReadyToFile, Audit(w).Status);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void CorrectInstallationDoesNotCreateAnAuditEvenWithAccessGrants()
    {
        var w = Load(j => j["initialState"]!["items"]!.AsArray().Last()!["resource"] = "correct").CreateWorld(42, true);
        w.AdvanceTo(300); Assert.Empty(w.InspectAudits(Supervisor)); Assert.DoesNotContain(w.Trace, e => e.Type == "RecordsQueried");
    }

    [Fact]
    public void PartialKnowledgeDisclosureCannotBecomeRetainedEvidence()
    {
        var w = Load(j => j["initialState"]!["information"]!["configuration"]!["knowledgeCapacity"] = 20).CreateWorld(42, true); w.AdvanceTo(300);
        Assert.Equal(AuditStatus.NeedsReview, Audit(w).Status); Assert.Equal("IncompleteDisclosure", Audit(w).Reason); Assert.Null(Audit(w).Finding);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void CancelledQueryDisclosesNothingAndRetryKeepsCursor()
    {
        var w = Load().CreateWorld(42, true); long start = -1;
        for (int tick = 29; tick < 80; tick++) { w.AdvanceTo(tick); var query = w.Trace.LastOrDefault(e => e.Type == "ActionStarted" && e.ActorId == 2 && e.Detail == "QueryRecords"); if (query != null) { start = query.Tick; break; } }
        Assert.True(start >= 0); w.Submit(start + 1, new(Supervisor, ActionKind.Cancel, default)); w.AdvanceTo(start + 4);
        Assert.Empty(Audit(w).Evidence); Assert.Equal(0, Audit(w).Pages); Assert.Equal(0, Audit(w).Cursor); Assert.Null(w.ActorView(Supervisor).LastRecordQuery);
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(300); copy.AdvanceTo(300);
        Assert.Equal(AuditStatus.ReadyToFile, Audit(w).Status); Assert.Equal(w.Serialize(), copy.Serialize());
    }

    [Fact]
    public void AdoptedOffDutyRosterRetainsAuditUntilNextDuty()
    {
        var w = Load().CreateWorld(42, true);
        w.ScheduleRosterChange(30, new RosterAssignmentSeed(Supervisor, 1, new Coord(0, 0, 0)), "off-duty");
        w.AdvanceTo(100); Assert.Empty(Audit(w).Evidence); Assert.Equal(AuditStatus.QueryingAttempts, Audit(w).Status);
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(1300); copy.AdvanceTo(1300);
        Assert.Equal(AuditStatus.ReadyToFile, Audit(w).Status); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.DoesNotContain(w.Trace, e => e.Type == "RecordsQueried" && e.Tick < 1000);
    }

    [Fact]
    public void HistoryAndEvidenceBoundsEndInReviewWithoutTruncatingAttribution()
    {
        var w = Load(j => {
            var s = j["initialState"]!; s["information"]!["configuration"]!["queryTicks"] = 1;
            for (int id = 2; id <= 70; id++)
            {
                s["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = id, ["machineId"] = 1, ["kind"] = "VerifyMachine" });
                s["jobSystem"]!["assignments"]!.AsArray().Add(new JsonObject { ["key"] = "unrelated/" + id, ["jobId"] = id, ["actorId"] = 1, ["expires"] = 10000 });
            }
        }).CreateWorld(42); w.AdvanceTo(500);
        Assert.Equal("HistoryPageLimitExceeded", Audit(w).Reason); Assert.Equal(World.AuditPageLimit, Audit(w).Pages); Assert.Null(Audit(w).Finding);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());

        var crowded = Load(j => j["initialState"]!["qualityAssurance"]!["delayTicks"] = 160).CreateWorld(42);
        for (int tick = 8; tick < 128; tick += 3)
        {
            crowded.Submit(tick, new(new(1), ActionKind.Login, new(1, 0, 0), serviceId: 1));
            crowded.Submit(tick + 1, new(new(1), ActionKind.PostMessage, new(1, 0, 0), serviceId: 1,
                message: new MessageDraft(MessageKind.WorkCompleted, RecipientKind.Authority, default, new(SubjectKind.Machine, 1, AttributeKind.Operational), FactValue.Boolean(true), tick, 1, 1, operationKey: "repeat/" + tick)));
        }
        crowded.AdvanceTo(500); Assert.Equal("EvidenceCapacityExceeded", Audit(crowded).Reason); Assert.Equal(World.AuditEvidenceLimit, Audit(crowded).Evidence.Count); Assert.Null(Audit(crowded).Finding);
        Assert.Equal(crowded.Serialize(), World.Restore(crowded.Serialize()).Serialize());
    }

    [Fact]
    public void SnapshotRejectsChangedAuditOwnerDeskPhaseAndEvidenceAccount()
    {
        var w = Finished(); var a = Audit(w); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { writer.Write(a.VerificationAttemptId); writer.Write(a.ActorId); writer.Write(a.TerminalId); writer.Write((int)a.Status); writer.Write(a.Reason); }
        var pattern = buffer.ToArray(); int index = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var mutation in new[] { (Offset: index + 8, Value: 1L), (Offset: index + 16, Value: 1L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Value).CopyTo(bad, mutation.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
        var phase = bytes.ToArray(); BitConverter.GetBytes((int)AuditStatus.QueryingAttempts).CopyTo(phase, index + 24); Assert.Throws<ArgumentException>(() => World.Restore(phase));
        var row = a.Evidence[0]; using var rowBuffer = new MemoryStream(); using (var writer = new BinaryWriter(rowBuffer, System.Text.Encoding.UTF8, true))
        { writer.Write((int)row.Kind); writer.Write(row.EntityId); writer.Write(row.AccountId); writer.Write(row.Version); writer.Write(row.Tick); writer.Write(row.OperationKey); }
        var rowPattern = rowBuffer.ToArray(); int rowIndex = Assert.Single(Enumerable.Range(0, bytes.Length - rowPattern.Length + 1), i => bytes.AsSpan(i, rowPattern.Length).SequenceEqual(rowPattern));
        var forged = bytes.ToArray(); BitConverter.GetBytes(2L).CopyTo(forged, rowIndex + 12); Assert.Throws<ArgumentException>(() => World.Restore(forged));
    }

    [Fact]
    public void PolicyRequiresCompleteHistoryAndRefusesAmbiguousOrMissingEvidence()
    {
        var rows = Audit(Finished()).Evidence;
        AccountabilityFinding Assess(IEnumerable<RecordEntry> evidence, bool complete = true, string installed = "wrong") => AccountabilityPolicy.Assess(1, "correct", installed, 29, evidence, complete);
        Assert.Equal("IncompleteHistory", Assess(rows, false).Reason);
        Assert.Equal("NoAttributablePartMismatch", Assess(rows, installed: "").Reason);
        Assert.Equal("NoAttributablePartMismatch", Assess(rows, installed: "correct").Reason);
        Assert.Equal("MissingAssignment", Assess(rows.Where(r => r.Kind != RecordKind.WorkAttempt)).Reason);
        Assert.Equal("MissingSubmission", Assess(rows.Where(r => r.Kind != RecordKind.WorkReport)).Reason);
        var assignment = rows.Last(r => r.Kind == RecordKind.WorkAttempt);
        var duplicate = new RecordEntry(RecordKind.WorkAttempt, 99, 2, 1000, 10, "handoff", assignment.Fields.Select(f => f.Attribute == AttributeKind.AttemptId ? new RecordField(f.Attribute, FactValue.Integer(99)) : f));
        Assert.Equal("AmbiguousHandoff", Assess(rows.Append(duplicate)).Reason);
        var canceled = new RecordEntry(assignment.Kind, assignment.EntityId, assignment.AccountId, 1001, 20, "cancel", assignment.Fields.Select(f => f.Attribute == AttributeKind.OrderStatus ? new RecordField(f.Attribute, new(FactValueKind.Text, text: "Superseded")) : f));
        Assert.Equal("AssignmentAmended", Assess(rows.Append(canceled)).Reason);
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("account")]
    [InlineData("observation")]
    [InlineData("claim")]
    [InlineData("amendment")]
    public void PolicyRejectsUnprovenOrConflictingSubmission(string invalid)
    {
        var rows = Audit(Finished()).Evidence.ToArray(); int index = Array.FindIndex(rows, r => r.Kind == RecordKind.WorkReport && r.Fields.Any(f => f.Attribute == AttributeKind.ReportKind && f.Value.Text == "WorkCompleted"));
        var old = rows[index]; var fields = old.Fields.Select(f =>
            invalid == "terminal" && f.Attribute == AttributeKind.TerminalId ? new RecordField(f.Attribute, FactValue.Integer(0)) :
            invalid == "observation" && f.Attribute == AttributeKind.ObservedTick ? new RecordField(f.Attribute, FactValue.Integer(500)) :
            invalid == "claim" && f.Attribute == AttributeKind.ReportClaim ? new RecordField(f.Attribute, FactValue.Boolean(false)) :
            invalid == "amendment" && f.Attribute == AttributeKind.ReportKind ? new RecordField(f.Attribute, new(FactValueKind.Text, text: "WorkBlocked")) : f);
        rows[index] = new(old.Kind, old.EntityId, invalid == "account" ? 2 : old.AccountId, old.Version, old.Tick, old.OperationKey, fields);
        Assert.False(AccountabilityPolicy.Assess(1, "correct", "wrong", 29, rows, true).Attributable);
    }

    [Fact]
    public void AuditCheckpointContinuationIsExactAcrossInspectionTravelQueryPagesAndCompletion()
    {
        foreach (ulong seed in Enumerable.Range(1, 12).Select(i => (ulong)i)) foreach (int checkpoint in new[] { 0, 7, 28, 29, 30, 32, 33, 34, 35, 40, 45, 55, 65, 80, 200 })
        {
            var w = Load(j => { j["initialState"]!["information"]!["configuration"]!["queryTicks"] = (int)(seed % 5 + 1); j["initialState"]!["qualityAssurance"]!["delayTicks"] = (int)(seed % 6); }).CreateWorld(seed, true); w.AdvanceTo(checkpoint); var saved = w.Serialize(); var copy = World.Restore(saved, true);
            Assert.Equal(saved, copy.Serialize()); _ = w.InspectAudits(Supervisor); w.AdvanceTo(300); copy.AdvanceTo(300);
            Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > checkpoint).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void ChunkedAndConcurrentAuditsMatchDirectExecution()
    {
        string Run(int seed, bool chunked)
        {
            var w = Load(j => j["initialState"]!["information"]!["configuration"]!["queryTicks"] = seed % 5 + 1).CreateWorld((ulong)seed);
            if (chunked) for (int tick = 0; tick < 300; tick += 7) { w.AdvanceTo(tick); _ = w.InspectAudits(Supervisor); _ = w.ActorView(Supervisor); }
            w.AdvanceTo(300); return w.Digest();
        }
        var expected = Enumerable.Range(0, 16).Select(s => Run(s, false)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 })
        {
            var actual = new string[16]; Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, true)); Assert.Equal(expected, actual);
        }
    }
}
