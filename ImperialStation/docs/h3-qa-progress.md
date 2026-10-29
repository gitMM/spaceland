# Supervisor QA verification — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements sampled repair verification (R-WF-04) and the local
inspection/evidence portion of R-WF-31..32. It does not complete T20, H3 or the release.
The system remains isolated inside ImperialStation with no Unity runtime dependency.

## Implemented

The first accepted repair-completion receipt per attempt draws once from the qa stream.
The configured sample rate defaults to 250 permille; delay defaults to 18000 ticks.
Selected checks retain their source receipt and wait until due. Known role, duty,
commitment and origin determine eligible supervisors; the source worker is excluded.
No eligible supervisor leaves a pending check, with known-boundary/update wakeups.

Verification is a distinct typed work order. Ordinary terminal delivery/read,
acknowledgment, bounded GOAP navigation, finite inspection, local result capture and
terminal reporting apply. Existing suspension, cancellation, expiry, expectations and
overdue replacement operate on verification attempts. A supervisor never repairs the
machine as part of this order. A clear or discrepant result remains a received claim.

Inspection captures required/installed part types and status with the observation
clock. It does not identify the installer. A discrepant local order retains
ResolveJobDiscrepancy and captured evidence for the next permitted-record audit.

## Verification evidence

Release build: zero warnings/errors. Full suite: **291 passed, 0 failed, 0 skipped**.
TRX: `artifacts/test-results/h3-qa.trx`.

Fifteen new cases cover receipt/due/read/inspection/result separation; fitting wrong
installation and false completion through shared actions; identical sampling/dispatch
under different hidden machine truth; one sample across repeated report IDs; default,
zero, full and intermediate rates against the independent RNG; disabled QA and physical
completion without receipt; unread orders; off-duty waiting; captured evidence surviving
a later fault; cancelled inspection/resume; lease expiry; bounded planner behavior;
cleanup exclusion; overdue supervisor replacement and terminal recovery; malformed
snapshot receipt/roll/due rejection; and deterministic continuation.
Generated coverage includes 20 worlds at 14 checkpoints (280 resumes), byte/trace
agreement, getter/chunk neutrality and concurrency 2/4/8. Sampling additionally spans
20 seeds at rates 0, 250 and 1000.

`scenarios/quality_verification.json` starts with a live machine failure and uses no
scripted actors. All 12 assertions pass. Repair result posts at 117 and arrives 118;
QA is due 123 and dispatched 124. Supervisor reads 134, posts acceptance 136, inspects
139, posts result 143 and authority receives it 144. At horizon 300: one selected and
clear QA check, two work attempts, two satisfied expectations, 52 completed actions,
zero failed actions, and no overdue incidents in this baseline.

Run/replay digest:
`7282066c1a3d6a72f42757bf17c164541edba2b220c3085ea8aef6578731b3f2`.
Build: `4bba6476a75e9ec514d706c0608797ac978cac63982e5ad6eee6b20bc3d586ab`.
Catalog: `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38`.
Artifacts: `artifacts/quality_verification/` and `artifacts/quality_verification-replay/`.

Binary schema 17 persists QA policy, sampling/receipt evidence, due/review timers,
verification assignments/results, typed work payloads and private inspection captures.
Runner adds policy configuration, QA assertions, inspect views and metrics.

## Remaining work

Next: ResolveJobDiscrepancy must drive travel to the assigned Records Terminal,
authenticated scoped queries and evidence linking to a responsible account, followed
by idempotent cases/reprimands and separate detention request/approval. Legitimate
failure reports and authorized sensors also need general verification-need handling;
this increment implements the sampled QA trigger. NeedsAttention retains blocked or
rejected verification; broad institutional recovery policy remains unfinished.

Full release still needs capacity/project GOAP, construction, remaining work/recipes,
staffing/berths, credential recovery, guards/equipment/custody/escort/intake/release,
full daily life, generalized patrol follow-up, population/tuning benchmarks, viewer
presentation and complete requirement/gate verification. Shutdown remains authorized
only after the entire release is complete and verified. It has not been scheduled.
