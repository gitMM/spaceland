using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Retained repair-goal decisions around the bounded RepairPlanning search.
    public static class RepairPolicy
    {
        public const string BudgetExhaustedReason = "PlanBudgetExhausted";
        public const string NoPlanReason = "NoKnownRepairPrerequisitesOrRoute";

        // Finishing work can establish success after shift end. Off duty, only a
        // known result is recognized; no further physical repair step starts.
        public static bool Proceeds(bool knownOnDuty, IEnumerable<KnowledgeFact> knowledge, long machineId) =>
            knownOnDuty || knowledge.Any(f => f.Key.Equals(new FactKey(SubjectKind.Machine, machineId, AttributeKind.Operational)) && f.Value.Equals(FactValue.Boolean(true)));

        // Published equipment declares whether operating it needs a usable credential.
        public static bool RequiresCredential(PublicKnowledge? published, long machineId) =>
            published?.Work?.Equipment.FirstOrDefault(m => m.Id == machineId)?.RequiresCredential == true;

        public static WorkGoalStep Classify(PlanResult result) =>
            result.Outcome != PlanOutcome.PlanFound ? WorkGoalStep.Blocked : result.Steps.Count == 0 ? WorkGoalStep.Completed : WorkGoalStep.Execute;

        // The first step, bound to the retained order attempt when there is one.
        public static ActionIntent FirstIntent(PlanResult result, long? attemptId)
        {
            var first = result.Steps[0].Intent;
            if (attemptId.HasValue) first = new ActionIntent(first.Actor, first.Kind, first.Target, first.Item, first.ServiceId, first.Message, first.RecipeId, attemptId.Value, first.Query, first.Quantity, first.OtherItem);
            if ((first.Kind == ActionKind.PickUp || first.Kind == ActionKind.Install || first.Kind == ActionKind.SplitStack) && first.Item.Value == 0)
                throw new InvalidOperationException("Repair plan attempted to execute an unbound hypothetical output.");
            return first;
        }

        public static string BlockedReason(PlanOutcome outcome) => outcome == PlanOutcome.BudgetExhausted ? BudgetExhaustedReason : NoPlanReason;

        // A selected plan keeps the facts it relies on from eviction: the target
        // machine and every bound item it handles.
        public static SortedSet<long> PlannedItems(PlanResult plan) => new SortedSet<long>(plan.Steps.Select(s => s.Intent.Item.Value).Where(id => id > 0));
        public static bool PinsForPlan(FactKey key, long machineId, SortedSet<long> plannedItems) =>
            key.SubjectKind == SubjectKind.Machine && key.SubjectId == machineId || key.SubjectKind == SubjectKind.Item && plannedItems.Contains(key.SubjectId);

        // Learned item or tile facts, or a fact about a pending machine, can open a new route.
        public static bool KnowledgeWakes(FactKey key, IReadOnlyCollection<long> pendingMachines) =>
            key.SubjectKind == SubjectKind.Item || key.SubjectKind == SubjectKind.Tile || key.SubjectKind == SubjectKind.Machine && pendingMachines.Contains(key.SubjectId);
    }
}
