# Station disruption and recovery

Snapshot-format note: the performance evidence below belongs to the archived
format-42 build. The later [institutional milestone](institutional-station.md) uses
format 43 and includes deliberate behavior fixes. Reuse the archived executable for
old binary checkpoints; current code can regenerate workloads from scenarios.

Goal: first expose and fix behavior with 32 citizens, then run the same workload
with 2,048. Ordinary operation is followed by a finite burst of lost cards,
equipment faults and communication disruption. Citizens must discover problems,
report them, receive institutional responses and finish physical recovery while
meals, sleep and ordinary work continue. No scripted response commands are allowed.

Implementation plan:

1. Generate finite normal work and finite disruption work using the existing daily
   services, roster, credential, facility-reporting and overdue-report systems.
2. Record phase measurements and per-citizen participation; require physical
   recovery, delivered resolution reports and a draining institutional backlog.
3. Run 32 citizens, investigate failures and add regression coverage for fixes.
4. Run the same generator with 2,048 citizens, verify checkpoint continuation and
   record timings, memory and every unmet acceptance condition.

The inherited shortened-day fixture uses 36,000 ticks per configured day; it does
not establish default 24-hour pacing. Final measurements and limitations will be
recorded below. **The requested 32-to-2,048 recovery goal is verified for this
declared workload.** This is not full first-release or default-pacing acceptance.

## Findings from 32 citizens

- The first trial's 200 dirt units per district were exhausted before the third
  cohort worked. The finite normal backlog is now 400 units per district, with
  another 100 added in the burst. All 32 citizens make physical work progress.
- A failed recovery desk's exclusion expired at exactly the next retry. A citizen
  repeatedly chose an empty printer while another had stock. The exclusion now
  survives the next retry; a dedicated two-desk regression verifies local failure,
  travel to the alternative, physical issuance and resumed work.
- Routine wakeups bypassed report retry delays, and finding a plan reset backoff
  even when its action failed. Retained delays and success-based reset reduce
  failed actions in the five-day trial from 50,055 to 333. These failures are
  counted, not hidden; the gate rejects a retry storm.
- Concurrent cleanup could leave a job's physical status in progress after another
  worker removed its last dirt unit. Physical target completion now updates all
  jobs for that tile, independently of individual work credit or authority reports.
- Seeds 42 and 7 pass the complete small-station gate and checkpoint continuation.
  Seed 91 recovers physically but produces no overdue report; its gate deliberately
  fails that coverage condition. A regression verifies this rejection, rather than
  treating an unexercised branch as successful recovery.

The run spans five configured days: a normal day, disruptions beginning at tick
38,000, terminals disconnected from 39,500 to 48,000, then recovery and a sustained
observation period to 180,000. The physical burst consists of two card losses,
one consumed installed machine cartridge and 100 dirt units per 16-person district.
Card stock, spare parts, meals and services are finite. All citizens are declared
maintenance generalists (role `cleaner`), also eligible for worker-status patrols.
This exercises dispatch and physical patrol behavior, not specialized guard staffing.
Initial per-citizen cleaning orders overlap on their district's work tile. Physical
dirt is removed once, with accounting checks; the number of completed orders is
not the amount of work. The physical cleanup workload is 1,000 dirt units at 32
citizens and 64,000 at 2,048, including the burst. This shared-work contention is
what exposed the stale physical completion status described above.

## Verified small run

`artifacts/recovery-32-dispatch/recovery-result.json` passes: 32 active workers,
four recovered credentials, two repaired machines, one overdue incident with
delivered work and patrol responses, and all 34 jobs physically complete with
completion reports received. Every citizen eats and sleeps each configured day;
all due scheduled rests complete. Physical cleanup drains by tick 54,000; all
measured facility tasks are resolved by 78,000 and remain resolved through 180,000.
Checkpoint continuation from 48,000 using 997-tick chunks matches the final digest.
All action requests have declared autonomous or system-protocol origins and
positive earlier cause IDs. The system-protocol actions include consenting
conversation partners, not scripted citizen commands.

The complete suite passed 798 tests after the behavioral fixes. Alternative-desk
route search now shares one frontier per immutable actor view. Institutional
dispatch now indexes accepted origin reports and public commitments once per
selection instead of rescanning them for every candidate. The small run's full
trace and final digest are byte-identical before/after the dispatch optimization.
The first population attempt was deliberately interrupted after tick 42,000 to
replace repeated full-history dispatch scans and include the cleanup-status fix.
It has no completed timing or acceptance result. The replacement run uses an
isolated Release build in `artifacts/recovery-build-verified/`; results are targeted
to `artifacts/recovery-2048-verified/`. That run completed with no gate failures;
its checkpoint continuation also matches exactly.

Latest complete regression: **801 passed, zero failed, zero skipped**, saved in
`artifacts/recovery-tests/perf-final2.trx`. The recovery-only check
passed 84 tests before the dispatch optimization; the full suite covers it afterward.

