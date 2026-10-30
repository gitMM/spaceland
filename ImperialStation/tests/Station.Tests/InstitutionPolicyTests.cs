using Station.Contracts;
using Station.Mind;
using Xunit;

namespace Station.Tests;

public sealed class InstitutionPolicyTests
{
    private static readonly KnownPersonnel[] Registry = { new(1, 11, "worker"), new(2, 12, "supervisor") };
    private static DetentionRequestFacts Facts(string validation = "", DetentionDecisionPayload? prior = null, bool same = true, long priorExpires = 0, bool released = false, bool revoked = false,
        bool policy = true, bool active = false, long activeExpires = 0, int count = 2, IEnumerable<KnownPersonnel>? registry = null) =>
        new(100, 11, validation, count, 2, prior, same, priorExpires, released, revoked, policy, active, active ? 7 : 0, activeExpires, registry ?? Registry);
    private static DetentionDecisionPayload Prior(DetentionDecisionState state, string reason, long authorization = 0, long expires = 0) => new(5, 11, authorization, state, reason, 2, expires);

    [Fact]
    public void DetentionApprovalFollowsTheDeskReasonChain()
    {
        Assert.Equal((DetentionDecisionState.Waiting, "PersonnelUnavailable"), Pick(DetentionDecisions.Decide(Facts("PersonnelUnavailable"))));
        Assert.Equal((DetentionDecisionState.Rejected, "RequesterPermissionDenied"), Pick(DetentionDecisions.Decide(Facts("RequesterPermissionDenied"))));
        Assert.Equal((DetentionDecisionState.Waiting, "DetentionPolicyUnavailable"), Pick(DetentionDecisions.Decide(Facts(policy: false))));
        Assert.Equal((DetentionDecisionState.Rejected, "ThresholdNotMet"), Pick(DetentionDecisions.Decide(Facts(count: 1))));
        Assert.Equal((DetentionDecisionState.Waiting, "AmbiguousSubjectIdentity"), Pick(DetentionDecisions.Decide(Facts(registry: Registry.Append(new KnownPersonnel(3, 11, "worker"))))));
        var existing = DetentionDecisions.Decide(Facts(active: true, activeExpires: 101, count: 0));
        Assert.Equal((DetentionAuthorizationChoice.ReuseActive, 7L, 101L, "ExistingAuthorization"), (existing.Authorization, existing.AuthorizationId, existing.Expires, existing.Reason));
        var issued = DetentionDecisions.Decide(Facts(active: true, activeExpires: 100));
        Assert.Equal((DetentionAuthorizationChoice.IssueNew, DetentionDecisionState.Approved, "Approved", 1L), (issued.Authorization, issued.State, issued.Reason, issued.SubjectActorId));
    }

    [Fact]
    public void RetriedDetentionRequestsReuseOnlyALiveAuthorization()
    {
        var approved = Prior(DetentionDecisionState.Approved, "Approved", 3, 150);
        Assert.Equal((DetentionDecisionState.Rejected, "IdempotencyConflict"), Pick(DetentionDecisions.Decide(Facts("PersonnelUnavailable", approved, same: false))));
        // A matching retry that fails validation keeps its own validation answer.
        Assert.Equal((DetentionDecisionState.Waiting, "PersonnelUnavailable"), Pick(DetentionDecisions.Decide(Facts("PersonnelUnavailable", approved))));
        var duplicate = DetentionDecisions.Decide(Facts(prior: approved, priorExpires: 150));
        Assert.Equal((DetentionAuthorizationChoice.ReusePrior, 3L, 150L, "DuplicateRequest"), (duplicate.Authorization, duplicate.AuthorizationId, duplicate.Expires, duplicate.Reason));
        Assert.Equal("PriorRequestClosed", DetentionDecisions.Decide(Facts(prior: approved, priorExpires: 150, released: true)).Reason);
        Assert.Equal("PriorRequestClosed", DetentionDecisions.Decide(Facts(prior: approved, priorExpires: 100)).Reason);
        Assert.Equal((DetentionDecisionState.Waiting, "AmbiguousSubjectIdentity"), Pick(DetentionDecisions.Decide(Facts(prior: Prior(DetentionDecisionState.Waiting, "AmbiguousSubjectIdentity")))));
        Assert.True(DetentionDecisions.PriorRequestClosed(approved, 150, 100, false, true));
        Assert.False(DetentionDecisions.PriorRequestClosed(Prior(DetentionDecisionState.Waiting, "DetentionPolicyUnavailable"), 0, 100, true, true));
    }

