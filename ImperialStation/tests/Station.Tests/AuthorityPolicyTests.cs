using Station.Contracts;
using Station.Mind;
using Xunit;

namespace Station.Tests;

// Pure authority/citizen decision rules moved into Station.Mind (R-ARC-01/03/05).
public sealed class AuthorityPolicyTests
{
    private sealed class View : IAuthorityView
    {
        public long Tick { get; init; }
        public long NominalTravelTicks { get; init; } = 1;
        public PublicKnowledge Publication { get; init; } = new(1, Array.Empty<KnownTile>(), Array.Empty<PublicService>());
        public IReadOnlyList<KnownPersonnel> Personnel { get; init; } = Array.Empty<KnownPersonnel>();
        public long LoginTicks => 1;
        public long PostTicks => 1;
        public long LatencyTicks => 1;
        public long CheckInWindow => 0;
    }
    private static RosterPayload OnShift(long actor, int cohort = 0, Coord? rest = null) => new(1, actor, cohort, 100, 0, 50, 0, rest);
    private static RosterPayload OffShift(long actor) => new(1, actor, 0, 100, 60, 30, 0, null);
    private static KnownPersonnel Person(long actor, string role, RosterPayload? roster) => new(actor, actor + 100, role, roster);
    private static PublicKnowledge Catalog() => new(1, Array.Empty<KnownTile>(), Array.Empty<PublicService>(),
        new PublicWorkCatalog(Array.Empty<ResourceDefinition>(), Array.Empty<RecipeDefinition>(), Array.Empty<PublicWorkstation>(),
            new[] { new PublicEquipment(7, new Coord(5, 0, 0), "cartridge", "", new[] { "worker" }, 3, 2) }, 1,
            new[] { new PublicCleanable(new Id<TileTag>(9), new Coord(0, 4, 0), 2, "", new[] { "cleaner" }) }));

    [Fact]
    public void AdmissionBudgetAdmitsAtLeastOneAndCountsOnlyKnownOnDutyCohort()
    {
        var policy = new AuthorityConfig(incidentAdmissionPermille: 50);
        Assert.Equal(1, IncidentPolicy.AdmissionBudget(0, policy));
        Assert.Equal(2, IncidentPolicy.AdmissionBudget(40, policy));
        var view = new View { Personnel = new[] { Person(1, "w", OnShift(1)), Person(2, "w", OffShift(2)), Person(3, "w", OnShift(3, 1)), Person(4, "w", null) } };
        Assert.Equal(1, IncidentPolicy.KnownOnDutyHeadcount(view, 0, 10));
        Assert.True(IncidentPolicy.DepthLimited(2, new AuthorityConfig(maximumReplacementDepth: 2)));
        Assert.False(IncidentPolicy.DepthLimited(1, new AuthorityConfig(maximumReplacementDepth: 2)));
        Assert.True(IncidentPolicy.AdmissionDeferred(-1, 3, 3));
        Assert.False(IncidentPolicy.AdmissionDeferred(4, 3, 3));
    }

    [Fact]
    public void ResponderFollowsJobKindAndCandidateSource()
    {
        var view = new View { Tick = 10, Publication = Catalog(), Personnel = new[] { Person(1, "worker", OnShift(1)), Person(2, "supervisor", OnShift(2)), Person(3, "cleaner", OnShift(3)) } };
        DispatchCandidate Gather(KnownPersonnel p) => new(p, new Coord(0, 0, 0), false);
        var shared = view.Personnel.Select(Gather).ToArray();
        Assert.Equal(1, IncidentPolicy.Responder(view, new AuthorityJobTarget(WorkKind.RepairMachine, 7, 0, 0, null), 0, "supervisor", Gather, shared));
        Assert.Equal(2, IncidentPolicy.Responder(view, new AuthorityJobTarget(WorkKind.VerifyMachine, 7, 0, 0, null), 0, "supervisor", Gather, Array.Empty<DispatchCandidate>()));
        Assert.Equal(3, IncidentPolicy.Responder(view, new AuthorityJobTarget(WorkKind.CleanTile, 0, 9, 0, null), 0, "supervisor", Gather, shared));
        Assert.Equal(0, IncidentPolicy.Responder(view, new AuthorityJobTarget(WorkKind.RepairMachine, 7, 0, 0, null), 1, "supervisor", Gather, shared));
        Assert.Equal(new Coord(5, 0, 0), IncidentPolicy.JobSite(view, new AuthorityJobTarget(WorkKind.VerifyMachine, 7, 0, 0, null)));
        Assert.Null(IncidentPolicy.JobSite(view, new AuthorityJobTarget(WorkKind.CleanTile, 0, 8, 0, null)));
        Assert.Equal("ReportedBlockedByReplacementChain", IncidentPolicy.WorkResolution(MessageKind.WorkBlocked, true));
        Assert.True(IncidentPolicy.CancelOnOriginalResult(MessageKind.WorkCompleted, true));
        Assert.False(IncidentPolicy.CancelOnOriginalResult(MessageKind.WorkBlocked, true));
        Assert.False(IncidentPolicy.CancelOnParentEnded(5, true));
    }

