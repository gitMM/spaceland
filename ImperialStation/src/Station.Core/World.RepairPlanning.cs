using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<Id<ActorTag>, RepairAgent> repairAgents = new SortedDictionary<Id<ActorTag>, RepairAgent>();
        private void InitializeRepairGoals(IEnumerable<RepairGoalSeed> goals)
        {
            foreach (var goal in goals)
            {
                if (!actors.ContainsKey(goal.Actor) || !machines.ContainsKey(goal.MachineId) || publicKnowledge?.Work == null) throw new ArgumentException("Invalid initial repair goal or missing published work catalog.");
                if (!repairAgents.TryGetValue(goal.Actor, out var agent))
                { agent = new RepairAgent { Actor = goal.Actor, Backoff = planner.InitialBackoff }; repairAgents.Add(goal.Actor, agent); }
                if (!agent.Pending.Contains(goal.MachineId)) agent.Pending.Add(goal.MachineId);
            }
            foreach (var agent in repairAgents.Values) RequestRepairDecision(agent, 0);
        }
        private void RequestRepairDecision(RepairAgent agent, long tick)
        {
            if (agent.Pending.Count == 0) return;
            if (processingTick >= 0 && (tick < processingTick || tick == processingTick && processingPhase >= EventPhase.Decision)) tick = checked(processingTick + 1);
            if (agent.NextDecision >= 0 && agent.NextDecision <= tick) return;
            if (agent.NextDecision >= 0)
                foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateRepair && e.Intent.Actor == agent.Actor).ToArray()) scheduled.Remove(e);
            agent.NextDecision = tick;
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateRepair, Intent = new ActionIntent(agent.Actor, ActionKind.Install, default) });
        }
        private void EvaluateRepair(Id<ActorTag> actorId)
        {
            var agent = repairAgents[actorId]; agent.NextDecision = -1; var actor = actors[actorId];
            if (EscortControls(actorId) || actor.Action != null || agent.Pending.Count == 0 || DeferToNeed(actor) || RepairCredentialBlocked(actor, agent.Pending[0]) || DeferToDueGoal(actor)) return;
            // Off duty, only a known result is recognized (Mind rule).
            if (!RepairPolicy.Proceeds(KnownOnDuty(actorId), actor.Knowledge.Entries.Values.Select(e => e.Fact), agent.Pending[0])) return;
            // Explicit report-first priority avoids two independent goals issuing competing intents.
            // Report completion wakes this retained repair; it never discards carried materials.
            if (WorkReportsFirst(actorId)) return;
            if (!SpendPlanningRequest()) { RequestRepairDecision(agent, checked(processingTick + 1)); return; }
            var result = RepairPlanning.Plan(ActorView(actorId), agent.Pending[0], planner.Candidates, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += result.Expansions; agent.LastOutcome = result.Outcome; agent.LastCost = result.Cost;
            var step = RepairPolicy.Classify(result);
            if (step != WorkGoalStep.Blocked)
            {
                PlansFound++; agent.BlockedReason = ""; agent.Backoff = planner.InitialBackoff;
                if (step == WorkGoalStep.Completed)
                {
                    Emit("RepairGoalCompleted", actor, detail: "repair-known-equipment/v1; machine=" + agent.Pending[0]);
                    RepairOrderSatisfied(agent, agent.Pending[0]);
                    agent.Pending.RemoveAt(0); agent.CompletedRepairs++; RequestRepairDecision(agent, processingTick); return;
                }
                var first = RepairPolicy.FirstIntent(result, agent.Attempts.TryGetValue(agent.Pending[0], out long attemptId) ? attemptId : (long?)null);
                long cause = Emit("PlanSelected", actor, causeId: publicKnowledge?.Work?.Equipment.FirstOrDefault(m => m.Id == agent.Pending[0])?.RequiresCredential == true ? RestoredCredentialCause(actor) : 0, detail: "repair-known-equipment/v1; machine=" + agent.Pending[0] + "; cost=" + result.Cost + "; " + string.Join(" -> ", result.Steps.Select(s => s.Name)));
                PinRepairPlan(actor, agent, result);
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), Intent = first, CauseId = cause });
                return;
            }
            if (result.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
            agent.BlockedReason = RepairPolicy.BlockedReason(result.Outcome);
            RepairOrderBlocked(agent, agent.BlockedReason);
            Emit("PlanUnavailable", actor, detail: "repair-known-equipment/v1; " + result.Outcome + "; retry=" + agent.Backoff);
            RequestRepairDecision(agent, checked(processingTick + agent.Backoff));
            agent.Backoff = NextWorkBackoff(agent.Backoff);
        }
        private static void ReleaseRepairPins(Actor actor, RepairAgent agent)
        {
            foreach (var key in agent.PlanPins)
                if (actor.Knowledge.Entries.TryGetValue(key, out var entry))
                { var f = entry.Fact; entry.Fact = new KnowledgeFact(f.Id, f.Key, f.Value, f.SourceKind, f.SourceId, f.ObservedTick, f.LearnedTick, false, f.SourceActorId); }
            agent.PlanPins.Clear();
        }
        private static void PinRepairPlan(Actor actor, RepairAgent agent, PlanResult plan)
        {
            var itemIds = RepairPolicy.PlannedItems(plan);
            foreach (var entry in actor.Knowledge.Entries.Values)
            {
                var f = entry.Fact;
                if (f.Pinned || !RepairPolicy.PinsForPlan(f.Key, agent.Pending[0], itemIds)) continue;
                agent.PlanPins.Add(f.Key); entry.Fact = new KnowledgeFact(f.Id, f.Key, f.Value, f.SourceKind, f.SourceId, f.ObservedTick, f.LearnedTick, true, f.SourceActorId); entry.Recency = ++actor.Knowledge.Sequence;
            }
        }
        private void OnRepairActionFinished(Actor actor, bool failed)
        {
            if (!repairAgents.TryGetValue(actor.Id, out var agent) || actor.Action != null) return;
            ReleaseRepairPins(actor, agent);
            if (failed) agent.BlockedReason = actor.LastFailure.ToString();
            RequestRepairDecision(agent, WorkBackoff.RetryTick(processingTick, agent.Backoff, failed));
        }
        private void OnRepairKnowledgeChanged(Actor actor, FactKey key)
        {
            if (!repairAgents.TryGetValue(actor.Id, out var agent) || agent.Pending.Count == 0 || actor.Action != null) return;
            if (!RepairPolicy.KnowledgeWakes(key, agent.Pending)) return;
            agent.Backoff = planner.InitialBackoff; RequestRepairDecision(agent, processingTick);
        }
        public IReadOnlyList<RepairAgentSnapshot> InspectRepairAgents() => repairAgents.Values.Select(a => new RepairAgentSnapshot
        { ActorId = a.Actor.Value, PendingMachines = a.Pending.ToArray(), CompletedRepairs = a.CompletedRepairs, NextDecisionTick = a.NextDecision,
            Backoff = a.Backoff, LastOutcome = a.LastOutcome, LastPlanCost = a.LastCost, BlockedReason = a.BlockedReason }).ToArray();
    }
}
