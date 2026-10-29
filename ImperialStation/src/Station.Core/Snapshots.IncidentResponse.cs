using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteIncidentResponses(BinaryWriter w)
        {
            w.Write(nextAdmissionBucketTick); w.Write(nextIncidentReview);
            w.Write(admissionBuckets.Count);
            foreach (var b in admissionBuckets.Values)
            { w.Write(b.Bucket); w.Write(b.Cohort); w.Write(b.OpenedTick); w.Write(b.KnownOnDutyHeadcount); w.Write(b.Budget); w.Write(b.Used); }
            w.Write(missingReports.Count);
            foreach (var i in missingReports.Values)
            {
                w.Write(i.AttemptId); w.Write(i.OpeningEventId); w.Write(i.AdmissionBucket); w.Write(i.AdmissionTick); w.Write(i.ReplacementAttemptId); w.Write(i.ReplacementDepth);
                w.Write((int)i.WorkResponse); w.Write(i.WorkerStatusResponse); w.Write(i.ResponseReason);
                w.Write(i.ReplacementResultMessageId); w.Write(i.WorkResolution);
                w.Write(i.PatrolOrderMessageId); w.Write(i.PatrolResultMessageId); w.Write(i.PatrolReason);
            }
        }
        private void ReadIncidentResponses(BinaryReader r)
        {
            nextAdmissionBucketTick = r.ReadInt64(); nextIncidentReview = r.ReadInt64(); int count = Count(r);
            for (int index = 0; index < count; index++)
            {
                var b = new IncidentAdmissionBucket { Bucket = r.ReadInt64(), Cohort = r.ReadInt32(), OpenedTick = r.ReadInt64(), KnownOnDutyHeadcount = r.ReadInt64(), Budget = r.ReadInt64(), Used = r.ReadInt64() };
                if (b.Bucket < 0 || b.OpenedTick < 0 || HourBucket(b.OpenedTick) != b.Bucket || b.Cohort < -1 || b.Cohort >= (Routines?.Cohorts ?? 0) || b.KnownOnDutyHeadcount < 0 || b.Used < 0 || b.Used > b.Budget ||
                    b.Budget != Math.Max(1, checked(b.KnownOnDutyHeadcount * Authority!.IncidentAdmissionPermille) / 1000)) throw new ArgumentException("Invalid incident admission budget.");
                long headcount = rosters.Values.Where(e => e.Published && e.PublicationTick <= b.OpenedTick).GroupBy(e => e.Roster.ActorId)
                    .Select(g => g.OrderByDescending(e => e.Roster.Version).First().Roster).LongCount(p => p.Cohort == b.Cohort && p.OnDuty(b.OpenedTick));
                if (headcount != b.KnownOnDutyHeadcount) throw new ArgumentException("Admission headcount does not match known opening rosters.");
                admissionBuckets.Add((b.Bucket, b.Cohort), b);
            }
            count = Count(r); if (count != missingReports.Count) throw new ArgumentException("Incomplete response goals.");
            long previous = 0;
            for (int index = 0; index < count; index++)
            {
                long attempt = r.ReadInt64(); if (attempt <= previous || !missingReports.TryGetValue(attempt, out var i)) throw new ArgumentException("Invalid response incident."); previous = attempt;
                i.OpeningEventId = r.ReadInt64(); i.AdmissionBucket = r.ReadInt64(); i.AdmissionTick = r.ReadInt64(); i.ReplacementAttemptId = r.ReadInt64(); i.ReplacementDepth = r.ReadInt32();
                i.WorkResponse = ReadEnum<IncidentWorkResponse>(r); i.WorkerStatusResponse = r.ReadString(); i.ResponseReason = r.ReadString();
                i.ReplacementResultMessageId = r.ReadInt64(); i.WorkResolution = r.ReadString();
                i.PatrolOrderMessageId = r.ReadInt64(); i.PatrolResultMessageId = r.ReadInt64(); i.PatrolReason = r.ReadString();
                if (i.ReplacementDepth < 0 || i.ReplacementDepth > Authority!.MaximumReplacementDepth) throw new ArgumentException("Invalid incident goal depth.");
                if (i.OpeningEventId < 1 || i.OpeningEventId >= nextTrace) throw new ArgumentException("Invalid incident opening cause.");
                if (RuntimeError == null && (i.ReplacementAttemptId > 0) != (i.WorkResponse == IncidentWorkResponse.Dispatched)) throw new ArgumentException("Invalid replacement goal state.");
                if (i.ReplacementAttemptId > 0)
                {
                    if (!workAttempts.TryGetValue(i.ReplacementAttemptId, out var child) || child.JobId != i.JobId || child.ActorId == i.ActorId || child.Id <= i.AttemptId || child.DispatchKey != "incident/" + i.Id + "/replacement" ||
                        i.AdmissionTick > child.DispatchTick)
                        throw new ArgumentException("Invalid committed replacement evidence.");
                }
                if (i.PatrolOrderMessageId > 0)
                {
                    if (!patrolOrders.TryGetValue(i.PatrolOrderMessageId, out var patrol) || patrol.Payload.IncidentId != i.Id || i.PatrolResultMessageId != patrol.ResultMessageId ||
                        i.WorkerStatusResponse != (patrol.ResultMessageId > 0 ? patrol.Status : "PatrolPosted")) throw new ArgumentException("Invalid patrol branch evidence.");
                }
                else if (i.PatrolResultMessageId != 0 || !(i.WorkerStatusResponse == "PendingPatrol" || i.WorkerStatusResponse == "ResponseReceived" && i.ResponseMessageId > 0)) throw new ArgumentException("Patrol resolution without order.");
                if (i.ReplacementAttemptId > 0 || i.PatrolOrderMessageId > 0)
                {
                    long firstPosted = Math.Min(i.ReplacementAttemptId > 0 ? workAttempts[i.ReplacementAttemptId].DispatchTick : long.MaxValue, i.PatrolOrderMessageId > 0 ? messages[i.PatrolOrderMessageId].SubmittedTick : long.MaxValue);
                    if (i.AdmissionTick != firstPosted || i.AdmissionTick < i.OpenedTick || HourBucket(i.AdmissionTick) != i.AdmissionBucket || !admissionBuckets.ContainsKey((i.AdmissionBucket, i.Cohort))) throw new ArgumentException("Invalid first-response admission.");
                }
                else if (i.AdmissionBucket != -1 || i.AdmissionTick != -1) throw new ArgumentException("Admission token without committed response.");
            }
            // An ancestor can reference a later replacement. Read every branch
            // before walking the chain used to authenticate its received outcome.
            foreach (var i in missingReports.Values)
            {
                if (i.ReplacementResultMessageId == 0)
                { if (i.WorkResolution != "Unverified") throw new ArgumentException("Work resolution lacks report evidence."); }
                else
                {
                    long descendant = i.ReplacementAttemptId;
                    var visited = new SortedSet<long>();
                    while (descendant > 0 && visited.Add(descendant) && expectations.TryGetValue(descendant, out var current) && current.ResultMessageId != i.ReplacementResultMessageId)
                        descendant = missingReports.TryGetValue(descendant, out var child) ? child.ReplacementAttemptId : 0;
                    if (!expectations.TryGetValue(descendant, out var expectation) || expectation.ResultMessageId != i.ReplacementResultMessageId || !messages.TryGetValue(i.ReplacementResultMessageId, out var result)) throw new ArgumentException("Invalid replacement result evidence.");
                    string resolution = result.Draft.Kind == MessageKind.WorkCompleted ? "ReportedComplete" : result.Draft.Kind == MessageKind.WorkBlocked ? "ReportedBlocked" : "ReportedUnableToContinue";
                    if (descendant != i.ReplacementAttemptId) resolution += "ByReplacementChain";
                    if (i.WorkResolution != resolution) throw new ArgumentException("Replacement resolution disagrees with received claim.");
                }
            }
            foreach (var i in missingReports.Values)
            {
                var parent = missingReports.Values.SingleOrDefault(p => p.ReplacementAttemptId == i.AttemptId);
                if (i.ReplacementDepth != (parent == null ? 0 : parent.ReplacementDepth + 1)) throw new ArgumentException("Invalid replacement ancestry.");
            }
            foreach (var b in admissionBuckets.Values)
                if (b.Used != missingReports.Values.LongCount(i => i.AdmissionBucket == b.Bucket && i.Cohort == b.Cohort)) throw new ArgumentException("Admission usage does not match tokens.");
            ValidateResponseTimer(ScheduledKind.OpenAdmissionBucket, nextAdmissionBucketTick);
            ValidateResponseTimer(ScheduledKind.ReviewIncidents, nextIncidentReview);
            if (RuntimeError == null)
            {
                if (nextAdmissionBucketTick != (LastProcessedTick < 0 ? 0 : NextHourTick(HourBucket(LastProcessedTick)))) throw new ArgumentException("Invalid next hour boundary.");
                if (missingReports.Values.Any(NeedsIncidentResponse) && nextIncidentReview < 0) throw new ArgumentException("Pending incident queue lacks a review.");
            }
        }
        private void ValidateResponseTimer(ScheduledKind kind, long tick)
        {
            var events = scheduled.Where(e => e.Kind == kind).ToArray();
            if (tick < -1 || events.Length > 1 || events.Any(e => e.Tick != tick || e.Phase != EventPhase.Decision) ||
                RuntimeError == null && (events.Length != (tick < 0 ? 0 : 1) || tick >= 0 && tick <= LastProcessedTick)) throw new ArgumentException("Invalid incident response timer.");
        }
    }
}
