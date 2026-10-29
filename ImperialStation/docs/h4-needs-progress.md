# Exact needs and critical interruption — H4 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements the arithmetic and scheduling foundation for R-LIF-07,
private need visibility, and critical interruption under R-LIF-12/R-WF-38. The
system remains independent of Unity. Full daily life and the T27 gate are not
complete: physical sleep, rest, meals, toilets, conversation and their service
planning still need implementation.

## Exact arithmetic

NeedAccumulator is an immutable rate segment with AnchorQ, AnchorTick and a signed
integer RatePerHour. It uses checked Int64 arithmetic and the specification's
3,600,000 denominator. Display floors the full accumulator; rate changes retain
its remainder. Same-rate partitions retain the original anchor. Relief subtracts
whole points from the exact value; reset and rate changes clamp at physical bounds.
No hidden overshoot survives a subsequent mode change. Reads and serialization
never re-anchor.

FirstAtLeast uses ceiling division and supports positive integer tick durations
that do not divide an hour. The pure arithmetic supports sleep/rest rates and
discrete relief, but the world exposes no free relief or arbitrary mode-change API.
Only real activities will be allowed to call those transitions.

## World integration

Optional initialState.needs enables Hunger, Bladder, SleepPressure, WorkStrain and
Social for every actor using D-50 through D-54 defaults. Initial values use exact
Q units. All five rate/threshold rules may be overridden together for fixtures.
Historical fixtures without needs retain their earlier behavioral scope; enabling
needs across the complete release remains part of the final gate audit.

Current world activities derive Awake or Working modes. Finite recipe production,
installation, functional testing, machine inspection and cleaning accrue work
strain for their actual elapsed duration. Completion, failure and cancellation
restore the awake rate. Staff shifts, physical sleep/rest and their rates remain
to be connected to their own real activities.

Each actor has one next threshold event. Seek, urgent and critical crossings use
the exact accumulator; an already announced band does not create a zero-time loop.
Thresholds run in the Decision phase after physical completion. No calendar event
reduces needs. Missing relief services are exposed as NoKnownReliefService.
The actor view contains only that actor's needs. The authority view, formal records
and beliefs receive no automatic need disclosure.

Critical movement stops at the committed edge boundary. Other active cancellable
actions stop without their unfinished result. A critical escort guard interrupts
the whole group at its shared boundary and queues the existing ordinary reports.
Critical guards cannot start or finish a new custody handoff. A detained target's
needs continue without cancelling the sentence or gaining unrestricted actions.
Full priority selection, urgent relief planning, retained routine goals, service
queues and custody-permitted relief remain outstanding.

## Persistence and diagnostics

Snapshot 27 persists rules, per-actor modes, exact segments, announced bands, next
thresholds and critical history. Restore checks owners, activity-derived rates,
bounded accumulators, timestamps and threshold timers. Actor snapshots are detached.
The CLI adds inspect --view needs; exported snapshots include the need state.
Metrics add needThresholdsReached and criticalNeedInterruptions. NeedQ assertions
compare a named need's exact accumulator for one actor.

## Verification evidence

Release compilation completed with zero warnings/errors. The full suite passes
494 tests with zero failures or skips in 22 seconds.

Tests cover the exact T27 alternating-mode value 1,799,500,000 (displayed 499),
same-rate partition invariance, a first crossing at tick 600 rather than 599,
137 ms clocks, relief remainder preservation, saturation, checked overflow,
pure getters, pending-threshold checkpoints, different advancement chunks,
actual working time through completion/cancellation, rejected corrupt anchors,
critical movement/transfer interruption, escort interruption and continuing
needs in detention. These are evidence for the implemented foundation, not a
substitute for the required physical-activity and full-release gates.

The critical_escort scenario passes 14 assertions through tick 800. Hunger reaches
critical at tick 566 during a shared movement edge. The group arrives and stops at
571. Two reports subsequently inform the authority. There are 231 completed actions,
zero failed actions, one critical interruption, and zero detention occupants.
Original run and replay have the same state digest and detailed trace hash.

| Evidence | Value |
|---|---|
| Build | `71d205e1b8e0602c58451b82d63800d51e8641ee3acd20697b315e615914cbc7` |
| Content | `317804d50ce4b2862f9f1c3b623bd7d0688af6bc36b1214985938e82879fe85f` |
| State | `b27d77531cb7af93ee23030f44461b227c0149c1e5502f383f7e971e1b9dfddc` |

Artifacts are in artifacts/critical_escort and artifacts/critical_escort-replay.
The full regression result is artifacts/test-results/h4-needs.trx.

Next: physical relief services with real stock, capacity and claims; finite/pro-rata
effects and cancellation; berth authorization; bounded autonomous service planning;
priority arbitration and custody-permitted services. Credential recovery, authority
projects/capacity, staffing, other work domains, scale/tuning and the complete gate
audit also remain outstanding. The full-release shutdown condition is not satisfied.
