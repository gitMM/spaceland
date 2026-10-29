using System;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteConversations(BinaryWriter w)
        {
            w.Write(Conversations != null); if (Conversations == null) return;
            w.Write(Conversations.DurationTicks); w.Write(Conversations.PartnerCandidates); w.Write(Conversations.FactsPerSpeaker); w.Write(Conversations.ExplicitConsentActors.Count); foreach (long id in Conversations.ExplicitConsentActors) w.Write(id);
            w.Write(ConversationPartnerCandidatesExamined); w.Write(conversations.Count);
            foreach (var c in conversations.Values)
            {
                w.Write(c.Id); w.Write(c.FirstActorId); w.Write(c.SecondActorId); w.Write(c.SecondActionId); WriteCoord(w, c.Position); w.Write((int)c.Topic); w.Write(c.StartedTick); w.Write(c.CompletesTick); w.Write(c.EndedTick); w.Write((int)c.Status);
                w.Write(c.Exchanges.Count); foreach (var e in c.Exchanges) { w.Write(e.SpeakerId); w.Write(e.RecipientId); WriteFact(w, e.Fact); w.Write(e.Learned); }
            }
        }
        private void ReadConversations(BinaryReader r)
        {
            bool enabled = r.ReadBoolean(); if (enabled != (Needs != null)) throw new ArgumentException("Conversation configuration disagrees with needs.");
            if (!enabled)
            {
                if (actors.Values.Any(a => a.Knowledge.Entries.Values.Any(e => e.Fact.SourceKind == EvidenceKind.Heard || e.Fact.SourceActorId != 0))) throw new ArgumentException("Heard evidence requires conversation history.");
                return;
            }
            long duration = r.ReadInt64(); int candidates = r.ReadInt32(), facts = r.ReadInt32(), n = Count(r); var consent = new long[n]; for (int i = 0; i < n; i++) consent[i] = r.ReadInt64();
            InitializeConversations(new ConversationConfig(duration, candidates, facts, consent)); ConversationPartnerCandidatesExamined = r.ReadInt64(); if (ConversationPartnerCandidatesExamined < 0) throw new ArgumentException("Invalid partner search counter.");
            int count = Count(r); var actionIds = new System.Collections.Generic.SortedSet<long>();
            for (int i = 0; i < count; i++)
            {
                var c = new ConversationSnapshot { Id = r.ReadInt64(), FirstActorId = r.ReadInt64(), SecondActorId = r.ReadInt64(), SecondActionId = r.ReadInt64(), Position = ReadCoord(r), Topic = ReadEnum<ConversationTopic>(r), StartedTick = r.ReadInt64(), CompletesTick = r.ReadInt64(), EndedTick = r.ReadInt64(), Status = ReadEnum<ConversationStatus>(r) };
                if (c.Id < 1 || c.SecondActionId <= c.Id || c.SecondActionId >= nextAction || !actionIds.Add(c.Id) || !actionIds.Add(c.SecondActionId) || c.FirstActorId == c.SecondActorId ||
                    !actors.ContainsKey(new Id<ActorTag>(c.FirstActorId)) || !actors.ContainsKey(new Id<ActorTag>(c.SecondActorId)) || !Topology.Contains(c.Position) || c.StartedTick < 0 || c.StartedTick > LastProcessedTick || c.CompletesTick != checked(c.StartedTick + duration) ||
                    (c.Status == ConversationStatus.Active ? c.EndedTick != -1 || c.CompletesTick <= LastProcessedTick : c.EndedTick < c.StartedTick || c.EndedTick > LastProcessedTick || c.EndedTick > c.CompletesTick) || c.Status == ConversationStatus.Completed && c.EndedTick != c.CompletesTick) throw new ArgumentException("Invalid conversation identity or timeline.");
                n = Count(r); if (n > facts * 2 || c.Status != ConversationStatus.Completed && n != 0) throw new ArgumentException("Invalid conversation exchange count.");
                var exchanges = new ConversationExchange[n]; var keys = new System.Collections.Generic.SortedSet<(long, FactKey)>();
                for (int j = 0; j < n; j++)
                {
                    var e = new ConversationExchange { SpeakerId = r.ReadInt64(), RecipientId = r.ReadInt64(), Fact = ReadFact(r), Learned = r.ReadBoolean() };
                    if (!(e.SpeakerId == c.FirstActorId && e.RecipientId == c.SecondActorId || e.SpeakerId == c.SecondActorId && e.RecipientId == c.FirstActorId) || !keys.Add((e.SpeakerId, e.Fact.Key)) ||
                        e.Fact.Id < 1 || e.Fact.Id >= nextFact || e.Fact.ObservedTick > c.EndedTick || e.Fact.LearnedTick > c.EndedTick || !ConversationPolicy.MayShare(e.SpeakerId, c.Topic, e.Fact)) throw new ArgumentException("Invalid conversation disclosure.");
                    exchanges[j] = e;
                }
                if (exchanges.GroupBy(e => e.SpeakerId).Any(g => g.Count() > facts)) throw new ArgumentException("Conversation disclosure exceeds speaker budget."); c.Exchanges = exchanges;
                if (c.Status == ConversationStatus.Active)
                {
                    foreach (long id in new[] { c.FirstActorId, c.SecondActorId })
                    {
                        var actor = actors[new Id<ActorTag>(id)]; var a = actor.Action; long ownAction = id == c.FirstActorId ? c.Id : c.SecondActionId, partner = id == c.FirstActorId ? c.SecondActorId : c.FirstActorId;
                        if (a == null || a.Kind != ActionKind.Converse || a.Id.Value != ownAction || a.TargetActor.Value != partner || a.ConversationTopic != c.Topic || a.Started != c.StartedTick || a.Completes != c.CompletesTick || a.Target != c.Position || actor.Position != c.Position || a.Waiting || ActiveEscort(actor.Id) != null || resourceClaims.Any(x => x.Kind == ClaimKind.Staff && x.ResourceId == id)) throw new ArgumentException("Conversation lacks both coordination claims.");
                        var timers = scheduled.Where(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == ownAction).ToArray();
                        if (timers.Length != (id == c.FirstActorId ? 1 : 0) || timers.Any(e => e.Intent.Actor != actor.Id || e.Tick != c.CompletesTick || e.Phase != EventPhase.PhysicalCompletion)) throw new ArgumentException("Invalid conversation timer.");
                        conversationClaims.Add(id, c.Id);
                    }
                }
                conversations.Add(c.Id, c); conversationActions.Add(c.Id, c.Id); conversationActions.Add(c.SecondActionId, c.Id);
            }
            if (actors.Values.Any(a => a.Action?.Kind == ActionKind.Converse && !conversationClaims.ContainsKey(a.Id.Value))) throw new ArgumentException("Unregistered conversation action.");
            foreach (var actor in actors.Values)
            foreach (var entry in actor.Knowledge.Entries.Values)
            {
                var f = entry.Fact;
                if (f.SourceKind == EvidenceKind.Heard ? f.SourceActorId < 1 || !conversations.TryGetValue(f.SourceId, out var c) || c.Status != ConversationStatus.Completed || f.LearnedTick != c.EndedTick ||
                    !c.Exchanges.Any(e => e.Learned && e.SpeakerId == f.SourceActorId && e.RecipientId == actor.Id.Value && e.Fact.Key.Equals(f.Key) && e.Fact.Value.Equals(f.Value) && e.Fact.ObservedTick == f.ObservedTick) : f.SourceActorId != 0) throw new ArgumentException("Invalid heard fact provenance.");
            }
        }
    }
}
