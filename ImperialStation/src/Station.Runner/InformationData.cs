using System.Text.Json.Serialization;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public sealed class FactKeyData
{
    [JsonRequired] public SubjectKind SubjectKind { get; set; }
    [JsonRequired] public long SubjectId { get; set; }
    [JsonRequired] public AttributeKind Attribute { get; set; }
    public FactKey ToKey() => new(SubjectKind,SubjectId,Attribute);
}
public sealed class FactValueData
{
    [JsonRequired] public FactValueKind Kind { get; set; }
    public long Number { get; set; }
    public CoordinateData? Coordinate { get; set; }
    public string Text { get; set; } = "";
    public FactValue ToValue()
    {
        if (Kind is FactValueKind.Coordinate or FactValueKind.AbsentAt && Coordinate == null) throw new ArgumentException("Coordinate/AbsentAt facts require a coordinate.");
        if (Kind == FactValueKind.Boolean && Number is not (0 or 1)) throw new ArgumentException("Boolean fact number must be zero or one.");
        return new(Kind,Number,Coordinate?.ToCoord() ?? default,Text);
    }
}
public sealed class MessageData
{
    [JsonRequired] public MessageKind Kind { get; set; }
    [JsonRequired] public RecipientKind RecipientKind { get; set; }
    public long RecipientAccount { get; set; }
    [JsonRequired] public FactKeyData Subject { get; set; } = new();
    [JsonRequired] public FactValueData Claim { get; set; } = new();
    [JsonRequired] public long ObservedTick { get; set; }
    public long TaskId { get; set; }
    public long AttemptId { get; set; }
    public long RelatedMessageId { get; set; }
    public WorkOrderData? WorkOrder { get; set; }
    public string OperationKey { get; set; } = "";
    public InspectionReportData? InspectionReport { get; set; }
    public CaseSubmissionData? CaseSubmission { get; set; }
    public DetentionRequestData? DetentionRequest { get; set; }
    public MessageDraft ToDraft() => new(Kind,RecipientKind,new(RecipientAccount),Subject.ToKey(),Claim.ToValue(),ObservedTick,TaskId,AttemptId,RelatedMessageId,WorkOrder?.ToPayload(),OperationKey,
        inspectionReport: InspectionReport?.ToPayload(), caseSubmission: CaseSubmission?.ToPayload(), detentionRequest: DetentionRequest?.ToPayload());
}
public sealed class WorkOrderData
{
    public ConstructionOrderData? Construction { get; set; }
    public long MachineId { get; set; }
    public WorkKind Kind { get; set; }
    public long TileId { get; set; }
    [JsonRequired] public long Expires { get; set; }
    public WorkOrderPayload ToPayload()
    {
        if (Kind == WorkKind.ConstructHabitat && Construction != null && MachineId == 0 && TileId == 0) return new(Construction.ToOrder(), Expires);
        if (Construction != null) throw new ArgumentException("Construction specification requires a construction order.");
        if ((Kind is WorkKind.RepairMachine or WorkKind.VerifyMachine) && TileId == 0) return new(MachineId, Expires, Kind);
        if (Kind == WorkKind.CleanTile && MachineId == 0) return new(new Id<TileTag>(TileId), Expires);
        throw new ArgumentException("Order kind and target fields disagree.");
    }
}
public sealed class TerminalData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public CoordinateData Position { get; set; } = new();
    [JsonRequired] public List<string> PermittedRoles { get; set; } = new();
    public int Capacity { get; set; } = 1;
    public TerminalCapability Capabilities { get; set; } = TerminalCapability.Inbox | TerminalCapability.Post;
    public bool Enabled { get; set; } = true;
    public bool Connected { get; set; } = true;
}
public sealed class CredentialData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public long Holder { get; set; }
    [JsonRequired] public long Account { get; set; }
    [JsonRequired] public long CardItem { get; set; }
    public bool Active { get; set; } = true;
    public int Generation { get; set; } = 1;
}
public sealed class InitialFactData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public FactKeyData Key { get; set; } = new();
    [JsonRequired] public FactValueData Value { get; set; } = new();
    public bool Pinned { get; set; }
}
public sealed class InitialMessageData
{
    [JsonRequired] public MessageData Message { get; set; } = new();
    [JsonRequired] public long SenderAccount { get; set; }
    public long DeliveryTick { get; set; }
}
public sealed class InformationConfigurationData
{
    public long LoginTicks { get; set; } = 30;
    public long ReadBaseTicks { get; set; } = 40;
    public long ReadPerMessageTicks { get; set; } = 10;
    public long ReadMaximumTicks { get; set; } = 200;
    public long PostTicks { get; set; } = 60;
    public long LatencyTicks { get; set; } = 2;
    public long SessionTicks { get; set; } = 1200;
    public long InspectTicks { get; set; } = 100;
    public int KnowledgeCapacity { get; set; } = 256;
    public long QueryTicks { get; set; } = 150;
    public InformationConfig ToConfig() => new(LoginTicks,ReadBaseTicks,ReadPerMessageTicks,ReadMaximumTicks,PostTicks,LatencyTicks,SessionTicks,InspectTicks,KnowledgeCapacity,QueryTicks);
}
public sealed class InformationData
{
    public InformationConfigurationData Configuration { get; set; } = new();
    public List<TerminalData> Terminals { get; set; } = new();
    public List<CredentialData> Credentials { get; set; } = new();
    public List<InitialFactData> Facts { get; set; } = new();
    public List<InitialMessageData> Messages { get; set; } = new();
    public List<ReportGoalData> ReportGoals { get; set; } = new();
    public List<RepairGoalData> RepairGoals { get; set; } = new();
    public List<RecordAccessData> RecordAccess { get; set; } = new();
    public List<MachineSensorData> Sensors { get; set; } = new();
    public PlannerConfigurationData Planner { get; set; } = new();
    public CredentialRecoveryData? Recovery { get; set; }
    public InformationSeed ToSeed(IEnumerable<PerceptionRuleData>? perception = null, IEnumerable<long>? autonomousRecoveryActors = null) => new(Configuration.ToConfig(),
        Terminals.Select(t=>new TerminalSeed(t.Id,t.Position.ToCoord(),t.PermittedRoles,t.Capacity,t.Capabilities,t.Enabled,t.Connected)),
        Credentials.Select(c=>new CredentialSeed(c.Id,new(c.Holder),new(c.Account),new(c.CardItem),c.Active,c.Generation)),
        Facts.Select(f=>new FactSeed(new(f.ActorId),f.Key.ToKey(),f.Value.ToValue(),f.Pinned)),
        Messages.Select(m=>new InitialMessage(m.Message.ToDraft(),new(m.SenderAccount),m.DeliveryTick)),
        ReportGoals.Select(g=>new ReportGoalSeed(new(g.ActorId),g.Report.ToDraft())),Planner.ToConfig(), repairGoals: RepairGoals.Select(g => new RepairGoalSeed(new(g.ActorId), g.MachineId)),
        recordAccess: RecordAccess.Select(g => new RecordAccessSeed(new(g.ActorId), g.TerminalId, g.RequiredRole, g.Kind)),
        perception: new PerceptionCatalog(perception?.Select(p => new PerceptionRule(p.Subject, p.Attribute, p.Mode))), sensors: Sensors.Select(s => new MachineSensorSeed(s.Id, s.MachineId, s.FirstTick, s.Interval, s.Latency, s.Enabled, s.Channel)), recovery: Recovery?.ToSeed(autonomousRecoveryActors));
}
public sealed class PerceptionRuleData
{
    [JsonRequired] public SubjectKind Subject { get; set; }
    [JsonRequired] public AttributeKind Attribute { get; set; }
    [JsonRequired] public PerceptionMode Mode { get; set; }
}
public sealed class MachineSensorData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public long MachineId { get; set; }
    [JsonRequired] public long FirstTick { get; set; }
    [JsonRequired] public long Interval { get; set; }
    public long Latency { get; set; } = 2;
    public bool Enabled { get; set; } = true;
    public SensorChannel Channel { get; set; } = SensorChannel.Authority;
}
public sealed class RecordAccessData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public long TerminalId { get; set; }
    [JsonRequired] public string RequiredRole { get; set; } = "";
    [JsonRequired] public RecordKind Kind { get; set; }
}
public sealed class RecordQueryData
{
    [JsonRequired] public RecordKind Kind { get; set; }
    public long EntityId { get; set; }
    public long After { get; set; }
    public int Limit { get; set; } = 16;
    public bool History { get; set; }
    public long MachineId { get; set; }
    public RecordQuery ToQuery() => new(Kind, EntityId, After, Limit, History, MachineId);
}
public sealed class RepairGoalData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public long MachineId { get; set; }
}
public sealed class ReportGoalData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public MessageData Report { get; set; } = new();
}
public sealed class PlannerConfigurationData
{
    public int MaximumDepth { get; set; }=8;
    public int NodeBudget { get; set; }=512;
    public int Candidates { get; set; }=3;
    public int RequestsPerTick { get; set; }=64;
    public long InitialBackoff { get; set; }=50;
    public long MaximumBackoff { get; set; }=1000;
    public PlannerConfig ToConfig()=>new(MaximumDepth,NodeBudget,Candidates,RequestsPerTick,InitialBackoff,MaximumBackoff);
}
