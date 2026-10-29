using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class DetentionTests
{
    private static readonly Id<ActorTag> Supervisor = new(2);
    private static LoadedScenario Load(Action<JsonObject>? change = null, string scenario = "detention_authorization")
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", scenario + ".json")))!.AsObject(); change?.Invoke(json);
        if (change != null) json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "detention-test");
    }
    private static LocalDetentionRequestSnapshot Local(World w) => Assert.Single(w.InspectLocalDetentionRequests(Supervisor));
    private static DetentionAuthorizationSnapshot Authorization(World w) => Assert.Single(w.InspectDetentionAuthorizations());
    private static MessageDraft Request(DetentionRequestPayload p, string key = "manual-request", long observed = 177) => new(MessageKind.RequestDetention, RecipientKind.Authority, default,
        new(SubjectKind.Order, p.CaseId, AttributeKind.DetentionStatus), new(FactValueKind.Text, text: "RequestDetention"), observed, relatedMessageId: p.CaseReplyMessageId, operationKey: key, detentionRequest: p);
    private static void Post(World w, MessageDraft draft, long tick = 801, Id<ActorTag>? actor = null)
    {
        var who = actor ?? Supervisor; var position = who == Supervisor ? new Coord(0, 0, 0) : new Coord(1, 0, 0); long terminal = who == Supervisor ? 2 : 1;
        w.Submit(tick, new(who, ActionKind.Login, position, serviceId: terminal)); w.Submit(tick + 1, new(who, ActionKind.PostMessage, position, serviceId: terminal, message: draft)); w.AdvanceTo(tick + 4);
    }
    private static World Finished(Action<JsonObject>? change = null)
    { var w = Load(change).CreateWorld(42, true); w.AdvanceTo(734); return w; }

    [Fact]
    public void ReadCountRequestPostApprovalAndDecisionReadAreSeparateEvents()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(110); Assert.Equal(2, w.InspectReprimands().Count); Assert.Empty(w.InspectLocalDetentionRequests(Supervisor)); Assert.Empty(w.InspectDetentionAuthorizations());
        w.AdvanceTo(111); Assert.Equal(DetentionRequestStatus.Queued, Local(w).Status); Assert.Empty(w.InspectDetentionRequests());
        w.AdvanceTo(112); Assert.Equal(DetentionRequestStatus.Posted, Local(w).Status); Assert.Empty(w.InspectDetentionAuthorizations());
        w.AdvanceTo(113); var order = Authorization(w); Assert.Equal(DetentionAuthorizationStatus.Authorized, order.Status); Assert.Equal(new long[] { 1, 2 }, order.JustifyingCases); Assert.Equal(2113, order.Expires); Assert.Equal(1000, order.SentenceTicks);
        Assert.Equal(DetentionRequestStatus.Posted, Local(w).Status); Assert.Equal(0, Local(w).AuthorizationId);
        w.AdvanceTo(114); Assert.Equal(MessageStage.DeliveredToMailbox, w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.DetentionDecision).Stage); Assert.Equal(-1, Local(w).ReadTick);
        w.AdvanceTo(133); Assert.Equal(DetentionRequestStatus.Approved, Local(w).Status); Assert.Equal(1, Local(w).AuthorizationId); Assert.Equal(2113, Local(w).Expires);
        Assert.Equal(0, w.ActionsFailed); Assert.Single(w.InspectRecordHistory(), r => r.Kind == RecordKind.DetentionRequest); Assert.Single(w.InspectRecordHistory(), r => r.Kind == RecordKind.DetentionAuthorization);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
        var run = Runs.Execute(Load(), 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
    }

    [Fact]
    public void AuthorizationCannotFreezeOrMoveTheDistantTarget()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(113);
        Assert.Equal(new Coord(1, 0, 0), w.ActorView(new(1)).CurrentTile);
        w.Submit(114, new(new(1), ActionKind.Move, new(0, 0, 0))); w.AdvanceTo(115);
        Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(1)).CurrentTile); Assert.Equal(DetentionAuthorizationStatus.Authorized, Authorization(w).Status);
        Assert.DoesNotContain(w.Trace, e => e.Type is "Detained" or "CustodyStarted" or "EscortStarted");
    }

    [Fact]
    public void OneReprimandDoesNotCreateAnAutonomousRequestAndManualRequestFailsThreshold()
    {
        var w = Load(j => j["initialState"]!["detention"] = new JsonObject(), "personnel_case").CreateWorld(42); w.AdvanceTo(234);
        Assert.Empty(w.InspectLocalDetentionRequests(Supervisor)); Assert.Empty(w.InspectDetentionRequests());
        var reply = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.CaseDecision);
        Post(w, Request(new(1, reply.Draft.CaseDecision!.CaseId, reply.Id), observed: 78), 301);
        var decision = Assert.Single(w.InspectDetentionRequests()).Decision; Assert.Equal(DetentionDecisionState.Rejected, decision.State); Assert.Equal("ThresholdNotMet", decision.Reason); Assert.Empty(w.InspectDetentionAuthorizations());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void MissingApprovalPolicyLeavesAnExplainedWaitingRequest()
    {
        var w = Finished(j => j["initialState"]!.AsObject().Remove("detention"));
        Assert.Equal(DetentionRequestStatus.Waiting, Local(w).Status); Assert.Equal("DetentionPolicyUnavailable", Local(w).Reason); Assert.Empty(w.InspectDetentionAuthorizations());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void ThresholdIsRecomputedAtReceiptRatherThanCopiedFromAnOldReply()
    {
        var w = Finished(j => j["initialState"]!["personnel"]!["qualifyingWindow"] = 34);
        Assert.Contains(w.InspectCaseFilings(Supervisor), f => f.QualifyingCount == 2); Assert.Equal(DetentionRequestStatus.Rejected, Local(w).Status); Assert.Equal("ThresholdNotMet", Local(w).Reason);
        Assert.Equal(1, Assert.Single(w.InspectDetentionRequests()).Decision.QualifyingCount); Assert.Empty(w.InspectDetentionAuthorizations()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void DeliveredApprovalCannotTeachAnOfflineRequester()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(113, 2, false, false); w.AdvanceTo(234);
        Assert.Single(w.InspectDetentionAuthorizations()); Assert.Equal(DetentionRequestStatus.Posted, Local(w).Status); Assert.Equal(0, Local(w).AuthorizationId);
        var copy = World.Restore(w.Serialize()); w.ScheduleTerminalChange(235, 2, true, true); copy.ScheduleTerminalChange(235, 2, true, true); w.AdvanceTo(734); copy.AdvanceTo(734);
        Assert.Equal(DetentionRequestStatus.Approved, Local(w).Status); Assert.Equal(w.Serialize(), copy.Serialize());
    }

    [Fact]
    public void DuplicateAndFreshRequestsReuseOneUnextendedAuthorization()
    {
        var w = Finished(); var original = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft;
        Post(w, original); Assert.Equal("DuplicateRequest", w.InspectDetentionRequests().Last().Decision.Reason);
        Post(w, Request(original.DetentionRequest!, "another-operation"), 811); Assert.Equal("ExistingAuthorization", w.InspectDetentionRequests().Last().Decision.Reason);
        Post(w, Request(new(2, 2, original.DetentionRequest!.CaseReplyMessageId), original.OperationKey), 821); Assert.Equal("IdempotencyConflict", w.InspectDetentionRequests().Last().Decision.Reason);
        Assert.Equal(2113, Authorization(w).Expires); Assert.Equal(113, Authorization(w).IssuedTick); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Theory]
    [InlineData("role", "RequesterPermissionDenied")]
    [InlineData("case", "InvalidAcceptedCaseEvidence")]
    [InlineData("reply", "InvalidAcceptedCaseEvidence")]
    [InlineData("account", "InvalidAcceptedCaseEvidence")]
    public void InvalidPermissionOrAcceptedCaseReferenceCannotAuthorize(string kind, string reason)
    {
        var w = Finished(); var p = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft.DetentionRequest!;
        var payload = new DetentionRequestPayload(kind == "account" ? 2 : p.AccountId, kind == "case" ? 999 : p.CaseId, kind == "reply" ? 1 : p.CaseReplyMessageId);
        Post(w, Request(payload), actor: kind == "role" ? new Id<ActorTag>(1) : Supervisor);
        Assert.Equal(reason, w.InspectDetentionRequests().Last().Decision.Reason); Assert.Single(w.InspectDetentionAuthorizations()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void ExpiryClosesAuthorizationWithoutErasingHistoryOrCreatingCustody()
    {
        Assert.Equal(864000, new DetentionPolicy().SentenceTicks); Assert.Equal(1728000, new DetentionPolicy().OrderTicks);
        Assert.Throws<ArgumentException>(() => new DetentionPolicy(0)); Assert.Throws<ArgumentException>(() => new DetentionPolicy(orderTicks: 0));
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 50).CreateWorld(42, true);
        w.AdvanceTo(162); Assert.Equal(DetentionAuthorizationStatus.Authorized, Authorization(w).Status);
        var copy = World.Restore(w.Serialize()); w.AdvanceTo(163); copy.AdvanceTo(163); Assert.Equal(DetentionAuthorizationStatus.Expired, Authorization(w).Status); Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Equal(DetentionRequestStatus.Approved, Local(w).Status); Assert.Equal(163, Local(w).Expires);
        var original = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft;
        Post(w, original, 301); Assert.Equal("PriorRequestClosed", w.InspectDetentionRequests().Last().Decision.Reason); Assert.Single(w.InspectDetentionAuthorizations());
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void NewRequestAtExpiryCannotBeRemovedByTheOldAuthorizationTimer()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 50).CreateWorld(42); w.AdvanceTo(159);
        var p = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft.DetentionRequest!;
        Post(w, Request(p, "renew-after-expiry", observed: 111), 160);
        Assert.Equal(2, w.InspectDetentionAuthorizations().Count); Assert.Equal(DetentionAuthorizationStatus.Expired, w.InspectDetentionAuthorizations()[0].Status);
        Assert.Equal(DetentionAuthorizationStatus.Authorized, w.InspectDetentionAuthorizations()[1].Status); Assert.Equal(163, w.InspectDetentionAuthorizations()[1].IssuedTick);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void AmendmentLaterInTheSameTickCannotRewriteACompletedPersonnelDecision()
    {
        var w = Load(scenario: "personnel_case").CreateWorld(42); w.AdvanceTo(66); w.ScheduleWorkCancellation(67, 1, "after-acceptance-in-same-tick"); w.AdvanceTo(100);
        Assert.Single(w.InspectCases()); Assert.Equal(67, w.InspectCases()[0].AcceptedTick); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void SameTickCasesRetainTheirOwnCountCutoffs()
    {
        var w = Load(j => {
            var s = j["initialState"]!; var actor = s["actors"]![1]!.DeepClone(); actor["id"] = 3; actor["name"] = "Second supervisor"; s["actors"]!.AsArray().Add(actor);
            s["items"]!.AsArray().Add(new JsonObject { ["id"] = 10, ["resource"] = "card", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = 3 });
            s["information"]!["credentials"]!.AsArray().Add(new JsonObject { ["id"] = 3, ["holder"] = 3, ["account"] = 3, ["cardItem"] = 10 });
            s["information"]!["terminals"]![1]!["capacity"] = 2;
            foreach (var grant in s["information"]!["recordAccess"]!.AsArray().ToArray()) { var copy = grant!.DeepClone(); copy["actorId"] = 3; s["information"]!["recordAccess"]!.AsArray().Add(copy); }
            var roster = s["routines"]!["assignments"]![0]!.DeepClone(); roster["actorId"] = 3; s["routines"]!["assignments"]!.AsArray().Add(roster);
            var check = s["jobSystem"]!["checkIns"]![0]!.DeepClone(); check["actorId"] = 3; s["jobSystem"]!["checkIns"]!.AsArray().Add(check);
        }).CreateWorld(42); w.AdvanceTo(734);
        Assert.Equal(2, w.InspectCases().Count); Assert.Equal(w.InspectCases()[0].AcceptedTick, w.InspectCases()[1].AcceptedTick);
        var replies = w.InspectMessages().Where(m => m.Draft.Kind == MessageKind.CaseDecision).Select(m => m.Draft.CaseDecision!).ToArray();
        Assert.Equal(new[] { 1, 2 }, replies.Select(r => r.QualifyingCount)); Assert.Equal(new long[] { 1, 2 }, replies.Select(r => r.CountThroughCaseId));
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void SnapshotRejectsAlteredAuthorizationIdentityExpirySentenceOrCaseCutoff()
    {
        var w = Finished(); var o = Authorization(w); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { writer.Write(o.Id); writer.Write(o.AccountId); writer.Write(o.ActorId); writer.Write(o.RequestMessageId); writer.Write(o.CaseId); writer.Write(o.IssuedTick); writer.Write(o.Expires); writer.Write(o.SentenceTicks); writer.Write(o.CountThroughCaseId); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var change in new[] { (Offset: 8, Value: 2L), (Offset: 16, Value: 2L), (Offset: 48, Value: o.Expires + 1), (Offset: 56, Value: 0L), (Offset: 64, Value: 1L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(change.Value).CopyTo(bad, start + change.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }

    [Fact]
    public void CheckpointContinuationIncludesRequestDeliveryReplyReadAndExpiry()
    {
        foreach (int seed in Enumerable.Range(0, 10)) foreach (int tick in new[] { 0, 65, 102, 166, 110, 111, 112, 113, 114, 132, 133, 162, 163, 234 })
        {
            var w = Load(j => { j["initialState"]!["detention"]!["orderTicks"] = 50 + seed; j["initialState"]!["information"]!["configuration"]!["queryTicks"] = seed % 4 + 1; }).CreateWorld((ulong)seed, true);
            w.AdvanceTo(tick); var bytes = w.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
            w.AdvanceTo(734); copy.AdvanceTo(734); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void ExistingUnexpiredAuthorizationIsReusedWithoutRenewingExpiredCountEvidence()
    {
        var w = Finished(j => j["initialState"]!["personnel"]!["qualifyingWindow"] = 80);
        Assert.Equal(0, w.QualifyingReprimandCount(1, 800)); var first = Authorization(w);
        var p = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft.DetentionRequest!;
        Post(w, Request(p, "existing-authorization")); var decision = w.InspectDetentionRequests().Last().Decision;
        Assert.Equal("ExistingAuthorization", decision.Reason); Assert.Equal(0, decision.QualifyingCount); Assert.Equal(first.Id, decision.AuthorizationId); Assert.Equal(first.Expires, decision.Expires);
        Assert.Single(w.InspectDetentionAuthorizations()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }

    [Fact]
    public void ConcurrentAndChunkedAuthorityDecisionsAreDeterministic()
    {
        string Run(int seed, bool chunked)
        {
            var w = Load(j => { j["initialState"]!["detention"]!["orderTicks"] = 60 + seed; j["initialState"]!["information"]!["configuration"]!["queryTicks"] = seed % 4 + 1; }).CreateWorld((ulong)seed);
            if (chunked) for (int t = 0; t < 800; t += 13) { w.AdvanceTo(t); _ = w.InspectDetentionRequests(); _ = w.InspectDetentionAuthorizations(); _ = w.InspectLocalDetentionRequests(Supervisor); }
            w.AdvanceTo(800); return w.Digest();
        }
        var expected = Enumerable.Range(0, 12).Select(s => Run(s, false)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[12]; Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, true)); Assert.Equal(expected, actual); }
    }

    [Fact]
    public void ExpiryOverflowCannotPartiallyCreateAnAuthorization()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = long.MaxValue).CreateWorld(42); Assert.Throws<OverflowException>(() => w.AdvanceTo(734));
        Assert.NotNull(w.RuntimeError); Assert.Empty(w.InspectDetentionAuthorizations()); Assert.Empty(w.InspectDetentionRequests()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
}
