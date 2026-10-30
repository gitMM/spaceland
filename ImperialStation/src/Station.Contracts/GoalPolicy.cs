namespace Station.Contracts
{
    // Vocabulary shared by Core (which gathers a citizen's own goals and performs
    // wakes) and Mind (which decides which goal has priority).
    public enum DueSource { None, Report, CheckIn, Routine, Toilet }

    // One goal competing for a citizen's next free decision. Goals are ordered by the
    // tick they became due, then by their creation sequence.
    public readonly struct DueOption
    {
        public DueSource Source { get; }
        public long Id { get; }
        public long Due { get; }
        public long Sequence { get; }
        public DueOption(DueSource source, long id, long due, long sequence) { Source = source; Id = id; Due = due; Sequence = sequence; }
    }

    // One step of a priority check. Mind lists the steps of each decision in order;
    // Core evaluates them one at a time and stops at the first that defers. Steps
    // marked "wakes" make Core schedule the goal that takes priority when they defer.
    public enum PriorityGate
    {
        None,
        Inactive,              // the deciding goal itself is absent, not due or disabled
        EscortControls,        // an escort protocol controls the citizen
        CommittedAction,       // the citizen is already committed to an action
        PlannedThisTick,       // the deciding goal already scheduled a command this tick
        UrgentNeed,            // an urgent/critical own need comes first (wakes)
        CredentialRecovery,    // a missing own credential is recovered first (wakes)
        Recovery,              // a pending credential recovery comes first (wakes)
        KnownUrgentOrder,      // a known urgent order comes first
        CriticalNeed,          // an own need is critical
        StaffClaimed,          // the citizen is reserved as staff for an operation
        RoutineForbidsCheckIn, // the known roster does not allow a check-in now
        GuardPriority,         // a known guard order comes first
        DueGoal,               // an earlier-due goal comes first (wakes)
        DueGoalSelected,       // some due goal is selected (no wake)
        AlreadyPlanned,        // this decision source already planned this tick
        ReportQueueFirst,      // a non-arbitrating citizen posts queued reports first
        OrdinaryWork,          // known on-duty work is ready
        OptionalRest,          // optional rest or conversation comes first (wakes)
        OnDuty,                // the known roster says on duty
        AfterDutyCheckIn,      // an after-duty check-in or report is still owed
        MissingRestDestination,// the known roster names no rest tile
        AtRest,                // the citizen is at its known rest tile
    }
}