    [Fact]
    public void SupervisorRequestsDetentionOnlyAtThreshold()
    {
        var personnel = new PersonnelConfig("supervisor", 1000, 2);
        Assert.True(DetentionDecisions.ShouldRequestDetention(new CaseDecisionPayload(4, 1, 11, 2, "Accepted"), personnel, "supervisor"));
        Assert.False(DetentionDecisions.ShouldRequestDetention(new CaseDecisionPayload(4, 1, 11, 1, "Accepted"), personnel, "supervisor"));
        Assert.False(DetentionDecisions.ShouldRequestDetention(new CaseDecisionPayload(4, 1, 11, 2, "Accepted"), personnel, "worker"));
        Assert.False(DetentionDecisions.ShouldRequestDetention(new CaseDecisionPayload(4, 0, 11, 0, "IdempotencyConflict"), personnel, "supervisor"));
        Assert.False(DetentionDecisions.ShouldRequestDetention(new CaseDecisionPayload(4, 1, 11, 2, "Accepted"), null, "supervisor"));
    }

    [Fact]
    public void ReprimandWindowRestartsAfterAReleaseVisibleAtTheVersionBound()
    {
        var personnel = new PersonnelConfig("supervisor", 100, 2);
        var reprimands = new[] { new ReprimandEntry(1, 11, 10), new ReprimandEntry(2, 11, 150), new ReprimandEntry(3, 11, 190), new ReprimandEntry(4, 12, 195) };
        var release = new RecordEntry(RecordKind.DetentionRelease, 9, 11, 40, 180, "release/9", new[] { new RecordField(AttributeKind.ObservedTick, FactValue.Integer(160)) });
        Assert.Equal(new long[] { 2, 3 }, ReprimandPolicy.QualifyingCases(personnel, 11, 200, Array.Empty<RecordEntry>(), reprimands));
        Assert.Equal(new long[] { 3 }, ReprimandPolicy.QualifyingCases(personnel, 11, 200, new[] { release }, reprimands));
        Assert.Equal(new long[] { 2, 3 }, ReprimandPolicy.QualifyingCases(personnel, 11, 200, new[] { release }, reprimands, throughRecordVersion: 39));
        Assert.Equal(new long[] { 2 }, ReprimandPolicy.QualifyingCases(personnel, 11, 200, Array.Empty<RecordEntry>(), reprimands, throughCaseId: 2));
        Assert.Empty(ReprimandPolicy.QualifyingCases(null, 11, 200, Array.Empty<RecordEntry>(), reprimands));
    }

    [Fact]
    public void CaseAcceptanceChoosesOneCanonicalCase()
    {
        Assert.Equal((CaseAcceptance.Rejected, "IdempotencyConflict"), Pick(CasePolicy.Decide("InvalidQueryProvenance", true, true, 11, 4, 11, 9)));
        Assert.Equal((CaseAcceptance.Rejected, "InvalidQueryProvenance"), Pick(CasePolicy.Decide("InvalidQueryProvenance", false, false, 0, 0, 11, 9)));
        Assert.Equal((CaseAcceptance.OpenCase, "Accepted"), Pick(CasePolicy.Decide("", false, false, 0, 0, 11, 9)));
        Assert.Equal((CaseAcceptance.Rejected, "CanonicalCaseAccountConflict"), Pick(CasePolicy.Decide("", false, true, 12, 4, 11, 9)));
        Assert.Equal((CaseAcceptance.AlreadyAccepted, "AlreadyAccepted"), Pick(CasePolicy.Decide("", false, true, 11, 4, 11, 9)));
        // Restore re-validates the opening filing itself.
        Assert.Equal((CaseAcceptance.OpenCase, "Accepted"), Pick(CasePolicy.Decide("", false, true, 11, 9, 11, 9)));
        Assert.True(CasePolicy.Accepts("AlreadyAccepted"));
        Assert.False(CasePolicy.Accepts("CanonicalCaseAccountConflict"));
    }

