using Station.Contracts;
using Station.Core;
using Station.Mind;
using Xunit;

namespace Station.Tests;

public sealed class PlanningTests
{
    private static World Reporting(bool leftExpensive=false,bool rightExpensive=false,bool unread=false,long hiddenItemTile=1,PlannerConfig? planner=null,ulong seed=1,bool trace=true)
    {
        var tiles=Enumerable.Range(0,5).Select(i=>new Tile(new(i+1),"Corridor",new Coord(i-2,0,0),traversalOverride:2)).ToArray();
        var facts=new List<FactSeed>();
        if(leftExpensive)facts.Add(new(new(1),new FactKey(SubjectKind.Tile,2,AttributeKind.TraversalTicks),FactValue.Integer(20)));
        if(rightExpensive)facts.Add(new(new(1),new FactKey(SubjectKind.Tile,4,AttributeKind.TraversalTicks),FactValue.Integer(20)));
        var report=new MessageDraft(MessageKind.Observation,RecipientKind.Authority,default,new FactKey(SubjectKind.Item,1,AttributeKind.Location),FactValue.At(new(-2,0,0)),0);
        return new World("report_planning_fixture",seed,new WorldConfig(),Fixtures.Catalog,tiles,new[]{new ActorSeed(new(1),"ReportWorker","worker",new(0,0,0))},
            new[]{new Item(new(101),"keycard",1,new ItemLocation(LocationKind.ActorInventory,1)),new Item(new(1),"component",1,new ItemLocation(LocationKind.TileContainer,hiddenItemTile))},trace,
            new InformationSeed(TerminalFixtures.Tiny(),new[]{new TerminalSeed(1,new(-2,0,0),new[]{"worker"}),new TerminalSeed(2,new(2,0,0),new[]{"worker"})},
                new[]{new CredentialSeed(1,new(1),new(1),new(101))},facts,
                unread?new[]{new InitialMessage(TerminalFixtures.Draft(1),new(0))}:null,
                new[]{new ReportGoalSeed(new(1),report)},planner));
    }
    [Fact]
    public void AutonomousReportUsesSearchThenPhysicalTravelLoginAndPost()
    {
        var w=Reporting();w.AdvanceTo(0);
        var selected=Assert.Single(w.Trace,e=>e.Type=="PlanSelected");Assert.Contains("TravelToTerminal:1 -> Login:1 -> Post:1",selected.Detail);Assert.Contains("cost=6",selected.Detail);
        Assert.True(w.InspectActor(new(1)).InTransit);Assert.Empty(w.InspectMessages());
        w.AdvanceTo(3);Assert.Empty(w.InspectSessions());Assert.Empty(w.InspectMessages());
        w.AdvanceTo(4);Assert.Equal(new Coord(-2,0,0),w.InspectActor(new(1)).Position);Assert.Empty(w.InspectMessages());
        w.AdvanceTo(5);Assert.Single(w.InspectSessions());Assert.Empty(w.InspectMessages());
        w.AdvanceTo(6);Assert.Equal(MessageStage.Posted,Assert.Single(w.InspectMessages()).Stage);Assert.Empty(w.InspectBeliefs());
        w.AdvanceTo(8);Assert.Single(w.InspectBeliefs());Assert.Equal(1,Assert.Single(w.InspectAgents()).CompletedReports);Assert.Equal(0,Assert.Single(w.InspectAgents()).PendingReports);
        Assert.Equal(3,w.PlansFound);Assert.Equal(3,w.ActionsCompleted);Assert.Equal(0,w.ActionsFailed);
    }
    [Fact]
    public void T23_KnownCostsFlipChosenTerminalAndTiesAreStable()
    {
        var left=Reporting(rightExpensive:true);var right=Reporting(leftExpensive:true);left.AdvanceTo(0);right.AdvanceTo(0);
        Assert.Contains("TravelToTerminal:1",Assert.Single(left.Trace,e=>e.Type=="PlanSelected").Detail);
        Assert.Contains("TravelToTerminal:2",Assert.Single(right.Trace,e=>e.Type=="PlanSelected").Detail);
        for(ulong seed=1;seed<=10;seed++){var equal=Reporting(seed:seed);equal.AdvanceTo(0);Assert.Contains("TravelToTerminal:1",Assert.Single(equal.Trace,e=>e.Type=="PlanSelected").Detail);}
    }
    [Fact]
    public void T23_UnknownTraversalChangeDoesNotChangePlan()
    {
        var baseline=Reporting();var changed=Reporting();changed.ScheduleTraversalChange(0,new Coord(-1,0,0),100);
        baseline.AdvanceTo(0);changed.AdvanceTo(0);
        Assert.Equal(Assert.Single(baseline.Trace,e=>e.Type=="PlanSelected").Detail,Assert.Single(changed.Trace,e=>e.Type=="PlanSelected").Detail);
        Assert.NotEqual(baseline.InspectActor(new(1)).Arrival,changed.InspectActor(new(1)).Arrival);
    }
    [Fact]
    public void T08_HiddenItemAndUnreadMessageLeaveMeaningfulDecisionsUnchanged()
    {
        var first=Reporting(hiddenItemTile:1);var hidden=Reporting(hiddenItemTile:5);var unread=Reporting(hiddenItemTile:1,unread:true);
        first.AdvanceTo(1);hidden.AdvanceTo(1);unread.AdvanceTo(1);
        var decision=Assert.Single(first.Trace,e=>e.Type=="PlanSelected");Assert.Contains("TravelToTerminal",decision.Detail);
        Assert.Equal(decision.Detail,Assert.Single(hidden.Trace,e=>e.Type=="PlanSelected").Detail);Assert.Equal(decision.Detail,Assert.Single(unread.Trace,e=>e.Type=="PlanSelected").Detail);
        Assert.True(unread.InspectActor(new(1)).InTransit);Assert.Equal(MessageStage.DeliveredToMailbox,Assert.Single(unread.InspectMailbox(new(1))).Stage);
        Assert.DoesNotContain(unread.InspectKnowledge(new(1)),f=>f.Key.SubjectKind==SubjectKind.Job);
    }
    [Fact]
    public void T16_BudgetExhaustionBackoffAndContinuationAreDeterministic()
    {
        var config=new PlannerConfig(nodeBudget:0,initialBackoff:2,maximumBackoff:8);
        var w=Reporting(planner:config);w.AdvanceTo(3);var restored=World.Restore(w.Serialize(),true);w.AdvanceTo(20);for(int t=4;t<=20;t++)restored.AdvanceTo(t);
        Assert.Equal(w.Digest(),restored.Digest());Assert.Equal(4,w.PlansExhausted);Assert.Equal(0,w.PlansUnavailable);Assert.Equal(22,Assert.Single(w.InspectAgents()).NextDecisionTick);
        Assert.Equal(new long[]{0,2,6,14},w.Trace.Where(e=>e.Type=="PlanUnavailable").Select(e=>e.Tick));Assert.Equal(20,w.LastProcessedTick);Assert.Empty(w.InspectMessages());
    }
    [Fact]
    public void GenericPlannerSearchesAlternativePrerequisitesAndRespectsDepth()
    {
        SymbolicAction Action(string name,ulong required,ulong add,long cost)=>new(name,required,0,add,0,cost,default);
        var actions=new[]{Action("Acquire",0,2,10),Action("Fabricate",1,2,3),Action("Repair",2,4,2)};
        var problem=new PlanningProblem(1,4,new[]{"Input","Part","Repaired"},actions);
        var result=GoapPlanner.Search(problem);Assert.Equal(PlanOutcome.PlanFound,result.Outcome);Assert.Equal(new[]{"Fabricate","Repair"},result.Steps.Select(s=>s.Name));Assert.Equal(5,result.Cost);
        var direct=new PlanningProblem(1,4,problem.Predicates,new[]{Action("Acquire",0,2,1),actions[1],actions[2]});
        Assert.Equal(new[]{"Acquire","Repair"},GoapPlanner.Search(direct).Steps.Select(s=>s.Name));
        Assert.Equal(PlanOutcome.NoPlan,GoapPlanner.Search(problem,maxDepth:1).Outcome);
        Assert.Equal(PlanOutcome.BudgetExhausted,GoapPlanner.Search(problem,nodeBudget:1).Outcome);
        Assert.Equal(PlanOutcome.NoPlan,GoapPlanner.Search(new PlanningProblem(0,4,problem.Predicates,new[]{actions[1],actions[2]})).Outcome);
    }
    [Fact]
    public void T12_T13_T25_AutonomousPlanningPinsRequestsAndMessagesRoundTrip()
    {
        for(ulong seed=0;seed<20;seed++)
        {
            var direct=Reporting(seed:seed,trace:false);direct.AdvanceTo(20);var rng=Pcg32.Create(seed,"planner-snapshots");
            foreach(int tick in new[]{0,3,4,5,6}.Concat(Enumerable.Range(0,5).Select(_=>rng.Next(20))))
            {
                var prefix=Reporting(seed:seed);prefix.AdvanceTo(tick);byte[] bytes=prefix.Serialize();var restored=World.Restore(bytes,true);Assert.Equal(bytes,restored.Serialize());
                for(int t=tick+1;t<=20;t++){restored.ActorView(new(1));restored.InspectAgents();restored.AdvanceTo(t);}Assert.Equal(direct.Digest(),restored.Digest());
            }
        }
    }
    [Fact]
    public void PlannerRequestsAreBoundedPerSimulationTickAndDrainInOrder()
    {
        var population=Enumerable.Range(1,5).Select(i=>new ActorSeed(new(i),"BudgetActor"+i,"worker",Fixtures.A)).ToArray();
        var report=new MessageDraft(MessageKind.Observation,RecipientKind.Authority,default,new FactKey(SubjectKind.Item,99,AttributeKind.Present),FactValue.Boolean(false),0);
        var w=new World("planning_budget_fixture",1,new WorldConfig(),Fixtures.Catalog,new[]{new Tile(new(1),"Corridor",Fixtures.A,traversalOverride:2),new Tile(new(2),"Command",Fixtures.B,traversalOverride:2)},population,
            population.Select(a=>new Item(new(a.Id.Value),"keycard",1,new ItemLocation(LocationKind.ActorInventory,a.Id.Value))),true,
            new InformationSeed(TerminalFixtures.Tiny(),new[]{new TerminalSeed(1,Fixtures.B,new[]{"worker"},capacity:5)},population.Select(a=>new CredentialSeed(a.Id.Value,a.Id,a.Account,new(a.Id.Value))),reportGoals:population.Select(a=>new ReportGoalSeed(a.Id,report)),planner:new PlannerConfig(requestsPerTick:2)));
        w.AdvanceTo(20);Assert.All(w.InspectAgents(),a=>Assert.Equal(1,a.CompletedReports));
        Assert.All(w.Trace.Where(e=>e.Type is "PlanSelected" or "PlanUnavailable").GroupBy(e=>e.Tick),group=>Assert.InRange(group.Count(),1,2));
        Assert.Equal(new long[]{1,2},w.Trace.Where(e=>e.Type=="PlanSelected"&&e.Tick==0).Select(e=>e.ActorId));
    }
    [Fact]
    public void T14_AutonomousWorldsAreIndependentAtConcurrencyTwoFourEight()
    {
        string Run(int i){var w=Reporting(seed:(ulong)i);w.AdvanceTo(20);return w.Digest();}
        var expected=Enumerable.Range(0,20).Select(Run).ToArray();
        foreach(int concurrency in new[]{2,4,8}){var actual=new string[20];Parallel.For(0,20,new ParallelOptions{MaxDegreeOfParallelism=concurrency},i=>actual[i]=Run(i));Assert.Equal(expected,actual);}
    }
}
