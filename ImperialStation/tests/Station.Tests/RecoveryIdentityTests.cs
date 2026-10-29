using System.Reflection;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class RecoveryIdentityTests
{
    [Fact]
    public void RunnerIdentityFixtureVerifiesScopeWithoutClaimingAutonomousRecovery()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "recovery_identity.json"));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        Assert.Equal(9, loaded.Scenario.Assertions.Count); Assert.Single(run.World.InspectItems()); Assert.Empty(run.World.InspectBeliefs());
        Assert.DoesNotContain(run.World.Trace, t => t.Type == "CredentialRevoked");
    }

    private static World Station(bool retained = true, bool held = false, int population = 1, long duration = 3, long session = 30)
    {
        var actors = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "worker" + i, "worker", Fixtures.A)).ToArray();
        return new World("recovery_identity", 1, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 2), new Tile(new(2), "Command", Fixtures.B, traversalOverride: 2) }, actors,
            actors.Select(a => new Item(new(100 + a.Id.Value), "keycard", 1, new ItemLocation(held ? LocationKind.ActorInventory : LocationKind.TileContainer, held ? a.Id.Value : 2))), true,
            new InformationSeed(TerminalFixtures.Tiny(session: session),
                new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }, capabilities: TerminalCapability.CredentialRecovery | TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.Records),
                    new TerminalSeed(2, Fixtures.A, new[] { "worker" }), new TerminalSeed(3, Fixtures.B, new[] { "worker" }, capabilities: TerminalCapability.CredentialRecovery) },
                actors.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(100 + a.Id.Value))),
                messages: new[] { new InitialMessage(TerminalFixtures.Draft(1), new(1)) },
                recovery: new CredentialRecoverySeed(actors.Select(a => new RecoveryCapabilitySeed(a.Id.Value, a.Account.Value, retained)), duration, autonomousActors: Array.Empty<long>())));
    }
    private static void Submit(World world, long tick, ActionKind kind, long actor = 1, long service = 1)
        => world.Submit(tick, new(new(actor), kind, Fixtures.A, serviceId: service));

    [Fact]
    public void FiniteIdentityCheckNeedsNoCardAndCreatesOnlyOwnRestrictedSession()
    {
        var w = Station(); Submit(w, 0, ActionKind.Login); w.AdvanceTo(0);
        Assert.Equal(FailureReason.MissingCredential, w.InspectActor(new(1)).LastFailure);
        Submit(w, 1, ActionKind.RecoveryLogin); w.AdvanceTo(3); Assert.Empty(w.InspectSessions());
        Assert.Equal(1, Assert.Single(Assert.Single(w.InspectTerminals(), t => t.Id == 1).Claims));
        w.AdvanceTo(4); var session = Assert.Single(w.InspectSessions());
        Assert.Equal(SessionScope.CredentialRecovery, session.Scope); Assert.Equal(1, session.ActorId); Assert.Equal(1, session.AccountId);
        Assert.Equal(0, w.ActorView(new(1)).SessionTerminalId);
        Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(w.InspectMailbox(new(1))).Stage);
        Assert.Empty(w.InspectBeliefs()); Assert.DoesNotContain(w.Trace, t => t.Type == "CredentialRevoked");
        Assert.Single(w.InspectItems()); Assert.Equal(2, Assert.Single(w.InspectItems()).Location.Owner); w.AssertAccounting();
    }

    [Theory]
    [InlineData(ActionKind.ReadInbox)]
    [InlineData(ActionKind.QueryRecords)]
    [InlineData(ActionKind.PostMessage)]
    public void RestrictedSessionCannotUseOrdinaryInboxRecordsOrPosts(ActionKind kind)
    {
        var w = Station(held: true); Submit(w, 0, ActionKind.RecoveryLogin);
        w.Submit(3, new(new(1), kind, Fixtures.A, serviceId: 1, message: kind == ActionKind.PostMessage ? TerminalFixtures.Draft(1) : null,
            query: kind == ActionKind.QueryRecords ? new RecordQuery(RecordKind.Credential) : null));
        w.AdvanceTo(8); Assert.Equal(FailureReason.RecoverySessionRestricted, w.InspectActor(new(1)).LastFailure);
        Assert.Equal(SessionScope.CredentialRecovery, Assert.Single(w.InspectSessions()).Scope);
        Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(w.InspectMessages()).Stage);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)), f => f.SourceKind is EvidenceKind.MessageRead or EvidenceKind.RecordQuery);
        Assert.Null(w.ActorView(new(1)).LastRecordQuery);
    }

    [Fact]
    public void RecoveryNeedsLocalDeskAndRetainedOwnCapability()
    {
        var w = Station(retained: false); Submit(w, 0, ActionKind.RecoveryLogin); w.AdvanceTo(0);
        Assert.Equal(FailureReason.IdentityVerificationFailed, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectSessions());
        w = Station(); Submit(w, 0, ActionKind.RecoveryLogin, service: 2); w.AdvanceTo(0);
        Assert.Equal(FailureReason.RequiredServiceNotPresent, w.InspectActor(new(1)).LastFailure);
        Submit(w, 1, ActionKind.RecoveryLogin, service: 3); w.AdvanceTo(1);
        Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(new(1)).LastFailure);
        w.Submit(2, new(new(1), ActionKind.RecoveryLogin, Fixtures.A, serviceId: 1, targetActor: new(2))); w.AdvanceTo(2);
        Assert.Equal(FailureReason.IdentityVerificationFailed, w.InspectActor(new(1)).LastFailure);
    }

    [Theory]
    [InlineData(ActionKind.Cancel)]
    [InlineData(ActionKind.Logout)]
    public void CancelledIdentityCheckReleasesQueueWithoutGrantingAccess(ActionKind kind)
    {
        var w = Station(population: 2); Submit(w, 0, ActionKind.RecoveryLogin); Submit(w, 1, ActionKind.RecoveryLogin, actor: 2);
        Submit(w, 2, kind); w.AdvanceTo(4); Assert.Empty(w.InspectSessions());
        w.AdvanceTo(5); Assert.Equal(2, Assert.Single(w.InspectSessions()).ActorId);
        Assert.DoesNotContain(w.Trace, t => t.Type == "RecoveryLoginSucceeded" && t.ActorId == 1);
        Assert.Empty(Assert.Single(w.InspectTerminals(), t => t.Id == 1).Queue);
    }

    [Fact]
    public void NormalAndRecoverySessionsSharePhysicalFifoCapacity()
    {
        var w = Station(held: true, population: 2); Submit(w, 0, ActionKind.Login); Submit(w, 1, ActionKind.RecoveryLogin, actor: 2);
        w.AdvanceTo(2); Assert.Equal(SessionScope.Normal, Assert.Single(w.InspectSessions()).Scope);
        Assert.Equal(2, Assert.Single(Assert.Single(w.InspectTerminals(), t => t.Id == 1).Queue));
        Submit(w, 3, ActionKind.Logout); w.AdvanceTo(5); Assert.Empty(w.InspectSessions());
        w.AdvanceTo(6); Assert.Equal(SessionScope.CredentialRecovery, Assert.Single(w.InspectSessions()).Scope);
        Assert.Equal(2, w.TotalTerminalWaitTicks);
    }

    [Fact]
    public void RecoveryCannotSubstituteForNormalLoginAfterSessionReplacement()
    {
        var w = Station(held: true); Submit(w, 0, ActionKind.RecoveryLogin); Submit(w, 3, ActionKind.Login);
        Submit(w, 4, ActionKind.ReadInbox); w.AdvanceTo(5);
        Assert.Equal(SessionScope.Normal, Assert.Single(w.InspectSessions()).Scope);
        Assert.Equal(MessageStage.Read, Assert.Single(w.InspectMailbox(new(1))).Stage);
        Assert.Equal(1, w.ActorView(new(1)).SessionTerminalId);
        w = Station(); Submit(w, 0, ActionKind.RecoveryLogin); Submit(w, 3, ActionKind.Login); w.AdvanceTo(4);
        Assert.Equal(FailureReason.MissingCredential, w.InspectActor(new(1)).LastFailure);
        Assert.Equal(SessionScope.CredentialRecovery, Assert.Single(w.InspectSessions()).Scope);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("departure")]
    [InlineData("outage")]
    public void ScopedSessionEndsOnNormalPhysicalBoundaries(string interruption)
    {
        var w = Station(session: 4); Submit(w, 0, ActionKind.RecoveryLogin);
        if (interruption == "departure") w.Submit(4, new(new(1), ActionKind.Move, Fixtures.B));
        if (interruption == "outage") w.ScheduleTerminalChange(4, 1, false, false);
        w.AdvanceTo(7); Assert.Empty(w.InspectSessions()); Assert.Empty(Assert.Single(w.InspectTerminals(), t => t.Id == 1).Claims);
    }

    [Fact]
    public void OutageDuringVerificationAndOldCardRevocationCannotGrantNormalAccess()
    {
        var w = Station(); Submit(w, 0, ActionKind.RecoveryLogin); w.ScheduleTerminalChange(1, 1, false, false); w.AdvanceTo(1);
        var restored = World.Restore(w.Serialize()); w.AdvanceTo(4); restored.AdvanceTo(4); Assert.Equal(w.Digest(), restored.Digest());
        Assert.Empty(w.InspectSessions()); Assert.Equal(FailureReason.RequiredServiceNotPresent, w.InspectActor(new(1)).LastFailure);
        w = Station(); Submit(w, 0, ActionKind.RecoveryLogin); w.ScheduleCredentialRevocation(1, 1); w.AdvanceTo(3);
        Assert.Equal(SessionScope.CredentialRecovery, Assert.Single(w.InspectSessions()).Scope);
    }

    [Fact]
    public void CheckpointsPreserveQueuedChecksSessionsAndExpiry()
    {
        foreach (long checkpoint in new long[] { 0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 14 })
        {
            var w = Station(population: 2, session: 4); Submit(w, 0, ActionKind.RecoveryLogin); Submit(w, 1, ActionKind.RecoveryLogin, actor: 2);
            w.AdvanceTo(checkpoint); var restored = World.Restore(w.Serialize(), true); Assert.Equal(w.Digest(), restored.Digest());
            w.AdvanceTo(20); restored.AdvanceTo(20); Assert.Equal(w.Digest(), restored.Digest());
            Assert.Equal(w.Trace.Where(t => t.Tick > checkpoint).Select(t => t.Type + "/" + t.Tick + "/" + t.ActorId), restored.Trace.Select(t => t.Type + "/" + t.Tick + "/" + t.ActorId));
        }
    }

    [Fact]
    public void SnapshotRejectsWidenedScopeForeignAccountAndMissingExpiry()
    {
        foreach (var mutation in new[] { "scope", "account", "credential", "expiry" })
        {
            var w = Station(population: 2); Submit(w, 0, ActionKind.RecoveryLogin); w.AdvanceTo(3);
            var map = typeof(World).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            var session = map.GetType().GetProperty("Item")!.GetValue(map, new object[] { new Id<ActorTag>(1) })!;
            string field = mutation switch { "scope" => "Scope", "account" => "Account", "credential" => "Credential", _ => "Expires" };
            object value = mutation switch { "scope" => SessionScope.Normal, "account" => new Id<AccountTag>(2), "credential" => 1L, _ => 99L };
            session.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, value);
            Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
        }
    }

    [Fact]
    public void DefaultsAndCapabilityOwnershipAreValidated()
    {
        Assert.Equal(300, new CredentialRecoverySeed(Array.Empty<RecoveryCapabilitySeed>()).IdentityCheckTicks);
        Assert.Throws<ArgumentException>(() => new CredentialRecoverySeed(Array.Empty<RecoveryCapabilitySeed>(), 0));
        var w = Station(); var initialize = typeof(World).GetMethod("InitializeCredentialRecovery", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var capability in new[] { new RecoveryCapabilitySeed(2, 2), new RecoveryCapabilitySeed(1, 2), new RecoveryCapabilitySeed(1, 1) })
            Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => initialize.Invoke(w, new object[] { new CredentialRecoverySeed(new[] { capability }) })).InnerException);
    }
}
