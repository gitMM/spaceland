# H3 authority report expectations: verified increment

The independent headless system now creates report expectations from received work
acceptance, estimates deadlines from published evidence, applies known off-duty gaps,
learns from received results and retains overdue incidents separately from physical
task outcomes. The complete first-release goal remains active.

## Requirement coverage

| Requirement | Implemented behavior |
| --- | --- |
| R-AUT-10 | Expectation begins at authenticated OrderAccepted receipt, never dispatch or private read. |
| R-AUT-11 | Acknowledgment-terminal origin, nominal Manhattan travel, published installation duration, nearest permitted reporting terminal, login/post/latency, checked ceil and clamped slack. |
| R-AUT-12 | Frozen published roster version; one known off-duty gap plus check-in window when the base deadline exceeds known shift end. |
| R-AUT-13 | Checked integer EMA by registered role/task kind, initialized at 1000; one sample per attempt's first qualifying response after acceptance. |
| R-AUT-14 | One persistent MissingWorkReport incident per overdue attempt, meaning status unknown. Response dispatch is still outstanding. |
| R-AUT-16 | Same-attempt WorkCompleted, WorkBlocked or UnableToContinue satisfies responsiveness. Repeats/unrelated messages do not train again. |
| R-AUT-17 | Stable nearest-terminal tie order and explicit ordinal role/task ordering; no hidden physical state in estimation. This does not implement general dispatch/placement. |
| R-AUT-18 | Separate unacknowledged-order watch with off-duty extension. Automatic repost/reassignment policy remains outstanding. |
| R-CLK-08 / R-SNP | Phase-4 receipt beats phase-5 deadline; schema 10 retains evidence, learning, incidents and watchdogs across continuation. |

The repair contract currently estimates installation work, without enumerating live
prerequisite locations or queues. Missing published job/terminal evidence produces
Unestimated and no fabricated deadline. The shift comparison uses the last known
shift start preceding receipt, adds one complete off-duty gap, and does not repeatedly
extend multi-day deadlines. Later roster revisions affect new expectations only.

Results before acceptance remain ordinary recorded claims, without retroactive samples.
Cancellation stops pending watchdogs but retains history; a later first result can train
one sample while the expectation remains Cancelled. A response to an overdue incident
updates its response evidence without closing the incident or proving task completion.

## Verification

Windows, SDK 9.0.311, .NET 8.0.15, Release. Build: zero warnings/errors. Full suite:
**198 passed, zero failed/skipped**, using approved local VSTest IPC. The saved result
is `artifacts/test-results/h3-expectations.trx`.

Tests cover exact deadline ordering for all three result types, unrelated/foreign
reports, repeated results, independent attempts and exact EMA arithmetic, immutable
existing deadlines, known off-duty extensions, roster changes, cancellation, late
acceptance, missing evidence, hidden machine/route differences, getter purity and
overflow diagnostics. Twenty generated timing cases each continue from seven checkpoints
for **140 continuations**, with matching bytes, digests and remaining traces. Chunked
advancement, tracing disabled, and independent runs at concurrency 2/4/8 agree.

Both canonical scenarios pass strict validation, every assertion and standalone CLI
replay with identical final digests. All four new inspection views were exercised.

| Scenario | Observed behavior |
| --- | --- |
| authority_reports | Autonomous worker, no scripted actions. Ack receipt tick 5; estimate 8; deadline 18; result receipt 16; learned ratio 1187. Eleven actions completed, zero failed, one physical repair and no missing report. |
| late_report | Explicitly scripted misleading report. Ack receipt 5; estimate 9; base deadline 14 plus 21 off-duty ticks gives 35. One incident opens at 35; result arrives at 40; learned ratio 2444. Authority belief says operational, but the machine remains faulty and the physical job Assigned. Four actions completed, zero failed. |

| Identity | Value |
| --- | --- |
| Binary schema | 10 (JSON envelope 1) |
| Build | `f0a40b85f21b552196ce69637ca11803b260ba5a0fae6ed4007cbb73214aa0fc` |
| Content | `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38` |
| Autonomous digest | `5380fa249bbcc63a59c80579dcffda518d141aaee2e08f753dc2816e1b4f3c50` |
| Late-report digest | `4abe364e39cc44118c90eec8db2f82ab3597efe2468741da222eb2944aef9ce9` |

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h3-expectations.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/authority_reports.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/authority_reports.json --out artifacts/authority_reports --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/authority_reports/replay.json --out artifacts/authority_reports-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/late_report.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/late_report.json --out artifacts/late_report --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/late_report/replay.json --out artifacts/late_report-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/late_report/checkpoint.json --view expectations
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/late_report/checkpoint.json --view incidents
```

## Remaining release work

R-AUT-14 patrol/replacement response goals and R-AUT-15 bucket admission, dispatch caps,
replacement depth and damping are not implemented. Incident opening is not response
dispatch. A controlled learning-improvement workload and population scaling remain
unverified; exact EMA arithmetic alone is not calibration evidence. Authority view
construction still projects registered personnel; population indexing is later work.

Authority project/capacity planning, construction, staffing/template assignment,
credential recovery, discipline/patrol/escort/detention, full needs and routine windows,
task generators, benchmark/tuning/sweep operations and integration remain outstanding.
The complete H2/H3 gates are not claimed. Historical reports retain their original
build/schema evidence; old development checkpoints require their original builds.
