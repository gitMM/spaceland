using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public enum WorkExecution { Pending, Assigned, InProgress, Waiting, Blocked, CompletedPhysically, Cancelled, Failed }
    public enum AssignmentStatus { PendingDispatch, Active, Superseded, Cancelled }
    public enum LocalOrderStatus { Read, AcknowledgmentPending, Working, Suspended, Completed, Rejected, Cancelled, Expired }
    public sealed class JobSeed
    {
        public WorkKind Kind { get; }
        public Id<TileTag> Tile { get; }
        public long ReportedUnits { get; }
        public long Id { get; }
        public long MachineId { get; }
        public JobSeed(long id, long machineId, WorkKind kind = WorkKind.RepairMachine) { if (id < 1 || machineId < 1 || !(kind is WorkKind.RepairMachine or WorkKind.VerifyMachine)) throw new ArgumentException("Invalid repair job."); Id = id; Kind = kind; MachineId = machineId; }
        public JobSeed(long id, Id<TileTag> tile, long reportedUnits = 1)
        { if (id < 1 || tile.Value == 0 || reportedUnits < 0) throw new ArgumentException("Invalid cleanup job."); Id = id; Kind = WorkKind.CleanTile; Tile = tile; ReportedUnits = reportedUnits; }
    }
    public sealed class AssignmentSeed
    {
        public string Key { get; }
        public long JobId { get; }
        public Id<ActorTag> Actor { get; }
        public long Expires { get; }
        public AssignmentSeed(string key, long jobId, Id<ActorTag> actor, long expires)
        { if (string.IsNullOrWhiteSpace(key) || jobId < 1 || actor.Value == 0 || expires < 1) throw new ArgumentException("Invalid assignment."); Key = key; JobId = jobId; Actor = actor; Expires = expires; }
    }
    public sealed class CheckInSeed
    {
        public Id<ActorTag> Actor { get; }
        public long FirstTick { get; }
        public long Interval { get; }
        public CheckInSeed(Id<ActorTag> actor, long firstTick = 0, long interval = 36000)
        { if (actor.Value == 0 || firstTick < 0 || interval < 1) throw new ArgumentException("Invalid check-in schedule."); Actor = actor; FirstTick = firstTick; Interval = interval; }
    }
    public sealed class JobSystemSeed
    {
        public IReadOnlyList<JobSeed> Jobs { get; }
        public IReadOnlyList<AssignmentSeed> Assignments { get; }
        public IReadOnlyList<CheckInSeed> CheckIns { get; }
        public JobSystemSeed(IEnumerable<JobSeed> jobs, IEnumerable<AssignmentSeed>? assignments = null, IEnumerable<CheckInSeed>? checkIns = null)
        { Jobs = jobs.ToArray(); Assignments = (assignments ?? Array.Empty<AssignmentSeed>()).ToArray(); CheckIns = (checkIns ?? Array.Empty<CheckInSeed>()).ToArray(); }
    }
    public sealed class JobSnapshot
    {
        public long ProjectId { get; internal set; }
        public WorkKind Kind { get; internal set; }
        public long TileId { get; internal set; }
        public long ReportedUnits { get; internal set; }
        public long Id { get; internal set; }
        public long MachineId { get; internal set; }
        public long CurrentAttemptId { get; internal set; }
        public WorkExecution PhysicalStatus { get; internal set; }
        public string AuthorityStatus { get; internal set; } = "Unassigned";
        internal JobSnapshot Copy() => (JobSnapshot)MemberwiseClone();
    }
    public sealed class WorkAttemptSnapshot
    {
        public long Id { get; internal set; }
        public long JobId { get; internal set; }
        public long ActorId { get; internal set; }
        public long AccountId { get; internal set; }
        public string DispatchKey { get; internal set; } = "";
        public long DispatchTick { get; internal set; }
        public long Expires { get; internal set; }
        public long OrderMessageId { get; internal set; }
        public AssignmentStatus Assignment { get; internal set; }
        public WorkExecution Execution { get; internal set; }
        public long? AcknowledgmentReceivedTick { get; internal set; }
        public string AuthorityStatus { get; internal set; } = "Unacknowledged";
        public IReadOnlyList<long> ReportMessageIds { get; internal set; } = Array.Empty<long>();
        internal WorkAttemptSnapshot Copy() { var copy = (WorkAttemptSnapshot)MemberwiseClone(); copy.ReportMessageIds = ReportMessageIds.ToArray(); return copy; }
    }
    public sealed class LocalWorkOrderSnapshot
    {
        public long CompletionCauseId { get; internal set; }
        public long CompletionObservedTick { get; internal set; } = -1;
        public long ProjectId { get; internal set; }
        public WorkKind Kind { get; internal set; }
        public long TileId { get; internal set; }
        public bool CleanupChecked { get; internal set; }
        public long CleanedUnits { get; internal set; }
        public long AttemptId { get; internal set; }
        public long JobId { get; internal set; }
        public long ActorId { get; internal set; }
        public long MachineId { get; internal set; }
        public long OrderMessageId { get; internal set; }
        public long ReadTick { get; internal set; }
        public long Expires { get; internal set; }
        public LocalOrderStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "";
        public long NextDecision { get; internal set; } = -1;
        public bool BlockedReported { get; internal set; }
        public long Backoff { get; internal set; }
        internal LocalWorkOrderSnapshot Copy() => (LocalWorkOrderSnapshot)MemberwiseClone();
    }
    public sealed class WorkHistoryEntry
    {
        public long Version { get; internal set; }
        public long Tick { get; internal set; }
        public long JobId { get; internal set; }
        public long AttemptId { get; internal set; }
        public long MessageId { get; internal set; }
        public string Kind { get; internal set; } = "";
        internal WorkHistoryEntry Copy() => (WorkHistoryEntry)MemberwiseClone();
    }
    internal sealed class CheckInAgent
    {
        internal Id<ActorTag> Actor;
        internal long Interval;
        internal long NextDecision = -1;
        internal long DueTick;
        internal bool Due;
        internal GoalStamp Stamp;
        internal long BlockedUntil;
        internal long Completed;
        internal long Backoff;
    }
}
