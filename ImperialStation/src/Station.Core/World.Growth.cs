using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public GrowthSeed? Growth { get; private set; }
        private GrowthSnapshot? growthState;
        private readonly SortedDictionary<long, GrowthCommitmentSnapshot> growthCommitments = new SortedDictionary<long, GrowthCommitmentSnapshot>();
        public GrowthSnapshot? InspectGrowth()
        {
            if (growthState == null) return null;
            return new GrowthSnapshot { Population = growthState.Population, Target = growthState.Target, BelievedCompletedBerths = growthState.BelievedCompletedBerths,
                CommittedPendingBerths = growthCommitments.Values.Where(c => c.AcceptedReportId == 0).Sum(c => c.Berths), NextReview = growthState.NextReview, Backoff = growthState.Backoff,
                BatchTarget = growthState.BatchTarget, LastOutcome = growthState.LastOutcome, LastPlanCost = growthState.LastPlanCost, Reason = growthState.Reason,
                AvailableMaterials = Array.AsReadOnly(AvailableGrowthMaterials().ToArray()), Commitments = Array.AsReadOnly(growthCommitments.Values.Select(c => c.Copy()).ToArray()) };
        }
        private void InitializeGrowth(GrowthSeed? seed)
        {
            if (seed == null) return;
            if (Authority == null || publicKnowledge == null || constructionProjects.Count > 0 || seed.KnownMaterials.Any(m => !resources.ContainsKey(m.Resource)))
                throw new ArgumentException("Growth requires an authority, public layout, declared materials and no separately seeded construction projects.");
            Growth = seed;
            growthState = new GrowthSnapshot { Population = registrations.Count, Target = ExpansionPlanning.CapacityTarget(registrations.Count, seed.ReservePermille),
                BelievedCompletedBerths = seed.BelievedCompletedBerths, NextReview = -1, Backoff = planner.InitialBackoff, LastOutcome = PlanOutcome.NoPlan };
            RequestGrowthReview(0);
        }
        private IEnumerable<MaterialAmount> AvailableGrowthMaterials()
        {
            foreach (var m in Growth!.KnownMaterials)
            {
                long remaining = m.Quantity - growthCommitments.Values.SelectMany(c => c.Materials).Where(x => x.Resource == m.Resource).Sum(x => x.Quantity);
                if (remaining < 0) throw new InvalidOperationException("Growth material budget is overcommitted.");
                if (remaining > 0) yield return new MaterialAmount(m.Resource, remaining);
            }
        }
        private ExpansionCandidate[] GrowthCandidates()
        {
            // Publication and administrative commitments are the sole occupancy evidence here.
            var layout = new SortedSet<Coord>(publicKnowledge!.Layout.Select(t => t.Position).Concat(growthCommitments.Values.Where(c => c.AcceptedReportId > 0).Select(c => constructionProjects[c.ProjectId].Site)));
            var sites = new SortedSet<Coord>();
            foreach (var tile in layout)
            foreach (var site in tile.Neighbors())
                if (Growth!.Contains(site) && !layout.Contains(site) && !constructionSites.ContainsKey(site)) sites.Add(site);
            var options = from d in Topology.Definitions.Values
                          where d.Family == TileFamily.Habitat && d.Construction != null && d.Construction.Berths > 0 && registrations.Values.Any(p => d.Construction.Access.Roles.Contains(p.Role, StringComparer.Ordinal))
                          from site in sites
                          let recipe = recipes[d.Construction!.RecipeId]
                          orderby recipe.Duration, site
                          select new { d.Id, Site = site, d.Construction!.Berths, Recipe = recipe };
            return options.OrderBy(c => c.Recipe.Duration).ThenBy(c => c.Site).ThenBy(c => c.Id, StringComparer.Ordinal).Take(Growth!.Candidates).Select((c, i) => new ExpansionCandidate(i + 1, c.Id, c.Site, c.Berths, c.Recipe.Duration, c.Recipe.Inputs)).ToArray();
        }
        private void RequestGrowthReview(long tick)
        {
            if (processingPhase > EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1); if (growthState!.NextReview >= 0 && growthState.NextReview <= tick) return; foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ReviewGrowth).ToArray()) scheduled.Remove(e); growthState.NextReview = tick;
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.ReviewGrowth });
        }
        private void ReviewGrowth()
        {
            var s = growthState!; s.NextReview = -1; bool dispatchPending = DispatchGrowthProjects();
            long pending = growthCommitments.Values.Where(c => c.AcceptedReportId == 0).Sum(c => c.Berths);
            long deficit = s.BelievedCompletedBerths >= s.Target ? 0 : Math.Max(0, s.Target - s.BelievedCompletedBerths - pending);
            if (deficit == 0)
            {
                s.Reason = s.BelievedCompletedBerths >= s.Target ? "BelievedCapacitySufficient" : "CapacityCommittedAwaitingEvidence";
                s.LastOutcome = PlanOutcome.PlanFound; s.LastPlanCost = 0; s.BatchTarget = 0; if (dispatchPending) RequestGrowthReview(checked(processingTick + planner.MaximumBackoff));
                Emit("GrowthCapacityCovered", detail: "target=" + s.Target + "; believed=" + s.BelievedCompletedBerths + "; pending=" + pending + "; " + s.Reason); return;
            }
            if (!SpendPlanningRequest()) { s.Reason = "SharedPlanningBudgetDeferred"; RequestGrowthReview(checked(processingTick + 1)); return; }
            var candidates = GrowthCandidates(); var result = ExpansionPlanning.Plan(deficit, candidates, AvailableGrowthMaterials(), planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += result.Plan.Expansions; s.LastOutcome = result.Plan.Outcome; s.BatchTarget = result.BatchTarget; s.LastPlanCost = result.Plan.Cost;
            long cause = Emit("GrowthPlanEvaluated", detail: "capacity-growth/v1; target=" + s.Target + "; believed=" + s.BelievedCompletedBerths + "; pending=" + pending +
                "; batch=" + result.BatchTarget + "; " + result.Plan.Outcome + "; cost=" + result.Plan.Cost + "; expansions=" + result.Plan.Expansions);
            if (result.Plan.Outcome != PlanOutcome.PlanFound)
            {
                if (result.Plan.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
                s.Reason = result.Plan.Outcome == PlanOutcome.BudgetExhausted ? "PlanningBudgetExhausted" : "NoKnownAffordablePlacement";
                RequestGrowthReview(checked(processingTick + s.Backoff)); s.Backoff = NextWorkBackoff(s.Backoff); return;
            }
            PlansFound++; var chosen = candidates.Single(c => c.Id == result.Plan.Steps[0].Intent.ServiceId);
            long projectId = constructionProjects.Count == 0 ? 1 : checked(constructionProjects.Keys.Last() + 1);
            long tileId = checked(Math.Max(Topology.ById.Keys.Select(k => k.Value).DefaultIfEmpty().Max(), constructionProjects.Values.Select(p => p.TileId).DefaultIfEmpty().Max()) + 1);
            // Identity allocation may see existing IDs; placement deliberately does not validate physical occupancy.
            var project = new ConstructionProjectSnapshot { Id = projectId, DefinitionId = chosen.DefinitionId, Site = chosen.Site, TileId = tileId, AuthorityManaged = true };
            long committed = Emit("GrowthProjectCommitted", target: chosen.Site, detail: "project=" + projectId + "; definition=" + chosen.DefinitionId + "; berths=" + chosen.Berths, causeId: cause);
            constructionProjects.Add(projectId, project); constructionSites.Add(project.Site, projectId);
            long jobId = nextJob; nextJob = checked(nextJob + 1); jobs.Add(jobId, new JobSnapshot { Id = jobId, ProjectId = projectId, Kind = WorkKind.ConstructHabitat }); growthCommitments.Add(projectId, new GrowthCommitmentSnapshot { JobId = jobId, ProjectId = projectId, Berths = chosen.Berths, CommittedTick = processingTick, CauseId = committed, Materials = chosen.Materials });
            s.Backoff = planner.InitialBackoff; s.Reason = "ProjectCommittedAwaitingExecution"; RequestGrowthReview(checked(processingTick + 1));
        }
    }
}
