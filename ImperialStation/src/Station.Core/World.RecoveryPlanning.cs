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
        private bool RepairCredentialBlocked(Actor actor, long machine) => publicKnowledge?.Work?.Equipment.FirstOrDefault(m => m.Id == machine)?.RequiresCredential == true && PrepareCredentialGoal(actor);
        private void DiscoverMissingCredential(Actor actor, string reason)
        {
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || agent.Pending) return;
            var own = actor.Knowledge.Entries.Values.Select(e => e.Fact).Where(f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.Attribute == AttributeKind.CredentialCard && f.Value.Kind == FactValueKind.Number && credentials.TryGetValue(f.Key.SubjectId, out var registered) && registered.Definition.Holder == actor.Id && registered.Definition.Card.Value == f.Value.Number).OrderByDescending(f => f.Key.SubjectId).FirstOrDefault();
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
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || !agent.Pending || agent.RetryAt > NeedNow || KnownUrgentOrder(actor.Id) && !agent.UrgentCredentialBlocked) return false;
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
        private IReadOnlyList<PublicRecoveryDesk> KnownRecoveryDesks(IActorView view) => recoveryDesks.Values.Where(d => view.PublicKnowledge?.Work != null &&
            view.PublicKnowledge.Work.Recipes.Any(r => r.Id == d.RecipeId) && view.PublicKnowledge.Work.Workstations.Any(w => w.Id == d.WorkstationId && view.PublicKnowledge.Services.Any(t => t.Id == d.TerminalId && t.Position == w.Position)))
            .Select(d => new PublicRecoveryDesk(d.TerminalId, view.PublicKnowledge!.Services.Single(t => t.Id == d.TerminalId).Position, view.PublicKnowledge.Work!.Recipes.Single(r => r.Id == d.RecipeId).Duration)).ToArray();
        private void EvaluateRecovery(Id<ActorTag> actorId)
        {
            var agent = recoveryAgents[actorId.Value]; agent.NextDecision = -1; var actor = actors[actorId];
            if (!agent.Pending || actor.Action != null || agent.PlannedTick == processingTick || EscortControls(actorId) || DeferToNeed(actor) || KnownUrgentOrder(actorId) && !agent.UrgentCredentialBlocked) return;
            if (agent.RetryAt > processingTick) { RequestRecoveryDecision(agent, agent.RetryAt); return; }
            if (!SpendPlanningRequest()) { RequestRecoveryDecision(agent, checked(processingTick + 1)); return; }
            var view = ActorView(actorId); bool held = CredentialPlanning.KnownUsableCredential(view);
            bool alternate = recoveryCapabilities.TryGetValue(actorId.Value, out var capability) && capability.Retained && capability.AccountId == actor.Account.Value;
            var memory = new RecoveryPlanningState(agent.OldCredentialId, agent.OldCardId, alternate, agent.PostedTick >= 0, agent.KnownCaseId > 0, agent.IssuedCardId,
                sessions.TryGetValue(actorId, out var session) && session.Scope == SessionScope.CredentialRecovery ? session.Terminal : 0, agent.TerminalRetry.Where(t => t.Value > processingTick).Select(t => t.Key).Union(KnownTerminalOutages(actorId)).OrderBy(t => t).ToArray());
            var plan = held ? ReportPlanning.PlanInbox(view, Information!.LoginTicks, Information.ReadBaseTicks, planner.Candidates, planner.MaximumDepth, planner.NodeBudget, KnownTerminalOutages(actorId)) :
                CredentialPlanning.Plan(view, memory, KnownRecoveryDesks(view), RecoveryIdentityCheckTicks, Information!.PostTicks, Information.ReadBaseTicks, Information.LatencyTicks, Config.ItemActionTicks, Math.Min(6, planner.Candidates), planner.MaximumDepth, planner.NodeBudget);
            PlannerExpansions += plan.Expansions; agent.LastOutcome = plan.Outcome; agent.LastCost = plan.Cost;
            if (plan.Outcome == PlanOutcome.PlanFound && plan.Steps.Count > 0)
            {
                var intent = plan.Steps[0].Intent;
                if (intent.Kind == ActionKind.PickUp && intent.Item.Value == 0) throw new InvalidOperationException("Unbound recovery output cannot execute.");
                PlansFound++; agent.Reason = ""; agent.PlannedTick = processingTick; agent.PlannedKind = intent.Kind; agent.PlannedTerminal = intent.ServiceId;
                long cause = Emit("PlanSelected", actor, causeId: agent.DiscoveryCauseId, detail: "restore-credential/v1; cost=" + plan.Cost + "; " + string.Join(" -> ", plan.Steps.Select(s => s.Name)));
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.RecoveryCommand, Intent = intent, CauseId = cause }); return;
            }
            if (plan.Outcome == PlanOutcome.BudgetExhausted) PlansExhausted++; else PlansUnavailable++;
            RetryRecovery(agent, plan.Outcome == PlanOutcome.BudgetExhausted ? "PlanBudgetExhausted" : !alternate ? "MissingRecoveryCapabilityOrKnownCard" : "NoKnownRecoveryRouteOrService");
        }
        private void StartRecoveryIntent(ScheduledEvent e)
        {
            var actor = actors[e.Intent.Actor]; var agent = recoveryAgents[actor.Id.Value];
            if (!agent.Pending || actor.Action != null || agent.RetryAt > processingTick || EscortControls(actor.Id) || DeferToNeed(actor) || KnownUrgentOrder(actor.Id) && !agent.UrgentCredentialBlocked) { RequestRecoveryDecision(agent, checked(processingTick + 1)); return; }
            agent.ActiveActionId = nextAction; Start(e.Intent, e.CauseId);
        }
        private void RetryRecovery(RecoveryAgentSnapshot agent, string reason)
        {
            agent.Reason = reason; agent.RetryAt = checked(processingTick + agent.Backoff); agent.Backoff = NextWorkBackoff(agent.Backoff);
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
                // Reaching another desk does not establish that recovery service
                // works there. Preserve failure backoff across travel so a large
                // station cannot turn an outage into a rapid tour of every desk.
                if (action.Kind != ActionKind.Move) agent.Backoff = planner.InitialBackoff;
                if (action.Kind == ActionKind.PostLostCard) { agent.PostedTick = processingTick; agent.RetryAt = checked(processingTick + 2 * Information!.LatencyTicks); agent.Reason = "AwaitingRecoveryReply"; }
                if (action.Kind == ActionKind.ReadRecoveryInbox && agent.KnownCaseId == 0) { RetryRecovery(agent, "NoReadRecoveryAuthorization"); return; }
                if (action.Kind == ActionKind.ReadInbox)
                {
                    if (action.CapturedMessages.Any(id => messages[id].Stage != MessageStage.Read)) { RetryRecovery(agent, "CurrentInboxNotFullyRead"); return; }
                    agent.Pending = false; agent.Completed++; agent.Reason = ""; agent.NextDecision = -1;
                    foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateRecovery && e.Intent.Actor == actor.Id).ToArray()) scheduled.Remove(e);
                    agent.CompletionCauseId = Emit("CredentialRecoveryCompleted", actor, action.Id, causeId: action.CauseId, detail: "CurrentOrdersRead"); return;
                }
            }
            RequestRecoveryDecision(agent, NeedNow);
        }
        private void RecoveryActionFailed(Actor actor, Id<ActionTag> action, FailureReason reason)
        {
            if (reason is FailureReason.MissingCredential or FailureReason.CredentialRevoked) DiscoverMissingCredential(actor, reason.ToString());
            if (!recoveryAgents.TryGetValue(actor.Id.Value, out var agent) || !agent.Pending || agent.ActiveActionId != action.Value) return;
            agent.ActiveActionId = 0;
            if (reason == FailureReason.CredentialRevoked && agent.PlannedKind == ActionKind.Login && agent.IssuedCardId > 0)
            { agent.Pending = false; DiscoverMissingCredential(actor, "ReplacementCredentialRevoked"); }
            if (agent.PlannedTerminal > 0 && reason is FailureReason.RequiredServiceNotPresent or FailureReason.InsufficientMaterials or FailureReason.NotQualified or FailureReason.IdentityVerificationFailed)
                // Keep observed service failure beyond the next decision. Expiring
                // it at RetryAt made every retry choose the same failed desk.
                agent.TerminalRetry[agent.PlannedTerminal] = checked(processingTick + agent.Backoff + planner.MaximumBackoff);
            RetryRecovery(agent, reason == FailureReason.None ? "Interrupted" : reason.ToString());
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
