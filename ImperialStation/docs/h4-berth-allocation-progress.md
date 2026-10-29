# Finite berth allocation — H4 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment advances R-LIF-09 and own-berth knowledge. It does not complete the
first release. In particular, explicit rest fallback with explained unmet sleep
still needs implementation, along with the other remaining H4 and authority gates.

## Behavior

An ordinary vacant bed offers a local AllocateBerth service. An applicant must be
physically present, unassigned, outside custody, and authorized. The action reserves
the bed for its finite allocationTicks duration (default 100). At completion the
executor rechecks availability and authorization, appends an account-scoped
BerthAllocation record, and gives the applicant a pinned private AssignedBerth
receipt. It neither creates nor moves inventory. Initial assignments receive
seeded receipts and corresponding initial records.

The routine access policy requires an active, physically held, holder-bound
credential by default. This is an implementation choice where the specification
does not define an allocation access protocol. A service may explicitly declare
allocationRequiresCredential:false to authorize its registered local applicant
without a card. Allocation remains finite in that mode. Sleeping in an already
assigned berth does not require a card. Custody beds use the existing detention
service rules and cannot be acquired through ordinary housing allocation.

There is at most one assignment per citizen and one citizen per berth. Concurrent
applicants cannot share a reservation. Cancellation, service outage, or credential
revocation before completion grants no assignment. Completed assignments persist
across vacancy and temporary outages. UsableHousingBerths counts enabled ordinary
beds, including vacant ones; allocation and occupancy do not create capacity.

NeedPlanning builds a bounded travel → allocation → sleep plan from published
service definitions, private own-berth receipts, and known held credentials. The
allocation duration contributes to candidate costs. After completion, replanning
uses the private receipt. Another citizen's view retains its original published
vacancy information; a failed local attempt supplies that citizen's private failure
memory and permits trying another known bed. Urgent and scheduled sleep both accept
allocation as an intermediate action. A critical sleep crossing does not cancel an
allocation already owned by that sleep goal.

## Persistence and diagnostics

Snapshot 31 adds live assignment state, formal allocations, private receipts and
pending reservations. Restore validates unique assignments, immutable initial
assignment constraints, record fields/version/account, pinned receipt provenance,
bound credential identity, and finite action/timer ownership. A revoked credential
may remain bound to a pending action; completion must reject it normally.
Messaging-disabled physical fixtures may still carry validated berth records.
Previous binary snapshot schemas are intentionally incompatible.

Runner adds berths and berth-allocations inspection views, corresponding exported
arrays, and usableHousingBerths, vacantHousingBerths and berthAllocations metrics.
The allocation service configuration is exposed in scenario need services.

## Verification

Release build: zero warnings and errors. Full suite: **565 passed, zero failed,
zero skipped**, 22 seconds. Evidence: artifacts/test-results/h4-berth-allocation.trx.
Fourteen new test cases cover finite completion, private receipt isolation,
contention, duplicate assignments, cancellation, outage, credential denial and
revocation, explicit cardless authorization, stale vacancy alternatives, critical
sleep crossing, 24 usable beds with 20 assigned and four vacant, corrupted snapshot
rejection, and exact continuation across 17 checkpoints.

scenarios/berth_allocation.json has no scripted actors or commands. All 13
assertions pass. Applicant travels at ticks 0–4, allocates at 4–7, queues at 7,
and sleeps at 8–13. The observer learns no assignment. This is a deliberately
short service fixture, not the full-day or population release gate.

Run and replay artifacts are in artifacts/berth_allocation and
artifacts/berth_allocation-replay. Both have three completed actions, zero failed
actions, and matching state and detailed trace:

- Build: f40862e0f2e41f34741c45d0efed6197784d9dc573119d961df0dbf534aef932
- Content: b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6
- State: 3115c65cfd03b37eaa525d7adf865297eaeaea99f0cd6c85f8b2364ea27576b3
- Trace SHA-256: 038C3B1FB010D967D3FB8B06D9A6EB5F899D87671AD289E57DA3B483A42DD8AA

The first-release goal remains active. Shutdown is authorized only after the whole
release is implemented and verified; no shutdown has been scheduled or executed.
