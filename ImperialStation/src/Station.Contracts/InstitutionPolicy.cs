using System;
using System.Collections.Generic;

namespace Station.Contracts
{
    // Inputs and outputs of the institutional decision services in Station.Mind.
    // Core gathers these facts from its registers and applies the returned choice.

    public enum DetentionAuthorizationChoice { None, ReusePrior, ReuseActive, IssueNew }
    public sealed class DetentionRequestFacts
    {
        public long Tick { get; }
        public long SubjectAccountId { get; }
        // Empty when the request evidence is valid.
        public string ValidationReason { get; }
        public int QualifyingCount { get; }
        public int Threshold { get; }
        // Non-null when the same sender already used this operation key.
        public DetentionDecisionPayload? PriorDecision { get; }
        public bool PriorSameDraft { get; }
        public long PriorAuthorizationExpires { get; }
        public bool PriorReleased { get; }
        public bool PriorRevoked { get; }
        public bool PolicyAvailable { get; }
        public bool HasActiveAuthorization { get; }
        public long ActiveAuthorizationId { get; }
        public long ActiveAuthorizationExpires { get; }
        // Enumerated only when a new authorization would be issued.
        public IEnumerable<KnownPersonnel> Registry { get; }
        public DetentionRequestFacts(long tick, long subjectAccountId, string validationReason, int qualifyingCount, int threshold, DetentionDecisionPayload? priorDecision, bool priorSameDraft,
            long priorAuthorizationExpires, bool priorReleased, bool priorRevoked, bool policyAvailable, bool hasActiveAuthorization, long activeAuthorizationId, long activeAuthorizationExpires, IEnumerable<KnownPersonnel> registry)
        {
            Tick = tick; SubjectAccountId = subjectAccountId; ValidationReason = validationReason ?? throw new ArgumentNullException(nameof(validationReason)); QualifyingCount = qualifyingCount; Threshold = threshold;
            PriorDecision = priorDecision; PriorSameDraft = priorSameDraft; PriorAuthorizationExpires = priorAuthorizationExpires; PriorReleased = priorReleased; PriorRevoked = priorRevoked;
            PolicyAvailable = policyAvailable; HasActiveAuthorization = hasActiveAuthorization; ActiveAuthorizationId = activeAuthorizationId; ActiveAuthorizationExpires = activeAuthorizationExpires;
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }
    }
    public sealed class DetentionRequestChoice
    {
        public DetentionDecisionState State { get; }
        public string Reason { get; }
        public DetentionAuthorizationChoice Authorization { get; }
        // Reused authorization and expiry; zero for IssueNew (Core allocates them).
        public long AuthorizationId { get; }
        public long Expires { get; }
        public long SubjectActorId { get; }
        public DetentionRequestChoice(DetentionDecisionState state, string reason, DetentionAuthorizationChoice authorization = DetentionAuthorizationChoice.None, long authorizationId = 0, long expires = 0, long subjectActorId = 0)
        { State = state; Reason = reason; Authorization = authorization; AuthorizationId = authorizationId; Expires = expires; SubjectActorId = subjectActorId; }
    }

    public readonly struct ReprimandEntry
    {
        public long CaseId { get; }
        public long AccountId { get; }
        public long AcceptedTick { get; }
        public ReprimandEntry(long caseId, long accountId, long acceptedTick) { CaseId = caseId; AccountId = accountId; AcceptedTick = acceptedTick; }
    }

    public enum CaseAcceptance { Rejected, OpenCase, AlreadyAccepted }
    public sealed class CaseAcceptanceChoice
    {
        public CaseAcceptance Outcome { get; }
        public string Reason { get; }
        public CaseAcceptanceChoice(CaseAcceptance outcome, string reason) { Outcome = outcome; Reason = reason; }
    }

    // One completed page of a citizen's discrepancy audit, restricted to facts this action taught.
    public sealed class AuditPageInput
    {
        public bool QueryingAttempts { get; }
        public RecordKind Kind { get; }
        public long Cutoff { get; }
        public int Pages { get; }
        public int PageLimit { get; }
        public int EvidenceLimit { get; }
        public RecordQueryReceipt Receipt { get; }
        public long ActionId { get; }
        public IReadOnlyList<KnowledgeFact> Learned { get; }
        public IReadOnlyList<RecordEntry> Evidence { get; }
        public IReadOnlyList<long> QueryActionIds { get; }
        public long MachineId { get; }
        public string RequiredResource { get; }
        public string InstalledResource { get; }
        public long InspectionTick { get; }
        public AuditPageInput(bool queryingAttempts, RecordKind kind, long cutoff, int pages, int pageLimit, int evidenceLimit, RecordQueryReceipt receipt, long actionId, IReadOnlyList<KnowledgeFact> learned,
            IReadOnlyList<RecordEntry> evidence, IReadOnlyList<long> queryActionIds, long machineId, string requiredResource, string installedResource, long inspectionTick)
        {
            QueryingAttempts = queryingAttempts; Kind = kind; Cutoff = cutoff; Pages = pages; PageLimit = pageLimit; EvidenceLimit = evidenceLimit; Receipt = receipt; ActionId = actionId; Learned = learned;
            Evidence = evidence; QueryActionIds = queryActionIds; MachineId = machineId; RequiredResource = requiredResource; InstalledResource = installedResource; InspectionTick = inspectionTick;
        }
    }
    public sealed class AuditPageResult
    {
        public IReadOnlyList<RecordEntry> Evidence { get; }
        public IReadOnlyList<long> QueryActionIds { get; }
        // Null when the page stopped before the cursor could advance.
        public long? Cursor { get; }
        public bool AdvanceToReports { get; }
        // Null while the audit continues.
        public string? FinishReason { get; }
        public AccountabilityFinding? Finding { get; }
        public AuditPageResult(IReadOnlyList<RecordEntry> evidence, IReadOnlyList<long> queryActionIds, long? cursor, bool advanceToReports, string? finishReason, AccountabilityFinding? finding = null)
        { Evidence = evidence; QueryActionIds = queryActionIds; Cursor = cursor; AdvanceToReports = advanceToReports; FinishReason = finishReason; Finding = finding; }
    }

    // Mirror of the authority's escort dispatch progress, for guard outcome classification.
    public enum EscortDispatchProgress { AwaitingGuards, AwaitingReaders, TeamForming, Blocked, Expired, Detained, Released, Cancelled, NeedsAttention }
    public enum CustodyRetry { None, AwaitLocalHandoff, Reinspect }
    public enum LostCardResponse { OpenCaseAndRevoke, ReplyWithExistingCase }
}
