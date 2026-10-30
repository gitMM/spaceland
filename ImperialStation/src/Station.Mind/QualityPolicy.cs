using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Quality assurance: sampling of claimed repairs, supervisor choice, verdicts.
    public static class QualityPolicy
    {
        // The roll is drawn by the caller from its seeded QA stream.
        public static bool Sampled(QualityAssuranceConfig config, int roll) => roll < config.SamplePermille;
        // Reject ineligible roles/rosters before gathering their commitments. Gathering
        // reads the work history; doing it for every citizen for every pending
        // verification made concurrent QA quadratic in population.
        public static IEnumerable<DispatchCandidate> VerificationCandidates(IAuthorityView view, string supervisorRole, Func<KnownPersonnel, DispatchCandidate> gather) => view.Personnel
            .Where(p => p.Role == supervisorRole && p.Roster != null && p.Roster.OnDuty(view.Tick))
            .Select(gather).Where(c => !c.Committed && c.KnownOrigin.HasValue);
        public static long Verifier(IAuthorityView view, long machine, long excludedActor, IEnumerable<DispatchCandidate> candidates)
        {
            var target = view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == machine); if (target == null) return 0;
            return candidates.Where(c => c.Person.ActorId != excludedActor)
                .OrderBy(c => TravelEstimates.Authority(c.KnownOrigin!.Value, target.Position, view.NominalTravelTicks)).ThenBy(c => c.Person.ActorId).Select(c => c.Person.ActorId).FirstOrDefault();
        }
        public static VerificationVerdict Verdict(MessageKind kind, FactValue claim) =>
            kind == MessageKind.WorkCompleted ? (claim.Equals(FactValue.Boolean(true)) ? VerificationVerdict.Clear : VerificationVerdict.Discrepancy) :
            kind is MessageKind.OrderRejected or MessageKind.WorkBlocked or MessageKind.UnableToContinue ? VerificationVerdict.NeedsAttention : VerificationVerdict.Ignored;
    }
}
