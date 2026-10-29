# Integrated station and population baseline

2026-10-29. This work implements the stabilization/integration/scale sequence in
the build specification. It does not claim full first-release completion. See
`acceptance-current.md` for current requirement and gate status.

## Workloads and acceptance

`scenarios/generators/DailyStationGenerator.cs` constructs initial state only;
`Station.Runner/StationBaseline.cs` runs the ordinary World and collects evidence.
There are no scripted actors or action commands. Citizen responses are selected
by the existing policies and bounded planner. Exported scenario/catalog files are
ordinary loader inputs; `run`, `inspect` and checkpoints remain applicable.

The daily workload has 12 citizens, three staggered cohorts and 72,000 ticks,
covering two configured days. The population workload has 2,048 citizens and
36,000 ticks, including cohort boundaries at 12,000 and 24,000. Every citizen is
a registered cleaner with an initial ordinary work order. Each district has up to
16 residents, three connected tiles, a two-slot terminal, eight cafeteria seats,
four toilets, eight rest places and one assigned bed per person. Districts connect
into one graph. All 12 tile families appear in the population workload.

The declared configured day is **36,000 ticks at 100 ms = one real simulated hour**.
It represents 24 routine hours: default physiological rates are multiplied by 24;
meals/toilets/rest/sleep/conversation durations are divided by 24 (375/75/250/12,000/75
ticks). Thresholds and relief amounts remain unchanged. Terminal operations retain
their default durations; edges take 10 ticks; cleaning takes 300 ticks/unit.
Check-ins recur every 1,500 ticks with a 750-tick shift-start window. Social pressure
starts at 400; other needs start at zero. This is an explicitly shortened-day
integration fixture, not a claim about default 24-hour pacing.

Each district starts with 10,000 dirt units and deterministic ongoing soiling,
providing a sustained ordinary-work backlog. There is food for 20 meals per citizen.
Cleaning makes real progress but the backlog intentionally does not complete in
the measurement window. Zero completed jobs therefore does not mean zero work.
This baseline does not yet include the required multiple recovery/overdue/audit/
escort instances at population scale; **full T15 remains partial**.

The daily check requires every citizen to eat and spend positive time sleeping
on each day, work on each day, and also rest, use a toilet and converse. All due
scheduled rests must complete, even with the work backlog. Initial-condition
response roles audit every actor's action provenance. Population participation
is checked separately: the one-day benchmark does not apply the two-day
conversation-completion gate. Every external action in either workload is a failure.

## Verification

The original increment stabilized with 785/785 passing tests. New integration tests
cover three seeds (42, 7, 91), physical food shortage, recurring daily participation,
mandatory rest, snapshots mid-sleep and mid-conversation, and trace-retention purity.
A further integrated variant loses a card during credential-required cleaning,
performs finite HQ replacement from blank stock, and resumes cleaning while the
other citizens continue their routines. Its action origins are audited as well.

The daily run has 12/12 active workers, 80 consumed meals, 24 completed scheduled
rests, zero overdue scheduled rests and 12 passing causal roles. It also reports
16 missed meal/sleep windows and 12 critical interruptions; these are visible
metrics, not silently treated as completed activities. Every actor nevertheless
meets the per-day meal/sleep and over-horizon participation checks. Eight goals
remain pending at the horizon. No food/service rejection occurs in the supplied run.

Final regression: **794 passed, zero failed, zero skipped**, including nine new
integration cases. The Release build also passes with zero warnings and errors.
Saved evidence is under `artifacts/integration-tests/`,
`artifacts/baseline-daily-verified/` and `artifacts/baseline-population-verified/`.
The exported daily scenario was run and replayed through the ordinary CLI; both
final state digests and SHA-256 hashes of the full detailed traces match exactly.
Those artifacts are in `artifacts/daily-integration-replay-source/` and
`artifacts/daily-integration-replayed/`.

