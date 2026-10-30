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
                EndDailyGoal(goal, goal.Need == NeedKind.Hunger && !MealNeeded(actorId, goal) ? DailyGoalStatus.Unnecessary : DailyGoalStatus.MissedWindow, ClosedDailyReason(goal));
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
        // A meal below seek is still needed when the known roster offers no later meal
        // opportunity before own hunger, at its configured rates, would become critical
        // (typically across sleep). Uses only own needs and the known calendar.
        private bool MealNeeded(Id<ActorTag> actor, DailyGoalSnapshot goal)
        {
            var rule = Needs!.Rules[(int)NeedKind.Hunger]; if (rule.Seek <= 0) return false;
            long q = NeedValues(actor.Value).Single(n => n.Kind == NeedKind.Hunger).Q;
            if (q >= rule.Seek * NeedAccumulator.HourMilliseconds) return true;
            if (rule.Critical <= 0 || !routines.TryGetValue(actor, out var routine) || routine.KnownRoster == null || routine.KnownRoster.Version != goal.RosterVersion) return false;
            long next = DailyRoutine.Around(routine.KnownRoster, goal.Closes).Where(w => w.Need == NeedKind.Hunger && w.Opens >= goal.Closes).Select(w => w.Opens).DefaultIfEmpty(-1).Min();
            return next > NeedNow && new NeedAccumulator(q, NeedNow, Math.Max(rule.AwakeRate, rule.SleepingRate)).At(next, Config.TickDurationMs) >= rule.Critical * NeedAccumulator.HourMilliseconds;
        }
        private bool DailyGoalEligible(DailyGoalSnapshot goal) => goal.Status == DailyGoalStatus.Pending && goal.DueTick <= NeedNow && goal.RetryAt <= NeedNow &&
            (goal.Kind == RoutineOpportunityKind.ScheduledRest || goal.Closes > NeedNow) && (goal.Need != NeedKind.Hunger || MealNeeded(new Id<ActorTag>(goal.ActorId), goal));
        // Before committing to the known bed duration, own hunger or bladder that would
        // become critical while asleep is relieved first. Only own needs, configured
        // sleeping rates and known bed durations are used. A failed relief backs off and
        // yields to sleep, so this cannot keep a citizen awake indefinitely.
        private NeedKind? PreSleepRelief(Id<ActorTag> actor)
        {
            if (Needs == null || !needAgents.TryGetValue(actor.Value, out var agent) || agent.RetryAt > NeedNow || actors[actor].Action?.Kind == ActionKind.Sleep) return null;
            // Sleep is imminent through the roster's sleep opportunity or urgent sleep pressure.
            if (!DailyFor(actor.Value).Any(g => g.Kind == RoutineOpportunityKind.Sleep && DailyGoalEligible(g)) &&
                !NeedValues(actor.Value).Any(n => n.Kind == NeedKind.SleepPressure && n.Band >= NeedBand.Urgent)) return null;
            var view = ActorView(actor);
            long sleep = view.NeedServices.Where(s => s.Kind == NeedServiceKind.Bed && s.DetentionFacilityId == 0 && (s.Id == view.OwnBerthId || s.AssignedActorId == actor.Value))
                .Select(s => s.DurationTicks).DefaultIfEmpty(0).Max();
            if (sleep <= 0) return null;
            foreach (var need in view.Needs.Where(n => n.Kind is NeedKind.Hunger or NeedKind.Bladder).OrderByDescending(n => n.Band).ThenBy(n => n.Kind))
            {
                var rule = Needs.Rules[(int)need.Kind];
                if (rule.Critical > 0 && need.Band < NeedBand.Critical && view.NeedServices.Any(s => s.DetentionFacilityId == 0 && NeedPlanning.Serves(s.Kind, need.Kind)) &&
                    new NeedAccumulator(need.Q, NeedNow, rule.SleepingRate).At(checked(NeedNow + sleep), Config.TickDurationMs) >= rule.Critical * NeedAccumulator.HourMilliseconds) return need.Kind;
            }
            return null;
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
            var pending = DailyFor(actor.Id.Value).Where(g => g.Status == DailyGoalStatus.Pending && (g.Kind == RoutineOpportunityKind.ScheduledRest || g.Closes > NeedNow) && (g.Need != NeedKind.Hunger || MealNeeded(actor.Id, g))).ToArray();
            if (pending.Length > 0) RequestDailyDecision(actor.Id, Math.Max(NeedNow, pending.Min(g => g.RetryAt)));
        }
        private void EvaluateDaily(Id<ActorTag> actorId)
        {
            dailySchedules[actorId.Value].NextDecision = -1; var actor = actors[actorId];
            if (actor.Action != null || scheduled.ForActor(actorId.Value).Any(e => e.Tick == processingTick && e.Kind == ScheduledKind.DailyCommand) || DeferToNeed(actor) || DeferToRecovery(actor) || KnownUrgentOrder(actorId)) return;
            var selection = SelectedDueGoal(actor);
            if (selection.Source != DueSource.Routine) { DeferToDueGoal(actor); return; }
            var goal = dailyGoals[selection.Id];
            if (goal.Kind == RoutineOpportunityKind.Sleep && PreSleepRelief(actorId).HasValue) { RequestNeedDecision(needAgents[actorId.Value], NeedNow); return; }
            if (!SpendPlanningRequest()) { RequestDailyDecision(actorId, checked(processingTick + 1)); return; }
            var memory = needAgents[actorId.Value];
            var plan = NeedPlanning.Plan(ActorView(actorId), goal.Need, memory.KnownFailures.Values.Where(f => f.RetryAt > processingTick).Select(f => f.ServiceId), planner.Candidates, planner.MaximumDepth, planner.NodeBudget, KnownServiceWaits(actorId));
            PlannerExpansions += plan.Expansions; bool fallback = false;
            if (plan.Outcome == PlanOutcome.NoPlan && goal.Need == NeedKind.SleepPressure && memory.SleepFallbackRetryAt <= processingTick && plan.Expansions < planner.NodeBudget)
            {
                int remaining = planner.NodeBudget - plan.Expansions;
                plan = NeedPlanning.Plan(ActorView(actorId), NeedKind.WorkStrain, memory.KnownFailures.Values.Where(f => f.RetryAt > processingTick).Select(f => f.ServiceId), planner.Candidates, planner.MaximumDepth, remaining);
                PlannerExpansions += plan.Expansions; fallback = plan.Outcome == PlanOutcome.PlanFound;
            }
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
            if (actor.Action != null) return;
            if (!DailyGoalEligible(goal) || DeferToNeed(actor) || DeferToRecovery(actor) || KnownUrgentOrder(actor.Id) || DeferToDueGoal(actor, DueSource.Routine, goal.Id)) { WakeDaily(actor); return; }
            if (goal.Kind == RoutineOpportunityKind.Sleep && PreSleepRelief(actor.Id).HasValue) { RequestNeedDecision(needAgents[actor.Id.Value], NeedNow); return; }
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
