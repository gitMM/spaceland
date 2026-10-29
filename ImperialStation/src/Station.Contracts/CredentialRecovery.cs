using System;
using System.Collections.Generic;

namespace Station.Contracts
{
    public sealed class PublicRecoveryDesk
    {
        public long TerminalId { get; }
        public Coord Position { get; }
        public long IssuanceTicks { get; }
        public PublicRecoveryDesk(long terminalId, Coord position, long issuanceTicks)
        { if (terminalId < 1 || issuanceTicks < 1) throw new ArgumentException("Invalid public recovery desk."); TerminalId = terminalId; Position = position; IssuanceTicks = issuanceTicks; }
    }
    public sealed class RecoveryPlanningState
    {
        public long OldCredentialId { get; }
        public long OldCardId { get; }
        public bool HasAlternateCapability { get; }
        public bool ReportPosted { get; }
        public bool AuthorizationRead { get; }
        public long IssuedCardId { get; }
        public long RestrictedTerminalId { get; }
        public IReadOnlyList<long> UnavailableTerminals { get; }
        public RecoveryPlanningState(long oldCredentialId, long oldCardId, bool hasAlternateCapability, bool reportPosted, bool authorizationRead, long issuedCardId, long restrictedTerminalId, IReadOnlyList<long> unavailableTerminals)
        { OldCredentialId = oldCredentialId; OldCardId = oldCardId; HasAlternateCapability = hasAlternateCapability; ReportPosted = reportPosted; AuthorizationRead = authorizationRead; IssuedCardId = issuedCardId; RestrictedTerminalId = restrictedTerminalId; UnavailableTerminals = unavailableTerminals; }
    }
    public sealed class RecoveryReplyPayload
    {
        public long CaseId { get; }
        public long OldCredentialId { get; }
        public long IssuedCardId { get; }
        public long IssuedCredentialId { get; }
        public long IssuedTick { get; }
        public Coord OutputPosition { get; }
        public RecoveryReplyPayload(long caseId, long oldCredentialId, long issuedCardId = 0, long issuedCredentialId = 0, long issuedTick = -1, Coord outputPosition = default)
        {
            if (caseId < 1 || oldCredentialId < 1 || issuedCardId < 0 || issuedCredentialId < 0 || (issuedCardId == 0 ? issuedCredentialId != 0 || issuedTick != -1 : issuedCredentialId == 0 || issuedTick < 0))
                throw new ArgumentException("Invalid recovery reply identifiers or issuance receipt.");
            CaseId = caseId; OldCredentialId = oldCredentialId; IssuedCardId = issuedCardId; IssuedCredentialId = issuedCredentialId; IssuedTick = issuedTick; OutputPosition = outputPosition;
        }
    }
}
