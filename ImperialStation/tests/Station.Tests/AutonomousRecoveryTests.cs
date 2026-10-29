using Station.Contracts;
using Station.Core;
using Station.Runner;
using System.Reflection;
using Xunit;

namespace Station.Tests;

public sealed class AutonomousRecoveryTests
{
    private static Coord At(int x) => new(x, 0, 0);
    internal static World Station(bool loss = true, int? knownCard = null, long stock = 2, bool desk = true, bool capability = true, ulong seed = 42, PlannerConfig? planner = null, long travelTicks = 2, bool secondDesk = false)
    {
        var items = new List<Item> { new(new(1), "card", 1, new(LocationKind.ActorInventory, 1)), new(new(2), "tool", 1, new(LocationKind.ActorInventory, 1)), new(new(3), "part", 1, new(LocationKind.ActorInventory, 1)) };
        if (stock > 0) items.Add(new(new(4), "blank", stock, new(LocationKind.TileContainer, 5)));
        if (secondDesk) items.Add(new(new(5), "blank", 1, new(LocationKind.TileContainer, 6)));
        var work = new WorkSeed(new[] { new ResourceDefinition("card"), new ResourceDefinition("blank"), new ResourceDefinition("tool"), new ResourceDefinition("part", "slot") },
            new[] { new RecipeDefinition(1, "CredentialIssuance", new[] { new MaterialAmount("blank", 1) }, new[] { new MaterialAmount("card", 1) }, 5) },
            new[] { new WorkstationDefinition(1, At(4), "CredentialIssuance", new WorkAccess(new[] { "worker" })) }.Concat(secondDesk ? new[] { new WorkstationDefinition(2, At(5), "CredentialIssuance", new WorkAccess(new[] { "worker" })) } : Array.Empty<WorkstationDefinition>()),
            new[] { new MachineDefinition(1, At(3), "slot", "part", new WorkAccess(new[] { "worker" }, "tool", requiresCredential: true), installTicks: 3, inspectTicks: 2) });
        var facts = new List<FactSeed> { new(new(1), new(SubjectKind.Machine, 1, AttributeKind.RequiredResource), new(FactValueKind.Text, text: "part")) };
        if (knownCard.HasValue) facts.Add(new(new(1), new(SubjectKind.Item, 1, AttributeKind.Location), FactValue.At(At(knownCard.Value))));
        var info = new InformationSeed(new InformationConfig(loginTicks: 1, readBaseTicks: 1, readPerMessageTicks: 0, readMaximumTicks: 1, postTicks: 1, latencyTicks: 1, sessionTicks: 500),
            new[] { new TerminalSeed(1, At(0), new[] { "worker" }), new TerminalSeed(2, At(4), new[] { "worker" }, capabilities: TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.CredentialRecovery) }.Concat(secondDesk ? new[] { new TerminalSeed(3, At(5), new[] { "worker" }, capabilities: TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.CredentialRecovery) } : Array.Empty<TerminalSeed>()),
            new[] { new CredentialSeed(1, new(1), new(1), new(1)) }, facts: facts, planner: planner,
            recovery: new CredentialRecoverySeed(new[] { new RecoveryCapabilitySeed(1, 1, capability) }, 3, desk ? new[] { new RecoveryDeskSeed(2, 1, 1) }.Concat(secondDesk ? new[] { new RecoveryDeskSeed(3, 2, 1) } : Array.Empty<RecoveryDeskSeed>()) : null));
        var jobs = new JobSystemSeed(new[] { new JobSeed(1, 1) }, new[] { new AssignmentSeed("initial", 1, new(1), 1000) }, new[] { new CheckInSeed(new(1), 0, 500) });
        var w = new World("autonomous-recovery", seed, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            Enumerable.Range(0, Math.Max(secondDesk ? 5 : 4, knownCard ?? 4) + 1).Select(i => new Tile(new(i + 1), "Industrial", At(i), traversalOverride: travelTicks)),
            new[] { new ActorSeed(new(1), "Worker", "worker", At(0)) }, items, true, info, work, jobs);
        if (loss) w.ScheduleCardLoss(5, new(1), new(1));
        return w;
    }

