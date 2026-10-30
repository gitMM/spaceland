using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Missing-report incident response: responder choice, depth and admission rules.
    public static class IncidentPolicy
    {
        public static long KnownOnDutyHeadcount(IAuthorityView view, int cohort, long tick) =>
            view.Personnel.LongCount(p => p.Roster != null && p.Roster.Cohort == cohort && p.Roster.OnDuty(tick));
        // Every cohort-hour admits at least one escalation.
        public static long AdmissionBudget(long knownOnDutyHeadcount, AuthorityConfig policy) => Math.Max(1, checked(knownOnDutyHeadcount * policy.IncidentAdmissionPermille) / 1000);
        public static bool DepthLimited(int replacementDepth, AuthorityConfig policy) => replacementDepth >= policy.MaximumReplacementDepth;
        // An incident not yet admitted waits once its cohort-hour budget is spent.
        public static bool AdmissionDeferred(long admissionBucket, long used, long budget) => admissionBucket < 0 && used >= budget;
        // The published location of a job's target, or null when it is not published.
        public static Coord? JobSite(IAuthorityView view, AuthorityJobTarget job) =>
            job.Kind == WorkKind.ConstructHabitat ? job.Construction!.Site : job.Kind != WorkKind.CleanTile ? view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == job.MachineId)?.Position :
                view.Publication.Work?.Cleanable.SingleOrDefault(c => c.Tile.Value == job.TileId)?.Position;
        // A replacement responder by job kind. Construction and verification gather their
        // own eligible people; repair and cleanup share the review's candidate list.
        public static long Responder(IAuthorityView view, AuthorityJobTarget job, long excludedActor, string supervisorRole,
            Func<KnownPersonnel, DispatchCandidate> gather, IEnumerable<DispatchCandidate> candidates) =>
            job.Kind == WorkKind.ConstructHabitat ? ConstructionPlanning.Worker(view, job.Construction!, excludedActor, gather) :
            job.Kind == WorkKind.VerifyMachine ? QualityPolicy.Verifier(view, job.MachineId, excludedActor, QualityPolicy.VerificationCandidates(view, supervisorRole, gather)) :
            job.Kind == WorkKind.RepairMachine ? AuthorityDispatch.Replacement(view, job.MachineId, excludedActor, candidates) :
            AuthorityDispatch.CleanupReplacement(view, new Id<TileTag>(job.TileId), excludedActor, candidates);
        // A received completion removes the need for an unposted replacement.
        public static bool CancelOnOriginalResult(MessageKind kind, bool needsWorkResponse) => kind == MessageKind.WorkCompleted && needsWorkResponse;
        // An ended parent assignment cancels a still-unposted replacement.
        public static bool CancelOnParentEnded(long replacementAttemptId, bool needsWorkResponse) => replacementAttemptId == 0 && needsWorkResponse;
        // Received replies close a search only while no patrol order was issued.
        public static bool ReplyClosesSearch(long patrolOrderMessageId) => patrolOrderMessageId == 0;
        public static string WorkResolution(MessageKind kind, bool viaReplacementChain)
        {
            string resolution = kind == MessageKind.WorkCompleted ? "ReportedComplete" : kind == MessageKind.WorkBlocked ? "ReportedBlocked" : "ReportedUnableToContinue";
            return viaReplacementChain ? resolution + "ByReplacementChain" : resolution;
        }
    }
}
