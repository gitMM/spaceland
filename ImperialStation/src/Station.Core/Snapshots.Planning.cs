using System;
using System.IO;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WritePlanning(BinaryWriter w)
        {
            w.Write(planner.MaximumDepth);w.Write(planner.NodeBudget);w.Write(planner.Candidates);w.Write(planner.RequestsPerTick);w.Write(planner.InitialBackoff);w.Write(planner.MaximumBackoff);
            w.Write(planningBudgetTick);w.Write(planningRequestsThisTick);w.Write(PlannerRequests);w.Write(PlannerExpansions);w.Write(PlansFound);w.Write(PlansUnavailable);w.Write(PlansExhausted);
            w.Write(reportAgents.Count);
            foreach(var agent in reportAgents.Values)
            {
                w.Write(agent.Actor.Value);w.Write(agent.NextDecision);w.Write(agent.Backoff);w.Write((int)agent.LastOutcome);w.Write(agent.LastCost);w.Write(agent.CompletedReports);w.Write(agent.BlockedReason);
                w.Write(agent.Pending.Count);for(int i=0;i<agent.Pending.Count;i++) { WriteDraft(w,agent.Pending[i]); w.Write(agent.PendingStamps[i].Due); w.Write(agent.PendingStamps[i].Sequence); }
                w.Write(agent.PlanPins.Count);foreach(var key in agent.PlanPins)WriteKey(w,key);
            }
            w.Write(repairAgents.Count);
            foreach (var agent in repairAgents.Values)
            {
                w.Write(agent.Actor.Value); w.Write(agent.NextDecision); w.Write(agent.Backoff); w.Write((int)agent.LastOutcome); w.Write(agent.LastCost); w.Write(agent.CompletedRepairs); w.Write(agent.BlockedReason);
                w.Write(agent.Pending.Count); foreach (long id in agent.Pending) w.Write(id);
                w.Write(agent.Attempts.Count); foreach (var pair in agent.Attempts) { w.Write(pair.Key); w.Write(pair.Value); }
                w.Write(agent.PlanPins.Count); foreach (var key in agent.PlanPins) WriteKey(w, key);
            }
        }
        private void ReadPlanning(BinaryReader r)
        {
            planner=new PlannerConfig(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt64(),r.ReadInt64());
            planningBudgetTick=r.ReadInt64();planningRequestsThisTick=r.ReadInt32();PlannerRequests=r.ReadInt64();PlannerExpansions=r.ReadInt64();PlansFound=r.ReadInt64();PlansUnavailable=r.ReadInt64();PlansExhausted=r.ReadInt64();
            int count=Count(r);
            for(int i=0;i<count;i++)
            {
                var agent=new ReportAgent{Actor=new Id<ActorTag>(r.ReadInt64()),NextDecision=r.ReadInt64(),Backoff=r.ReadInt64(),LastOutcome=ReadEnum<PlanOutcome>(r),LastCost=r.ReadInt64(),CompletedReports=r.ReadInt64(),BlockedReason=r.ReadString()};
                int n=Count(r);for(int j=0;j<n;j++) { agent.Pending.Add(ReadDraft(r)??throw new ArgumentException("Missing report goal.")); agent.PendingStamps.Add(new GoalStamp(r.ReadInt64(),r.ReadInt64())); }
                n=Count(r);for(int j=0;j<n;j++)agent.PlanPins.Add(ReadKey(r));reportAgents.Add(agent.Actor,agent);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var agent = new RepairAgent { Actor = new Id<ActorTag>(r.ReadInt64()), NextDecision = r.ReadInt64(), Backoff = r.ReadInt64(), LastOutcome = ReadEnum<PlanOutcome>(r), LastCost = r.ReadInt64(), CompletedRepairs = r.ReadInt64(), BlockedReason = r.ReadString() };
                int n = Count(r); for (int j = 0; j < n; j++) agent.Pending.Add(r.ReadInt64());
                n = Count(r); for (int j = 0; j < n; j++) agent.Attempts.Add(r.ReadInt64(), r.ReadInt64());
                n = Count(r); for (int j = 0; j < n; j++) agent.PlanPins.Add(ReadKey(r));
                if (!actors.ContainsKey(agent.Actor) || agent.Backoff < 1 || agent.Backoff > planner.MaximumBackoff || agent.Pending.Exists(id => !machines.ContainsKey(id))) throw new ArgumentException("Invalid saved repair agent.");
                repairAgents.Add(agent.Actor, agent);
            }
        }
    }
}
