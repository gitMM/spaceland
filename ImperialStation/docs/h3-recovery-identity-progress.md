# Restricted recovery identity — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment establishes the finite identity check and scoped session required
by R-WF-13 and D-36. It is a foundation for lost-card recovery, not completion of
R-WF-10..15 or the autonomous T18 gate.

## Implemented behavior

CredentialRecovery is an explicit terminal capability. RecoveryLogin must occur
at an enabled, connected, role-permitted desk. The worker's account is the modeled
employee identity. A configured retained capability belongs to exactly that actor
and account; this is simulation data, never a real password or secret. Missing or
foreign capability fails with IdentityVerificationFailed. A caller cannot supply
another actor or card as an alternate identity.

The check takes 300 ticks by default, with positive scenario overrides. It shares
physical terminal claims and FIFO queueing with ordinary Login. Cancellation and
logout release pending claims, and service availability is rechecked when queued
work is granted and when the check completes. Revocation of the old card does not
invalidate alternate identity verification.

Successful completion creates a session bound to the physical actor, their account,
the desk and an expiry. Its scope is CredentialRecovery and its ordinary credential
reference is zero. Normal inbox reads, posts and record queries fail explicitly with
RecoverySessionRestricted, including when the worker also happens to hold a valid
card. No message is read or posted by the identity check. It grants no equipment
credential, revokes no old card, creates no replacement and changes no inventory.
Ordinary equipment authorization continues to check a real held card independently.

Ordinary citizen planners see no normal session terminal while this restricted
session is open. A separate successful normal Login is required for ordinary access.
Leaving the terminal, expiry, logout or terminal outage ends the restricted session.

## Configuration and persistence

information.recovery contains identityCheckTicks and capabilities, each with
actorId, accountId and retained (default true). Missing recovery configuration means
the service is unavailable. Terminal capabilities include CredentialRecovery.
RecoveryLogin uses the usual serviceId and local target. Session inspection/export
now exposes Scope. Sessions assertions count sessions of a named scope and optional
actorId.

Snapshot 35 adds recovery configuration/capabilities and session scope. Restore
validates identity ownership, credential/scope separation, local desk capability,
claims, FIFO ownership and finite completion/expiry events. Older binary schemas are
intentionally incompatible. Ordinary session restoration also verifies its bound
credential belongs to the session actor/account.

## Verification

Release build: zero warnings or errors. Full suite: **624 passed, zero failed,
zero skipped**. Evidence: artifacts/test-results/h3-recovery-identity.trx.
Seventeen new test cases cover the identity boundary, all three forbidden ordinary
operations, missing capability/service, foreign identity, normal/recovery FIFO,
cancellation, session replacement, departure, expiry, outage and old-card revocation.
Eleven checkpoints cover active checks, queued checks and session expiration with
identical continuation state and trace. Corruption cases reject widened scope,
foreign account, a credential attached to a restricted session and invalid expiry.

scenarios/recovery_identity.json is deliberately scripted: it tests physical service
primitives, not autonomous recovery. All nine assertions pass. Ordinary login fails
at tick 0; recovery check runs at ticks 1–4; an ordinary read fails at tick 5 and
leaves the mailbox unread; logout releases the session at tick 6. The original card
remains on its original tile and no authority belief changes.

artifacts/recovery-identity and artifacts/recovery-identity-replay agree:

- Build: bc13f80b4509bf6d7f90ca54e6c2eb63a754e57aec678914489801bd4574a647
- Content: b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6
- State: 5f9f942b3d52176aafe01c7ac6b05406431a9172b817966aefcff4c6f507ee54
- Detailed trace SHA-256: 37F83DEFBB034F5A5F8B389477D0A4227A6AFD0B60BF9FB9486224C4025EFA66

## Next work

Add the scoped loss request, idempotent case and revocation, recovery mailbox and
explicit read, finite stock-consuming issuance, physical pickup and autonomous
RestoreUsableCredential planning with known-card retrieval alternatives. Retained
work must resume only after normal login and current-order read. Full T18 remains
pending, along with the broader first-release requirements described in the earlier
progress reports. Shutdown remains pending until the complete release is verified.
