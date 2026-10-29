# H3 autonomous cleanup orders and missing-cleanup workflow

The standalone simulation now runs the missing-cleanup workflow with autonomous original,
replacement and patrol actors. Cleanup uses typed tile targets through the existing
read/accept/work/report protocol. This advances the first-release scope; the full goal
remains active, with facility reporting/task generation and later institutions unfinished.

## Implemented behavior

CleanTile jobs record a tile and reported workload, separately from RepairMachine jobs.
Published cleanup rules expose roles, tools, positions and unit durations, never current
dirt. The bounded Mind planner can pick up a known tool, travel, inspect on arrival and
clean one unit at a time. Each move or suspension invalidates the prior arrival check.
Only checked local zero dirt completes the goal. If that attempt removed no dirt, its
terminal WorkCompleted report says NoWorkNeeded. Tools remain physical inventory.

Orders are adopted only after a completed read; posting acceptance enables work.
Cleanup supports existing suspension, offline cancellation, lease expiry and shift
retention. Multiple cleanup orders for a citizen execute in stable attempt order.
Authority deadlines use reported units and published durations, frozen at acknowledgment
receipt. Replacement selection and patrol candidates use permitted public/recorded evidence.
Schema 14 retains the typed targets, publication, workload, local checks, counters and
in-flight work. Restore verifies private read provenance and job/message/target agreement.

## Canonical evidence

`scenarios/missing_cleanup_report.json` has **24 assertions and no scripted actors**.
The original crosses two 51-tick physical edges while authority travel uses nominal
Manhattan estimates. The work tile has no terminal. Replacement and patrol learn their
orders at ordinary check-ins, and all reports are posted from physical terminals.

| Tick | Observed event |
| --- | --- |
| 0 / 2 | Original order posted / delivered. |
| 4 / 6 / 8 | Original reads / posts acceptance / acceptance reaches authority. |
| 8 | Frozen estimate is 18 ticks; deadline is 31. |
| 31 / 32 | Missing report opens; one admission token posts replacement and patrol orders. |
| 34–51 | Both response orders are delivered but unread. Original still follows its last read order. |
| 52 / 54 | Responders read their orders / post acceptance. |
| 56 | Patrol's local inspection finds the original absent from its last terminal location. |
| 60 / 63 | Replacement removes the two dirt units; physical cleanup completes. |
| 71 | Replacement result reaches authority; work resolves by report while patrol remains outstanding. |
| 108 / 110 | Original finally arrives, inspects the clean tile and creates its NoWorkNeeded report. |
| 166 / 173 | Patrol finds the original absent at the job site / its negative report reaches authority. |
| 218 / 219 | Original reads cancellation / its previously posted late result reaches authority. |

The original result remains on attempt 1; replacement result remains on attempt 2.
Two units were removed in total, tools remain held, 70 actions complete and none fail.
The negative patrol result leaves worker status unresolved. Completing cleanup never
establishes welfare or current location, and a failed local search does not imply death.

## Verification

The full Release test suite passes: **247 passed, 0 failed, 0 skipped**.
Build/test compilation has zero warnings/errors. Evidence is in
`artifacts/test-results/h3-cleanup-orders.trx`.

Fifteen new test cases cover the canonical timeline, already-clean replacement,
either unavailable response role, hidden-state independence, suspension/tool retention,
lease expiry, known-tool pickup, exact-deadline delivery, unread orders, off-duty
extension, multiple retained orders, invalid/duplicate acknowledgment messages,
transformed IDs/geometry and generated continuation runs.

Twenty generated worlds vary seeds, actual traversal, inspection and cleaning durations.
Twelve checkpoints each give **240 byte-stable continuations**, with matching remaining
traces. Tick-by-tick advancement, getters, disabled tracing and concurrency 2/4/8 agree.
Renaming actor/item/tile/job/credential/terminal IDs and rotating/translating geometry
preserves normalized deadline/result timing and response outcomes. Physical traversal
disturbances give different original-worker positions under identical authority views;
the initial deadline, dispatches and patrol candidate coordinates remain unchanged.

The off-duty variant retains a partial clean across duty end, then posts its report at
137. Its base deadline is 110 and its extended deadline 151; the known 41-tick extension
prevents an erroneous incident. The exact-deadline variant uses a real terminal result
received in phase 4, suppressing the same-tick phase-5 watchdog. Wrong-attempt/account,
unrelated and repeated acknowledgment messages do not satisfy or extend the expectation.
Shared `JobTests` and `AuthorityTests` also verify repeated result idempotency and one
duration-learning sample per attempt.

Strict canonical replay completes with the same digest and all assertions passing.

| Evidence | Value |
| --- | --- |
| Build | `873497bda1684a9e08ea7bb3fdd4c0e54d592faf37c876240f1f480c3edf76bc` |
| Catalog content | `6c8791f7dbb1e6c9fea51a2cd63fee86477d16c1dede30d28a7abb90e6cb1487` |
| Final digest | `81e39f96f3a7a2f4869a7fe04032fdc1a1a42323c6d66d4ad85275484acfc23f` |
| Run | `artifacts/missing_cleanup_report/` |
| Replay | `artifacts/missing_cleanup_report-replay/` |

```powershell
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/missing_cleanup_report.json --out artifacts/missing_cleanup_report --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/missing_cleanup_report/replay.json --out artifacts/missing_cleanup_report-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/missing_cleanup_report/checkpoint.json --view incidents
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/missing_cleanup_report/checkpoint.json --view orders --actor 1
```

## Remaining release work

This workflow begins with a seeded job and assignment. Facility-condition duties,
automatic report goals and creation/dispatch of new tasks from received dirt/fault
observations remain pending. Cleanup does not yet fabricate an unavailable tool.
General authority capacity/project planning, credential recovery, QA/audit/reprimand,
detention/custody, full needs/daily life, population benchmarks and the complete release
gate audit remain unfinished. Negative patrol follow-up and generalized patrol expectation
tracking remain pending. This report does not claim completion of H2, H3 or the first release.
