using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly RecordLedger records = new RecordLedger();
        private readonly SortedDictionary<Id<ActorTag>, RecordQueryReceipt> recordReceipts = new SortedDictionary<Id<ActorTag>, RecordQueryReceipt>();
        private readonly SortedDictionary<(long Actor, long Terminal, RecordKind Kind), RecordAccessSeed> recordAccess = new SortedDictionary<(long, long, RecordKind), RecordAccessSeed>();
        // Each holder's own copy of the grant notices it was given; the records office keeps recordAccess.
        private readonly SortedDictionary<(long Actor, long Terminal, RecordKind Kind), string> knownRecordGrants = new SortedDictionary<(long, long, RecordKind), string>();
        private IEnumerable<(long Terminal, RecordKind Kind, string Role)> KnownRecordGrants(Id<ActorTag> actor) =>
            knownRecordGrants.Where(g => g.Key.Actor == actor.Value).Select(g => (g.Key.Terminal, g.Key.Kind, g.Value));
        public IReadOnlyList<RecordEntry> InspectRecordHistory() => records.History.Values.ToArray();
        private static RecordField Number(AttributeKind key, long value) => new RecordField(key, FactValue.Integer(value));
        private static RecordField Text(AttributeKind key, string value) => new RecordField(key, new FactValue(FactValueKind.Text, text: value));
        private void InitializeRecords(IEnumerable<RecordAccessSeed> access)
        {
            foreach (var grant in access)
            {
                if (!actors.TryGetValue(grant.Actor, out var actor) || actor.Role != grant.RequiredRole || string.IsNullOrWhiteSpace(grant.RequiredRole) ||
                    !terminals.TryGetValue(grant.TerminalId, out var terminal) || (terminal.Definition.Capabilities & TerminalCapability.Records) == 0 || !terminal.Definition.PermittedRoles.Contains(grant.RequiredRole, StringComparer.Ordinal) ||
                    !(grant.Kind is RecordKind.WorkAttempt or RecordKind.WorkReport)) throw new ArgumentException("Audit grant requires an existing actor, assigned records terminal, permitted role and work-record kind.");
                recordAccess.Add((grant.Actor.Value, grant.TerminalId, grant.Kind), grant); knownRecordGrants.Add((grant.Actor.Value, grant.TerminalId, grant.Kind), grant.RequiredRole);
            }
            foreach (var credential in credentials.Values) RecordCredential(credential, "initial/credential/" + credential.Definition.Id);
        }
        private IRecordStore RecordsFor(Actor actor, long terminal, RecordKind kind)
        {
            bool audit = recordAccess.TryGetValue((actor.Id.Value, terminal, kind), out var grant) && actor.Role == grant.RequiredRole;
            return records.Scope(kind, audit ? 0 : actor.Account.Value);
        }
        private void RecordCredential(Credential credential, string key)
        {
            var c = credential.Definition;
            records.Append(RecordKind.Credential, c.Id, c.Account.Value, processingTick, key, new[] {
                Number(AttributeKind.ActorId, c.Holder.Value), Number(AttributeKind.CredentialCard, c.Card.Value), Number(AttributeKind.Generation, c.Generation),
                new RecordField(AttributeKind.CredentialStatus, FactValue.Boolean(credential.Active)), Text(AttributeKind.Role, actors[c.Holder].Role) });
        }
        private void RecordSession(Session session, string status)
        {
            records.Append(RecordKind.TerminalSession, session.Id, session.Account.Value, processingTick, "session/" + session.Id + "/" + status, new[] {
                Number(AttributeKind.ActorId, session.Actor.Value), Number(AttributeKind.TerminalId, session.Terminal), Number(AttributeKind.Expires, session.Expires), Text(AttributeKind.WorkStatus, status) }.Concat(session.Scope == SessionScope.CredentialRecovery ? new[] { Number(AttributeKind.SessionScope, (long)session.Scope) } : Array.Empty<RecordField>()));
        }
        private void RecordWorkAttempt(WorkAttemptSnapshot attempt, string operationKey)
        {
            // Administrative state and received submissions only. Never Execution or FirstReadTick.
            var fields = new List<RecordField> {
                Number(AttributeKind.ActorId, attempt.ActorId), Number(AttributeKind.TaskId, attempt.JobId), Number(AttributeKind.AttemptId, attempt.Id),
                Number(jobs[attempt.JobId].Kind == WorkKind.ConstructHabitat ? AttributeKind.ProjectId : jobs[attempt.JobId].Kind != WorkKind.CleanTile ? AttributeKind.MachineId : AttributeKind.TileId, jobs[attempt.JobId].Kind == WorkKind.ConstructHabitat ? jobs[attempt.JobId].ProjectId : jobs[attempt.JobId].Kind != WorkKind.CleanTile ? jobs[attempt.JobId].MachineId : jobs[attempt.JobId].TileId),
                Text(AttributeKind.TaskKind, jobs[attempt.JobId].Kind.ToString()),
                Number(AttributeKind.MessageId, attempt.OrderMessageId), Number(AttributeKind.SubmittedTick, attempt.DispatchTick), Number(AttributeKind.Expires, attempt.Expires),
                Number(AttributeKind.AcknowledgedTick, attempt.AcknowledgmentReceivedTick ?? -1), Text(AttributeKind.OrderStatus, attempt.Assignment.ToString()), Text(AttributeKind.WorkStatus, attempt.AuthorityStatus) };
            // The declared slot specification is institutional job data, never installed-part truth.
            if (jobs[attempt.JobId].Kind is WorkKind.RepairMachine or WorkKind.VerifyMachine) fields.Add(Text(AttributeKind.RequiredResource, machines[jobs[attempt.JobId].MachineId].RequiredResource));
            records.Append(RecordKind.WorkAttempt, attempt.Id, attempt.AccountId, processingTick, operationKey, fields);
        }
        private void RecordWorkReport(MessageSnapshot message)
        {
            var d = message.Draft;
            records.Append(RecordKind.WorkReport, message.Id, message.SenderAccount.Value, processingTick, "work-report/" + message.Id, new[] {
                Number(AttributeKind.ActorId, message.PhysicalSender.Value), Number(AttributeKind.TerminalId, message.TerminalId), Number(AttributeKind.TaskId, d.TaskId), Number(AttributeKind.AttemptId, d.AttemptId),
                Number(AttributeKind.MessageId, message.Id), Number(AttributeKind.SubmittedTick, message.SubmittedTick), Number(AttributeKind.ReceivedTick, processingTick), Number(AttributeKind.ObservedTick, d.ObservedTick),
                Text(AttributeKind.ReportKind, d.Kind.ToString()), new RecordField(AttributeKind.ReportClaim, d.Claim), Text(AttributeKind.ReportSubjectKind, d.Subject.SubjectKind.ToString()),
                Number(AttributeKind.ReportSubjectId, d.Subject.SubjectId), Text(AttributeKind.ReportAttribute, d.Subject.Attribute.ToString()) }.Concat(
                    workAttempts.TryGetValue(d.AttemptId, out var attempt) && jobs[attempt.JobId].Kind is WorkKind.RepairMachine or WorkKind.VerifyMachine
                        ? new[] { Number(AttributeKind.MachineId, jobs[attempt.JobId].MachineId) } : Array.Empty<RecordField>()));
        }
        private void CompleteRecordQuery(Actor actor, ActiveAction action)
        {
            var page = action.CapturedRecords ?? throw new InvalidOperationException("Query has no captured result.");
            // Refilter at completion. A revoked grant cannot disclose a previously captured foreign row.
            var current = RecordsFor(actor, action.ServiceId, action.Query!.Kind);
            int disclosed = 0;
            foreach (var entry in page.Entries)
            {
                if (!current.Query(new RecordQuery(entry.Kind, entry.EntityId, limit: 1)).Entries.Any()) continue;
                var fields = new[] { Text(AttributeKind.RecordType, entry.Kind.ToString()), Number(AttributeKind.RecordEntityId, entry.EntityId), Number(AttributeKind.RecordVersion, entry.Version), Number(AttributeKind.AccountId, entry.AccountId), Number(AttributeKind.RecordFieldCount, entry.Fields.Count) }.Concat(entry.Fields);
                foreach (var field in fields)
                    Learn(actor, new FactKey(SubjectKind.Record, entry.Version, field.Attribute), field.Value, EvidenceKind.RecordQuery, action.Id.Value, entry.Tick, action.CauseId);
                disclosed++;
                RecordQueryDisclosure(actor, action, entry, page.Version);
            }
            recordReceipts[actor.Id] = new RecordQueryReceipt(action.Query, page.Version, page.NextCursor, page.HasMore, processingTick, disclosed);
            AuditQueryCompleted(actor, action);
            Emit("RecordsQueried", actor, action.Id, terminalId: action.ServiceId, sessionId: action.SessionId, causeId: action.CauseId,
                detail: "kind=" + action.Query.Kind + "; version=" + page.Version + "; rows=" + disclosed + "; cursor=" + page.NextCursor + "; more=" + page.HasMore);
        }
    }
}