| Final verified measurement | Daily station | Population baseline |
| --- | ---: | ---: |
| Citizens / ticks | 12 / 72,000 | 2,048 / 36,000 |
| Execution seconds | 0.332 | 106.995 |
| Initialization seconds | 0.004 | 0.613 |
| Events | 45,485 | 4,629,038 |
| Active workers | 12 | 2,048 |
| Meals consumed | 80 | 4,924 |
| Completed / overdue scheduled rests | 24 / 0 | 2,048 / 0 |
| Maximum terminal queue | 5 | 12 |
| Maximum need wait, ticks | 1 | 71 |
| Allocated bytes during execution | 120,714,496 | 39,579,849,304 |
| Process peak working set, bytes | 89,186,304 | 346,038,272 |
| Missed routine windows / critical interruptions | 16 / 12 | 2,199 / 1,219 |
| Pending daily goals at horizon | 8 | 1,902 |
| External actions / service rejections | 0 / 0 | 0 / 0 |

Both runs use build fingerprint
`7b7f9833673ad42f70d339c3955f9a0f99e55e5c879f1daa3017c13f83aa7263`.
The population final state digest is
`3ebad755a30bedf660e9fe63e190b2857ec549d98aa8f16f48b37c6724edfa25`,
unchanged from the earlier completed indexed runs. The population workload passes
its declared participation checks, but the missed windows, interruptions and
allocation volume remain tuning targets. Pending goals include work scheduled
beyond the cutoff; they are not all overdue. Seventy-two citizens did not complete
a conversation in the one-day population window; the stronger conversation check
belongs to the two-day daily fixture and passes there.

## Performance work and measurement limits

The unoptimized 2,048-citizen attempt was interrupted after reaching tick 6,000;
there is no completed unoptimized population timing to compare against.
At 128 citizens the original measured run took 3.406 s and allocated 1.061 GB.
Reusing known-navigation search frontiers within a planning request took 3.199 s
and allocated 0.858 GB. Adding actor-specific timer and daily-goal indexes took
2.933 s and allocated 0.867 GB. All three ended with identical digest
`e34d07f92b8fcc6cb43235e5ee6d010e1568e77857ce81fa7c7f927795245c62`.
These are single diagnostic samples, not statistically controlled speed promises;
some runs overlapped with other verification work.

No lookup cache sees physical truth: route reuse is confined to one immutable
actor view and one planning request. Derived indexes preserve the original ordered
queue and goal iteration. Restore rebuilds them from canonical state. Regression
and integrated continuation tests cover those changes. The benchmark drains
diagnostic traces each 1,000 ticks without resetting event IDs; the small daily
causal audit retains its proof separately. Population tracing is bounded by that
interval, while historical simulation records remain ordinary persisted state.

Execution timing includes advancement, accounting checks, diagnostic sampling and
trace collection, but excludes scenario construction, final serialization and causal
audit. Initialization time measures World creation separately; the loader also
validates by creating a world. Allocation is thread allocation over the measured
execution block. Peak working set is process-wide, not retained simulation size.
Runtime: .NET 8.0.15, Release, Windows build 26300, 32 logical processors,
AMD64 Family 25 Model 97 Stepping 2. Precise CPU marketing name was unavailable
through sandbox CIM. Each world runs on one thread; no Unity Editor is involved.

## Reproduce

From `ImperialStation/`:

```powershell
dotnet test ImperialStation.sln --no-restore -c Release -m:1 -p:UseSharedCompilation=false
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark daily --out artifacts/baseline-daily
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark population --out artifacts/baseline-population
```

For concurrent implementation, copy the Release runner directory to a dedicated
artifact directory before launching a long run. A running Windows .NET process
locks its loaded DLLs; one attempted build hit that lock, then passed after the
benchmark was stopped and isolated. It was a build-environment failure, not a test pass.

Remaining work includes full T15 institutional coverage, full T22 custody/roster/
staffing variants, T26 calibration and backlog recovery, complete generators and
holdouts, Unity integration, tuning sweeps and viewer verification. The current
acceptance matrix keeps these open.
