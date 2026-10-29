using Station.Contracts;
using Station.Core;
using Station.Runner;
using Station.Scenarios;
using Xunit;

namespace Station.Tests;

public sealed class ConstructionGeneratorTests
{
    private static GeneratedConstruction Generate(ulong seed, ConstructionVariant variant) => ConstructionGenerator.Generate(
        File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "autonomous_construction.json")),
        File.ReadAllText(Path.Combine(Fixtures.Root, "content", "autonomous-construction.json")), seed, variant);
    [Theory]
    [InlineData(ConstructionVariant.Expansion)]
    [InlineData(ConstructionVariant.DelayedReport)]
    [InlineData(ConstructionVariant.NeedInterruption)]
    [InlineData(ConstructionVariant.NoStock)]
    [InlineData(ConstructionVariant.NoQualifiedWorker)]
    [InlineData(ConstructionVariant.NoLegalSite)]
    [InlineData(ConstructionVariant.VacantCapacity)]
    public void TwentyGeneratedCasesPreserveDeclaredPropertiesAndCheckpointContinuation(ConstructionVariant variant)
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var g = Generate(seed, variant); var run = Runs.Execute(g.Scenario, seed, true); var w = run.World;
            Assert.True(run.Result.Status == "completed", variant + "/" + seed + ": " + string.Join("; ", run.Result.AssertionFailures));
            Assert.Empty(g.Scenario.Scenario.ScriptedActors); Assert.DoesNotContain(g.Scenario.Scenario.InitialState.Actors, a => a.Scripted);
            Assert.True(Assert.Single(run.Result.ResponseChains).Passed); Assert.Empty(w.InspectResourceClaims()); w.AssertAccounting();
            Assert.DoesNotContain(w.Trace, e => e.Origin == ActionOrigin.ExternalCommand);
            var boundaries = new List<long> { 0, 1, 100, 1000, 1999 };
            if (variant is ConstructionVariant.Expansion or ConstructionVariant.DelayedReport or ConstructionVariant.NeedInterruption)
            {
                var p = Assert.Single(w.InspectConstructionProjects()); var c = Assert.Single(w.InspectGrowth()!.Commitments);
                Assert.Equal(g.WorkerId, p.CompletedActorId); Assert.Equal(g.Site, p.Site); Assert.True(c.AcceptedTick >= p.CompletedTick + g.Latency);
                Assert.Equal(PathOutcome.PathFound, w.Topology.FindPath(g.Depot, g.Site).Outcome); Assert.Equal(-4, w.InspectResourceTransactions().Where(t => t.Resource == g.MaterialResource).Sum(t => t.Delta));
                var before = g.Scenario.CreateWorld(); before.AdvanceTo(p.CompletedTick - 1);
                Assert.False(before.Topology.Contains(g.Site)); Assert.Equal(8, before.UsableHousingBerths); Assert.Equal(4, before.InspectItems().Where(i => i.Resource == g.MaterialResource).Sum(i => i.Quantity));
                before.AdvanceTo(p.CompletedTick); Assert.Equal(16, before.UsableHousingBerths); Assert.Equal(8, before.InspectGrowth()!.BelievedCompletedBerths); Assert.Equal(8, before.InspectGrowth()!.CommittedPendingBerths);
                before.AdvanceTo(c.AcceptedTick - 1); Assert.Single(before.InspectConstructionProjects()); Assert.Equal(8, before.InspectGrowth()!.BelievedCompletedBerths);
                boundaries.AddRange(new[] { p.CompletedTick - 1, p.CompletedTick, c.AcceptedTick - 1, c.AcceptedTick });
                if (variant == ConstructionVariant.NeedInterruption)
                {
                    Assert.True(w.CriticalNeedInterruptions > 0); Assert.True(w.MealsConsumed > 0);
                    var starts = w.Trace.Where(e => e.Type == "ActionStarted" && e.Detail == "Construct" && e.ActorId == g.WorkerId).ToArray();
                    Assert.True(starts.Length >= 2, "Construction must restart after relief."); Assert.Equal(g.WorkTicks, p.CompletedTick - starts.Last().Tick);
                    var cancelled = w.Trace.First(e => e.Type == "ActionCancelled" && e.ActionId == starts.First().ActionId);
                    var interruption = g.Scenario.CreateWorld(); interruption.AdvanceTo(cancelled.Tick);
                    Assert.Equal(ConstructionStatus.Reserved, Assert.Single(interruption.InspectConstructionProjects()).Status); Assert.Equal(8, interruption.UsableHousingBerths);
                    Assert.Equal(4, interruption.InspectItems().Where(i => i.Resource == g.MaterialResource).Sum(i => i.Quantity)); Assert.Empty(interruption.InspectResourceClaims());
                    boundaries.Add(cancelled.Tick);
                }
            }
            else if (variant == ConstructionVariant.NoStock)
            {
                Assert.Equal("OrderRejected", Assert.Single(w.InspectWorkAttempts()).AuthorityStatus);
                Assert.Equal(0, Assert.Single(w.InspectGrowth()!.Commitments).AcceptedReportId);
            }
            else
            {
                Assert.Empty(w.InspectConstructionProjects()); Assert.Empty(w.InspectWorkAttempts());
                if (variant == ConstructionVariant.VacantCapacity) { Assert.Equal(4, w.VacantHousingBerths); Assert.Equal(20, w.InspectBerthAllocations().Count); }
            }
            foreach (long tick in boundaries.Distinct().Where(t => t >= 0 && t <= 2000))
            {
                var prefix = g.Scenario.CreateWorld(trace: true); prefix.AdvanceTo(tick); var restored = World.Restore(prefix.Serialize(), true);
                Assert.Equal(prefix.Serialize(), restored.Serialize()); restored.AdvanceTo(2000); Assert.Equal(w.Digest(), restored.Digest());
                Assert.Equal(w.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), restored.Trace.Select(Runs.EventJson));
            }
        }
    }
    [Fact]
    public void TwentyAdditionalSeedsAreInvariantToParallelExecutionTracingAndGetterFrequency()
    {
        var cases = Enumerable.Range(21, 20).Select(i => Generate((ulong)i, ConstructionVariant.Expansion)).ToArray();
        Assert.Equal(3, cases.Select(c => c.Axis).Distinct().Count()); Assert.Equal(2, cases.Select(c => c.Sign).Distinct().Count());
        Assert.True(cases.Select(c => c.Builders).Distinct().Count() > 1); Assert.Contains(cases, c => c.Terminal != c.Depot);
        var expected = cases.Select(c => { var w = c.Scenario.CreateWorld(trace: true); w.AdvanceTo(2000); return w.Digest(); }).ToArray();
        foreach (int concurrency in new[] { 2, 4, 8 })
        {
            var actual = new string[cases.Length];
            Parallel.For(0, cases.Length, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
            {
                var w = cases[i].Scenario.CreateWorld();
                for (int tick = 0; tick <= 2000; tick++) { w.AdvanceTo(tick); _ = w.InspectGrowth(); _ = w.InspectConstructionProjects(); _ = w.InspectOwnNeeds(new(cases[i].WorkerId)); }
                actual[i] = w.Digest();
            });
            Assert.Equal(expected, actual);
        }
    }
    [Fact]
    public void ExportedFixtureLoadsWithItsRelativeCatalogAndPreservesDeclaredOutcome()
    {
        var canonical = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_construction.json"));
        string output = Path.Combine(Fixtures.Root, "artifacts", "generated-construction-export-test");
        var files = ConstructionGeneration.Write(canonical, 103, ConstructionVariant.NeedInterruption, output);
        var loaded = LoadedScenario.Load(files.Scenario); Assert.Equal("catalog.json", loaded.Scenario.InitialState.Catalog);
        var run = Runs.Execute(loaded, 103, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures);
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(files.Manifest));
        Assert.Equal("NeedInterruption", manifest.RootElement.GetProperty("variant").GetString());
        Assert.Equal(loaded.ContentDigest, manifest.RootElement.GetProperty("generatedContentDigest").GetString());
        Assert.True(manifest.RootElement.GetProperty("preconditions").GetProperty("needsEnabled").GetBoolean());
        Assert.True(run.World.CriticalNeedInterruptions > 0);
    }
}
