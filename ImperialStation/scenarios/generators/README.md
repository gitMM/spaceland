# Scenario generators

`DailyStationGenerator.cs` generates a connected station for `benchmark daily`
and `benchmark population`. It varies population and seeded cohort offsets/soiling,
provides finite physical services and an ordinary work backlog, and declares all
shortened-day overrides. It schedules no actor commands. Twelve initial-condition
roles audit the two-day fixture; population action origins are checked while traces
are drained. Three daily seeds plus shortage, recovery and checkpoint tests are in
`StationIntegrationTests`. Full parameterized T22 and population institutional gates
remain open; see `docs/acceptance-current.md`.

`tests/Station.Tests/Fixtures.cs::Generated(seed)` creates connected weighted grids
with translated coordinates, varied dimensions, tile families and costs. It schedules
a real cost disturbance, movement, pickup and put-down through the shared executor.
The test RNG is separate from world streams. All durations and horizons are explicit.

`GeneratedRoutesMatchDijkstraForOneThousandSeeds` validates optimal costs, adjacency,
physical completion and conservation for 1,000 generated seeds.
`T25_TwentyGeneratedSeedsAtFiveCheckpointTicks` uses 20 seeds with five independently
selected checkpoints each. The canonical JSON fixture has its own equivalent fuzz run.

`FalseDeliveryGenerator.cs` derives parameterized T07 fixtures from the canonical
JSON and catalog. It varies translation, rotation/reflection, actor/terminal IDs,
travel and operation durations, delivery latency, background staffing and reporting
terminal. Reporting from another tile schedules real travel and recomputes the
timeline. It preserves the positive setup: component at A, false report from the
player, inspector checking B and then reporting the discrepancy. Twenty generated
seeds each receive five checkpoint-continuation checks.

Generators are test inputs, never runtime behavior branches. These fixtures do not
claim institutional or population coverage. H3–H4 still need generated authority
and daily-life cases as those systems are implemented.

`RepairGenerator.cs` derives autonomous cases from `alternative_planning.json` and
the work catalog. Variants cover an existing component, fabrication, stale item
location, no prerequisites and an unknown specification requiring inspection. It
varies axis/translation/reflection, actor/machine/workstation IDs, traversal and work
durations while preserving each variant's positive prerequisites or intentional
shortage. There are no scripted commands. Tests exercise 20 seeds per variant and
five checkpoint continuations per generated case. Stacked existing/fabricated parts
and stash variants now bring this to eight variants (160 cases, 800 continuations).
`planner_costs.json` supplies the canonical distant-part/fabrication cost choice;
direct planner tests additionally check the exact crossover and hidden cost changes.

`WorkOrderGenerator.cs` varies the autonomous `work_orders.json` fixture across entity
IDs, distinct physical actors/authenticated accounts, layout axes/reflections/translations,
travel/install/terminal durations, latency and own-clock check-in timing. Twenty generated
cases each receive five checkpoint continuations. The worker receives its goal only by
reading and acknowledging the posted order; there are no scripted actions or initial
repair goals. Separate tests compare 24 seeds at concurrency 2/4/8.

`ResourceFlowGenerator.cs` derives source/merge/split/haul/stash/pickup/sink sequences
from `resource_flow.json`. It varies initial/supplied/split/consumed quantities, operation
and movement times, actors/items/services/stashes/project tags, and layout transforms.
Twenty cases each continue from all ten operation starts; results also match at
concurrency 2/4/8. These are scripted executor/accounting cases. Separately, twenty
generated work-order cases require autonomous cartridge separation and resume from
the actual split-action start while retaining the original attempt identity.

`SensorTests.GeneratedSchedulesReplayAcrossPendingPacketsTogglesAndConcurrentWorlds`
constructs twenty configurations varying seed, sensor IDs, initial sampling offsets,
intervals and delivery latency. Each includes two machine scopes, initially disabled
and subsequently enabled sensing, a disable/re-enable gap, and physical status changes.
Nine checkpoints per case cover pre-tick-zero state, sampling, queued delivery, disabled
state and resumption. All 180 continuations preserve canonical bytes/digests and the
remaining trace; separate runs agree at concurrency 2/4/8. Exact tests cover same-tick
physical completion and human/sensor delivery ordering. These fixtures exercise the
sensor capability, not an autonomous authority policy.

`RoutineTests.GeneratedRosterContinuationsChunkingTracingAndConcurrentRunsAgree`
varies seed, cohort and publication tick across twenty three-citizen worlds, each
running for 125 ticks with a 60-tick day. Nine checkpoints per case cover initial
knowledge, travel, queued publication/read, duty boundaries and later days. All 180
continuations preserve bytes, final digests and remaining traces. Tick-by-tick runs
with frequent getters and tracing disabled match single advances. Separate runs
agree at concurrency 2/4/8. These tests cover the H3 duty/rest placeholder, not H4 needs.

`AuthorityTests.GeneratedReceiptSchedulesContinueAcrossDeadlinesAndConcurrentWorlds`
varies seed, published work duration, nominal travel and message latency across twenty
worlds. Three qualifying result kinds arrive on the deadline or one tick late. Seven
checkpoints per case give 140 continuations, preserving bytes, final digest and remaining
trace. Tick-by-tick runs with getters and no tracing match direct advancement; independent
worlds agree at concurrency 2/4/8. These are scripted report-protocol cases, not calibrated
population workloads. The separate `authority_reports.json` fixture uses an autonomous
repair worker; `late_report.json` explicitly scripts a misleading report and off-duty gap.

