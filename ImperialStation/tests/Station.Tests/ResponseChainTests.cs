using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class ResponseChainTests
{
    private static LoadedScenario Golden() => LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "autonomous_recovery.json"));
    private static void Set(TraceEvent e, string name, object value) => typeof(TraceEvent).GetProperty(name)!.SetValue(e, value);

    [Fact]
    public void CanonicalChainAuditsAllTwelveTransitionsAndReplaysInCompactAndDetailedModes()
    {
        var loaded = Golden(); string directory = Path.Combine(Fixtures.Root, "artifacts", "test-response-chain");
        var run = Runs.Execute(loaded, 42, false, directory); Assert.Equal("completed", run.Result.Status);
        var proof = Assert.Single(run.Result.ResponseChains); Assert.True(proof.Passed); Assert.Equal(12, proof.TransitionEventIds.Count); Assert.Equal(18, proof.ActionsChecked);
        Assert.Equal("CardLost", Assert.Single(run.World.Trace, e => e.Id == proof.RootEventId).Type);
        Assert.Empty(File.ReadAllText(Path.Combine(directory, "trace.jsonl"))); Assert.True(File.Exists(Path.Combine(directory, "response-chains.json")));
        var bundle = JsonData.Read<ReplayBundle>(File.ReadAllText(Path.Combine(directory, "replay.json")), "replay");
        var replay = Runs.Execute(loaded, 42, true, startingWorld: bundle.Start.Restore());
        Assert.Equal(run.Result.Digest, replay.Result.Digest); Assert.Equal(JsonData.Write(run.Result.ResponseChains), JsonData.Write(replay.Result.ResponseChains));
        Assert.Equal(run.World.Trace.Select(Runs.EventJson), replay.World.Trace.Select(Runs.EventJson));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InjectedResponseCommandCannotHideBehindSuccessfulOutcomeOrCheckpoint(bool restore)
    {
        var loaded = Golden(); var world = loaded.CreateWorld(); world.Submit(90, new(new(1), ActionKind.Move, new(4, 0, 0)));
        if (restore) world = World.Restore(world.Serialize());
        var run = Runs.Execute(loaded, 42, false, startingWorld: world);
        Assert.True(Assert.Single(world.InspectMachines()).Operational); Assert.Equal(1, Assert.Single(world.InspectRecoveryAgents()).Completed);
        Assert.Equal("assertion_failed", run.Result.Status); Assert.Contains(run.Result.AssertionFailures, f => f.Contains("Scripted response"));
        Assert.Equal(ActionOrigin.ExternalCommand, Assert.Single(world.Trace, e => e.Type == "ActionRequested" && e.Tick == 90).Origin);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InitialScriptedTriggerRequiresAnExactDeclaredAllowance(bool allow, bool duplicate)
    {
        var loaded = Golden(); var data = JsonData.Read<ScenarioData>(loaded.ScenarioJson, "trigger"); var s = data.InitialState;
        s.Information = null; s.Work = null; s.JobSystem = null; s.Horizon = 5; data.Disturbances.Clear(); data.Assertions.Clear();
        s.ResponseChainRoles = new() { new() { Name = "player-trigger", ActorId = 1, TriggerKind = ResponseTriggerKind.InitialCondition,
            RequiredEvents = new() { new() { Type = "ActionCompleted", ActorId = 1, Detail = "Move" } } } };
        data.ScriptedActors.Add(new() { ActorId = 1, Commands = new() { new() { Tick = 0, Kind = ActionKind.Move, Target = new() { X = 1 } } } });
        // Tick-zero commands are legal inputs even on an actor not flagged scripted;
        // the execution marker, not that flag, determines the purity result.
        if (allow) s.ResponseChainRoles[0].AllowedTriggerCommands.Add(new() { Tick = 0, Kind = ActionKind.Move });
        var variant = new LoadedScenario(JsonData.Write(data), loaded.CatalogJson, "trigger"); var world = variant.CreateWorld();
        if (duplicate) world.Submit(0, new(new(1), ActionKind.Move, new(1, 0, 0)));
        var run = Runs.Execute(variant, 42, false, startingWorld: world);
        Assert.Equal(allow && !duplicate ? "completed" : "assertion_failed", run.Result.Status); Assert.Equal(allow && !duplicate, Assert.Single(run.Result.ResponseChains).Passed);
    }

    [Theory]
    [InlineData("missing-link")]
    [InlineData("foreign-script")]
    [InlineData("forged-system")]
    [InlineData("missing-request")]
    [InlineData("forward-cause")]
    [InlineData("missing-origin")]
    [InlineData("unknown-origin")]
    public void CausalAuditRejectsBrokenOrContaminatedProofs(string damage)
    {
        var loaded = Golden(); var run = Runs.Execute(loaded, 42, true); var events = run.World.Trace.ToList();
        var discovery = Assert.Single(events, e => e.Type == "CredentialRecoveryNeeded");
        if (damage == "missing-link") Set(discovery, "CauseId", 0L);
        if (damage == "forward-cause") Set(discovery, "CauseId", events.Last().Id);
        if (damage == "foreign-script")
        {
            var helper = events.First(e => e.Type == "ActionRequested"); Set(helper, "ActorId", 2L); Set(helper, "Origin", ActionOrigin.ExternalCommand);
            Set(Assert.Single(events, e => e.Type == "CardLost"), "CauseId", helper.Id);
        }
        if (damage == "forged-system") Set(Assert.Single(events, e => e.Type == "ActionRequested" && e.Detail == "Install"), "Origin", ActionOrigin.SystemProtocol);
        if (damage == "missing-origin") Set(Assert.Single(events, e => e.Type == "ActionRequested" && e.Detail == "Install"), "Origin", ActionOrigin.None);
        if (damage == "unknown-origin") Set(Assert.Single(events, e => e.Type == "ActionRequested" && e.Detail == "Install"), "Origin", (ActionOrigin)999);
        if (damage == "missing-request") events.RemoveAll(e => e.Type == "ActionRequested" && e.Detail == "Install");
        Assert.False(Assert.Single(ResponseChains.Check(events, loaded.Scenario.InitialState.ResponseChainRoles, -1)).Passed);
    }

    [Fact]
    public void ConsentingConversationPartnerHasARealSystemProtocolOrigin()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "conversation.json"));
        var data = JsonData.Read<ScenarioData>(loaded.ScenarioJson, "conversation proof");
        foreach (var actor in data.InitialState.Actors.Take(2)) data.InitialState.ResponseChainRoles.Add(new()
        { Name = "participant-" + actor.Id, ActorId = actor.Id, TriggerKind = ResponseTriggerKind.InitialCondition,
            RequiredEvents = new() { new() { Type = "ActionCompleted", ActorId = actor.Id, Detail = "Converse" } } });
        var run = Runs.Execute(new LoadedScenario(JsonData.Write(data), loaded.CatalogJson, "conversation proof"), 42, true);
        Assert.Equal("completed", run.Result.Status); Assert.All(run.Result.ResponseChains, c => Assert.True(c.Passed));
        Assert.Single(run.World.Trace, e => e.Type == "ActionRequested" && e.Origin == ActionOrigin.SystemProtocol && e.Detail == "Converse");
    }

    [Fact]
    public void AContinuationCannotClaimACompleteResponseChainWithoutItsHistory()
    {
        var loaded = Golden(); var world = loaded.CreateWorld(); world.AdvanceTo(20);
        var run = Runs.Execute(loaded, 42, false, startingWorld: World.Restore(world.Serialize()));
        Assert.Equal("assertion_failed", run.Result.Status); Assert.Contains(run.Result.AssertionFailures, f => f.Contains("Complete causal history"));
        Assert.True(Assert.Single(run.World.InspectMachines()).Operational);
    }

    [Theory]
    [InlineData("unknown-actor")]
    [InlineData("empty-proof")]
    [InlineData("duplicate-role")]
    [InlineData("initial-with-event")]
    [InlineData("unmatched-allowance")]
    [InlineData("late-allowance")]
    public void LoaderRejectsInvalidOrVacuousChainDeclarations(string error)
    {
        var loaded = Golden(); var data = JsonData.Read<ScenarioData>(loaded.ScenarioJson, "invalid chain"); var role = data.InitialState.ResponseChainRoles[0];
        if (error == "unknown-actor") role.ActorId = 999;
        if (error == "empty-proof") role.RequiredEvents.Clear();
        if (error == "duplicate-role") data.InitialState.ResponseChainRoles.Add(role);
        if (error == "initial-with-event") role.TriggerKind = ResponseTriggerKind.InitialCondition;
        if (error == "unmatched-allowance") role.AllowedTriggerCommands.Add(new() { Tick = 0, Kind = ActionKind.Move });
        if (error == "late-allowance") role.AllowedTriggerCommands.Add(new() { Tick = 90, Kind = ActionKind.Move });
        Assert.Throws<ScenarioException>(() => new LoadedScenario(JsonData.Write(data), loaded.CatalogJson, "invalid chain"));
    }
}
