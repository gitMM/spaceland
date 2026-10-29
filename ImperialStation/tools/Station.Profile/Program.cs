using System.Diagnostics;
using Station.Core;
using Station.Runner;

// A bounded performance probe using the real simulation, not an acceptance gate.
// initial POPULATION TICKS | CHECKPOINT.bin TICKS
bool initial = args.Length > 0 && args[0] == "initial";
if (args.Length != (initial ? 3 : 2) || !long.TryParse(args[^1], out long ticks) || ticks <= 0 ||
    initial && (!int.TryParse(args[1], out int population) || population <= 0))
{
    Console.Error.WriteLine("Usage: Station.Profile initial POPULATION TICKS | CHECKPOINT.bin TICKS (positive integers)");
    return 2;
}
var timer = Stopwatch.StartNew();
var world = initial ? RecoveryStation.Create(int.Parse(args[1])).CreateWorld(trace: true)
    : World.Restore(File.ReadAllBytes(args[0]), true);
long start = Math.Max(0, world.LastProcessedTick), end = checked(start + ticks);
double setupSeconds = timer.Elapsed.TotalSeconds;
long allocated = GC.GetAllocatedBytesForCurrentThread(), events = 0;
timer.Restart();
for (long tick = start; tick < end;)
{
    tick = Math.Min(end, tick + 1000); world.AdvanceTo(tick);
    events += world.Trace.Count; world.ClearRecordedTrace();
}
double seconds = timer.Elapsed.TotalSeconds;
long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
world.AssertAccounting();
Console.WriteLine(JsonData.Write(new { start, end, setupSeconds, seconds, allocatedBytes = bytes, events, digest = world.Digest() }));
return 0;
