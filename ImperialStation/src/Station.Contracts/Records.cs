using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum RecordKind { Credential, TerminalSession, WorkAttempt, WorkReport, Roster, ReportExpectation, AcknowledgmentWatch, MissingWorkReport, IncidentResponse, QueryDisclosure, Case, Reprimand, DetentionRequest, DetentionAuthorization, GuardAssignment, DetentionIntake, DetentionRelease, GuardOutcome, BerthAllocation, CredentialRecovery }

    // The query contains no caller-supplied account/permission override. IRecordStore is scope-bound.
    public sealed class RecordQuery
    {
        public RecordKind Kind { get; }
        public long EntityId { get; }
        public long After { get; }
        public int Limit { get; }
        public bool History { get; }
        public long MachineId { get; }
        public RecordQuery(RecordKind kind, long entityId = 0, long after = 0, int limit = 16, bool history = false, long machineId = 0)
        {
            if (!Enum.IsDefined(typeof(RecordKind), kind) || entityId < 0 || after < 0 || limit < 1 || limit > 32) throw new ArgumentException("Invalid record query kind, ID, cursor or limit (1..32).");
            if (machineId < 0 || machineId > 0 && kind is not (RecordKind.WorkAttempt or RecordKind.WorkReport)) throw new ArgumentException("Machine scope requires a work record kind.");
            Kind = kind; EntityId = entityId; After = after; Limit = limit; History = history; MachineId = machineId;
        }
    }
    public readonly struct RecordField
    {
        public AttributeKind Attribute { get; }
        public FactValue Value { get; }
        public RecordField(AttributeKind attribute, FactValue value) { Attribute = attribute; Value = value; }
    }
    public sealed class RecordEntry
    {
        public RecordKind Kind { get; }
        public long EntityId { get; }
        public long AccountId { get; }
        public long Version { get; }
        public long Tick { get; }
        public string OperationKey { get; }
        public IReadOnlyList<RecordField> Fields { get; }
        public RecordEntry(RecordKind kind, long entityId, long accountId, long version, long tick, string operationKey, IEnumerable<RecordField> fields)
        {
            if (!Enum.IsDefined(typeof(RecordKind), kind) || entityId < 1 || accountId < 1 || version < 1 || tick < -1 || string.IsNullOrWhiteSpace(operationKey)) throw new ArgumentException("Invalid record entry.");
            var data = fields.OrderBy(f => f.Attribute).ToArray();
            if (data.Length > 32 || data.Select(f => f.Attribute).Distinct().Count() != data.Length || data.Any(f => !Enum.IsDefined(typeof(AttributeKind), f.Attribute) || !Enum.IsDefined(typeof(FactValueKind), f.Value.Kind))) throw new ArgumentException("Invalid record fields.");
            Kind = kind; EntityId = entityId; AccountId = accountId; Version = version; Tick = tick; OperationKey = operationKey; Fields = Array.AsReadOnly(data);
        }
    }
    public sealed class RecordPage
    {
        public long Version { get; }
        public long NextCursor { get; }
        public bool HasMore { get; }
        public IReadOnlyList<RecordEntry> Entries { get; }
        public RecordPage(long version, long nextCursor, bool hasMore, IEnumerable<RecordEntry> entries)
        { Version = version; NextCursor = nextCursor; HasMore = hasMore; Entries = Array.AsReadOnly(entries.ToArray()); }
    }
    // One bounded receipt per actor; row content remains in the ordinary capped knowledge store.
    public sealed class RecordQueryReceipt
    {
        public RecordQuery Query { get; }
        public long Version { get; }
        public long NextCursor { get; }
        public bool HasMore { get; }
        public long CompletedTick { get; }
        public int DisclosedRows { get; }
        public RecordQueryReceipt(RecordQuery query, long version, long nextCursor, bool hasMore, long completedTick, int disclosedRows)
        { Query = query; Version = version; NextCursor = nextCursor; HasMore = hasMore; CompletedTick = completedTick; DisclosedRows = disclosedRows; }
    }
}
