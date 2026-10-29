# Imperial Station

Independent headless station simulation, based on build specification **1.2.1**.
Current requirement status: [acceptance matrix](docs/acceptance-current.md).
Current integration work and measurements: [daily/population baseline](docs/integration-baseline.md).
Current institutional milestone: [specialized station and recovery controls](docs/institutional-station.md).
Completed recovery milestone and evidence: [finite disruption and recovery](docs/recovery-station.md).

Run `dotnet run --project src/Station.Runner -c Release -- benchmark recovery --count 32 --out artifacts/recovery-32`
for the small recovery gate; use `--count 2048` for the same population workload.
Use the small gate for frequent behavior edits and the population gate for scale
checks. A [bounded performance probe](tools/Station.Profile/README.md) can continue
an existing checkpoint when investigating a particular slow phase.
For audit, detention and escort changes, run `benchmark institutional --count 32`;
its `--variant` controls and population command are documented in the institutional report.
The measured complete small institutional command takes **3.309 seconds**; the
2,048-citizen command takes **213.815 seconds** including checkpoint continuation,
excluding compilation. Use the small command for iteration and population runs at
verification boundaries. The [next verification priorities](docs/acceptance-current.md#recommended-next-verification)
are missed ordinary routine windows and the remaining daily-life priority/roster
checks, followed by broader concurrent institutional load.
Older increment reports describe their verification date, not the current backlog.
This folder is a standalone .NET solution: it has no dependency on spaceland,
DeathStar assets, Unity, an editor process, rendering, or network services at runtime.
It can be copied out of this repository and built on its own.

Current status (2026-10-29): **816 tests passed; first release still incomplete**. See the [consolidated implementation status](<../Assets/DeathStar/doc/imperial_station_simulation_build_spec_v1.2 (2).md#implementation-status--2026-10-29>) for delivered systems and remaining gates. The user has [cancelled the shutdown instruction](docs/completion-instruction.md); keep the computer running.

**Implemented:** the H1 foundation plus parts of H2, H3 and H4: bounded GOAP search, knowledge,
local sensing, physical terminal sessions, delayed messages, authority beliefs and
autonomous reporting and repair planning with existing-part/fabrication alternatives.
Physical work includes fabrication, installation, equipment
inspection/testing, atomic claims and recipe accounting. State and replay cover these
additions. Jobs now retain distinct attempts, offline order copies, posted acknowledgments,
suspension/resumption and delayed outcome reports. Autonomous periodic and work-result
check-ins use the worker's own clock and knowledge. Finite record queries capture versioned,
permission-filtered credential/session/work records, with bounded learned facts and history.
Resource operations now include finite stack split/merge, physical project stashes,
declared source/sink recipes and autonomous separation of stacked repair cartridges.
Content controls passive versus inspection-only local facts. Scoped wired sensors
capture machine status and deliver delayed, explicitly sourced authority evidence.
The first H3 routine increment adds a configured station day, versioned cohort rosters,
staggered check-ins, and physical travel to a known rest tile after duty. Citizens adopt
roster changes only after reading them; unfinished work remains retained across shifts.
Authority report expectations now start on received acceptance, estimate from published
information, extend across known off-duty time and learn from received results. Separate
acknowledgment watches and persistent overdue incidents retain uncertainty about work.
Overdue repair incidents can now dispatch qualified replacements through ordinary orders,
with fixed-hour cohort admission budgets, FIFO deferral and bounded replacement depth.
Patrol guards read and acknowledge orders, inspect recorded search locations, and report
worker status through terminals. Patrol and replacement share one admission token while
retaining separate result evidence, including when one branch becomes available later.
Cleanable tiles now accumulate deterministic soiling and spills. Finite cleaning checks
local dirt at arrival and completion, preserves tools, and handles concurrent cleaners
without negative dirt or duplicate effects. Autonomous cleaners read and accept typed
tile orders, collect known tools, inspect on arrival, clean and report through terminals.
The missing-cleanup scenario now runs without scripted actors: a slow original triggers
independent replacement/patrol responses, then reports NoWorkNeeded on its own attempt.
Facility duties now turn local dirt/fault observations into terminal reports, deduplicated authority tasks and qualified worker assignments. Completion and follow-up work depend on received evidence. Machines now fail through explicit disturbances and deterministic wear timers. Failed cartridges enter an accounted sink; local observation and normal reports drive replacement work. Sampled repair QA now dispatches supervisors through typed verification orders. Supervisors inspect locally and report captured results; discrepancies now open bounded audits at assigned Records Terminals. Completed, permitted queries can establish assigned-account responsibility; missing or ambiguous evidence produces NeedsReview. With personnel enabled, supervisors post cases through terminals; validated cases append one reprimand and return a count through an ordinary mailbox reply. Canonical keys prevent duplicate reprimands. Supervisors now request detention after reading a qualifying count. The authority separately approves, rejects or records a wait, retaining unique expiring authorizations. Optional escort dispatch now reserves a complete team from known eligible guards and sends individual orders with the roster, justification, destinations and expiry. Each guard reads and acknowledges through a terminal. Guards now collect real known equipment, complete a finite equip action and travel to the rendezvous on their read copies, including offline. Each guard now completes a local join; the physically assembled team searches copied locations through finite target inspections. A finite local handoff now establishes cooperative custody, suspends target work and closes ordinary sessions. Guards and target move with shared frozen edge timing, stop together on cancellation or known expiry, and wait at the copied destination. Declared detention services now reserve finite bed and operation capacity, perform local intake and release, and issue delayed authority receipts and release notices. Successful intake starts the sentence; release frees the slot. Current orders must be read before retained work resumes. Completed detention resets qualifying counts through received evidence while preserving historical decisions. Revocations now reach individual guards through inbox reads or connected intake checks. Negative searches, blocked preparation, expiry and interrupted custody produce terminal reports; received outcomes explain authority NeedsAttention states. Optional needs now retain exact fractional accumulators and schedule private thresholds. Critical guards stop escorts at safe boundaries and report through terminals. Physical meals, toilets, rest and assigned sleep now require real local services, capacity queues and finite activity. Food is conserved; cancellation keeps only elapsed sleep/rest benefit. Autonomous urgent/critical relief now plans known travel and service use, learns local failures, retries alternatives and respects custody. Known rosters now generate retained meal, sleep and mandatory rest goals. Due goals share time/sequence ordering with reports and check-ins, and execution rechecks priority at safe boundaries. Vacant berths now require finite local allocation. Sleep pressure can select bounded physical rest fallback while retaining an unmet-sleep reason. Due toilet use and optional off-duty strain rest share the priority policy. Local conversation now claims both consenting participants, relieves social need and shares only bounded permitted facts with speaker provenance. HQ recovery identity checks now create finite, terminal-bound restricted sessions; normal inbox, query and post access remains separately authorized. Formal lost-card reports now create idempotent account-scoped recovery cases, revoke old credentials, and return delayed replies through a separate recovery mailbox. Explicit reading authorizes finite physical issuance from real blank stock to a printer output, followed by pickup and normal login/read. Retries reuse the issued card, and historical receipts preserve newer local evidence. Autonomous card-loss discovery now retains work and chooses known-card retrieval or finite HQ replacement. Recovery requires normal login and a current-order read before work resumes; work services can explicitly require credentials at start and completion. The runner now audits declared response roles using explicit command origins and causal transitions. Canonical and generated recovery cases must pass this audit even in compact runs. Finite construction now consumes held materials at completion, adds connected topology and real housing berths, and preserves claims/materials across interruption. Authority capacity planning now uses bounded GOAP over known layout and material budgets, reserves identified projects and pending capacity, and avoids duplicate expansion while commitments cover the target. Typed construction jobs now dispatch through ordinary orders; workers read, acknowledge, collect material, travel, build and report autonomously. The first accepted project report converts pending capacity to believed completed capacity exactly once and extends the believed layout. Construction now has 20 generated seeds each for expansion, delayed reports, need interruption/resumption, shortages and vacant capacity, plus concurrency and checkpoint controls. Finite local construction surveys now queue ordinary return reports; receipt revises the identified project capacity without changing physical beds, spent materials or the original completion record. Duplicate evidence cannot roll back a correction. Initial capacity identification, blocked-work follow-up and other priority/release gates remain unfinished. The viewer still needs work-specific presentation.
**Outstanding:** remaining planning/work domains and H2 gate verification, H3 authority and
institutions, and H4 daily life/population/tuning. This is not the complete first release.
See [current construction survey progress](docs/h3-construction-surveys-progress.md), [generated construction verification](docs/h3-construction-generated-progress.md), [construction-order progress](docs/h3-construction-orders-progress.md), [capacity commitment progress](docs/h3-growth-commitments-progress.md), [physical construction progress](docs/h3-physical-construction-progress.md), [response-chain verification](docs/response-chain-progress.md), [autonomous recovery progress](docs/h3-autonomous-recovery-progress.md), [recovery protocol progress](docs/h3-recovery-protocol-progress.md), [recovery identity progress](docs/h3-recovery-identity-progress.md), [conversation progress](docs/h4-conversations-progress.md), [personal-need progress](docs/h4-personal-needs-progress.md), [sleep fallback progress](docs/h4-sleep-fallback-progress.md), [berth allocation progress](docs/h4-berth-allocation-progress.md), [daily-goal progress](docs/h4-daily-goals-progress.md), [autonomous relief progress](docs/h4-need-planning-progress.md), [physical relief service progress](docs/h4-need-services-progress.md), [exact needs progress](docs/h4-needs-progress.md), [guard revocation and reporting progress](docs/h3-guard-outcomes-progress.md), [detention service progress](docs/h3-detention-services-progress.md), [custody and escort progress](docs/h3-escorts-progress.md), [local guard search progress](docs/h3-guard-search-progress.md), [guard preparation progress](docs/h3-guard-preparation-progress.md), [escort dispatch progress](docs/h3-escort-dispatch-progress.md), [detention approval progress](docs/h3-detention-approval-progress.md), [personnel progress](docs/h3-personnel-progress.md), [audit progress](docs/h3-audit-progress.md), [QA verification progress](docs/h3-qa-progress.md), [machine fault progress](docs/h3-wear-progress.md), [facility workflow progress](docs/h3-facility-progress.md), [cleanup workflow progress](docs/h3-cleanup-orders-progress.md), [cleanup physics progress](docs/h3-cleanliness-progress.md), [patrol progress](docs/h3-patrol-progress.md), [replacement-response progress](docs/h3-response-progress.md), [expectation progress](docs/h3-expectations-progress.md), [routine progress](docs/h3-routines-progress.md), [sensor progress](docs/h2-sensors-progress.md), [resource progress](docs/h2-resources-progress.md), [record-query progress](docs/h2-records-progress.md), [job-system progress](docs/h2-jobs-progress.md), [autonomous-repair progress](docs/h2-repair-progress.md), [physical work progress](docs/h2-work-progress.md) and [information-system progress](docs/h2-progress.md); the [H1 report](docs/h1-report.md)
and [H1 traceability](docs/traceability-h1.md) preserve the earlier milestone evidence.

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
2,048 citizens for 36,000 ticks. Both use the ordinary World, with scaled daily-life
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
events and knowledge at that tick). Export a timeline first; it runs the ordinary
simulation and samples it, and its `finalDigest` matches an ordinary run of the same scenario and seed:

```powershell
dotnet run --project src/Station.Runner -c Release --no-build -- timeline recovery --count 32 --out artifacts/timeline-recovery-32
dotnet run --project src/Station.Runner -c Release --no-build -- timeline scenarios/roster_routines.json --every 100 --out artifacts/timeline-roster
```

`timeline` accepts a scenario file or the `recovery`, `institutional` and `daily`
generators (`--count`, `--seed`, `--variant`); `--every` sets the sample
interval (default 300 ticks). The five-day, 32-citizen recovery run exports in about
2 seconds to 0.7 MB. Open `tools/viewer/index.html` and drop in `timeline.json.gz`, or serve
this folder (`python -m http.server 8765 --bind 127.0.0.1`) and open
`http://127.0.0.1:8765/tools/viewer/?timeline=../../artifacts/timeline-recovery-32/timeline.json.gz`.
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
src/Station.Mind       Bounded symbolic search, known navigation, report/repair planning
src/Station.Core       Physical execution, knowledge, terminals, scheduler, snapshots
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
roadmap is background design, not implemented functionality. Next: remaining planning/work
domains, H2 requirement/gate verification and the authority/institution/daily-life systems.
