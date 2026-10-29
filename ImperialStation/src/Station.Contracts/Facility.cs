using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public sealed class FacilityConfig
    {
        public IReadOnlyList<string> ReportingRoles { get; }
        public long DirtThreshold { get; }
        public long RepeatTicks { get; }
        public long OrderTicks { get; }
        // Repair priority: with no uncommitted qualified responder, a reported repair
        // may be ordered to one whose only known commitment is cleanup work.
        public bool RepairPreemptsCleanup { get; }
        public FacilityConfig(IEnumerable<string>? reportingRoles = null, long dirtThreshold = 2, long repeatTicks = 36000, long orderTicks = 1728000, bool repairPreemptsCleanup = false)
        {
            ReportingRoles = Array.AsReadOnly((reportingRoles ?? new[] { "cleaner", "supervisor", "guard", "patrol" }).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToArray());
            if (ReportingRoles.Any(string.IsNullOrWhiteSpace) || dirtThreshold < 1 || repeatTicks < 1 || orderTicks < 1) throw new ArgumentException("Invalid facility reporting policy.");
            DirtThreshold = dirtThreshold; RepeatTicks = repeatTicks; OrderTicks = orderTicks; RepairPreemptsCleanup = repairPreemptsCleanup;
        }
    }
}
