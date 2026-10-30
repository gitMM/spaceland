using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed class RecoveryAgentSnapshot
    {
        public long ActorId { get; internal set; }
        public bool Pending { get; internal set; }
        public long DiscoveredTick { get; internal set; } = -1;
        public long OldCredentialId { get; internal set; }
        public long OldCardId { get; internal set; }
        public long PostedTick { get; internal set; } = -1;
        public long KnownCaseId { get; internal set; }
        public long IssuedCardId { get; internal set; }
        public long NextDecision { get; internal set; } = -1;
        public long RetryAt { get; internal set; }
        public long Backoff { get; internal set; }
        public long ActiveActionId { get; internal set; }
        public long PlannedTick { get; internal set; } = -1;
        public long PlannedTerminal { get; internal set; }
        public ActionKind PlannedKind { get; internal set; }
        public bool UrgentCredentialBlocked { get; internal set; }
        // The citizen's own knowledge that it still holds its alternate identity proof.
        public bool RetainsAlternateIdentity { get; internal set; }
        public long Completed { get; internal set; }
        public long DiscoveryCauseId { get; internal set; }
        public long CompletionCauseId { get; internal set; }
        public string Reason { get; internal set; } = "";
        public PlanOutcome LastOutcome { get; internal set; }
        public long LastCost { get; internal set; }
        public IReadOnlyList<long> UnavailableTerminals { get; internal set; } = Array.Empty<long>();
        internal readonly SortedDictionary<long, long> TerminalRetry = new SortedDictionary<long, long>();
        internal RecoveryAgentSnapshot Copy()
        { var copy = (RecoveryAgentSnapshot)MemberwiseClone(); copy.UnavailableTerminals = TerminalRetry.Keys.ToArray(); return copy; }
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, RecoveryAgentSnapshot> recoveryAgents = new SortedDictionary<long, RecoveryAgentSnapshot>();
        // Privileged causal bookkeeping; never exposed through ActorView or used to select a goal.
        private readonly SortedDictionary<long, long> cardLossCauses = new SortedDictionary<long, long>();
        private long RestoredCredentialCause(Actor actor) => recoveryAgents.TryGetValue(actor.Id.Value, out var agent) && !agent.Pending ? agent.CompletionCauseId : 0;
        public IReadOnlyList<RecoveryAgentSnapshot> InspectRecoveryAgents() => recoveryAgents.Values.Select(a => a.Copy()).ToArray();
        private void InitializeRecoveryAgents(CredentialRecoverySeed seed)
        {
            foreach (long id in seed.AutonomousActors ?? actors.Keys.Select(id => id.Value).ToArray())
            {
                if (!actors.ContainsKey(new Id<ActorTag>(id)) || recoveryAgents.ContainsKey(id)) throw new ArgumentException("Recovery autonomy requires unique registered actors.");
                recoveryAgents.Add(id, new RecoveryAgentSnapshot { ActorId = id, Backoff = planner.InitialBackoff });
            }
        }
        private bool PrepareCredentialGoal(Actor actor)
        {
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent)) return false;
            if (!agent.Pending && !CredentialPlanning.KnownUsableCredential(ActorView(actor.Id))) DiscoverMissingCredential(actor, "InventoryCheck");
            if (!agent.Pending) return false;
            if (KnownUrgentOrder(actor.Id)) agent.UrgentCredentialBlocked = true;
            RequestRecoveryDecision(agent, NeedNow); return true;
        }
        private bool RepairCredentialBlocked(Actor actor, long machine) => RepairPolicy.RequiresCredential(publicKnowledge, machine) && PrepareCredentialGoal(actor);
        private void DiscoverMissingCredential(Actor actor, string reason)
        {
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || agent.Pending) return;
            // Credential facts are learned only by their holder (issue and recovery), so these are the citizen's own.
            var own = actor.Knowledge.Entries.Values.Select(e => e.Fact).Where(f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.Attribute == AttributeKind.CredentialCard && f.Value.Kind == FactValueKind.Number).OrderByDescending(f => f.Key.SubjectId).FirstOrDefault();
            if (own == null) return;
            agent.Pending = true; agent.DiscoveredTick = NeedNow; agent.OldCredentialId = own.Key.SubjectId; agent.OldCardId = own.Value.Number;
            agent.PostedTick = -1; agent.KnownCaseId = 0; agent.IssuedCardId = 0; agent.RetryAt = 0; agent.Backoff = planner.InitialBackoff; agent.TerminalRetry.Clear(); agent.Reason = reason;
            agent.UrgentCredentialBlocked = KnownUrgentOrder(actor.Id);
            cardLossCauses.TryGetValue(agent.OldCardId, out long lossCause);
            agent.DiscoveryCauseId = Emit("CredentialRecoveryNeeded", actor, item: new Id<ItemTag>(agent.OldCardId), causeId: lossCause, detail: reason + "; credential=" + agent.OldCredentialId);
            RequestRecoveryDecision(agent, NeedNow);
        }
        private bool DeferToRecovery(Actor actor)
        {
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || !RecoveryPolicy.Ready(agent.Pending, agent.RetryAt, NeedNow) || RecoveryPolicy.YieldsToUrgentOrder(KnownUrgentOrder(actor.Id), agent.UrgentCredentialBlocked)) return false;
            RequestRecoveryDecision(agent, NeedNow); return true;
        }
        private void RequestRecoveryDecision(RecoveryAgentSnapshot agent, long tick)
        {
            if (!agent.Pending) return;
            tick = Math.Max(tick, agent.RetryAt);
            if (processingTick >= 0 && (tick < processingTick || tick == processingTick && processingPhase >= EventPhase.Decision)) tick = checked(processingTick + 1);
            if (agent.NextDecision >= agent.RetryAt && agent.NextDecision >= 0 && agent.NextDecision <= tick) return;
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateRecovery && e.Intent.Actor.Value == agent.ActorId).ToArray()) scheduled.Remove(e);
            agent.NextDecision = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.EvaluateRecovery, Intent = new ActionIntent(new Id<ActorTag>(agent.ActorId), ActionKind.RecoveryLogin, default) });
        }
        private void EvaluateRecovery(Id<ActorTag> actorId)
        {
            var agent = recoveryAgents[actorId.Value]; agent.NextDecision = -1; var actor = actors[actorId];
            if (!agent.Pending || actor.Action != null || agent.PlannedTick == processingTick || EscortControls(actorId) || DeferToNeed(actor) || RecoveryPolicy.YieldsToUrgentOrder(KnownUrgentOrder(actorId), agent.UrgentCredentialBlocked)) return;
            if (agent.RetryAt > processingTick) { RequestRecoveryDecision(agent, agent.RetryAt); return; }
            if (!SpendPlanningRequest()) { RequestRecoveryDecision(agent, checked(processingTick + 1)); return; }
            var view = ActorView(actorId);
            bool alternate = agent.RetainsAlternateIdentity;
            var memory = new RecoveryPlanningState(agent.OldCredentialId, agent.OldCardId, alternate, agent.PostedTick >= 0, agent.KnownCaseId > 0, agent.IssuedCardId,
                sessions.TryGetValue(actorId, out var session) && session.Scope == SessionScope.CredentialRecovery ? session.Terminal : 0, agent.TerminalRetry.Where(t => t.Value > processingTick).Select(t => t.Key).ToArray());
            var timings = new CredentialRecoveryTimings(Information!.LoginTicks, Information.ReadBaseTicks, Information.PostTicks, Information.LatencyTicks, Config.ItemActionTicks, RecoveryIdentityCheckTicks);
            var plan = RecoveryPolicy.Plan(view, memory, () => view.PublicKnowledge?.RecoveryDesks ?? Array.Empty<PublicRecoveryDesk>(), timings, planner.Candidates, planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += plan.Expansions; agent.LastOutcome = plan.Outcome; agent.LastCost = plan.Cost;
            var first = RecoveryPolicy.FirstIntent(plan);
            if (first.HasValue)
            {
                var intent = first.Value;
                PlansFound++; agent.Reason = ""; agent.PlannedTick = processingTick; agent.PlannedKind = intent.Kind; agent.PlannedTerminal = intent.ServiceId;
                long cause = Emit("PlanSelected", actor, causeId: agent.DiscoveryCauseId, detail: "restore-credential/v1; cost=" + plan.Cost + "; " + string.Join(" -> ", plan.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.RecoveryCommand, Intent = intent, CauseId = cause }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
            RetryRecovery(agent, RecoveryPolicy.RetryReason(plan.Outcome, alternate));
        }
        private void StartRecoveryIntent(ScheduledEvent e)
        {
            var actor = actors[e.Intent.Actor]; var agent = recoveryAgents[actor.Id.Value];
            if (!agent.Pending || actor.Action != null || agent.RetryAt > processingTick || EscortControls(actor.Id) || DeferToNeed(actor) || RecoveryPolicy.YieldsToUrgentOrder(KnownUrgentOrder(actor.Id), agent.UrgentCredentialBlocked)) { RequestRecoveryDecision(agent, checked(processingTick + 1)); return; }
            agent.ActiveActionId = nextAction; Start(e.Intent, e.CauseId);
        }
        private void RetryRecovery(RecoveryAgentSnapshot agent, string reason)
        {
            agent.Reason = reason; agent.RetryAt = WorkBackoff.RetryTick(processingTick, agent.Backoff, true); agent.Backoff = NextWorkBackoff(agent.Backoff);
            Emit("CredentialRecoveryWaiting", actors[new Id<ActorTag>(agent.ActorId)], causeId: agent.DiscoveryCauseId, detail: reason + "; retry=" + agent.RetryAt);
            RequestRecoveryDecision(agent, agent.RetryAt); WakeDaily(actors[new Id<ActorTag>(agent.ActorId)]); WakeNeed(actors[new Id<ActorTag>(agent.ActorId)]);
        }
        private void RecoveryReceiptRead(Actor actor, RecoveryReplyPayload reply)
        {
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || !agent.Pending || agent.OldCredentialId != reply.OldCredentialId) return;
            agent.KnownCaseId = reply.CaseId; if (reply.IssuedCardId > 0) agent.IssuedCardId = reply.IssuedCardId;
        }
        private void RecoveryIssued(Actor actor, CredentialRecoveryCaseSnapshot recovery)
        {
            if (recoveryAgents.TryGetValue(actor.Id.Value, out var agent) && agent.Pending && agent.OldCredentialId == recovery.OldCredentialId) agent.IssuedCardId = recovery.IssuedCardId;
        }
        private void RecoveryActionCompleted(Actor actor, ActiveAction action)
        {
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || !agent.Pending) return;
            if (agent.ActiveActionId == action.Id.Value)
            {
                agent.ActiveActionId = 0; agent.RetryAt = 0;
                if (RecoveryPolicy.ResetsBackoff(action.Kind)) agent.Backoff = planner.InitialBackoff;
                // Whether this read left any captured message unread is the citizen's own result.
                bool unread = action.Kind == ActionKind.ReadInbox && action.LeftUnread;
                var progress = RecoveryPolicy.AfterAction(action.Kind, agent.KnownCaseId != 0, unread);
                if (progress == RecoveryProgress.AwaitReply) { agent.PostedTick = processingTick; agent.RetryAt = RecoveryPolicy.ReplyWaitUntil(processingTick, Information!.LatencyTicks); agent.Reason = RecoveryPolicy.AwaitingReplyReason; }
                if (progress == RecoveryProgress.RetryUnauthorized) { RetryRecovery(agent, "NoReadRecoveryAuthorization"); return; }
                if (progress == RecoveryProgress.RetryUnreadInbox) { RetryRecovery(agent, "CurrentInboxNotFullyRead"); return; }
                if (progress == RecoveryProgress.Complete)
                {
                    agent.Pending = false; agent.Completed++; agent.Reason = ""; agent.NextDecision = -1;
                    foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateRecovery && e.Intent.Actor == actor.Id).ToArray()) scheduled.Remove(e);
                    agent.CompletionCauseId = Emit("CredentialRecoveryCompleted", actor, action.Id, causeId: action.CauseId, detail: "CurrentOrdersRead"); return;
                }
            }
            RequestRecoveryDecision(agent, NeedNow);
        }
        private void RecoveryActionFailed(Actor actor, Id<ActionTag> action, FailureReason reason)
        {
            if (RecoveryPolicy.RevealsMissingCredential(reason)) DiscoverMissingCredential(actor, reason.ToString());
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || !agent.Pending || agent.ActiveActionId != action.Value) return;
            agent.ActiveActionId = 0;
            if (RecoveryPolicy.ReplacementRevoked(reason, agent.PlannedKind, agent.IssuedCardId))
            { agent.Pending = false; DiscoverMissingCredential(actor, "ReplacementCredentialRevoked"); }
            var unavailableUntil = RecoveryPolicy.DeskUnavailableUntil(reason, agent.PlannedTerminal, processingTick, agent.Backoff, planner.MaximumBackoff);
            if (unavailableUntil.HasValue) agent.TerminalRetry[agent.PlannedTerminal] = unavailableUntil.Value;
            RetryRecovery(agent, RecoveryPolicy.FailureRetryReason(reason));
        }
        private void WakeRecovery(Actor actor)
        { if (actor.Action == null && recoveryAgents.TryGetValue(actor.Id.Value, out var agent) && agent.Pending) RequestRecoveryDecision(agent, NeedNow); }
        public void ScheduleCardLoss(long tick, Id<ActorTag> actor, Id<ItemTag> card)
        {
            ValidateFuture(tick);
            if (!actors.TryGetValue(actor, out var owner) || OwnReportedCredential(owner, card) == null) throw new ArgumentException("CardLoss must name a registered holder and card.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.CardLoss, Intent = new ActionIntent(actor, ActionKind.PutDown, default, card) });
        }
        private void LoseCard(ActionIntent loss)
        {
            var actor = actors[loss.Actor]; var card = items[loss.Item];
            if (!HeldBy(card, actor)) { Emit("CardLossSkipped", actor, item: card.Id, detail: "NotHeld"); return; }
            MoveItem(card, new ItemLocation(LocationKind.TileContainer, Topology.Tiles[actor.Position].Id.Value));
            // Physical disturbance only: no sensing, discovery, revocation, report or wake.
            cardLossCauses[card.Id.Value] = Emit("CardLost", actor, item: card.Id, target: actor.Position);
        }
    }
}
