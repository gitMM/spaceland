using System;

namespace Station.Contracts
{
    // Mirror of a citizen's private work-order copy state. Core maps its own
    // status one-to-one by ordinal; Mind decides from this mirror only.
    public enum KnownWorkOrderStatus { Read, AcknowledgmentPending, Working, Suspended, Completed, Rejected, Cancelled, Expired }

    // Mirror of a guard's private escort-order copy state (same ordinal contract).
    public enum KnownGuardOrderStatus { AcknowledgmentPending, EquipmentPending, Rejected, Expired, AtRendezvous, WaitingTeam, Searching, TargetLocated, SearchExhausted, EscortInterrupted, TransferredToFacility, Revoked, SearchAbandoned }

    // The citizen's own recollection of one private work order.
    public readonly struct WorkOrderBrief
    {
        public long AttemptId { get; }
        public WorkKind Kind { get; }
        public KnownWorkOrderStatus Status { get; }
        public long MachineId { get; }
        public long TileId { get; }
        public WorkOrderBrief(long attemptId, WorkKind kind, KnownWorkOrderStatus status, long machineId, long tileId)
        { AttemptId = attemptId; Kind = kind; Status = status; MachineId = machineId; TileId = tileId; }
    }

    public enum WorkOrderRoute { None, Acceptance, Verification, Construction, Cleanup }
    public enum WorkOrderReadOutcome { Evaluate, OwnCompletionAlreadyPosted, Expired }
    public enum WorkOrderVerdict { Retry, Reject, Accept }
    public readonly struct WorkOrderAcceptance
    {
        public WorkOrderVerdict Verdict { get; }
        public string Reason { get; }
        public WorkOrderAcceptance(WorkOrderVerdict verdict, string reason) { Verdict = verdict; Reason = reason ?? throw new ArgumentNullException(nameof(reason)); }
    }

    // Outcome of one bounded plan for a retained work goal (repair or cleanup).
    public enum WorkGoalStep { Completed, Execute, Blocked }

    public enum RecoveryProgress { Continue, AwaitReply, RetryUnauthorized, RetryUnreadInbox, Complete }
    public sealed class CredentialRecoveryTimings
    {
        public long LoginTicks { get; }
        public long ReadTicks { get; }
        public long PostTicks { get; }
        public long LatencyTicks { get; }
        public long ItemActionTicks { get; }
        public long IdentityCheckTicks { get; }
        public CredentialRecoveryTimings(long loginTicks, long readTicks, long postTicks, long latencyTicks, long itemActionTicks, long identityCheckTicks)
        { LoginTicks = loginTicks; ReadTicks = readTicks; PostTicks = postTicks; LatencyTicks = latencyTicks; ItemActionTicks = itemActionTicks; IdentityCheckTicks = identityCheckTicks; }
    }

    // Side-effect-free observations of a guard's own situation. Core supplies them
    // lazily; Mind reads them in its own priority order and returns one step.
    public interface IGuardPreparationInputs
    {
        bool CriticalNeed();
        bool JourneyNeedsRelief();
        bool AwaitingIntake();
        bool Busy();
        bool KnownOnDuty();
        bool ReportsPending();
        bool EscortControls();
    }
    public enum GuardStep { Wait, WithdrawForCriticalNeed, Expire, SeekRelief, RegisterIntake, BeginEscort, Search, Prepare }
    public enum GuardPreparationStep { ArriveAtRendezvous, Join, Plan }
    public enum GuardSearchObservation { Continue, Located, Exhausted }
    // A failed guard plan: the retained reason and, when unreachable, the outcome to report.
    public readonly struct GuardPlanFailure
    {
        public string Reason { get; }
        public string? Outcome { get; }
        public GuardPlanFailure(string reason, string? outcome) { Reason = reason; Outcome = outcome; }
    }
}
