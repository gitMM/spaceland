using Station.Contracts;

namespace Station.Mind
{
    public static class ConversationPolicy
    {
        public static PlanResult Plan(IActorView view, System.Collections.Generic.IEnumerable<long> candidates, long duration, int depth, int budget)
        {
            if (!view.CurrentTile.HasValue) return new PlanResult(PlanOutcome.NoPlan);
            var steps = System.Linq.Enumerable.Select(candidates, id => new SymbolicAction("Converse:" + id, 0, 1, 1, 0, duration,
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
                         key.SubjectKind == SubjectKind.Tile && key.Attribute == AttributeKind.Dirt && fact.Value.Kind == FactValueKind.Number && fact.Value.Number > 0 ||
                         // An empty counter or a queue the speaker saw; the listener keeps its age and source.
                         key.SubjectKind == SubjectKind.Service && key.Attribute == AttributeKind.Quantity && fact.Value.Kind == FactValueKind.Number && fact.Value.Number == 0 ||
                         key.SubjectKind == SubjectKind.Service && key.Attribute == AttributeKind.EstimateTicks && fact.Value.Kind == FactValueKind.Number && fact.Value.Number > 0);
                case ConversationTopic.AllowedRequest:
                    return key.SubjectKind == SubjectKind.Machine && key.Attribute == AttributeKind.RequiredResource && fact.Value.Kind == FactValueKind.Text;
                default: return false;
            }
        }
    }
}
