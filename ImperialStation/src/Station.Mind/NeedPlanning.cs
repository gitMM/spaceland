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

        // Live stock, occupancy and current availability are deliberately absent. The
        // service executor checks them locally; a failed attempt informs retries.
        // Cafeteria choice may use the citizen's own remembered estimates: a queue
        // wait stays usable for one configured hour, an empty counter for six,
        // each from its observation tick whether seen directly or heard.
        public static long CafeteriaWait(IActorView view, NeedServiceDefinition s) => Estimate(view, s, AttributeKind.EstimateTicks, 1) ?? -1;
        private static long? Estimate(IActorView view, NeedServiceDefinition s, AttributeKind attribute, int hours)
        {
            long hour = (view.KnownRoster?.DayTicks ?? 36000) / 24;
            var fact = view.Knowledge.FirstOrDefault(f => f.Key.Equals(new FactKey(SubjectKind.Service, s.Id, attribute)) && f.Value.Kind == FactValueKind.Number);
            return fact != null && view.Tick - fact.ObservedTick <= checked(hours * hour) ? fact.Value.Number : (long?)null;
        }
        public static PlanResult Plan(IActorView view, NeedKind need, IEnumerable<long>? avoidedServices = null, int candidates = 3, int depth = 8, int budget = 512)
        {
            if (candidates < 1 || candidates > 30) throw new ArgumentOutOfRangeException(nameof(candidates));
            if (!view.CurrentTile.HasValue || view.NeedCustodyFacilityId == 0) return new PlanResult(PlanOutcome.NoPlan);
            var avoided = new SortedSet<long>(avoidedServices ?? Array.Empty<long>());
            bool credential = view.Knowledge.Any(f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.Attribute == AttributeKind.CredentialCard && f.Value.Kind == FactValueKind.Number
                && view.Inventory.Any(i => i.Value == f.Value.Number) && view.Knowledge.Any(s => s.Key.SubjectKind == SubjectKind.Credential && s.Key.SubjectId == f.Key.SubjectId && s.Key.Attribute == AttributeKind.CredentialStatus && s.Value.Equals(FactValue.Boolean(true))));
            bool RequiresAllocation(NeedServiceDefinition s) => s.Kind == NeedServiceKind.Bed && s.DetentionFacilityId == 0 && view.OwnBerthId != s.Id;
            var distances = new KnownNavigation.Distances(view, view.CurrentTile.Value);
            // Hysteresis: at a cafeteria with a fresh estimate, an alternative must beat it by a
            // whole meal, and an alternative with no estimate is assumed no shorter.
            bool Usable(NeedServiceDefinition s) => view.NeedCustodyFacilityId < 0 ? s.DetentionFacilityId == 0 : s.DetentionFacilityId == view.NeedCustodyFacilityId;
            bool KnownEmpty(NeedServiceDefinition s) => s.Kind == NeedServiceKind.Cafeteria && Estimate(view, s, AttributeKind.Quantity, 6) == 0;
            var here = need == NeedKind.Hunger ? view.NeedServices.FirstOrDefault(s => s.Kind == NeedServiceKind.Cafeteria && s.Position == view.CurrentTile.Value && Usable(s) && !avoided.Contains(s.Id) && !KnownEmpty(s) && CafeteriaWait(view, s) >= 0) : null;
            long localWait = here != null ? CafeteriaWait(view, here) : 0;
            long Wait(NeedServiceDefinition s)
            {
                if (s.Kind != NeedServiceKind.Cafeteria) return 0;
                long known = CafeteriaWait(view, s);
                return known >= 0 ? known : localWait;
            }
            long Margin(NeedServiceDefinition s) => here != null && s.Id != here.Id && s.Kind == NeedServiceKind.Cafeteria ? s.DurationTicks : 0;
            var options = view.NeedServices.Where(s => Serves(s.Kind, need) && !avoided.Contains(s.Id) && !KnownEmpty(s) &&
                (view.NeedCustodyFacilityId < 0 ? s.DetentionFacilityId == 0 : s.DetentionFacilityId == view.NeedCustodyFacilityId && s.Position == view.CurrentTile.Value) &&
                (s.Kind != NeedServiceKind.Bed || view.OwnBerthId == s.Id || s.AssignedActorId == 0 && (s.DetentionFacilityId > 0 || view.OwnBerthId == 0 && (!s.AllocationRequiresCredential || credential))))
                .Select(s => (Service: s, Travel: s.Position == view.CurrentTile.Value ? 0 : distances.To(s.Position)))
                .Where(s => s.Travel.HasValue).OrderBy(s => checked(s.Travel!.Value + s.Service.DurationTicks + Wait(s.Service) + Margin(s.Service) + (RequiresAllocation(s.Service) ? s.Service.AllocationTicks : 0))).ThenBy(s => s.Service.Id).Take(candidates).ToArray();
            var names = new List<string> { "ReliefCompleted", "AtOrigin" }; var steps = new List<SymbolicAction>();
            foreach (var option in options)
            {
                var service = option.Service; ulong at = 2;
                if (service.Position != view.CurrentTile.Value)
                {
                    at = 1UL << names.Count; names.Add("AtService:" + service.Id);
                    steps.Add(new SymbolicAction("TravelToRelief:" + service.Id, 2, at, at, 2, checked(option.Travel!.Value + Margin(service)),
                        new ActionIntent(view.Actor, ActionKind.Move, service.Position, serviceId: service.Id)));
                }
                ulong permission = 0;
                if (RequiresAllocation(service))
                {
                    permission = 1UL << names.Count; names.Add("AllocatedBerth:" + service.Id);
                    steps.Add(new SymbolicAction("AllocateBerth:" + service.Id, at, permission, permission, 0, service.AllocationTicks,
                        new ActionIntent(view.Actor, ActionKind.AllocateBerth, service.Position, serviceId: service.Id)));
                }
                steps.Add(new SymbolicAction("UseRelief:" + service.Id, at | permission, 1, 1, 0, checked(service.DurationTicks + Wait(service)),
                    new ActionIntent(view.Actor, service.Action, service.Position, serviceId: service.Id)));
            }
            return GoapPlanner.Search(new PlanningProblem(2, 1, names, steps), depth, budget);
        }
    }
}
