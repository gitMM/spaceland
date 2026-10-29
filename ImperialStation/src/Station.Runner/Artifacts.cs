using System.Reflection;
using System.Text;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public sealed class Checkpoint
{
    public int SchemaVersion { get; set; } = 1;
    public string Digest { get; set; } = "";
    public string State { get; set; } = "";
    public static Checkpoint Capture(World world) => new() { Digest = world.Digest(), State = Convert.ToBase64String(world.Serialize()) };
    public World Restore(bool trace = false)
    {
        if (SchemaVersion != 1) throw new ArgumentException("Unsupported checkpoint schema.");
        var world = World.Restore(Convert.FromBase64String(State), trace);
        if (world.Digest() != Digest) throw new ArgumentException("Checkpoint digest mismatch."); return world;
    }
}
public sealed class ReplayBundle
{
    public int SchemaVersion { get; set; } = 1;
    public string BuildId { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string ContentDigest { get; set; } = "";
    public string ScenarioJson { get; set; } = "";
    public string CatalogJson { get; set; } = "";
    public string MovementModel { get; set; } = World.MovementModel;
    public string RandomModel { get; set; } = World.RandomModel;
    public Checkpoint Start { get; set; } = new();
    public long Horizon { get; set; }
    public string ExpectedDigest { get; set; } = "";
    public string ExpectedStatus { get; set; } = "";
    public List<string> ExpectedAssertionFailures { get; set; } = new();
    public string EventOfInterest { get; set; } = "end_of_run";
}
public sealed class RunResult
{
    public IReadOnlyList<ResponseChainResult> ResponseChains { get; set; } = Array.Empty<ResponseChainResult>();
    public int SchemaVersion { get; set; } = 1;
    public string RunId { get; set; } = "";
    public string Scenario { get; set; } = "";
    public int ScenarioVersion { get; set; }
    public ulong Seed { get; set; }
    public string BuildId { get; set; } = "";
    public string ContentDigest { get; set; } = "";
    public ConfigurationData Configuration { get; set; } = new();
    public InformationConfigurationData? InformationConfiguration { get; set; }
    public PlannerConfigurationData? PlannerConfiguration { get; set; }
    public RoutineConfigurationData? RoutineConfiguration { get; set; }
    public AuthorityData? AuthorityConfiguration { get; set; }
    public int Population { get; set; }
    public int CompletedTiles { get; set; }
    public long SimulatedTicks { get; set; }
    public long SimulatedMilliseconds { get; set; }
    public string Status { get; set; } = "completed";
    public List<string> AssertionFailures { get; set; } = new();
    public string? Error { get; set; }
    public string Digest { get; set; } = "";
    public SortedDictionary<string, long> Metrics { get; set; } = new(StringComparer.Ordinal);
    public object Performance { get; set; } = new();
    public string Reproduce { get; set; } = "";
    public SortedDictionary<string, string> Artifacts { get; set; } = new(StringComparer.Ordinal);
}
public static class Runs
{
    public static string BuildId()
    {
        var assemblies = new[] { typeof(World).Assembly, typeof(ActionIntent).Assembly, typeof(Station.Mind.TravelEstimates).Assembly, typeof(Runs).Assembly };
        return World.Hash(Encoding.UTF8.GetBytes(string.Join("\n", assemblies.Select(a => World.Hash(File.ReadAllBytes(a.Location))))));
    }
    public static (RunResult Result, World World) Execute(LoadedScenario loaded, ulong seed, bool trace, string? outputDirectory = null, World? startingWorld = null)
    {
        bool audit = loaded.Scenario.InitialState.ResponseChainRoles.Count > 0;
        var world = startingWorld ?? loaded.CreateWorld(seed, trace || audit); if (audit) world.TraceEnabled = true;
        var checkpoint = Checkpoint.Capture(world); long startTick = world.LastProcessedTick;
        var s = loaded.Scenario.InitialState; string build = BuildId();
        var result = new RunResult { RunId = s.Id + "-" + seed, Scenario = s.Id, ScenarioVersion = s.SchemaVersion, Seed = seed,
            BuildId = build, ContentDigest = loaded.ContentDigest, Configuration = s.Configuration, InformationConfiguration = s.Information?.Configuration, PlannerConfiguration = s.Information?.Planner, Population = world.InspectActors().Count,
            CompletedTiles = world.Topology.InspectTiles().Count(t => t.Completed) };
        var timer = System.Diagnostics.Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            foreach (var assertion in loaded.Scenario.Assertions.OrderBy(a => a.Tick))
            {
                if (assertion.Tick < world.LastProcessedTick) continue;
                world.AdvanceTo(assertion.Tick);
                if (!Check(world, assertion)) result.AssertionFailures.Add($"tick {assertion.Tick}: {assertion.Kind} actor={assertion.ActorId} item={assertion.ItemId}");
            }
            world.AdvanceTo(s.Horizon); world.AssertAccounting();
            result.ResponseChains = ResponseChains.Check(world.Trace, s.ResponseChainRoles, startTick);
            foreach (var chain in result.ResponseChains) foreach (string failure in chain.Failures) result.AssertionFailures.Add("response chain " + chain.Role + ": " + failure);
            if (result.AssertionFailures.Count > 0) result.Status = "assertion_failed";
        }
        catch (Exception e) when (e is InvalidOperationException || e is OverflowException)
        { result.Status = "runtime_error"; result.Error = e.Message; }
        timer.Stop(); result.Digest = world.Digest(); result.SimulatedTicks = Math.Max(0, world.LastProcessedTick);
        result.SimulatedMilliseconds = checked(result.SimulatedTicks * world.Config.TickDurationMs);
        result.Metrics = new SortedDictionary<string, long>(StringComparer.Ordinal) { ["pathRequests"] = world.PathRequests, ["pathExpansions"] = world.PathExpansions,
            ["actionsCompleted"] = world.ActionsCompleted, ["actionsFailed"] = world.ActionsFailed, ["physicalItemStacks"] = world.InspectItems().Count };
        result.Metrics["constructionProjects"] = world.InspectConstructionProjects().Count; result.Metrics["constructionCompleted"] = world.InspectConstructionProjects().Count(p => p.Status == ConstructionStatus.Completed); result.Metrics["resourceClaims"] = world.InspectResourceClaims().Count;
        result.Metrics["qaDecisions"] = world.InspectQualityAssurance().Count;
        result.Metrics["qaSelected"] = world.InspectQualityAssurance().Count(q => q.Status != QualityAssuranceStatus.NotSampled);
        result.Metrics["qaReportedClear"] = world.InspectQualityAssurance().Count(q => q.Status == QualityAssuranceStatus.ReportedClear);
        result.Metrics["qaReportedDiscrepancy"] = world.InspectQualityAssurance().Count(q => q.Status == QualityAssuranceStatus.ReportedDiscrepancy);
        result.Metrics["auditsReadyToFile"] = world.InspectActors().Sum(a => world.InspectAudits(a.Id).Count(q => q.Status == AuditStatus.ReadyToFile));
        result.Metrics["auditsNeedsReview"] = world.InspectActors().Sum(a => world.InspectAudits(a.Id).Count(q => q.Status == AuditStatus.NeedsReview));
        result.Metrics["acceptedCases"] = world.InspectCases().Count;
        result.Metrics["reprimands"] = world.InspectReprimands().Count;
        result.Metrics["caseDecisionsRead"] = world.InspectActors().Sum(a => world.InspectCaseFilings(a.Id).Count(f => f.ReadTick >= 0));
        result.Metrics["credentialRecoveriesCompleted"] = world.InspectRecoveryAgents().Sum(a => a.Completed); result.Metrics["credentialRecoveriesPending"] = world.InspectRecoveryAgents().Count(a => a.Pending); result.Metrics["credentialRecoveryCases"] = world.InspectCredentialRecoveryCases().Count; result.Metrics["replacementCardsIssued"] = world.InspectCredentialRecoveryCases().Count(c => c.IssuedCardId > 0); result.Metrics["conversationsCompleted"] = world.InspectConversations().Count(c => c.Status == ConversationStatus.Completed); result.Metrics["conversationPartnerCandidatesExamined"] = world.ConversationPartnerCandidatesExamined; result.Metrics["usableHousingBerths"] = world.UsableHousingBerths; result.Metrics["vacantHousingBerths"] = world.VacantHousingBerths; result.Metrics["berthAllocations"] = world.InspectBerthAllocations().Count; result.Metrics["dailyGoalsCompleted"] = world.InspectDailyGoals().Count(g => g.Status == DailyGoalStatus.Completed); result.Metrics["dailyGoalsMissed"] = world.InspectDailyGoals().Count(g => g.Status == DailyGoalStatus.MissedWindow); result.Metrics["dailyGoalsBlocked"] = world.InspectDailyGoals().Count(g => g.Status == DailyGoalStatus.Pending && g.RetryAt > world.LastProcessedTick); result.Metrics["mealsConsumed"] = world.MealsConsumed; result.Metrics["needActivitiesCompleted"] = world.InspectNeedActivities().Count(a => a.Status == NeedActivityStatus.Completed); result.Metrics["needServiceQueued"] = world.InspectNeedServices().Sum(s => s.QueuedActionIds.Count); result.Metrics["needThresholdsReached"] = world.NeedThresholdsReached; result.Metrics["criticalNeedInterruptions"] = world.CriticalNeedInterruptions; result.Metrics["detentionRevocations"] = world.InspectDetentionRevocations().Count(v => v.Applied); result.Metrics["guardOutcomesReceived"] = world.InspectGuardOutcomes(true).Count; result.Metrics["detentionReservations"] = world.InspectDetentionFacilities().Sum(f => f.Reservations);
        result.Metrics["detainedOccupants"] = world.InspectDetentionFacilities().Sum(f => f.Occupants);
        result.Metrics["detentionsCompleted"] = world.InspectDetentionStays().Count(s => s.Status == DetentionStayStatus.Released);
        result.Metrics["escortGroupsStarted"] = world.InspectEscorts().Count;
        result.Metrics["escortsAwaitingIntake"] = world.InspectEscorts().Count(e => e.Status == EscortStatus.AwaitingIntake);
        result.Metrics["escortsInterrupted"] = world.InspectEscorts().Count(e => e.Status == EscortStatus.Interrupted);
        result.Metrics["localGuardTeamsFormed"] = world.InspectLocalGuardTeams().Count(t => t.FormedTick >= 0);
        result.Metrics["guardSearchInspections"] = world.InspectActors().Sum(a => world.InspectLocalGuardOrders(a.Id).Sum(l => l.SearchObservations.Count));
        result.Metrics["guardsWithLocatedTarget"] = world.InspectActors().Sum(a => world.InspectLocalGuardOrders(a.Id).Count(l => l.Status == GuardOrderStatus.TargetLocated));
        result.Metrics["equippedActors"] = world.InspectEquipment().Count;
        result.Metrics["guardsAtRendezvous"] = world.InspectActors().Sum(a => world.InspectLocalGuardOrders(a.Id).Count(l => l.Status == GuardOrderStatus.AtRendezvous));
        result.Metrics["escortTeamsDispatched"] = world.InspectEscortDispatches().Count(d => d.Payload != null);
        result.Metrics["escortTeamsAwaitingGuards"] = world.InspectEscortDispatches().Count(d => d.Status == EscortDispatchStatus.AwaitingGuards);
        result.Metrics["guardOrdersRead"] = world.InspectActors().Sum(a => world.InspectLocalGuardOrders(a.Id).Count);
        result.Metrics["guardAcknowledgmentsReceived"] = world.InspectGuardAssignments().Count(a => a.AcknowledgmentMessageId != 0);
        result.Metrics["detentionRequests"] = world.InspectDetentionRequests().Count;
        result.Metrics["detentionAuthorizations"] = world.InspectDetentionAuthorizations().Count;
        result.Metrics["activeDetentionAuthorizations"] = world.InspectDetentionAuthorizations().Count(o => o.Status == DetentionAuthorizationStatus.Authorized);
        result.Metrics["detentionRequestsWaiting"] = world.InspectDetentionRequests().Count(r => r.Decision.State == DetentionDecisionState.Waiting);
        result.Metrics["machineFaultEvents"] = world.InspectMachineWear().Sum(m => m.FaultEvents);
        result.Metrics["faultConsumedParts"] = world.InspectMachineWear().Sum(m => m.ConsumedParts);
        result.Metrics["dirtUnits"] = world.InspectDirt().Sum(d => d.Units);
        result.Metrics["dirtUnitsAdded"] = world.InspectDirt().Sum(d => d.AddedUnits);
        result.Metrics["dirtUnitsRemoved"] = world.InspectDirt().Sum(d => d.RemovedUnits);
        result.Metrics["facilityTasks"] = world.InspectFacilityTasks().Count;
        result.Metrics["facilityTasksPending"] = world.InspectFacilityTasks().Count(t => t.Status == FacilityTaskStatus.Pending);
        result.Metrics["facilityTasksReportedComplete"] = world.InspectFacilityTasks().Count(t => t.Status == FacilityTaskStatus.ReportedComplete);
        result.Metrics["facilityTasksNeedAttention"] = world.InspectFacilityTasks().Count(t => t.Status == FacilityTaskStatus.NeedsAttention);
        result.Metrics["resourceTransactions"] = world.InspectResourceTransactions().Count;
        result.Metrics["stackChanges"] = world.InspectStackChanges().Count;
        result.Metrics["projectStashes"] = world.InspectStashes().Count;
        result.Metrics["sensorReadings"] = world.InspectSensorReadings().Count;
        result.Metrics["sensorDeliveries"] = world.InspectSensorReadings().Count(r => r.Delivered);
        result.RoutineConfiguration = s.Routines?.Configuration;
        result.AuthorityConfiguration = s.Authority;
        result.Metrics["reportExpectations"] = world.InspectExpectations().Count;
        result.Metrics["reportExpectationsSatisfied"] = world.InspectExpectations().Count(e => e.ResultMessageId != 0);
        result.Metrics["missingReportsOpened"] = world.InspectMissingReports().Count;
        result.Metrics["incidentsAdmitted"] = world.InspectMissingReports().Count(i => i.AdmissionBucket >= 0);
        result.Metrics["replacementOrders"] = world.InspectMissingReports().Count(i => i.ReplacementAttemptId > 0);
        result.Metrics["escalationDeferred"] = world.InspectMissingReports().Count(i => i.WorkResponse == IncidentWorkResponse.EscalationDeferred || i.PatrolReason == "EscalationDeferred");
        result.Metrics["replacementResponseWaiting"] = world.InspectMissingReports().Count(i => i.WorkResponse is IncidentWorkResponse.Pending or IncidentWorkResponse.NoEligibleResponder or IncidentWorkResponse.EscalationDeferred);
        result.Metrics["workerStatusPending"] = world.InspectMissingReports().Count(i => i.WorkerStatusResponse != "ReportedFound");
        result.Metrics["patrolOrders"] = world.InspectPatrolOrders().Count;
        result.Metrics["patrolResultsReceived"] = world.InspectPatrolOrders().Count(p => p.ResultMessageId > 0);
        result.Metrics["patrolCandidatesInspected"] = world.InspectActors().Sum(a => (long)world.InspectLocalPatrols(a.Id).Sum(p => p.Observations.Count));
        result.Metrics["incidentsResolvedByReports"] = world.InspectMissingReports().Count(i => i.WorkerStatusResponse == "ReportedFound" && i.WorkResolution == "ReportedComplete");
        result.Metrics["replacementResultsReceived"] = world.InspectMissingReports().Count(i => i.ReplacementResultMessageId > 0);
        result.Metrics["acknowledgmentsOverdue"] = world.InspectAcknowledgmentWatches().Count(w => w.Status == AcknowledgmentStatus.Overdue);
        result.Metrics["rosterVersions"] = world.InspectRosters().Count;
        result.Metrics["offDutyAtRest"] = world.InspectRoutines().Count(r => r.Activity == "OffDuty");
        result.Metrics["operationalMachines"] = world.InspectMachines().Count(m => m.Operational);
        result.Metrics["repairGoalsCompleted"] = world.InspectRepairAgents().Sum(a => a.CompletedRepairs);
        result.Metrics["repairGoalsPending"] = world.InspectRepairAgents().Sum(a => (long)a.PendingMachines.Count);
        result.Metrics["jobsPhysicallyCompleted"] = world.InspectJobs().Count(j => j.PhysicalStatus == WorkExecution.CompletedPhysically);
        result.Metrics["workAttempts"] = world.InspectWorkAttempts().Count;
        result.Metrics["ordersReadPrivileged"] = world.InspectActors().Sum(a => (long)world.InspectLocalOrders(a.Id).Count);
        result.Metrics["checkInsCompleted"] = world.InspectCheckIns().Sum(c => c.Completed);
        if(world.Information != null)
        {
            result.Metrics["knowledgeEvictions"]=world.KnowledgeEvictions; result.Metrics["maximumTerminalQueue"]=world.MaximumTerminalQueue;result.Metrics["totalTerminalWaitTicks"]=world.TotalTerminalWaitTicks;
            result.Metrics["messagesPosted"]=world.InspectMessages().Count(m=>!m.Seeded);result.Metrics["messagesRead"]=world.InspectMessages().Count(m=>m.FirstReadTick.HasValue);
            result.Metrics["unreadMessages"]=world.InspectMessages().Count(m=>m.Stage==MessageStage.DeliveredToMailbox);result.Metrics["beliefUpdates"]=world.InspectBeliefHistory().Count;
            result.Metrics["plannerRequests"]=world.PlannerRequests;result.Metrics["plannerExpansions"]=world.PlannerExpansions;result.Metrics["plansFound"]=world.PlansFound;result.Metrics["plansUnavailable"]=world.PlansUnavailable;result.Metrics["plansExhausted"]=world.PlansExhausted;
        }
        result.Performance = new { elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, allocatedBytesCurrentThread = GC.GetAllocatedBytesForCurrentThread() - allocated,
            managedHeapBytesProcessApproximate = GC.GetTotalMemory(false), runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, logicalCores = Environment.ProcessorCount,
            configuration = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration, traceLevel = trace ? "detailed" : "none" };
        if (outputDirectory != null)
        {
            string dir = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(dir);
            result.Artifacts["checkpoint"] = Path.Combine(dir, "checkpoint.json"); result.Artifacts["snapshot"] = Path.Combine(dir, "snapshot.json");
            result.Artifacts["replay"] = Path.Combine(dir, "replay.json"); result.Artifacts["trace"] = Path.Combine(dir, "trace.jsonl");
            result.Reproduce = $"dotnet run --project src/Station.Runner -c Release --no-build -- replay \"{result.Artifacts["replay"]}\" --trace detailed";
            File.WriteAllText(result.Artifacts["checkpoint"], JsonData.Write(Checkpoint.Capture(world)));
            File.WriteAllText(result.Artifacts["snapshot"], JsonData.Write(ViewSnapshot(checkpoint.Restore(), world.LastProcessedTick)));
            // The bundle starts after this checkpoint. Seed/prior events belong
            // to its saved state and cannot be replayed as new events.
            File.WriteAllLines(result.Artifacts["trace"], world.Trace.Where(e => trace && e.Tick > startTick).Select(EventJson));
            if (audit)
            {
                result.Artifacts["responseChains"] = Path.Combine(dir, "response-chains.json");
                File.WriteAllText(result.Artifacts["responseChains"], JsonData.Write(result.ResponseChains));
            }
            var bundle = new ReplayBundle { BuildId = build, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ContentDigest = loaded.ContentDigest, ScenarioJson = loaded.ScenarioJson, CatalogJson = loaded.CatalogJson, Start = checkpoint, Horizon = s.Horizon,
                ExpectedDigest = result.Digest, ExpectedStatus = result.Status, ExpectedAssertionFailures = result.AssertionFailures,
                EventOfInterest = result.Error ?? (result.AssertionFailures.Count > 0 ? result.AssertionFailures[0] : "end_of_run") };
            File.WriteAllText(result.Artifacts["replay"], JsonData.Write(bundle));
            File.WriteAllText(Path.Combine(dir, "result.json"), JsonData.Write(result));
        }
        return (result, world);
    }
    private static bool Check(World w, AssertionData a)
    {
        if (a.Kind == "GrowthTarget") return w.InspectGrowth()?.Target == a.Quantity; if (a.Kind == "GrowthBelievedBerths") return w.InspectGrowth()?.BelievedCompletedBerths == a.Quantity; if (a.Kind == "GrowthPendingBerths") return w.InspectGrowth()?.CommittedPendingBerths == a.Quantity; if (a.Kind == "GrowthCommitments") return w.InspectGrowth()?.Commitments.Count == a.Quantity; if (a.Kind == "Construction") return w.InspectConstructionProjects().Any(p => p.Id == a.EntityId && p.Status.ToString() == a.State); if (a.Kind == "TileCompleted") return w.Topology.Contains(a.Position!.ToCoord()) == (a.State == "Completed"); if (a.Kind == "HousingBerths") return w.UsableHousingBerths == a.Quantity; if (a.Kind == "RecoveryAgent") return w.InspectRecoveryAgents().Any(agent => agent.ActorId == a.ActorId && (a.State == "Pending" ? agent.Pending && agent.Completed == a.Quantity : !agent.Pending && agent.Completed == a.Quantity)); if (a.Kind == "CredentialRecoveryCases") return w.InspectCredentialRecoveryCases().Count(c => (c.IssuedCardId == 0 ? "Authorized" : "Issued") == a.State && (a.ActorId == 0 || c.ActorId == a.ActorId)) == a.Quantity; if (a.Kind == "Sessions") return w.InspectSessions().Count(s => s.Scope.ToString() == a.State && (a.ActorId == 0 || s.ActorId == a.ActorId)) == a.Quantity; if (a.Kind == "Conversations") return w.InspectConversations().Count(c => c.Status.ToString() == a.State && (a.ActorId == 0 || c.FirstActorId == a.ActorId || c.SecondActorId == a.ActorId)) == a.Quantity; if (a.Kind == "DailyGoals") return w.InspectDailyGoals().Count(g => g.Status.ToString() == a.State && (a.ActorId == 0 || g.ActorId == a.ActorId)) == a.Quantity; if (a.Kind == "NeedService") return w.InspectNeedServices().Any(s => s.Definition.Id == a.EntityId && (a.State == "Queued" ? s.QueuedActionIds.Count : s.OccupantActionIds.Count) == a.Quantity); if (a.Kind == "NeedActivities") return w.InspectNeedActivities().Count(n => n.Status.ToString() == a.State && (a.ActorId == 0 || n.ActorId == a.ActorId)) == a.Quantity; if (a.Kind == "NeedQ") return w.InspectOwnNeeds(new(a.ActorId)).Any(n => n.Kind.ToString() == a.State && n.Q == a.Quantity); if (a.Kind == "Expectation") { var e = w.InspectExpectations().SingleOrDefault(e => e.AttemptId == a.AttemptId); return a.State == "Absent" ? e == null : e?.Status.ToString() == a.State; }
        if (a.Kind == "Acknowledgment") return w.InspectAcknowledgmentWatches().Any(e => e.AttemptId == a.AttemptId && e.Status.ToString() == a.State);
        if (a.Kind == "MissingReport") return w.InspectMissingReports().Any(e => e.AttemptId == a.AttemptId) == (a.State == "Present");
        if (a.Kind == "Dirt") return w.InspectDirt().Any(d => d.Position == a.Position!.ToCoord() && d.Units == a.Quantity);
        if (a.Kind == "FacilityTask") { var t = w.InspectFacilityTasks().SingleOrDefault(t => t.JobId == a.JobId); return a.State == "Absent" ? t == null : t?.Status.ToString() == a.State; }
        if (a.Kind == "QualityAssurance") { var q = w.InspectQualityAssurance().SingleOrDefault(q => q.RepairAttemptId == a.AttemptId); return a.State == "Absent" ? q == null : q?.Status.ToString() == a.State; }
        if (a.Kind == "Audit") { var q = w.InspectAudits(new(a.ActorId)).SingleOrDefault(q => q.VerificationAttemptId == a.AttemptId); return a.State == "Absent" ? q == null : q?.Status.ToString() == a.State; }
        if (a.Kind == "CaseFiling") { var f = w.InspectCaseFilings(new(a.ActorId)).SingleOrDefault(f => f.VerificationAttemptId == a.AttemptId); return a.State == "Absent" ? f == null : f?.Status.ToString() == a.State; }
        if (a.Kind == "ReprimandCount") return w.InspectReprimands().Count(p => p.AccountId == a.AccountId) == a.Quantity;
        if (a.Kind == "QualifyingReprimands") return w.QualifyingReprimandCount(a.AccountId, w.LastProcessedTick) == a.Quantity;
        if (a.Kind == "GuardOutcomeReceived") return w.InspectGuardOutcomes(true).Count(o => o.Outcome == a.State && (a.ActorId == 0 || a.ActorId == o.ActorId)) == a.Quantity; if (a.Kind == "KnownGuardRevocations") return w.InspectKnownGuardRevocations().Count(o => a.ActorId == 0 || w.InspectLocalGuardOrders(new(a.ActorId)).Any(l => l.OrderMessageId == o.OrderMessageId)) == a.Quantity; if (a.Kind == "DetentionStay") { var stay = w.InspectDetentionStays().SingleOrDefault(s => s.AuthorizationId == a.EntityId); return a.State == "Absent" ? stay == null : stay?.Status.ToString() == a.State; }
        if (a.Kind == "DetentionFacility") { var facility = w.InspectDetentionFacilities().SingleOrDefault(f => f.Definition.Id == a.EntityId); return facility != null && (a.State == "Reservations" ? facility.Reservations : a.State == "Occupants" ? facility.Occupants : facility.Operations) == a.Quantity; }
        if (a.Kind == "EscortGroup") { var group = w.InspectEscorts().SingleOrDefault(e => e.AuthorizationId == a.EntityId); return a.State == "Absent" ? group == null : group != null && group.Status.ToString() == a.State && group.Members.Count == a.Quantity; }
        if (a.Kind == "LocalGuardTeam") { var team = w.InspectLocalGuardTeams().SingleOrDefault(t => t.AuthorizationId == a.EntityId); return a.State == "Absent" ? team == null : team != null && (a.State == "Formed" ? team.FormedTick >= 0 : team.FormedTick < 0) && team.Members.Count == a.Quantity; }
        if (a.Kind == "Equipped") return w.ActorView(new(a.ActorId)).EquippedItem.Value == a.ItemId;
        if (a.Kind == "EscortDispatch") return w.InspectEscortDispatches().Any(d => d.AuthorizationId == a.EntityId && d.Status.ToString() == a.State);
        if (a.Kind == "LocalGuardOrder") { var orders = w.InspectLocalGuardOrders(new(a.ActorId)); return a.State == "Absent" ? orders.Count == 0 : orders.Any(o => o.Status.ToString() == a.State); }
        if (a.Kind == "DetentionAuthorizations") return w.InspectDetentionAuthorizations().Count(o => o.AccountId == a.AccountId && (a.State.Length == 0 || o.Status.ToString() == a.State)) == a.Quantity;
        if (a.Kind == "LocalDetentionRequest") { var requests = w.InspectLocalDetentionRequests(new(a.ActorId)).Where(r => r.AccountId == a.AccountId).ToArray(); return a.State == "Absent" ? requests.Length == 0 : requests.Any(r => r.Status.ToString() == a.State); }
        if (a.Kind == "WorkerStatus") return w.InspectMissingReports().Any(e => e.AttemptId == a.AttemptId && e.WorkerStatusResponse == a.State);
        if (a.Kind == "Accounting") { w.AssertAccounting(); return true; }
        if (a.Kind is "KnownRecordValue" or "UnknownRecordValue")
        {
            var facts = w.InspectKnowledge(new(a.ActorId)).Where(f => f.Key.SubjectKind == SubjectKind.Record).GroupBy(f => f.Key.SubjectId);
            bool known = facts.Any(group => group.Any(f => f.Key.Attribute == AttributeKind.RecordType && f.Value.Text == a.RecordKind.ToString()) &&
                group.Any(f => f.Key.Attribute == AttributeKind.RecordEntityId && f.Value.Number == a.RecordEntityId) && group.Any(f => f.Key.Attribute == a.RecordAttribute && f.Value.Equals(a.Value!.ToValue())));
            return a.Kind == "KnownRecordValue" ? known : !known;
        }
        if (a.Kind == "JobPhysical") return w.InspectJobs().Any(j => j.Id == a.JobId && j.PhysicalStatus.ToString() == a.State);
        if (a.Kind == "JobAuthority") return w.InspectJobs().Any(j => j.Id == a.JobId && j.AuthorityStatus == a.State);
        if (a.Kind == "AttemptExecution") return w.InspectWorkAttempts().Any(t => t.Id == a.AttemptId && t.Execution.ToString() == a.State);
        if (a.Kind == "LocalOrder") return w.InspectLocalOrders(new(a.ActorId)).Any(o => o.AttemptId == a.AttemptId && o.Status.ToString() == a.State);
        if (a.Kind == "MachineOperational") return w.InspectMachines().Any(m => m.Id == a.MachineId && m.Operational == a.Operational);
        if (a.Kind == "ItemAbsent") return w.InspectItems().All(i => i.Id.Value != a.ItemId);
        if (a.Kind == "ItemState") return w.InspectItems().Any(i => i.Id.Value == a.ItemId && i.Quantity == a.Quantity && i.Location.Kind == a.LocationKind && i.Location.Owner == a.Owner);
        if(a.Kind is "KnowledgeValue" or "KnowledgeAbsent")
        { var fact=w.InspectKnowledge(new(a.ActorId)).SingleOrDefault(f=>f.Key.Equals(a.Fact!.ToKey()));return a.Kind=="KnowledgeAbsent"?fact==null:fact!=null&&fact.Value.Equals(a.Value!.ToValue()); }
        if(a.Kind is "BeliefValue" or "BeliefAbsent")
        { var belief=w.InspectBeliefs().SingleOrDefault(b=>b.Key.Equals(a.Fact!.ToKey()));return a.Kind=="BeliefAbsent"?belief==null:belief!=null&&belief.Value.Equals(a.Value!.ToValue()); }
        if(a.Kind=="MessageStage")return w.InspectMessages().Any(m=>m.Id==a.MessageId&&m.Stage==a.Stage);
        if (a.Kind is "ItemAtTile" or "ItemHeld")
        {
            var item = w.InspectItems().SingleOrDefault(i => i.Id.Value == a.ItemId); if (item == null) return false;
            return a.Kind == "ItemHeld" ? item.Location.Kind == LocationKind.ActorInventory && item.Location.Owner == a.ActorId
                : item.Location.Kind == LocationKind.TileContainer && w.Topology.InspectTiles().Single(t => t.Id.Value == item.Location.Owner).Position == a.Position!.ToCoord();
        }
        var actor = w.InspectActor(new Id<ActorTag>(a.ActorId));
        return a.Kind switch { "ActorAt" => !actor.InTransit && actor.Position == a.Position!.ToCoord(), "ActorInTransit" => actor.InTransit, "LastFailure" => actor.LastFailure == a.Reason, _ => false };
    }
    public static object ViewSnapshot(World world, long horizon) => new { schemaVersion = 1, kind = "viewer_snapshot", startTick = world.LastProcessedTick, horizon,
        checkpoint = Checkpoint.Capture(world), tiles = world.Topology.InspectTiles().Select(t => new { id = t.Id.Value, definition = t.DefinitionId, position = CoordinateData.From(t.Position), completed = t.Completed, traversalTicks = world.Topology.TraversalTicks(t.Position) }),
        actors = world.InspectActors().Select(a => new { id = a.Id.Value, name = a.Name, role = a.Role, position = CoordinateData.From(a.Position), a.InTransit, source = CoordinateData.From(a.Source), destination = CoordinateData.From(a.Destination), a.Departure, a.Arrival }),
        items = world.InspectItems().Select(i => new { id = i.Id.Value, i.Resource, i.Quantity, i.Location }),
        knowledge=world.InspectActors().Select(a=>new { actorId=a.Id.Value,facts=world.InspectKnowledge(a.Id) }), beliefs=world.InspectBeliefs(),messages=world.InspectMessages(),sessions=world.InspectSessions(),terminals=world.InspectTerminals(),agents=world.InspectAgents(), repairs=world.InspectRepairAgents(), machines=world.InspectMachines(), claims=world.InspectResourceClaims(), resourceTransactions=world.InspectResourceTransactions(), jobs=world.InspectJobs(), attempts=world.InspectWorkAttempts(), localOrders=world.InspectActors().SelectMany(a=>world.InspectLocalOrders(a.Id)),checkIns=world.InspectCheckIns(), qualityAssurance=world.InspectQualityAssurance(), verifications=world.InspectActors().SelectMany(a=>world.InspectVerifications(a.Id)), audits=world.InspectActors().SelectMany(a=>world.InspectAudits(a.Id)), cases=world.InspectCases(), reprimands=world.InspectReprimands(), caseFilings=world.InspectActors().SelectMany(a=>world.InspectCaseFilings(a.Id)), queryDisclosures=world.InspectQueryDisclosures(), constructionProjects=world.InspectConstructionProjects(),constructionSurveys=world.InspectConstructionSurveys(),growth=world.InspectGrowth(),recoveryAgents=world.InspectRecoveryAgents(),credentialRecoveryCases=world.InspectCredentialRecoveryCases(),conversations=world.InspectConversations(),berths=world.InspectBerths(),berthAllocations=world.InspectBerthAllocations(),dailyGoals=world.InspectDailyGoals(),needs=world.InspectNeeds(),needAgents=world.InspectNeedAgents(),needServices=world.InspectNeedServices(),needActivities=world.InspectNeedActivities(),detentionRevocations=world.InspectDetentionRevocations(),knownGuardRevocations=world.InspectKnownGuardRevocations(),guardOutcomes=world.InspectGuardOutcomes(),guardOutcomeReceipts=world.InspectGuardOutcomes(true),detentionFacilities=world.InspectDetentionFacilities(),detentionStays=world.InspectDetentionStays(),escorts=world.InspectEscorts(),localGuardTeams=world.InspectLocalGuardTeams(), equipment=world.InspectEquipment(), escortDispatches=world.InspectEscortDispatches(), guardAssignments=world.InspectGuardAssignments(), localGuardOrders=world.InspectActors().SelectMany(a=>world.InspectLocalGuardOrders(a.Id)), detentionRequests=world.InspectDetentionRequests(), detentionAuthorizations=world.InspectDetentionAuthorizations(), localDetentionRequests=world.InspectActors().SelectMany(a=>world.InspectLocalDetentionRequests(a.Id)), machineWear=world.InspectMachineWear(), faultSinks=world.InspectFaultSinks(), dirt=world.InspectDirt(), facilityTasks=world.InspectFacilityTasks(), facilityObservations=world.InspectActors().SelectMany(a=>world.InspectFacilityObservations(a.Id)) };
    public static string EventJson(TraceEvent e) => JsonData.Write(new { e.Id, e.Tick, e.Type, e.Visibility, e.ActorId, e.ActionId, e.ItemId, e.OtherItemId, e.Quantity,
        source = CoordinateData.From(e.Source), target = CoordinateData.From(e.Target), e.Departure, e.Arrival, e.Detail, e.Origin, e.CauseId, e.MessageId, e.TerminalId, e.SessionId, e.AccountId,e.FactKey,e.FactValue,e.TaskId,e.AttemptId,e.SensorId,e.ReadingId });
}