    [Fact]
    public void T18_UnscriptedRecoveryRetainsAcceptedWorkAndRestoresThroughNormalRead()
    {
        var w = Station(); w.AdvanceTo(5);
        Assert.True(w.InspectActor(new(1)).InTransit); Assert.False(Assert.Single(w.InspectRecoveryAgents()).Pending); Assert.Empty(w.InspectCredentialRecoveryCases());
        Assert.Equal(5, Assert.Single(w.InspectWorkAttempts()).AcknowledgmentReceivedTick); Assert.Equal(LocalOrderStatus.Working, Assert.Single(w.InspectLocalOrders(new(1))).Status);
        Assert.DoesNotContain(w.Trace, t => t.Type == "CredentialRevoked"); Assert.Contains(new Id<ItemTag>(3), w.InspectActor(new(1)).Inventory);
        w.AdvanceTo(100);
        Assert.False(Assert.Single(w.InspectRecoveryAgents()).Pending); Assert.Equal(1, Assert.Single(w.InspectRecoveryAgents()).Completed);
        Assert.True(Assert.Single(w.InspectMachines()).Operational); Assert.Equal("WorkCompleted", Assert.Single(w.InspectJobs()).AuthorityStatus);
        var c = Assert.Single(w.InspectCredentialRecoveryCases()); Assert.True(c.IssuedCardId > 0); Assert.Equal(1, w.InspectItems().Where(i => i.Resource == "blank").Sum(i => i.Quantity));
        Assert.Equal(LocationKind.TileContainer, Assert.Single(w.InspectItems(), i => i.Id.Value == 1).Location.Kind);
        Assert.Equal(1, Assert.Single(w.InspectItems(), i => i.Id.Value == 1).Location.Owner); Assert.Empty(w.InspectResourceClaims());
        var restored = Assert.Single(w.Trace, t => t.Type == "CredentialRecoveryCompleted");
        var install = Assert.Single(w.Trace, t => t.Type == "ActionStarted" && t.Detail == ActionKind.Install.ToString()); Assert.True(install.Tick > restored.Tick);
        Assert.All(w.Trace.Where(t => t.Type == "ActionRequested"), t => Assert.True(t.CauseId > 0));
        Assert.Contains(w.Trace, t => t.Type == "PlanSelected" && t.Detail.Contains("restore-credential/v1")); w.AssertAccounting();
    }

    [Fact]
    public void T18_WaitingCancellationIsReadBeforeRetainedWorkCanResume()
    {
        var w = Station(); w.ScheduleWorkCancellation(14, 1, "cancel-during-recovery"); w.AdvanceTo(100);
        Assert.Equal(1, Assert.Single(w.InspectRecoveryAgents()).Completed); Assert.Equal(LocalOrderStatus.Cancelled, Assert.Single(w.InspectLocalOrders(new(1))).Status);
        Assert.False(Assert.Single(w.InspectMachines()).Operational); Assert.Contains(new Id<ItemTag>(3), w.InspectActor(new(1)).Inventory);
        Assert.DoesNotContain(w.Trace, t => t.Type == "ActionStarted" && t.Detail == "Install");
        Assert.Equal(MessageStage.Read, Assert.Single(w.InspectMessages(), m => m.Draft.Kind == MessageKind.OrderCancelled).Stage);
    }

    [Fact]
    public void T18_ValidCardDoesNotCreateRecoveryTrip()
    {
        var w = Station(loss: false); w.AdvanceTo(100); Assert.Equal(0, Assert.Single(w.InspectRecoveryAgents()).Completed); Assert.Empty(w.InspectCredentialRecoveryCases());
        Assert.True(Assert.Single(w.InspectMachines()).Operational); Assert.DoesNotContain(w.Trace, t => t.Type == "CredentialRecoveryNeeded");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(20, false)]
    public void T18_KnownCardRetrievalCompetesOnActualEstimatedCost(int knownCard, bool retrieve)
    {
        var w = Station(knownCard: knownCard); w.AdvanceTo(150);
        Assert.Equal(1, Assert.Single(w.InspectRecoveryAgents()).Completed); Assert.True(Assert.Single(w.InspectMachines()).Operational);
        var plan = w.Trace.First(t => t.Type == "PlanSelected" && t.Detail.StartsWith("restore-credential/v1"));
        Assert.Equal(retrieve, plan.Detail.Contains("RetrieveKnownCard"));
        if (retrieve) { Assert.Empty(w.InspectCredentialRecoveryCases()); Assert.Contains(new Id<ItemTag>(1), w.InspectActor(new(1)).Inventory); }
        else Assert.Single(w.InspectCredentialRecoveryCases());
    }

