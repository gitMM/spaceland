# Current acceptance matrix

Updated 2026-10-30: current suite **856 passed, zero failed** (`artifacts/pressure-tests/routines-all.trx`), snapshot schema 49; the pressure gate passes at 32 and 256 citizens for five seeds ([details](#remaining-daily-life-verification)). The paragraphs below up to the first dated section are the 2026-10-29 baseline. Initial stabilization: 785 passed, zero failed; artifacts/integration-tests/stabilization.trx (historical). Component evidence is not full handoff acceptance. This matrix supersedes historical outstanding-work lists. Initial integration results are recorded in integration-baseline.md; current institutional evidence is linked below.
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

Population scope (user decision, 2026-10-30): population runs and tests use **256
citizens** (four 64-citizen groups), not 2,048. The 2,048-citizen results below are
historical evidence and are no longer pursued as a gate.

The [combined pressure workload](pressure-station.md) now passes at 32 citizens:
overlapping recovery and disciplinary chains, shared guards, all daily meals/sleep,
drained work and service queues, conservation and exact state/event continuation.
Earlier 2,048-citizen trials exposed institutional backlogs and the inline checkpoint
export limit; those defects are fixed, but population acceptance remains pending. Expansion
is deferred. Current nine-day evidence is in `artifacts/pressure-32-completion/`, with
a passing 512-citizen diagnostic in `artifacts/pressure-512-fast/`, including exact replay.
The previous complete suite passed **843 tests** in
`artifacts/pressure-tests/pressure-all-completion.trx`; after the staffing and
daily-timing step below it passed **849** (`staffing-timing-all.trx`); the current
count is 856. Authenticated original replies
now close unposted searches without generating unnecessary patrol work. See the pressure report for
the current fixture, finite guard searches, audit retry backoff, intake waiting,
reply handling, and checkpoint fixes. Snapshot schema was 45 then; it is now 49. Explicit evidence-based
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

Requested after the user's review of the 32-person logs (2026-10-29). This is the
next implementation and verification step, not a completed milestone. Keep the
station layout and population fixed; expansion remains deferred.

**Status 2026-10-30: all six items are implemented and verified at 32 and 256
citizens (items 6, 1 and 3 first, then 5, 4 and 2). With all six, the 256-citizen
population gate passes: clear from 288,000, exact replay, independent audit.**
The suite passes **854 tests**, zero failures or skips
(`artifacts/pressure-tests/items-all.trx`). Snapshot schema is 49.
Details, evidence and limitations are in the
[pressure report](pressure-station.md#staffing-daily-timing-and-response-times-2026-10-30).

| 32 citizens, 9 days | Before (`pressure-32-baseline`) | Staggered default (`p32-default`) |
| --- | --- | --- |
| Gate | passes, clear from 258,000 | passes, clear from 288,000; exact state/event replay |
| Samples with no rostered escort team | 2,160 of 3,241 | 0 (old-staffing control on the new schedule: 2,160) |
| Escort authorization to dispatch | max 23,594 (`NoOnDutyEscortTeam`) | max 0 |
| Critical needs interrupting sleep | 168 | 0 |
| Missed ordinary routine windows | 189, unclassified | 19, all classified |

The baseline column ran on this host's .NET 8 SDK and reproduced the documented
`pressure-32-completion` result (same 258,000 clear tick and 189 missed windows).
The default's later clear tick is not a regression in itself: its last disturbance
is at 93,000 instead of 84,000, and its corridor cleanup is slow (see item 6). On the
baseline's own all-daytime schedule the `daytime` control clears at 198,000.

1. **Stagger guard shifts.** The pressure generator currently places every guard
   in cohort 0, leaving predictable gaps in security coverage. Distribute guard
   duty across the day and verify that each interval can supply a complete escort
   team while allowing patrols, breaks and ongoing custody commitments. Merely
   putting one guard on each shift is insufficient. Declare any redistribution of
   existing citizen roles or duty lengths explicitly; do not silently add staff.

   *Done (32).* Five existing cleaners per 64-citizen group (b+21..b+25, never a
   misconduct or card-loss actor) become guards: nine guards, three per cohort, so
   each interval has a two-guard team plus one for patrol, breaks or custody. Duty
   lengths and population are unchanged. The gate now requires three or more guards
   per cohort and zero roster gaps. Sampled every 100 ticks, on-duty guards lacked a
   free team in 277 of 3,241 samples: 187 from needs/breaks, 90 from escort/patrol
   commitments, none from the roster. Before item 3, needs alone caused 542 such
   samples, mostly on-duty guards still in bed. The `single-cohort-guards` control
   keeps the old staffing on the same schedule and shows 2,160 roster-gap samples.

2. **Escalate unacknowledged urgent orders; defer emergency alarms.** Add bounded
   acknowledgment deadlines and reassignment when an eligible team does not
   acknowledge. Distinguish posting, delivery, actual reading and acknowledgment;
   handle late replies without duplicate active assignments or custody effects.
   First test overnight coverage with ordinary awake staff. A future emergency
   wake-up must use an explicitly modeled physical alarm or local interaction:
   an inbox message must not wake a sleeper. Add that mechanism only if the
   staffing and response-time evidence shows it is necessary.

   *Done (32 and 256), conservative scope.* Each escort dispatch now carries an
   acknowledgment deadline of two configured hours (3,000 ticks here). If *no* team
   member's acknowledgment has been received by then, the authority posts a reassignment
   notice (`EscortRevoked`, claim `AcknowledgmentOverdue`) for every order, records the
   assignments as `Unacknowledged`, releases those guards and dispatches a replacement
   from guards who did not go silent. A team in which someone acknowledged is not
   revoked, because it may already be moving; its partner wait stays bounded by the
   existing `TeamNotReady` deadline. Posting, delivery, actual reading and receipt of the
   acknowledgment are separate events and response stages. A reply after reassignment
   is recorded as `GuardAcknowledgmentLate` and restores no commitment; the guard who
   reads the notice drops the order (`KnownOrderReassigned`) and reports it, which
   changes nothing for the current team. `EscortDispatchTests` covers a silent first
   team, the replacement, both late replies, snapshot round trip and identical
   continuation. Work orders keep their existing acknowledgment watches.
   Overnight coverage was tested with ordinary awake staff only: with staggered guards
   no escort or patrol order went unacknowledged at 32 or 256 citizens (no escalation
   fired), so **no physical emergency alarm was added**; it stays a deferred scope item.

3. **Align sleep and meal opportunities.** Sleep currently overlaps some pre-duty
   meal windows, and critical hunger/bladder needs interrupt sleep. Budget time
   for travel, queues, meals, toilet use and check-in around sleep and duty. Check
   physiological rates and thresholds as well as the calendar; preserve actual
   service durations and relief. Classify late eligibility, committed-action
   delays and travel across a window boundary, and verify that due goals are
   retained according to the routine rules. Do not hide misses or reset needs.

   *Done (32).* The sleep opportunity opens one configured hour early with the same
   close, so a full 12,000-tick sleep ends 1,500 ticks before the pre-duty meal.
   Before sleeping (routine window or urgent sleep pressure), a citizen relieves
   hunger or bladder that its own value and the configured sleeping rate say would
   become critical during the known bed duration; failed relief backs off and sleep
   proceeds. A meal below seek counts as needed if hunger would become critical
   before the next known meal window. Service durations, relief amounts and rates are
   unchanged. Missed windows now record `CommittedAction` (15 in the default run,
   mostly a pre-duty meal opening while the citizen is still asleep), `NeverSelected`
   (4) or travel across the close. Critical sleep interruptions fell from 168 to 0;
   all 288 scheduled rests complete and every citizen eats and sleeps each day.

4. **Improve cafeteria selection using learned information.** Distance already
   affects selection; extend it with observed or received estimates of queue wait
   and food availability. Retain when and how each estimate was learned, allow it
   to become stale, and use bounded reconsideration after local failure. Citizens
   must not read live remote stock or queues. Verify useful alternatives without
   repeated switching between cafeterias.

   *Done (32 and 256).* Each citizen keeps private cafeteria estimates learned in
   person: the queue seen on arrival, the wait actually experienced, and meals seen
   remaining after eating (or none after a failed meal), each with its own tick and
   source. Estimates older than 6,000 ticks are ignored. A fresh wait adds only its
   excess over one service duration to that cafeteria's cost, so ordinary queues cause
   no switching; a cafeteria last seen empty is least preferred but still chosen when
   nothing else is known, so restocking is discovered. Live remote queues and stock are
   never read; the existing per-service failure backoff remains the bounded
   reconsideration after a local failure. Received (told) estimates are not modelled.
   `ServiceEstimateTests`: only the diner who waited about two rounds moves to the
   farther cafeteria; one-round waits do not switch. Fails before, passes after.

5. **Remember terminal outages consistently.** Some retry-delay resets are fixed,
   but reporting, audits and credential recovery need consistent tests for learned
   outages, increasing retry delays and eligible alternative terminals. Travel
   must not erase an observed failure. Respect terminal capabilities and record
   permissions; use bounded rechecks or new observations to discover restoration.

   *Done (32 and 256).* A citizen whose login, post, read or query fails at a disabled
   or disconnected terminal *while standing at it* records a private learned outage
   (first/last observed tick, failures, recheck time). Travel and unrelated actions
   never clear it; only a later successful login there (a new local observation)
   does. Report and check-in planning prefer eligible alternatives (same capability
   and permitted role) while the recheck is pending; the delay starts at the planner's
   1,000-tick maximum backoff and doubles to 8,000. If every eligible terminal is
   known to be out, the citizen keeps its ordinary bounded retry and rechecks the
   oldest outage first. Records audits keep their assigned terminal (record
   permissions); credential recovery keeps its existing per-desk retry memory.
   `TerminalOutageTests` fail before and pass after. At 32 citizens failed actions
   fell from 449 to 350 and the median facility acknowledgment from 5,757 to 542 ticks.

6. **Test response times, including overnight incidents.** Eventual completion is
   insufficient. Measure physical incident to discovery, received report to order,
   order to acknowledgment, acknowledgment to arrival, and arrival to resolution.
   Separate detection delay from institutional response delay. Report median,
   95th-percentile and maximum waits plus unresolved counts and ages. Declare
   fixture-specific limits before assessing a fix. Include shift boundaries,
   overnight incidents, occupied/unavailable guards, terminal outages and late
   acknowledgments; show the cause of each missed response target.

   *Done (32).* `pressure-result.json` has a `responses` report and `guardCoverage`.
   Chains: facility (machine fault/spill), escort, patrol, case and credential.
   Stages: incident, detection, report received, order, delivery, reading,
   acknowledgment, arrival and resolution; detection and reporting are kept apart
   from the institutional stages. Each stage reports completed count, median, p95,
   maximum, unresolved count and oldest unresolved age. Targets were declared before
   judging (`PressureStation.ResponseTargets`; one configured hour = 1,500 ticks),
   for example escort dispatch 1,500, escort acknowledgment 3,000 and facility
   dispatch 1,500. Every missed or unresolved stage carries a cause such as
   `NoOnDutyEscortTeam`, `NoOnDutyGuard`, `NoUncommittedOnDutyResponder`,
   `ResponderOffDuty`, `ResponderAsleep`, `TerminalOutage` or `LossHolderAsleep`,
   with `/AcrossShiftBoundary` when the stage spans a shift change. The default wave
   schedule now has waves in day duty, at the 24,000 shift boundary while the day
   cohort sleeps, and in the evening; `daytime` and `overnight` controls cover the
   other schedules. Targets are reported, not gates. The default run still misses 25
   targets; the largest are facility dispatch waits of up to 71,900 ticks
   (`NoUncommittedOnDutyResponder`: every cleaner already holds a cleaning
   assignment) and corridor cleanup resolution of up to 217,336 ticks.

Implementation order: establish the measurements and overnight controls in item 6
alongside staffing and routine changes in items 1 and 3. Then address terminal and
cafeteria choices (5 and 4). Exercise acknowledgment/reassignment from item 2 in
those controls; physical emergency alarms remain a deferred scope addition.

Verify changes first with targeted controls and the 32-person station, then the
same declared workload at 256 people. Preserve the full clear-day recovery gate,
daily participation, finite resources, knowledge boundaries, bounded retries,
unique assignments and exact state/event continuation after save/load. Keep
evidence-based review outcomes valid without forcing unsupported reprimands.

## Follow-up — population margin and unacknowledged facility orders (2026-10-30)

The 256-citizen gate passed with no margin (first clear sample 288,000, exactly one
clear day). Dirt was the binding backlog; machines were repaired by tick 120,000. The
response report showed repair dispatch waiting up to ~82,000 ticks for a cleaner with
no assignment, single cleaners taking up to ~217,000 ticks on accumulated corridor
spills, and facility orders waiting up to ~25,000 ticks for an off-duty assignee.

- **Repair priority.** Facility and incident repair dispatch may choose a qualified
  on-duty responder whose only commitment is routine cleanup; uncommitted responders
  are still preferred. Escort, patrol, repair and verification commitments still bind.
- **Evidence hold.** A machine is not dispatched for repair while the authority's own
  quality check of its claimed repair is scheduled, with a supervisor or assigned, nor
  after a reported discrepancy until a case decision for that repair has been posted
  (bounded to one configured day). Without the hold, repair priority let the subject
  or a cleaner repair sabotaged machines before inspection or filing (0 reprimands at
  32; two `StaleWorkRecords` reviews at 256).
- **Cleanup helpers.** When the latest received report for a cleanup task exceeds what
  one cleaner clears in a duty period (duty ticks / clean-unit ticks), up to two extra
  cleanup orders for the same tile go to on-duty, uncommitted cleaners (at most four
  over the task's life). Each is its own job; the tile's last removed unit completes all.
- **Unacknowledged facility orders.** A facility repair or cleanup order with no
  acknowledgment two configured hours after dispatch goes to another eligible on-duty
  responder through the ordinary superseding dispatch (`OrderCancelled` to the silent
  one). A late reply reaches only the superseded attempt. Helper orders are not
  reassigned. `FacilityTests.UnacknowledgedCleanupOrderIsReassignedAndTheLateReplyChangesNothing`
  fails without the change and passes with it, including restore and continuation.

| Run | Clear from | Clear ticks | Reprimands / releases | Failed actions (max) | Audit |
| --- | --- | --- | --- | --- | --- |
| `p32-item2` (before) | 288,000 | 36,000 | 4 / 2 | 350 (22) | passed |
| `p32-margin` | 144,000 | 180,000 | 4 / 2 | 352 (20) | passed, 66 assignments |
| `pressure-256-item2` (before) | 288,000 | 36,000 | 16 / 8 | 2,663 (23) | passed |
| `pressure-256-margin` | 138,000 | 186,000 | 16 / 8 | 2,756 (20) | passed, 509 assignments |

At 256: 83 helper orders, 47 reassigned facility orders, all 16 cases accepted, all 8
qualifying subjects released and back at work, exact state and event replay. Facility
dispatch waits are now only the evidence hold (max 48,753, `EvidenceHoldForQualityVerification`).
Remaining: 41 facility acknowledgments still exceed the target because the assignee went
off duty (`ResponderOffDuty`), when no eligible replacement was on duty. The pressure
test now requires backlogs clear by day six. The suite passes **855 tests**
(`artifacts/pressure-tests/margin-all.trx`); snapshot schema stays 49.

## Remaining daily-life verification

Refreshed 2026-10-30 for the current pressure workload (the 46-window
`routine-32-completion` and 189-window nine-day counts below were earlier builds
and are kept only as history).

**Missed routine windows (T22).** `pressure-result.json` now gives every missed
window one category (see [pressure-station.md](pressure-station.md)); the gate fails
on any `Unexplained`. The 32-citizen acceptance run `artifacts/p32-default/` has 21
misses, all allowed:

| Category | Count | Explanation |
| --- | --- | --- |
| `InitialSleepWindow` | 11 | Day-0 pre-duty meals: every citizen starts in a pre-start sleep (tick 10 to ~12,000). A fixture-start effect, not a timing defect. |
| `LateEligibility` | 5 | Duty meals during cleaning whose hunger reached seek only 90–219 ticks before the close; a meal below seek is not needed by design. |
| `InCustody` | 2 | Subject 1 under escort or detained across a duty and an after-duty meal. |
| `EscortOrCustodyDuty` | 2 | Guards 4 and 21 between reading an escort order and intake. |
| `CredentialRecovery` | 1 | Actor 2 recovering its lost card at the first wave. |

Zero critical needs interrupted sleep. Sleep interrupted by critical needs (69 in the
older nine-day run) and pre-duty meals during sleep (108) no longer occur outside day 0.
Building the classifier exposed one miscount: custody was counted from intake, so a
subject waiting under escort for intake capacity (seed 2026) was `Unexplained`.
Custody now runs from `CustodyBegan`.

**Check-in priority under sustained work.** `checkIns` measures on-duty time between
completed inbox reads, after removing declared outages, custody, escort duty, credential
recovery and sleep. The gate is two periodic intervals (3,000 ticks). At 32 citizens
the worst gap is 2,109 ticks (median of per-citizen worst 1,902) over 3,000 reads.
Cleaners keep cleaning through 300-tick units and check in between them within about
600 ticks of their own due time; the 8,350/7,100-tick raw gaps for guards 21 and 4
are escort duty (15,747 excluded on-duty ticks).

**Unread rosters.** The `roster-change` control publishes a new cohort for three
cleaners per group at tick 42,000 during sustained cleaning. At 32 citizens every
change was read only through the citizen's own inbox read (43,442, 46,522 and 58,516).
All 53 duty boundaries of the changed cleaners follow the roster the citizen knew at the time. Cleaners 11
and 12 made their pre-duty check-in at the old roster's time, which the new roster
would not have scheduled. Cleaner 12's new shift had already started while it was
unread, so it correctly stayed off duty until it read the change. The shift-cycling
gate allows one fewer duty period per change. Observed limitation: cleaner 10 read a
shift that started 4,558 ticks later and began its full 12,000-tick sleep immediately,
sleeping 7,453 ticks into the new shift (excluded as `Sleeping`). Sleep start does not
yet weigh the known duty start.

**Seed coverage (T25/T26).** Seeds 7, 91, 1234 and 2026 at 32 citizens (`artifacts/seeds/p32-s*`)
all pass the gate, exact state and continuation replay and the independent audit:
4 reprimands and 2 releases each; clear from 138,000 (150,000 for seed 7); 18–21
missed windows, none needing attention; worst check-in gap 2,012–2,088; distinct
state digests.

At 256 citizens every run passes the gate, exact state and continuation replay and
the independent audit, with 16 reprimands, all 8 qualifying subjects released and
no missed window needing attention:

| Run | Clear from | Missed windows | Worst check-in gap | Assignments (max failures) |
| --- | --- | --- | --- | --- |
| `pressure-256-routines` (seed 42) | 138,000 | 125 | 2,322 | 509 (20) |
| `pressure-256-roster-change` | 138,000 | 125 | 2,322 | 512 (24) |
| `seeds/p256-s7` | 144,000 | 122 | 2,162 | 513 (21) |
| `seeds/p256-s91` | 138,000 | 122 | 2,422 | 510 (21) |
| `seeds/p256-s1234` | 138,000 | 118 | 2,929 | 529 (23) |
| `seeds/p256-s2026` | 138,000 | 130 | 2,422 | 511 (24) |

The seed-42 misses are 82 `InitialSleepWindow`, 27 `LateEligibility`, 13 `InCustody`,
2 `EscortOrCustodyDuty` and 1 `CredentialRecovery`. In the roster control all 12
changed cleaners adopted their new roster only by reading it; all 212 of their duty
boundaries follow the known roster and 8 old-roster-only check-ins occurred before
the reads. Seed 1234's 2,929-tick gap is close to the 3,000 bound; the bound is a
declared assessment choice, not a calibrated one. The complete suite passes **856
tests** (`artifacts/pressure-tests/routines-all.trx`).

T22 still lacks calibration of the declared targets and roster-aware sleep start; the
missed-window, check-in and unread-roster obligations now have integrated evidence.

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
| T15 | population/recovery/institutional benchmarks; StationIntegrationTests; RecoveryStationTests; InstitutionalStationTests | partial: finite recovery plus an audit-to-release chain with 2,048 real citizens pass (historical; population gate is now 256); many simultaneous institutional chains, calibration and full gate inventory remain open |
| T16 | PlanningTests budget exhaustion | pass: bounded failure/backoff and continuation |
| T17 | WorkTests and integration workload | partial: actual ongoing work measured; all blocked-job recovery policies open |
| T18 | AutonomousRecoveryTests; RecoveryGeneratorTests; integrated lost-card variant | partial: real recovery/resumption covered; complete requirement/generator audit open |
| T19 | CleanupOrderTests; IncidentResponseTests; PatrolTests | partial: autonomous cleanup/patrol/replacement covered; broader recovery/generation open |
| T20 | QA, Audit, Personnel, Detention, Guard, Escort and InstitutionalStationTests | partial: integrated chain and eight declared controls pass, including full capacity and replacement after interruption; complete variant/requirement audit remains open |
| T21 | InformationTests; RoutineTests; daily/population terminal use | partial: reads/privacy/queues/offline orders covered; complete contention/security variant audit open |
| T22 | StationIntegrationTests; RecoveryStationTests; InstitutionalStationTests; population runs | partial: daily needs, scheduled rests, custody, staffing waits and resumed work pass for declared shortened days; every missed window classified (none unexplained), check-in gaps bounded and the `roster-change` control pass at 32 and 256 (2026-10-30); calibration and roster-aware sleep start remain open |
| T23 | PlanningTests; RepairPlanningTests | pass: known costs change choices; unknown changes do not |
| T24 | ConstructionGeneratorTests; RecoveryGeneratorTests | partial: declared transformations covered only for implemented generator families |
| T25 | ReproducibilityTests; RunnerTests; feature checkpoint tests | partial: broad continuation coverage; pressure workload replays exactly for four extra seeds at 32 and 256 (2026-10-30); every-fixture × 20-seed × five-tick campaign not closed |
| T26 | AuthorityTests; IncidentResponseTests; RecoveryStationTests; benchmark recovery | partial: finite-burst physical and institutional backlogs drain at 32 and 256 citizens for seeds 42, 7, 91, 1234 and 2026 (clear from 138,000–150,000; 2026-10-30); controlled calibration and full specialized institutional workload remain open |
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
