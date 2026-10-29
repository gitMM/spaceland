# H3 generated construction verification

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

Verified 2026-10-29. This increment adds parameterized T11 coverage, runnable fixture
exports and a correction to rejected-order trace provenance. It does not complete the
first-release audit or the outstanding construction belief/follow-up behavior.

## Generator and property contracts

`scenarios/generators/ConstructionGenerator.cs` transforms the canonical autonomous
construction fixture. Parameters vary translations, all three layout axes, reflections,
line length, material and terminal placement, actor/account/item/tile/recipe/service IDs,
role and material names, traversal/work/terminal durations, latency, terminal capacity,
one-to-three qualified builders, and single versus multiple input stacks. Initial
builder positions and roster locations agree; materials are initially visible to them.
There are no scripted actors or actor commands.

Seven labeled variants each run 20 seeds:

- Expansion: one eight-berth habitat with exactly four material units.
- DelayedReport: positive latency of 100–200 ticks; no duplicate project before receipt.
- NeedInterruption: declared accelerated hunger while working, real cafeteria and food,
  critical interruption, relief and a full restarted construction action.
- NoStock: the authority's budget is wrong; the worker rejects through an ordinary report.
- NoQualifiedWorker: no known qualification, so no construction commitment.
- NoLegalSite: construction bounds contain no empty adjacent coordinate.
- VacantCapacity: 20 citizens, 24 usable berths, 20 assignments, four vacancies and the
  default 50-permille reserve. The target is 21 and no expansion begins.

Positive and negative outcomes have different assertions and response-chain transitions.
Input validation checks preservation of declared material, latency and autonomy
prerequisites; the generator does not simulate outcomes and discard failing cases.
Needs stay enabled. Normal variants retain default need rules. The interruption variant
declares hunger starting at 600 points and a working rate of 100,000 points/hour; its
150–169-tick recipe permits one full restart after relief. These are explicit stress
overrides, not the default D-35 duration. The canonical fixture continues to verify
the default four-material, 18,000-tick, eight-berth recipe.

Comparisons across transformed inputs require the capacity, conservation and event-order
properties, not identical timestamps or digests. Repeated execution with identical inputs
requires exact state/trace equality. Tests check checkpoints before/after physical
completion and report receipt, initial/intermediate boundaries, and interrupted work.
Twenty further seeds match under concurrency 2/4/8, tracing on/off, incremental advancement
and frequent read-only inspections. A separate exported fixture is loaded and executed
from its relative catalog path.

The generated NoStock cases exposed a trace gap: WorkOrderEvaluated omitted its order
message and attempt, preventing the causal audit from following the rejection path.
The event now carries both identifiers for all work kinds. No physical or policy behavior
was changed by that fix.

## Reproduction

From the standalone ImperialStation directory:

```text
dotnet run --project src/Station.Runner -c Release --no-build -- generate-construction scenarios/autonomous_construction.json --variant NeedInterruption --seed 7 --out artifacts/generated-construction/NeedInterruption-7
dotnet run --project src/Station.Runner -c Release --no-build -- run artifacts/generated-construction/NeedInterruption-7/scenario.json --trace detailed --out artifacts/construction-interrupted-7
dotnet run --project src/Station.Runner -c Release --no-build -- replay artifacts/construction-interrupted-7/replay.json --trace detailed --out artifacts/construction-interrupted-7-replay
```

The export includes `scenario.json`, `catalog.json` and `manifest.json`. The manifest
records the seed, variant, transformed inputs, prerequisites, expected property,
comparison normalization and content digests. Other variant names are listed above.
The generator lives in the requested scenarios/generators directory and is linked into
Runner; it is not part of Core or Mind.

## Results

Release build: zero warnings/errors. Full suite: **769 passed, 0 failed** in 34 seconds.
Results: `artifacts/test-results/h3-construction-generated.trx`. The nine added test cases
exercise 140 parameterized cases, 20 additional seeds and one export round trip.

In exported NeedInterruption seed 7, builder 151 starts at tick 39, is interrupted before
completion, eats at 158, restarts at 207 and completes at 371 after the full 164-tick
recipe. A second meal completes at 381 before the return report. The authority receives
completion at 403. Construction material remains intact through interruption and is
consumed exactly once. Actual capacity rises to 16 at physical completion; belief stays
at 8 and pending capacity at 8 until receipt, then becomes 16/0. The response chain checks
75 actions and six required transitions without external response commands.

Run and replay match:

- Build: `9f53fe3bbfdbcb34c021d062e32d4f176f5012d61e2981f21af6cb096f01e409`
- Content: `53e759576ae68a23f39dde47f66fb3becb9ed6b533fc11368bc6f875ab19f93b`
- State: `b36bfb969e95982b9ea33fba58abf96c53387421c95b84f9df9dee95c8a60821`
- Trace SHA-256: `A17A4C4713D08AC4BE809D18D9B74E595E33495C9802C49B4D126E3120F8E847`
- Response proof SHA-256: `9F97FB3ABF45A6A624619012F7F4A5F5AE2BFEF3DCE564A3A4ABD796C4689BBC`

## Remaining release work

Construction still needs later contradictory evidence to update its believed capacity
record, explicit follow-up for blocked/rejected work, and communication of newly learned
layout to other workers. Initial capacity is currently an explicit aggregate belief;
the complete identified berth/capacity-record audit remains. Generation here does not
prove all contention, replacement, cancellation, multi-day or population conditions.
Other work-domain, priority, institutional, performance, viewer and full requirement
gates remain active. Shutdown remains pending until the entire first release is
implemented and verified.