    private static KnowledgeFact Learned(long version, AttributeKind key, FactValue value, long action = 30) =>
        new(1, new FactKey(SubjectKind.Record, version, key), value, EvidenceKind.RecordQuery, action, 50, 60);
    private static KnowledgeFact[] AttemptRow(long version, long machine)
    {
        var text = (string s) => new FactValue(FactValueKind.Text, text: s);
        return new[] { Learned(version, AttributeKind.RecordType, text("WorkAttempt")), Learned(version, AttributeKind.RecordEntityId, FactValue.Integer(8)), Learned(version, AttributeKind.RecordVersion, FactValue.Integer(version)),
            Learned(version, AttributeKind.AccountId, FactValue.Integer(11)), Learned(version, AttributeKind.RecordFieldCount, FactValue.Integer(2)),
            Learned(version, AttributeKind.MachineId, FactValue.Integer(machine)), Learned(version, AttributeKind.TaskKind, text("RepairMachine")) };
    }
    private static AuditPageInput Page(KnowledgeFact[] learned, int disclosed, bool hasMore = false, int pages = 1, long cutoff = 20) =>
        new(true, RecordKind.WorkAttempt, cutoff, pages, 4, 32, new RecordQueryReceipt(new RecordQuery(RecordKind.WorkAttempt, limit: 1, history: true, machineId: 5), cutoff, hasMore ? 6 : cutoff, hasMore, 60, disclosed),
            30, learned, Array.Empty<RecordEntry>(), Array.Empty<long>(), 5, "good-part", "bad-part", 70);

    [Fact]
    public void AuditPagesKeepRelevantDisclosedRowsAndStopAtLimits()
    {
        var read = AuditPlanning.ReadPage(Page(AttemptRow(6, 5), 1));
        Assert.True(read.AdvanceToReports); Assert.Null(read.FinishReason); Assert.Equal(0L, read.Cursor);
        Assert.Equal(new long[] { 6 }, read.Evidence.Select(r => r.Version)); Assert.Equal(new long[] { 30 }, read.QueryActionIds);
        var other = AuditPlanning.ReadPage(Page(AttemptRow(6, 4), 1, hasMore: true));
        Assert.Empty(other.Evidence); Assert.Equal(6L, other.Cursor); Assert.Null(other.FinishReason); Assert.False(other.AdvanceToReports);
        Assert.Equal("HistoryPageLimitExceeded", AuditPlanning.ReadPage(Page(AttemptRow(6, 5), 1, hasMore: true, pages: 4)).FinishReason);
        var missing = AuditPlanning.ReadPage(Page(AttemptRow(6, 5), 2));
        Assert.Equal("IncompleteDisclosure", missing.FinishReason); Assert.Null(missing.Cursor);
        Assert.Equal("IncompleteDisclosure", AuditPlanning.ReadPage(Page(AttemptRow(6, 5).Take(6).ToArray(), 1)).FinishReason);
    }

    [Fact]
    public void AuditDeskAndArbitrationRules()
    {
        var grants = new[] { (3L, RecordKind.WorkAttempt, "supervisor"), (4L, RecordKind.WorkAttempt, "supervisor"), (4L, RecordKind.WorkReport, "supervisor"), (5L, RecordKind.WorkAttempt, "supervisor"), (5L, RecordKind.WorkReport, "supervisor") };
        Assert.Equal(4L, AuditPlanning.ChooseTerminal(grants, "supervisor"));
        Assert.Equal(0L, AuditPlanning.ChooseTerminal(grants, "worker"));
        Assert.True(AuditPlanning.ProgressResetsBackoff(ActionKind.QueryRecords, 4, 4));
        Assert.False(AuditPlanning.ProgressResetsBackoff(ActionKind.ReadInbox, 4, 4));
        Assert.False(AuditPlanning.DefersToOtherWork(false, true, true, true, false));
        Assert.True(AuditPlanning.DefersToOtherWork(false, false, true, false, false));
        Assert.Equal("PlanBudgetExhausted", AuditPlanning.PlanFailureReason(PlanOutcome.BudgetExhausted));
    }

