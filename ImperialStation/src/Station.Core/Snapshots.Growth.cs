using System;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteGrowthMaterials(BinaryWriter w, System.Collections.Generic.IReadOnlyList<MaterialAmount> materials)
        { w.Write(materials.Count); foreach (var m in materials) { w.Write(m.Resource); w.Write(m.Quantity); } }
        private static MaterialAmount[] ReadGrowthMaterials(BinaryReader r)
        { int count = Count(r); var result = new MaterialAmount[count]; for (int i = 0; i < count; i++) result[i] = new MaterialAmount(r.ReadString(), r.ReadInt64()); return result; }
        private void WriteGrowth(BinaryWriter w)
        {
            w.Write(Growth != null); if (Growth == null) return;
            w.Write(Growth.BelievedCompletedBerths); WriteCoord(w, Growth.Minimum); WriteCoord(w, Growth.Maximum); w.Write(Growth.ReservePermille); w.Write(Growth.Candidates); WriteGrowthMaterials(w, Growth.KnownMaterials);
            var s = growthState!;
            w.Write(s.Population); w.Write(s.Target); w.Write(s.BelievedCompletedBerths); w.Write(s.NextReview); w.Write(s.Backoff); w.Write(s.BatchTarget); w.Write((int)s.LastOutcome); w.Write(s.LastPlanCost); w.Write(s.Reason);
            w.Write(growthCommitments.Count);
            foreach (var c in growthCommitments.Values) { w.Write(c.ProjectId); w.Write(c.JobId); w.Write(c.AcceptedReportId); w.Write(c.AcceptedTick); w.Write(c.BelievedBerths); w.Write(c.CapacityEvidenceMessageId); w.Write(c.Berths); w.Write(c.CommittedTick); w.Write(c.CauseId); WriteGrowthMaterials(w, c.Materials); }
            WriteConstructionSurveys(w);
        }
        private void ReadGrowth(BinaryReader r)
        {
            if (!r.ReadBoolean()) return;
            long capacity = r.ReadInt64(); var min = ReadCoord(r); var max = ReadCoord(r); long reserve = r.ReadInt64(); int candidates = r.ReadInt32();
            Growth = new GrowthSeed(capacity, min, max, ReadGrowthMaterials(r), reserve, candidates);
            growthState = new GrowthSnapshot { Population = r.ReadInt64(), Target = r.ReadInt64(), BelievedCompletedBerths = r.ReadInt64(), NextReview = r.ReadInt64(), Backoff = r.ReadInt64(),
                BatchTarget = r.ReadInt64(), LastOutcome = ReadEnum<PlanOutcome>(r), LastPlanCost = r.ReadInt64(), Reason = r.ReadString() };
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var c = new GrowthCommitmentSnapshot { ProjectId = r.ReadInt64(), JobId = r.ReadInt64(), AcceptedReportId = r.ReadInt64(), AcceptedTick = r.ReadInt64(), BelievedBerths = r.ReadInt64(), CapacityEvidenceMessageId = r.ReadInt64(), Berths = r.ReadInt64(), CommittedTick = r.ReadInt64(), CauseId = r.ReadInt64(), Materials = Array.AsReadOnly(ReadGrowthMaterials(r)) };
                growthCommitments.Add(c.ProjectId, c);
            }
            ReadConstructionSurveys(r);
        }
        private void ValidateGrowthState()
        {
            ValidateConstructionSurveys(); var timers = scheduled.Where(e => e.Kind == ScheduledKind.ReviewGrowth).ToArray();
            if (Growth == null)
            {
                if (constructionProjects.Values.Any(p => p.AuthorityManaged) || timers.Length > 0) throw new ArgumentException("Growth project or timer without policy."); return;
            }
            var s = growthState!;
            if (Authority == null || publicKnowledge == null || Growth.KnownMaterials.Any(m => !resources.ContainsKey(m.Resource)) || s.Population != registrations.Count ||
                s.Target != ExpansionPlanning.CapacityTarget(s.Population, Growth.ReservePermille) || s.BelievedCompletedBerths != checked(Growth.BelievedCompletedBerths + growthCommitments.Values.Sum(c => c.BelievedBerths)) || s.NextReview < -1 ||
                s.Backoff < planner.InitialBackoff || s.Backoff > planner.MaximumBackoff || s.BatchTarget < 0 || s.BatchTarget > s.Target || s.LastPlanCost < 0 ||
                !new[] { "NotReviewed", "BelievedCapacitySufficient", "CapacityCommittedAwaitingEvidence", "SharedPlanningBudgetDeferred", "PlanningBudgetExhausted", "NoKnownAffordablePlacement", "ProjectCommittedAwaitingExecution" }.Contains(s.Reason))
                throw new ArgumentException("Invalid growth policy, capacity record or review state.");
            if (s.NextReview == -1 ? timers.Length != 0 : s.NextReview <= LastProcessedTick || timers.Length != 1 || timers[0].Tick != s.NextReview || timers[0].Phase != EventPhase.Decision || timers[0].Intent.Actor.Value != 0 || timers[0].Value != 0 || timers[0].ActionId != 0 || timers[0].CauseId != 0)
                throw new ArgumentException("Invalid growth review timer.");
            if (constructionProjects.Count != growthCommitments.Count || constructionProjects.Values.Any(p => !p.AuthorityManaged)) throw new ArgumentException("Growth projects lack their administrative commitments.");
            var initialLayout = publicKnowledge.Layout.Select(t => t.Position).ToArray();
            foreach (var c in growthCommitments.Values)
            {
                if (!jobs.TryGetValue(c.JobId, out var job) || job.Kind != WorkKind.ConstructHabitat || job.ProjectId != c.ProjectId ||
                    growthCommitments.Values.Count(other => other.JobId == c.JobId) != 1 || c.AcceptedReportId < 0 ||
                    (c.AcceptedReportId == 0 ? c.AcceptedTick != -1 : c.AcceptedTick < c.CommittedTick || c.AcceptedTick > LastProcessedTick)) throw new ArgumentException("Invalid construction job or accepted report record.");
                if (c.AcceptedReportId > 0 && (!messages.TryGetValue(c.AcceptedReportId, out var report) || report.Stage != MessageStage.Accepted || report.DeliveryTick != c.AcceptedTick ||
                    report.Draft.Kind != MessageKind.WorkCompleted || !workAttempts.TryGetValue(report.Draft.AttemptId, out var attempt) || attempt.JobId != c.JobId || attempt.AccountId != report.SenderAccount.Value ||
                    !attempt.ReportMessageIds.Contains(report.Id) || !ValidConstructionReport(report, attempt))) throw new ArgumentException("Construction capacity lacks accepted report evidence.");
                var layout = initialLayout.Concat(growthCommitments.Values.Where(other => other.ProjectId != c.ProjectId && other.AcceptedReportId > 0 && other.AcceptedTick <= c.CommittedTick).Select(other => constructionProjects[other.ProjectId].Site)).ToArray(); if (!constructionProjects.TryGetValue(c.ProjectId, out var p) || !p.AuthorityManaged || !Growth.Contains(p.Site) || layout.Contains(p.Site) || !layout.Any(t => t.Manhattan(p.Site) == 1) ||
                    Topology.Definitions[p.DefinitionId].Family != TileFamily.Habitat || c.Berths != Topology.Definitions[p.DefinitionId].Construction!.Berths || c.Berths < 1 || c.CommittedTick < 0 || c.CommittedTick > LastProcessedTick || c.CauseId < 1 || c.CauseId >= nextTrace)
                    throw new ArgumentException("Invalid growth commitment identity, site, capacity or provenance.");
                var inputs = recipes[Topology.Definitions[p.DefinitionId].Construction!.RecipeId].Inputs;
                if (c.Materials.Count != inputs.Count || c.Materials.Select(m => m.Resource).Distinct(StringComparer.Ordinal).Count() != inputs.Count ||
                    inputs.Any(m => !c.Materials.Any(x => x.Resource == m.Resource && x.Quantity == m.Quantity))) throw new ArgumentException("Growth material reservation differs from recipe.");
            }
            foreach (var resource in growthCommitments.Values.SelectMany(c => c.Materials).Select(m => m.Resource).Distinct(StringComparer.Ordinal))
            {
                long used = growthCommitments.Values.SelectMany(c => c.Materials).Where(m => m.Resource == resource).Sum(m => m.Quantity);
                if (!Growth.KnownMaterials.Any(m => m.Resource == resource && m.Quantity >= used)) throw new ArgumentException("Growth material budget is overcommitted.");
            }
            long pending = growthCommitments.Values.Where(c => c.AcceptedReportId == 0).Sum(c => c.Berths);
            if (s.NextReview == -1 && s.BelievedCompletedBerths < s.Target && pending < s.Target - s.BelievedCompletedBerths) throw new ArgumentException("Uncovered growth goal has no review timer.");
            if (growthCommitments.Values.Select(c => c.CauseId).Distinct().Count() != growthCommitments.Count) throw new ArgumentException("Duplicate growth commitment cause.");
            foreach (var attempt in workAttempts.Values.Where(a => jobs[a.JobId].Kind == WorkKind.ConstructHabitat && a.OrderMessageId > 0))
                if (!messages.TryGetValue(attempt.OrderMessageId, out var order) || order.Draft.WorkOrder?.Construction == null ||
                    !order.Draft.WorkOrder.Construction.Same(ConstructionSpecification(jobs[attempt.JobId].ProjectId))) throw new ArgumentException("Construction order differs from its committed specification.");
        }
    }
}
