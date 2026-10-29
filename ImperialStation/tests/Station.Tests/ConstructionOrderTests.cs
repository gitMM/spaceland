using System.Reflection;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class ConstructionOrderTests
{
    private static LoadedScenario Fixture(bool shortWork = true, bool autonomous = true, long material = 4, bool split = false, long latency = 5)
    {
        var original = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_construction.json"));
        var data = JsonData.Read<ScenarioData>(original.ScenarioJson, "construction"); var catalog = JsonData.Read<CatalogData>(original.CatalogJson, "catalog");
        data.Assertions.Clear(); data.InitialState.Horizon = 1000; data.InitialState.Information!.Configuration.LatencyTicks = latency;
        if (shortWork) catalog.Recipes[0].Duration = 5;
        if (!autonomous) { data.InitialState.Routines = null; data.InitialState.Actors[0].Scripted = true; }
        if (material == 0) data.InitialState.Items.RemoveAll(i => i.Resource == "material"); else data.InitialState.Items.Single(i => i.Resource == "material").Quantity = material;
        if (split) { data.InitialState.Items.Single(i => i.Resource == "material").Quantity = 2; data.InitialState.Items.Add(new ItemData { Id = 3, Resource = "material", Quantity = 2, LocationKind = LocationKind.TileContainer, Owner = 1 }); }
        return new LoadedScenario(JsonData.Write(data), JsonData.Write(catalog), "construction-orders");
    }
    [Fact]
    public void DefaultDurationFixtureCompletesWithoutScriptedActorsAndChangesBeliefOnlyAtReceipt()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_construction.json"));
        Assert.Empty(loaded.Scenario.ScriptedActors); Assert.DoesNotContain(loaded.Scenario.InitialState.Actors, a => a.Scripted);
        var w = loaded.CreateWorld(trace: true); w.AdvanceTo(18524); Assert.Equal(8, w.UsableHousingBerths); Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths);
        w.AdvanceTo(18525); Assert.Equal(16, w.UsableHousingBerths); Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(8, w.InspectGrowth()!.CommittedPendingBerths);
        w.AdvanceTo(18537); Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths);
        w.AdvanceTo(18538); Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(0, w.InspectGrowth()!.CommittedPendingBerths);
        var c = Assert.Single(w.InspectGrowth()!.Commitments); Assert.True(c.AcceptedReportId > 0); Assert.Equal(18538, c.AcceptedTick);
        Assert.Single(w.InspectConstructionProjects()); Assert.Single(w.InspectResourceTransactions()); Assert.Empty(w.InspectResourceClaims());
        var order = Assert.Single(w.InspectLocalOrders(new(1))); Assert.Equal(LocalOrderStatus.Completed, order.Status); Assert.Equal(1, order.ProjectId);
        var expectation = Assert.Single(w.InspectExpectations()); Assert.True(expectation.UnscaledEstimate >= 18000);
        var construction = Assert.Single(w.Trace, e => e.Type == "ConstructionCompleted"); Assert.Equal(18525, construction.Tick);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerCollectsKnownMaterialStacksAndExecutesFiniteRecipe(bool split)
    {
        var w = Fixture(split: split).CreateWorld(trace: true); w.AdvanceTo(1000);
        Assert.Equal(16, w.UsableHousingBerths); Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(0, w.InspectGrowth()!.CommittedPendingBerths);
        Assert.DoesNotContain(w.InspectItems(), i => i.Resource == "material"); Assert.Single(w.InspectItems(), i => i.Resource == "card");
        Assert.Equal(split ? 2 : 1, w.Trace.Count(e => e.Type == "ActionCompleted" && e.Detail == "PickUp"));
        Assert.Contains(w.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("construct-read-project/v1"));
        w.AssertAccounting(); Assert.Equal(w.Digest(), World.Restore(w.Serialize()).Digest());
    }
    [Fact]
    public void MissingRealStockProducesAnOrdinaryRejectionAndNoPhysicalOrBelievedCompletion()
    {
        var w = Fixture(material: 0).CreateWorld(trace: true); w.AdvanceTo(1000);
        Assert.Equal(8, w.UsableHousingBerths); Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(8, w.InspectGrowth()!.CommittedPendingBerths);
        Assert.Equal(LocalOrderStatus.Rejected, Assert.Single(w.InspectLocalOrders(new(1))).Status);
        Assert.Equal("OrderRejected", Assert.Single(w.InspectWorkAttempts()).AuthorityStatus); Assert.Empty(w.InspectResourceTransactions()); Assert.Single(w.InspectConstructionProjects());
    }
    private static void PostClaim(World w, long tick, string key, string claim = "ReportedComplete", long related = 1, long subjectAttempt = 1)
    {
        var attempt = Assert.Single(w.InspectWorkAttempts());
        w.Submit(tick, new(new(1), ActionKind.Login, default, serviceId: 1));
        w.Submit(tick + 2, new(new(1), ActionKind.PostMessage, default, serviceId: 1, message: new MessageDraft(MessageKind.WorkCompleted, RecipientKind.Authority, default,
            new(SubjectKind.Attempt, subjectAttempt, AttributeKind.WorkStatus), new(FactValueKind.Text, text: claim), tick, attempt.JobId, attempt.Id, related, operationKey: key)));
    }
    private static World ManualReports()
    {
        var w = Fixture(autonomous: false).CreateWorld(trace: true); w.AdvanceTo(0);
        w.ScheduleWorkAssignment(1, new AssignmentSeed("manual-dispatch", 1, new(1), 1000)); w.AdvanceTo(1); return w;
    }
    [Fact]
    public void FalseCompletionCanDeceiveRecordsButCannotCreateTilesAndProjectDedupIgnoresNewOperationKeys()
    {
        var w = ManualReports(); var attempt = Assert.Single(w.InspectWorkAttempts());
        PostClaim(w, 2, "false/one", related: attempt.OrderMessageId); w.AdvanceTo(10);
        Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths); w.AdvanceTo(11);
        Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(0, w.InspectGrowth()!.CommittedPendingBerths); Assert.Equal(8, w.UsableHousingBerths);
        Assert.Equal(ConstructionStatus.Reserved, Assert.Single(w.InspectConstructionProjects()).Status); Assert.Empty(w.InspectResourceTransactions());
        PostClaim(w, 12, "false/two", related: attempt.OrderMessageId); w.AdvanceTo(30);
        Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Single(w.Trace, e => e.Type == "ConstructionCapacityAccepted"); Assert.Single(w.Trace, e => e.Type == "ConstructionReportDuplicate");
        Assert.Equal(4, Assert.Single(w.InspectItems(), i => i.Resource == "material").Quantity); Assert.Empty(w.InspectGrowth()!.AvailableMaterials);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Theory]
    [InlineData("Wrong", 1, 1)]
    [InlineData("ReportedComplete", 999, 1)]
    [InlineData("ReportedComplete", 1, 999)]
    public void MismatchedConstructionClaimIsRejected(string claim, long related, long subjectAttempt)
    {
        var w = ManualReports(); PostClaim(w, 2, "invalid", claim, related, subjectAttempt); w.AdvanceTo(20);
        Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(0, Assert.Single(w.InspectGrowth()!.Commitments).AcceptedReportId);
        Assert.Contains(w.Trace, e => e.Type == "WorkReportRejected" && e.Detail == "InvalidConstructionClaim");
    }
    [Fact]
    public void EveryProtocolBoundaryRestoresIdenticallyIncludingPendingReportDelivery()
    {
        var fixture = Fixture(); var direct = fixture.CreateWorld(trace: true); direct.AdvanceTo(1000);
        var boundaries = direct.Trace.Where(e => e.Type is "AuthorityOrderPosted" or "MessageRead" or "OrderAcknowledged" or "ActionStarted" or "ConstructionCompleted" or "ConstructionCapacityAccepted")
            .SelectMany(e => new[] { e.Tick - 1, e.Tick, e.Tick + 1 }).Where(t => t >= 0 && t <= 1000).Append(0).Distinct().Order().ToArray();
        foreach (long tick in boundaries)
        {
            var w = fixture.CreateWorld(trace: true); w.AdvanceTo(tick); var restored = World.Restore(w.Serialize(), true); Assert.Equal(w.Serialize(), restored.Serialize());
            restored.AdvanceTo(1000); Assert.Equal(direct.Digest(), restored.Digest()); Assert.Equal(direct.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        }
        var quiet = fixture.CreateWorld(); for (int tick = 0; tick <= 1000; tick++) { quiet.AdvanceTo(tick); _ = quiet.InspectGrowth(); _ = quiet.InspectConstructionProjects(); }
        Assert.Equal(direct.Digest(), quiet.Digest());
    }
    [Fact]
    public void SnapshotRejectsForgedAcceptedCapacityEvidence()
    {
        var w = Fixture().CreateWorld(); w.AdvanceTo(1000);
        var map = typeof(World).GetField("growthCommitments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
        var c = map.GetType().GetProperty("Item")!.GetValue(map, new object[] { 1L })!;
        c.GetType().GetProperty("AcceptedReportId")!.SetValue(c, 999L); Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
    }
    [Fact]
    public void RunnerVerifiesCompleteAutonomousResponseChainAndReportHasLocalCompletionCause()
    {
        var fixture = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_construction.json"));
        var run = Runs.Execute(fixture, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        var chain = Assert.Single(run.Result.ResponseChains); Assert.True(chain.Passed); Assert.Equal(7, chain.TransitionEventIds.Count); Assert.True(chain.ActionsChecked > 0);
        var local = Assert.Single(run.World.InspectLocalOrders(new(1)));
        var completion = Assert.Single(run.World.Trace, e => e.Id == local.CompletionCauseId);
        Assert.Equal("ConstructionGoalCompleted", completion.Type);
        Assert.Equal("ConstructionCompleted", Assert.Single(run.World.Trace, e => e.Id == completion.CauseId).Type);
        Assert.Contains(run.World.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("report-due/v1") && e.CauseId == completion.Id);
    }
    [Fact]
    public void ReceivedCompletionExtendsBelievedLayoutForTheNextConstructionProject()
    {
        var fixture = Fixture(material: 8); var data = JsonData.Read<ScenarioData>(fixture.ScenarioJson, "growth-chain");
        data.InitialState.Growth!.BelievedCompletedBerths = 0; data.InitialState.Growth.KnownMaterials[0].Quantity = 8;
        var w = new LoadedScenario(JsonData.Write(data), fixture.CatalogJson, "growth-chain").CreateWorld(trace: true); w.AdvanceTo(3000);
        Assert.Equal(new[] { new Coord(2, 0, 0), new Coord(3, 0, 0) }, w.InspectConstructionProjects().Select(p => p.Site));
        Assert.All(w.InspectConstructionProjects(), p => Assert.Equal(ConstructionStatus.Completed, p.Status));
        Assert.Equal(24, w.UsableHousingBerths); Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(0, w.InspectGrowth()!.CommittedPendingBerths);
        Assert.All(w.InspectGrowth()!.Commitments, c => Assert.True(c.AcceptedReportId > 0)); Assert.Empty(w.InspectGrowth()!.AvailableMaterials);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
}
