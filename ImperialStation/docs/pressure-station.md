# Fixed station under sustained pressure

Requested 2026-10-29. Status: implementation and verification in progress.

Staffing, daily timing and response times: the six issues from the user's 32-person
log review are implemented ([progress](staffing-response-progress.md), requirements in
[acceptance-current.md](acceptance-current.md#next-step--staffing-daily-timing-and-response-times)).
Expansion and physical emergency alarms remain deferred.

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

Acceptance: 32 citizens first, then the same generator at 256 (reduced from 2,048 on
2026-10-29 by user decision; earlier 2,048 trials below remain history). Require overlapping
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

## Workload

The generator uses nine shortened configured days, each 36,000 ticks at 100 ms.
Needs rates and routine durations inherit the explicitly accelerated daily fixture;
this is not real-time calibration. Population, geometry, beds and supplies are fixed.
Each 64-person group has two misconduct subjects, one supervisor and nine guards;
the remaining citizens clean and repair. The 32-person station uses one group.
Staffing redistribution (2026-10-29, declared): five former cleaners per group
(`b+8..b+12`) became guards, staggered three per cohort; previously four guards all
worked cohort 0. Escort teams need an hour of published duty left, the authority holds
a team while its own dispatches and receipts fill the declared facility capacity, and
an escort team must acknowledge within two configured hours or is withdrawn and
replaced. See [staffing and response progress](staffing-response-progress.md).
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
Waves begin at 6,000, 42,000 and 78,000; public terminals disconnect 500 ticks later
and reconnect at 12,000, 48,000 and 84,000. Daily services, repair parts, replacement
cards, guard equipment and meals are finite. Terminal and need queues are sampled,
and conservation is asserted throughout primary and checkpoint execution.
Assigned bedrooms and their published roster rest locations sit across each
district's maintenance corridor from meals. Daily travel supplies local observations;
the authority cannot discover an unvisited fault by reading physical truth.
Overlapping institutional chains are counted from distinct reprimanded accounts
until release, including the audit/request stages before guards are dispatched.
Resumed work includes inspecting an assigned task and confirming that another
worker has already completed it. If all assignments are terminal, a later inbox
read and on-duty boundary prove return to idle duty; that is counted separately.
At least one released subject must resume a real available task.

## Current evidence

- **Staffing and response build (schema 47, 2026-10-29, cloud run).** The 32-person
  gate passes with exact state and every continuation event after restoring tick
  12,000. The backlog clears at tick 198,000; four reprimands, two releases, both
  released subjects resume work. There are 10 missed ordinary windows (previously 189),
  all classified with zero retention violations, and 1 critical need interruption
  (previously 168). Guard team-gap samples: 0 of 325. Maximum custody response:
  10,000 ticks, caused only by the single detention slot. Repairs may pre-empt cleanup
  (machine-fault ordering wait 30,126 → 0) and spills are measured by their own units.
  Full response tables are in [staffing-response-progress.md](staffing-response-progress.md).
- **256 citizens, same build (2026-10-29, cloud run).** All pressure gates pass with exact
  state and continuation-event replay from tick 12,000 (digest `fe17e866…`; primary
  148 s, replay 134 s). The backlog clears at tick 264,000; 14 reprimands, 6 releases,
  all six released subjects resume work; two cases end as evidence-based
  `NeedsReview`/`StaleWorkRecords`. No guard team-gap samples (0 of 325), 69 missed
  ordinary windows (all classified, zero retention violations), no critical need
  interruption, every citizen eats and sleeps each day, at most one cafeteria
  reconsideration per meal. One escort team was withdrawn and replaced at its
  acknowledgment deadline. Machine-fault ordering never waits (maximum 0 ticks).
  Getting this run green exposed and fixed two defects: restore rejected an
  authorization waiting for detention capacity before team selection, and an audit
  never rechecked its bound desk while its supervisor remembered an unvisited, less
  confirmed outage elsewhere (one case stayed pending). The 2,048-citizen run was
  stopped at the user's request (overkill for this step).
- The complete suite passes all **860 tests**, zero failures or skips
  (`artifacts/staffing-tests/staffing-response.trx` in the cloud run).

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
