using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // A citizen's discrepancy audit: which records desk to use, when to defer, how to read
    // each disclosed page and when to stop. Core executes queries and applies the result.
    public static class AuditPlanning
    {
        private static readonly AttributeKind[] Metadata = { AttributeKind.RecordType, AttributeKind.RecordEntityId, AttributeKind.RecordVersion, AttributeKind.AccountId, AttributeKind.RecordFieldCount };
        // The first (lowest) terminal at which the auditor holds role-matched grants for both work-record kinds.
        public static long ChooseTerminal(IEnumerable<(long Terminal, RecordKind Kind, string Role)> grants, string role)
        {
            var all = grants.ToArray();
            return all.Where(g => g.Role == role && g.Kind == RecordKind.WorkAttempt && all.Any(r => r.Terminal == g.Terminal && r.Kind == RecordKind.WorkReport && r.Role == role))
                .Select(g => g.Terminal).FirstOrDefault();
        }
        public static bool ReadyToFile(AccountabilityFinding? finding) => finding?.Attributable == true;
        // Physical progress at the audit desk (not merely finding the same plan) resets the retry delay.
        public static bool ProgressResetsBackoff(ActionKind completedKind, long completedService, long auditTerminal) =>
            completedService == auditTerminal && completedKind is ActionKind.Login or ActionKind.QueryRecords;
        // Other personal work that outranks this audit once needs, due goals and duty are settled.
        public static bool DefersToOtherWork(bool earlierAuditPending, bool needPlanningOwnsReports, bool reportsPending, bool checkInDue, bool orderInProgress) =>
            earlierAuditPending || !needPlanningOwnsReports && (reportsPending || checkInDue) || orderInProgress;
        public static string PlanFailureReason(PlanOutcome outcome) => outcome == PlanOutcome.BudgetExhausted ? "PlanBudgetExhausted" : "NoKnownRecordsRouteOrCredential";
        public static AuditPageResult ReadPage(AuditPageInput a)
        {
            var evidence = a.Evidence; var actions = a.QueryActionIds;
            AuditPageResult Stop(string reason) => new AuditPageResult(evidence, actions, null, false, reason);
            // Read only facts actually learned by this completed action. CapturedRecords is a service
            // implementation detail and is deliberately unavailable to this citizen decision.
            var groups = a.Learned.GroupBy(f => f.Key.SubjectId).ToArray();
            if (groups.Length != a.Receipt.DisclosedRows) return Stop("IncompleteDisclosure");
            foreach (var group in groups)
            {
                FactValue? Field(AttributeKind key) => group.Where(f => f.Key.Attribute == key).Select(f => (FactValue?)f.Value).SingleOrDefault();
                long Num(AttributeKind key) => Field(key) is FactValue f && f.Kind == FactValueKind.Number ? f.Number : -1;
                var fields = group.Where(f => !Metadata.Contains(f.Key.Attribute)).Select(f => new RecordField(f.Key.Attribute, f.Value)).ToArray();
                if (Num(AttributeKind.RecordVersion) != group.Key || Num(AttributeKind.RecordEntityId) < 1 || Num(AttributeKind.AccountId) < 1 || Num(AttributeKind.RecordFieldCount) != fields.Length ||
                    Field(AttributeKind.RecordType)?.Text != a.Kind.ToString() || group.Select(f => f.ObservedTick).Distinct().Count() != 1)
                    return Stop("IncompleteDisclosure");
                if (group.Key > a.Cutoff) continue;
                var row = new RecordEntry(a.Kind, Num(AttributeKind.RecordEntityId), Num(AttributeKind.AccountId), group.Key, group.First().ObservedTick, "disclosed/" + group.Key, fields);
                bool relevant = row.Kind == RecordKind.WorkAttempt ? fields.Any(f => f.Attribute == AttributeKind.MachineId && f.Value.Equals(FactValue.Integer(a.MachineId))) && fields.Any(f => f.Attribute == AttributeKind.TaskKind && f.Value.Text == "RepairMachine") :
                    evidence.Any(r => r.Kind == RecordKind.WorkAttempt && fields.Any(f => f.Attribute == AttributeKind.AttemptId && f.Value.Equals(FactValue.Integer(r.EntityId))));
                if (!relevant) continue;
                if (evidence.Count >= a.EvidenceLimit) return Stop("EvidenceCapacityExceeded");
                evidence = evidence.Concat(new[] { row }).ToArray(); actions = actions.Concat(new[] { a.ActionId }).ToArray();
            }
            long cursor = Math.Min(a.Receipt.NextCursor, a.Cutoff);
            string? pageLimit = a.Pages >= a.PageLimit ? "HistoryPageLimitExceeded" : null;
            if (a.Receipt.HasMore && a.Receipt.NextCursor < a.Cutoff) return new AuditPageResult(evidence, actions, cursor, false, pageLimit);
            if (a.QueryingAttempts) return new AuditPageResult(evidence, actions, 0, true, pageLimit);
            var finding = AccountabilityPolicy.Assess(a.MachineId, a.RequiredResource, a.InstalledResource, a.InspectionTick, evidence, true);
            return new AuditPageResult(evidence, actions, cursor, false, finding.Reason, finding);
        }
    }
}
