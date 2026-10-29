# H2 implementation progress

This report records the information/planning increment. The subsequent
[physical work report](h2-work-progress.md) has current validation and scope updates.

Status: H1 foundation implemented; H2 partially implemented; H3 and H4 outstanding.
The user's first-release goal remains active. The solution is independent of Unity
and DeathStar assets and lives entirely under `ImperialStation/`.

## Verified behavior

- A bounded uniform-cost GOAP planner performs real forward search. Known routes,
  terminal candidates, held credentials and session state determine report plans.
  Core executes each selected action and replans; there is no scripted response in
  `autonomous_report.json`.
- Citizens have bounded private knowledge with provenance, observation/learning
  times, deterministic recency and explicit pins. Published layout is separate from
  physical topology. Hidden changes and unread mail cannot change a plan.
- Local sensing and finite inspection teach facts, including absence at a location.
  Negative inspection does not reveal an item's actual remote location.
- Terminals have finite login/read/post, capacity, queues, seeded contention,
  credentials, sessions, expiry, cancellation and availability changes. Read captures
  eligible IDs at start; later arrivals remain unread. Checks at completion prevent
  interrupted or invalid operations from silently succeeding.
- Delivery, learned knowledge and authority belief are separate. Citizen reports may
  be false; provenance is genuine. Citizen messages cannot impersonate authority
  orders, cancellations or service replies. A held stolen card cannot use its owner's
  account. Authority beliefs retain contradictory claims without consulting truth.
- Snapshot version 2 includes all introduced state, scheduled events, queues, captured
  reads, planner budgets/retries/goals and knowledge pins. Diagnostics remain outside
  the digest. Focused traces connect planning, physical actions and message effects.

## Scenario evidence

`false_delivery.json` implements exact T07 timings: tile travel costs five; player at
A and inspector at C. Inspector login/read ends at tick 2, travels to B by 7, inspects
7–8, logs in 8–9 and posts 9–10. Player posts a false location 2–3; authority receives
it at 5. The correction arrives at 12. The physical component stays at A throughout.
The exact run completed nine actions, two reads, two posts and two belief updates.

`autonomous_report.json` starts with a pending report and no scripted commands. The
actor chooses between terminals using known costs, travels to A by 5, logs in by 6
and posts by 7; the authority receives it at 9. It completes three physical actions
using three planner requests and eight node expansions.

The T07 generator varies geometry, IDs, timings, latency, background staffing and
reporting terminal. Alternate terminal cases require actual travel after inspection.
Twenty seeds each receive five checkpoint-continuation checks.

## Validation on 2026-10-28

Release build: zero warnings and errors. Full suite: **60 passed, 0 failed, 0 skipped**.
Tests cover exact timings, generated movement and T07 fixtures, snapshot continuation,
private-view isolation, cancellation, terminal queues, credential/session validity,
bounded planning/backoff, candidate changes and concurrent world isolation. The
existing 1,000 generated movement worlds remain covered. The symbolic recipe-shaped
planner test is a search test, not an implementation of physical recipe work or T10.

Manual CLI validate/run/replay/inspect checks passed for both new scenarios. Original
and replay digests match:

| Scenario | SHA-256 final digest |
| --- | --- |
| false_delivery | `e27e3ed76bb8935c3efc5e3fa871abf388ade0f533928120ba8d9c0360f5b467` |
| autonomous_report | `dee8f96c6bc3752d6f47cc3e1a77b65ec08e562207db6b544bf6e9ec72b37ca1` |

Build ID: `c0be87229aaebd56602d43eacfabf552c2ba9e043827ac7de1a0266cec3e058c`.
Content ID: `53daf0232d08bffe5bd815973c127c9118ae6d77090bd5cd70410dcd4f6a9074`.
These identify this validation build; subsequent source edits require fresh artifacts.

A focused chain for autonomous belief event 23 includes plan event 16 at tick 6,
request/start 17/18, posted 19 at 7, delivered 22 and belief 23 at 9.

A CLI batch of 1,000 autonomous-report seeds (0–999), concurrency 8, trace disabled,
completed with zero failures. Seed 42 matches its detailed-run digest. Mean measured
simulation time was 0.6040912 ms and maximum 24.9702 ms. This is a one-actor, 20-tick
fixture, not a population benchmark; timing excludes build/loading/artifact writes.
Host: Windows 10.0.26300, .NET 8.0.15, SDK 9.0.311, 32 logical processors, Release.

Artifacts are generated and ignored: `artifacts/false-delivery/`,
`artifacts/autonomous-report/`, their replay directories,
`artifacts/autonomous-report-batch.jsonl` and `artifacts/test-results/h2-information.trx`.
VSTest required permission to use local testhost IPC because the sandbox blocks it.
The offline viewer passes `node --check tools/viewer/viewer.js`; visual QA remains
unverified because the browser tool rejected local file URLs. No alternative route
was attempted to bypass that policy.

## Scope still required

| Area | Current status | Remaining work |
| --- | --- | --- |
| H2 knowledge/communication | Partial, executable | Periodic check-ins, reply waiting/session release, order handling and full perception policy |
| H2 GOAP | Search and reporting domain | Work candidates, shared action preconditions/effects, broader task selection and T10 physical alternatives |
| H2 work/resources | Physical recipes/machines/claims added in subsequent increment | Jobs/attempts, remaining resource operations and autonomous work planning; see the work report |
| H2 records | Capability enum reserved | Record store, finite queries, scoped replies and access controls |
| H3 authority | Message-derived beliefs and travel estimator | Actual planning, construction, recovery, discipline, escort, rosters and credentials policy |
| H4 daily life | Not implemented | Needs, routines, population, tuning, benchmarks/sweeps and release acceptance suite |
| Viewer | Implemented and syntax checked | Visual verification and future system layers |

Next after the physical work increment: autonomous work planning and jobs/attempts,
then record queries and autonomous communication policy. The full first release must
pass the build spec's corresponding acceptance gates before it can be called complete.
