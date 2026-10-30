using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

// T08 / R-ARC-04: each test runs two identical worlds, changes state the decider cannot know in one
// of them just before the decision, and requires the same choice in both.
public sealed class DecisionInputIsolationTests
{
    private static LoadedScenario Load(string scenario, Action<JsonObject>? change = null, string catalog = "jobs")
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", scenario + ".json")))!.AsObject(); change?.Invoke(json);
        json["assertions"] = new JsonArray();
        return new(json.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", catalog + ".json")), "isolation-test");
    }
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    internal static object Get(object owner, string field) => owner.GetType().GetField(field, Any)!.GetValue(owner)!;
    internal static void Set(object owner, string member, object? value)
    {
        var field = owner.GetType().GetField(member, Any);
        if (field != null) field.SetValue(owner, value); else owner.GetType().GetProperty(member, Any)!.SetValue(owner, value);
    }
    internal static object Entry(World w, string map, object key) => ((IDictionary)Get(w, map))[key]!;
    internal static object Actor(World w, long id) => ((IDictionary)Get(w, "actors"))[new Id<ActorTag>(id)]!;
    // Advances both worlds to the same tick, hides a change in the second, then advances both to the same end.
    private static (World Plain, World Changed) Twin(Func<World> make, long changeAt, Action<World> change, long end)
    {
        var plain = make(); var changed = make();
        if (changeAt >= 0) { plain.AdvanceTo(changeAt); changed.AdvanceTo(changeAt); }
        change(changed);
        plain.AdvanceTo(end); changed.AdvanceTo(end); return (plain, changed);
    }
    // A forged physical identity: the supervisor's body now claims another role and account. The institution never saw it.
    private static void Disguise(World w, long actor) { var a = Actor(w, actor); Set(a, "Role", "worker"); Set(a, "Account", new Id<AccountTag>(999)); }

    [Fact]
    public void T08_DetentionRequestAuthenticationAndReplyUseRegistrationsNotPhysicalActors()
    {
        var (plain, changed) = Twin(() => Load("detention_authorization").CreateWorld(42, true), 112, w => Disguise(w, 2), 113);
        var expected = Assert.Single(plain.InspectDetentionRequests()); var actual = Assert.Single(changed.InspectDetentionRequests());
        Assert.Equal(DetentionDecisionState.Approved, expected.Decision.State);
        Assert.Equal(expected.Decision.State, actual.Decision.State); Assert.Equal(expected.Decision.Reason, actual.Decision.Reason);
        Assert.NotEqual(0, expected.ReplyMessageId); Assert.Equal(expected.ReplyMessageId, actual.ReplyMessageId);
    }

    [Fact]
    public void T08_CaseFilingAuthenticationAndReplyUseRegistrationsNotPhysicalActors()
    {
        var (plain, changed) = Twin(() => Load("personnel_case").CreateWorld(42, true), 66, w => Disguise(w, 2), 67);
        string Decided(World w) => Assert.Single(w.Trace, e => e.Type == "CaseSubmissionDecided").Detail;
        Assert.Equal(Decided(plain), Decided(changed)); Assert.Single(changed.InspectCases());
        Assert.Equal(plain.InspectMessages().Count(m => m.Draft.Kind == MessageKind.CaseDecision), changed.InspectMessages().Count(m => m.Draft.Kind == MessageKind.CaseDecision));
    }

    [Fact]
    public void T08_DetentionIntakeChecksThePublishedAuthorizationRecordNotTheAuthorityRegister()
    {
        // The register entry changes without a record; only the authority itself could see it.
        var (plain, changed) = Twin(() => Load("detention_release", catalog: "escort").CreateWorld(42, true), 494, w => Set(Entry(w, "detentionAuthorizations", 1L), "Status", DetentionAuthorizationStatus.Revoked), 495);
        foreach (var w in new[] { plain, changed })
        {
            Assert.Single(w.Trace, e => e.Type == "DetentionSlotReserved" && e.Tick == 495);
            Assert.DoesNotContain(w.Trace, e => e.Type == "GuardRevocationLearned"); Assert.Empty(w.InspectKnownGuardRevocations());
        }
    }

    [Fact]
    public void T08_SupervisorDetentionRequestUsesThePublishedThresholdNotThePrivatePersonnelConfig()
    {
        // The institution privately raises its threshold after publishing 2; the supervisor knows only the publication.
        var (plain, changed) = Twin(() => Load("detention_authorization").CreateWorld(42, true), 110, w => Set(w, "<Personnel>k__BackingField", new PersonnelConfig("supervisor", 6048000, 5)), 111);
        Assert.Equal(5, changed.Personnel!.DetentionThreshold);
        foreach (var w in new[] { plain, changed }) Assert.Equal(DetentionRequestStatus.Queued, Assert.Single(w.InspectLocalDetentionRequests(new(2))).Status);
    }

    [Fact]
    public void T08_AuditTerminalComesFromTheCitizensOwnGrantsNotTheAuthorityGrantTable()
    {
        // The records office silently drops the supervisor's grants; the supervisor still holds its grant notice.
        var (plain, changed) = Twin(() => Load("discrepancy_audit").CreateWorld(42, true), 28, w => ((IDictionary)Get(w, "recordAccess")).Clear(), 29);
        long Terminal(World w) => Assert.Single(w.Trace, e => e.Type == "DiscrepancyAuditOpened").TerminalId;
        Assert.NotEqual(0, Terminal(plain)); Assert.Equal(Terminal(plain), Terminal(changed));
        Assert.Equal(plain.InspectAudits(new(2)).Single().TerminalId, changed.InspectAudits(new(2)).Single().TerminalId);
    }

    [Fact]
    public void T08_ConversationInitiatorChoosesFromVisiblePartnersNotTheirPrivateGoals()
    {
        // Citizen 1 privately loses its roster, so it believes it is on duty and will decline; citizen 2 can see only who is present and still.
        var (plain, changed) = Twin(() => Load("conversation", catalog: "tiles").CreateWorld(42, true), -1, w => Set(Entry(w, "routines", new Id<ActorTag>(1)), "KnownRoster", null), 0);
        string Choice(World w) => w.Trace.First(e => e.Type == "PlanSelected" && e.ActorId == 2).Detail;
        Assert.EndsWith("partner=1", Choice(plain)); Assert.Equal(Choice(plain), Choice(changed));
        Assert.Contains(changed.Trace, e => e.Type == "ActionFailed" && e.ActorId == 2 && e.Detail.Contains("ConsentDeclined"));
    }
}
