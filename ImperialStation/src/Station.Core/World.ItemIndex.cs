using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        // Derived physical-location index. IDs retain the canonical item ordering;
        // no additional knowledge is exposed by inventory or local observation.
        private readonly SortedDictionary<(LocationKind, long), SortedDictionary<Id<ItemTag>, Item>> itemsByLocation = new SortedDictionary<(LocationKind, long), SortedDictionary<Id<ItemTag>, Item>>();
        private IEnumerable<Item> ItemsAt(LocationKind kind, long owner) => itemsByLocation.TryGetValue((kind, owner), out var group) ? group.Values : (IEnumerable<Item>)Array.Empty<Item>();
        private void IndexItem(Item item)
        {
            var key = (item.Location.Kind, item.Location.Owner);
            if (!itemsByLocation.TryGetValue(key, out var group)) itemsByLocation.Add(key, group = new SortedDictionary<Id<ItemTag>, Item>());
            group.Add(item.Id, item);
        }
        private void UnindexItem(Item item)
        {
            var key = (item.Location.Kind, item.Location.Owner); var group = itemsByLocation[key];
            group.Remove(item.Id); if (group.Count == 0) itemsByLocation.Remove(key);
        }
        private void AddItem(Item item) { items.Add(item.Id, item); IndexItem(item); }
        private void RemoveItem(Id<ItemTag> id) { UnindexItem(items[id]); items.Remove(id); }
        private void MoveItem(Item item, ItemLocation location) { UnindexItem(item); item.Location = location; IndexItem(item); }
        private IEnumerable<Item> AvailableItemsAt(Coord position)
        {
            var local = new List<Item>();
            local.AddRange(ItemsAt(LocationKind.TileContainer, Topology.Tiles[position].Id.Value));
            foreach (var service in workstations.Values)
                if (service.Position == position) local.AddRange(ItemsAt(LocationKind.ServiceOutput, service.Id));
            foreach (var stash in stashes.Values)
                if (stash.Position == position) local.AddRange(ItemsAt(LocationKind.ProjectStash, stash.Id));
            return local.OrderBy(i => i.Id);
        }
    }
}
