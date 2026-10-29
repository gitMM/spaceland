# H1 implementation report — 2026-10-28

The independent foundation is implemented in `ImperialStation/`. It builds without
Unity, passes 27 automated tests and reproduces the exact movement fixture's digest.
The full first-release goal remains outstanding: H2, H3 and H4 are not implemented.
This is an implementation candidate, not a claim of reviewer holdout acceptance.

## Delivered

- Independent solution with Contracts, Mind, Core, Runner and Tests.
- All twelve tile families, sparse connected 3D navigation, A* with deterministic
  ties, distinct search outcomes and Dijkstra reference comparisons.
- Integer simulation clock, ordered event phases, frozen movement segments, exact
  progress, positive finite item transfers and conservation checking.
- World-owned named PRNG streams; no wall-clock decisions or global mutable world state.
- Explicit complete H1 snapshots, byte-stable restoration, canonical SHA-256 digests,
  self-contained replay bundles and pure inspection.
- Strict scenario/catalog validation; JSONL CLI run/batch results and event traces.
- Static offline viewer with Y layers, scrubber, interpolated actors and entity history.
- Pinned SDK/packages, decisions, formats, integration notes and complete H1 ID matrix.

See [README](../README.md) for the repository structure and usage,
[architecture](architecture.md) for system boundaries,
[decisions](decisions.md) for implementation choices, and
[traceability](traceability-h1.md) for each requirement's evidence and current ownership.

## Commands and actual results

Commands ran from `C:\Transhuman\spaceland\ImperialStation` unless noted.
`DOTNET_CLI_HOME` was set to the solution's `.dotnet` directory.

```powershell
dotnet restore ImperialStation.sln --packages .nuget/packages --disable-parallel -p:BuildInParallel=false -m:1 --verbosity minimal
```

Initial sandbox execution failed NuGet DNS resolution (NU1301). The same restore
with network permission succeeded for all five projects. The unsuccessful background
restore subsequently overwrote some asset diagnostics; a cached restore repaired them:

```powershell
dotnet restore ImperialStation.sln --packages .nuget/packages --ignore-failed-sources -p:NuGetAudit=false -m:1 -p:BuildInParallel=false --verbosity minimal
dotnet build src/Station.Runner/Station.Runner.csproj --no-restore -m:1 -p:UseSharedCompilation=false -c Release --verbosity minimal
```

