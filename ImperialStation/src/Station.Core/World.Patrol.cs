using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<long, PatrolOrderSnapshot> patrolOrders = new SortedDictionary<long, PatrolOrderSnapshot>();
        private readonly SortedDictionary<long, LocalPatrolSnapshot> localPatrols = new SortedDictionary<long, LocalPatrolSnapshot>();
        public IReadOnlyList<PatrolOrderSnapshot> InspectPatrolOrders() => patrolOrders.Values.Select(p => p.Copy()).ToArray();
        public IReadOnlyList<LocalPatrolSnapshot> InspectLocalPatrols(Id<ActorTag> actor) => localPatrols.Values.Where(p => p.ActorId == actor.Value).Select(p => p.Copy()).ToArray();
        private IReadOnlyList<Coord> PatrolCandidates(MissingReportIncident incident, IAuthorityView view)
        {
            var person = view.Personnel.Single(p => p.ActorId == incident.ActorId);
            var origin = Candidate(person).KnownOrigin;
            return PatrolPlanning.SearchSites(origin, IncidentPolicy.JobSite(view, JobTarget(jobs[incident.JobId])), person.Roster?.RestTile);
        }
        private long SelectPatrol(MissingReportIncident incident, IAuthorityView view, IReadOnlyList<Coord> candidates, IEnumerable<DispatchCandidate>? available = null)
        {
            return AuthorityDispatch.Patrol(view, incident.ActorId, Authority!.PatrolRole, candidates, available ?? Candidates(view));
        }
        private void DispatchPatrol(MissingReportIncident incident, long actor, IReadOnlyList<Coord> candidates)
        {
            var payload = new PatrolOrderPayload(incident.Id, new Id<ActorTag>(incident.ActorId), checked(processingTick + Authority!.ResponseOrderTicks), Authority.PatrolRole, candidates);
            long id = nextMessage;
            long posted = PostAuthorityWork(new MessageDraft(MessageKind.PatrolOrder, RecipientKind.Account, new Id<AccountTag>(registrations[actor].AccountId),
                new FactKey(SubjectKind.Order, id, AttributeKind.PatrolStatus), new FactValue(FactValueKind.Text, text: "EstablishWorkerStatus"), processingTick,
                taskId: incident.JobId, operationKey: "incident/" + incident.Id + "/patrol", patrolOrder: payload), incident.OpeningEventId);
            patrolOrders.Add(posted, new PatrolOrderSnapshot { OrderMessageId = posted, ActorId = actor, Payload = payload });
            incident.PatrolOrderMessageId = posted; incident.WorkerStatusResponse = "PatrolPosted"; incident.PatrolReason = "";
            RecordIncidentResponse(incident);
        }
        private void ReadPatrolOrder(Actor actor, MessageSnapshot message)
        {
            var payload = message.Draft.PatrolOrder;
            if (message.Draft.Kind != MessageKind.PatrolOrder || message.SenderAccount.Value != 0 || payload == null || localPatrols.ContainsKey(message.Id)) return;
            var order = new LocalPatrolSnapshot { OrderMessageId = message.Id, ActorId = actor.Id.Value, Payload = payload, ReadTick = processingTick, Backoff = planner.InitialBackoff };
            localPatrols.Add(message.Id, order);
            if (payload.Expires <= processingTick) { QueuePatrolResult(order, MessageKind.PatrolBlocked, new FactValue(FactValueKind.Text, text: "KnownOrderExpired"), processingTick); return; }
            Schedule(new ScheduledEvent { Tick = payload.Expires, Phase = EventPhase.Decision, Kind = ScheduledKind.ExpirePatrol, Value = message.Id });
            RequestPatrolDecision(order, processingTick);
        }
        private void RequestPatrolDecision(LocalPatrolSnapshot order, long tick)
        {
            if (!(order.Status is LocalPatrolStatus.Read or LocalPatrolStatus.Searching)) return;
            if (processingPhase >= EventPhase.Decision && tick <= processingTick) tick = checked(processingTick + 1);
            if (order.NextDecision >= 0 && order.NextDecision <= tick) return;
            foreach (var old in scheduled.Where(e => e.Kind == ScheduledKind.EvaluatePatrol && e.Value == order.OrderMessageId).ToArray()) scheduled.Remove(old);
            order.NextDecision = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluatePatrol, Value = order.OrderMessageId });
        }
        private void WakePatrol(Actor actor, bool failed = false)
        {
            if (actor.Action != null) return;
            foreach (var order in localPatrols.Values.Where(p => p.ActorId == actor.Id.Value)) RequestPatrolDecision(order, failed ? checked(processingTick + order.Backoff) : processingTick);
        }
        private void EvaluatePatrol(long messageId)
        {
            var order = localPatrols[messageId]; order.NextDecision = -1; var actor = actors[new Id<ActorTag>(order.ActorId)];
            if (!(order.Status is LocalPatrolStatus.Read or LocalPatrolStatus.Searching)) return;
            if (processingTick >= order.Payload.Expires) { ExpirePatrol(messageId); return; }
            if (EscortControls(actor.Id) || actor.Action != null || DeferToNeed(actor, true) || !KnownOnDuty(actor.Id) || reportAgents.TryGetValue(actor.Id, out var report) && report.Pending.Count > 0 ||
                !needAgents.ContainsKey(actor.Id.Value) && checkIns.TryGetValue(actor.Id, out var checkIn) && (checkIn.Due || checkIn.DueTick == processingTick)) return;
            var view = ActorView(actor.Id);
            var gate = PatrolPlanning.OrderGate(view, order.Payload, order.Status == LocalPatrolStatus.Read);
            if (gate == PatrolOrderGate.NotQualified) { QueuePatrolResult(order, MessageKind.PatrolBlocked, new FactValue(FactValueKind.Text, text: "NotQualified"), processingTick); return; }
            if (gate == PatrolOrderGate.Accept)
            {
                order.Status = LocalPatrolStatus.AcceptancePending;
                QueuePatrolReport(order, MessageKind.PatrolAccepted, new FactValue(FactValueKind.Text, text: "Accepted"), processingTick); return;
            }
            if (!SpendPlanningRequest()) { RequestPatrolDecision(order, checked(processingTick + 1)); return; }
            var plan = PatrolPlanning.InspectCandidate(view, order.Payload.Candidates[order.CandidateIndex], order.Payload.TargetActor, Information!.InspectTicks, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += plan.Expansions;
            if (plan.Outcome == PlanOutcome.BudgetExhausted)
            { PlansExhausted++; RequestPatrolDecision(order, checked(processingTick + order.Backoff)); order.Backoff = NextWorkBackoff(order.Backoff); return; }
            if (plan.Outcome != PlanOutcome.PlanFound || plan.Steps.Count == 0)
            { PlansUnavailable++; QueuePatrolResult(order, MessageKind.PatrolBlocked, new FactValue(FactValueKind.Text, text: "NoKnownSearchRoute"), processingTick); return; }
            PlansFound++; order.Backoff = planner.InitialBackoff;
            var step = plan.Steps[0].Intent;
            long cause = Emit("PlanSelected", actor, messageId: messageId, detail: "patrol-search/v1; candidate=" + order.CandidateIndex + "; cost=" + plan.Cost);
            Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command,
                Intent = new ActionIntent(step.Actor, step.Kind, step.Target, targetActor: step.TargetActor, patrolOrderId: messageId), CauseId = cause });
        }
        private bool CanExecutePatrol(Actor actor, long orderId) => !HasCriticalNeed(actor.Id) && localPatrols.TryGetValue(orderId, out var local) && local.ActorId == actor.Id.Value && local.Status == LocalPatrolStatus.Searching && processingTick < local.Payload.Expires;
        private void PatrolObserved(Actor actor, ActiveAction action, FactValue value)
        {
            if (action.PatrolOrderId == 0 || !localPatrols.TryGetValue(action.PatrolOrderId, out var order) || order.Status != LocalPatrolStatus.Searching ||
                order.ActorId != actor.Id.Value || action.TargetActor != order.Payload.TargetActor || action.Target != order.Payload.Candidates[order.CandidateIndex]) return;
            bool found = PatrolPlanning.Found(value);
            order.Observations = order.Observations.Concat(new[] { new PatrolObservation(processingTick, action.Id.Value, action.Target, found) }).ToArray();
            order.CandidateIndex++;
            Emit("PatrolCandidateInspected", actor, action.Id, messageId: order.OrderMessageId, target: action.Target, detail: found ? "FoundHere" : "AbsentHere", causeId: action.CauseId);
            if (PatrolPlanning.SearchResult(found, order.CandidateIndex, order.Payload.Candidates.Count) is MessageKind result) QueuePatrolResult(order, result, value, processingTick);
        }
        private void QueuePatrolReport(LocalPatrolSnapshot order, MessageKind kind, FactValue claim, long observed)
        {
            var actorId = new Id<ActorTag>(order.ActorId);
            var subject = kind == MessageKind.PatrolAccepted ? new FactKey(SubjectKind.Order, order.OrderMessageId, AttributeKind.PatrolStatus) : new FactKey(SubjectKind.Actor, order.Payload.TargetActor.Value, AttributeKind.Location);
            var draft = new MessageDraft(kind, RecipientKind.Authority, default, subject, claim, observed, relatedMessageId: order.OrderMessageId, operationKey: "patrol/" + order.OrderMessageId + "/" + kind);
            if (!reportAgents.TryGetValue(actorId, out var reports)) { reports = new ReportAgent { Actor = actorId, Backoff = planner.InitialBackoff }; reportAgents.Add(actorId, reports); }
            if (!reports.Pending.Any(p => p.OperationKey == draft.OperationKey)) QueueReport(reports, draft);
            RequestReportDecision(reports, processingTick);
        }
        private void QueuePatrolResult(LocalPatrolSnapshot order, MessageKind kind, FactValue claim, long observed)
        {
            if (order.Status is LocalPatrolStatus.ResultPending or LocalPatrolStatus.Completed) return;
            order.Status = LocalPatrolStatus.ResultPending; order.Reason = string.IsNullOrEmpty(claim.Text) ? kind.ToString() : claim.Text;
            RemovePatrolDecision(order); QueuePatrolReport(order, kind, claim, observed); TriggerWorkCheckIn(new Id<ActorTag>(order.ActorId));
        }
        private void RemovePatrolDecision(LocalPatrolSnapshot order)
        {
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluatePatrol && e.Value == order.OrderMessageId).ToArray()) scheduled.Remove(e);
            order.NextDecision = -1;
        }
        private void ExpirePatrol(long id)
        {
            var order = localPatrols[id]; if (order.Status is LocalPatrolStatus.Completed or LocalPatrolStatus.ResultPending) return;
            var actor = actors[new Id<ActorTag>(order.ActorId)];
            if (reportAgents.TryGetValue(actor.Id, out var report)) RemoveReports(report, p => p.Kind == MessageKind.PatrolAccepted && p.RelatedMessageId == id);
            if (actor.Action?.PatrolOrderId == id && actor.Action.Kind == ActionKind.Move) actor.Action.CancelAtBoundary = true;
            QueuePatrolResult(order, MessageKind.PatrolBlocked, new FactValue(FactValueKind.Text, text: "KnownOrderExpired"), processingTick);
        }
        private void PatrolMessagePosted(Actor actor, MessageSnapshot message)
        {
            if (!localPatrols.TryGetValue(message.Draft.RelatedMessageId, out var order) || order.ActorId != actor.Id.Value) return;
            if (message.Draft.Kind == MessageKind.PatrolAccepted && order.Status == LocalPatrolStatus.AcceptancePending)
            { order.Status = LocalPatrolStatus.Searching; RequestPatrolDecision(order, processingTick); }
            else if (IsPatrolResult(message.Draft.Kind) && order.Status == LocalPatrolStatus.ResultPending)
            {
                order.Status = LocalPatrolStatus.Completed;
                foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.ExpirePatrol && e.Value == order.OrderMessageId).ToArray()) scheduled.Remove(e);
                var key = new FactKey(SubjectKind.Order, order.OrderMessageId, AttributeKind.PatrolStatus);
                if (actor.Knowledge.Entries.TryGetValue(key, out var entry)) { var f = entry.Fact; entry.Fact = new KnowledgeFact(f.Id, f.Key, f.Value, f.SourceKind, f.SourceId, f.ObservedTick, f.LearnedTick, false, f.SourceActorId); }
            }
        }
        private static bool IsPatrolResult(MessageKind kind) => kind is MessageKind.PatrolFound or MessageKind.PatrolNotLocated or MessageKind.PatrolBlocked;
        private bool ReceivePatrolReport(MessageSnapshot message)
        {
            var draft = message.Draft; if (draft.Kind != MessageKind.PatrolAccepted && !IsPatrolResult(draft.Kind)) return true;
            if (!patrolOrders.TryGetValue(draft.RelatedMessageId, out var order) || registrations[order.ActorId].AccountId != message.SenderAccount.Value)
            { Emit("PatrolReportRejected", messageId: message.Id, detail: "OrderOrAccountMismatch"); return false; }
            if (draft.Kind == MessageKind.PatrolAccepted)
            {
                if (!draft.Subject.Equals(new FactKey(SubjectKind.Order, order.OrderMessageId, AttributeKind.PatrolStatus)) || draft.Claim.Kind != FactValueKind.Text) return false;
                if (order.AcknowledgmentMessageId != 0 || order.ResultMessageId != 0) return false;
                order.AcknowledgmentMessageId = message.Id; order.Status = "Accepted"; return true;
            }
            if (order.ResultMessageId != 0 || !draft.Subject.Equals(new FactKey(SubjectKind.Actor, order.Payload.TargetActor.Value, AttributeKind.Location)) ||
                draft.Kind != MessageKind.PatrolBlocked && order.AcknowledgmentMessageId == 0 ||
                draft.Kind == MessageKind.PatrolFound && draft.Claim.Kind != FactValueKind.Coordinate || draft.Kind == MessageKind.PatrolNotLocated && draft.Claim.Kind != FactValueKind.AbsentAt ||
                draft.Kind == MessageKind.PatrolBlocked && draft.Claim.Kind != FactValueKind.Text) return false;
            order.ResultMessageId = message.Id; order.Status = draft.Kind == MessageKind.PatrolFound ? "ReportedFound" : draft.Kind == MessageKind.PatrolNotLocated ? "ReportedNotLocated" : "ReportedBlocked";
            var incident = missingReports.Values.Single(i => i.Id == order.Payload.IncidentId);
            incident.PatrolResultMessageId = message.Id; incident.WorkerStatusResponse = order.Status;
            RecordIncidentResponse(incident);
            if (draft.Kind != MessageKind.PatrolBlocked) ReceiveBelief(draft.Subject, draft.Claim, draft.ObservedTick, message.Id, 0, 0, 0);
            Emit("PatrolResultReceived", messageId: message.Id, detail: "incident=" + incident.Id + "; " + order.Status);
            return true;
        }
    }
}
