using Station.Contracts;
using Station.Core;
using Station.Runner;
using Station.Mind;
using Xunit;

namespace Station.Tests;

public sealed class RecordTests
{
    private static readonly Coord Desk = new(0, 0, 0);
    private static readonly Id<ActorTag> Supervisor = new(2);
    private static World Make(bool audit = true, bool report = false, long sessionTicks = 100, int capacity = 256, ulong seed = 1, long queryTicks = 3)
    {
        var work = new WorkSeed(new[] { new ResourceDefinition("card"), new ResourceDefinition("part", "slot") }, Array.Empty<RecipeDefinition>(), Array.Empty<WorkstationDefinition>(),
            new[] { new MachineDefinition(1, new(1, 0, 0), "slot", "part", new WorkAccess(new[] { "worker" })) });
        var info = new InformationSeed(new InformationConfig(loginTicks: 1, sessionTicks: sessionTicks, knowledgeCapacity: capacity, queryTicks: queryTicks),
            new[] {
                new TerminalSeed(1, Desk, new[] { "worker", "supervisor" }, capabilities: TerminalCapability.Records | TerminalCapability.Inbox | TerminalCapability.Post),
                new TerminalSeed(2, Desk, new[] { "worker", "supervisor" }, capabilities: TerminalCapability.Records),
                new TerminalSeed(3, Desk, new[] { "worker", "supervisor" }) },
            new[] { new CredentialSeed(1, new(1), new(101), new(1)), new CredentialSeed(2, Supervisor, new(202), new(2)) },
            messages: report ? new[] { new InitialMessage(new MessageDraft(MessageKind.WorkCompleted, RecipientKind.Authority, default,
                new(SubjectKind.Attempt, 1, AttributeKind.WorkStatus), new(FactValueKind.Text, text: "claimed complete"), 0, taskId: 1, attemptId: 1, operationKey: "false-completion"), new(101), 2) } : null,
            recordAccess: audit ? new[] { new RecordAccessSeed(Supervisor, 1, "supervisor", RecordKind.WorkAttempt), new RecordAccessSeed(Supervisor, 1, "supervisor", RecordKind.WorkReport) } : null);
        return new World("record_queries", seed, new WorldConfig(), Fixtures.Catalog,
            new[] { new Tile(new(1), "Command", Desk, traversalOverride: 1), new Tile(new(2), "Industrial", new(1, 0, 0), traversalOverride: 1) },
            new[] { new ActorSeed(new(1), "Worker", "worker", Desk, new(101)), new ActorSeed(Supervisor, "Supervisor", "supervisor", Desk, new(202)) },
            new[] { new Item(new(1), "card", 1, new(LocationKind.ActorInventory, 1)), new Item(new(2), "card", 1, new(LocationKind.ActorInventory, 2)) }, true, info, work,
            new JobSystemSeed(new[] { new JobSeed(1, 1) }, new[] { new AssignmentSeed("first", 1, new(1), 200) }));
    }
    private static void Query(World w, RecordKind kind = RecordKind.WorkAttempt, long terminal = 1, long tick = 1, long entity = 0, long after = 0, int limit = 16, bool history = false)
    {
        if (tick == 1) w.Submit(0, new(Supervisor, ActionKind.Login, Desk, serviceId: terminal));
        w.Submit(tick, new(Supervisor, ActionKind.QueryRecords, Desk, serviceId: terminal, query: new RecordQuery(kind, entity, after, limit, history)));
    }
    private static KnowledgeFact[] Learned(World w) => w.InspectKnowledge(Supervisor).Where(f => f.SourceKind == EvidenceKind.RecordQuery).ToArray();
    [Fact]
    public void CapturedMachineFilterCannotBeChangedToAnotherMachineOnRestore()
    {
        var w = Make(); w.Submit(0, new(Supervisor, ActionKind.Login, Desk, serviceId: 1));
        w.Submit(1, new(Supervisor, ActionKind.QueryRecords, Desk, serviceId: 1, query: new RecordQuery(RecordKind.WorkAttempt, history: true, machineId: 1))); w.AdvanceTo(2);
        var bytes = w.Serialize(); using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        { writer.Write(true); writer.Write((int)RecordKind.WorkAttempt); writer.Write(0L); writer.Write(0L); writer.Write(16); writer.Write(true); writer.Write(1L); }
        var pattern = buffer.ToArray(); int offset = Assert.Single(Enumerable.Range(0, bytes.Length - pattern.Length + 1), i => bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern));
        BitConverter.GetBytes(2L).CopyTo(bytes, offset + pattern.Length - 8);
        Assert.Throws<ArgumentException>(() => World.Restore(bytes));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MachineFilterPreservesAccessScopeAndCapturedQueryAcrossCheckpoint(bool audit)
    {
        var w = Make(audit: audit); w.Submit(0, new(Supervisor, ActionKind.Login, Desk, serviceId: 1));
        w.Submit(1, new(Supervisor, ActionKind.QueryRecords, Desk, serviceId: 1, query: new RecordQuery(RecordKind.WorkAttempt, history: true, machineId: 1)));
        w.AdvanceTo(2); var copy = World.Restore(w.Serialize()); w.AdvanceTo(4); copy.AdvanceTo(4);
        var receipt = w.ActorView(Supervisor).LastRecordQuery!;
        Assert.Equal(audit ? 1 : 0, receipt.DisclosedRows); Assert.Equal(1, receipt.Query.MachineId);
        Assert.Equal(w.Serialize(), copy.Serialize());
        Assert.Throws<ArgumentException>(() => new RecordQuery(RecordKind.Credential, machineId: 1));
    }
    [Fact]
    public void QueryDisclosesOnlyCapturedAdministrativeVersionAtFiniteCompletion()
    {
        var w = Make(report: true); Query(w); w.AdvanceTo(3);
        Assert.Empty(Learned(w)); Assert.Null(w.ActorView(Supervisor).LastRecordQuery);
        Assert.Equal("WorkCompleted", w.InspectWorkAttempts()[0].AuthorityStatus); Assert.False(w.InspectMachines()[0].Operational);
        w.AdvanceTo(4);
        Assert.Contains(Learned(w), f => f.Key.Attribute == AttributeKind.WorkStatus && f.Value.Text == "Unacknowledged");
        Assert.DoesNotContain(Learned(w), f => f.Value.Text == "WorkCompleted" || f.Key.Attribute == AttributeKind.Operational);
        Assert.Equal(1, w.ActorView(Supervisor).LastRecordQuery!.DisclosedRows);
        Assert.All(Learned(w), f => Assert.Equal(4, f.LearnedTick));
        Query(w, tick: 5); w.AdvanceTo(8);
        Assert.Contains(Learned(w), f => f.Key.Attribute == AttributeKind.WorkStatus && f.Value.Text == "WorkCompleted");
        Assert.False(w.InspectMachines()[0].Operational);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("logout")]
    [InlineData("disconnect")]
    [InlineData("revoke")]
    [InlineData("expiry")]
    public void QueryCancellationOrLostAuthorizationDisclosesNothing(string operation)
    {
        var w = Make(sessionTicks: operation == "expiry" ? 3 : 100); Query(w);
        if (operation == "cancel") w.Submit(2, new(Supervisor, ActionKind.Cancel, Desk));
        if (operation == "logout") { w.Submit(2, new(Supervisor, ActionKind.Logout, Desk)); w.Submit(3, new(Supervisor, ActionKind.Move, new(1, 0, 0))); }
        if (operation == "disconnect") w.ScheduleTerminalChange(2, 1, true, false);
        if (operation == "revoke") w.ScheduleCredentialRevocation(2, 2);
        w.AdvanceTo(6); Assert.Empty(Learned(w)); Assert.Null(w.ActorView(Supervisor).LastRecordQuery);
        Assert.DoesNotContain(w.Trace, e => e.Type == "RecordsQueried");
    }
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ForeignWorkRecordsRequireGrantAtAssignedTerminal(bool audit, long terminal)
    {
        var w = Make(audit: audit); Query(w, terminal: terminal); w.AdvanceTo(4);
        Assert.Empty(Learned(w)); Assert.Equal(0, w.ActorView(Supervisor).LastRecordQuery!.DisclosedRows);
        Assert.Equal(0, w.ActorView(Supervisor).LastRecordQuery!.Version);
    }
    [Fact]
    public void QueriesRequireLocalSessionAndRecordsCapability()
    {
        var noSession = Make(); noSession.Submit(0, new(Supervisor, ActionKind.QueryRecords, Desk, serviceId: 1, query: new(RecordKind.Credential))); noSession.AdvanceTo(4);
        Assert.Equal(FailureReason.SessionRequired, noSession.InspectActor(Supervisor).LastFailure);
        var wrongDevice = Make(); Query(wrongDevice, terminal: 3); wrongDevice.AdvanceTo(4);
        Assert.Equal(FailureReason.RequiredServiceNotPresent, wrongDevice.InspectActor(Supervisor).LastFailure);
        var remote = Make(); remote.Submit(0, new(Supervisor, ActionKind.Move, new(1, 0, 0))); remote.Submit(2, new(Supervisor, ActionKind.QueryRecords, new(1, 0, 0), serviceId: 1, query: new(RecordKind.Credential))); remote.AdvanceTo(5);
        Assert.Equal(FailureReason.NotAtRequiredTile, remote.InspectActor(Supervisor).LastFailure);
        Assert.All(new[] { noSession, wrongDevice, remote }, w => Assert.Empty(Learned(w)));
    }
    [Fact]
    public void CredentialAndSessionQueriesAreOwnAccountOnlyEvenForAuditor()
    {
        var w = Make(); Query(w, RecordKind.Credential); w.AdvanceTo(4);
        Assert.Contains(Learned(w), f => f.Key.Attribute == AttributeKind.CredentialCard && f.Value.Number == 2);
        Assert.DoesNotContain(Learned(w), f => f.Key.Attribute == AttributeKind.AccountId && f.Value.Number == 101);
        Query(w, RecordKind.Credential, tick: 5, entity: 1); w.AdvanceTo(8); Assert.Equal(0, w.ActorView(Supervisor).LastRecordQuery!.DisclosedRows);
        Query(w, RecordKind.TerminalSession, tick: 9); w.AdvanceTo(12);
        Assert.Equal(1, w.ActorView(Supervisor).LastRecordQuery!.DisclosedRows);
        Assert.DoesNotContain(Learned(w), f => f.Key.Attribute == AttributeKind.AccountId && f.Value.Number == 101);
    }
    [Fact]
    public void AuditReadsReportProvenanceAndHistoryWithoutInferringPhysicalTruth()
    {
        var w = Make(report: true); Query(w, tick: 1, history: true); w.AdvanceTo(4);
        Query(w, tick: 5, history: true); w.AdvanceTo(8); Assert.Equal(2, w.ActorView(Supervisor).LastRecordQuery!.DisclosedRows);
        Query(w, RecordKind.WorkReport, tick: 9); w.AdvanceTo(12);
        var facts = Learned(w); Assert.Contains(facts, f => f.Key.Attribute == AttributeKind.ReportClaim && f.Value.Text == "claimed complete");
        Assert.Contains(facts, f => f.Key.Attribute == AttributeKind.AccountId && f.Value.Number == 101);
        Assert.Contains(facts, f => f.Key.Attribute == AttributeKind.ReceivedTick && f.Value.Number == 2);
        Assert.DoesNotContain(facts, f => f.Key.Attribute == AttributeKind.Operational || f.Value.Text == "CompletedPhysically");
        Assert.False(w.InspectMachines()[0].Operational);
    }
    [Fact]
    public void PagesExposeUsableCursorAndBoundedKnowledgeDoesNotBecomeRecordCache()
    {
        var w = Make(capacity: 24);
        for (int i = 1; i <= 5; i++) w.ScheduleWorkAssignment(i, new AssignmentSeed("retry/" + i, 1, new(1), 200));
        w.Submit(0, new(Supervisor, ActionKind.Login, Desk, serviceId: 1)); Query(w, tick: 6, limit: 2); w.AdvanceTo(9);
        var first = w.ActorView(Supervisor).LastRecordQuery!; Assert.True(first.HasMore); Assert.Equal(2, first.NextCursor); Assert.Equal(2, first.DisclosedRows);
        Query(w, tick: 10, limit: 2, after: first.NextCursor); w.AdvanceTo(13); var second = w.ActorView(Supervisor).LastRecordQuery!; Assert.Equal(4, second.NextCursor);
        Query(w, tick: 14, limit: 2, after: second.NextCursor); w.AdvanceTo(17); Assert.False(w.ActorView(Supervisor).LastRecordQuery!.HasMore);
        Assert.True(w.InspectKnowledge(Supervisor).Count <= 24); Assert.True(w.KnowledgeEvictions > 0);
    }
    [Fact]
    public void CapturedQueriesReceiptsAndHistoryContinueAcrossGeneratedCheckpoints()
    {
        for (ulong seed = 0; seed < 20; seed++)
        foreach (long checkpoint in new long[] { -1, 0, 1, 2, 3, 4, 9 })
        {
            var w = Make(report: true, seed: seed, queryTicks: 1 + (long)(seed % 4)); Query(w); Query(w, tick: 6, history: true);
            if (checkpoint >= 0) w.AdvanceTo(checkpoint); var data = w.Serialize(); var copy = World.Restore(data, true); Assert.Equal(data, copy.Serialize());
            w.AdvanceTo(12); copy.AdvanceTo(12); Assert.Equal(w.Digest(), copy.Digest());
            Assert.Equal(JsonData.Write(w.InspectRecordHistory()), JsonData.Write(copy.InspectRecordHistory()));
        }
    }
    [Fact]
    public void DuplicateInstitutionalInputsDoNotDuplicateRecordHistory()
    {
        var w = Make(); w.ScheduleWorkCancellation(2, 1, "cancel"); w.ScheduleWorkCancellation(2, 1, "cancel"); w.ScheduleCredentialRevocation(3, 1); w.ScheduleCredentialRevocation(4, 1); w.AdvanceTo(5);
        Assert.Equal(2, w.InspectRecordHistory().Count(r => r.Kind == RecordKind.WorkAttempt));
        Assert.Equal(2, w.InspectRecordHistory().Count(r => r.Kind == RecordKind.Credential && r.EntityId == 1));
        var restored = World.Restore(w.Serialize()); restored.ScheduleWorkCancellation(6, 1, "cancel"); restored.ScheduleCredentialRevocation(6, 1); restored.AdvanceTo(7);
        Assert.Equal(w.InspectRecordHistory().Count, restored.InspectRecordHistory().Count);
    }
    [Fact]
    public void AuditScenarioUsesSharedExecutorAndStrictQueryShape()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "record_audit.json"));
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status);
        Assert.Equal(4, run.World.Trace.Count(e => e.Type == "RecordsQueried"));
        Assert.All(run.World.Trace.Where(e => e.Type == "RecordsQueried"), e => { Assert.Equal(2, e.ActorId); Assert.Equal(202, e.AccountId); Assert.Equal(2, e.TerminalId); Assert.True(e.SessionId > 0); });
        var missing = JsonData.Read<ScenarioData>(loaded.ScenarioJson, "audit"); missing.ScriptedActors[0].Commands[1].Query = null;
        Assert.Throws<ScenarioException>(() => new LoadedScenario(JsonData.Write(missing), loaded.CatalogJson, "missing query"));
        var excessive = JsonData.Read<ScenarioData>(loaded.ScenarioJson, "audit"); excessive.ScriptedActors[0].Commands[1].Query!.Limit = 33;
        Assert.Throws<ScenarioException>(() => new LoadedScenario(JsonData.Write(excessive), loaded.CatalogJson, "query limit"));
        var invalidGrant = JsonData.Read<ScenarioData>(loaded.ScenarioJson, "audit"); invalidGrant.InitialState.Information!.RecordAccess[0].RequiredRole = "worker";
        Assert.Throws<ScenarioException>(() => new LoadedScenario(JsonData.Write(invalidGrant), loaded.CatalogJson, "bad grant"));
    }
    [Fact]
    public void RecordPlanUsesKnownTerminalAndExecutesLoginBeforeFiniteQuery()
    {
        var w = Make(); var query = new RecordQuery(RecordKind.WorkAttempt, 1);
        var plan = ReportPlanning.PlanRecords(w.ActorView(Supervisor), query, 1, 1, 3);
        Assert.Equal(PlanOutcome.PlanFound, plan.Outcome); Assert.Equal(4, plan.Cost);
        Assert.Equal(new[] { ActionKind.Login, ActionKind.QueryRecords }, plan.Steps.Select(s => s.Intent.Kind));
        Assert.Equal(PlanOutcome.NoPlan, ReportPlanning.PlanRecords(w.ActorView(Supervisor), query, 3, 1, 3).Outcome);
        w.Submit(0, plan.Steps[0].Intent); w.AdvanceTo(1);
        var next = ReportPlanning.PlanRecords(w.ActorView(Supervisor), query, 1, 1, 3); Assert.Single(next.Steps);
        w.Submit(2, next.Steps[0].Intent); w.AdvanceTo(4); Assert.Empty(Learned(w));
        w.AdvanceTo(5); Assert.Equal(1, w.ActorView(Supervisor).LastRecordQuery!.DisclosedRows);
    }
}
