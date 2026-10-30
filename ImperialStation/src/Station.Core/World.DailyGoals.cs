using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public enum DailyGoalStatus { Pending, Completed, Unnecessary, MissedWindow, Superseded }
    public sealed class DailyGoalSnapshot
    {
        public long Id { get; internal set; }
        public long ActorId { get; internal set; }
        public long RosterVersion { get; internal set; }
        public RoutineOpportunityKind Kind { get; internal set; }
        public long Opens { get; internal set; }
        public long DueTick { get; internal set; }
        public long Closes { get; internal set; }
        public DailyGoalStatus Status { get; internal set; }
        public long RetryAt { get; internal set; }
        public long Backoff { get; internal set; }
        public string Reason { get; internal set; } = "";
        public long ServiceId { get; internal set; }
        public long ActiveActionId { get; internal set; }
        public long CompletedActionId { get; internal set; }
        public long EndedTick { get; internal set; } = -1;
        public NeedKind Need => Kind == RoutineOpportunityKind.Sleep ? NeedKind.SleepPressure : Kind == RoutineOpportunityKind.ScheduledRest ? NeedKind.WorkStrain : NeedKind.Hunger;
        internal DailyGoalSnapshot Copy() => (DailyGoalSnapshot)MemberwiseClone();
    }
    internal sealed class DailySchedule
    {
        internal long ActorId;
        internal long RosterVersion;
        internal long NextBoundary = -1;
        internal long NextDecision = -1;
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, DailyGoalSnapshot> dailyGoals = new SortedDictionary<long, DailyGoalSnapshot>();
        private readonly SortedDictionary<long, List<DailyGoalSnapshot>> dailyByActor = new SortedDictionary<long, List<DailyGoalSnapshot>>();
        private IEnumerable<DailyGoalSnapshot> DailyFor(long actor) => dailyByActor.TryGetValue(actor, out var goals) ? goals : (IEnumerable<DailyGoalSnapshot>)Array.Empty<DailyGoalSnapshot>();
        private void AddDailyGoal(DailyGoalSnapshot goal)
        {
            dailyGoals.Add(goal.Id, goal);
            if (!dailyByActor.TryGetValue(goal.ActorId, out var goals)) { goals = new List<DailyGoalSnapshot>(); dailyByActor.Add(goal.ActorId, goals); }
            goals.Add(goal);
        }
        private readonly SortedDictionary<long, DailySchedule> dailySchedules = new SortedDictionary<long, DailySchedule>();
        public IReadOnlyList<DailyGoalSnapshot> InspectDailyGoals() => dailyGoals.Values.Select(g => g.Copy()).ToArray();
        private void InitializeDailyGoals()
        { foreach (var actor in actors.Values.Where(a => needAgents.ContainsKey(a.Id.Value))) AdoptDailyRoster(actor); }
        private void AdoptDailyRoster(Actor actor)
        {
            if (!needAgents.ContainsKey(actor.Id.Value) || !routines.TryGetValue(actor.Id, out var routine) || routine.KnownRoster == null) return;
            if (dailySchedules.TryGetValue(actor.Id.Value, out var old) && old.RosterVersion == routine.KnownRoster.Version) return;
            foreach (var goal in DailyFor(actor.Id.Value).Where(g => g.Status == DailyGoalStatus.Pending)) EndDailyGoal(goal, DailyGoalStatus.Superseded, "ReadRosterSuperseded");
            foreach (var e in scheduled.ForActor(actor.Id.Value).Where(e => e.Kind is ScheduledKind.DailyBoundary or ScheduledKind.EvaluateDaily).ToArray()) scheduled.Remove(e);
            dailySchedules[actor.Id.Value] = new DailySchedule { ActorId = actor.Id.Value, RosterVersion = routine.KnownRoster.Version };
            UpdateDailyCalendar(actor.Id);
        }
        private void EndDailyGoal(DailyGoalSnapshot goal, DailyGoalStatus status, string reason)
        {
            goal.Status = status; goal.EndedTick = NeedNow; goal.Reason = reason;
            Emit("DailyGoalResolved", actors[new Id<ActorTag>(goal.ActorId)], detail: "goal=" + goal.Id + "; " + status + "; " + reason);
        }
        // Missed windows keep the reason they were not met: a commitment when the window
        // opened, travel that crossed the close, a blocked plan or failure, or a goal that
        // was never selected (for example a meal that only became needed late).
        private static string ClosedDailyReason(DailyGoalSnapshot goal, bool travelling = false) => "OpportunityClosed" +
            (travelling ? "/TravelAcrossBoundary" : goal.Reason.Length > 0 ? "/" + goal.Reason : goal.ServiceId == 0 ? "/NeverSelected" : "/SelectedNotStarted");
        private void UpdateDailyCalendar(Id<ActorTag> actorId)
        {
            var schedule = dailySchedules[actorId.Value]; var roster = routines[actorId].KnownRoster!; long now = NeedNow;
            foreach (var goal in DailyFor(actorId.Value).Where(g => g.Status == DailyGoalStatus.Pending && g.Kind != RoutineOpportunityKind.ScheduledRest && g.Closes <= now && g.ActiveActionId == 0).ToArray())
                EndDailyGoal(goal, DailyPolicy.MealUnnecessary(goal.Need, () => MealNeeded(actorId, goal)) ? DailyGoalStatus.Unnecessary : DailyGoalStatus.MissedWindow, ClosedDailyReason(goal));
            var windows = DailyRoutine.Around(roster, now);
            foreach (var window in windows.Where(w => w.Opens <= now && now < w.Closes))
            {
                if (DailyFor(actorId.Value).Any(g => g.RosterVersion == roster.Version && g.Kind == window.Kind && g.Opens == window.Opens)) continue;
                var stamp = NewGoalStamp(Math.Max(0, window.Opens));
                var goal = new DailyGoalSnapshot { Id = stamp.Sequence, ActorId = actorId.Value, RosterVersion = roster.Version, Kind = window.Kind, Opens = window.Opens, DueTick = stamp.Due, Closes = window.Closes, Backoff = planner.InitialBackoff };
                AddDailyGoal(goal); Emit("DailyGoalDue", actors[actorId], detail: "goal=" + goal.Id + "; " + goal.Kind + "; due=" + goal.DueTick + "; roster=" + roster.Version);
                if (actors[actorId].Action != null) { goal.Reason = "CommittedAction"; Emit("DailyGoalDeferred", actors[actorId], detail: "goal=" + goal.Id + "; CommittedAction"); }
                if (window.Kind == RoutineOpportunityKind.PreDutyMeal)
                { routines[actorId].EndShiftCheckIn = true; MarkRoutineCheckIn(actorId); }
            }
            schedule.NextBoundary = windows.SelectMany(w => new[] { w.Opens, w.Closes }).Where(t => t > now).Min();
            foreach (var e in scheduled.ForActor(actorId.Value).Where(e => e.Kind == ScheduledKind.DailyBoundary).ToArray()) scheduled.Remove(e);
            Schedule(new ScheduledEvent { Tick = schedule.NextBoundary, Phase = EventPhase.Decision, Kind = ScheduledKind.DailyBoundary, Intent = new ActionIntent(actorId, ActionKind.Inspect, default) });
            WakeDaily(actors[actorId]); WakeNeed(actors[actorId]);
        }
        // Gathers own hunger and the known roster; Mind decides whether a meal is needed.
        private bool MealNeeded(Id<ActorTag> actor, DailyGoalSnapshot goal) =>
            DailyPolicy.MealNeeded(Needs!.Rules[(int)NeedKind.Hunger], () => NeedValues(actor.Value).Single(n => n.Kind == NeedKind.Hunger).Q,
                routines.TryGetValue(actor, out var routine) ? routine.KnownRoster : null, goal.RosterVersion, goal.Closes, NeedNow, Config.TickDurationMs);
        // Gathers own hunger and the open duty-meal goal; Mind forecasts when the meal is needed.
        private long DutyMealComingDue(Actor actor)
        {
            if (Needs == null || !dailySchedules.ContainsKey(actor.Id.Value)) return -1;
            var goal = DailyFor(actor.Id.Value).FirstOrDefault(g => g.Kind == RoutineOpportunityKind.DutyMeal && g.Status == DailyGoalStatus.Pending && g.Opens <= NeedNow && NeedNow < g.Closes);
            return goal == null ? -1 : DailyPolicy.DutyMealComingDue(Needs.Rules[(int)NeedKind.Hunger], NeedValues(actor.Id.Value).Single(n => n.Kind == NeedKind.Hunger).Q, goal.Closes, NeedNow, Config.TickDurationMs);
        }
        private bool DailyGoalEligible(DailyGoalSnapshot goal) => DailyPolicy.Eligible(goal.Status == DailyGoalStatus.Pending, goal.Kind, goal.Need, goal.DueTick, goal.RetryAt, goal.Closes, NeedNow,
            () => MealNeeded(new Id<ActorTag>(goal.ActorId), goal));
        private NeedKind? PreSleepRelief(Id<ActorTag> actor)
        {
            if (Needs == null || !needAgents.TryGetValue(actor.Value, out var agent)) return null;
            return NeedPriority.PreSleepRelief(agent.RetryAt, NeedNow, actors[actor].Action?.Kind == ActionKind.Sleep,
                () => DailyFor(actor.Value).Any(g => g.Kind == RoutineOpportunityKind.Sleep && DailyGoalEligible(g)), () => NeedValues(actor.Value), () => ActorView(actor), Needs.Rules, Config.TickDurationMs);
        }
        private void RequestDailyDecision(Id<ActorTag> actor, long tick)
        {
            if (!dailySchedules.TryGetValue(actor.Value, out var state)) return;
            if (processingTick >= 0 && (tick < processingTick || tick == processingTick && processingPhase >= EventPhase.Decision)) tick = checked(processingTick + 1);
            if (state.NextDecision >= 0 && state.NextDecision <= tick) return;
            foreach (var e in scheduled.ForActor(actor.Value).Where(e => e.Kind == ScheduledKind.EvaluateDaily).ToArray()) scheduled.Remove(e);
            state.NextDecision = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateDaily, Intent = new ActionIntent(actor, ActionKind.Inspect, default) });
        }
        private void WakeDaily(Actor actor)
        {
            if (actor.Action != null || !dailySchedules.ContainsKey(actor.Id.Value)) return;
            var pending = DailyFor(actor.Id.Value).Where(g => DailyPolicy.Open(g.Status == DailyGoalStatus.Pending, g.Kind, g.Need, g.Closes, NeedNow, () => MealNeeded(actor.Id, g))).ToArray();
            if (pending.Length > 0) RequestDailyDecision(actor.Id, Math.Max(NeedNow, pending.Min(g => g.RetryAt)));
        }
        private void EvaluateDaily(Id<ActorTag> actorId)
        {
            dailySchedules[actorId.Value].NextDecision = -1; var actor = actors[actorId];
            if (GoalPriority.FirstDeferring(GoalPriority.DailyGates, g => g == PriorityGate.PlannedThisTick ? scheduled.ForActor(actorId.Value).Any(e => e.Tick == processingTick && e.Kind == ScheduledKind.DailyCommand) : Defers(actor, g)) != PriorityGate.None) return;
            var selection = SelectedDueGoal(actor);
            if (selection.Source != DueSource.Routine) { DeferToDueGoal(actor); return; }
            var goal = dailyGoals[selection.Id];
            if (DailyPolicy.YieldsToPreSleepRelief(goal.Kind, () => PreSleepRelief(actorId))) { RequestNeedDecision(needAgents[actorId.Value], NeedNow); return; }
            if (!SpendPlanningRequest()) { RequestDailyDecision(actorId, checked(processingTick + 1)); return; }
            var memory = needAgents[actorId.Value];
            var choice = NeedPlanning.PlanWithRestFallback(ActorView(actorId), goal.Need, memory.KnownFailures.Values.Where(f => f.RetryAt > processingTick).Select(f => f.ServiceId), memory.SleepFallbackRetryAt <= processingTick,
                planner.Candidates, planner.MaximumDepth, planner.NodeBudget, KnownServiceWaits(actorId), SleptThisWindow(actorId));
            PlannerExpansions += choice.Expansions; var plan = choice.Plan; bool fallback = choice.Fallback;
            if (plan.Outcome == PlanOutcome.PlanFound && plan.Steps.Count > 0)
            {
                PlansFound++; goal.Reason = fallback ? UnmetSleepReason : ""; var step = plan.Steps[0].Intent; goal.ServiceId = step.ServiceId;
                if (fallback) Emit("SleepUnmet", actor, detail: UnmetSleepReason + "; goal=" + goal.Id + "; planning rest; service=" + step.ServiceId);
                long cause = Emit("PlanSelected", actor, detail: "daily-routine/v1; goal=" + goal.Id + "; " + goal.Kind + "; due=" + goal.DueTick + "; cost=" + plan.Cost);
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.DailyCommand, Intent = step, Value = goal.Id, CauseId = cause }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) { PlansExhausted++; goal.Reason = "PlanBudgetExhausted"; }
            else { PlansUnavailable++; goal.Reason = OwnNeedCustodyFacility(actorId) == 0 ? "CustodyRestricted" : goal.Need == NeedKind.SleepPressure ? UnmetSleepReason : "NoKnownReliefRouteOrEligibleService"; }
            goal.RetryAt = checked(processingTick + goal.Backoff); goal.Backoff = NextWorkBackoff(goal.Backoff);
            Emit("DailyGoalBlocked", actor, detail: "goal=" + goal.Id + "; " + goal.Reason + "; retry=" + goal.RetryAt);
            RequestDailyDecision(actorId, goal.RetryAt); WakeAfterNeed(actor);
        }
        private void StartDailyIntent(ScheduledEvent command)
        {
            var goal = dailyGoals[command.Value]; var actor = actors[new Id<ActorTag>(goal.ActorId)];
            var gate = GoalPriority.FirstDeferring(GoalPriority.DailyStartGates, g => g == PriorityGate.Inactive ? !DailyGoalEligible(goal) : Defers(actor, g, DueSource.Routine, goal.Id));
            if (gate == PriorityGate.CommittedAction) return;
            if (gate != PriorityGate.None) { WakeDaily(actor); return; }
            if (DailyPolicy.YieldsToPreSleepRelief(goal.Kind, () => PreSleepRelief(actor.Id))) { RequestNeedDecision(needAgents[actor.Id.Value], NeedNow); return; }
            goal.ActiveActionId = nextAction; Start(command.Intent, command.CauseId);
        }
        private bool TravellingForDailyNeed(Actor actor, NeedKind? kind = null) => (actor.Action?.Kind is ActionKind.Move or ActionKind.AllocateBerth or ActionKind.Rest) && DailyFor(actor.Id.Value).Any(g => g.ActiveActionId == actor.Action.Id.Value && (!kind.HasValue || g.Need == kind.Value));
        private void DailyActionEnded(Actor actor, long action, FailureReason failure)
        {
            var goal = DailyFor(actor.Id.Value).SingleOrDefault(g => g.ActiveActionId == action);
            if (goal == null) return; goal.ActiveActionId = 0;
            if (failure != FailureReason.None)
            {
                var memory = needAgents[actor.Id.Value];
                memory.KnownFailures[goal.ServiceId] = new NeedServiceFailureSnapshot { ServiceId = goal.ServiceId, ActionId = action, Tick = processingTick, RetryAt = checked(processingTick + goal.Backoff), Reason = failure };
                goal.Reason = failure.ToString(); goal.Backoff = NextWorkBackoff(goal.Backoff);
                Emit("NeedServiceFailureLearned", actor, new Id<ActionTag>(action), detail: "routine=" + goal.Id + "; service=" + goal.ServiceId + "; " + failure);
            }
            else if (needActivities.TryGetValue(action, out var activity) && activity.Status == NeedActivityStatus.Completed && goal.Status == DailyGoalStatus.Pending)
            {
                if (IsSleepFallback(goal.Need, goal.ServiceId)) { goal.Reason = UnmetSleepReason; goal.RetryAt = CompleteSleepFallback(actor, action); }
                else { goal.CompletedActionId = action; EndDailyGoal(goal, DailyGoalStatus.Completed, "PhysicalActivityCompleted"); }
            }
            if (goal.Status == DailyGoalStatus.Pending && goal.Kind != RoutineOpportunityKind.ScheduledRest && goal.Closes <= NeedNow)
                EndDailyGoal(goal, DailyGoalStatus.MissedWindow, ClosedDailyReason(goal, failure == FailureReason.None && !needActivities.ContainsKey(action)));
            WakeDaily(actor);
        }
    }
}
