using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed class CheckInSnapshot
    {
        public long ActorId { get; internal set; }
        public long DueTick { get; internal set; }
        public bool Due { get; internal set; }
        public long NextDecisionTick { get; internal set; }
        public long Completed { get; internal set; }
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<Id<ActorTag>, CheckInAgent> checkIns = new SortedDictionary<Id<ActorTag>, CheckInAgent>();
        public IReadOnlyList<CheckInSnapshot> InspectCheckIns() => checkIns.Values.Select(c => new CheckInSnapshot { ActorId = c.Actor.Value, DueTick = c.DueTick, Due = c.Due, NextDecisionTick = c.NextDecision, Completed = c.Completed }).ToArray();
        private void InitializeCheckIns(IEnumerable<CheckInSeed> seeds)
        {
            foreach (var seed in seeds)
            {
                if (!actors.ContainsKey(seed.Actor)) throw new ArgumentException("Unknown check-in actor.");
                var agent = new CheckInAgent { Actor = seed.Actor, Interval = seed.Interval, DueTick = seed.FirstTick, Backoff = planner.InitialBackoff }; checkIns.Add(seed.Actor, agent);
                Schedule(new ScheduledEvent { Tick = seed.FirstTick, Phase = EventPhase.Decision, Kind = ScheduledKind.CheckInDue, Intent = new ActionIntent(seed.Actor, ActionKind.ReadInbox, default) });
            }
        }
        private void CheckInDue(Id<ActorTag> actor)
        {
            var agent = checkIns[actor]; MarkCheckInDue(agent); RequestCheckInDecision(agent, processingTick);
            Emit("CheckInDue", actors[actor], detail: "own-clock/v1");
        }
        private void TriggerWorkCheckIn(Id<ActorTag> actor)
        {
            if (!checkIns.TryGetValue(actor, out var agent)) return;
            MarkCheckInDue(agent); RequestCheckInDecision(agent, processingTick);
            Emit("CheckInDue", actors[actor], detail: "own-work-result/v1");
        }
        private void RequestCheckInDecision(CheckInAgent agent, long tick)
        {
            if (!agent.Due) return;
            if (needAgents.ContainsKey(agent.Actor.Value)) tick = Math.Max(tick, agent.BlockedUntil);
            if (tick < processingTick || tick == processingTick && processingPhase > EventPhase.Decision) tick = checked(processingTick + 1);
            if (agent.NextDecision >= 0 && agent.NextDecision <= tick) return;
            if (agent.NextDecision >= 0) foreach (var e in scheduled.ForActor(agent.Actor.Value).Where(e => e.Kind == ScheduledKind.EvaluateCheckIn).ToArray()) scheduled.Remove(e);
            agent.NextDecision = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateCheckIn, Intent = new ActionIntent(agent.Actor, ActionKind.ReadInbox, default) });
        }
        private void EvaluateCheckIn(Id<ActorTag> actorId)
        {
            var agent = checkIns[actorId]; agent.NextDecision = -1; var actor = actors[actorId];
            if (EscortControls(actorId) || !agent.Due || actor.Action != null || DeferToNeed(actor) || PrepareCredentialGoal(actor) || !RoutineAllowsCheckIn(actorId) || GuardHasPriority(actorId) || DeferToDueGoal(actor, DueSource.CheckIn) || AlreadyPlanned(actor, DueSource.CheckIn) || !needAgents.ContainsKey(actorId.Value) && reportAgents.TryGetValue(actorId, out var report) && report.Pending.Count > 0) return;
            if (needAgents.ContainsKey(actorId.Value) && agent.BlockedUntil > processingTick) { RequestCheckInDecision(agent, agent.BlockedUntil); return; }
            if (!SpendPlanningRequest()) { RequestCheckInDecision(agent, checked(processingTick + 1)); return; }
            var result = ReportPlanning.PlanInbox(ActorView(actorId), Information!.LoginTicks, Information.ReadBaseTicks, planner.Candidates, planner.MaximumDepth, planner.NodeBudget, AvoidedTerminals(actorId));
            PlannerExpansions += result.Expansions;
            if (result.Outcome == PlanOutcome.PlanFound && result.Steps.Count > 0)
            {
                PlansFound++; agent.BlockedUntil = 0; long cause = Emit("PlanSelected", actor, detail: "periodic-check-in/v1; cost=" + result.Cost + "; " + string.Join(" -> ", result.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.CheckIn), Intent = result.Steps[0].Intent, CauseId = cause });
            }
            else
            {
                if (result.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
                Emit("PlanUnavailable", actor, detail: "periodic-check-in/v1; " + result.Outcome);
                if (!KnownOnDuty(actorId)) { RoutineCheckInFinished(actor, true); return; }
                agent.BlockedUntil = checked(processingTick + agent.Backoff); WakeDaily(actor); RequestCheckInDecision(agent, agent.BlockedUntil); agent.Backoff = NextWorkBackoff(agent.Backoff);
            }
        }
        private void OnCheckInActionFinished(Actor actor, ActiveAction? action, bool failed)
        {
            if (checkIns.TryGetValue(actor.Id, out var agent) && actor.Action == null)
            {
                if (!failed && action?.Kind is ActionKind.Login or ActionKind.ReadInbox) agent.Backoff = planner.InitialBackoff;
                if (failed && actor.LastFailure == FailureReason.RequiredServiceNotPresent)
                {
                    agent.BlockedUntil = checked(processingTick + agent.Backoff); agent.Backoff = NextWorkBackoff(agent.Backoff);
                    foreach (var e in scheduled.ForActor(actor.Id.Value).Where(e => e.Kind == ScheduledKind.EvaluateCheckIn).ToArray()) scheduled.Remove(e);
                    agent.NextDecision = -1;
                }
                if (!failed && action?.Kind == ActionKind.ReadInbox)
                {
                    agent.Completed++; agent.Due = false; agent.DueTick = checked(processingTick + agent.Interval);
                    RoutineCheckInFinished(actor, false);
                    foreach (var e in scheduled.ForActor(actor.Id.Value).Where(e => e.Kind == ScheduledKind.CheckInDue).ToArray()) scheduled.Remove(e);
                    Schedule(new ScheduledEvent { Tick = agent.DueTick, Phase = EventPhase.Decision, Kind = ScheduledKind.CheckInDue, Intent = new ActionIntent(actor.Id, ActionKind.ReadInbox, default) });
                }
                if (agent.Due) RequestCheckInDecision(agent, failed ? checked(processingTick + planner.InitialBackoff) : processingTick);
            }
            if ((JobsEnabled || Routines != null) && !failed && action?.Kind is ActionKind.ReadInbox or ActionKind.PostMessage)
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.ReleaseIdleTerminal, Intent = new ActionIntent(actor.Id, ActionKind.Logout, actor.Position) });
        }
        private void ReleaseIdleTerminal(Id<ActorTag> actor)
        {
            if (reportAgents.TryGetValue(actor, out var report) && report.Pending.Count > 0) return;
            if (checkIns.TryGetValue(actor, out var checkIn) && checkIn.Due) return;
            if (KnownOnDuty(actor) && audits.Values.Any(a => a.ActorId == actor.Value && a.Pending && a.TerminalId == ActorView(actor).SessionTerminalId)) return;
            if (actors[actor].Action?.Kind is ActionKind.Login or ActionKind.RecoveryLogin or ActionKind.PostLostCard or ActionKind.ReadRecoveryInbox or ActionKind.IssueReplacement or ActionKind.ReadInbox or ActionKind.PostMessage or ActionKind.QueryRecords) return;
            EndSession(actor, "CheckInFinished");
        }
    }
}