    [Fact]
    public void PatrolSearchSitesAndCitizenGates()
    {
        var a = new Coord(1, 0, 0); var b = new Coord(2, 0, 0);
        Assert.Equal(new[] { a, b }, PatrolPlanning.SearchSites(a, a, b));
        Assert.Empty(PatrolPlanning.SearchSites(null, null, null));
        Assert.Equal(MessageKind.PatrolFound, PatrolPlanning.SearchResult(true, 1, 3));
        Assert.Null(PatrolPlanning.SearchResult(false, 1, 3));
        Assert.Equal(MessageKind.PatrolNotLocated, PatrolPlanning.SearchResult(false, 3, 3));
        Assert.True(PatrolPlanning.Found(FactValue.At(a)));
        Assert.False(PatrolPlanning.Found(FactValue.Absent(a)));
    }

    [Fact]
    public void EscortSearchSitesDemoteASearchedVisitAndTeamsExcludePriorGuards()
    {
        var visit = new Coord(1, 0, 0); var work = new Coord(2, 0, 0); var older = new Coord(3, 0, 0); var rest = new Coord(4, 0, 0);
        var assignments = new[] { new KnownAssignmentSite(1, older), new KnownAssignmentSite(2, work), new KnownAssignmentSite(3, null) };
        Assert.Equal(new[] { visit, work, older, rest }, EscortPolicy.SearchSites(visit, false, assignments, rest));
        Assert.Equal(new[] { work, older, rest, visit }, EscortPolicy.SearchSites(visit, true, assignments, rest));
        var dispatch = new EscortDispatchPolicy(1, rest, rest, "cuffs", guardCount: 2);
        var order = new EscortOrderPayload(1, 101, 1, 100, 10, dispatch, new[] { 1L }, new[] { 2L, 3L }, new[] { visit });
        Assert.True(EscortPolicy.SearchedSinceVisit(1, visit, 5, new[] { new GuardSearchEvidence("TargetNotLocated", 5, order) }));
        Assert.False(EscortPolicy.SearchedSinceVisit(1, visit, 6, new[] { new GuardSearchEvidence("TargetNotLocated", 5, order) }));
        Assert.False(EscortPolicy.SearchedSinceVisit(1, visit, 5, new[] { new GuardSearchEvidence("TeamNotReady", 5, order) }));
        var view = new View { Tick = 10, Personnel = new[] { Person(1, "guard", OnShift(1)), Person(2, "guard", OnShift(2)), Person(3, "guard", OnShift(3)), Person(4, "guard", OnShift(4)) } };
        DispatchCandidate Gather(KnownPersonnel p) => new(p, rest, false);
        Assert.Equal(new[] { 3L, 4L }, EscortPolicy.Team(view, 1, dispatch, new[] { 2L }, Gather));
        Assert.Empty(EscortPolicy.Team(view, 1, dispatch, new[] { 2L, 3L }, Gather));
        Assert.Equal("KnownOrderExpired", EscortPolicy.GuardOrderResponse(order, "guard", 100));
        Assert.Equal("NotQualified", EscortPolicy.GuardOrderResponse(order, "cook", 99));
        Assert.Equal("Accepted", EscortPolicy.GuardOrderResponse(order, "guard", 99));
        Assert.Equal(EscortTeamResponse.Blocked, EscortPolicy.TeamResponse("NotQualified", false, new[] { "Accepted" }));
        Assert.Equal(EscortTeamResponse.TeamForming, EscortPolicy.TeamResponse("Accepted", false, new[] { "Accepted", "Accepted" }));
        Assert.Equal(EscortTeamResponse.Unchanged, EscortPolicy.TeamResponse("Accepted", true, new[] { "Accepted", "Accepted" }));
        Assert.True(EscortPolicy.ReassignUnacknowledgedTeam(new[] { 0L, 0L }, true));
        Assert.False(EscortPolicy.ReassignUnacknowledgedTeam(new[] { 0L, 9L }, true));
        Assert.Equal(100, EscortPolicy.UrgentAcknowledgmentTicks(1200, 7));
        Assert.Equal(7, EscortPolicy.UrgentAcknowledgmentTicks(null, 7));
        Assert.Equal(1, EscortPolicy.UrgentAcknowledgmentTicks(null, null));
    }

