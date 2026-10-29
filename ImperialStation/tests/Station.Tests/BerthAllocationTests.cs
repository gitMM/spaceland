using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class BerthAllocationTests
{
    private static World Make(int population = 2, bool autonomous = false, bool credential = true, int beds = 1, int assigned = 0, bool requiresCredential = true, bool crossingCritical = false)
    {
        var actors = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "Resident " + i, "worker", Fixtures.A)).ToArray();
        var tiles = new[] { new Tile(new(1), "Habitat", Fixtures.A, traversalOverride: 4) };
        return new World("berth-allocation", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog, tiles, actors,
            credential ? actors.Select(a => new Item(new(100 + a.Id.Value), "keycard", 1, new(LocationKind.ActorInventory, a.Id.Value))) : Array.Empty<Item>(), true,
            information: new InformationSeed(TerminalFixtures.Tiny(), credentials: credential ? actors.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(100 + a.Id.Value))) : null,
                publication: new PublicKnowledge(1, tiles.Select(t => new KnownTile(t.Id, t.Position, 4)), Array.Empty<PublicService>())),
            needs: new NeedSeed(initial: actors.Select(a => new NeedInitialValue(a.Id, NeedKind.SleepPressure, crossingCritical ? 1000 * NeedAccumulator.HourMilliseconds - 10000 : 850 * NeedAccumulator.HourMilliseconds)),
                services: Enumerable.Range(1, beds).Select(i => new NeedServiceDefinition(i, NeedServiceKind.Bed, Fixtures.A, durationTicks: 5, assignedActorId: i <= assigned ? i : 0, allocationTicks: 3, allocationRequiresCredential: requiresCredential)),
                autonomousActors: autonomous ? null : Array.Empty<long>()));
    }
    private static void Allocate(World w, long actor = 1, long berth = 1, long tick = 0) => w.Submit(tick, new(new(actor), ActionKind.AllocateBerth, Fixtures.A, serviceId: berth));
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());

    [Fact]
    public void AllocationIsFinitePrivateRecordedAndDoesNotChangeCapacity()
    {
        var w = Make(); Allocate(w); w.AdvanceTo(2);
        Assert.Empty(w.InspectBerthAllocations()); Assert.Equal(0, w.ActorView(new(1)).OwnBerthId); Assert.Equal(1, w.VacantHousingBerths);
        Assert.True(Assert.Single(w.InspectBerths()).ReservedActionId > 0); RoundTrip(w);
        w.AdvanceTo(3); var a = Assert.Single(w.InspectBerthAllocations()); Assert.Equal(3, a.GrantedTick); Assert.Equal(1, a.ActorId);
        Assert.Equal(1, w.ActorView(new(1)).OwnBerthId); Assert.Equal(0, w.ActorView(new(2)).OwnBerthId);
        Assert.Equal(0, w.ActorView(new(2)).NeedServices[0].AssignedActorId); Assert.Equal(1, w.UsableHousingBerths); Assert.Equal(0, w.VacantHousingBerths);
        Assert.True(Assert.Single(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.AssignedBerth).Pinned);
        Assert.DoesNotContain(w.InspectKnowledge(new(2)), f => f.Key.Attribute == AttributeKind.AssignedBerth);
        Assert.Single(w.InspectRecordHistory(), r => r.Kind == RecordKind.BerthAllocation); Assert.Equal(0, Assert.Single(w.InspectBerths()).ReservedActionId);
        w.AssertAccounting(); RoundTrip(w);
    }
    [Fact]
    public void CompetingApplicantsCannotHotBunkOrAcquireTwoBerths()
    {
        var w = Make(beds: 2); Allocate(w); Allocate(w, 2); w.AdvanceTo(3);
        var assigned = Assert.Single(w.InspectBerthAllocations()); Assert.Equal(1, w.ActionsFailed); RoundTrip(w);
        Allocate(w, assigned.ActorId, 2, 4); w.AdvanceTo(4);
        Assert.Equal(FailureReason.AlreadyAssigned, w.InspectActor(new(assigned.ActorId)).LastFailure); Assert.Single(w.InspectBerthAllocations());
        long other = assigned.ActorId == 1 ? 2 : 1;
        w.Submit(5, new(new(other), ActionKind.Sleep, Fixtures.A, serviceId: 1)); w.AdvanceTo(5);
        Assert.Equal(FailureReason.NotQualified, w.InspectActor(new(other)).LastFailure); RoundTrip(w);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAndOutageReleaseWithoutGranting(bool outage)
    {
        var w = Make(); Allocate(w);
        if (outage) w.ScheduleNeedServiceChange(1, 1, false); else w.Submit(1, new(new(1), ActionKind.Cancel, Fixtures.A));
        w.AdvanceTo(5); Assert.Empty(w.InspectBerthAllocations()); Assert.Equal(0, Assert.Single(w.InspectBerths()).ReservedActionId);
        Assert.Equal(0, w.ActorView(new(1)).OwnBerthId); RoundTrip(w);
    }
    [Fact]
    public void RevocationBeforeCompletionPreventsAssignmentButDoesNotErasePrivateHistory()
    {
        var w = Make(); Allocate(w); w.ScheduleCredentialRevocation(1, 1); w.AdvanceTo(2); RoundTrip(w);
        w.AdvanceTo(3); Assert.Empty(w.InspectBerthAllocations()); Assert.Equal(FailureReason.CredentialRevoked, w.InspectActor(new(1)).LastFailure); RoundTrip(w);
    }
    [Fact]
    public void MissingCredentialCannotAuthorizeDefaultAllocation()
    {
        var w = Make(credential: false); Assert.Equal(PlanOutcome.NoPlan, NeedPlanning.Plan(w.ActorView(new(1)), NeedKind.SleepPressure).Outcome);
        Allocate(w); w.AdvanceTo(3); Assert.Equal(FailureReason.MissingCredential, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectBerthAllocations()); RoundTrip(w);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutonomousApplicantAllocatesThenSleeps(bool credential)
    {
        var w = Make(population: 1, autonomous: true, credential: credential, requiresCredential: credential);
        var plan = NeedPlanning.Plan(w.ActorView(new(1)), NeedKind.SleepPressure);
        Assert.Equal(new[] { ActionKind.AllocateBerth, ActionKind.Sleep }, plan.Steps.Select(s => s.Intent.Kind));
        w.AdvanceTo(2); Assert.Empty(w.InspectNeedActivities()); Assert.Empty(w.InspectBerthAllocations()); RoundTrip(w);
        w.AdvanceTo(3); Assert.Single(w.InspectBerthAllocations()); Assert.Equal(NeedActivityStatus.Queued, Assert.Single(w.InspectNeedActivities()).Status); RoundTrip(w);
        w.AdvanceTo(9); Assert.Contains(w.InspectNeedActivities(), a => a.Kind == ActionKind.Sleep && a.Status == NeedActivityStatus.Completed); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
    }
    [Fact]
    public void StaleVacancyFailsLocallyThenAnAlternativeCanBeAllocated()
    {
        var w = Make(autonomous: true, beds: 2); w.AdvanceTo(15);
        Assert.Equal(2, w.InspectBerthAllocations().Count); Assert.Equal(2, w.InspectBerthAllocations().Select(a => a.ActorId).Distinct().Count());
        Assert.Contains(w.InspectNeedAgents(), a => a.Failures.Any(f => f.Reason == FailureReason.ResourceClaimed));
        Assert.All(w.InspectActors(), a => Assert.True(w.ActorView(a.Id).OwnBerthId > 0)); RoundTrip(w);
    }
    [Fact]
    public void VacantUsableBedsCountAsCapacityBeforeAssignment()
    {
        var w = Make(population: 20, beds: 24, assigned: 20); Assert.Equal(24, w.UsableHousingBerths); Assert.Equal(4, w.VacantHousingBerths);
        Assert.All(w.InspectBerths(), b => Assert.Equal(0, b.Occupants)); RoundTrip(w);
        w.ScheduleNeedServiceChange(0, 24, false); w.AdvanceTo(0); Assert.Equal(23, w.UsableHousingBerths); Assert.Equal(3, w.VacantHousingBerths); RoundTrip(w);
    }
    [Fact]
    public void EveryAllocationAndSleepCheckpointContinuesIdentically()
    {
        var baseline = Make(autonomous: true, beds: 2); baseline.AdvanceTo(30);
        for (int tick = -1; tick <= 15; tick++)
        {
            var prefix = Make(autonomous: true, beds: 2); if (tick >= 0) prefix.AdvanceTo(tick);
            var restored = World.Restore(prefix.Serialize(), true); Assert.Equal(prefix.Serialize(), restored.Serialize()); restored.AdvanceTo(30);
            Assert.Equal(baseline.Serialize(), restored.Serialize()); Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        }
    }
    [Fact]
    public void CriticalSleepCrossingDoesNotCancelItsOwnFiniteAllocation()
    {
        var w = Make(population: 1, autonomous: true, crossingCritical: true); w.AdvanceTo(2);
        Assert.Equal(0, w.CriticalNeedInterruptions); Assert.True(Assert.Single(w.InspectBerths()).ReservedActionId > 0); RoundTrip(w);
        w.AdvanceTo(9); Assert.Single(w.InspectBerthAllocations()); Assert.Contains(w.InspectNeedActivities(), a => a.Status == NeedActivityStatus.Completed); RoundTrip(w);
    }
    [Fact]
    public void AutonomousGoldenAllocationPassesAllAssertions()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "berth_allocation.json")); Assert.Empty(loaded.Scenario.ScriptedActors);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(13, loaded.Scenario.Assertions.Count);
        Assert.Equal(1, run.World.UsableHousingBerths); Assert.Equal(0, run.World.VacantHousingBerths); Assert.Equal(0, run.World.ActionsFailed); RoundTrip(run.World);
    }
    [Fact]
    public void SnapshotRejectsForgedAllocationReceiptRecordAndReservation()
    {
        var w = Make(); Allocate(w); w.AdvanceTo(3); var bytes = w.Serialize(); var a = Assert.Single(w.InspectBerthAllocations());
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(1); writer.Write(a.BerthId); writer.Write(a.ActorId); writer.Write(a.AccountId); writer.Write(a.GrantedTick); writer.Write(a.ActionId); writer.Write(a.CredentialId); writer.Write(a.RecordVersion);
            writer.Write(1); writer.Write(a.ActorId); writer.Write(a.BerthId); writer.Write(0);
        }
        var block = stream.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        foreach (var mutation in new[] { (4, 999L), (12, 2L), (20, 2L), (28, 2L), (36, 0L), (44, 2L), (52, 1L), (64, 2L), (72, 999L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Item2).CopyTo(bad, start + mutation.Item1); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
        var pending = Make(); Allocate(pending); pending.AdvanceTo(0); var pendingBytes = pending.Serialize();
        var reservation = new byte[28]; BitConverter.GetBytes(1).CopyTo(reservation, 8); BitConverter.GetBytes(1L).CopyTo(reservation, 12); BitConverter.GetBytes(1L).CopyTo(reservation, 20);
        int offset = Assert.Single(Enumerable.Range(0, pendingBytes.Length - reservation.Length + 1), i => pendingBytes.AsSpan(i, reservation.Length).SequenceEqual(reservation));
        BitConverter.GetBytes(999L).CopyTo(pendingBytes, offset + 20); Assert.Throws<ArgumentException>(() => World.Restore(pendingBytes));
    }
}
