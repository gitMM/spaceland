# Seek toilet and additional rest — H4 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment adds D-51 seek-level toilet use and D-53 additional strain-driven
rest, advancing R-LIF-03 and R-LIF-06..12. It does not complete the first release.

## Policy and priority

At the exact bladder seek crossing, an autonomous citizen receives a retained
toilet due tick and stable sequence. Initial seek-level bladder receives a seeded
due tick of zero. The stamp survives travel, work, unavailable services and retries;
it clears when actual toilet relief puts bladder below seek. As an explicit policy
choice for the ladder's otherwise unspecified seek-level toilet category, this
personal due goal joins reports, check-ins and scheduled activities at priority 5.
Earliest due tick and the shared stable goal sequence determine selection. Urgent
and critical bladder continue using their higher need priorities.

Additional rest requires strain at seek and an actor-known off-duty period or an
open scheduled-break opportunity. Unknown rosters do not establish a break. This
additional rest yields to known urgent orders, eligible higher needs, due goals,
and locally retained ordinary duty work. The independent mandatory scheduled break
still outranks ordinary work and does not require strain at seek. Both paths use
finite RestSpot capacity and elapsed physical relief.

At the next permitted boundary, a busy worker selects a due toilet goal before
more ordinary work. A committed finite work action is not cancelled merely by a
seek crossing. If no known route or usable service can be planned, the goal keeps
its original stamp and a bounded retry, permitting other useful activity.

Urgent failures now retain a separate urgentRetryAt. A blocked urgent goal can
yield to attainable seek relief or optional rest, while its own retry remains
scheduled. The lower tier cannot extend an already due urgent retry. A new local
threshold event can trigger reconsideration. All plans use the existing bounded
GOAP and per-tick request cap; actor views supply needs, knowledge and services.
No service availability or authority knowledge is pushed into the citizen.

## Persistence and replay

Snapshot 33 adds toiletDueTick, toiletSequence and urgentRetryAt to need-agent
state. Restore checks seek/stamp consistency, due-time bounds, shared sequence
uniqueness, bounded urgent retries and the corresponding decision timer. Inspection
and viewer exports expose the fields through need-agents / needAgents.

The integration fixture exposed a trace export discrepancy for daily windows
already open at initialization. Such goals are contained in the replay's starting
checkpoint, so their initialization events are not replayed. Runner now exports
events strictly after the checkpoint's last processed tick. This also excludes
prior events when continuing an already traced world. The world’s complete in-memory
trace remains available. A regression test verifies exported run/replay trace
equality while preserving the initially seeded daily goals.

## Verification

Release build: zero warnings/errors. Full suite: **592 passed, zero failed, zero
skipped**, 22 seconds. Evidence: artifacts/test-results/h4-personal-needs.trx.
Twelve new cases cover finite cardless toilet use, exact seek crossing during real
work, same-tick report ordering, blocked-goal retention, optional-rest eligibility,
mandatory-break and ordinary-work precedence, backoff, useful relief beneath a
blocked urgent goal, forged stamp/retry rejection, and checkpoint-scoped trace
export. Nineteen checkpoint positions cover thresholds, services, duty and routine
boundaries with identical continuation state and trace.

scenarios/personal_needs.json contains no scripts or cards. Both citizens know a
240-tick day with 80-tick duty and are off duty at the start. The first seeks a
toilet at 500 bladder; the second seeks additional rest at 500 strain. Both travel
at 0–4 and queue at 4. Toilet use runs 5–7; rest runs 5–10. All ten assertions pass
with four completed actions and zero failures. These short durations verify
mechanics and do not satisfy the full-day/population gate.

Run/replay artifacts: artifacts/personal_needs and artifacts/personal_needs-replay.
Their final state and detailed trace match:

- Build: 758baa283d7c97a235d97188a7db4cf6543d0f30af94203d8a2d605fab82384f
- Content: b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6
- State: 4e4e3e65d569af7cc819abc210324de9aa1473932dd2128a6a314343dfd99aeb
- Trace SHA-256: CE22CEE265D7C0E39813E835F1C6E96A30ED0B981DD214FAFA7213D4967703A5

Remaining work includes finite consenting conversation and bounded local partner
discovery, credential recovery, complete priority integration across institutional
failure/recovery paths, normal-day/population gates, authority construction and
capacity projects, additional work domains, performance gates and the full release
audit. Shutdown remains authorized only after the entire release is complete and
verified. No shutdown has been scheduled or executed.
