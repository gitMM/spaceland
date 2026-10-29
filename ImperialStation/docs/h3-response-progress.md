# H3 bounded replacement response: verified increment

The independent headless system now dispatches repair replacements for overdue report
incidents, using known personnel, fixed-hour admission budgets and a persistent FIFO
queue. Posted replacement orders pass through the existing physical terminal and work
pipeline. The original worker's status remains a separate PendingPatrol goal; patrol
search and welfare resolution are not implemented by this increment.

## Requirement coverage

| Requirement | Implemented evidence |
| --- | --- |
| R-AUT-09/17 | Registered role, published on-duty roster and known commitments determine availability. Nominal travel from received terminal/assigned-rest evidence ranks candidates; actor ID breaks ties. Hidden routes, locations, actions and inventory are not consulted. |
| R-AUT-14 | EnsureTaskCompleted dispatches a new attempt excluding the overdue assignee. The child ID persists, preventing duplicate dispatch. EstablishWorkerStatus is separately pending; the replacement can proceed without patrol. |
| R-AUT-15 | Fixed millisecond-based hour buckets, frozen known headcounts, floor/minimum budgets, one token on first posted response, no token for ineligible incidents, FIFO deferral, retained queue age, explicit cancellation and depth limit. Shared-token behavior with an actual patrol child remains unverified until patrol exists. |
| R-WF-21/26 | Replacement order and supersession cancellation use delayed mail. Dispatch does not grant private awareness or stop distant physical work. |
| R-WF-24/25 | Late original claims remain attached to the original attempt. Replacement result evidence and worker-status evidence are independent. |
| R-SNP | Schema 11 persists bucket counts/usage, token identities, queue state, child ancestry, causal opening ID and claimed replacement outcomes. Restore validates links, usage and historical roster headcounts. |

Eligibility requires a known on-duty roster and location estimate. An unexpired
pending/active assignment is a commitment unless a result or rejection has been
received. The estimate uses the most recently received authenticated terminal metadata,
falling back to the known assigned rest tile. Both can be stale; neither is a tracker.

Budget openings use `floor(tick * tickDurationMs / 3600000)`, and the first representable
tick in each new bucket. Mid-bucket roster changes can wake eligibility but cannot
rewrite the opening headcount or budget. The default cap is 50 permille, floored with
a minimum of one. Unknown cohort -1 has count zero and budget one. A responder without
a known roster is still ineligible. Default maximum replacement depth is two; root
incidents are depth zero. Each posted replacement has an explicit 1,728,000-tick lease.

The queue wakes on received messages, roster publication, known duty boundaries,
commitment expiry and new buckets. Ineligible entries retain their age and record a
reason. Eligible entries beyond budget become EscalationDeferred. Committed replacements
are never posted again by queue reevaluation. A response result records ReportedComplete,
ReportedBlocked or ReportedUnableToContinue without physical verification or welfare
resolution. No incident is silently closed while PendingPatrol remains.

## Verification performed

Windows, SDK 9.0.311, .NET 8.0.15, Release. Build: zero warnings/errors. Full suite:
**207 passed, zero failed/skipped**, with the approved local VSTest IPC exception.
Evidence is saved in `artifacts/test-results/h3-response.trx`.

Exact tests cover FIFO admission, exhausted budgets, no eligible responder/no token,
roster publication and frozen headcounts, floor rounding at 41 known workers, independent
pending patrol state, skipped older ineligible incidents and explicit cancellation,
two replacement levels, late original claims, nonintegral hour boundaries, hidden
machine/route changes and pure getters. A discovered restore bug incorrectly rejected
valid same-tick admission after incident opening; its regression now passes.

Twenty generated worlds vary seed and tick duration. Eight checkpoints each give
**160 continuations**, preserving canonical bytes, final digests and remaining traces.
Tick-by-tick advancement with getters and tracing disabled matches single advancement.
Independent worlds agree at concurrency 2/4/8. Nine more continuations cover the
autonomous canonical replacement, from incident opening through its final report.

`replacement_response.json` scripts only the original worker's acknowledgment trigger.
The replacement has no scripted commands. The incident opens at tick 24; replacement
attempt 2 posts at 25 and is learned through a later terminal visit. Its acceptance
arrives at 42, physical repair completes at 47, and the result arrives at 53, ahead of
deadline 61. The replacement order's causal parent is the incident opening event.

The 80-tick run completes **23 actions with zero failures**, seven check-ins, one real
repair, one admitted incident and one replacement result. WorkerStatusPending remains
one. All ten scenario assertions pass, strict validation succeeds, both inspection
views load, and standalone CLI replay reproduces the final digest.

| Identity | Value |
| --- | --- |
| Binary schema | 11 (JSON envelope 1) |
| Build | `037966789d59b22d982ac208376458be6f55c44734331b9ef0c1353b55812bac` |
| Content | `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38` |
| Digest | `2c6bb720041df6187220a3c753d1cee01f51b2b18ff7eb0ea1b6ebac4c77d24b` |

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h3-response.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/replacement_response.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/replacement_response.json --out artifacts/replacement_response --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/replacement_response/replay.json --out artifacts/replacement_response-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/replacement_response/checkpoint.json --view incidents
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/replacement_response/checkpoint.json --view admission
```

## Remaining release work

Patrol order transport/read/acknowledgment, local search, negative observations, status
reports and independent welfare resolution remain the immediate missing response branch.
The canonical missing-cleanup workflow also requires dirt/cleanup work, which is not
substituted by this repair fixture. Joint patrol/replacement admission tests and full
T19/T26 gates remain outstanding.

Authority project/capacity planning, construction, staffing/berths, credential recovery,
audit/discipline/escort/detention, full daily-life needs and routines, task generators,
population scaling/indexing, calibration/tuning, benchmark/sweep and integration remain
unfinished. Queue scans and evidence projections have not been population-benchmarked.
This milestone does not complete H2/H3 or the first release. Historical progress reports
retain their original build/schema evidence; old development snapshots need their original
builds. The complete first-release goal remains active.
