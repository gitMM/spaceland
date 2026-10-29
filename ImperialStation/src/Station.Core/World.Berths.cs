using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class BerthAllocationSnapshot
    {
        public long BerthId { get; internal set; }
        public long ActorId { get; internal set; }
        public long AccountId { get; internal set; }
        public long GrantedTick { get; internal set; }
        public long ActionId { get; internal set; }
        public long CredentialId { get; internal set; }
        public long RecordVersion { get; internal set; }
        internal BerthAllocationSnapshot Copy() => (BerthAllocationSnapshot)MemberwiseClone();
    }
    public sealed class BerthSnapshot
    {
        public long Id { get; internal set; }
        public Coord Position { get; internal set; }
        public bool Usable { get; internal set; }
        public long AssignedActorId { get; internal set; }
        public long ReservedActionId { get; internal set; }
        public int Occupants { get; internal set; }
        public long DetentionFacilityId { get; internal set; }
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, BerthAllocationSnapshot> berthAllocations = new SortedDictionary<long, BerthAllocationSnapshot>();
        private readonly SortedDictionary<long, long> ownBerths = new SortedDictionary<long, long>();
        private readonly SortedDictionary<long, long> berthReservations = new SortedDictionary<long, long>();
        public IReadOnlyList<BerthAllocationSnapshot> InspectBerthAllocations() => berthAllocations.Values.Select(a => a.Copy()).ToArray();
        public IReadOnlyList<BerthSnapshot> InspectBerths() => needServices.Values.Where(s => s.Definition.Kind == NeedServiceKind.Bed).Select(s => new BerthSnapshot {
            Id = s.Definition.Id, Position = s.Definition.Position, Usable = s.Enabled, AssignedActorId = s.AssignedActorId,
            ReservedActionId = berthReservations.TryGetValue(s.Definition.Id, out long action) ? action : 0, Occupants = s.Occupants.Count, DetentionFacilityId = s.Definition.DetentionFacilityId }).ToArray();
        public long UsableHousingBerths => needServices.Values.Count(s => s.Definition.Kind == NeedServiceKind.Bed && s.Definition.DetentionFacilityId == 0 && s.Enabled);
        public long VacantHousingBerths => needServices.Values.Count(s => s.Definition.Kind == NeedServiceKind.Bed && s.Definition.DetentionFacilityId == 0 && s.Enabled && s.AssignedActorId == 0);
        private long KnownOwnBerth(Id<ActorTag> actor) => ownBerths.TryGetValue(actor.Value, out long berth) ? berth : 0;
        private void InitializeBerths()
        {
            foreach (var service in needServices.Values.Where(s => s.Definition.Kind == NeedServiceKind.Bed && s.AssignedActorId > 0))
            {
                var actor = actors[new Id<ActorTag>(service.AssignedActorId)];
                if (!LearnOwnBerth(actor, service.Definition.Id, 0, -1)) throw new ArgumentException("No knowledge capacity for own pinned berth.");
                GrantBerth(actor, service, 0, 0, -1);
            }
        }
        private bool LearnOwnBerth(Actor actor, long berth, long action, long tick) => Learn(actor, new FactKey(SubjectKind.Actor, actor.Id.Value, AttributeKind.AssignedBerth),
            FactValue.Integer(berth), action == 0 ? EvidenceKind.Seeded : EvidenceKind.Direct, action, tick, actor.Action?.CauseId ?? 0, pin: true);
        private void GrantBerth(Actor actor, NeedService service, long action, long credential, long tick)
        {
            var allocation = new BerthAllocationSnapshot { BerthId = service.Definition.Id, ActorId = actor.Id.Value, AccountId = actor.Account.Value, GrantedTick = tick, ActionId = action, CredentialId = credential };
            service.AssignedActorId = actor.Id.Value; ownBerths.Add(actor.Id.Value, service.Definition.Id);
            records.Append(RecordKind.BerthAllocation, service.Definition.Id, actor.Account.Value, tick, (action == 0 ? "initial/berth/" : "berth-allocation/") + service.Definition.Id,
                new[] { Number(AttributeKind.ActorId, actor.Id.Value), Number(AttributeKind.AssignedBerth, service.Definition.Id), Number(AttributeKind.AllocationActionId, action), Number(AttributeKind.ObservedTick, tick), new RecordField(AttributeKind.Location, FactValue.At(service.Definition.Position)) });
            allocation.RecordVersion = records.Version; berthAllocations.Add(service.Definition.Id, allocation);
            if (action > 0) Emit("BerthAllocated", actor, new Id<ActionTag>(action), target: service.Definition.Position, detail: "berth=" + service.Definition.Id + "; record=" + allocation.RecordVersion);
        }
        private FailureReason BerthAccess(Actor actor, ActiveAction action, NeedService service, bool completing)
        {
            if (Needs == null || service.Definition.Kind != NeedServiceKind.Bed || service.Definition.DetentionFacilityId > 0) return FailureReason.RequiredServiceNotPresent;
            if (ActiveEscort(actor.Id) != null) return FailureReason.CustodyRestricted;
            if (actor.Position != service.Definition.Position || action.Target != service.Definition.Position) return FailureReason.NotAtRequiredTile;
            if (action.TargetActor.Value > 0 && action.TargetActor != actor.Id) return FailureReason.NotQualified;
            if (berthAllocations.Values.Any(a => a.ActorId == actor.Id.Value)) return FailureReason.AlreadyAssigned;
            if (service.AssignedActorId != 0) return FailureReason.BerthUnavailable;
            if (!service.Enabled || service.Occupants.Count > 0 || service.Queue.Count > 0) return FailureReason.ServiceBusy;
            if (berthReservations.TryGetValue(service.Definition.Id, out long claim) && claim != action.Id.Value) return FailureReason.ResourceClaimed;
            if (completing && (!berthReservations.TryGetValue(service.Definition.Id, out claim) || claim != action.Id.Value)) return FailureReason.ResourceClaimed;
            if (service.Definition.AllocationRequiresCredential)
            {
                var failure = CredentialFor(actor, out var credential); if (failure != FailureReason.None) return failure;
                if (completing && action.CredentialId != credential!.Definition.Id) return FailureReason.UnknownRequiredFact;
                if (!completing) action.CredentialId = credential!.Definition.Id;
            }
            return FailureReason.None;
        }
        private void StartBerthAllocation(Actor actor, ActiveAction action)
        {
            if (!needServices.TryGetValue(action.ServiceId, out var service)) { Fail(actor, action.Id, FailureReason.RequiredServiceNotPresent); return; }
            var failure = BerthAccess(actor, action, service, false); if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            EndSession(actor.Id, "BerthAllocation"); berthReservations.Add(service.Definition.Id, action.Id.Value);
            actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None; action.Completes = checked(processingTick + service.Definition.AllocationTicks);
            Emit("BerthAllocationStarted", actor, action.Id, arrival: action.Completes, detail: "berth=" + service.Definition.Id); ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteBerthAllocation(Actor actor, ActiveAction action)
        {
            var service = needServices[action.ServiceId]; var failure = BerthAccess(actor, action, service, true);
            if (failure == FailureReason.None && !LearnOwnBerth(actor, service.Definition.Id, action.Id.Value, processingTick)) failure = FailureReason.KnowledgeCapacity;
            if (failure != FailureReason.None)
            { ReleaseBerthReservation(actor, action); actor.Action = null; Fail(actor, action.Id, failure); return; }
            GrantBerth(actor, service, action.Id.Value, action.CredentialId, processingTick); Finish(actor);
        }
        private void ReleaseBerthReservation(Actor actor, ActiveAction action)
        {
            if (action.Kind != ActionKind.AllocateBerth || !berthReservations.TryGetValue(action.ServiceId, out long held) || held != action.Id.Value) return;
            berthReservations.Remove(action.ServiceId); Emit("BerthReservationReleased", actor, action.Id, detail: "berth=" + action.ServiceId);
        }
    }
}
