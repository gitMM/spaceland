using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class ProjectStashDefinition
    {
        public long Id { get; }
        public long ProjectId { get; }
        public Coord Position { get; }
        public ProjectStashDefinition(long id, long projectId, Coord position)
        { if (id < 1 || projectId < 1) throw new ArgumentException("Stash and project identifiers must be positive."); Id = id; ProjectId = projectId; Position = position; }
    }
    public sealed class StackChange
    {
        public long Id { get; internal set; }
        public long Tick { get; internal set; }
        public long ActorId { get; internal set; }
        public long ActionId { get; internal set; }
        public ActionKind Kind { get; internal set; }
        public long SourceItem { get; internal set; }
        public long DestinationItem { get; internal set; }
        public string Resource { get; internal set; } = "";
        public long SourceBefore { get; internal set; }
        public long DestinationBefore { get; internal set; }
        public long Quantity { get; internal set; }
        public ItemLocation Location { get; internal set; }
        internal StackChange Copy() => (StackChange)MemberwiseClone();
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, ProjectStashDefinition> stashes = new SortedDictionary<long, ProjectStashDefinition>();
        private readonly List<StackChange> stackChanges = new List<StackChange>();
        private long nextStackChange = 1;
        public IReadOnlyList<ProjectStashDefinition> InspectStashes() => stashes.Values.ToArray();
        public IReadOnlyList<StackChange> InspectStackChanges() => stackChanges.Select(c => c.Copy()).ToArray();
        private static bool IsStackAction(ActionKind kind) => kind is ActionKind.SplitStack or ActionKind.MergeStacks or ActionKind.StoreInStash;
        private FailureReason ValidateStackAction(Actor actor, ActiveAction action)
        {
            if (!WorkEnabled) return FailureReason.RequiredServiceNotPresent;
            if (!items.TryGetValue(action.Item, out var item)) return FailureReason.MissingItem;
            if (!HeldBy(item, actor) && !ItemAvailableAt(item, actor.Position)) return FailureReason.NotAtRequiredTile;
            if (ClaimedByOther(ClaimKind.Item, item.Id.Value, action.Id.Value)) return FailureReason.ResourceClaimed;
            if (action.Kind == ActionKind.StoreInStash)
            {
                if (!HeldBy(item, actor)) return FailureReason.NotHeld;
                if (!stashes.TryGetValue(action.ServiceId, out var stash)) return FailureReason.RequiredServiceNotPresent;
                return actor.Position == stash.Position ? FailureReason.None : FailureReason.NotAtRequiredTile;
            }
            if (credentials.Values.Any(c => c.Definition.Card == item.Id)) return FailureReason.ProtectedItem;
            if (action.Kind == ActionKind.SplitStack)
                return action.Quantity > 0 && action.Quantity < item.Quantity ? FailureReason.None : FailureReason.InvalidQuantity;
            if (action.OtherItem == action.Item) return FailureReason.IncompatibleStack;
            if (!items.TryGetValue(action.OtherItem, out var other)) return FailureReason.MissingItem;
            if (!HeldBy(other, actor) && !ItemAvailableAt(other, actor.Position)) return FailureReason.NotAtRequiredTile;
            if (credentials.Values.Any(c => c.Definition.Card == other.Id)) return FailureReason.ProtectedItem;
            if (item.Resource != other.Resource || item.Location.Kind != other.Location.Kind || item.Location.Owner != other.Location.Owner) return FailureReason.IncompatibleStack;
            if (ClaimedByOther(ClaimKind.Item, other.Id.Value, action.Id.Value)) return FailureReason.ResourceClaimed;
            return other.Quantity <= long.MaxValue - item.Quantity ? FailureReason.None : FailureReason.InvalidQuantity;
        }
        private void StartStackAction(Actor actor, ActiveAction action)
        {
            var failure = ValidateStackAction(actor, action);
            if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            action.Completes = checked(processingTick + Config.ItemActionTicks);
            AddClaim(ClaimKind.Item, action.Item.Value, actor, action);
            if (action.Kind == ActionKind.MergeStacks) AddClaim(ClaimKind.Item, action.OtherItem.Value, actor, action);
            actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, action.Item, target: actor.Position, arrival: action.Completes, detail: action.Kind.ToString(), causeId: action.CauseId);
            ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteStackAction(Actor actor, ActiveAction action)
        {
            var failure = ValidateStackAction(actor, action);
            if (failure != FailureReason.None) { ReleaseResourceClaims(action.Id.Value); actor.Action = null; Fail(actor, action.Id, failure); return; }
            var item = items[action.Item];
            if (action.Kind == ActionKind.StoreInStash)
            {
                MoveItem(item, new ItemLocation(LocationKind.ProjectStash, action.ServiceId));
                Emit("ItemMoved", actor, action.Id, item.Id, target: actor.Position, detail: "ProjectStash:" + action.ServiceId, causeId: action.CauseId);
                ObserveOwnTransfer(actor, item, action.CauseId);
            }
            else
            {
                _ = checked(nextStackChange + 1);
                if (action.Kind == ActionKind.SplitStack) _ = checked(nextItem + 1);
                var change = new StackChange { Id = nextStackChange++, Tick = processingTick, ActorId = actor.Id.Value, ActionId = action.Id.Value, Kind = action.Kind, Resource = item.Resource, Location = item.Location };
                Item source, destination;
                if (action.Kind == ActionKind.SplitStack)
                {
                    source = item; destination = new Item(new Id<ItemTag>(nextItem++), item.Resource, action.Quantity, item.Location);
                    change.SourceBefore = source.Quantity; change.DestinationBefore = 0; change.Quantity = action.Quantity;
                    source.Quantity -= action.Quantity; AddItem(destination);
                }
                else
                {
                    source = items[action.OtherItem]; destination = item;
                    change.SourceBefore = source.Quantity; change.DestinationBefore = destination.Quantity; change.Quantity = source.Quantity;
                    destination.Quantity = checked(destination.Quantity + source.Quantity); source.Quantity = 0; RemoveItem(source.Id);
                }
                change.SourceItem = source.Id.Value; change.DestinationItem = destination.Id.Value; stackChanges.Add(change);
                Emit(action.Kind == ActionKind.SplitStack ? "StackSplit" : "StacksMerged", actor, action.Id, source.Id, target: actor.Position,
                    detail: "destination=" + destination.Id.Value + "; quantity=" + change.Quantity + "; resource=" + item.Resource, causeId: action.CauseId, otherItemId: destination.Id.Value, quantity: change.Quantity);
                ObserveOwnTransfer(actor, source, action.CauseId); ObserveOwnTransfer(actor, destination, action.CauseId);
            }
            Finish(actor); AssertAccounting();
            foreach (var observer in actors.Values.Where(a => a.Position == actor.Position && a.Action?.Kind != ActionKind.Move)) ScheduleSensing(observer, action.CauseId);
        }
    }
}
