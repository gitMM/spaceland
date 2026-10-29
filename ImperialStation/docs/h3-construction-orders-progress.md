# H3 construction orders and received capacity

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

Verified 2026-10-29. The authority-to-worker construction loop now runs autonomously
through ordinary messages and finite physical work. This advances the first-release
goal; broader release verification and remaining behavior are still unfinished.

## Implemented behavior

Growth commitments now create typed ConstructHabitat jobs. Dispatch uses registered
qualifications, published on-duty rosters, known commitments and reported terminal or
roster locations. It ranks nominal travel estimates with stable actor-ID ties. It never
uses a worker's live location, private inbox, current inventory or actual project
completion to choose a worker. Unassigned projects retry on the growth review clock;
pending capacity still prevents duplicate expansion. Ordinary overdue-report replacement
and patrol policies now recognize construction jobs and their recorded sites.

The order carries the committed project ID, output tile ID, site, definition, recipe,
role/tool/credential requirements and berth output. Only a completed authenticated inbox
read creates the worker's private order. The worker evaluates a bounded construction
GOAP domain, posts acceptance, and proceeds when that acknowledgment is posted. The
authority creates a deadline only when acceptance arrives, using declared recipe and
pickup costs plus nominal travel, terminal service and latency estimates.

Construction planning uses the citizen's own inventory and learned item facts. It binds
bounded material-stack/tool alternatives and reachable neighboring standing tiles, then
searches pickup, travel and finite construction actions. Only the first action executes
before replanning. Multi-stack inputs are supported. Real construction still checks the
site, role, tools, materials and claims; a failed physical attempt queues an ordinary
blocked report. Retained work respects existing needs, due reports, duty and custody
gates. Normal cancellation and expiry operate on the read order.

Physical completion creates the actual tile and beds, consumes the real recipe inputs,
records physical job completion, and queues a worker report. It does not change authority
capacity. The local completion event is retained and linked to the return-report plan.
On receipt, construction claims require the assigned account, attempt, job, related order,
subject, supported claim and observation time. No physical completion check is performed
to decide whether the claim is believed.

The first accepted completion report for a project atomically increments believed
completed capacity and removes that project's expected capacity from the pending total.
The identified ledger entry remains as history and keeps the already-spent material
budget unavailable. New operation keys cannot add the same project's capacity twice.
The accepted site extends the authority's believed layout for subsequent projects.
False reports can therefore deceive capacity/layout records without creating tiles,
consuming items or adding actual beds.

Schema 41 persists typed order payloads, job/private-copy project links, worker completion
evidence and accepted report records. Restore checks job/commitment ownership, immutable
order specifications, capacity arithmetic, recipe budgets and accepted-report evidence.
Older artifacts require their original build.

## Verified evidence

Release build: zero warnings/errors. Full regression suite: **760 passed, 0 failed**.
Results: `artifacts/test-results/h3-construction-orders.trx`. Twelve new cases cover the
default recipe, single/multiple input stacks, unavailable stock, false and duplicate
reports, malformed claims, forged acceptance evidence, protocol-boundary checkpoints,
trace/getter invariance, response-chain auditing and subsequent expansion from a newly
believed completed site. Existing physical construction and growth tests also pass.

`scenarios/autonomous_construction.json` contains 12 unscripted citizens, eight initial
berths, reserve zero, exactly four material units and a builder with ordinary terminal
access. Needs remain enabled with default thresholds. The habitat recipe consumes four
materials, takes 18,000 work ticks and creates eight beds. The fixture has no actor
commands. All 13 assertions pass:

- Tick 0: one project is committed; actual/believed/pending capacity is 8/8/8.
- Tick 1: the authority posts the order.
- The builder obtains it through its next ordinary check-in/read, acknowledges,
  collects materials, travels and starts work.
- Tick 18,525: physical work completes; capacity is 16/8/8.
- Tick 18,538: the returned completion report arrives; capacity becomes 16/16/0.
- Tick 20,000: there is still exactly one project and one accepted capacity addition.

The declared habitat-builder response chain passes seven transitions from commitment
through order/read/acknowledgment/pickup/completion/report acceptance, checking 16 actions.
The audit rejects external response commands under the existing response-chain rules.
This fixture does not by itself prove every generated T11 variant or the full release.

`artifacts/autonomous-construction` and `artifacts/autonomous-construction-replay` match:

- Build: `8a89b855314e54a64d65c4484016aad01a53286fe40ecf449b9d09b586188e59`
- Content: `9e5380ed7ab90c4b62a61eafe5e221204a223b1c40ad9e495b2ebea4063c4c87`
- State: `1feab0d2bcc06710bc456959f1f6f373f68469b8c19fe53fe4327828d6ea1da0`
- Trace SHA-256: `5B2BC12A92176FDA596B097D97F18923C14A9A2BE1CF36A279C2547EC4A12F05`
- Response proof SHA-256: `E10FBA2ACC4F6B59FF6AE286BE23E03345BE8EBE3E1F1F00480632930C522072`

## Remaining release work

Add broad generated construction coverage, dispatch contention and interruption/resumption
controls, and correction of accepted capacity/layout beliefs by later contradictory
evidence. Blocked/rejected construction needs an explicit follow-up lifecycle; the current
commitment remains pending rather than guessing physical success or silently releasing
its budget. A different worker may lack routes through newly built tiles until that
layout is communicated or learned locally. Viewer construction presentation still needs
verification. These remain explicit limitations rather than completed gates.

Other remaining work includes the full H2 work-domain audit, priority integration,
institutional recovery, multi-day/population scenarios, performance/tuning gates and the
requirement-by-requirement first-release audit. The goal stays active. The authorized
shutdown remains pending until the complete first release is implemented and verified.
