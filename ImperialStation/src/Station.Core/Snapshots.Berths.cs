using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteBerths(BinaryWriter w)
        {
            w.Write(berthAllocations.Count);
            foreach (var a in berthAllocations.Values)
            { w.Write(a.BerthId); w.Write(a.ActorId); w.Write(a.AccountId); w.Write(a.GrantedTick); w.Write(a.ActionId); w.Write(a.CredentialId); w.Write(a.RecordVersion); }
            w.Write(ownBerths.Count); foreach (var p in ownBerths) { w.Write(p.Key); w.Write(p.Value); }
            w.Write(berthReservations.Count); foreach (var p in berthReservations) { w.Write(p.Key); w.Write(p.Value); }
        }
        private void ReadBerths(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new BerthAllocationSnapshot { BerthId = r.ReadInt64(), ActorId = r.ReadInt64(), AccountId = r.ReadInt64(), GrantedTick = r.ReadInt64(), ActionId = r.ReadInt64(), CredentialId = r.ReadInt64(), RecordVersion = r.ReadInt64() };
                if (!needServices.TryGetValue(a.BerthId, out var service) || service.Definition.Kind != NeedServiceKind.Bed || service.AssignedActorId != a.ActorId ||
                    !actors.TryGetValue(new Id<ActorTag>(a.ActorId), out var actor) || actor.Account.Value != a.AccountId || a.GrantedTick < -1 || a.GrantedTick > LastProcessedTick ||
                    a.ActionId < 0 || a.ActionId >= nextAction || a.CredentialId < 0 || berthAllocations.Values.Any(b => b.ActorId == a.ActorId)) throw new ArgumentException("Invalid berth allocation identity.");
                if (a.ActionId == 0 ? a.GrantedTick != -1 || service.Definition.AssignedActorId != a.ActorId || a.CredentialId != 0 :
                    a.GrantedTick < service.Definition.AllocationTicks || service.Definition.AssignedActorId != 0 || service.Definition.DetentionFacilityId != 0 ||
                    service.Definition.AllocationRequiresCredential != (a.CredentialId > 0)) throw new ArgumentException("Invalid berth allocation authorization.");
                if (a.CredentialId > 0 && (!credentials.TryGetValue(a.CredentialId, out var credential) || credential.Definition.Holder != actor.Id || credential.Definition.Account != actor.Account)) throw new ArgumentException("Invalid berth credential binding.");
                if (!records.History.TryGetValue(a.RecordVersion, out var row) || row.Kind != RecordKind.BerthAllocation || row.EntityId != a.BerthId || row.AccountId != a.AccountId || row.Tick != a.GrantedTick ||
                    row.OperationKey != (a.ActionId == 0 ? "initial/berth/" : "berth-allocation/") + a.BerthId || row.Fields.Count != 5 ||
                    !row.Fields.Any(f => f.Attribute == AttributeKind.ActorId && f.Value.Equals(FactValue.Integer(a.ActorId))) ||
                    !row.Fields.Any(f => f.Attribute == AttributeKind.AssignedBerth && f.Value.Equals(FactValue.Integer(a.BerthId))) ||
                    !row.Fields.Any(f => f.Attribute == AttributeKind.AllocationActionId && f.Value.Equals(FactValue.Integer(a.ActionId))) ||
                    !row.Fields.Any(f => f.Attribute == AttributeKind.ObservedTick && f.Value.Equals(FactValue.Integer(a.GrantedTick))) ||
                    !row.Fields.Any(f => f.Attribute == AttributeKind.Location && f.Value.Equals(FactValue.At(service.Definition.Position)))) throw new ArgumentException("Berth allocation lacks its formal record.");
                berthAllocations.Add(a.BerthId, a);
            }
            if (records.History.Values.Count(e => e.Kind == RecordKind.BerthAllocation) != berthAllocations.Count || needServices.Values.Any(s => s.AssignedActorId < 0 ||
                s.AssignedActorId > 0 && (s.Definition.Kind != NeedServiceKind.Bed || !berthAllocations.ContainsKey(s.Definition.Id)) ||
                s.Definition.AssignedActorId > 0 && s.AssignedActorId != s.Definition.AssignedActorId)) throw new ArgumentException("Unregistered berth assignment.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long actorId = r.ReadInt64(), berth = r.ReadInt64();
                if (!berthAllocations.TryGetValue(berth, out var allocation) || allocation.ActorId != actorId) throw new ArgumentException("Invalid private berth receipt.");
                var actor = actors[new Id<ActorTag>(actorId)];
                if (Information != null && (!actor.Knowledge.Entries.TryGetValue(new FactKey(SubjectKind.Actor, actorId, AttributeKind.AssignedBerth), out var known) ||
                    !known.Fact.Pinned || !known.Fact.Value.Equals(FactValue.Integer(berth)) || known.Fact.SourceId != allocation.ActionId || known.Fact.ObservedTick != allocation.GrantedTick ||
                    known.Fact.SourceKind != (allocation.ActionId == 0 ? EvidenceKind.Seeded : EvidenceKind.Direct))) throw new ArgumentException("Missing pinned own berth evidence.");
                ownBerths.Add(actorId, berth);
            }
            if (ownBerths.Count != berthAllocations.Count) throw new ArgumentException("Missing private berth receipt.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                long berth = r.ReadInt64(), actionId = r.ReadInt64();
                var actor = actors.Values.SingleOrDefault(a => a.Action?.Id.Value == actionId); var action = actor?.Action;
                if (!needServices.TryGetValue(berth, out var service) || !service.Enabled || service.Definition.Kind != NeedServiceKind.Bed || service.Definition.DetentionFacilityId != 0 || service.AssignedActorId != 0 ||
                    action == null || action.Kind != ActionKind.AllocateBerth || action.ServiceId != berth || action.Waiting || action.Started < 0 || action.Started > LastProcessedTick ||
                    action.Completes != checked(action.Started + service.Definition.AllocationTicks) || action.Completes <= LastProcessedTick ||
                    actor!.Position != service.Definition.Position || action.Target != actor.Position || action.TargetActor.Value != 0 && action.TargetActor != actor.Id ||
                    ownBerths.ContainsKey(actor.Id.Value) || ActiveEscort(actor.Id) != null || service.Queue.Count > 0 || service.Occupants.Count > 0 ||
                    service.Definition.AllocationRequiresCredential != (action.CredentialId > 0)) throw new ArgumentException("Invalid pending berth reservation.");
                if (action.CredentialId > 0 && (!credentials.TryGetValue(action.CredentialId, out var credential) || credential.Definition.Holder != actor.Id || credential.Definition.Account != actor.Account)) throw new ArgumentException("Invalid pending allocation credential.");
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.ActionCompletion && e.ActionId == actionId).ToArray();
                if (timers.Length != 1 || timers[0].Tick != action.Completes || timers[0].Phase != EventPhase.PhysicalCompletion || timers[0].Intent.Actor != actor.Id) throw new ArgumentException("Invalid berth completion timer.");
                berthReservations.Add(berth, actionId);
            }
            if (actors.Values.Any(a => a.Action?.Kind == ActionKind.AllocateBerth && (!berthReservations.TryGetValue(a.Action.ServiceId, out long id) || id != a.Action.Id.Value))) throw new ArgumentException("Unregistered berth allocation action.");
        }
    }
}
