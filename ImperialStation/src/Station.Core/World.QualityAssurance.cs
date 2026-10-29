using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public QualityAssuranceConfig? QualityAssurance { get; private set; }
        private readonly SortedDictionary<long, QualityAssuranceSnapshot> qualityChecks = new SortedDictionary<long, QualityAssuranceSnapshot>();
        private readonly SortedDictionary<long, VerificationInspectionSnapshot> verificationInspections = new SortedDictionary<long, VerificationInspectionSnapshot>();
        private long nextQualityReview = -1;
        public IReadOnlyList<QualityAssuranceSnapshot> InspectQualityAssurance() => qualityChecks.Values.Select(q => q.Copy()).ToArray();
        public IReadOnlyList<VerificationInspectionSnapshot> InspectVerifications(Id<ActorTag> actor) => verificationInspections.Values.Where(v => v.ActorId == actor.Value).Select(v => v.Copy()).ToArray();
        private void InitializeQualityAssurance(QualityAssuranceConfig config)
        {
            if (Authority == null || !JobsEnabled || publicKnowledge?.Work == null) throw new ArgumentException("QA requires authority, jobs and a published work catalog.");
            QualityAssurance = config; streams.Add("qa", Pcg32.Create(Seed, "qa"));
        }
        private void QualityWorkReported(MessageSnapshot message, WorkAttemptSnapshot attempt)
        {
            var draft = message.Draft; var job = jobs[attempt.JobId];
            if (QualityAssurance != null && job.Kind == WorkKind.RepairMachine && draft.Kind == MessageKind.WorkCompleted && !qualityChecks.ContainsKey(attempt.Id))
            {
                int roll = streams["qa"].Next(1000); bool sample = roll < QualityAssurance.SamplePermille;
                var check = new QualityAssuranceSnapshot { RepairAttemptId = attempt.Id, RepairJobId = job.Id, MachineId = job.MachineId, CompletionMessageId = message.Id,
                    ReceivedTick = processingTick, SampleRoll = roll, DueTick = sample ? checked(processingTick + QualityAssurance.DelayTicks) : -1,
                    Status = sample ? QualityAssuranceStatus.Scheduled : QualityAssuranceStatus.NotSampled };
                check.CauseId = Emit("QualitySampled", messageId: message.Id, attemptId: attempt.Id, detail: "roll=" + roll + "; selected=" + sample);
                qualityChecks.Add(attempt.Id, check);
                if (sample) Schedule(new ScheduledEvent { Tick = check.DueTick, Phase = EventPhase.Decision, Kind = ScheduledKind.QualityDue, Value = attempt.Id, CauseId = check.CauseId });
            }
            if (job.Kind != WorkKind.VerifyMachine || jobs[attempt.JobId].CurrentAttemptId != attempt.Id) return;
            var q = qualityChecks.Values.SingleOrDefault(c => c.VerificationJobId == job.Id);
            if (q == null || q.ResultMessageId > 0) return;
            q.VerificationAttemptId = attempt.Id;
            if (draft.Kind == MessageKind.WorkCompleted)
            { q.ResultMessageId = message.Id; q.Status = draft.Claim.Equals(FactValue.Boolean(true)) ? QualityAssuranceStatus.ReportedClear : QualityAssuranceStatus.ReportedDiscrepancy; q.Reason = "VerificationClaimReceived"; }
            else if (draft.Kind is MessageKind.OrderRejected or MessageKind.WorkBlocked or MessageKind.UnableToContinue)
            { q.Status = QualityAssuranceStatus.NeedsAttention; q.Reason = draft.Claim.Text; }
            else return;
            Emit("QualityResultReceived", attemptId: attempt.Id, messageId: message.Id, detail: q.Status.ToString(), causeId: q.CauseId);
        }
        private void QualityDue(long attempt)
        {
            var q = qualityChecks[attempt]; if (q.Status != QualityAssuranceStatus.Scheduled) return;
            q.Status = QualityAssuranceStatus.PendingSupervisor; RequestQualityReview(processingTick);
        }
        private IEnumerable<DispatchCandidate> VerificationCandidates(IAuthorityView view) => view.Personnel
            .Where(p => p.Role == (QualityAssurance?.SupervisorRole ?? "supervisor") && p.Roster != null && p.Roster.OnDuty(view.Tick))
            .Select(Candidate).Where(c => !c.Committed && c.KnownOrigin.HasValue);
        private long SelectVerification(IAuthorityView view, long machine, long excludedActor, IEnumerable<DispatchCandidate>? available = null)
        {
            var target = view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == machine); if (target == null) return 0;
            // Reject ineligible roles/rosters before evaluating their commitments.
            // Candidate reads the work history; scanning it for every citizen for
            // every pending verification made concurrent QA quadratic in population.
            return (available ?? VerificationCandidates(view)).Where(c => c.Person.ActorId != excludedActor)
                .OrderBy(c => TravelEstimates.Authority(c.KnownOrigin!.Value, target.Position, view.NominalTravelTicks)).ThenBy(c => c.Person.ActorId).Select(c => c.Person.ActorId).FirstOrDefault();
        }
        private void RequestQualityReview(long tick)
        {
            if (QualityAssurance == null || !qualityChecks.Values.Any(q => q.Status == QualityAssuranceStatus.PendingSupervisor)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (nextQualityReview >= 0 && nextQualityReview <= tick) return;
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ReviewQuality).ToArray()) scheduled.Remove(e);
            nextQualityReview = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.ReviewQuality });
        }
        private void ReviewQuality()
        {
            nextQualityReview = -1; var view = AuthorityView();
            var available = VerificationCandidates(view).ToList();
            foreach (var q in qualityChecks.Values.Where(q => q.Status == QualityAssuranceStatus.PendingSupervisor).OrderBy(q => q.DueTick).ThenBy(q => q.RepairAttemptId))
            {
                long supervisor = SelectVerification(view, q.MachineId, workAttempts[q.RepairAttemptId].ActorId, available);
                if (supervisor == 0) { q.Reason = "NoKnownOnDutyUncommittedSupervisor"; continue; }
                long id = nextJob; nextJob = checked(nextJob + 1); jobs.Add(id, new JobSnapshot { Id = id, Kind = WorkKind.VerifyMachine, MachineId = q.MachineId });
                var attempt = CreateWorkAttempt(processingTick, new AssignmentSeed("qa/" + q.RepairAttemptId, id, new Id<ActorTag>(supervisor), checked(processingTick + QualityAssurance!.OrderTicks)));
                q.VerificationJobId = id; q.VerificationAttemptId = attempt.Id; q.Status = QualityAssuranceStatus.Assigned; q.Reason = "VerificationOrderPosted";
                DispatchWork(attempt.Id, q.CauseId);
                available.RemoveAll(c => c.Person.ActorId == supervisor);
            }
            if (!qualityChecks.Values.Any(q => q.Status == QualityAssuranceStatus.PendingSupervisor)) return;
            long next = NextHourTick(HourBucket(processingTick));
            foreach (var p in view.Personnel.Where(p => p.Roster != null)) next = Math.Min(next, p.Roster!.NextBoundary(processingTick));
            foreach (var a in workAttempts.Values.Where(a => a.Expires > processingTick)) next = Math.Min(next, a.Expires);
            RequestQualityReview(next);
        }
    }
}
