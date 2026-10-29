using System.Text.Json.Serialization;
using Station.Contracts;

namespace Station.Runner;

public sealed class NeedRuleData
{
    [JsonRequired] public NeedKind Kind { get; set; }
    [JsonRequired] public int AwakeRate { get; set; }
    [JsonRequired] public int WorkingRate { get; set; }
    [JsonRequired] public int SleepingRate { get; set; }
    [JsonRequired] public int RestingRate { get; set; }
    public int Seek { get; set; }
    public int Urgent { get; set; }
    public int Critical { get; set; }
    public NeedRule ToRule() => new(Kind, AwakeRate, WorkingRate, SleepingRate, RestingRate, Seek, Urgent, Critical);
}
public sealed class NeedInitialData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public NeedKind Kind { get; set; }
    [JsonRequired] public long Q { get; set; }
    public NeedInitialValue ToValue() => new(new(ActorId), Kind, Q);
}
public sealed class NeedsData
{
    public List<NeedRuleData>? Rules { get; set; }
    public List<NeedInitialData> Initial { get; set; } = new();
    public List<NeedServiceData> Services { get; set; } = new();
    public ConversationData Conversations { get; set; } = new();
    public NeedSeed ToSeed(IEnumerable<long>? autonomousActors = null) => new(new NeedConfig(Rules?.Select(r => r.ToRule())), Initial.Select(i => i.ToValue()), Services.Select(s => s.ToDefinition()), autonomousActors, Conversations.ToConfig());
}
public sealed class ConversationData
{
    public long DurationTicks { get; set; } = 1800;
    public int PartnerCandidates { get; set; } = 4;
    public int FactsPerSpeaker { get; set; } = 4;
    public List<long> ExplicitConsentActors { get; set; } = new();
    public ConversationConfig ToConfig() => new(DurationTicks, PartnerCandidates, FactsPerSpeaker, ExplicitConsentActors);
}
public sealed class NeedServiceData
{
    [JsonRequired] public long Id { get; set; }
    [JsonRequired] public NeedServiceKind Kind { get; set; }
    [JsonRequired] public CoordinateData Position { get; set; } = new();
    public int Capacity { get; set; } = 1;
    public long DurationTicks { get; set; }
    public string MealResource { get; set; } = "meal";
    public long AssignedActorId { get; set; }
    public long DetentionFacilityId { get; set; }
    public bool Enabled { get; set; } = true;
    public long AllocationTicks { get; set; } = 100;
    public bool AllocationRequiresCredential { get; set; } = true;
    public NeedServiceDefinition ToDefinition() => new(Id, Kind, Position.ToCoord(), Capacity, DurationTicks, MealResource, AssignedActorId, DetentionFacilityId, Enabled, AllocationTicks, AllocationRequiresCredential);
}
