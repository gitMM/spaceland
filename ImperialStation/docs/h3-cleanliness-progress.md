# H3 cleanup physical foundation: verified increment

The independent simulation now implements local dirt, deterministic soiling, spills and
finite cleaning. This advances R-WF-01 and the physical invariants in R-WF-23. It does
not complete T19 or the first-release scope. The prior patrol/replacement system remains
implemented, but cleanup has not yet been connected to autonomous job orders/reports.

## Behavior

- Completed Cleanable tiles retain nonnegative integer dirt.
- The named soiling stream samples bounded intervals; spills add configured or explicit
  amounts without drawing randomness. Both execute before same-tick action completion.
- Cleaners must physically arrive and satisfy configured role/tool/staff access.
- Each finite action checks current dirt and removes at most one unit at completion.
  Concurrent or stale cleanup attempts produce NoWorkNeeded when no dirt remains.
- Cancellation releases claims without cleaning; tools are retained and never consumed.
- Dirt observations stay local. Remote knowledge and authority beliefs do not update.
- Schema 13 saves balances, configuration, timers, random state and in-flight actions.
  Restore validates balances and timer/claim ownership. Overflow leaves an inspectable
  failed world with no partially applied dirt addition.

## Verification

Release build succeeded with zero warnings/errors. The full test suite passes:
**232 passed, 0 failed, 0 skipped** (`artifacts/test-results/h3-cleanliness.trx`).
Eleven new test cases cover concurrency, timing, tool retention, cancellation, access,
local/private perception, invalid input, soiling bounds/stream isolation, malformed
snapshots, overflow and the Runner fixture.

The generated test varies 20 seeds/worlds with tool availability, dirt amounts and
soiling intervals. Ten checkpoints per world yield **200 byte-stable continuations**,
including mid-cleaning and disturbance boundaries. Remaining traces match. Per-tick
advancement, disabled tracing, repeated getters and concurrency 2/4/8 preserve digests.

`cleanup_physics.json` passes ten assertions: two cleaners start with one dirt unit,
both finish at tick 4, one removes it and the other finds no work. A further clean ends
without an effect at tick 8. A spill adds four at tick 9; the next cleaning removes one
at tick 13. The final balance is 1 initial + 4 added - 2 removed = 3. Four actions
complete with zero failures. The remote observer and authority never learn the dirt.

Strict replay completed with the same final digest. The dirt inspection view shows
the expected balance and no automatic timer for this controlled fixture.

| Evidence | Value |
| --- | --- |
| Build | `115ea0db541e46c1b5efac0a3c78e9b17bcf836bbe2ac3afe564601a43c8f14c` |
| Content | `577d7f41f0d63b04499379954dc44b8dd93149e1cfc270e270a545237bde6b08` |
| Final digest | `f6fe0bd3b3c214887f518cd5986af029ee2cf1317ab49d0b11bc964f4f8dbc77` |
| Original run | `artifacts/cleanup_physics/` |
| Strict replay | `artifacts/cleanup_physics-replay/` |

```powershell
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/cleanup_physics.json --out artifacts/cleanup_physics --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/cleanup_physics/replay.json --out artifacts/cleanup_physics-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/cleanup_physics/checkpoint.json --view dirt
```

## Remaining work

Next connect a typed cleanup target to public work descriptions, authority estimates,
qualification/dispatch, read-order acceptance, bounded local planning and terminal
results. Add facility-condition reporting/task creation and the actual missing-cleanup
scenario with slow original routing, independent replacement/patrol branches, late
original reports and offline cancellation. The current fixture is scripted physics,
not the canonical institutional workflow. Other release gaps remain: wider authority
planning, credential recovery, audit/custody, full needs, population tests and gate audit.
