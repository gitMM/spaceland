# Discrepancy records audit — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment connects retained R-WF-32 inspection discrepancies to the permitted
Records Terminal audit and conservative accountability assessment in R-WF-33.
It does not complete T20, H3 or the first release. The standalone solution still has
no Unity or spaceland dependency.

## Implemented

A supervisor's completed discrepant verification opens ResolveJobDiscrepancy once for
that verification attempt. The assigned desk must explicitly grant both work-record
kinds. Ordinary bounded GOAP travel/login/query actions, authentication, session loss,
query cancellation, knowledge capacity, check-ins and known duty all apply.

The audit scans immutable history one row per finite query, freezes a captured cutoff
for each record kind, and retains only complete actually disclosed relevant rows.
Declared required-part specification is included in WorkAttempt records. No physical
installer identity, installed-part truth, first-read marker or execution status is
added to the institutional records. Page/evidence bounds are 64/32 respectively.

The pure Mind policy requires one repair assignment, matching specification, a valid
accepted acknowledgment and successful completion before inspection. Missing records,
incomplete disclosures, conflicting amendments or ambiguous handoffs yield NeedsReview.
Missing parts and faults with the correct installed type also remain inconclusive.
A positive finding identifies assigned-account responsibility and retains job,
attempt, machine, account, evidence versions and both queried version cutoffs.

The current policy conservatively treats multiple repair assignments in slot history
as ambiguous. It does not yet narrow historical handoffs using additional evidence.
The two record-kind scans are explicitly versioned, not one atomic transaction.
Later personnel-service validation must account for changes after either cutoff.

## Verification evidence

Release build: zero warnings/errors. Full suite: **311 passed, 0 failed, 0 skipped**.
TRX: `artifacts/test-results/h3-audit.trx`.

Twenty new cases cover finite assigned-desk disclosure, role/grant completeness,
separate desks, unavailable assigned desk/recovery, no audit for a correct installation,
captured inspection surviving later wear, incomplete knowledge disclosure, cancelled
queries, retained off-duty goals, history/evidence bounds, missing/conflicting record
policy inputs and forged snapshot owners, desks, phases and evidence accounts.
Generated tests compare exact bytes and trace continuation at 15 checkpoints for
12 seeds/query durations/delays (180 resumes), plus chunk/getter neutrality and
concurrent execution at 2/4/8 workers across 16 seeds.

The canonical `scenarios/discrepancy_audit.json` has nine passing assertions.
The worker uses shared physical and terminal actions to install a fitting wrong
cartridge and submit a false completion. The supervisor is autonomous throughout:

| Tick | Evidence/action |
| --- | --- |
| 7 | Completion received; QA selects verification |
| 29 | Finite local inspection captures wrong installed type; audit opens |
| 34..37 | First finite Records Terminal query |
| 37..52 | Six WorkAttempt history pages, captured cutoff 27 |
| 55..65 | Four WorkReport pages, captured cutoff 26, including a check-in |
| 65 | ReadyToFile/WrongInstalledPart for job 1, attempt 1, machine 1, account 1 |

The run completes 48 actions with zero action failures. It retains five relevant
record revisions and ten completed page receipts, with evidence query action IDs.
No reprimand or detention side effect occurs.

Run and strict replay both complete with identical digest:

| Identity | Value |
| --- | --- |
| Build | `84128e0363a705e2fb6142298ce351d6d9a9f80684e012c33789533f781bbe11` |
| Catalog | `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38` |
| Digest | `715bf775674cf79faa48bc37b1fb62a5fa24fe3e6e8240d585266b75079f04de` |

Artifacts: `artifacts/discrepancy_audit/` and `artifacts/discrepancy_audit-replay/`.
Binary snapshot schema is 18; earlier binary checkpoints are rejected.

## Remaining release work

Next: canonical case identity, ordinary case posting, personnel-service permission and
provenance validation, exactly-once reprimands, service replies/count reading, detention
requests and separate authority approval. General failure/sensor verification triggers,
authority projects/capacity, credential recovery, guard/custody workflow, full daily
life, other work domains and the complete H1-H4 acceptance audit are also outstanding.
The goal remains active. The user-authorized shutdown remains pending full release
completion and verification, not this increment.