    [Fact]
    public void FacilityReportsTasksHoldsAndHelpers()
    {
        var config = new FacilityConfig(dirtThreshold: 2);
        var dirt = new FactKey(SubjectKind.Tile, 9, AttributeKind.Dirt);
        Assert.True(FacilityPolicy.Adverse(config, dirt, FactValue.Integer(2)));
        Assert.False(FacilityPolicy.Adverse(config, dirt, FactValue.Integer(1)));
        Assert.True(FacilityPolicy.Observes(config, EvidenceKind.Inspection, dirt, "cleaner"));
        Assert.False(FacilityPolicy.Observes(config, EvidenceKind.MessageRead, dirt, "cleaner"));
        Assert.True(FacilityPolicy.ShouldReport(true, false, 10, 50, dirt, Array.Empty<MessageDraft>()));
        Assert.False(FacilityPolicy.ShouldReport(true, true, 10, 50, dirt, Array.Empty<MessageDraft>()));
        Assert.True(FacilityPolicy.ShouldReport(true, true, 50, 50, dirt, Array.Empty<MessageDraft>()));
        Assert.True(FacilityPolicy.Published(Catalog().Work, dirt));
        Assert.False(FacilityPolicy.Published(null, dirt));
        Assert.Equal(FacilityReportDisposition.Stale, FacilityPolicy.Disposition(true, true, 10, 10));
        Assert.Equal(FacilityReportDisposition.NewTask, FacilityPolicy.Disposition(true, true, 10, 11));
        Assert.Equal(FacilityReportDisposition.NewTask, FacilityPolicy.Disposition(false, false, 0, 0));
        Assert.Equal(FacilityReportDisposition.Merge, FacilityPolicy.Disposition(true, false, 0, 0));
        Assert.True(FacilityPolicy.Reopens(true, 5, 4));
        Assert.False(FacilityPolicy.Reopens(true, 4, 4));
        Assert.Equal(FacilityWorkResponse.Reopen, FacilityPolicy.WorkResponse(MessageKind.WorkCompleted, 3, 4));
        Assert.Equal(FacilityWorkResponse.Complete, FacilityPolicy.WorkResponse(MessageKind.WorkCompleted, 4, 4));
        Assert.Equal(FacilityWorkResponse.NeedsAttention, FacilityPolicy.WorkResponse(MessageKind.OrderRejected, 0, 4));
        Assert.Equal(FacilityWorkResponse.Ignored, FacilityPolicy.WorkResponse(MessageKind.OrderAccepted, 0, 4));
        Assert.False(FacilityPolicy.EvidenceHold(false, new[] { new QualityHoldEvidence(QualityCheckStage.Scheduled, 1, null) }, 0, 10, _ => false));
        Assert.True(FacilityPolicy.EvidenceHold(true, new[] { new QualityHoldEvidence(QualityCheckStage.Assigned, 1, null) }, 0, 10, _ => false));
        var discrepancy = new[] { new QualityHoldEvidence(QualityCheckStage.ReportedDiscrepancy, 1, 5) };
        Assert.True(FacilityPolicy.EvidenceHold(true, discrepancy, 14, 10, _ => false));
        Assert.False(FacilityPolicy.EvidenceHold(true, discrepancy, 15, 10, _ => false));
        Assert.False(FacilityPolicy.EvidenceHold(true, discrepancy, 14, 10, attempt => attempt == 1));
        Assert.True(FacilityPolicy.AcknowledgmentOverdue(10, 5, 15));
        Assert.False(FacilityPolicy.AcknowledgmentOverdue(10, 5, 14));
        Assert.Equal(0, FacilityPolicy.CleanupHelpersWanted(100, 10, 10, 0, 0));
        Assert.Equal(2, FacilityPolicy.CleanupHelpersWanted(100, 10, 35, 0, 0));
        Assert.Equal(1, FacilityPolicy.CleanupHelpersWanted(100, 10, 35, 1, 1));
        Assert.Equal(0, FacilityPolicy.CleanupHelpersWanted(100, 10, 35, 4, 0));
        Assert.False(FacilityPolicy.CleanupHelpersApply(0, true, true));
    }

