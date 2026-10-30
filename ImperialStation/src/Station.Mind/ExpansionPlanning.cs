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
        // Berths still to commit: none once believed capacity meets the target.
        public static long Deficit(long believedCompletedBerths, long target, long pendingBerths) =>
            believedCompletedBerths >= target ? 0 : Math.Max(0, target - believedCompletedBerths - pendingBerths);
        // The first received completion claim retires a commitment; later ones are duplicates.
        public static bool AcceptsCompletionClaim(long acceptedReportId) => !(acceptedReportId > 0);
        // Habitat placements adjacent to the believed layout inside the growth bounds,
        // excluding reserved sites, cheapest recipe first. Publication and administrative
        // commitments are the sole occupancy evidence.
        public static ExpansionCandidate[] Candidates(IEnumerable<Coord> believedLayout, Coord minimum, Coord maximum, IEnumerable<Coord> reservedSites,
            IEnumerable<HabitatOption> definitions, IEnumerable<string> registeredRoles, int limit)
        {
            var layout = new SortedSet<Coord>(believedLayout); var reserved = new SortedSet<Coord>(reservedSites); var roles = registeredRoles.ToArray();
            bool Inside(Coord c) => c.X >= minimum.X && c.X <= maximum.X && c.Y >= minimum.Y && c.Y <= maximum.Y && c.Z >= minimum.Z && c.Z <= maximum.Z;
            var sites = new SortedSet<Coord>();
            foreach (var tile in layout)
            foreach (var site in tile.Neighbors())
                if (Inside(site) && !layout.Contains(site) && !reserved.Contains(site)) sites.Add(site);
            var options = from d in definitions
                          where d.Family == TileFamily.Habitat && d.Berths > 0 && roles.Any(r => d.Roles.Contains(r, StringComparer.Ordinal))
                          from site in sites
                          orderby d.Duration, site
                          select new { d.DefinitionId, Site = site, d.Berths, d.Duration, d.Inputs };
            return options.OrderBy(c => c.Duration).ThenBy(c => c.Site).ThenBy(c => c.DefinitionId, StringComparer.Ordinal).Take(limit)
                .Select((c, i) => new ExpansionCandidate(i + 1, c.DefinitionId, c.Site, c.Berths, c.Duration, c.Inputs)).ToArray();
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
