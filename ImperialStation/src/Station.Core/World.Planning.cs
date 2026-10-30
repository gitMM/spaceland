using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<Id<ActorTag>,ReportAgent> reportAgents=new SortedDictionary<Id<ActorTag>,ReportAgent>();
        private PlannerConfig planner=new PlannerConfig();
        private long planningBudgetTick=-1;
        private int planningRequestsThisTick;
        public long PlannerRequests { get; private set; }
        public long PlannerExpansions { get; private set; }
        public long PlansFound { get; private set; }
        public long PlansUnavailable { get; private set; }
        public long PlansExhausted { get; private set; }
        private void InitializeReportGoals(IEnumerable<ReportGoalSeed> goals,PlannerConfig config)
        {
            planner=config;
            foreach(var goal in goals)
            {
                if(!actors.ContainsKey(goal.Actor)||!ValidDraft(goal.Report)||!CitizenCanPost(goal.Report.Kind)||goal.Report.ObservedTick>0)throw new ArgumentException("Invalid initial report goal.");
                if(!reportAgents.TryGetValue(goal.Actor,out var agent)){agent=new ReportAgent{Actor=goal.Actor,Backoff=config.InitialBackoff};reportAgents.Add(goal.Actor,agent);}
                QueueReport(agent, goal.Report);
            }
            foreach(var agent in reportAgents.Values)RequestReportDecision(agent,0);
        }
        private void RequestReportDecision(ReportAgent agent,long tick)
        {
            if(agent.Pending.Count==0)return;
            // Unrelated action/daily-goal wakeups must not erase an observed
            // failure's retry delay. NextDecision is persisted with this reason.
            if(agent.BlockedReason.Length > 0 && agent.NextDecision > processingTick) tick = Math.Max(tick, agent.NextDecision);
            if(processingTick>=0 && (tick<processingTick || tick==processingTick&&processingPhase>=EventPhase.Decision))tick=checked(processingTick+1);
            if(agent.NextDecision>=0&&agent.NextDecision<=tick)return;
            if(agent.NextDecision>=0)foreach(var e in scheduled.ForActor(agent.Actor.Value).Where(e=>e.Kind==ScheduledKind.EvaluateReport).ToArray())scheduled.Remove(e);
            agent.NextDecision=tick;Schedule(new ScheduledEvent{Tick=tick,Phase=EventPhase.Decision,Kind=ScheduledKind.EvaluateReport,Intent=new ActionIntent(agent.Actor,ActionKind.PostMessage,default)});
        }
        private void EvaluateReport(Id<ActorTag> actorId)
        {
            var agent=reportAgents[actorId];agent.NextDecision=-1;var actor=actors[actorId];
            if(EscortControls(actorId)||actor.Action!=null||agent.Pending.Count==0||DeferToNeed(actor)||PrepareCredentialGoal(actor)||DeferToDueGoal(actor, DueSource.Report)||AlreadyPlanned(actor, DueSource.Report))return;
            if(planningBudgetTick!=processingTick){planningBudgetTick=processingTick;planningRequestsThisTick=0;}
            if(planningRequestsThisTick>=planner.RequestsPerTick){RequestReportDecision(agent,checked(processingTick+1));return;}
            planningRequestsThisTick++;PlannerRequests++;
            var result=ReportPlanning.Plan(ActorView(actorId),agent.Pending[0],Information!.LoginTicks,Information.PostTicks,planner.Candidates,planner.MaximumDepth,planner.NodeBudget,avoidedTerminals:AvoidedTerminals(actorId));
            PlannerExpansions+=result.Expansions;agent.LastOutcome=result.Outcome;agent.LastCost=result.Cost;
            if(result.Outcome==PlanOutcome.PlanFound&&result.Steps.Count>0)
            {
                PlansFound++;agent.BlockedReason="";
                long cause=Emit("PlanSelected",actor,messageId:agent.Pending[0].RelatedMessageId,causeId:ConstructionReportCause(agent.Pending[0]),detail:"report-due/v1; cost="+result.Cost+"; "+string.Join(" -> ",result.Steps.Select(s=>s.Name)));
                PinReportPlan(actor,agent);
                Schedule(new ScheduledEvent{Tick=processingTick,Phase=EventPhase.Intent,Kind=ScheduledKind.Command,Value=DecisionSource(actor, DueSource.Report),Intent=result.Steps[0].Intent,CauseId=cause});
                return;
            }
            if(result.Outcome==PlanOutcome.BudgetExhausted){PlansExhausted++;agent.BlockedReason="PlanBudgetExhausted";}
            else{PlansUnavailable++;agent.BlockedReason="UnknownRequiredFactOrCredential";}
            Emit("PlanUnavailable",actor,detail:"report-due/v1; "+result.Outcome+"; retry="+agent.Backoff);
            WakeDaily(actor);RequestReportDecision(agent,checked(processingTick+agent.Backoff));agent.Backoff=Math.Min(planner.MaximumBackoff,checked(agent.Backoff*2));
        }
        private static void PinReportPlan(Actor actor,ReportAgent agent)
        {
            foreach(var entry in actor.Knowledge.Entries.Values)
            {
                var fact=entry.Fact;
                if(fact.Pinned || fact.Key.SubjectKind!=SubjectKind.Tile || (fact.Key.Attribute!=AttributeKind.TraversalTicks&&fact.Key.Attribute!=AttributeKind.Location))continue;
                agent.PlanPins.Add(fact.Key);entry.Fact=new KnowledgeFact(fact.Id,fact.Key,fact.Value,fact.SourceKind,fact.SourceId,fact.ObservedTick,fact.LearnedTick,true,fact.SourceActorId);
                entry.Recency=++actor.Knowledge.Sequence; // An actual planning read, not diagnostic inspection.
            }
        }
        private static void ReleasePlanPins(Actor actor,ReportAgent agent)
        {
            foreach(var key in agent.PlanPins)
                if(actor.Knowledge.Entries.TryGetValue(key,out var entry)){var f=entry.Fact;entry.Fact=new KnowledgeFact(f.Id,f.Key,f.Value,f.SourceKind,f.SourceId,f.ObservedTick,f.LearnedTick,false,f.SourceActorId);}
            agent.PlanPins.Clear();
        }
        private void OnActionCompleted(Actor actor,ActiveAction action)
        {
            RecoveryActionCompleted(actor, action); DailyActionEnded(actor, action.Id.Value, FailureReason.None); WakeDaily(actor); NeedActionEnded(actor, action.Id.Value, FailureReason.None); WakeNeed(actor); RefreshNeedMode(actor); ApplyLearnedGuardRevocation(actor); RefreshEquipment(actor);
            RefreshGuardReadiness(actor);
            CleanupActionCompleted(actor, action);
            WakeGuard(actor);
            WakePatrol(actor);
            WakeRoutine(actor);
            WakeOrderDecisions(actor);
            WakeAudits(actor, completed: action);
            OnCheckInActionFinished(actor, action, false);
            OnRepairActionFinished(actor, false);
            if(!reportAgents.TryGetValue(actor.Id,out var agent))return;
            ReleasePlanPins(actor,agent);
            if(action.Kind is ActionKind.Login or ActionKind.PostMessage or ActionKind.ReadInbox) agent.Backoff=planner.InitialBackoff;
            if(action.Kind==ActionKind.PostMessage&&agent.Pending.Count>0&&SameDraft(action.Message,agent.Pending[0]))
            {agent.Pending.RemoveAt(0);agent.PendingStamps.RemoveAt(0);agent.CompletedReports++;Emit("ReportGoalCompleted",actor,action.Id,detail:"report-due/v1",causeId:action.CauseId);}
            RequestReportDecision(agent,processingTick);
        }
        private void OnActionFailed(Actor actor)
        {
            WakeRecovery(actor); WakeDaily(actor); WakeNeed(actor); RefreshNeedMode(actor); WakeGuard(actor, true);
            WakePatrol(actor, true);
            WakeRoutine(actor);
            WakeOrderDecisions(actor, true);
            WakeAudits(actor, true);
            OnCheckInActionFinished(actor, null, true);
            OnRepairActionFinished(actor, true);
            if(!reportAgents.TryGetValue(actor.Id,out var agent)||actor.Action!=null)return;
            ReleasePlanPins(actor,agent);agent.BlockedReason=actor.LastFailure.ToString();
            foreach(var e in scheduled.ForActor(actor.Id.Value).Where(e=>e.Kind==ScheduledKind.EvaluateReport).ToArray())scheduled.Remove(e);
            agent.NextDecision=-1;RequestReportDecision(agent,checked(processingTick+agent.Backoff));agent.Backoff=NextWorkBackoff(agent.Backoff);
        }
        private static bool SameDraft(MessageDraft? a,MessageDraft b)=>a!=null&&a.Kind==b.Kind&&a.RecipientKind==b.RecipientKind&&a.Recipient==b.Recipient&&a.Subject.Equals(b.Subject)&&a.Claim.Equals(b.Claim)&&a.TaskId==b.TaskId&&a.AttemptId==b.AttemptId&&a.ObservedTick==b.ObservedTick&&a.RelatedMessageId==b.RelatedMessageId&&a.OperationKey==b.OperationKey&&a.WorkOrder?.Kind==b.WorkOrder?.Kind&&a.WorkOrder?.Tile==b.WorkOrder?.Tile&&a.WorkOrder?.MachineId==b.WorkOrder?.MachineId&&a.WorkOrder?.Expires==b.WorkOrder?.Expires&&(a.WorkOrder?.Construction == null ? b.WorkOrder?.Construction == null : b.WorkOrder?.Construction != null && a.WorkOrder.Construction.Same(b.WorkOrder.Construction))&&SamePersonnelPayloads(a,b)&&SameDetentionPayloads(a,b)&&(a.EscortOrder == null ? b.EscortOrder == null : b.EscortOrder != null && a.EscortOrder.Same(b.EscortOrder));
        public IReadOnlyList<AgentSnapshot> InspectAgents()=>reportAgents.Values.Select(a=>new AgentSnapshot{ActorId=a.Actor.Value,PendingReports=a.Pending.Count,CompletedReports=a.CompletedReports,NextDecisionTick=a.NextDecision,Backoff=a.Backoff,LastOutcome=a.LastOutcome,LastPlanCost=a.LastCost,BlockedReason=a.BlockedReason}).ToArray();
    }
}
