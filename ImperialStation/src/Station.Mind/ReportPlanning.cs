using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class ReportPlanning
    {
        public static PlanResult PlanInbox(IActorView view, long loginTicks, long readTicks, int candidates = 3, int maxDepth = 8, int nodeBudget = 512)
            => Plan(view, null, loginTicks, readTicks, candidates, maxDepth, nodeBudget, true);
        public static PlanResult PlanRecords(IActorView view, RecordQuery query, long terminalId, long loginTicks, long queryTicks, int maxDepth = 8, int nodeBudget = 512)
            => PlanTerminal(view, null, query, terminalId, loginTicks, queryTicks, 3, maxDepth, nodeBudget, false);
        public static PlanResult Plan(IActorView view, MessageDraft? report, long loginTicks, long postTicks, int candidates = 3, int maxDepth = 8, int nodeBudget = 512, bool readInbox = false)
            => PlanTerminal(view, report, null, 0, loginTicks, postTicks, candidates, maxDepth, nodeBudget, readInbox);
        private static PlanResult PlanTerminal(IActorView view, MessageDraft? report, RecordQuery? query, long requiredTerminal, long loginTicks, long postTicks, int candidates, int maxDepth, int nodeBudget, bool readInbox)
        {
            if (candidates < 1 || candidates > 30 || loginTicks < 1 || postTicks < 1 || requiredTerminal < 0) throw new ArgumentException("Invalid terminal-planning limits/durations.");
            if (view.CurrentTile == null || view.PublicKnowledge == null) return new PlanResult(PlanOutcome.NoPlan);
            bool credential = view.Knowledge.Any(f => f.Key.SubjectKind == SubjectKind.Credential && f.Key.Attribute == AttributeKind.CredentialCard && f.Value.Kind == FactValueKind.Number
                && view.Inventory.Any(i => i.Value == f.Value.Number) && view.Knowledge.Any(s => s.Key.SubjectKind == SubjectKind.Credential && s.Key.SubjectId == f.Key.SubjectId && s.Key.Attribute == AttributeKind.CredentialStatus && s.Value.Equals(FactValue.Boolean(true))));
            var capability = query != null ? TerminalCapability.Records : readInbox ? TerminalCapability.Inbox : TerminalCapability.Post;
            var distances = new KnownNavigation.Distances(view, view.CurrentTile.Value);
            var terminals = view.PublicKnowledge.Services.Where(s => (requiredTerminal == 0 || s.Id == requiredTerminal) && (s.Capabilities & capability) != 0 && s.PermittedRoles.Contains(view.Role, StringComparer.Ordinal))
                .Select(s => (Service: s, Travel: distances.To(s.Position)))
                .Where(x => x.Travel.HasValue).OrderBy(x => x.Travel!.Value).ThenBy(x => x.Service.Id).Take(candidates).ToArray();
            var predicates = new List<string> { "KnownHeldCredential", query != null ? "RecordsQueried" : readInbox ? "InboxRead" : "ReportPosted" }; var actions = new List<SymbolicAction>(); ulong initial = credential ? 1UL : 0UL;
            ulong locations = 0, sessions = 0;
            for (int i = 0; i < terminals.Length; i++) { locations |= 1UL << (2 + i * 2); sessions |= 1UL << (3 + i * 2); predicates.Add("AtTerminal:" + terminals[i].Service.Id); predicates.Add("SessionAt:" + terminals[i].Service.Id); }
            for (int i = 0; i < terminals.Length; i++)
            {
                var terminal = terminals[i].Service; ulong at = 1UL << (2 + i * 2), session = 1UL << (3 + i * 2);
                if (view.CurrentTile.Value == terminal.Position) initial |= at;
                if (view.SessionTerminalId == terminal.Id && (initial & at) != 0) initial |= session;
                // Replan at safe boundaries; every travel action here starts from the current view.
                // Forbid moving after reaching any bound terminal to avoid an invalid fixed-cost inter-terminal hop.
                if (view.CurrentTile.Value != terminal.Position)
                    actions.Add(new SymbolicAction("TravelToTerminal:" + terminal.Id, 0, locations, at, (locations & ~at) | sessions, terminals[i].Travel!.Value, new ActionIntent(view.Actor, ActionKind.Move, terminal.Position)));
                actions.Add(new SymbolicAction("Login:" + terminal.Id, at | 1, session, session, 0, loginTicks, new ActionIntent(view.Actor, ActionKind.Login, terminal.Position, serviceId: terminal.Id)));
                actions.Add(new SymbolicAction((query != null ? "QueryRecords:" : readInbox ? "ReadInbox:" : "Post:") + terminal.Id, at | session, 2, 2, 0, postTicks, new ActionIntent(view.Actor, query != null ? ActionKind.QueryRecords : readInbox ? ActionKind.ReadInbox : ActionKind.PostMessage, terminal.Position, serviceId: terminal.Id, message: report, query: query)));
            }
            return GoapPlanner.Search(new PlanningProblem(initial, 2, predicates, actions), maxDepth, nodeBudget);
        }
    }
}