`IncidentResponseTests.DeferredContinuationsChunkingAndConcurrentRunsAreIdentical`
varies seed and tick duration across twenty worlds with three overdue originals and
three reserve workers. Eight checkpoints each provide 160 continuations around queue
creation, first admission, deferral and hour boundaries. Digests and remaining traces
match; tracing-off, getter-heavy tick advancement and concurrency 2/4/8 agree. These
are scripted original-report fixtures with autonomous authority dispatch; response
execution is separately covered by `replacement_response.json`, which scripts only
original-worker actions and continues the autonomous replacement at nine checkpoints.

`PatrolTests.GeneratedPatrolContinuationsTraceModesAndConcurrentWorldsMatch` varies
seed, inspection duration and delivery latency across twenty autonomous guard/replacement
worlds. Twelve checkpoints each give 240 continuations through posting, finite reads,
acceptance, inspection, reporting and shift end. Byte round trips, final digests and
remaining traces match; tick-by-tick runs with getters and tracing off agree, as do
independent worlds at concurrency 2/4/8. Only the original worker's acknowledgment and
move are scripted triggers. Additional variants exercise later availability of either
branch, one shared token, moving/stale targets, failed reads, expiry and shift-spanning
inspection. This does not claim the still-missing cleanup or population gate.

`CleanupOrderTests.GeneratedCleanupOrderContinuationsPreserveReadsToolsDeadlinesAndSeparateResults`
varies 20 seeds, actual traversal costs, cleaning and inspection durations. Twelve checkpoints
give 240 byte-stable continuations through reads, acceptance, overdue response, local cleanup,
late original reports and cancellation reads. Remaining traces match, as do getter-heavy
per-tick advancement, disabled tracing and concurrency 2/4/8. The base fixture
`missing_cleanup_report.json` has no scripted actors. Additional transformations rename actor,
item, tile, job, credential and terminal IDs and rotate/translate geometry while preserving
deadline/result timing and normalized outcomes. These are workflow tests, not population gates.

`RecoveryGenerator.cs` derives seven labeled autonomous T18 variants from
`autonomous_recovery.json`: replacement, cheaper known retrieval, card rediscovery
en route to HQ, no stock, no desk, no retained alternate capability and valid card.
Each varies actor/account/item/credential/job/service/tile/recipe IDs, translated
and reflected geometry across three axes, HQ/terminal placement, travel and finite
operation durations, latency, blank stock, service capacity and background staff.
Background clerks test identity isolation; they are not printer staffing dependencies.

An undisturbed autonomous prefix determines the CardLoss trigger during accepted
work travel. No response action is copied or scripted. A second prefix validates
retained work, travel and undiscovered loss. Replacement/shortage variants keep
the HQ route away from the dropped card; the rediscovery variant deliberately
crosses it and requires ordinary retrieval. Known retrieval placements preserve a
strictly cheaper estimated route. Missing prerequisites have blocked-work assertions,
not success assertions. Per-variant comparisons normalize entity IDs and compare
recovery count, case/output count, stock conservation, retained/completed work and
local failure explanations; changed timing is not expected to preserve completion ticks.

Tests run 20 seeds for each of seven variants, with up to five checkpoint continuations
per case. Another 20 replacement seeds compare traced direct runs with getter-heavy,
tick-by-tick, untraced runs at concurrency 2/4/8. This exposed and fixed a recovery
retry timer that could precede a newly extended backoff. The full response-chain
causal metadata/harness and wider institutional/population generators remain pending.

Recovery variants now declare responseChainRoles, with remapped actor/card/trigger
IDs and outcome-specific ordered causal transitions. Replacement requires the full
12-transition chain; retrieval, rediscovery, valid-card and blocked variants use
contracts appropriate to their outcomes. Compact Runs.Execute calls audit too.
Checkpoint continuation state tests remain distinct from full-history proof; the
latter is verified from the initial state and reproduced in replay.

`ConstructionGenerator.cs` provides seven labeled T11 variants: Expansion,
DelayedReport, NeedInterruption, NoStock, NoQualifiedWorker, NoLegalSite and
VacantCapacity. Each has 20 tested seeds; 20 additional seeds test concurrency,
tracing and read-only inspection invariance. They vary layout transforms, service
placement, IDs, role/resource names, staffing, stack counts and timings. The
interruption variant declares accelerated working hunger and verifies real relief
before a full restart. No response commands are generated. Runner's
`generate-construction TEMPLATE --variant NAME --seed N --out DIR` exports the
scenario, relative catalog and property/normalization manifest. See
`docs/h3-construction-generated-progress.md` for the scope and verified artifacts.

`RecoveryStationGenerator.cs` composes the daily station with finite normal work,
card losses, equipment faults, spills and one bounded terminal outage. It generates
initial conditions and disturbances only. `benchmark recovery --count 32` and
`--count 2048` use the same generator and the ordinary World policies. The gate
checks physical backlog drainage, received institutional outcomes, daily life,
credential recovery, bounded failed-action counts and checkpoint continuation.
See `docs/recovery-station.md` for exact timings, supplied stock, role assumptions,
the intentionally rejected seed without overdue-report coverage, and current results.
