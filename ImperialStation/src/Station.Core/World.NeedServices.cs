using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public enum NeedActivityStatus { Queued, Active, Completed, Cancelled, Rejected }
    public sealed class NeedActivitySnapshot
    {
        public long ActionId { get; internal set; }
        public long ActorId { get; internal set; }
        public long ServiceId { get; internal set; }
        public ActionKind Kind { get; internal set; }
        public long RequestedTick { get; internal set; }
        public long StartedTick { get; internal set; } = -1;
        public long EndedTick { get; internal set; } = -1;
        public long MealItemId { get; internal set; }
        public NeedActivityStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "";
        internal NeedActivitySnapshot Copy() => (NeedActivitySnapshot)MemberwiseClone();
    }
    internal sealed class NeedService
    {
        internal NeedServiceDefinition Definition = null!;
        internal bool Enabled;
        internal long AssignedActorId;
        internal long NextGrant = -1;
        internal List<long> Queue = new List<long>();
        internal SortedSet<long> Occupants = new SortedSet<long>();
    }
    public sealed class NeedServiceSnapshot
    {
        public NeedServiceDefinition Definition { get; internal set; } = null!;
        public bool Enabled { get; internal set; }
        public long AssignedActorId { get; internal set; }
        public long NextGrant { get; internal set; }
        public IReadOnlyList<long> QueuedActionIds { get; internal set; } = Array.Empty<long>();
        public IReadOnlyList<long> OccupantActionIds { get; internal set; } = Array.Empty<long>();
    }
    public sealed partial class World
    {
        private readonly SortedDictionary<long, NeedService> needServices = new SortedDictionary<long, NeedService>();
        private readonly SortedDictionary<long, NeedActivitySnapshot> needActivities = new SortedDictionary<long, NeedActivitySnapshot>();
        // Derived per-actor index of own sleep activities in action order; rebuilt on restore.
        private readonly SortedDictionary<long, List<NeedActivitySnapshot>> sleepsByActor = new SortedDictionary<long, List<NeedActivitySnapshot>>();
        private void IndexSleep(NeedActivitySnapshot activity)
        {
            if (activity.Kind != ActionKind.Sleep) return;
            if (!sleepsByActor.TryGetValue(activity.ActorId, out var list)) sleepsByActor.Add(activity.ActorId, list = new List<NeedActivitySnapshot>());
            list.Add(activity);
        }
        // A sleep runs its planned finite length (never longer than the bed's duration).
        private static long NeedDuration(ActiveAction action, NeedService service) =>
            action.Kind == ActionKind.Sleep && action.Quantity > 0 ? Math.Min(action.Quantity, service.Definition.DurationTicks) : service.Definition.DurationTicks;
        // Own sleep already taken (ended activities) since the current known sleep window
        // opened: the result of the citizen's own actions, used to sleep only what remains.
        private long SleptThisWindow(Id<ActorTag> actor)
        {
            long? opened = DailyPolicy.SleepWindowOpened(routines.TryGetValue(actor, out var routine) ? routine.KnownRoster : null, NeedNow);
            if (!opened.HasValue || !sleepsByActor.TryGetValue(actor.Value, out var list)) return 0;
            long slept = 0;
            for (int i = list.Count - 1; i >= 0 && list[i].RequestedTick >= opened.Value; i--)
                if (list[i].StartedTick >= 0 && list[i].EndedTick >= list[i].StartedTick) slept = checked(slept + list[i].EndedTick - list[i].StartedTick);
            return slept;
        }
        // Derived live reservations; historical activities remain the serialized authority.
        private readonly SortedDictionary<long, SortedSet<long>> mealReservations = new SortedDictionary<long, SortedSet<long>>();
        private readonly SortedDictionary<string, long> mealSinks = new SortedDictionary<string, long>(StringComparer.Ordinal);
        public IReadOnlyList<NeedServiceSnapshot> InspectNeedServices() => needServices.Values.Select(s => new NeedServiceSnapshot { Definition = s.Definition, Enabled = s.Enabled, AssignedActorId = s.AssignedActorId, NextGrant = s.NextGrant, QueuedActionIds = s.Queue.ToArray(), OccupantActionIds = s.Occupants.ToArray() }).ToArray();
        public IReadOnlyList<NeedActivitySnapshot> InspectNeedActivities() => needActivities.Values.Select(a => a.Copy()).ToArray();
        public long MealsConsumed => mealSinks.Values.Sum();
        private static bool IsNeedAction(ActionKind kind) => kind is ActionKind.Eat or ActionKind.UseToilet or ActionKind.Rest or ActionKind.Sleep;
        private static bool RelievesNeed(ActionKind kind, NeedKind need) => NeedPriority.Relieves(kind, need);
        private void InitializeNeedServices(IEnumerable<NeedServiceDefinition> definitions)
        {
            foreach (var d in definitions)
            {
                if (!Topology.Contains(d.Position) || d.AssignedActorId > 0 && !actors.ContainsKey(new Id<ActorTag>(d.AssignedActorId)) ||
                    d.DetentionFacilityId > 0 && (!detentionFacilities.TryGetValue(d.DetentionFacilityId, out var detention) || detention.Definition.Position != d.Position) ||
                    d.AssignedActorId > 0 && needServices.Values.Any(s => s.Definition.AssignedActorId == d.AssignedActorId)) throw new ArgumentException("Need service requires a completed local site, valid unique berth assignment and declared custody facility.");
                needServices.Add(d.Id, new NeedService { Definition = d, Enabled = d.Enabled, AssignedActorId = d.AssignedActorId }); IndexNeedService(needServices[d.Id]);
            }
        }
        private bool CustodyNeedAllowed(Actor actor, ActionKind kind, long service)
        {
            var escort = ActiveEscort(actor.Id);
            if (escort == null) return true;
            if (escort.Status != EscortStatus.Detained || EscortPayload(escort).SubjectActorId != actor.Id.Value) return false;
            if (kind == ActionKind.Cancel) return actor.Action != null && IsNeedAction(actor.Action.Kind);
            return IsNeedAction(kind) && needServices.TryGetValue(service, out var s) && s.Definition.DetentionFacilityId == detentionStays[escort.AuthorizationId].FacilityId;
        }
        private FailureReason NeedServiceAccess(Actor actor, ActiveAction action, NeedService service)
        {
            var d = service.Definition;
            if (Needs == null || !actorNeeds.ContainsKey(actor.Id.Value) || d.Action != action.Kind) return FailureReason.RequiredServiceNotPresent;
            if (actor.Position != d.Position || action.Target != d.Position) return FailureReason.NotAtRequiredTile;
            if (!CustodyNeedAllowed(actor, action.Kind, d.Id)) return FailureReason.CustodyRestricted;
            if (!service.Enabled) return FailureReason.ServiceBusy;
            if (d.DetentionFacilityId > 0 && !detentionStays.Values.Any(s => s.ActorId == actor.Id.Value && s.FacilityId == d.DetentionFacilityId && s.Status == DetentionStayStatus.Detained)) return FailureReason.CustodyRestricted;
            if (d.Kind == NeedServiceKind.Bed && service.AssignedActorId != actor.Id.Value && !(service.AssignedActorId == 0 && d.DetentionFacilityId > 0)) return FailureReason.NotQualified;
            return FailureReason.None;
        }
        private int OtherMealReservations(long item, long action) => mealReservations.TryGetValue(item, out var claims) ? claims.Count - (claims.Contains(action) ? 1 : 0) : 0;
        private bool MealReservedByOther(long item, long action) => OtherMealReservations(item, action) > 0;
        private void ReserveMeal(NeedActivitySnapshot activity)
        {
            if (activity.Kind != ActionKind.Eat) return;
            if (!mealReservations.TryGetValue(activity.MealItemId, out var claims)) mealReservations.Add(activity.MealItemId, claims = new SortedSet<long>());
            claims.Add(activity.ActionId);
        }
        private void ReleaseMeal(NeedActivitySnapshot activity)
        {
            if (activity.Kind != ActionKind.Eat || !mealReservations.TryGetValue(activity.MealItemId, out var claims)) return;
            claims.Remove(activity.ActionId); if (claims.Count == 0) mealReservations.Remove(activity.MealItemId);
        }
        private Item? AvailableMeal(Actor actor, ActiveAction action, NeedService service)
        {
            return AvailableItemsAt(actor.Position).Concat(ItemsAt(LocationKind.ActorInventory, actor.Id.Value)).OrderBy(i => i.Id).FirstOrDefault(i => (action.Item.Value == 0 || action.Item == i.Id) && i.Resource == service.Definition.MealResource &&
                (HeldBy(i, actor) || ItemAvailableAt(i, actor.Position)) && !resourceClaims.Any(c => c.Kind == ClaimKind.Item && c.ResourceId == i.Id.Value && c.ActionId != action.Id.Value) &&
                !credentials.Values.Any(c => c.Definition.Card == i.Id) && i.Quantity > OtherMealReservations(i.Id.Value, action.Id.Value));
        }
        private void StartNeedActivity(Actor actor, ActiveAction action)
        {
            if (!needServices.TryGetValue(action.ServiceId, out var service)) { Fail(actor, action.Id, FailureReason.RequiredServiceNotPresent); return; }
            // Only sleep may be shorter than the service's declared duration (a finite length).
            if (action.Quantity < 0 || action.Quantity > 0 && (action.Kind != ActionKind.Sleep || action.Quantity > service.Definition.DurationTicks)) { Fail(actor, action.Id, FailureReason.InvalidQuantity); return; }
            var failure = NeedServiceAccess(actor, action, service); if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            service = TakeVisiblyFreeIdentical(actor, action, service);
            EndSession(actor.Id, "NeedActivity");
            var activity = new NeedActivitySnapshot { ActionId = action.Id.Value, ActorId = actor.Id.Value, ServiceId = service.Definition.Id, Kind = action.Kind, RequestedTick = processingTick, Status = NeedActivityStatus.Queued };
            needActivities.Add(activity.ActionId, activity); IndexSleep(activity); action.Waiting = true; actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            ObserveServiceQueue(actor, service); service.Queue.Add(action.Id.Value); ServiceVisiblyChanged(service); Emit("NeedServiceQueued", actor, action.Id, detail: service.Definition.Kind.ToString()); RequestNeedGrant(service);
        }
        // Arriving at a service that visibly has someone ahead, a citizen uses an identical
        // service on the same tile that it may use and that stands free (for example
        // another facility bed). Plans made in the same tick cannot see each other's
        // choice; this decision uses only what is visible here and now.
        private NeedService TakeVisiblyFreeIdentical(Actor actor, ActiveAction action, NeedService chosen)
        {
            if (!ServicePreference.SeeksIdentical(SeenWait(actor, chosen)) || !seenServiceWaits.TryGetValue(actor.Id.Value, out var seen)) return chosen;
            var d = chosen.Definition;
            var free = seen.Where(p => p.Key != d.Id && ServicePreference.Identical(needServices[p.Key].Definition, d) && ServicePreference.VisiblyFree(p.Value))
                .Select(p => needServices[p.Key]).FirstOrDefault(s => NeedServiceAccess(actor, action, s) == FailureReason.None);
            if (free == null) return chosen;
            action.ServiceId = free.Definition.Id;
            foreach (var goal in DailyFor(actor.Id.Value).Where(g => g.ActiveActionId == action.Id.Value)) goal.ServiceId = free.Definition.Id;
            if (needAgents.TryGetValue(actor.Id.Value, out var agent) && agent.ActiveActionId == action.Id.Value) agent.ServiceId = free.Definition.Id;
            Emit("NeedServiceSwitched", actor, action.Id, detail: "from=" + d.Id + "; to=" + free.Definition.Id + "; " + d.Kind);
            return free;
        }
        private void RequestNeedGrant(NeedService service)
        {
            if (!service.Enabled || service.Queue.Count == 0 || service.Occupants.Count >= service.Definition.Capacity)
            { if (service.NextGrant >= 0) foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.GrantNeedService && e.Value == service.Definition.Id).ToArray()) scheduled.Remove(e); service.NextGrant = -1; return; }
            if (service.NextGrant >= 0) return;
            long tick = processingTick >= 0 ? processingTick : 0; if (processingPhase >= EventPhase.Decision) tick = checked(tick + 1);
            service.NextGrant = tick; Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Decision, Kind = ScheduledKind.GrantNeedService, Value = service.Definition.Id });
        }
        private void GrantNeedService(long id)
        {
            var service = needServices[id]; service.NextGrant = -1;
            while (service.Enabled && service.Queue.Count > 0 && service.Occupants.Count < service.Definition.Capacity)
            {
                long actionId = service.Queue[0]; service.Queue.RemoveAt(0); var activity = needActivities[actionId]; var actor = actors[new Id<ActorTag>(activity.ActorId)]; var action = actor.Action!;
                var failure = NeedServiceAccess(actor, action, service); Item? meal = null;
                if (failure == FailureReason.None && action.Kind == ActionKind.Eat) { meal = AvailableMeal(actor, action, service); if (meal == null) failure = FailureReason.InsufficientMaterials; }
                if (failure != FailureReason.None)
                {
                    if (failure == FailureReason.InsufficientMaterials) LearnServiceEstimate(actor, service, ServiceEstimateSource.ServiceFailure, meals: 0);
                    activity.Status = NeedActivityStatus.Rejected; activity.EndedTick = processingTick; activity.Reason = failure.ToString(); actor.Action = null; Fail(actor, action.Id, failure); continue;
                }
                LearnServiceEstimate(actor, service, ServiceEstimateSource.ExperiencedWait, wait: checked(processingTick - activity.RequestedTick));
                if (meal != null) { action.Item = meal.Id; activity.MealItemId = meal.Id.Value; }
                activity.Status = NeedActivityStatus.Active; activity.StartedTick = processingTick; service.Occupants.Add(action.Id.Value);
                ReserveMeal(activity);
                action.Waiting = false; action.Started = processingTick; action.Completes = checked(processingTick + NeedDuration(action, service));
                Emit("NeedServiceClaimed", actor, action.Id, action.Item, arrival: action.Completes, detail: service.Definition.Kind.ToString()); ScheduleCompletion(actor, action.Completes);
            }
            ServiceVisiblyChanged(service);
        }
        private void CompleteNeedActivity(Actor actor, ActiveAction action)
        {
            var service = needServices[action.ServiceId]; var activity = needActivities[action.Id.Value]; var failure = NeedServiceAccess(actor, action, service);
            if (failure == FailureReason.None && action.Kind == ActionKind.Eat && AvailableMeal(actor, action, service) == null) failure = FailureReason.InsufficientMaterials;
            if (failure != FailureReason.None) { CancelRunning(actor); Fail(actor, action.Id, failure); return; }
            if (action.Kind == ActionKind.Eat)
            {
                var meal = items[action.Item]; meal.Quantity--; if (meal.Quantity == 0) RemoveItem(meal.Id);
                mealSinks.TryGetValue(meal.Resource, out long consumed); mealSinks[meal.Resource] = checked(consumed + 1);
                ObserveItemDescription(actor, meal, action.CauseId); RelieveNeed(actor, NeedKind.Hunger, 500, false);
                Emit("MealConsumed", actor, action.Id, meal.Id, detail: meal.Resource, quantity: 1);
                LearnServiceEstimate(actor, service, ServiceEstimateSource.ObservedStock, meals: MealsVisible(service));
            }
            else if (action.Kind == ActionKind.UseToilet) RelieveNeed(actor, NeedKind.Bladder, 0, true);
            ReleaseMeal(activity); activity.Status = NeedActivityStatus.Completed; activity.EndedTick = processingTick;
            Emit("NeedActivityCompleted", actor, action.Id, detail: action.Kind.ToString()); Finish(actor);
        }
        private void RelieveNeed(Actor actor, NeedKind kind, int points, bool reset)
        {
            var n = actorNeeds[actor.Id.Value]; int index = (int)kind; var v = n.Values[index];
            v.Segment = reset ? v.Segment.Reset(processingTick, Config.TickDurationMs) : v.Segment.Relieve(processingTick, Config.TickDurationMs, points);
            v.Announced = Needs!.Rules[index].Band(v.Segment.AnchorQ); n.UnmetReason = ""; ScheduleNeedThreshold(n);
        }
        private void ReleaseNeedActivity(Actor actor, ActiveAction action)
        {
            if (!IsNeedAction(action.Kind) || !needActivities.TryGetValue(action.Id.Value, out var activity)) return;
            var service = needServices[action.ServiceId]; service.Queue.Remove(action.Id.Value); service.Occupants.Remove(action.Id.Value); ServiceVisiblyChanged(service);
            ReleaseMeal(activity);
            if (activity.Status is NeedActivityStatus.Active or NeedActivityStatus.Queued) { activity.Status = NeedActivityStatus.Cancelled; activity.EndedTick = processingTick; activity.Reason = "Cancelled"; }
            Emit("NeedServiceReleased", actor, action.Id, detail: service.Definition.Kind.ToString()); RequestNeedGrant(service);
        }
        public void ScheduleNeedServiceChange(long tick, long service, bool enabled)
        {
            ValidateFuture(tick); if (!needServices.ContainsKey(service)) throw new ArgumentException("Unknown need service.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.NeedServiceChange, ActionId = service, Value = enabled ? 1 : 0 });
        }
        private void ChangeNeedService(long id, bool enabled)
        {
            var service = needServices[id]; service.Enabled = enabled; ServiceVisiblyChanged(service);
            if (!enabled)
            {
                foreach (var reservation in berthReservations.Where(p => p.Key == id).ToArray()) CancelRunning(actors.Values.Single(a => a.Action?.Id.Value == reservation.Value));
                foreach (long action in service.Occupants.ToArray()) CancelRunning(actors[new Id<ActorTag>(needActivities[action].ActorId)]);
                foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.GrantNeedService && e.Value == id).ToArray()) scheduled.Remove(e); service.NextGrant = -1;
            }
            else RequestNeedGrant(service);
            Emit("NeedServiceAvailabilityChanged", detail: "service=" + id + "; enabled=" + enabled);
        }
    }
}
