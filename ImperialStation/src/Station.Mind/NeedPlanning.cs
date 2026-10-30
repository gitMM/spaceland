using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // The selected relief plan, the need it serves (null when none was tried), whether
    // it is the rest fallback for unmet sleep, and all node expansions spent.
    public sealed class ReliefChoice
    {
        public PlanResult Plan { get; }
        public NeedValue? Need { get; }
        public bool Fallback { get; }
        public long Expansions { get; }
        public ReliefChoice(PlanResult plan, NeedValue? need, bool fallback, long expansions) { Plan = plan; Need = need; Fallback = fallback; Expansions = expansions; }
    }
    public static class NeedPlanning
    {
        public static bool Serves(NeedServiceKind service, NeedKind need) =>
            service == NeedServiceKind.Cafeteria && need == NeedKind.Hunger ||
            service == NeedServiceKind.Toilet && need == NeedKind.Bladder ||
            service == NeedServiceKind.Bed && need == NeedKind.SleepPressure ||
            service == NeedServiceKind.RestSpot && need == NeedKind.WorkStrain;

        // Live stock, occupancy and availability are deliberately absent. The service
        // executor checks them locally; a failed attempt informs retries. expectedWaits
        // holds only the citizen's own fresh learned estimates and the queues it sees on
        // its own tile (extra ticks per service).
        public static PlanResult Plan(IActorView view, NeedKind need, IEnumerable<long>? avoidedServices = null, int candidates = 3, int depth = 8, int budget = 512, SortedDictionary<long, long>? expectedWaits = null)
        {
            long Wait(long service) => expectedWaits != null && expectedWaits.TryGetValue(service, out long wait) ? wait : 0;
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
                .Where(s => s.Travel.HasValue).OrderBy(s => checked(s.Travel!.Value + s.Service.DurationTicks + Wait(s.Service.Id) + (RequiresAllocation(s.Service) ? s.Service.AllocationTicks : 0))).ThenBy(s => s.Service.Id).Take(candidates).ToArray();
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
                steps.Add(new SymbolicAction("UseRelief:" + service.Id, at | permission, 1, 1, 0, checked(service.DurationTicks + Wait(service.Id)),
                    new ActionIntent(view.Actor, service.Action, service.Position, serviceId: service.Id)));
            }
            return GoapPlanner.Search(new PlanningProblem(2, 1, names, steps), depth, budget);
        }

        // Urgent relief: tries each candidate need in order under one shared node budget,
        // then, with no plan for urgent sleep pressure, falls back to rest (rest never
        // satisfies sleep; a real alternative need wins first).
        public static ReliefChoice PlanUrgent(IActorView view, IReadOnlyList<NeedValue> candidates, NeedValue? urgentSleep, bool sleepFallbackDue, IEnumerable<long> avoidedServices, int candidateLimit, int depth, int budget, SortedDictionary<long, long>? expectedWaits)
        {
            var plan = new PlanResult(PlanOutcome.NoPlan); int remaining = budget; long expansions = 0; NeedValue? need = null;
            foreach (var candidate in candidates)
            {
                need = candidate;
                plan = Plan(view, need.Kind, avoidedServices, candidateLimit, depth, remaining, expectedWaits);
                expansions += plan.Expansions; remaining -= plan.Expansions;
                if (plan.Outcome != PlanOutcome.NoPlan) break;
                if (remaining <= 0) { plan = new PlanResult(PlanOutcome.BudgetExhausted); break; }
            }
            bool fallback = false;
            if (plan.Outcome == PlanOutcome.NoPlan && urgentSleep != null && remaining > 0 && sleepFallbackDue)
            {
                need = urgentSleep;
                plan = Plan(view, NeedKind.WorkStrain, avoidedServices, candidateLimit, depth, remaining);
                expansions += plan.Expansions; fallback = plan.Outcome == PlanOutcome.PlanFound;
            }
            return new ReliefChoice(plan, need, fallback, expansions);
        }
        // Relief for one need; unmet sleep pressure may fall back to rest with the budget left.
        public static ReliefChoice PlanWithRestFallback(IActorView view, NeedKind need, IEnumerable<long> avoidedServices, bool sleepFallbackDue, int candidateLimit, int depth, int budget, SortedDictionary<long, long>? expectedWaits)
        {
            var plan = Plan(view, need, avoidedServices, candidateLimit, depth, budget, expectedWaits);
            long expansions = plan.Expansions; bool fallback = false;
            if (plan.Outcome == PlanOutcome.NoPlan && need == NeedKind.SleepPressure && sleepFallbackDue && plan.Expansions < budget)
            {
                int remaining = budget - plan.Expansions;
                plan = Plan(view, NeedKind.WorkStrain, avoidedServices, candidateLimit, depth, remaining);
                expansions += plan.Expansions; fallback = plan.Outcome == PlanOutcome.PlanFound;
            }
            return new ReliefChoice(plan, null, fallback, expansions);
        }
    }
}
