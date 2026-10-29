using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteQuery(BinaryWriter w, RecordQuery? query)
        {
            w.Write(query != null); if (query == null) return;
            w.Write((int)query.Kind); w.Write(query.EntityId); w.Write(query.After); w.Write(query.Limit); w.Write(query.History); w.Write(query.MachineId);
        }
        private static RecordQuery? ReadQuery(BinaryReader r) => r.ReadBoolean() ? new RecordQuery(ReadEnum<RecordKind>(r), r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadBoolean(), r.ReadInt64()) : null;
        private static void WriteRecord(BinaryWriter w, RecordEntry entry)
        {
            w.Write((int)entry.Kind); w.Write(entry.EntityId); w.Write(entry.AccountId); w.Write(entry.Version); w.Write(entry.Tick); w.Write(entry.OperationKey); w.Write(entry.Fields.Count);
            foreach (var field in entry.Fields) { w.Write((int)field.Attribute); WriteValue(w, field.Value); }
        }
        private static RecordEntry ReadRecord(BinaryReader r)
        {
            var kind = ReadEnum<RecordKind>(r); long id = r.ReadInt64(), account = r.ReadInt64(), version = r.ReadInt64(), tick = r.ReadInt64(); string key = r.ReadString();
            int count = Count(r); var fields = new List<RecordField>(); for (int i = 0; i < count; i++) fields.Add(new RecordField(ReadEnum<AttributeKind>(r), ReadValue(r)));
            return new RecordEntry(kind, id, account, version, tick, key, fields);
        }
        private static void WriteRecordPage(BinaryWriter w, RecordPage? page)
        {
            w.Write(page != null); if (page == null) return;
            w.Write(page.Version); w.Write(page.NextCursor); w.Write(page.HasMore); w.Write(page.Entries.Count); foreach (var row in page.Entries) WriteRecord(w, row);
        }
        private static RecordPage? ReadRecordPage(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            long version = r.ReadInt64(), cursor = r.ReadInt64(); bool more = r.ReadBoolean(); int count = Count(r);
            if (version < 0 || cursor < 0 || count > 32) throw new ArgumentException("Invalid captured record page.");
            var entries = new List<RecordEntry>(); for (int i = 0; i < count; i++) entries.Add(ReadRecord(r));
            return new RecordPage(version, cursor, more, entries);
        }
        private void WriteRecords(BinaryWriter w)
        {
            w.Write(recordAccess.Count); foreach (var grant in recordAccess.Values) { w.Write(grant.Actor.Value); w.Write(grant.TerminalId); w.Write(grant.RequiredRole); w.Write((int)grant.Kind); }
            w.Write(records.History.Count); foreach (var entry in records.History.Values) WriteRecord(w, entry);
            w.Write(recordReceipts.Count);
            foreach (var pair in recordReceipts)
            { var receipt = pair.Value; w.Write(pair.Key.Value); WriteQuery(w, receipt.Query); w.Write(receipt.Version); w.Write(receipt.NextCursor); w.Write(receipt.HasMore); w.Write(receipt.CompletedTick); w.Write(receipt.DisclosedRows); }
        }
        private void ReadRecords(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var grant = new RecordAccessSeed(new Id<ActorTag>(r.ReadInt64()), r.ReadInt64(), r.ReadString(), ReadEnum<RecordKind>(r));
                if (!actors.TryGetValue(grant.Actor, out var actor) || actor.Role != grant.RequiredRole || !terminals.TryGetValue(grant.TerminalId, out var terminal) || (terminal.Definition.Capabilities & TerminalCapability.Records) == 0 ||
                    !terminal.Definition.PermittedRoles.Contains(grant.RequiredRole, StringComparer.Ordinal) || !(grant.Kind is RecordKind.WorkAttempt or RecordKind.WorkReport)) throw new ArgumentException("Invalid snapshot record grant.");
                recordAccess.Add((grant.Actor.Value, grant.TerminalId, grant.Kind), grant);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var entry = ReadRecord(r);
                if (Information == null && entry.Kind != RecordKind.BerthAllocation || RuntimeError == null && entry.Tick > LastProcessedTick || !actors.Values.Any(a => a.Account.Value == entry.AccountId)) throw new ArgumentException("Invalid snapshot record owner or time.");
                records.RestoreEntry(entry);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var actor = new Id<ActorTag>(r.ReadInt64()); var query = ReadQuery(r) ?? throw new ArgumentException("Missing receipt query.");
                var receipt = new RecordQueryReceipt(query, r.ReadInt64(), r.ReadInt64(), r.ReadBoolean(), r.ReadInt64(), r.ReadInt32());
                if (!actors.ContainsKey(actor) || receipt.Version < 0 || receipt.Version > records.Version || receipt.NextCursor < 0 || receipt.CompletedTick < 0 || receipt.CompletedTick > LastProcessedTick || receipt.DisclosedRows < 0 || receipt.DisclosedRows > query.Limit) throw new ArgumentException("Invalid query receipt.");
                recordReceipts.Add(actor, receipt);
            }
            foreach (var actor in actors.Values)
            {
                var action = actor.Action; if (action == null) continue;
                if (action.Kind != ActionKind.QueryRecords)
                { if (action.Query != null || action.CapturedRecords != null) throw new ArgumentException("Query data on non-query action."); continue; }
                if (action.Query == null || action.CapturedRecords == null || action.CapturedRecords.Entries.Count > action.Query.Limit || action.SessionId < 1) throw new ArgumentException("Missing bound snapshot query.");
                foreach (var entry in action.CapturedRecords.Entries)
                {
                    if (!records.History.TryGetValue(entry.Version, out var original) || original.Kind != entry.Kind || original.EntityId != entry.EntityId || original.AccountId != entry.AccountId || original.Tick != entry.Tick || original.OperationKey != entry.OperationKey || !original.Fields.SequenceEqual(entry.Fields) || entry.Kind != action.Query.Kind || entry.Version > action.CapturedRecords.Version ||
                        action.Query.MachineId > 0 && !entry.Fields.Any(f => f.Attribute == AttributeKind.MachineId && f.Value.Equals(FactValue.Integer(action.Query.MachineId))))
                        throw new ArgumentException("Captured query record does not match immutable history.");
                }
            }
        }
    }
}
