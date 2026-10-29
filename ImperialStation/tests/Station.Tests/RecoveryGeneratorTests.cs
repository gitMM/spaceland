using Station.Contracts;
using Station.Core;
using Station.Runner;
using Station.Scenarios;
using Xunit;

namespace Station.Tests;

public sealed class RecoveryGeneratorTests
{
    private static GeneratedRecovery Generate(ulong seed, RecoveryVariant variant) => RecoveryGenerator.Generate(
        File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "autonomous_recovery.json")),
        File.ReadAllText(Path.Combine(Fixtures.Root, "content", "recovery-work.json")), seed, variant);

    [Theory]
    [InlineData(RecoveryVariant.Replacement)]
    [InlineData(RecoveryVariant.KnownRetrieval)]
    [InlineData(RecoveryVariant.RediscoveredCard)]
    [InlineData(RecoveryVariant.NoStock)]
    [InlineData(RecoveryVariant.NoDesk)]
    [InlineData(RecoveryVariant.NoCapability)]
    [InlineData(RecoveryVariant.ValidCard)]
    public void TwentyGeneratedVariantsPreserveDeclaredOutcomeAndCheckpointContinuation(RecoveryVariant variant)
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var generated = Generate(seed, variant); var loaded = generated.Scenario;
            Assert.Empty(loaded.Scenario.ScriptedActors); Assert.All(loaded.Scenario.InitialState.Actors, a => Assert.False(a.Scripted));
            var run = Runs.Execute(loaded, seed, true); Assert.Empty(run.Result.AssertionFailures); Assert.Equal("completed", run.Result.Status);
            var world = run.World; var agent = Assert.Single(world.InspectRecoveryAgents(), a => a.ActorId == generated.ActorId);
            Assert.All(world.Trace.Where(t => t.Type == "ActionRequested"), t => Assert.True(t.CauseId > 0));
            Assert.All(world.InspectRecoveryAgents().Where(a => a.ActorId != generated.ActorId), a => Assert.False(a.Pending));
            Assert.Empty(world.InspectResourceClaims()); world.AssertAccounting();
            long expectedStock = generated.Stock - (variant == RecoveryVariant.Replacement ? 1 : 0);
            Assert.Equal(expectedStock, world.InspectItems().Where(i => i.Resource == "blank").Sum(i => i.Quantity));
            if (variant == RecoveryVariant.Replacement)
            {
                var c = Assert.Single(world.InspectCredentialRecoveryCases()); Assert.Equal(generated.ActorId, c.ActorId);
                Assert.Contains(new Id<ItemTag>(c.IssuedCardId), world.InspectActor(new(generated.ActorId)).Inventory);
                Assert.Equal(LocationKind.TileContainer, Assert.Single(world.InspectItems(), i => i.Id.Value == generated.CardId).Location.Kind);
                var finish = Assert.Single(world.Trace, e => e.Type == "CredentialRecoveryCompleted");
                Assert.True(Assert.Single(world.Trace, e => e.Type == "ActionStarted" && e.Detail == "Install").Tick > finish.Tick);
            }
            else if (variant is RecoveryVariant.KnownRetrieval or RecoveryVariant.RediscoveredCard or RecoveryVariant.ValidCard)
            {
                Assert.Empty(world.InspectCredentialRecoveryCases()); Assert.Contains(new Id<ItemTag>(generated.CardId), world.InspectActor(new(generated.ActorId)).Inventory);
                Assert.DoesNotContain(world.Trace, e => e.Type == "ActionRequested" && e.Detail == "RecoveryLogin");
            }
            else
            {
                Assert.True(agent.Pending); Assert.NotEmpty(agent.Reason); Assert.True(agent.NextDecision > world.LastProcessedTick);
                Assert.Contains(new Id<ItemTag>(generated.PartId), world.InspectActor(new(generated.ActorId)).Inventory);
                Assert.Empty(world.InspectResourceTransactions());
            }
            long[] ticks = { generated.LossTick, generated.LossTick + generated.TravelTicks * 3, 50, 100, 400 };
            foreach (long tick in ticks.Distinct())
            {
                var prefix = loaded.CreateWorld(trace: true); prefix.AdvanceTo(tick);
                foreach (var memory in prefix.InspectRecoveryAgents())
                    Assert.True(memory.NextDecision == -1 || memory.NextDecision >= memory.RetryAt, "Retry timer precedes backoff: seed=" + seed + "; tick=" + tick + "; " + JsonData.Write(memory));
                var copy = World.Restore(prefix.Serialize(), true);
                Assert.Equal(prefix.Serialize(), copy.Serialize()); prefix.AdvanceTo(800); copy.AdvanceTo(800);
                Assert.Equal(world.Digest(), copy.Digest()); Assert.Equal(prefix.Digest(), copy.Digest());
                Assert.Equal(prefix.Trace.Where(t => t.Tick > tick).Select(t => JsonData.Write(t)), copy.Trace.Select(t => JsonData.Write(t)));
            }
        }
    }

    [Fact]
    public void GeneratedRecoveryIsIndependentOfTracingChunkingAndConcurrentWorlds()
    {
        var cases = Enumerable.Range(21, 20).Select(i => Generate((ulong)i, RecoveryVariant.Replacement)).ToArray();
        var expected = cases.Select(c => { var w = c.Scenario.CreateWorld(trace: true); w.AdvanceTo(800); return w.Digest(); }).ToArray();
        foreach (int concurrency in new[] { 2, 4, 8 })
        {
            var actual = new string[cases.Length];
            Parallel.For(0, cases.Length, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, i =>
            {
                var w = cases[i].Scenario.CreateWorld();
                for (int tick = 0; tick <= 800; tick++) { w.AdvanceTo(tick); _ = w.InspectRecoveryAgents(); _ = w.InspectActor(new(cases[i].ActorId)); }
                actual[i] = w.Digest();
            });
            Assert.Equal(expected, actual);
        }
        Assert.True(cases.Select(c => c.HeadquartersIndex).Distinct().Count() > 1);
        Assert.True(cases.Select(c => c.BackgroundStaff).Distinct().Count() > 1);
    }
}
