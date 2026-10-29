using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteKey(BinaryWriter w, FactKey key) { w.Write((int)key.SubjectKind); w.Write(key.SubjectId); w.Write((int)key.Attribute); }
        private static FactKey ReadKey(BinaryReader r) => new FactKey(ReadEnum<SubjectKind>(r), r.ReadInt64(), ReadEnum<AttributeKind>(r));
        private static void WriteValue(BinaryWriter w, FactValue value) { w.Write((int)value.Kind); w.Write(value.Number); WriteCoord(w, value.Coordinate); w.Write(value.Text ?? ""); }
        private static FactValue ReadValue(BinaryReader r) => new FactValue(ReadEnum<FactValueKind>(r), r.ReadInt64(), ReadCoord(r), r.ReadString());
        private static void WriteDraft(BinaryWriter w, MessageDraft? draft)
        {
            w.Write(draft != null); if (draft == null) return;
            w.Write((int)draft.Kind); w.Write((int)draft.RecipientKind); w.Write(draft.Recipient.Value); WriteKey(w, draft.Subject); WriteValue(w, draft.Claim);
            w.Write(draft.ObservedTick); w.Write(draft.TaskId); w.Write(draft.AttemptId); w.Write(draft.RelatedMessageId);
            w.Write(draft.WorkOrder != null); if (draft.WorkOrder != null) { w.Write((int)draft.WorkOrder.Kind); w.Write(draft.WorkOrder.Kind != WorkKind.CleanTile ? draft.WorkOrder.MachineId : draft.WorkOrder.Tile.Value); w.Write(draft.WorkOrder.Expires); if (draft.WorkOrder.Kind == WorkKind.ConstructHabitat) WriteConstructionOrder(w, draft.WorkOrder.Construction!); } w.Write(draft.OperationKey);
            WriteRosterPayload(w, draft.Roster);
            WritePatrolPayload(w, draft.PatrolOrder);
            WriteInspectionPayload(w, draft.InspectionReport); WriteCasePayload(w, draft.CaseSubmission); WriteCaseDecisionPayload(w, draft.CaseDecision);
            WriteDetentionRequestPayload(w, draft.DetentionRequest); WriteDetentionDecisionPayload(w, draft.DetentionDecision); WriteEscortPayload(w, draft.EscortOrder); WriteRecoveryReply(w, draft.RecoveryReply);
        }
        private static MessageDraft? ReadDraft(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            var kind = ReadEnum<MessageKind>(r); var recipientKind = ReadEnum<RecipientKind>(r); var recipient = new Id<AccountTag>(r.ReadInt64()); var key = ReadKey(r); var value = ReadValue(r);
            long observed = r.ReadInt64(), task = r.ReadInt64(), attempt = r.ReadInt64(), related = r.ReadInt64();
            WorkOrderPayload? order = null;
            if (r.ReadBoolean()) { var workKind = ReadEnum<WorkKind>(r); long target = r.ReadInt64(), expires = r.ReadInt64(); order = workKind == WorkKind.ConstructHabitat ? ReadConstructionWorkOrder(r, target, expires) : workKind != WorkKind.CleanTile ? new WorkOrderPayload(target, expires, workKind) : new WorkOrderPayload(new Id<TileTag>(target), expires); }
            string operation = r.ReadString();
            return new MessageDraft(kind, recipientKind, recipient, key, value, observed, task, attempt, related, order, operation, ReadRosterPayload(r), ReadPatrolPayload(r), ReadInspectionPayload(r), ReadCasePayload(r), ReadCaseDecisionPayload(r), ReadDetentionRequestPayload(r), ReadDetentionDecisionPayload(r), ReadEscortPayload(r), ReadRecoveryReply(r));
        }
        private static void WriteFact(BinaryWriter w, KnowledgeFact f)
        { w.Write(f.Id); WriteKey(w, f.Key); WriteValue(w, f.Value); w.Write((int)f.SourceKind); w.Write(f.SourceId); w.Write(f.ObservedTick); w.Write(f.LearnedTick); w.Write(f.Pinned); w.Write(f.SourceActorId); }
        private static KnowledgeFact ReadFact(BinaryReader r) => new KnowledgeFact(r.ReadInt64(), ReadKey(r), ReadValue(r), ReadEnum<EvidenceKind>(r), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadBoolean(), r.ReadInt64());
        private static void WriteBelief(BinaryWriter w, BeliefSnapshot b)
        { WriteKey(w, b.Key); WriteValue(w, b.Value); w.Write(b.MessageId); w.Write(b.ObservedTick); w.Write(b.ReceivedTick); w.Write(b.Source); w.Write(b.ContradictsPrevious); w.Write(b.SensorId); w.Write(b.ReadingId); }
        private static BeliefSnapshot ReadBelief(BinaryReader r) => new BeliefSnapshot { Key = ReadKey(r), Value = ReadValue(r), MessageId = r.ReadInt64(), ObservedTick = r.ReadInt64(), ReceivedTick = r.ReadInt64(), Source = r.ReadString(), ContradictsPrevious = r.ReadBoolean(), SensorId = r.ReadInt64(), ReadingId = r.ReadInt64() };
        private void WriteInformation(BinaryWriter w)
        {
            w.Write(Information != null); if (Information == null) return;
            var c = Information; w.Write(c.LoginTicks); w.Write(c.ReadBaseTicks); w.Write(c.ReadPerMessageTicks); w.Write(c.ReadMaximumTicks); w.Write(c.PostTicks); w.Write(c.LatencyTicks); w.Write(c.SessionTicks); w.Write(c.InspectTicks); w.Write(c.KnowledgeCapacity); w.Write(c.QueryTicks);
            w.Write(nextFact); w.Write(nextMessage); w.Write(nextSession); w.Write(KnowledgeEvictions); w.Write(MaximumTerminalQueue); w.Write(TotalTerminalWaitTicks);
            w.Write(publicKnowledge!.Version); w.Write(publicKnowledge.Layout.Count);
            foreach (var tile in publicKnowledge.Layout) { w.Write(tile.Id.Value); WriteCoord(w, tile.Position); w.Write(tile.TraversalTicks); }
            w.Write(publicKnowledge.Services.Count);
            foreach (var service in publicKnowledge.Services)
            { w.Write(service.Id); WriteCoord(w, service.Position); w.Write((int)service.Capabilities); w.Write(service.PermittedRoles.Count); foreach (string role in service.PermittedRoles) w.Write(role); }
            WritePublicWork(w, publicKnowledge.Work);
            foreach (var actor in actors.Values)
            {
                w.Write(actor.Knowledge.Sequence); w.Write(actor.Knowledge.Entries.Count);
                foreach (var entry in actor.Knowledge.Entries.Values) { WriteFact(w, entry.Fact); w.Write(entry.Recency); }
            }
            w.Write(terminals.Count);
            foreach (var t in terminals.Values)
            {
                var d = t.Definition; w.Write(d.Id); WriteCoord(w, d.Position); w.Write(d.Capacity); w.Write((int)d.Capabilities); w.Write(d.PermittedRoles.Count); foreach (string role in d.PermittedRoles) w.Write(role);
                w.Write(t.Enabled); w.Write(t.Connected); w.Write(t.Claims.Count); foreach (var id in t.Claims) w.Write(id.Value); w.Write(t.Queue.Count); foreach (var id in t.Queue) w.Write(id.Value);
            }
            w.Write(credentials.Count);
            foreach (var credential in credentials.Values)
            { var d = credential.Definition; w.Write(d.Id); w.Write(d.Holder.Value); w.Write(d.Account.Value); w.Write(d.Card.Value); w.Write(d.Generation); w.Write(credential.Active); }
            w.Write(sessions.Count);
            foreach (var s in sessions.Values) { w.Write(s.Id); w.Write(s.Actor.Value); w.Write(s.Account.Value); w.Write(s.Terminal); w.Write(s.Credential); w.Write(s.Expires); w.Write((int)s.Scope); }
            w.Write(messages.Count);
            foreach (var m in messages.Values)
            {
                w.Write(m.Id); WriteDraft(w, m.Draft); w.Write(m.PhysicalSender.Value); w.Write(m.SenderAccount.Value); w.Write(m.TerminalId); w.Write(m.SessionId); w.Write(m.SubmittedTick); w.Write(m.DeliveryTick);
                w.Write(m.FirstReadTick.HasValue); if (m.FirstReadTick.HasValue) w.Write(m.FirstReadTick.Value); w.Write((int)m.Stage); w.Write(m.Seeded);
            }
            w.Write(beliefs.Count); foreach (var b in beliefs.Values) WriteBelief(w, b);
            w.Write(beliefHistory.Count); foreach (var b in beliefHistory) WriteBelief(w, b);
            WritePlanning(w); WriteCredentialRecovery(w);
        }
        private void ReadInformation(BinaryReader r)
        {
            if (!r.ReadBoolean()) return;
            Information = new InformationConfig(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadInt64());
            nextFact = r.ReadInt64(); nextMessage = r.ReadInt64(); nextSession = r.ReadInt64(); KnowledgeEvictions = r.ReadInt64(); MaximumTerminalQueue = r.ReadInt64(); TotalTerminalWaitTicks = r.ReadInt64();
            long version = r.ReadInt64(); int count = Count(r); var layout = new List<KnownTile>();
            for (int i = 0; i < count; i++) layout.Add(new KnownTile(new Id<TileTag>(r.ReadInt64()), ReadCoord(r), r.ReadInt64()));
            count = Count(r); var services = new List<PublicService>();
            for (int i = 0; i < count; i++)
            { long id = r.ReadInt64(); var position = ReadCoord(r); var capabilities = (TerminalCapability)r.ReadInt32(); int n = Count(r); var roles = new List<string>(); for (int j = 0; j < n; j++) roles.Add(r.ReadString()); services.Add(new PublicService(id, position, capabilities, roles)); }
            publicKnowledge = new PublicKnowledge(version, layout, services, ReadPublicWork(r));
            foreach (var actor in actors.Values)
            {
                actor.Knowledge.Sequence = r.ReadInt64(); count = Count(r);
                for (int i = 0; i < count; i++) { var f = ReadFact(r); actor.Knowledge.Entries.Add(f.Key, new KnownEntry { Fact = f, Recency = r.ReadInt64() }); }
                if (actor.Knowledge.Entries.Count > Information.KnowledgeCapacity) throw new ArgumentException("Snapshot knowledge exceeds capacity.");
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long id = r.ReadInt64(); var pos = ReadCoord(r); int capacity = r.ReadInt32(); var capabilities = (TerminalCapability)r.ReadInt32(); int n = Count(r); var roles = new List<string>(); for (int j = 0; j < n; j++) roles.Add(r.ReadString());
                bool enabled = r.ReadBoolean(), connected = r.ReadBoolean(); var terminal = new Terminal { Definition = new TerminalSeed(id, pos, roles, capacity, capabilities, enabled, connected), Enabled = enabled, Connected = connected };
                n = Count(r); for (int j = 0; j < n; j++) terminal.Claims.Add(new Id<ActorTag>(r.ReadInt64()));
                n = Count(r); for (int j = 0; j < n; j++) terminal.Queue.Add(new Id<ActorTag>(r.ReadInt64())); terminals.Add(id, terminal);
                if (terminal.Claims.Count > capacity || terminal.Claims.Distinct().Count() != terminal.Claims.Count) throw new ArgumentException("Snapshot terminal capacity invalid.");
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            { long id = r.ReadInt64(); var holder = new Id<ActorTag>(r.ReadInt64()); var account = new Id<AccountTag>(r.ReadInt64()); var card = new Id<ItemTag>(r.ReadInt64()); int generation = r.ReadInt32(); bool active = r.ReadBoolean(); credentials.Add(id, new Credential { Definition = new CredentialSeed(id, holder, account, card, active, generation), Active = active }); }
            count = Count(r);
            for (int i = 0; i < count; i++) { var s = new Session { Id = r.ReadInt64(), Actor = new Id<ActorTag>(r.ReadInt64()), Account = new Id<AccountTag>(r.ReadInt64()), Terminal = r.ReadInt64(), Credential = r.ReadInt64(), Expires = r.ReadInt64(), Scope = ReadEnum<SessionScope>(r) }; sessions.Add(s.Actor, s); }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var m = new MessageSnapshot { Id = r.ReadInt64(), Draft = ReadDraft(r) ?? throw new ArgumentException("Missing message draft."), PhysicalSender = new Id<ActorTag>(r.ReadInt64()), SenderAccount = new Id<AccountTag>(r.ReadInt64()), TerminalId = r.ReadInt64(), SessionId = r.ReadInt64(), SubmittedTick = r.ReadInt64(), DeliveryTick = r.ReadInt64() };
                if (r.ReadBoolean()) m.FirstReadTick = r.ReadInt64(); m.Stage = ReadEnum<MessageStage>(r); m.Seeded = r.ReadBoolean(); messages.Add(m.Id, m);
                IndexAuthorityVisit(m);
                IndexUnreadMessage(m);
            }
            count = Count(r); for (int i = 0; i < count; i++) { var b = ReadBelief(r); beliefs.Add(b.Key, b); }
            count = Count(r); for (int i = 0; i < count; i++) beliefHistory.Add(ReadBelief(r));
            ReadPlanning(r); ReadCredentialRecovery(r);
        }
    }
}
