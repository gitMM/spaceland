using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public enum ConversationStatus { Active, Completed, Cancelled }
    public sealed class ConversationExchange
    {
        public long SpeakerId { get; internal set; }
        public long RecipientId { get; internal set; }
        public KnowledgeFact Fact { get; internal set; } = null!;
        public bool Learned { get; internal set; }
    }
    public sealed class ConversationSnapshot
    {
        public long Id { get; internal set; }
        public long FirstActorId { get; internal set; }
        public long SecondActorId { get; internal set; }
        public long SecondActionId { get; internal set; }
        public Coord Position { get; internal set; }
        public ConversationTopic Topic { get; internal set; }
        public long StartedTick { get; internal set; }
        public long CompletesTick { get; internal set; }
        public long EndedTick { get; internal set; } = -1;
        public ConversationStatus Status { get; internal set; }
        public IReadOnlyList<ConversationExchange> Exchanges { get; internal set; } = Array.Empty<ConversationExchange>();
        internal ConversationSnapshot Copy() { var c = (ConversationSnapshot)MemberwiseClone(); c.Exchanges = Exchanges.Select(e => new ConversationExchange { SpeakerId = e.SpeakerId, RecipientId = e.RecipientId, Fact = e.Fact, Learned = e.Learned }).ToArray(); return c; }
    }
    public sealed partial class World
    {
        public ConversationConfig? Conversations { get; private set; }
        private readonly SortedDictionary<long, ConversationSnapshot> conversations = new SortedDictionary<long, ConversationSnapshot>();
        private readonly SortedDictionary<long, long> conversationClaims = new SortedDictionary<long, long>();
        private readonly SortedDictionary<long, long> conversationActions = new SortedDictionary<long, long>();
        private readonly SortedDictionary<Coord, SortedSet<long>> localActors = new SortedDictionary<Coord, SortedSet<long>>();
        public long ConversationPartnerCandidatesExamined { get; private set; }
        public IReadOnlyList<ConversationSnapshot> InspectConversations() => conversations.Values.Select(c => c.Copy()).ToArray();
        private void RegisterLocalActor(Actor actor)
        { if (!localActors.TryGetValue(actor.Position, out var bucket)) { bucket = new SortedSet<long>(); localActors.Add(actor.Position, bucket); } bucket.Add(actor.Id.Value); }
        private void RelocateActor(Actor actor, Coord position)
        {
            var old = localActors[actor.Position]; old.Remove(actor.Id.Value); if (old.Count == 0) localActors.Remove(actor.Position);
            actor.Position = position; RegisterLocalActor(actor);
        }
        private void InitializeConversations(ConversationConfig config)
        {
            if (config.ExplicitConsentActors.Any(id => !actors.ContainsKey(new Id<ActorTag>(id)))) throw new ArgumentException("Unknown conversation consent actor.");
            Conversations = config;
        }
        private bool OffDutyConversation(Id<ActorTag> actor) => ConversationPolicy.OffDuty(routines.TryGetValue(actor, out var r) ? r.KnownRoster : null, NeedNow);
        private bool ConversationOpportunity(Id<ActorTag> actor) => ConversationPolicy.Opportunity(AtSeek(actor, NeedKind.Social), () => OffDutyConversation(actor));
        // Evaluated for the invited partner: its own checks may wake its own goals.
        private bool ConversationConsents(Actor actor) =>
            GoalPriority.FirstDeferring(ConversationPolicy.ConsentGates, g => g == PriorityGate.Inactive ? Conversations == null : Defers(actor, g)) == PriorityGate.None &&
            ConversationPolicy.Consents(Conversations!.ExplicitConsentActors.Contains(actor.Id.Value), needAgents.ContainsKey(actor.Id.Value), () => OffDutyConversation(actor.Id));
        private IReadOnlyList<long> LocalConversationCandidates(Actor actor, long after)
        {
            if (Conversations == null || !localActors.TryGetValue(actor.Position, out var bucket)) return Array.Empty<long>();
            return ConversationPolicy.PartnerCandidates(bucket, actor.Id.Value, after, Conversations.PartnerCandidates);
        }
        private void StartConversation(Actor actor, ActiveAction action)
        {
            if (Conversations == null || !Enum.IsDefined(typeof(ConversationTopic), action.ConversationTopic) || action.TargetActor == actor.Id || !actors.TryGetValue(action.TargetActor, out var partner)) { Fail(actor, action.Id, FailureReason.RequiredServiceNotPresent); return; }
            if (partner.Position != actor.Position || partner.Action?.Kind == ActionKind.Move) { Fail(actor, action.Id, FailureReason.NotAtRequiredTile); return; }
            if (partner.Action != null || conversationClaims.ContainsKey(partner.Id.Value)) { Fail(actor, action.Id, FailureReason.ActorBusy); return; }
            if (!ConversationConsents(partner)) { Fail(actor, action.Id, FailureReason.ConsentDeclined); return; }
            long end = checked(processingTick + Conversations.DurationTicks);
            var other = new ActiveAction { Id = new Id<ActionTag>(nextAction++), Kind = ActionKind.Converse, Target = actor.Position, TargetActor = actor.Id, Started = processingTick, Completes = end, ConversationTopic = action.ConversationTopic, CauseId = action.CauseId };
            var c = new ConversationSnapshot { Id = action.Id.Value, FirstActorId = actor.Id.Value, SecondActorId = partner.Id.Value, SecondActionId = other.Id.Value, Position = actor.Position, Topic = action.ConversationTopic, StartedTick = processingTick, CompletesTick = end };
            conversations.Add(c.Id, c); conversationActions.Add(c.Id, c.Id); conversationActions.Add(other.Id.Value, c.Id); conversationClaims.Add(actor.Id.Value, c.Id); conversationClaims.Add(partner.Id.Value, c.Id);
            EndSession(actor.Id, "Conversation"); EndSession(partner.Id, "Conversation"); action.Completes = end;
            actor.Action = action; partner.Action = other; actor.LastStatus = partner.LastStatus = ActionStatus.Running; actor.LastFailure = partner.LastFailure = FailureReason.None;
            if (needAgents.TryGetValue(partner.Id.Value, out var agent)) { agent.Goal = NeedKind.Social; agent.ServiceId = 0; agent.ActiveActionId = other.Id.Value; agent.PlannedTick = processingTick; }
            other.CauseId = Emit("ConversationConsented", partner, other.Id, causeId: action.CauseId, detail: "conversation=" + c.Id + "; speaker=" + actor.Id.Value);
            Emit("ActionRequested", partner, other.Id, target: other.Target, detail: "Converse", causeId: other.CauseId, origin: ActionOrigin.SystemProtocol);
            Emit("ConversationStarted", actor, action.Id, arrival: end, detail: "partner=" + partner.Id.Value + "; topic=" + c.Topic);
            ScheduleCompletion(actor, end);
        }
        private void CompleteConversation(Actor actor, ActiveAction action)
        {
            var c = conversations[conversationClaims[actor.Id.Value]]; var partner = actors[new Id<ActorTag>(c.SecondActorId)];
            if (c.Id != action.Id.Value || partner.Action?.Id.Value != c.SecondActionId || actor.Position != c.Position || partner.Position != c.Position) throw new InvalidOperationException("Conversation lost its coordination claims.");
            var exchanges = new List<ConversationExchange>();
            foreach (var speaker in new[] { actor, partner })
                foreach (var fact in ConversationPolicy.SharedFacts(speaker.Id.Value, c.Topic, speaker.Knowledge.Inspect(), Conversations!.FactsPerSpeaker))
                    exchanges.Add(new ConversationExchange { SpeakerId = speaker.Id.Value, RecipientId = speaker.Id == actor.Id ? partner.Id.Value : actor.Id.Value, Fact = fact });
            foreach (var e in exchanges)
            {
                var recipient = actors[new Id<ActorTag>(e.RecipientId)];
                // Conversation cannot overwrite pinned administrative/identity evidence.
                if (recipient.Knowledge.Entries.TryGetValue(e.Fact.Key, out var old) && old.Fact.Pinned) continue;
                e.Learned = Learn(recipient, e.Fact.Key, e.Fact.Value, EvidenceKind.Heard, c.Id, e.Fact.ObservedTick, action.CauseId, sourceActorId: e.SpeakerId);
            }
            c.Exchanges = exchanges.ToArray(); c.Status = ConversationStatus.Completed; c.EndedTick = processingTick;
            conversationClaims.Remove(actor.Id.Value); conversationClaims.Remove(partner.Id.Value);
            RelieveNeed(actor, NeedKind.Social, 300, false); RelieveNeed(partner, NeedKind.Social, 300, false);
            Emit("ConversationCompleted", actor, action.Id, detail: "partner=" + partner.Id.Value + "; shared=" + exchanges.Count(e => e.Learned));
            Finish(actor); Finish(partner);
        }
        private bool CancelConversation(Actor actor)
        {
            if (!conversationClaims.TryGetValue(actor.Id.Value, out long id)) return false;
            var c = conversations[id]; c.Status = ConversationStatus.Cancelled; c.EndedTick = processingTick;
            conversationClaims.Remove(c.FirstActorId); conversationClaims.Remove(c.SecondActorId);
            CancelRunning(actors[new Id<ActorTag>(c.FirstActorId)]); CancelRunning(actors[new Id<ActorTag>(c.SecondActorId)]);
            Emit("ConversationCancelled", actor, detail: "conversation=" + c.Id); return true;
        }
        private ConversationSnapshot? ConversationForAction(long action) => conversationActions.TryGetValue(action, out long id) ? conversations[id] : null;
    }
}
