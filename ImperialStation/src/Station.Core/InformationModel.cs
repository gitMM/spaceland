using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class InformationConfig
    {
        public long LoginTicks { get; }
        public long ReadBaseTicks { get; }
        public long ReadPerMessageTicks { get; }
        public long ReadMaximumTicks { get; }
        public long PostTicks { get; }
        public long LatencyTicks { get; }
        public long SessionTicks { get; }
        public long InspectTicks { get; }
        public int KnowledgeCapacity { get; }
        public long QueryTicks { get; }
        public InformationConfig(long loginTicks = 30, long readBaseTicks = 40, long readPerMessageTicks = 10, long readMaximumTicks = 200,
            long postTicks = 60, long latencyTicks = 2, long sessionTicks = 1200, long inspectTicks = 100, int knowledgeCapacity = 256, long queryTicks = 150)
        {
            if (loginTicks < 1 || readBaseTicks < 1 || readPerMessageTicks < 0 || readMaximumTicks < readBaseTicks || postTicks < 1 || latencyTicks < 1 || sessionTicks < 1 || inspectTicks < 1 || knowledgeCapacity < 1 || queryTicks < 1)
                throw new ArgumentException("Information durations/capacity must be positive, per-message cost nonnegative and maximum read >= base.");
            LoginTicks = loginTicks; ReadBaseTicks = readBaseTicks; ReadPerMessageTicks = readPerMessageTicks; ReadMaximumTicks = readMaximumTicks;
            PostTicks = postTicks; LatencyTicks = latencyTicks; SessionTicks = sessionTicks; InspectTicks = inspectTicks; KnowledgeCapacity = knowledgeCapacity; QueryTicks = queryTicks;
        }
    }
    public sealed class TerminalSeed
    {
        public long Id { get; }
        public Coord Position { get; }
        public int Capacity { get; }
        public TerminalCapability Capabilities { get; }
        public IReadOnlyList<string> PermittedRoles { get; }
        public bool Enabled { get; }
        public bool Connected { get; }
        public TerminalSeed(long id, Coord position, IEnumerable<string> permittedRoles, int capacity = 1, TerminalCapability capabilities = TerminalCapability.Inbox | TerminalCapability.Post, bool enabled = true, bool connected = true)
        { Id = id; Position = position; Capacity = capacity; Capabilities = capabilities; PermittedRoles = Array.AsReadOnly(permittedRoles.OrderBy(x => x, StringComparer.Ordinal).ToArray()); Enabled = enabled; Connected = connected; }
    }
    public sealed class CredentialSeed
    {
        public long Id { get; }
        public Id<ActorTag> Holder { get; }
        public Id<AccountTag> Account { get; }
        public Id<ItemTag> Card { get; }
        public bool Active { get; }
        public int Generation { get; }
        public CredentialSeed(long id, Id<ActorTag> holder, Id<AccountTag> account, Id<ItemTag> card, bool active = true, int generation = 1)
        { Id = id; Holder = holder; Account = account; Card = card; Active = active; Generation = generation; }
    }
    public sealed class FactSeed
    {
        public Id<ActorTag> Actor { get; }
        public FactKey Key { get; }
        public FactValue Value { get; }
        public bool Pinned { get; }
        public FactSeed(Id<ActorTag> actor, FactKey key, FactValue value, bool pinned = false) { Actor = actor; Key = key; Value = value; Pinned = pinned; }
    }
    public sealed class InitialMessage
    {
        public MessageDraft Draft { get; }
        public Id<AccountTag> Sender { get; }
        public long DeliveryTick { get; }
        public InitialMessage(MessageDraft draft, Id<AccountTag> sender, long deliveryTick = 0) { Draft = draft; Sender = sender; DeliveryTick = deliveryTick; }
    }
    public sealed class InformationSeed
    {
        public InformationConfig Config { get; }
        public CredentialRecoverySeed? Recovery { get; }
        public IReadOnlyList<TerminalSeed> Terminals { get; }
        public IReadOnlyList<CredentialSeed> Credentials { get; }
        public IReadOnlyList<FactSeed> Facts { get; }
        public IReadOnlyList<InitialMessage> Messages { get; }
        public IReadOnlyList<ReportGoalSeed> ReportGoals { get; }
        public PlannerConfig Planner { get; }
        public PublicKnowledge? Publication { get; }
        public IReadOnlyList<RepairGoalSeed> RepairGoals { get; }
        public IReadOnlyList<RecordAccessSeed> RecordAccess { get; }
        public PerceptionCatalog Perception { get; }
        public IReadOnlyList<MachineSensorSeed> Sensors { get; }
        public InformationSeed(InformationConfig? config = null, IEnumerable<TerminalSeed>? terminals = null, IEnumerable<CredentialSeed>? credentials = null, IEnumerable<FactSeed>? facts = null, IEnumerable<InitialMessage>? messages = null,
            IEnumerable<ReportGoalSeed>? reportGoals = null, PlannerConfig? planner = null, PublicKnowledge? publication = null, IEnumerable<RepairGoalSeed>? repairGoals = null, IEnumerable<RecordAccessSeed>? recordAccess = null, PerceptionCatalog? perception = null, IEnumerable<MachineSensorSeed>? sensors = null, CredentialRecoverySeed? recovery = null)
        { Recovery = recovery; Config = config ?? new InformationConfig(); Terminals = (terminals ?? Array.Empty<TerminalSeed>()).ToArray(); Credentials = (credentials ?? Array.Empty<CredentialSeed>()).ToArray(); Facts = (facts ?? Array.Empty<FactSeed>()).ToArray(); Messages = (messages ?? Array.Empty<InitialMessage>()).ToArray(); ReportGoals = (reportGoals ?? Array.Empty<ReportGoalSeed>()).ToArray(); Planner = planner ?? new PlannerConfig(); Publication = publication; RepairGoals = (repairGoals ?? Array.Empty<RepairGoalSeed>()).ToArray(); RecordAccess = (recordAccess ?? Array.Empty<RecordAccessSeed>()).ToArray(); Perception = perception ?? new PerceptionCatalog(); Sensors = (sensors ?? Array.Empty<MachineSensorSeed>()).ToArray(); }
    }
    internal sealed class KnownEntry
    {
        internal KnowledgeFact Fact = null!;
        internal long Recency;
    }
    internal sealed class KnowledgeStore
    {
        internal readonly SortedDictionary<FactKey, KnownEntry> Entries = new SortedDictionary<FactKey, KnownEntry>();
        internal long Sequence;
        internal IReadOnlyList<KnowledgeFact> Inspect() => Array.AsReadOnly(Entries.Values.Select(e => e.Fact).ToArray());
    }
    internal sealed class Terminal
    {
        internal TerminalSeed Definition = null!;
        internal bool Enabled;
        internal bool Connected;
        internal readonly List<Id<ActorTag>> Claims = new List<Id<ActorTag>>();
        internal readonly List<Id<ActorTag>> Queue = new List<Id<ActorTag>>();
    }
    internal sealed class Session
    {
        internal long Id;
        internal Id<ActorTag> Actor;
        internal Id<AccountTag> Account;
        internal long Terminal;
        internal long Credential;
        internal SessionScope Scope;
        internal long Expires;
    }
    internal sealed class Credential
    {
        internal CredentialSeed Definition = null!;
        internal bool Active;
    }
    public sealed class MessageSnapshot
    {
        public long Id { get; internal set; }
        public MessageDraft Draft { get; internal set; } = null!;
        public Id<ActorTag> PhysicalSender { get; internal set; }
        public Id<AccountTag> SenderAccount { get; internal set; }
        public long TerminalId { get; internal set; }
        public long SessionId { get; internal set; }
        public long SubmittedTick { get; internal set; }
        public long DeliveryTick { get; internal set; }
        public long? FirstReadTick { get; internal set; }
        public MessageStage Stage { get; internal set; }
        public bool Seeded { get; internal set; }
        internal MessageSnapshot Copy() => (MessageSnapshot)MemberwiseClone();
    }
    public sealed class BeliefSnapshot
    {
        public FactKey Key { get; internal set; }
        public FactValue Value { get; internal set; }
        public long MessageId { get; internal set; }
        public long ObservedTick { get; internal set; }
        public long ReceivedTick { get; internal set; }
        public string Source { get; internal set; } = "Reported";
        public long SensorId { get; internal set; }
        public long ReadingId { get; internal set; }
        public bool ContradictsPrevious { get; internal set; }
        internal BeliefSnapshot Copy() => (BeliefSnapshot)MemberwiseClone();
    }
    public sealed class SessionSnapshot
    {
        public long Id { get; internal set; }
        public long ActorId { get; internal set; }
        public long AccountId { get; internal set; }
        public long TerminalId { get; internal set; }
        public long Expires { get; internal set; }
        public SessionScope Scope { get; internal set; }
    }
    public sealed class TerminalSnapshot
    {
        public long Id { get; internal set; }
        public Coord Position { get; internal set; }
        public bool Enabled { get; internal set; }
        public bool Connected { get; internal set; }
        public int Capacity { get; internal set; }
        public IReadOnlyList<long> Claims { get; internal set; } = Array.Empty<long>();
        public IReadOnlyList<long> Queue { get; internal set; } = Array.Empty<long>();
    }
}
