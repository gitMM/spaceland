using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteRecoveryAgents(BinaryWriter w)
        {
            w.Write(recoveryAgents.Count);
            foreach (var a in recoveryAgents.Values)
            {
                w.Write(a.ActorId); w.Write(a.Pending); w.Write(a.DiscoveredTick); w.Write(a.OldCredentialId); w.Write(a.OldCardId); w.Write(a.PostedTick); w.Write(a.KnownCaseId); w.Write(a.IssuedCardId);
                w.Write(a.NextDecision); w.Write(a.RetryAt); w.Write(a.Backoff); w.Write(a.ActiveActionId); w.Write(a.PlannedTick); w.Write(a.PlannedTerminal); w.Write((int)a.PlannedKind); w.Write(a.UrgentCredentialBlocked); w.Write(a.RetainsAlternateIdentity);
                w.Write(a.Completed); w.Write(a.DiscoveryCauseId); w.Write(a.CompletionCauseId); w.Write(a.Reason); w.Write((int)a.LastOutcome); w.Write(a.LastCost); w.Write(a.TerminalRetry.Count);
                foreach (var pair in a.TerminalRetry) { w.Write(pair.Key); w.Write(pair.Value); }
            }
            w.Write(cardLossCauses.Count); foreach (var p in cardLossCauses) { w.Write(p.Key); w.Write(p.Value); }
        }
        private void ReadRecoveryAgents(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new RecoveryAgentSnapshot { ActorId = r.ReadInt64(), Pending = r.ReadBoolean(), DiscoveredTick = r.ReadInt64(), OldCredentialId = r.ReadInt64(), OldCardId = r.ReadInt64(), PostedTick = r.ReadInt64(), KnownCaseId = r.ReadInt64(), IssuedCardId = r.ReadInt64(),
                    NextDecision = r.ReadInt64(), RetryAt = r.ReadInt64(), Backoff = r.ReadInt64(), ActiveActionId = r.ReadInt64(), PlannedTick = r.ReadInt64(), PlannedTerminal = r.ReadInt64(), PlannedKind = ReadEnum<ActionKind>(r), UrgentCredentialBlocked = r.ReadBoolean(), RetainsAlternateIdentity = r.ReadBoolean(),
                    Completed = r.ReadInt64(), DiscoveryCauseId = r.ReadInt64(), CompletionCauseId = r.ReadInt64(), Reason = r.ReadString(), LastOutcome = ReadEnum<PlanOutcome>(r), LastCost = r.ReadInt64() };
                int n = Count(r); for (int j = 0; j < n; j++) a.TerminalRetry.Add(r.ReadInt64(), r.ReadInt64()); recoveryAgents.Add(a.ActorId, a);
            }
            count = Count(r); for (int i = 0; i < count; i++) cardLossCauses.Add(r.ReadInt64(), r.ReadInt64());
        }
        private void ValidateRecoveryAgents()
        {
            foreach (var a in recoveryAgents.Values)
            {
                if (a.DiscoveryCauseId < 0 || a.DiscoveryCauseId >= nextTrace || a.CompletionCauseId < 0 || a.CompletionCauseId >= nextTrace ||
                    (a.DiscoveredTick >= 0) != (a.DiscoveryCauseId > 0) || (a.Completed > 0) != (a.CompletionCauseId > 0)) throw new ArgumentException("Invalid recovery causal references.");
                if (RecoveryIdentityCheckTicks == 0 || !actors.TryGetValue(new Id<ActorTag>(a.ActorId), out var actor) || a.Completed < 0 || a.RetryAt < 0 || a.Backoff < planner.InitialBackoff || a.Backoff > planner.MaximumBackoff || a.PlannedTick < -1 || a.PlannedTick > LastProcessedTick || a.LastCost < 0)
                    throw new ArgumentException("Invalid recovery agent or retry state.");
                if (a.DiscoveredTick == -1)
                { if (a.Pending || a.OldCredentialId != 0 || a.OldCardId != 0 || a.KnownCaseId != 0 || a.PostedTick != -1 || a.Completed != 0) throw new ArgumentException("Undiscovered recovery goal has progress."); }
                else if (a.DiscoveredTick < 0 || a.DiscoveredTick > LastProcessedTick || !credentials.TryGetValue(a.OldCredentialId, out var old) || old.Definition.Holder != actor.Id || old.Definition.Card.Value != a.OldCardId)
                    throw new ArgumentException("Recovery discovery references a foreign credential.");
                if (a.PostedTick >= 0 && !messages.Values.Any(m => m.Draft.Kind == MessageKind.LostCardReport && m.PhysicalSender == actor.Id && m.Draft.Subject.SubjectId == a.OldCredentialId && m.SubmittedTick == a.PostedTick)) throw new ArgumentException("Recovery agent has not posted its claimed report.");
                if (a.KnownCaseId > 0 && (!recoveryCases.TryGetValue(a.KnownCaseId, out var c) || c.OldCredentialId != a.OldCredentialId || c.ActorId != a.ActorId || !KnowsRecoveryCase(actor, c))) throw new ArgumentException("Recovery agent has unread or foreign authorization.");
                if (a.IssuedCardId > 0 && (a.KnownCaseId == 0 || recoveryCases[a.KnownCaseId].IssuedCardId != a.IssuedCardId || !actor.Knowledge.Entries.Values.Any(e => e.Fact.Key.SubjectKind == SubjectKind.Credential && e.Fact.Key.Attribute == AttributeKind.CredentialCard && e.Fact.Value.Equals(FactValue.Integer(a.IssuedCardId))))) throw new ArgumentException("Recovery agent has no issued-card evidence.");
                if (a.ActiveActionId != 0 && (!a.Pending || actor.Action?.Id.Value != a.ActiveActionId || actor.Action.Kind != a.PlannedKind || a.PlannedTick != actor.Action.Started)) throw new ArgumentException("Recovery agent does not own its active action.");
                int timers = scheduled.Count(e => e.Kind == ScheduledKind.EvaluateRecovery && e.Intent.Actor.Value == a.ActorId);
                if (a.NextDecision == -1 ? timers != 0 : !a.Pending || a.NextDecision <= LastProcessedTick || a.NextDecision < a.RetryAt || timers != 1 || !scheduled.Any(e => e.Kind == ScheduledKind.EvaluateRecovery && e.Intent.Actor.Value == a.ActorId && e.Tick == a.NextDecision)) throw new ArgumentException("Invalid recovery decision timer.");
                if (a.TerminalRetry.Any(p => !terminals.ContainsKey(p.Key) || p.Value < 0)) throw new ArgumentException("Invalid private recovery service failure.");
            }
            if (cardLossCauses.Any(p => !items.ContainsKey(new Id<ItemTag>(p.Key)) || p.Value <= 0 || p.Value >= nextTrace)) throw new ArgumentException("Invalid physical card loss cause.");
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.EvaluateRecovery || e.Kind == ScheduledKind.RecoveryCommand))
                if (!recoveryAgents.TryGetValue(e.Intent.Actor.Value, out var agent) || !agent.Pending) throw new ArgumentException("Unowned recovery event.");
            foreach (var e in scheduled.Where(e => e.Kind == ScheduledKind.CardLoss))
                if (!actors.TryGetValue(e.Intent.Actor, out var actor) || OwnReportedCredential(actor, e.Intent.Item) == null) throw new ArgumentException("Invalid scheduled card loss.");
            foreach (var e in scheduled.Where(e => e.Kind is ScheduledKind.Command or ScheduledKind.ExternalCommand))
                if (e.Phase != EventPhase.Intent || !actors.ContainsKey(e.Intent.Actor) ||
                    (e.Kind == ScheduledKind.ExternalCommand ? e.CauseId != 0 || e.Value != 0 || e.ActionId != 0 : e.CauseId <= 0 || e.CauseId >= nextTrace))
                    throw new ArgumentException("Invalid scheduled command origin.");
        }
    }
}
