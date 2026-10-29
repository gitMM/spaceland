# Guard assignment and individual order reading — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements administrative team assignment and the terminal read /
acknowledgment step of R-WF-38. It does not complete physical team formation, T20,
H3, or the first release. The system remains independent of spaceland and Unity.

## Implemented

An optional `initialState.detention.escort` policy declares the destination facility
ID and tile, rendezvous, equipment resource, guard role and required team size
(default two). These are dispatch requirements. The facility declaration does not
create an intake service, capacity, occupancy or a detention claim.

Each new authorization has at most one dispatch. Selection uses published rosters,
registered roles, known commitments, and origins derived from accepted terminal
transport or known rest tiles. It excludes the subject, guards without known duty or
origin, and guards already committed to work, patrol or another escort. Travel
estimates and actor ID determine ordering and the leader. Selection requires the
whole team; insufficient staffing leaves an explained pending dispatch. Published
roster/evidence changes and known expiry/boundary timers can trigger reconsideration.

Orders contain subject account and known actor identity, accepted case IDs, expiry,
sentence, full roster, deterministic leader, rendezvous, declared destination and
equipment, and bounded known search candidates. Candidates come from previous
terminal transport, known assigned work sites and published rest locations. Dispatch
never reads the subject's current position, inventory or action.

Each guard gets a separate ordinary mailbox message. Delivery creates no private
copy. A successful finite authenticated read creates that guard's copy and queues an
ordinary acknowledgment report. Posting and authority receipt remain distinct. The
authority advances from AwaitingReaders to TeamForming only after every guard has
reported acceptance; this means reported readiness to form, not physical co-location.
Private copies stop at EquipmentPending until physical equipment work is implemented.

An authenticated acknowledgment is a claim: the authority validates account, order,
transport and expiry without inspecting private read state. A manually posted claim
can enter institutional records but cannot create a private copy or authorize local
escort action. Denial yields an explained Blocked dispatch. Duplicate receipts do not
add assignments or history; duplicate detention requests reuse the existing team.

Known expiry applies separately to administrative dispatch and each read local copy,
including while terminals are offline. A read after expiry can only report expiry.
An acknowledgment arriving at the expiry tick is too late. Expiration releases known
guard commitments; a fresh authorization may select those guards again.

Snapshots (schema **21**) preserve pending reviews, dispatch payloads, assignment
records, individual reads, queued/posted acknowledgments and local expiry timers.
Restore checks authorization/payload/transport agreement, individual read provenance,
record history, acknowledgment-derived progress and overlapping commitments. Older
binary snapshots are intentionally rejected; JSON envelopes remain schema 1.

Runner additions: `inspect --view escorts`, `guard-assignments`, and `guard-local
--actor ID`; EscortDispatch and LocalGuardOrder assertions; team/read/receipt metrics;
and snapshot projection data. Specialized visual presentation remains pending.

## Verification

Release compilation: **0 warnings, 0 errors**. Full suite: **372 passed, 0 failed,
0 skipped**, including 21 new escort test cases. Result:
`artifacts/test-results/h3-escort-dispatch.trx`.

Coverage includes individual read/post/receipt boundaries, no remote target effects,
missing role/roster/duty, delayed learning of a changed roster, offline reads, expiry
before reading and while offline, deadline receipts, duplicate requests and receipts,
wrong-account and authenticated-claim handling, denied teams, reuse after expiry,
corrupted private awareness, 112 checkpoint continuations, and concurrent/chunked
determinism at 2/4/8 workers.

Canonical `scenarios/escort_order_read.json`: six assertions, 280 completed actions,
zero failed actions. Authorization and dispatch at tick 179; guard 3 reads at 200,
posts at 201, receipt at 202; guard 4 reads at 203, posts at 204, receipt at 205.
The target's two deliberately wrong installations remain scripted shared actions;
supervisor audit/case/request and guard reads/acknowledgments are autonomous.

Strict run and replay both completed with matching final digest:

| Identity | Value |
| --- | --- |
| Build | `3f5c835e4307e135997c8a6898e06724688d06f9ba31bd1ec268a39fe38f18cb` |
| Catalog | `ec5d1e774933d6fbef24fd255352398841ba22c81e600609dfc64e0609f43c38` |
| Digest | `d05fb91784ef40f75f91848ab821c9b702a8e136cafcda322e65993a8872266b` |

Artifacts: `artifacts/escort_order_read/` and `artifacts/escort_order_read-replay/`.

## Remaining release work

Next: real equipment acquisition, local rendezvous/search and identification, then
co-located escort start, coupled movement with persisted membership/frozen edge timing,
interruption/revocation, physical intake service/capacity, custody permissions,
sentence/release and completed-detention count exclusion. Historical count validation
must use completed detention history as of each decision. Current orders must be read
before work resumes. Pending/blocked teams still need explicit recovery policy.

General verification triggers, authority capacity/projects, credential recovery,
staffing/berths, full daily life, other work domains, population/tuning, specialized
viewer presentation, and the full H1-H4 acceptance audit also remain. The goal is
active. User-authorized shutdown remains pending full release completion and
verification; this increment does not satisfy that condition.
