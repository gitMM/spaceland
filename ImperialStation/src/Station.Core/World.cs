using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    internal enum ScheduledKind { TraversalChange, ActionCompletion, Command, Sense, MessageDelivery, SessionExpiry, TerminalChange, CredentialRevocation, GrantTerminal, EvaluateReport, WorkServiceChange, EvaluateRepair, DispatchWork, EvaluateWorkOrder, CheckInDue, EvaluateCheckIn, ExpireWorkOrder, CancelAssignment, ReleaseIdleTerminal, SensorSample, SensorDelivery, SensorChange, PublishRoster, RoutineBoundary, ShiftCheckIn, EvaluateRoutine, ReportExpectationDue, AcknowledgmentDue, OpenAdmissionBucket, ReviewIncidents, EvaluatePatrol, ExpirePatrol, Soiling, Spill, ReviewFacilityTasks, MachineFault, MachineWear, QualityDue, ReviewQuality, EvaluateAudit, ExpireDetentionAuthorization, ReviewEscorts, ExpireGuardOrder, EvaluateGuard, DetentionServiceChange, DetentionReleaseDue, RevokeDetention, NeedThreshold, GrantNeedService, NeedServiceChange, EvaluateNeed, NeedCommand, DailyBoundary, EvaluateDaily, DailyCommand, CardLoss, EvaluateRecovery, RecoveryCommand, ExternalCommand, ReviewGrowth, EscortAcknowledgmentDeadline }
    internal sealed class ScheduledEvent : IComparable<ScheduledEvent>
    {
        internal long Tick;
        internal EventPhase Phase;
        internal long Sequence;
        internal ScheduledKind Kind;
        internal ActionIntent Intent;
        internal long ActionId;
        internal long Value;
        internal long CauseId;
        public int CompareTo(ScheduledEvent? b)
        {
            if (b == null) return 1;
            int c = Tick.CompareTo(b.Tick); if (c == 0) c = Phase.CompareTo(b.Phase);
            return c == 0 ? Sequence.CompareTo(b.Sequence) : c;
        }
    }

    public sealed partial class World
    {
        private readonly SortedDictionary<Id<ActorTag>, Actor> actors = new SortedDictionary<Id<ActorTag>, Actor>();
        private readonly SortedDictionary<Id<ItemTag>, Item> items = new SortedDictionary<Id<ItemTag>, Item>();
        private readonly SortedDictionary<string, long> initialBalances = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Pcg32> streams = new SortedDictionary<string, Pcg32>(StringComparer.Ordinal);
        private readonly ScheduledQueue scheduled = new ScheduledQueue();
        private readonly List<TraceEvent> trace = new List<TraceEvent>();
        private long nextSequence = 1;
        private long nextAction = 1;
        private long nextTrace = 1;
        private long processingTick = -1;
        private EventPhase processingPhase;
        public string Identity { get; }
        public ulong Seed { get; }
        public WorldConfig Config { get; }
        public Topology Topology { get; }
        public long LastProcessedTick { get; private set; } = -1;
        public bool TraceEnabled { get; set; }
        public long PathRequests { get; private set; }
        public long PathExpansions { get; private set; }
        public long ActionsCompleted { get; private set; }
        public long ActionsFailed { get; private set; }
        public string? RuntimeError { get; private set; }
        public IReadOnlyList<TraceEvent> Trace => trace.AsReadOnly();
        // Diagnostic retention only. Event IDs and all consequential counters survive.
        public void ClearRecordedTrace() => trace.Clear();

        public World(string identity, ulong seed, WorldConfig config, IEnumerable<TileDefinition> definitions,
            IEnumerable<Tile> tiles, IEnumerable<ActorSeed> population, IEnumerable<Item> initialItems, bool traceEnabled = false, InformationSeed? information = null, WorkSeed? work = null, JobSystemSeed? jobs = null, RoutineSeed? routines = null, AuthorityConfig? authority = null, CleanlinessSeed? cleanliness = null, FacilityConfig? facility = null, MachineWearConfig? machineWear = null, QualityAssuranceConfig? qualityAssurance = null, PersonnelConfig? personnel = null, DetentionPolicy? detention = null, NeedSeed? needs = null, ConstructionSeed? construction = null, GrowthSeed? growth = null)
        {
            Identity = identity; Seed = seed; Config = config; TraceEnabled = traceEnabled;
            Topology = new Topology(definitions, tiles);
            foreach (var a in population)
            {
                if (a.Id.Value == 0 || !Topology.Contains(a.Position)) throw new ArgumentException($"Invalid actor {a.Id} at {a.Position}.");
                actors.Add(a.Id, new Actor { Id = a.Id, Name = a.Name, Role = a.Role, Position = a.Position, Account = a.Account, LastStatus = ActionStatus.Completed });
                string key = "actor/" + a.Id.Value.ToString(CultureInfo.InvariantCulture); streams.Add(key, Pcg32.Create(seed, key));
            }
            Topology.ValidateConnected(actors.Count > 0);
            if (work != null) InitializeWork(work); ValidateConstructionDefinitions();
            foreach (var item in initialItems)
            {
                if (item.Id.Value == 0 || item.Quantity < 1 || string.IsNullOrWhiteSpace(item.Resource)) throw new ArgumentException("Invalid item ID, resource or quantity.");
                ValidateLocation(item.Location);
                AddItem(item.Copy());
                initialBalances.TryGetValue(item.Resource, out long balance); initialBalances[item.Resource] = checked(balance + item.Quantity);
            }
            nextItem = items.Count == 0 ? 1 : checked(items.Keys.Last().Value + 1);
            foreach (var actor in actors.Values) RegisterLocalActor(actor); ValidateWorkItems();
            InitializeMachineWear(machineWear);
            streams.Add("arbitration", Pcg32.Create(seed, "arbitration"));
            if (cleanliness != null) InitializeCleanliness(cleanliness);
            if (information != null) InitializeInformation(information);
            if (jobs != null) InitializeJobs(jobs);
            if (routines != null) InitializeRoutines(routines);
            if (authority != null) InitializeAuthority(authority);
            if (facility != null) InitializeFacility(facility);
            if (qualityAssurance != null) InitializeQualityAssurance(qualityAssurance);
            if (personnel != null) InitializePersonnel(personnel);
            if (detention != null) InitializeDetention(detention); if (needs != null) { InitializeNeeds(needs); InitializeConversations(needs.Conversations); InitializeBerths(); InitializeNeedAgents(needs); InitializeDailyGoals(); } InitializeConstruction(construction); InitializeGrowth(growth);
        }

        private void ValidateLocation(ItemLocation location)
        {
            bool valid = location.Kind switch
            {
                LocationKind.TileContainer => location.Owner > 0 && Topology.ById.TryGetValue(new Id<TileTag>(location.Owner), out var tile) && tile.Completed,
                LocationKind.ActorInventory => location.Owner > 0 && actors.ContainsKey(new Id<ActorTag>(location.Owner)),
                LocationKind.MachineSlot => machines.ContainsKey(location.Owner),
                LocationKind.ServiceOutput => workstations.ContainsKey(location.Owner),
                LocationKind.ProjectStash => stashes.ContainsKey(location.Owner),
                _ => false // Later owners must supply and validate real stash/slot/service entities.
            };
            if (!valid) throw new ArgumentException($"Invalid or unsupported physical location {location.Kind}:{location.Owner}.");
        }

        public void Submit(long tick, ActionIntent intent)
        {
            ValidateFuture(tick);
            if (!actors.ContainsKey(intent.Actor)) throw new ArgumentException($"Unknown actor {intent.Actor}.");
            if (!Enum.IsDefined(typeof(ActionKind), intent.Kind)) throw new ArgumentException("Unknown action kind.");
            if (intent.Query != null && intent.Kind != ActionKind.QueryRecords) throw new ArgumentException("Query payload requires QueryRecords.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Intent, Kind = ScheduledKind.ExternalCommand, Intent = intent });
        }
        public void ScheduleTraversalChange(long tick, Coord tile, long ticks)
        {
            ValidateFuture(tick); Topology.ValidateCost(ticks);
            if (!Topology.Contains(tile)) throw new ArgumentException($"Unknown completed tile {tile}.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.TraversalChange, Intent = new ActionIntent(default, ActionKind.Move, tile), Value = ticks });
        }
        private void ValidateFuture(long tick)
        { if (tick < 0 || tick <= LastProcessedTick || tick <= processingTick) throw new ArgumentException($"Command tick {tick} has already been processed or is negative."); }
        private void Schedule(ScheduledEvent e)
        {
            if (e.Tick < processingTick || (e.Tick == processingTick && e.Phase < processingPhase)) throw new InvalidOperationException("Cannot schedule into an earlier phase.");
            e.Sequence = nextSequence++; scheduled.Add(e);
        }

        public void AdvanceTo(long targetTick)
        {
            if (targetTick < 0) throw new ArgumentOutOfRangeException(nameof(targetTick));
            if (RuntimeError != null) throw new InvalidOperationException(RuntimeError);
            if (targetTick <= LastProcessedTick) return;
            long countedTick = -1; int count = 0;
            try
            {
                while (scheduled.Count > 0 && scheduled.Min!.Tick <= targetTick)
                {
                    var e = scheduled.Min!;
                    if (e.Tick != countedTick) { countedTick = e.Tick; count = 0; }
                    if (++count > Config.MaxEventsPerTick) throw new InvalidOperationException($"Event cap exceeded at tick {e.Tick}.");
                    scheduled.Remove(e); processingTick = e.Tick; processingPhase = e.Phase;
                    switch (e.Kind)
                    {
                        case ScheduledKind.CardLoss: LoseCard(e.Intent); break; case ScheduledKind.EvaluateRecovery: EvaluateRecovery(e.Intent.Actor); break; case ScheduledKind.RecoveryCommand: ProcessIntentGroup(e, ref count); break; case ScheduledKind.TraversalChange:
                            Topology.ChangeCost(e.Intent.Target, e.Value);
                            Emit("TraversalChanged", target: e.Intent.Target, detail: e.Value.ToString(CultureInfo.InvariantCulture)); break;
                        case ScheduledKind.ExternalCommand: case ScheduledKind.Command:
                            ProcessIntentGroup(e, ref count); break;
                        case ScheduledKind.ActionCompletion: Complete(e.Intent.Actor, e.ActionId); break;
                        case ScheduledKind.Sense: Sense(actors[e.Intent.Actor], e.CauseId); break;
                        case ScheduledKind.MessageDelivery: DeliverMessage(e.Value,e.CauseId); break;
                        case ScheduledKind.SensorSample: SampleSensor(e.Value); break;
                        case ScheduledKind.SensorDelivery: DeliverSensor(e.Value, e.CauseId); break;
                        case ScheduledKind.SensorChange: ChangeSensor(e.ActionId, e.Value != 0); break;
                        case ScheduledKind.PublishRoster: PublishRoster(e.Value); break;
                        case ScheduledKind.RoutineBoundary: RoutineBoundary(e.Intent.Actor); break;
                        case ScheduledKind.ShiftCheckIn: ShiftCheckIn(e.Intent.Actor); break;
                        case ScheduledKind.EvaluateRoutine: EvaluateRoutine(e.Intent.Actor); break;
                        case ScheduledKind.ReportExpectationDue: ReportExpectationDue(e.Value, e.Sequence); break;
                        case ScheduledKind.AcknowledgmentDue: AcknowledgmentDue(e.Value); break;
                        case ScheduledKind.OpenAdmissionBucket: OpenAdmissionBucket(); break;
                        case ScheduledKind.ReviewIncidents: ReviewIncidents(); break;
                        case ScheduledKind.QualityDue: QualityDue(e.Value); break;
                        case ScheduledKind.ReviewQuality: ReviewQuality(); break; case ScheduledKind.ReviewGrowth: ReviewGrowth(); break;
                        case ScheduledKind.ReviewEscorts: ReviewEscorts(); break;
                        case ScheduledKind.EscortAcknowledgmentDeadline: EscortAcknowledgmentDeadline(e.Value, e.ActionId); break;
                        case ScheduledKind.EvaluateGuard: EvaluateGuard(e.Value); break;
                        case ScheduledKind.DetentionServiceChange: ChangeDetentionService(e.ActionId, e.Value); break;
                        case ScheduledKind.DetentionReleaseDue: BeginDetentionRelease(e.Value); break;
                        case ScheduledKind.ExpireGuardOrder: ExpireGuardOrder(e.Value); break;
                        case ScheduledKind.EvaluateAudit: EvaluateAudit(e.Value); break;
                        case ScheduledKind.DailyBoundary: UpdateDailyCalendar(e.Intent.Actor); break; case ScheduledKind.EvaluateDaily: EvaluateDaily(e.Intent.Actor); break; case ScheduledKind.DailyCommand: ProcessIntentGroup(e, ref count); break; case ScheduledKind.EvaluateNeed: EvaluateNeed(e.Intent.Actor); break; case ScheduledKind.NeedCommand: ProcessIntentGroup(e, ref count); break; case ScheduledKind.GrantNeedService: GrantNeedService(e.Value); break; case ScheduledKind.NeedServiceChange: ChangeNeedService(e.ActionId, e.Value != 0); break; case ScheduledKind.NeedThreshold: ReachNeedThreshold(e.Intent.Actor); break; case ScheduledKind.RevokeDetention: RevokeDetention(e.Value); break; case ScheduledKind.ExpireDetentionAuthorization: ExpireDetentionAuthorization(e.Value); break;
                        case ScheduledKind.ReviewFacilityTasks: ReviewFacilityTasks(); break;
                        case ScheduledKind.EvaluatePatrol: EvaluatePatrol(e.Value); break;
                        case ScheduledKind.ExpirePatrol: ExpirePatrol(e.Value); break;
                        case ScheduledKind.Soiling: Soil(e.Value); break;
                        case ScheduledKind.MachineFault: FaultMachine(e.Value, "MachineFault"); break;
                        case ScheduledKind.MachineWear: WearMachine(e.Value); break;
                        case ScheduledKind.Spill: AddDirt(dirt[e.Value], e.ActionId, "Spill"); break;
                        case ScheduledKind.SessionExpiry:
                            if (sessions.TryGetValue(e.Intent.Actor, out var session) && session.Id == e.Value) EndSession(e.Intent.Actor, "Expired"); break;
                        case ScheduledKind.TerminalChange: ChangeTerminal(e.ActionId, (e.Value & 1) != 0, (e.Value & 2) != 0); break;
                        case ScheduledKind.CredentialRevocation: RevokeCredential(e.Value); break;
                        case ScheduledKind.GrantTerminal: GrantTerminal(e.Intent.Actor, e.Value,e.ActionId); break;
                        case ScheduledKind.EvaluateReport: EvaluateReport(e.Intent.Actor); break;
                        case ScheduledKind.EvaluateRepair: EvaluateRepair(e.Intent.Actor); break;
                        case ScheduledKind.DispatchWork: DispatchWork(e.Value); break;
                        case ScheduledKind.EvaluateWorkOrder: EvaluateWorkOrder(e.Value); break;
                        case ScheduledKind.CheckInDue: CheckInDue(e.Intent.Actor); break;
                        case ScheduledKind.EvaluateCheckIn: EvaluateCheckIn(e.Intent.Actor); break;
                        case ScheduledKind.ExpireWorkOrder: ExpireWorkOrder(e.Value); break;
                        case ScheduledKind.CancelAssignment: CancelAssignment(e.Value, "Cancelled"); break;
                        case ScheduledKind.ReleaseIdleTerminal: ReleaseIdleTerminal(e.Intent.Actor); break;
                        case ScheduledKind.WorkServiceChange: ChangeWorkService(e.ActionId, e.Intent.Kind == ActionKind.Install, e.Value != 0); break;
                        default: throw new InvalidOperationException("Unknown event.");
                    }
                    LastProcessedTick = e.Tick;
                }
                LastProcessedTick = targetTick;
            }
            catch (Exception e) when (e is InvalidOperationException || e is OverflowException)
            {
                RuntimeError = e.Message; Emit("RuntimeError", detail: e.Message); throw;
            }
            finally { processingTick = -1; processingPhase = 0; }
        }

        private void Start(ActionIntent intent,long parentCause = 0, ActionOrigin origin = ActionOrigin.Autonomous)
        {
            Actor actor = actors[intent.Actor]; var id = new Id<ActionTag>(nextAction++);
            long cause = Emit("ActionRequested", actor, id, intent.Item, target: intent.Target, detail: intent.Kind.ToString(),causeId:parentCause, attemptId:intent.AttemptId, origin: origin);
            var escort = ActiveEscort(actor.Id); if (escort != null && !((IsNeedAction(intent.Kind) || intent.Kind == ActionKind.Cancel) && CustodyNeedAllowed(actor, intent.Kind, intent.ServiceId)) && !GuardIntakeCommunication(escort, actor.Id, intent.Kind) && !(intent.Kind == ActionKind.Cancel && EscortPayload(escort).SubjectActorId != actor.Id.Value) && !(intent.Kind == ActionKind.RegisterIntake && escort.Status == EscortStatus.AwaitingIntake && EscortPayload(escort).Leader == actor.Id.Value)) { Fail(actor, id, FailureReason.CustodyRestricted); return; }
            if (intent.Kind == ActionKind.Cancel) { CancelAction(actor, id); return; }
            if (origin == ActionOrigin.Autonomous && intent.Kind == ActionKind.Move && custodyHandoffs.Count > 0 && HeldForCustodyHandoff(actor)) { Fail(actor, id, FailureReason.ActorBusy); return; }
            if (intent.Kind is ActionKind.SuspendWork or ActionKind.ResumeWork) { ChangeWorkSuspension(actor, id, intent.AttemptId, intent.Kind == ActionKind.ResumeWork); return; }
            if (Information != null && intent.Kind == ActionKind.Logout) { Logout(actor, id); return; }
            bool transit = actor.Action?.Kind == ActionKind.Move;
            // A local action at an unarrived destination is explicitly a locality failure.
            if (intent.Kind != ActionKind.Move && (transit || actor.Position != intent.Target)) { Fail(actor, id, FailureReason.NotAtRequiredTile); return; }
            if (actor.Action != null) { Fail(actor, id, FailureReason.ActorBusy); return; }
            if (resourceClaims.Any(c => c.Kind == ClaimKind.Staff && c.ResourceId == actor.Id.Value)) { Fail(actor, id, FailureReason.ActorBusy); return; }
            var action = new ActiveAction { Id = id, Kind = intent.Kind, Item = intent.Item, Target = intent.Target, Started = processingTick, ServiceId = intent.ServiceId, Message = intent.Message, CauseId = cause, RecipeId = intent.RecipeId, AttemptId = intent.AttemptId, Query = intent.Query, Quantity = intent.Quantity, OtherItem = intent.OtherItem, TargetActor = intent.TargetActor, PatrolOrderId = intent.PatrolOrderId, GuardOrderId = intent.GuardOrderId, ConversationTopic = intent.ConversationTopic };
            if (action.GuardOrderId > 0 && !CanExecuteGuard(actor, action)) { Fail(actor, id, FailureReason.UnknownRequiredFact); return; }
            if (action.PatrolOrderId > 0 && !CanExecutePatrol(actor, action.PatrolOrderId)) { Fail(actor, id, FailureReason.UnknownRequiredFact); return; }
            if (action.AttemptId > 0 && !CanExecuteKnownOrder(actor, action.AttemptId)) { Fail(actor, id, FailureReason.UnknownRequiredFact); return; }
            if (intent.Kind == ActionKind.Move)
            {
                PathRequests++; var result = Topology.FindPath(actor.Position, intent.Target, Config.PathBudget); PathExpansions += result.Expansions;
                if (result.Outcome != PathOutcome.PathFound)
                {
                    Fail(actor, id, result.Outcome switch { PathOutcome.InvalidDestination => FailureReason.InvalidDestination, PathOutcome.BudgetExhausted => FailureReason.PathBudgetExhausted, _ => FailureReason.NoRoute }); return;
                }
                if(result.Route.Count>1)EndSession(actor.Id, "Departure");
                action.Route.AddRange(result.Route); actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
                if (action.Route.Count > 1) RefreshGuardReadiness(actor);
                foreach (var order in localOrders.Values.Where(o => o.ActorId == actor.Id.Value && o.Kind == WorkKind.CleanTile)) order.CleanupChecked = false;
                if (action.Route.Count == 1) { Finish(actor); return; }
                StartSegment(actor); return;
            }
            if (IsRecoveryAction(intent.Kind)) { StartRecoveryAction(actor, action); return; } if (intent.Kind == ActionKind.Converse) { StartConversation(actor, action); return; } if (intent.Kind == ActionKind.AllocateBerth) { StartBerthAllocation(actor, action); return; }
            if (IsNeedAction(intent.Kind)) { StartNeedActivity(actor, action); return; } if (intent.Kind == ActionKind.ReleaseDetention) { Fail(actor, id, FailureReason.UnknownRequiredFact); return; }
            if (intent.Kind == ActionKind.RegisterIntake) { StartDetentionIntake(actor, action); return; }
            if (intent.Kind == ActionKind.BeginEscort) { StartBeginEscort(actor, action); return; }
            if (intent.Kind == ActionKind.JoinGuardTeam) { StartGuardJoin(actor, action); return; }
            if (intent.Kind == ActionKind.Equip) { StartEquip(actor, action); return; }
            if (intent.Kind == ActionKind.Clean) { StartCleaning(actor, action); return; }
            if (intent.Kind == ActionKind.InspectConstruction) { StartConstructionSurvey(actor, action); return; } if (intent.Kind == ActionKind.Construct) { StartConstruction(actor, action); return; } if (IsWorkAction(intent.Kind)) { StartWorkAction(actor, action); return; }
            if (IsStackAction(intent.Kind)) { StartStackAction(actor, action); return; }
            if (intent.Kind != ActionKind.PickUp && intent.Kind != ActionKind.PutDown)
            { StartInformationAction(actor, action); return; }
            FailureReason failure = ValidateTransfer(actor, action);
            if (failure != FailureReason.None) { ObserveFailedPickup(actor, action, failure); Fail(actor, id, failure); return; }
            action.Completes = checked(processingTick + Config.ItemActionTicks);
            AddClaim(ClaimKind.Item, action.Item.Value, actor, action);
            actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, id, intent.Item, target: intent.Target, arrival: action.Completes, detail: intent.Kind.ToString());
            ScheduleCompletion(actor, action.Completes);
        }
        private FailureReason ValidateTransfer(Actor actor, ActiveAction action)
        {
            if (actor.Position != action.Target) return FailureReason.NotAtRequiredTile;
            if (!items.TryGetValue(action.Item, out var item)) return action.Kind == ActionKind.PutDown ? FailureReason.NotHeld : FailureReason.MissingItem;
            if (action.Kind == ActionKind.PickUp)
            {
                if (!ItemAvailableAt(item, actor.Position)) return FailureReason.MissingItem;
            }
            else if (!HeldBy(item, actor)) return FailureReason.NotHeld;
            return ClaimedByOther(ClaimKind.Item, item.Id.Value, action.Id.Value) ? FailureReason.ResourceClaimed : FailureReason.None;
        }
        private void ScheduleCompletion(Actor actor, long tick)
        {
            RefreshNeedMode(actor); MarkWorkInProgress(actor.Action!);
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.PhysicalCompletion, Kind = ScheduledKind.ActionCompletion,
                Intent = new ActionIntent(actor.Id, actor.Action!.Kind, actor.Action.Target, actor.Action.Item), ActionId = actor.Action.Id.Value });
        }
        private void StartSegment(Actor actor)
        {
            var a = actor.Action!; a.Source = a.Route[a.RouteIndex]; a.Destination = a.Route[a.RouteIndex + 1];
            a.Departure = processingTick; a.Arrival = checked(processingTick + Topology.EdgeTicks(a.Source, a.Destination));
            Emit("MovementStarted", actor, a.Id, source: a.Source, target: a.Destination, departure: a.Departure, arrival: a.Arrival);
            ScheduleCompletion(actor, a.Arrival);
        }
        private void Complete(Id<ActorTag> actorId, long actionId)
        {
            var actor = actors[actorId]; var a = actor.Action;
            if (a == null || a.Id.Value != actionId) throw new InvalidOperationException("Completion does not own the current action.");
            if (a.Kind == ActionKind.Move)
            {
                var escort = ActiveEscort(actor.Id); if (escort != null) { CompleteEscortSegment(escort); return; }
                RelocateActor(actor, a.Destination); a.RouteIndex++;
                Emit("TileArrived", actor, a.Id, source: a.Source, target: actor.Position, departure: a.Departure, arrival: a.Arrival);
                ScheduleSensing(actor, a.CauseId);
                if (a.CancelAtBoundary) { CancelRunning(actor); return; }
                if (a.RouteIndex < a.Route.Count - 1) StartSegment(actor); else Finish(actor);
                return;
            }
            if (IsRecoveryAction(a.Kind)) { CompleteRecoveryAction(actor, a); return; } if (a.Kind == ActionKind.Converse) { CompleteConversation(actor, a); return; } if (a.Kind == ActionKind.AllocateBerth) { CompleteBerthAllocation(actor, a); return; }
            if (IsNeedAction(a.Kind)) { CompleteNeedActivity(actor, a); return; } if (a.Kind == ActionKind.RegisterIntake) { CompleteDetentionIntake(actor, a); return; }
            if (a.Kind == ActionKind.ReleaseDetention) { CompleteDetentionRelease(actor, a); return; }
            if (a.Kind == ActionKind.BeginEscort) { CompleteBeginEscort(actor, a); return; }
            if (a.Kind == ActionKind.JoinGuardTeam) { CompleteGuardJoin(actor, a); return; }
            if (a.Kind == ActionKind.Equip) { CompleteEquip(actor, a); return; }
            if (a.Kind == ActionKind.Clean) { CompleteCleaning(actor, a); return; }
            if (a.Kind == ActionKind.InspectConstruction) { CompleteConstructionSurvey(actor, a); return; } if (a.Kind == ActionKind.Construct) { CompleteConstruction(actor, a); return; } if (IsWorkAction(a.Kind)) { CompleteWorkAction(actor, a); return; }
            if (IsStackAction(a.Kind)) { CompleteStackAction(actor, a); return; }
            if (a.Kind != ActionKind.PickUp && a.Kind != ActionKind.PutDown) { CompleteInformationAction(actor, a); return; }
            var failure = ValidateTransfer(actor, a);
            if (failure != FailureReason.None) { ObserveFailedPickup(actor, a, failure); ReleaseResourceClaims(a.Id.Value); ReleaseBerthReservation(actor, a); ReleaseNeedActivity(actor, a); actor.Action = null; Fail(actor, a.Id, failure); return; }
            var item = items[a.Item];
            MoveItem(item, a.Kind == ActionKind.PickUp ? new ItemLocation(LocationKind.ActorInventory, actor.Id.Value)
                : new ItemLocation(LocationKind.TileContainer, Topology.Tiles[actor.Position].Id.Value));
            Emit("ItemMoved", actor, a.Id, item.Id, target: actor.Position, detail: item.Location.Kind.ToString());
            ObserveOwnTransfer(actor, item, a.CauseId);
            Finish(actor);
        }
        private void Finish(Actor actor)
        {
            var a = actor.Action!; ActionsCompleted++; actor.LastStatus = ActionStatus.Completed; actor.LastFailure = FailureReason.None;
            RecordPhysicalWorkCompletion(actor, a);
            ReleaseResourceClaims(a.Id.Value); ReleaseBerthReservation(actor, a); ReleaseNeedActivity(actor, a);
            Emit("ActionCompleted", actor, a.Id, a.Item, target: actor.Position, detail: a.Kind.ToString()); actor.Action = null; OnActionCompleted(actor, a);
        }
        private void Fail(Actor actor, Id<ActionTag> id, FailureReason reason)
        { ActionsFailed++; actor.LastFailure = reason; actor.LastStatus = ActionStatus.Failed; long cause = Emit("ActionFailed", actor, id, detail: reason.ToString()); RecordCredentialFailure(actor, reason, cause); RecoveryActionFailed(actor, id, reason); DailyActionEnded(actor, id.Value, reason); NeedActionEnded(actor, id.Value, reason); OnActionFailed(actor); }
        private long Emit(string type, Actor? actor = null, Id<ActionTag> action = default, Id<ItemTag> item = default,
            Coord source = default, Coord target = default, long departure = 0, long arrival = 0, string detail = "", long causeId = 0, long messageId = 0, long terminalId = 0, long sessionId = 0, FactKey? factKey = null, FactValue? factValue = null, long attemptId = 0, long otherItemId = 0, long quantity = 0, long sensorId = 0, long readingId = 0, ActionOrigin origin = ActionOrigin.None)
        {
            long id = nextTrace++; // Trace IDs progress identically even with trace collection disabled.
            if (attemptId == 0 && actor?.Action?.Id == action) attemptId = actor.Action.AttemptId;
            if (attemptId == 0 && messageId > 0 && messages.TryGetValue(messageId, out var message)) attemptId = message.Draft.AttemptId;
            long taskId = attemptId > 0 && workAttempts.TryGetValue(attemptId, out var attempt) ? attempt.JobId : 0;
            if (TraceEnabled) trace.Add(new TraceEvent { Id = id, Tick = processingTick < 0 ? LastProcessedTick : processingTick, Type = type,
                ActorId = actor?.Id.Value ?? 0, ActionId = action.Value, ItemId = item.Value, OtherItemId = otherItemId, Quantity = quantity, Source = source, Target = target,
                Departure = departure, Arrival = arrival, Detail = detail, Visibility = actor == null ? "privileged_truth" : "actor_visible",
                Origin = origin, CauseId = causeId, MessageId = messageId, TerminalId = terminalId, SessionId = sessionId, AccountId = actor?.Account.Value ?? 0, FactKey=factKey, FactValue=factValue, TaskId=taskId, AttemptId=attemptId, SensorId=sensorId, ReadingId=readingId });
            return id;
        }

        public ActorSnapshot InspectActor(Id<ActorTag> id)
        {
            var actor = actors[id]; var a = actor.Action; bool transit = a?.Kind == ActionKind.Move;
            return new ActorSnapshot { Id = id, Name = actor.Name, Role = actor.Role, Position = actor.Position, InTransit = transit,
                Source = transit ? a!.Source : actor.Position, Destination = transit ? a!.Destination : actor.Position,
                Departure = transit ? a!.Departure : 0, Arrival = transit ? a!.Arrival : 0,
                ProgressNumerator = transit ? Math.Max(0, LastProcessedTick - a!.Departure) : 0,
                ProgressDenominator = transit ? a!.Arrival - a.Departure : 1,
                Action = a?.Kind, ActionId = a?.Id.Value ?? 0, ActionStart = a?.Started ?? 0, ActionCompletion = a?.Completes ?? 0,
                LastStatus = actor.LastStatus, LastFailure = actor.LastFailure,
                Inventory = Array.AsReadOnly(ItemsAt(LocationKind.ActorInventory, id.Value).Select(i => i.Id).ToArray()) };
        }
        public IReadOnlyList<ActorSnapshot> InspectActors() => actors.Keys.Select(InspectActor).ToArray();
        public IReadOnlyList<Item> InspectItems() => items.Values.Select(i => i.Copy()).ToArray();
        public IActorView ActorView(Id<ActorTag> id) => new ActorViewSnapshot(InspectActor(id), processingTick < 0 ? LastProcessedTick : processingTick, actors[id].Account, actors[id].Knowledge.Inspect(), publicKnowledge, sessions.TryGetValue(id, out var session) && session.Scope == SessionScope.Normal ? session.Terminal : 0, recordReceipts.TryGetValue(id, out var receipt) ? receipt : null, routines.TryGetValue(id, out var routine) ? routine.KnownRoster : null, EquippedItem(id), NeedValues(id.Value), KnownNeedServices(actors[id]), OwnNeedCustodyFacility(id), KnownOwnBerth(id));
        private sealed class ActorViewSnapshot : IActorView
        {
            public Id<ActorTag> Actor { get; }
            public long Tick { get; }
            public Coord? CurrentTile { get; }
            public IReadOnlyList<Id<ItemTag>> Inventory { get; }
            public Id<ItemTag> EquippedItem { get; } public IReadOnlyList<NeedValue> Needs { get; } public IReadOnlyList<NeedServiceDefinition> NeedServices { get; } public long NeedCustodyFacilityId { get; } public long OwnBerthId { get; }
            public IReadOnlyList<KnowledgeFact> Knowledge { get; }
            public PublicKnowledge? PublicKnowledge { get; }
            public Id<AccountTag> Account { get; }
            public string Role { get; }
            public long SessionTerminalId { get; }
            public RecordQueryReceipt? LastRecordQuery { get; }
            public RosterPayload? KnownRoster { get; }
            internal ActorViewSnapshot(ActorSnapshot a, long tick, Id<AccountTag> account, IReadOnlyList<KnowledgeFact> knowledge, PublicKnowledge? shared, long terminal, RecordQueryReceipt? receipt, RosterPayload? roster, Id<ItemTag> equippedItem, IReadOnlyList<NeedValue> needs, IReadOnlyList<NeedServiceDefinition> needServices, long needCustodyFacilityId, long ownBerthId)
            { Actor = a.Id; Tick = tick; CurrentTile = a.InTransit ? (Coord?)null : a.Position; Inventory = a.Inventory; Account = account; Role = a.Role; Knowledge = knowledge; PublicKnowledge = shared; SessionTerminalId = terminal; LastRecordQuery = receipt; KnownRoster = roster; EquippedItem = equippedItem; Needs = needs; NeedServices = needServices; NeedCustodyFacilityId = needCustodyFacilityId; OwnBerthId = ownBerthId; }
        }
        public void AssertAccounting()
        {
            AssertDirtAccounting();
            if (itemsByLocation.Values.Sum(g => g.Count) != items.Count || items.Values.Any(i =>
                !itemsByLocation.TryGetValue((i.Location.Kind, i.Location.Owner), out var group) || !group.TryGetValue(i.Id, out var indexed) || !ReferenceEquals(i, indexed)))
                throw new InvalidOperationException("Physical item index mismatch.");
            var balances = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var item in items.Values) { ValidateLocation(item.Location); balances.TryGetValue(item.Resource, out long n); balances[item.Resource] = checked(n + item.Quantity); }
            var expected = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var balance in initialBalances) expected.Add(balance.Key, balance.Value);
            foreach (var transaction in resourceTransactions)
            { expected.TryGetValue(transaction.Resource, out decimal n); expected[transaction.Resource] = checked(n + transaction.Delta); }
            foreach (var sink in mealSinks) { expected.TryGetValue(sink.Key, out decimal eaten); expected[sink.Key] = checked(eaten - sink.Value); } foreach (var sink in faultSinks)
            { expected.TryGetValue(sink.Key, out decimal n); expected[sink.Key] = checked(n - sink.Value); }
            if (!balances.Where(x => x.Value != 0).Select(x => (x.Key, (decimal)x.Value)).SequenceEqual(expected.Where(x => x.Value != 0).Select(x => (x.Key, x.Value)))) throw new InvalidOperationException("Resource accounting mismatch.");
            foreach (var actor in actors.Values) RegisterLocalActor(actor); ValidateWorkItems();
        }
    }
}
