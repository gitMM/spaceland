using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteRecoveryReply(BinaryWriter writer, RecoveryReplyPayload? reply)
        {
            writer.Write(reply != null); if (reply == null) return;
            writer.Write(reply.CaseId); writer.Write(reply.OldCredentialId); writer.Write(reply.IssuedCardId); writer.Write(reply.IssuedCredentialId); writer.Write(reply.IssuedTick); WriteCoord(writer, reply.OutputPosition);
        }
        private static RecoveryReplyPayload? ReadRecoveryReply(BinaryReader reader) => reader.ReadBoolean() ? new RecoveryReplyPayload(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), ReadCoord(reader)) : null;
        private void WriteCredentialRecovery(BinaryWriter writer)
        {
            writer.Write(RecoveryIdentityCheckTicks); writer.Write(recoveryCapabilities.Count);
            foreach (var capability in recoveryCapabilities.Values)
            { writer.Write(capability.ActorId); writer.Write(capability.AccountId); writer.Write(capability.Retained); }
            writer.Write(recoveryDesks.Count); foreach (var desk in recoveryDesks.Values) { writer.Write(desk.TerminalId); writer.Write(desk.WorkstationId); writer.Write(desk.RecipeId); }
            writer.Write(nextRecoveryCase); writer.Write(recoveryCases.Count);
            foreach (var c in recoveryCases.Values)
            { writer.Write(c.Id); writer.Write(c.ActorId); writer.Write(c.AccountId); writer.Write(c.OldCredentialId); writer.Write(c.FirstRequestMessageId); writer.Write(c.AcceptedTick); writer.Write(c.IssuedCardId); writer.Write(c.IssuedCredentialId); writer.Write(c.IssuedTick); writer.Write(c.IssueActionId); writer.Write(c.IssueTerminalId); writer.Write(c.IssueRecipeId); writer.Write(c.RecordVersion); }
            WriteRecoveryAgents(writer);
        }
        private void ReadCredentialRecovery(BinaryReader reader)
        {
            long duration = reader.ReadInt64(); int count = Count(reader);
            if (duration < 0 || duration == 0 && count != 0) throw new ArgumentException("Invalid recovery configuration.");
            var capabilities = new RecoveryCapabilitySeed[count];
            for (int i = 0; i < count; i++) capabilities[i] = new RecoveryCapabilitySeed(reader.ReadInt64(), reader.ReadInt64(), reader.ReadBoolean());
            if (duration > 0) InitializeCredentialRecovery(new CredentialRecoverySeed(capabilities, duration, autonomousActors: Array.Empty<long>()));
            count = Count(reader); for (int i = 0; i < count; i++) RegisterRecoveryDesk(new RecoveryDeskSeed(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64()));
            if (duration > 0) PublishRecoveryDesks();
            nextRecoveryCase = reader.ReadInt64(); count = Count(reader);
            for (int i = 0; i < count; i++)
            {
                var c = new CredentialRecoveryCaseSnapshot { Id = reader.ReadInt64(), ActorId = reader.ReadInt64(), AccountId = reader.ReadInt64(), OldCredentialId = reader.ReadInt64(), FirstRequestMessageId = reader.ReadInt64(), AcceptedTick = reader.ReadInt64(), IssuedCardId = reader.ReadInt64(), IssuedCredentialId = reader.ReadInt64(), IssuedTick = reader.ReadInt64(), IssueActionId = reader.ReadInt64(), IssueTerminalId = reader.ReadInt64(), IssueRecipeId = reader.ReadInt64(), RecordVersion = reader.ReadInt64() };
                recoveryCases.Add(c.Id, c); recoveryCaseByCredential.Add(c.OldCredentialId, c.Id);
            }
            ReadRecoveryAgents(reader);
        }
        private void ValidateRecoverySessions()
        {
            ValidateRecoveryProtocol(); ValidateRecoveryAgents();
            foreach (var session in sessions.Values)
            {
                if (session.Id < 1 || session.Id >= nextSession || !actors.TryGetValue(session.Actor, out var actor) || actor.Account != session.Account || !terminals.TryGetValue(session.Terminal, out var terminal))
                    throw new ArgumentException("Invalid session identity.");
                if (session.Scope == SessionScope.Normal)
                {
                    if (!credentials.TryGetValue(session.Credential, out var credential) || credential.Definition.Holder != session.Actor || credential.Definition.Account != session.Account)
                        throw new ArgumentException("Normal session requires its holder-bound credential.");
                    continue;
                }
                if (session.Credential != 0 || RecoveryIdentityCheckTicks == 0 || !recoveryCapabilities.TryGetValue(session.Actor.Value, out var capability) || !capability.Retained || capability.AccountId != session.Account.Value ||
                    (terminal.Definition.Capabilities & TerminalCapability.CredentialRecovery) == 0 || !terminal.Enabled || !terminal.Connected || actor.Position != terminal.Definition.Position || actor.Action?.Kind == ActionKind.Move ||
                    !terminal.Definition.PermittedRoles.Contains(actor.Role, StringComparer.Ordinal) || terminal.Claims.Count(id => id == actor.Id) != 1 || terminal.Queue.Contains(actor.Id) || session.Expires <= LastProcessedTick ||
                    scheduled.Count(e => e.Kind == ScheduledKind.SessionExpiry && e.Intent.Actor == actor.Id && e.Value == session.Id && e.Tick == session.Expires) != 1)
                    throw new ArgumentException("Invalid restricted recovery session or capacity/expiry claim.");
                if (actor.Action?.Kind is ActionKind.ReadInbox or ActionKind.PostMessage or ActionKind.QueryRecords)
                    throw new ArgumentException("Restricted session cannot own an ordinary information action.");
            }
            foreach (var actor in actors.Values.Where(a => a.Action?.Kind == ActionKind.RecoveryLogin))
            {
                var action = actor.Action!;
                if (RecoveryIdentityCheckTicks == 0 || action.CredentialId != 0 || action.SessionId != 0 || sessions.ContainsKey(actor.Id) || action.Item.Value != 0 || action.TargetActor.Value != 0 ||
                    !recoveryCapabilities.TryGetValue(actor.Id.Value, out var capability) || !capability.Retained || capability.AccountId != actor.Account.Value ||
                    !terminals.TryGetValue(action.ServiceId, out var terminal) || actor.Position != terminal.Definition.Position || (terminal.Definition.Capabilities & TerminalCapability.CredentialRecovery) == 0 ||
                    !terminal.Definition.PermittedRoles.Contains(actor.Role, StringComparer.Ordinal))
                    throw new ArgumentException("Invalid recovery identity check.");
                int completions = scheduled.Count(e => e.Kind == ScheduledKind.ActionCompletion && e.Intent.Actor == actor.Id && e.ActionId == action.Id.Value);
                if (action.Waiting)
                {
                    bool queued = terminal.Queue.Count(id => id == actor.Id) == 1 && !terminal.Claims.Contains(actor.Id);
                    bool granted = terminal.Claims.Count(id => id == actor.Id) == 1 && !terminal.Queue.Contains(actor.Id) && scheduled.Count(e => e.Kind == ScheduledKind.GrantTerminal && e.Intent.Actor == actor.Id && e.ActionId == action.Id.Value && e.Value == action.ServiceId) == 1;
                    if (completions != 0 || !(queued || granted)) throw new ArgumentException("Invalid queued recovery claim.");
                }
                else if (terminal.Claims.Count(id => id == actor.Id) != 1 || terminal.Queue.Contains(actor.Id) || completions != 1 ||
                    action.Completes < checked(action.Started + RecoveryIdentityCheckTicks) || action.Completes > checked(LastProcessedTick + RecoveryIdentityCheckTicks) ||
                    !scheduled.Any(e => e.Kind == ScheduledKind.ActionCompletion && e.Intent.Actor == actor.Id && e.ActionId == action.Id.Value && e.Tick == action.Completes))
                    throw new ArgumentException("Invalid finite recovery check timer or claim.");
            }
        }
    }
}
