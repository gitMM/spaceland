# Current acceptance matrix

Updated 2026-10-29. Initial stabilization: 785 passed, zero failed; artifacts/integration-tests/stabilization.trx (historical). Component evidence is not full handoff acceptance. This matrix supersedes historical outstanding-work lists. Initial integration results are recorded in integration-baseline.md; current institutional evidence is linked below.
Prior institutional regression: **816 passed, zero failed, zero skipped**;
`artifacts/institutional-tests/institutional-verified.trx`. All 230 normative requirement
IDs are present. The two-day station passes its declared participation and causal
checks; the 2,048-citizen workload completes its work/needs baseline. Open full-gate
clauses below are not promoted to pass by these narrower results.
The [recovery follow-up](recovery-station.md) proves the same finite disruption
gate with 32 and 2,048 citizens, including checkpoint continuation, physical
recovery, daily meals/sleep, received completion reports and patrol outcomes.
The [specialized institutional station](institutional-station.md) now proves the
audit/detention/escort/release chain with daily life at 32 and 2,048 citizens. Eight
small controls include two real occupants competing for one slot and a replacement
guard team after interruption. Full release gates remain partial.

## Active verification — fixed station under pressure

The [combined pressure workload](pressure-station.md) now passes at 32 citizens:
overlapping recovery and disciplinary chains, shared guards, all daily meals/sleep,
drained work and service queues, conservation and exact state/event continuation.
Earlier 2,048-citizen trials exposed institutional backlogs and the inline checkpoint
export limit; those defects are fixed, but population acceptance remains pending. Expansion
is deferred. Current nine-day evidence is in `artifacts/pressure-32-completion/`, with
a passing 512-citizen diagnostic in `artifacts/pressure-512-fast/`, including exact replay.
The latest complete suite passes **858 tests**, zero failures or skips
(staffing and response build; earlier: 843 in `artifacts/pressure-tests/pressure-all-completion.trx`). Authenticated original replies
now close unposted searches without generating unnecessary patrol work. See the pressure report for
the current fixture, finite guard searches, audit retry backoff, intake waiting,
reply handling, and checkpoint fixes. Snapshot schema is 46. Explicit evidence-based
review outcomes are acceptable per the user's clarification; unfinished audits,
unread decisions, and open escorts remain failures.
The roster-search population trial reached 59 of 63 required releases; its remaining
escort histories show stale search priorities and predictable critical needs during
long journeys. The subsequent `pressure-2048-ready` trial completes every required
release and exact state/event replay, but leaves only 30,000 clear ticks versus the
required 36,000. Its last cleaner lost a fresh completion observation when leaving
for a need break. The subsequent `artifacts/pressure-2048-completion/` primary run
also completes all 63 required releases, but first clears its backlogs at tick
318,000, leaving only 6,000 clear ticks. It fails the unchanged 36,000-tick clear-day
requirement; replay cannot turn that primary failure into a passing population gate.

## Next step — staffing, daily timing and response times

Requested after the user's review of the 32-person logs (2026-10-29). Keep the
station layout and population fixed; expansion remains deferred.

