using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class EscortDispatchTests
{
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "escort_order_read.json")))!.AsObject(); change?.Invoke(json);
        if (change != null) json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "escort-test");
    }
    private static EscortDispatchSnapshot Dispatch(World w) => Assert.Single(w.InspectEscortDispatches());
    private static LocalGuardOrderSnapshot Local(World w, long actor) => Assert.Single(w.InspectLocalGuardOrders(new(actor)));
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    private static MessageDraft Ack(long order, string response = "Accepted", long observed = 114) => new(MessageKind.EscortAcknowledged, RecipientKind.Authority, default,
        new(SubjectKind.Order, order, AttributeKind.DetentionStatus), new(FactValueKind.Text, text: response), observed, relatedMessageId: order, operationKey: "test-ack/" + order);

    [Fact]
    public void DispatchDeliveryIndividualReadsPostsAndReceiptsAreSeparate()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(112); Assert.Empty(w.InspectEscortDispatches());
        w.AdvanceTo(113); var d = Dispatch(w); Assert.Equal(EscortDispatchStatus.AwaitingReaders, d.Status); Assert.Equal(new long[] { 3, 4 }, d.Payload!.Guards); Assert.Equal(3, d.Payload.Leader);
        Assert.Equal(new long[] { 1, 2 }, d.Payload.JustifyingCases); Assert.Equal(1, d.Payload.SubjectAccountId); Assert.Equal(1, d.Payload.SubjectActorId);
        Assert.Equal(2113, d.Payload.Expires); Assert.Equal(1000, d.Payload.SentenceTicks); Assert.Equal(new Coord(2, 0, 0), d.Payload.Dispatch.Destination);
        Assert.Equal(new[] { new Coord(1, 0, 0) }, d.Payload.Candidates); Assert.Equal(2, d.OrderMessageIds.Count); RoundTrip(w);
        w.AdvanceTo(114); Assert.All(d.OrderMessageIds, id => Assert.Equal(MessageStage.DeliveredToMailbox, w.InspectMessages().Single(m => m.Id == id).Stage));
        Assert.Empty(w.InspectLocalGuardOrders(new(3))); Assert.Empty(w.InspectLocalGuardOrders(new(4))); RoundTrip(w);
        w.AdvanceTo(134); Assert.Equal(GuardOrderStatus.AcknowledgmentPending, Local(w, 3).Status); Assert.Empty(w.InspectLocalGuardOrders(new(4)));
        Assert.All(w.InspectGuardAssignments(), a => Assert.Equal(0, a.AcknowledgmentMessageId)); RoundTrip(w);
        w.AdvanceTo(135); Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w, 3).Status); Assert.All(w.InspectGuardAssignments(), a => Assert.Equal(0, a.AcknowledgmentMessageId));
        w.AdvanceTo(136); Assert.Single(w.InspectGuardAssignments(), a => a.AcknowledgmentMessageId > 0); Assert.Equal(EscortDispatchStatus.AwaitingReaders, Dispatch(w).Status);
        w.AdvanceTo(138); Assert.Empty(w.InspectLocalGuardOrders(new(4))); Assert.Equal("EquipmentUnavailable", Assert.Single(w.InspectGuardOutcomes(true)).Outcome); w.AdvanceTo(139); Assert.Equal(GuardOrderStatus.AcknowledgmentPending, Local(w, 4).Status); RoundTrip(w);
        w.AdvanceTo(141); Assert.Equal(EscortDispatchStatus.NeedsAttention, Dispatch(w).Status); Assert.All(w.InspectGuardAssignments(), a => Assert.Equal("Accepted", a.Response)); RoundTrip(w);
        var run = Runs.Execute(Load(), 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(0, run.World.ActionsFailed);
    }

    [Fact]
    public void CancelledWorkDoesNotRemainACurrentSubjectSearchDestination()
    {
        var w = Load(j =>
        {
            var s = j["initialState"]!;
            var machine = s["work"]!["machines"]![0]!.DeepClone();
            machine["id"] = 3; machine["position"]!["x"] = 2;
            s["work"]!["machines"]!.AsArray().Add(machine);
            s["jobSystem"]!["jobs"]!.AsArray().Add(new JsonObject { ["id"] = 3, ["machineId"] = 3 });
            s["jobSystem"]!["assignments"]!.AsArray().Add(new JsonObject { ["key"] = "obsolete-work", ["jobId"] = 3, ["actorId"] = 1, ["expires"] = 10000 });
        }).CreateWorld(42, true);
        w.ScheduleWorkCancellation(20, 3, "cancelled-site");
        w.AdvanceTo(113);
        Assert.Equal(AssignmentStatus.Cancelled, Assert.Single(w.InspectWorkAttempts(), a => a.JobId == 3).Assignment);
        Assert.DoesNotContain(new Coord(2, 0, 0), Dispatch(w).Payload!.Candidates);
        Assert.Contains(new Coord(1, 0, 0), Dispatch(w).Payload!.Candidates);
        RoundTrip(w);
    }

    [Fact]
    public void SelectedTeamDoesNotFreezeOrMoveTheTargetOrInventEquipment()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(139);
        w.Submit(140, new(new(1), ActionKind.Move, new(0, 0, 0))); w.AdvanceTo(141);
        Assert.Equal(new Coord(0, 0, 0), w.ActorView(new(1)).CurrentTile); Assert.Equal(EscortDispatchStatus.NeedsAttention, Dispatch(w).Status);
        Assert.Equal("GuardReported/EquipmentUnavailable", Dispatch(w).Reason); Assert.Empty(w.InspectResourceTransactions()); RoundTrip(w);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("roster")]
    [InlineData("offDuty")]
    public void UnavailableGuardLeavesWholeTeamPendingWithoutPartialReservation(string missing)
    {
        var w = Load(j => {
            if (missing == "role") j["initialState"]!["actors"]![3]!["role"] = "visitor";
            else if (missing == "roster") j["initialState"]!["routines"]!["assignments"]!.AsArray().RemoveAt(2);
            else j["initialState"]!["routines"]!["assignments"]![2]!["cohort"] = 1;
        }).CreateWorld(42); w.AdvanceTo(734);
        Assert.Equal(EscortDispatchStatus.AwaitingGuards, Dispatch(w).Status); Assert.Equal("InsufficientKnownOnDutyUncommittedGuards", Dispatch(w).Reason);
        Assert.Null(Dispatch(w).Payload); Assert.Empty(w.InspectGuardAssignments()); Assert.Empty(w.InspectLocalGuardOrders(new(3))); RoundTrip(w);
    }

    [Fact]
    public void PublishedRosterChangeWakesPendingDispatchWithoutPrivateDutyInspection()
    {
        var w = Load(j => j["initialState"]!["routines"]!["assignments"]![2]!["cohort"] = 1).CreateWorld(42); w.AdvanceTo(234);
        Assert.Equal(EscortDispatchStatus.AwaitingGuards, Dispatch(w).Status);
        w.ScheduleRosterChange(235, new(new(4), 0, new Coord(0, 0, 0)), "guard-on-duty"); w.AdvanceTo(235);
        Assert.Equal(EscortDispatchStatus.AwaitingReaders, Dispatch(w).Status); Assert.Empty(w.InspectLocalGuardOrders(new(4))); RoundTrip(w);
        w.AdvanceTo(734); Assert.Equal(EscortDispatchStatus.NeedsAttention, Dispatch(w).Status); Assert.Equal("EquipmentUnavailable", Assert.Single(w.InspectGuardOutcomes(true)).Outcome); Assert.Empty(w.InspectLocalGuardOrders(new(4))); RoundTrip(w);
        w.AdvanceTo(1034); Assert.Equal(EscortDispatchStatus.NeedsAttention, Dispatch(w).Status); RoundTrip(w);
    }

    [Fact]
    public void OfflineReadersCannotBePulledIntoTheTeam()
    {
        var w = Load().CreateWorld(42); w.ScheduleTerminalChange(113, 3, false, false); w.ScheduleTerminalChange(113, 4, false, false); w.AdvanceTo(234);
        Assert.Equal(EscortDispatchStatus.AwaitingReaders, Dispatch(w).Status); Assert.Empty(w.InspectLocalGuardOrders(new(3))); Assert.Empty(w.InspectLocalGuardOrders(new(4)));
        Assert.All(w.InspectGuardAssignments(), a => Assert.Equal(0, a.AcknowledgmentMessageId)); var copy = World.Restore(w.Serialize());
        foreach (var world in new[] { w, copy }) { world.ScheduleTerminalChange(235, 3, true, true); world.ScheduleTerminalChange(235, 4, true, true); world.AdvanceTo(734); }
        Assert.Equal(EscortDispatchStatus.NeedsAttention, Dispatch(w).Status); Assert.Equal(w.Serialize(), copy.Serialize());
    }

    [Fact]
    public void SilentTeamIsReassignedAfterTheAcknowledgmentDeadlineAndLateRepliesChangeNothing()
    {
        // Two more on-duty guards with their own terminal. Every guard terminal is offline
        // from dispatch at tick 113 until 380, so the first team cannot reply before its
        // deadline (363); the replacement can reply before its own (613).
        var w = Load(j =>
        {
            var s = j["initialState"]!; s["horizon"] = 2000; var info = s["information"]!;
            info["terminals"]!.AsArray().Add(new JsonObject { ["id"] = 5, ["position"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }, ["permittedRoles"] = new JsonArray("guard"), ["capacity"] = 1, ["capabilities"] = "Inbox, Post" });
            foreach (int g in new[] { 5, 6 })
            {
                s["actors"]!.AsArray().Add(new JsonObject { ["id"] = g, ["name"] = "Guard " + g, ["role"] = "guard", ["position"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 } });
                s["items"]!.AsArray().Add(new JsonObject { ["id"] = 10 + g, ["resource"] = "card", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = g });
                info["credentials"]!.AsArray().Add(new JsonObject { ["id"] = g, ["holder"] = g, ["account"] = g, ["cardItem"] = 10 + g });
                s["routines"]!["assignments"]!.AsArray().Add(new JsonObject { ["actorId"] = g, ["cohort"] = 0, ["restTile"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 } });
            }
        }).CreateWorld(42, true);
        foreach (long terminal in new long[] { 3, 4, 5 }) { w.ScheduleTerminalChange(113, terminal, false, false); w.ScheduleTerminalChange(380, terminal, true, true); }
        w.AdvanceTo(113); var first = Dispatch(w); var silent = first.Payload!.Guards.ToArray(); Assert.Equal(113 + 250, first.AcknowledgmentDeadline);
        w.AdvanceTo(362); Assert.Equal(EscortDispatchStatus.AwaitingReaders, Dispatch(w).Status);
        w.AdvanceTo(364);
        Assert.Single(w.Trace, e => e.Type == "EscortAcknowledgmentOverdue");
        var replacement = Dispatch(w); Assert.Equal(1, replacement.Reassignments);
        Assert.Equal(EscortDispatchStatus.AwaitingReaders, replacement.Status);
        Assert.Empty(replacement.Payload!.Guards.Intersect(silent));
        Assert.All(w.InspectGuardAssignments().Where(a => silent.Contains(a.ActorId)), a => Assert.Equal("Unacknowledged", a.Response));
        RoundTrip(w);
        var copy = World.Restore(w.Serialize());
        foreach (var world in new[] { w, copy }) world.AdvanceTo(950);
        Assert.Equal(w.Serialize(), copy.Serialize());
        // The silent guards learn of the reassignment by reading it; nothing they post
        // afterwards restores their commitment or joins them to the current team.
        Assert.All(silent, g => Assert.All(w.InspectLocalGuardOrders(new(g)), l => Assert.Equal("KnownOrderReassigned", l.Reason)));
        Assert.All(w.InspectGuardAssignments().Where(a => silent.Contains(a.ActorId)), a => Assert.Equal("Unacknowledged", a.Response));
        Assert.Equal(replacement.OrderMessageIds, Dispatch(w).OrderMessageIds);
        Assert.All(Dispatch(w).OrderMessageIds, id => Assert.True(w.InspectGuardAssignments().Single(a => a.OrderMessageId == id).AcknowledgmentMessageId > 0));
        Assert.Single(w.Trace, e => e.Type == "EscortAcknowledgmentOverdue");
        // Both silent guards read their order and the reassignment together; their replies
        // arrive late and are recorded without restoring a commitment.
        Assert.Equal(2, w.Trace.Count(e => e.Type == "GuardAcknowledgmentLate"));
    }

    [Fact]
    public void OrderReadAfterExpiryOnlyReportsExpiryAndCannotBecomeReady()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 10).CreateWorld(42); w.AdvanceTo(734);
        Assert.Equal(EscortDispatchStatus.Expired, Dispatch(w).Status); Assert.Equal(GuardOrderStatus.Expired, Local(w, 3).Status); Assert.Equal("KnownOrderExpired", Local(w, 3).Response);
        Assert.All(w.InspectGuardAssignments(), a => Assert.Equal(0, a.AcknowledgmentMessageId));
        foreach (long guard in new long[] { 3, 4 }) Assert.DoesNotContain(w.InspectKnowledge(new(guard)), f => f.Key.Attribute == AttributeKind.DetentionStatus && f.Pinned);
        RoundTrip(w);
    }

    [Fact]
    public void KnownExpiryIsAppliedLocallyWithoutTerminalConnectivity()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 50).CreateWorld(42); w.AdvanceTo(139);
        w.ScheduleTerminalChange(140, 3, false, false); w.ScheduleTerminalChange(140, 4, false, false); w.AdvanceTo(163);
        Assert.Equal(EscortDispatchStatus.Expired, Dispatch(w).Status); Assert.Equal(GuardOrderStatus.Expired, Local(w, 3).Status); Assert.Equal(GuardOrderStatus.Expired, Local(w, 4).Status); RoundTrip(w);
    }

    [Fact]
    public void DuplicateRequestsCannotCreateAnotherTeamOrExtendExpiry()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(734); var original = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft;
        w.Submit(735, new(new(2), ActionKind.Login, new(0, 0, 0), serviceId: 2)); w.Submit(736, new(new(2), ActionKind.PostMessage, new(0, 0, 0), serviceId: 2, message: original)); w.AdvanceTo(739);
        Assert.Equal("DuplicateRequest", w.InspectDetentionRequests().Last().Decision.Reason); Assert.Single(w.InspectEscortDispatches()); Assert.Equal(2, w.InspectGuardAssignments().Count); Assert.Equal(2113, Dispatch(w).Payload!.Expires); RoundTrip(w);
    }

    [Fact]
    public void AssignmentSelectionUsesKnownOriginsAndCommitmentsWithDeterministicTies()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(100); var view = w.AuthorityView(); var p = w.Detention!.Escort!;
        var guards = view.Personnel.Where(x => x.Role == "guard").ToArray();
        var candidates = new[] { new DispatchCandidate(guards[1], new Coord(0, 0, 0), false), new DispatchCandidate(guards[0], new Coord(0, 0, 0), false) };
        Assert.Equal(new long[] { 3, 4 }, AuthorityDispatch.EscortTeam(view, 1, p, candidates));
        Assert.Empty(AuthorityDispatch.EscortTeam(view, 1, p, new[] { candidates[0], new DispatchCandidate(guards[0], new Coord(0, 0, 0), true) }));
        Assert.Empty(AuthorityDispatch.EscortTeam(view, 1, p, new[] { candidates[0], new DispatchCandidate(guards[0], null, false) }));
        Assert.Empty(AuthorityDispatch.EscortTeam(view, 3, p, candidates));
        w.AdvanceTo(113); var snapshot = Dispatch(w); ((long[])snapshot.OrderMessageIds)[0] = 999;
        Assert.DoesNotContain(999L, Dispatch(w).OrderMessageIds); RoundTrip(w);
    }

    [Fact]
    public void AnotherAccountCannotAcknowledgeAGuardsOrder()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); long id = Dispatch(w).OrderMessageIds[0];
        w.Submit(115, new(new(1), ActionKind.Login, new(1, 0, 0), serviceId: 1)); w.Submit(116, new(new(1), ActionKind.PostMessage, new(1, 0, 0), serviceId: 1, message: Ack(id))); w.AdvanceTo(119);
        Assert.All(w.InspectGuardAssignments(), a => Assert.Equal(0, a.AcknowledgmentMessageId)); Assert.Empty(w.InspectLocalGuardOrders(new(3))); RoundTrip(w);
    }

    [Fact]
    public void AuthenticatedAcknowledgmentIsAClaimAndDoesNotForgeAPrivateOrderCopy()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); long id = Dispatch(w).OrderMessageIds[0];
        w.Submit(115, new(new(3), ActionKind.Login, new(0, 0, 0), serviceId: 3)); w.Submit(116, new(new(3), ActionKind.PostMessage, new(0, 0, 0), serviceId: 3, message: Ack(id))); w.AdvanceTo(119);
        Assert.Single(w.InspectGuardAssignments(), a => a.AcknowledgmentMessageId > 0); Assert.Empty(w.InspectLocalGuardOrders(new(3))); RoundTrip(w);
    }

    [Fact]
    public void SnapshotContinuationCoversDispatchReadsTransportAndExpiry()
    {
        foreach (int seed in Enumerable.Range(0, 8)) foreach (int tick in new[] { 0, 112, 113, 114, 133, 134, 135, 136, 137, 138, 139, 162, 163, 184 })
        {
            var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 50 + seed).CreateWorld((ulong)seed, true); w.AdvanceTo(tick);
            var bytes = w.Serialize(); var copy = World.Restore(bytes, true); Assert.Equal(bytes, copy.Serialize());
            w.AdvanceTo(734); copy.AdvanceTo(734); Assert.Equal(w.Serialize(), copy.Serialize()); Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }

    [Fact]
    public void ExpiryClosesAnUnstaffedDispatchAndRemovesItsReviewTimer()
    {
        var w = Load(j => { j["initialState"]!["detention"]!["orderTicks"] = 50; j["initialState"]!["routines"]!["assignments"]!.AsArray().RemoveAt(2); }).CreateWorld(42);
        w.AdvanceTo(162); Assert.Equal(EscortDispatchStatus.AwaitingGuards, Dispatch(w).Status); RoundTrip(w);
        w.AdvanceTo(163); Assert.Equal(EscortDispatchStatus.Expired, Dispatch(w).Status); Assert.Empty(w.InspectGuardAssignments()); RoundTrip(w);
    }

    [Fact]
    public void AcknowledgmentAtTheExpiryTickCannotAdvanceTheTeam()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 28).CreateWorld(42); w.AdvanceTo(141);
        Assert.Equal(EscortDispatchStatus.Expired, Dispatch(w).Status); Assert.Single(w.InspectGuardAssignments(), a => a.AcknowledgmentMessageId > 0);
        Assert.Equal(GuardOrderStatus.Expired, Local(w, 4).Status); Assert.True(Local(w, 4).AcknowledgmentMessageId > 0); RoundTrip(w);
    }

    [Fact]
    public void DuplicateAcknowledgmentsCannotAddHistoryOrChangeTheFirstReceipt()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(139); long ack = Local(w, 3).AcknowledgmentMessageId; var draft = w.InspectMessages().Single(m => m.Id == ack).Draft;
        w.Submit(140, new(new(3), ActionKind.Login, new(0, 0, 0), serviceId: 3)); w.Submit(141, new(new(3), ActionKind.PostMessage, new(0, 0, 0), serviceId: 3, message: draft)); w.AdvanceTo(144);
        Assert.Equal(ack, w.InspectGuardAssignments().Single(a => a.ActorId == 3).AcknowledgmentMessageId); Assert.Equal(4, w.InspectRecordHistory().Count(r => r.Kind == RecordKind.GuardAssignment)); RoundTrip(w);
    }

    [Fact]
    public void GuardDenialIsAnExplainedBlockedTeamAndDoesNotCreateLocalAwareness()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); long order = Dispatch(w).OrderMessageIds[0];
        w.Submit(115, new(new(3), ActionKind.Login, new(0, 0, 0), serviceId: 3)); w.Submit(116, new(new(3), ActionKind.PostMessage, new(0, 0, 0), serviceId: 3, message: Ack(order, "NotQualified"))); w.AdvanceTo(119);
        Assert.Equal(EscortDispatchStatus.Blocked, Dispatch(w).Status); Assert.Equal("GuardReported/NotQualified", Dispatch(w).Reason); Assert.Empty(w.InspectLocalGuardOrders(new(3))); RoundTrip(w);
        w.AdvanceTo(734); Assert.Equal(EscortDispatchStatus.Blocked, Dispatch(w).Status); Assert.Equal(GuardOrderStatus.EquipmentPending, Local(w, 3).Status); RoundTrip(w);
    }

    [Fact]
    public void FreshApprovalAfterExpiryReusesGuardsOnlyForANewAuthorization()
    {
        var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 50).CreateWorld(42); w.AdvanceTo(234);
        var request = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.RequestDetention).Draft;
        var fresh = new MessageDraft(request.Kind, request.RecipientKind, request.Recipient, request.Subject, request.Claim, 234,
            relatedMessageId: request.RelatedMessageId, operationKey: "fresh-request", detentionRequest: request.DetentionRequest);
        w.Submit(235, new(new(2), ActionKind.Login, new(0, 0, 0), serviceId: 2)); w.Submit(236, new(new(2), ActionKind.PostMessage, new(0, 0, 0), serviceId: 2, message: fresh)); w.AdvanceTo(239);
        Assert.Equal(2, w.InspectDetentionAuthorizations().Count); Assert.Equal(2, w.InspectEscortDispatches().Count); Assert.Equal(4, w.InspectGuardAssignments().Count);
        Assert.Equal(new long[] { 3, 4 }, w.InspectEscortDispatches().Last().Payload!.Guards); RoundTrip(w);
        w.AdvanceTo(734); RoundTrip(w);
    }

    [Fact]
    public void CorruptPrivateAwarenessCannotBeRestored()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(139); var local = Local(w, 3); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { writer.Write(local.OrderMessageId); writer.Write(local.ActorId); writer.Write(local.ReadTick); writer.Write(local.AcknowledgmentMessageId); writer.Write(local.Response); writer.Write((int)local.Status); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var edit in new[] { (Offset: 8, Value: 4L), (Offset: 16, Value: 180L), (Offset: 24, Value: 0L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(edit.Value).CopyTo(bad, start + edit.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }

    [Fact]
    public void ConcurrentAndChunkedTeamDispatchIsDeterministic()
    {
        string Run(int seed, bool chunked)
        {
            var w = Load(j => j["initialState"]!["detention"]!["orderTicks"] = 10 + seed * 10).CreateWorld((ulong)seed);
            if (chunked) for (int tick = 0; tick < 800; tick += 17) { w.AdvanceTo(tick); _ = w.InspectEscortDispatches(); _ = w.InspectGuardAssignments(); _ = w.InspectLocalGuardOrders(new(3)); }
            w.AdvanceTo(800); return w.Digest();
        }
        var expected = Enumerable.Range(0, 10).Select(s => Run(s, false)).ToArray();
        foreach (int parallel in new[] { 2, 4, 8 }) { var actual = new string[10]; Parallel.For(0, 10, new ParallelOptions { MaxDegreeOfParallelism = parallel }, s => actual[s] = Run(s, true)); Assert.Equal(expected, actual); }
    }
}
