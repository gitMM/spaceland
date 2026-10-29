using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

// Bounded acknowledgment deadlines for urgent escort orders, withdrawal and reassignment.
public sealed class EscortAcknowledgmentTests
{
    // The escort fixture plus two reserve guards and a 60-tick acknowledgment deadline.
    private static LoadedScenario Load(long acknowledgment = 60)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "escort_order_read.json")))!.AsObject();
        var s = json["initialState"]!;
        s["detention"]!["escort"]!["acknowledgmentTicks"] = acknowledgment;
        // Every guard carries the required equipment, so readiness never ends a team early.
        foreach (int guard in new[] { 3, 4, 5, 6 })
            s["items"]!.AsArray().Add(new JsonObject { ["id"] = 20 + guard, ["resource"] = "tool", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = guard });
        foreach (int guard in new[] { 5, 6 })
        {
            s["actors"]!.AsArray().Add(new JsonObject { ["id"] = guard, ["name"] = "Guard " + guard, ["role"] = "guard", ["position"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 } });
            s["items"]!.AsArray().Add(new JsonObject { ["id"] = 10 + guard, ["resource"] = "card", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = guard });
            s["information"]!["credentials"]!.AsArray().Add(new JsonObject { ["id"] = guard, ["holder"] = guard, ["account"] = guard, ["cardItem"] = 10 + guard });
            s["routines"]!["assignments"]!.AsArray().Add(new JsonObject { ["actorId"] = guard, ["cohort"] = 0, ["restTile"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 } });
        }
        json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "escort-acknowledgment-test");
    }
    private static EscortDispatchSnapshot Dispatch(World w) => Assert.Single(w.InspectEscortDispatches());
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());

    [Fact]
    public void SilentTeamIsWithdrawnAtTheDeadlineAndReassignedWithoutDuplicates()
    {
        // The guard terminals go offline before dispatch: no selected guard can read or acknowledge.
        var w = Load().CreateWorld(42, true); w.ScheduleTerminalChange(112, 3, false, false); w.ScheduleTerminalChange(112, 4, false, false);
        w.AdvanceTo(172);
        var first = Dispatch(w); Assert.Equal(EscortDispatchStatus.AwaitingReaders, first.Status); Assert.Equal(new long[] { 3, 4 }, first.Payload!.Guards);
        RoundTrip(w);
        w.AdvanceTo(174);
        Assert.Contains(w.Trace, e => e.Type == "EscortAcknowledgmentOverdue" && e.Tick == 173 && e.Detail.Contains("unacknowledged=3,4", StringComparison.Ordinal));
        var withdrawn = w.InspectGuardAssignments().Where(a => first.OrderMessageIds.Contains(a.OrderMessageId)).ToArray();
        Assert.All(withdrawn, a => { Assert.Equal(173, a.WithdrawnTick); Assert.True(a.WithdrawalMessageId > 0); });
        // Silent guards are not reselected; reserves form the replacement team at once.
        var second = Dispatch(w); Assert.Equal(new long[] { 5, 6 }, second.Payload!.Guards); Assert.NotEqual(first.Payload.DispatchId, second.Payload.DispatchId);
        RoundTrip(w);
        // Reconnected silent guards read the old order together with its withdrawal and never acknowledge it.
        w.ScheduleTerminalChange(175, 3, true, true); w.ScheduleTerminalChange(175, 4, true, true);
        var copy = World.Restore(w.Serialize(), traceEnabled: true);
        w.AdvanceTo(400); copy.AdvanceTo(400); Assert.Equal(w.Serialize(), copy.Serialize());
        foreach (long guard in new long[] { 3, 4 })
        {
            var local = Assert.Single(w.InspectLocalGuardOrders(new(guard)));
            Assert.Equal(GuardOrderStatus.Revoked, local.Status); Assert.Equal("KnownAssignmentWithdrawn", local.Reason);
        }
        Assert.All(withdrawn.Select(a => w.InspectGuardAssignments().Single(x => x.OrderMessageId == a.OrderMessageId)), a => Assert.Equal(0, a.AcknowledgmentMessageId));
        // Only the current team holds active commitments for the authorization.
        Assert.All(w.InspectGuardAssignments().Where(a => a.WithdrawnTick < 0), a => Assert.Contains(a.ActorId, new long[] { 5, 6 }));
        RoundTrip(w);
    }

    [Fact]
    public void LateAcknowledgmentAfterWithdrawalIsRejectedWithoutStateChange()
    {
        // Guard 4 reads the order at 136; both guard terminals fail before its acknowledgment
        // is posted and return after the deadline, so the reply reaches a withdrawn order.
        var w = Load().CreateWorld(42, true); w.ScheduleTerminalChange(137, 3, false, false); w.ScheduleTerminalChange(137, 4, false, false);
        w.ScheduleTerminalChange(200, 3, true, true); w.ScheduleTerminalChange(200, 4, true, true);
        w.AdvanceTo(1500);
        Assert.Contains(w.Trace, e => e.Type == "EscortAcknowledgmentOverdue");
        var rejected = w.Trace.Where(e => e.Type == "GuardAcknowledgmentRejected" && e.Detail == "AssignmentWithdrawn").ToArray();
        Assert.NotEmpty(rejected);
        foreach (var e in rejected)
        {
            var late = w.InspectMessages().Single(m => m.Id == e.MessageId);
            var assignment = w.InspectGuardAssignments().Single(a => a.OrderMessageId == late.Draft.RelatedMessageId);
            Assert.True(assignment.WithdrawnTick >= 0); Assert.Equal(0, assignment.AcknowledgmentMessageId);
        }
        Assert.DoesNotContain(w.InspectGuardAssignments().GroupBy(a => a.ActorId), g => g.Count(a => a.WithdrawnTick < 0) > 1);
        // Guard 3 acknowledged before the withdrawal and stays eligible; silent guard 4 does not.
        var dispatches = w.Trace.Where(e => e.Type == "EscortTeamDispatched").Select(e => e.Detail).ToArray();
        Assert.Equal("authorization=1; guards=3,4", dispatches[0]); Assert.Equal("authorization=1; guards=3,5", dispatches[1]);
        RoundTrip(w);
    }

    [Fact]
    public void DisabledDeadlineKeepsWaitingForReaders()
    {
        var w = Load(acknowledgment: 0).CreateWorld(42, true); w.ScheduleTerminalChange(112, 3, false, false); w.ScheduleTerminalChange(112, 4, false, false);
        w.AdvanceTo(400);
        Assert.DoesNotContain(w.Trace, e => e.Type == "EscortAcknowledgmentOverdue");
        Assert.Equal(EscortDispatchStatus.AwaitingReaders, Dispatch(w).Status); Assert.All(w.InspectGuardAssignments(), a => Assert.Equal(-1, a.WithdrawnTick));
    }
}
