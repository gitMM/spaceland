using System.Globalization;
using Station.Contracts;
using Station.Core;

namespace Station.Runner;

public static class Program
{
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) when (e is ArgumentException or IOException or FormatException or System.Text.Json.JsonException or ScenarioException or OverflowException)
        { Console.WriteLine(JsonData.Write(new { schemaVersion = 1, status = "invalid_scenario", error = e.Message })); Console.Error.WriteLine(e.Message); return 2; }
        catch (Exception e)
        { Console.WriteLine(JsonData.Write(new { schemaVersion = 1, status = "runtime_error", error = e.Message })); Console.Error.WriteLine(e); return 3; }
    }
    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "help")
        { Console.Error.WriteLine("Station.Runner validate|run|batch SCENARIO | replay BUNDLE | inspect CHECKPOINT | benchmark daily|population|recovery|institutional|pressure --count N --seed N --out DIR | generate-construction TEMPLATE --variant NAME --seed N --out DIR | timeline SCENARIO|pressure|recovery|institutional|daily --count N --seed N --every TICKS --out DIR\nOptions: --seed N --count N --parallel N --out DIR --trace none|detailed|focused --chain EVENT --actor ID --view actor|knowledge|mailbox|sessions|beliefs|agents|repairs|machines|wear|qa|verifications|audits|cases|reprimands|case-filings|disclosures|detention-requests|detention-local|detention-authorizations|escorts|guard-assignments|guard-local|guard-teams|escort-groups|detention-facilities|detention-stays|needs|need-services|need-activities|need-agents|daily-goals|conversations|recovery-agents|recovery-cases|recovery-mailbox|berths|berth-allocations|revocations|guard-revocations|guard-outcomes|guard-receipts|equipment|dirt|facility|facility-local|claims|resources|stacks|sensors|perception|routines|rosters|jobs|attempts|orders|checkins|work-history|construction|construction-surveys|growth|record-history|query|expectations|acknowledgments|incidents|admission|patrols|patrol-local|authority|all\nSee docs/acceptance-current.md for implemented scope and open release gates."); return 0; }
        if (args.Length < 2) throw new ArgumentException("An input file is required.");
        string operation = args[0], input = args[1]; var opts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 2; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !new[] { "--seed", "--count", "--parallel", "--out", "--trace", "--actor", "--view", "--chain", "--variant", "--every" }.Contains(args[i]) || !opts.TryAdd(args[i], args[i + 1])) throw new ArgumentException($"Invalid or duplicate option {args[i]}.");
        }
        string? Option(string name) => opts.GetValueOrDefault(name);
        string traceLevel = Option("--trace") ?? (operation == "batch" ? "none" : "detailed");
        if (traceLevel is not ("none" or "detailed" or "focused")) throw new ArgumentException("--trace must be none, detailed or focused.");
        if(traceLevel=="focused"&&(operation!="replay"||Option("--chain")==null))throw new ArgumentException("Focused tracing requires replay --chain EVENT.");
        bool trace = traceLevel != "none";
        if (operation == "inspect")
        {
            var world = JsonData.Read<Checkpoint>(File.ReadAllText(input), input).Restore(directory: Path.GetDirectoryName(Path.GetFullPath(input)));
            string view=Option("--view")??(Option("--actor")==null?"all":"actor");
            Id<ActorTag> Actor()=>new(long.Parse(Option("--actor")??throw new ArgumentException("This view requires --actor ID."),CultureInfo.InvariantCulture));
            if (view == "record-history") { Console.WriteLine(JsonData.Write(world.InspectRecordHistory())); return 0; }
            if (view == "stacks") { Console.WriteLine(JsonData.Write(new { changes = world.InspectStackChanges(), stashes = world.InspectStashes() })); return 0; }
            if (view == "sensors") { Console.WriteLine(JsonData.Write(new { sensors = world.InspectSensors(), readings = world.InspectSensorReadings() })); return 0; }
            if (view == "perception") { Console.WriteLine(JsonData.Write(world.InspectPerceptionRules())); return 0; }
            if (view == "qa") { Console.WriteLine(JsonData.Write(world.InspectQualityAssurance())); return 0; }
            if (view == "audits") { Console.WriteLine(JsonData.Write(world.InspectAudits(Actor()))); return 0; }
            if (view == "cases") { Console.WriteLine(JsonData.Write(world.InspectCases())); return 0; }
            if (view == "reprimands") { Console.WriteLine(JsonData.Write(world.InspectReprimands())); return 0; }
            if (view == "case-filings") { Console.WriteLine(JsonData.Write(world.InspectCaseFilings(Actor()))); return 0; }
            if (view == "disclosures") { Console.WriteLine(JsonData.Write(world.InspectQueryDisclosures())); return 0; }
            if (view == "detention-facilities") { Console.WriteLine(JsonData.Write(world.InspectDetentionFacilities())); return 0; }
            if (view == "recovery-agents") { Console.WriteLine(JsonData.Write(world.InspectRecoveryAgents())); return 0; } if (view == "recovery-cases") { Console.WriteLine(JsonData.Write(world.InspectCredentialRecoveryCases())); return 0; } if (view == "recovery-mailbox") { Console.WriteLine(JsonData.Write(world.InspectRecoveryMailbox(world.ActorView(Actor()).Account))); return 0; } if (view == "conversations") { Console.WriteLine(JsonData.Write(world.InspectConversations())); return 0; } if (view == "berths") { Console.WriteLine(JsonData.Write(world.InspectBerths())); return 0; } if (view == "berth-allocations") { Console.WriteLine(JsonData.Write(world.InspectBerthAllocations())); return 0; } if (view == "daily-goals") { Console.WriteLine(JsonData.Write(world.InspectDailyGoals())); return 0; } if (view == "need-agents") { Console.WriteLine(JsonData.Write(world.InspectNeedAgents())); return 0; } if (view == "need-services") { Console.WriteLine(JsonData.Write(world.InspectNeedServices())); return 0; } if (view == "need-activities") { Console.WriteLine(JsonData.Write(world.InspectNeedActivities())); return 0; } if (view == "needs") { Console.WriteLine(JsonData.Write(world.InspectNeeds())); return 0; } if (view == "revocations") { Console.WriteLine(JsonData.Write(world.InspectDetentionRevocations())); return 0; } if (view == "guard-revocations") { Console.WriteLine(JsonData.Write(world.InspectKnownGuardRevocations())); return 0; } if (view == "guard-outcomes") { Console.WriteLine(JsonData.Write(world.InspectGuardOutcomes())); return 0; } if (view == "guard-receipts") { Console.WriteLine(JsonData.Write(world.InspectGuardOutcomes(true))); return 0; } if (view == "detention-stays") { Console.WriteLine(JsonData.Write(world.InspectDetentionStays())); return 0; }
            if (view == "escort-groups") { Console.WriteLine(JsonData.Write(world.InspectEscorts())); return 0; }
            if (view == "guard-teams") { Console.WriteLine(JsonData.Write(world.InspectLocalGuardTeams())); return 0; }
            if (view == "equipment") { Console.WriteLine(JsonData.Write(world.InspectEquipment())); return 0; }
            if (view == "escorts") { Console.WriteLine(JsonData.Write(world.InspectEscortDispatches())); return 0; }
            if (view == "guard-assignments") { Console.WriteLine(JsonData.Write(world.InspectGuardAssignments())); return 0; }
            if (view == "guard-local") { Console.WriteLine(JsonData.Write(world.InspectLocalGuardOrders(Actor()))); return 0; }
            if (view == "detention-requests") { Console.WriteLine(JsonData.Write(world.InspectDetentionRequests())); return 0; }
            if (view == "detention-local") { Console.WriteLine(JsonData.Write(world.InspectLocalDetentionRequests(Actor()))); return 0; }
            if (view == "detention-authorizations") { Console.WriteLine(JsonData.Write(world.InspectDetentionAuthorizations())); return 0; }
            if (view == "verifications") { Console.WriteLine(JsonData.Write(world.InspectVerifications(Actor()))); return 0; }
            if (view == "wear") { Console.WriteLine(JsonData.Write(new { configuration = world.MachineWear, machines = world.InspectMachineWear(), sinks = world.InspectFaultSinks() })); return 0; }
            if (view == "dirt") { Console.WriteLine(JsonData.Write(world.InspectDirt())); return 0; }
            if (view == "facility") { Console.WriteLine(JsonData.Write(world.InspectFacilityTasks())); return 0; }
            if (view == "facility-local") { Console.WriteLine(JsonData.Write(world.InspectFacilityObservations(Actor()))); return 0; }
            if (view == "routines") { Console.WriteLine(JsonData.Write(new { configuration = world.Routines, citizens = world.InspectRoutines() })); return 0; }
            if (view == "rosters") { Console.WriteLine(JsonData.Write(world.InspectRosters())); return 0; }
            if (view == "expectations") { Console.WriteLine(JsonData.Write(new { expectations = world.InspectExpectations(), learned = world.InspectLearnedDurations() })); return 0; }
            if (view == "acknowledgments") { Console.WriteLine(JsonData.Write(world.InspectAcknowledgmentWatches())); return 0; }
            if (view == "incidents") { Console.WriteLine(JsonData.Write(world.InspectMissingReports())); return 0; }
            if (view == "admission") { Console.WriteLine(JsonData.Write(world.InspectAdmissionBuckets())); return 0; }
            if (view == "patrols") { Console.WriteLine(JsonData.Write(world.InspectPatrolOrders())); return 0; }
            if (view == "patrol-local") { Console.WriteLine(JsonData.Write(world.InspectLocalPatrols(Actor()))); return 0; }
            if (view == "authority") { Console.WriteLine(JsonData.Write(world.AuthorityView())); return 0; }
            if (view == "query") { Console.WriteLine(JsonData.Write(world.ActorView(Actor()).LastRecordQuery)); return 0; }
            object data=view switch {"actor"=>world.InspectActor(Actor()),"knowledge"=>world.InspectKnowledge(Actor()),"mailbox"=>world.InspectMailbox(world.ActorView(Actor()).Account),"sessions"=>world.InspectSessions(),"beliefs"=>world.InspectBeliefs(),"agents"=>world.InspectAgents(),"repairs"=>world.InspectRepairAgents(),"construction"=>world.InspectConstructionProjects(),"construction-surveys"=>world.InspectConstructionSurveys(),"growth"=>world.InspectGrowth() ?? (object)"Disabled","machines"=>world.InspectMachines(),"claims"=>world.InspectResourceClaims(),"resources"=>new {items=world.InspectItems(),transactions=world.InspectResourceTransactions()},"jobs"=>world.InspectJobs(),"attempts"=>world.InspectWorkAttempts(),"orders"=>world.InspectLocalOrders(Actor()),"checkins"=>world.InspectCheckIns(),"work-history"=>world.InspectWorkHistory(),"all"=>Runs.ViewSnapshot(world,world.LastProcessedTick),_=>throw new ArgumentException("Unknown inspection view.")};
            Console.WriteLine(JsonData.Write(data));
            return 0;
        }
        if (operation == "timeline")
        {
            int Count(int fallback) => Option("--count") is string c ? int.Parse(c, CultureInfo.InvariantCulture) : fallback;
            ulong generatorSeed = Option("--seed") is string g ? ulong.Parse(g, CultureInfo.InvariantCulture) : 42;
            var source = input switch
            {
                "pressure" => PressureStation.Create(Count(32), generatorSeed),
                "recovery" => RecoveryStation.Create(Count(32), generatorSeed),
                "institutional" => InstitutionalStation.Create(Count(32), generatorSeed, Option("--variant") ?? "normal"),
                "daily" => StationBaseline.Create(Count(12), 2, generatorSeed),
                _ => LoadedScenario.Load(input)
            };
            return Timeline.Run(source, Option);
        }
        if (operation == "replay")
        {
            var bundle = JsonData.Read<ReplayBundle>(File.ReadAllText(input), input);
            if (bundle.SchemaVersion != 1 || bundle.BuildId != Runs.BuildId() || bundle.MovementModel != World.MovementModel || bundle.RandomModel != World.RandomModel)
                throw new ArgumentException("Replay schema/build/model mismatch. Use the original build.");
            var loaded = new LoadedScenario(bundle.ScenarioJson, bundle.CatalogJson, input + " (embedded scenario)");
            if (loaded.ContentDigest != bundle.ContentDigest || loaded.Scenario.InitialState.Horizon != bundle.Horizon) throw new ArgumentException("Replay content digest or horizon mismatch.");
            var world = bundle.Start.Restore(trace);
            var execution = Runs.Execute(loaded, world.Seed, trace, Option("--out") ?? "artifacts/replayed", world);var replay=execution.Result;
            if (replay.Digest != bundle.ExpectedDigest || replay.Status != bundle.ExpectedStatus || !replay.AssertionFailures.SequenceEqual(bundle.ExpectedAssertionFailures))
            { replay.Status = "assertion_failed"; replay.AssertionFailures.Add("Replay differs from recorded digest/status/assertions."); }
            if(Option("--chain") is string chain)
            {
                var selected=TraceChains.Select(execution.World.Trace,long.Parse(chain,CultureInfo.InvariantCulture));
                string path=Path.GetFullPath(Path.Combine(Option("--out")??"artifacts/replayed","chain.json"));
                File.WriteAllText(path,"["+string.Join(",",selected.Select(Runs.EventJson))+"]");replay.Artifacts["chain"]=path;
            }
            File.WriteAllText(Path.Combine(Option("--out") ?? "artifacts/replayed", "result.json"), JsonData.Write(replay));
            Console.WriteLine(JsonData.Write(replay)); return replay.Status == "completed" ? 0 : 1;
        }
        if (operation == "benchmark")
        {
            if (input == "pressure")
            {
                var pressure = PressureStation.Execute(PressureStation.Create(Option("--count") is string pressureCount ? int.Parse(pressureCount, CultureInfo.InvariantCulture) : 32,
                    Option("--seed") is string pressureSeed ? ulong.Parse(pressureSeed, CultureInfo.InvariantCulture) : 42), Option("--out") ?? "artifacts/pressure-station");
                Console.WriteLine(JsonData.Write(pressure)); return pressure.Status == "completed" ? 0 : 1;
            }
            if (input == "institutional")
            {
                string variant = Option("--variant") ?? "normal";
                var institution = InstitutionalStation.Execute(InstitutionalStation.Create(Option("--count") is string institutionCount ? int.Parse(institutionCount, CultureInfo.InvariantCulture) : 32,
                    Option("--seed") is string institutionSeed ? ulong.Parse(institutionSeed, CultureInfo.InvariantCulture) : 42, variant), variant, Option("--out") ?? "artifacts/institutional-station");
                Console.WriteLine(JsonData.Write(institution)); return institution.Status == "completed" ? 0 : 1;
            }
            if (input == "recovery")
            {
                var recovery = RecoveryStation.Execute(RecoveryStation.Create(Option("--count") is string countText ? int.Parse(countText, CultureInfo.InvariantCulture) : 32,
                    Option("--seed") is string recoverySeed ? ulong.Parse(recoverySeed, CultureInfo.InvariantCulture) : 42), Option("--out") ?? "artifacts/recovery-station");
                Console.WriteLine(JsonData.Write(recovery)); return recovery.Status == "completed" ? 0 : 1;
            }
            if (input is not ("daily" or "population")) throw new ArgumentException("benchmark requires daily, population, recovery, institutional or pressure.");
            int population = Option("--count") is string n ? int.Parse(n, CultureInfo.InvariantCulture) : input == "daily" ? 12 : 2048;
            var workload = StationBaseline.Create(population, input == "daily" ? 2 : 1, Option("--seed") is string seedText ? ulong.Parse(seedText, CultureInfo.InvariantCulture) : 42);
            var measured = StationBaseline.Execute(workload, Option("--out") ?? "artifacts/baseline-" + input, requireDaily: input == "daily",
                progress: tick => { if (tick % 6000 == 0) Console.Error.WriteLine("Baseline tick " + tick + "/" + workload.Scenario.InitialState.Horizon); });
            Console.WriteLine(JsonData.Write(measured)); return measured.Status == "completed" ? 0 : 1;
        }
        var scenario = LoadedScenario.Load(input);
        ulong seed = Option("--seed") is string text ? ulong.Parse(text, CultureInfo.InvariantCulture) : scenario.Scenario.InitialState.Seed;
        if (operation == "generate-construction")
        {
            string variantText = Option("--variant") ?? "Expansion";
            if (!Enum.TryParse<Station.Scenarios.ConstructionVariant>(variantText, false, out var variant) || !Enum.IsDefined(variant)) throw new ArgumentException("Unknown construction variant.");
            var files = ConstructionGeneration.Write(scenario, seed, variant, Option("--out") ?? "artifacts/generated-construction/" + variant + "-" + seed.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine(JsonData.Write(new { schemaVersion = 1, status = "completed", operation, seed, variant, files })); return 0;
        }
        if (operation == "validate")
        { Console.WriteLine(JsonData.Write(new { schemaVersion = 1, status = "completed", operation, scenario = scenario.Scenario.InitialState.Id, contentDigest = scenario.ContentDigest })); return 0; }
        if (operation == "run")
        {
            var result = Runs.Execute(scenario, seed, trace, Option("--out") ?? "artifacts/" + scenario.Scenario.InitialState.Id + "-" + seed).Result;
            Console.WriteLine(JsonData.Write(result)); return result.Status == "completed" ? 0 : 1;
        }
        if (operation == "batch")
        {
            int count = Option("--count") is string c ? int.Parse(c, CultureInfo.InvariantCulture) : 20;
            int parallel = Option("--parallel") is string p ? int.Parse(p, CultureInfo.InvariantCulture) : Environment.ProcessorCount;
            if (count < 1 || count > 1000000 || parallel < 1) throw new ArgumentException("Batch count must be 1..1000000 and parallelism positive.");
            var results = new RunResult[count]; string? output = Option("--out");
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
            { ulong runSeed = checked(seed + (ulong)i); results[i] = Runs.Execute(scenario, runSeed, trace, output == null ? null : Path.Combine(output, runSeed.ToString(CultureInfo.InvariantCulture))).Result; });
            foreach (var result in results) Console.WriteLine(JsonData.Write(result));
            return results.All(r => r.Status == "completed") ? 0 : 1;
        }
        throw new ArgumentException($"Unknown or unimplemented operation {operation}.");
    }
}
