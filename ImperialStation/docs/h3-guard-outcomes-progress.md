# Guard revocation and outcome reporting — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment advances R-WF-38 while retaining the standalone .NET boundary.
It does not complete T20, H3, or the first release.

## Revocation and individual knowledge

`ScheduleDetentionRevocation` and the `RevokeDetention` scenario disturbance revoke
an issued authorization with an idempotent operation key. The authority records
the change, closes pending dispatch, and sends an ordinary notice to each assigned
guard. Neither delivery nor central status changes a guard's private copy, current
action, or distant custody. A command at or after authorization expiry is a no-op.
An authorization ID may be scheduled before it is issued, but must exist when the
command executes.

A guard learns revocation by completing its own inbox read or by attempting intake
at the copied, powered and connected facility. Offline copies still permit local
formation, search and custody. Disconnected intake cannot disclose current status.
The first guard to learn revocation stops the physical group; other participants
record interruption without receiving the informed guard's private knowledge.
Guards waiting at intake can use a co-located terminal to read notices. The target
remains subject to custody restrictions. An in-flight intake rechecks authorization
at completion; rejection frees the reservation and never creates occupancy.

An already completed intake retains its sentence after authorization revocation.
Ordinary sentence completion and release receipts still apply. Historical approvals
are validated against the records available when those decisions were made.

## Reports and authority state

Guards queue ordinary authenticated reports for exhausted searches, unavailable
equipment, unreachable routes, interrupted escorts, local expiry and learned
revocation. Each order/outcome pair queues once. Travel, login, posting and message
delivery remain separate steps and can be delayed or blocked. Expiry after a
successful transfer does not produce a false failure report.

The authority accepts reports using order identity, registered account, physical
sender and authenticated transport. It does not inspect private read state,
equipment, observations or current position to decide whether a claim is true.
Received outcomes append formal GuardOutcome records; order IDs use the distinct
GuardOrderMessageId field. Nonterminal failures produce NeedsAttention while retaining
the guard commitment. A terminal report releases that guard's institutional commitment.
Existing cancellation, expiry, custody, release and explicit refusal states retain
their meaning. Duplicate reports do not duplicate formal outcomes.

## Persistence and inspection

Snapshot schema 26 includes revocation commands and timers, individual notices,
private learning proofs, queued/posted outcomes and received formal outcomes.
Restore validates these against order, transport, local action and record histories.
Copied authority claims are not validated against inaccessible private evidence.
Outcome keys use ordinal string ordering.

CLI inspection adds `revocations`, `guard-revocations`, `guard-outcomes` and
`guard-receipts`. Exported snapshots include all four layers. Scenario assertions
add KnownGuardRevocations and GuardOutcomeReceived.

## Verification

Release build: zero warnings/errors. Full suite: 477 passed, zero failed, zero
skipped in 22 seconds (`h3-guard-outcomes.trx`).

The regression suite adds notice-delivery/read separation, offline continuation,
disconnected intake, revocation during and exactly at intake completion, retained
sentences, expiry idempotency, authenticated negative reports and duplicate receipts,
wrong-account rejection, forged-notice rejection, unstaffed cancellation, local
terminal reads with and without an active intake, and corrupted learning-proof checks.
Fifty-five checkpoints resume through the major revocation boundaries with identical
serialized final states.

`escort_revocation.json` completes 20 milestone assertions at horizon 1000. It has
240 completed actions and one expected failed intake. The original run and replay
have identical state digests and trace hashes.

| Evidence | Value |
|---|---|
| Build | `29350fa9dd09eac990bceaff9709b273b7fbc5f7a8ef94826a56161deb227203` |
| Content | `317804d50ce4b2862f9f1c3b623bd7d0688af6bc36b1214985938e82879fe85f` |
| State | `0c625786375dd821e4a420008546c7be242ac822fdc7a8d07919fb56d0febe28` |

Artifacts: `artifacts/escort_revocation/`, `artifacts/escort_revocation-replay/`,
and `artifacts/test-results/h3-guard-outcomes.trx`.

The canonical revocation occurs at tick 600. The leader discovers it during the
intake completion check at 861. Custody ends with zero occupants. Its partner reads
the notice at 866. Four separate interruption/revocation reports arrive by 871.

## Remaining work

NeedsAttention now carries received evidence, but automatic recovery/reassignment
policy is still pending. Critical needs and detention-permitted daily services,
credential recovery, authority capacity/projects, staffing and berths, full daily
life, other work domains, scale/tuning, viewer presentation and the complete gate
audit remain outstanding. The user's shutdown condition is not satisfied.
