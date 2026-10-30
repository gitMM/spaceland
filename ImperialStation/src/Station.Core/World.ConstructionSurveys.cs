using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed class ConstructionSurveySnapshot
    {
        public long ActionId { get; internal set; }
        public long ActorId { get; internal set; }
        public long ProjectId { get; internal set; }
        public long ObservedTick { get; internal set; }
        public long UsableBerths { get; internal set; }
        public long CauseId { get; internal set; }
        internal ConstructionSurveySnapshot Copy() => (ConstructionSurveySnapshot)MemberwiseClone();
    }
    public sealed partial class World
    {
        private readonly List<ConstructionSurveySnapshot> constructionSurveys = new List<ConstructionSurveySnapshot>();
        private readonly List<long> acceptedConstructionSurveys = new List<long>();
        public IReadOnlyList<ConstructionSurveySnapshot> InspectConstructionSurveys() => Array.AsReadOnly(constructionSurveys.Select(s => s.Copy()).ToArray());
        private static FactKey ConstructionCapacityKey(long project) => new FactKey(SubjectKind.ConstructionProject, project, AttributeKind.UsableBerths);
        private static string SurveyKey(ConstructionSurveySnapshot s) => "construction-survey/" + s.ProjectId + "/" + s.ActionId;
        private MessageDraft SurveyDraft(ConstructionSurveySnapshot s) => new MessageDraft(MessageKind.ConstructionSurvey, RecipientKind.Authority, default,
            ConstructionCapacityKey(s.ProjectId), FactValue.Integer(s.UsableBerths), s.ObservedTick, operationKey: SurveyKey(s));
        private long ConstructionSurveyCause(MessageDraft d) => constructionSurveys.FirstOrDefault(s => d.OperationKey == SurveyKey(s) && SameDraft(d, SurveyDraft(s)))?.CauseId ?? 0;
        private FailureReason ValidateConstructionSurvey(Actor actor, ActiveAction action)
        {
            if (Information == null || Growth == null) return FailureReason.RequiredServiceNotPresent;
            if (!growthCommitments.ContainsKey(action.ServiceId)) return FailureReason.UnknownRequiredFact;
            return actor.Position != action.Target || actor.Position.Manhattan(constructionProjects[action.ServiceId].Site) > 1 ? FailureReason.NotAtRequiredTile : FailureReason.None;
        }
        private void StartConstructionSurvey(Actor actor, ActiveAction action)
        {
            var failure = ValidateConstructionSurvey(actor, action);
            if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            action.Completes = checked(processingTick + Information!.InspectTicks);
            actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, target: actor.Position, arrival: action.Completes, detail: action.Kind.ToString());
            ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteConstructionSurvey(Actor actor, ActiveAction action)
        {
            var failure = ValidateConstructionSurvey(actor, action);
            if (failure != FailureReason.None) { actor.Action = null; Fail(actor, action.Id, failure); return; }
            var p = constructionProjects[action.ServiceId];
            bool present = Topology.Tiles.TryGetValue(p.Site, out var tile) && tile.Completed && tile.Id.Value == p.TileId;
            long count = present ? p.BerthIds.Count(id => needServices.TryGetValue(id, out var service) && service.Enabled && service.Definition.Kind == NeedServiceKind.Bed) : 0;
            if (!Learn(actor, ConstructionCapacityKey(p.Id), FactValue.Integer(count), EvidenceKind.Inspection, action.Id.Value, processingTick, action.CauseId))
            { actor.Action = null; Fail(actor, action.Id, FailureReason.KnowledgeCapacity); return; }
            var survey = new ConstructionSurveySnapshot { ActionId = action.Id.Value, ActorId = actor.Id.Value, ProjectId = p.Id, ObservedTick = processingTick, UsableBerths = count };
            survey.CauseId = Emit("ConstructionSurveyCompleted", actor, action.Id, target: p.Site, detail: "project=" + p.Id + "; usable=" + count, causeId: action.CauseId);
            constructionSurveys.Add(survey);
            if (!reportAgents.TryGetValue(actor.Id, out var agent)) { agent = new ReportAgent { Actor = actor.Id, Backoff = planner.InitialBackoff }; reportAgents.Add(actor.Id, agent); }
            QueueReport(agent, SurveyDraft(survey)); RequestReportDecision(agent, processingTick);
            Finish(actor);
        }
        private static bool ValidConstructionSurveyDraft(MessageDraft d) => d.Kind != MessageKind.ConstructionSurvey ||
            d.RecipientKind == RecipientKind.Authority && d.Recipient.Value == 0 && d.Subject.SubjectKind == SubjectKind.ConstructionProject && d.Subject.SubjectId > 0 &&
            d.Subject.Attribute == AttributeKind.UsableBerths && d.Claim.Kind == FactValueKind.Number && d.Claim.Number >= 0 && d.ObservedTick >= 0 &&
            d.TaskId == 0 && d.AttemptId == 0 && d.RelatedMessageId == 0 && !string.IsNullOrWhiteSpace(d.OperationKey) && d.WorkOrder == null && d.PatrolOrder == null && d.Roster == null;
        private string ConstructionSurveyRejection(MessageSnapshot m)
        {
            var d = m.Draft;
            if (!ValidConstructionSurveyDraft(d)) return "InvalidClaim";
            if (m.Seeded || m.SessionId < 1 || m.TerminalId < 1 || !registrations.Values.Any(p => p.AccountId == m.SenderAccount.Value)) return "UnauthenticatedReport";
            if (!growthCommitments.TryGetValue(d.Subject.SubjectId, out var c)) return "UnknownProject";
            return SurveyEvidencePolicy.Admissibility(c.AcceptedReportId != 0, c.AcceptedTick, c.Berths, c.CommittedTick, m.DeliveryTick, d.Claim.Number, d.ObservedTick, m.SubmittedTick);
        }
        private bool ReceiveConstructionSurvey(MessageSnapshot m, long cause)
        {
            if (m.Draft.Kind != MessageKind.ConstructionSurvey) return true;
            var prior = acceptedConstructionSurveys.Select(id => messages[id]).FirstOrDefault(old => old.SenderAccount == m.SenderAccount && old.Draft.OperationKey == m.Draft.OperationKey);
            if (prior != null)
            {
                Emit(SurveyEvidencePolicy.DuplicateRetry(SameDraft(prior.Draft, m.Draft)) ? "ConstructionSurveyDuplicate" : "ConstructionSurveyRejected", messageId: m.Id, causeId: cause, detail: "OperationKeyAlreadyUsed"); return false;
            }
            string reason = ConstructionSurveyRejection(m);
            if (reason.Length > 0) { Emit("ConstructionSurveyRejected", messageId: m.Id, causeId: cause, detail: reason); return false; }
            var c = growthCommitments[m.Draft.Subject.SubjectId]; long previous = c.BelievedBerths;
            // Last received admissible evidence wins, as for other authority beliefs. Never inspect physics here.
            growthState!.BelievedCompletedBerths = SurveyEvidencePolicy.CorrectedBelief(growthState.BelievedCompletedBerths, previous, m.Draft.Claim.Number);
            c.BelievedBerths = m.Draft.Claim.Number; c.CapacityEvidenceMessageId = m.Id; acceptedConstructionSurveys.Add(m.Id);
            Emit("ConstructionCapacityCorrected", messageId: m.Id, causeId: cause, detail: "project=" + c.ProjectId + "; previous=" + previous + "; usable=" + c.BelievedBerths + "; believed=" + growthState.BelievedCompletedBerths);
            RequestGrowthReview(processingTick); return true;
        }
        private void WriteConstructionSurveys(BinaryWriter w)
        {
            w.Write(constructionSurveys.Count);
            foreach (var s in constructionSurveys) { w.Write(s.ActionId); w.Write(s.ActorId); w.Write(s.ProjectId); w.Write(s.ObservedTick); w.Write(s.UsableBerths); w.Write(s.CauseId); }
            w.Write(acceptedConstructionSurveys.Count); foreach (long id in acceptedConstructionSurveys) w.Write(id);
        }
        private void ReadConstructionSurveys(BinaryReader r)
        {
            int n = Count(r);
            for (int i = 0; i < n; i++) constructionSurveys.Add(new ConstructionSurveySnapshot { ActionId = r.ReadInt64(), ActorId = r.ReadInt64(), ProjectId = r.ReadInt64(), ObservedTick = r.ReadInt64(), UsableBerths = r.ReadInt64(), CauseId = r.ReadInt64() });
            n = Count(r); for (int i = 0; i < n; i++) acceptedConstructionSurveys.Add(r.ReadInt64());
        }
        private void ValidateConstructionSurveys()
        {
            var seen = new SortedSet<string>(StringComparer.Ordinal); long lastTick = -1;
            foreach (long id in acceptedConstructionSurveys)
            {
                if (!messages.TryGetValue(id, out var m) || m.Draft.Kind != MessageKind.ConstructionSurvey || m.Stage != MessageStage.Accepted ||
                    m.DeliveryTick < lastTick || m.DeliveryTick > LastProcessedTick || ConstructionSurveyRejection(m).Length > 0 || !seen.Add(m.SenderAccount.Value + "/" + m.Draft.OperationKey))
                    throw new ArgumentException("Invalid accepted construction survey evidence.");
                lastTick = m.DeliveryTick;
            }
            foreach (var c in growthCommitments.Values)
            {
                long last = acceptedConstructionSurveys.LastOrDefault(id => messages[id].Draft.Subject.SubjectId == c.ProjectId);
                long evidence = last == 0 ? c.AcceptedReportId : last;
                long capacity = last == 0 ? (c.AcceptedReportId == 0 ? 0 : c.Berths) : messages[last].Draft.Claim.Number;
                if (c.CapacityEvidenceMessageId != evidence || c.BelievedBerths != capacity) throw new ArgumentException("Construction capacity differs from its last accepted evidence.");
            }
            if (constructionSurveys.Select(s => s.ActionId).Distinct().Count() != constructionSurveys.Count || constructionSurveys.Select(s => s.CauseId).Distinct().Count() != constructionSurveys.Count)
                throw new ArgumentException("Duplicate local construction survey.");
            lastTick = -1;
            foreach (var s in constructionSurveys)
            {
                if (s.ActionId < 1 || s.ActionId >= nextAction || s.CauseId < 1 || s.CauseId >= nextTrace || !actors.ContainsKey(new Id<ActorTag>(s.ActorId)) ||
                    !growthCommitments.TryGetValue(s.ProjectId, out var c) || s.UsableBerths < 0 || s.UsableBerths > c.Berths || s.ObservedTick < c.CommittedTick || s.ObservedTick < lastTick || s.ObservedTick > LastProcessedTick)
                    throw new ArgumentException("Invalid local construction survey.");
                lastTick = s.ObservedTick;
            }
            foreach (var actor in actors.Values.Where(a => a.Action?.Kind == ActionKind.InspectConstruction))
            {
                var a = actor.Action!;
                if (ValidateConstructionSurvey(actor, a) != FailureReason.None || a.Waiting || a.Completes != checked(a.Started + Information!.InspectTicks) ||
                    constructionSurveys.Any(s => s.ActionId == a.Id.Value)) throw new ArgumentException("Invalid active construction inspection.");
            }
        }
    }
}
