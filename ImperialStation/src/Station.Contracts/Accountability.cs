using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public sealed class AccountabilityFinding
    {
        public bool Attributable => AccountId > 0;
        public string Reason { get; }
        public long JobId { get; }
        public long AttemptId { get; }
        public long MachineId { get; }
        public long AccountId { get; }
        public IReadOnlyList<long> EvidenceVersions { get; }
        public AccountabilityFinding(string reason, long machineId, long jobId = 0, long attemptId = 0, long accountId = 0, IEnumerable<long>? versions = null)
        {
            if (string.IsNullOrWhiteSpace(reason) || machineId < 1 || jobId < 0 || attemptId < 0 || accountId < 0) throw new ArgumentException("Invalid accountability finding.");
            Reason = reason; MachineId = machineId; JobId = jobId; AttemptId = attemptId; AccountId = accountId;
            EvidenceVersions = Array.AsReadOnly((versions ?? Array.Empty<long>()).Distinct().OrderBy(v => v).ToArray());
        }
    }
}
