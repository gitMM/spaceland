using System;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteAudits(BinaryWriter w)
        {
            w.Write(audits.Count);
            foreach (var a in audits.Values)
            {
                w.Write(a.VerificationAttemptId); w.Write(a.ActorId); w.Write(a.TerminalId); w.Write((int)a.Status); w.Write(a.Reason); w.Write(a.NextDecision); w.Write(a.Backoff);
                w.Write(a.Cursor); w.Write(a.RecordVersion); w.Write(a.AttemptVersion); w.Write(a.ReportVersion); w.Write(a.Pages);
                w.Write(a.Evidence.Count); for (int i = 0; i < a.Evidence.Count; i++) { WriteRecord(w, a.Evidence[i]); w.Write(a.QueryActionIds[i]); }
                w.Write(a.Finding != null);
                if (a.Finding != null) { var f = a.Finding; w.Write(f.Reason); w.Write(f.MachineId); w.Write(f.JobId); w.Write(f.AttemptId); w.Write(f.AccountId); w.Write(f.EvidenceVersions.Count); foreach (long version in f.EvidenceVersions) w.Write(version); }
            }
        }
        private void ReadAudits(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new AuditSnapshot { VerificationAttemptId = r.ReadInt64(), ActorId = r.ReadInt64(), TerminalId = r.ReadInt64(), Status = ReadEnum<AuditStatus>(r), Reason = r.ReadString(),
                    NextDecision = r.ReadInt64(), Backoff = r.ReadInt64(), Cursor = r.ReadInt64(), RecordVersion = r.ReadInt64(), AttemptVersion = r.ReadInt64(), ReportVersion = r.ReadInt64(), Pages = r.ReadInt32() };
                int n = Count(r); if (n > AuditEvidenceLimit) throw new ArgumentException("Audit evidence capacity exceeded.");
                var evidence = new RecordEntry[n]; var actions = new long[n];
                for (int j = 0; j < n; j++) { evidence[j] = ReadRecord(r); actions[j] = r.ReadInt64(); }
                a.Evidence = evidence; a.QueryActionIds = actions;
                if (r.ReadBoolean())
                {
                    string reason = r.ReadString(); long machine = r.ReadInt64(), job = r.ReadInt64(), attempt = r.ReadInt64(), account = r.ReadInt64();
                    n = Count(r); if (n > AuditEvidenceLimit) throw new ArgumentException("Invalid audit finding evidence.");
                    var versions = new long[n]; for (int j = 0; j < n; j++) versions[j] = r.ReadInt64();
                    a.Finding = new AccountabilityFinding(reason, machine, job, attempt, account, versions);
                }
                if (!actors.TryGetValue(new Id<ActorTag>(a.ActorId), out var actor) || !verificationInspections.TryGetValue(a.VerificationAttemptId, out var v) || v.ActorId != a.ActorId || !v.Discrepancy ||
                    !localOrders.TryGetValue(a.VerificationAttemptId, out var order) || order.ReadTick > v.Tick ||
                    a.TerminalId < 0 || a.Pages < a.Evidence.Count || a.Pages > AuditPageLimit || a.Pending && a.Pages >= AuditPageLimit || a.Backoff < planner.InitialBackoff || a.Backoff > planner.MaximumBackoff || a.NextDecision < -1 ||
                    a.Cursor < 0 || a.Cursor > records.Version || a.RecordVersion < -1 || a.RecordVersion > records.Version || a.AttemptVersion < -1 || a.ReportVersion < -1 ||
                    a.RecordVersion != Math.Max(a.AttemptVersion, a.ReportVersion) || a.Pages == 0 && (a.RecordVersion != -1 || a.Evidence.Count > 0) ||
                    a.Status == AuditStatus.QueryingAttempts && a.ReportVersion != -1 || a.Status == AuditStatus.QueryingReports && a.AttemptVersion < 0 ||
                    a.TerminalId == 0 && (a.Status != AuditStatus.NeedsReview || a.Reason != "NoAssignedRecordsTerminal" || a.Pages != 0)) throw new ArgumentException("Invalid retained discrepancy audit.");
                if (a.TerminalId > 0 && (!recordAccess.TryGetValue((a.ActorId, a.TerminalId, RecordKind.WorkAttempt), out var attemptGrant) || attemptGrant.RequiredRole != actor.Role ||
                    !recordAccess.TryGetValue((a.ActorId, a.TerminalId, RecordKind.WorkReport), out var reportGrant) || reportGrant.RequiredRole != actor.Role)) throw new ArgumentException("Audit lacks assigned records access.");
                for (int j = 0; j < evidence.Length; j++)
                {
                    var row = evidence[j];
                    if (!(row.Kind is RecordKind.WorkAttempt or RecordKind.WorkReport) || !records.History.TryGetValue(row.Version, out var original) || row.Kind != original.Kind || row.EntityId != original.EntityId ||
                        row.AccountId != original.AccountId || row.Tick != original.Tick || !row.Fields.SequenceEqual(original.Fields) || row.OperationKey != "disclosed/" + row.Version ||
                        row.Version > (row.Kind == RecordKind.WorkAttempt ? a.AttemptVersion : a.ReportVersion) || actions[j] <= v.ActionId || actions[j] >= nextAction || j > 0 && actions[j] <= actions[j - 1])
                        throw new ArgumentException("Invalid disclosed audit evidence.");
                }
                if (evidence.Select(e => e.Version).Distinct().Count() != evidence.Length) throw new ArgumentException("Duplicate audit evidence.");
                if (a.Finding != null)
                {
                    // A finished scan cannot silently omit an amendment or a competing assignment.
                    var attemptRows = records.History.Values.Where(row => row.Kind == RecordKind.WorkAttempt && row.Version <= a.AttemptVersion &&
                        row.Fields.Any(f => f.Attribute == AttributeKind.MachineId && f.Value.Equals(FactValue.Integer(v.MachineId))) && row.Fields.Any(f => f.Attribute == AttributeKind.TaskKind && f.Value.Text == "RepairMachine")).ToArray();
                    var reportRows = records.History.Values.Where(row => row.Kind == RecordKind.WorkReport && row.Version <= a.ReportVersion &&
                        attemptRows.Any(attempt => row.Fields.Any(f => f.Attribute == AttributeKind.AttemptId && f.Value.Equals(FactValue.Integer(attempt.EntityId)))));
                    if (a.Cursor != a.ReportVersion || !attemptRows.Concat(reportRows).Select(row => row.Version).OrderBy(version => version).SequenceEqual(evidence.Select(row => row.Version).OrderBy(version => version)))
                        throw new ArgumentException("Incomplete finished audit history.");
                    var expected = AccountabilityPolicy.Assess(v.MachineId, v.RequiredResource, v.InstalledResource, v.Tick, a.Evidence, true); var f = a.Finding;
                    if (a.AttemptVersion < 0 || a.ReportVersion < 0 || a.Reason != f.Reason || f.Reason != expected.Reason || f.MachineId != expected.MachineId || f.JobId != expected.JobId || f.AttemptId != expected.AttemptId ||
                        f.AccountId != expected.AccountId || !f.EvidenceVersions.SequenceEqual(expected.EvidenceVersions) || a.Status != (f.Attributable ? AuditStatus.ReadyToFile : AuditStatus.NeedsReview)) throw new ArgumentException("Invalid audit assessment.");
                }
                else if (a.Status == AuditStatus.ReadyToFile) throw new ArgumentException("Missing audit assessment.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.EvaluateAudit && e.Value == a.VerificationAttemptId).ToArray();
                if (timers.Length != (a.NextDecision >= 0 ? 1 : 0) || !a.Pending && a.NextDecision != -1 ||
                    timers.Any(e => e.Tick != a.NextDecision || e.Tick <= LastProcessedTick || e.Phase != EventPhase.Decision)) throw new ArgumentException("Invalid audit decision timer.");
                audits.Add(a.VerificationAttemptId, a);
            }
            if (scheduled.Any(e => e.Kind == ScheduledKind.EvaluateAudit && !audits.ContainsKey(e.Value))) throw new ArgumentException("Unknown audit timer owner.");
        }
    }
}
