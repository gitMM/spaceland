using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<long, RecoveryCapabilitySeed> recoveryCapabilities = new SortedDictionary<long, RecoveryCapabilitySeed>();
        private readonly SortedDictionary<long, RecoveryDeskSeed> recoveryDesks = new SortedDictionary<long, RecoveryDeskSeed>();
        private readonly SortedDictionary<long, CredentialRecoveryCaseSnapshot> recoveryCases = new SortedDictionary<long, CredentialRecoveryCaseSnapshot>();
        private readonly SortedDictionary<long, long> recoveryCaseByCredential = new SortedDictionary<long, long>();
        private long nextRecoveryCase = 1;
        public long RecoveryIdentityCheckTicks { get; private set; }
        public IReadOnlyList<CredentialRecoveryCaseSnapshot> InspectCredentialRecoveryCases() => recoveryCases.Values.Select(c => c.Copy()).ToArray();
        public IReadOnlyList<MessageSnapshot> InspectRecoveryMailbox(Id<AccountTag> account) => messages.Values.Where(m => m.Draft.RecipientKind == RecipientKind.RecoveryAccount && m.Draft.Recipient == account && m.Stage != MessageStage.Posted).Select(m => m.Copy()).ToArray();

        private void InitializeCredentialRecovery(CredentialRecoverySeed? seed)
        {
            if (seed == null) return;
            RecoveryIdentityCheckTicks = seed.IdentityCheckTicks;
            foreach (var capability in seed.Capabilities)
            {
                if (!actors.TryGetValue(new Id<ActorTag>(capability.ActorId), out var actor) || capability.AccountId <= 0 || actor.Account.Value != capability.AccountId || recoveryCapabilities.ContainsKey(capability.ActorId))
                    throw new ArgumentException("Recovery capability must name a unique registered actor and their own account.");
                recoveryCapabilities.Add(capability.ActorId, capability);
            }
            foreach (var desk in seed.Desks) RegisterRecoveryDesk(desk); InitializeRecoveryAgents(seed);
        }

        private void RegisterRecoveryDesk(RecoveryDeskSeed desk)
        {
            if (!terminals.TryGetValue(desk.TerminalId, out var terminal) || (terminal.Definition.Capabilities & TerminalCapability.CredentialRecovery) == 0 ||
                !workstations.TryGetValue(desk.WorkstationId, out var station) || station.Position != terminal.Definition.Position || !recipes.TryGetValue(desk.RecipeId, out var recipe) ||
                recipe.Capability != station.Capability || recipe.Inputs.Count != 1 || recipe.Outputs.Count != 1 || recipe.Inputs[0].Quantity != 1 || recipe.Outputs[0].Quantity != 1 || recipe.Inputs[0].Resource == recipe.Outputs[0].Resource ||
                station.Access.ToolResource.Length != 0 || station.Access.Staff.Value != 0 || recoveryDesks.ContainsKey(desk.TerminalId))
                throw new ArgumentException("Recovery desk requires a local printer and a one-blank-to-one-card recipe without extra tool/staff dependencies.");
            recoveryDesks.Add(desk.TerminalId, desk);
        }

        private static bool IsLogin(ActionKind kind) => kind is ActionKind.Login or ActionKind.RecoveryLogin;
        private long LoginDuration(ActionKind kind) => kind == ActionKind.RecoveryLogin ? RecoveryIdentityCheckTicks : Information!.LoginTicks;
        private static bool IsRecoveryAction(ActionKind kind) => kind is ActionKind.PostLostCard or ActionKind.ReadRecoveryInbox or ActionKind.IssueReplacement;
        private FailureReason CheckRecoveryIdentity(Actor actor, ActiveAction action)
        {
            if (RecoveryIdentityCheckTicks == 0) return FailureReason.RequiredServiceNotPresent;
            var failure = TerminalAccess(actor, action.ServiceId, TerminalCapability.CredentialRecovery, out _);
            if (failure != FailureReason.None) return failure;
            if (action.Item.Value != 0 || action.TargetActor.Value != 0 || !recoveryCapabilities.TryGetValue(actor.Id.Value, out var capability) || !capability.Retained || capability.AccountId != actor.Account.Value)
                return FailureReason.IdentityVerificationFailed;
            return FailureReason.None;
        }

        private FailureReason ValidRecoverySession(Actor actor, ActiveAction action, out Session? session)
        {
            session = null;
            var failure = TerminalAccess(actor, action.ServiceId, TerminalCapability.CredentialRecovery, out _);
            if (failure != FailureReason.None) return failure;
            if (!sessions.TryGetValue(actor.Id, out session) || session.Scope != SessionScope.CredentialRecovery || session.Terminal != action.ServiceId || session.Account != actor.Account || session.Expires <= processingTick || action.SessionId != 0 && action.SessionId != session.Id)
                return FailureReason.SessionRequired;
            return FailureReason.None;
        }

        private Credential? OwnReportedCredential(Actor actor, Id<ItemTag> oldCard) => credentials.Values.FirstOrDefault(c => c.Definition.Card == oldCard && c.Definition.Holder == actor.Id && c.Definition.Account == actor.Account);
        private bool KnowsRecoveryCase(Actor actor, CredentialRecoveryCaseSnapshot recovery) => actor.Knowledge.Entries.TryGetValue(new FactKey(SubjectKind.Credential, recovery.OldCredentialId, AttributeKind.RecoveryCase), out var entry) &&
            entry.Fact.Value.Equals(FactValue.Integer(recovery.Id)) && entry.Fact.SourceKind == EvidenceKind.MessageRead && messages.TryGetValue(entry.Fact.SourceId, out var message) &&
            message.Stage == MessageStage.Read && message.Draft.RecipientKind == RecipientKind.RecoveryAccount && message.Draft.Recipient == actor.Account && message.Draft.RecoveryReply?.CaseId == recovery.Id;

        private void StartRecoveryAction(Actor actor, ActiveAction action)
        {
            if (Information == null || RecoveryIdentityCheckTicks == 0) { Fail(actor, action.Id, FailureReason.RequiredServiceNotPresent); return; }
            var failure = ValidRecoverySession(actor, action, out var session);
            if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            action.SessionId = session!.Id;
            action.RecipeId = 0; action.Message = null;
            long duration;
            if (action.Kind == ActionKind.ReadRecoveryInbox)
            {
                action.CapturedMessages = UnreadMessages(RecipientKind.RecoveryAccount, actor.Account.Value).ToList();
                duration = Math.Min(Information.ReadMaximumTicks, checked(Information.ReadBaseTicks + Information.ReadPerMessageTicks * action.CapturedMessages.Count));
            }
            else
            {
                var credential = OwnReportedCredential(actor, action.Item);
                if (credential == null) { Fail(actor, action.Id, FailureReason.UnknownRequiredFact); return; }
                action.CredentialId = credential.Definition.Id;
                if (action.Kind == ActionKind.PostLostCard)
                {
                    duration = Information.PostTicks;
                    action.Message = new MessageDraft(MessageKind.LostCardReport, RecipientKind.CredentialService, default, new FactKey(SubjectKind.Credential, action.CredentialId, AttributeKind.CredentialStatus), FactValue.Boolean(false), processingTick);
                }
                else
                {
                    failure = ValidateReplacementIssue(actor, action, true, out duration);
                    if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
                }
            }
            action.Completes = checked(processingTick + duration);
            if (action.Kind == ActionKind.IssueReplacement && action.RecipeId > 0)
            {
                AddClaim(ClaimKind.Workstation, recoveryDesks[action.ServiceId].WorkstationId, actor, action);
                foreach (var binding in action.Materials) AddClaim(ClaimKind.Item, binding.Item.Value, actor, action);
            }
            actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, action.Item, arrival: action.Completes, terminalId: action.ServiceId, sessionId: action.SessionId, detail: action.Kind.ToString(), causeId: action.CauseId);
            ScheduleCompletion(actor, action.Completes);
        }

        private void CompleteRecoveryAction(Actor actor, ActiveAction action)
        {
            var failure = ValidRecoverySession(actor, action, out _);
            if (failure == FailureReason.None && action.Kind == ActionKind.IssueReplacement) failure = ValidateReplacementIssue(actor, action, false, out _);
            if (failure != FailureReason.None) { ReleaseResourceClaims(action.Id.Value); actor.Action = null; Fail(actor, action.Id, failure); return; }
            if (action.Kind == ActionKind.PostLostCard)
            {
                var message = new MessageSnapshot { Id = nextMessage++, Draft = action.Message!, PhysicalSender = actor.Id, SenderAccount = actor.Account, TerminalId = action.ServiceId, SessionId = action.SessionId,
                    SubmittedTick = processingTick, DeliveryTick = checked(processingTick + Information!.LatencyTicks), Stage = MessageStage.Posted };
                messages.Add(message.Id, message);
                long cause = Emit("LostCardReportPosted", actor, action.Id, action.Item, terminalId: action.ServiceId, sessionId: action.SessionId, messageId: message.Id, causeId: action.CauseId);
                Schedule(new ScheduledEvent { Tick = message.DeliveryTick, Phase = EventPhase.Delivery, Kind = ScheduledKind.MessageDelivery, Value = message.Id, CauseId = cause });
            }
            else if (action.Kind == ActionKind.ReadRecoveryInbox)
            {
                foreach (long id in action.CapturedMessages)
                {
                    var message = messages[id];
                    if (message.Stage != MessageStage.DeliveredToMailbox || message.Draft.RecipientKind != RecipientKind.RecoveryAccount || message.Draft.Recipient != actor.Account) throw new InvalidOperationException("Invalid recovery read snapshot.");
                    if (!LearnRecoveryReply(actor, message, action.CauseId)) continue;
                    message.Stage = MessageStage.Read; message.FirstReadTick = processingTick;
                    RemoveUnreadMessage(message);
                    Emit("RecoveryReplyRead", actor, action.Id, terminalId: action.ServiceId, sessionId: action.SessionId, messageId: id, causeId: action.CauseId);
                }
            }
            else CommitReplacementIssue(actor, action);
            Finish(actor); AssertAccounting();
        }

        private static bool ValidRecoveryDraft(MessageDraft draft)
        {
            bool recoveryKind = draft.Kind is MessageKind.LostCardReport or MessageKind.RecoveryReply;
            if (!recoveryKind) return draft.RecoveryReply == null && draft.Subject.Attribute != AttributeKind.RecoveryCase && !(draft.RecipientKind is RecipientKind.CredentialService or RecipientKind.RecoveryAccount);
            if (draft.Subject.SubjectKind != SubjectKind.Credential || draft.TaskId != 0 || draft.AttemptId != 0 || draft.WorkOrder != null || draft.Roster != null || draft.PatrolOrder != null || draft.InspectionReport != null || draft.CaseSubmission != null || draft.CaseDecision != null || draft.DetentionRequest != null || draft.DetentionDecision != null || draft.EscortOrder != null) return false;
            if (draft.Kind == MessageKind.LostCardReport) return draft.RecipientKind == RecipientKind.CredentialService && draft.Recipient.Value == 0 && draft.RecoveryReply == null && draft.RelatedMessageId == 0 && draft.Subject.Attribute == AttributeKind.CredentialStatus && draft.Claim.Equals(FactValue.Boolean(false));
            return draft.RecipientKind == RecipientKind.RecoveryAccount && draft.RecoveryReply != null && draft.RelatedMessageId > 0 && draft.Subject.Attribute == AttributeKind.RecoveryCase && draft.Subject.SubjectId == draft.RecoveryReply.OldCredentialId && draft.Claim.Equals(FactValue.Integer(draft.RecoveryReply.CaseId));
        }

        private bool RecoveryTransportAuthentic(MessageSnapshot message) => !message.Seeded && message.PhysicalSender.Value > 0 && actors.TryGetValue(message.PhysicalSender, out var actor) && actor.Account == message.SenderAccount &&
            !records.History.Values.Any(r => r.Kind == RecordKind.TerminalSession && r.EntityId == message.SessionId && r.Tick < message.SubmittedTick && r.Fields.Any(f => f.Attribute == AttributeKind.WorkStatus && f.Value.Text == "Ended")) &&
            records.History.Values.Any(r => r.Kind == RecordKind.TerminalSession && r.EntityId == message.SessionId && r.AccountId == message.SenderAccount.Value && r.Tick <= message.SubmittedTick &&
                r.Fields.Any(f => f.Attribute == AttributeKind.ActorId && f.Value.Equals(FactValue.Integer(message.PhysicalSender.Value))) && r.Fields.Any(f => f.Attribute == AttributeKind.TerminalId && f.Value.Equals(FactValue.Integer(message.TerminalId))) &&
                r.Fields.Any(f => f.Attribute == AttributeKind.SessionScope && f.Value.Equals(FactValue.Integer((long)SessionScope.CredentialRecovery))) && r.Fields.Any(f => f.Attribute == AttributeKind.WorkStatus && f.Value.Text == "Active") &&
                r.Fields.Any(f => f.Attribute == AttributeKind.Expires && f.Value.Number > message.SubmittedTick));

        private void ReceiveLostCardReport(MessageSnapshot message, long cause)
        {
            if (!ValidRecoveryDraft(message.Draft) || message.Draft.Kind != MessageKind.LostCardReport || !RecoveryTransportAuthentic(message) ||
                !credentials.TryGetValue(message.Draft.Subject.SubjectId, out var credential) || credential.Definition.Holder != message.PhysicalSender || credential.Definition.Account != message.SenderAccount)
                throw new InvalidOperationException("Invalid authenticated recovery report.");
            if (!recoveryCaseByCredential.TryGetValue(credential.Definition.Id, out long caseId))
            {
                caseId = nextRecoveryCase++;
                var recovery = new CredentialRecoveryCaseSnapshot { Id = caseId, ActorId = message.PhysicalSender.Value, AccountId = message.SenderAccount.Value, OldCredentialId = credential.Definition.Id, FirstRequestMessageId = message.Id, AcceptedTick = processingTick };
                recoveryCases.Add(caseId, recovery); recoveryCaseByCredential.Add(credential.Definition.Id, caseId);
                RevokeCredential(credential.Definition.Id, cause); RecordRecoveryCase(recovery);
                Emit("LostCardReportAccepted", messageId: message.Id, causeId: cause, detail: "case=" + caseId + "; credential=" + credential.Definition.Id);
            }
            var current = recoveryCases[caseId];
            var position = current.IssuedCardId == 0 ? default : terminals[current.IssueTerminalId].Definition.Position;
            var payload = new RecoveryReplyPayload(caseId, current.OldCredentialId, current.IssuedCardId, current.IssuedCredentialId, current.IssuedTick, position);
            PostAuthorityWork(new MessageDraft(MessageKind.RecoveryReply, RecipientKind.RecoveryAccount, message.SenderAccount, new FactKey(SubjectKind.Credential, current.OldCredentialId, AttributeKind.RecoveryCase), FactValue.Integer(caseId), processingTick,
                relatedMessageId: message.Id, operationKey: "recovery-reply/" + message.Id, recoveryReply: payload), cause);
            message.Stage = MessageStage.Accepted;
        }

        private void RecordRecoveryCase(CredentialRecoveryCaseSnapshot recovery)
        {
            records.Append(RecordKind.CredentialRecovery, recovery.Id, recovery.AccountId, processingTick, "credential-recovery/" + recovery.Id + "/" + (recovery.IssuedCardId == 0 ? "authorized" : "issued"), new[] {
                Number(AttributeKind.ActorId, recovery.ActorId), Number(AttributeKind.RecoveryCase, recovery.OldCredentialId), Number(AttributeKind.MessageId, recovery.FirstRequestMessageId),
                Text(AttributeKind.CaseStatus, recovery.IssuedCardId == 0 ? "Authorized" : "Issued"), Number(AttributeKind.CredentialCard, recovery.IssuedCardId), Number(AttributeKind.ObservedTick, recovery.IssuedTick) });
            recovery.RecordVersion = records.Version;
        }

        private bool CanPinRecoveryFacts(Actor actor, IEnumerable<FactKey> keys)
        {
            var required = new SortedSet<FactKey>(keys);
            foreach (var entry in actor.Knowledge.Entries.Values.Where(e => e.Fact.Pinned)) required.Add(entry.Fact.Key);
            return required.Count <= Information!.KnowledgeCapacity;
        }
        private IEnumerable<FactKey> RecoveryReplyKeys(RecoveryReplyPayload reply)
        {
            yield return new FactKey(SubjectKind.Credential, reply.OldCredentialId, AttributeKind.RecoveryCase);
            yield return new FactKey(SubjectKind.Credential, reply.OldCredentialId, AttributeKind.CredentialStatus);
            if (reply.IssuedCardId > 0)
            {
                yield return new FactKey(SubjectKind.Credential, reply.IssuedCredentialId, AttributeKind.CredentialCard);
                yield return new FactKey(SubjectKind.Credential, reply.IssuedCredentialId, AttributeKind.CredentialStatus);
            }
        }
        private bool LearnRecoveryReply(Actor actor, MessageSnapshot message, long cause)
        {
            var reply = message.Draft.RecoveryReply!;
            if (!CanPinRecoveryFacts(actor, RecoveryReplyKeys(reply))) { Emit("KnowledgeCapacity", actor, messageId: message.Id, causeId: cause, detail: "RecoveryReply"); return false; }
            Learn(actor, message.Draft.Subject, message.Draft.Claim, EvidenceKind.MessageRead, message.Id, message.Draft.ObservedTick, cause, true);
            Learn(actor, new FactKey(SubjectKind.Credential, reply.OldCredentialId, AttributeKind.CredentialStatus), FactValue.Boolean(false), EvidenceKind.MessageRead, message.Id, message.Draft.ObservedTick, cause, true);
            if (reply.IssuedCardId > 0) LearnIssuedCard(actor, reply, EvidenceKind.MessageRead, message.Id, cause); RecoveryReceiptRead(actor, reply);
            return true;
        }
        private void LearnIssuedCard(Actor actor, RecoveryReplyPayload reply, EvidenceKind source, long sourceId, long cause)
        {
            void Historical(FactKey key, FactValue value, bool pinned)
            {
                if (actor.Knowledge.Entries.TryGetValue(key, out var prior) && prior.Fact.ObservedTick > reply.IssuedTick) return;
                Learn(actor, key, value, source, sourceId, reply.IssuedTick, cause, pinned);
            }
            Historical(new FactKey(SubjectKind.Credential, reply.IssuedCredentialId, AttributeKind.CredentialCard), FactValue.Integer(reply.IssuedCardId), true);
            Historical(new FactKey(SubjectKind.Credential, reply.IssuedCredentialId, AttributeKind.CredentialStatus), FactValue.Boolean(true), true);
            // The issuance location is historical evidence, not a live card tracker.
            Historical(new FactKey(SubjectKind.Item, reply.IssuedCardId, AttributeKind.Location), FactValue.At(reply.OutputPosition), false);
        }

        private FailureReason ValidateReplacementIssue(Actor actor, ActiveAction action, bool bind, out long duration)
        {
            duration = Information!.PostTicks;
            if (!recoveryCaseByCredential.TryGetValue(action.CredentialId, out long id) || !recoveryCases.TryGetValue(id, out var recovery) || recovery.ActorId != actor.Id.Value || recovery.AccountId != actor.Account.Value || !KnowsRecoveryCase(actor, recovery)) return FailureReason.UnknownRequiredFact;
            if (recovery.IssuedCardId != 0) return CanPinRecoveryFacts(actor, RecoveryReplyKeys(new RecoveryReplyPayload(id, recovery.OldCredentialId, recovery.IssuedCardId, recovery.IssuedCredentialId, recovery.IssuedTick))) ? FailureReason.None : FailureReason.KnowledgeCapacity;
            if (!recoveryDesks.TryGetValue(action.ServiceId, out var desk) || disabledWorkstations.Contains(desk.WorkstationId)) return FailureReason.RequiredServiceNotPresent;
            var station = workstations[desk.WorkstationId]; var recipe = recipes[desk.RecipeId]; duration = recipe.Duration;
            if (!station.Access.Roles.Contains(actor.Role, StringComparer.Ordinal)) return FailureReason.NotQualified;
            if (items[credentials[action.CredentialId].Definition.Card].Resource != recipe.Outputs[0].Resource) return FailureReason.IncompatiblePart;
            if (resourceClaims.Count(c => c.Kind == ClaimKind.Workstation && c.ResourceId == station.Id && c.ActionId != action.Id.Value) >= station.Capacity) return FailureReason.ServiceBusy;
            long newCredential = checked(credentials.Keys.Last() + 1);
            if (!CanPinRecoveryFacts(actor, new[] { new FactKey(SubjectKind.Credential, newCredential, AttributeKind.CredentialCard), new FactKey(SubjectKind.Credential, newCredential, AttributeKind.CredentialStatus) })) return FailureReason.KnowledgeCapacity;
            if (bind)
            {
                var stock = items.Values.FirstOrDefault(i => i.Resource == recipe.Inputs[0].Resource && ItemAvailableAt(i, actor.Position) && !credentials.Values.Any(c => c.Definition.Card == i.Id) && !ClaimedByOther(ClaimKind.Item, i.Id.Value, action.Id.Value));
                if (stock == null) return FailureReason.InsufficientMaterials;
                action.RecipeId = desk.RecipeId; action.Materials.Add(new MaterialBinding { Item = stock.Id, Quantity = 1 });
            }
            if (action.RecipeId != desk.RecipeId || action.Materials.Count != 1 || action.Materials[0].Quantity != 1 || !items.TryGetValue(action.Materials[0].Item, out var blank) || blank.Resource != recipe.Inputs[0].Resource || blank.Quantity < 1 || !ItemAvailableAt(blank, actor.Position) || ClaimedByOther(ClaimKind.Item, blank.Id.Value, action.Id.Value)) return FailureReason.InsufficientMaterials;
            return FailureReason.None;
        }

        private void CommitReplacementIssue(Actor actor, ActiveAction action)
        {
            var recovery = recoveryCases[recoveryCaseByCredential[action.CredentialId]];
            if (recovery.IssuedCardId == 0)
            {
                var desk = recoveryDesks[action.ServiceId]; var recipe = recipes[desk.RecipeId];
                ValidateRecipeCommit(recipe); long credentialId = checked(credentials.Keys.Last() + 1); int generation = checked(credentials[recovery.OldCredentialId].Definition.Generation + 1);
                var blank = items[action.Materials[0].Item]; blank.Quantity--; if (blank.Quantity == 0) RemoveItem(blank.Id);
                RecordRecipeDelta(actor, action, recipe.Inputs[0].Resource, -1);
                var card = new Item(new Id<ItemTag>(nextItem++), recipe.Outputs[0].Resource, 1, new ItemLocation(LocationKind.ServiceOutput, desk.WorkstationId)); AddItem(card);
                RecordRecipeDelta(actor, action, card.Resource, 1);
                var credential = new Credential { Definition = new CredentialSeed(credentialId, actor.Id, actor.Account, card.Id, generation: generation), Active = true }; credentials.Add(credentialId, credential);
                recovery.IssuedCardId = card.Id.Value; recovery.IssuedCredentialId = credentialId; recovery.IssuedTick = processingTick; recovery.IssueActionId = action.Id.Value; recovery.IssueTerminalId = action.ServiceId; recovery.IssueRecipeId = action.RecipeId;
                RecordCredential(credential, "recovery/credential/" + credentialId); RecordRecoveryCase(recovery);
                Emit("BlankCardConsumed", actor, action.Id, blank.Id, detail: recipe.Inputs[0].Resource, causeId: action.CauseId);
                Emit("ReplacementCardIssued", actor, action.Id, card.Id, target: actor.Position, terminalId: action.ServiceId, sessionId: action.SessionId, detail: "case=" + recovery.Id + "; credential=" + credentialId, causeId: action.CauseId);
            }
            else Emit("ReplacementCardAlreadyIssued", actor, action.Id, new Id<ItemTag>(recovery.IssuedCardId), detail: "case=" + recovery.Id, causeId: action.CauseId);
            LearnIssuedCard(actor, new RecoveryReplyPayload(recovery.Id, recovery.OldCredentialId, recovery.IssuedCardId, recovery.IssuedCredentialId, recovery.IssuedTick, terminals[recovery.IssueTerminalId].Definition.Position), EvidenceKind.Direct, action.Id.Value, action.CauseId); RecoveryIssued(actor, recovery);
        }
    }
}
