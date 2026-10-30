using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class ConversationPolicy
    {
        private static readonly PriorityGate[] Consent = { PriorityGate.Inactive, PriorityGate.CommittedAction, PriorityGate.EscortControls, PriorityGate.StaffClaimed, PriorityGate.CriticalNeed,
            PriorityGate.KnownUrgentOrder, PriorityGate.UrgentNeed, PriorityGate.Recovery, PriorityGate.DueGoalSelected, PriorityGate.OrdinaryWork };
        // Conversation happens off duty, by the known roster.
        public static bool OffDuty(RosterPayload? knownRoster, long now) => knownRoster != null && !knownRoster.OnDuty(now);
        // Initiating needs social pressure while off duty.
        public static bool Opportunity(bool socialAtSeek, Func<bool> offDuty) => socialAtSeek && offDuty();
        // Checks before an idle citizen consents to an invitation; any that applies declines.
        public static IReadOnlyList<PriorityGate> ConsentGates => Consent;
        // Initiating needs social pressure; consenting does not. An idle off-duty citizen
        // can accept an invitation even after their own need was relieved.
        public static bool Consents(bool explicitConsent, bool hasNeedAgent, Func<bool> offDuty) => explicitConsent || hasNeedAgent && offDuty();
        // Local partners to invite, rotating after the last examined candidate. Both views
        // are ordered tree ranges; at most the configured number are visited.
        public static IReadOnlyList<long> PartnerCandidates(SortedSet<long> local, long self, long after, int limit)
        {
            var later = after == long.MaxValue ? Enumerable.Empty<long>() : local.GetViewBetween(after + 1, long.MaxValue);
            return later.Concat(local.GetViewBetween(0, after)).Where(id => id != self).Take(limit).ToArray();
        }
        // Facts a speaker shares on the topic, in key order, bounded per speaker.
        public static IEnumerable<KnowledgeFact> SharedFacts(long speaker, ConversationTopic topic, IEnumerable<KnowledgeFact> known, int factsPerSpeaker) =>
            known.Where(f => MayShare(speaker, topic, f)).OrderBy(f => f.Key).Take(factsPerSpeaker);
        public static PlanResult Plan(IActorView view, IEnumerable<long> candidates, long duration, int depth, int budget)
        {
            if (!view.CurrentTile.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            var steps = Enumerable.Select(candidates, id => new SymbolicAction("Converse:" + id, 0, 1, 1, 0, duration,
                new ActionIntent(view.Actor, ActionKind.Converse, view.CurrentTile.Value, targetActor: new Id<ActorTag>(id))));
            return GoapPlanner.Search(new PlanningProblem(0, 1, new[] { "ConversationCompleted" }, steps), depth, budget);
        }
        public static bool MayShare(long speaker, ConversationTopic topic, KnowledgeFact fact)
        {
            var key = fact.Key;
            switch (topic)
            {
                case ConversationTopic.Routine:
                    return key.SubjectKind == SubjectKind.Actor && key.SubjectId == speaker && (key.Attribute is AttributeKind.Cohort or AttributeKind.RestTile);
                case ConversationTopic.WorkStatus:
                    return (key.SubjectKind is SubjectKind.Job or SubjectKind.Attempt) && key.Attribute == AttributeKind.WorkStatus;
                case ConversationTopic.WitnessedProblem:
                    return (fact.SourceKind is EvidenceKind.Direct or EvidenceKind.Inspection) &&
                        (key.SubjectKind == SubjectKind.Machine && key.Attribute == AttributeKind.Operational && fact.Value.Equals(FactValue.Boolean(false)) ||
                         key.SubjectKind == SubjectKind.Tile && key.Attribute == AttributeKind.Dirt && fact.Value.Kind == FactValueKind.Number && fact.Value.Number > 0);
                case ConversationTopic.AllowedRequest:
                    return key.SubjectKind == SubjectKind.Machine && key.Attribute == AttributeKind.RequiredResource && fact.Value.Kind == FactValueKind.Text;
                default: return false;
            }
        }
    }
}
