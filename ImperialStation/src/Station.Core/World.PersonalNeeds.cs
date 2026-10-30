using System;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private bool AtSeek(Id<ActorTag> actor, NeedKind kind) => NeedValues(actor.Value).Any(n => n.Kind == kind && n.Band >= NeedBand.Seek);
        private void UpdateToiletGoal(NeedAgentSnapshot agent)
        {
            var actor = new Id<ActorTag>(agent.ActorId);
            if (AtSeek(actor, NeedKind.Bladder))
            {
                if (agent.ToiletSequence != 0) return;
                var stamp = NewGoalStamp(NeedNow); agent.ToiletDueTick = stamp.Due; agent.ToiletSequence = stamp.Sequence;
                if (processingTick >= 0) Emit("PersonalNeedDue", actors[actor], detail: "Bladder; due=" + stamp.Due + "; sequence=" + stamp.Sequence);
            }
            else { agent.ToiletDueTick = -1; agent.ToiletSequence = 0; }
        }
        private bool OptionalRestOpportunity(Id<ActorTag> actor)
        {
            if (!AtSeek(actor, NeedKind.WorkStrain) || !routines.TryGetValue(actor, out var routine) || routine.KnownRoster == null) return false;
            return !routine.KnownRoster.OnDuty(NeedNow) || DailyRoutine.Around(routine.KnownRoster, NeedNow).Any(w => w.Kind == RoutineOpportunityKind.ScheduledRest && w.Opens <= NeedNow && NeedNow < w.Closes);
        }
        private bool OrdinaryWorkReady(Actor actor)
        {
            if (!KnownOnDuty(actor.Id)) return false;
            if (repairAgents.TryGetValue(actor.Id, out var repairs) && repairs.Pending.Count > 0 && (repairs.BlockedReason.Length == 0 || repairs.NextDecision <= NeedNow)) return true;
            return localOrders.Values.Any(o => o.ActorId == actor.Id.Value && (o.Status is LocalOrderStatus.Read or LocalOrderStatus.Working)) || audits.Values.Any(a => a.ActorId == actor.Id.Value && a.Pending);
        }
        private bool DeferToOptionalRest(Actor actor)
        {
            if (!needAgents.TryGetValue(actor.Id.Value, out var agent) || agent.RetryAt > NeedNow || !(OptionalRestOpportunity(actor.Id) || ConversationOpportunity(actor.Id)) || KnownUrgentOrder(actor.Id) || OrdinaryWorkReady(actor)) return false;
            RequestNeedDecision(agent, NeedNow); return true;
        }
        private bool LowerNeedMayStart(Actor actor, NeedKind goal)
        {
            // Pre-sleep relief deliberately precedes urgent (never critical) sleep pressure.
            if (PreSleepRelief(actor.Id) == goal && !HasCriticalNeed(actor.Id)) return !KnownUrgentOrder(actor.Id) && !DeferToRecovery(actor);
            if (KnownUrgentOrder(actor.Id) || DeferToNeed(actor) || DeferToRecovery(actor)) return false;
            var due = SelectedDueGoal(actor);
            if (goal == NeedKind.Bladder) return AtSeek(actor.Id, goal) && due.Source == DueSource.Toilet;
            return (goal == NeedKind.WorkStrain && OptionalRestOpportunity(actor.Id) || goal == NeedKind.Social && ConversationOpportunity(actor.Id)) && due.Source == DueSource.None && !OrdinaryWorkReady(actor);
        }
        private void EvaluateLowerNeed(Actor actor, NeedAgentSnapshot agent)
        {
            var due = SelectedDueGoal(actor); var preSleep = PreSleepRelief(actor.Id);
            NeedKind goal = preSleep ?? (due.Source == DueSource.Toilet ? NeedKind.Bladder : OptionalRestOpportunity(actor.Id) ? NeedKind.WorkStrain : NeedKind.Social);
            if (!LowerNeedMayStart(actor, goal))
            {
                if (UrgentNeed(actor.Id) != null && agent.UrgentRetryAt > processingTick) RequestNeedDecision(agent, agent.UrgentRetryAt);
                WakeAfterNeed(actor); return;
            }
            if (!SpendPlanningRequest()) { RequestNeedDecision(agent, checked(processingTick + 1)); return; }
            agent.Goal = goal;
            PlanResult plan;
            if (goal == NeedKind.Social)
            {
                var partners = new System.Collections.Generic.List<long>();
                foreach (long id in LocalConversationCandidates(actor, agent.ConversationCursor))
                {
                    ConversationPartnerCandidatesExamined++; agent.ConversationCursor = id;
                    var other = actors[new Id<ActorTag>(id)]; if (other.Action?.Kind != ActionKind.Move && ConversationConsents(other)) partners.Add(id);
                }
                plan = ConversationPolicy.Plan(ActorView(actor.Id), partners, Conversations!.DurationTicks, planner.MaximumDepth, planner.NodeBudget);
            }
            else plan = NeedPlanning.Plan(ActorView(actor.Id), goal, agent.KnownFailures.Values.Where(f => f.RetryAt > processingTick).Select(f => f.ServiceId), planner.Candidates, planner.MaximumDepth, planner.NodeBudget, KnownServiceWaits(actor.Id));
            PlannerExpansions += plan.Expansions; agent.LastOutcome = plan.Outcome; agent.LastCost = plan.Cost;
            if (plan.Outcome == PlanOutcome.PlanFound && plan.Steps.Count > 0)
            {
                PlansFound++; agent.BlockedReason = ""; agent.RetryAt = 0; agent.PlannedTick = processingTick;
                var step = plan.Steps[0].Intent; agent.ServiceId = step.ServiceId;
                long cause = Emit("PlanSelected", actor, detail: (preSleep.HasValue ? "pre-sleep-relief/v1" : goal == NeedKind.Bladder ? "seek-toilet/v1" : goal == NeedKind.Social ? "conversation/v1" : "optional-rest/v1") + "; need=" + goal + "; due=" + agent.ToiletDueTick + "; cost=" + plan.Cost);
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.NeedCommand, Intent = step, CauseId = cause }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; agent.BlockedReason = "PlanBudgetExhausted"; }
            else { PlansUnavailable++; agent.BlockedReason = OwnNeedCustodyFacility(actor.Id) == 0 ? "CustodyRestricted" : goal == NeedKind.Social ? "NoLocalConsentingPartner" : "NoKnownReliefRouteOrEligibleService"; }
            agent.RetryAt = checked(processingTick + agent.Backoff); agent.Backoff = NextWorkBackoff(agent.Backoff);
            Emit("PlanUnavailable", actor, detail: "personal-need/v1; need=" + goal + "; " + agent.BlockedReason + "; retry=" + agent.RetryAt);
            RequestNeedDecision(agent, agent.RetryAt); WakeAfterNeed(actor);
        }
    }
}
