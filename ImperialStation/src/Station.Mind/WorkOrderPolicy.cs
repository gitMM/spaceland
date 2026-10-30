using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Citizen-side work-order lifecycle: belief revision on read, acceptance,
    // goal superseding and expiry. Core gathers the private order copy and applies.
    public static class WorkOrderPolicy
    {
        public const string ExpiredReason = "KnownOrderExpired";
        public const string OwnCompletionReason = "OwnCompletionAlreadyPosted";
        public const string SupersededReason = "SupersedingOrderRead";
        public const string CancellationReadReason = "CancellationRead";
        public const string AcceptedReason = "Accepted";
        public const string ReportedCompleteReason = "ReportedComplete";

        // Read orders await acceptance; accepted non-repair orders are driven by
        // their own retained goal. Repair orders run through the repair agent.
        public static bool AwaitsDecision(KnownWorkOrderStatus status, WorkKind kind) =>
            status == KnownWorkOrderStatus.Read || status == KnownWorkOrderStatus.Working && kind != WorkKind.RepairMachine;

        public static bool Settled(KnownWorkOrderStatus status) =>
            status is KnownWorkOrderStatus.Completed or KnownWorkOrderStatus.Cancelled or KnownWorkOrderStatus.Rejected or KnownWorkOrderStatus.Expired;

        public static WorkOrderRoute Route(KnownWorkOrderStatus status, WorkKind kind)
        {
            if (status == KnownWorkOrderStatus.Working && kind != WorkKind.RepairMachine)
                return kind == WorkKind.VerifyMachine ? WorkOrderRoute.Verification : kind == WorkKind.ConstructHabitat ? WorkOrderRoute.Construction : WorkOrderRoute.Cleanup;
            return status == KnownWorkOrderStatus.Read ? WorkOrderRoute.Acceptance : WorkOrderRoute.None;
        }

        // A repair order is the authority's authenticated claim that the machine
        // needed repair when it was posted. Returns the condition it claims, if any.
        public static FactKey? ClaimedCondition(WorkKind kind, long machineId) =>
            kind == WorkKind.RepairMachine && machineId > 0 ? new FactKey(SubjectKind.Machine, machineId, AttributeKind.Operational) : (FactKey?)null;

        // An older private sighting that the machine works cannot close the order;
        // seeded knowledge has no observation time. Fresher observations win.
        public static bool OrderSupersedesBelief(KnowledgeFact? known, long orderObservedTick) =>
            known != null && known.SourceKind != EvidenceKind.Seeded && known.Value.Equals(FactValue.Boolean(true)) && known.ObservedTick < orderObservedTick;

        // A late inbox read must not restart an assignment the citizen already
        // reported complete through their own session; an expired order is refused.
        public static WorkOrderReadOutcome OnRead(bool ownCompletionPosted, long expires, long now) =>
            ownCompletionPosted ? WorkOrderReadOutcome.OwnCompletionAlreadyPosted : expires <= now ? WorkOrderReadOutcome.Expired : WorkOrderReadOutcome.Evaluate;

        // Chooses and runs the bounded acceptance plan for one read order.
        public static PlanResult PlanAcceptance(IActorView view, WorkKind kind, long machineId, long tileId, ConstructionOrder? construction, long inspectTicks, int candidates, int depth, int budget)
        {
            var equipment = view.PublicKnowledge?.Work?.Equipment.SingleOrDefault(m => m.Id == machineId);
            return kind == WorkKind.ConstructHabitat ? ConstructionPlanning.Plan(view, construction!, candidates, depth, budget) :
                kind == WorkKind.VerifyMachine ? VerificationPlanning.Plan(view, machineId, depth, budget) :
                kind == WorkKind.CleanTile ? CleanupPlanning.Plan(view, new Id<TileTag>(tileId), false, inspectTicks, candidates, depth, budget) :
                equipment == null || !equipment.Roles.Contains(view.Role, StringComparer.Ordinal) ? new PlanResult(PlanOutcome.NoPlan) : RepairPlanning.Plan(view, machineId, candidates, depth, budget);
        }

        public static WorkOrderAcceptance Accept(PlanOutcome outcome, WorkKind kind) =>
            outcome == PlanOutcome.BudgetExhausted ? new WorkOrderAcceptance(WorkOrderVerdict.Retry, "") :
            outcome == PlanOutcome.NoPlan ? new WorkOrderAcceptance(WorkOrderVerdict.Reject, NoPlanReason(kind)) :
            new WorkOrderAcceptance(WorkOrderVerdict.Accept, AcceptedReason);

        public static string NoPlanReason(WorkKind kind) => kind == WorkKind.ConstructHabitat ? "NoKnownConstructionPrerequisitesOrRoute" : kind == WorkKind.VerifyMachine ? "NoKnownVerificationRoute" :
            kind == WorkKind.CleanTile ? "NoKnownCleanupPrerequisitesOrRoute" : "NoKnownRepairPrerequisitesOrRoute";

        // A newly accepted cleanup supersedes the citizen's other working cleanup
        // orders for the same tile, in attempt order.
        public static long[] SupersededCleanups(WorkOrderBrief order, IEnumerable<WorkOrderBrief> own) =>
            own.Where(o => o.AttemptId != order.AttemptId && o.Kind == WorkKind.CleanTile && o.TileId == order.TileId && o.Status == KnownWorkOrderStatus.Working).Select(o => o.AttemptId).ToArray();

        // A newly accepted repair attempt replaces the retained attempt for its machine.
        public static bool SupersedesRepair(long? retainedAttempt, long attempt) => retainedAttempt.HasValue && retainedAttempt.Value != attempt;

        // Explicit report-first priority: without autonomous needs, pending reports
        // and a due check-in run before another work intent is issued.
        public static bool ReportsFirst(bool autonomousNeeds, int pendingReports, bool checkInDue) => !autonomousNeeds && (pendingReports > 0 || checkInDue);
    }
}
