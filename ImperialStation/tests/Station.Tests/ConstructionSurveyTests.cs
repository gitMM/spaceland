using System.Reflection;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class ConstructionSurveyTests
{
    private static World Setup(bool trace = true, bool credit = true)
    {
        var original = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_construction.json"));
        var data = JsonData.Read<ScenarioData>(original.ScenarioJson, "survey"); var catalog = JsonData.Read<CatalogData>(original.CatalogJson, "catalog");
        data.Assertions.Clear(); data.InitialState.Routines = null;
        foreach (var actor in data.InitialState.Actors) actor.Scripted = true;
        foreach (var tile in catalog.Tiles) tile.TraversalTicks = 2;
        catalog.Recipes[0].Duration = 5; data.InitialState.Configuration.ItemActionTicks = 2; data.InitialState.Information!.Configuration.InspectTicks = 4;
        var w = new LoadedScenario(JsonData.Write(data), JsonData.Write(catalog), "survey").CreateWorld(trace: trace);
        w.AdvanceTo(0); w.ScheduleWorkAssignment(1, new AssignmentSeed("survey-setup", 1, new(1), 1000)); w.AdvanceTo(1);
        if (credit) { CompleteClaim(w, 2, "original"); w.AdvanceTo(11); Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths); }
        return w;
    }
    private static void CompleteClaim(World w, long tick, string key)
    {
        var a = Assert.Single(w.InspectWorkAttempts());
        Post(w, tick, new(MessageKind.WorkCompleted, RecipientKind.Authority, default, new(SubjectKind.Attempt, a.Id, AttributeKind.WorkStatus),
            new(FactValueKind.Text, text: "ReportedComplete"), tick, a.JobId, a.Id, a.OrderMessageId, operationKey: key));
    }
    private static void Post(World w, long tick, MessageDraft draft)
    {
        w.Submit(tick, new(new(1), ActionKind.Login, default, serviceId: 1));
        w.Submit(tick + 2, new(new(1), ActionKind.PostMessage, default, serviceId: 1, message: draft));
    }
    private static MessageDraft Claim(long count, long observed, string key = "survey-claim", long project = 1) => new(MessageKind.ConstructionSurvey, RecipientKind.Authority, default,
        new(SubjectKind.ConstructionProject, project, AttributeKind.UsableBerths), FactValue.Integer(count), observed, operationKey: key);
    private static World Inspection(bool built, bool trace = true)
    {
        var w = Setup(trace);
        if (built) w.Submit(20, new(new(1), ActionKind.PickUp, default, new(1)));
        w.Submit(23, new(new(1), ActionKind.Move, new(1, 0, 0)));
        if (built)
        {
            w.Submit(26, new(new(1), ActionKind.Construct, new(1, 0, 0), serviceId: 1)); w.AdvanceTo(31);
            var p = Assert.Single(w.InspectConstructionProjects()); Assert.Equal(ConstructionStatus.Completed, p.Status);
            foreach (long id in p.BerthIds.Take(2)) w.ScheduleNeedServiceChange(40, id, false);
        }
        w.Submit(41, new(new(1), ActionKind.InspectConstruction, new(1, 0, 0), serviceId: 1)); return w;
    }
    [Theory]
    [InlineData(false, 0, 8)]
    [InlineData(true, 6, 14)]
    public void FiniteLocalInspectionReturnsThroughOrdinaryTerminalBeforeAuthorityChanges(bool built, long usable, long total)
    {
        var w = Inspection(built); w.AdvanceTo(44);
        Assert.Empty(w.InspectConstructionSurveys()); Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(total, w.UsableHousingBerths);
        w.AdvanceTo(45); var local = Assert.Single(w.InspectConstructionSurveys()); Assert.Equal(usable, local.UsableBerths);
        Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths);
        Assert.Contains(w.ActorView(new(1)).Knowledge, f => f.Key.Equals(new FactKey(SubjectKind.ConstructionProject, 1, AttributeKind.UsableBerths)) && f.Value.Number == usable);
        w.AdvanceTo(100); var correction = Assert.Single(w.Trace, e => e.Type == "ConstructionCapacityCorrected"); Assert.True(correction.Tick > 45);
        Assert.Equal(total, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(total, w.UsableHousingBerths);
        Assert.Equal(0, w.InspectGrowth()!.CommittedPendingBerths); Assert.Empty(w.InspectGrowth()!.AvailableMaterials);
        var c = Assert.Single(w.InspectGrowth()!.Commitments); Assert.Equal(usable, c.BelievedBerths); Assert.NotEqual(c.AcceptedReportId, c.CapacityEvidenceMessageId);
        Assert.Contains(w.Trace, e => e.Type == "PlanSelected" && e.Detail.StartsWith("report-due/v1") && e.CauseId == local.CauseId);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Fact]
    public void CorrectedProjectCannotBeRecreditedByNewCompletionKeysAndSurveyDuplicatesCannotRollback()
    {
        var w = Setup(); Post(w, 20, Claim(0, 20)); w.AdvanceTo(29); Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths);
        CompleteClaim(w, 30, "late-original"); w.AdvanceTo(39); Assert.Equal(8, w.InspectGrowth()!.BelievedCompletedBerths);
        Post(w, 40, Claim(3, 40, "later")); w.AdvanceTo(49); Assert.Equal(11, w.InspectGrowth()!.BelievedCompletedBerths);
        Post(w, 50, Claim(0, 20)); w.AdvanceTo(59); Assert.Equal(11, w.InspectGrowth()!.BelievedCompletedBerths);
        Post(w, 60, Claim(8, 20)); w.AdvanceTo(69); Assert.Equal(11, w.InspectGrowth()!.BelievedCompletedBerths);
        Assert.Single(w.Trace, e => e.Type == "ConstructionSurveyDuplicate"); Assert.Single(w.Trace, e => e.Type == "ConstructionSurveyRejected");
        Assert.Equal(8, w.UsableHousingBerths); Assert.Equal(0, w.InspectGrowth()!.CommittedPendingBerths); Assert.Empty(w.InspectGrowth()!.AvailableMaterials);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Theory]
    [InlineData(9, 1, true, "InvalidCapacityOrObservationTime")]
    [InlineData(0, 999, true, "UnknownProject")]
    [InlineData(0, 1, false, "NotYetCredited")]
    public void InvalidSurveyDoesNotChangeCapacity(long count, long project, bool credit, string reason)
    {
        var w = Setup(credit: credit); Post(w, 20, Claim(count, 20, project: project)); w.AdvanceTo(40);
        Assert.Equal(credit ? 16 : 8, w.InspectGrowth()!.BelievedCompletedBerths);
        Assert.Contains(w.Trace, e => e.Type == "ConstructionSurveyRejected" && e.Detail == reason);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Fact]
    public void LaterReceiptWinsEvenWithOlderObservationAndCanBeFalseWithoutPhysicalConsequences()
    {
        var w = Setup(); Post(w, 20, Claim(0, 20)); w.AdvanceTo(29);
        Post(w, 30, Claim(5, 10, "older-observation")); w.AdvanceTo(39);
        Assert.Equal(13, w.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(8, w.UsableHousingBerths);
        Assert.Equal(ConstructionStatus.Reserved, Assert.Single(w.InspectConstructionProjects()).Status);
        Assert.Empty(w.InspectResourceTransactions()); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Fact]
    public void RemoteInspectionFailsWithoutLearningOrReporting()
    {
        var w = Setup(); w.Submit(20, new(new(1), ActionKind.InspectConstruction, default, serviceId: 1)); w.AdvanceTo(40);
        Assert.Equal(FailureReason.NotAtRequiredTile, w.InspectActor(new(1)).LastFailure); Assert.Empty(w.InspectConstructionSurveys());
        Assert.DoesNotContain(w.Trace, e => e.Type == "ConstructionCapacityCorrected");
    }
    [Fact]
    public void CancelledInspectionHasNoObservationOrReport()
    {
        var w = Inspection(false); w.Submit(43, new(new(1), ActionKind.Cancel, new(1, 0, 0))); w.AdvanceTo(100);
        Assert.Empty(w.InspectConstructionSurveys()); Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths);
        Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.Kind == MessageKind.ConstructionSurvey);
        Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
    [Fact]
    public void InspectionMeasuresPhysicalAvailabilityAtCompletion()
    {
        var w = Inspection(true); long third = Assert.Single(w.InspectConstructionProjects()).BerthIds[2];
        w.ScheduleNeedServiceChange(44, third, false); w.AdvanceTo(100);
        Assert.Equal(5, Assert.Single(w.InspectConstructionSurveys()).UsableBerths); Assert.Equal(13, w.UsableHousingBerths);
        Assert.Equal(13, w.InspectGrowth()!.BelievedCompletedBerths);
    }
    [Fact]
    public void InvalidNegativeCapacityCannotBePosted()
    {
        var w = Setup(); Post(w, 20, Claim(-1, 20)); w.AdvanceTo(40);
        Assert.Equal(FailureReason.InvalidMessage, w.InspectActor(new(1)).LastFailure);
        Assert.DoesNotContain(w.InspectMessages(), m => m.Draft.Kind == MessageKind.ConstructionSurvey);
        Assert.Equal(16, w.InspectGrowth()!.BelievedCompletedBerths);
    }
    [Fact]
    public void RunnableScenarioProvesAutonomousReturnAfterControlledInspectionTrigger()
    {
        var fixture = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "construction_capacity_correction.json"));
        var run = Runs.Execute(fixture, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        var proof = Assert.Single(run.Result.ResponseChains, c => c.Role == "capacity-survey-return");
        Assert.True(proof.Passed); Assert.Equal(3, proof.TransitionEventIds.Count); Assert.True(proof.ActionsChecked > 0);
        var changes = run.World.Trace.Where(e => e.Type == "ConstructionCapacityCorrected").ToArray(); Assert.Equal(2, changes.Length);
        Assert.Equal(19009, changes[0].Tick); Assert.True(changes[1].Tick > 19200);
        Assert.Equal(16, run.World.UsableHousingBerths); Assert.Equal(16, run.World.InspectGrowth()!.BelievedCompletedBerths);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectionReportAndReceiptBoundariesRestoreAndIgnoreTraceAndGetterFrequency(bool built)
    {
        var direct = Inspection(built); direct.AdvanceTo(100);
        var boundaries = direct.Trace.Where(e => e.Tick >= 40).SelectMany(e => new[] { e.Tick - 1, e.Tick }).Distinct().Order();
        foreach (long tick in boundaries)
        {
            var w = Inspection(built); w.AdvanceTo(tick); var restored = World.Restore(w.Serialize(), true);
            Assert.Equal(w.Serialize(), restored.Serialize()); restored.AdvanceTo(100); Assert.Equal(direct.Digest(), restored.Digest());
            Assert.Equal(direct.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
        }
        var quiet = Inspection(built, false);
        for (long t = quiet.LastProcessedTick + 1; t <= 100; t++) { quiet.AdvanceTo(t); _ = quiet.InspectConstructionSurveys(); _ = quiet.InspectGrowth(); }
        Assert.Equal(direct.Digest(), quiet.Digest());
    }
    [Theory]
    [InlineData("CapacityEvidenceMessageId", 999)]
    [InlineData("BelievedBerths", 8)]
    public void CheckpointRejectsCapacityNotSupportedByLastEvidence(string field, long value)
    {
        var w = Inspection(false); w.AdvanceTo(100);
        var map = typeof(World).GetField("growthCommitments", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        var c = map.GetType().GetProperty("Item")!.GetValue(map, new object[] { 1L })!; c.GetType().GetProperty(field)!.SetValue(c, value);
        Assert.Throws<ArgumentException>(() => World.Restore(w.Serialize()));
    }
}
