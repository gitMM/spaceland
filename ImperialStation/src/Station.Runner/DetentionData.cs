using System.Text.Json.Serialization;
using Station.Contracts;

namespace Station.Runner;

public sealed class DetentionData
{
    public long SentenceTicks { get; set; } = 864000;
    public long OrderTicks { get; set; } = 1728000;
    public EscortDispatchData? Escort { get; set; }
    public List<DetentionFacilityData> Facilities { get; set; } = new();
    public DetentionPolicy ToPolicy() => new(SentenceTicks, OrderTicks, Escort?.ToPolicy(), Facilities.Select(f => f.ToDefinition()));
}
public sealed class DetentionFacilityData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public CoordinateData Position { get; set; } = new();
    [JsonRequired] public int Capacity { get; set; }
    public int ServiceCapacity { get; set; } = 1;
    public long IntakeTicks { get; set; } = 300;
    public long ReleaseTicks { get; set; } = 300;
    public bool Powered { get; set; } = true;
    public bool Connected { get; set; } = true;
    public DetentionFacility ToDefinition() => new(Id, Position.ToCoord(), Capacity, ServiceCapacity, IntakeTicks, ReleaseTicks, Powered, Connected);
}
public sealed class EscortDispatchData
{
    [JsonRequired] public long FacilityId { get; set; }
    [JsonRequired] public CoordinateData Destination { get; set; } = new();
    [JsonRequired] public CoordinateData Rendezvous { get; set; } = new();
    [JsonRequired] public string RequiredEquipment { get; set; } = "";
    public string GuardRole { get; set; } = "guard";
    public int GuardCount { get; set; } = 2;
    public long EquipTicks { get; set; } = 50;
    public long MinimumRemainingDutyTicks { get; set; }
    public long AcknowledgmentTicks { get; set; }
    public EscortDispatchPolicy ToPolicy() => new(FacilityId, Destination.ToCoord(), Rendezvous.ToCoord(), RequiredEquipment, GuardRole, GuardCount, EquipTicks, MinimumRemainingDutyTicks, AcknowledgmentTicks);
}
public sealed class DetentionRequestData
{
    [JsonRequired] public long AccountId { get; set; }
    [JsonRequired] public long CaseId { get; set; }
    [JsonRequired] public long CaseReplyMessageId { get; set; }
    public DetentionRequestPayload ToPayload() => new(AccountId, CaseId, CaseReplyMessageId);
}
