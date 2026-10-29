using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public RoutineConfig? Routines { get; private set; }
        private readonly SortedDictionary<long, RosterSnapshot> rosters = new SortedDictionary<long, RosterSnapshot>();
        private readonly SortedDictionary<string, long> rosterKeys = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<Id<ActorTag>, RoutineSnapshot> routines = new SortedDictionary<Id<ActorTag>, RoutineSnapshot>();
        private long nextRoster = 1;
        public IReadOnlyList<RosterSnapshot> InspectRosters() => rosters.Values.Select(r => r.Copy()).ToArray();
        public IReadOnlyList<RoutineSnapshot> InspectRoutines() => routines.Values.Select(r => r.Copy()).ToArray();
        // Institutional schedule evidence only; no physical activity is consulted.
        public IReadOnlyList<RosterPayload> PublishedRosters() => rosters.Values.Where(r => r.Published).GroupBy(r => r.Roster.ActorId).Select(g => g.Last().Roster).OrderBy(r => r.ActorId).ToArray();
        private void InitializeRoutines(RoutineSeed seed)
        {
            if (Information == null) throw new ArgumentException("Routines require the information system.");
            Routines = seed.Config;
            foreach (var assignment in seed.Assignments)
            {
                if (routines.ContainsKey(assignment.Actor)) throw new ArgumentException("Duplicate initial roster actor.");
                var entry = CreateRoster(assignment, "initial/roster/" + assignment.Actor.Value, assignment.InitiallyKnown ? -1 : 0);
                routines.Add(assignment.Actor, new RoutineSnapshot { ActorId = assignment.Actor.Value });
                if (assignment.InitiallyKnown)
                { entry.Published = true; RecordRoster(entry); if (!AdoptRoster(actors[assignment.Actor], entry.Roster, EvidenceKind.Seeded, entry.Roster.Version, -1)) throw new ArgumentException("No capacity for initial pinned roster."); }
                else Schedule(new ScheduledEvent { Tick = 0, Phase = EventPhase.Delivery, Kind = ScheduledKind.PublishRoster, Value = entry.Roster.Version });
                if (!checkIns.ContainsKey(assignment.Actor)) InitializeCheckIns(new[] { new CheckInSeed(assignment.Actor, assignment.InitiallyKnown ? entry.Roster.NextCheckIn(0, true) : 0, Routines.PeriodicCheckInTicks) });
            }
        }
        private RosterSnapshot CreateRoster(RosterAssignmentSeed assignment, string key, long tick)
        {
            if (Routines == null || !actors.ContainsKey(assignment.Actor) || assignment.Cohort >= Routines.Cohorts || string.IsNullOrWhiteSpace(key) ||
                assignment.RestTile.HasValue && (publicKnowledge == null || !publicKnowledge.Layout.Any(t => t.Position == assignment.RestTile.Value)))
                throw new ArgumentException("Roster requires a registered actor, configured cohort and known rest tile.");
            long version = nextRoster;
            _ = checked(nextRoster + 1);
            int offset = streams["actor/" + assignment.Actor.Value.ToString(CultureInfo.InvariantCulture)].Next(Routines.CheckInWindow);
            var payload = new RosterPayload(version, assignment.Actor.Value, assignment.Cohort, Routines.DayTicks,
                checked(assignment.Cohort * (Routines.DayTicks / Routines.Cohorts)), Routines.DutyTicks, offset, assignment.RestTile);
            var entry = new RosterSnapshot { Roster = payload, OperationKey = key, PublicationTick = tick };
            rosters.Add(version, entry); rosterKeys.Add(key, version); nextRoster++; return entry;
        }
        public long ScheduleRosterChange(long tick, RosterAssignmentSeed assignment, string key)
        {
            if (rosterKeys.TryGetValue(key, out long prior))
            {
                var old = rosters[prior];
                if (old.Roster.ActorId != assignment.Actor.Value || old.Roster.Cohort != assignment.Cohort || old.Roster.RestTile != assignment.RestTile || old.PublicationTick != tick) throw new ArgumentException("Roster operation key conflicts.");
                return prior;
            }
            ValidateFuture(tick);
            if (!routines.ContainsKey(assignment.Actor)) throw new ArgumentException("Roster change requires a registered routine actor.");
            var entry = CreateRoster(assignment, key, tick);
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Delivery, Kind = ScheduledKind.PublishRoster, Value = entry.Roster.Version });
            return entry.Roster.Version;
        }
        private void RecordRoster(RosterSnapshot entry)
        {
            var r = entry.Roster; var actor = actors[new Id<ActorTag>(r.ActorId)];
            var fields = new List<RecordField> { Number(AttributeKind.ActorId, r.ActorId), Number(AttributeKind.RosterVersion, r.Version), Number(AttributeKind.Cohort, r.Cohort),
                Number(AttributeKind.DayTicks, r.DayTicks), Number(AttributeKind.ShiftStart, r.ShiftStart), Number(AttributeKind.DutyTicks, r.DutyTicks), Number(AttributeKind.CheckInOffset, r.CheckInOffset) };
            if (r.RestTile.HasValue) fields.Add(new RecordField(AttributeKind.RestTile, FactValue.At(r.RestTile.Value)));
            records.Append(RecordKind.Roster, r.ActorId, actor.Account.Value, processingTick, "roster/" + r.Version, fields);
        }
        private void PublishRoster(long version)
        {
            var entry = rosters[version]; if (entry.Published) return;
            _ = checked(processingTick + Information!.LatencyTicks);
            entry.Published = true; var r = entry.Roster;
            if (rosters.Values.Any(e => e.Published && e.Roster.ActorId == r.ActorId && e.Roster.Version > version))
            { Emit("RosterPublicationSuperseded", detail: "actor=" + r.ActorId + "; version=" + version); return; }
            RecordRoster(entry);
            entry.MessageId = PostAuthorityWork(new MessageDraft(MessageKind.RosterUpdated, RecipientKind.Account, actors[new Id<ActorTag>(r.ActorId)].Account,
                new FactKey(SubjectKind.Actor, r.ActorId, AttributeKind.RosterVersion), FactValue.Integer(r.Version), processingTick, operationKey: entry.OperationKey, roster: r));
            Emit("RosterPublished", messageId: entry.MessageId, detail: "actor=" + r.ActorId + "; version=" + r.Version + "; cohort=" + r.Cohort);
            RequestIncidentReview(processingTick);
        }
        private bool ReadRoster(Actor actor, MessageSnapshot message)
        {
            var r = message.Draft.Roster;
            if (message.Draft.Kind != MessageKind.RosterUpdated || message.SenderAccount.Value != 0 || r == null || r.ActorId != actor.Id.Value || !routines.ContainsKey(actor.Id)) return false;
            return AdoptRoster(actor, r, EvidenceKind.MessageRead, message.Id, message.Draft.ObservedTick);
        }
        private bool AdoptRoster(Actor actor, RosterPayload roster, EvidenceKind source, long sourceId, long observed)
        {
            var state = routines[actor.Id]; if (state.KnownRoster != null && state.KnownRoster.Version >= roster.Version) return true;
            if (!Learn(actor, new FactKey(SubjectKind.Actor, actor.Id.Value, AttributeKind.RosterVersion), FactValue.Integer(roster.Version), source, sourceId, observed, 0, pin: true))
                return false;
            state.KnownRoster = roster; state.OnDuty = roster.OnDuty(Math.Max(0, processingTick)); state.EndShiftCheckIn = false;
            foreach (var e in scheduled.Where(e => (e.Kind is ScheduledKind.RoutineBoundary or ScheduledKind.ShiftCheckIn) && e.Intent.Actor == actor.Id).ToArray()) scheduled.Remove(e);
            long now = Math.Max(0, processingTick); state.NextBoundary = roster.NextBoundary(now);
            state.NextShiftCheckIn = roster.NextCheckIn(now, processingPhase < EventPhase.Decision);
            ScheduleRoutineEvent(actor.Id, state.NextBoundary, ScheduledKind.RoutineBoundary);
            ScheduleRoutineEvent(actor.Id, state.NextShiftCheckIn, ScheduledKind.ShiftCheckIn);
            AdoptDailyRoster(actor); WakeRoutine(actor); WakeNeed(actor);
            if (source != EvidenceKind.Seeded) Emit("RosterRead", actor, messageId: sourceId, detail: "version=" + roster.Version);
            return true;
        }
        private void ScheduleRoutineEvent(Id<ActorTag> actor, long tick, ScheduledKind kind) => Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = kind, Intent = new ActionIntent(actor, ActionKind.Move, default) });
        private bool KnownOnDuty(Id<ActorTag> actor) => !routines.TryGetValue(actor, out var state) || state.KnownRoster == null || state.KnownRoster.OnDuty(Math.Max(0, processingTick));
        private bool RoutineAllowsCheckIn(Id<ActorTag> actor) => KnownOnDuty(actor) || routines[actor].EndShiftCheckIn;
        private void MarkRoutineCheckIn(Id<ActorTag> actor)
        {
            if (!checkIns.TryGetValue(actor, out var agent)) return;
            MarkCheckInDue(agent); RequestCheckInDecision(agent, processingTick);
            Emit("CheckInDue", actors[actor], detail: "known-roster/v1");
        }
        private void RoutineBoundary(Id<ActorTag> actorId)
        {
            var state = routines[actorId]; var roster = state.KnownRoster!; var actor = actors[actorId];
            state.OnDuty = roster.OnDuty(processingTick); state.NextBoundary = roster.NextBoundary(processingTick);
            SetRoutineActivity(actor, state, state.OnDuty ? "OnDuty" : actor.Action == null ? "AfterDutyCheckIn" : "FinishingAction");
            ScheduleRoutineEvent(actorId, state.NextBoundary, ScheduledKind.RoutineBoundary);
            state.EndShiftCheckIn = !state.OnDuty;
            if (!state.OnDuty)
            {
                // Movement is stopped only on arrival at the next tile. Finite local
                // work may finish; the retained goal cannot start another work step.
                if (actor.Action?.Kind == ActionKind.Move && !EscortControls(actorId) && !TravellingForDailyNeed(actor) && !(needAgents.TryGetValue(actorId.Value, out var needAgent) && needAgent.ActiveActionId == actor.Action.Id.Value)) Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, CauseId = Emit("RoutineStopRequested", actor, detail: "off-duty-stop/v1; roster=" + roster.Version), Intent = new ActionIntent(actorId, ActionKind.Cancel, actor.Position) });
                MarkRoutineCheckIn(actorId);
            }
            WakeRoutine(actor); WakeOrderDecisions(actor); WakeAudits(actor); WakeNeed(actor);
            WakePatrol(actor); WakeGuard(actor);
            if (repairAgents.TryGetValue(actorId, out var repair)) RequestRepairDecision(repair, processingTick);
            Emit("DutyBoundary", actor, detail: state.OnDuty ? "OnDuty" : "OffDuty");
        }
        private void ShiftCheckIn(Id<ActorTag> actor)
        {
            var state = routines[actor]; state.NextShiftCheckIn = state.KnownRoster!.NextCheckIn(processingTick, false);
            ScheduleRoutineEvent(actor, state.NextShiftCheckIn, ScheduledKind.ShiftCheckIn);
            MarkRoutineCheckIn(actor);
        }
        private void WakeRoutine(Actor actor)
        {
            if (!routines.TryGetValue(actor.Id, out var state)) return;
            long tick = Math.Max(0, processingTick); if (processingPhase >= EventPhase.Decision) tick = checked(tick + 1);
            if (state.NextDecision >= 0 && state.NextDecision <= tick) return;
            foreach (var e in scheduled.ForActor(actor.Id.Value).Where(e => e.Kind == ScheduledKind.EvaluateRoutine).ToArray()) scheduled.Remove(e);
            state.NextDecision = tick; ScheduleRoutineEvent(actor.Id, tick, ScheduledKind.EvaluateRoutine);
        }
        private void EvaluateRoutine(Id<ActorTag> actorId)
        {
            var state = routines[actorId]; state.NextDecision = -1; var actor = actors[actorId]; var roster = state.KnownRoster;
            if (EscortControls(actorId)) { SetRoutineActivity(actor, state, "EscortProtocol"); return; }
            if (roster == null) { SetRoutineActivity(actor, state, "UnknownRoster"); return; }
            state.OnDuty = roster.OnDuty(processingTick);
            if (state.OnDuty) { SetRoutineActivity(actor, state, "OnDuty"); return; }
            if (actor.Action != null) { SetRoutineActivity(actor, state, "FinishingAction"); return; }
            if (DeferToNeed(actor) || DeferToDueGoal(actor)) { SetRoutineActivity(actor, state, "SeekingNeedRelief"); return; }
            if (DeferToOptionalRest(actor)) { SetRoutineActivity(actor, state, "SeekingAdditionalRest"); return; }
            if (!needAgents.ContainsKey(actorId.Value) && (reportAgents.TryGetValue(actorId, out var report) && report.Pending.Count > 0 || state.EndShiftCheckIn && checkIns.TryGetValue(actorId, out var checkIn) && checkIn.Due))
            { SetRoutineActivity(actor, state, "AfterDutyCheckIn"); return; }
            if (!roster.RestTile.HasValue) { SetRoutineActivity(actor, state, "MissingRestDestination"); EndSession(actorId, "OffDutyMissingRestDestination"); return; }
            if (actor.Position == roster.RestTile.Value) { SetRoutineActivity(actor, state, "OffDuty"); EndSession(actorId, "OffDuty"); return; }
            if (!SpendPlanningRequest()) { WakeRoutine(actor); return; }
            // A policy chooses a known destination. The ordinary planner and executor
            // still own travel cost, adjacency, action timing and arrival.
            var result = RoutinePlanning.PlanRest(ActorView(actorId), roster.RestTile.Value, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += result.Expansions;
            if (result.Outcome != PlanOutcome.PlanFound || result.Steps.Count == 0)
            { SetRoutineActivity(actor, state, result.Outcome == PlanOutcome.BudgetExhausted ? "RestPlanBudgetExhausted" : "NoKnownRestRoute"); if (result.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++; return; }
            PlansFound++; SetRoutineActivity(actor, state, "GoingOffDuty");
            long cause = Emit("PlanSelected", actor, detail: "off-duty/v1; roster=" + roster.Version + "; cost=" + result.Cost);
            Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.Command, Value = DecisionSource(actor, DueSource.None), Intent = result.Steps[0].Intent, CauseId = cause });
        }
        private void RoutineCheckInFinished(Actor actor, bool failed)
        {
            if (!routines.TryGetValue(actor.Id, out var state)) return;
            state.EndShiftCheckIn = false;
            if (failed && !KnownOnDuty(actor.Id) && checkIns.TryGetValue(actor.Id, out var checkIn)) checkIn.Due = false;
            WakeRoutine(actor);
        }
        private void SetRoutineActivity(Actor actor, RoutineSnapshot state, string activity)
        {
            if (state.Activity == activity) return;
            state.Activity = activity; Emit("RoutineActivityChanged", actor, detail: activity + "; roster=" + (state.KnownRoster?.Version ?? 0));
        }
    }
}
