using System.Reflection;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class CredentialRecoveryProtocolTests
{
    [Fact]
    public void RunnerUsesSpecifiedIdentityAndIssuanceDurationsAndConservesPhysicalCards()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "recovery_protocol.json"));
        Assert.Equal(300, loaded.Scenario.InitialState.Information!.Recovery!.IdentityCheckTicks);
        Assert.Equal(600, Assert.Single(loaded.Catalog.Recipes).Duration);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(17, loaded.Scenario.Assertions.Count);
        Assert.Equal(7, run.World.ActionsCompleted); Assert.Equal(0, run.World.ActionsFailed); Assert.Empty(run.World.InspectBeliefs()); Assert.Equal(906, Assert.Single(run.World.InspectCredentialRecoveryCases()).IssuedTick);
        Assert.Equal(run.World.Digest(), World.Restore(run.World.Serialize()).Digest());
    }
    private static World Station(long stock = 2, long session = 100, long read = 1, int knowledge = 256, bool desk = true)
    {
        var items = new List<Item> { new(new(101), "keycard", 1, new(LocationKind.TileContainer, 2)), new(new(102), "keycard", 1, new(LocationKind.TileContainer, 2)) };
        if (stock > 0) items.Add(new(new(10), "blank", stock, new(LocationKind.TileContainer, 1)));
        return new World("recovery_protocol", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Storage", Fixtures.A, traversalOverride: 2), new Tile(new(2), "Command", Fixtures.B, traversalOverride: 2) },
            new[] { new ActorSeed(new(1), "worker", "worker", Fixtures.A), new ActorSeed(new(2), "other worker", "worker", Fixtures.A) }, items, true,
            new InformationSeed(TerminalFixtures.Tiny(session: session, read: read, knowledge: knowledge),
                new[] { new TerminalSeed(1, Fixtures.A, new[] { "worker" }, capacity: 2, capabilities: TerminalCapability.CredentialRecovery | TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.Records), new TerminalSeed(2, Fixtures.B, new[] { "worker" }) },
                new[] { new CredentialSeed(1, new(1), new(1), new(101)), new CredentialSeed(2, new(2), new(2), new(102)) },
                messages: new[] { new InitialMessage(TerminalFixtures.Draft(1), new(2), 0) },
                recovery: new CredentialRecoverySeed(new[] { new RecoveryCapabilitySeed(1, 1), new RecoveryCapabilitySeed(2, 2) }, 3, desk ? new[] { new RecoveryDeskSeed(1, 1, 1) } : null, autonomousActors: Array.Empty<long>())),
            new WorkSeed(new[] { new ResourceDefinition("blank"), new ResourceDefinition("keycard") }, new[] { new RecipeDefinition(1, "CredentialIssuance", new[] { new MaterialAmount("blank", 1) }, new[] { new MaterialAmount("keycard", 1) }, 5) },
                new[] { new WorkstationDefinition(1, Fixtures.A, "CredentialIssuance", new WorkAccess(new[] { "worker" })) }, Array.Empty<MachineDefinition>()));
    }
    private static void Act(World w, long tick, ActionKind kind, long actor = 1, long item = 101) => w.Submit(tick, new(new(actor), kind, Fixtures.A, new(kind is ActionKind.PostLostCard or ActionKind.IssueReplacement or ActionKind.PickUp ? item : 0), serviceId: 1));
    private static void Authorize(World w, long actor = 1, long item = 101)
    { Act(w, 0, ActionKind.RecoveryLogin, actor); Act(w, 3, ActionKind.PostLostCard, actor, item); Act(w, 8, ActionKind.ReadRecoveryInbox, actor); }
    private static bool KnownCase(World w, long actor = 1) => w.InspectKnowledge(new(actor)).Any(f => f.Key.Attribute == AttributeKind.RecoveryCase);
    private static long Stock(World w) => w.InspectItems().Where(i => i.Resource == "blank").Sum(i => i.Quantity);

    [Fact]
    public void FormalAcceptanceRevokesButUnreadReplyNeitherTeachesNorIssues()
    {
        var w = Station(); Act(w, 0, ActionKind.RecoveryLogin); Act(w, 3, ActionKind.PostLostCard); Act(w, 8, ActionKind.IssueReplacement);
        w.AdvanceTo(5); Assert.Empty(w.InspectCredentialRecoveryCases()); Assert.DoesNotContain(w.Trace, t => t.Type == "CredentialRevoked");
        w.AdvanceTo(6); var c = Assert.Single(w.InspectCredentialRecoveryCases()); Assert.Equal(6, c.AcceptedTick); Assert.Equal(0, c.IssuedCardId); Assert.False(KnownCase(w));
        Assert.Equal(2, Stock(w)); Assert.Equal(3, w.InspectItems().Count); Assert.Empty(w.InspectRecoveryMailbox(new(1))); Assert.Empty(w.InspectBeliefs());
        w.AdvanceTo(8); Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(1)).LastFailure); Assert.False(KnownCase(w));
        Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(w.InspectRecoveryMailbox(new(1))).Stage);
        Assert.Single(w.InspectMailbox(new(1))); Assert.Empty(w.InspectRecoveryMailbox(new(2))); Assert.False(KnownCase(w, 2));
        Act(w, 9, ActionKind.ReadRecoveryInbox); w.AdvanceTo(10); Assert.True(KnownCase(w));
        var fact = Assert.Single(w.InspectKnowledge(new(1)), f => f.Key.Attribute == AttributeKind.RecoveryCase); Assert.Equal(EvidenceKind.MessageRead, fact.SourceKind); Assert.Equal(10, fact.LearnedTick);
        Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(w.InspectMailbox(new(1))).Stage);
    }

    [Fact]
    public void IssuanceConsumesOneBlankAfterDurationLeavesOutputAndRequiresPickupForNormalLogin()
    {
        var w = Station(); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); w.AdvanceTo(13);
        Assert.Equal(2, Stock(w)); Assert.Equal(0, Assert.Single(w.InspectCredentialRecoveryCases()).IssuedCardId); Assert.Equal(2, w.InspectResourceClaims().Count);
        w.AdvanceTo(14); var c = Assert.Single(w.InspectCredentialRecoveryCases()); Assert.Equal(103, c.IssuedCardId); Assert.Equal(3, c.IssuedCredentialId); Assert.Equal(14, c.IssuedTick);
        var card = Assert.Single(w.InspectItems(), i => i.Id.Value == 103); Assert.Equal(LocationKind.ServiceOutput, card.Location.Kind); Assert.Equal(1, Stock(w)); Assert.Empty(w.InspectResourceClaims());
        Act(w, 15, ActionKind.Login); w.AdvanceTo(15); Assert.Equal(FailureReason.MissingCredential, w.InspectActor(new(1)).LastFailure);
        Act(w, 16, ActionKind.PickUp, item: 103); Act(w, 17, ActionKind.Login); Act(w, 18, ActionKind.ReadInbox); w.AdvanceTo(19);
        Assert.Equal(SessionScope.Normal, Assert.Single(w.InspectSessions()).Scope); Assert.Equal(MessageStage.Read, Assert.Single(w.InspectMailbox(new(1))).Stage);
        Assert.Equal(LocationKind.ActorInventory, Assert.Single(w.InspectItems(), i => i.Id.Value == 103).Location.Kind);
        Assert.Equal(2, Assert.Single(w.InspectItems(), i => i.Id.Value == 101).Location.Owner);
        Assert.Equal(2, w.InspectResourceTransactions().Count); w.AssertAccounting();
    }

    [Fact]
    public void RepeatedRequestsAndIssuanceReturnSameCaseAndCardWithoutExtraStock()
    {
        var w = Station(); Authorize(w); Act(w, 5, ActionKind.PostLostCard); Act(w, 9, ActionKind.IssueReplacement); Act(w, 14, ActionKind.IssueReplacement); Act(w, 15, ActionKind.PostLostCard); Act(w, 20, ActionKind.ReadRecoveryInbox); w.AdvanceTo(21);
        var c = Assert.Single(w.InspectCredentialRecoveryCases()); Assert.Equal(103, c.IssuedCardId); Assert.Equal(1, Stock(w)); Assert.Equal(2, w.InspectResourceTransactions().Count);
        Assert.Single(w.Trace, e => e.Type == "ReplacementCardIssued"); Assert.Single(w.Trace, e => e.Type == "ReplacementCardAlreadyIssued");
        Assert.Equal(3, w.InspectRecoveryMailbox(new(1)).Count); Assert.All(w.InspectRecoveryMailbox(new(1)), m => Assert.Equal(c.Id, m.Draft.RecoveryReply!.CaseId));
        var last = w.InspectRecoveryMailbox(new(1)).Last(); Assert.Equal(103, last.Draft.RecoveryReply!.IssuedCardId); Assert.Equal(3, last.Draft.RecoveryReply.IssuedCredentialId);
        Assert.Single(w.Trace, e => e.Type == "CredentialRevoked"); w.AssertAccounting();
    }

    [Theory]
    [InlineData(false, 0, FailureReason.RequiredServiceNotPresent)]
    [InlineData(true, 0, FailureReason.InsufficientMaterials)]
    public void MissingDeskOrStockExplainsWaitWithoutFabricatingCard(bool desk, long stock, FailureReason expected)
    {
        var w = Station(stock: stock, desk: desk); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); w.AdvanceTo(20);
        Assert.Equal(expected, w.InspectActor(new(1)).LastFailure); Assert.Equal(0, Assert.Single(w.InspectCredentialRecoveryCases()).IssuedCardId); Assert.Empty(w.InspectResourceTransactions()); Assert.Empty(w.InspectResourceClaims());
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("logout")]
    [InlineData("terminal")]
    [InlineData("printer")]
    [InlineData("expiry")]
    public void InterruptedIssuanceConsumesNothingAndReleasesClaims(string interruption)
    {
        var w = Station(session: interruption == "expiry" ? 9 : 100); Authorize(w); Act(w, 9, ActionKind.IssueReplacement);
        if (interruption == "cancel") Act(w, 11, ActionKind.Cancel);
        if (interruption == "logout") Act(w, 11, ActionKind.Logout);
        if (interruption == "terminal") w.ScheduleTerminalChange(11, 1, false, false);
        if (interruption == "printer") w.ScheduleWorkServiceChange(11, 1, false, false);
        w.AdvanceTo(12); var copy = World.Restore(w.Serialize()); w.AdvanceTo(15); copy.AdvanceTo(15); Assert.Equal(w.Digest(), copy.Digest());
        Assert.Equal(2, Stock(w)); Assert.Equal(0, Assert.Single(w.InspectCredentialRecoveryCases()).IssuedCardId); Assert.Empty(w.InspectResourceTransactions()); Assert.Empty(w.InspectResourceClaims());
    }

    [Fact]
    public void ForeignAccountAndOrdinaryPostsCannotForgeRecovery()
    {
        var w = Station(); Act(w, 0, ActionKind.RecoveryLogin); Act(w, 3, ActionKind.PostLostCard, item: 102); w.AdvanceTo(4);
        Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectCredentialRecoveryCases());
        Act(w, 5, ActionKind.ReadRecoveryInbox); w.AdvanceTo(6); Assert.False(KnownCase(w)); Assert.False(KnownCase(w, 2));
        Act(w, 7, ActionKind.IssueReplacement, item: 102); w.AdvanceTo(8); Assert.Equal(FailureReason.UnknownRequiredFact, w.InspectActor(new(1)).LastFailure);
    }

    [Theory]
    [InlineData(MessageKind.LostCardReport)]
    [InlineData(MessageKind.RecoveryReply)]
    public void OrdinaryTerminalPostCannotForgeRecoveryProtocol(MessageKind kind)
    {
        var w = Station(); w.Submit(0, new(new(1), ActionKind.Move, Fixtures.B)); w.Submit(2, new(new(1), ActionKind.PickUp, Fixtures.B, new(101)));
        w.Submit(3, new(new(1), ActionKind.Login, Fixtures.B, serviceId: 2));
        var draft = kind == MessageKind.LostCardReport
            ? new MessageDraft(kind, RecipientKind.CredentialService, default, new FactKey(SubjectKind.Credential, 1, AttributeKind.CredentialStatus), FactValue.Boolean(false), 4)
            : new MessageDraft(kind, RecipientKind.RecoveryAccount, new(1), new FactKey(SubjectKind.Credential, 1, AttributeKind.RecoveryCase), FactValue.Integer(1), 4, relatedMessageId: 1, recoveryReply: new RecoveryReplyPayload(1, 1));
        w.Submit(4, new(new(1), ActionKind.PostMessage, Fixtures.B, serviceId: 2, message: draft)); w.AdvanceTo(8);
        Assert.Equal(FailureReason.InvalidMessage, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectCredentialRecoveryCases()); Assert.Single(w.InspectMessages());
    }

    [Fact]
    public void AnotherRecoverySessionCannotReadTheOwnersMailbox()
    {
        var w = Station(); Authorize(w); Act(w, 0, ActionKind.RecoveryLogin, actor: 2); Act(w, 8, ActionKind.ReadRecoveryInbox, actor: 2); w.AdvanceTo(9);
        Assert.True(KnownCase(w)); Assert.False(KnownCase(w, 2)); Assert.Empty(w.InspectRecoveryMailbox(new(2)));
    }

    [Fact]
    public void StaleIssuanceReceiptsDoNotUndoNewerCardRevocationOrPickupEvidence()
    {
        var w = Station(); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); Act(w, 14, ActionKind.PickUp, item: 103);
        Act(w, 15, ActionKind.PostLostCard, item: 103); Act(w, 16, ActionKind.PostLostCard); Act(w, 18, ActionKind.Login);
        Act(w, 20, ActionKind.ReadRecoveryInbox); Act(w, 22, ActionKind.ReadRecoveryInbox); Act(w, 24, ActionKind.IssueReplacement);
        Act(w, 25, ActionKind.IssueReplacement, item: 103); w.AdvanceTo(30);
        var status = Assert.Single(w.InspectKnowledge(new(1)), f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.SubjectId == 3 && f.Key.Attribute == AttributeKind.CredentialStatus);
        Assert.Equal(FactValue.Boolean(false), status.Value); Assert.Equal(18, status.ObservedTick);
        var location = Assert.Single(w.InspectKnowledge(new(1)), f => f.Key.SubjectKind == SubjectKind.Item && f.Key.SubjectId == 103 && f.Key.Attribute == AttributeKind.Location);
        Assert.Equal(15, location.ObservedTick);
        Assert.Equal(2, w.InspectCredentialRecoveryCases().Count); Assert.Equal(0, Stock(w)); Assert.Equal(104, w.InspectCredentialRecoveryCases().Last().IssuedCardId);
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest()); w.AssertAccounting();
    }

    [Fact]
    public void CancelledIssuanceCanRetryAndCallerCannotChooseAnotherRecipe()
    {
        var w = Station(); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); Act(w, 11, ActionKind.Cancel);
        w.Submit(12, new(new(1), ActionKind.IssueReplacement, Fixtures.A, new(101), serviceId: 1, recipeId: 999));
        w.AdvanceTo(17); Assert.Equal(103, Assert.Single(w.InspectCredentialRecoveryCases()).IssuedCardId); Assert.Equal(1, Stock(w));
        w.Submit(18, new(new(1), ActionKind.IssueReplacement, Fixtures.A, new(101), serviceId: 1, recipeId: 999));
        w.AdvanceTo(18); var copy = World.Restore(w.Serialize()); w.AdvanceTo(19); copy.AdvanceTo(19); Assert.Equal(w.Digest(), copy.Digest()); Assert.Equal(1, Stock(w));
    }

    [Fact]
    public void CapturedRecoveryReadExcludesLaterReplyAndCancellationDisclosesNothing()
    {
        var w = Station(read: 4); Act(w, 0, ActionKind.RecoveryLogin); Act(w, 3, ActionKind.PostLostCard); Act(w, 5, ActionKind.ReadRecoveryInbox); w.AdvanceTo(9);
        Assert.False(KnownCase(w)); Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(w.InspectRecoveryMailbox(new(1))).Stage);
        Act(w, 10, ActionKind.ReadRecoveryInbox); Act(w, 11, ActionKind.Cancel); w.AdvanceTo(14); Assert.False(KnownCase(w));
        Act(w, 15, ActionKind.ReadRecoveryInbox); w.AdvanceTo(19); Assert.True(KnownCase(w));
    }

    [Fact]
    public void CappedKnowledgeCannotAcceptAuthorizationOrConsumeStockWithoutCardFacts()
    {
        var w = Station(knowledge: 2); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); w.AdvanceTo(14);
        Assert.False(KnownCase(w)); Assert.Equal(MessageStage.DeliveredToMailbox, Assert.Single(w.InspectRecoveryMailbox(new(1))).Stage); Assert.Equal(2, Stock(w));
        w = Station(knowledge: 3); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); w.AdvanceTo(14);
        Assert.True(KnownCase(w)); Assert.Equal(FailureReason.KnowledgeCapacity, w.InspectActor(new(1)).LastFailure); Assert.Equal(2, Stock(w));
    }

    [Fact]
    public void CompetingAccountsCannotReserveSameBlankOrPrinter()
    {
        var w = Station(stock: 1); Authorize(w); Authorize(w, 2, 102); Act(w, 9, ActionKind.IssueReplacement); Act(w, 9, ActionKind.IssueReplacement, 2, 102); w.AdvanceTo(14);
        Assert.Single(w.InspectCredentialRecoveryCases(), c => c.IssuedCardId > 0); Assert.Equal(0, Stock(w));
        Assert.Single(w.Trace, t => t.Type == "ActionFailed" && t.Detail == FailureReason.ServiceBusy.ToString()); w.AssertAccounting();
    }

    [Fact]
    public void OldCardRemainsAndFailsConnectedLoginAfterReportAcceptance()
    {
        var w = Station(); Authorize(w); w.Submit(9, new(new(1), ActionKind.Move, Fixtures.B));
        w.Submit(11, new(new(1), ActionKind.PickUp, Fixtures.B, new(101))); w.Submit(12, new(new(1), ActionKind.Login, Fixtures.B, serviceId: 2)); w.AdvanceTo(13);
        Assert.Equal(FailureReason.CredentialRevoked, w.InspectActor(new(1)).LastFailure); Assert.Contains(w.InspectItems(), i => i.Id.Value == 101 && i.Location.Kind == LocationKind.ActorInventory); Assert.Empty(w.InspectSessions());
    }

    [Fact]
    public void CheckpointContinuationsCoverTransportReadIssuancePickupAndNormalAccess()
    {
        for (int tick = 0; tick <= 19; tick++)
        {
            var w = Station(); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); Act(w, 14, ActionKind.PickUp, item: 103); Act(w, 15, ActionKind.Login); Act(w, 16, ActionKind.ReadInbox);
            w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); Assert.Equal(w.Digest(), copy.Digest());
            w.AdvanceTo(22); copy.AdvanceTo(22); Assert.Equal(w.Digest(), copy.Digest());
            Assert.Equal(w.Trace.Where(t => t.Tick > tick).Select(t => t.Type + "/" + t.Tick + "/" + t.ActorId), copy.Trace.Select(t => t.Type + "/" + t.Tick + "/" + t.ActorId));
        }
    }

    [Fact]
    public void SnapshotRejectsForgedCaseAndIssuedCardReferences()
    {
        foreach (var field in new[] { "IssuedCardId", "IssuedCredentialId", "IssueActionId", "OldCredentialId", "RecordVersion" })
        {
            var w = Station(); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); w.AdvanceTo(14);
            var map = typeof(World).GetField("recoveryCases", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            var c = map.GetType().GetProperty("Item")!.GetValue(map, new object[] { 1L })!;
            c.GetType().GetProperty(field)!.SetValue(c, 999L); Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
        }
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("timer")]
    [InlineData("stock")]
    public void SnapshotRejectsIssuanceWithoutItsConservedClaimAndTimer(string mutation)
    {
        var w = Station(); Authorize(w); Act(w, 9, ActionKind.IssueReplacement); w.AdvanceTo(10);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var actors = typeof(World).GetField("actors", flags)!.GetValue(w)!;
        var actor = actors.GetType().GetProperty("Item")!.GetValue(actors, new object[] { new Id<ActorTag>(1) })!;
        var action = actor.GetType().GetField("Action", flags)!.GetValue(actor)!;
        if (mutation == "claim") ((System.Collections.IList)typeof(World).GetField("resourceClaims", flags)!.GetValue(w)!).RemoveAt(0);
        if (mutation == "timer") action.GetType().GetField("Completes", flags)!.SetValue(action, 15L);
        if (mutation == "stock")
        {
            var materials = (System.Collections.IList)action.GetType().GetField("Materials", flags)!.GetValue(action)!;
            materials[0]!.GetType().GetField("Quantity", flags)!.SetValue(materials[0], 2L);
        }
        Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
    }

    [Fact]
    public void SnapshotRejectsCapturedRecoveryReplyBelongingToAnotherAccount()
    {
        var w = Station(read: 4); Authorize(w); Authorize(w, 2, 102); w.AdvanceTo(8);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var actors = typeof(World).GetField("actors", flags)!.GetValue(w)!;
        var actor = actors.GetType().GetProperty("Item")!.GetValue(actors, new object[] { new Id<ActorTag>(1) })!;
        var action = actor.GetType().GetField("Action", flags)!.GetValue(actor)!;
        var captured = (System.Collections.IList)action.GetType().GetField("CapturedMessages", flags)!.GetValue(action)!;
        captured.Add(Assert.Single(w.InspectRecoveryMailbox(new(2))).Id);
        Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
    }
}