Build: **0 warnings, 0 errors**. The audit override was for this offline command;
it is not a project setting. Dependency versions and content hashes are locked.

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h1.trx' --results-directory artifacts/test-results --verbosity minimal
```

The sandbox testhost connection timed out at 90 seconds and that run was aborted.
Running with local IPC access succeeded. Final regression after the failed-snapshot
and replay-result fixes: **27 passed, 0 failed, 0 skipped**; reported test duration
approximately **1 second**. The suite deliberately compiles `DateTime.UtcNow` and
verifies compiler error **RS0030**. Probe output is in `artifacts/analyzer-probe/build.log`.

Coverage includes T00–T06, H1 variants of T12–T14 and T25, plus strict schema,
resource transfer, event-limit, PRNG-vector, presentation-neutrality and replay tests.
The generated routing test runs **1,000 varied small worlds**, comparing A* with
Dijkstra and checking actual movement/transfers and accounting. Snapshot fuzzing
covers the canonical fixture and generated worlds, each with **20 seeds × 5 ticks**.
Parallel-world comparisons use concurrency **2, 4 and 8** against sequential results.

```powershell
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/movement_exact.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/movement_exact.json --out artifacts/movement-exact --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/movement-exact/replay.json --out artifacts/movement-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/movement-exact/checkpoint.json --actor 1
```

All completed successfully. Inspection shows actor 1 at `(2,0,0)`, no active transit,
empty inventory and a completed final action. Original and replay digests match:

```text
a63de86e8787d860c62bef4eab7647a6e81e5bd971eb90f205402cc88bd9d439
```

Final build identity:

```text
e64730a8c2bd10282d9d7c6ecef8328804d11676c2990d9b3b52788195ef3520
```

Content identity:

```text
53daf0232d08bffe5bd815973c127c9118ae6d77090bd5cd70410dcd4f6a9074
```

```powershell
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll batch scenarios/movement_exact.json --seed 0 --count 1000 --parallel 8 --trace none > artifacts/batch-1000.jsonl
```

**1,000 completed, 0 failed.** This is a one-actor, three-tile H1 fixture over 20 ticks
(2 simulated seconds), not the H4 population workload. Per-run measured simulation
time: average **0.1093 ms**, minimum **0.0299 ms**, maximum **8.5796 ms**. The sum of
per-run times (**109.2928 ms**) is not batch elapsed time because worlds ran concurrently.
Metrics exclude loading/build/artifact-write overhead and are not a throughput target.

Environment: Windows `10.0.26300`, .NET `8.0.15`, SDK `9.0.311`, Release, 32 reported
logical processors, batch concurrency 8, trace disabled. Managed heap measurement is
process-wide approximate; allocation measurement is current-thread bytes. Neither
memory nor timing values enter decisions or digests.

An invalid-duration copy of the scenario was written under `artifacts/` and checked:

```powershell
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate artifacts/invalid-scenario.json
```

It returned **exit 2**, status `invalid_scenario`, and the precise path
`$.initialState.tiles[0].traversalOverride` with `invalid duration`.

For independence verification, source/config/content/test files were copied into the
empty `artifacts/portable-check` directory, without `bin`, `obj`, Unity files or prior
build products. From the outer repository directory:

```powershell
dotnet restore ImperialStation/artifacts/portable-check/ImperialStation.sln --locked-mode --packages C:/Transhuman/spaceland/ImperialStation/.nuget/packages --ignore-failed-sources -p:NuGetAudit=false -m:1 --verbosity minimal
dotnet build ImperialStation/artifacts/portable-check/ImperialStation.sln --no-restore -p:RestorePackagesPath=C:/Transhuman/spaceland/ImperialStation/.nuget/packages -p:UseSharedCompilation=false -m:1 -c Release --verbosity minimal
```

All five projects built with **0 warnings, 0 errors** (reported build time 5.25 s).
Only the already-restored package cache was shared; no outer project source or Unity
assembly was referenced. The full tests ran in the primary standalone directory.

```powershell
node --check tools/viewer/viewer.js
```

Passed with exit 0. Browser visual inspection was **not run**: the browser tool rejected
the local `file:` URL under its URL policy. No alternate route was used to bypass it.
Open the viewer manually and load the matching snapshot/trace for visual review.

## Reproduce the decisive trace

Use `artifacts/movement-exact/replay.json` with the replay command above. The trace has:

| Tick | Transition |
| --- | --- |
| 0 | Move requested; A→B segment starts, arrival 4 |
| 4 | Arrive B; B→C starts, arrival 12 |
| 11 | Pickup at C rejected with `NotAtRequiredTile`; actor remains in transit |
| 12 | Arrive C; pickup starts through the same executor |
| 13 | Item moves into actor inventory; pickup completes |
| 14 | Put-down starts |
| 15 | Item returns to tile C; put-down completes |
| 20 | Horizon reached with all assertions/accounting satisfied |

The rejected pickup is an expected physical outcome, not a runtime failure.
The authority estimate in T05 is four ticks while actual arrival remains twelve.

## Deferred and unresolved

- H2: bounded GOAP, work actions/recipes, known-layout estimates, facts/eviction,
  FIFO/permuted claims, holder-bound credentials, terminals/sessions, transport,
  record/query infrastructure, false reporting and delayed correction.
- H3: authority planning/dispatch, construction, rosters, recovery, escalation
  budgets/calibration, audit/cases/reprimands, escort/intake/release.
- H4: exact needs, daily activities/conversation, fairness diagnostics, population,
  tuning sweeps and full integration regression.
- Tile definitions currently carry H1 identity/family/traversal/visual/capability
  metadata. Recipes, capacities, equipment and operational services are later-owned.
- Minimal view/message/record contracts are extension boundaries, not working later
  systems. Unimplemented enum owner kinds are rejected, not silently accepted.
- Viewer visual QA and reviewer holdout variants remain outstanding.
- No Unity runtime/backend digest comparison, 2,048-citizen workload or gameplay
  usability/fun evaluation was performed.

The next implementation step is H2's restricted knowledge and known-layout view,
followed by bounded symbolic planning and real alternative prerequisite chains.
Terminal sessions/messages then establish the false-report and unread-order tests.
Snapshots, digests and traceability must expand with every introduced state type.
