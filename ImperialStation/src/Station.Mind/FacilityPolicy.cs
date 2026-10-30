using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Facility-condition reporting (citizen side) and the authority's facility task rules.
    public static class FacilityPolicy
    {
        public const int MaximumCleanupHelpers = 2;
        public static bool Subject(FactKey key) => key.SubjectKind == SubjectKind.Tile && key.Attribute == AttributeKind.Dirt || key.SubjectKind == SubjectKind.Machine && key.Attribute == AttributeKind.Operational;
        public static bool Adverse(FacilityConfig config, FactKey key, FactValue value) =>
            key.SubjectKind == SubjectKind.Tile && key.Attribute == AttributeKind.Dirt && value.Kind == FactValueKind.Number && value.Number >= config.DirtThreshold ||
            key.SubjectKind == SubjectKind.Machine && key.Attribute == AttributeKind.Operational && value.Equals(FactValue.Boolean(false));
        public static bool Published(PublicWorkCatalog? work, FactKey key) => work != null && (key.SubjectKind == SubjectKind.Tile ? work.Cleanable.Any(c => c.Tile.Value == key.SubjectId) :
            key.SubjectKind == SubjectKind.Machine && work.Equipment.Any(m => m.Id == key.SubjectId));
        // A reporting-duty citizen watches its own direct or inspected facility facts.
        public static bool Observes(FacilityConfig config, EvidenceKind source, FactKey key, string role) =>
            source is EvidenceKind.Direct or EvidenceKind.Inspection && Subject(key) && config.ReportingRoles.Contains(role, StringComparer.Ordinal);
        // Report a newly adverse condition, or repeat one after the repeat interval, unless
        // a report about the same subject is already waiting to be posted.
        public static bool ShouldReport(bool adverse, bool wasAdverse, long tick, long nextRepeatTick, FactKey subject, IEnumerable<MessageDraft> pendingReports) =>
            adverse && !(wasAdverse && tick < nextRepeatTick) && !pendingReports.Any(p => p.Kind == MessageKind.FacilityCondition && p.Subject.Equals(subject));
        public static bool AcceptsReport(FacilityConfig? config, KnownPersonnel? reporter, long observedTick, long tick, FactKey subject, FactValue claim, PublicWorkCatalog? work) =>
            config != null && reporter != null && observedTick >= 0 && observedTick <= tick && config.ReportingRoles.Contains(reporter.Role, StringComparer.Ordinal) && Adverse(config, subject, claim) && Published(work, subject);
        // Reports older than a reported completion are stale; otherwise a report opens a
        // new task when none is open, or merges into the open one.
        public static FacilityReportDisposition Disposition(bool hasTask, bool reportedComplete, long resolvedObservedTick, long observedTick) =>
            hasTask && reportedComplete && observedTick <= resolvedObservedTick ? FacilityReportDisposition.Stale :
            !hasTask || reportedComplete ? FacilityReportDisposition.NewTask : FacilityReportDisposition.Merge;
        // A fresher condition report reopens a task that needed attention.
        public static bool Reopens(bool needsAttention, long observedTick, long latestObservedTick) => observedTick > latestObservedTick && needsAttention;
        public static FacilityWorkResponse WorkResponse(MessageKind kind, long observedTick, long latestConditionTick) =>
            kind == MessageKind.WorkCompleted ? (observedTick < latestConditionTick ? FacilityWorkResponse.Reopen : FacilityWorkResponse.Complete) :
            kind is MessageKind.OrderRejected or MessageKind.WorkBlocked or MessageKind.UnableToContinue ? FacilityWorkResponse.NeedsAttention : FacilityWorkResponse.Ignored;
        // Evidence hold: the authority's own pending quality verification of a machine's
        // claimed repair must inspect it before another repair can change the evidence. A
        // reported discrepancy keeps the hold until a case decision for that repair is posted,
        // bounded to holdTicks after the report so a missing filing cannot freeze it.
        public static bool EvidenceHold(bool repairJob, IEnumerable<QualityHoldEvidence> checks, long tick, long holdTicks, Func<long, bool> caseDecided)
        {
            if (!repairJob) return false;
            foreach (var q in checks)
            {
                if (q.Stage is QualityCheckStage.Scheduled or QualityCheckStage.PendingSupervisor or QualityCheckStage.Assigned) return true;
                if (q.Stage != QualityCheckStage.ReportedDiscrepancy || !q.ResultDeliveryTick.HasValue) continue;
                long limit = checked(q.ResultDeliveryTick.Value + holdTicks);
                if (tick < limit && !caseDecided(q.RepairAttemptId)) return true;
            }
            return false;
        }
        public static bool AcknowledgmentOverdue(long dispatchTick, long acknowledgmentTicks, long tick) => tick >= checked(dispatchTick + acknowledgmentTicks);
        public static long Responder(IAuthorityView view, WorkKind kind, long tileId, long machineId, long excludedActor, IEnumerable<DispatchCandidate> candidates) =>
            kind == WorkKind.CleanTile ? AuthorityDispatch.CleanupReplacement(view, new Id<TileTag>(tileId), excludedActor, candidates) : AuthorityDispatch.Replacement(view, machineId, excludedActor, candidates);
        public static bool CleanupHelpersApply(long cleanUnitTicks, bool assigned, bool tileSubject) => cleanUnitTicks >= 1 && assigned && tileSubject;
        // A reported spill larger than one cleaner clears in a known duty period gets up to
        // two additional cleanup orders, within twice that bound over the task's whole life.
        public static long CleanupHelpersWanted(long dutyTicks, long cleanUnitTicks, long reportedUnits, int helpersDispatched, int helpersOpen)
        {
            long perShift = Math.Max(1, dutyTicks / cleanUnitTicks);
            long wanted = Math.Min(MaximumCleanupHelpers, (reportedUnits + perShift - 1) / perShift - 1);
            return helpersDispatched >= 2 * MaximumCleanupHelpers ? 0 : Math.Max(0, wanted - helpersOpen);
        }
        // A helper is never a cleaner already actively helping this task.
        public static long CleanupHelper(IAuthorityView view, long tileId, long excludedActor, IEnumerable<DispatchCandidate> candidates, IEnumerable<long> activeHelpers)
        {
            var active = new SortedSet<long>(activeHelpers);
            return AuthorityDispatch.CleanupReplacement(view, new Id<TileTag>(tileId), excludedActor, candidates.Where(c => !active.Contains(c.Person.ActorId)));
        }
        // Urgent facility orders are bounded like escorts: two configured hours (day / 12).
        public static long UrgentAcknowledgmentTicks(long? dayTicks, long? unacknowledgedTicks) => EscortPolicy.UrgentAcknowledgmentTicks(dayTicks, unacknowledgedTicks);
    }
}
