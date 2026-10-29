# Physical equipment and guard rendezvous — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements physical equipment preparation and individual rendezvous
travel in R-WF-38. It does not complete local search, coupled escort, T20, H3, or the
first release. The implementation remains independent of spaceland and Unity.

## Implemented

`Equip` is a shared finite physical action. It requires a real held item, claims that
item for the duration, and records equipped state only on successful completion.
Default duration is D-38's 50 ticks; `detention.escort.equipTicks` configures it and is
copied into the guard order. Credential cards cannot be equipped. Cancellation
releases the claim and does not grant equipment. Equipment remains owned inventory;
there is no material creation or consumption from equipping. Dropping, installing,
fully consuming or merging away the item identity clears equipped state. A stack
with remaining held units retains its equipped identity.

The actor view exposes only that actor's own equipped item, alongside its existing
inventory. Institutional state cannot read this through an acknowledgment. Equipment
has a privileged diagnostic inspection view for testing and presentation.

After reading and posting acknowledgment, each guard uses bounded GOAP preparation
from its own order copy, known item descriptions/locations, inventory, equipment and
published navigation. It selects up to K candidates by estimated cost and ID, then
executes ordinary movement, pickup and Equip actions. Inventory claims and local
validation apply. Stale warehouse knowledge cannot take another actor's item. Missing
equipment or route produces an explained private wait and bounded retries.

Successful preparation requires the configured equipment and physical presence at
the copied rendezvous. Departure or equipment loss invalidates readiness; replacement
equipment requires another finite action. The authority remains at TeamForming until
it receives later protocol evidence. Private readiness does not silently update its
beliefs, start custody, move the target or occupy a detention slot.

Active preparation and waiting at the rendezvous take precedence over routine
check-ins. Guards can therefore act on read copies while terminals are offline.
Report goals retain priority; blocked preparation permits check-ins, and known duty
boundaries still apply. On expiry, preparation movement stops at the next frozen
edge boundary. A finite equip already underway may finish, preserving the physical
item, but an expired order cannot become rendezvous-ready or start another step.

Binary snapshot schema is **22**, preserving equipment identity/action/time, guard
action provenance, planning timers/backoff, readiness and local reason. Restore
validates ownership, protected items, finite equip timing/claims, read-copy ownership,
expiry boundary cancellation, preparation timers and physical rendezvous readiness.
Older binary snapshots are intentionally rejected; JSON envelopes remain schema 1.

Runner adds `inspect --view equipment`, an Equipped assertion (item ID zero means
unequipped), equipment/readiness metrics, and equipment in snapshot projection data.

## Evidence

Release compilation has zero warnings/errors. Full suite: **391 passed, 0 failed,
0 skipped**, including 19 new preparation cases. TRX:
`artifacts/test-results/h3-guard-preparation.trx`.

Tests cover finite completion and cancellation; locality, inventory and card rules;
consumption/merge cleanup; unknown and stale stock; offline copies; missing read-copy
provenance; equipment loss/departure; expiry during equip and frozen travel; corrupted
equipped ownership; supported K bounds and search budget; 120 checkpoint continuations;
and deterministic concurrent/chunked execution at 2/4/8 workers.

Canonical `scenarios/guard_preparation.json` uses `content/escort.json` and real guard
equipment stock at the storage tile. The supervisor and guards are autonomous; the
original wrong installations remain scripted through shared physical actions.
All ten assertions pass: 180 completed actions, zero failed actions. Guard 3 picks up
item 21, equips from tick 204 to 254, and reaches the rendezvous at 256. Guard 4 equips
item 22 from tick 207 to 257 and reaches it at 259. Both remain equipped and present.

Strict run and replay both complete with matching final digest:

| Identity | Value |
| --- | --- |
| Build | `2e81745c300afa3c72480f08b5f727f00a44dbcfb2fe8217765aa7746b454f76` |
| Catalog | `317804d50ce4b2862f9f1c3b623bd7d0688af6bc36b1214985938e82879fe85f` |
| Digest | `f42a97b932b75f1a17cb3296256ad867158e1e00f9737dc3250e532fd8f296e3` |

Artifacts: `artifacts/guard_preparation/` and `artifacts/guard_preparation-replay/`.

## Remaining release work

Next: local team/search protocol and target identification, followed by co-located
escort start, coupled movement, interruption/revocation, physical detention service
and capacity, custody permissions, sentence/release and historical completed-detention
count exclusion. Work must require current-order reading after release. Blocked
teams and missing equipment still need institutional reporting/recovery policy.

General verification triggers, authority capacity/projects, credential recovery,
staffing/berths, full daily life, remaining work domains, population/tuning, viewer
presentation and the full H1-H4 acceptance audit also remain. The goal stays active.
The authorized shutdown remains pending full release completion and verification.
