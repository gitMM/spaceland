using System.Text.Json;
using System.Text.Json.Serialization;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public sealed class CoordinateData
{
    [JsonRequired] public int X { get; set; }
    [JsonRequired] public int Y { get; set; }
    [JsonRequired] public int Z { get; set; }
    public Coord ToCoord() => new(X, Y, Z);
    public static CoordinateData From(Coord c) => new() { X = c.X, Y = c.Y, Z = c.Z };
}
public sealed class CatalogData
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public List<DefinitionData> Tiles { get; set; } = new();
    public List<ResourceData> Resources { get; set; } = new();
    public List<RecipeData> Recipes { get; set; } = new();
    public List<PerceptionRuleData> Perception { get; set; } = new();
}
public sealed class DefinitionData
{
    public ConstructionDefinitionData? Construction { get; set; }
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public TileFamily Family { get; set; }
    [JsonRequired] public long TraversalTicks { get; set; }
    public string VisualKey { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
}
public sealed class ScenarioData
{
    [JsonRequired] public InitialStateData InitialState { get; set; } = new();
    public List<DisturbanceData> Disturbances { get; set; } = new();
    public List<ScriptedActorData> ScriptedActors { get; set; } = new();
    public List<AssertionData> Assertions { get; set; } = new();
    public List<string> Metrics { get; set; } = new();
}
public sealed class InitialStateData
{
    public ConstructionData? Construction { get; set; }
    public GrowthData? Growth { get; set; }
    public List<ResponseChainRoleData> ResponseChainRoles { get; set; } = new();
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string Catalog { get; set; } = "";
    [JsonRequired] public ulong Seed { get; set; }
    [JsonRequired] public long Horizon { get; set; }
    public ConfigurationData Configuration { get; set; } = new();
    [JsonRequired] public List<TileData> Tiles { get; set; } = new();
    public List<ActorData> Actors { get; set; } = new();
    public List<ItemData> Items { get; set; } = new();
    public InformationData? Information { get; set; }
    public WorkData? Work { get; set; }
    public JobSystemData? JobSystem { get; set; }
    public RoutineData? Routines { get; set; } public NeedsData? Needs { get; set; }
    public AuthorityData? Authority { get; set; }
    public CleanlinessData? Cleanliness { get; set; }
    public QualityAssuranceData? QualityAssurance { get; set; }
    public PersonnelData? Personnel { get; set; }
    public DetentionData? Detention { get; set; }
    public MachineWearData? MachineWear { get; set; }
    public FacilityData? Facility { get; set; }
}
public sealed class ConfigurationData
{
    public int TickDurationMs { get; set; } = 100;
    public long ItemActionTicks { get; set; } = 10;
    public int PathBudget { get; set; } = 100000;
    public int MaxEventsPerTick { get; set; } = 1000000;
    public long AuthorityNominalTicks { get; set; } = 40;
    public WorldConfig ToConfig() => new(TickDurationMs, ItemActionTicks, PathBudget, MaxEventsPerTick, AuthorityNominalTicks);
}
public sealed class TileData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public string Definition { get; set; } = "";
    [JsonRequired] public CoordinateData Position { get; set; } = new();
    public bool Completed { get; set; } = true;
    public long? TraversalOverride { get; set; }
}
public sealed class ActorData
{
    [JsonRequired] public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "worker";
    [JsonRequired] public CoordinateData Position { get; set; } = new();
    public bool Scripted { get; set; }
    public bool? AutonomousNeeds { get; set; }
    public long AccountId { get; set; }
}
public sealed class ItemData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public string Resource { get; set; } = "";
    [JsonRequired] public long Quantity { get; set; }
    [JsonRequired] public LocationKind LocationKind { get; set; }
    [JsonRequired] public long Owner { get; set; }
}
public sealed class ScriptedActorData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public List<CommandData> Commands { get; set; } = new();
}
public sealed class CommandData
{
    [JsonRequired] public long Tick { get; set; }
    [JsonRequired] public ActionKind Kind { get; set; }
    [JsonRequired] public CoordinateData Target { get; set; } = new();
    public long ItemId { get; set; }
    public long ServiceId { get; set; }
    public MessageData? Message { get; set; }
    public long RecipeId { get; set; }
    public long AttemptId { get; set; }
    public RecordQueryData? Query { get; set; }
    public long Quantity { get; set; }
    public long OtherItemId { get; set; }
    public long TargetActorId { get; set; }
    public ConversationTopic ConversationTopic { get; set; }
}
public sealed class DisturbanceData
{
    public long ActorId { get; set; }
    public long ItemId { get; set; }
    public long? DirtUnits { get; set; }
    [JsonRequired] public string Type { get; set; } = "";
    [JsonRequired] public long Tick { get; set; }
    public CoordinateData? Tile { get; set; }
    public long TraversalTicks { get; set; }
    public long TerminalId { get; set; }
    public bool? Enabled { get; set; }
    public bool? Connected { get; set; }
    public long WorkServiceId { get; set; }
    public long NeedServiceId { get; set; } public long DetentionFacilityId { get; set; } public long DetentionAuthorizationId { get; set; }
    public bool Machine { get; set; }
    public long SensorId { get; set; }
    public RosterAssignmentData? Roster { get; set; }
    public string OperationKey { get; set; } = "";
}
public sealed class AssertionData
{
    public long EntityId { get; set; }
    [JsonRequired] public long Tick { get; set; }
    [JsonRequired] public string Kind { get; set; } = "";
    public long ActorId { get; set; }
    public long AccountId { get; set; }
    public long ItemId { get; set; }
    public CoordinateData? Position { get; set; }
    public FailureReason Reason { get; set; }
    public FactKeyData? Fact { get; set; }
    public FactValueData? Value { get; set; }
    public long MessageId { get; set; }
    public MessageStage Stage { get; set; }
    public long MachineId { get; set; }
    public bool Operational { get; set; }
    public LocationKind LocationKind { get; set; }
    public long Owner { get; set; }
    public long Quantity { get; set; }
    public long JobId { get; set; }
    public long AttemptId { get; set; }
    public string State { get; set; } = "";
    public RecordKind? RecordKind { get; set; }
    public long RecordEntityId { get; set; }
    public AttributeKind? RecordAttribute { get; set; }
}

