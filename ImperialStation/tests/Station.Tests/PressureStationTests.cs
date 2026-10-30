using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class PressureStationTests
{
    [Fact]
    public void CombinedPressureRecoversWithDailyLifeConservationAndExactContinuation()
    {
        var result = PressureStation.Execute(PressureStation.Create());
        Assert.True(result.Status == "completed", string.Join("\n", result.Failures));
        Assert.Equal(4, result.Reprimands);
        Assert.Equal(2, result.Released);
        Assert.True(result.OverlappingPressure && result.SharedGuardDuties);
        Assert.True(result.CheckpointMatches && result.ContinuationTraceMatches);
        Assert.True(result.RecoveryTick >= PressureStation.PressureEnd);
        Assert.True(result.Participation.Horizon - result.RecoveryTick >= StationBaseline.Day);
        // Repair priority over routine cleanup, cleanup helpers for large spills and
        // reassignment of silent facility orders leave a wide margin (138,000 measured).
        Assert.True(result.RecoveryTick <= 6 * StationBaseline.Day, "Backlogs should clear by day six, leaving three clear days.");
        Assert.True(result.Events.GetValueOrDefault("CleanupHelperDispatched") > 0);
        // The evidence hold keeps every sabotaged machine for its quality inspection.
        Assert.All(result.CaseOutcomes, c => Assert.Equal(PressureStation.CaseOutcomeKind.Accepted, c.Outcome));
        // Staggered guards: every duty interval has a rostered escort team, and no
        // escort waits for a shift change before dispatch.
        var coverage = Assert.Single(result.GuardCoverage);
        Assert.Equal(new[] { 3, 3, 3 }, coverage.GuardsPerCohort.Values);
        Assert.Equal(0, coverage.SamplesWithoutTeamFromRoster);
        var dispatch = Assert.Single(result.Responses.Stages, s => s.Chain == "escort" && s.Stage == "Dispatch");
        Assert.Equal(0, dispatch.MissedTarget); Assert.Equal(0, dispatch.Unresolved);
        Assert.Contains(result.Responses.Stages, s => s.Chain == "patrol" && s.Stage == "Acknowledgment" && s.Completed > 0);
        // Every stage that misses its declared target, or never ends, carries a classified cause.
        foreach (var chain in result.Responses.Exceptions)
            foreach (var stage in chain.Stages.Where(s => s.Start >= 0 && PressureStation.ResponseTargets.TryGetValue(chain.Chain + "/" + s.Stage, out long t) && (s.End < 0 || s.Duration > t)))
                Assert.NotEqual("", stage.Cause);
        Assert.Equal(result.Responses.MissedTargets, result.Responses.Exceptions.Sum(c => c.Stages.Count(s => s.Cause.Length > 0)));
        // Pre-sleep relief: no critical need interrupts sleep; every missed window is classified.
        Assert.Equal(0, result.Participation.CriticalInterruptions);
        Assert.All(result.Participation.MissedDailyGoalReasons.Keys, k => Assert.Contains("OpportunityClosed/", k));
        // Every missed routine window has one allowed explanation (fixture start, custody,
        // escort duty, credential recovery or hunger reaching seek only at the close).
        Assert.Equal(result.Participation.MissedDailyGoals, result.MissedWindows.Total);
        Assert.Equal(0, result.MissedWindows.NeedsAttention);
        Assert.All(result.MissedWindows.Windows, w => Assert.True(PressureStation.MissCategoryAllowed[w.Category], w.Category));
        // Sustained cleaning delays but never starves check-ins: at most two periodic
        // intervals of available duty time between inbox reads (2,109 measured).
        Assert.Equal(result.Participation.Citizens.Count, result.CheckIns.Actors);
        Assert.Equal(0, result.CheckIns.GapsOverBound);
        Assert.InRange(result.CheckIns.MaximumGap, 1, result.CheckIns.BoundTicks);
        Assert.True(result.CheckIns.ExcludedOnDutyTicks["TerminalOutage"] > 0 && result.CheckIns.ExcludedOnDutyTicks["EscortOrCustodyDuty"] > 0);
        Assert.Empty(result.RosterChanges);
    }

    [Fact]
    public void UnreadRosterChangeKeepsTheOldRosterUntilTheCitizenReadsIt()
    {
        var scenario = PressureStation.Create(variant: "roster-change");
        var result = PressureStation.Execute(scenario);
        Assert.True(result.Status == "completed", string.Join("\n", result.Failures));
        Assert.Equal("roster-change", result.Variant);
        Assert.Equal(PressureStation.RosterChangeActors(0), result.RosterChanges.Select(c => c.Actor));
        Assert.All(result.RosterChanges, c =>
        {
            Assert.True(c.Read > c.Published, "The new roster is adopted only by reading it.");
            Assert.Equal((c.OldCohort + 1) % 3, c.NewCohort);
            Assert.Equal(0, c.Inconsistent);
        });
        // Old-roster check-ins between publication and read prove the roster was unread.
        Assert.True(result.RosterChanges.Sum(c => c.OldRosterOnlyEvents) > 0);
        Assert.Equal(0, result.CheckIns.GapsOverBound);
        Assert.True(result.CheckpointMatches && result.ContinuationTraceMatches);
    }

    [Fact]
    public void SingleCohortGuardControlExposesTheOvernightEscortGap()
    {
        var result = PressureStation.Execute(PressureStation.Create(variant: "single-cohort-guards"));
        Assert.Equal("single-cohort-guards", result.Variant);
        var coverage = Assert.Single(result.GuardCoverage);
        Assert.Equal(4, coverage.GuardsPerCohort[0]); Assert.True(coverage.SamplesWithoutTeamFromRoster > 0);
        Assert.Contains(result.Responses.MissCauses.Keys, k => k.StartsWith("escort/Dispatch: NoOnDutyEscortTeam", StringComparison.Ordinal) || k.StartsWith("patrol/Dispatch: NoOnDutyGuard", StringComparison.Ordinal));
        Assert.True(result.CheckpointMatches && result.ContinuationTraceMatches);
    }

    [Fact]
    public void StaggeredGuardsComeFromDeclaredCleanersWithoutChangingPopulation()
    {
        var staggered = PressureStation.Create(PressureStation.PopulationCount).Scenario.InitialState;
        var single = PressureStation.Create(PressureStation.PopulationCount, variant: "single-cohort-guards").Scenario.InitialState;
        Assert.Equal(single.Actors.Count, staggered.Actors.Count);
        var cardLoss = PressureStation.Create(PressureStation.PopulationCount).Scenario.Disturbances.Where(d => d.Type == "CardLoss").Select(d => d.ActorId).ToHashSet();
        for (long b = 0; b < PressureStation.PopulationCount; b += 64)
        {
            var guards = staggered.Actors.Where(a => a.Role == "guard" && (a.Id - 1) / 64 == b / 64).Select(a => a.Id).ToArray();
            Assert.Equal(9, guards.Length);
            Assert.All(PressureStation.ConvertedGuards(b), id =>
            {
                Assert.Contains(id, guards); Assert.Equal("cleaner", single.Actors.Single(a => a.Id == id).Role); Assert.DoesNotContain(id, cardLoss);
            });
            Assert.Equal(new[] { 0, 0, 0, 1, 1, 1, 2, 2, 2 }, guards.Select(g => staggered.Routines!.Assignments.Single(r => r.ActorId == g).Cohort).Order());
        }
        Assert.All(staggered.Actors.Where(a => a.Role is "supervisor" or "worker"), a => Assert.Equal(0, staggered.Routines!.Assignments.Single(r => r.ActorId == a.Id).Cohort));
        Assert.Throws<ArgumentException>(() => PressureStation.Create(32, variant: "unknown"));
    }

    [Fact]
    public void PopulationGeneratorCreatesRealIndependentBedsAndScaledMisconduct()
    {
        // 256 citizens (four 64-citizen groups) is the population workload.
        var scenario = PressureStation.Create(PressureStation.PopulationCount);
        Assert.Equal(8, scenario.Scenario.ScriptedActors.Count);
        Assert.Equal(64, scenario.Scenario.ScriptedActors.Sum(a => a.Commands.Count));
        Assert.Equal(48, scenario.Scenario.Disturbances.Count(d => d.Type == "CardLoss"));
        var state = scenario.Scenario.InitialState;
        foreach (var supervisor in state.Actors.Where(a => a.Role == "supervisor"))
        {
            var grants = state.Information!.RecordAccess.Where(g => g.ActorId == supervisor.Id).ToArray();
            Assert.Equal(2, grants.Length);
            Assert.All(grants, g => Assert.Equal((supervisor.Id - 1) / 16 + 1, g.TerminalId));
            Assert.Equal(new[] { Station.Contracts.RecordKind.WorkAttempt, Station.Contracts.RecordKind.WorkReport }, grants.Select(g => g.Kind).Order());
            var desk = Assert.Single(state.Information.Terminals, t => t.Id == grants[0].TerminalId);
            Assert.Contains("worker", desk.PermittedRoles);
            Assert.Contains("guard", desk.PermittedRoles);
            Assert.Contains(state.Information.Recovery!.Desks, d => d.TerminalId == desk.Id);
        }
        var w = scenario.CreateWorld();
        Assert.Equal(4, w.InspectNeedServices().Count(n => n.Definition.Kind == Station.Contracts.NeedServiceKind.Bed && n.Definition.DetentionFacilityId == 1));
        Assert.Empty(w.InspectEquipment());
        Assert.All(scenario.Scenario.InitialState.Actors.Where(a => a.Role == "guard"), a =>
            Assert.Single(scenario.Scenario.InitialState.Items, i => i.Resource == "guard_equipment" &&
                i.LocationKind == Station.Contracts.LocationKind.ActorInventory && i.Owner == a.Id && i.Quantity == 1));
        w.AdvanceTo(50);
        Assert.Equal(16, w.InspectMachines().Count(m => m.Id <= 16 && !m.Operational));
        w.AssertAccounting();
    }
}
