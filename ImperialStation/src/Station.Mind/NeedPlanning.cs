using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class NeedPlanning
    {
        public static bool Serves(NeedServiceKind service, NeedKind need) =>
            service == NeedServiceKind.Cafeteria && need == NeedKind.Hunger ||
            service == NeedServiceKind.Toilet && need == NeedKind.Bladder ||
            service == NeedServiceKind.Bed && need == NeedKind.SleepPressure ||
            service == NeedServiceKind.RestSpot && need == NeedKind.WorkStrain;

        // Stock, occupancy and current availability are deliberately absent. The
        // service executor checks them locally; a failed attempt informs retries.
        public static PlanResult Plan(IActorView view, NeedKind need, IEnumerable<long>? avoidedServices = null, int candidates = 3, int depth = 8, int budget = 512)
        {
            if (candidates < 1 || candidates > 30) throw new ArgumentOutOfRangeException(nameof(candidates));
            if (!view.CurrentTile.HasValue || view.NeedCustodyFacilityId == 0) return new PlanResult(PlanOutcome.NoPlan);
            var avoided = new SortedSet<long>(avoidedServices ?? Array.Empty<long>());
            bool credential = view.Knowledge.Any(f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.Attribute == AttributeKind.CredentialCard && f.Value.Kind == FactValueKind.Number
                && view.Inventory.Any(i => i.Value == f.Value.Number) && view.Knowledge.Any(s => s.Key.SubjectKind == SubjectKind.Credential && s.Key.SubjectId == f.Key.SubjectId && s.Key.Attribute == AttributeKind.CredentialStatus && s.Value.Equals(FactValue.Boolean(true))));
            bool RequiresAllocation(NeedServiceDefinition s) => s.Kind == NeedServiceKind.Bed && s.DetentionFacilityId == 0 && view.OwnBerthId != s.Id;
            var distances = new KnownNavigation.Distances(view, view.CurrentTile.Value);
            var options = view.NeedServices.Where(s => Serves(s.Kind, need) && !avoided.Contains(s.Id) &&
                (view.NeedCustodyFacilityId < 0 ? s.DetentionFacilityId == 0 : s.DetentionFacilityId == view.NeedCustodyFacilityId && s.Position == view.CurrentTile.Value) &&
                (s.Kind != NeedServiceKind.Bed || view.OwnBerthId == s.Id || s.AssignedActorId == 0 && (s.DetentionFacilityId > 0 || view.OwnBerthId == 0 && (!s.AllocationRequiresCredential || credential))))
                .Select(s => (Service: s, Travel: s.Position == view.CurrentTile.Value ? 0 : distances.To(s.Position)))
                .Where(s => s.Travel.HasValue).OrderBy(s => checked(s.Travel!.Value + s.Service.DurationTicks + (RequiresAllocation(s.Service) ? s.Service.AllocationTicks : 0))).ThenBy(s => s.Service.Id).Take(candidates).ToArray();
            var names = new List<string> { "ReliefCompleted", "AtOrigin" }; var steps = new List<SymbolicAction>();
            foreach (var option in options)
            {
                var service = option.Service; ulong at = 2;
                if (service.Position != view.CurrentTile.Value)
                {
                    at = 1UL << names.Count; names.Add("AtService:" + service.Id);
                    steps.Add(new SymbolicAction("TravelToRelief:" + service.Id, 2, at, at, 2, option.Travel!.Value,
                        new ActionIntent(view.Actor, ActionKind.Move, service.Position, serviceId: service.Id)));
                }
                ulong permission = 0;
                if (RequiresAllocation(service))
                {
                    permission = 1UL << names.Count; names.Add("AllocatedBerth:" + service.Id);
                    steps.Add(new SymbolicAction("AllocateBerth:" + service.Id, at, permission, permission, 0, service.AllocationTicks,
                        new ActionIntent(view.Actor, ActionKind.AllocateBerth, service.Position, serviceId: service.Id)));
                }
                steps.Add(new SymbolicAction("UseRelief:" + service.Id, at | permission, 1, 1, 0, service.DurationTicks,
                    new ActionIntent(view.Actor, service.Action, service.Position, serviceId: service.Id)));
            }
            return GoapPlanner.Search(new PlanningProblem(2, 1, names, steps), depth, budget);
        }
    }
}
