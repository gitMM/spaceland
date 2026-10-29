# Facility reporting and generated tasks — H3 progress

This increment implements the citizen facility-condition reporting path of R-WF-02.
It does not complete H3 or the first-release goal. Everything remains standalone
inside ImperialStation, outside Assets, with no Unity runtime dependency.

## Implemented behavior

Configured duty roles form private report goals after successful local observations
of dirt at or above threshold or a machine fault. Existing terminal planning provides
travel, credentials, login, finite post and delayed receipt. Recovery observations
reset the local condition transition; repeat observations are bounded by a cooldown
and one pending report per actor/subject. Hidden state and seeded/secondhand knowledge
do not trigger this duty.

Authority validates the registered duty and published target, rejects future-dated
claims, deduplicates sender-account/operation keys and merges active target reports.
It can attach an existing open job or allocate a new monotonic job ID. Dispatch uses
published roles, known rosters, origins and commitments. No physical condition, tool
availability or current position is consulted. The worker receives a normal order,
reads and acknowledges it, performs local work and posts a result.

A received current-attempt completion resolves the task only when its observation
is at least as recent as every received condition report. Older completion prompts
a follow-up attempt. Old adverse observations cannot reopen reported completion;
fresh observations create a new job. Rejected/blocked work retains NeedsAttention
until fresh condition evidence arrives. Pending tasks retry on institutional updates,
known duty boundaries and known commitment expiry. Ordinary dispatch uses no overdue
incident token; existing expectation/incident policy handles accepted overdue work.

## Evidence

Release build: zero warnings/errors. Full suite: **263 passed, 0 failed, 0 skipped**.
TRX: `artifacts/test-results/h3-facility.trx`.

Sixteen new cases cover physical post/receipt separation, hidden and seeded evidence,
unavailable terminals, duplicate/conflicting keys, merged claims and multiple observers,
wrong duties, thresholds, future dates, false dirt claims without physical mutation,
old and fresh observations after completion, off-duty waiting, rejected-work retry,
completion older than a new condition, cooldown reset, machine repair, and deterministic
continuation. Generated coverage includes 20 worlds at 12 checkpoint ticks (240 resumes),
trace equality, diagnostic getter neutrality, and concurrency levels 2/4/8.

`scenarios/facility_cleanup.json` starts with no jobs, no assignments and no scripted
actors. Its 11 assertions pass. Guard observes at tick 0; post completes 5; receipt
creates and assigns a task at 7; worker reads 28, posts acceptance 30; physical cleaning
finishes 39; completion is received 47. At the final horizon 300 there is one completed
task, two removed dirt units, 63 successful actions, zero failed actions and no overdue
incident. The guard's own stale adverse observation remains private and unchanged until
it visits again; authority completion does not update citizen knowledge remotely.

Run and replay both completed, with identical final digest:
`5d6638ebe4a56f6d736564b34c5f80cde2354dfc5ef1c8fe7554fc6eec361e01`.
Build ID: `50628af79b316a4ea8c4705ee049d78c00834482dd1d535fffdb97d60d3c8835`.
Catalog digest: `6c8791f7dbb1e6c9fea51a2cd63fee86477d16c1dede30d28a7abb90e6cb1487`.
Artifacts: `artifacts/facility_cleanup/` and `artifacts/facility_cleanup-replay/`.

## Persistence and remaining scope

Binary schema 15 stores the new job allocator, facility configuration, private report
watches, receipt identity index, authority tasks and dispatch timer. The reader checks
retained report goals/post provenance, task receipt/completion evidence, target and
attempt bindings, ID sequence, and timer consistency. Runner supports facility task
assertions, task/private-observation inspect views, snapshot fields and metrics.

Machine wear/fault generation (R-WF-03) and sampled repair QA (R-WF-04) remain next.
Optional cleanliness sensors are not added. General failure recovery, capacity/project
GOAP, construction, additional work domains/recipes, staffing and berths, credential
recovery, audit/reprimand and detention/custody, full daily life, generalized patrol
expectations/follow-up, population benchmarks and complete release gate audit remain.
The viewer still needs work-specific presentation and visual verification.
