using System.Text.Json.Nodes;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class StaleRepairBeliefTests
{
    // machine_faults.json adapted: an inspector at the machine reports the fault; the
    // only qualified worker waits at a separate terminal and still believes, from its
    // own earlier observation, that the machine works.
    private static LoadedScenario Load()
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "scenarios", "machine_faults.json")))!.AsObject();
        var s = j["initialState"]!.AsObject(); var info = s["information"]!.AsObject();
        JsonObject At(int x) => new() { ["x"] = x, ["y"] = 0, ["z"] = 0 };
        s["actors"]![0]!["role"] = "inspector";
        // The worker starts at the machine (observing it working at tick 0), then walks to
        // the terminal for its first check-in before the fault at tick 10.
        s["actors"]!.AsArray().Add(new JsonObject { ["id"] = 2, ["name"] = "Repair worker", ["role"] = "worker", ["position"] = At(1), ["scripted"] = false });
        info["terminals"]![0]!["position"] = At(2); info["terminals"]![0]!["permittedRoles"] = new JsonArray("worker", "inspector");
        info["credentials"]!.AsArray().Add(new JsonObject { ["id"] = 2, ["holder"] = 2, ["account"] = 2, ["cardItem"] = 7 });
        foreach (var item in s["items"]!.AsArray()) if (item!["id"]!.GetValue<long>() is 1 or 2 or 3) item["owner"] = 2;
        s["items"]!.AsArray().Add(new JsonObject { ["id"] = 7, ["resource"] = "card", ["quantity"] = 1, ["locationKind"] = "ActorInventory", ["owner"] = 2 });
        foreach (var fact in info["facts"]!.AsArray().ToArray()) { var copy = fact!.DeepClone(); copy["actorId"] = 2; info["facts"]!.AsArray().Add(copy); }
        s["facility"]!["reportingRoles"] = new JsonArray("inspector");
        // The inspector stays at the machine: only the worker has a roster and check-ins.
        s["jobSystem"]!["checkIns"] = new JsonArray(new JsonObject { ["actorId"] = 2, ["interval"] = 100, ["firstTick"] = 0 });
        s["routines"]!["assignments"] = new JsonArray(new JsonObject { ["actorId"] = 2, ["restTile"] = At(2), ["cohort"] = 0 });
        j["assertions"] = new JsonArray();
        return new(j.ToJsonString(), File.ReadAllText(Path.Combine(Fixtures.Root, "content", "jobs.json")), "stale-repair-belief");
    }

    [Fact]
    public void RepairOrderSupersedesAnOlderBeliefThatTheMachineWorks()
    {
        var w = Load().CreateWorld(42, true); w.AdvanceTo(300);
        var job = Assert.Single(w.InspectJobs(), j => j.Kind == WorkKind.RepairMachine && j.MachineId == 1);
        var attempt = Assert.Single(w.InspectWorkAttempts(), a => a.JobId == job.Id);
        Assert.Equal(2, attempt.ActorId);
        Assert.Contains(w.Trace, e => e.Type == "ObservationRecorded" && e.ActorId == 2 && e.Tick == 0 && e.Detail == "Machine:1/Operational = 1; source=Direct");
        // The order read replaces the stale private belief; it is not physical truth.
        Assert.Contains(w.Trace, e => e.Type == "ObservationRecorded" && e.ActorId == 2 && e.Detail.StartsWith("Machine:1/Operational = 0; source=MessageRead", StringComparison.Ordinal));
        var installed = Assert.Single(w.Trace, e => e.Type == "ItemInstalled" && e.ActorId == 2 && e.Detail == "Machine:1");
        Assert.DoesNotContain(w.Trace, e => e.Type == "RepairGoalCompleted" && e.ActorId == 2 && e.Tick < installed.Tick);
        Assert.Equal(WorkExecution.CompletedPhysically, job.PhysicalStatus);
        Assert.True(Assert.Single(w.InspectMachines()).Operational);
        w.AssertAccounting(); Assert.Equal(w.Serialize(), World.Restore(w.Serialize()).Serialize());
    }
}
