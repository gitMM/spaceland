# H2 physical work progress

Historical executor increment. The subsequent [autonomous repair report](h2-repair-progress.md)
records the current planning implementation, validation and outstanding work.

The independent system now has physical fabrication, installation, equipment
inspection/testing, atomic resource claims and recipe accounting. H2 remains
incomplete: autonomous repair planning and job/attempt lifecycle are not yet connected.
H3 and H4 remain outstanding. The full first-release goal is still active.

## Behavior

`ProduceRecipe` validates service capability, role, tool, capacity, staff and carried
materials before committing all claims. It consumes exactly the declared inputs at
completion, including partial stacks, and emits declared outputs at the workstation's
service output. Pickup remains a finite ordinary action. Cancellation or completion
failure releases claims and retains inputs. Overflow is detected before consumption.

`Install` requires a held single cartridge of the correct physical fit, tool and
authorization. It deliberately allows a fitting unsuitable cartridge. The machine
stays non-operational; replacing the part moves the old one onto the local tile.
`FunctionalTest` observes actual operation. `InspectMachine` reveals the required
and installed type without revealing the installer. Passive sensing reveals status.
An ordinary false completion report changes authority belief without repairing the
machine or consuming any material.

Items, machine positions, workstation capacity, tools and staff are claimed atomically.
Earlier claims retain their resources. Same-tick claim requests use one seeded
Fisher-Yates permutation within each consecutive command group; unrelated non-claim
commands retain their positions. Work contention returns `ResourceClaimed`,
`ServiceBusy` or another inspectable reason. Terminal queues retain FIFO behavior.
Work claims release on completion, failure or cancellation, with no item movement on
release. Claims have no lease expiry yet. Moving actors remain unreserved.

Each production transaction records its recipe, action, actor, tick and signed resource
delta. Accounting checks initial balances plus declared transformations. Binary state
version 3 includes work definitions, active bindings, claims, transactions, disabled
services and counters. Older development state versions are rejected; JSON artifact
envelopes remain version 1. Restore validates active claim ownership/capacity and
transaction groups against their recipe.

## Verification on 2026-10-28

Release build/test: **79 passed, 0 failed, 0 skipped**, with warnings treated as errors.
Command, from this solution directory:

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h2-work.trx' --results-directory artifacts/test-results --verbosity minimal
```

VSTest needed local process/socket permission in this sandbox. Tests include partial
stack consumption, real output pickup, wrong-fit rejection, fitting wrong-part
installation/replacement, missing inputs/tools, cancellation, service failure at
completion, finite capacity, staff coordination, inspection-only facts, false reports,
remote-claim privacy and overflow atomicity. Twenty seeds each resume at five
fabrication/pickup/install/inspection boundaries with byte-identical roundtrips and
matching final digests. Work scenarios also match isolated results at concurrency
2, 4 and 8 for 24 seeds, with tracing on/off.

Controlled RNG draws cover all six permutations of three contenders. The 64-actor
item race ran for seeds 0–999: one winner per seed, 63 inspectable losers, exact
replay and each contender present once in every request ordering. All actors won
at least once; counts range 9–26. Pearson chi-square = **57.152**, df = **63**,
p = **0.6837201776**. This diagnostic shows no bias signal in the sample; statistical
cutoffs are not correctness gates. Output: `artifacts/claim-fairness/result.json`.
This covers resource competition, not the still-missing two-job assignment part of T09.

Manual CLI validation/run/replay/inspection passed for `scenarios/work_actions.json`:

```powershell
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/work_actions.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/work_actions.json --out artifacts/work-actions --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/work-actions/replay.json --out artifacts/work-actions-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/work-actions/checkpoint.json --view resources
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/work-actions/checkpoint.json --view machines
```

The fixture fabricates at tick 5, picks up by 6, arrives at the machine at 8, installs
a wrong fitting part by 11, inspects by 13, installs the correct part by 16 and tests
by 18. Seven actions complete with zero failures and no remaining work claims. Final
physical resources: one remaining metal, the tool, the displaced wrong part on the
tile, and the correct part in the slot. Three recipe transactions reconcile the totals.

Original/replay digest:
`047f266231cc4a93f67b66c771e80756b2db9e75109afbf07b982285f665bea5`.
Build ID: `5f76dbfa5d126808aabaecab46d3bb7a9728f5c044b922c2faabe3b769d78982`.
Content ID: `d5f5a335159b76b65d2b6cc5e5f8bb5dc8e912c93cbfa5846a0ca8764d88b571`.
Host: Windows 10.0.26300, .NET 8.0.15, SDK 9.0.311, Release.
Generated evidence lives in `artifacts/work-actions/`, `artifacts/work-actions-replay/`
and `artifacts/test-results/h2-work.trx` and is ignored by version control.

## Remaining scope

- Connect these shared actions to citizen GOAP with bounded known candidates,
  alternative existing-part/fabrication routes, discovery replanning and meaningful
  no-plan results (T10/T23). The current work fixture is explicitly scripted.
- Add task/attempt/expectation/incident records and ordinary order acceptance,
  suspension, cancellation and reassignment policy. The resource layer alone does
  not establish job lifecycle or the full T09 gate.
- Complete resource operations: split/merge actions, project stashes, other declared
  sources/sinks, inventory indexing and remaining service contracts.
- Add records/query protocols, periodic check-ins and reply/session-release policy.
- Implement H3 authority/construction/recovery/discipline/escorts/rosters, then H4
  needs/daily life/population/tuning and the complete release acceptance matrix.
- Extend the viewer's work presentation and perform visual QA when supported.

No Unity Editor or existing DeathStar implementation was changed for this increment.
