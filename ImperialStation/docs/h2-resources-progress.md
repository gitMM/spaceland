# H2 resource flows: verified increment

Implemented finite stack split/merge, physical project stashes, explicit source/sink
recipes and autonomous separation of stacked repair cartridges. These changes remain
inside the independent `ImperialStation` solution. This is not the full release or a
claim that operational projects, authority institutions or daily life are finished.

## Behavior and requirement coverage

| Requirement | Implemented evidence |
| --- | --- |
| R-RES-01 | Stashes have real completed-tile locations; deposit requires a held item and presence; withdrawal uses ordinary pickup. Splits preserve location; merging requires the same container. |
| R-RES-02 | Exact split/merge ledger, source/sink recipe transactions and checked quantities reconcile physical totals. Cancellation creates/consumes nothing. |
| R-RES-03,05 | Physical changes occur only through the executor; stash project tags and administrative messages do not move items. |
| R-RES-09 | Two-stack claims acquired together; overlap contention and prior claims are tested with deterministic arbitration. |
| R-RES-12..14 | Stacked cartridges must be separated before installation. Declared sources/sinks retain the ordinary finite service/access/capacity/staffing rules. |
| R-ACT, R-SNP, R-OBS | View-only repair planning includes separation; attempt IDs and actual output bindings survive replanning. Snapshot schema 7 and typed stack traces include the new operations. |

A split retains the original ID on its remainder and creates a new monotonic ID for
the separated amount. Repair planning separates all but one unit, leaving one cartridge
on its bound ID. A merge retains `itemId` and retires `otherItemId`. Registered credential
cards cannot be split/merged. Project IDs on stashes are association tags; H3 still
owns operational project/construction records. No completed project is inferred from a tag.

## Verification performed

Windows, SDK 9.0.311, .NET 8.0.15, Release. Build: zero warnings/errors. Full suite:
**151 passed, zero failed/skipped**. VSTest used the approved local IPC exception.

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h2-stacks.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/resource_flow.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/resource_flow.json --out artifacts/resource_flow --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/resource_flow/replay.json --out artifacts/resource_flow-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/resource_flow/checkpoint.json --view stacks
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/stacked_order.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/stacked_order.json --out artifacts/stacked_order --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/stacked_order/replay.json --out artifacts/stacked_order-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/stacked_order/checkpoint.json --view stacks
```

Both scenarios and their replays completed with no assertion failures and identical
run/replay digests. `resource_flow` completes ten actions, three stack changes and
two recipe transactions: initial 10 metal + source 5 − sink 2 = final 13. It physically
hauls, deposits and retrieves the separated material. `stacked_order` has no scripted
commands or initial repair goal: the worker reads/accepts an order, collects four
cartridges from a stash, separates three at tick 8, installs one and reports completion.
It completes twelve actions, one job and one split with zero failed actions; three spare
cartridges remain physically carried.

| Identity | Value |
| --- | --- |
| Binary schema | 7 (JSON envelope 1) |
| Build | `1f9e688d07804b980cef97ec7e79ac5576f40c31e3ad69d297d02c880181996c` |
| Resource-flow content | `9fa664b2066d05caa71a1cc5651bdc71f05676db4f419dbaccec68488abacaf2` |
| Resource-flow digest | `0e328cec76cda1be8135a73e36f8c3e215a86a81615f41bb7492b66d96eca930` |
| Stacked-order content | `86af7d426c9cb03b454ab51cb0a4f8e5ef933e5ebb8bfcf640e8c8ddf91f1ee0` |
| Stacked-order digest | `7a7506bbbbede7afaac7f6dd6d191d6373f29268005a4ffb5705b5a1ef687d4b` |

Tests cover exact finite boundaries, cancellation, invalid quantities, location/resource
mismatches, protected credential IDs, overlapping merges over 64 seeds, pickup/split
contention, source/sink accounting and `Int64.MaxValue` stack preservation. Twenty
varied resource flows pass ten continuation checkpoints each and concurrency 2/4/8.
Twenty generated order cases retain attempt identity through actual split snapshots.
The expanded repair generator covers eight variants × twenty seeds × five checkpoints.

Remaining work includes a full H2 requirement/gate audit, further planning/work domains,
population-scale inventory indexing, H3 evidence-based authority/rosters/deadlines,
construction/institutions, and H4 needs/population/tuning. Nested recipe planning and
dedicated viewer work presentation remain outstanding. No population performance or
browser visual verification is claimed. Earlier progress reports retain their historical
build/schema evidence; old replay bundles need their original builds.
