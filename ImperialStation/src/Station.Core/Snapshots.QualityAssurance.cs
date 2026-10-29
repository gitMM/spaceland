using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteQualityAssurance(BinaryWriter w)
        {
            w.Write(QualityAssurance != null);
            if (QualityAssurance != null) { w.Write(QualityAssurance.SamplePermille); w.Write(QualityAssurance.DelayTicks); w.Write(QualityAssurance.OrderTicks); w.Write(QualityAssurance.SupervisorRole); }
            w.Write(nextQualityReview); w.Write(qualityChecks.Count);
            foreach (var q in qualityChecks.Values)
            {
                w.Write(q.RepairAttemptId); w.Write(q.RepairJobId); w.Write(q.MachineId); w.Write(q.CompletionMessageId); w.Write(q.ReceivedTick); w.Write(q.SampleRoll); w.Write(q.DueTick); w.Write((int)q.Status);
                w.Write(q.VerificationJobId); w.Write(q.VerificationAttemptId); w.Write(q.ResultMessageId); w.Write(q.Reason); w.Write(q.CauseId);
            }
            w.Write(verificationInspections.Count);
            foreach (var v in verificationInspections.Values) { w.Write(v.AttemptId); w.Write(v.ActorId); w.Write(v.MachineId); w.Write(v.Tick); w.Write(v.ActionId); w.Write(v.Operational); w.Write(v.RequiredResource); w.Write(v.InstalledResource); }
        }
        private void ReadQualityAssurance(BinaryReader r)
        {
            if (r.ReadBoolean()) QualityAssurance = new QualityAssuranceConfig(r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadString());
            if (QualityAssurance != null && (Authority == null || !JobsEnabled || publicKnowledge?.Work == null) || streams.ContainsKey("qa") != (QualityAssurance != null)) throw new ArgumentException("Invalid QA configuration or stream.");
            nextQualityReview = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var q = new QualityAssuranceSnapshot { RepairAttemptId = r.ReadInt64(), RepairJobId = r.ReadInt64(), MachineId = r.ReadInt64(), CompletionMessageId = r.ReadInt64(), ReceivedTick = r.ReadInt64(),
                    SampleRoll = r.ReadInt32(), DueTick = r.ReadInt64(), Status = ReadEnum<QualityAssuranceStatus>(r), VerificationJobId = r.ReadInt64(), VerificationAttemptId = r.ReadInt64(), ResultMessageId = r.ReadInt64(), Reason = r.ReadString(), CauseId = r.ReadInt64() };
                if (QualityAssurance == null || !workAttempts.TryGetValue(q.RepairAttemptId, out var source) || source.JobId != q.RepairJobId || jobs[source.JobId].Kind != WorkKind.RepairMachine || jobs[source.JobId].MachineId != q.MachineId ||
                    !messages.TryGetValue(q.CompletionMessageId, out var report) || report.Draft.Kind != MessageKind.WorkCompleted || report.Draft.AttemptId != source.Id || report.Draft.TaskId != source.JobId || report.Stage != MessageStage.Accepted || report.SenderAccount.Value != source.AccountId ||
                    report.DeliveryTick != q.ReceivedTick || q.ReceivedTick < 0 || q.ReceivedTick > LastProcessedTick || q.SampleRoll < 0 || q.SampleRoll >= 1000 || q.CauseId < 1 || q.CauseId >= nextTrace ||
                    source.ReportMessageIds.FirstOrDefault(id => messages[id].Draft.Kind == MessageKind.WorkCompleted) != report.Id) throw new ArgumentException("Invalid QA receipt provenance.");
                bool sampled = q.SampleRoll < QualityAssurance.SamplePermille;
                if (sampled ? q.Status == QualityAssuranceStatus.NotSampled || q.DueTick != checked(q.ReceivedTick + QualityAssurance.DelayTicks) : q.Status != QualityAssuranceStatus.NotSampled || q.DueTick != -1)
                    throw new ArgumentException("Invalid QA sampling decision.");
                if (sampled && q.Status != QualityAssuranceStatus.Scheduled && q.DueTick > LastProcessedTick) throw new ArgumentException("QA advanced before its due tick.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.QualityDue && e.Value == q.RepairAttemptId).ToArray();
                if (timers.Length != (q.Status == QualityAssuranceStatus.Scheduled ? 1 : 0) || timers.Any(e => e.Tick != q.DueTick || e.Phase != EventPhase.Decision) ||
                    q.Status == QualityAssuranceStatus.Scheduled && RuntimeError == null && q.DueTick <= LastProcessedTick) throw new ArgumentException("Invalid QA due timer.");
                if (q.VerificationJobId == 0)
                {
                    if (!(q.Status is QualityAssuranceStatus.NotSampled or QualityAssuranceStatus.Scheduled or QualityAssuranceStatus.PendingSupervisor) || q.VerificationAttemptId != 0 || q.ResultMessageId != 0) throw new ArgumentException("Unassigned QA has assignment evidence.");
                }
                else if (!jobs.TryGetValue(q.VerificationJobId, out var job) || job.Kind != WorkKind.VerifyMachine || job.MachineId != q.MachineId || !workAttempts.TryGetValue(q.VerificationAttemptId, out var a) || a.JobId != job.Id ||
                    a.DispatchTick < q.DueTick || q.Status is QualityAssuranceStatus.NotSampled or QualityAssuranceStatus.Scheduled or QualityAssuranceStatus.PendingSupervisor) throw new ArgumentException("Invalid QA assignment.");
                if (q.Status is QualityAssuranceStatus.ReportedClear or QualityAssuranceStatus.ReportedDiscrepancy)
                {
                    if (!messages.TryGetValue(q.ResultMessageId, out var result) || result.Stage != MessageStage.Accepted || result.Draft.Kind != MessageKind.WorkCompleted || result.Draft.AttemptId != q.VerificationAttemptId || result.Draft.TaskId != q.VerificationJobId ||
                        result.SenderAccount.Value != workAttempts[q.VerificationAttemptId].AccountId || !workAttempts[q.VerificationAttemptId].ReportMessageIds.Contains(result.Id) ||
                        !result.Draft.Subject.Equals(new FactKey(SubjectKind.Machine, q.MachineId, AttributeKind.Operational)) || result.Draft.Claim.Kind != FactValueKind.Boolean ||
                        result.Draft.Claim.Equals(FactValue.Boolean(true)) != (q.Status == QualityAssuranceStatus.ReportedClear)) throw new ArgumentException("Invalid QA result.");
                }
                else if (q.ResultMessageId != 0) throw new ArgumentException("Unresolved QA has a result.");
                qualityChecks.Add(q.RepairAttemptId, q);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var v = new VerificationInspectionSnapshot { AttemptId = r.ReadInt64(), ActorId = r.ReadInt64(), MachineId = r.ReadInt64(), Tick = r.ReadInt64(), ActionId = r.ReadInt64(), Operational = r.ReadBoolean(), RequiredResource = r.ReadString(), InstalledResource = r.ReadString() };
                if (!localOrders.TryGetValue(v.AttemptId, out var order) || order.Kind != WorkKind.VerifyMachine || order.ActorId != v.ActorId || order.MachineId != v.MachineId || v.Tick < order.ReadTick || v.Tick > order.Expires || v.Tick > LastProcessedTick ||
                    v.ActionId < 1 || v.ActionId >= nextAction || !resources.ContainsKey(v.RequiredResource) || v.InstalledResource.Length > 0 && !resources.ContainsKey(v.InstalledResource)) throw new ArgumentException("Invalid retained verification inspection.");
                verificationInspections.Add(v.AttemptId, v);
            }
            var reviews = scheduled.Where(e => e.Kind == ScheduledKind.ReviewQuality).ToArray();
            if (QualityAssurance == null && (nextQualityReview != -1 || qualityChecks.Count > 0 || scheduled.Any(e => e.Kind is ScheduledKind.QualityDue or ScheduledKind.ReviewQuality))) throw new ArgumentException("Disabled QA has authority state.");
            if (nextQualityReview < -1 || nextQualityReview >= 0 && RuntimeError == null && nextQualityReview <= LastProcessedTick || reviews.Length != (nextQualityReview < 0 ? 0 : 1) || reviews.Any(e => e.Tick != nextQualityReview || e.Phase != EventPhase.Decision) ||
                RuntimeError == null && qualityChecks.Values.Any(q => q.Status == QualityAssuranceStatus.PendingSupervisor) && nextQualityReview < 0 ||
                scheduled.Any(e => e.Kind == ScheduledKind.QualityDue && !qualityChecks.ContainsKey(e.Value))) throw new ArgumentException("Invalid QA dispatch timers.");
        }
    }
}
