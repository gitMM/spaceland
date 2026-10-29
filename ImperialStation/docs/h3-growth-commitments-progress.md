# H3 authority capacity commitments

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

Verified 2026-10-29. This increment implements bounded authority capacity planning
and administrative construction commitments. It does not complete T11 or the release.

## Behavior

The optional `initialState.growth` policy supplies an explicit initial completed-berth
belief, a known material budget, inclusive three-dimensional placement bounds, reserve
permille (default 50), and a candidate limit (default 3, maximum 5). Population comes
from registered personnel. The target is population plus the reserve rounded up.
The capacity record counts completed usable berths including vacancies; assignment
and occupancy do not change it. Fixtures seed this record explicitly, so tests can
also exercise false capacity information. It is not synchronized to live physics.

Candidate sites are empty coordinates adjacent to the published layout, filtered
by bounds and existing administrative reservations. Habitat blueprints need a known
registered qualified role. Candidates rank by declared recipe work cost, coordinates,
then ordinal definition ID. Physical occupancy, actor positions, inventory and actual
bed availability do not affect this decision. Output-ID allocation reads existing IDs
only to ensure uniqueness. An incorrect layout can therefore produce an invalid
reservation, which the physical construction action rejects on arrival.

The Mind domain uses the same uniform-cost bounded GOAP search as citizen planning.
Symbolic states encode feasible committed candidate subsets, including cumulative
material requirements and mutually exclusive sites. Search compares combined capacity
and costs, rather than hardcoding a construction sequence. With at most five candidates,
there are at most 32 subset predicates plus one goal predicate. The batch target is the
lesser of the outstanding deficit and the maximum affordable candidate capacity.
Only the first selected commitment executes before the next decision. Bounded batches
can advance larger goals without pretending the full target is satisfied.

Planning shares the existing per-tick request budget, node/depth bounds and metrics.
No-plan and exhausted searches use capped exponential backoff. Sufficient believed plus
pending capacity suspends further expansion until new evidence is supported. The
initial pending-capacity record is empty; growth currently rejects separately seeded
construction projects rather than silently double-counting them.

A commitment creates an identified reserved project, reserves its recipe amounts in
the administrative budget, and records expected berth capacity. It creates no physical
tile, bed, item claim, movement or worker order. The existing finite Construct executor
can build the reserved project and still validates real locality, qualification,
materials, claims and completion conditions. Physical completion leaves the authority's
capacity belief and pending commitment unchanged. Report reconciliation is the next
layer; it must not infer success by polling this physical status.

`inspect --view growth` and viewer snapshot exports expose the target, believed and
pending capacities, available material budget, planning state, and identified
commitments. Physical housing remains separately observable. GrowthPlanEvaluated and
GrowthProjectCommitted trace events preserve the capacity arithmetic, search result,
cost and causal link. Snapshot schema 40 persists policy, decisions, timers and
commitments; restore validates recipe budgets, sites, capacity, project ownership and
provenance. Old snapshots require their original build.

## Verification

Release build: zero warnings and errors. Complete suite: **748 passed, 0 failed**,
including 28 new growth cases. Results: `artifacts/test-results/h3-growth-commitments.trx`.
Coverage includes combined-plan costs, shared-material conflicts, duplicate sites,
bounded batches, reserve rounding, actual-versus-known stock/capacity/layout controls,
vacant capacity, finite physical execution, checkpoint boundaries, trace/input-order
determinism, capped backoff, malformed policy and forged persistence.

`scenarios/capacity_commitment.json` has 12 unscripted citizens, 8 initial beds, zero
reserve, four physical and known material units, and a legal site. All 10 assertions
pass. At tick 0 the authority commits one eight-berth habitat. At tick 100 there is
still exactly one reservation: actual capacity 8, believed completed capacity 8,
committed pending capacity 8. The materials remain physically in storage. No worker
commands are supplied. This is a commitment fixture, not autonomous end-to-end T11.

Run and replay artifacts: `artifacts/capacity-commitment` and
`artifacts/capacity-commitment-replay`. Their state and detailed trace match:

- Build: `468db813db7fcabb38f1fc4a3f6b1517bda51e5032ef77dd961445b5b76ccd0e`
- Content: `a4a842fa95e1798ec777670f934bd91f8f6f8e3a517e855147885ce7aaf60d1e`
- State: `d54d57e2c6fe87bd52f00b589eb73cb8ee15eb1eaa22b0a5574a628a49740c52`
- Trace SHA-256: `1E3F85D0AC8E1FA9E08BB8FD162B3F23F87790C4445BBA4EF8B7FD0FE4F530D7`

## Remaining release work

Add typed construction jobs/orders, qualified dispatch using known estimates, ordinary
read/acknowledgment, worker material collection/travel/construction planning, terminal
outcome reports and atomic project-key reconciliation. Accepted completion must convert
pending capacity to believed completed capacity exactly once; false reports must affect
records without creating physical tiles. The authority also needs its own evolving
believed layout for subsequent expansion after reports, and follow-up for blocked work.

Then complete autonomous T11 and its generated/false-report/vacant-capacity controls,
including needs enabled. Other work, priority, institutional, population, performance,
viewer and full requirement-audit gates remain. The first-release goal is active.
Shutdown remains pending until the entire release is implemented and verified.
