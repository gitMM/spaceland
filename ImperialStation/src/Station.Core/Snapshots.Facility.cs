using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteFacility(BinaryWriter w)
        {
            w.Write(Facility != null); if (Facility == null) return;
            WriteStrings(w, Facility.ReportingRoles); w.Write(Facility.DirtThreshold); w.Write(Facility.RepeatTicks); w.Write(Facility.OrderTicks); w.Write(nextFacilityReview);
            w.Write(facilityObservations.Count);
            foreach (var o in facilityObservations.Values)
            { w.Write(o.ActorId); WriteKey(w, o.Subject); w.Write(o.Adverse); w.Write(o.Generation); w.Write(o.LastObservedTick); w.Write(o.LastQueuedTick); w.Write(o.NextRepeatTick); w.Write(o.OperationKey); w.Write(o.LastMessageId); }
            w.Write(facilityTasks.Count);
            foreach (var t in facilityTasks.Values)
            {
                w.Write(t.JobId); WriteKey(w, t.Subject); w.Write(t.CreatedTick); w.Write(t.FirstMessageId); w.Write(t.LatestMessageId); w.Write(t.LatestObservedTick); w.Write(t.ReportsReceived);
                w.Write(t.AttemptId); w.Write((int)t.Status); w.Write(t.Reason); w.Write(t.CompletionMessageId); w.Write(t.ResolvedObservedTick); w.Write(t.CauseId);
            }
            w.Write(facilityReportKeys.Count); foreach (var pair in facilityReportKeys) { w.Write(pair.Key); w.Write(pair.Value); }
        }
        private void ReadFacility(BinaryReader r)
        {
            if (!r.ReadBoolean())
            {
                if (scheduled.Any(e => e.Kind == ScheduledKind.ReviewFacilityTasks)) throw new ArgumentException("Disabled facility policy has a review timer.");
                return;
            }
            InitializeFacility(new FacilityConfig(ReadStrings(r), r.ReadInt64(), r.ReadInt64(), r.ReadInt64())); nextFacilityReview = r.ReadInt64();
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var o = new FacilityObservationSnapshot { ActorId = r.ReadInt64(), Subject = ReadKey(r), Adverse = r.ReadBoolean(), Generation = r.ReadInt64(), LastObservedTick = r.ReadInt64(),
                    LastQueuedTick = r.ReadInt64(), NextRepeatTick = r.ReadInt64(), OperationKey = r.ReadString(), LastMessageId = r.ReadInt64() };
                if (!actors.TryGetValue(new Id<ActorTag>(o.ActorId), out var actor) || !Facility!.ReportingRoles.Contains(actor.Role, StringComparer.Ordinal) || !FacilitySubject(o.Subject) || o.Generation < 0 || o.LastObservedTick < 0 || o.LastObservedTick > LastProcessedTick)
                    throw new ArgumentException("Invalid local facility observation.");
                if (o.Generation == 0 ? o.LastQueuedTick != -1 || o.NextRepeatTick != -1 || o.OperationKey.Length != 0 || o.LastMessageId != 0 :
                    o.LastQueuedTick < 0 || o.LastQueuedTick > o.LastObservedTick || o.NextRepeatTick != checked(o.LastQueuedTick + Facility.RepeatTicks) || o.OperationKey != "facility/" + o.ActorId + "/" + o.Subject + "/" + o.Generation)
                    throw new ArgumentException("Invalid facility observation generation.");
                if (o.LastMessageId != 0 && (!messages.TryGetValue(o.LastMessageId, out var posted) || posted.PhysicalSender.Value != o.ActorId || posted.Draft.Kind != MessageKind.FacilityCondition || !posted.Draft.Subject.Equals(o.Subject) || posted.Draft.OperationKey != o.OperationKey))
                    throw new ArgumentException("Invalid facility post evidence.");
                if (o.Generation > 0 && o.LastMessageId == 0 && (!reportAgents.TryGetValue(actor.Id, out var pending) || !pending.Pending.Any(p => p.Kind == MessageKind.FacilityCondition && p.Subject.Equals(o.Subject) && p.OperationKey == o.OperationKey)))
                    throw new ArgumentException("Unposted facility observation has no retained report goal.");
                facilityObservations.Add((o.ActorId, o.Subject), o);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var t = new FacilityTaskSnapshot { JobId = r.ReadInt64(), Subject = ReadKey(r), CreatedTick = r.ReadInt64(), FirstMessageId = r.ReadInt64(), LatestMessageId = r.ReadInt64(), LatestObservedTick = r.ReadInt64(),
                    ReportsReceived = r.ReadInt64(), AttemptId = r.ReadInt64(), Status = ReadEnum<FacilityTaskStatus>(r), Reason = r.ReadString(), CompletionMessageId = r.ReadInt64(), ResolvedObservedTick = r.ReadInt64(), CauseId = r.ReadInt64() };
                bool Source(long id, out MessageSnapshot? message)
                { bool present = messages.TryGetValue(id, out var found); message = found; return present && found!.Draft.Kind == MessageKind.FacilityCondition && found.Draft.Subject.Equals(t.Subject) && found.Stage == MessageStage.Accepted; }
                if (!jobs.TryGetValue(t.JobId, out var job) || !JobMatches(job, t.Subject) || !FacilityPublished(t.Subject) || !Source(t.FirstMessageId, out var first) || !Source(t.LatestMessageId, out var latest) ||
                    first!.DeliveryTick != t.CreatedTick || t.LatestObservedTick < first.Draft.ObservedTick || t.LatestObservedTick < latest!.Draft.ObservedTick || t.LatestObservedTick > LastProcessedTick || t.ReportsReceived < 1 || t.CauseId < 1 || t.CauseId >= nextTrace ||
                    t.AttemptId != 0 && (!workAttempts.TryGetValue(t.AttemptId, out var attempt) || attempt.JobId != t.JobId)) throw new ArgumentException("Invalid facility task evidence.");
                if (t.Status == FacilityTaskStatus.ReportedComplete)
                {
                    if (!messages.TryGetValue(t.CompletionMessageId, out var completed) || completed.Draft.Kind != MessageKind.WorkCompleted || completed.Draft.TaskId != t.JobId || completed.Draft.AttemptId != t.AttemptId ||
                        completed.Stage != MessageStage.Accepted || completed.Draft.ObservedTick != t.ResolvedObservedTick || t.ResolvedObservedTick < t.LatestObservedTick) throw new ArgumentException("Invalid facility completion evidence.");
                }
                else if (t.CompletionMessageId != 0 || t.ResolvedObservedTick != -1) throw new ArgumentException("Unresolved facility task has completion evidence.");
                facilityTasks.Add(t.JobId, t); facilityTargets[t.Subject] = t.JobId;
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                string key = r.ReadString(); long id = r.ReadInt64();
                if (!messages.TryGetValue(id, out var m) || m.Draft.Kind != MessageKind.FacilityCondition || m.Stage != MessageStage.Accepted && RuntimeError == null || key != m.SenderAccount.Value + "/" + m.Draft.OperationKey)
                    throw new ArgumentException("Invalid facility receipt idempotency key.");
                facilityReportKeys.Add(key, id);
            }
            foreach (var t in facilityTasks.Values)
                if (!facilityReportKeys.Values.Contains(t.FirstMessageId) || !facilityReportKeys.Values.Contains(t.LatestMessageId)) throw new ArgumentException("Facility task lacks accepted receipt identity.");
            var timers = scheduled.Where(e => e.Kind == ScheduledKind.ReviewFacilityTasks).ToArray();
            if (nextFacilityReview < -1 || nextFacilityReview >= 0 && nextFacilityReview <= LastProcessedTick && RuntimeError == null || timers.Length != (nextFacilityReview < 0 ? 0 : 1) ||
                timers.Any(e => e.Tick != nextFacilityReview || e.Phase != EventPhase.Decision) || RuntimeError == null && facilityTasks.Values.Any(t => t.Status == FacilityTaskStatus.Pending) && nextFacilityReview < 0)
                throw new ArgumentException("Invalid facility dispatch timer.");
        }
    }
}
