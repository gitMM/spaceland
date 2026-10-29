using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class GuardOutcomeTests
{
    [Fact]
    public void RevocationGoldenScenarioCompletesAllMilestoneAssertions()
    {
        var scenario = new LoadedScenario(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "escort_revocation.json")), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "revocation-golden");
        var run = Runs.Execute(scenario, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(1, run.World.ActionsFailed);
        Assert.Equal(20, scenario.Scenario.Assertions.Count); RoundTrip(run.World);
    }
    private static LoadedScenario Load(Action<JsonObject>? change = null)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "detention_release.json")))!.AsObject();
        j["assertions"] = new JsonArray(); change?.Invoke(j);
        return new(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "escort.json")), "guard-outcome-test");
    }
    private static LocalGuardOrderSnapshot Local(World w, long actor = 3) => Assert.Single(w.InspectLocalGuardOrders(new(actor)));
    [Fact]
    public void LateAcknowledgmentsCannotCancelAReplacementWaitEstablishedByReceivedReports()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); var dispatch = Assert.Single(w.InspectEscortDispatches());
        foreach (var assignment in w.InspectGuardAssignments())
        {
            long guard = assignment.ActorId, order = assignment.OrderMessageId;
            w.Submit(115, new(new(guard), ActionKind.Login, new(0, 0, 0), serviceId: guard));
            var report = new MessageDraft(MessageKind.EscortOutcome, RecipientKind.Authority, default, new(SubjectKind.Order, order, AttributeKind.DetentionStatus),
                new(FactValueKind.Text, text: "TargetNotLocated"), 114, relatedMessageId: order, operationKey: "escort-result/" + order + "/TargetNotLocated");
            w.Submit(116, new(new(guard), ActionKind.PostMessage, new(0, 0, 0), serviceId: guard, message: report));
            w.Submit(120, new(new(guard), ActionKind.Login, new(0, 0, 0), serviceId: guard));
            var ack = new MessageDraft(MessageKind.EscortAcknowledged, RecipientKind.Authority, default, report.Subject,
                new(FactValueKind.Text, text: "Accepted"), 118, relatedMessageId: order, operationKey: "late-ack/" + order);
            w.Submit(121, new(new(guard), ActionKind.PostMessage, new(0, 0, 0), serviceId: guard, message: ack));
        }
        w.AdvanceTo(118); Assert.Equal(EscortDispatchStatus.AwaitingGuards, Assert.Single(w.InspectEscortDispatches()).Status);
        w.AdvanceTo(123); Assert.All(w.InspectGuardAssignments(), a => Assert.True(a.AcknowledgmentMessageId > 0));
        Assert.Equal(EscortDispatchStatus.AwaitingGuards, Assert.Single(w.InspectEscortDispatches()).Status);
        Assert.Empty(w.InspectLocalGuardOrders(new(3))); RoundTrip(w);
    }
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void RevocationDeliveryDoesNotGrantPrivateAwarenessOrFreezeTheTarget()
    {
        var w = Load().CreateWorld(42, true); w.ScheduleDetentionRevocation(124, 1, "withdraw-1"); w.AdvanceTo(124);
        Assert.Equal(DetentionAuthorizationStatus.Revoked, Assert.Single(w.InspectDetentionAuthorizations()).Status);
        Assert.Equal(EscortDispatchStatus.Cancelled, Assert.Single(w.InspectEscortDispatches()).Status);
        Assert.Empty(w.InspectKnownGuardRevocations()); Assert.Empty(w.InspectLocalGuardOrders(new(3)));
        Assert.Empty(w.InspectEscorts()); Assert.Equal(new Coord(1, 0, 0), w.ActorView(new(1)).CurrentTile); RoundTrip(w);
        w.AdvanceTo(125); Assert.All(Assert.Single(w.InspectDetentionRevocations()).Notices, id => Assert.Equal(MessageStage.DeliveredToMailbox, w.InspectMessages().Single(m => m.Id == id).Stage));
        Assert.Empty(w.InspectKnownGuardRevocations()); RoundTrip(w);
        w.AdvanceTo(134); Assert.Equal(GuardOrderStatus.Revoked, Local(w).Status); Assert.Single(w.InspectKnownGuardRevocations()); Assert.Empty(w.InspectLocalGuardOrders(new(4))); RoundTrip(w);
        w.AdvanceTo(734); Assert.Equal(2, w.InspectKnownGuardRevocations().Count); Assert.Equal(2, w.InspectGuardOutcomes(true).Count(o => o.Outcome == "AuthorizationRevoked")); Assert.Empty(w.InspectEscorts()); RoundTrip(w);
    }
    [Fact]
    public void OfflineCopiesContinueUntilConnectedIntakeChecksCurrentAuthorization()
    {
        var w = Load().CreateWorld(42, true); w.ScheduleTerminalChange(140, 3, false, false); w.ScheduleTerminalChange(140, 4, false, false);
        w.ScheduleDetentionRevocation(234, 1, "withdraw-offline"); w.AdvanceTo(494);
        Assert.Equal(GuardOrderStatus.TargetLocated, Local(w).Status); Assert.Equal(GuardOrderStatus.TargetLocated, Local(w, 4).Status); Assert.Empty(w.InspectKnownGuardRevocations());
        Assert.Equal(EscortStatus.Escorting, Assert.Single(w.InspectEscorts()).Status); RoundTrip(w);
        w.AdvanceTo(495); Assert.Equal(EscortStatus.Interrupted, Assert.Single(w.InspectEscorts()).Status); Assert.Equal("LearnedRevocation", Assert.Single(w.InspectEscorts()).Interruption);
        Assert.Equal(GuardOrderStatus.Revoked, Local(w).Status); Assert.Equal(GuardOrderStatus.EscortInterrupted, Local(w, 4).Status);
        var proof = Assert.Single(w.InspectKnownGuardRevocations()); Assert.Equal(0, proof.MessageId); Assert.True(proof.ServiceActionId > 0); Assert.Equal(1, proof.FacilityId);
        Assert.Empty(w.InspectDetentionStays()); Assert.Equal(0, Assert.Single(w.InspectDetentionFacilities()).Occupants); RoundTrip(w);
    }
    [Fact]
    public void DisconnectedIntakeCannotRevealRevocationUntilItReconnects()
    {
        var w = Load().CreateWorld(42); w.ScheduleDetentionServiceChange(234, 1, true, false); w.ScheduleDetentionRevocation(234, 1, "withdraw-disconnected");
        w.AdvanceTo(734); Assert.Empty(w.InspectKnownGuardRevocations()); Assert.Equal(EscortStatus.AwaitingIntake, Assert.Single(w.InspectEscorts()).Status); RoundTrip(w);
        w.ScheduleDetentionServiceChange(735, 1, true, true); w.AdvanceTo(736); Assert.Single(w.InspectKnownGuardRevocations()); Assert.Equal(EscortStatus.Interrupted, Assert.Single(w.InspectEscorts()).Status); RoundTrip(w);
    }
    [Theory]
    [InlineData(534)]
    [InlineData(795)]
    public void RevocationBeforeOrAtIntakeCompletionPreventsOccupancy(long revoked)
    {
        var w = Load().CreateWorld(42); w.ScheduleDetentionRevocation(revoked, 1, "withdraw-intake"); w.AdvanceTo(revoked);
        if (revoked < 795) { Assert.Empty(w.InspectKnownGuardRevocations()); Assert.Equal(DetentionStayStatus.Reserved, Assert.Single(w.InspectDetentionStays()).Status); RoundTrip(w); }
        w.AdvanceTo(795); Assert.Equal(DetentionStayStatus.Cancelled, Assert.Single(w.InspectDetentionStays()).Status); Assert.Equal(0, Assert.Single(w.InspectDetentionFacilities()).Occupants);
        Assert.Single(w.InspectKnownGuardRevocations()); Assert.Equal(GuardOrderStatus.Revoked, Local(w).Status); Assert.Equal(GuardOrderStatus.EscortInterrupted, Local(w, 4).Status); RoundTrip(w);
    }
    [Fact]
    public void RevocationAfterIntakeDoesNotEraseAnAlreadyServingSentence()
    {
        var w = Load().CreateWorld(42); w.ScheduleDetentionRevocation(834, 1, "withdraw-after-intake"); w.AdvanceTo(934);
        Assert.Equal(DetentionStayStatus.Detained, Assert.Single(w.InspectDetentionStays()).Status); Assert.Equal(1, Assert.Single(w.InspectDetentionFacilities()).Occupants); RoundTrip(w);
        w.AdvanceTo(2134); Assert.Equal(DetentionStayStatus.Released, Assert.Single(w.InspectDetentionStays()).Status); RoundTrip(w);
    }
    [Fact]
    public void RevocationAtExpiryIsAnIdempotentNoOpAndDoesNotPostMisleadingNotices()
    {
        var w = Load().CreateWorld(42); w.ScheduleDetentionRevocation(2113, 1, "expired"); w.AdvanceTo(2134); w.ScheduleDetentionRevocation(0, 1, "expired");
        Assert.Equal(DetentionAuthorizationStatus.Expired, Assert.Single(w.InspectDetentionAuthorizations()).Status); Assert.Empty(Assert.Single(w.InspectDetentionRevocations()).Notices);
        Assert.Throws<ArgumentException>(() => w.ScheduleDetentionRevocation(2135, 2, "expired")); RoundTrip(w);
    }
    [Fact]
    public void NegativeSearchIsOnlyKnownByAuthorityAfterAuthenticatedReportDelivery()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); w.Submit(115, new(new(1), ActionKind.Move, new(2, 0, 0))); w.AdvanceTo(394);
        Assert.Equal(2, w.InspectGuardOutcomes().Count(o => o.Outcome == "TargetNotLocated")); Assert.Empty(w.InspectGuardOutcomes(true)); Assert.Equal(EscortDispatchStatus.TeamForming, Assert.Single(w.InspectEscortDispatches()).Status); RoundTrip(w);
        w.AdvanceTo(734); Assert.Equal(2, w.InspectGuardOutcomes(true).Count(o => o.Outcome == "TargetNotLocated")); Assert.Equal(EscortDispatchStatus.AwaitingGuards, Assert.Single(w.InspectEscortDispatches()).Status);
        Assert.Equal(2, w.InspectRecordHistory().Count(e => e.Kind == RecordKind.GuardOutcome)); RoundTrip(w);
    }
    [Fact]
    public void AuthorityAcceptsAnAuthenticatedClaimWithoutConsultingPrivateSearchState()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); long order = Assert.Single(w.InspectEscortDispatches()).OrderMessageIds[0];
        var claim = new MessageDraft(MessageKind.EscortOutcome, RecipientKind.Authority, default, new(SubjectKind.Order, order, AttributeKind.DetentionStatus), new(FactValueKind.Text, text: "TargetNotLocated"), 114, relatedMessageId: order, operationKey: "escort-result/" + order + "/TargetNotLocated");
        w.Submit(115, new(new(3), ActionKind.Login, new(0, 0, 0), serviceId: 3)); w.Submit(116, new(new(3), ActionKind.PostMessage, new(0, 0, 0), serviceId: 3, message: claim)); w.AdvanceTo(118);
        Assert.Empty(w.InspectLocalGuardOrders(new(3))); Assert.Empty(w.InspectGuardOutcomes()); Assert.Single(w.InspectGuardOutcomes(true)); Assert.Equal(EscortDispatchStatus.NeedsAttention, Assert.Single(w.InspectEscortDispatches()).Status); RoundTrip(w);
        w.Submit(119, new(new(3), ActionKind.PostMessage, new(0, 0, 0), serviceId: 3, message: claim)); w.AdvanceTo(122); Assert.Single(w.InspectGuardOutcomes(true)); RoundTrip(w);
    }
    [Fact]
    public void EveryRevocationBoundaryRestoresAndContinuesDeterministically()
    {
        foreach (long revoke in new long[] { 190, 300, 600, 861, 900 })
        foreach (long tick in new long[] { 180, 190, 191, 200, 300, 560, 561, 600, 861, 862, 1000 })
        {
            var w = Load().CreateWorld(42); w.ScheduleDetentionRevocation(revoke, 1, "resume"); w.AdvanceTo(tick); var copy = World.Restore(w.Serialize());
            Assert.Equal(w.Serialize(), copy.Serialize()); w.AdvanceTo(2134); copy.AdvanceTo(2134); Assert.Equal(w.Serialize(), copy.Serialize()); RoundTrip(w);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaitingGuardCanReadAtALocalTerminalAndStopCustodyWithoutInformingItsPartner(bool withIntake)
    {
        var w = Load(j => {
            if (!withIntake) j["initialState"]!["detention"]!["facilities"] = new JsonArray();
            j["initialState"]!["information"]!["terminals"]!.AsArray().Add(new JsonObject {
                ["id"] = 5, ["position"] = new JsonObject { ["x"] = 2, ["y"] = 0, ["z"] = 0 }, ["capabilities"] = "Inbox", ["permittedRoles"] = new JsonArray("guard") });
        }).CreateWorld(42); w.ScheduleDetentionRevocation(534, 1, "mailbox-stop"); w.AdvanceTo(544);
        Assert.Equal(EscortStatus.AwaitingIntake, Assert.Single(w.InspectEscorts()).Status); Assert.Empty(w.InspectKnownGuardRevocations());
        w.Submit(545, new(new(4), ActionKind.Login, new(2, 0, 0), serviceId: 5)); w.AdvanceTo(546); RoundTrip(w);
        w.Submit(547, new(new(4), ActionKind.ReadInbox, new(2, 0, 0), serviceId: 5)); w.AdvanceTo(547); RoundTrip(w);
        w.AdvanceTo(548); Assert.Equal(EscortStatus.Interrupted, Assert.Single(w.InspectEscorts()).Status); Assert.Equal(GuardOrderStatus.Revoked, Local(w, 4).Status);
        Assert.Equal(GuardOrderStatus.EscortInterrupted, Local(w).Status); Assert.True(Assert.Single(w.InspectKnownGuardRevocations()).MessageId > 0); RoundTrip(w);
    }
    [Fact]
    public void AGuardCannotForgeAnAuthorityRevocationNotice()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); long order = Assert.Single(w.InspectEscortDispatches()).OrderMessageIds[0];
        var notice = new MessageDraft(MessageKind.EscortRevoked, RecipientKind.Account, new(3), new(SubjectKind.Order, order, AttributeKind.DetentionStatus), new(FactValueKind.Text, text: "AuthorizationRevoked"), 180, relatedMessageId: order, operationKey: "escort-revoked/" + order);
        w.Submit(115, new(new(3), ActionKind.Login, new(0, 0, 0), serviceId: 3)); w.Submit(116, new(new(3), ActionKind.PostMessage, new(0, 0, 0), serviceId: 3, message: notice)); w.AdvanceTo(118);
        Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.Kind == MessageKind.EscortRevoked); Assert.Empty(w.InspectKnownGuardRevocations()); Assert.Equal(DetentionAuthorizationStatus.Authorized, Assert.Single(w.InspectDetentionAuthorizations()).Status); RoundTrip(w);
    }
    [Fact]
    public void WrongAccountCannotReportForAnotherGuardsOrder()
    {
        var w = Load().CreateWorld(42); w.AdvanceTo(114); long order = Assert.Single(w.InspectEscortDispatches()).OrderMessageIds[0];
        var report = new MessageDraft(MessageKind.EscortOutcome, RecipientKind.Authority, default, new(SubjectKind.Order, order, AttributeKind.DetentionStatus), new(FactValueKind.Text, text: "TargetNotLocated"), 114, relatedMessageId: order, operationKey: "escort-result/" + order + "/TargetNotLocated");
        w.Submit(115, new(new(4), ActionKind.Login, new(0, 0, 0), serviceId: 4)); w.Submit(116, new(new(4), ActionKind.PostMessage, new(0, 0, 0), serviceId: 4, message: report)); w.AdvanceTo(118);
        Assert.Empty(w.InspectGuardOutcomes(true)); Assert.Equal(EscortDispatchStatus.AwaitingReaders, Assert.Single(w.InspectEscortDispatches()).Status); RoundTrip(w);
    }
    [Fact]
    public void AnUnstaffedAuthorizationCanBeRevokedWithoutInventingReaders()
    {
        var w = Load(j => j["initialState"]!["routines"]!["assignments"]!.AsArray().RemoveAt(2)).CreateWorld(42); w.ScheduleDetentionRevocation(234, 1, "unstaffed"); w.AdvanceTo(734);
        Assert.Equal(EscortDispatchStatus.Cancelled, Assert.Single(w.InspectEscortDispatches()).Status); Assert.Empty(w.InspectGuardAssignments()); Assert.Empty(Assert.Single(w.InspectDetentionRevocations()).Notices); RoundTrip(w);
    }
    [Fact]
    public void SnapshotCannotInventAnUnreadRevocationOrChangeItsServiceProof()
    {
        var w = Load().CreateWorld(42); w.ScheduleDetentionRevocation(234, 1, "proof"); w.AdvanceTo(495); var known = Assert.Single(w.InspectKnownGuardRevocations()); var bytes = w.Serialize();
        using var buffer = new MemoryStream(); using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { writer.Write(known.OrderMessageId); writer.Write(known.LearnedTick); writer.Write(known.MessageId); writer.Write(known.ServiceActionId); writer.Write(known.FacilityId); }
        var pattern = buffer.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        foreach (var edit in new[] { (Offset: 8, Value: 299L), (Offset: 16, Value: Assert.Single(w.InspectDetentionRevocations()).Notices[0]), (Offset: 24, Value: 0L), (Offset: 32, Value: 999L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(edit.Value).CopyTo(bad, start + edit.Offset); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
    }
}
