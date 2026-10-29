using System;
using System.Collections.Generic;
using Station.Contracts;

namespace Station.Core
{
    public sealed class PlannerConfig
    {
        public int MaximumDepth { get; }
        public int NodeBudget { get; }
        public int Candidates { get; }
        public int RequestsPerTick { get; }
        public long InitialBackoff { get; }
        public long MaximumBackoff { get; }
        public PlannerConfig(int maximumDepth=8,int nodeBudget=512,int candidates=3,int requestsPerTick=64,long initialBackoff=50,long maximumBackoff=1000)
        {
            if(maximumDepth<1||nodeBudget<0||candidates<1||candidates>30||requestsPerTick<1||initialBackoff<1||maximumBackoff<initialBackoff)throw new ArgumentException("Invalid planner bounds.");
            MaximumDepth=maximumDepth;NodeBudget=nodeBudget;Candidates=candidates;RequestsPerTick=requestsPerTick;InitialBackoff=initialBackoff;MaximumBackoff=maximumBackoff;
        }
    }
    public sealed class ReportGoalSeed
    {
        public Id<ActorTag> Actor { get; }
        public MessageDraft Report { get; }
        public ReportGoalSeed(Id<ActorTag> actor,MessageDraft report){Actor=actor;Report=report;}
    }
    public sealed class RepairGoalSeed
    {
        public Id<ActorTag> Actor { get; }
        public long MachineId { get; }
        public RepairGoalSeed(Id<ActorTag> actor, long machineId) { Actor = actor; MachineId = machineId; }
    }
    internal sealed class RepairAgent
    {
        internal Id<ActorTag> Actor;
        internal readonly List<long> Pending = new List<long>();
        internal readonly SortedDictionary<long, long> Attempts = new SortedDictionary<long, long>();
        internal readonly List<FactKey> PlanPins = new List<FactKey>();
        internal long NextDecision = -1;
        internal long Backoff;
        internal PlanOutcome LastOutcome;
        internal long LastCost;
        internal long CompletedRepairs;
        internal string BlockedReason = "";
    }
    public sealed class RepairAgentSnapshot
    {
        public long ActorId { get; internal set; }
        public IReadOnlyList<long> PendingMachines { get; internal set; } = Array.Empty<long>();
        public long CompletedRepairs { get; internal set; }
        public long NextDecisionTick { get; internal set; }
        public long Backoff { get; internal set; }
        public PlanOutcome LastOutcome { get; internal set; }
        public long LastPlanCost { get; internal set; }
        public string BlockedReason { get; internal set; } = "";
    }
    internal sealed class ReportAgent
    {
        internal Id<ActorTag> Actor;
        internal readonly List<MessageDraft> Pending = new List<MessageDraft>();
        internal readonly List<GoalStamp> PendingStamps = new List<GoalStamp>();
        internal readonly List<FactKey> PlanPins = new List<FactKey>();
        internal long NextDecision=-1;
        internal long Backoff;
        internal PlanOutcome LastOutcome;
        internal long LastCost;
        internal long CompletedReports;
        internal string BlockedReason="";
    }
    public sealed class AgentSnapshot
    {
        public long ActorId { get; internal set; }
        public int PendingReports { get; internal set; }
        public long CompletedReports { get; internal set; }
        public long NextDecisionTick { get; internal set; }
        public long Backoff { get; internal set; }
        public PlanOutcome LastOutcome { get; internal set; }
        public long LastPlanCost { get; internal set; }
        public string BlockedReason { get; internal set; }="";
    }
}
