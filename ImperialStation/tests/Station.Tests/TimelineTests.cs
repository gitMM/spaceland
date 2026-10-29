using System.IO.Compression;
using System.Text.Json;
using Station.Contracts;
using Station.Core;
using Station.Runner;
using Xunit;

namespace Station.Tests;

public sealed class TimelineTests
{
    private static byte[] Inflate(string path)
    {
        using var file = File.OpenRead(path); using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var memory = new MemoryStream(); zip.CopyTo(memory); return memory.ToArray();
    }
    private static (Timeline.Summary Summary, JsonElement Root) Export(LoadedScenario scenario, long every, string directory)
    {
        var summary = Timeline.Export(scenario, scenario.Scenario.InitialState.Seed, directory, every);
        using var document = JsonDocument.Parse(Inflate(summary.Path));
        return (summary, document.RootElement.Clone());
    }
    private static long At(JsonElement row, int index) => index < row.GetArrayLength() ? row[index].GetInt64() : 0;

    [Fact]
    public void TimelineIsTheOrdinaryRunAndReconstructsKnowledgePositionsAndActions()
    {
        var scenario = StationBaseline.Create(6, days: 1);
        string directory = Path.Combine(Path.GetTempPath(), "station-timeline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (summary, t) = Export(scenario, 500, directory);
            var (_, world) = Runs.Execute(scenario, scenario.Scenario.InitialState.Seed, trace: false);
            // Advancing in sampling chunks must not change the simulation.
            Assert.Equal(world.Digest(), summary.FinalDigest);
            Assert.Equal(world.Digest(), t.GetProperty("finalDigest").GetString());
            long horizon = scenario.Scenario.InitialState.Horizon;
            Assert.Equal(horizon / 500 + 1, t.GetProperty("frames").GetProperty("ticks").GetArrayLength());
            var strings = t.GetProperty("strings").EnumerateArray().Select(s => s.GetString()!).ToArray();
            Assert.Equal("", strings[0]);
            var citizens = t.GetProperty("citizens").EnumerateArray().ToArray();
            Assert.Equal(6, citizens.Length);
            foreach (var c in citizens)
            {
                var id = new Id<ActorTag>(c.GetProperty("id").GetInt64());
                // Initial knowledge plus value changes and evictions equals the final private knowledge.
                var known = c.GetProperty("knowledge").EnumerateArray().ToDictionary(k => strings[k[0].GetInt32()], k => strings[k[1].GetInt32()], StringComparer.Ordinal);
                foreach (var row in c.GetProperty("learned").EnumerateArray())
                {
                    string key = strings[row[1].GetInt32()];
                    if (row[2].GetInt64() < 0) known.Remove(key); else known[key] = strings[row[2].GetInt32()];
                }
                var actual = world.InspectKnowledge(id).ToDictionary(f => f.Key.ToString(), f => Timeline.Format(f.Value), StringComparer.Ordinal);
                Assert.Equal(actual.OrderBy(p => p.Key, StringComparer.Ordinal), known.OrderBy(p => p.Key, StringComparer.Ordinal));
                // The last completed movement segment ends where the actor is.
                var snapshot = world.InspectActor(id);
                var moves = c.GetProperty("moves").EnumerateArray().ToArray();
                Assert.NotEmpty(moves);
                if (!snapshot.InTransit) Assert.Equal((long)snapshot.Position.X, At(moves[^1], 4));
                for (int i = 1; i < moves.Length; i++) Assert.True(At(moves[i], 0) >= At(moves[i - 1], 1), "Movement segments overlap.");
                // Actions are recorded in request order with consistent interval bounds.
                long previous = long.MinValue;
                foreach (var act in c.GetProperty("acts").EnumerateArray())
                {
                    long requested = At(act, 0), began = At(act, 1), end = At(act, 2), outcome = At(act, 4);
                    Assert.True(requested >= previous); previous = requested;
                    if (began >= 0) Assert.True(began >= requested);
                    if (outcome != 0) Assert.True(end >= Math.Max(requested, began));
                    else Assert.Equal(-1, end);
                }
            }
            var events = t.GetProperty("events").EnumerateArray().ToArray();
            Assert.NotEmpty(events);
            Assert.DoesNotContain(events, e => strings[e[2].GetInt32()] is "ObservationRecorded" or "MovementStarted" or "ActionRequested");
            Assert.Contains(events, e => strings[e[2].GetInt32()] == "PlanSelected");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void TimelineExportIsDeterministic()
    {
        var scenario = StationBaseline.Create(6, days: 1);
        string first = Path.Combine(Path.GetTempPath(), "station-timeline-" + Guid.NewGuid().ToString("N")), second = first + "-again";
        try
        {
            var a = Timeline.Export(scenario, 42, first, 700);
            var b = Timeline.Export(scenario, 42, second, 700);
            Assert.Equal(Inflate(a.Path), Inflate(b.Path));
        }
        finally { foreach (var d in new[] { first, second }) if (Directory.Exists(d)) Directory.Delete(d, true); }
    }
}
