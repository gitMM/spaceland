using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // This policy consumes only a completed inspection and records actually disclosed at a desk.
    // It never asserts who physically installed a part. Its conclusion is assigned-account responsibility.
    public static class AccountabilityPolicy
    {
        public const string Version = "unique-assignment/v1";
        private static FactValue? Value(RecordEntry row, AttributeKind key) => row.Fields.Where(f => f.Attribute == key).Select(f => (FactValue?)f.Value).SingleOrDefault();
        private static long Number(RecordEntry row, AttributeKind key) => Value(row, key) is FactValue v && v.Kind == FactValueKind.Number ? v.Number : long.MinValue;
        private static string Text(RecordEntry row, AttributeKind key) => Value(row, key) is FactValue v && v.Kind == FactValueKind.Text ? v.Text : "";
        public static AccountabilityFinding Assess(long machineId, string required, string installed, long inspectionTick, IEnumerable<RecordEntry> disclosed, bool completeHistory)
        {
            AccountabilityFinding Review(string reason) => new AccountabilityFinding(reason, machineId);
            if (!completeHistory) return Review("IncompleteHistory");
            if (required.Length == 0 || installed.Length == 0 || required == installed) return Review("NoAttributablePartMismatch");
            var rows = disclosed.OrderBy(r => r.Version).ToArray();
            var attempts = rows.Where(r => r.Kind == RecordKind.WorkAttempt && Number(r, AttributeKind.MachineId) == machineId && Text(r, AttributeKind.TaskKind) == "RepairMachine")
                .GroupBy(r => r.EntityId).Select(g => g.Last()).Where(r => Number(r, AttributeKind.SubmittedTick) <= inspectionTick).ToArray();
            if (attempts.Length == 0) return Review("MissingAssignment");
            // Conservatively refuse attribution when more than one repair assignment could explain
            // this slot. A later policy may narrow this using an explicitly documented handoff.
            if (attempts.Length != 1) return Review("AmbiguousHandoff");
            var attempt = attempts[0]; long job = Number(attempt, AttributeKind.TaskId), id = Number(attempt, AttributeKind.AttemptId);
            long dispatch = Number(attempt, AttributeKind.SubmittedTick), expiry = Number(attempt, AttributeKind.Expires), acknowledged = Number(attempt, AttributeKind.AcknowledgedTick);
            if (job < 1 || id != attempt.EntityId || dispatch < 0 || expiry <= dispatch || acknowledged < dispatch || acknowledged > inspectionTick || Number(attempt, AttributeKind.MessageId) < 1 ||
                Text(attempt, AttributeKind.RequiredResource) != required) return Review("IncompleteAssignment");
            if (Text(attempt, AttributeKind.OrderStatus) != "Active") return Review("AssignmentAmended");
            var reports = rows.Where(r => r.Kind == RecordKind.WorkReport && Number(r, AttributeKind.AttemptId) == id).ToArray();
            if (reports.Any(r => r.AccountId != attempt.AccountId || Number(r, AttributeKind.TaskId) != job || Number(r, AttributeKind.TerminalId) < 1 ||
                Number(r, AttributeKind.ActorId) < 1 || Number(r, AttributeKind.MessageId) != r.EntityId || Number(r, AttributeKind.SubmittedTick) < dispatch ||
                Number(r, AttributeKind.ReceivedTick) != r.Tick || r.Tick < Number(r, AttributeKind.SubmittedTick) || Number(r, AttributeKind.ObservedTick) < dispatch || Number(r, AttributeKind.ObservedTick) > r.Tick))
                return Review("InvalidReportProvenance");
            if (reports.Any(r => Text(r, AttributeKind.ReportKind) is "OrderRejected" or "WorkBlocked" or "UnableToContinue")) return Review("ConflictingAmendment");
            var acceptance = reports.Where(r => Text(r, AttributeKind.ReportKind) == "OrderAccepted" && r.Tick == acknowledged).ToArray();
            var completion = reports.Where(r => Text(r, AttributeKind.ReportKind) == "WorkCompleted" && r.Tick <= inspectionTick).ToArray();
            if (acceptance.Length != 1 || completion.Length == 0) return Review("MissingSubmission");
            bool Successful(RecordEntry row)
            {
                var claim = Value(row, AttributeKind.ReportClaim);
                return Text(row, AttributeKind.ReportSubjectKind) == "Machine" && Number(row, AttributeKind.ReportSubjectId) == machineId && Text(row, AttributeKind.ReportAttribute) == "Operational" && claim.HasValue && claim.Value.Equals(FactValue.Boolean(true)) ||
                    Text(row, AttributeKind.ReportSubjectKind) == "Attempt" && Number(row, AttributeKind.ReportSubjectId) == id && Text(row, AttributeKind.ReportAttribute) == "WorkStatus" && claim.HasValue && claim.Value.Kind == FactValueKind.Text && claim.Value.Text is "ReportedComplete" or "NoWorkNeeded";
            }
            if (completion.Any(r => !Successful(r) || r.Tick < acknowledged) || reports.Any(r => Text(r, AttributeKind.ReportKind) == "WorkCompleted" && r.Tick > inspectionTick)) return Review("AmbiguousCompletion");
            return new AccountabilityFinding("WrongInstalledPart", machineId, job, id, attempt.AccountId,
                new[] { attempt.Version, acceptance[0].Version }.Concat(completion.Select(r => r.Version)));
        }
    }
}
