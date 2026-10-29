using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteGuardSearch(BinaryWriter w)
        {
            w.Write(localGuardOrders.Count);
            foreach (var l in localGuardOrders.Values)
            {
                w.Write(l.OrderMessageId); w.Write(l.JoinTick); w.Write(l.JoinActionId); w.Write(l.FormedTick); w.Write(l.CandidateIndex); w.Write(l.SearchObservations.Count);
                foreach (var o in l.SearchObservations) { w.Write(o.Tick); w.Write(o.ActionId); WriteCoord(w, o.Tile); w.Write(o.Found); }
            }
            w.Write(localGuardTeams.Count);
            foreach (var t in localGuardTeams.Values)
            {
                w.Write(t.AuthorizationId); w.Write(t.DispatchId); w.Write(t.FormedTick); w.Write(t.Members.Count);
                foreach (var m in t.Members) { w.Write(m.ActorId); w.Write(m.OrderMessageId); w.Write(m.Tick); w.Write(m.ActionId); }
            }
        }
        private void ReadGuardSearch(BinaryReader r)
        {
            int count = Count(r); if (count != localGuardOrders.Count) throw new ArgumentException("Missing local guard search state.");
            var read = new SortedSet<long>();
            for (int i = 0; i < count; i++)
            {
                long id = r.ReadInt64(); if (!read.Add(id) || !localGuardOrders.TryGetValue(id, out var l)) throw new ArgumentException("Unknown local guard search owner.");
                l.JoinTick = r.ReadInt64(); l.JoinActionId = r.ReadInt64(); l.FormedTick = r.ReadInt64(); l.CandidateIndex = r.ReadInt32();
                var observations = new PatrolObservation[Count(r)];
                for (int j = 0; j < observations.Length; j++) observations[j] = new PatrolObservation(r.ReadInt64(), r.ReadInt64(), ReadCoord(r), r.ReadBoolean());
                l.SearchObservations = observations;
                if (l.JoinTick < -1 || l.FormedTick < -1 || l.JoinTick == -1 && l.JoinActionId != 0 || l.JoinTick >= 0 &&
                    (l.JoinTick < l.ReadTick || l.JoinTick >= l.Payload.Expires || l.JoinTick > LastProcessedTick || l.JoinActionId < 1 || l.JoinActionId >= nextAction) ||
                    l.FormedTick >= 0 && (l.JoinTick < 0 || l.FormedTick < l.JoinTick || l.FormedTick >= l.Payload.Expires || l.FormedTick > LastProcessedTick)) throw new ArgumentException("Invalid local team join proof.");
                if (l.CandidateIndex != observations.Length || observations.Length > l.Payload.Candidates.Count || observations.Length > 0 && l.FormedTick < 0 ||
                    observations.Where((o, j) => o.ActionId <= l.JoinActionId || o.ActionId >= nextAction || o.Tick < l.FormedTick || o.Tick >= l.Payload.Expires || o.Tick > LastProcessedTick || o.Tile != l.Payload.Candidates[j] ||
                        o.Found && j != observations.Length - 1 || j > 0 && (o.Tick <= observations[j - 1].Tick || o.ActionId <= observations[j - 1].ActionId)).Any()) throw new ArgumentException("Invalid retained guard search observations.");
                bool found = observations.LastOrDefault()?.Found == true;
                if (l.Status == GuardOrderStatus.WaitingTeam && (l.JoinTick < 0 || l.FormedTick >= 0 || l.Reason != "AwaitingLocalTeamMembers") ||
                    l.Status == GuardOrderStatus.AtRendezvous && (l.JoinTick >= 0 || l.FormedTick >= 0) ||
                    l.Status == GuardOrderStatus.Searching && (l.FormedTick < 0 || found || l.CandidateIndex >= l.Payload.Candidates.Count) ||
                    l.Status == GuardOrderStatus.TargetLocated && !found ||
                    l.Status == GuardOrderStatus.SearchAbandoned && (l.FormedTick < 0 || l.Reason != "TeamNotReady" || LastProcessedTick < GuardTargetWaitDeadline(l)) ||
                    l.Status == GuardOrderStatus.SearchExhausted && (found || l.CandidateIndex != l.Payload.Candidates.Count || l.Reason != "TargetNotLocated")) throw new ArgumentException("Invalid local search progress.");
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var t = new LocalGuardTeamSnapshot { AuthorizationId = r.ReadInt64(), DispatchId = r.ReadInt64(), FormedTick = r.ReadInt64() };
                var members = new GuardJoinSnapshot[Count(r)];
                for (int j = 0; j < members.Length; j++) members[j] = new GuardJoinSnapshot { ActorId = r.ReadInt64(), OrderMessageId = r.ReadInt64(), Tick = r.ReadInt64(), ActionId = r.ReadInt64() }; t.Members = members;
                var payload = messages.TryGetValue(t.DispatchId, out var source) ? source.Draft.EscortOrder : null;
                if (payload == null || payload.AuthorizationId != t.AuthorizationId || payload.DispatchId != t.DispatchId || t.FormedTick < -1 || t.FormedTick > LastProcessedTick ||
                    t.FormedTick >= payload.Expires || members.Length > payload.Guards.Count || !members.Select(m => m.ActorId).SequenceEqual(members.Select(m => m.ActorId).Distinct().OrderBy(id => id))) throw new ArgumentException("Invalid local guard team.");
                foreach (var member in members)
                {
                    if (!localGuardOrders.TryGetValue(member.OrderMessageId, out var l) || l.ActorId != member.ActorId || !l.Payload.Same(payload) || l.JoinTick != member.Tick || l.JoinActionId != member.ActionId ||
                        l.FormedTick != t.FormedTick || t.FormedTick < 0 && l.Status != GuardOrderStatus.WaitingTeam) throw new ArgumentException("Local guard membership lacks its participant's completed join.");
                }
                if (t.FormedTick >= 0 && (members.Length != payload.Guards.Count || !members.Select(m => m.ActorId).OrderBy(id => id).SequenceEqual(payload.Guards.OrderBy(id => id)) || members.Max(m => m.Tick) != t.FormedTick)) throw new ArgumentException("Incomplete local team formation.");
                localGuardTeams.Add(t.DispatchId, t);
            }
            foreach (var l in localGuardOrders.Values)
                if (l.JoinTick >= 0 && (!localGuardTeams.TryGetValue(l.Payload.DispatchId, out var t) || !t.Members.Any(m => m.OrderMessageId == l.OrderMessageId)) ||
                    l.JoinTick < 0 && l.Status == GuardOrderStatus.WaitingTeam) throw new ArgumentException("Unregistered local guard join.");
            foreach (var actor in actors.Values.Where(a => a.Action?.GuardOrderId > 0))
            {
                var action = actor.Action!; var l = localGuardOrders[action.GuardOrderId];
                if (action.Kind == ActionKind.JoinGuardTeam && (action.Target != l.Payload.Dispatch.Rendezvous || actor.Position != action.Target || action.Completes != checked(action.Started + Information!.InspectTicks) ||
                    l.JoinTick >= 0 || l.FormedTick >= 0 || !PhysicallyEquipped(actor, l.Payload.Dispatch.RequiredEquipment))) throw new ArgumentException("Invalid active local guard join.");
                if (action.Kind == ActionKind.InspectActor && (l.FormedTick < 0 || l.CandidateIndex >= l.Payload.Candidates.Count || action.TargetActor.Value != l.Payload.SubjectActorId ||
                    action.Target != l.Payload.Candidates[l.CandidateIndex] || action.Completes != checked(action.Started + Information!.InspectTicks))) throw new ArgumentException("Invalid active guard search inspection.");
            }
        }
    }
}