## Reproduction and evidence

From `ImperialStation/`, build Release and run:

```powershell
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark recovery --count 32 --out artifacts/recovery-32
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark recovery --count 2048 --out artifacts/recovery-2048
```

`recovery-result.json` is the acceptance result; `result.json` contains only the
inherited participation measurements and must not be mistaken for full acceptance.
Both populations get the same checks. `institutions.json` contains final jobs,
attempts, incidents, recovery cases, facility tasks and expectations. Ordinary
`scenario.json`, `catalog.json` and `checkpoint.json` are also exported.
`burst-end.bin` saves the state at tick 48,000. The harness restores it and checks
the final digest with 997-tick advancement chunks; its elapsed time is separate
from the primary execution timing.

The 32-citizen run writes the full trace. Both sizes write selected institutional
events to `institution-events.jsonl`; all events are still inspected and counted
before each diagnostic trace buffer is drained. Primary execution timing includes
accounting, participation, phase sampling, trace processing and the burst-end
snapshot. It excludes final exports and the second checkpoint continuation.
Allocation is cumulative allocation, not live memory; peak working set measures
the process. Concurrent diagnostic runs mean these are engineering samples,
not controlled comparative performance measurements.

## Verified population result

`artifacts/recovery-2048-verified/recovery-result.json` reports `completed`, an
empty failure list, zero action requests without declared provenance, and
`checkpointMatches: true`. The process exited successfully. An independent read
of the final institution export confirms zero unfinished jobs, open facility
tasks, missing patrol outcomes or unissued accepted recovery cases.

| Measurement | Verified population result |
| --- | ---: |
| Citizens / active workers during the run | 2,048 / 2,048 |
| Horizon | 180,000 ticks; five shortened configured days |
| Primary execution | 861.335 seconds |
| Checkpoint continuation verification | 529.662 seconds |
| Cumulative allocated bytes, primary execution | 182,499,710,328 |
| Process peak working set before continuation | 669,995,008 bytes |
| Events | 12,003,518 |
| Physical dirt removed | 64,000 units |
| Lost credentials / recovered | 256 / 256 |
| Replacement cards issued / blank cards consumed | 112 / 112 |
| Existing cards retrieved | 144 |
| Faults / installed parts consumed / final broken machines | 128 / 128 / 0 |
| Overdue incidents / received patrol outcomes | 18 / 18 |
| Completed jobs with received completion reports | 2,176 |
| Facility tasks / final open tasks | 305 / 0 |
| Meals consumed | 35,816 |
| Completed scheduled rests / overdue rests | 10,240 / 0 |
| Citizens missing a day's meals or sleep | 0 |
| Maximum terminal queue | 14 |
| Corrected maximum need-service queue wait | 309 ticks = 30.9 seconds |
| Failed actions during the whole run | 15,805 |
| External action commands | 0 |

After communications return at tick 48,000, all 18 overdue work incidents have
received work responses by 54,000. Dirt reaches zero by 60,000. All machines and
facility tasks are recovered by 90,000, and stay recovered through 180,000.
Every citizen performs physical cleaning during the normal phase; 1,095 citizens
continue cleaning on day two. Cleaning stops when the finite backlog is empty;
repairs continue into day three. No new busywork is generated to inflate activity.
Every citizen eats at least two meals per configured day. The lowest daily sleep
total is 11,890 ticks, compared with the configured 12,000-tick sleep activity.
See `daily-distribution.json` for daily distributions.

Population build fingerprint:
`8c75b161cc286bc7577127d5de3cd1fec95bd640e1ed28fdd2913a5ae5b3defa`.
Final state digest:
`f782dd78dc4def0a6aa64b36c35f1eb27d11f5a267c90bab7ea4f469f3a52597`.
That fingerprint identifies the preserved original population build. Subsequent
Core optimizations remove historical scans and share immutable data; their
behavior and performance evidence is recorded below. Runner changes correct
queue-wait accounting and print checkpoint progress. The corrected wait
calculation is covered by the nine passing integration tests in
`artifacts/recovery-tests/need-wait-integration.trx`; the final Release build has
zero warnings or errors.

### Measurement correction and remaining limits

The original participation export reports a maximum wait of 13,484 ticks. That
was a diagnostic bug: rejected requests with no service start were counted as
waiting until the end of the run. Waiting now ends at service start, rejection or
cancellation; only still-queued requests use the horizon. Recomputing from the
saved checkpoint gives 309 ticks. The original export is preserved; see
`need-activities.json` and `need-wait-correction.json` for the correction evidence.

There were 37 meal-service rejections due to local stock exhaustion. Citizens
found alternatives, and nobody missed a day's meals. There were also 5,580 missed
routine meal/sleep windows and 4,521 critical-need interruptions. These remain
visible tuning targets even though actual meals, sleep, mandatory rest and
recovery all pass. Allocation volume and runtime still merit profiling.

