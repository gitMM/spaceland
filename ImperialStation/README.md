# Imperial Station

Independent headless station simulation, based on build specification **1.2.2**
(fixed-station scope amendment). This folder is a standalone .NET solution: it has no
dependency on spaceland, DeathStar assets, Unity, an editor process, rendering, or
network services at runtime, and can be copied out of this repository and built on its own.

## Status (2026-10-30)

**918 tests pass; the first release is still incomplete.** First-release scope uses a
**fixed layout and population**; expansion and construction acceptance are deferred,
while existing beds, allocation and occupancy stay in scope.

- **Active milestone:** [a fixed station under sustained pressure](docs/pressure-station.md)
  passes at 32 and 256 citizens for five seeds, with exact state/event replay and the
  independent assignment audit. Still open: calibration of response targets and the
  check-in bound, off-duty facility acknowledgments and
  broader concurrent institutional load.
- **Population gate:** 256 citizens since 2026-10-30; 2,048-citizen results are historical.
- **Architecture:** All goal-priority, dispatch and institutional decisions run in `Station.Mind`; Core gathers inputs and applies choices (T00 pass, behaviour-identical move on 2026-10-30; see [architecture](docs/architecture.md#decision-boundary-2026-10-30)). The decision-input leaks that audit found are closed (T08 pass for the audited inputs); R-ARC-04 stays partial because `IInstitutionView` carries only the tick.
- **Requirements and gates:** the [acceptance matrix](docs/acceptance-current.md) is the
  current map of all 230 requirement IDs and T00–T27. Delivered systems are summarised
  in the [build specification's implementation status](<../Assets/DeathStar/doc/imperial_station_simulation_build_spec_v1.2 (2).md#implementation-status--2026-10-30>).
- The user has [cancelled the shutdown instruction](docs/completion-instruction.md); keep the computer running.

## Main gates

```powershell
dotnet run --project src/Station.Runner -c Release -- benchmark pressure --count 32 --out artifacts/pressure-32
dotnet run --project src/Station.Runner -c Release -- benchmark pressure --count 256 --out artifacts/pressure-256
```

The acceptance result is `pressure-result.json`, including exact state and event
continuation checks. Controls: `--variant single-cohort-guards|daytime|overnight|roster-change`.
Use 32 citizens for behaviour edits and 256 at verification boundaries.

- `benchmark recovery --count 32` is the [finite disruption and recovery](docs/recovery-station.md) gate.
- `benchmark institutional --count 32` covers audit, detention and escort changes; its
  `--variant` controls are documented in the [institutional report](docs/institutional-station.md).
  The complete small command takes about 3.3 seconds excluding compilation.
- A [bounded performance probe](tools/Station.Profile/README.md) can continue an
  existing checkpoint when investigating a slow phase.

## Documentation

- [Architecture](docs/architecture.md), [decision log](docs/decisions.md) and [data formats](docs/formats.md).
- Milestone reports: [pressure station](docs/pressure-station.md),
  [institutional station](docs/institutional-station.md),
  [recovery station](docs/recovery-station.md),
  [daily/population baseline](docs/integration-baseline.md).
- Increment reports. These describe their verification date, not the current backlog:
  - H1: [H1 implementation report — 2026-10-28](docs/h1-report.md), [H1 traceability](docs/traceability-h1.md).
  - H2: [H2 jobs and check-ins: verified increment](docs/h2-jobs-progress.md), [H2 implementation progress](docs/h2-progress.md), [H2 scoped records: verified increment](docs/h2-records-progress.md), [H2 autonomous repair progress](docs/h2-repair-progress.md), [H2 resource flows: verified increment](docs/h2-resources-progress.md), [H2 perception and sensors: verified increment](docs/h2-sensors-progress.md), [H2 physical work progress](docs/h2-work-progress.md).
  - H3: [Discrepancy records audit — H3 progress](docs/h3-audit-progress.md), [Autonomous credential recovery — H3 progress](docs/h3-autonomous-recovery-progress.md), [H3 cleanup physical foundation: verified increment](docs/h3-cleanliness-progress.md), [H3 autonomous cleanup orders and missing-cleanup workflow](docs/h3-cleanup-orders-progress.md), [H3 generated construction verification](docs/h3-construction-generated-progress.md), [H3 construction orders and received capacity](docs/h3-construction-orders-progress.md), [H3 construction capacity correction](docs/h3-construction-surveys-progress.md), [Detention requests and separate approval — H3 progress](docs/h3-detention-approval-progress.md), [Detention services, finite release and completed-sentence history — H3 progress](docs/h3-detention-services-progress.md), [Guard assignment and individual order reading — H3 progress](docs/h3-escort-dispatch-progress.md), [Physical custody and coupled escort movement — H3 progress](docs/h3-escorts-progress.md), [H3 authority report expectations: verified increment](docs/h3-expectations-progress.md), [Facility reporting and generated tasks — H3 progress](docs/h3-facility-progress.md), [H3 authority capacity commitments](docs/h3-growth-commitments-progress.md), [Guard revocation and outcome reporting — H3 progress](docs/h3-guard-outcomes-progress.md), [Physical equipment and guard rendezvous — H3 progress](docs/h3-guard-preparation-progress.md), [Local guard formation and target search — H3 progress](docs/h3-guard-search-progress.md), [H3 patrol and independent incident branches: verified increment](docs/h3-patrol-progress.md), [Case filing and personnel service — H3 progress](docs/h3-personnel-progress.md), [Physical construction — H3 progress](docs/h3-physical-construction-progress.md), [Supervisor QA verification — H3 progress](docs/h3-qa-progress.md), [Restricted recovery identity — H3 progress](docs/h3-recovery-identity-progress.md), [Physical credential recovery protocol — H3 progress](docs/h3-recovery-protocol-progress.md), [H3 bounded replacement response: verified increment](docs/h3-response-progress.md), [H3 roster routines: verified increment](docs/h3-routines-progress.md), [Machine faults and wear — H3 progress](docs/h3-wear-progress.md), [Response-chain verification — release progress](docs/response-chain-progress.md).
  - H4: [Finite berth allocation — H4 progress](docs/h4-berth-allocation-progress.md), [Finite local conversation — H4 progress](docs/h4-conversations-progress.md), [Retained daily opportunities and due-time ordering — H4 progress](docs/h4-daily-goals-progress.md), [Autonomous urgent relief — H4 progress](docs/h4-need-planning-progress.md), [Physical relief services — H4 progress](docs/h4-need-services-progress.md), [Exact needs and critical interruption — H4 progress](docs/h4-needs-progress.md), [Seek toilet and additional rest — H4 progress](docs/h4-personal-needs-progress.md), [Explained sleep fallback — H4 progress](docs/h4-sleep-fallback-progress.md).

## Build and test

Prerequisites: .NET SDK **9.0.311** and .NET **8** runtime. The local environment has
SDK 9.0.311 and runtime 8.0.15. Shared libraries target `netstandard2.1`, C# 9;
the runner/tests target the supported .NET 8 LTS. SDK and package versions are pinned.
NuGet access is required for the first restore, but no simulation needs network access.

From this directory:

```powershell
dotnet restore ImperialStation.sln --locked-mode -m:1
dotnet build ImperialStation.sln --no-restore -c Release -m:1
dotnet test ImperialStation.sln --no-restore -c Release -m:1
```

Packages restore to `.nuget/packages/`. Artifacts, packages, `bin/` and `obj/` are ignored.
On restricted Windows hosts, `-p:UseSharedCompilation=false` avoids shared compiler IPC.
VSTest itself needs local process/socket access. A sandbox that blocks testhost connections
cannot run the suite; a timeout is not a passing test.

## Run, batch, replay, inspect

```powershell
dotnet run --project src/Station.Runner -c Release --no-build -- validate scenarios/movement_exact.json
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/movement_exact.json --out artifacts/demo --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- batch scenarios/movement_exact.json --seed 0 --count 1000 --parallel 8 --trace none
dotnet run --project src/Station.Runner -c Release --no-build -- replay artifacts/demo/replay.json --out artifacts/replayed --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/demo/checkpoint.json --actor 1
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/false_delivery.json --out artifacts/false-delivery --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/autonomous_report.json --out artifacts/autonomous-report --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/autonomous-report/checkpoint.json --view knowledge --actor 1
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/work_actions.json --out artifacts/work-actions --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/work-actions/checkpoint.json --view resources
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/alternative_planning.json --out artifacts/alternative-planning --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/planner_costs.json --out artifacts/planner-costs --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/planner-costs/checkpoint.json --view repairs
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/work_orders.json --out artifacts/work-orders --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/missing_cleanup_report.json --out artifacts/missing_cleanup_report --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/missing_cleanup_report/checkpoint.json --view dirt
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/work-orders/checkpoint.json --view attempts
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/record_audit.json --out artifacts/record-audit --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/record-audit/checkpoint.json --view query --actor 2
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/resource_flow.json --out artifacts/resource_flow --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/stacked_order.json --out artifacts/stacked_order --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/resource_flow/checkpoint.json --view stacks
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/sensor_status.json --out artifacts/sensor_status --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/sensor_status/checkpoint.json --view sensors
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/sensor_status/checkpoint.json --view perception
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/roster_routines.json --out artifacts/roster_routines --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/roster_routines/checkpoint.json --view routines
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/roster_routines/checkpoint.json --view rosters
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/authority_reports.json --out artifacts/authority_reports --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/late_report.json --out artifacts/late_report --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/late_report/checkpoint.json --view expectations
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/late_report/checkpoint.json --view incidents
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/replacement_response.json --out artifacts/replacement_response --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/replacement_response/checkpoint.json --view admission
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/patrol_response.json --out artifacts/patrol_response --trace detailed
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/patrol_response/checkpoint.json --view patrols
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/patrol_response/checkpoint.json --view patrol-local --actor 3
```

Stdout is machine-readable JSON (one result per batch seed). Human diagnostics go to
stderr. Exit codes: `0` completed, `1` run assertion/runtime outcome requiring attention,
`2` invalid input, `3` unexpected runner failure. A rejected gameplay action is an
ordinary outcome, not a failed run, unless an assertion requires otherwise.

The example intentionally attempts pickup at tick 11 while still travelling. Arrival
at the intermediate tile is tick 4; final arrival is tick 12. Pickup succeeds at tick 13
and put-down completes at tick 15. All resources remain accounted for.

A run directory contains:

- `result.json`: configuration, status, metrics, digest and artifact paths.
- `replay.json`: embedded scenario/content, initial checkpoint including all commands,
  build/runtime identity, horizon, expected digest and assertions.
- `checkpoint.json`: final resumable canonical state in a versioned JSON envelope.
- `snapshot.json`: initial viewer projection plus checkpoint and run horizon.
- `trace.jsonl`: detailed events, if requested.

Replay requires the original build and matching content. A seed alone is insufficient.
`inspect` is pure; it does not advance simulation time or write to the checkpoint.
Its views include `actor`, `knowledge`, `mailbox`, `sessions`, `beliefs`, `agents`, `repairs`, `machines`, `claims`, `resources`, and
`jobs`, `attempts`, `orders` (requires `--actor`), `checkins`, `work-history`,
`record-history`, `query` (requires `--actor`), `stacks`, `sensors`, `perception`, `routines`, `rosters`,
`expectations`, `acknowledgments`, `incidents`, `admission`, `patrols`, `patrol-local` (requires `--actor`),
`authority` (requires authority enabled), and `all`.
A focused replay accepts `--trace focused --chain EVENT_ID` and writes the
causal event chain alongside the ordinary replay verification.
`benchmark daily` runs the integrated two-day station; `benchmark population` runs
256 citizens by default for 36,000 ticks. Both use the ordinary World, with scaled daily-life
configuration documented in `docs/integration-baseline.md`. Optional `--count`,
`--seed` and `--out` select population, seed and artifact directory. For example:

```powershell
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark daily --out artifacts/baseline-daily
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark population --out artifacts/baseline-population
```

Progress goes to stderr; JSON results go to stdout. Results include per-citizen
participation and explicit failures. These benchmarks do not close every T15/T22
clause. `sweep` remains unimplemented.

## Viewer

The station timeline viewer replays a whole run: a top-down deck slice with every
citizen at the service they are using, a time slider, and a dossier for the selected
citizen (path, current action and intent with its trigger, needs, activity history,
events and knowledge at that tick). The station authority has its own entry and hub on
the map: what it believes against the truth (wrong beliefs are flagged), its open report
deadlines, incidents and facility tasks, its decisions and its messages. Reports and
orders travel as pulses between terminals, the hub and recipients, unread orders show
on the citizen, and the Beliefs toggle draws the authority's view over the rooms.
Clicking any event or action opens its cause chain: what led to it and what it led to.
The Cases list (detentions, missing-report incidents, facility tasks, card recoveries)
opens a case under the map: one lane per participant with custody bands, causing
actions, events, labelled milestones and messages between lanes, plus a stage list with
each wait. Cases are assembled in the viewer from shared attempt, message, authorization
and case IDs.

Export a timeline first; it runs the ordinary simulation and samples it, and its
`finalDigest` matches an ordinary run of the same scenario and seed:

```powershell
dotnet run --project src/Station.Runner -c Release --no-build -- timeline pressure --count 32 --out artifacts/timeline-pressure-32
dotnet run --project src/Station.Runner -c Release --no-build -- timeline scenarios/roster_routines.json --every 100 --out artifacts/timeline-roster
```

`timeline` accepts a scenario file or the `pressure`, `recovery`, `institutional`
and `daily` generators (`--count`, `--seed`, `--variant`); `--every` sets the sample
interval (default 300 ticks). The 32-citizen pressure run exports in about 9 seconds
to 1.3 MB. Open `tools/viewer/index.html` and drop in `timeline.json.gz`, or serve
this folder (`python -m http.server 8765 --bind 127.0.0.1`) and open
`http://127.0.0.1:8765/tools/viewer/?timeline=../../artifacts/timeline-pressure-32/timeline.json.gz`.
Space plays, arrows step (Shift for a station minute), `[`/`]` jump between the
selected citizen's events, the wheel zooms and dragging pans. The format is in
[docs/formats.md](docs/formats.md#station-timeline). Population-scale timelines are
not yet streamed; use 32–128 citizens.

The earlier trace explorer remains at `tools/viewer/trace.html`: select a run's
`snapshot.json` and `trace.jsonl` to compare physical truth, selected-actor knowledge
and authority belief per tile. A compact/no-trace run cannot reconstruct movement
history; replay with `--trace detailed` first.

## Structure

```text
src/Station.Contracts  Typed IDs, intents, immutable knowledge, planning contracts
src/Station.Mind       All decision code: goal priority, planning, dispatch, institutional policies
src/Station.Core       World state, input gathering, execution, terminals, scheduler, snapshots
src/Station.Runner     Strict JSON loading, CLI, assertions, batches, replay/artifacts
tests/Station.Tests   Structural, exact, generated, continuation and isolation checks
content/              Versioned twelve-family tile catalog
scenarios/            Runnable fixtures; generator documentation
tools/viewer/         Offline development viewer
docs/                 Design decisions, formats, scope and verification evidence
unity/                Optional integration instructions; no adapter installed
artifacts/            Generated files, excluded from version control
```

The source documents are preserved under `docs/reference/`. Their future infiltration
roadmap is background design, not implemented functionality. Expansion is deferred.
Finish the combined population pressure proof, then classify missed routine windows
and verify roster/check-in priorities as described in the current acceptance matrix.
