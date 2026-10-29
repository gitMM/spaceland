using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<string, ResourceDefinition> resources = new SortedDictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        private readonly SortedDictionary<long, RecipeDefinition> recipes = new SortedDictionary<long, RecipeDefinition>();
        private readonly SortedDictionary<long, WorkstationDefinition> workstations = new SortedDictionary<long, WorkstationDefinition>();
        private readonly SortedDictionary<long, MachineDefinition> machines = new SortedDictionary<long, MachineDefinition>();
        private readonly SortedSet<long> disabledWorkstations = new SortedSet<long>();
        private readonly SortedSet<long> disabledMachines = new SortedSet<long>();
        private readonly List<ResourceClaim> resourceClaims = new List<ResourceClaim>();
        private readonly List<ResourceTransaction> resourceTransactions = new List<ResourceTransaction>();
        private long nextItem = 1;
        private long nextTransaction = 1;
        public bool WorkEnabled { get; private set; }
        private PublicWorkCatalog? PublishWork() => !WorkEnabled && cleanliness == null ? null : new PublicWorkCatalog(resources.Values, recipes.Values,
            workstations.Values.Select(s => new PublicWorkstation(s.Id, s.Position, s.Capability, s.Access.ToolResource, s.Access.Roles, s.Access.RequiresCredential)),
            machines.Values.Select(m => new PublicEquipment(m.Id, m.Position, m.FitClass, m.Access.ToolResource, m.Access.Roles, m.InstallTicks, m.InspectTicks, m.Access.RequiresCredential)), Config.ItemActionTicks,
            dirt.Values.Select(d => new PublicCleanable(new Id<TileTag>(d.TileId), d.Position, cleanliness!.CleanUnitTicks, cleanliness.Access.ToolResource, cleanliness.Access.Roles)));
        public IReadOnlyList<ResourceClaim> InspectResourceClaims() => resourceClaims.Select(c => c.Copy()).ToArray();
        public IReadOnlyList<ResourceTransaction> InspectResourceTransactions() => resourceTransactions.Select(t => t.Copy()).ToArray();
        public IReadOnlyList<MachineSnapshot> InspectMachines() => machines.Values.Select(m => new MachineSnapshot
        { Id = m.Id, Position = m.Position, RequiredResource = m.RequiredResource, InstalledItemId = InstalledItem(m.Id)?.Id.Value ?? 0, Operational = MachineOperational(m) }).ToArray();

        private void InitializeWork(WorkSeed seed)
        {
            WorkEnabled = true;
            foreach (var stash in seed.Stashes)
            { if (!Topology.Contains(stash.Position)) throw new ArgumentException("Stash requires a completed physical tile."); stashes.Add(stash.Id, stash); }
            foreach (var r in seed.Resources) resources.Add(r.Id, r);
            foreach (var r in seed.Recipes)
            {
                if (r.Inputs.Concat(r.Outputs).Any(x => !resources.ContainsKey(x.Resource))) throw new ArgumentException("Recipe references an unknown resource.");
                recipes.Add(r.Id, r);
            }
            foreach (var w in seed.Workstations)
            {
                ValidateWorkAccess(w.Position, w.Access); workstations.Add(w.Id, w);
            }
            foreach (var m in seed.Machines)
            {
                ValidateWorkAccess(m.Position, m.Access);
                if (!resources.TryGetValue(m.RequiredResource, out var r) || r.FitClass != m.FitClass || r.Rating < m.RequiredRating)
                    throw new ArgumentException("Machine specification must reference a fitting resource of sufficient rating.");
                machines.Add(m.Id, m);
            }
        }
        private void ValidateWorkAccess(Coord position, WorkAccess access)
        {
            if (!Topology.Contains(position) || (access.ToolResource.Length > 0 && !resources.ContainsKey(access.ToolResource)) ||
                (access.Staff.Value > 0 && !actors.ContainsKey(access.Staff))) throw new ArgumentException("Invalid work service location, tool or staff.");
        }
        private void ValidateWorkItems()
        {
            foreach (var item in items.Values)
            {
                if (WorkEnabled && !resources.ContainsKey(item.Resource)) throw new ArgumentException("Unknown item resource: " + item.Resource);
                if (item.Quantity < 1) throw new ArgumentException("Nonpositive item stack.");
                if (item.Location.Kind != LocationKind.MachineSlot) continue;
                var machine = machines[item.Location.Owner];
                if (item.Quantity != 1 || resources[item.Resource].FitClass != machine.FitClass) throw new ArgumentException("Invalid cartridge in machine slot.");
            }
            if (items.Values.Where(i => i.Location.Kind == LocationKind.MachineSlot).GroupBy(i => i.Location.Owner).Any(g => g.Count() > 1))
                throw new ArgumentException("Multiple items in one machine slot.");
        }
        private static bool IsWorkAction(ActionKind kind) => kind == ActionKind.ProduceRecipe || kind == ActionKind.Install || kind == ActionKind.FunctionalTest || kind == ActionKind.InspectMachine;
        private static bool RequestsClaim(ActionKind kind) => kind == ActionKind.Construct || kind == ActionKind.Converse || kind == ActionKind.AllocateBerth || kind == ActionKind.IssueReplacement || IsLogin(kind) || kind == ActionKind.PickUp || kind == ActionKind.PutDown || kind == ActionKind.Clean || IsWorkAction(kind) || IsStackAction(kind) || IsNeedAction(kind);
        private bool ClaimedByOther(ClaimKind kind, long id, long action) => kind == ClaimKind.Item && MealReservedByOther(id, action) || resourceClaims.Any(c => c.Kind == kind && c.ResourceId == id && c.ActionId != action);
        private void AddClaim(ClaimKind kind, long id, Actor actor, ActiveAction action)
        {
            if (resourceClaims.Any(c => c.Kind == kind && c.ResourceId == id && c.ActionId == action.Id.Value)) return;
            resourceClaims.Add(new ResourceClaim { Kind = kind, ResourceId = id, ActorId = actor.Id.Value, ActionId = action.Id.Value, RequestedTick = processingTick });
            Emit("ResourceClaimed", actor, action.Id, detail: kind + ":" + id, causeId: action.CauseId);
        }
        private void ReleaseResourceClaims(long action)
        {
            foreach (var c in resourceClaims.Where(c => c.ActionId == action).ToArray())
            {
                resourceClaims.Remove(c);
                Emit("ResourceClaimReleased", actors[new Id<ActorTag>(c.ActorId)], new Id<ActionTag>(action), detail: c.Kind + ":" + c.ResourceId);
            }
        }
        private bool ItemAvailableAt(Item item, Coord position) =>
            (item.Location.Kind == LocationKind.TileContainer && Topology.ById[new Id<TileTag>(item.Location.Owner)].Position == position) ||
            (item.Location.Kind == LocationKind.ServiceOutput && workstations[item.Location.Owner].Position == position) ||
            (item.Location.Kind == LocationKind.ProjectStash && stashes[item.Location.Owner].Position == position);
        private bool HeldBy(Item item, Actor actor) => item.Location.Kind == LocationKind.ActorInventory && item.Location.Owner == actor.Id.Value;
        private Item? InstalledItem(long machine) => items.Values.FirstOrDefault(i => i.Location.Kind == LocationKind.MachineSlot && i.Location.Owner == machine);
        private bool MachineOperational(MachineDefinition machine)
        {
            var part = InstalledItem(machine.Id);
            return !disabledMachines.Contains(machine.Id) && part != null && part.Resource == machine.RequiredResource && resources[part.Resource].Rating >= machine.RequiredRating;
        }

        public void ScheduleWorkServiceChange(long tick, long id, bool machine, bool enabled)
        {
            ValidateFuture(tick);
            if (machine ? !machines.ContainsKey(id) : !workstations.ContainsKey(id)) throw new ArgumentException("Unknown work service.");
            Schedule(new ScheduledEvent { Tick = tick, Phase = EventPhase.Disturbance, Kind = ScheduledKind.WorkServiceChange,
                ActionId = id, Value = enabled ? 1 : 0, Intent = new ActionIntent(default, machine ? ActionKind.Install : ActionKind.ProduceRecipe, default) });
        }
        private void ChangeWorkService(long id, bool machine, bool enabled)
        {
            var disabled = machine ? disabledMachines : disabledWorkstations;
            if (enabled) disabled.Remove(id); else disabled.Add(id);
            Emit("WorkServiceChanged", detail: (machine ? "Machine:" : "Workstation:") + id + "; enabled=" + enabled);
            var at = machine ? machines[id].Position : workstations[id].Position;
            foreach (var actor in actors.Values.Where(a => a.Position == at && a.Action?.Kind != ActionKind.Move)) ScheduleSensing(actor, 0);
        }
        private FailureReason CheckWorkAccess(Actor actor, ActiveAction action, Coord position, WorkAccess access, bool bind)
        {
            if (actor.Position != position || actor.Position != action.Target) return FailureReason.NotAtRequiredTile;
            if (!access.Roles.Contains(actor.Role, StringComparer.Ordinal)) return FailureReason.NotQualified;
            if (access.RequiresCredential) { var credentialFailure = CredentialFor(actor, out _); if (credentialFailure != FailureReason.None) return credentialFailure; }
            if (access.Staff.Value > 0)
            {
                var staff = actors[access.Staff];
                if (staff.Id == actor.Id || staff.Action != null || staff.Position != position || ClaimedByOther(ClaimKind.Staff, staff.Id.Value, action.Id.Value)) return FailureReason.StaffUnavailable;
            }
            if (access.ToolResource.Length == 0) return FailureReason.None;
            if (bind)
            {
                var tool = items.Values.FirstOrDefault(i => HeldBy(i, actor) && i.Resource == access.ToolResource && !ClaimedByOther(ClaimKind.Item, i.Id.Value, action.Id.Value));
                if (tool == null) return FailureReason.MissingTool;
                action.Materials.Add(new MaterialBinding { Item = tool.Id });
            }
            else if (!action.Materials.Any(b => b.Quantity == 0 && items.TryGetValue(b.Item, out var i) && HeldBy(i, actor) && i.Resource == access.ToolResource)) return FailureReason.MissingTool;
            return FailureReason.None;
        }
        private FailureReason ValidateWork(Actor actor, ActiveAction action, bool bind, out long duration, out WorkAccess? access)
        {
            duration = 0; access = null;
            if (!WorkEnabled) return FailureReason.RequiredServiceNotPresent;
            if (action.Kind == ActionKind.ProduceRecipe)
            {
                if (!workstations.TryGetValue(action.ServiceId, out var station) || disabledWorkstations.Contains(station.Id) ||
                    !recipes.TryGetValue(action.RecipeId, out var recipe) || recipe.Capability != station.Capability) return FailureReason.RequiredServiceNotPresent;
                access = station.Access; duration = recipe.Duration;
                var failure = CheckWorkAccess(actor, action, station.Position, access, bind); if (failure != FailureReason.None) return failure;
                if (resourceClaims.Count(c => c.Kind == ClaimKind.Workstation && c.ResourceId == station.Id && c.ActionId != action.Id.Value) >= station.Capacity) return FailureReason.ServiceBusy;
                if (bind)
                {
                    foreach (var input in recipe.Inputs)
                    {
                        long remaining = input.Quantity; bool claimed = false;
                        foreach (var item in items.Values.Where(i => i.Resource == input.Resource && HeldBy(i, actor)))
                        {
                            // Credential lifecycle owns card destruction/replacement; a recipe cannot
                            // invalidate credential records by deleting their physical card entity.
                            if (credentials.Values.Any(c => c.Definition.Card == item.Id)) continue;
                            if (action.Materials.Any(b => b.Item == item.Id)) continue; // A reserved tool cannot be consumed.
                            if (ClaimedByOther(ClaimKind.Item, item.Id.Value, action.Id.Value)) { claimed = true; continue; }
                            long amount = Math.Min(remaining, item.Quantity);
                            action.Materials.Add(new MaterialBinding { Item = item.Id, Quantity = amount }); remaining -= amount;
                            if (remaining == 0) break;
                        }
                        if (remaining > 0) return claimed ? FailureReason.ResourceClaimed : FailureReason.InsufficientMaterials;
                    }
                }
                else
                {
                    foreach (var input in recipe.Inputs)
                        if (action.Materials.Where(b => b.Quantity > 0 && items.TryGetValue(b.Item, out var i) && i.Resource == input.Resource).Sum(b => b.Quantity) != input.Quantity)
                            return FailureReason.InsufficientMaterials;
                }
            }
            else
            {
                if (!machines.TryGetValue(action.ServiceId, out var machine) || disabledMachines.Contains(machine.Id)) return FailureReason.RequiredServiceNotPresent;
                access = machine.Access;
                // Inspection observes local equipment and requires no installation tool or staff.
                if (action.Kind == ActionKind.InspectMachine)
                {
                    if (actor.Position != machine.Position) return FailureReason.NotAtRequiredTile;
                    duration = machine.InspectTicks;
                }
                else
                {
                    var failure = CheckWorkAccess(actor, action, machine.Position, access, bind); if (failure != FailureReason.None) return failure;
                    duration = action.Kind == ActionKind.Install ? machine.InstallTicks : machine.TestTicks;
                }
                if (ClaimedByOther(ClaimKind.Machine, machine.Id, action.Id.Value)) return FailureReason.ServiceBusy;
                if (action.Kind == ActionKind.Install)
                {
                    if (!items.TryGetValue(action.Item, out var part) || !HeldBy(part, actor)) return FailureReason.NotHeld;
                    if (credentials.Values.Any(c => c.Definition.Card == part.Id)) return FailureReason.IncompatiblePart;
                    if (part.Quantity != 1 || resources[part.Resource].FitClass != machine.FitClass) return FailureReason.IncompatiblePart;
                    if (ClaimedByOther(ClaimKind.Item, part.Id.Value, action.Id.Value)) return FailureReason.ResourceClaimed;
                    if (action.Materials.Any(b => b.Item == part.Id)) return FailureReason.MissingTool;
                    // Suitability is deliberately absent here: fitting wrong parts can be installed.
                }
            }
            foreach (var binding in action.Materials)
            {
                if (!items.TryGetValue(binding.Item, out var item) || !HeldBy(item, actor) || item.Quantity < binding.Quantity) return FailureReason.InsufficientMaterials;
                if (ClaimedByOther(ClaimKind.Item, binding.Item.Value, action.Id.Value)) return FailureReason.ResourceClaimed;
            }
            return FailureReason.None;
        }
        private void ValidateRecipeCommit(RecipeDefinition recipe)
        {
            // Overflow is detected before consumption, so even a failed world keeps its resources.
            _ = checked(nextItem + recipe.Outputs.Count);
            _ = checked(nextTransaction + recipe.Inputs.Count + recipe.Outputs.Count);
            foreach (var resource in recipe.Inputs.Select(i => i.Resource).Concat(recipe.Outputs.Select(o => o.Resource)).Distinct())
            {
                long total = items.Values.Where(i => i.Resource == resource).Sum(i => i.Quantity);
                long removed = recipe.Inputs.Where(i => i.Resource == resource).Sum(i => i.Quantity);
                long added = recipe.Outputs.Where(i => i.Resource == resource).Sum(i => i.Quantity);
                _ = checked(total - removed + added);
            }
        }
        private void StartWorkAction(Actor actor, ActiveAction action)
        {
            var failure = ValidateWork(actor, action, true, out long duration, out var access);
            if (failure != FailureReason.None) { Fail(actor, action.Id, failure); return; }
            long completes = checked(processingTick + duration);
            // Commit all claims only after every input/access/capacity check passes.
            AddClaim(action.Kind == ActionKind.ProduceRecipe ? ClaimKind.Workstation : ClaimKind.Machine, action.ServiceId, actor, action);
            foreach (var binding in action.Materials) AddClaim(ClaimKind.Item, binding.Item.Value, actor, action);
            if (action.Kind == ActionKind.Install) AddClaim(ClaimKind.Item, action.Item.Value, actor, action);
            if (action.Kind != ActionKind.InspectMachine && access!.Staff.Value > 0) AddClaim(ClaimKind.Staff, access.Staff.Value, actor, action);
            action.Completes = completes; actor.Action = action; actor.LastStatus = ActionStatus.Running; actor.LastFailure = FailureReason.None;
            Emit("ActionStarted", actor, action.Id, action.Item, target: actor.Position, arrival: action.Completes, detail: action.Kind.ToString(), causeId: action.CauseId);
            ScheduleCompletion(actor, action.Completes);
        }
        private void CompleteWorkAction(Actor actor, ActiveAction action)
        {
            var failure = ValidateWork(actor, action, false, out _, out _);
            if (failure != FailureReason.None)
            { ReleaseResourceClaims(action.Id.Value); actor.Action = null; Fail(actor, action.Id, failure); return; }
            if (action.Kind == ActionKind.ProduceRecipe)
            {
                var recipe = recipes[action.RecipeId];
                ValidateRecipeCommit(recipe);
                foreach (var binding in action.Materials.Where(b => b.Quantity > 0))
                {
                    var item = items[binding.Item]; item.Quantity -= binding.Quantity;
                    Emit("ItemConsumed", actor, action.Id, item.Id, detail: item.Resource + ":" + binding.Quantity, causeId: action.CauseId);
                    if (item.Quantity == 0) RemoveItem(item.Id);
                    ObserveItemDescription(actor, item, action.CauseId);
                }
                foreach (var input in recipe.Inputs) RecordRecipeDelta(actor, action, input.Resource, -input.Quantity);
                foreach (var output in recipe.Outputs)
                {
                    var item = new Item(new Id<ItemTag>(nextItem++), output.Resource, output.Quantity, new ItemLocation(LocationKind.ServiceOutput, action.ServiceId));
                    AddItem(item); RecordRecipeDelta(actor, action, output.Resource, output.Quantity);
                    Emit("ItemProduced", actor, action.Id, item.Id, target: actor.Position, detail: item.Resource + ":" + item.Quantity, causeId: action.CauseId);
                    ObserveOwnTransfer(actor, item, action.CauseId);
                }
            }
            else
            {
                var machine = machines[action.ServiceId];
                if (action.Kind == ActionKind.Install)
                {
                    var previous = InstalledItem(machine.Id);
                    if (previous != null)
                    {
                        MoveItem(previous, new ItemLocation(LocationKind.TileContainer, Topology.Tiles[machine.Position].Id.Value));
                        Emit("ItemMoved", actor, action.Id, previous.Id, target: actor.Position, detail: "TileContainer", causeId: action.CauseId);
                        ObserveOwnTransfer(actor, previous, action.CauseId);
                    }
                    var part = items[action.Item]; MoveItem(part, new ItemLocation(LocationKind.MachineSlot, machine.Id));
                    Emit("ItemInstalled", actor, action.Id, part.Id, target: actor.Position, detail: "Machine:" + machine.Id, causeId: action.CauseId);
                    ObserveOwnTransfer(actor, part, action.CauseId);
                }
                ObserveMachine(actor, machine, action.Kind == ActionKind.InspectMachine, action);
                if (action.Kind == ActionKind.FunctionalTest || action.Kind == ActionKind.Install)
                    Emit("MachineTested", actor, action.Id, target: actor.Position, detail: "Machine:" + machine.Id + "; operational=" + MachineOperational(machine), causeId: action.CauseId);
            }
            Finish(actor); AssertAccounting();
            foreach (var observer in actors.Values.Where(a => a.Position == actor.Position && a.Action?.Kind != ActionKind.Move)) ScheduleSensing(observer, action.CauseId);
        }
        private void RecordRecipeDelta(Actor actor, ActiveAction action, string resource, long delta)
        {
            resourceTransactions.Add(new ResourceTransaction { Id = nextTransaction++, Tick = processingTick, ActorId = actor.Id.Value,
                ActionId = action.Id.Value, RecipeId = action.RecipeId, Resource = resource, Delta = delta });
        }
        private void ObserveMachine(Actor actor, MachineDefinition machine, bool inspect, ActiveAction? action = null, long sensingCause = 0)
        {
            if (Information == null) return;
            var source = inspect ? EvidenceKind.Inspection : EvidenceKind.Direct;
            long cause = action?.CauseId ?? sensingCause, sourceId = action?.Id.Value ?? machine.Id;
            ObservePerceived(actor, new FactKey(SubjectKind.Machine, machine.Id, AttributeKind.Location), FactValue.At(machine.Position), source, sourceId, cause, !inspect && action == null);
            ObservePerceived(actor, new FactKey(SubjectKind.Machine, machine.Id, AttributeKind.Operational), FactValue.Boolean(MachineOperational(machine)), source, sourceId, cause, !inspect && action == null);
            ObservePerceived(actor, new FactKey(SubjectKind.Machine, machine.Id, AttributeKind.RequiredResource), new FactValue(FactValueKind.Text, text: machine.RequiredResource), source, sourceId, cause, !inspect);
            ObservePerceived(actor, new FactKey(SubjectKind.Machine, machine.Id, AttributeKind.InstalledResource), new FactValue(FactValueKind.Text, text: InstalledItem(machine.Id)?.Resource ?? ""), source, sourceId, cause, !inspect);
        }
        private void ObserveItemDescription(Actor actor, Item item, long cause, EvidenceKind source = EvidenceKind.Direct, long sourceId = 0, bool passive = false)
        {
            if (Information == null || !WorkEnabled) return;
            if (sourceId == 0) sourceId = actor.Id.Value;
            ObservePerceived(actor, new FactKey(SubjectKind.Item, item.Id.Value, AttributeKind.Resource), new FactValue(FactValueKind.Text, text: item.Resource), source, sourceId, cause, passive);
            ObservePerceived(actor, new FactKey(SubjectKind.Item, item.Id.Value, AttributeKind.Quantity), FactValue.Integer(item.Quantity), source, sourceId, cause, passive);
            ObservePerceived(actor, new FactKey(SubjectKind.Item, item.Id.Value, AttributeKind.Present), FactValue.Boolean(item.Quantity > 0), source, sourceId, cause, passive);
            ObservePerceived(actor, new FactKey(SubjectKind.Item, item.Id.Value, AttributeKind.LocationKind), FactValue.Integer((int)item.Location.Kind), source, sourceId, cause, passive);
            ObservePerceived(actor, new FactKey(SubjectKind.Item, item.Id.Value, AttributeKind.LocationOwner), FactValue.Integer(item.Location.Owner), source, sourceId, cause, passive);
        }
    }
}
