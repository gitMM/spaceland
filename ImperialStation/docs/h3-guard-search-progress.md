# Local guard formation and target search — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements local team formation and finite target identification in
R-WF-38. It does not implement custody or coupled escort movement, complete T20/H3,
or complete the first release. The system remains independent of spaceland and Unity.

## Implemented

After equipping and reaching the copied rendezvous, each guard plans and executes
its own finite JoinGuardTeam action. The action uses the configured local inspection
duration (100 ticks by default). Physical validation requires that guard's own read,
acknowledged, unexpired order, correct held/equipped resource and actual rendezvous
presence. An unread guard cannot join or be pulled into another guard's action.

Each completed join enters a local cooperation record. Until all required guards
join, each waits locally. Departure or equipment loss withdraws an unformed join.
When the last join completes, formation requires every named participant to remain
co-located, equipped and holding matching valid copies. The completing local
handshake informs those participants. It does not update the authority's reported
TeamForming state. Formation trace events reference the completing join as their
cause instead of attributing another guard's action ID to each participant.

Formed guards follow the bounded search candidates in their copied orders, using
ordinary individual movement and finite InspectActor actions. Search planning uses
only their actor views and the copied locations/identity. A prior location belief
cannot replace inspection. The completion-time observation distinguishes the named
target from bystanders and from an actor still in transit. A missing target produces
absence at that tile, followed by the next known candidate. Exhaustion leaves an
explicit private TargetNotLocated state; it never discovers a remote position.

Successful observations retain their action, tile and time. TargetLocated means a
captured local identification, not a live tracker or custody. Later target movement
does not rewrite the observation. The target remains free to move. Equipment loss
invalidates readiness and, after reacquisition, requires a new search observation.
The historical local formation record remains distinct from current guard activity.

Offline guards continue using their copies. Cancellation cannot complete a join.
Expiry withdraws unformed joins and stops new protocol progress. An already-running
finite inspection can finish as ordinary local knowledge, but cannot advance an
expired order. Previously formed teams remain historical evidence after expiry.

Snapshot schema **23** retains pending joins, completed join proofs, local formation
time, per-guard candidate cursor and bounded observations. Restore cross-checks
membership against individual joins, read copies, times, roster, local progress and
active action duration/target. Older binary checkpoints are intentionally rejected;
JSON envelopes remain schema 1.

Runner adds `inspect --view guard-teams`, LocalGuardTeam assertions, formation/search
metrics and snapshot projection data. The focused guard_preparation fixture now ends
at tick 300, before joins finish; guard_search extends the sequence through search.
Earlier progress reports/artifacts preserve their original build and embedded input.

## Verification evidence

Release compilation: zero warnings/errors. Full suite: **410 passed, 0 failed,
0 skipped**, including 19 new search cases. TRX:
`artifacts/test-results/h3-guard-search.trx`.

Tests cover finite individual joins; missing equipment and unread partners;
cancellation, departure and re-equipping; expiry during join/inspection; offline
formation/search; stale locations and multiple known candidates; departure during
inspection; bystander identity; prior knowledge versus actual inspection; continued
target freedom; equipment loss after identification; corrupted join proofs;
88 checkpoint continuations; and concurrent/chunked determinism at 2/4/8 workers.

Canonical `scenarios/guard_search.json`: all eleven assertions pass, 186 completed
actions and zero failed actions. Guard 3 joins at tick 356, guard 4 at 359; formation
completes locally at 359. Both travel to the candidate and inspect from 360 to 460.
Both identify the target at 460. The authority still only knows the received
acknowledgments; no custody, intake or detention occupancy exists yet.

Strict run and replay both completed with matching final digest:

| Identity | Value |
| --- | --- |
| Build | `ed6cc83b3f5cd7c7dc6270a033d7912f5c3b7c1e210112b2bff4c0e4f0583bef` |
| Catalog | `317804d50ce4b2862f9f1c3b623bd7d0688af6bc36b1214985938e82879fe85f` |
| Digest | `5b1272e77e4e38cb95757a38393885896362dd71c172ae969e1c0fe27d6813d8` |

Artifacts: `artifacts/guard_search/` and `artifacts/guard_search-replay/`.

## Remaining release work

Next: a physical escort-start action that revalidates current target/guard
co-location, equipment and authentic copies; suspension and custody restrictions;
coupled movement with a deterministic leader and frozen shared edge timing;
interruption/revocation; physical intake service/capacity and receipt; sentence/release;
historical completed-detention count exclusion; and current-order reading before
resuming work. Negative search/blocked-team institutional reports and recovery also
remain. Individual search movement implemented here is not coupled custody movement.

General verification triggers, authority capacity/projects, credential recovery,
staffing/berths, full daily life, remaining work domains, population/tuning, viewer
presentation and the full H1-H4 acceptance audit remain. The full goal stays active.
User-authorized shutdown remains pending full release completion and verification.
