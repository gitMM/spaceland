# Case filing and personnel service — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements canonical cases and exactly-once reprimand acceptance
(R-WF-34..35), the time-window portion of R-WF-36, and the service-reply/read portion
of R-WF-37. It does not complete T20, H3 or the first release. The system remains
standalone, outside Unity assets and without a spaceland runtime dependency.

## Implemented

With personnel configured, an attributable completed audit queues a FileCase report.
The supervisor uses ordinary GOAP terminal travel/login/post actions. Its submission
references its authenticated inspection report, both queried record-kind versions and
the completed query actions that disclosed the relevant revisions.

Personnel validates requester role and transport, accepted verification-report
provenance, published evidence types, assigned-desk query receipts, complete relevant
history and the accountability policy. Relevant amendments after either cutoff require
review. No current physical machine, private audit state or developer trace is queried
to make this decision. Authenticated inspection reports remain claims under policy.

The canonical key is (job, attempt, target slot, violation); the machine ID identifies
its one cartridge slot. New report IDs and new filings reuse an accepted case.
Conflicting operation-key retries are rejected. A new accepted case appends exactly
one reprimand and scoped Case/Reprimand records. QueryDisclosure records preserve
service provenance independently of citizen memory and trace logging.

The service posts an ordinary delayed CaseDecision reply with the case and qualifying
count. Only a completed authenticated mailbox read updates private filing state.
Current counts include accepted reprimands in the inclusive [now-window, now] interval;
default window is 6048000 ticks. Historical reprimands are retained after expiry.
The default detention threshold of two is configured and retained, but requests,
approval and custody are not implemented by this increment. Excluding reprimands
accepted before a completed detention remains part of the future custody lifecycle.

## Verification evidence

Release compilation: zero warnings/errors. Full suite: **331 passed, 0 failed,
0 skipped**. TRX: `artifacts/test-results/h3-personnel.trx`.

Twenty new tests cover filing/post/receipt/reply/read separation, offline readers,
operation and canonical idempotency, new verification-report IDs, distinct slot cases,
permission denial, forged query references, omitted history, invalid source reports,
versions/policy/accounts, informal accusations and forged authority replies, stale
amendments, acceptance despite later physical repair/failure, inclusive count boundaries,
snapshot corruption and later amendments preserving historical accepted cases.

Generated verification includes 10 seeds/query durations at 14 checkpoints (140
resumes), exact byte and trace continuation, and chunk/getter neutrality with concurrent
execution at 2/4/8 workers across 12 seeds and two window settings.

Canonical `personnel_case.json`: ten assertions; 49 completed actions, zero failures.

| Tick | State/evidence |
| --- | --- |
| 65 | Audit finding queues FileCase |
| 66 | Finite post completes; no reprimand yet |
| 67 | Personnel accepts case 1 and appends one reprimand |
| 68 | CaseDecision reaches the mailbox; private filing still Posted |
| 78 | Supervisor reads Accepted/count 1 |

Canonical `personnel_two_cases.json`: four assertions; 129 completed actions, zero
failures. Distinct slots create cases at ticks 102 and 166 for account 1. The supervisor
reads count 1 at tick 111 and count 2 at tick 177. No guard team or custody is created.
Wrong installations are declared worker inputs through shared actions; supervisors
perform inspection, audit, filing and reading autonomously.

Both runs and strict replays complete with identical corresponding digests:

| Identity | Value |
| --- | --- |
| Build | `0bcee00a96b6a11427a358f14dac1901d118019bcd6da62d75816a59edafd503` |
| Catalog | `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38` |
| One-case digest | `81c9af8cc1ba569a4e8ef00e9e4e7f100d1219b16a3f093a12794fea9361c404` |
| Two-case digest | `5fccf608fd7690f429f003a4ceeba85e7cb1b65a58ea5c63f6ff90d61d948a14` |

Artifacts are under `artifacts/personnel_case/`, `artifacts/personnel_case-replay/`,
`artifacts/personnel_two_cases/` and `artifacts/personnel_two_cases-replay/`.
Binary snapshot schema is now 19. Older binary checkpoints are intentionally rejected.

## Remaining release work

Next: supervisor RequestDetention after reading a qualifying count, separate authority
validation/approval/wait/rejection, and unique guard-team orders. Armed team formation,
known-location search, coupled escort, finite intake/slot occupancy, sentence/release,
completed-detention count exclusion and current-order reading after release remain.

General failure/sensor verification triggers, authority projects/capacity, credential
recovery, full daily life, other work domains, population/tuning, specialized viewer
presentation and the full H1-H4 acceptance audit are also outstanding. The goal remains
active. The user-authorized shutdown remains pending full release completion and
verification; this increment does not satisfy that condition.
