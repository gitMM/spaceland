using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WritePatrolPayload(BinaryWriter w, PatrolOrderPayload? p)
        {
            w.Write(p != null); if (p == null) return;
            w.Write(p.IncidentId); w.Write(p.TargetActor.Value); w.Write(p.Expires); w.Write(p.RequiredRole); w.Write(p.Candidates.Count);
            foreach (var tile in p.Candidates) WriteCoord(w, tile);
        }
        private static PatrolOrderPayload? ReadPatrolPayload(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            long incident = r.ReadInt64(); var target = new Id<ActorTag>(r.ReadInt64()); long expires = r.ReadInt64(); string role = r.ReadString();
            int count = Count(r); if (count > 8) throw new ArgumentException("Patrol candidate bound exceeded.");
            var candidates = new List<Coord>(); for (int i = 0; i < count; i++) candidates.Add(ReadCoord(r));
            return new PatrolOrderPayload(incident, target, expires, role, candidates);
        }
        private void WritePatrols(BinaryWriter w)
        {
            w.Write(patrolOrders.Count);
            foreach (var p in patrolOrders.Values)
            { w.Write(p.OrderMessageId); w.Write(p.ActorId); w.Write(p.AcknowledgmentMessageId); w.Write(p.ResultMessageId); w.Write(p.Status); }
            w.Write(localPatrols.Count);
            foreach (var p in localPatrols.Values)
            {
                w.Write(p.OrderMessageId); w.Write(p.ActorId); w.Write(p.ReadTick); w.Write((int)p.Status); w.Write(p.CandidateIndex); w.Write(p.NextDecision); w.Write(p.Backoff); w.Write(p.Reason);
                w.Write(p.Observations.Count); foreach (var o in p.Observations) { w.Write(o.Tick); w.Write(o.ActionId); WriteCoord(w, o.Tile); w.Write(o.Found); }
            }
        }
        private void ReadPatrols(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var p = new PatrolOrderSnapshot { OrderMessageId = r.ReadInt64(), ActorId = r.ReadInt64(), AcknowledgmentMessageId = r.ReadInt64(), ResultMessageId = r.ReadInt64(), Status = r.ReadString() };
                if (!messages.TryGetValue(p.OrderMessageId, out var message) || message.Draft.Kind != MessageKind.PatrolOrder || message.Draft.PatrolOrder == null || message.SenderAccount.Value != 0 ||
                    !registrations.TryGetValue(p.ActorId, out var person) || message.Draft.Recipient.Value != person.AccountId) throw new ArgumentException("Invalid institutional patrol order.");
                p.Payload = message.Draft.PatrolOrder;
                if (!missingReports.Values.Any(v => v.Id == p.Payload.IncidentId && v.ActorId == p.Payload.TargetActor.Value) || !actors.ContainsKey(p.Payload.TargetActor) || p.Payload.Expires <= message.SubmittedTick) throw new ArgumentException("Invalid patrol target or incident.");
                if (p.AcknowledgmentMessageId > 0) ValidatePatrolReport(p, p.AcknowledgmentMessageId, true);
                if (p.ResultMessageId > 0) ValidatePatrolReport(p, p.ResultMessageId, false);
                string expected = p.ResultMessageId > 0 ? messages[p.ResultMessageId].Draft.Kind == MessageKind.PatrolFound ? "ReportedFound" : messages[p.ResultMessageId].Draft.Kind == MessageKind.PatrolNotLocated ? "ReportedNotLocated" : "ReportedBlocked" : p.AcknowledgmentMessageId > 0 ? "Accepted" : "Posted";
                if (p.Status != expected) throw new ArgumentException("Invalid patrol response status.");
                patrolOrders.Add(p.OrderMessageId, p);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var p = new LocalPatrolSnapshot { OrderMessageId = r.ReadInt64(), ActorId = r.ReadInt64(), ReadTick = r.ReadInt64(), Status = ReadEnum<LocalPatrolStatus>(r), CandidateIndex = r.ReadInt32(), NextDecision = r.ReadInt64(), Backoff = r.ReadInt64(), Reason = r.ReadString() };
                if (!patrolOrders.TryGetValue(p.OrderMessageId, out var order) || order.ActorId != p.ActorId || messages[p.OrderMessageId].FirstReadTick != p.ReadTick || messages[p.OrderMessageId].Stage != MessageStage.Read) throw new ArgumentException("Patrol awareness lacks a completed read.");
                p.Payload = order.Payload;
                int n = Count(r); var observations = new List<PatrolObservation>();
                for (int j = 0; j < n; j++) observations.Add(new PatrolObservation(r.ReadInt64(), r.ReadInt64(), ReadCoord(r), r.ReadBoolean()));
                p.Observations = observations.ToArray();
                if (n > p.Payload.Candidates.Count || p.CandidateIndex != n || p.Backoff < 1 || observations.Where((o, j) => o.ActionId < 1 || o.ActionId >= nextAction || o.Tick < p.ReadTick || RuntimeError == null && o.Tick > LastProcessedTick || o.Tile != p.Payload.Candidates[j] || o.Found && j != n - 1).Any()) throw new ArgumentException("Invalid patrol observations.");
                if (p.Status == LocalPatrolStatus.Searching && p.CandidateIndex >= p.Payload.Candidates.Count) throw new ArgumentException("Exhausted search remains active.");
                var events = scheduled.Where(e => e.Kind == ScheduledKind.EvaluatePatrol && e.Value == p.OrderMessageId).ToArray();
                if (events.Length != (p.NextDecision < 0 ? 0 : 1) || events.Any(e => e.Tick != p.NextDecision || e.Phase != EventPhase.Decision)) throw new ArgumentException("Invalid patrol decision watchdog.");
                var expiry = scheduled.Where(e => e.Kind == ScheduledKind.ExpirePatrol && e.Value == p.OrderMessageId).ToArray();
                if (RuntimeError == null && expiry.Length != (p.Status != LocalPatrolStatus.Completed && p.Payload.Expires > LastProcessedTick ? 1 : 0) || expiry.Any(e => e.Tick != p.Payload.Expires || e.Phase != EventPhase.Decision)) throw new ArgumentException("Invalid patrol expiry.");
                localPatrols.Add(p.OrderMessageId, p);
            }
            if (scheduled.Any(e => (e.Kind is ScheduledKind.EvaluatePatrol or ScheduledKind.ExpirePatrol) && !localPatrols.ContainsKey(e.Value))) throw new ArgumentException("Unknown patrol watchdog owner.");
            foreach (var actor in actors.Values.Where(a => a.Action?.PatrolOrderId > 0))
                if (!localPatrols.TryGetValue(actor.Action!.PatrolOrderId, out var order) || order.ActorId != actor.Id.Value) throw new ArgumentException("Patrol action lacks a read order.");
        }
        private void ValidatePatrolReport(PatrolOrderSnapshot order, long id, bool acknowledgment)
        {
            if (!messages.TryGetValue(id, out var m) || m.SenderAccount.Value != registrations[order.ActorId].AccountId || m.Draft.RelatedMessageId != order.OrderMessageId || m.Stage != MessageStage.Accepted ||
                (acknowledgment ? m.Draft.Kind != MessageKind.PatrolAccepted : !IsPatrolResult(m.Draft.Kind))) throw new ArgumentException("Invalid received patrol evidence.");
        }
    }
}
