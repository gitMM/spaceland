using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<long, Terminal> terminals = new SortedDictionary<long, Terminal>();
        private readonly SortedDictionary<long, Credential> credentials = new SortedDictionary<long, Credential>();
        private readonly SortedDictionary<Id<ActorTag>, Session> sessions = new SortedDictionary<Id<ActorTag>, Session>();
        private readonly SortedDictionary<long, MessageSnapshot> messages = new SortedDictionary<long, MessageSnapshot>();
        private readonly SortedDictionary<FactKey, BeliefSnapshot> beliefs = new SortedDictionary<FactKey, BeliefSnapshot>();
        private readonly List<BeliefSnapshot> beliefHistory = new List<BeliefSnapshot>();
        private PublicKnowledge? publicKnowledge;
        private long nextFact = 1;
        private long nextMessage = 1;
        private long nextSession = 1;
        public InformationConfig? Information { get; private set; }
        public long KnowledgeEvictions { get; private set; }
        public long MaximumTerminalQueue { get; private set; }
        public long TotalTerminalWaitTicks { get; private set; }

        private void InitializeInformation(InformationSeed seed)
        {
            Information = seed.Config;
            perception = seed.Perception;
            if (actors.Values.Select(a => a.Account).Distinct().Count() != actors.Count) throw new ArgumentException("Each actor must have a distinct account in v1.");
            foreach (var t in seed.Terminals)
            {
                if (t.Id <= 0 || t.Capacity < 1 || !Topology.Contains(t.Position) || (t.Capabilities & ~(TerminalCapability.Inbox | TerminalCapability.Post | TerminalCapability.Records | TerminalCapability.CredentialRecovery)) != 0)
                    throw new ArgumentException("Invalid terminal ID, tile, capacity or capabilities.");
                terminals.Add(t.Id, new Terminal { Definition = t, Enabled = t.Enabled, Connected = t.Connected });
            }
            foreach (var c in seed.Credentials)
            {
                if (c.Id <= 0 || c.Generation < 1 || !actors.TryGetValue(c.Holder, out var actor) || actor.Account != c.Account || !items.ContainsKey(c.Card) || credentials.Values.Any(x => x.Definition.Card == c.Card))
                    throw new ArgumentException("Invalid credential ID, holder/account or card.");
                credentials.Add(c.Id, new Credential { Definition = c, Active = c.Active });
                if (items[c.Card].Location.Kind == LocationKind.MachineSlot) throw new ArgumentException("A registered credential card cannot be a consumable machine cartridge.");
                var holder = actors[c.Holder];
                if (!Learn(holder, new FactKey(SubjectKind.Credential,c.Id,AttributeKind.CredentialCard),FactValue.Integer(c.Card.Value),EvidenceKind.Seeded,0,-1,0,true)
                    || !Learn(holder,new FactKey(SubjectKind.Credential,c.Id,AttributeKind.CredentialStatus),FactValue.Boolean(c.Active),EvidenceKind.Seeded,0,-1,0,true)) throw new ArgumentException("Knowledge capacity cannot hold own credential facts.");
            }
            publicKnowledge = seed.Publication ?? new PublicKnowledge(1, Topology.Tiles.Values.Where(t => t.Completed).Select(t => new KnownTile(t.Id, t.Position, Topology.TraversalTicks(t.Position))),
                terminals.Values.Select(t => new PublicService(t.Definition.Id, t.Definition.Position, t.Definition.Capabilities, t.Definition.PermittedRoles)), PublishWork());
            foreach (var fact in seed.Facts)
            {
                if (!actors.TryGetValue(fact.Actor, out var actor)) throw new ArgumentException("Seeded fact has unknown actor.");
                if (!Learn(actor, fact.Key, fact.Value, EvidenceKind.Seeded, 0, -1, 0, fact.Pinned)) throw new ArgumentException("Seeded knowledge exceeds pinned capacity.");
            }
            foreach (var actor in actors.Values) Schedule(new ScheduledEvent { Tick = 0, Phase = EventPhase.Observation, Kind = ScheduledKind.Sense, Intent = new ActionIntent(actor.Id, ActionKind.Inspect, actor.Position) });
            foreach (var initial in seed.Messages)
            {
                if (initial.DeliveryTick < 0 || initial.Draft.Kind is MessageKind.LostCardReport or MessageKind.RecoveryReply or MessageKind.RosterUpdated or MessageKind.PatrolOrder or MessageKind.CaseDecision or MessageKind.DetentionDecision or MessageKind.EscortOrder or MessageKind.DetentionIntakeReceipt or MessageKind.DetentionReleaseReceipt or MessageKind.DetentionReleaseNotice or MessageKind.EscortRevoked || !ValidDraft(initial.Draft) || (!CitizenCanPost(initial.Draft.Kind)&&initial.Sender.Value!=0)) throw new ArgumentException("Invalid initial message, authority provenance or delivery tick.");
                var message = new MessageSnapshot { Id = nextMessage++, Draft = initial.Draft, SenderAccount = initial.Sender, SubmittedTick = -1, DeliveryTick = initial.DeliveryTick, Stage = MessageStage.Posted, Seeded = true };
                messages.Add(message.Id, message); Schedule(new ScheduledEvent { Tick = initial.DeliveryTick, Phase = EventPhase.Delivery, Kind = ScheduledKind.MessageDelivery, Value = message.Id });
            }
            InitializeReportGoals(seed.ReportGoals, seed.Planner);
            InitializeRepairGoals(seed.RepairGoals);
            InitializeRecords(seed.RecordAccess);
            InitializeSensors(seed.Sensors);
            InitializeCredentialRecovery(seed.Recovery);
        }
        private bool Learn(Actor actor, FactKey key, FactValue value, EvidenceKind source, long sourceId, long observed, long cause, bool pin = false,bool preservePin = true, long sourceActorId = 0)
        {
            if (Information == null) return true;
            var store = actor.Knowledge;
            if (store.Entries.TryGetValue(key, out var previous)) { if(preservePin)pin |= previous.Fact.Pinned; }
            else if (store.Entries.Count >= Information.KnowledgeCapacity)
            {
                var victim = store.Entries.Values.Where(e => !e.Fact.Pinned).OrderBy(e => e.Recency).ThenBy(e => e.Fact.Key).FirstOrDefault();
                if (victim == null) { Emit("KnowledgeCapacity", actor, detail: key.ToString(), causeId: cause); return false; }
                store.Entries.Remove(victim.Fact.Key); KnowledgeEvictions++;
                Emit("KnowledgeEvicted", actor, detail: victim.Fact.Key.ToString(), causeId: cause,factKey:victim.Fact.Key);
            }
            long now = processingTick < 0 ? LastProcessedTick : processingTick;
            var fact = new KnowledgeFact(nextFact++, key, value, source, sourceId, observed, now, pin, sourceActorId);
            store.Entries[key] = new KnownEntry { Fact = fact, Recency = ++store.Sequence };
            if (source != EvidenceKind.Seeded) Emit("ObservationRecorded", actor, detail: key + " = " + value + "; source=" + source, causeId: cause, messageId: source == EvidenceKind.MessageRead ? sourceId : 0,factKey:key,factValue:value);
            if (source != EvidenceKind.Seeded && (previous == null || !previous.Fact.Value.Equals(value))) OnRepairKnowledgeChanged(actor, key);
            FacilityObserved(actor, fact, cause);
            return true;
        }
        private void ObserveFailedPickup(Actor actor, ActiveAction action, FailureReason failure)
        {
            if (Information == null || action.Kind != ActionKind.PickUp || failure != FailureReason.MissingItem || action.Item.Value == 0) return;
            Learn(actor, new FactKey(SubjectKind.Item, action.Item.Value, AttributeKind.Location), FactValue.Absent(actor.Position), EvidenceKind.ActionFailure, action.Id.Value, processingTick, action.CauseId);
        }
        private void ScheduleSensing(Actor actor, long cause)
        {
            if (Information != null) Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Observation, Kind = ScheduledKind.Sense,
                Intent = new ActionIntent(actor.Id, ActionKind.Inspect, actor.Position), CauseId = cause });
        }
        private void Sense(Actor actor, long cause)
        {
            // Called only by scheduled arrival/initial sensing, never by getters or rendering.
            var tile = Topology.Tiles[actor.Position];
            ObserveDirt(actor, true, tile.Id.Value, cause); ObserveConstructedServices(actor, cause);
            ObservePerceived(actor, new FactKey(SubjectKind.Tile, tile.Id.Value, AttributeKind.TraversalTicks), FactValue.Integer(Topology.TraversalTicks(actor.Position)), EvidenceKind.Direct, tile.Id.Value, cause, true);
            ObservePerceived(actor, new FactKey(SubjectKind.Tile, tile.Id.Value, AttributeKind.Location), FactValue.At(actor.Position), EvidenceKind.Direct, tile.Id.Value, cause, true);
            foreach (var item in AvailableItemsAt(actor.Position))
            {
                ObservePerceived(actor, new FactKey(SubjectKind.Item, item.Id.Value, AttributeKind.Location), FactValue.At(actor.Position), EvidenceKind.Direct, tile.Id.Value, cause, true);
                ObserveItemDescription(actor, item, cause, passive: true);
            }
            foreach (var item in ItemsAt(LocationKind.ActorInventory, actor.Id.Value)) ObserveItemDescription(actor, item, cause, passive: true);
            foreach (var machine in machines.Values.Where(m => m.Position == actor.Position)) ObserveMachine(actor, machine, false, sensingCause: cause);
            if (perception.Mode(SubjectKind.Actor, AttributeKind.Location) == PerceptionMode.Passive)
                foreach (var other in actors.Values.Where(a => a.Id != actor.Id && a.Position == actor.Position && a.Action?.Kind != ActionKind.Move))
                    ObservePerceived(actor, new FactKey(SubjectKind.Actor, other.Id.Value, AttributeKind.Location), FactValue.At(actor.Position), EvidenceKind.Direct, tile.Id.Value, cause, true);
        }
        private void ObserveOwnTransfer(Actor actor, Item item, long cause)
        {
            if (Information == null) return;
            // Inventory is exposed separately. A known physical tile is a last observation, never a remote tracker.
            Learn(actor, new FactKey(SubjectKind.Item, item.Id.Value, AttributeKind.Location), FactValue.At(actor.Position), EvidenceKind.Direct, actor.Id.Value, processingTick, cause);
            ObserveItemDescription(actor, item, cause);
        }
        public IReadOnlyList<KnowledgeFact> InspectKnowledge(Id<ActorTag> actor) => actors[actor].Knowledge.Inspect();
        public IReadOnlyList<MessageSnapshot> InspectMailbox(Id<AccountTag> account) => messages.Values.Where(m => m.Draft.RecipientKind == RecipientKind.Account && m.Draft.Recipient == account && m.Stage != MessageStage.Posted).Select(m => m.Copy()).ToArray();
        public IReadOnlyList<MessageSnapshot> InspectMessages() => messages.Values.Select(m => m.Copy()).ToArray();
        public IReadOnlyList<BeliefSnapshot> InspectBeliefs() => beliefs.Values.Select(b => b.Copy()).ToArray();
        public IReadOnlyList<BeliefSnapshot> InspectBeliefHistory() => beliefHistory.Select(b => b.Copy()).ToArray();
        public IReadOnlyList<SessionSnapshot> InspectSessions() => sessions.Values.Select(s => new SessionSnapshot { Id = s.Id, ActorId = s.Actor.Value, AccountId = s.Account.Value, TerminalId = s.Terminal, Expires = s.Expires, Scope = s.Scope }).ToArray();
        public IReadOnlyList<TerminalSnapshot> InspectTerminals() => terminals.Values.Select(t => new TerminalSnapshot { Id = t.Definition.Id, Position = t.Definition.Position, Enabled = t.Enabled, Connected = t.Connected, Capacity = t.Definition.Capacity, Claims = t.Claims.Select(a => a.Value).ToArray(), Queue = t.Queue.Select(a => a.Value).ToArray() }).ToArray();

        public void ScheduleTerminalChange(long tick, long terminalId, bool enabled, bool connected)
        {
            ValidateFuture(tick); if (!terminals.ContainsKey(terminalId)) throw new ArgumentException("Unknown terminal.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.TerminalChange, ActionId = terminalId, Value = (enabled ? 1 : 0) | (connected ? 2 : 0) });
        }
        public void ScheduleCredentialRevocation(long tick, long credentialId)
        {
            ValidateFuture(tick); if (!credentials.ContainsKey(credentialId)) throw new ArgumentException("Unknown credential.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.CredentialRevocation, Value = credentialId });
        }
        private void ChangeTerminal(long id, bool enabled, bool connected)
        {
            var t = terminals[id]; t.Enabled = enabled; t.Connected = connected;
            Emit("TerminalStateChanged", terminalId: id, detail: $"enabled={enabled}; connected={connected}");
            if (!enabled || !connected) foreach (var actor in sessions.Values.Where(s => s.Terminal == id).Select(s => s.Actor).ToArray()) EndSession(actor, "TerminalUnavailable");
            if (enabled && connected) WakeTerminalQueue(t);
        }
        private void RevokeCredential(long id, long cause = 0)
        { credentials[id].Active = false; RecordCredential(credentials[id], "credential/revoked/" + id); Emit("CredentialRevoked", item: credentials[id].Definition.Card, causeId: cause, detail: id.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        private void RecordCredentialFailure(Actor actor, FailureReason reason, long cause)
        {
            if(Information==null || reason!=FailureReason.CredentialRevoked)return;
            foreach(var c in credentials.Values.Where(c=>c.Definition.Holder==actor.Id&&!c.Active))
            {
                var card=items[c.Definition.Card];
                if(card.Location.Kind==LocationKind.ActorInventory&&card.Location.Owner==actor.Id.Value)
                    Learn(actor,new FactKey(SubjectKind.Credential,c.Definition.Id,AttributeKind.CredentialStatus),FactValue.Boolean(false),EvidenceKind.ActionFailure,cause,processingTick,cause,true);
            }
        }

        private void ProcessIntentGroup(ScheduledEvent first, ref int count)
        {
            var group = new List<ScheduledEvent> { first };
            // Gather consecutive phase-6 commands. A single permutation orders claim requests;
            // prior-tick owners retain their claims and non-claim command positions stay fixed.
            foreach (var e in scheduled.TakeWhile(e => e.Tick == processingTick && e.Phase == EventPhase.Intent && (e.Kind is ScheduledKind.ExternalCommand or ScheduledKind.Command or ScheduledKind.NeedCommand or ScheduledKind.DailyCommand or ScheduledKind.RecoveryCommand)).ToArray())
            { if (++count > Config.MaxEventsPerTick) throw new InvalidOperationException($"Event cap exceeded at tick {processingTick}."); scheduled.Remove(e); group.Add(e); }
            var indexes = new List<int>();
            for (int i = 0; i < group.Count; i++)
            {
                if (RequestsClaim(group[i].Intent.Kind)) indexes.Add(i);
            }
            if (indexes.Count > 1)
            {
                var contenders = indexes.Select(i => group[i]).ToArray();
                ClaimArbitration.Permute(contenders, streams["arbitration"].Next);
                for (int i = 0; i < indexes.Count; i++) group[indexes[i]] = contenders[i];
            }
            foreach (var e in group)
                if (e.Kind == ScheduledKind.RecoveryCommand) StartRecoveryIntent(e); else if (e.Kind == ScheduledKind.NeedCommand) StartNeedIntent(e);
                else if (e.Kind == ScheduledKind.DailyCommand) StartDailyIntent(e);
                else if (MayStartDecision(e)) Start(e.Intent,e.CauseId, e.Kind == ScheduledKind.ExternalCommand ? ActionOrigin.ExternalCommand : ActionOrigin.Autonomous);
        }
        private FailureReason CredentialFor(Actor actor, out Credential? credential)
        {
            credential = null; bool revoked = false;
            foreach (var c in credentials.Values)
            {
                if (c.Definition.Holder != actor.Id || c.Definition.Account != actor.Account) continue;
                var item = items[c.Definition.Card]; if (item.Location.Kind != LocationKind.ActorInventory || item.Location.Owner != actor.Id.Value) continue;
                if (!c.Active) { revoked = true; continue; } credential = c; return FailureReason.None;
            }
            return revoked ? FailureReason.CredentialRevoked : FailureReason.MissingCredential;
        }
        private FailureReason TerminalAccess(Actor actor, long terminalId, TerminalCapability needed, out Terminal? terminal)
        {
            terminal = null;
            if (terminals.TryGetValue(terminalId, out terminal) && (!terminal.Enabled || !terminal.Connected)) { LearnTerminalOutage(actor, terminal); return FailureReason.RequiredServiceNotPresent; }
            if (terminal == null || (terminal.Definition.Capabilities & needed) != needed) return FailureReason.RequiredServiceNotPresent;
            if (actor.Position != terminal.Definition.Position || actor.Action?.Kind == ActionKind.Move) return FailureReason.NotAtRequiredTile;
            if (!terminal.Definition.PermittedRoles.Contains(actor.Role, StringComparer.Ordinal)) return FailureReason.NotQualified;
            return FailureReason.None;
        }
        private FailureReason ValidSession(Actor actor, ActiveAction action, TerminalCapability capability, out Session? session)
        {
            session = null;
            var failure = TerminalAccess(actor, action.ServiceId, capability, out _); if (failure != FailureReason.None) return failure;
            if (!sessions.TryGetValue(actor.Id, out session) || session.Terminal != action.ServiceId || session.Account != actor.Account || session.Expires <= processingTick || (action.SessionId != 0 && session.Id != action.SessionId)) return FailureReason.SessionRequired;
            if (session.Scope != SessionScope.Normal) return FailureReason.RecoverySessionRestricted;
            var c = credentials[session.Credential]; var card = items[c.Definition.Card];
            if (!c.Active) return FailureReason.CredentialRevoked;
            return card.Location.Kind == LocationKind.ActorInventory && card.Location.Owner == actor.Id.Value ? FailureReason.None : FailureReason.MissingCredential;
        }
        private void StartInformationAction(Actor actor, ActiveAction action)
        {
            if (Information == null) { Fail(actor, action.Id, FailureReason.RequiredServiceNotPresent); return; }
            FailureReason failure; long duration;
            if (action.Kind is ActionKind.Inspect or ActionKind.InspectTile or ActionKind.InspectActor)
            {
                if (action.Kind == ActionKind.Inspect && action.Item.Value == 0) { Fail(actor, action.Id, FailureReason.UnknownRequiredFact); return; }
                if (action.Kind == ActionKind.InspectActor && action.TargetActor.Value == 0) { Fail(actor, action.Id, FailureReason.UnknownRequiredFact); return; }
                duration = Information.InspectTicks;
            }
            else if (IsLogin(action.Kind))
            {
                failure = TerminalAccess(actor, action.ServiceId, TerminalCapability.None, out var terminal);
                if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
                Credential? credential = null; failure = action.Kind == ActionKind.RecoveryLogin ? CheckRecoveryIdentity(actor, action) : CredentialFor(actor, out credential); if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
                EndSession(actor.Id, "NewLogin"); actor.Action = action; actor.LastStatus = ActionStatus.Running; action.CredentialId = credential?.Definition.Id ?? 0;
                var t = terminal!;
                if (t.Claims.Count >= t.Definition.Capacity || t.Queue.Count > 0)
                {
                    action.Waiting = true; actor.LastFailure = FailureReason.ServiceBusy; t.Queue.Add(actor.Id); MaximumTerminalQueue = Math.Max(MaximumTerminalQueue, t.Queue.Count);
                    Emit("TerminalQueued", actor, action.Id, terminalId: action.ServiceId, causeId: action.CauseId); return;
                }
                t.Claims.Add(actor.Id); duration = LoginDuration(action.Kind);
            }
            else
            {
                var capability = InformationCapability(action.Kind);
                failure = ValidSession(actor, action, capability, out var session); if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
                action.SessionId = session!.Id;
                if (action.Kind == ActionKind.ReadInbox)
                {
                    action.CapturedMessages = UnreadMessages(RecipientKind.Account, actor.Account.Value).ToList();
                    duration = Math.Min(Information.ReadMaximumTicks, checked(Information.ReadBaseTicks + Information.ReadPerMessageTicks * action.CapturedMessages.Count));
                }
                else if (action.Kind == ActionKind.QueryRecords && action.Query != null)
                {
                    action.CapturedRecords = RecordsFor(actor, action.ServiceId, action.Query.Kind).Query(action.Query);
                    duration = Information.QueryTicks;
                }
                else if (action.Kind == ActionKind.PostMessage && action.Message != null && CitizenCanPost(action.Message.Kind) && ValidDraft(action.Message) && action.Message.ObservedTick <= processingTick)
                    duration = Information.PostTicks;
                else { Fail(actor, action.Id, FailureReason.InvalidMessage); return; }
            }
            actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None; action.Completes = checked(processingTick + duration);
            Emit("ActionStarted", actor, action.Id, target: actor.Position, arrival: action.Completes, detail: action.Kind.ToString(), causeId: action.CauseId, terminalId: action.ServiceId, sessionId: action.SessionId);
            ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteInformationAction(Actor actor, ActiveAction action)
        {
            FailureReason failure;
            if (action.Kind == ActionKind.InspectActor)
            {
                bool present = actors.TryGetValue(action.TargetActor, out var target) && target.Position == actor.Position && target.Action?.Kind != ActionKind.Move;
                var value = present ? FactValue.At(actor.Position) : FactValue.Absent(actor.Position);
                if (!Learn(actor, new FactKey(SubjectKind.Actor, action.TargetActor.Value, AttributeKind.Location), value, EvidenceKind.Inspection, action.Id.Value, processingTick, action.CauseId))
                { actor.Action = null; Fail(actor, action.Id, FailureReason.KnowledgeCapacity); return; }
                PatrolObserved(actor, action, value);
                GuardTargetObserved(actor, action, value);
            }
            else if (action.Kind == ActionKind.InspectTile)
            {
                ObserveDirt(actor, false, action.Id.Value, action.CauseId);
                var tile = Topology.Tiles[actor.Position];
                ObservePerceived(actor, new FactKey(SubjectKind.Tile, tile.Id.Value, AttributeKind.Location), FactValue.At(tile.Position), EvidenceKind.Inspection, action.Id.Value, action.CauseId, false);
                ObservePerceived(actor, new FactKey(SubjectKind.Tile, tile.Id.Value, AttributeKind.TraversalTicks), FactValue.Integer(Topology.TraversalTicks(tile.Position)), EvidenceKind.Inspection, action.Id.Value, action.CauseId, false);
            }
            else if (action.Kind == ActionKind.Inspect)
            {
                bool present = items.TryGetValue(action.Item, out var item) && (ItemAvailableAt(item, actor.Position) || HeldBy(item, actor));
                Learn(actor, new FactKey(SubjectKind.Item, action.Item.Value, AttributeKind.Location), present ? FactValue.At(actor.Position) : FactValue.Absent(actor.Position), EvidenceKind.Inspection, action.Id.Value, processingTick, action.CauseId);
                if (present) ObserveItemDescription(actor, item!, action.CauseId, EvidenceKind.Inspection, action.Id.Value);
            }
            else if (IsLogin(action.Kind))
            {
                failure = TerminalAccess(actor, action.ServiceId, TerminalCapability.None, out var terminal);
                if (failure == FailureReason.None && action.Kind == ActionKind.RecoveryLogin) failure = CheckRecoveryIdentity(actor, action);
                if (failure == FailureReason.None && action.Kind == ActionKind.Login)
                {
                    var c = credentials[action.CredentialId]; var card = items[c.Definition.Card];
                    if (!c.Active) failure = FailureReason.CredentialRevoked;
                    else if (card.Location.Kind != LocationKind.ActorInventory || card.Location.Owner != actor.Id.Value) failure = FailureReason.MissingCredential;
                }
                if (failure != FailureReason.None) { ReleaseTerminalClaim(actor.Id, action.ServiceId); actor.Action = null; Fail(actor, action.Id, failure); return; }
                if (!terminal!.Claims.Contains(actor.Id)) throw new InvalidOperationException("Login completed without terminal capacity.");
                var session = new Session { Id = nextSession++, Actor = actor.Id, Account = actor.Account, Terminal = action.ServiceId, Credential = action.CredentialId, Scope = action.Kind == ActionKind.RecoveryLogin ? SessionScope.CredentialRecovery : SessionScope.Normal, Expires = checked(processingTick + Information!.SessionTicks) };
                sessions.Add(actor.Id, session);
                RecordSession(session, "Active");
                Schedule(new ScheduledEvent { Tick = session.Expires, Phase = EventPhase.PhysicalCompletion, Kind = ScheduledKind.SessionExpiry, Intent = new ActionIntent(actor.Id, ActionKind.Logout, actor.Position), Value = session.Id });
                Emit(session.Scope == SessionScope.Normal ? "TerminalLoginSucceeded" : "RecoveryLoginSucceeded", actor, action.Id, terminalId: session.Terminal, sessionId: session.Id, causeId: action.CauseId);
                ObserveTerminalRestored(actor, session.Terminal);
            }
            else
            {
                failure = ValidSession(actor, action, InformationCapability(action.Kind), out var session);
                if (failure != FailureReason.None) { actor.Action = null; EndSession(actor.Id, failure.ToString()); Fail(actor, action.Id, failure); return; }
                if (action.Kind == ActionKind.ReadInbox)
                {
                    foreach (long id in action.CapturedMessages)
                    {
                        var message = messages[id];
                        if (message.Draft.RecipientKind != RecipientKind.Account || message.Draft.Recipient != actor.Account || message.Stage != MessageStage.DeliveredToMailbox) throw new InvalidOperationException("Read snapshot contains an ineligible message.");
                        // Capacity is checked before disclosure; overflow stays unread instead of silently granting knowledge.
                        bool accepted = message.Draft.Kind == MessageKind.RosterUpdated ? ReadRoster(actor, message) : Learn(actor, message.Draft.Subject, message.Draft.Claim, EvidenceKind.MessageRead, id, message.Draft.ObservedTick, action.CauseId,
                            message.Draft.Kind is MessageKind.Order or MessageKind.PatrolOrder or MessageKind.EscortOrder,preservePin:message.Draft.Kind!=MessageKind.OrderCancelled);
                        if (!accepted) continue;
                        message.Stage = MessageStage.Read; message.FirstReadTick = processingTick;
                        RemoveUnreadMessage(message);
                        Emit("MessageRead", actor, action.Id, terminalId: session!.Terminal, sessionId: session.Id, messageId: id, causeId: action.CauseId, detail: message.Draft.Kind.ToString());
                        ReadWorkOrder(actor, message);
                        ReadPatrolOrder(actor, message);
                        ReadCaseDecision(actor, message);
                        ReadDetentionProtocol(actor, message);
                        ReadGuardOrder(actor, message); ReadGuardRevocation(actor, message);
                    }
                }
                else if (action.Kind == ActionKind.QueryRecords) CompleteRecordQuery(actor, action);
                else
                {
                    var message = new MessageSnapshot { Id = nextMessage++, Draft = action.Message!, PhysicalSender = actor.Id, SenderAccount = actor.Account,
                        TerminalId = session!.Terminal, SessionId = session.Id, SubmittedTick = processingTick, DeliveryTick = checked(processingTick + Information!.LatencyTicks), Stage = MessageStage.Posted };
                    messages.Add(message.Id, message);
                    long cause = Emit("MessagePosted", actor, action.Id, terminalId: session.Terminal, sessionId: session.Id, messageId: message.Id, causeId: action.CauseId, detail: message.Draft.Kind.ToString());
                    Schedule(new ScheduledEvent { Tick = message.DeliveryTick, Phase = EventPhase.Delivery, Kind = ScheduledKind.MessageDelivery, Value = message.Id, CauseId = cause });
                    WorkMessagePosted(actor, message);
                    PatrolMessagePosted(actor, message);
                    FacilityMessagePosted(actor, message);
                    PersonnelMessagePosted(actor, message);
                    DetentionMessagePosted(actor, message);
                    GuardMessagePosted(actor, message);
                }
            }
            Finish(actor);
        }
        private static TerminalCapability InformationCapability(ActionKind kind) => kind == ActionKind.ReadInbox ? TerminalCapability.Inbox : kind == ActionKind.QueryRecords ? TerminalCapability.Records : TerminalCapability.Post;
        private bool ValidDraft(MessageDraft draft)
        {
            if (!ValidConstructionSurveyDraft(draft) || !ValidRecoveryDraft(draft) || !ValidPersonnelDraft(draft) || !ValidDetentionDraft(draft) || !ValidEscortDraft(draft) || !ValidGuardOutcomeDraft(draft)) return false;
            if (draft.Kind == MessageKind.FacilityCondition && (draft.RecipientKind != RecipientKind.Authority || !FacilitySubject(draft.Subject) ||
                string.IsNullOrWhiteSpace(draft.OperationKey) || draft.TaskId != 0 || draft.AttemptId != 0 || draft.WorkOrder != null || draft.PatrolOrder != null || draft.Roster != null ||
                !(draft.Subject.SubjectKind == SubjectKind.Tile && draft.Claim.Kind == FactValueKind.Number && draft.Claim.Number >= 0 || draft.Subject.SubjectKind == SubjectKind.Machine && draft.Claim.Equals(FactValue.Boolean(false))))) return false;
            if (draft.PatrolOrder != null && draft.Kind != MessageKind.PatrolOrder) return false;
            if (draft.Kind == MessageKind.PatrolOrder && (draft.PatrolOrder == null || draft.RecipientKind != RecipientKind.Account || draft.WorkOrder != null || draft.Roster != null || draft.AttemptId != 0)) return false;
            if (draft.Subject.Attribute == AttributeKind.RosterVersion && draft.Kind != MessageKind.RosterUpdated) return false;
            if (draft.Kind == MessageKind.RosterUpdated && (draft.Roster == null || draft.RecipientKind != RecipientKind.Account || !actors.TryGetValue(new Id<ActorTag>(draft.Roster.ActorId), out var owner) || owner.Account != draft.Recipient || !draft.Subject.Equals(new FactKey(SubjectKind.Actor, draft.Roster.ActorId, AttributeKind.RosterVersion)) || !draft.Claim.Equals(FactValue.Integer(draft.Roster.Version)))) return false;
            if (draft.Roster != null && draft.Kind != MessageKind.RosterUpdated) return false;
            if (draft.WorkOrder != null && (draft.Kind != MessageKind.Order || draft.RecipientKind != RecipientKind.Account || draft.TaskId < 1 || draft.AttemptId < 1 || draft.Subject.SubjectKind != SubjectKind.Attempt || draft.Subject.SubjectId != draft.AttemptId || draft.Subject.Attribute != AttributeKind.OrderStatus)) return false;
            if (!Enum.IsDefined(typeof(MessageKind), draft.Kind) || !Enum.IsDefined(typeof(RecipientKind), draft.RecipientKind) || draft.Subject.SubjectId <= 0 || !Enum.IsDefined(typeof(FactValueKind), draft.Claim.Kind)) return false;
            return draft.RecipientKind is RecipientKind.Authority or RecipientKind.CredentialService || actors.Values.Any(a => a.Account == draft.Recipient);
        }
        private static bool CitizenCanPost(MessageKind kind)=>kind is MessageKind.ConstructionSurvey or MessageKind.Observation or MessageKind.WorkCompleted or MessageKind.WorkBlocked or MessageKind.UnableToContinue or MessageKind.OrderAccepted or MessageKind.OrderRejected or MessageKind.Peer or MessageKind.PatrolAccepted or MessageKind.PatrolFound or MessageKind.PatrolNotLocated or MessageKind.PatrolBlocked or MessageKind.FacilityCondition or MessageKind.FileCase or MessageKind.RequestDetention or MessageKind.EscortAcknowledged or MessageKind.EscortOutcome;
        private void DeliverMessage(long id,long causeId)
        {
            var message = messages[id]; if (message.Stage != MessageStage.Posted) return;
            message.Stage = MessageStage.DeliveredToMailbox;
            IndexUnreadMessage(message);
            long cause = Emit("MailboxDelivered", messageId: id, terminalId: message.TerminalId, sessionId: message.SessionId, detail: message.Draft.RecipientKind.ToString(),causeId:causeId);
            if (message.Draft.RecipientKind == RecipientKind.CredentialService) { ReceiveLostCardReport(message, cause); return; } if (message.Draft.RecipientKind != RecipientKind.Authority) return;
            if (!ReceiveConstructionSurvey(message, cause) || !ReceiveDetentionServiceReceipt(message) || !ReceiveGuardOutcome(message) || !ReceiveGuardAcknowledgment(message) || !ReceiveDetentionRequest(message, cause) || !ReceiveCaseSubmission(message, cause) || !ReceivePatrolReport(message) || !ReceiveWorkReport(message) || !ReceiveFacilityReport(message, cause)) { message.Stage = MessageStage.Accepted; IndexAuthorityVisit(message); return; }
            // Authority processing uses only the submitted claim and its authentic transport metadata.
            if (message.Draft.Kind is MessageKind.ConstructionSurvey or MessageKind.Observation or MessageKind.FacilityCondition or MessageKind.WorkCompleted or MessageKind.WorkBlocked or MessageKind.UnableToContinue)
            {
                ReceiveBelief(message.Draft.Subject, message.Draft.Claim, message.Draft.ObservedTick, id, 0, 0, cause);
            }
            message.Stage = MessageStage.Accepted;
            IndexAuthorityVisit(message);
            RequestIncidentReview(processingTick);
        }
        private void GrantTerminal(Id<ActorTag> actorId, long terminalId,long actionId)
        {
            var actor = actors[actorId]; var action = actor.Action;
            if (action == null || action.Id.Value!=actionId || !action.Waiting || !IsLogin(action.Kind) || action.ServiceId != terminalId) return;
            var failure = TerminalAccess(actor, terminalId, TerminalCapability.None, out _);
            if (failure == FailureReason.None) failure = action.Kind == ActionKind.RecoveryLogin ? CheckRecoveryIdentity(actor, action) : CredentialFor(actor, out _);
            if (failure != FailureReason.None) { actor.Action = null; ReleaseTerminalClaim(actorId, terminalId); Fail(actor, action.Id, failure); return; }
            action.Waiting = false; actor.LastFailure = FailureReason.None; TotalTerminalWaitTicks = checked(TotalTerminalWaitTicks + processingTick - action.Started);
            action.Completes = checked(processingTick + LoginDuration(action.Kind)); ScheduleCompletion(actor, action.Completes);
            Emit("TerminalQueueGranted", actor, action.Id, terminalId: terminalId, arrival: action.Completes, causeId: action.CauseId);
        }
        private void WakeTerminalQueue(Terminal terminal)
        {
            if (!terminal.Enabled || !terminal.Connected) return;
            while (terminal.Claims.Count < terminal.Definition.Capacity && terminal.Queue.Count > 0)
            {
                var id = terminal.Queue[0]; terminal.Queue.RemoveAt(0); var actor = actors[id];
                if (actor.Action?.Waiting != true) continue;
                terminal.Claims.Add(id);
                Schedule(new ScheduledEvent { Tick = processingTick, Phase = EventPhase.Intent, Kind = ScheduledKind.GrantTerminal, Intent = new ActionIntent(id, actor.Action!.Kind, terminal.Definition.Position), Value = terminal.Definition.Id,ActionId=actor.Action!.Id.Value });
            }
        }
        private void ReleaseTerminalClaim(Id<ActorTag> actor, long terminalId)
        {
            if (!terminals.TryGetValue(terminalId, out var terminal)) return;
            terminal.Claims.Remove(actor); terminal.Queue.Remove(actor); WakeTerminalQueue(terminal);
        }
        private void EndSession(Id<ActorTag> actor, string reason)
        {
            if (!sessions.TryGetValue(actor, out var session)) return;
            sessions.Remove(actor); ReleaseTerminalClaim(actor, session.Terminal);
            RecordSession(session, "Ended");
            Emit("TerminalSessionEnded", actors[actor], terminalId: session.Terminal, sessionId: session.Id, detail: reason);
        }
        private void Logout(Actor actor, Id<ActionTag> request)
        {
            if (actor.Action != null && actor.Action.Kind != ActionKind.Move) CancelRunning(actor);
            EndSession(actor.Id, "Logout"); ActionsCompleted++; Emit("ActionCompleted", actor, request, detail: "Logout");
        }
        private void CancelAction(Actor actor, Id<ActionTag> request)
        {
            if (actor.Action != null && IsNeedAction(actor.Action.Kind) && CustodyNeedAllowed(actor, ActionKind.Cancel, 0)) { CancelRunning(actor); Emit("CancellationRequested", actor, request); return; } var escort = ActiveEscort(actor.Id); if (escort != null) { InterruptEscort(escort, "MemberCancelled"); Emit("CancellationRequested", actor, request); return; }
            if (actor.Action?.Kind == ActionKind.Move) actor.Action.CancelAtBoundary = true;
            else CancelRunning(actor);
            Emit("CancellationRequested", actor, request);
        }
        private void CancelRunning(Actor actor)
        {
            var action = actor.Action; if (action == null) return; if (action.Kind == ActionKind.Converse && CancelConversation(actor)) return;
            foreach (var e in scheduled.ForActor(actor.Id.Value).Where(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == action.Id.Value).ToArray()) scheduled.Remove(e);
            actor.Action = null; actor.LastStatus = ActionStatus.Cancelled; RecoveryActionFailed(actor, action.Id, FailureReason.None);
            ReleaseResourceClaims(action.Id.Value);
            ConstructionInterrupted(action); CancelDetentionOperation(action); ReleaseBerthReservation(actor, action); ReleaseNeedActivity(actor, action); DailyActionEnded(actor, action.Id.Value, FailureReason.None); NeedActionEnded(actor, action.Id.Value, FailureReason.None); WakeNeed(actor);
            if (IsLogin(action.Kind)) ReleaseTerminalClaim(actor.Id, action.ServiceId);
            Emit("ActionCancelled", actor, action.Id, causeId: action.CauseId, detail: action.Kind.ToString());
            OnActionFailed(actor);
        }
    }
}
