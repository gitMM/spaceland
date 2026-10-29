# Explained sleep fallback — H4 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements the rest fallback in R-LIF-09. It preserves the physical
relief rules in R-LIF-08, cardless rest in R-LIF-10, and retained daily goals in
R-LIF-11. The full first release remains incomplete.

## Selection and execution

An urgent sleep goal first searches for real sleep, including finite berth
allocation where eligible. Other achievable urgent needs are considered before
fallback rest. Only after those searches return NoPlan may the actor use remaining
nodes from the same bounded planning request to seek a RestSpot. Exhausting the
primary search budget does not imply that no berth exists and does not authorize
another full search budget. Scheduled sleep uses the same primary-then-fallback
ordering within its due goal.

Fallback is based on private knowledge, published services, and locally learned
failures. Hidden outages are discovered only by actual attempts. The reason is
SleepUnmet/NoKnownUsableBerth, which describes the actor's knowledge rather than
asserting that no physical berth exists anywhere. The trace records SleepUnmet at
fallback selection and completion. If neither service nor known route is usable,
the goal remains blocked with this explanation.

Rest still travels, queues, reserves physical capacity, and runs for the declared
finite duration. No card is required. Its elapsed-time strain benefit applies;
sleep pressure continues at the configured resting rate. The action and activity
history remain Rest. The urgent goal remains SleepPressure, and a daily sleep goal
does not receive a completedActionId or Completed status from fallback rest.
Closing the sleep window retains the explanation in the missed-window reason.
Cancellation preserves only elapsed physical benefit and grants no completion.

A critical sleep crossing does not cancel the same goal's fallback rest. Other
critical needs retain the existing interruption rules. Completing fallback rest
sets a shared per-citizen sleepFallbackRetryAt using bounded backoff, then yields.
This prevents urgent and scheduled sleep producers from repeatedly selecting rest
without a retry interval. Later real berth plans remain eligible; a reopened own
berth is discovered through the citizen's private retry, not an availability push.

## Persistence

Snapshot 32 adds sleepFallbackRetryAt to need-agent state. Restore requires a
bounded clock supported by completed physical rest, validates active fallback
service/action ownership and its explanation, and rejects a rest activity presented
as completed daily sleep. No prior binary snapshot migration is provided.

## Verification

Release build: zero warnings/errors. Full suite: **580 passed, zero failed, zero
skipped**, 23 seconds. Evidence: artifacts/test-results/h4-sleep-fallback.trx.
Fifteen new test cases cover cardless fallback, exact elapsed strain and continuing
sleep accumulation, real-berth preference, other urgent relief, missing routes and
services, locally learned outages, critical crossing/cancellation, private recovery,
daily retention and window expiry, bounded search, and forged completion/cooldown
rejection. Both urgent and daily variants continue identically from 17 checkpoints
each spanning travel, queue, rest, retry and calendar boundaries.

scenarios/sleep_fallback.json has no scripts or cards and passes all ten assertions.
The citizen travels at 0–4, queues at 4, rests at 5–10, and remains idle through
the horizon at 30 with sleep unmet and retry tick 60. Two actions complete, none
fail. The short physical duration deliberately tests mechanics; it is not evidence
for the full 24-hour/population gate.

Run/replay artifacts: artifacts/sleep_fallback and artifacts/sleep_fallback-replay.
Their state and detailed trace match:

- Build: 1599f4d9aaff00aea64a4bef33c038eb40442584a94f51c8bff2dfa8b1346bd1
- Content: b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6
- State: f0e8c0bb2d02996d1f54e561f059db8599757b3a7ba75bd8a72f9de2c0f9627b
- Trace SHA-256: 16DE95E818FD5CBAFD7738BAA15CCADDC2D856F8BCFA010ECDA5B3247560C620

Remaining release work includes seek/optional need policies, finite consenting
conversation and bounded partner discovery, credential recovery, full-day and
population gates, authority capacity/projects/construction, institutional recovery,
other work domains, performance gates and the full requirement audit. The shutdown
instruction remains pending until the entire first release is implemented and
verified; no shutdown has been scheduled or executed.
