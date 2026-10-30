using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class TileDefinition
    {
        public string Id { get; }
        public TileFamily Family { get; }
        public long TraversalTicks { get; }
        public string VisualKey { get; }
        public IReadOnlyList<string> Capabilities { get; }
        public ConstructionDefinition? Construction { get; }
        public TileDefinition(string id, TileFamily family, long traversalTicks, string visualKey = "", IEnumerable<string>? capabilities = null, ConstructionDefinition? construction = null)
        {
            if (string.IsNullOrWhiteSpace(id) || !Enum.IsDefined(typeof(TileFamily), family) || traversalTicks < 1 || traversalTicks > long.MaxValue / 2)
                throw new ArgumentException("Invalid tile definition ID, family or traversal duration.");
            Id = id; Family = family; TraversalTicks = traversalTicks; VisualKey = visualKey;
            Capabilities = Array.AsReadOnly((capabilities ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Construction = construction;
        }
    }

    public sealed class Tile
    {
        public Id<TileTag> Id { get; }
        public string DefinitionId { get; }
        public Coord Position { get; }
        public bool Completed { get; }
        public long? TraversalOverride { get; internal set; }
        public Tile(Id<TileTag> id, string definitionId, Coord position, bool completed = true, long? traversalOverride = null)
        { Id = id; DefinitionId = definitionId; Position = position; Completed = completed; TraversalOverride = traversalOverride; }
        internal Tile Copy() => new Tile(Id, DefinitionId, Position, Completed, TraversalOverride);
    }

    public readonly struct ItemLocation
    {
        public LocationKind Kind { get; }
        public long Owner { get; }
        public ItemLocation(LocationKind kind, long owner) { Kind = kind; Owner = owner; }
    }

    public sealed class Item
    {
        public Id<ItemTag> Id { get; }
        public string Resource { get; }
        public long Quantity { get; internal set; }
        public ItemLocation Location { get; internal set; }
        public Item(Id<ItemTag> id, string resource, long quantity, ItemLocation location)
        { Id = id; Resource = resource; Quantity = quantity; Location = location; }
        internal Item Copy() => new Item(Id, Resource, Quantity, Location);
    }

    public sealed class ActorSeed
    {
        public Id<ActorTag> Id { get; }
        public string Name { get; }
        public string Role { get; }
        public Coord Position { get; }
        public Id<AccountTag> Account { get; }
        public ActorSeed(Id<ActorTag> id, string name, string role, Coord position, Id<AccountTag> account = default)
        { Id = id; Name = name; Role = role; Position = position; Account = account.Value == 0 ? new Id<AccountTag>(id.Value) : account; }
    }

    internal sealed class Actor
    {
        internal Id<ActorTag> Id;
        internal string Name = "";
        internal string Role = "";
        internal Coord Position;
        internal Id<AccountTag> Account;
        internal KnowledgeStore Knowledge = new KnowledgeStore();
        internal ActiveAction? Action;
        internal FailureReason LastFailure;
        internal ActionStatus LastStatus;
    }

    internal sealed class ActiveAction
    {
        internal Id<ActionTag> Id;
        internal ActionKind Kind;
        internal Coord Target;
        internal Id<ItemTag> Item;
        internal long Started;
        internal long Completes;
        internal List<Coord> Route = new List<Coord>();
        internal int RouteIndex;
        internal Coord Source;
        internal Coord Destination;
        internal long Departure;
        internal long Arrival;
        internal long ServiceId;
        internal long SessionId;
        internal long CredentialId;
        internal long CauseId;
        internal MessageDraft? Message;
        internal List<long> CapturedMessages = new List<long>();
        // Set when a completed inbox read could not take in a captured message (knowledge capacity).
        internal bool LeftUnread;
        internal bool Waiting;
        internal bool CancelAtBoundary;
        internal long RecipeId;
        internal long AttemptId;
        internal RecordQuery? Query;
        internal RecordPage? CapturedRecords;
        internal long Quantity;
        internal Id<ItemTag> OtherItem;
        internal Id<ActorTag> TargetActor;
        internal long PatrolOrderId;
        internal long GuardOrderId; internal ConversationTopic ConversationTopic;
        internal List<MaterialBinding> Materials = new List<MaterialBinding>();
    }

    public sealed class ActorSnapshot
    {
        public Id<ActorTag> Id { get; internal set; }
        public string Name { get; internal set; } = "";
        public string Role { get; internal set; } = "";
        public Coord Position { get; internal set; }
        public bool InTransit { get; internal set; }
        public Coord Source { get; internal set; }
        public Coord Destination { get; internal set; }
        public long Departure { get; internal set; }
        public long Arrival { get; internal set; }
        public long ProgressNumerator { get; internal set; }
        public long ProgressDenominator { get; internal set; } = 1;
        public ActionKind? Action { get; internal set; }
        public long ActionId { get; internal set; }
        public long ActionStart { get; internal set; }
        public long ActionCompletion { get; internal set; }
        public ActionStatus LastStatus { get; internal set; }
        public FailureReason LastFailure { get; internal set; }
        public IReadOnlyList<Id<ItemTag>> Inventory { get; internal set; } = Array.Empty<Id<ItemTag>>();
    }

    public sealed class WorldConfig
    {
        public int TickDurationMs { get; }
        public long ItemActionTicks { get; }
        public int PathBudget { get; }
        public int MaxEventsPerTick { get; }
        public long AuthorityNominalTicks { get; }
        public WorldConfig(int tickDurationMs = 100, long itemActionTicks = 10, int pathBudget = 100000, int maxEventsPerTick = 1000000, long authorityNominalTicks = 40)
        {
            if (tickDurationMs < 1 || itemActionTicks < 1 || pathBudget < 1 || maxEventsPerTick < 1 || authorityNominalTicks < 1)
                throw new ArgumentException("Durations and budgets must be positive.");
            TickDurationMs = tickDurationMs; ItemActionTicks = itemActionTicks; PathBudget = pathBudget;
            MaxEventsPerTick = maxEventsPerTick; AuthorityNominalTicks = authorityNominalTicks;
        }
    }

    public enum ActionOrigin { None, ExternalCommand, Autonomous, SystemProtocol }
    public sealed class TraceEvent
    {
        public long Id { get; internal set; }
        public long Tick { get; internal set; }
        public string Type { get; internal set; } = "";
        public string Visibility { get; internal set; } = "privileged_truth";
        public long ActorId { get; internal set; }
        public long ActionId { get; internal set; }
        public long ItemId { get; internal set; }
        public long OtherItemId { get; internal set; }
        public long Quantity { get; internal set; }
        public Coord Source { get; internal set; }
        public Coord Target { get; internal set; }
        public long Departure { get; internal set; }
        public long Arrival { get; internal set; }
        public string Detail { get; internal set; } = "";
        public long CauseId { get; internal set; }
        public ActionOrigin Origin { get; internal set; }
        public long MessageId { get; internal set; }
        public long TerminalId { get; internal set; }
        public long SessionId { get; internal set; }
        public long SensorId { get; internal set; }
        public long ReadingId { get; internal set; }
        public long AccountId { get; internal set; }
        public long TaskId { get; internal set; }
        public long AttemptId { get; internal set; }
        public FactKey? FactKey { get; internal set; }
        public FactValue? FactValue { get; internal set; }
    }
}
