using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    // An administrative job as the authority knows it: its kind and declared target
    // identities. Construction carries the committed specification, never a site survey.
    public sealed class AuthorityJobTarget
    {
        public WorkKind Kind { get; }
        public long MachineId { get; }
        public long TileId { get; }
        public long ReportedUnits { get; }
        public ConstructionOrder? Construction { get; }
        public AuthorityJobTarget(WorkKind kind, long machineId, long tileId, long reportedUnits, ConstructionOrder? construction)
        { Kind = kind; MachineId = machineId; TileId = tileId; ReportedUnits = reportedUnits; Construction = construction; }
    }
    // A received guard outcome with the payload of the order it answered.
    public sealed class GuardSearchEvidence
    {
        public string Outcome { get; }
        public long ObservedTick { get; }
        public EscortOrderPayload? Order { get; }
        public GuardSearchEvidence(string outcome, long observedTick, EscortOrderPayload? order) { Outcome = outcome; ObservedTick = observedTick; Order = order; }
    }
    // A currently open assignment of a detention subject and its published work site.
    public sealed class KnownAssignmentSite
    {
        public long AttemptId { get; }
        public Coord? Site { get; }
        public KnownAssignmentSite(long attemptId, Coord? site) { AttemptId = attemptId; Site = site; }
    }
    public enum EscortTeamResponse { Unchanged, Blocked, TeamForming }
    public enum QualityCheckStage { NotSampled, Scheduled, PendingSupervisor, Assigned, ReportedClear, ReportedDiscrepancy, NeedsAttention }
    // The authority's own QA record for a machine: its stage and, once a verification
    // result arrived, that result's delivery tick.
    public sealed class QualityHoldEvidence
    {
        public QualityCheckStage Stage { get; }
        public long RepairAttemptId { get; }
        public long? ResultDeliveryTick { get; }
        public QualityHoldEvidence(QualityCheckStage stage, long repairAttemptId, long? resultDeliveryTick) { Stage = stage; RepairAttemptId = repairAttemptId; ResultDeliveryTick = resultDeliveryTick; }
    }
    public enum VerificationVerdict { Ignored, Clear, Discrepancy, NeedsAttention }
    public enum FacilityReportDisposition { Stale, NewTask, Merge }
    public enum FacilityWorkResponse { Ignored, Reopen, Complete, NeedsAttention }
    public enum PatrolOrderGate { NotQualified, Accept, Search }
    // A citizen's own local work order, as its private order book records it.
    public sealed class KnownLocalOrder
    {
        public long AttemptId { get; }
        public WorkKind Kind { get; }
        public bool Working { get; }
        public KnownLocalOrder(long attemptId, WorkKind kind, bool working) { AttemptId = attemptId; Kind = kind; Working = working; }
    }
    // A declared constructible tile definition with its recipe's duration and inputs.
    public sealed class HabitatOption
    {
        public string DefinitionId { get; }
        public TileFamily Family { get; }
        public long Berths { get; }
        public IReadOnlyList<string> Roles { get; }
        public long Duration { get; }
        public IReadOnlyList<MaterialAmount> Inputs { get; }
        public HabitatOption(string definitionId, TileFamily family, long berths, IEnumerable<string> roles, long duration, IEnumerable<MaterialAmount> inputs)
        { DefinitionId = definitionId; Family = family; Berths = berths; Roles = Array.AsReadOnly(roles.ToArray()); Duration = duration; Inputs = Array.AsReadOnly(inputs.ToArray()); }
    }
}
