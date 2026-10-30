# Bounded simulation profiling

This diagnostic runs the real recovery scenario or continues a saved binary
checkpoint. It does not replace the recovery acceptance gate. The population,
rules, event ordering and trace generation are unchanged.

From `ImperialStation/`:

```powershell
dotnet build tools/Station.Profile -c Release
dotnet tools/Station.Profile/bin/Release/net8.0/Station.Profile.dll initial 32 180000
dotnet tools/Station.Profile/bin/Release/net8.0/Station.Profile.dll artifacts/recovery-256/burst-end.bin 3000
```

The second command measures a complete small simulation without acceptance
exports. The third measures ticks 48000–51000 of an existing population recovery (run `benchmark recovery --count 256 --out artifacts/recovery-256` first).
JSON output separates initialization/restore from simulation seconds and includes
allocation, event count and the final digest. Traces are drained every 1000 ticks.
Compare matching event counts and digests as well as timings. Keep isolated
before/after builds when profiling changes; do not time competing benchmark jobs.

The complete acceptance test, including checkpoint continuation, remains:

```powershell
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark recovery --count 32 --out artifacts/recovery-32
```

Use `--count 256` for the population gate. For CPU samples, an optional locally
installed `dotnet-trace` can launch either probe:

```powershell
dotnet-trace collect --profile cpu-sampling --format Speedscope --output artifacts/probe.nettrace -- dotnet tools/Station.Profile/bin/Release/net8.0/Station.Profile.dll initial 32 180000
```

Restore validates the saved state and can itself be significant. Its time is
reported separately; shortened checkpoint probes do not measure initialization
from a fresh scenario or prove all recovery requirements.
