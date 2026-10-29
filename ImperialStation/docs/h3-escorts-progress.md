# Physical custody and coupled escort movement — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements the local custody start, cooperative input boundary and
coupled movement portions of R-WF-38 and R-WF-39. It does not complete T20, H3 or the
first release. The standalone implementation remains independent of Unity/spaceland.

## Behavior

The deterministic leader plans BeginEscort from its own read copy and finite local
identification. The handoff uses the configured local inspection duration (100 ticks
by default). Physical preconditions are checked at both start and completion: all
named guards must be co-located, idle, physically equipped, locally formed, and hold
matching acknowledged unexpired copies with local target observations. The named
account/actor must still be present and not in transit or claimed as staff. A stale
search result cannot freeze a distant target or replace missing guards.

Successful completion establishes a local group, cancels the target's remaining
finite action, releases its claims, closes ordinary member sessions, suspends the
target's read/acknowledgment-pending/working orders and removes their executable goals.
Institutional assignments are not cancelled or marked completed. Custody input
filtering precedes Cancel, Logout and ResumeWork handling. Ordinary target inputs
are rejected as CustodyRestricted; resistance gameplay is not introduced. Citizen
planners retain their pending goals but do not issue independent actions in custody.

The leader chooses the destination route. Every participant, including the target,
gets an ordinary Move action caused by CustodyBegan. All members depart and arrive
with one shared frozen edge duration. Arrival is atomic: every position changes
before sensing or decisions run, and another edge begins only for the complete group.
Later traversal changes affect the next edge, not the current one. An ordinary shift
boundary does not split the urgent escort. Member cancellation and known order
expiry stop the entire group at the next shared tile boundary and emit
EscortInterrupted. Interrupted groups do not silently restart on the same order.

Arrival produces AwaitingIntake while preserving custody. It does not create a
facility, occupancy, sentence timer or Detained state. Starting at the destination
requires the same finite handoff but creates no fictitious movement. Expiry while
waiting ends custody locally. Offline guards use their copied orders. Neither local
custody nor arrival updates the authority's reported TeamForming state.

## Persistence and inspection

Snapshot schema 24 persists authorization, begin action, start/boundary ticks,
interruption, member/order/movement identities and shared active routes/progress.
Restore validates authentic local copy and formation evidence, participant uniqueness,
equipment, custody restrictions, matching routes/timing and completion events.
Pending finite handoffs and expiry/cancellation during movement restore exactly.
Inspection returns detached copies. Runner adds `inspect --view escort-groups`,
EscortGroup assertions, exported groups and started/waiting/interrupted metrics.
The existing `escorts` view remains the institutional dispatch view.

## Verification

Release build has zero warnings/errors. All 431 tests pass, with zero failures or skips. The full test result is
`artifacts/test-results/h3-escorts.trx`; new cases are in EscortMovementTests.cs.
Coverage includes finite revalidation, target departure, equipment loss, cooperative
inputs, resource-claim cleanup, work suspension without assignment cancellation,
offline copies, frozen multi-edge timing, cancellation/expiry boundaries, shift
changes, destination-local custody, corrupt membership and continuation.

`scenarios/escort_movement.json` passes 13 assertions at tick 800 with 190 completed
actions and zero failures. The autonomous chain forms at 359, identifies at 460,
starts custody at 560 and arrives at 561. Both ordinary run and strict replay complete
with the same digest:

- Build: `f27e15266426fb959271557d1abe38ff5ff717b906af8396ac084647af5a1cc3`
- Content: `317804d50ce4b2862f9f1c3b623bd7d0688af6bc36b1214985938e82879fe85f`
- State: `1401274fa075ddf0c68127bb2122e13fb5987912302d6c27a57024ea30fc733f`
- Artifacts: `artifacts/escort_movement/` and `artifacts/escort_movement-replay/`.

The search-only golden fixture now stops at tick 460, before handoff completes.
Equipment-only tests use orders expiring before custody, or cancel a pending handoff
before testing manual equipment loss. Historical milestone reports remain unchanged.

## Remaining work

Actual declared detention services, finite capacity/claims/intake, connected current
authorization checks, occupancy/receipts, sentence and finite release remain pending.
So do critical-need interruptions, learned revocation handling, terminal interruption
and negative-search reports, institutional recovery, and reading current orders before
post-release work. Completed detention must affect qualifying reprimand counts while
preserving historical decision cutoffs. Full daily life, credential recovery, authority
projects and H2/H3/H4 verification remain outside this increment. No release-complete
or shutdown condition has been satisfied.