This workload uses shortened days, maintenance generalists, overlapping cleanup
orders and a finite known disruption set. Specialized guard staffing, audits and
custody, remaining T22 variants, controlled timing calibration and default 24-hour
pacing remain outside this completed goal and open in the acceptance matrix.

## Faster iteration — 2026-10-29

The performance pass preserves the generator, population, horizon, random seed,
trace generation, event ordering and all acceptance checks. Derived indices are
rebuilt from canonical state on restore; the snapshot schema is unchanged.

- Keep each sender's latest accepted authority-message metadata as messages arrive,
  avoiding repeated scans and rebuilding of the entire message history for dispatch.
- Reuse a facility review's candidate list while selections fail. A successful
  dispatch invalidates it, including when it supersedes a previous assignment.
- Index physical items by location for inventory and local sensing. All insertion,
  removal and movement use central helpers; accounting verifies index consistency.
- Track only live meal reservations for stock conflicts, retain the complete meal
  history, and avoid event-queue scans when no service-grant event exists.
- Share immutable seeded need-service definitions between actor views. Newly
  constructed berths still require each actor's observed knowledge.
- Index unread normal and recovery mailboxes at delivery/read/restore, preserving
  the ordered set of messages captured when a finite inbox read begins.
- Store per-search known distances in compact sorted-coordinate arrays, retaining
  frontier tie order and checked arithmetic. Three weighted-grid tests compare
  arbitrary repeated targets against the independent physical pathfinder; the
  existing overflow-boundary test remains green.

The first CPU profile attributed about 60% of its samples to repeated dispatch
candidate construction. The same profiled 2,048-citizen checkpoint window
(ticks 48000–51000) fell from 97.876 to 39.344 seconds after the first changes;
both runs emitted 690,868 events and the digest
`639be46147e2ff81a3a6c23fae1b786e41fa3411132165216aeaa734a389720a`.
These instrumented windows guided the changes; they are not full-run timings.

The final small acceptance run in `artifacts/recovery-32-fast2/` passes with a
2.688-second primary execution and 1.204-second checkpoint continuation. Its full
trace is byte-identical to `recovery-32-dispatch/` (SHA-256
`6c88813d9c35755bde0bb5bb66355c3289c4411cf17bb773918e8c1d05369e3e`).
The final complete suite passed all 801 tests in `perf-final2.trx`.

The final population primary execution in `artifacts/recovery-2048-fast2/` takes
**320.194 seconds (5m20s), versus 861.335 seconds (14m21s)** in the original run:
about **2.69× faster** for the same 2,048 citizens and 180,000 ticks. It emits the
same 12,003,518 events and final state digest. Cumulative primary allocation is
97,600,635,040 bytes, down from 182,499,710,328 (about 46.5% less); peak working set
is 668,614,656 bytes, essentially unchanged. Allocation savings do not imply an
equivalent reduction in peak resident memory. These are engineering measurements
on the same machine, not repeated controlled statistical benchmarks.

The final isolated build is `artifacts/recovery-build-fast2/`, fingerprint
`3a238d260512accdeddfed7a048feaaa01d30b1b2b4366a186c8134586af9f07`.
Its burst-end snapshot is byte-identical to the original (SHA-256
`62a2acacfb5307bc3c8e1bc23e72614bd22b6bdd809c11907cb774f9fabae2e9`).

The final population recovery gate reports `completed`, no failures, and matching
checkpoint continuation. The independent comparison in
`artifacts/recovery-performance-comparison-final.json` confirms identical phase
measurements, per-type event counts and non-timing citizen participation. Scenario,
catalog, burst-end checkpoint, institutional records and institutional trace files
are byte-identical. The previously corrected queue-wait metric is 309 ticks.

| Timed scope | Original | Optimized |
| --- | ---: | ---: |
| Primary execution | 861.335 s | 320.194 s |
| Checkpoint continuation | 529.662 s | 128.723 s |
| Primary plus continuation | 1,390.997 s | 448.917 s |
| Entire optimized CLI, including initialization and exports | not recorded | 456.775 s (7m37s) |

The final run used no profiler or competing simulation process. Its whole-command
timing and exit code are in `artifacts/recovery-2048-fast2-wall.json`. The earlier
optimization trial remains in `recovery-2048-fast/`; its replay was sampled for
20 seconds to identify the remaining inbox and navigation costs. The original
verified artifacts and intermediate measurements are preserved.

For daily iteration, run the 32-citizen acceptance command above. It includes
disruption, recovery and checkpoint replay. Use the 2,048-citizen gate for changes
that affect scale or dispatch behavior. The optional
[bounded probe](../tools/Station.Profile/README.md) helps isolate slow phases from
an existing checkpoint and reports restore time separately.
