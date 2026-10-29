# Detention requests and separate approval — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements the request/authority decision path in R-WF-37 and retains
unique expiring administrative authorizations for subsequent escort fulfillment.
It does not implement R-WF-38 escort/custody, complete T20, H3 or the first release.
The solution remains isolated from spaceland and Unity.

## Implemented

A supervisor who completes a read of an accepted case reply with count at or above
the configured threshold queues RequestDetention. The ordinary report planner and
finite terminal posting/transport apply. An unread count never creates a request.

The authority separately validates requester permission, the service-issued accepted
case reply addressed to that account, accepted case ownership, current time-window
count and existing authorizations. It uses institutional evidence and registered
identity, without looking at target location, activity, inventory or private state.
Missing approval policy leaves an explicit wait; stale counts can fail the threshold.

New approval retains a unique authorization with known actor/account, source request
and case, justifying case IDs, issue-time count cutoff, expiry and sentence duration.
Defaults are 1728000 ticks to expiry and 864000 ticks for the eventual sentence.
Duplicates and fresh request IDs reuse an active authorization without extending it.
An expired operation retry cannot allocate a new authorization. A fresh operation can
be evaluated after expiry, including in the same tick as the old expiry event.

Decision replies use ordinary delayed mailboxes. Private request state changes only
through its own post/read actions. Central expiry does not rewrite an offline local
copy; the known expiry remains in that copy. Expiry allocates no physical effect.
Authorization does not freeze/move the target, create a team, claim a detention slot,
start a sentence or schedule release. These remain explicit next implementation work.

## Historical decision correction

Personnel count replies now retain countThroughCaseId. Accepted cases and case replies
also retain validationRecordVersion. Authorization proofs and request decisions carry
their count cutoff. Ticks alone cannot distinguish two acceptances or an acceptance
and later amendment within one tick. These cutoffs preserve the exact historical
institutional decision through snapshots. Regression tests exercise both collisions.

## Verification evidence

Release compilation: zero warnings/errors. Full suite: **351 passed, 0 failed,
0 skipped**. TRX: `artifacts/test-results/h3-detention-approval.trx`.

Twenty new tests cover read/request/post/approval/reply boundaries; no remote target
restriction; one-reprimand no-request behavior and manual threshold rejection;
unavailable policy waits; receipt-time count expiry; offline requester awareness;
canonical reuse and operation-key conflicts; invalid requester/case/reply/account;
authorization expiry and same-tick renewal; same-tick case/record cutoffs; malformed
authorization snapshots; reuse without renewing expired count evidence; and atomic
expiry-overflow failure. The overflow path follows the existing explicit runtime-error
exception contract and leaves no partial authorization or request decision.

Generated verification compares exact state bytes and trace suffixes at 14 checkpoints
for 10 seeds/query durations/expiry periods (140 resumes). Concurrent/chunk/getter
neutrality runs at 2/4/8 workers across 12 seeds.

Canonical `scenarios/detention_authorization.json`: ten passing assertions, 130 completed
actions and zero action failures. It extends the two distinct wrong-slot cases for
account 1. Wrong-installation worker inputs use shared actions; supervisors perform
verification, audit, filing, reply reading and detention requests autonomously.

| Tick | Evidence/state |
| --- | --- |
| 166 | Second distinct case accepted; count reply records 2 |
| 177 | Supervisor reads that reply and queues RequestDetention |
| 178 | Finite request post completes; no authorization yet |
| 179 | Authority approves authorization 1, justified by cases 1 and 2 |
| 180 | Approval reply delivered; private request remains Posted |
| 199 | Requester reads Approved and the known expiry |

This fixture overrides sentence to 1000 ticks and authorization lifetime to 2000 ticks.
Authorization expiry is 2179; sentence time has not begun because intake has not occurred.

Run and strict replay both complete with the same digest:

| Identity | Value |
| --- | --- |
| Build | `0837c15c5a224dc2172f84589585b5feeaa56cb234c359eed8402f3e30c529c7` |
| Catalog | `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38` |
| Digest | `581e9da7fe675d355fba6a28102d4ca1ceb683b9a678287c862e3d5987942e5f` |

Artifacts: `artifacts/detention_authorization/` and
`artifacts/detention_authorization-replay/`. Binary snapshot schema is 20; older binary
checkpoints are intentionally rejected. JSON envelopes remain schema 1.

## Remaining release work

Next: bind one guard team to the authorization, publish complete escort orders with
roster/rendezvous/known search candidates/destination, require individual reads and
acknowledgments, acquire equipment and form the team physically. Then implement local
identification, coupled escort, interruption/revocation, finite capacity-checked intake,
custody restrictions, sentence/release, completed-detention count exclusion and current
order reading before work resumes. Historical count validation must use detention
completion history as of each decision, not only the latest completion timestamp.

General verification triggers, authority capacity/projects, credential recovery, full
daily life, other work domains, population/tuning, specialized viewer presentation and
the full H1-H4 acceptance audit also remain. The goal is active. User-authorized shutdown
remains pending full release completion and verification; this increment is insufficient.
