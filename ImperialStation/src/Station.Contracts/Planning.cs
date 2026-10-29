using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum PlanOutcome { PlanFound, NoPlan, BudgetExhausted }
    public sealed class SymbolicAction
    {
        public string Name { get; }
        public ulong Required { get; }
        public ulong Forbidden { get; }
        public ulong Add { get; }
        public ulong Remove { get; }
        public long Cost { get; }
        public ActionIntent Intent { get; }
        public SymbolicAction(string name, ulong required, ulong forbidden, ulong add, ulong remove, long cost, ActionIntent intent)
        {
            if (cost < 0 || (add & remove) != 0 || (required & forbidden) != 0) throw new ArgumentException("Invalid symbolic action effects, conditions or cost.");
            Name = name; Required = required; Forbidden = forbidden; Add = add; Remove = remove; Cost = cost; Intent = intent;
        }
    }
    public sealed class PlanningProblem
    {
        public ulong Initial { get; }
        public ulong Goal { get; }
        public IReadOnlyList<string> Predicates { get; }
        public IReadOnlyList<SymbolicAction> Actions { get; }
        public PlanningProblem(ulong initial, ulong goal, IEnumerable<string> predicates, IEnumerable<SymbolicAction> actions)
        {
            var names = predicates.ToArray(); if (names.Length > 64 || names.Distinct(StringComparer.Ordinal).Count() != names.Length) throw new ArgumentException("Require up to 64 distinct predicates.");
            ulong mask = names.Length == 64 ? ulong.MaxValue : (1UL << names.Length) - 1;
            var steps = actions.ToArray();
            if (((initial | goal) & ~mask) != 0 || steps.Any(a => ((a.Required | a.Forbidden | a.Add | a.Remove) & ~mask) != 0)) throw new ArgumentException("Predicate bit outside bound state.");
            Initial = initial; Goal = goal; Predicates = Array.AsReadOnly(names); Actions = Array.AsReadOnly(steps);
        }
    }
    public sealed class PlanResult
    {
        public PlanOutcome Outcome { get; }
        public IReadOnlyList<SymbolicAction> Steps { get; }
        public long Cost { get; }
        public int Expansions { get; }
        public PlanResult(PlanOutcome outcome, IEnumerable<SymbolicAction>? steps = null, long cost = 0, int expansions = 0)
        { Outcome = outcome; Steps = Array.AsReadOnly((steps ?? Array.Empty<SymbolicAction>()).ToArray()); Cost = cost; Expansions = expansions; }
    }
}
