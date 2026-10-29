using System;
using System.Collections.Generic;

namespace Station.Contracts
{
    // Tags keep actor, tile, item and action identifiers distinct at compile time.
    public readonly struct ActorTag { }
    public readonly struct TileTag { }
    public readonly struct ItemTag { }
    public readonly struct ActionTag { }
    public readonly struct AccountTag { }
    public readonly struct MessageTag { }
    public readonly struct RecordTag { }

    public readonly struct Id<T> : IEquatable<Id<T>>, IComparable<Id<T>>
    {
        public long Value { get; }
        public Id(long value) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); Value = value; }
        public bool Equals(Id<T> other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is Id<T> id && Equals(id);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(Id<T> other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool operator ==(Id<T> a, Id<T> b) => a.Equals(b);
        public static bool operator !=(Id<T> a, Id<T> b) => !a.Equals(b);
    }

    public readonly struct Coord : IEquatable<Coord>, IComparable<Coord>
    {
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public Coord(int x, int y, int z) { X = x; Y = y; Z = z; }
        public long Manhattan(Coord b) => checked(Math.Abs((long)X - b.X) + Math.Abs((long)Y - b.Y) + Math.Abs((long)Z - b.Z));
        public bool Equals(Coord b) => X == b.X && Y == b.Y && Z == b.Z;
        public override bool Equals(object? obj) => obj is Coord c && Equals(c);
        public override int GetHashCode() => unchecked((X * 397 ^ Y) * 397 ^ Z);
        public int CompareTo(Coord b) { int c = X.CompareTo(b.X); if (c == 0) c = Y.CompareTo(b.Y); return c == 0 ? Z.CompareTo(b.Z) : c; }
        public override string ToString() => $"({X},{Y},{Z})";
        public static bool operator ==(Coord a, Coord b) => a.Equals(b);
        public static bool operator !=(Coord a, Coord b) => !a.Equals(b);
        public IEnumerable<Coord> Neighbors()
        {
            if (X < int.MaxValue) yield return new Coord(X + 1, Y, Z);
            if (X > int.MinValue) yield return new Coord(X - 1, Y, Z);
            if (Y < int.MaxValue) yield return new Coord(X, Y + 1, Z);
            if (Y > int.MinValue) yield return new Coord(X, Y - 1, Z);
            if (Z < int.MaxValue) yield return new Coord(X, Y, Z + 1);
            if (Z > int.MinValue) yield return new Coord(X, Y, Z - 1);
        }
    }

    public enum TileFamily { Habitat, Industrial, Power, LifeSupport, Storage, Defense, Command, Corridor, LandingPad, Conduit, Hull, Truss }
    public enum ActionKind { Move, PickUp, PutDown, Login, ReadInbox, PostMessage, Logout, Inspect, Cancel, ProduceRecipe, Install, FunctionalTest, InspectMachine, SuspendWork, ResumeWork, QueryRecords, SplitStack, MergeStacks, StoreInStash, InspectTile, InspectActor, Clean, Equip, JoinGuardTeam, BeginEscort, RegisterIntake, ReleaseDetention, Eat, UseToilet, Rest, Sleep, AllocateBerth, Converse, RecoveryLogin, PostLostCard, ReadRecoveryInbox, IssueReplacement, Construct, InspectConstruction }
    public enum ActionStatus { Requested, Started, Running, Completed, Failed, Cancelled }
    public enum FailureReason { None, MissingItem, NotAtRequiredTile, ActorBusy, InvalidDestination, NoRoute, PathBudgetExhausted, NotHeld, MissingCredential, CredentialRevoked, RequiredServiceNotPresent, SessionRequired, ServiceBusy, NotQualified, UnknownRequiredFact, KnowledgeCapacity, ResourceClaimed, InvalidMessage, InsufficientMaterials, MissingTool, IncompatiblePart, StaffUnavailable, InvalidQuantity, IncompatibleStack, ProtectedItem, CustodyRestricted, BerthUnavailable, AlreadyAssigned, ConsentDeclined, IdentityVerificationFailed, RecoverySessionRestricted, InvalidConstructionSite }
    public enum PathOutcome { PathFound, NoRoute, BudgetExhausted, InvalidDestination }
    public enum LocationKind { TileContainer, ActorInventory, ProjectStash, MachineSlot, ServiceOutput }
    public enum EventPhase { Disturbance = 1, PhysicalCompletion = 2, Observation = 3, Delivery = 4, Decision = 5, Intent = 6 }

    public readonly struct ActionIntent
    {
        public Id<ActorTag> Actor { get; }
        public ActionKind Kind { get; }
        public Coord Target { get; }
        public Id<ItemTag> Item { get; }
        public long ServiceId { get; }
        public MessageDraft? Message { get; }
        public long RecipeId { get; }
        public long AttemptId { get; }
        public RecordQuery? Query { get; }
        public long Quantity { get; }
        public Id<ItemTag> OtherItem { get; }
        public Id<ActorTag> TargetActor { get; }
        public long PatrolOrderId { get; }
        public long GuardOrderId { get; } public ConversationTopic ConversationTopic { get; }
        public ActionIntent(Id<ActorTag> actor, ActionKind kind, Coord target, Id<ItemTag> item = default, long serviceId = 0, MessageDraft? message = null, long recipeId = 0, long attemptId = 0, RecordQuery? query = null, long quantity = 0, Id<ItemTag> otherItem = default, Id<ActorTag> targetActor = default, long patrolOrderId = 0, long guardOrderId = 0, ConversationTopic conversationTopic = ConversationTopic.Routine)
        { Actor = actor; Kind = kind; Target = target; Item = item; ServiceId = serviceId; Message = message; RecipeId = recipeId; AttemptId = attemptId; Query = query; Quantity = quantity; OtherItem = otherItem; TargetActor = targetActor; PatrolOrderId = patrolOrderId; GuardOrderId = guardOrderId; ConversationTopic = conversationTopic; }
    }

    public interface IActorView
    {
        Id<ActorTag> Actor { get; }
        long Tick { get; }
        Coord? CurrentTile { get; }
        IReadOnlyList<Id<ItemTag>> Inventory { get; }
        Id<ItemTag> EquippedItem { get; } IReadOnlyList<NeedValue> Needs { get; } IReadOnlyList<NeedServiceDefinition> NeedServices { get; }
        // -1: free citizen; 0: escort controls movement; positive: own detention facility.
        long NeedCustodyFacilityId { get; }
        long OwnBerthId { get; }
        IReadOnlyList<KnowledgeFact> Knowledge { get; }
        PublicKnowledge? PublicKnowledge { get; }
        Id<AccountTag> Account { get; }
        string Role { get; }
        long SessionTerminalId { get; }
        RecordQueryReceipt? LastRecordQuery { get; }
        RosterPayload? KnownRoster { get; }
    }
    public interface IAuthorityView
    {
        long Tick { get; }
        long NominalTravelTicks { get; }
        PublicKnowledge Publication { get; }
        IReadOnlyList<KnownPersonnel> Personnel { get; }
        long LoginTicks { get; }
        long PostTicks { get; }
        long LatencyTicks { get; }
        long CheckInWindow { get; }
    }
    public interface IInstitutionView { long Tick { get; } }
    public interface IRecordStore { long Version { get; } RecordPage Query(RecordQuery query); }
    public interface IMessagePayload { string Kind { get; } }
    public interface IActionSource { ActionIntent? Decide(IActorView view); }
}
