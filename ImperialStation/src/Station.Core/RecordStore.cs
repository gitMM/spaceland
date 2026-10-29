using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    // Only authenticated services construct a scoped interface. This store never reads physical state.
    internal sealed class RecordLedger
    {
        internal readonly SortedDictionary<long, RecordEntry> History = new SortedDictionary<long, RecordEntry>();
        private readonly SortedDictionary<string, RecordEntry> keys = new SortedDictionary<string, RecordEntry>(StringComparer.Ordinal);
        private readonly SortedDictionary<(RecordKind Kind, long Account), SortedDictionary<long, RecordEntry>> current = new SortedDictionary<(RecordKind, long), SortedDictionary<long, RecordEntry>>();
        private readonly SortedDictionary<(RecordKind Kind, long Account), SortedDictionary<long, RecordEntry>> history = new SortedDictionary<(RecordKind, long), SortedDictionary<long, RecordEntry>>();
        internal long Version { get; private set; }
        private readonly SortedDictionary<(RecordKind Kind, long Account), long> versions = new SortedDictionary<(RecordKind, long), long>();
        internal void Append(RecordKind kind, long entity, long account, long tick, string key, IEnumerable<RecordField> fields)
        {
            var entry = new RecordEntry(kind, entity, account, checked(Version + 1), tick, key, fields);
            if (keys.TryGetValue(key, out var prior))
            {
                if (prior.Kind != kind || prior.EntityId != entity || prior.AccountId != account || !prior.Fields.SequenceEqual(entry.Fields)) throw new ArgumentException("Record idempotency conflict.");
                return;
            }
            RestoreEntry(entry);
        }
        internal void RestoreEntry(RecordEntry entry)
        {
            if (entry.Version != Version + 1) throw new ArgumentException("Non-contiguous record history.");
            keys.Add(entry.OperationKey, entry); History.Add(entry.Version, entry);
            Version = entry.Version;
            Index(entry.Kind, entry.AccountId, entry); Index(entry.Kind, 0, entry);
        }
        private void Index(RecordKind kind, long account, RecordEntry entry)
        {
            var key = (kind, account);
            if (!current.TryGetValue(key, out var rows)) { rows = new SortedDictionary<long, RecordEntry>(); current.Add(key, rows); history.Add(key, new SortedDictionary<long, RecordEntry>()); }
            if (rows.TryGetValue(entry.EntityId, out var prior) && prior.AccountId != entry.AccountId) throw new ArgumentException("Record ownership cannot change.");
            rows[entry.EntityId] = entry; history[key].Add(entry.Version, entry);
            versions[key] = entry.Version;
        }
        internal IRecordStore Scope(RecordKind kind, long account) => new ScopedStore(this, kind, account);
        private sealed class ScopedStore : IRecordStore
        {
            private readonly RecordLedger ledger; private readonly RecordKind kind; private readonly long account;
            internal ScopedStore(RecordLedger ledger, RecordKind kind, long account) { this.ledger = ledger; this.kind = kind; this.account = account; }
            public long Version => ledger.versions.TryGetValue((kind, account), out long version) ? version : 0;
            public RecordPage Query(RecordQuery query)
            {
                if (query.Kind != kind) throw new ArgumentException("Query outside record service scope.");
                var index = query.History ? ledger.history : ledger.current;
                if (!index.TryGetValue((kind, account), out var rows)) return new RecordPage(Version, query.After, false, Array.Empty<RecordEntry>());
                IEnumerable<KeyValuePair<long, RecordEntry>> eligible;
                if (!query.History && query.EntityId > 0)
                    eligible = query.EntityId > query.After && rows.TryGetValue(query.EntityId, out var entry) ? new[] { new KeyValuePair<long, RecordEntry>(query.EntityId, entry) } : Array.Empty<KeyValuePair<long, RecordEntry>>();
                else eligible = rows.Where(p => p.Key > query.After && (query.EntityId == 0 || p.Value.EntityId == query.EntityId));
                if (query.MachineId > 0) eligible = eligible.Where(p => p.Value.Fields.Any(f => f.Attribute == AttributeKind.MachineId && f.Value.Equals(FactValue.Integer(query.MachineId))));
                var selected = eligible.Take(query.Limit + 1).ToArray();
                var page = selected.Take(query.Limit).ToArray();
                // A filtered history scan reaches the scoped ledger's watermark even when
                // its last relevant row precedes unrelated records. No rows are disclosed by this cursor.
                long cursor = query.History && query.MachineId > 0 && selected.Length <= query.Limit ? Math.Max(query.After, Version) : page.Length == 0 ? query.After : page[page.Length - 1].Key;
                return new RecordPage(Version, cursor, selected.Length > query.Limit, page.Select(p => p.Value));
            }
        }
    }
    public sealed class RecordAccessSeed
    {
        public Id<ActorTag> Actor { get; }
        public long TerminalId { get; }
        public string RequiredRole { get; }
        public RecordKind Kind { get; }
        public RecordAccessSeed(Id<ActorTag> actor, long terminalId, string requiredRole, RecordKind kind)
        { Actor = actor; TerminalId = terminalId; RequiredRole = requiredRole; Kind = kind; }
    }
}
