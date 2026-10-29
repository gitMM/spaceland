using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Core
{
    // Account identity is the employee ID in this simulation. This capability is
    // modeled data, never a password, token or real-world authentication secret.
    public sealed class RecoveryCapabilitySeed
    {
        public long ActorId { get; }
        public long AccountId { get; }
        public bool Retained { get; }
        public RecoveryCapabilitySeed(long actorId, long accountId, bool retained = true)
        { ActorId = actorId; AccountId = accountId; Retained = retained; }
    }

    public sealed class CredentialRecoverySeed
    {
        public long IdentityCheckTicks { get; }
        public IReadOnlyList<RecoveryCapabilitySeed> Capabilities { get; }
        public IReadOnlyList<RecoveryDeskSeed> Desks { get; }
        public IReadOnlyList<long>? AutonomousActors { get; }
        public CredentialRecoverySeed(IEnumerable<RecoveryCapabilitySeed> capabilities, long identityCheckTicks = 300, IEnumerable<RecoveryDeskSeed>? desks = null, IEnumerable<long>? autonomousActors = null)
        {
            if (identityCheckTicks < 1) throw new ArgumentException("Recovery identity check must take positive ticks.");
            IdentityCheckTicks = identityCheckTicks;
            Capabilities = Array.AsReadOnly(capabilities.OrderBy(c => c.ActorId).ToArray());
            Desks = Array.AsReadOnly((desks ?? Array.Empty<RecoveryDeskSeed>()).OrderBy(d => d.TerminalId).ToArray());
            AutonomousActors = autonomousActors == null ? null : Array.AsReadOnly(autonomousActors.OrderBy(id => id).ToArray());
        }
    }

    public sealed class RecoveryDeskSeed
    {
        public long TerminalId { get; }
        public long WorkstationId { get; }
        public long RecipeId { get; }
        public RecoveryDeskSeed(long terminalId, long workstationId, long recipeId)
        { TerminalId = terminalId; WorkstationId = workstationId; RecipeId = recipeId; }
    }

    public sealed class CredentialRecoveryCaseSnapshot
    {
        public long Id { get; internal set; }
        public long ActorId { get; internal set; }
        public long AccountId { get; internal set; }
        public long OldCredentialId { get; internal set; }
        public long FirstRequestMessageId { get; internal set; }
        public long AcceptedTick { get; internal set; }
        public long IssuedCardId { get; internal set; }
        public long IssuedCredentialId { get; internal set; }
        public long IssuedTick { get; internal set; } = -1;
        public long IssueActionId { get; internal set; }
        public long IssueTerminalId { get; internal set; }
        public long IssueRecipeId { get; internal set; }
        public long RecordVersion { get; internal set; }
        internal CredentialRecoveryCaseSnapshot Copy() => (CredentialRecoveryCaseSnapshot)MemberwiseClone();
    }
}
