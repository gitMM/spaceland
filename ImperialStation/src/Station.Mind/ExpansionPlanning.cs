using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class ExpansionPlanning
    {
        public static long CapacityTarget(long population, long reservePermille = 50)
        {
            if (population < 0 || reservePermille < 0 || reservePermille > 1000) throw new ArgumentOutOfRangeException(nameof(population));
            // Quotient/remainder avoids overflowing an otherwise representable target.
            return checked(population + population / 1000 * reservePermille + (population % 1000 * reservePermille + 999) / 1000);
        }
        public static ExpansionPlan Plan(long deficit, IEnumerable<ExpansionCandidate> candidates, IEnumerable<MaterialAmount> available, int maximumDepth = 8, int nodeBudget = 512)
        {
            if (deficit < 0) throw new ArgumentOutOfRangeException(nameof(deficit));
            var options = candidates.OrderBy(c => c.Cost).ThenBy(c => c.Site).ThenBy(c => c.DefinitionId, StringComparer.Ordinal).ThenBy(c => c.Id).ToArray();
            if (options.Length > 5 || options.Select(c => c.Id).Distinct().Count() != options.Length) throw new ArgumentException("Expansion requires at most five distinct candidate IDs.");
            var budget = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var m in available) budget.Add(m.Resource, m.Quantity);
            if (deficit == 0) return new ExpansionPlan(new PlanResult(PlanOutcome.PlanFound), 0);
            int count = 1 << options.Length; var feasible = new bool[count]; var capacity = new long[count]; feasible[0] = true;
            for (int subset = 1; subset < count; subset++)
            {
                var selected = options.Where((c, i) => (subset & (1 << i)) != 0).ToArray();
                if (selected.Select(c => c.Site).Distinct().Count() != selected.Length) continue;
                var used = new SortedDictionary<string, long>(StringComparer.Ordinal); bool fits = true;
                foreach (var c in selected)
                foreach (var m in c.Materials)
                {
                    used.TryGetValue(m.Resource, out long prior);
                    if (!budget.TryGetValue(m.Resource, out long total) || m.Quantity > total - prior) { fits = false; break; }
                    used[m.Resource] = prior + m.Quantity;
                }
                if (!fits) continue;
                feasible[subset] = true;
                foreach (var c in selected) capacity[subset] = c.Berths >= deficit - capacity[subset] ? deficit : capacity[subset] + c.Berths;
            }
            long target = capacity.Max();
            if (target == 0) return new ExpansionPlan(new PlanResult(PlanOutcome.NoPlan), 0);
            var predicates = Enumerable.Range(0, count).Select(i => "CommittedSubset/" + i).Append("BatchCapacityCommitted").ToArray();
            ulong goal = 1UL << count; var actions = new List<SymbolicAction>();
            for (int subset = 0; subset < count; subset++)
            {
                if (!feasible[subset]) continue;
                for (int i = 0; i < options.Length; i++)
                {
                    int next = subset | (1 << i); if (next == subset || !feasible[next]) continue;
                    var c = options[i]; ulong add = (1UL << next) | (capacity[next] >= target ? goal : 0);
                    actions.Add(new SymbolicAction("CommitHabitat/" + c.Id, 1UL << subset, 0, add, 1UL << subset, c.Cost,
                        new ActionIntent(default, ActionKind.Construct, c.Site, serviceId: c.Id)));
                }
            }
            return new ExpansionPlan(GoapPlanner.Search(new PlanningProblem(1, goal, predicates, actions), maximumDepth, nodeBudget), target);
        }
    }
}
