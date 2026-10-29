using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed class NeedServiceFailureSnapshot
    {
        public long ServiceId { get; internal set; }
        public long ActionId { get; internal set; }
        public long Tick { get; internal set; }
        public long RetryAt { get; internal set; }
        public FailureReason Reason { get; internal set; }
        internal NeedServiceFailureSnapshot Copy() => (NeedServiceFailureSnapshot)MemberwiseClone();
    }
    public sealed class NeedAgentSnapshot
    {
        public long ActorId { get; internal set; }
        public long NextDecision { get; internal set; } = -1;
        public long RetryAt { get; internal set; }
        public long UrgentRetryAt { get; internal set; }
        public long Backoff { get; internal set; }
        public NeedKind Goal { get; internal set; }
        public long ServiceId { get; internal set; }
        public long ActiveActionId { get; internal set; }
        public long PlannedTick { get; internal set; } = -1;
        public long CompletedActivities { get; internal set; }
        public long SleepFallbackRetryAt { get; internal set; }
        public long ToiletDueTick { get; internal set; } = -1;
        public long ToiletSequence { get; internal set; }
        public long ConversationCursor { get; internal set; }
        public string BlockedReason { get; internal set; } = "";
        public PlanOutcome LastOutcome { get; internal set; }
        public long LastCost { get; internal set; }
        public IReadOnlyList<NeedServiceFailureSnapshot> Failures { get; internal set; } = Array.Empty<NeedServiceFailureSnapshot>();
        internal SortedDictionary<long, NeedServiceFailureSnapshot> KnownFailures = new SortedDictionary<long, NeedServiceFailureSnapshot>();
        internal NeedAgentSnapshot Copy()
        { var copy = (NeedAgentSnapshot)MemberwiseClone(); copy.Failures = KnownFailures.Values.Select(f => f.Copy()).ToArray(); copy.KnownFailures = new SortedDictionary<long, NeedServiceFailureSnapshot>(); return copy; }
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, NeedAgentSnapshot> needAgents = new SortedDictionary<long, NeedAgentSnapshot>();
        private const string UnmetSleepReason = "SleepUnmet/NoKnownUsableBerth";
        private bool IsSleepFallback(NeedKind need, long service) => need == NeedKind.SleepPressure && needServices.TryGetValue(service, out var s) && s.Definition.Kind == NeedServiceKind.RestSpot;
        private long CompleteSleepFallback(Actor actor, long action)
        {
            var memory = needAgents[actor.Id.Value]; memory.SleepFallbackRetryAt = checked(processingTick + memory.Backoff); memory.Backoff = NextWorkBackoff(memory.Backoff);
            Emit("SleepUnmet", actor, new Id<ActionTag>(action), detail: UnmetSleepReason + "; rest completed; retry=" + memory.SleepFallbackRetryAt);
            return memory.SleepFallbackRetryAt;
        }
        public IReadOnlyList<NeedAgentSnapshot> InspectNeedAgents() => needAgents.Values.Select(n => n.Copy()).ToArray();
        private void InitializeNeedAgents(NeedSeed seed)
        {
            foreach (long id in seed.AutonomousActors ?? actors.Keys.Select(a => a.Value).ToArray())
            {
                if (!actors.ContainsKey(new Id<ActorTag>(id))) throw new ArgumentException("Unknown autonomous need actor.");
                var agent = new NeedAgentSnapshot { ActorId = id, Backoff = planner.InitialBackoff }; needAgents.Add(id, agent);
                UpdateToiletGoal(agent);
                if (UrgentNeed(new Id<ActorTag>(id)) != null || agent.ToiletSequence > 0 || OptionalRestOpportunity(new Id<ActorTag>(id)) || ConversationOpportunity(new Id<ActorTag>(id))) RequestNeedDecision(agent, 0);
            }
        }
        private long OwnNeedCustodyFacility(Id<ActorTag> actor)
        {
            var escort = ActiveEscort(actor);
            return escort == null ? -1 : escort.Status == EscortStatus.Detained && EscortPayload(escort).SubjectActorId == actor.Value ? detentionStays[escort.AuthorizationId].FacilityId : 0;
        }
        private NeedValue? UrgentNeed(Id<ActorTag> actor) => NeedValues(actor.Value).Where(v => v.Band >= NeedBand.Urgent).OrderByDescending(v => v.Band).ThenBy(v => v.Kind).FirstOrDefault();
        private bool KnownUrgentOrder(Id<ActorTag> actor)
        {
            if (reportAgents.TryGetValue(actor, out var reports) && reports.Pending.Count > 0)
            {
                // Existing guard/patrol protocols wait for their reporting queue.
                // A locally blocked queue must not prohibit all useful relief.
                if (reports.BlockedReason.Length > 0 && reports.NextDecision > NeedNow) return false;
                if (reports.Pending.Any(p => p.Kind is MessageKind.EscortAcknowledged or MessageKind.EscortOutcome or MessageKind.PatrolAccepted or MessageKind.PatrolFound or MessageKind.PatrolNotLocated or MessageKind.PatrolBlocked or MessageKind.RequestDetention)) return true;
            }
            return GuardHasPriority(actor) || KnownOnDuty(actor) &&
                (localGuardOrders.Values.Any(l => l.ActorId == actor.Value && l.Payload.Expires > NeedNow && l.Status == GuardOrderStatus.AcknowledgmentPending) ||
                 localPatrols.Values.Any(l => l.ActorId == actor.Value && l.Payload.Expires > NeedNow && (l.Status is LocalPatrolStatus.Read or LocalPatrolStatus.AcceptancePending or LocalPatrolStatus.Searching)));
        }
        private bool TravellingForNeed(Actor actor, NeedKind kind) => (actor.Action?.Kind is ActionKind.Move or ActionKind.AllocateBerth or ActionKind.Rest) && needAgents.TryGetValue(actor.Id.Value, out var agent) && agent.ActiveActionId == actor.Action.Id.Value && agent.Goal == kind || TravellingForDailyNeed(actor, kind);
        private void RequestNeedDecision(NeedAgentSnapshot agent, long tick)
        {
            bool urgent = UrgentNeed(new Id<ActorTag>(agent.ActorId)) != null;
            tick = urgent && agent.UrgentRetryAt <= NeedNow ? tick : Math.Max(tick, agent.RetryAt);
            if (urgent && agent.UrgentRetryAt > NeedNow) tick = Math.Min(tick, agent.UrgentRetryAt);
            if (processingTick >= 0 && (tick < processingTick || tick == processingTick && processingPhase >= EventPhase.Decision)) tick = checked(processingTick + 1);
            if (agent.NextDecision >= 0 && agent.NextDecision <= tick) return;
            foreach (var e in scheduled.ForActor(agent.ActorId).Where(e => e.Kind == ScheduledKind.EvaluateNeed).ToArray()) scheduled.Remove(e);
            agent.NextDecision = tick;
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateNeed, Intent = new ActionIntent(new Id<ActorTag>(agent.ActorId), ActionKind.Inspect, default) });
        }
        private void WakeNeed(Actor actor, bool threshold = false)
        {
            if (!needAgents.TryGetValue(actor.Id.Value, out var agent)) return;
            UpdateToiletGoal(agent);
            if (threshold) { agent.RetryAt = 0; agent.UrgentRetryAt = 0; }
            if (actor.Action == null && (UrgentNeed(actor.Id) != null || agent.ToiletSequence > 0 || OptionalRestOpportunity(actor.Id) || ConversationOpportunity(actor.Id))) RequestNeedDecision(agent, NeedNow);
        }
        // All ordinary decision producers consult this before issuing an intent.
        // A failed bounded attempt yields until its private retry clock expires.
        private bool DeferToNeed(Actor actor, bool urgentOrder = false)
        {
            if (!needAgents.TryGetValue(actor.Id.Value, out var agent) || agent.UrgentRetryAt > NeedNow) return false;
            var need = UrgentNeed(actor.Id);
            if (need == null || need.Band != NeedBand.Critical && (urgentOrder || KnownUrgentOrder(actor.Id))) return false;
            RequestNeedDecision(agent, NeedNow); return true;
        }
        private void EvaluateNeed(Id<ActorTag> actorId)
        {
            var agent = needAgents[actorId.Value]; agent.NextDecision = -1; var actor = actors[actorId];
            if (actor.Action != null || agent.PlannedTick == processingTick) return;
            var need = UrgentNeed(actorId);
            if (need == null || agent.UrgentRetryAt > processingTick) { EvaluateLowerNeed(actor, agent); return; }
            if (need.Band != NeedBand.Critical && KnownUrgentOrder(actorId)) { agent.BlockedReason = "KnownUrgentOrder"; return; }
            if (!SpendPlanningRequest()) { RequestNeedDecision(agent, checked(processingTick + 1)); return; }
            var view = ActorView(actorId); var plan = new PlanResult(PlanOutcome.NoPlan); int remaining = planner.NodeBudget;
            // One bounded policy request can reject an unavailable higher need
            // and seek useful relief for another. All searches share its node cap.
            foreach (var candidate in view.Needs.Where(v => v.Band >= NeedBand.Urgent && (v.Band == NeedBand.Critical || !KnownUrgentOrder(actorId))).OrderByDescending(v => v.Band).ThenBy(v => v.Kind))
            {
                need = candidate; agent.Goal = need.Kind;
                plan = NeedPlanning.Plan(view, need.Kind, agent.KnownFailures.Values.Where(f => f.RetryAt > processingTick).Select(f => f.ServiceId), planner.Candidates, planner.MaximumDepth, remaining);
                PlannerExpansions += plan.Expansions; remaining -= plan.Expansions;
                if (plan.Outcome != PlanOutcome.NoPlan) break;
                if (remaining <= 0) { plan = new PlanResult(PlanOutcome.BudgetExhausted); break; }
            }
            bool fallback = false;
            // A real alternative need wins before fallback rest. Rest never satisfies sleep.
            var sleep = view.Needs.FirstOrDefault(v => v.Kind == NeedKind.SleepPressure && v.Band >= NeedBand.Urgent && (v.Band == NeedBand.Critical || !KnownUrgentOrder(actorId)));
            if (plan.Outcome == PlanOutcome.NoPlan && sleep != null && remaining > 0 && agent.SleepFallbackRetryAt <= processingTick)
            {
                need = sleep; agent.Goal = NeedKind.SleepPressure;
                plan = NeedPlanning.Plan(view, NeedKind.WorkStrain, agent.KnownFailures.Values.Where(f => f.RetryAt > processingTick).Select(f => f.ServiceId), planner.Candidates, planner.MaximumDepth, remaining);
                PlannerExpansions += plan.Expansions; fallback = plan.Outcome == PlanOutcome.PlanFound;
            }
            agent.LastOutcome = plan.Outcome; agent.LastCost = plan.Cost;
            if (plan.Outcome == PlanOutcome.PlanFound && plan.Steps.Count > 0)
            {
                PlansFound++; agent.BlockedReason = fallback ? UnmetSleepReason : ""; agent.RetryAt = 0; agent.UrgentRetryAt = 0; agent.PlannedTick = processingTick;
                var step = plan.Steps[0].Intent; agent.ServiceId = step.ServiceId;
                NoteCafeteriaChoice(actor, need.Kind, plan);
                if (fallback) Emit("SleepUnmet", actor, detail: UnmetSleepReason + "; planning rest; service=" + step.ServiceId);
                long cause = Emit("PlanSelected", actor, detail: "urgent-need/v1; need=" + need.Kind + "; band=" + need.Band + "; service=" + step.ServiceId + "; cost=" + plan.Cost + "; " + string.Join(" -> ", plan.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.NeedCommand, Intent = step, CauseId = cause }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; agent.BlockedReason = "PlanBudgetExhausted"; }
            else { PlansUnavailable++; agent.BlockedReason = OwnNeedCustodyFacility(actorId) == 0 ? "CustodyRestricted" : sleep != null ? UnmetSleepReason : "NoKnownReliefRouteOrEligibleService"; }
            agent.RetryAt = checked(processingTick + agent.Backoff);
            foreach (var failure in agent.KnownFailures.Values.Where(f => f.RetryAt > processingTick)) agent.RetryAt = Math.Min(agent.RetryAt, failure.RetryAt);
            agent.Backoff = NextWorkBackoff(agent.Backoff);
            agent.UrgentRetryAt = agent.RetryAt;
            Emit("PlanUnavailable", actor, detail: "urgent-need/v1; " + agent.BlockedReason + "; retryAt=" + agent.RetryAt);
            RequestNeedDecision(agent, agent.RetryAt); WakeAfterNeed(actor);
            if (agent.ToiletSequence > 0 || OptionalRestOpportunity(actor.Id) || ConversationOpportunity(actor.Id))
            { agent.RetryAt = 0; RequestNeedDecision(agent, checked(processingTick + 1)); }
        }
        private void StartNeedIntent(ScheduledEvent command)
        {
            var actor = actors[command.Intent.Actor]; var agent = needAgents[actor.Id.Value];
            if (actor.Action != null) return;
            var need = NeedValues(actor.Id.Value).FirstOrDefault(n => n.Kind == agent.Goal && n.Band >= NeedBand.Seek);
            if (need == null || (need.Band >= NeedBand.Urgent ? need.Band != NeedBand.Critical && KnownUrgentOrder(actor.Id) : !LowerNeedMayStart(actor, agent.Goal))) { WakeNeed(actor); WakeAfterNeed(actor); return; }
            if (GuardJourneyNeedsRelief(actor.Id))
                foreach (var local in localGuardOrders.Values.Where(l => l.ActorId == actor.Id.Value && l.Status == GuardOrderStatus.TargetLocated)) local.Reason = "PreparingForEscort";
            // Sleep inside the known sleep window is that window's sleep, so it is
            // owned by the daily goal and ends at the known wake time.
            var window = command.Intent.Kind == ActionKind.Sleep ? DailyFor(actor.Id.Value).FirstOrDefault(g => g.Kind == RoutineOpportunityKind.Sleep && g.Status == DailyGoalStatus.Pending &&
                g.ActiveActionId == 0 && g.Opens <= NeedNow && NeedNow < g.Closes) : null;
            if (window != null) { window.ActiveActionId = nextAction; window.ServiceId = command.Intent.ServiceId; window.Reason = ""; Start(command.Intent, command.CauseId); return; }
            agent.ActiveActionId = nextAction;
            Start(command.Intent, command.CauseId);
        }
        private void NeedActionEnded(Actor actor, long action, FailureReason failure)
        {
            if (!needAgents.TryGetValue(actor.Id.Value, out var agent) || agent.ActiveActionId != action) return;
            agent.ActiveActionId = 0;
            var conversation = ConversationForAction(action);
            if (conversation != null)
            {
                if (conversation.Status == ConversationStatus.Completed) { agent.CompletedActivities++; agent.Backoff = planner.InitialBackoff; agent.RetryAt = 0; agent.BlockedReason = ""; }
                else { agent.BlockedReason = "ConversationCancelled"; agent.RetryAt = checked(processingTick + agent.Backoff); }
                WakeNeed(actor); return;
            }
            if (failure != FailureReason.None)
            {
                if (agent.Goal == NeedKind.Social) { agent.BlockedReason = failure.ToString(); agent.RetryAt = checked(processingTick + agent.Backoff); agent.Backoff = NextWorkBackoff(agent.Backoff); WakeNeed(actor); return; }
                agent.KnownFailures[agent.ServiceId] = new NeedServiceFailureSnapshot { ServiceId = agent.ServiceId, ActionId = action, Tick = processingTick, RetryAt = checked(processingTick + agent.Backoff), Reason = failure };
                agent.BlockedReason = failure.ToString(); agent.Backoff = NextWorkBackoff(agent.Backoff);
                Emit("NeedServiceFailureLearned", actor, new Id<ActionTag>(action), detail: "service=" + agent.ServiceId + "; " + failure);
            }
            else if (needActivities.TryGetValue(action, out var activity) && activity.Status == NeedActivityStatus.Completed)
            {
                agent.CompletedActivities++; agent.KnownFailures.Remove(agent.ServiceId);
                if (IsSleepFallback(agent.Goal, agent.ServiceId))
                { agent.BlockedReason = UnmetSleepReason; agent.RetryAt = CompleteSleepFallback(actor, action); agent.UrgentRetryAt = agent.RetryAt; WakeNeed(actor); WakeAfterNeed(actor); return; }
                agent.Backoff = planner.InitialBackoff; agent.BlockedReason = "";
            }
            agent.RetryAt = 0; WakeNeed(actor);
        }
        private void WakeAfterNeed(Actor actor)
        {
            WakeRecovery(actor); WakeDaily(actor); WakeGuard(actor); WakePatrol(actor); WakeRoutine(actor); WakeOrderDecisions(actor); WakeAudits(actor);
            if (checkIns.TryGetValue(actor.Id, out var checkIn)) RequestCheckInDecision(checkIn, NeedNow);
            if (reportAgents.TryGetValue(actor.Id, out var reports)) RequestReportDecision(reports, NeedNow);
            if (repairAgents.TryGetValue(actor.Id, out var repairs)) RequestRepairDecision(repairs, NeedNow);
        }
    }
}
