# Physical construction — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements the physical construction lifecycle needed by T11 and
R-TIL-05/R-AUT-08. Authority GOAP, placement/commitment, construction orders, worker
planning and report-driven capacity reconciliation remain to be connected. The
runnable fixture explicitly scripts the worker and seeds a project reservation;
it is not evidence that the autonomous T11 gate is complete.

## Reservation, finite work and physical output

A tile definition can declare a construction recipe, work access and berth outputs.
The recipe consumes declared inputs and has no item outputs: its physical output
is the tile and its declared services. content/construction.json supplies D-35's
four material units, 18,000 work ticks and eight ordinary housing berths.

A project reservation identifies an empty adjacent coordinate, an unused future
tile ID and a buildable definition. It does not insert a tile into Topology and
does not add capacity. Construct identifies the project through serviceId and a
completed standing tile through target. A qualified worker must stand on an
orthogonal completed neighbor and physically hold all materials and any required
tool. Registered credential cards cannot become consumable construction inputs.

Only after all checks pass does the action atomically claim the site, input stacks,
tool and any declared staff. Work takes the actual recipe duration. It contributes
the Working needs rate. Cancellation, critical needs and failed completion checks
release claims, return the project to Reserved, and preserve materials; restarting
requires a full finite action. Two builders cannot consume one project's inputs
twice or create duplicate output. Credential requirements are checked at start and
completion, while save/restore preserves a valid ongoing action after a mid-work
card loss so that the completion check can fail normally.

Successful completion consumes the held inputs through the existing recipe ledger,
adds one connected tile, increments topology version and creates eight individually
identified ordinary Bed services for the default habitat. The new tile can then
be traversed. The beds are real capacity: ordinary finite allocation and sleeping
work on them, with the usual claims and occupancy rules. Physical beds can exist
without a needs simulation; they do not create active need behavior by themselves.

Construction teaches the builder the completed tile and service locations. Other
citizens learn those new services through local observation on arrival. Dynamic
bed IDs are excluded from their view until known; neither the initial public layout
nor authority beliefs changes on physical completion. A coordinate index supports
local service discovery without scanning all projects on every observation.

## Persistence and runner support

Snapshot 39 adds definition construction metadata, reserved/building/completed
project state, construction-site claims, output tile/action/timing references and
individual berth IDs. Restore checks unique sites/outputs, conserved recipe inputs,
active material/site/tool/staff claims, finite timers, topology and actual bed
definitions. Mutable credential access is rechecked at completion rather than
mistakenly making an interrupted-access checkpoint invalid. Derived site and berth
indexes are rebuilt on restore. Older snapshots require their original build.

The runner loads construction definitions and initial project reservations, supports
Construct through the ordinary action API, exports constructionProjects and offers
inspect --view construction. Assertions add Construction, TileCompleted and
HousingBerths; metrics add constructionProjects and constructionCompleted. No
construction-specific message can currently create a project or update its capacity
belief; that authority integration is the next feature layer.

## Verification

Release compilation succeeds without warnings or errors. All 720 tests pass in
the full suite (25 seconds). TRX: artifacts/test-results/h3-physical-construction.trx.

The construction suite has 21 cases covering finite timing, material accounting,
locality, roles/tools, recipe binding, cancellation/retry, critical needs, competing
builders, mid-action credential loss, local service discovery, usable constructed
beds, corrupt snapshots and strict input rejection. Eighteen checkpoint boundaries
with and without needs preserve canonical state and remaining trace.

scenarios/physical_construction.json has twelve citizens, eight initially usable
berths and ordinary needs enabled with default thresholds. It seeds one project
and scripts physical pickup, travel, construction and travel onto the completed tile.
All 13 assertions pass. Pickup takes ticks 0–10, travel 10–11, and construction
11–18,011. Exactly four materials are consumed at 18,011; actual usable housing
increases from eight to sixteen at that tick. The worker starts walking onto the
new tile at 18,012 and arrives at 18,033. Four actions complete, none fail. Authority
beliefs stay unchanged throughout.

artifacts/physical-construction and artifacts/physical-construction-replay match:

- Build: f9aed00f7c6f47082db4357715278113f24f3a5e4971405e7dee5f2c56efcf32
- Content: a4a842fa95e1798ec777670f934bd91f8f6f8e3a517e855147885ce7aaf60d1e
- State: 55e81a14861df893693ed4821242d76c2c5a26adcc32f0f146ba69cbe6291473
- Detailed trace SHA-256: 8EC9E39CD54501F77C00953886A687D5D02B40A61E5A05A429A080FE8B8BB3FE

## Remaining release work

Next is authority capacity planning with identified believed/committed capacity,
bounded placement, project/material commitments, ordinary posted/read/acknowledged
construction orders, worker collection/travel/construction planning, outcome reports
and idempotent reconciliation. Complete the autonomous T11 fixture and generators,
vacant-capacity and false-report controls, and update viewer construction presentation.
Broader authority/work/priority/institution/population gates and the complete release
audit remain active. Shutdown remains pending until the entire first release is
implemented and verified.
