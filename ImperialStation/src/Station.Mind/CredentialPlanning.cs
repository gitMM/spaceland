using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class CredentialPlanning
    {
        public static bool KnownUsableCredential(IActorView view) => view.Knowledge.Any(f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.Attribute == AttributeKind.CredentialCard && f.Value.Kind == FactValueKind.Number &&
            view.Inventory.Any(i => i.Value == f.Value.Number) && KnownActive(view, f.Key.SubjectId));
        private static bool KnownActive(IActorView view, long credential) => view.Knowledge.Any(f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.SubjectId == credential && f.Key.Attribute == AttributeKind.CredentialStatus && f.Value.Equals(FactValue.Boolean(true)));
        public static PlanResult Plan(IActorView view, RecoveryPlanningState memory, IReadOnlyList<PublicRecoveryDesk> directory,
            long identityTicks, long postTicks, long readTicks, long latencyTicks, long pickupTicks, int candidates = 3, int maximumDepth = 8, int nodeBudget = 512)
        {
            if (candidates < 1 || candidates > 6 || identityTicks < 1 || postTicks < 1 || readTicks < 1 || latencyTicks < 1 || pickupTicks < 1) throw new ArgumentException("Invalid recovery planning limits.");
            if (!view.CurrentTile.HasValue || view.PublicKnowledge == null) return new PlanResult(PlanOutcome.NoPlan);
            if (KnownUsableCredential(view)) return GoapPlanner.Search(new PlanningProblem(1, 1, new[] { "KnownUsableCredential" }, Array.Empty<SymbolicAction>()), maximumDepth, nodeBudget);
            const ulong held = 1, posted = 2, authorized = 4, issued = 8;
            ulong initial = (memory.ReportPosted ? posted : 0) | (memory.AuthorizationRead ? authorized : 0) | (memory.IssuedCardId > 0 ? issued : 0);
            var predicates = new List<string> { "KnownUsableCredential", "LossReported", "AuthorizationRead", "CardIssued" }; var actions = new List<SymbolicAction>();
            var distances = new KnownNavigation.Distances(view, view.CurrentTile.Value);
            var desks = directory.Where(d => !memory.UnavailableTerminals.Contains(d.TerminalId) && view.PublicKnowledge.Services.Any(s => s.Id == d.TerminalId && s.Position == d.Position && (s.Capabilities & TerminalCapability.CredentialRecovery) != 0 && s.PermittedRoles.Contains(view.Role, StringComparer.Ordinal)))
                .Select(d => (Desk: d, Travel: distances.To(d.Position))).Where(x => x.Travel.HasValue)
                .OrderBy(x => checked(x.Travel!.Value + x.Desk.IssuanceTicks)).ThenBy(x => x.Desk.TerminalId).Take(candidates).ToArray();
            ulong locations = 0, sessions = 0;
            foreach (var desk in desks) { int index = predicates.Count; predicates.Add("AtRecoveryDesk:" + desk.Desk.TerminalId); predicates.Add("RecoverySession:" + desk.Desk.TerminalId); locations |= 1UL << index; sessions |= 1UL << (index + 1); }
            int cardBit = predicates.Count; predicates.Add("AtKnownCard"); ulong atCard = 1UL << cardBit; locations |= atCard;
            var knownCard = memory.IssuedCardId > 0 ? memory.IssuedCardId : !memory.ReportPosted && KnownActive(view, memory.OldCredentialId) ? memory.OldCardId : 0;
            var location = knownCard == 0 ? null : view.Knowledge.FirstOrDefault(f => f.Key.SubjectKind == SubjectKind.Item && f.Key.SubjectId == knownCard && f.Key.Attribute == AttributeKind.Location && f.Value.Kind == FactValueKind.Coordinate);
            if (location != null && location.Value.Coordinate == view.CurrentTile.Value) initial |= atCard;
            for (int i = 0; i < desks.Length; i++)
            {
                ulong at = 1UL << (4 + 2 * i), session = 1UL << (5 + 2 * i);
                if (view.CurrentTile.Value == desks[i].Desk.Position) initial |= at;
                if ((initial & at) != 0 && memory.RestrictedTerminalId == desks[i].Desk.TerminalId) initial |= session;
            }
            ulong initialLocations = initial & locations;
            if (location != null)
            {
                var cost = distances.To(location.Value.Coordinate);
                if (cost.HasValue)
                {
                    if ((initial & atCard) == 0) actions.Add(new SymbolicAction("RetrieveKnownCard:Travel", 0, locations & ~initialLocations, atCard, (locations & ~atCard) | sessions, cost.Value, new ActionIntent(view.Actor, ActionKind.Move, location.Value.Coordinate)));
                    actions.Add(new SymbolicAction("RetrieveKnownCard:Pickup", atCard, held, held, 0, pickupTicks, new ActionIntent(view.Actor, ActionKind.PickUp, location.Value.Coordinate, new Id<ItemTag>(knownCard))));
                }
            }
            if (memory.HasAlternateCapability && memory.IssuedCardId == 0)
                for (int i = 0; i < desks.Length; i++)
                {
                    var desk = desks[i].Desk; ulong at = 1UL << (4 + 2 * i), session = 1UL << (5 + 2 * i);
                    if ((initial & at) == 0) actions.Add(new SymbolicAction("TravelToRecovery:" + desk.TerminalId, 0, locations & ~initialLocations, at, (locations & ~at) | sessions, desks[i].Travel!.Value, new ActionIntent(view.Actor, ActionKind.Move, desk.Position)));
                    actions.Add(new SymbolicAction("RecoveryLogin:" + desk.TerminalId, at, session, session, 0, identityTicks, new ActionIntent(view.Actor, ActionKind.RecoveryLogin, desk.Position, serviceId: desk.TerminalId)));
                    actions.Add(new SymbolicAction("PostLostCard:" + desk.TerminalId, at | session, posted, posted, 0, checked(postTicks + 2 * latencyTicks), new ActionIntent(view.Actor, ActionKind.PostLostCard, desk.Position, new Id<ItemTag>(memory.OldCardId), desk.TerminalId)));
                    actions.Add(new SymbolicAction("ReadRecoveryReply:" + desk.TerminalId, at | session | posted, authorized, authorized, 0, readTicks, new ActionIntent(view.Actor, ActionKind.ReadRecoveryInbox, desk.Position, serviceId: desk.TerminalId)));
                    actions.Add(new SymbolicAction("IssueReplacement:" + desk.TerminalId, at | session | authorized, issued, issued, 0, desk.IssuanceTicks, new ActionIntent(view.Actor, ActionKind.IssueReplacement, desk.Position, new Id<ItemTag>(memory.OldCardId), desk.TerminalId)));
                    // The hypothetical output is rebound from the completed service receipt before execution.
                    actions.Add(new SymbolicAction("CollectReplacement:" + desk.TerminalId, at | issued, held, held, 0, pickupTicks, new ActionIntent(view.Actor, ActionKind.PickUp, desk.Position)));
                }
            return GoapPlanner.Search(new PlanningProblem(initial, held, predicates, actions), maximumDepth, nodeBudget);
        }
    }
}