    [Fact]
    public void QualitySamplingVerdictsAndSupervisorChoice()
    {
        Assert.True(QualityPolicy.Sampled(new QualityAssuranceConfig(samplePermille: 250), 249));
        Assert.False(QualityPolicy.Sampled(new QualityAssuranceConfig(samplePermille: 250), 250));
        Assert.Equal(VerificationVerdict.Clear, QualityPolicy.Verdict(MessageKind.WorkCompleted, FactValue.Boolean(true)));
        Assert.Equal(VerificationVerdict.Discrepancy, QualityPolicy.Verdict(MessageKind.WorkCompleted, FactValue.Boolean(false)));
        Assert.Equal(VerificationVerdict.NeedsAttention, QualityPolicy.Verdict(MessageKind.WorkBlocked, FactValue.Boolean(false)));
        var view = new View { Tick = 10, Publication = Catalog(), Personnel = new[] { Person(1, "supervisor", OnShift(1)), Person(2, "supervisor", OnShift(2)), Person(3, "supervisor", OffShift(3)), Person(4, "worker", OnShift(4)) } };
        var gathered = new List<long>();
        DispatchCandidate Gather(KnownPersonnel p) { gathered.Add(p.ActorId); return new(p, p.ActorId == 1 ? new Coord(0, 0, 0) : new Coord(5, 0, 0), false); }
        var candidates = QualityPolicy.VerificationCandidates(view, "supervisor", Gather).ToArray();
        Assert.Equal(new[] { 1L, 2L }, gathered);
        Assert.Equal(2, QualityPolicy.Verifier(view, 7, 0, candidates));
        Assert.Equal(1, QualityPolicy.Verifier(view, 7, 2, candidates));
        Assert.Equal(0, QualityPolicy.Verifier(view, 8, 0, candidates));
        Assert.True(VerificationPlanning.Discrepancy(true, "a", "b"));
        Assert.Equal("VerifiedClear", VerificationPlanning.Finding(VerificationPlanning.Discrepancy(true, "a", "a")));
    }

    [Fact]
    public void CitizenOrderPriorityAndGrowthRules()
    {
        var orders = new[] { new KnownLocalOrder(3, WorkKind.ConstructHabitat, true), new KnownLocalOrder(4, WorkKind.RepairMachine, true) };
        Assert.True(ConstructionPlanning.YieldsToEarlierOrder(5, orders));
        Assert.False(ConstructionPlanning.YieldsToEarlierOrder(3, orders));
        Assert.True(VerificationPlanning.YieldsToOlderOrder(5, orders));
        Assert.False(VerificationPlanning.YieldsToOlderOrder(5, new[] { new KnownLocalOrder(4, WorkKind.RepairMachine, true), new KnownLocalOrder(2, WorkKind.CleanTile, false) }));
        Assert.Equal(0, ExpansionPlanning.Deficit(10, 10, 0));
        Assert.Equal(3, ExpansionPlanning.Deficit(5, 10, 2));
        Assert.Equal(0, ExpansionPlanning.Deficit(5, 10, 9));
        Assert.True(ExpansionPlanning.AcceptsCompletionClaim(0));
        Assert.False(ExpansionPlanning.AcceptsCompletionClaim(4));
        var inputs = new[] { new MaterialAmount("plate", 1) };
        var options = new[] { new HabitatOption("slow", TileFamily.Habitat, 2, new[] { "builder" }, 9, inputs), new HabitatOption("fast", TileFamily.Habitat, 1, new[] { "builder" }, 4, inputs),
            new HabitatOption("power", TileFamily.Power, 1, new[] { "builder" }, 1, inputs), new HabitatOption("nobody", TileFamily.Habitat, 1, new[] { "cook" }, 1, inputs) };
        var found = ExpansionPlanning.Candidates(new[] { new Coord(0, 0, 0) }, new Coord(0, 0, 0), new Coord(1, 1, 0), new[] { new Coord(0, 1, 0) }, options, new[] { "builder" }, 2);
        Assert.Equal(new[] { ("fast", new Coord(1, 0, 0)), ("slow", new Coord(1, 0, 0)) }, found.Select(c => (c.DefinitionId, c.Site)));
        Assert.Equal(new[] { 1L, 2L }, found.Select(c => c.Id));
    }
}
