using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // A learned terminal outage redirects planning to eligible alternatives until its
    // recheck. The delay starts at the planner's maximum retry delay and doubles to a bound.
    public static class TerminalAvoidance
    {
        public static long RecheckLimit(long maximumBackoff) => checked(maximumBackoff * 8);
        public static long InitialRecheckDelay(long maximumBackoff) => maximumBackoff;
        public static long NextRecheckDelay(long delay, long maximumBackoff) => Math.Min(RecheckLimit(maximumBackoff), checked(delay * 2));
        // Terminals not to plan to use while an eligible alternative exists. After the
        // recheck tick a terminal is an ordinary candidate again. Ordered by recheck time
        // so a fallback rechecks the oldest observation first.
        public static long[] Avoided(IEnumerable<(long TerminalId, long RecheckAt)> outages, long now) =>
            outages.Where(o => o.RecheckAt > now).OrderBy(o => o.RecheckAt).ThenBy(o => o.TerminalId).Select(o => o.TerminalId).ToArray();
        // After a check-in or post, an idle session is kept while the citizen still has
        // terminal work: queued reports, a due check-in, a pending on-duty audit at this
        // terminal, or a terminal action in progress.
        public static bool KeepsSession(bool reportsPending, bool checkInDue, Func<bool> auditAtSessionTerminal, ActionKind? action) =>
            reportsPending || checkInDue || auditAtSessionTerminal() ||
            action is ActionKind.Login or ActionKind.RecoveryLogin or ActionKind.PostLostCard or ActionKind.ReadRecoveryInbox or ActionKind.IssueReplacement or ActionKind.ReadInbox or ActionKind.PostMessage or ActionKind.QueryRecords;
    }
}
