using System;
using System.Collections.Generic;
using Station.Contracts;

namespace Station.Mind
{
    // Uniform-cost forward search. Predicate truth is hypothetical until an intent executes in Core.
    public static class GoapPlanner
    {
        private sealed class Node : IComparable<Node>
        {
            internal ulong State; internal int Depth; internal long Cost; internal long Sequence;
            internal Node? Parent; internal SymbolicAction? Action;
            public int CompareTo(Node? other) { if (other == null) return 1; int c = Cost.CompareTo(other.Cost); return c == 0 ? Sequence.CompareTo(other.Sequence) : c; }
        }
        public static PlanResult Search(PlanningProblem problem, int maxDepth = 8, int nodeBudget = 512)
        {
            if (maxDepth < 0 || nodeBudget < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            var open = new SortedSet<Node>();
            // Depth belongs in the key: a cheap long prefix must not prune a viable short prefix.
            var best = new SortedDictionary<(ulong State, int Depth), long>(); long sequence = 0; int expansions = 0;
            open.Add(new Node { State = problem.Initial, Sequence = sequence++ }); best.Add((problem.Initial, 0), 0);
            while (open.Count > 0)
            {
                var node = open.Min!; open.Remove(node);
                if (node.Cost != best[(node.State, node.Depth)]) continue;
                if ((node.State & problem.Goal) == problem.Goal)
                {
                    var plan = new List<SymbolicAction>(); var step = node;
                    while (step.Action != null) { plan.Add(step.Action); step = step.Parent!; }
                    plan.Reverse(); return new PlanResult(PlanOutcome.PlanFound, plan, node.Cost, expansions);
                }
                if (node.Depth >= maxDepth) continue;
                if (expansions >= nodeBudget) return new PlanResult(PlanOutcome.BudgetExhausted, expansions: expansions);
                expansions++;
                foreach (var action in problem.Actions)
                {
                    if ((node.State & action.Required) != action.Required || (node.State & action.Forbidden) != 0) continue;
                    ulong state = (node.State & ~action.Remove) | action.Add;
                    if (state == node.State) continue;
                    long cost = checked(node.Cost + action.Cost); int depth = node.Depth + 1;
                    if (best.TryGetValue((state, depth), out long prior) && prior <= cost) continue;
                    best[(state, depth)] = cost;
                    open.Add(new Node { State = state, Depth = depth, Cost = cost, Sequence = sequence++, Parent = node, Action = action });
                }
            }
            return new PlanResult(PlanOutcome.NoPlan, expansions: expansions);
        }
    }
}