public sealed class ScenarioException : Exception
{
    public ScenarioException(string file, string path, string message) : base($"{file}:{path}: {message}") { }
}

public static class JsonData
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)); return options;
    }
    public static T Read<T>(string json, string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); Check(doc.RootElement, "$");
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new ScenarioException(file, "$", "null document");
        }
        catch (JsonException e) { throw new ScenarioException(file, e.Path ?? "$", e.Message); }
        void Check(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new ScenarioException(file, path + "." + p.Name, "duplicate field"); Check(p.Value, path + "." + p.Name); }
            }
            else if (e.ValueKind == JsonValueKind.Array) { int index = 0; foreach (var value in e.EnumerateArray()) Check(value, $"{path}[{index++}]"); }
            else if (e.ValueKind == JsonValueKind.Null) throw new ScenarioException(file, path, "explicit null is not supported; omit optional fields");
        }
    }
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}

public sealed class LoadedScenario
{
    public ScenarioData Scenario { get; }
    public CatalogData Catalog { get; }
    public string ScenarioJson { get; }
    public string CatalogJson { get; }
    public string Source { get; }
    public string ContentDigest { get; }
    public LoadedScenario(string scenarioJson, string catalogJson, string source)
    {
        Source = source; ScenarioJson = scenarioJson; CatalogJson = catalogJson;
        Scenario = JsonData.Read<ScenarioData>(scenarioJson, source); Catalog = JsonData.Read<CatalogData>(catalogJson, source + " (catalog)");
        ContentDigest = World.Hash(System.Text.Encoding.UTF8.GetBytes(JsonData.Write(Catalog)));
        Validate();
    }
    public static LoadedScenario Load(string file)
    {
        string json = File.ReadAllText(file); var scenario = JsonData.Read<ScenarioData>(json, file);
        string catalog = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, scenario.InitialState.Catalog));
        return new LoadedScenario(json, File.ReadAllText(catalog), file);
    }
    private void Require(bool condition, string path, string message) { if (!condition) throw new ScenarioException(Source, path, message); }
    private void Validate()
    {
        var s = Scenario.InitialState;
        ResponseChains.Validate(Scenario, Source);
        Require(s.SchemaVersion == 1, "$.initialState.schemaVersion", "supported version is 1");
        Require(Catalog.SchemaVersion == 1, "catalog.schemaVersion", "supported version is 1");
        try { _ = new PerceptionCatalog(Catalog.Perception.Select(p => new PerceptionRule(p.Subject, p.Attribute, p.Mode))); }
        catch (ArgumentException e) { throw new ScenarioException(Source, "catalog.perception", e.Message); }
        Require(s.Id.Length > 0 && s.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'), "$.initialState.id", "use ASCII letters, numbers, underscore or hyphen");
        Require(s.Horizon >= 0, "$.initialState.horizon", "must be nonnegative");
        Require(Catalog.Tiles.Select(t => t.Family).Distinct().Count() == 12, "catalog.tiles", "all twelve families required");
        Require(Catalog.Tiles.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == Catalog.Tiles.Count, "catalog.tiles", "duplicate definition ID");
        for (int i = 0; i < Catalog.Tiles.Count; i++)
        {
            var d = Catalog.Tiles[i]; Require(d.Id.Length > 0 && d.TraversalTicks >= 1 && d.TraversalTicks <= long.MaxValue / 2, $"catalog.tiles[{i}]", "invalid ID or traversalTicks");
            if (d.Construction != null)
            {
                var c = d.Construction;
                Require(s.Work != null && Catalog.Recipes.Any(r => r.Id == c.RecipeId && r.Inputs.Count > 0 && r.Outputs.Count == 0), $"catalog.tiles[{i}].construction.recipeId", "construction requires a material-consuming recipe without item outputs and the work system");
                Require(c.Berths >= 0 && c.SleepTicks > 0 && c.AllocationTicks > 0, $"catalog.tiles[{i}].construction", "invalid berth count or timing");
            }
        }
        if (s.Growth != null)
        {
            try { s.Growth.ToSeed(); } catch (ArgumentException e) { throw new ScenarioException(Source, "$.initialState.growth", e.Message); }
            Require(s.Authority != null && s.JobSystem != null && s.Information != null && s.Work != null && (s.Construction == null || s.Construction.Projects.Count == 0) && s.Growth.KnownMaterials.All(m => Catalog.Resources.Any(r => r.Id == m.Resource)), "$.initialState.growth", "requires authority, jobs, information, work, declared materials and no separately seeded projects");
        }
        if (s.Construction != null)
        {
            Require(s.Construction.Projects.Select(p => p.Id).Distinct().Count() == s.Construction.Projects.Count && s.Construction.Projects.Select(p => p.Site.ToCoord()).Distinct().Count() == s.Construction.Projects.Count && s.Construction.Projects.Select(p => p.TileId).Distinct().Count() == s.Construction.Projects.Count,
                "$.initialState.construction.projects", "duplicate project, reserved site or output tile ID");
            for (int i = 0; i < s.Construction.Projects.Count; i++)
            {
                var p = s.Construction.Projects[i];
                Require(p.Id > 0 && p.TileId > 0 && Catalog.Tiles.Any(d => d.Id == p.DefinitionId && d.Construction != null) && !s.Tiles.Any(t => t.Id == p.TileId || t.Position.ToCoord() == p.Site.ToCoord()) && s.Tiles.Any(t => t.Completed && t.Position.ToCoord().Manhattan(p.Site.ToCoord()) == 1),
                    $"$.initialState.construction.projects[{i}]", "reservation requires a buildable definition and unique empty site adjacent to a completed tile");
            }
        }
        Require(s.Tiles.Select(t => t.Id).Distinct().Count() == s.Tiles.Count, "$.initialState.tiles", "duplicate tile ID");
        Require(s.Tiles.Select(t => t.Position.ToCoord()).Distinct().Count() == s.Tiles.Count, "$.initialState.tiles", "duplicate coordinate");
        Require(s.Actors.Select(a => a.Id).Distinct().Count() == s.Actors.Count, "$.initialState.actors", "duplicate actor ID");
        Require(s.Items.Select(i => i.Id).Distinct().Count() == s.Items.Count, "$.initialState.items", "duplicate item ID");
        for (int i = 0; i < s.Tiles.Count; i++)
        {
            var t = s.Tiles[i]; Require(t.Id > 0 && Catalog.Tiles.Any(d => d.Id == t.Definition), $"$.initialState.tiles[{i}]", "invalid ID or unknown definition");
            Require(!t.TraversalOverride.HasValue || t.TraversalOverride >= 1 && t.TraversalOverride <= long.MaxValue / 2, $"$.initialState.tiles[{i}].traversalOverride", "invalid duration");
        }
        for (int i = 0; i < Scenario.Disturbances.Count; i++)
        {
            var d = Scenario.Disturbances[i]; Require(d.Type is "CardLoss" or "TraversalChange" or "TerminalChange" or "WorkServiceChange" or "SensorChange" or "RosterChange" or "Spill" or "MachineFault" or "DetentionServiceChange" or "RevokeDetention" or "NeedServiceChange", $"$.disturbances[{i}].type", "unknown disturbance");
            Require(d.Tick >= 0 && d.Tick <= s.Horizon, $"$.disturbances[{i}].tick", "invalid tick");
            if(d.Type == "TraversalChange")
            {
                Require(d.TraversalTicks >= 1 && d.TraversalTicks <= long.MaxValue / 2, $"$.disturbances[{i}].traversalTicks", "invalid duration");
                Require(d.Tile != null && s.Tiles.Any(t => t.Completed && t.Position.ToCoord() == d.Tile.ToCoord()), $"$.disturbances[{i}].tile", "unknown completed tile");
            }
            else if (d.Type == "Spill") Require(s.Cleanliness != null && d.Tile != null && (!d.DirtUnits.HasValue || d.DirtUnits > 0) && s.Tiles.Any(t => t.Completed && t.Position.ToCoord() == d.Tile.ToCoord() && Catalog.Tiles.Single(def => def.Id == t.Definition).Capabilities.Contains("Cleanable")), $"$.disturbances[{i}]", "Spill requires cleanliness, a completed Cleanable tile and positive dirtUnits when supplied");
            else if (d.Type == "CardLoss") Require(s.Information != null && s.Information.Credentials.Any(c => c.Holder == d.ActorId && c.CardItem == d.ItemId), $"$.disturbances[{i}]", "CardLoss requires registered holder and card"); else if (d.Type == "MachineFault") Require(s.Work != null && s.Work.Machines.Any(m => m.Id == d.WorkServiceId), $"$.disturbances[{i}]", "MachineFault requires a known machine workServiceId");
            else if (d.Type == "TerminalChange") Require(s.Information != null && s.Information.Terminals.Any(t=>t.Id==d.TerminalId) && d.Enabled.HasValue && d.Connected.HasValue, $"$.disturbances[{i}]", "TerminalChange requires a known terminal, enabled and connected");
            else if (d.Type == "NeedServiceChange") Require(s.Needs != null && s.Needs.Services.Any(n => n.Id == d.NeedServiceId) && d.Enabled.HasValue, $"$.disturbances[{i}]", "NeedServiceChange requires a declared needServiceId and enabled"); else if (d.Type == "RevokeDetention") Require(s.Detention != null && d.DetentionAuthorizationId > 0 && !string.IsNullOrWhiteSpace(d.OperationKey), $"$.disturbances[{i}]", "RevokeDetention requires detentionAuthorizationId and operationKey"); else if (d.Type == "DetentionServiceChange") Require(s.Detention != null && s.Detention.Facilities.Any(f => f.Id == d.DetentionFacilityId) && d.Enabled.HasValue && d.Connected.HasValue, $"$.disturbances[{i}]", "DetentionServiceChange requires a declared detentionFacilityId, enabled and connected");
            else if (d.Type == "SensorChange") Require(s.Information != null && s.Information.Sensors.Any(sensor => sensor.Id == d.SensorId) && d.Enabled.HasValue, $"$.disturbances[{i}]", "SensorChange requires a known sensor and enabled");
            else if (d.Type == "RosterChange") Require(s.Routines != null && d.Roster != null && s.Routines.Assignments.Any(a => a.ActorId == d.Roster.ActorId) && !string.IsNullOrWhiteSpace(d.OperationKey), $"$.disturbances[{i}]", "RosterChange requires a registered routine actor, roster and operationKey");
            else Require(s.Work != null && d.Enabled.HasValue && (d.Machine ? s.Work.Machines.Any(m => m.Id == d.WorkServiceId) : s.Work.Workstations.Any(w => w.Id == d.WorkServiceId)), $"$.disturbances[{i}]", "WorkServiceChange requires a known service and enabled");
        }
        for (int i = 0; i < Scenario.ScriptedActors.Count; i++)
        {
            var script = Scenario.ScriptedActors[i]; var actor = s.Actors.SingleOrDefault(a => a.Id == script.ActorId);
            Require(actor != null, $"$.scriptedActors[{i}].actorId", "unknown actor");
            for (int j = 0; j < script.Commands.Count; j++)
            {
                var c = script.Commands[j]; string path = $"$.scriptedActors[{i}].commands[{j}]";
                Require(c.Tick >= 0 && c.Tick <= s.Horizon, path + ".tick", "must be within horizon");
                Require(c.Tick == 0 || actor!.Scripted, path, "post-tick-0 commands require a scripted actor");
                if(c.Kind is ActionKind.PickUp or ActionKind.PutDown or ActionKind.Inspect or ActionKind.Install or ActionKind.Equip) Require(s.Work == null ? s.Items.Any(x => x.Id == c.ItemId) : c.ItemId > 0, path + ".itemId", "unknown item or invalid produced item ID");
                if (c.Kind == ActionKind.ProduceRecipe) Require(s.Work != null && s.Work.Workstations.Any(w => w.Id == c.ServiceId) && Catalog.Recipes.Any(r => r.Id == c.RecipeId), path, "unknown workstation or recipe");
                if (c.Kind == ActionKind.InspectConstruction) Require(s.Growth != null && c.ServiceId > 0, path + ".serviceId", "survey requires a managed construction project"); if (c.Kind == ActionKind.Construct) Require((s.Growth != null && c.ServiceId > 0 || s.Construction != null && s.Construction.Projects.Any(p => p.Id == c.ServiceId)), path + ".serviceId", "unknown construction project");
                if (c.Kind is ActionKind.Install or ActionKind.FunctionalTest or ActionKind.InspectMachine) Require(s.Work != null && s.Work.Machines.Any(m => m.Id == c.ServiceId), path + ".serviceId", "unknown machine");
                if (c.Kind is ActionKind.SuspendWork or ActionKind.ResumeWork) Require(s.JobSystem != null && c.AttemptId > 0, path + ".attemptId", "positive attempt ID and job system required");
                if (c.Kind is ActionKind.SplitStack or ActionKind.MergeStacks or ActionKind.StoreInStash) Require(s.Work != null && c.ItemId > 0, path + ".itemId", "positive item ID and work system required");
                if (c.Kind == ActionKind.SplitStack) Require(c.Quantity > 0, path + ".quantity", "positive split quantity required");
                if (c.Kind == ActionKind.InspectActor) Require(c.TargetActorId > 0, path + ".targetActorId", "positive actor identity required");
                if (c.Kind == ActionKind.Converse) Require(s.Needs != null && c.TargetActorId != actor!.Id && s.Actors.Any(a => a.Id == c.TargetActorId), path + ".targetActorId", "Converse requires needs and a distinct registered partner");
                if (c.Kind == ActionKind.MergeStacks) Require(c.OtherItemId > 0 && c.OtherItemId != c.ItemId, path + ".otherItemId", "distinct positive source item required");
                if (c.Kind == ActionKind.StoreInStash) Require(s.Work != null && s.Work.Stashes.Any(x => x.Id == c.ServiceId), path + ".serviceId", "unknown stash");
                if(c.Kind is ActionKind.Login or ActionKind.RecoveryLogin or ActionKind.PostLostCard or ActionKind.ReadRecoveryInbox or ActionKind.IssueReplacement or ActionKind.ReadInbox or ActionKind.PostMessage or ActionKind.QueryRecords)
                    Require(s.Information != null && s.Information.Terminals.Any(t=>t.Id==c.ServiceId), path + ".serviceId", "unknown terminal");
                if (c.Kind is ActionKind.PostLostCard or ActionKind.IssueReplacement) Require(s.Information?.Recovery != null && c.ItemId > 0, path + ".itemId", "recovery commands require recovery configuration and a positive old card item ID"); if(c.Kind == ActionKind.PostMessage) Require(c.Message != null, path + ".message", "PostMessage requires typed message");
                if(c.Kind == ActionKind.QueryRecords) Require(c.Query != null, path + ".query", "QueryRecords requires typed query");
                if(c.Query != null)
                {
                    Require(c.Kind == ActionKind.QueryRecords, path + ".query", "query is only valid for QueryRecords");
                    try { c.Query.ToQuery(); } catch(ArgumentException e) { throw new ScenarioException(Source, path + ".query", e.Message); }
                }
                if(c.Message != null)
                {
                    Require(c.Kind==ActionKind.PostMessage,path+".message","message is only valid for PostMessage");
                    Require(c.Message.ObservedTick<=c.Tick,path+".message.observedTick","observation cannot be in the future");
                    Require(c.Message.RecipientKind==RecipientKind.Authority || s.Actors.Any(a=>(a.AccountId==0?a.Id:a.AccountId)==c.Message.RecipientAccount),path+".message.recipientAccount","unknown account");
                    try { c.Message.ToDraft(); } catch(ArgumentException e) { throw new ScenarioException(Source,path+".message",e.Message); }
                }
            }
        }
        for (int i = 0; i < Scenario.Assertions.Count; i++)
        {
            var a = Scenario.Assertions[i]; string path = $"$.assertions[{i}]";
            Require(a.Tick >= 0 && a.Tick <= s.Horizon, path + ".tick", "must be within horizon");
            Require(new[] { "ActorAt", "ActorInTransit", "ItemAtTile", "ItemHeld", "LastFailure", "Accounting", "KnowledgeValue", "KnowledgeAbsent", "BeliefValue", "BeliefAbsent", "MessageStage", "MachineOperational", "ItemState", "ItemAbsent", "JobPhysical", "JobAuthority", "AttemptExecution", "LocalOrder", "KnownRecordValue", "UnknownRecordValue", "Expectation", "Acknowledgment", "MissingReport", "WorkerStatus", "Dirt", "FacilityTask", "QualityAssurance", "Audit", "CaseFiling", "ReprimandCount", "DetentionAuthorizations", "LocalDetentionRequest", "EscortDispatch", "LocalGuardOrder", "Equipped", "LocalGuardTeam", "EscortGroup", "DetentionStay", "DetentionFacility", "QualifyingReprimands", "GuardOutcomeReceived", "KnownGuardRevocations", "NeedQ", "NeedService", "NeedActivities", "DailyGoals", "Conversations", "Sessions", "CredentialRecoveryCases", "RecoveryAgent", "Construction", "TileCompleted", "HousingBerths", "GrowthTarget", "GrowthBelievedBerths", "GrowthPendingBerths", "GrowthCommitments" }.Contains(a.Kind), path + ".kind", "unknown assertion");
            if (a.Kind.StartsWith("Growth", StringComparison.Ordinal)) Require(s.Growth != null && a.Quantity >= 0, path, "Growth assertion requires policy and nonnegative quantity"); if (a.Kind == "Construction") Require((s.Growth != null && a.EntityId > 0 || s.Construction != null && s.Construction.Projects.Any(p => p.Id == a.EntityId)) && Enum.TryParse<ConstructionStatus>(a.State, out var constructionStatus) && Enum.IsDefined(constructionStatus), path, "Construction requires a registered project and status"); if (a.Kind == "TileCompleted") Require(a.Position != null && a.State is "Absent" or "Completed", path, "TileCompleted requires position and Absent/Completed"); if (a.Kind == "HousingBerths") Require(a.Quantity >= 0, path, "HousingBerths requires nonnegative quantity"); if (a.Kind == "Dirt") Require(s.Cleanliness != null && a.Position != null && a.Quantity >= 0 && s.Tiles.Any(t => t.Completed && t.Position.ToCoord() == a.Position.ToCoord() && Catalog.Tiles.Single(def => def.Id == t.Definition).Capabilities.Contains("Cleanable")), path, "Dirt assertion requires cleanliness, a Cleanable position and nonnegative quantity");
            if (a.Kind is "Expectation" or "Acknowledgment" or "MissingReport" or "WorkerStatus") Require(s.Authority != null && a.AttemptId > 0, path, "authority and positive attempt ID required");
            if (a.Kind == "FacilityTask") Require(s.Facility != null && a.JobId > 0 && (a.State == "Absent" || Enum.TryParse<FacilityTaskStatus>(a.State, out var facilityStatus) && Enum.IsDefined(facilityStatus)), path, "FacilityTask requires facility policy, a positive job ID and valid state");
            if (a.Kind == "QualityAssurance") Require(s.QualityAssurance != null && a.AttemptId > 0 && (a.State == "Absent" || Enum.TryParse<QualityAssuranceStatus>(a.State, out var qualityStatus) && Enum.IsDefined(qualityStatus)), path, "QualityAssurance requires QA policy, a positive repair attempt ID and valid state");
            if (a.Kind == "Audit") Require(s.Actors.Any(x => x.Id == a.ActorId) && a.AttemptId > 0 && (a.State == "Absent" || Enum.TryParse<AuditStatus>(a.State, out var auditStatus) && Enum.IsDefined(auditStatus)), path, "Audit requires an actor, positive verification attempt ID and valid state");
            if (a.Kind == "CaseFiling") Require(s.Actors.Any(x => x.Id == a.ActorId) && a.AttemptId > 0 && (a.State == "Absent" || Enum.TryParse<CaseFilingStatus>(a.State, out var filingStatus) && Enum.IsDefined(filingStatus)), path, "CaseFiling requires an actor, positive verification attempt ID and valid state");
            if (a.Kind == "ReprimandCount") Require(a.AccountId > 0 && a.Quantity >= 0, path, "ReprimandCount requires a positive account ID and nonnegative quantity");
            if (a.Kind == "QualifyingReprimands") Require(a.AccountId > 0 && a.Quantity >= 0, path, "QualifyingReprimands requires account and count");
            if (a.Kind == "RecoveryAgent") Require(s.Information?.Recovery != null && s.Actors.Any(actor => actor.Id == a.ActorId) && (a.State is "Pending" or "Completed") && a.Quantity >= 0, path, "RecoveryAgent requires recovery, actor, Pending/Completed and count"); if (a.Kind == "CredentialRecoveryCases") Require(s.Information?.Recovery != null && a.Quantity >= 0 && (a.State is "Authorized" or "Issued") && (a.ActorId == 0 || s.Actors.Any(actor => actor.Id == a.ActorId)), path, "CredentialRecoveryCases requires recovery, a status and count"); if (a.Kind == "Sessions") Require(s.Information != null && a.Quantity >= 0 && (a.ActorId == 0 || s.Actors.Any(actor => actor.Id == a.ActorId)) && Enum.TryParse<SessionScope>(a.State, out var sessionScope) && Enum.IsDefined(sessionScope), path, "Sessions requires information, a valid scope and count"); if (a.Kind == "Conversations") Require(s.Needs != null && a.Quantity >= 0 && (a.ActorId == 0 || s.Actors.Any(actor => actor.Id == a.ActorId)) && Enum.TryParse<ConversationStatus>(a.State, out var conversationStatus) && Enum.IsDefined(conversationStatus), path, "Conversations requires a valid status and count"); if (a.Kind == "DailyGoals") Require(s.Needs != null && s.Routines != null && a.Quantity >= 0 && (a.ActorId == 0 || s.Actors.Any(actor => actor.Id == a.ActorId)) && Enum.TryParse<DailyGoalStatus>(a.State, out var dailyStatus) && Enum.IsDefined(dailyStatus), path, "DailyGoals requires enabled daily life, a valid status and count"); if (a.Kind == "NeedService") Require(s.Needs != null && s.Needs.Services.Any(n => n.Id == a.EntityId) && a.Quantity >= 0 && (a.State is "Queued" or "Occupants"), path, "NeedService requires a declared service ID, quantity and Queued/Occupants state"); if (a.Kind == "NeedActivities") Require(a.Quantity >= 0 && (a.ActorId == 0 || s.Actors.Any(actor => actor.Id == a.ActorId)) && Enum.TryParse<NeedActivityStatus>(a.State, out var needStatus) && Enum.IsDefined(needStatus), path, "NeedActivities requires a valid status and count"); if (a.Kind == "NeedQ") Require(s.Needs != null && s.Actors.Any(actor => actor.Id == a.ActorId) && a.Quantity >= 0 && a.Quantity <= NeedAccumulator.MaximumQ && Enum.TryParse<NeedKind>(a.State, out var needKind) && Enum.IsDefined(needKind), path, "NeedQ requires enabled needs, a registered actor, valid need state and bounded accumulator quantity"); if (a.Kind == "GuardOutcomeReceived") Require(a.Quantity >= 0 && a.ActorId >= 0 && new[] { "TargetNotLocated", "EscortInterrupted", "KnownOrderExpired", "AuthorizationRevoked", "EquipmentUnavailable", "RendezvousUnreachable", "SearchUnreachable" }.Contains(a.State), path, "GuardOutcomeReceived requires a valid outcome state and nonnegative count"); if (a.Kind == "KnownGuardRevocations") Require(a.Quantity >= 0 && a.ActorId >= 0, path, "KnownGuardRevocations requires a nonnegative count"); if (a.Kind == "DetentionStay") Require(a.EntityId > 0 && (a.State == "Absent" || Enum.TryParse<DetentionStayStatus>(a.State, out var stayState) && Enum.IsDefined(stayState)), path, "DetentionStay requires authorization ID and valid state");
            if (a.Kind == "DetentionFacility") Require(a.EntityId > 0 && a.Quantity >= 0 && (a.State is "Reservations" or "Occupants" or "Operations"), path, "DetentionFacility requires facility ID, count and Reservations/Occupants/Operations");
            if (a.Kind == "EscortGroup") Require(a.EntityId > 0 && a.Quantity >= 0 && (a.State == "Absent" || Enum.TryParse<EscortStatus>(a.State, out _)), path, "EscortGroup requires authorization ID, member quantity and valid state");
            if (a.Kind == "LocalGuardTeam") Require(a.EntityId > 0 && a.Quantity >= 0 && (a.State is "Absent" or "AwaitingMembers" or "Formed"), path, "LocalGuardTeam requires authorization entity ID, member quantity and valid state");
            if (a.Kind == "Equipped") Require(s.Actors.Any(x => x.Id == a.ActorId) && a.ItemId >= 0, path, "Equipped requires actor and nonnegative item ID (zero means unequipped)");
            if (a.Kind == "EscortDispatch") Require(a.EntityId > 0 && Enum.TryParse<EscortDispatchStatus>(a.State, out var escortState) && Enum.IsDefined(escortState), path, "EscortDispatch requires authorization entity ID and valid state");
            if (a.Kind == "LocalGuardOrder") Require(s.Actors.Any(x => x.Id == a.ActorId) && (a.State == "Absent" || Enum.TryParse<GuardOrderStatus>(a.State, out var guardState) && Enum.IsDefined(guardState)), path, "LocalGuardOrder requires actor and valid state");
            if (a.Kind == "DetentionAuthorizations") Require(a.AccountId > 0 && a.Quantity >= 0 && (a.State.Length == 0 || Enum.TryParse<DetentionAuthorizationStatus>(a.State, out var authorizationState) && Enum.IsDefined(authorizationState)), path, "DetentionAuthorizations requires an account, nonnegative quantity and optional valid state");
            if (a.Kind == "LocalDetentionRequest") Require(s.Actors.Any(x => x.Id == a.ActorId) && a.AccountId > 0 && (a.State == "Absent" || Enum.TryParse<DetentionRequestStatus>(a.State, out var requestState) && Enum.IsDefined(requestState)), path, "LocalDetentionRequest requires actor/account IDs and a valid state");
            if (a.Kind == "Expectation") Require(a.State == "Absent" || Enum.TryParse<ExpectationStatus>(a.State, out var expectation) && Enum.IsDefined(expectation), path + ".state", "unknown expectation state");
            if (a.Kind == "Acknowledgment") Require(Enum.TryParse<AcknowledgmentStatus>(a.State, out var acknowledgment) && Enum.IsDefined(acknowledgment), path + ".state", "unknown acknowledgment state");
            if (a.Kind == "WorkerStatus") Require(a.State is "PendingPatrol" or "PatrolPosted" or "ReportedFound" or "ReportedNotLocated" or "ReportedBlocked", path + ".state", "unknown worker-status response");
            if (a.Kind == "MissingReport") Require(a.State is "Present" or "Absent", path + ".state", "use Present or Absent");
            if (a.Kind is "KnownRecordValue" or "UnknownRecordValue") Require(s.Actors.Any(x => x.Id == a.ActorId) && a.RecordKind.HasValue && Enum.IsDefined(a.RecordKind.Value) && a.RecordEntityId > 0 && a.RecordAttribute.HasValue && Enum.IsDefined(a.RecordAttribute.Value) && a.Value != null, path, "record assertion requires actor, recordKind, recordEntityId, recordAttribute and value");
            if (a.Kind is "JobPhysical" or "JobAuthority") Require(s.JobSystem != null && s.JobSystem.Jobs.Any(j => j.Id == a.JobId), path + ".jobId", "unknown job");
            if (a.Kind is "AttemptExecution" or "LocalOrder") Require(s.JobSystem != null && a.AttemptId > 0, path + ".attemptId", "positive attempt ID and job system required");
            if (a.Kind is "JobPhysical" or "AttemptExecution") Require(Enum.TryParse<WorkExecution>(a.State, out var execution) && Enum.IsDefined(execution), path + ".state", "unknown execution state");
            if (a.Kind == "LocalOrder") Require(s.Actors.Any(x => x.Id == a.ActorId) && Enum.TryParse<LocalOrderStatus>(a.State, out var local) && Enum.IsDefined(local), path, "unknown actor or local order state");
            if (a.Kind is "ActorAt" or "ActorInTransit" or "ItemHeld" or "LastFailure" or "KnowledgeValue" or "KnowledgeAbsent") Require(s.Actors.Any(x => x.Id == a.ActorId), path + ".actorId", "unknown actor");
            if (a.Kind is "ItemAtTile" or "ItemHeld") Require(s.Work == null ? s.Items.Any(x => x.Id == a.ItemId) : a.ItemId > 0, path + ".itemId", "unknown item");
            if (a.Kind is "ItemState" or "ItemAbsent") Require(a.ItemId > 0, path + ".itemId", "positive ID required");
            if (a.Kind == "ItemState") Require(a.Quantity > 0 && a.Owner > 0, path, "positive quantity and owner required");
            if (a.Kind == "MachineOperational") Require(s.Work != null && s.Work.Machines.Any(m => m.Id == a.MachineId), path + ".machineId", "unknown machine");
            if (a.Kind is "ActorAt" or "ItemAtTile") Require(a.Position != null, path + ".position", "required");
            if(a.Kind is "KnowledgeValue" or "KnowledgeAbsent" or "BeliefValue" or "BeliefAbsent") Require(a.Fact != null,path+".fact","required");
            if(a.Kind is "KnowledgeValue" or "BeliefValue") Require(a.Value != null,path+".value","required");
            try { a.Fact?.ToKey(); a.Value?.ToValue(); } catch(ArgumentException e) { throw new ScenarioException(Source,path,e.Message); }
        }
        Require(Scenario.Metrics.All(m => new[] { "movement", "actions", "resources", "information", "terminals" }.Contains(m)), "$.metrics", "unknown metric");
        try { CreateWorld(); } catch (ArgumentException e) { throw new ScenarioException(Source, "$.initialState", e.Message); }
    }
    public World CreateWorld(ulong? seed = null, bool trace = false)
    {
        var s = Scenario.InitialState;
        var world = new World(s.Id, seed ?? s.Seed, s.Configuration.ToConfig(),
            Catalog.Tiles.Select(d => new TileDefinition(d.Id, d.Family, d.TraversalTicks, d.VisualKey, d.Capabilities, d.Construction?.ToDefinition())),
            s.Tiles.Select(t => new Tile(new Id<TileTag>(t.Id), t.Definition, t.Position.ToCoord(), t.Completed, t.TraversalOverride)),
            s.Actors.Select(a => new ActorSeed(new Id<ActorTag>(a.Id), a.Name, a.Role, a.Position.ToCoord(),new Id<AccountTag>(a.AccountId))),
            s.Items.Select(i => new Item(new Id<ItemTag>(i.Id), i.Resource, i.Quantity, new ItemLocation(i.LocationKind, i.Owner))), trace, s.Information?.ToSeed(Catalog.Perception, s.Actors.Where(a => !a.Scripted).Select(a => a.Id)), s.Work?.ToSeed(Catalog), s.JobSystem?.ToSeed(), s.Routines?.ToSeed(), s.Authority?.ToConfig(), s.Cleanliness?.ToSeed(), s.Facility?.ToConfig(), s.MachineWear?.ToConfig(), s.QualityAssurance?.ToConfig(), s.Personnel?.ToConfig(), s.Detention?.ToPolicy(), s.Needs?.ToSeed(s.Actors.Where(a => a.AutonomousNeeds ?? !a.Scripted).Select(a => a.Id)), s.Construction?.ToSeed(), s.Growth?.ToSeed());
        foreach (var d in Scenario.Disturbances)
        {
            if(d.Type=="TraversalChange")world.ScheduleTraversalChange(d.Tick, d.Tile!.ToCoord(), d.TraversalTicks);
            else if (d.Type == "CardLoss") world.ScheduleCardLoss(d.Tick, new(d.ActorId), new(d.ItemId)); else if (d.Type == "MachineFault") world.ScheduleMachineFault(d.Tick, d.WorkServiceId);
            else if (d.Type == "Spill") world.ScheduleSpill(d.Tick, d.Tile!.ToCoord(), d.DirtUnits);
            else if (d.Type == "TerminalChange") world.ScheduleTerminalChange(d.Tick,d.TerminalId,d.Enabled!.Value,d.Connected!.Value);
            else if (d.Type == "NeedServiceChange") world.ScheduleNeedServiceChange(d.Tick, d.NeedServiceId, d.Enabled!.Value); else if (d.Type == "RevokeDetention") world.ScheduleDetentionRevocation(d.Tick, d.DetentionAuthorizationId, d.OperationKey); else if (d.Type == "DetentionServiceChange") world.ScheduleDetentionServiceChange(d.Tick, d.DetentionFacilityId, d.Enabled!.Value, d.Connected!.Value);
            else if (d.Type == "SensorChange") world.ScheduleSensorChange(d.Tick, d.SensorId, d.Enabled!.Value);
            else if (d.Type == "RosterChange") world.ScheduleRosterChange(d.Tick, d.Roster!.ToSeed(), d.OperationKey);
            else world.ScheduleWorkServiceChange(d.Tick, d.WorkServiceId, d.Machine, d.Enabled!.Value);
        }
        foreach (var actor in Scenario.ScriptedActors)
        foreach (var c in actor.Commands) world.Submit(c.Tick, new ActionIntent(new Id<ActorTag>(actor.ActorId), c.Kind, c.Target.ToCoord(), new Id<ItemTag>(c.ItemId), c.ServiceId,c.Message?.ToDraft(), c.RecipeId, c.AttemptId, c.Query?.ToQuery(), c.Quantity, new Id<ItemTag>(c.OtherItemId), new Id<ActorTag>(c.TargetActorId), conversationTopic: c.ConversationTopic));
        return world;
    }
}
