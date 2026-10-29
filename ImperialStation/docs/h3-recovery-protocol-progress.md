# Physical credential recovery protocol — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements the service-side sequence in R-WF-14 and the physical,
account-isolation and retry invariants in R-WF-15. It builds on the restricted
identity check in h3-recovery-identity-progress.md. The autonomous T18 gate remains
unfinished: loss disturbance/discovery, recovery goal selection and retained-work
resumption still need integration.

## Formal report and scoped mailbox

PostLostCard requires the actor's current restricted recovery session and the item
ID of a card registered to that actor/account. It is a finite terminal operation.
Successful posting creates an authenticated LostCardReport with the physical actor,
account, terminal, session and submission time. It does not revoke a card, create a
case, alter inventory, or teach acceptance at posting time.

The delayed message reaches CredentialService, which validates its authenticated
restricted-session provenance and credential ownership. Acceptance creates or
reuses one case per old credential/account, revokes that credential and authorizes
one replacement. The old card entity remains in its actual location. Formal case
and credential records retain the decision. Repeated reports reuse the same case.

A typed reply travels to RecoveryAccount, a separate account-scoped mailbox.
ReadRecoveryInbox captures only already-delivered replies for the session's own
account and takes the configured finite read duration. Ordinary inbox reads and
queries remain forbidden in recovery sessions. Ordinary posts cannot forge either
recovery message type or route arbitrary content into the recovery mailbox.

Acceptance and mailbox delivery do not change actor knowledge. Only successful
reading pins the case authorization and the reported old-card revocation. If the
bounded knowledge store cannot hold the required facts, the reply remains unread.
IssueReplacement requires this read authorization; an unread delivered reply is
insufficient. Cancellation or a failed read discloses nothing.

## Finite issuance and physical pickup

A recovery desk explicitly connects a terminal to a local printer workstation and
a declared recipe. The supported service recipe consumes exactly one blank card
and produces exactly one card. Printer/recipe capabilities, locality, permitted
roles and positive timing are validated. Extra tool/staff dependencies are currently
rejected by this desk configuration rather than silently bypassed.

IssueReplacement reserves the printer's actual capacity and one local physical
blank stack. Its material and workstation claims participate in existing resource
arbitration. Completion rechecks the session, service, authorization, capacity and
stock. Successful completion consumes one blank, records both recipe transactions,
creates one unique card at the printer's ServiceOutput, and registers a new
holder-bound credential with the next generation. Only then is the case marked
Issued. No card is teleported to inventory. Ordinary PickUp and a separate normal
Login are necessary before ordinary inbox access.

The content/credential-recovery.json recipe uses D-37's 600-tick duration; the
identity check retains D-36's 300-tick default. Fixture overrides use shorter blocks
only where declared explicitly. Stock shortages, missing printer service, capacity
conflicts, missing authorization and knowledge-capacity limits produce typed action
failures. Cancellation, logout, expiry or outage consumes no blank and creates no
card; resource claims are released through the normal action lifecycle.

Issuance retries take a finite service operation and return the original card
reference without consuming more stock. Later loss reports for the same old card
return a captured issuance receipt in their replies. Those receipts preserve their
original observation time and cannot overwrite newer pickup or revocation evidence.
Repeated losses of successive cards create separate generation-linked cases.

## Persistence and runner support

Snapshot 36 adds the recovery reply payload, desk mappings, case registry and case
counter. Restore checks report/session provenance, case/account ownership, read
authorization, captured mailbox scope, output identity/generation, registry records,
conserved recipe transactions, printer/material claims and finite timers. Older
binary schemas are intentionally incompatible.

Runner supports PostLostCard, ReadRecoveryInbox and IssueReplacement commands,
recovery-cases and recovery-mailbox inspection, exported credentialRecoveryCases,
CredentialRecoveryCases assertions (Authorized/Issued, count, optional actor), and
credentialRecoveryCases/replacementCardsIssued metrics. The commands are physical
primitives; this increment does not claim that scripted chains pass T18.

## Verification

Release build: zero warnings/errors. Full suite: **651 passed, zero failed,
zero skipped**, 23 seconds. Evidence: artifacts/test-results/h3-recovery-protocol.trx.
Twenty-seven new test cases cover delivery/read separation, account isolation,
ordinary-message forgery rejection, finite timing, actual output/pickup, old-card
revocation, duplicate requests/issuance, absent stock/service, competing claims,
all interruption paths, bounded knowledge, stale receipts and repeated generations.
Twenty checkpoints cover pending reports, delivery, read, active issuance, output,
pickup and normal login/read, with identical continuation state and trace. Additional
corruption tests reject invalid case/credential references, missing claims, changed
stock bindings/timers and a captured reply belonging to another account.

scenarios/recovery_protocol.json is a scripted service test with 17 passing
assertions. Identity verification runs 0–300; the report posts at 301, is accepted
at 303, and its reply arrives at 305. Explicit reading completes at 306. Issuance
runs 306–906, consuming one of two blanks and creating card 102 at the output.
Pickup completes at 907, normal login at 908, and ordinary inbox reading at 909.
Seven actions complete, none fail; the old card remains on its original tile and
the authority's belief store remains unchanged.

artifacts/recovery-protocol and artifacts/recovery-protocol-replay match:

- Build: e67fc03343d3b6eb3a58d02cf366c761014b144cce93c8e060a2d6890c5e3147
- Content: d24a80f230a6cfeac4f339f7b09812686b15d66b4282e9b5e5d8ce7dd411e987
- State: d71c36a07d3aa9eb30d23e0773913accb36768f1190b55a01489f704b1b33c8d
- Detailed trace SHA-256: 9EE41BA2DEFDA2612A187253EBD51AA9D5BD4815A68B672055E811BDEBCEBD27

## Remaining release work

Next: modeled CardLoss, local discovery at check-in/credential-required actions,
RestoreUsableCredential at the specified priority, costed known-card retrieval
versus replacement, and normal-login/current-order-read gating before retained work
resumes. Then verify the unscripted T18 chain and negative controls. Broader pending
release requirements include authority projects/construction, other work domains,
institutional recovery, full daily-life/population/performance gates, presentation,
and the requirement-by-requirement release audit. Shutdown remains pending until
the entire first release is implemented and verified.
