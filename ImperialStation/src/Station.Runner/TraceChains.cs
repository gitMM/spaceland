using Station.Core;

namespace Station.Runner;

public static class TraceChains
{
    public static IReadOnlyList<TraceEvent> Select(IReadOnlyList<TraceEvent> events,long eventId)
    {
        var byId=events.ToDictionary(e=>e.Id);
        if(!byId.ContainsKey(eventId))throw new ArgumentException("Event ID is not present in this replay trace.");
        var parents=events.ToDictionary(e=>e.Id,_=>new SortedSet<long>());var children=events.ToDictionary(e=>e.Id,_=>new SortedSet<long>());
        var lastAction=new Dictionary<long,long>();var lastMessage=new Dictionary<long,long>();
        void Link(long from,long to){if(from>0&&from<to&&byId.ContainsKey(from)){parents[to].Add(from);children[from].Add(to);}}
        foreach(var e in events.OrderBy(e=>e.Id))
        {
            Link(e.CauseId,e.Id);
            if(e.ActionId>0){if(lastAction.TryGetValue(e.ActionId,out long prior))Link(prior,e.Id);lastAction[e.ActionId]=e.Id;}
            if(e.MessageId>0){if(lastMessage.TryGetValue(e.MessageId,out long prior))Link(prior,e.Id);lastMessage[e.MessageId]=e.Id;}
        }
        var selected=new SortedSet<long>{eventId};
        void Walk(Dictionary<long,SortedSet<long>> edges)
        {
            var visited=new HashSet<long>{eventId};var queue=new Queue<long>();queue.Enqueue(eventId);
            while(queue.Count>0)foreach(long id in edges[queue.Dequeue()])if(visited.Add(id)){selected.Add(id);queue.Enqueue(id);}
        }
        Walk(parents);Walk(children);return selected.Select(id=>byId[id]).ToArray();
    }
}