    [Fact]
    public void StaleCardLocationFailsLocallyThenChoosesReplacement()
    {
        var w = Station(knownCard: 6); w.AdvanceTo(180); Assert.Equal(1, Assert.Single(w.InspectRecoveryAgents()).Completed);
        Assert.Contains(w.Trace, t => t.Type == "ActionFailed" && t.Detail == FailureReason.MissingItem.ToString()); Assert.Single(w.InspectCredentialRecoveryCases());
    }

    [Theory]
    [InlineData("stock")]
    [InlineData("service")]
    [InlineData("capability")]
    public void T18_MissingPrerequisiteRetainsWorkAndExplainsBackoff(string missing)
    {
        var w = Station(stock: missing == "stock" ? 0 : 2, desk: missing != "service", capability: missing != "capability"); w.AdvanceTo(150);
        var agent = Assert.Single(w.InspectRecoveryAgents()); Assert.True(agent.Pending); Assert.NotEmpty(agent.Reason); Assert.True(agent.NextDecision > w.LastProcessedTick);
        Assert.Contains(new Id<ItemTag>(3), w.InspectActor(new(1)).Inventory); Assert.False(Assert.Single(w.InspectMachines()).Operational);
        Assert.DoesNotContain(w.InspectLocalOrders(new(1)), o => o.Status is LocalOrderStatus.Rejected or LocalOrderStatus.Cancelled); Assert.Empty(w.InspectResourceTransactions());
        Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }

