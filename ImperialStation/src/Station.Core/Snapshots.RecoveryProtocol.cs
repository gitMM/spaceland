using System;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void ValidateRecoveryProtocol()
        {
            if (nextRecoveryCase < 1 || RecoveryIdentityCheckTicks == 0 && (recoveryCases.Count != 0 || recoveryDesks.Count != 0)) throw new ArgumentException("Invalid recovery service state.");
            foreach (var c in recoveryCases.Values)
            {
                if (c.Id < 1 || c.Id >= nextRecoveryCase || !credentials.TryGetValue(c.OldCredentialId, out var old) || old.Active || old.Definition.Holder.Value != c.ActorId || old.Definition.Account.Value != c.AccountId ||
                    c.AcceptedTick < 0 || c.AcceptedTick > LastProcessedTick || !messages.TryGetValue(c.FirstRequestMessageId, out var request) || request.Draft.Kind != MessageKind.LostCardReport || request.Stage != MessageStage.Accepted || request.DeliveryTick != c.AcceptedTick || request.Draft.Subject.SubjectId != c.OldCredentialId ||
                    !records.History.TryGetValue(c.RecordVersion, out var record) || record.Kind != RecordKind.CredentialRecovery || record.EntityId != c.Id || record.AccountId != c.AccountId || record.Tick != (c.IssuedCardId == 0 ? c.AcceptedTick : c.IssuedTick) || !record.Fields.Any(f => f.Attribute == AttributeKind.CredentialCard && f.Value.Number == c.IssuedCardId))
                    throw new ArgumentException("Invalid credential recovery case or authorization provenance.");
                if (c.IssuedCardId == 0)
                {
                    if (c.IssuedCredentialId != 0 || c.IssuedTick != -1 || c.IssueActionId != 0 || c.IssueTerminalId != 0 || c.IssueRecipeId != 0) throw new ArgumentException("Unissued case contains issuance data.");
                    continue;
                }
                if (c.IssuedTick < c.AcceptedTick || c.IssuedTick > LastProcessedTick || c.IssueActionId < 1 || c.IssueActionId >= nextAction || !recoveryDesks.TryGetValue(c.IssueTerminalId, out var desk) || desk.RecipeId != c.IssueRecipeId ||
                    !credentials.TryGetValue(c.IssuedCredentialId, out var issued) || issued.Definition.Holder.Value != c.ActorId || issued.Definition.Account.Value != c.AccountId || issued.Definition.Card.Value != c.IssuedCardId || issued.Definition.Generation != old.Definition.Generation + 1 ||
                    !items.TryGetValue(issued.Definition.Card, out var card) || card.Quantity != 1 || card.Resource != recipes[c.IssueRecipeId].Outputs[0].Resource || !items.ContainsKey(old.Definition.Card))
                    throw new ArgumentException("Invalid issued card identity or generation.");
                var transactions = resourceTransactions.Where(t => t.ActionId == c.IssueActionId).ToArray();
                var recipe = recipes[c.IssueRecipeId];
                if (!records.History.Values.Any(r => r.Kind == RecordKind.Credential && r.EntityId == c.IssuedCredentialId && r.AccountId == c.AccountId && r.Tick == c.IssuedTick && r.OperationKey == "recovery/credential/" + c.IssuedCredentialId)) throw new ArgumentException("Replacement has no issuance registry record.");
                if (transactions.Length != 2 || transactions.Any(t => t.ActorId != c.ActorId || t.Tick != c.IssuedTick || t.RecipeId != c.IssueRecipeId) ||
                    !transactions.Any(t => t.Resource == recipe.Inputs[0].Resource && t.Delta == -1) || !transactions.Any(t => t.Resource == recipe.Outputs[0].Resource && t.Delta == 1))
                    throw new ArgumentException("Issuance has no conserved blank-to-card transaction.");
            }
            if (recoveryCases.Values.Where(c => c.IssuedCardId > 0).GroupBy(c => c.IssuedCardId).Any(g => g.Count() != 1) || recoveryCases.Values.Where(c => c.IssuedCardId > 0).GroupBy(c => c.IssuedCredentialId).Any(g => g.Count() != 1))
                throw new ArgumentException("Duplicate replacement identity.");
            foreach (var m in messages.Values)
            {
                if (!ValidRecoveryDraft(m.Draft)) throw new ArgumentException("Invalid recovery routing or payload.");
                if (!(m.Draft.Kind is MessageKind.LostCardReport or MessageKind.RecoveryReply)) continue;
                if (RecoveryIdentityCheckTicks == 0 || m.Seeded || m.SubmittedTick < 0 || m.DeliveryTick != checked(m.SubmittedTick + Information!.LatencyTicks)) throw new ArgumentException("Invalid recovery transport timing.");
                if (m.Stage == MessageStage.Posted && scheduled.Count(e => e.Kind == ScheduledKind.MessageDelivery && e.Value == m.Id && e.Tick == m.DeliveryTick) != 1) throw new ArgumentException("Recovery message has no delivery timer.");
                if (m.Draft.Kind == MessageKind.LostCardReport)
                {
                    if (!RecoveryTransportAuthentic(m) || !credentials.TryGetValue(m.Draft.Subject.SubjectId, out var old) || old.Definition.Holder != m.PhysicalSender || old.Definition.Account != m.SenderAccount || m.FirstReadTick.HasValue || m.SubmittedTick < Information!.PostTicks || m.Draft.ObservedTick != m.SubmittedTick - Information.PostTicks || !(m.Stage is MessageStage.Posted or MessageStage.Accepted) ||
                        m.Stage == MessageStage.Accepted && (!recoveryCaseByCredential.ContainsKey(old.Definition.Id) || messages.Values.Count(reply => reply.Draft.Kind == MessageKind.RecoveryReply && reply.Draft.RelatedMessageId == m.Id) != 1)) throw new ArgumentException("Invalid loss report identity or acceptance.");
                }
                else
                {
                    var p = m.Draft.RecoveryReply!;
                    if (m.SenderAccount.Value != 0 || m.PhysicalSender.Value != 0 || m.SessionId != 0 || m.TerminalId != 0 || !messages.TryGetValue(m.Draft.RelatedMessageId, out var request) || request.Draft.Kind != MessageKind.LostCardReport || request.Stage != MessageStage.Accepted ||
                        request.SenderAccount != m.Draft.Recipient || request.DeliveryTick != m.SubmittedTick || request.Draft.Subject.SubjectId != p.OldCredentialId || m.Draft.ObservedTick != m.SubmittedTick || !recoveryCases.TryGetValue(p.CaseId, out var c) || c.OldCredentialId != p.OldCredentialId || c.AccountId != m.Draft.Recipient.Value || c.AcceptedTick > m.SubmittedTick ||
                        !(m.Stage is MessageStage.Posted or MessageStage.DeliveredToMailbox or MessageStage.Read) || (m.Stage == MessageStage.Read ? !m.FirstReadTick.HasValue || m.FirstReadTick < m.DeliveryTick || m.FirstReadTick > LastProcessedTick : m.FirstReadTick.HasValue)) throw new ArgumentException("Invalid recovery reply provenance or disclosure.");
                    if (p.IssuedCardId > 0 && (p.IssuedCardId != c.IssuedCardId || p.IssuedCredentialId != c.IssuedCredentialId || p.IssuedTick != c.IssuedTick || c.IssuedTick > m.SubmittedTick || p.OutputPosition != terminals[c.IssueTerminalId].Definition.Position)) throw new ArgumentException("Reply contains an invalid issuance receipt.");
                    if (p.IssuedCardId == 0 && c.IssuedCardId > 0 && c.IssuedTick <= m.SubmittedTick) throw new ArgumentException("Retry reply omitted its already issued card.");
                }
            }
            foreach (var actor in actors.Values)
            {
                foreach (var entry in actor.Knowledge.Entries.Values.Where(e => e.Fact.Key.Attribute == AttributeKind.RecoveryCase && e.Fact.Key.SubjectKind == SubjectKind.Credential))
                    if (!recoveryCases.TryGetValue(entry.Fact.Value.Number, out var recovery) || recovery.ActorId != actor.Id.Value || entry.Fact.Key.SubjectId != recovery.OldCredentialId || !KnowsRecoveryCase(actor, recovery)) throw new ArgumentException("Recovery authorization was not read by its owner.");
                var action = actor.Action; if (action == null || !IsRecoveryAction(action.Kind)) continue;
                if (RecoveryIdentityCheckTicks == 0 || action.Waiting || action.Completes <= action.Started || action.Completes <= LastProcessedTick || !terminals.TryGetValue(action.ServiceId, out var terminal) || terminal.Definition.Position != actor.Position ||
                    !records.History.Values.Any(r => r.Kind == RecordKind.TerminalSession && r.EntityId == action.SessionId && r.AccountId == actor.Account.Value && r.Tick <= action.Started && r.Fields.Any(f => f.Attribute == AttributeKind.SessionScope && f.Value.Number == (long)SessionScope.CredentialRecovery) && r.Fields.Any(f => f.Attribute == AttributeKind.TerminalId && f.Value.Number == action.ServiceId) && r.Fields.Any(f => f.Attribute == AttributeKind.ActorId && f.Value.Number == actor.Id.Value)) ||
                    scheduled.Count(e => e.Kind == ScheduledKind.ActionCompletion && e.Intent.Actor == actor.Id && e.ActionId == action.Id.Value && e.Tick == action.Completes) != 1) throw new ArgumentException("Invalid finite recovery action.");
                if (sessions.TryGetValue(actor.Id, out var session) && (session.Scope != SessionScope.CredentialRecovery || session.Id != action.SessionId)) throw new ArgumentException("Recovery action owns a different session.");
                if (action.Kind == ActionKind.ReadRecoveryInbox)
                {
                    if (action.RecipeId != 0 || action.Message != null || action.CapturedMessages.Distinct().Count() != action.CapturedMessages.Count || action.CapturedMessages.Any(id => !messages.TryGetValue(id, out var m) || m.Draft.Kind != MessageKind.RecoveryReply || m.Draft.Recipient != actor.Account || m.Stage != MessageStage.DeliveredToMailbox || m.DeliveryTick > action.Started)) throw new ArgumentException("Recovery read includes a foreign or undelivered reply.");
                    long duration = Math.Min(Information!.ReadMaximumTicks, checked(Information.ReadBaseTicks + Information.ReadPerMessageTicks * action.CapturedMessages.Count));
                    if (action.Completes != action.Started + duration) throw new ArgumentException("Invalid recovery read duration.");
                    continue;
                }
                if (!credentials.TryGetValue(action.CredentialId, out var credential) || credential.Definition.Holder != actor.Id || credential.Definition.Card != action.Item) throw new ArgumentException("Recovery action names a foreign credential.");
                if (action.Kind == ActionKind.PostLostCard)
                {
                    if (action.RecipeId != 0 || action.Message == null || action.Message.Kind != MessageKind.LostCardReport || !ValidRecoveryDraft(action.Message) || action.Message.Subject.SubjectId != action.CredentialId || action.Message.ObservedTick != action.Started || action.Completes != action.Started + Information!.PostTicks) throw new ArgumentException("Invalid pending loss report.");
                    continue;
                }
                if (!recoveryCaseByCredential.TryGetValue(action.CredentialId, out long caseId) || !KnowsRecoveryCase(actor, recoveryCases[caseId])) throw new ArgumentException("Issuance lacks read authorization.");
                if (action.RecipeId == 0)
                {
                    if (recoveryCases[caseId].IssuedCardId == 0 || action.Materials.Count != 0 || action.Completes != action.Started + Information!.PostTicks) throw new ArgumentException("Invalid idempotent issuance retry.");
                }
                else
                {
                    bool Has(ClaimKind kind, long id) => resourceClaims.Any(c => c.Kind == kind && c.ResourceId == id && c.ActorId == actor.Id.Value && c.ActionId == action.Id.Value);
                    if (!recoveryDesks.TryGetValue(action.ServiceId, out var desk) || desk.RecipeId != action.RecipeId || recoveryCases[caseId].IssuedCardId != 0 || action.Completes != action.Started + recipes[action.RecipeId].Duration ||
                        !Has(ClaimKind.Workstation, desk.WorkstationId) || action.Materials.Count != 1 || action.Materials[0].Quantity != 1 || !Has(ClaimKind.Item, action.Materials[0].Item.Value) ||
                        !items.TryGetValue(action.Materials[0].Item, out var blank) || blank.Resource != recipes[action.RecipeId].Inputs[0].Resource || !ItemAvailableAt(blank, actor.Position)) throw new ArgumentException("Invalid issuance stock, printer claim or finite timer.");
                }
            }
        }
    }
}
