using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum SubjectKind { Actor, Item, Tile, Machine, Job, Attempt, Order, Credential, Record, Service, ConstructionProject }
    public enum AttributeKind { Location, TraversalTicks, Present, Operational, RequiredResource, InstalledResource, Quantity, WorkStatus, OrderStatus, CredentialStatus, CredentialCard, Resource, FitClass, Rating, LocationKind, LocationOwner,
        RecordType, RecordEntityId, RecordVersion, AccountId, ActorId, TaskId, AttemptId, TerminalId, Expires, Generation, Role, MessageId, MachineId, SubmittedTick, ReceivedTick, AcknowledgedTick, ReportKind, ReportClaim, ReportSubjectKind, ReportSubjectId, ReportAttribute, ObservedTick,
        RosterVersion, Cohort, DayTicks, ShiftStart, DutyTicks, CheckInOffset, RestTile, TaskKind, EstimateTicks, Deadline, SlackPermille, LearnedRatio, OffDutyTicks, ReturnTerminalId, ResultMessageId, AdmissionBucket, ReplacementAttemptId, ReplacementDepth, WorkResolution, PatrolStatus, PatrolOrderMessageId, PatrolResultMessageId, WorkerStatus, Dirt, TileId, RecordFieldCount, CaseStatus, QualifyingCount, CaseId, Policy, Violation, QueryActionId, QueriedVersion, EvidenceVersion, DetentionStatus, AuthorizationId, SentenceTicks, SubjectAccountId, JustifyingCases, FacilityId, GuardOrderMessageId, AssignedBerth, AllocationActionId, RecoveryCase, SessionScope, ProjectId, UsableBerths }
    public enum FactValueKind { Number, Boolean, Coordinate, Text, AbsentAt }
    public enum EvidenceKind { Direct, Inspection, ActionFailure, MessageRead, Heard, Seeded, RecordQuery }
    public enum MessageKind { Observation, WorkCompleted, WorkBlocked, Order, OrderAccepted, OrderRejected, OrderCancelled, Peer, ServiceReply, RosterUpdated, UnableToContinue, PatrolOrder, PatrolAccepted, PatrolFound, PatrolNotLocated, PatrolBlocked, FacilityCondition, FileCase, CaseDecision, RequestDetention, DetentionDecision, EscortOrder, EscortAcknowledged, DetentionIntakeReceipt, DetentionReleaseReceipt, DetentionReleaseNotice, EscortRevoked, EscortOutcome, LostCardReport, RecoveryReply, ConstructionSurvey }
    public enum RecipientKind { Account, Authority, CredentialService, RecoveryAccount }
    public enum MessageStage { Posted, DeliveredToMailbox, Read, Accepted }
    [Flags] public enum TerminalCapability { None = 0, Inbox = 1, Post = 2, Records = 4, CredentialRecovery = 8 }
    public enum SessionScope { Normal, CredentialRecovery }

    public readonly struct FactKey : IComparable<FactKey>, IEquatable<FactKey>
    {
        public SubjectKind SubjectKind { get; }
        public long SubjectId { get; }
        public AttributeKind Attribute { get; }
        public FactKey(SubjectKind kind, long subjectId, AttributeKind attribute)
        { if (subjectId <= 0) throw new ArgumentOutOfRangeException(nameof(subjectId)); SubjectKind = kind; SubjectId = subjectId; Attribute = attribute; }
        public int CompareTo(FactKey b) { int c = SubjectKind.CompareTo(b.SubjectKind); if (c == 0) c = SubjectId.CompareTo(b.SubjectId); return c == 0 ? Attribute.CompareTo(b.Attribute) : c; }
        public bool Equals(FactKey b) => CompareTo(b) == 0;
        public override bool Equals(object? obj) => obj is FactKey b && Equals(b);
        public override int GetHashCode() => unchecked(((int)SubjectKind * 397 ^ SubjectId.GetHashCode()) * 397 ^ (int)Attribute);
        public override string ToString() => $"{SubjectKind}:{SubjectId}/{Attribute}";
    }
    public readonly struct FactValue : IEquatable<FactValue>
    {
        public FactValueKind Kind { get; }
        public long Number { get; }
        public Coord Coordinate { get; }
        public string Text { get; }
        public FactValue(FactValueKind kind, long number = 0, Coord coordinate = default, string text = "")
        { Kind = kind; Number = number; Coordinate = coordinate; Text = text; }
        public static FactValue At(Coord at) => new FactValue(FactValueKind.Coordinate, coordinate: at);
        public static FactValue Absent(Coord at) => new FactValue(FactValueKind.AbsentAt, coordinate: at);
        public static FactValue Integer(long n) => new FactValue(FactValueKind.Number, n);
        public static FactValue Boolean(bool value) => new FactValue(FactValueKind.Boolean, value ? 1 : 0);
        public bool Equals(FactValue b) => Kind == b.Kind && Number == b.Number && Coordinate == b.Coordinate && (Text ?? "") == (b.Text ?? "");
        public override bool Equals(object? obj) => obj is FactValue b && Equals(b);
        public override int GetHashCode() => unchecked(((int)Kind * 397 ^ Number.GetHashCode()) * 397 ^ Coordinate.GetHashCode());
        public override string ToString() => Kind == FactValueKind.Coordinate || Kind == FactValueKind.AbsentAt ? Kind + " " + Coordinate : Kind == FactValueKind.Text ? Text : Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    public sealed class KnowledgeFact
    {
        public long Id { get; }
        public FactKey Key { get; }
        public FactValue Value { get; }
        public EvidenceKind SourceKind { get; }
        public long SourceId { get; }
        public long ObservedTick { get; }
        public long LearnedTick { get; }
        public bool Pinned { get; } public long SourceActorId { get; }
        public KnowledgeFact(long id, FactKey key, FactValue value, EvidenceKind sourceKind, long sourceId, long observedTick, long learnedTick, bool pinned = false, long sourceActorId = 0)
        { Id = id; Key = key; Value = value; SourceKind = sourceKind; SourceId = sourceId; ObservedTick = observedTick; LearnedTick = learnedTick; Pinned = pinned; SourceActorId = sourceActorId; }
    }
    public sealed class KnownTile
    {
        public Id<TileTag> Id { get; }
        public Coord Position { get; }
        public long TraversalTicks { get; }
        public KnownTile(Id<TileTag> id, Coord position, long traversalTicks) { Id = id; Position = position; TraversalTicks = traversalTicks; }
    }
    public sealed class PublicService
    {
        public long Id { get; }
        public Coord Position { get; }
        public TerminalCapability Capabilities { get; }
        public IReadOnlyList<string> PermittedRoles { get; }
        public PublicService(long id, Coord position, TerminalCapability capabilities, IEnumerable<string> roles)
        { Id = id; Position = position; Capabilities = capabilities; PermittedRoles = Array.AsReadOnly(roles.OrderBy(x => x, StringComparer.Ordinal).ToArray()); }
    }
    public sealed class PublicKnowledge
    {
        public long Version { get; }
        public IReadOnlyList<KnownTile> Layout { get; }
        public IReadOnlyList<PublicService> Services { get; }
        public PublicWorkCatalog? Work { get; }
        public PublicPersonnelPolicy? Personnel { get; }
        // The credential-recovery desk directory, in terminal order.
        public IReadOnlyList<PublicRecoveryDesk> RecoveryDesks { get; }
        public PublicKnowledge(long version, IEnumerable<KnownTile> layout, IEnumerable<PublicService> services, PublicWorkCatalog? work = null, PublicPersonnelPolicy? personnel = null, IEnumerable<PublicRecoveryDesk>? recoveryDesks = null)
        {
            Version = version; Layout = Array.AsReadOnly(layout.OrderBy(x => x.Id).ToArray()); Services = Array.AsReadOnly(services.OrderBy(x => x.Id).ToArray()); Work = work; Personnel = personnel;
            RecoveryDesks = Array.AsReadOnly((recoveryDesks ?? Array.Empty<PublicRecoveryDesk>()).OrderBy(x => x.TerminalId).ToArray());
        }
        public PublicKnowledge WithPersonnel(PublicPersonnelPolicy personnel) => new PublicKnowledge(Version, Layout, Services, Work, personnel, RecoveryDesks);
        public PublicKnowledge WithRecoveryDesks(IEnumerable<PublicRecoveryDesk> desks) => new PublicKnowledge(Version, Layout, Services, Work, Personnel, desks);
    }
    public sealed class MessageDraft
    {
        public MessageKind Kind { get; }
        public RecipientKind RecipientKind { get; }
        public Id<AccountTag> Recipient { get; }
        public FactKey Subject { get; }
        public FactValue Claim { get; }
        public long ObservedTick { get; }
        public long TaskId { get; }
        public long AttemptId { get; }
        public long RelatedMessageId { get; }
        public RecoveryReplyPayload? RecoveryReply { get; }
        public EscortOrderPayload? EscortOrder { get; }
        public DetentionRequestPayload? DetentionRequest { get; }
        public DetentionDecisionPayload? DetentionDecision { get; }
        public InspectionReportPayload? InspectionReport { get; }
        public CaseSubmissionPayload? CaseSubmission { get; }
        public CaseDecisionPayload? CaseDecision { get; }
        public WorkOrderPayload? WorkOrder { get; }
        public string OperationKey { get; }
        public RosterPayload? Roster { get; }
        public PatrolOrderPayload? PatrolOrder { get; }
        public MessageDraft(MessageKind kind, RecipientKind recipientKind, Id<AccountTag> recipient, FactKey subject, FactValue claim, long observedTick,
            long taskId = 0, long attemptId = 0, long relatedMessageId = 0, WorkOrderPayload? workOrder = null, string operationKey = "", RosterPayload? roster = null, PatrolOrderPayload? patrolOrder = null, InspectionReportPayload? inspectionReport = null, CaseSubmissionPayload? caseSubmission = null, CaseDecisionPayload? caseDecision = null, DetentionRequestPayload? detentionRequest = null, DetentionDecisionPayload? detentionDecision = null, EscortOrderPayload? escortOrder = null, RecoveryReplyPayload? recoveryReply = null)
        { RecoveryReply = recoveryReply; Kind = kind; RecipientKind = recipientKind; Recipient = recipient; Subject = subject; Claim = claim; ObservedTick = observedTick; TaskId = taskId; AttemptId = attemptId; RelatedMessageId = relatedMessageId; WorkOrder = workOrder; OperationKey = operationKey; Roster = roster; PatrolOrder = patrolOrder; InspectionReport = inspectionReport; CaseSubmission = caseSubmission; CaseDecision = caseDecision; DetentionRequest = detentionRequest; DetentionDecision = detentionDecision; EscortOrder = escortOrder; }
    }
    public sealed class WorkOrderPayload
    {
        public ConstructionOrder? Construction { get; }
        public WorkKind Kind { get; }
        public Id<TileTag> Tile { get; }
        public long MachineId { get; }
        public long Expires { get; }
        public WorkOrderPayload(long machineId, long expires, WorkKind kind = WorkKind.RepairMachine)
        { if (machineId < 1 || expires < 1 || !(kind is WorkKind.RepairMachine or WorkKind.VerifyMachine)) throw new ArgumentException("Invalid repair order."); Kind = kind; MachineId = machineId; Expires = expires; }
        public WorkOrderPayload(Id<TileTag> tile, long expires)
        { if (tile.Value == 0 || expires < 1) throw new ArgumentException("Invalid cleanup order."); Kind = WorkKind.CleanTile; Tile = tile; Expires = expires; }
        public WorkOrderPayload(ConstructionOrder construction, long expires)
        { if (construction == null || expires < 1) throw new ArgumentException("Invalid construction order."); Kind = WorkKind.ConstructHabitat; Construction = construction; Expires = expires; }
    }
}
