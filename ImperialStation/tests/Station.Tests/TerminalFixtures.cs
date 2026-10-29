using Station.Contracts;
using Station.Core;

namespace Station.Tests;

internal static class TerminalFixtures
{
    internal static FactKey ItemKey => new(SubjectKind.Item, 1, AttributeKind.Location);
    internal static InformationConfig Tiny(long read = 1, long post = 1, long session = 100, int knowledge = 256) => new(loginTicks: 1, readBaseTicks: read, readPerMessageTicks: 0, readMaximumTicks: read, postTicks: post, latencyTicks: 2, sessionTicks: session, inspectTicks: 1, knowledgeCapacity: knowledge);
    internal static MessageDraft Draft(long actor, long key = 1, long observed = 0) => new(MessageKind.Peer, RecipientKind.Account, new(actor), new FactKey(SubjectKind.Job, key, AttributeKind.WorkStatus), FactValue.Integer(key), observed);
    internal static World Station(InformationConfig? config = null, IEnumerable<InitialMessage>? messages = null, int population = 2, bool allAtA = false, ulong seed = 1, IEnumerable<FactSeed>? facts = null)
    {
        var actors = Enumerable.Range(1, population).Select(i => new ActorSeed(new(i), "TerminalActor" + i, "worker", allAtA || i == 1 ? Fixtures.A : Fixtures.C)).ToArray();
        var items = actors.Select(a => new Item(new(100 + a.Id.Value), "keycard", 1, new ItemLocation(LocationKind.ActorInventory, a.Id.Value))).Append(new Item(new(1), "component", 1, new ItemLocation(LocationKind.TileContainer, 1)));
        var terminals = new[] { new TerminalSeed(1, Fixtures.A, new[] {"worker"}), new TerminalSeed(2, Fixtures.B, new[] {"worker"}), new TerminalSeed(3, Fixtures.C, new[] {"worker"}) };
        return new World("terminal_fixture", seed, new WorldConfig(itemActionTicks:1), Fixtures.Catalog,
            new[] { new Tile(new(1),"Storage",Fixtures.A,traversalOverride:5), new Tile(new(2),"Hull",Fixtures.B,traversalOverride:5), new Tile(new(3),"Command",Fixtures.C,traversalOverride:5) }, actors, items, true,
            new InformationSeed(config ?? Tiny(), terminals, actors.Select(a => new CredentialSeed(a.Id.Value, a.Id, a.Account, new(100+a.Id.Value))), facts, messages));
    }
    internal static World FalseDelivery(ulong seed = 1)
    {
        var w = Station(messages: new[] {
            new InitialMessage(new MessageDraft(MessageKind.Order, RecipientKind.Account, new(1), new FactKey(SubjectKind.Order,1,AttributeKind.OrderStatus), FactValue.Integer(1),0),new(0)),
            new InitialMessage(new MessageDraft(MessageKind.Order, RecipientKind.Account, new(2), new FactKey(SubjectKind.Order,2,AttributeKind.OrderStatus), FactValue.Integer(1),0),new(0)) }, seed:seed);
        w.Submit(0,new(new(1),ActionKind.Login,Fixtures.A,serviceId:1)); w.Submit(0,new(new(2),ActionKind.Login,Fixtures.C,serviceId:3));
        w.Submit(1,new(new(1),ActionKind.ReadInbox,Fixtures.A,serviceId:1)); w.Submit(1,new(new(2),ActionKind.ReadInbox,Fixtures.C,serviceId:3));
        w.Submit(2,new(new(1),ActionKind.PostMessage,Fixtures.A,serviceId:1,message:new MessageDraft(MessageKind.WorkCompleted,RecipientKind.Authority,default,ItemKey,FactValue.At(Fixtures.B),2)));
        w.Submit(2,new(new(2),ActionKind.Move,Fixtures.B));
        w.Submit(7,new(new(2),ActionKind.Inspect,Fixtures.B,new(1)));
        w.Submit(8,new(new(2),ActionKind.Login,Fixtures.B,serviceId:2));
        w.Submit(9,new(new(2),ActionKind.PostMessage,Fixtures.B,serviceId:2,message:new MessageDraft(MessageKind.Observation,RecipientKind.Authority,default,ItemKey,FactValue.Absent(Fixtures.B),8)));
        return w;
    }
}
