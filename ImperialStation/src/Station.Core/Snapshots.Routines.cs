using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteRosterPayload(BinaryWriter w, RosterPayload? r)
        {
            w.Write(r != null); if (r == null) return;
            w.Write(r.Version); w.Write(r.ActorId); w.Write(r.Cohort); w.Write(r.DayTicks); w.Write(r.ShiftStart); w.Write(r.DutyTicks); w.Write(r.CheckInOffset);
            w.Write(r.RestTile.HasValue); if (r.RestTile.HasValue) WriteCoord(w, r.RestTile.Value);
        }
        private static RosterPayload? ReadRosterPayload(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            long version = r.ReadInt64(), actor = r.ReadInt64(); int cohort = r.ReadInt32(); long day = r.ReadInt64(), start = r.ReadInt64(), duty = r.ReadInt64(); int offset = r.ReadInt32();
            return new RosterPayload(version, actor, cohort, day, start, duty, offset, r.ReadBoolean() ? ReadCoord(r) : (Coord?)null);
        }
        private static bool SameRoster(RosterPayload a, RosterPayload b) => a.Version == b.Version && a.ActorId == b.ActorId && a.Cohort == b.Cohort && a.DayTicks == b.DayTicks && a.ShiftStart == b.ShiftStart && a.DutyTicks == b.DutyTicks && a.CheckInOffset == b.CheckInOffset && a.RestTile == b.RestTile;
        private void WriteRoutines(BinaryWriter w)
        {
            w.Write(Routines != null); if (Routines == null) return;
            w.Write(Routines.DayTicks); w.Write(Routines.Cohorts); w.Write(Routines.DutyTicks); w.Write(Routines.CheckInWindow); w.Write(Routines.PeriodicCheckInTicks);
            w.Write(nextRoster); w.Write(rosters.Count);
            foreach (var r in rosters.Values) { WriteRosterPayload(w, r.Roster); w.Write(r.OperationKey); w.Write(r.PublicationTick); w.Write(r.Published); w.Write(r.MessageId); }
            w.Write(routines.Count);
            foreach (var s in routines.Values)
            { w.Write(s.ActorId); w.Write(s.KnownRoster?.Version ?? 0); w.Write(s.OnDuty); w.Write(s.Activity); w.Write(s.NextBoundary); w.Write(s.NextShiftCheckIn); w.Write(s.NextDecision); w.Write(s.EndShiftCheckIn); }
        }
        private void ReadRoutines(BinaryReader r)
        {
            if (!r.ReadBoolean()) return;
            if (Information == null) throw new ArgumentException("Saved routines require information.");
            Routines = new RoutineConfig(r.ReadInt64(), r.ReadInt32(), r.ReadInt64(), r.ReadInt32(), r.ReadInt64());
            nextRoster = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var roster = ReadRosterPayload(r) ?? throw new ArgumentException("Missing saved roster.");
                var entry = new RosterSnapshot { Roster = roster, OperationKey = r.ReadString(), PublicationTick = r.ReadInt64(), Published = r.ReadBoolean(), MessageId = r.ReadInt64() };
                if (roster.Version != i + 1L || !actors.ContainsKey(new Id<ActorTag>(roster.ActorId)) || roster.Cohort >= Routines.Cohorts || roster.DayTicks != Routines.DayTicks || roster.DutyTicks != Routines.DutyTicks || roster.ShiftStart != roster.Cohort * (Routines.DayTicks / Routines.Cohorts) || roster.CheckInOffset >= Routines.CheckInWindow || string.IsNullOrWhiteSpace(entry.OperationKey) || entry.PublicationTick < -1 ||
                    roster.RestTile.HasValue && !publicKnowledge!.Layout.Any(t => t.Position == roster.RestTile.Value)) throw new ArgumentException("Invalid saved roster registration.");
                rosters.Add(roster.Version, entry); rosterKeys.Add(entry.OperationKey, roster.Version);
                var pending = scheduled.Where(e => e.Kind == ScheduledKind.PublishRoster && e.Value == roster.Version).ToArray();
                if (RuntimeError == null && (entry.Published != (entry.PublicationTick <= LastProcessedTick) || pending.Length != (entry.Published ? 0 : 1)) || pending.Any(e => e.Tick != entry.PublicationTick || e.Phase != EventPhase.Delivery)) throw new ArgumentException("Invalid roster publication schedule.");
                if (entry.MessageId != 0 && (!entry.Published || !messages.TryGetValue(entry.MessageId, out var message) || message.SenderAccount.Value != 0 || message.Draft.Roster == null || !SameRoster(message.Draft.Roster, roster))) throw new ArgumentException("Invalid roster publication message.");
            }
            if (nextRoster != rosters.Count + 1L) throw new ArgumentException("Invalid roster sequence.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var s = new RoutineSnapshot { ActorId = r.ReadInt64() }; long known = r.ReadInt64();
                s.KnownRoster = known == 0 ? null : rosters.TryGetValue(known, out var found) && found.Published ? found.Roster : throw new ArgumentException("Unknown local roster.");
                s.OnDuty = r.ReadBoolean(); s.Activity = r.ReadString(); s.NextBoundary = r.ReadInt64(); s.NextShiftCheckIn = r.ReadInt64(); s.NextDecision = r.ReadInt64(); s.EndShiftCheckIn = r.ReadBoolean();
                var actorId = new Id<ActorTag>(s.ActorId);
                if (!actors.ContainsKey(actorId) || !checkIns.ContainsKey(actorId) || s.KnownRoster != null && s.KnownRoster.ActorId != s.ActorId || s.NextDecision < -1) throw new ArgumentException("Invalid local routine owner.");
                routines.Add(actorId, s);
                ValidateRoutineEvent(actorId, ScheduledKind.RoutineBoundary, s.NextBoundary);
                ValidateRoutineEvent(actorId, ScheduledKind.ShiftCheckIn, s.NextShiftCheckIn);
                ValidateRoutineEvent(actorId, ScheduledKind.EvaluateRoutine, s.NextDecision);
                if (s.KnownRoster != null)
                {
                    var fact = actors[actorId].Knowledge.Entries.Values.SingleOrDefault(e => e.Fact.Key.Equals(new FactKey(SubjectKind.Actor, s.ActorId, AttributeKind.RosterVersion)))?.Fact;
                    if (fact == null || !fact.Pinned || fact.Value.Number != known || !(fact.SourceKind is EvidenceKind.Seeded or EvidenceKind.MessageRead)) throw new ArgumentException("Known roster lacks pinned evidence.");
                    if (RuntimeError == null && (s.NextBoundary != s.KnownRoster.NextBoundary(Math.Max(0, LastProcessedTick)) || LastProcessedTick >= 0 && s.OnDuty != s.KnownRoster.OnDuty(LastProcessedTick))) throw new ArgumentException("Invalid saved duty boundary.");
                }
                else if (s.NextBoundary != -1 || s.NextShiftCheckIn != -1) throw new ArgumentException("Unknown roster has a private schedule.");
            }
            foreach (var e in scheduled.Where(e => e.Kind is ScheduledKind.PublishRoster or ScheduledKind.RoutineBoundary or ScheduledKind.ShiftCheckIn or ScheduledKind.EvaluateRoutine))
                if (e.Kind == ScheduledKind.PublishRoster ? !rosters.ContainsKey(e.Value) : !routines.ContainsKey(e.Intent.Actor)) throw new ArgumentException("Unknown routine event owner.");
            foreach (var message in messages.Values.Where(m => m.Draft.Kind == MessageKind.RosterUpdated || m.Draft.Roster != null))
            {
                var payload = message.Draft.Roster;
                if (payload == null || !rosters.TryGetValue(payload.Version, out var entry) || !entry.Published || entry.MessageId != message.Id || !SameRoster(entry.Roster, payload) || message.SenderAccount.Value != 0 || !ValidDraft(message.Draft)) throw new ArgumentException("Roster message lacks institutional provenance.");
            }
        }
        private void ValidateRoutineEvent(Id<ActorTag> actor, ScheduledKind kind, long tick)
        {
            var pending = scheduled.Where(e => e.Kind == kind && e.Intent.Actor == actor).ToArray();
            if (RuntimeError == null && (pending.Length != (tick < 0 ? 0 : 1) || tick >= 0 && tick <= LastProcessedTick) || pending.Length > 1 || pending.Any(e => e.Tick != tick || e.Phase != EventPhase.Decision)) throw new ArgumentException("Invalid routine watchdog.");
        }
    }
}
