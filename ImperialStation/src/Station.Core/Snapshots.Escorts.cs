using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteEscorts(BinaryWriter w)
        {
            w.Write(escorts.Count);
            foreach (var g in escorts.Values)
            {
                w.Write(g.AuthorizationId); w.Write(g.DispatchId); w.Write(g.BeginActionId); w.Write(g.StartedTick); w.Write(g.BoundaryTick); w.Write((int)g.Status); w.Write(g.Interruption); w.Write(g.Members.Count);
                foreach (var m in g.Members) { w.Write(m.ActorId); w.Write(m.OrderMessageId); w.Write(m.MovementActionId); }
            }
        }
        private void ReadEscorts(BinaryReader r)
        {
            int count = Count(r); var activeMembers = new SortedSet<long>(); var movementIds = new SortedSet<long>();
            for (int i = 0; i < count; i++)
            {
                var g = new EscortSnapshot { AuthorizationId = r.ReadInt64(), DispatchId = r.ReadInt64(), BeginActionId = r.ReadInt64(), StartedTick = r.ReadInt64(), BoundaryTick = r.ReadInt64(), Status = ReadEnum<EscortStatus>(r), Interruption = r.ReadString() };
                var members = new EscortMemberSnapshot[Count(r)];
                for (int j = 0; j < members.Length; j++) members[j] = new EscortMemberSnapshot { ActorId = r.ReadInt64(), OrderMessageId = r.ReadInt64(), MovementActionId = r.ReadInt64() }; g.Members = members;
                var payload = messages.TryGetValue(g.DispatchId, out var source) ? source.Draft.EscortOrder : null;
                if (payload == null || payload.AuthorizationId != g.AuthorizationId || payload.DispatchId != g.DispatchId || !localGuardTeams.TryGetValue(g.DispatchId, out var team) || team.FormedTick < 0 ||
                    g.BeginActionId < 1 || g.BeginActionId >= nextAction || g.StartedTick < team.FormedTick || g.StartedTick >= payload.Expires || g.BoundaryTick < g.StartedTick || g.BoundaryTick > LastProcessedTick ||
                    !members.Select(m => m.ActorId).SequenceEqual(payload.Guards.Concat(new[] { payload.SubjectActorId }).OrderBy(id => id)) ||
                    g.Status == EscortStatus.Interrupted && g.Interruption.Length == 0 || g.Status == EscortStatus.AwaitingIntake && (g.Interruption.Length > 0 || payload.Expires <= LastProcessedTick)) throw new ArgumentException("Invalid escort custody history.");
                ActiveAction? shared = null;
                foreach (var m in members)
                {
                    var actor = actors[new Id<ActorTag>(m.ActorId)];
                    if (m.ActorId == payload.SubjectActorId ? m.OrderMessageId != 0 : !localGuardOrders.TryGetValue(m.OrderMessageId, out var l) || l.ActorId != m.ActorId || !l.Payload.Same(payload) ||
                        l.ReadTick > g.StartedTick || l.AcknowledgmentMessageId == 0 || l.FormedTick != team.FormedTick || l.SearchObservations.LastOrDefault()?.Found != true || l.SearchObservations.Last().Tick > g.StartedTick)
                        throw new ArgumentException("Escort member lacks local read and identification proof.");
                    if (m.MovementActionId < 0 || m.MovementActionId >= nextAction || m.MovementActionId > 0 && (m.MovementActionId <= g.BeginActionId || !movementIds.Add(m.MovementActionId))) throw new ArgumentException("Invalid escort movement identity.");
                    if (g.Status is EscortStatus.Interrupted or EscortStatus.Released || g.Status == EscortStatus.Detained && m.OrderMessageId > 0) continue;
                    if (!activeMembers.Add(m.ActorId)) throw new ArgumentException("Actor belongs to overlapping custody groups.");
                    if (sessions.ContainsKey(actor.Id) && !GuardIntakeCommunication(g, actor.Id, ActionKind.ReadInbox) || m.OrderMessageId == 0 && localOrders.Values.Any(o => o.ActorId == m.ActorId && o.Status is LocalOrderStatus.Read or LocalOrderStatus.AcknowledgmentPending or LocalOrderStatus.Working)) throw new ArgumentException("Custody member retains an unrestricted session or target work.");
                    if (m.OrderMessageId > 0 && !PhysicallyEquipped(actor, payload.Dispatch.RequiredEquipment)) throw new ArgumentException("Active escort guard lacks equipment.");
                    if (g.Status == EscortStatus.Detained)
                    {
                        if (actor.Position != payload.Dispatch.Destination || actor.Action != null && actor.Action.Kind != ActionKind.ReleaseDetention && !IsNeedAction(actor.Action.Kind)) throw new ArgumentException("Detained target is absent from its facility.");
                        continue;
                    }
                    if (g.Status == EscortStatus.AwaitingIntake)
                    {
                        if (actor.Action != null && !GuardIntakeCommunication(g, actor.Id, actor.Action.Kind) && !(actor.Id.Value == payload.Leader && actor.Action.Kind == ActionKind.RegisterIntake && actor.Action.GuardOrderId == m.OrderMessageId) || actor.Position != payload.Dispatch.Destination) throw new ArgumentException("Intake waiting group is not physically present.");
                        continue;
                    }
                    var a = actor.Action;
                    if (a == null || a.Kind != ActionKind.Move || a.Id.Value != m.MovementActionId || a.GuardOrderId != m.OrderMessageId || a.AttemptId != 0 || a.PatrolOrderId != 0 || a.Started != g.StartedTick ||
                        a.Target != payload.Dispatch.Destination || a.Route.Count < 2 || a.RouteIndex < 0 || a.RouteIndex >= a.Route.Count - 1 || a.Route[a.RouteIndex] != actor.Position || a.Source != actor.Position ||
                        a.Destination != a.Route[a.RouteIndex + 1] || a.Route.Last() != a.Target || a.Route.Any(c => !Topology.Contains(c)) || a.Route.Where((c, index) => index > 0 && c.Manhattan(a.Route[index - 1]) != 1).Any() ||
                        a.Departure != g.BoundaryTick || a.Arrival <= a.Departure || a.Arrival <= LastProcessedTick ||
                        a.CancelAtBoundary != (g.Interruption.Length > 0) || payload.Expires <= LastProcessedTick && g.Interruption.Length == 0 ||
                        scheduled.Count(e => e.Kind == ScheduledKind.ActionCompletion && e.Intent.Actor == actor.Id && e.ActionId == a.Id.Value && e.Tick == a.Arrival && e.Phase == EventPhase.PhysicalCompletion) != 1)
                        throw new ArgumentException("Invalid coupled escort movement.");
                    if (shared != null && (!shared.Route.SequenceEqual(a.Route) || shared.RouteIndex != a.RouteIndex || shared.Departure != a.Departure || shared.Arrival != a.Arrival || shared.CauseId != a.CauseId)) throw new ArgumentException("Escort members have divergent shared movement.");
                    shared = a;
                }
                escorts.Add(g.DispatchId, g);
            }
            foreach (var l in localGuardOrders.Values.Where(l => l.Status == GuardOrderStatus.EscortInterrupted))
                if (!escorts.TryGetValue(l.Payload.DispatchId, out var g) || g.Status != EscortStatus.Interrupted || !g.Members.Any(m => m.OrderMessageId == l.OrderMessageId) || l.Reason != g.Interruption)
                    throw new ArgumentException("Interrupted guard lacks a matching local escort history.");
            foreach (var actor in actors.Values.Where(a => a.Action?.Kind == ActionKind.BeginEscort))
            {
                var a = actor.Action!;
                if (!localGuardOrders.TryGetValue(a.GuardOrderId, out var l) || l.Payload.Leader != actor.Id.Value || l.FormedTick < 0 || a.Completes != checked(a.Started + Information!.InspectTicks) || actor.Position != a.Target ||
                    scheduled.Count(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == a.Id.Value && e.Intent.Actor == actor.Id && e.Tick == a.Completes) != 1) throw new ArgumentException("Invalid pending custody action.");
            }
        }
    }
}
