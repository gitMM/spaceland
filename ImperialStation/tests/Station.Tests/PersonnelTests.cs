using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class PersonnelTests
{
    private static readonly Id<ActorTag> Supervisor = new(2);
    private static readonly Coord Desk = new(0, 0, 0);
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "personnel_case.json")))!.AsObject(); change?.Invoke(json);
        if (change != null) json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "personnel-test");
    }
    private static CaseFilingSnapshot Filing(World w) => Assert.Single(w.InspectCaseFilings(Supervisor));
    private static World Finished(Action<JsonObject>? change = null)
    { var w = Load(change).CreateWorld(42, true); w.AdvanceTo(300); return w; }
    private static CaseSubmissionPayload Change(CaseSubmissionPayload p, long? account = null, long? inspection = null, IEnumerable<CaseEvidenceReference>? evidence = null, long? attemptVersion = null, string? policy = null) =>
        new(p.JobId, p.AttemptId, p.MachineId, account ?? p.AccountId, p.Violation, policy ?? p.Policy, inspection ?? p.InspectionReportId, attemptVersion ?? p.AttemptVersion, p.ReportVersion, evidence ?? p.Evidence);
    private static MessageDraft Draft(CaseSubmissionPayload p, string key = "extra", long observed = 65) => new(MessageKind.FileCase, RecipientKind.Authority, default,
        new(SubjectKind.Attempt, p.AttemptId, AttributeKind.CaseStatus), new(FactValueKind.Text, text: "FileCase"), observed, p.JobId, p.AttemptId, p.InspectionReportId, operationKey: key, caseSubmission: p);
    private static void Post(World w, MessageDraft draft, long tick = 301, Id<ActorTag>? actor = null)
    {
        var who = actor ?? Supervisor; var position = who == Supervisor ? Desk : new Coord(1, 0, 0); long terminal = who == Supervisor ? 2 : 1;
        w.Submit(tick, new(who, ActionKind.Login, position, serviceId: terminal)); w.Submit(tick + 1, new(who, ActionKind.PostMessage, position, serviceId: terminal, message: draft)); w.AdvanceTo(tick + 4);
    }
    private static CaseDecisionPayload LastDecision(World w) => w.InspectMessages().Last(m => m.Draft.Kind == MessageKind.CaseDecision).Draft.CaseDecision!;

    [Fact]
    public void FilingReceiptAcceptanceReplyDeliveryAndCitizenReadAreDistinct()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(64); Assert.Empty(w.InspectCaseFilings(Supervisor)); Assert.Empty(w.InspectCases());
        w.AdvanceTo(65); Assert.Equal(CaseFilingStatus.Queued, Filing(w).Status); Assert.Empty(w.InspectReprimands());
        w.AdvanceTo(66); Assert.Equal(CaseFilingStatus.Posted, Filing(w).Status); Assert.Empty(w.InspectCases());
        w.AdvanceTo(67); var c = Assert.Single(w.InspectCases()); Assert.Equal("1/1/1/WrongInstalledPart", c.Submission.CanonicalKey); Assert.Equal(67, Assert.Single(w.InspectReprimands()).AcceptedTick);
        Assert.Equal(CaseFilingStatus.Posted, Filing(w).Status); Assert.Equal(0, Filing(w).QualifyingCount);
        w.AdvanceTo(68); Assert.Equal(MessageStage.DeliveredToMailbox, w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.CaseDecision).Stage); Assert.Equal(-1, Filing(w).ReadTick);
        w.AdvanceTo(78); Assert.Equal(CaseFilingStatus.Accepted, Filing(w).Status); Assert.Equal(1, Filing(w).QualifyingCount); Assert.Equal(1, Filing(w).CaseId);
        Assert.Equal(10, w.InspectQueryDisclosures().Count); Assert.Single(w.InspectRecordHistory(), r => r.Kind == RecordKind.Case); Assert.Single(w.InspectRecordHistory(), r => r.Kind == RecordKind.Reprimand);
        Assert.Equal(0, w.ActionsFailed); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        var run = Runs.Execute(Load(), 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
    }

    [Fact]
    public void DeliveredDecisionCannotTeachAnOfflineSupervisor()
    {
        var w = Load().CreateWorld(42, true); w.ScheduleTerminalChange(67, 2, false, false); w.AdvanceTo(150);
        Assert.Single(w.InspectReprimands()); Assert.Equal(CaseFilingStatus.Posted, Filing(w).Status); Assert.Equal(0, Filing(w).CaseId); Assert.Equal(0, Filing(w).QualifyingCount);
        var copy = World.Restore(w.Serialize()); w.ScheduleTerminalChange(151, 2, true, true); copy.ScheduleTerminalChange(151, 2, true, true); w.AdvanceTo(500); copy.AdvanceTo(500);
        Assert.Equal(CaseFilingStatus.Accepted, Filing(w).Status); Assert.Equal(w.Serialize(), copy.Serialize());
    }

    [Fact]
    public void CanonicalKeyAndOperationKeyPreventDuplicateReprimands()
    {
        var w = Finished(); var c = Assert.Single(w.InspectCases()); var original = w.InspectMessages().Single(m => m.Id == c.FilingMessageId).Draft;
        Post(w, original); Assert.Equal("AlreadyAccepted", LastDecision(w).Reason);
        Post(w, Draft(c.Submission, "new-message"), 311); Assert.Equal("AlreadyAccepted", LastDecision(w).Reason);
        Post(w, Draft(Change(c.Submission, account: 2), original.OperationKey), 321); Assert.Equal("IdempotencyConflict", LastDecision(w).Reason);
        Assert.Single(w.InspectCases()); Assert.Single(w.InspectReprimands()); Assert.Equal(67, w.InspectReprimands()[0].AcceptedTick);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Theory]
    [InlineData("query", "InvalidQueryProvenance")]
    [InlineData("omitted", "IncompleteCaseHistory")]
    [InlineData("inspection", "InvalidInspectionProvenance")]
    [InlineData("version", "InvalidQueryProvenance")]
    [InlineData("policy", "UnsupportedAccountabilityPolicy")]
    [InlineData("account", "AccountabilityPolicyRejected:WrongInstalledPart")]
    public void PersonnelRejectsForgedIncompleteAndMismatchedEvidence(string mutation, string reason)
    {
        var w = Finished(); var p = w.InspectCases()[0].Submission;
        var changed = mutation switch {
            "query" => Change(p, evidence: p.Evidence.Select(e => new CaseEvidenceReference(e.RecordVersion, e.QueryActionId + 1000))),
            "omitted" => Change(p, evidence: p.Evidence.Skip(1)), "inspection" => Change(p, inspection: 1), "version" => Change(p, attemptVersion: p.AttemptVersion + 1000),
            "policy" => Change(p, policy: "unapproved"), _ => Change(p, account: 2) };
        Post(w, Draft(changed)); Assert.Equal(reason, LastDecision(w).Reason); Assert.False(LastDecision(w).Accepted); Assert.Single(w.InspectReprimands());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void FilingRequiresAuthenticatedSupervisorPermission()
    {
        var denied = Finished(j => j["initialState"]!["personnel"]!["supervisorRole"] = "guard");
        Assert.Empty(denied.InspectCases()); Assert.Empty(denied.InspectReprimands()); Assert.Equal(CaseFilingStatus.NeedsReview, Filing(denied).Status); Assert.Equal("RequesterPermissionDenied", Filing(denied).Reason);
        Assert.Equal(denied.Serialize(), World.Restore(denied.Serialize()).Serialize());
        var w = Finished(); Post(w, Draft(w.InspectCases()[0].Submission), actor: new(1)); Assert.Equal("RequesterPermissionDenied", LastDecision(w).Reason); Assert.Single(w.InspectReprimands());
    }

    [Fact]
    public void CaseAcceptanceUsesReportedEvidenceRatherThanCurrentPhysicalMachine()
    {
        var repaired = Load(j => j["initialState"]!["items"]!.AsArray().Add(new JsonObject { ["id"] = 9, ["resource"] = "correct", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = 1 })).CreateWorld(42);
        repaired.Submit(64, new(new(1), ActionKind.Install, new(1, 0, 0), new(9), serviceId: 1)); repaired.AdvanceTo(300);
        Assert.True(repaired.InspectMachines()[0].Operational); Assert.Single(repaired.InspectReprimands()); Assert.Equal(CaseFilingStatus.Accepted, Filing(repaired).Status);
        var failed = Load().CreateWorld(42); failed.ScheduleMachineFault(66, 1); failed.AdvanceTo(300);
        Assert.Equal(0, failed.InspectMachines()[0].InstalledItemId); Assert.Single(failed.InspectReprimands()); Assert.Equal(repaired.InspectReprimands()[0].AcceptedTick, failed.InspectReprimands()[0].AcceptedTick);
    }

    [Fact]
    public void AdministrativeAmendmentBetweenQueryAndFilingRequiresReview()
    {
        var w = Load().CreateWorld(42, true); w.ScheduleWorkCancellation(66, 1, "late-cancel"); w.AdvanceTo(300);
        Assert.Equal("StaleWorkRecords", LastDecision(w).Reason); Assert.Empty(w.InspectCases()); Assert.Empty(w.InspectReprimands()); Assert.Equal(CaseFilingStatus.NeedsReview, Filing(w).Status);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void QualifyingWindowHasExplicitInclusiveBoundaryAndPreservesHistory()
    {
        Assert.Equal(6048000, new PersonnelConfig().QualifyingWindow); Assert.Equal(2, new PersonnelConfig().DetentionThreshold);
        Assert.Throws<ArgumentException>(() => new PersonnelConfig(qualifyingWindow: 0)); Assert.Throws<ArgumentException>(() => new PersonnelConfig(detentionThreshold: 0));
        var w = Finished(j => j["initialState"]!["personnel"]!["qualifyingWindow"] = 20);
        Assert.Equal(0, w.QualifyingReprimandCount(1, 66)); Assert.Equal(1, w.QualifyingReprimandCount(1, 67)); Assert.Equal(1, w.QualifyingReprimandCount(1, 87)); Assert.Equal(0, w.QualifyingReprimandCount(1, 88));
        Assert.Single(w.InspectReprimands()); Assert.Equal(1, Filing(w).QualifyingCount); // The read reply carries its receipt-time count.
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void DistinctSlotsProduceDistinctCasesAndAReadCountOfTwo()
    {
        var w = Load(j => {
            var s = j["initialState"]!;
            var machine = s["work"]!["machines"]![0]!.DeepClone(); machine["id"] = 2; s["work"]!["machines"]!.AsArray().Add(machine);
            s["items"]!.AsArray().Add(new JsonObject { ["id"] = 9, ["resource"] = "wrong", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = 1 });
            s["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 2, ["machineId"] = 2 });
            s["jobSystem"]!["assignments"]!.AsArray().Add(new JsonObject { ["key"] = "repair-second", ["jobId"] = 2, ["actorId"] = 1, ["expires"] = 1000 });
            var commands = j["scriptedActors"]![0]!["commands"]!.AsArray();
            var login = commands[0]!.DeepClone(); login["tick"] = 7; commands.Add(login);
            var install = commands[1]!.DeepClone(); install["tick"] = 8; install["itemId"] = 9; install["serviceId"] = 2; commands.Add(install);
            var ack = commands[2]!.DeepClone(); ack["tick"] = 11; ack["message"]!["observedTick"] = 11; ack["message"]!["taskId"] = 2; ack["message"]!["attemptId"] = 2;
            ack["message"]!["relatedMessageId"] = 2; ack["message"]!["subject"]!["subjectId"] = 2; ack["message"]!["operationKey"] = "accept-second"; commands.Add(ack);
            var completed = commands[3]!.DeepClone(); completed["tick"] = 12; completed["message"]!["observedTick"] = 12; completed["message"]!["taskId"] = 2; completed["message"]!["attemptId"] = 2;
            completed["message"]!["subject"]!["subjectId"] = 2; completed["message"]!["operationKey"] = "complete-second"; commands.Add(completed);
        }).CreateWorld(42, true);
        w.AdvanceTo(800); Assert.Equal(2, w.InspectCases().Count); Assert.Equal(2, w.InspectReprimands().Count);
        Assert.Equal(new long[] { 1, 2 }, w.InspectCases().Select(c => c.Submission.MachineId).OrderBy(i => i));
        Assert.All(w.InspectReprimands(), p => Assert.Equal(1, p.AccountId)); Assert.Contains(w.InspectCaseFilings(Supervisor), f => f.Status == CaseFilingStatus.Accepted && f.QualifyingCount == 2);
        Assert.Equal(2, w.QualifyingReprimandCount(1, 800)); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void FreshVerificationReportIdReusesTheSameCanonicalCase()
    {
        var w = Finished(); var c = w.InspectCases()[0]; var source = w.InspectMessages().Single(m => m.Id == c.Submission.InspectionReportId).Draft;
        var repeated = new MessageDraft(source.Kind, source.RecipientKind, source.Recipient, source.Subject, source.Claim, source.ObservedTick, source.TaskId, source.AttemptId, source.RelatedMessageId,
            operationKey: "fresh-verification-report", inspectionReport: source.InspectionReport);
        Post(w, repeated); var received = w.InspectMessages().Single(m => m.Draft.OperationKey == "fresh-verification-report");
        Assert.Contains(w.InspectRecordHistory(), r => r.Kind == RecordKind.WorkReport && r.EntityId == received.Id);
        Post(w, Draft(Change(c.Submission, inspection: received.Id), "reinspection-filing"), 311);
        Assert.Equal("AlreadyAccepted", LastDecision(w).Reason); Assert.Equal(c.Id, LastDecision(w).CaseId); Assert.Single(w.InspectReprimands());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void InformalAccusationAndForgedAuthorityReplyCannotCreatePersonnelEffects()
    {
        var w = Finished(); Post(w, new MessageDraft(MessageKind.Observation, RecipientKind.Authority, default, new(SubjectKind.Actor, 1, AttributeKind.CaseStatus), new(FactValueKind.Text, text: "guilty"), 300));
        Assert.Single(w.InspectReprimands());
        var reply = new MessageDraft(MessageKind.CaseDecision, RecipientKind.Account, new(2), new(SubjectKind.Attempt, 1, AttributeKind.CaseStatus), new(FactValueKind.Text, text: "Accepted"), 300,
            relatedMessageId: 7, caseDecision: new CaseDecisionPayload(7, 99, 1, 99, "Accepted"));
        Post(w, reply, 311); Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.CaseDecision?.CaseId == 99); Assert.Single(w.InspectReprimands());
    }

    [Fact]
    public void SnapshotRejectsAlteredCaseIdentityReprimandAndQueryReceipt()
    {
        var w = Finished(); var bytes = w.Serialize(); var c = w.InspectCases()[0]; var d = w.InspectQueryDisclosures()[0];
        static byte[] Pattern(Action<BinaryWriter> write) { using var b = new MemoryStream(); using (var writer = new BinaryWriter(b, System.Text.Encoding.UTF8, true)) write(writer); return b.ToArray(); }
        int Find(byte[] pattern) => Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        int caseIndex = Find(Pattern(writer => { writer.Write(c.Id); writer.Write(c.FilingMessageId); writer.Write(c.AcceptedTick); writer.Write(true); writer.Write(c.Submission.JobId); writer.Write(c.Submission.AttemptId); writer.Write(c.Submission.MachineId); }));
        int disclosureIndex = Find(Pattern(writer => { writer.Write(d.ActionId); writer.Write(d.ActorId); writer.Write(d.AccountId); writer.Write(d.TerminalId); writer.Write(d.RecordVersion); writer.Write(d.CapturedVersion); writer.Write(d.CompletedTick); }));
        var p = w.InspectReprimands()[0]; int reprimandIndex = Find(Pattern(writer => { writer.Write(p.CaseId); writer.Write(p.AccountId); writer.Write(p.AcceptedTick); writer.Write(p.Policy); }));
        foreach (var mutation in new[] { (Offset: caseIndex, Value: 2L), (Offset: caseIndex + 16, Value: 68L), (Offset: disclosureIndex + 8, Value: 1L), (Offset: reprimandIndex + 8, Value: 2L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Value).CopyTo(bad, mutation.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }

    [Fact]
    public void CheckpointContinuationCoversQueriesPostReceiptAndReplyRead()
    {
        foreach (int seed in Enumerable.Range(0, 10)) foreach (int tick in new[] { 0, 29, 33, 37, 52, 55, 64, 65, 66, 67, 68, 77, 78, 100 })
        {
            var loaded = Load(j => j["initialState"]!["information"]!["configuration"]!["queryTicks"] = seed % 4 + 1);
            var w = loaded.CreateWorld((ulong)seed, true); w.AdvanceTo(tick); var bytes = w.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
            w.AdvanceTo(300); copy.AdvanceTo(300); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void LaterAmendmentsDoNotEraseAnAlreadyAcceptedHistoricalCase()
    {
        var w = Finished(); w.ScheduleWorkCancellation(301, 1, "later-amendment"); w.AdvanceTo(302);
        Assert.Single(w.InspectCases()); Assert.Single(w.InspectReprimands()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void ConcurrentAndChunkedPersonnelServicesRemainIndependent()
    {
        string Run(int seed, bool chunks)
        {
            var w = Load(j => { j["initialState"]!["information"]!["configuration"]!["queryTicks"] = seed % 5 + 1; j["initialState"]!["personnel"]!["qualifyingWindow"] = seed % 2 == 0 ? 20 : 6048000; }).CreateWorld((ulong)seed);
            if (chunks) for (int t = 0; t < 300; t += 11) { w.AdvanceTo(t); _ = w.InspectCases(); _ = w.InspectCaseFilings(Supervisor); _ = w.QualifyingReprimandCount(1, t); }
            w.AdvanceTo(300); return w.Digest();
        }
        var expected = Enumerable.Range(0, 12).Select(s => Run(s, false)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[12]; Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, true)); Assert.Equal(expected, actual); }
    }
}