**Status (2026-10-29): all six items are implemented and verified at 32 citizens.**
See [staffing and response progress](staffing-response-progress.md). Summary:
(1) nine guards per 64-person group, three per cohort, by declared conversion of five
cleaners; zero team-gap coverage samples. (2) Bounded escort acknowledgment deadlines
with withdrawal, reassignment and rejected late replies. (3) Sleep ends at the known
wake time, with sleep preparation from the citizen's own rates; missed windows
189 → 6, critical interruptions 168 → 1. (4) Cafeteria choice from fresh learned queue
and stock estimates with hysteresis. (5) Remembered terminal outages shared by
reporting, check-ins, audits and recovery. (6) Response-time stages, declared limits,
causes and an overnight control. The 2,048-citizen result is recorded in
[pressure-station.md](pressure-station.md#current-evidence). The original request follows.

1. **Stagger guard shifts.** The pressure generator currently places every guard
   in cohort 0, leaving predictable gaps in security coverage. Distribute guard
   duty across the day and verify that each interval can supply a complete escort
   team while allowing patrols, breaks and ongoing custody commitments. Merely
   putting one guard on each shift is insufficient. Declare any redistribution of
   existing citizen roles or duty lengths explicitly; do not silently add staff.

2. **Escalate unacknowledged urgent orders; defer emergency alarms.** Add bounded
   acknowledgment deadlines and reassignment when an eligible team does not
   acknowledge. Distinguish posting, delivery, actual reading and acknowledgment;
   handle late replies without duplicate active assignments or custody effects.
   First test overnight coverage with ordinary awake staff. A future emergency
   wake-up must use an explicitly modeled physical alarm or local interaction:
   an inbox message must not wake a sleeper. Add that mechanism only if the
   staffing and response-time evidence shows it is necessary.

3. **Align sleep and meal opportunities.** Sleep currently overlaps some pre-duty
   meal windows, and critical hunger/bladder needs interrupt sleep. Budget time
   for travel, queues, meals, toilet use and check-in around sleep and duty. Check
   physiological rates and thresholds as well as the calendar; preserve actual
   service durations and relief. Classify late eligibility, committed-action
   delays and travel across a window boundary, and verify that due goals are
   retained according to the routine rules. Do not hide misses or reset needs.

4. **Improve cafeteria selection using learned information.** Distance already
   affects selection; extend it with observed or received estimates of queue wait
   and food availability. Retain when and how each estimate was learned, allow it
   to become stale, and use bounded reconsideration after local failure. Citizens
   must not read live remote stock or queues. Verify useful alternatives without
   repeated switching between cafeterias.

5. **Remember terminal outages consistently.** Some retry-delay resets are fixed,
   but reporting, audits and credential recovery need consistent tests for learned
   outages, increasing retry delays and eligible alternative terminals. Travel
   must not erase an observed failure. Respect terminal capabilities and record
   permissions; use bounded rechecks or new observations to discover restoration.

6. **Test response times, including overnight incidents.** Eventual completion is
   insufficient. Measure physical incident to discovery, received report to order,
   order to acknowledgment, acknowledgment to arrival, and arrival to resolution.
   Separate detection delay from institutional response delay. Report median,
   95th-percentile and maximum waits plus unresolved counts and ages. Declare
   fixture-specific limits before assessing a fix. Include shift boundaries,
   overnight incidents, occupied/unavailable guards, terminal outages and late
   acknowledgments; show the cause of each missed response target.

Implementation order: establish the measurements and overnight controls in item 6
alongside staffing and routine changes in items 1 and 3. Then address terminal and
cafeteria choices (5 and 4). Exercise acknowledgment/reassignment from item 2 in
those controls; physical emergency alarms remain a deferred scope addition.

Verify changes first with targeted controls and the 32-person station, then the
same declared workload at 2,048 people. Preserve the full clear-day recovery gate,
daily participation, finite resources, knowledge boundaries, bounded retries,
unique assignments and exact state/event continuation after save/load. Keep
evidence-based review outcomes valid without forcing unsupported reprimands.

## Remaining daily-life verification

Prioritize T22: classify the 46 missed ordinary routine windows in the current normal
32-citizen run and revisit the historical 2,887 in the population run, then prove unread-roster and
routine/check-in priority behavior under sustained work. These counts are distinct
from overdue scheduled rests, which are zero in both runs; a missed window needs
an explanation before it can be classified as an allowed delay or a defect.
Use the small institutional command for iteration (3.309 seconds measured, including
continuation/export and excluding compilation). Run affected controls after fixes,
then the complete suite and population workload at the increment boundary.
Broader concurrent institutional load and seed coverage follow; no full acceptance
gate is closed by this recommended order alone.

Current-build timing triage: `artifacts/routine-32-completion/` passes the two-day
32-person participation gate, with all 64 scheduled rests complete and 46 ordinary
windows missed. Its detailed trace reproduces the benchmark state exactly. Of these
misses, 25 pre-duty meal windows close during committed sleep, 19 sleep goals end
with a same-tick critical interruption (10 hunger, nine bladder), and two duty meals
close during cleaning begun before hunger reaches seek. This replaces the old
48-window count for this build, without changing historical results. The nine-day
small pressure run has 189 misses: 108 pre-duty meals during sleep, 69 sleeps
interrupted by critical needs (eight hunger, 61 bladder), four meals during committed
cleaning, and eight remaining meal cases involving custody, threshold/window edges
or travel across closure. Detailed event references are in each run's
`routine-window-detail.json`. These explain observed timing; they do not close T22's
calibration, goal-retention or integrated unread-roster/priority obligations.

## Acceptance gates

Scope amendment (2026-10-29): first release now has a fixed layout and population.
T11 and R-AUT-01..08 are deferred, as are construction-specific parts of R-TIL-05,
R-MOV-12 and generated construction acceptance. Historical rows below retain their
evidence but are not outstanding expansion obligations. Beds, occupancy, allocation,
shortages, ordinary dispatch and material conservation remain required.
The active combined pressure milestone is specified in [pressure-station.md](pressure-station.md).

`pass` below names the demonstrated test scope; `partial` means some required
variants or integrated clauses remain open. Passing the suite is not proof that
an absent acceptance test passed. Requirement rows below retain conservative
family-level audit limits even where these specific tests pass.

| Gate | Evidence | Current result / missing closure |
| --- | --- | --- |
| T00 | StructuralTests; banned-API compile probe | partial: dependency checks pass; some goal/dispatcher decisions remain in Core |
| T01–T06 | FoundationTests | pass: family connectivity, weighted movement, locality, estimates, frozen segments |
| T07 | InformationTests; RunnerTests generated terminal variants | pass: false delivery and delayed correction |
| T08 | PlanningTests and feature isolation tests | partial: tested citizen/authority boundaries pass; complete decision-code audit open |
| T09 | WorkTests; StackTests; arbitration tests | partial: physical claims/accounting covered; full acceptance inventory audit open |
| T10 | RepairPlanningTests | partial: existing component/fabrication alternatives pass; nested production domains open |
| T11 | Historical construction tests and reports | deferred: expansion removed from first-release scope |
| T12–T14 | ReproducibilityTests; feature snapshots; StationIntegrationTests | pass for implemented states: checkpoints, chunking, concurrent worlds and pure inspection |
| T15 | population/recovery/institutional benchmarks; StationIntegrationTests; RecoveryStationTests; InstitutionalStationTests | partial: finite recovery plus an audit-to-release chain with 2,048 real citizens pass; many simultaneous institutional chains, calibration and full gate inventory remain open |
| T16 | PlanningTests budget exhaustion | pass: bounded failure/backoff and continuation |
| T17 | WorkTests and integration workload | partial: actual ongoing work measured; all blocked-job recovery policies open |
| T18 | AutonomousRecoveryTests; RecoveryGeneratorTests; integrated lost-card variant | partial: real recovery/resumption covered; complete requirement/generator audit open |
| T19 | CleanupOrderTests; IncidentResponseTests; PatrolTests | partial: autonomous cleanup/patrol/replacement covered; broader recovery/generation open |
| T20 | QA, Audit, Personnel, Detention, Guard, Escort and InstitutionalStationTests | partial: integrated chain and eight declared controls pass, including full capacity and replacement after interruption; complete variant/requirement audit remains open |
| T21 | InformationTests; RoutineTests; daily/population terminal use | partial: reads/privacy/queues/offline orders covered; complete contention/security variant audit open |
| T22 | StationIntegrationTests; RecoveryStationTests; InstitutionalStationTests; population runs | partial: daily needs, scheduled rests, custody, staffing waits and resumed work pass for declared shortened days; missed ordinary windows, unread-roster variants and calibration remain open |
| T23 | PlanningTests; RepairPlanningTests | pass: known costs change choices; unknown changes do not |
| T24 | ConstructionGeneratorTests; RecoveryGeneratorTests | partial: declared transformations covered only for implemented generator families |
| T25 | ReproducibilityTests; RunnerTests; feature checkpoint tests | partial: broad continuation coverage; every-fixture × 20-seed × five-tick campaign not closed |
| T26 | AuthorityTests; IncidentResponseTests; RecoveryStationTests; benchmark recovery | partial: finite-burst physical and institutional backlogs drain at 32 and 2,048 citizens; controlled calibration and full specialized institutional workload remain open |
| T27 | NeedTests; NeedServiceTests | pass: exact need accumulation, thresholds, mode changes and continuation |

## Requirement inventory

| Requirement | Implementation (under src unless specified) | Existing evidence | Current status |
| --- | --- | --- | --- |
| R-ARC-01 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-02 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-03 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-04 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-05 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-06 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-07 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-08 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-09 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-ARC-10 | src/Station.Mind; src/Station.Core | StructuralTests | partial: assembly isolation passes; goal and dispatch selection still partly in Core |
| R-DET-01 | Pcg32; BannedSymbols; Snapshots | ReproducibilityTests; StructuralTests | pass: implemented deterministic mechanisms and regression suite |
| R-DET-02 | Pcg32; BannedSymbols; Snapshots | ReproducibilityTests; StructuralTests | pass: implemented deterministic mechanisms and regression suite |
| R-DET-03 | Pcg32; BannedSymbols; Snapshots | ReproducibilityTests; StructuralTests | pass: implemented deterministic mechanisms and regression suite |
| R-DET-04 | Pcg32; BannedSymbols; Snapshots | ReproducibilityTests; StructuralTests | pass: implemented deterministic mechanisms and regression suite |
| R-DET-05 | Pcg32; BannedSymbols; Snapshots | ReproducibilityTests; StructuralTests | pass: implemented deterministic mechanisms and regression suite |
| R-DET-06 | Pcg32; BannedSymbols; Snapshots | ReproducibilityTests; StructuralTests | pass: implemented deterministic mechanisms and regression suite |
| R-DET-07 | Pcg32; BannedSymbols; Snapshots | ReproducibilityTests; StructuralTests | pass: implemented deterministic mechanisms and regression suite |
| R-CLK-01 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-02 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-03 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-04 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-05 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-06 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-07 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-08 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-09 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-10 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-11 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-CLK-12 | World; ClaimArbitration | FoundationTests; ReproducibilityTests | pass: clock, phases, chunking and arbitration regression evidence |
| R-TIL-01 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-02 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-03 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-04 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-05 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-06 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-07 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-08 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-09 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-10 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-TIL-11 | Topology; Construction; Scenario | FoundationTests; ConstructionTests; RunnerTests | partial: component coverage, full integration audit pending |
| R-MOV-01 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-02 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-03 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-04 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-05 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-06 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-07 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-08 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-09 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-10 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-11 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-MOV-12 | Topology; KnownNavigation | FoundationTests; RepairPlanningTests | pass: physical routing and known-cost component tests |
| R-RES-01 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-02 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-03 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-04 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-05 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-06 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-07 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-08 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-09 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-10 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-11 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-12 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-13 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-RES-14 | Work; Stacks; Jobs | WorkTests; StackTests; JobTests | partial: component accounting passes; all work domains not closed |
| R-ACT-01 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-02 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-03 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-04 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-05 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-06 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-07 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-08 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-09 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-10 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-11 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-12 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-13 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-14 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-15 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-16 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-ACT-17 | Station.Mind; World planning files | PlanningTests; RepairPlanningTests | partial: bounded planner passes; integration and all domains outstanding |
| R-KNW-01 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-02 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-03 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-04 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-05 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-06 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-07 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-08 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-09 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-10 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-11 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-12 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-KNW-13 | InformationModel; World.Information | InformationTests; RecordTests | partial: component isolation passes; integrated audit outstanding |
| R-MSG-01 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-02 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-03 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-04 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-05 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-06 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-07 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-08 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-09 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-10 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-11 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-12 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-13 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-14 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-15 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-16 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-17 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-MSG-18 | World.Information; World.CheckIns | InformationTests; RoutineTests | partial: terminal mechanisms pass; population contention not run |
| R-INS-01 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-INS-02 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-INS-03 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-INS-04 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-INS-05 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-INS-06 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-INS-07 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-INS-08 | RecordStore; Personnel; Detention | RecordTests; PersonnelTests; DetentionTests | partial: component protocols pass; full institutional chain audit outstanding |
| R-AUT-01 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-02 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-03 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-04 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-05 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-06 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-07 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-08 | Historical growth/construction implementation | Historical construction tests | deferred: fixed-station first release excludes expansion |
| R-AUT-09 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-10 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-11 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-12 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-13 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-14 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-15 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-16 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-17 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-AUT-18 | Authority; Growth; IncidentResponse | AuthorityTests; GrowthTests; ConstructionGeneratorTests | partial: capacity identification, recovery and T26 outstanding |
| R-LIF-01 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-02 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-03 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-04 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-05 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-06 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-07 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-08 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-09 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-10 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-11 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-12 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-13 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-14 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-15 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-16 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-17 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-18 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-LIF-19 | Needs; DailyGoals; Routines; Conversations | NeedTests; DailyGoalTests; ConversationTests | partial: exact needs pass; integrated T22/T15 being implemented |
| R-WF-01 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests; InstitutionalStationTests | partial: component and generated station chains pass; complete T20/negative variant audit outstanding |
| R-WF-02 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-03 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-04 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-10 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-11 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-12 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-13 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-14 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-15 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-20 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-21 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-22 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-23 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-24 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-25 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-26 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-30 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-31 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-32 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-33 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-34 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-35 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-36 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-37 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-38 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-39 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-40 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-41 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-42 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-WF-43 | Recovery; Patrol; Audits; Escorts | AutonomousRecoveryTests; PatrolTests; AuditTests; EscortMovementTests | partial: component chains pass; full T20/negative recovery audit outstanding |
| R-SNP-01 | Snapshots*.cs | ReproducibilityTests and feature checkpoint tests | partial: feature continuation passes; every-fixture T25 campaign outstanding |
| R-SNP-02 | Snapshots*.cs | ReproducibilityTests and feature checkpoint tests | partial: feature continuation passes; every-fixture T25 campaign outstanding |
| R-SNP-03 | Snapshots*.cs | ReproducibilityTests and feature checkpoint tests | partial: feature continuation passes; every-fixture T25 campaign outstanding |
| R-SNP-04 | Snapshots*.cs | ReproducibilityTests and feature checkpoint tests | partial: feature continuation passes; every-fixture T25 campaign outstanding |
| R-SNP-05 | Snapshots*.cs | ReproducibilityTests and feature checkpoint tests | partial: feature continuation passes; every-fixture T25 campaign outstanding |
| R-SNP-06 | Snapshots*.cs | ReproducibilityTests and feature checkpoint tests | partial: feature continuation passes; every-fixture T25 campaign outstanding |
| R-RUN-01 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-RUN-02 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-RUN-03 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-RUN-04 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-RUN-05 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-RUN-06 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-RUN-07 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-RUN-08 | Station.Runner | RunnerTests | partial: core CLI and declared population/recovery/institutional benchmarks pass; sweep and full CLI acceptance audit outstanding |
| R-OBS-01 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-OBS-02 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-OBS-03 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-OBS-04 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-OBS-05 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-OBS-06 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-OBS-07 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-OBS-08 | World.Emit; Artifacts; tools/viewer | RunnerTests; ResponseChainTests | partial: full viewer and metric coverage outstanding |
| R-TUN-01 | AuthorityConfig | AuthorityTests | partial: configurable controls; profiles and sweeps outstanding |
| R-TUN-02 | AuthorityConfig | AuthorityTests | partial: configurable controls; profiles and sweeps outstanding |
| R-TUN-03 | AuthorityConfig | AuthorityTests | partial: configurable controls; profiles and sweeps outstanding |
| R-TUN-04 | AuthorityConfig | AuthorityTests | partial: configurable controls; profiles and sweeps outstanding |
| R-UNI-01 | unity/README.md | StructuralTests | partial: framework boundary verified; runtime integration not run |
| R-UNI-02 | unity/README.md | StructuralTests | partial: framework boundary verified; runtime integration not run |
| R-UNI-03 | unity/README.md | StructuralTests | partial: framework boundary verified; runtime integration not run |
| R-UNI-04 | unity/README.md | StructuralTests | partial: framework boundary verified; runtime integration not run |
| R-UNI-05 | unity/README.md | StructuralTests | partial: framework boundary verified; runtime integration not run |
| R-UNI-06 | unity/README.md | StructuralTests | partial: framework boundary verified; runtime integration not run |
| R-UNI-07 | unity/README.md | StructuralTests | partial: framework boundary verified; runtime integration not run |
| R-VER-01 | Scenario; ResponseChains; scenarios/generators | ResponseChainTests; ConstructionGeneratorTests; RecoveryGeneratorTests | partial: generated coverage exists; all required generators/holdouts outstanding |
| R-VER-02 | Scenario; ResponseChains; scenarios/generators | ResponseChainTests; ConstructionGeneratorTests; RecoveryGeneratorTests | partial: generated coverage exists; all required generators/holdouts outstanding |
| R-VER-03 | Scenario; ResponseChains; scenarios/generators | ResponseChainTests; ConstructionGeneratorTests; RecoveryGeneratorTests | partial: generated coverage exists; all required generators/holdouts outstanding |
| R-VER-04 | Scenario; ResponseChains; scenarios/generators | ResponseChainTests; ConstructionGeneratorTests; RecoveryGeneratorTests | partial: generated coverage exists; all required generators/holdouts outstanding |
| R-VER-05 | Scenario; ResponseChains; scenarios/generators | ResponseChainTests; ConstructionGeneratorTests; RecoveryGeneratorTests | partial: generated coverage exists; all required generators/holdouts outstanding |
| R-VER-06 | Scenario; ResponseChains; scenarios/generators | ResponseChainTests; ConstructionGeneratorTests; RecoveryGeneratorTests | partial: generated coverage exists; all required generators/holdouts outstanding |
| R-VER-07 | Scenario; ResponseChains; scenarios/generators | ResponseChainTests; ConstructionGeneratorTests; RecoveryGeneratorTests | partial: generated coverage exists; all required generators/holdouts outstanding |
