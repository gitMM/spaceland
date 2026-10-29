# Physical relief services — H4 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment adds finite eating and toilet use, pro-rata sleep and rest, real
service capacity, FIFO waiting, meal reservations and custody-permitted relief.
It advances R-LIF-06..10, R-LIF-12 and R-LIF-17. Full daily life, autonomous service
selection, roster opportunities and the complete first release remain unfinished.
The implementation remains a standalone .NET system.

## Physical execution

Need services declare Cafeteria, Toilet, RestSpot or Bed on completed tiles. Each
has actual capacity and a finite activity duration. Default durations follow D-55:
Eat 9000, UseToilet 1800, Rest 6000 and Sleep 288000 ticks. Actions require physical
presence, permitted custody access and an enabled service. Basic relief needs no
credential. Beginning an activity ends an ordinary terminal session.

Requests wait in FIFO order for a seat, stall, rest place or berth. A queued actor
does not receive sleep/rest rates or a completed result. Admission starts the full
finite duration. Cancellation removes a queued or active claim. A service outage
cancels active use but retains waiting requests; reopening serves that queue.

Eating reserves one actual unit from an accessible meal stack. Multiple seats can
reserve different units of the same stack, while transfers/splits/merges cannot move
reserved stock. Completion consumes exactly one unit and subtracts 500 hunger points
from the full accumulator. Cancelled eating consumes nothing and grants no relief.
Meal sinks participate in global resource conservation and are checked against
completed activity histories. Missing stock yields an explicit failure, never food.

Completed toilet use resets the exact bladder accumulator. Sleep and rest derive
their rates from real active occupancy. Cancellation preserves only elapsed benefit;
rest does not reduce sleep pressure. Downward threshold crossings use the first
integer tick strictly below a threshold, preventing stale critical state during
relief. Mail delivery does not wake sleeping actors.

## Berths, custody and knowledge

Beds have capacity one. An initial assignment authorizes a particular actor and
each actor can have at most one initial assigned berth. Assignment is distinct from
the occupancy claim. Vacant beds exist but do not grant unauthorized sleep; obtaining
an allocation for an unassigned citizen remains future work.

A service can belong to a declared detention facility at the same location.
Only a detained occupant of that facility may use it. An unassigned custody bed
permits that authorized occupant; ordinary citizens cannot use custody-only services.
Detainees may cancel a need activity without cancelling their sentence or gaining
movement freedom. When release can begin, its timer interrupts ongoing need activity
with partial sleep/rest benefit and starts finite release; sleep cannot prolong the
sentence by its entire remaining block.

Actor views expose immutable published service definitions, never current queue,
occupancy, stock or outage state. Private need values remain separate from authority
views and formal records. Privileged diagnostics expose physical service state and
activity histories. Autonomous travel/choice/priority planning is not yet connected.

## Persistence and verification

Release compilation completed with zero warnings/errors. The full regression suite
passes 517 tests, with zero failures or skips, in 23 seconds.

Snapshot schema 28 persists service definitions and availability, queues, occupancy
claims, activity history, pending admissions, reserved meal identities and food sinks.
Restore verifies action ownership, finite completion timers, capacity, locality,
assignment, custody, accessible stock, rate mode and sink/history agreement.

Tests cover finite results, shared-stack reservations, cancellation, protected stock,
FIFO reopening, cardless use, distant-service rejection, assignment limits, real
one-tick sleep cycles, falling thresholds, sleeping mailboxes, custody relief and
release, corrupted reservations, and 28 checkpoint continuations across all four
service types. The actual sleep cycles produce T27's exact 1,799,500,000 accumulator.
The existing dirt corruption test now locates its actual serialized record rather
than relying on an obsolete fixed offset from the end of the snapshot.

The need_services scenario is an explicit scripted physical-contract fixture, not
an autonomous daily-life gate. Its 15 assertions cover two meals sharing one seat,
toilet relief, rest, assigned sleep and exact final accumulators. It completes five
actions with zero failures, consumes two real meals and leaves no queue or occupancy.
Run and replay have identical state digests and trace hashes.

| Evidence | Value |
|---|---|
| Build | `89c2f2a8dae4b0705915e23c93dfc84163be3f4f5651e56ba166e798d73d3fab` |
| Content | `b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6` |
| State | `66faf5d69b21c96bbda0fea5e5de42480e2bd923a9f7011fe853a842d0703dc0` |

Artifacts: artifacts/need_services, artifacts/need_services-replay and
artifacts/test-results/h4-need-services.trx.

Next: bounded autonomous need planning, full priority arbitration, retained roster
goals, meal/sleep windows and scheduled rest; authorized berth allocation; finite
conversation with consenting partners; shortages and coverage metrics. Credential
recovery, authority capacity/projects, staffing, remaining work domains, performance
tuning and the full requirement/gate audit also remain outstanding. Shutdown remains
conditional on completing and verifying the entire requested first release.
