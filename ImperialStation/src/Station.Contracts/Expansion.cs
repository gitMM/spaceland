using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    // Authority planning receives declared options and records, never a physical world handle.
    public sealed class ExpansionCandidate
    {
        public long Id { get; }
        public string DefinitionId { get; }
        public Coord Site { get; }
        public long Berths { get; }
        public long Cost { get; }
        public IReadOnlyList<MaterialAmount> Materials { get; }
        public ExpansionCandidate(long id, string definitionId, Coord site, long berths, long cost, IEnumerable<MaterialAmount> materials)
        {
            var amounts = materials.OrderBy(m => m.Resource, StringComparer.Ordinal).ToArray();
            if (id < 1 || string.IsNullOrWhiteSpace(definitionId) || berths < 1 || cost < 1 || amounts.Length == 0 || amounts.Select(m => m.Resource).Distinct(StringComparer.Ordinal).Count() != amounts.Length)
                throw new ArgumentException("Invalid expansion candidate.");
            Id = id; DefinitionId = definitionId; Site = site; Berths = berths; Cost = cost; Materials = Array.AsReadOnly(amounts);
        }
    }
    public sealed class ExpansionPlan
    {
        public PlanResult Plan { get; }
        // A bounded batch may cover only part of a large capacity deficit.
        public long BatchTarget { get; }
        public ExpansionPlan(PlanResult plan, long batchTarget) { Plan = plan; BatchTarget = batchTarget; }
    }
}
