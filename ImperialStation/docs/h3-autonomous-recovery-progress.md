# Autonomous credential recovery — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment connects the physical recovery protocol to private goal selection
and retained work. The core T18 recovery chain now runs without scripted actors.
It does not complete the full release or all T18 priority, contention and causal
verification requirements.

CardLoss moves the real held card to the actor's current committed tile. It does
not disclose its location, revoke it, create a case, send a message or wake the
actor. A later inventory check while preparing a credential-dependent goal, or a
locally failed credential check, retains RestoreUsableCredential. Work orders,
parts and tools remain attached to the worker throughout recovery.

The Mind planner compares known-card retrieval with travel to a published recovery
desk and the finite identity/report/read/issuance/pickup sequence. It uses private
facts, declared service capabilities and travel costs; hidden stock and current
card location do not select a plan. A stale retrieval belief can fail locally and
lead to replacement. Missing capabilities, stock or services and exhausted search
budgets produce explicit reasons and bounded retries. Ordinary useful needs may
run during recovery backoff. Urgent needs and custody retain priority.

Recovery finishes only after a physically held usable card, normal login and an
explicit current-inbox read. Cancellation received in that read prevents repair
resumption. An unread captured message keeps recovery pending. Old physical cards
remain where they were lost, and replacement consumes one real blank. Work access
can now declare requiresCredential, enforced at both start and completion; failure
releases claims without consuming a repair part.

Snapshot 37 persists private recovery progress, retries, planned/active actions and
timers, and credential requirements on work services. Restore checks ownership,
report/read/issuance provenance and active action/timer links. The runner adds
CardLoss, recovery-agents inspection/export, recovery metrics and assertions.

## Verification

Release build: zero warnings/errors. All 678 tests pass, including 19 exact autonomous
recovery cases. These cover five seed/travel/stock combinations, retrieval versus
replacement costs, stale location, cancellation waiting in the normal inbox,
missing prerequisites, budget exhaustion, loss during installation, forged private
progress and 51 checkpoint boundaries with identical continuation state and trace.
The existing identity, protocol, accounting and isolation tests also pass.
TRX: artifacts/test-results/h3-autonomous-recovery.trx.

The new scenarios/generators/RecoveryGenerator.cs adds seven labeled outcome
contracts, each exercised with 20 seeds: replacement, cheaper known retrieval,
local rediscovery en route, no stock, no desk, no alternate capability and valid
card. The 140 cases vary identities/accounts, translated/reflected 3D axes,
HQ/terminal placement, travel and service durations, latency, stock, capacity and
background staff. Each verifies its accepted-work/travel/loss prerequisites and
runs up to five checkpoint continuations. Twenty further replacement seeds agree
between direct traced runs and untraced getter-heavy per-tick advancement at
concurrency 2/4/8. Background clerks exercise identity isolation, not staffed printer
operation or competing recovery demand.

Generated stock-shortage checkpoints exposed a retry timer earlier than a newly
extended backoff. RequestRecoveryDecision now replaces such a timer instead of
retaining it. The generated regression verifies the timer invariant and restores
those checkpoints. A separate route variant verifies that locally rediscovering
the old card can correctly avoid issuance; blocked-stock fixtures explicitly keep
the HQ route away from it.

scenarios/autonomous_recovery.json contains one CardLoss disturbance and no scripted
actor commands. All 13 assertions pass. The card is lost at tick 5 during travel;
the worker discovers it at 10. HQ travel finishes at 13, identity at 16, posting
at 17, recovery reading at 20, issuance at 25 and pickup at 26. Normal login/read
finish at 27/28. Only then does the worker return and install at 30–33, posting the
result at 38. Eighteen actions complete with no failures; one recovery case and
one new card are created, and all resources balance. This fixture explicitly uses
short identity/issuance timings; the content defaults remain 300/600 ticks.

artifacts/autonomous-recovery and artifacts/autonomous-recovery-replay match:

- Build: 9869063444cddc1f39b818086bb743952459625eaedf02d42f1b110477ed98ac
- Content: f102a5dc4e50c079942ebf3dbacea2f256338e02040933ffe3571c098f75a1ea
- State: a6b689ab8ddf1f95bf3f37857cc6e50539f0f6136078092eaba8031092199f3e
- Detailed trace SHA-256: C7346C9F897691EF5A798401C93226DA3158667C03AE1B245B325330770FF33F

## Remaining release work

T18 still needs the remaining contention/metamorphic matrix and response-chain
causal audit. Broader work includes complete priority integration, authority
projects/construction, other work domains, institutional recovery, daily-life and
population/performance gates, presentation and the full requirement audit. The goal
remains active. Computer shutdown remains pending until the whole first release is
implemented and verified.
