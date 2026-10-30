# Fixed station under sustained pressure

Requested 2026-10-29. Status: implementation and verification in progress.

Next work: [staffing, daily timing and response times](acceptance-current.md#next-step--staffing-daily-timing-and-response-times),
covering the six issues raised in the user's 32-person log review and their
recommended fixes. That section defines the implementation order and verification
requirements; expansion and physical emergency alarms remain deferred.

Completion clarification approved by the user: insufficient evidence is a valid
explicit outcome. Every initial claim must have a completed verification and either
an accepted case, a received clear inspection, or a documented evidence-based review
outcome. Pending audits, unread decisions, missing permissions and invalid provenance
do not qualify. Reprimands must match accepted cases exactly; all subjects who
actually qualify for detention must still complete custody and return to duty.
Pending cases are included in backlog samples and must remain settled throughout
the final clear day. The previous requirement for exactly two reprimands per scripted
subject is superseded by this evidence-based rule; no citizen is punished merely
because the test knows what the initial script did.

Use one fixed layout and population. Several genuine bad-installation/false-report
cases compete for supervisors, guards, custody capacity and terminals while finite
waves of machine faults, card losses, spills and terminal outages create ordinary
recovery work. Only initial misconduct and environmental disturbances are scripted;
all responses must use the existing autonomous simulation.

Acceptance: 32 citizens first, then the same generator at 256 (the population gate
since 2026-10-30; earlier 2,048 trials below are historical). Require overlapping
work and institutions, local credential recovery, completed repairs, authorized
detention/release and resumed work, continued daily meals/sleep and roster activity,
conserved finite resources, no duplicate assignments/effects or retry storms, drained
physical and institutional backlogs, and an undisturbed observation period. Restore
a checkpoint captured during pressure and compare the final canonical state and
continuation events under different advancement chunks. Report queue histories,
blocked outcomes, missed routine windows and all limitations. A green component
suite alone does not satisfy this milestone.

Expansion is deferred by the user's scope decision. Do not add construction to
this workload or treat outstanding construction gates as required release work.

## Staffing, daily timing and response times (2026-10-30)

This increment implements items 6, 1 and 3 of the
[next step](acceptance-current.md#next-step--staffing-daily-timing-and-response-times).
Items 5, 4 and 2 follow in a later section. The population gate is now 256 citizens.

Declared workload changes (all in `PressureStation.Generator.cs`):

- **Staffing redistribution.** Five existing cleaners per 64-citizen group
  (b+21..b+25) become guards. Each group has nine guards rotated through the three
  cohorts in ID order: a two-guard escort team plus one guard per interval for
  patrols, breaks or ongoing custody. Subjects and supervisors stay on cohort 0.
  No citizen is added and no duty length changes. Misconduct and card-loss actors
  are never converted. At 32 citizens: 2 workers, 1 supervisor, 9 guards, 20 cleaners.
- **Wave schedule.** Wave 1 starts at 6,000 (day duty), wave 2 at 60,000 (the
  24,000 shift boundary while cohort 0 sleeps) and wave 3 at 87,000 (evening, after
  day duty). Each terminal outage still starts 500 ticks after its wave and ends
  6,000 ticks after it; recovery is measured from the last disturbance (93,000).
- **Corridor bedrooms.** Assigned beds and published rest tiles moved from x+2 to
  the maintenance corridor at x+1, where spills land. At x+2 every district but the
  last slept one tile from its neighbour's cafeteria and toilet, so nearest-service
  travel stopped crossing the corridor: in the daytime control nobody entered tile
  x=1 from day 3 to day 7 and a 100-unit spill went unreported for 208,000 ticks.
  The earlier critical-need detours had been masking this.
- **Variants** (`--variant`): `single-cohort-guards` (the old four cohort-0 guards on
  the new schedule; comparison control), `daytime` (staggered guards, the earlier
  all-daytime waves) and `overnight` (all waves at the shift boundary while the day
  cohort sleeps). Controls report the workload-exercise checks (overlap, overdue
  work, shared guard duty) as `controlLimitations` instead of failing on them.

Daily-life changes (core, schema 46): sleep opens one configured hour early with the
same close; needs forecast from own values and configured sleeping rates to become
critical during the known bed duration are relieved first (routine or urgent sleep;
a failed relief backs off and sleep proceeds); a meal below seek is needed when hunger
would become critical before the next known meal window; missed windows record
`CommittedAction`, `TravelAcrossBoundary`, a blocked reason or `NeverSelected`.
Regressions: `PreSleepReliefTests` (relief before bed with no critical need while
asleep, sleep without a known cafeteria, identical continuation after restore).

New measurements in `pressure-result.json`:

- `responses`: per chain (facility, escort, patrol, case, credential) and stage,
  completed count, median, p95, maximum, unresolved count, oldest unresolved age,
  declared target and classified causes of every miss, plus up to 400 exception
  chains with stage timestamps. Detection and reporting are separate from the
  institutional stages. Targets are in `PressureStation.ResponseTargets` and are
  reported, not gated. The report reads only the trace and final snapshots.
- `guardCoverage`: per 64-citizen group, guards per cohort and the number of
  100-tick samples in which fewer than two on-duty guards were free, split into
  roster, needs/breaks and escort/patrol commitments. The gate requires at least
  three guards per cohort and zero roster gaps when staffing is staggered.
- `participation.missedDailyGoalReasons`: missed windows by kind and reason.

Evidence at 32 citizens (this host, .NET 8.0.33):

| Run | Gate | Clear from | Missed windows | Critical sleep interruptions | No-team samples (roster/needs/commitments) |
| --- | --- | --- | --- | --- | --- |
| `pressure-32-baseline` (previous build) | passes | 258,000 | 189 | 168 | 2,160 / 213 / 0 |
| `p32-default` | passes, exact replay | 288,000 | 19 | 0 | 0 / 187 / 90 |
| `p32-single-cohort-guards` | passes, exact replay | 288,000 | 18 | 0 | 2,160 / 44 / 0 |
| `p32-daytime` | passes, exact replay | 198,000 | 19 | 0 | 0 / 182 / 90 |
| `p32-overnight` | passes, exact replay | 216,000 | 22 | 0 | 0 / 182 / 48 |

All runs: 4 reprimands, 2 releases, 288 scheduled rests complete, none overdue,
every citizen eats and sleeps each day, maximum individual failures 20–32.

Response times in `p32-default` (ticks; targets in brackets):

| Stage | n | Median | p95 | Max | Missed: cause |
| --- | --- | --- | --- | --- | --- |
| escort dispatch [1,500] | 2 | 0 | 0 | 0 | — (single-cohort control: 23,594, `NoOnDutyEscortTeam`) |
| escort acknowledgment [3,000] | 2 | 259 | 311 | 311 | — |
| escort arrival [3,000] | 2 | 424 | 455 | 455 | — |
| escort resolution [1,500] | 2 | 140 | 7,470 | 7,470 | 1 custody journey |
| patrol dispatch [1,500] | 4 | 1 | 1 | 1 | — (control: max 14,539, `NoOnDutyGuard`) |
| patrol acknowledgment [3,000] | 4 | 698 | 1,781 | 1,781 | — |
| facility detection [3,000] | 7 | 0 | 0 | 0 | — |
| facility dispatch [1,500] | 13 | 0 | 71,900 | 71,900 | 6 `NoUncommittedOnDutyResponder` |
| facility acknowledgment [3,000] | 13 | 5,757 | 24,889 | 24,889 | 5 `ResponderOffDuty`, 2 `TerminalOutage` |
| facility resolution [3,000] | 13 | 1,699 | 217,336 | 217,336 | 5 `ResponderOffDuty`, 1 work duration |
| credential detection [1,500] | 6 | 0 | 16,500 | 16,500 | 2 `LossHolderAsleep`, 1 off duty |
| case detection [4,500] | 4 | 3,084 | 5,996 | 5,996 | 2 institutional review |

Remaining findings and limitations:

- Facility dispatch selects only on-duty, uncommitted qualified responders. Every
  cleaner holds an initial cleaning assignment, so machine repairs wait until one
  finishes (up to 71,900 ticks). This is institutional policy, not staffing.
- Facility acknowledgment is late when the chosen cleaner is off duty; item 2
  (deadlines and reassignment) is the intended fix.
- A single assignee cleans a corridor at 300 ticks per unit and only on duty;
  accumulated spills took up to 217,336 ticks, with overdue replacements in between.
  The default therefore clears at 288,000, exactly the required clear day. The
  population run may fail this margin.
- Card losses discovered by a sleeping or off-duty holder wait until they wake.
- `CommittedAction` pre-duty meals: 11 citizens were still asleep when the window
  opened (their sleep started late after relief or a conversation).
- Response targets are fixture assessments for the shortened day, not calibrated
  real-time limits. Cause classification uses published rosters, recorded need
  activities and terminal events; `ResponderOccupied` is the residual category.
- The 2,048-citizen run of this build was stopped at tick 36,000 when the population
  gate changed to 256.

Population gate, `artifacts/pressure-256-staggered/` (four groups, nine days):
**passes**, including exact state and continuation-event replay and the independent
`tools/audit-pressure.cjs` audit (371 assignments, no duplicate active assignment).
Backlogs clear from tick 288,000, exactly the required 36,000 clear ticks. 16
reprimands; all 8 qualifying subjects released and all 8 resumed real work. Every
citizen eats and sleeps each day; 2,304 scheduled rests complete, none overdue; one
critical need interrupted sleep; maximum individual failures 23. 136 missed windows:
117 `CommittedAction` (82 pre-duty meals opening during sleep), 19 `NeverSelected`.
Every group has three guards per cohort and zero roster-gap samples (needs/breaks
144–187, commitments 0–96 per group). Primary execution 86.8 s, replay 81.7 s.
State `5d0c4526…eb01`, continuation `52125ac3…8ff3`.

| Stage at 256 | n | Median | p95 | Max | Missed: cause |
| --- | --- | --- | --- | --- | --- |
| escort dispatch | 8 | 0 | 0 | 0 | — |
| escort acknowledgment | 8 | 1,297 | 6,394 | 6,394 | 2 `TerminalOutage` |
| escort arrival | 8 | 888 | 30,654 | 30,654 | 4 `ResponderOffDuty` (team formed late in shift) |
| patrol dispatch | 12 | 1 | 113,692 | 113,692 | 5 `GuardsCommittedOrUnavailable` |
| facility dispatch | 83 | 0 | 80,879 | 82,214 | 32 `NoUncommittedOnDutyResponder` |
| facility acknowledgment | 83 | 5,909 | 25,223 | 25,440 | 31 `ResponderOffDuty`, 14 `TerminalOutage` |
| facility resolution | 83 | 1,876 | 215,254 | 217,105 | 32 `ResponderOffDuty`, 5 work duration |
| credential detection | 48 | 169 | 16,500 | 16,500 | 16 `LossHolderAsleep`, 8 off duty |

## Workload

The generator uses nine shortened configured days, each 36,000 ticks at 100 ms.
Needs rates and routine durations inherit the explicitly accelerated daily fixture;
this is not real-time calibration. Population, geometry, beds and supplies are fixed.
Each 64-person group has two misconduct subjects, one supervisor and nine guards
(four before 2026-10-30; see above); the remaining citizens clean and repair. The 32-person station uses one group.
There are four bad-installation/false-report cases per group. Only these initial
actions are scripted. Each subject's two incidents run contiguously so an autonomous
inbox read cannot start in a gap and interrupt the finite trigger. The population
generator test advances through all triggers and verifies 128 physically bad
installations before inspection begins. Dedicated worker terminals isolate startup
login traffic; records, recovery and ordinary reporting share public terminals afterward.
Each supervisor has paired records grants at the existing public desk in their home
district. Granting every supervisor every desk selected terminal 1 under the audit
policy's lowest-ID rule, inadvertently concentrating all population audits there.
Local binding scales use of the fixed infrastructure while retaining competition
with the district's ordinary reports and credential recovery.
Each guard starts carrying one physical equipment kit and must equip it through an
ordinary finite action. Remote equipment search is outside these declared disruptions.

Each 16-person district has 400 initial dirt units, three 100-unit spills, one
machine targeted by three fault waves, and three different credential-loss subjects.
An already empty machine consumes no additional cartridge; `MachineFaulted` counts
declared disturbances and `FaultPartConsumed` counts their actual destructive effects.
The passing 32-citizen run has six fault events consuming four installed cartridges.
Waves begin at 6,000, 60,000 and 87,000 (6,000, 42,000 and 78,000 before
2026-10-30 and in the `daytime` control); public terminals disconnect 500 ticks later
and reconnect 6,000 ticks after each wave starts. Daily services, repair parts, replacement
cards, guard equipment and meals are finite. Terminal and need queues are sampled,
and conservation is asserted throughout primary and checkpoint execution.
Assigned bedrooms and their published roster rest locations open onto each
district's maintenance corridor (x+1; x+2 before 2026-10-30). Daily travel supplies local observations;
the authority cannot discover an unvisited fault by reading physical truth.
Overlapping institutional chains are counted from distinct reprimanded accounts
until release, including the audit/request stages before guards are dispatched.
Resumed work includes inspecting an assigned task and confirming that another
worker has already completed it. If all assignments are terminal, a later inbox
read and on-duty boundary prove return to idle duty; that is counted separately.
At least one released subject must resume a real available task.

## Historical evidence (before 2026-10-30)

Entries naming 2,048 citizens are historical; that population is no longer run.


- `artifacts/pressure-32-completion/` passes all nine-day gates and the independent
  assignment-history/case audit, including exact state and every continuation event.
  Backlogs stay clear
  from tick 258,000 through 324,000. All 288 scheduled rests complete; every citizen
  eats and sleeps each day. Maximum individual failures: 32. There are 189 missed
  ordinary routine windows, still reported as a calibration limitation.
- `artifacts/pressure-128-provisioned/` is an additional passing diagnostic. Its
  backlog clears at tick 240,000. This is not a substitute for the population gate.
- `artifacts/pressure-256-team-wait/` passes all gates and exact state/event replay.
  Its backlog clears at tick 240,000; all eight subjects are released, seven resume
  work and one returns to idle duty after its assignments have finished.
- `artifacts/pressure-512-issued/` completed its nine-day primary diagnostic with
  all backlogs clear, 4,608 completed scheduled rests, no overdue rests, and daily
  meals/sleep for all citizens. Maximum individual failures: 77. Its optional replay
  was stopped to reduce host load and prioritize the required population trial;
  this is not a completed pressure acceptance result. There are 2,665 missed
  ordinary routine windows, retained as a calibration limitation.
- `artifacts/pressure-512-fast/` now passes every gate, including exact state and
  continuation-event replay: 32 reprimands, 16 releases, 15 subjects resuming work
  and one returning to idle duty. All backlogs remain clear from tick 264,000 through
  324,000. Its primary final state matches the earlier issued-kit build exactly.
- `artifacts/pressure-2048-incident-cache/` reached the nine-day horizon but did
  not pass. Physical and ordinary work backlogs drained, and all citizens ate and
  slept each day with no overdue scheduled rests. However, only 121 of 128 required
  reprimands and 35 of 64 releases completed; 22 escort dispatches remained open.
  Two guards reached 100 and 104 failed actions, exceeding the individual bound.
  Exporting the 210,826,568-character inline checkpoint exceeded the JSON writer's
  limit before combined validation/replay. `result.json` is participation evidence
  only. The end state reconstructed in `pressure-2048-diagnostic/` has the exact
  same canonical digest, `ec1aece7e8cf2cc84f56a05b7ad982225d58fdbcd87c8f79c518f39ed46718af`.
  Three initial discrepancies were repaired before their inspections, two audits
  ended with `AmbiguousHandoff`, and two filings with `StaleWorkRecords`. These
  are explicit evidence-based outcomes; they must not be forced into reprimands.
  Nineteen of the 22 open escorts were newly dispatched at the final day boundary;
  three were preparing. The corrected fixture and retry policies are now being
  verified in `pressure-2048-local-records/`, with all original gates retained.
  Slower indexed and guard-eligibility trials remain interrupted diagnostics.
- `artifacts/pressure-2048-local-records/` completes exact state/event replay but
  does not satisfy recovery: 127 accepted cases, 54 releases, nine open escorts.
  The remaining initial case has a read `StaleWorkRecords` decision, a valid explicit
  outcome under the user's clarification. Maximum individual failures fell to 48;
  ordinary work, faults, dirt and overdue responses all drained. Retained orders
  show late-shift teams abandoning searches during off-duty time, often before
  their first inspection. The full-cycle deadline and current-work search sites
  were then exercised in `artifacts/pressure-2048-roster-search/`.
- The roster-search population trial reached 127 accepted cases, one explicit review
  and 59 of 63 required releases, with four escorts open at the horizon. Its saved
  histories show repeated searches of an older terminal clue after received negative
  reports, plus long custody journeys interrupted by hunger or bladder thresholds.
  The following `artifacts/pressure-2048-ready/` trial completes all 63 required
  releases and exact state/event replay, but fails the full clear-day requirement:
  its first drained sample is tick 294,000, leaving only 30,000 of the required
  36,000 ticks. It remains failed evidence.
- That trial's last cleanup attempt physically finished at tick 264,291, but its
  report arrived at 288,162. A due need moved the cleaner away before planning
  retained local completion; the worker then had to revisit next shift. Completion
  is now retained immediately after the fresh zero-dirt observation, while delivery
  still requires ordinary terminal transport. A regression fails before the fix and
  passes afterward, including exact state/event replay. The unchanged nine-day
  population gate in `artifacts/pressure-2048-completion/` still fails its primary
  recovery check: all 63 required releases complete, but the first clear sample is
  tick 318,000, leaving only 6,000 clear ticks. The cleanup regression is fixed;
  that correction alone does not establish timely population recovery.
- The latest complete suite passes all **843 tests**, zero failures or skips:
  `artifacts/pressure-tests/pressure-all-completion.trx`. Recovery assertions now
  accept an authenticated original reply only when no patrol had been posted.
- Guard regressions cover returning to inspect a target after leaving, reinspection
  after a failed local handoff, and a finite team search across routine breaks.
  The deadline uses the known local formation time plus one configured roster cycle
  (at least two inspection durations). `TeamNotReady` does not claim target absence
  or read remote partner state. Receipt allows the authority to replace the team.
- Replacement teams demote a terminal clue after a newer received negative search;
  a newer authenticated visit restores its priority. The earlier site remains a
  candidate because a local negative report does not establish global absence.
  A two-branch regression verifies both evidence orderings and exact continuation.
- A guard with an identified target checks whether their own urgent need, or an
  already-due toilet visit, will reach critical during the known route and published intake duration. Ordinary relief
  precedes custody when needed; short journeys retain existing priority. This uses
  no remote partner state or live service queue. At the co-located handoff each
  participant can refuse based on their own readiness. Preparation survives travel
  to relief; returning guards inspect the target again. Accelerated food/toilet
  regressions create the need after order adoption, then prove relief before custody,
  no critical need before intake, and identical checkpoint continuation. The earlier
  `pressure-2048-journey` diagnostic was stopped after tick 72,000 because this
  stronger control exposed preparation being abandoned during travel; it is not
  completion evidence.
- Audit retry backoff increases through an outage and resets only after successful
  login/query at the assigned records terminal. Checkpoint continuation preserves it.
- Credential recovery also retains failure backoff across travel between desks.
  The population trace showed successful movement resetting the delay before each
  subsequent offline-terminal attempt. A two-desk outage regression fails before
  the correction and passes afterward, including service restoration, resumed work,
  conserved resources and exact state/event continuation. The updated small proof
  passes its full assignment-history audit; population acceptance is still pending.
- A failed custody handoff retains a growing retry delay when a local team
  precondition is unmet. Ordinary action wakeups cannot shorten that delay; finding
  the same BeginEscort plan does not reset it. The existing decision timer, reason
  and backoff persist the wait without a schema change. A delayed-partner regression
  reproduces 13 failures before the correction, bounds retries afterward, then
  proves successful custody and exact continuation. It reads no remote partner state.
- Snapshot schema is now 45 because the validated search-abandonment deadline
  changed. Schema-44 snapshots require their original build. The 128-citizen ordinary
  baseline's 1,802,775 encoded bytes differ only at the schema byte (44 to 45);
  its reference hash was updated after that comparison.
- A late-shift regression now carries a formed team's search across sleep, completes
  custody on the next duty period and reproduces exactly after restoration. The
  split-team control still ends with a bounded `TeamNotReady` report. Search orders
  omit cancelled/completed work destinations using received authority status; the
  last reported terminal visit and published rest location remain search evidence.
- Completed escort status is checked before scanning guard-report history during
  dispatch. The final 512-citizen checkpoint yields identical candidates and state;
  20 candidate scans took 6.757 seconds before and 0.042 seconds after in the local
  microbenchmark (`artifacts/pressure-candidates-before.json` and `-after.json`).
  This is not an end-to-end speed claim. The full 32-citizen state and continuation
  trace are identical before/after. The slow `pressure-2048-issued` run was replaced
  with the equivalent optimized build before completing acceptance.
- Active guard checks also use the ledger's existing record-kind history index.
  It is rebuilt by the existing restore path and adds no persisted state. The final
  indexed build passes all tests and preserves the full 32-citizen state/event hashes.
  The 2,048-citizen tick-12,000 checkpoint is byte-for-byte identical before/after
  this index change (SHA-256 `68e47e51f63712da99632cf34fd12f8369007dff47947e3b82236a33fd36e99e`).
- Escort dispatch now applies the policy's actor, role and duty eligibility filters
  before evaluating commitments. Previously each pending case scanned every citizen's
  work history even when all guards were off duty. Selection still uses the same
  policy and ordering. All 824 tests pass; the full 32-citizen trace, event counts,
  phases, final state and continuation hash are identical to the indexed build.
  The population checkpoint at tick 12,000 also retains the SHA-256 above.
- Incident review reuses its candidate list while cases wait on the same received
  evidence, rebuilding it immediately after posting replacement work or a patrol.
  A regression exercises three simultaneous incidents with dual-role responders:
  all six replacement/patrol responders are distinct, including after restoration.
  All 825 tests pass, and the full 32-citizen state, trace and phase results remain
  identical to the earlier builds. The population checkpoint at tick 12,000 also
  retains the SHA-256 above. This cache is local to one review, not persisted.
- A full detention intake waits for a local capacity/service change. The earlier
  128-citizen run exposed 4,044 failures by one guard; the new regression covers a
  full facility, subsequent release, and exact checkpoint continuation while waiting.
- Late original completion cancels only unposted replacement work. A received
  original reply closes an unposted welfare search. Issued orders remain independent.
- Replacement-chain validation now runs after every branch has been deserialized;
  the regression restores both before and after a descendant result arrives.
- Extending observation from eight to nine days exposed unplanned food shortages.
  Initial ordinary stock is now 60 meals per citizen, fixed before tick zero; no
  replenishment is injected. The earlier `*-current` runs and interrupted population
  trial used 30 meals per citizen and are retained as diagnostics.

Current digests and continuation hashes are stored in each `pressure-result.json`.
Both state and event continuation match after restoring tick 12,000 in the passing
32-, 256- and 512-citizen runs (the 256-citizen run precedes issued guard kits).
The final work histories were also reconstructed independently for the current
32- and 512-citizen proofs: 54 and 781 assignments respectively, with no duplicate
active assignment at any history transition. Reconstructed active assignments match
the final checkpoint. Their `completion-audit.json` files record these checks plus
daily participation, the complete disciplinary chains, recovery and replay evidence.

## Earlier diagnostic findings

- At 2,048 citizens, a one-tick gap between scripted incidents allowed a normal
  inbox read to block the last bad installation. The `*-team-wait` population run
  was interrupted after confirming the invalid trigger; no finding was invented.
- At 512 citizens, reserve guards forgot remote equipment facts while awaiting
  assignments. Their orders correctly remained pending with `EquipmentUnavailable`.
  The final fixture issues one kit per guard in initial inventory, without marking
  it equipped or granting custody. Remote/stale equipment behavior remains covered
  separately in `GuardPreparationTests`. These trials are diagnostic evidence only.
- Initial fixture correction: autonomous facility reporting interrupted the finite
  misconduct script. Worker misconduct triggers now use dedicated local terminals
  and the worker role does not double as a facility reporter. Other roles still
  observe/report conditions, and all citizen responses remain autonomous.
- Moving the first outage until after initial verification gives the workload real
  concurrent disciplinary chains. Earlier legitimate repairs sometimes removed a
  discrepancy before inspection; those trials correctly did not create a finding.
- The combined workload exposed a stale root incident after a replacement needed
  replacing. Received descendant outcomes now follow the explicit replacement chain
  back to its ancestors. They do not impersonate original-worker replies or change
  physical state. The targeted incident suite passed all ten tests.
- The first complete 32-citizen proof (`artifacts/pressure-32-fixed/`) passed: four
  reprimands, two releases, all six lost cards recovered, shared patrol/escort guards,
  and backlogs clear from tick 228,000 through 288,000. All citizens ate and slept
  on all eight days; all 256 scheduled rests completed. There were 140 missed
  ordinary routine windows, retained as a calibration limitation.
- Both final state and every continuation event match after restoring tick 12,000
  and advancing in 997-tick chunks. The primary full small trace SHA-256 is
  `ea091b4e36acaea99fb97221fddf60b6481d84255f2d5abd667cf6212eacd885`.
- Population profiling exposed expensive QA candidate scans. Eligibility filtering
  now precedes commitment checks, and one review reuses candidates while removing
  each assigned supervisor. The small result and entire trace remain byte-identical.
  The complete suite passed 819 tests before the batching optimization; all 30
  affected QA/incident/structural/pressure tests passed afterward.
- The earlier `artifacts/pressure-2048-fast2/` population trial was stopped after
  the intake retry defect was reproduced. It is not successful population evidence.
- The earlier small run (`artifacts/pressure-32-queues/`) also verifies that every
  terminal and every need-service queue emptied during the final observation day.
  All 289 primary accounting checks passed; continuation separately asserts accounting
  after each advancement. Final state is
  `a9bff95ad632988382850e03649974884015f7a2ee1b084a28826502f04d93a4`.

Commands from `ImperialStation/`:

```powershell
dotnet run --project src/Station.Runner -c Release -- benchmark pressure --count 32 --out artifacts/pressure-32
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark pressure --count 256 --out artifacts/pressure-256
```

`pressure-result.json` is the gate result; the inherited `result.json` only reports
participation. Exact scenario/catalog, the pressure checkpoint, final checkpoint,
phase metrics and institutional histories are exported. Population traces retain
selected events, but the continuation hash inspects every event in both executions.
`pressure-primary.json` exposes primary validation before the replay; its
`awaiting_replay` status is not a completed acceptance result. Replay progress is
printed separately to stderr without changing simulation events or state.
Benchmark final checkpoints now use a schema-2 `checkpoint.json` manifest with a
sibling `checkpoint.bin`, preserving the full state without JSON's single-string
size limit. Keep both files together. The manifest checks the binary SHA-256 and
the restored canonical digest; `inspect checkpoint.json` loads both formats.

After a completed current-format run, export `work-history.json` with
`inspect checkpoint.json --view work-history`, then run
`node tools/audit-pressure.cjs RUN_DIRECTORY`. The independent audit checks every
assignment transition and cross-checks each declared case against the exported
inspection, audit, filing, accepted case and read decision. Old diagnostic results
retain their original acceptance status; they are not silently relabelled as passes.
