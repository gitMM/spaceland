# H2 autonomous repair progress

The citizen planner now performs real repairs through the shared work executor. It
chooses between a known existing cartridge and fabrication, acquires known inputs,
inspects unknown specifications and replans after a local missing-item discovery.
The first-release goal remains active: H2 jobs/records/communication policy and the
H3/H4 systems are still incomplete.

## Implemented behavior

`RepairPlanning` grounds symbolic actions from `IActorView` and a shared immutable
published work catalog. Machine specifications and installed contents are excluded
from that catalog. Candidate bindings come from private facts, goal machine ID,
published service contracts and recipe definitions. Candidate counts, predicate
count, plan depth, search nodes and world request rate are bounded. Forward
uniform-cost search is the same engine used by report planning.

The domain includes known-location travel, ordinary pickup, fabrication, installation
and finite inspection. Travel is estimated separately for each pair of bound locations.
Predicted fabrication output is symbolic: production creates the actual item, the
actor observes it, and the next planning request binds its real ID. An invariant
rejects any attempt to execute an unbound predicted output.

One selected step executes before replanning. Repairs retain pending goals and
temporary knowledge pins across execution. Relevant learned facts trigger reevaluation;
budget exhaustion/no-plan use deterministic backoff. A failed pickup replaces its
location fact with local absence, permitting the next plan to use another known
route. Inspection and own-inventory failures do not distinguish nonexistent items
from unheld items elsewhere. No remote physical state is disclosed.

Report and repair requests share the existing world planning budget. Pending reports
have explicit priority; repair goals and carried materials remain retained. A completed
report wakes repair planning without an `ActorBusy` race. Duplicate initial repair
goals for the same actor/machine are deduplicated.

Binary state version **4** persists the published work catalog independently from
physical definitions, plus repair goals, retry timing, outcomes and pins. Earlier
development state versions are rejected. The JSON checkpoint envelope remains version 1.

## Acceptance evidence

Release suite on 2026-10-28: **96 passed, 0 failed, 0 skipped**; warnings are errors.

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h2-repair.trx' --results-directory artifacts/test-results --verbosity minimal
```

VSTest required local process/socket access in this sandbox. Tests prove:

- T10(a): a known existing part is physically collected and installed.
- T10(b): with no finished part, known materials are fabricated and the output is
  picked up and installed. Floor inputs add real pickup steps before production.
- T10(c): a stale known location produces a finite travel/pickup failure, local
  absence evidence and a successful fabrication replan; the unseen part stays remote.
- T10(d): no known prerequisites yields explained `NoPlan`, a future retry and no
  created resources.
- T23(b): in the test geometry, existing-part cost is `2*d + 3` ticks and fabrication
  costs 10 ticks. The planner selects existing at d=2/3 and fabrication at d=4/5.
  A more expensive recipe flips the choice back to collection.
- T23(c) and H2 information isolation: hidden item/machine differences or a traversal
  change outside local observation cannot change the initial plan. Actual travel
  duration still follows physical costs. Published/physical recipe duration divergence
  survives restoration and affects prediction separately from execution.
- Snapshot continuation: 20 direct seeds with five checkpoints each, plus 20 generated
  seeds for each of five variants with five checkpoints each. Restored states roundtrip
  byte-identically and match uninterrupted digests. Temporary repair pins release.
- Parallel isolation: 24 seeds match sequential digests/metrics at concurrency 2/4/8,
  including tracing on/off.
- Shared report/repair priority and request budget, retry times, and local inspection
  privacy are covered alongside the previous foundation/information/work suite.

The generator varies translation/reflection/axis, actor/machine/workstation IDs and
travel/work durations. Canonical and generated autonomous fixtures contain no scripted
commands. This increment does not claim job lifecycle, institutional or population gates.

## CLI evidence

Manual `validate`, `run`, `replay` and `inspect --view repairs` succeeded. Original and
replay final digests match:

| Scenario | Behavior | Final digest |
| --- | --- | --- |
| `alternative_planning` | Existing part; move completes 2, pickup 3, return 4, installation 7 | `b569cdd3afaa7159fc0d1d321868bf349dce966ff658709fca7b2b7812b3907c` |
| `planner_costs` | Fabrication completes 5, pickup 6, travel 7, installation 10 | `0b50913ab6e52e45ed7fac0eec4b027523d4b58fafe42649a55345bffcccac74` |

Both finish four physical actions and satisfy one repair goal, with zero failures or
remaining claims. Each makes five planning requests, including the final known-goal
satisfaction check. The existing-part case expands 60 nodes and produces no resource
transactions; the fabrication case expands 33 and records its three recipe deltas.

Build: `52bdf5f2650ab32b8296763cec2e863182a70e3499d9d7f6c0ee9f7243b5ca4c`.
Content: `d5f5a335159b76b65d2b6cc5e5f8bb5dc8e912c93cbfa5846a0ca8764d88b571`.

```powershell
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/alternative_planning.json --out artifacts/alternative-planning --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/alternative-planning/replay.json --out artifacts/alternative-planning-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/planner_costs.json --out artifacts/planner-costs --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/planner-costs/replay.json --out artifacts/planner-costs-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/alternative-planning/checkpoint.json --view repairs
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll batch scenarios/planner_costs.json --seed 0 --count 1000 --parallel 8 --trace none
```

The 1,000-seed CLI batch completed with zero failures; seed 42 matches the detailed
run. Mean measured simulation time was 8.2909292 ms, maximum 113.2302 ms. This is one
actor over 100 ticks, not a population benchmark, and excludes build/loading/artifact
writes. Host: Windows 10.0.26300, .NET 8.0.15, SDK 9.0.311, 32 logical processors,
Release. Generated outputs are ignored under `artifacts/`; batch output is
`artifacts/planner-costs-batch.jsonl` and the test log is `artifacts/test-results/h2-repair.trx`.

## Outstanding scope and limits

Next: task/attempt/expectation/incident records, normal dispatch/read/accept/acknowledge
flow, suspension/cancellation/reassignment and their physical-versus-belief distinction.
Current repair goals are initial policy goals, not a substitute for those records.
Goal satisfaction follows citizen knowledge; future job physical status must remain
separate from a citizen's believed completion and the authority's beliefs.

Current repair fabrication binds direct recipes producing one installable cartridge.
Nested recipe chains, arbitrary stack splitting/merging, project stashes and remaining
declared sources/sinks still need implementation. Other pending H2 work includes
record queries, periodic check-ins, reply/session-release policy and complete task
report generation. H3 authority/construction/recovery/discipline/escorts/rosters and
H4 daily life/population/tuning remain outstanding, as does complete release auditing.
The viewer still needs work/repair presentation and visual verification.

All implementation remains in the standalone `ImperialStation/` solution. No Unity
Editor or existing DeathStar simulation code was changed.
