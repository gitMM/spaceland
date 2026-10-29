# H3 patrol and independent incident branches: verified increment

The independent headless simulation now dispatches patrol orders, delivers them through
ordinary terminal reads, posts acceptance, searches permitted candidate locations with
finite local inspections, and reports findings through terminals. Patrol and repair
replacement proceed independently and retain one shared admission token. This is a
verified response increment; the complete first-release goal remains active.

## Requirement coverage

| Requirement | Implemented behavior and evidence |
| --- | --- |
| R-AUT-09/17 | Guard selection runs in Mind over registered role, known duty/commitments and estimated coordinates, with stable actor-ID ties. No live worker location enters selection or search candidates. |
| R-AUT-14 | EstablishWorkerStatus posts a persistent patrol child alongside EnsureTaskCompleted. Either can proceed when the other role is unavailable. Later availability does not duplicate either order. |
| R-AUT-15 | Both children use the first response's admission token. Tests cover patrol-first and replacement-first, with the other branch becoming available later under the same exhausted budget. |
| R-WF-21 | Posted/delivered/read/accepted states remain distinct. The guard starts searching only after physically posting acceptance. |
| R-WF-22 | Candidate coordinates derive from permitted terminal, published job-site and assigned-rest evidence. Each candidate gets ordinary movement and finite local InspectActor; negative observations disclose only absence at that tile. Findings reach the authority only through terminal reports. |
| R-WF-24/25 | Original, replacement and patrol results retain separate identities. Worker-status evidence and work evidence remain separate; one cannot complete the other. |
| R-LIF / R-ACT | Finite inspection may finish after shift end. Search/observations persist, and new search steps wait for known duty. Known expiry prevents new search and stops movement at a tile boundary. |
| R-SNP / R-OBS | Schema 12 retains payloads, private read/search state, observations, tagged actions, expiry and shared tokens. Causal links connect incident, order, read, search and result. |

The current patrol role defaults to `guard`. Payloads allow at most eight distinct
locations; policy currently proposes at most three. Actor.Location is inspection-only
by default. Optional passive content rules observe only co-located actors, and the
default avoids scanning the population for passive actor facts.

InspectActor has a typed target identity and the configured finite inspection duration.
Presence is evaluated at completion; a moving actor is absent from the interaction tile.
Failed/cancelled inspection teaches nothing. Search records the bounded observations
locally, then posts PatrolFound, PatrolNotLocated or PatrolBlocked. These are claims;
the authority validates sender/order and shape without checking the world. The first
result is retained; repeats and wrong accounts cannot replace it.

ReportedNotLocated preserves the last negative-at-location evidence and does not claim
global absence, death or misconduct. ReportedBlocked and negative responses leave worker
status unresolved. Both ReportedFound and ReportedComplete are required for the derived
incidentsResolvedByReports metric; historical records remain available.

## Verification performed

Windows, SDK 9.0.311, .NET 8.0.15, Release. Build: zero warnings/errors. Full suite:
**221 passed, zero failed/skipped**, with approved local VSTest IPC. Saved evidence:
`artifacts/test-results/h3-patrol.trx`.

Exact tests cover local/remote/transit presence at completion, cancelled inspection,
explicit passive perception, delivered-but-unread orders, posted acceptance before
search, delayed authority evidence, independent work/status results, either branch
becoming available later, one shared token, stale/moving targets, failed/cancelled reads,
credential/terminal loss, expiry before reading and during search, duplicate/foreign
reports, and inspection spanning shift end. A real simultaneous branch-result record
collision was found and fixed by including each branch's result identity in record keys.

Twenty generated worlds vary seed, inspection duration and delivery latency. Twelve
checkpoints each give **240 continuations**, with byte-identical round trips, matching
final digests and remaining traces. Tick-by-tick runs with getters and tracing disabled
agree; independent worlds agree at concurrency 2/4/8. Causal trace assertions connect
the final patrol result to an order read, local inspection and incident opening.

## Canonical autonomous response

`patrol_response.json` scripts only the original worker's acknowledgment and move as
the triggering condition. Neither the replacement nor guard has scripted commands.
The worker leaves its reporting terminal for C. Candidate A is stale; B is the known
job site; C is the assigned rest tile. The guard discovers the location locally.

| Tick | Observed transition |
| --- | --- |
| 24 | Original work-result expectation becomes overdue; one incident opens. |
| 25 | Replacement attempt 2 and patrol order 5 post; bucket 0/cohort 0 spends one token. |
| 38 | Replacement and guard each complete their own finite order read. |
| 40 | Both physically post acceptance. |
| 42 | Guard finishes inspecting A: AbsentHere. |
| 45 | Guard finishes inspecting B: AbsentHere. |
| 47 | Replacement physically completes the repair. |
| 48 | Guard finishes inspecting C: FoundHere. Authority still lacks this observation. |
| 53 | Replacement result is received; worker status remains PatrolPosted. |
| 54 | Patrol result is received; worker status becomes ReportedFound. |

The 110-tick run has **55 completed actions, zero failed actions**, 19 check-ins, three
patrol inspections, one real repair, one patrol result and one admission token. All
twelve scenario assertions pass. Strict CLI loading, institutional/private inspection
views and standalone replay succeed; replay has the same final digest.

| Identity | Value |
| --- | --- |
| Binary schema | 12 (JSON envelope 1) |
| Build | `bd7b623454b59dcfc8844235912f17045b8924d47a59ef20733410b25ee83015` |
| Content | `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38` |
| Digest | `121ed0a00be80231102ad3dde2dcd11e1cca6fa4de02c282e495530b980bd1d8` |

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h3-patrol.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/patrol_response.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/patrol_response.json --out artifacts/patrol_response --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/patrol_response/replay.json --out artifacts/patrol_response-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/patrol_response/checkpoint.json --view patrols
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/patrol_response/checkpoint.json --view patrol-local --actor 3
```

## Remaining release work

This repair-triggered fixture does not replace the specified missing-cleanup workflow:
dirt generation, finite cleanup, arrival/completion rechecks and the full T19 gate remain
unfinished. Patrol currently uses a lease; generalized adaptive expectations for patrol,
explicit remote patrol cancellation/reassignment and automatic follow-up after negative
or blocked searches remain future policy. Full population damping/calibration gates are
also outstanding; these small deterministic worlds do not prove scale performance.

Authority capacity/project planning and construction, staffing/berths, credential recovery,
audit/discipline/escort/detention, complete needs/routine windows, other work domains,
task generators, population indexing/benchmarks, tuning/sweep and integration remain.
Complete H2/H3 gates and the first release are not claimed. Historical reports retain
their original schema/build evidence; older development snapshots need their original
builds.
