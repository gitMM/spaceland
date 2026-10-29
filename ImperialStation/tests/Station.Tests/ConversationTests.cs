using Station.Contracts;
using Station.Core;
using Station.Mind;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class ConversationTests
{
    private const long H = NeedAccumulator.HourMilliseconds;
    private static World Make(int population = 2, bool autonomous = false, IEnumerable<long>? consent = null, IEnumerable<FactSeed>? facts = null, bool remotePartner = false, int knowledge = 256, bool critical = false, bool satisfiedPartner = false)
    {
        var actors = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "Citizen " + i, "worker", remotePartner && i == 2 ? Fixtures.B : Fixtures.A)).ToArray();
        return new World("conversations", 42, new WorldConfig(itemActionTicks: 1), Fixtures.Catalog,
            new[] { new Tile(new(1), "Habitat", Fixtures.A, traversalOverride: 4), new Tile(new(2), "Habitat", Fixtures.B, traversalOverride: 4) }, actors, Array.Empty<Item>(), true,
            information: new InformationSeed(TerminalFixtures.Tiny(knowledge: knowledge), facts: facts),
            routines: autonomous ? new RoutineSeed(new RoutineConfig(dayTicks: 240, dutyTicks: 80, checkInWindow: 1, periodicCheckInTicks: 30), actors.Select(a => new RosterAssignmentSeed(a.Id, 2, Fixtures.A))) : null,
            needs: new NeedSeed(initial: actors.Select(a => new NeedInitialValue(a.Id, NeedKind.Social, satisfiedPartner && a.Id.Value == 2 ? 0 : 400 * H)).Concat(critical ? new[] { new NeedInitialValue(new(2), NeedKind.Hunger, 900 * H - 3 * 6000) } : Array.Empty<NeedInitialValue>()),
                autonomousActors: autonomous ? null : Array.Empty<long>(), conversations: new ConversationConfig(durationTicks: 5, explicitConsentActors: consent)));
    }
    private static void Talk(World w, long first = 1, long second = 2, long tick = 0, ConversationTopic topic = ConversationTopic.Routine) => w.Submit(tick, new(new(first), ActionKind.Converse, Fixtures.A, targetActor: new(second), conversationTopic: topic));
    private static void RoundTrip(World w) => Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    [Fact]
    public void IdleOffDutyPartnerCanConsentWithoutAlsoHavingAnUnmetSocialNeed()
    {
        var w = Make(autonomous: true, satisfiedPartner: true); w.AdvanceTo(5);
        Assert.Equal(ConversationStatus.Completed, Assert.Single(w.InspectConversations()).Status);
        Assert.Equal(1, w.Trace.Count(e => e.Type == "ConversationConsented")); RoundTrip(w);
    }
    [Fact]
    public void BothParticipantsAreClaimedUntilFiniteCompletionAndBothReceiveRealRelief()
    {
        var w = Make(consent: new long[] { 2 }); Talk(w); w.AdvanceTo(4);
        Assert.All(w.InspectActors(), a => Assert.Equal(ActionKind.Converse, a.Action)); Assert.Equal(ConversationStatus.Active, Assert.Single(w.InspectConversations()).Status);
        Assert.All(w.InspectNeeds(), n => Assert.Equal(400 * H + 8000, n.Values.Single(v => v.Kind == NeedKind.Social).Q)); RoundTrip(w);
        w.AdvanceTo(5); Assert.Equal(2, w.ActionsCompleted); Assert.All(w.InspectActors(), a => Assert.Null(a.Action));
        Assert.All(w.InspectNeeds(), n => Assert.Equal(100 * H + 10000, n.Values.Single(v => v.Kind == NeedKind.Social).Q)); Assert.Equal(ConversationStatus.Completed, Assert.Single(w.InspectConversations()).Status); RoundTrip(w);
    }
    [Fact]
    public void NoConsentOrRemotePartnerCannotBeForcedIntoConversation()
    {
        var unwilling = Make(); Talk(unwilling); unwilling.AdvanceTo(5); Assert.Empty(unwilling.InspectConversations()); Assert.Equal(FailureReason.ConsentDeclined, unwilling.InspectActor(new(1)).LastFailure); RoundTrip(unwilling);
        var remote = Make(consent: new long[] { 2 }, remotePartner: true); Talk(remote); remote.AdvanceTo(5); Assert.Empty(remote.InspectConversations()); Assert.Equal(FailureReason.NotAtRequiredTile, remote.InspectActor(new(1)).LastFailure); RoundTrip(remote);
    }
    [Fact]
    public void CompetingInvitationsAndMovementCannotDoubleBookAConsentingPartner()
    {
        var w = Make(population: 3, consent: new long[] { 2 }); Talk(w); Talk(w, 3, 2); w.Submit(1, new(new(2), ActionKind.Move, Fixtures.B)); w.AdvanceTo(2);
        Assert.Single(w.InspectConversations()); Assert.Equal(FailureReason.ActorBusy, w.InspectActor(new(2)).LastFailure); Assert.Equal(Fixtures.A, w.InspectActor(new(2)).Position); RoundTrip(w);
        w.AdvanceTo(5); Assert.Equal(2, w.ActionsCompleted); Assert.Equal(2, w.ActionsFailed); RoundTrip(w);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EitherParticipantCanCancelBothClaimsWithoutCompletionBenefit(long actor)
    {
        var w = Make(consent: new long[] { 2 }); Talk(w); w.Submit(3, new(new(actor), ActionKind.Cancel, Fixtures.A)); w.AdvanceTo(5);
        Assert.Equal(ConversationStatus.Cancelled, Assert.Single(w.InspectConversations()).Status); Assert.All(w.InspectActors(), a => Assert.Null(a.Action)); Assert.Equal(0, w.ActionsCompleted);
        Assert.All(w.InspectNeeds(), n => Assert.Equal(400 * H + 10000, n.Values.Single(v => v.Kind == NeedKind.Social).Q)); RoundTrip(w);
    }
    [Fact]
    public void CriticalNeedOfEitherParticipantCancelsTheWholeActivity()
    {
        var w = Make(consent: new long[] { 2 }, critical: true); Talk(w); w.AdvanceTo(5);
        Assert.Equal(ConversationStatus.Cancelled, Assert.Single(w.InspectConversations()).Status); Assert.Equal(1, w.CriticalNeedInterruptions); Assert.Equal(0, w.ActionsCompleted); RoundTrip(w);
    }
    [Fact]
    public void OnlyPermittedKnownFactsReachParticipantsWithSpeakerAndConversationProvenance()
    {
        var key = new FactKey(SubjectKind.Job, 77, AttributeKind.WorkStatus);
        var w = Make(population: 3, consent: new long[] { 2 }, facts: new[] { new FactSeed(new(1), key, new(FactValueKind.Text, text: "Blocked")), new FactSeed(new(1), new(SubjectKind.Credential, 8, AttributeKind.CredentialCard), FactValue.Integer(88)) });
        Talk(w, topic: ConversationTopic.WorkStatus); w.AdvanceTo(4); Assert.DoesNotContain(w.InspectKnowledge(new(2)), f => f.Key.Equals(key)); RoundTrip(w);
        w.AdvanceTo(5); var c = Assert.Single(w.InspectConversations()); var heard = Assert.Single(w.InspectKnowledge(new(2)), f => f.Key.Equals(key));
        Assert.Equal(EvidenceKind.Heard, heard.SourceKind); Assert.Equal(1, heard.SourceActorId); Assert.Equal(c.Id, heard.SourceId); Assert.Equal(-1, heard.ObservedTick); Assert.Equal(5, heard.LearnedTick);
        Assert.DoesNotContain(w.InspectKnowledge(new(2)), f => f.Key.SubjectKind == SubjectKind.Credential); Assert.DoesNotContain(w.InspectKnowledge(new(3)), f => f.Key.Equals(key));
        Assert.Empty(w.InspectBeliefs()); Assert.Empty(w.InspectMessages()); Assert.Empty(w.InspectWorkAttempts()); RoundTrip(w);
    }
    [Fact]
    public void PinnedRecipientEvidenceIsNotReplacedByHearsay()
    {
        var key = new FactKey(SubjectKind.Job, 77, AttributeKind.WorkStatus);
        var w = Make(consent: new long[] { 2 }, facts: new[] { new FactSeed(new(1), key, FactValue.Integer(1)), new FactSeed(new(2), key, FactValue.Integer(2), pinned: true) }); Talk(w, topic: ConversationTopic.WorkStatus); w.AdvanceTo(5);
        var fact = Assert.Single(w.InspectKnowledge(new(2)), f => f.Key.Equals(key)); Assert.True(fact.Pinned); Assert.Equal(2, fact.Value.Number); Assert.Equal(EvidenceKind.Seeded, fact.SourceKind); RoundTrip(w);
    }
    [Fact]
    public void TypedSharingPolicyExcludesFormalIdentityAndRequiresDirectWitnessEvidence()
    {
        KnowledgeFact Fact(SubjectKind subject, AttributeKind attribute, FactValue value, EvidenceKind source = EvidenceKind.Direct, long id = 1) => new(1, new(subject, id, attribute), value, source, 1, 0, 0);
        Assert.True(ConversationPolicy.MayShare(1, ConversationTopic.Routine, Fact(SubjectKind.Actor, AttributeKind.Cohort, FactValue.Integer(2))));
        Assert.False(ConversationPolicy.MayShare(1, ConversationTopic.Routine, Fact(SubjectKind.Actor, AttributeKind.Cohort, FactValue.Integer(2), id: 2)));
        Assert.True(ConversationPolicy.MayShare(1, ConversationTopic.WitnessedProblem, Fact(SubjectKind.Machine, AttributeKind.Operational, FactValue.Boolean(false))));
        Assert.False(ConversationPolicy.MayShare(1, ConversationTopic.WitnessedProblem, Fact(SubjectKind.Machine, AttributeKind.Operational, FactValue.Boolean(false), EvidenceKind.Heard)));
        Assert.True(ConversationPolicy.MayShare(1, ConversationTopic.AllowedRequest, Fact(SubjectKind.Machine, AttributeKind.RequiredResource, new(FactValueKind.Text, text: "cartridge"))));
        foreach (var topic in Enum.GetValues<ConversationTopic>()) Assert.False(ConversationPolicy.MayShare(1, topic, Fact(SubjectKind.Record, AttributeKind.CredentialStatus, FactValue.Boolean(true))));
    }
    [Fact]
    public void AutonomousOffDutyCitizensConverseWithoutCommandsAndStayWithinLocalCandidateBound()
    {
        var w = Make(autonomous: true); w.AdvanceTo(5); Assert.Equal(ConversationStatus.Completed, Assert.Single(w.InspectConversations()).Status);
        Assert.All(w.InspectNeedAgents(), a => Assert.Equal(1, a.CompletedActivities)); Assert.InRange(w.ConversationPartnerCandidatesExamined, 1, 8); Assert.Equal(0, w.ActionsFailed); RoundTrip(w);
        var crowd = Make(population: 20, autonomous: true); crowd.AdvanceTo(0); Assert.True(crowd.ConversationPartnerCandidatesExamined <= 4 * crowd.PlannerRequests); RoundTrip(crowd);
    }
    [Fact]
    public void LocalIndexTracksMovementAndDoesNotDiscoverRemotePartners()
    {
        var w = Make(autonomous: true, remotePartner: true); w.AdvanceTo(0); Assert.Empty(w.InspectConversations()); Assert.Equal(0, w.ConversationPartnerCandidatesExamined); RoundTrip(w);
        w.Submit(1, new(new(2), ActionKind.Move, Fixtures.A)); w.AdvanceTo(4); Assert.Empty(w.InspectConversations()); RoundTrip(w);
        w.AdvanceTo(60); Assert.Contains(w.InspectConversations(), c => c.Status == ConversationStatus.Completed); RoundTrip(w);
    }
    [Fact]
    public void EveryMidConversationAndBoundaryCheckpointContinuesExactly()
    {
        World Variant() { var w = Make(autonomous: true); w.ScheduleRosterChange(3, new(new(1), 0, Fixtures.B), "new-duty"); return w; }
        var baseline = Variant(); baseline.AdvanceTo(250);
        foreach (long tick in new long[] { -1, 0, 1, 3, 4, 5, 6, 70, 80, 150, 160, 230 })
        {
            var w = Variant(); if (tick >= 0) w.AdvanceTo(tick); var copy = World.Restore(w.Serialize(), true); Assert.Equal(w.Serialize(), copy.Serialize()); copy.AdvanceTo(250);
            Assert.Equal(baseline.Serialize(), copy.Serialize()); Assert.Equal(baseline.Trace.Where(e => e.Tick > tick).Select(Runs.EventJson), copy.Trace.Select(Runs.EventJson));
        }
    }
    [Fact]
    public void BoundedKnowledgeAndDisclosureDoNotGrantPrivateRecordsOrOverflowCapacity()
    {
        var seeds = Enumerable.Range(1, 10).Select(i => new FactSeed(new(1), new(SubjectKind.Job, i, AttributeKind.WorkStatus), FactValue.Integer(i), pinned: true));
        var w = Make(consent: new long[] { 2 }, facts: seeds); Talk(w, topic: ConversationTopic.WorkStatus); w.AdvanceTo(5);
        Assert.Equal(4, Assert.Single(w.InspectConversations()).Exchanges.Count); Assert.Equal(4, w.InspectKnowledge(new(2)).Count(f => f.SourceKind == EvidenceKind.Heard)); RoundTrip(w);
        var full = Make(consent: new long[] { 2 }, knowledge: 1, facts: new[] { new FactSeed(new(1), new(SubjectKind.Job, 1, AttributeKind.WorkStatus), FactValue.Integer(1), pinned: true), new FactSeed(new(2), new(SubjectKind.Job, 2, AttributeKind.WorkStatus), FactValue.Integer(2), pinned: true) });
        Talk(full, topic: ConversationTopic.WorkStatus); full.AdvanceTo(5); Assert.Equal(ConversationStatus.Completed, Assert.Single(full.InspectConversations()).Status);
        Assert.All(full.InspectConversations()[0].Exchanges, e => Assert.False(e.Learned)); Assert.All(full.InspectActors(), a => Assert.Single(full.InspectKnowledge(a.Id))); RoundTrip(full);
    }
    [Fact]
    public void SnapshotRejectsForgedConversationClaimsTimersAndHeardSpeaker()
    {
        var w = Make(consent: new long[] { 2 }); Talk(w); w.AdvanceTo(2); var c = Assert.Single(w.InspectConversations()); var bytes = w.Serialize();
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(c.Id); writer.Write(c.FirstActorId); writer.Write(c.SecondActorId); writer.Write(c.SecondActionId); writer.Write(c.Position.X); writer.Write(c.Position.Y); writer.Write(c.Position.Z); writer.Write((int)c.Topic);
            writer.Write(c.StartedTick); writer.Write(c.CompletesTick); writer.Write(c.EndedTick); writer.Write((int)c.Status); writer.Write(0);
        }
        var block = stream.ToArray(); int start = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        foreach (var mutation in new[] { (16, 1L), (24, 999L), (48, -1L), (56, 6L), (64, 0L) })
        { var bad = bytes.ToArray(); BitConverter.GetBytes(mutation.Item2).CopyTo(bad, start + mutation.Item1); Assert.Throws<ArgumentException>(() => World.Restore(bad)); }
        var known = Make(population: 3, consent: new long[] { 2 }, facts: new[] { new FactSeed(new(1), new(SubjectKind.Job, 77, AttributeKind.WorkStatus), FactValue.Integer(42)) });
        Talk(known, topic: ConversationTopic.WorkStatus); known.AdvanceTo(5); var f = Assert.Single(known.InspectKnowledge(new(2)), f => f.SourceKind == EvidenceKind.Heard); bytes = known.Serialize();
        using var factStream = new MemoryStream(); using (var writer = new BinaryWriter(factStream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(f.Id); writer.Write((int)f.Key.SubjectKind); writer.Write(f.Key.SubjectId); writer.Write((int)f.Key.Attribute); writer.Write((int)f.Value.Kind); writer.Write(f.Value.Number);
            writer.Write(f.Value.Coordinate.X); writer.Write(f.Value.Coordinate.Y); writer.Write(f.Value.Coordinate.Z); writer.Write(f.Value.Text ?? ""); writer.Write((int)f.SourceKind); writer.Write(f.SourceId); writer.Write(f.ObservedTick); writer.Write(f.LearnedTick); writer.Write(f.Pinned); writer.Write(f.SourceActorId);
        }
        block = factStream.ToArray(); start = Assert.Single(Enumerable.Range(0, bytes.Length - block.Length + 1), i => bytes.AsSpan(i, block.Length).SequenceEqual(block));
        BitConverter.GetBytes(3L).CopyTo(bytes, start + block.Length - 8); Assert.Throws<ArgumentException>(() => World.Restore(bytes));
    }
    [Fact]
    public void AutonomousConversationGoldenScenarioNeedsNoScriptOrCards()
    {
        var loaded = LoadedScenario.Load(Path.Combine(Fixtures.Root, "scenarios", "conversation.json")); Assert.Empty(loaded.Scenario.ScriptedActors);
        var run = Runs.Execute(loaded, 42, true); Assert.Equal("completed", run.Result.Status); Assert.Empty(run.Result.AssertionFailures); Assert.Equal(11, loaded.Scenario.Assertions.Count);
        Assert.Equal(2, run.World.ActionsCompleted); Assert.Equal(0, run.World.ActionsFailed); Assert.Empty(run.World.InspectMessages()); Assert.Empty(run.World.InspectBeliefs()); RoundTrip(run.World);
    }
}
