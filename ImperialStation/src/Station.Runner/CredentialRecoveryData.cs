using System.Text.Json.Serialization;
using Station.Core;

namespace Station.Runner;

public sealed class CredentialRecoveryData
{
    public long IdentityCheckTicks { get; set; } = 300;
    public List<RecoveryCapabilityData> Capabilities { get; set; } = new();
    public List<RecoveryDeskData> Desks { get; set; } = new();
    public CredentialRecoverySeed ToSeed(IEnumerable<long>? autonomousActors = null) => new(Capabilities.Select(c => new RecoveryCapabilitySeed(c.ActorId, c.AccountId, c.Retained)), IdentityCheckTicks, Desks.Select(d => new RecoveryDeskSeed(d.TerminalId, d.WorkstationId, d.RecipeId)), autonomousActors);
}

public sealed class RecoveryDeskData
{
    [JsonRequired] public long TerminalId { get; set; }
    [JsonRequired] public long WorkstationId { get; set; }
    [JsonRequired] public long RecipeId { get; set; }
}

public sealed class RecoveryCapabilityData
{
    [JsonRequired] public long ActorId { get; set; }
    [JsonRequired] public long AccountId { get; set; }
    public bool Retained { get; set; } = true;
}
