using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class InformationTests
{
    [Fact]
    public void SameTileMoveDoesNotLogOutAndQueuedCancellationReleasesCapacity()
    {
        var w=TerminalFixtures.Station(allAtA:true);w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1));
        w.Submit(1,new(new(1),ActionKind.Move,Fixtures.A));w.Submit(1,new(new(2),ActionKind.Login,Fixtures.A,serviceId:1));
        w.Submit(2,new(new(2),ActionKind.Cancel,Fixtures.A));w.AdvanceTo(2);
        Assert.Equal(1,Assert.Single(w.InspectSessions()).ActorId);Assert.Empty(w.InspectTerminals().Single(t=>t.Id==1).Queue);
        w.Submit(3,new(new(1),ActionKind.Logout,Fixtures.A));w.Submit(4,new(new(2),ActionKind.Login,Fixtures.A,serviceId:1));w.AdvanceTo(5);
        Assert.Equal(2,Assert.Single(w.InspectSessions()).ActorId);
    }
    [Theory]
    [InlineData(MessageKind.Order)]
    [InlineData(MessageKind.OrderCancelled)]
    [InlineData(MessageKind.ServiceReply)]
    public void OrdinaryAccountCannotForgeAuthorityOrServiceMessages(MessageKind kind)
    {
        var w=TerminalFixtures.Station();w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1));
        w.Submit(1,new(new(1),ActionKind.PostMessage,Fixtures.A,serviceId:1,message:new MessageDraft(kind,RecipientKind.Account,new(2),new FactKey(SubjectKind.Order,1,AttributeKind.OrderStatus),FactValue.Integer(1),0)));
        w.AdvanceTo(5);Assert.Empty(w.InspectMessages());Assert.Equal(FailureReason.InvalidMessage,w.InspectActor(new(1)).LastFailure);
    }
    [Fact]
    public void T07_FalseReportChangesBeliefWithoutMovingItemAndCorrectionRequiresTerminal()
    {
        var w = TerminalFixtures.FalseDelivery(); Assert.Empty(w.Trace); Assert.Equal(-1,w.LastProcessedTick);
        w.AdvanceTo(1);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)),f => f.Key.SubjectKind == SubjectKind.Order);
        Assert.DoesNotContain(w.InspectKnowledge(new(2)),f => f.Key.SubjectKind == SubjectKind.Order);
        w.AdvanceTo(2); Assert.Contains(w.InspectKnowledge(new(1)),f => f.SourceKind == EvidenceKind.MessageRead);
        Assert.Contains(w.InspectKnowledge(new(2)),f => f.SourceKind == EvidenceKind.MessageRead); Assert.Empty(w.InspectBeliefs());
        Assert.DoesNotContain(w.InspectSessions(),s => s.ActorId == 2);
        w.AdvanceTo(5); Assert.Equal(FactValue.At(Fixtures.B),Assert.Single(w.InspectBeliefs()).Value);
        w.AdvanceTo(7); Assert.DoesNotContain(w.InspectKnowledge(new(2)),f => f.Key.Equals(TerminalFixtures.ItemKey));
        w.AdvanceTo(8); var fact = Assert.Single(w.InspectKnowledge(new(2)),f => f.Key.Equals(TerminalFixtures.ItemKey));
        Assert.Equal(EvidenceKind.Inspection,fact.SourceKind); Assert.Equal(FactValue.Absent(Fixtures.B),fact.Value); Assert.Equal(8,fact.LearnedTick);
        Assert.DoesNotContain(w.InspectKnowledge(new(2)),f => f.Key.Equals(TerminalFixtures.ItemKey) && f.Value.Equals(FactValue.At(Fixtures.A)));
        w.AdvanceTo(11); Assert.Equal(FactValue.At(Fixtures.B),Assert.Single(w.InspectBeliefs()).Value);
        w.AdvanceTo(12); var belief = Assert.Single(w.InspectBeliefs()); Assert.Equal(FactValue.Absent(Fixtures.B),belief.Value); Assert.True(belief.ContradictsPrevious);
        Assert.Equal(2,w.InspectBeliefHistory().Count); var item = Assert.Single(w.InspectItems(),i => i.Id.Value == 1);
        Assert.Equal(LocationKind.TileContainer,item.Location.Kind); Assert.Equal(1,item.Location.Owner); w.AssertAccounting();
        var reported = w.InspectMessages().Single(m => m.Draft.Kind == MessageKind.WorkCompleted);
        Assert.Equal(1,reported.PhysicalSender.Value); Assert.Equal(1,reported.SenderAccount.Value); Assert.Equal(1,reported.TerminalId); Assert.Equal(3,reported.SubmittedTick); Assert.Equal(5,reported.DeliveryTick);
    }
    [Fact]
    public void T21_ReadCapturesOnlyAlreadyDeliveredMessagesAndGettersArePure()
    {
        var w = TerminalFixtures.Station(TerminalFixtures.Tiny(read:3),new[] {new InitialMessage(TerminalFixtures.Draft(1,1),new(2),0),new InitialMessage(TerminalFixtures.Draft(1,2),new(2),2)});
        w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1)); w.Submit(1,new(new(1),ActionKind.ReadInbox,Fixtures.A,serviceId:1));
        w.AdvanceTo(2); byte[] before = w.Serialize();
        for(int i=0;i<10;i++){w.InspectMailbox(new(1));w.InspectKnowledge(new(1));w.InspectSessions();w.InspectTerminals();w.ActorView(new(1));}
        Assert.Equal(before,w.Serialize()); var resumedRead=World.Restore(before); Assert.DoesNotContain(w.InspectKnowledge(new(1)),f => f.Key.SubjectKind == SubjectKind.Job);
        w.AdvanceTo(4); var mailbox=w.InspectMailbox(new(1)); Assert.Equal(MessageStage.Read,mailbox[0].Stage); Assert.Equal(MessageStage.DeliveredToMailbox,mailbox[1].Stage);
        resumedRead.AdvanceTo(4);Assert.Equal(w.Digest(),resumedRead.Digest());
        Assert.Null(mailbox[1].FirstReadTick); Assert.DoesNotContain(w.InspectKnowledge(new(1)),f => f.Key.SubjectId==2 && f.Key.SubjectKind==SubjectKind.Job);
        w.Submit(5,new(new(1),ActionKind.ReadInbox,Fixtures.A,serviceId:1)); w.AdvanceTo(8); Assert.All(w.InspectMailbox(new(1)),m=>Assert.Equal(MessageStage.Read,m.Stage));
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("logout")]
    [InlineData("terminal")]
    [InlineData("revocation")]
    public void T21_InterruptedReadNeverDisclosesOrMarksRead(string interruption)
    {
        var w=TerminalFixtures.Station(TerminalFixtures.Tiny(read:4),new[]{new InitialMessage(TerminalFixtures.Draft(1),new(2))});
        w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1));w.Submit(1,new(new(1),ActionKind.ReadInbox,Fixtures.A,serviceId:1));
        if(interruption=="cancel")w.Submit(2,new(new(1),ActionKind.Cancel,Fixtures.A));
        if(interruption=="logout")w.Submit(2,new(new(1),ActionKind.Logout,Fixtures.A));
        if(interruption=="terminal")w.ScheduleTerminalChange(2,1,false,false);
        if(interruption=="revocation")w.ScheduleCredentialRevocation(2,1);
        w.AdvanceTo(6);Assert.Equal(MessageStage.DeliveredToMailbox,Assert.Single(w.InspectMailbox(new(1))).Stage);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)),f=>f.Key.SubjectKind==SubjectKind.Job);
        Assert.DoesNotContain(w.Trace,e=>e.Type=="MessageRead");
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("logout")]
    [InlineData("terminal")]
    [InlineData("revocation")]
    public void T21_InterruptedPostTransmitsNothing(string interruption)
    {
        var w=TerminalFixtures.Station(TerminalFixtures.Tiny(post:4));
        w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1));w.Submit(1,new(new(1),ActionKind.PostMessage,Fixtures.A,serviceId:1,message:TerminalFixtures.Draft(2)));
        if(interruption=="cancel")w.Submit(2,new(new(1),ActionKind.Cancel,Fixtures.A));
        if(interruption=="logout")w.Submit(2,new(new(1),ActionKind.Logout,Fixtures.A));
        if(interruption=="terminal")w.ScheduleTerminalChange(2,1,false,false);
        if(interruption=="revocation")w.ScheduleCredentialRevocation(2,1);
        w.AdvanceTo(8);Assert.Empty(w.InspectMessages());Assert.Empty(w.InspectMailbox(new(2)));
    }
    [Fact]
    public void T21_LoginRequiresLocationAndHeldHolderBoundCredential()
    {
        var w=TerminalFixtures.Station();
        w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:3));w.AdvanceTo(0);Assert.Equal(FailureReason.NotAtRequiredTile,w.InspectActor(new(1)).LastFailure);
        w.Submit(1,new(new(1),ActionKind.PutDown,Fixtures.A,new(101)));w.Submit(2,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1));w.AdvanceTo(2);
        Assert.Equal(FailureReason.MissingCredential,w.InspectActor(new(1)).LastFailure);Assert.Empty(w.InspectSessions());
        w.Submit(3,new(new(2),ActionKind.Move,Fixtures.A));w.Submit(13,new(new(2),ActionKind.PickUp,Fixtures.A,new(101)));
        w.Submit(15,new(new(2),ActionKind.PutDown,Fixtures.A,new(102)));w.Submit(16,new(new(2),ActionKind.Login,Fixtures.A,serviceId:1));w.AdvanceTo(17);
        Assert.Equal(FailureReason.MissingCredential,w.InspectActor(new(2)).LastFailure);Assert.Empty(w.InspectSessions());
    }
    [Fact]
    public void T21_PrivateMailboxAndLateReplySurviveDepartureWithoutAwareness()
    {
        var w=TerminalFixtures.Station(messages:new[]{new InitialMessage(TerminalFixtures.Draft(1,1),new(0),7),new InitialMessage(TerminalFixtures.Draft(2,2),new(0),0)},allAtA:true);
        w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1));w.Submit(1,new(new(1),ActionKind.ReadInbox,Fixtures.A,serviceId:1));w.Submit(2,new(new(1),ActionKind.Move,Fixtures.B));
        w.Submit(2,new(new(2),ActionKind.Login,Fixtures.A,serviceId:1));w.Submit(3,new(new(2),ActionKind.ReadInbox,Fixtures.A,serviceId:1));
        w.AdvanceTo(8);Assert.DoesNotContain(w.InspectKnowledge(new(1)),f=>f.Key.SubjectKind==SubjectKind.Job);
        Assert.Contains(w.InspectKnowledge(new(2)),f=>f.Key.SubjectKind==SubjectKind.Job&&f.Key.SubjectId==2);
        Assert.Equal(MessageStage.DeliveredToMailbox,Assert.Single(w.InspectMailbox(new(1))).Stage);Assert.DoesNotContain(w.InspectSessions(),s=>s.ActorId==1);
        w.Submit(9,new(new(1),ActionKind.ReadInbox,Fixtures.B,serviceId:1));w.AdvanceTo(10);Assert.Equal(FailureReason.NotAtRequiredTile,w.InspectActor(new(1)).LastFailure);
    }
    [Fact]
    public void T21_QueueUsesRealCapacityAndDrainsWithBoundedSessions()
    {
        var w=TerminalFixtures.Station(TerminalFixtures.Tiny(session:2),population:3,allAtA:true);
        for(int a=1;a<=3;a++)w.Submit(0,new(new(a),ActionKind.Login,Fixtures.A,serviceId:1));
        w.AdvanceTo(0);var terminal=w.InspectTerminals().Single(t=>t.Id==1);Assert.Single(terminal.Claims);Assert.Equal(2,terminal.Queue.Count);
        var resumedQueue=World.Restore(w.Serialize());
        w.AdvanceTo(10);Assert.Empty(w.InspectSessions());Assert.Empty(w.InspectTerminals().Single(t=>t.Id==1).Queue);Assert.Equal(3,w.Trace.Count(e=>e.Type=="TerminalLoginSucceeded"));
        Assert.Equal(2,w.MaximumTerminalQueue);Assert.Equal(9,w.TotalTerminalWaitTicks);
        resumedQueue.AdvanceTo(10);Assert.Equal(w.Digest(),resumedQueue.Digest());
    }
    [Fact]
    public void SameTickTerminalClaimPermutationCanSelectEveryActor()
    {
        var winners=new SortedSet<long>();
        for(ulong seed=0;seed<64;seed++)
        {
            var w=TerminalFixtures.Station(population:3,allAtA:true,seed:seed);
            for(int a=1;a<=3;a++)w.Submit(0,new(new(a),ActionKind.Login,Fixtures.A,serviceId:1));
            w.AdvanceTo(0);var t=w.InspectTerminals().Single(t=>t.Id==1);winners.Add(Assert.Single(t.Claims));Assert.Equal(new long[]{1,2,3},t.Claims.Concat(t.Queue).OrderBy(x=>x));
        }
        Assert.Equal(new long[]{1,2,3},winners);
    }
    [Fact]
    public void T12_T13_T25_TerminalCheckpointsAndReadSnapshotsContinueExactly()
    {
        for(ulong seed=0;seed<20;seed++)
        {
            var direct=TerminalFixtures.FalseDelivery(seed);direct.AdvanceTo(20);var rng=Pcg32.Create(seed,"terminal-checkpoints");
            foreach(int tick in new[]{0,1,2,3,9,10}.Concat(Enumerable.Range(0,5).Select(_=>rng.Next(20))))
            {
                var prefix=TerminalFixtures.FalseDelivery(seed);prefix.AdvanceTo(tick);byte[] bytes=prefix.Serialize();var restore=World.Restore(bytes,true);Assert.Equal(bytes,restore.Serialize());
                for(int t=tick+1;t<=20;t++){restore.InspectMessages();restore.InspectKnowledge(new(2));restore.AdvanceTo(t);}
                Assert.Equal(direct.Digest(),restore.Digest());Assert.Equal(direct.Trace.Where(e=>e.Tick>tick).Select(Runs.EventJson),restore.Trace.Select(Runs.EventJson));
            }
        }
    }
    [Fact]
    public void SharedPublicationIsImmutableAndPrivateCostsDoNotLeak()
    {
        var w=TerminalFixtures.Station();Assert.Same(w.ActorView(new(1)).PublicKnowledge,w.ActorView(new(2)).PublicKnowledge);
        w.ScheduleTraversalChange(1,Fixtures.B,1);w.AdvanceTo(2);
        Assert.Equal(5,w.ActorView(new(1)).PublicKnowledge!.Layout.Single(t=>t.Position==Fixtures.B).TraversalTicks);
        Assert.DoesNotContain(w.InspectKnowledge(new(1)),f=>f.Key.SubjectKind==SubjectKind.Tile&&f.Key.SubjectId==2);
    }
    [Fact]
    public void KnowledgeEvictsLeastRecentlyLearnedUnpinnedFactsAndSnapshotsRecency()
    {
        var pinned=new FactSeed(new(1),new FactKey(SubjectKind.Order,1,AttributeKind.OrderStatus),FactValue.Integer(1),true);
        var w=TerminalFixtures.Station(TerminalFixtures.Tiny(knowledge:4),facts:new[]{pinned});w.AdvanceTo(0);
        Assert.Equal(4,w.InspectKnowledge(new(1)).Count);Assert.Contains(w.InspectKnowledge(new(1)),f=>f.Pinned);Assert.True(w.KnowledgeEvictions>0);
        byte[] snapshot=w.Serialize();Assert.Equal(snapshot,World.Restore(snapshot).Serialize());
    }
    [Fact]
    public void CancellingOrLoggingOutDuringMovementNeverTeleports()
    {
        var w=TerminalFixtures.Station();w.Submit(0,new(new(1),ActionKind.Move,Fixtures.C));w.Submit(1,new(new(1),ActionKind.Logout,Fixtures.A));w.Submit(2,new(new(1),ActionKind.Cancel,Fixtures.A));
        w.AdvanceTo(4);Assert.True(w.InspectActor(new(1)).InTransit);w.AdvanceTo(5);Assert.False(w.InspectActor(new(1)).InTransit);Assert.Equal(Fixtures.B,w.InspectActor(new(1)).Position);
        w.AdvanceTo(10);Assert.Equal(Fixtures.B,w.InspectActor(new(1)).Position);
    }
}
