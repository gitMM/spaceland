using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Which accepted reprimands count toward the detention threshold at a given tick.
    public static class ReprimandPolicy
    {
        private static long Number(RecordEntry row, AttributeKind key) => row.Fields.Where(f => f.Attribute == key && f.Value.Kind == FactValueKind.Number).Select(f => f.Value.Number).DefaultIfEmpty(-1).Single();
        // Cases accepted inside the qualifying window and after the account's last completed release.
        // throughRecordVersion bounds only the release rows; throughCaseId bounds the reprimands.
        public static long[] QualifyingCases(PersonnelConfig? personnel, long account, long tick, IEnumerable<RecordEntry> history, IEnumerable<ReprimandEntry> reprimands,
            long throughCaseId = long.MaxValue, long throughRecordVersion = long.MaxValue)
        {
            if (personnel == null || account < 1 || tick < 0) return Array.Empty<long>();
            long start = tick >= personnel.QualifyingWindow ? tick - personnel.QualifyingWindow : 0;
            long completed = history.Where(e => e.Kind == RecordKind.DetentionRelease && e.AccountId == account && e.Tick <= tick && e.Version <= throughRecordVersion).Select(e => Number(e, AttributeKind.ObservedTick)).DefaultIfEmpty(-1).Max();
            return reprimands.Where(r => r.AcceptedTick > completed && r.CaseId <= throughCaseId && r.AccountId == account && r.AcceptedTick >= start && r.AcceptedTick <= tick).Select(r => r.CaseId).ToArray();
        }
    }
}
