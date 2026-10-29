using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class GrowthSeed
    {
        public long BelievedCompletedBerths { get; }
        public long ReservePermille { get; }
        public Coord Minimum { get; }
        public Coord Maximum { get; }
        public int Candidates { get; }
        public IReadOnlyList<MaterialAmount> KnownMaterials { get; }
        public GrowthSeed(long believedCompletedBerths, Coord minimum, Coord maximum, IEnumerable<MaterialAmount> knownMaterials, long reservePermille = 50, int candidates = 3)
        {
            if (believedCompletedBerths < 0 || reservePermille < 0 || reservePermille > 1000 || minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z || candidates < 1 || candidates > 5)
                throw new ArgumentException("Invalid growth capacity, bounds or candidate limit.");
            var materials = knownMaterials.OrderBy(m => m.Resource, StringComparer.Ordinal).ToArray();
            if (materials.Select(m => m.Resource).Distinct(StringComparer.Ordinal).Count() != materials.Length) throw new ArgumentException("Duplicate known material budget.");
            BelievedCompletedBerths = believedCompletedBerths; Minimum = minimum; Maximum = maximum; ReservePermille = reservePermille; Candidates = candidates; KnownMaterials = Array.AsReadOnly(materials);
        }
        public bool Contains(Coord site) => site.X >= Minimum.X && site.X <= Maximum.X && site.Y >= Minimum.Y && site.Y <= Maximum.Y && site.Z >= Minimum.Z && site.Z <= Maximum.Z;
    }
    public sealed class GrowthCommitmentSnapshot
    {
        public long JobId { get; internal set; }
        public long BelievedBerths { get; internal set; }
        public long CapacityEvidenceMessageId { get; internal set; }
        public long AcceptedReportId { get; internal set; }
        public long AcceptedTick { get; internal set; } = -1;
        public long ProjectId { get; internal set; }
        public long Berths { get; internal set; }
        public long CommittedTick { get; internal set; }
        public long CauseId { get; internal set; }
        public IReadOnlyList<MaterialAmount> Materials { get; internal set; } = Array.Empty<MaterialAmount>();
        internal GrowthCommitmentSnapshot Copy() { var c = (GrowthCommitmentSnapshot)MemberwiseClone(); c.Materials = Array.AsReadOnly(Materials.ToArray()); return c; }
    }
    public sealed class GrowthSnapshot
    {
        public long Population { get; internal set; }
        public long Target { get; internal set; }
        public long BelievedCompletedBerths { get; internal set; }
        public long CommittedPendingBerths { get; internal set; }
        public long NextReview { get; internal set; }
        public long Backoff { get; internal set; }
        public long BatchTarget { get; internal set; }
        public PlanOutcome LastOutcome { get; internal set; }
        public long LastPlanCost { get; internal set; }
        public string Reason { get; internal set; } = "NotReviewed";
        public IReadOnlyList<MaterialAmount> AvailableMaterials { get; internal set; } = Array.Empty<MaterialAmount>();
        public IReadOnlyList<GrowthCommitmentSnapshot> Commitments { get; internal set; } = Array.Empty<GrowthCommitmentSnapshot>();
    }
}