    [Fact]
    public void GuardOutcomesAndCustodyRetries()
    {
        Assert.True(GuardOutcomePolicy.Terminal("TeamNotReady")); Assert.False(GuardOutcomePolicy.Terminal("SearchUnreachable")); Assert.True(GuardOutcomePolicy.Valid("SearchUnreachable"));
        Assert.True(GuardOutcomePolicy.NeedsAttention(true, EscortDispatchProgress.TeamForming));
        Assert.False(GuardOutcomePolicy.NeedsAttention(true, EscortDispatchProgress.Detained));
        var outcomes = new[] { new[] { "EquipmentUnavailable", "TargetNotLocated" }, new[] { "EscortInterrupted" } };
        Assert.True(GuardOutcomePolicy.RequestReplacementTeam(EscortDispatchProgress.NeedsAttention, outcomes, true));
        Assert.False(GuardOutcomePolicy.RequestReplacementTeam(EscortDispatchProgress.NeedsAttention, outcomes.Append(new[] { "SearchUnreachable" }), true));
        Assert.False(GuardOutcomePolicy.RequestReplacementTeam(EscortDispatchProgress.NeedsAttention, outcomes, false));
        Assert.Equal("KnownOrderReassigned", GuardOutcomePolicy.RevocationReason("AcknowledgmentOverdue"));
        Assert.Equal(CustodyRetry.AwaitLocalHandoff, GuardOutcomePolicy.AfterFailedHandoff(FailureReason.UnknownRequiredFact, false, true));
        Assert.Equal(CustodyRetry.None, GuardOutcomePolicy.AfterFailedHandoff(FailureReason.NotAtRequiredTile, true, true));
        Assert.Equal(CustodyRetry.Reinspect, GuardOutcomePolicy.AfterFailedHandoff(FailureReason.NotAtRequiredTile, false, false));
    }

    [Fact]
    public void DetentionServiceAdmissionAndReleaseRules()
    {
        var facility = new DetentionFacility(1, new Coord(0, 0, 0), 2, 1);
        Assert.Equal("Intake/NoPower", DetentionServicePolicy.IntakeRefusal(FailureReason.ServiceBusy, facility, false, true, 0));
        Assert.Equal("Intake/FacilityFull", DetentionServicePolicy.IntakeRefusal(FailureReason.ServiceBusy, facility, true, true, 2));
        Assert.Equal("Intake/ServiceBusy", DetentionServicePolicy.IntakeRefusal(FailureReason.ServiceBusy, facility, true, true, 1));
        Assert.Equal("Intake/MissingTool", DetentionServicePolicy.IntakeRefusal(FailureReason.MissingTool, facility, true, true, 0));
        Assert.False(DetentionServicePolicy.RetryIntake("Intake/FacilityFull")); Assert.True(DetentionServicePolicy.RetryIntake("Intake/ServiceChanged"));
        Assert.True(DetentionServicePolicy.IntakeAdmits(facility, 1, 0)); Assert.False(DetentionServicePolicy.IntakeAdmits(facility, 1, 1));
        Assert.True(DetentionServicePolicy.PreemptNeedForRelease(facility, true, true, 0, true));
        Assert.False(DetentionServicePolicy.PreemptNeedForRelease(facility, true, false, 0, true));
        Assert.False(DetentionServicePolicy.ReleaseReady(facility, true, true, 0, false, false));
        Assert.True(DetentionServicePolicy.ReleaseReady(facility, true, true, 0, false, true));
    }

    [Fact]
    public void SurveyAndRecoveryAnswers()
    {
        Assert.Equal("NotYetCredited", SurveyEvidencePolicy.Admissibility(false, 0, 4, 10, 20, 2, 15, 18));
        Assert.Equal("InvalidCapacityOrObservationTime", SurveyEvidencePolicy.Admissibility(true, 12, 4, 10, 20, 5, 15, 18));
        Assert.Equal("", SurveyEvidencePolicy.Admissibility(true, 12, 4, 10, 20, 2, 15, 18));
        Assert.Equal(9L, SurveyEvidencePolicy.CorrectedBelief(10, 4, 3));
        Assert.Equal(LostCardResponse.OpenCaseAndRevoke, RecoveryCasePolicy.OnLostCardReport(false));
        Assert.Equal(LostCardResponse.ReplyWithExistingCase, RecoveryCasePolicy.OnLostCardReport(true));
    }

    private static (DetentionDecisionState, string) Pick(DetentionRequestChoice c) => (c.State, c.Reason);
    private static (CaseAcceptance, string) Pick(CaseAcceptanceChoice c) => (c.Outcome, c.Reason);
}