    [Fact]
    public void TravelBetweenUnavailableRecoveryDesksPreservesBackoffAndEventuallyRecovers()
    {
        var w = Station(secondDesk: true, planner: new PlannerConfig(initialBackoff: 8, maximumBackoff: 128));
        foreach (long terminal in new[] { 2L, 3L })
        {
            w.ScheduleTerminalChange(0, terminal, false, false);
            w.ScheduleTerminalChange(300, terminal, true, true);
        }
        long firstFailure = -1, firstBackoff = 0;
        bool sawSecondFailure = false;
        for (long tick = 0; tick < 300; tick++)
        {
            w.AdvanceTo(tick);
            if (!w.Trace.Any(e => e.Tick == tick && e.Type == "ActionFailed" && e.Detail == "RequiredServiceNotPresent")) continue;
            var recovery = Assert.Single(w.InspectRecoveryAgents());
            if (firstFailure < 0) { firstFailure = tick; firstBackoff = recovery.Backoff; continue; }
            Assert.Contains(w.Trace, e => e.Tick > firstFailure && e.Tick < tick && e.Type == "ActionRequested" && e.Detail == "Move");
            Assert.True(recovery.Backoff > firstBackoff, "Travel must not erase a failed recovery service's backoff.");
            sawSecondFailure = true; break;
        }
        Assert.True(sawSecondFailure);
        var restored = World.Restore(w.Serialize(), true);
        w.ClearRecordedTrace(); restored.ClearRecordedTrace();
        w.AdvanceTo(800); restored.AdvanceTo(800);
        Assert.Equal(1, Assert.Single(w.InspectRecoveryAgents()).Completed);
        Assert.True(Assert.Single(w.InspectMachines()).Operational);
        w.AssertAccounting(); restored.AssertAccounting();
        Assert.Equal(w.Digest(), restored.Digest());
        Assert.Equal(w.Trace.Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
    }

    [Fact]
    public void EveryRecoveryBoundaryRestoresAndReplaysIdentically()
    {
        for (int tick = 0; tick <= 50; tick++)
        {
            var w = Station(); w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); Assert.Equal(w.Digest(), copy.Digest());
            w.AdvanceTo(100); copy.AdvanceTo(100); Assert.Equal(w.Digest(), copy.Digest());
            Assert.Equal(w.Trace.Where(t => t.Tick > tick).Select(t => t.Tick + "/" + t.Type + "/" + t.ActorId), copy.Trace.Select(t => t.Tick + "/" + t.Type + "/" + t.ActorId));
        }
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 3, 3)]
    [InlineData(99, 1, 3)]
    [InlineData(200, 3, 1)]
    public void GeneratedSeedTravelAndStockVariantsRecoverWithoutScripts(int seed, long travelTicks, long stock)
    {
        var w = Station(seed: (ulong)seed, travelTicks: travelTicks, stock: stock); w.AdvanceTo(100);
        Assert.Equal(1, Assert.Single(w.InspectRecoveryAgents()).Completed); Assert.True(Assert.Single(w.InspectMachines()).Operational);
        Assert.Equal(stock - 1, w.InspectItems().Where(i => i.Resource == "blank").Sum(i => i.Quantity));
        Assert.All(w.Trace.Where(t => t.Type == "ActionRequested"), t => Assert.True(t.CauseId > 0)); w.AssertAccounting();
    }

    [Fact]
    public void MissingCardAtWorkCompletionReleasesClaimsWithoutConsumingPart()
    {
        var w = Station(loss: false); w.ScheduleCardLoss(11, new(1), new(1)); w.AdvanceTo(12);
        Assert.Equal(ActionKind.Install, w.InspectActor(new(1)).Action); Assert.Contains(new Id<ItemTag>(3), w.InspectActor(new(1)).Inventory);
        w.AdvanceTo(13); Assert.False(Assert.Single(w.InspectMachines()).Operational); Assert.Empty(w.InspectResourceClaims());
        Assert.Contains(w.Trace, t => t.Type == "ActionFailed" && t.Detail == FailureReason.MissingCredential.ToString());
        Assert.Contains(new Id<ItemTag>(3), w.InspectActor(new(1)).Inventory); w.AdvanceTo(100);
        Assert.Equal(1, Assert.Single(w.InspectRecoveryAgents()).Completed); Assert.True(Assert.Single(w.InspectMachines()).Operational); w.AssertAccounting();
    }

    [Fact]
    public void ExhaustedPlannerBudgetExplainsWaitAndNeverInventsRecoveryActions()
    {
        var w = Station(planner: new PlannerConfig(nodeBudget: 0)); w.AdvanceTo(100);
        var agent = Assert.Single(w.InspectRecoveryAgents()); Assert.True(agent.Pending); Assert.Equal("PlanBudgetExhausted", agent.Reason);
        Assert.Equal(PlanOutcome.BudgetExhausted, agent.LastOutcome); Assert.Equal(0, w.ActionsCompleted); Assert.Empty(w.InspectCredentialRecoveryCases());
    }

    [Fact]
    public void SnapshotRejectsForgedPrivateRecoveryProgress()
    {
        foreach (var field in new[] { "KnownCaseId", "IssuedCardId", "ActiveActionId", "NextDecision" })
        {
            var w = Station(); w.AdvanceTo(15);
            var map = typeof(World).GetField("recoveryAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            var agent = map.GetType().GetProperty("Item")!.GetValue(map, new object[] { 1L })!;
            agent.GetType().GetProperty(field)!.SetValue(agent, 999L); Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
        }
    }

    [Fact]
    public void RunnerGoldenRecoveryHasNoScriptedActorCommands()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_recovery.json"));
        Assert.Empty(loaded.Scenario.ScriptedActors); Assert.All(loaded.Scenario.InitialState.Actors, a => Assert.False(a.Scripted));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(13, loaded.Scenario.Assertions.Count);
        Assert.Equal(1, Assert.Single(run.World.InspectRecoveryAgents()).Completed);
    }
}
