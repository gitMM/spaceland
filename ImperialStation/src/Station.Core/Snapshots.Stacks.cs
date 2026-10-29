using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteStacks(BinaryWriter w)
        {
            w.Write(nextStackChange); w.Write(stackChanges.Count);
            foreach (var c in stackChanges)
            {
                w.Write(c.Id); w.Write(c.Tick); w.Write(c.ActorId); w.Write(c.ActionId); w.Write((int)c.Kind); w.Write(c.SourceItem); w.Write(c.DestinationItem);
                w.Write(c.Resource); w.Write(c.SourceBefore); w.Write(c.DestinationBefore); w.Write(c.Quantity); w.Write((int)c.Location.Kind); w.Write(c.Location.Owner);
            }
        }
        private void ReadStacks(BinaryReader r)
        {
            nextStackChange = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var c = new StackChange { Id = r.ReadInt64(), Tick = r.ReadInt64(), ActorId = r.ReadInt64(), ActionId = r.ReadInt64(), Kind = ReadEnum<ActionKind>(r), SourceItem = r.ReadInt64(), DestinationItem = r.ReadInt64(),
                    Resource = r.ReadString(), SourceBefore = r.ReadInt64(), DestinationBefore = r.ReadInt64(), Quantity = r.ReadInt64(), Location = new ItemLocation(ReadEnum<LocationKind>(r), r.ReadInt64()) };
                if (c.Id != i + 1 || c.Tick < 0 || c.Tick > LastProcessedTick || c.ActionId < 1 || c.ActionId >= nextAction || !actors.ContainsKey(new Id<ActorTag>(c.ActorId)) || !resources.ContainsKey(c.Resource) ||
                    c.SourceItem < 1 || c.SourceItem >= nextItem || c.DestinationItem < 1 || c.DestinationItem >= nextItem || c.SourceItem == c.DestinationItem || c.SourceBefore < 1 || c.Quantity < 1)
                    throw new ArgumentException("Invalid stack change identity, ownership, time or quantity.");
                if (c.Kind == ActionKind.SplitStack)
                { if (c.DestinationBefore != 0 || c.Quantity >= c.SourceBefore) throw new ArgumentException("Invalid split amounts."); }
                else if (c.Kind == ActionKind.MergeStacks)
                { if (c.DestinationBefore < 1 || c.Quantity != c.SourceBefore || c.Quantity > long.MaxValue - c.DestinationBefore) throw new ArgumentException("Invalid merge amounts."); }
                else throw new ArgumentException("Invalid stack change operation.");
                ValidateLocation(c.Location); stackChanges.Add(c);
            }
            if (nextStackChange != count + 1L) throw new ArgumentException("Invalid stack change counter.");
            if (stackChanges.GroupBy(c => c.ActionId).Any(g => g.Count() != 1)) throw new ArgumentException("Duplicate stack action commit.");
            foreach (var actor in actors.Values.Where(a => a.Action != null && IsStackAction(a.Action.Kind)))
                if (ValidateStackAction(actor, actor.Action!) != FailureReason.None) throw new ArgumentException("Invalid active stack operation.");
        }
    }
}
