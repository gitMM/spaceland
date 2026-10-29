# Data formats

All files use UTF-8. Scenario and catalog DTOs use camelCase JSON property names and
case-sensitive string enums. Unknown/duplicate fields, explicit nulls, unknown IDs,
fractional coordinates, invalid durations and unsupported versions are errors.
Errors identify the input and JSON path; graph validation also names disconnected
components and representative coordinates.

The scenario root accepts only `initialState`, `disturbances`, `scriptedActors`,
`assertions` and `metrics`. `initialState` requires schemaVersion, id, catalog, seed,
horizon and tiles. Optional configuration uses the documented defaults. Catalog
paths resolve relative to the scenario. A catalog has schemaVersion, id and tile
definitions, with at least one of every specified family.

Disturbances support `TraversalChange` and `TerminalChange`. Commands support `Move`,
`PickUp`, `PutDown`, `Login`, `ReadInbox`, `PostMessage`, `Logout`, `Inspect`, and `Cancel`.
Work-enabled scenarios also support `ProduceRecipe`, `Install`, `FunctionalTest` and
`InspectMachine`, plus `WorkServiceChange` disturbances. Job-enabled scenarios add
`SuspendWork` and `ResumeWork` with a positive `attemptId`. Information-enabled scenarios
support `QueryRecords` with a typed `query` and terminal `serviceId`.
Work-enabled scenarios also support `SplitStack`, `MergeStacks` and `StoreInStash`.
Commands after tick zero require the actor's `scripted` flag. Commands still pass
through the common phase-6 physical executor. Assertions may observe privileged
state but cannot drive actions. No record or knowledge injection section is accepted.

Assertions execute after their tick's full processing: ActorAt, ActorInTransit,
ItemAtTile, ItemHeld, LastFailure, Accounting, KnowledgeValue, KnowledgeAbsent,
BeliefValue, BeliefAbsent and MessageStage. The small fixtures declare positive
one-tick item operations; production defaults remain ten ticks.

`checkpoint.json` has schemaVersion, digest and base64 state. State uses the explicit
`World.Serialize` encoding; restoration validates format/model versions, enum values,
topology, owners, accounting and future event ordering. The envelope checks its digest.
Byte-identical round-tripping is tested, including before tick zero and mid-segment.
The current binary state schema is **42**; the JSON checkpoint envelope remains **1**.
Actor timer indexes, daily-goal indexes and request-local known-route search state
are derived acceleration structures. They do not add checkpoint fields or alter
the canonical queue order. Diagnostic trace retention can be cleared without
resetting event IDs, counters or state.
Older binary checkpoints are rejected;
there is no migration guarantee for unpublished development artifacts.

`snapshot.json` is a viewer projection, **not** the canonical save format. It embeds
the initial checkpoint, completed/uncompleted tiles and actors, start tick and horizon.
`trace.jsonl` contains event IDs/ticks/types, visibility, relevant actor/action/item IDs,
coordinates, segment times and detail. No gameplay system reads this diagnostic trace.
Information events additionally carry cause/message/terminal/session/account IDs and
typed fact keys/values. `snapshot.json` includes initial knowledge, beliefs, messages,
sessions, terminals and report agents. Focused replay writes `chain.json` by following
explicit causes and related action/message events.

`replay.json` embeds scenario/catalog text, their content identity, starting checkpoint,
movement/PRNG versions, assembly-derived build ID, runtime description, horizon and
expected final digest/status/assertions. Restore does not enqueue commands twice.
Replay reproduces a captured failure; failures still return nonzero exit status.

Results include effective configuration, seed, schema/build/content IDs, status,
population/tiles, simulated duration, physical metrics, labeled wall-clock/allocated
memory diagnostics and artifact paths. Performance values are not deterministic;
digests, statuses and gameplay metrics are the batch-isolation comparisons.

## Information and planning initialization

`initialState.information` is optional. It declares finite operation durations, terminal
definitions, actor-bound credentials, initial facts/messages and pending report goals,
plus planner bounds. Actor accounts default to actor IDs and must be unique in this
mode. See `scenarios/false_delivery.json` and `scenarios/autonomous_report.json` for
complete strict-loader examples. `InformationData.cs` defines the accepted JSON fields.

Facts have typed subject/attribute keys, values, provenance and observation/learning
times. `AbsentAt` records a negative observation at a location without disclosing a
remote location. Initial state may seed knowledge; disturbances cannot inject it.
Citizen posts cannot forge authority orders, cancellations or service replies.
The Records capability enables finite scoped queries as described below.

## Work catalog and state

The catalog can declare `resources` (ID, fit class and rating) and `recipes` (ID,
capability, positive duration and unique positive input/output quantities).
`initialState.work` declares physical workstations and machines, their locations,
capacities, roles, required tools and optional staff actor. Automated workstations
omit `staffId`; staffed operations require another idle co-located actor.
All initial items in a work-enabled scenario must reference a declared resource.

`ProduceRecipe` uses `serviceId` for its workstation and `recipeId` for its recipe.
Inputs must be carried; the executor binds eligible stacks deterministically and
reserves them together with the workstation/tool/staff. Outputs receive monotonic
item IDs, appear at `ServiceOutput`, and need ordinary pickup. `Install` uses a
machine `serviceId` and a held single-cartridge `itemId`. Physical fit is checked
independently from suitability. Inspection reveals the installed and required type;
default passive sensing reveals the machine's status, without the specification or installer.

Work scenario commands may reference positive IDs of future produced items. They
are resolved physically at execution, so a nonexistent item fails normally. This
also applies to work fixture assertions. `ItemState`, `ItemAbsent` and
`MachineOperational` assertions cover production and installation. See
`scenarios/work_actions.json` and `content/work.json` for a complete example.

Binary schema 15 serializes work definitions, active material bindings, resource
claims, recipe transactions, disabled services and item/transaction ID counters.
Restore validates claim capacity/ownership and each transaction group against its
recipe. `inspect --view resources` includes items and the transaction ledger.

`initialState.information.repairGoals` accepts `{ "actorId": 1, "machineId": 1 }`
entries; duplicate goals for the same actor/machine are deduplicated. These require
`initialState.work` and its published catalog. They do not grant knowledge of the
machine's required part. Seed private specification facts explicitly or let the
actor perform inspection. `alternative_planning.json` and `planner_costs.json` contain
no scripted actor commands. Work planning uses the existing planner configuration.

The shared immutable public knowledge object now includes a work catalog. Snapshots
preserve this publication independently from actual work definitions, plus repair
goals, retry times, outcomes and temporary pins. `inspect --view repairs` reports
pending goals and explained waiting/no-plan states. Item observations include typed
location kind/owner alongside resource, quantity and last observed location. Failure
observations record local absence without disclosing an item's remote existence.

## Jobs and check-ins

`initialState.jobSystem` requires information and a physical work or cleanliness system.
Repair jobs contain positive `id`/`machineId` entries with default `kind: "RepairMachine"`.
Cleanup jobs use `kind: "CleanTile"`, positive `id`/`tileId` and nonnegative `reportedUnits`
(default 1). The tile must be completed and Cleanable. Machine and tile target fields
are mutually exclusive. `assignments` contains `key`, `jobId`, `actorId`, `expires`;
`checkIns` contains `actorId`, `firstTick` and `interval`. See `work_orders.json` for
an autonomous example with no scripted commands or initial repair goals.

Authority orders carry typed `workOrder` (`kind`, `machineId` or `tileId`, `expires`), `taskId`, `attemptId`
and `operationKey`. Formal reports preserve these identities and the related order
message. Dispatch/reassignment and cancellation keys survive snapshots. Report keys
are scoped to authenticated account; identical repeats cannot duplicate mutations.

`JobPhysical`, `JobAuthority`, `AttemptExecution` and `LocalOrder` assertions use `state`
and their relevant job/attempt/actor IDs. Binary state includes administrative attempts,
private order copies, history, retry/backoff state, bound active actions, retained repair
goals and own-clock check-ins. Trace events include task/attempt IDs. Privileged inspect
views do not grant gameplay access to physical execution or mailbox read markers.

## Scoped record queries

`information.configuration.queryTicks` defaults to 150 (D-14). A `QueryRecords`
command supplies `query: { "kind": "WorkAttempt", "entityId": 1 }`. Kinds are
`Credential`, `TerminalSession`, `WorkAttempt`, `WorkReport`. Optional `limit` defaults
to 16 and must be 1..32; `after` defaults to zero. `history: false` pages current rows
by entity ID; `history: true` pages revisions by version. `entityId: 0` selects all
permitted records of that kind. A completed operation exposes the cursor/version in
`IActorView.LastRecordQuery`; failed or cancelled operations preserve the previous receipt.

All queries require a normal session at a terminal with Records capability. Default
scope is the authenticated account's own records. `information.recordAccess` explicitly
grants cross-account work audit with `{ "actorId": 2, "terminalId": 2,
"requiredRole": "supervisor", "kind": "WorkAttempt" }`. Only WorkAttempt/WorkReport
may use these grants. The grant must match an existing actor, role and Records Terminal.
Using another terminal does not inherit the audit grant. Query input cannot override scope.

Rows carry kind, entity ID, account, immutable revision/version, tick and typed fields.
Learning uses `SubjectKind.Record` with the revision as subject ID and
`EvidenceKind.RecordQuery` with the action ID as source. Metadata attributes include
RecordType, RecordEntityId, RecordVersion and AccountId. Row facts obey normal capacity
and eviction; a cursor receipt is not a retained copy of the rows. Audit results include
administrative assignments and received submissions, never physical execution status or
private read markers. The captured page remains unchanged if records change mid-query.

`KnownRecordValue` / `UnknownRecordValue` assertions take `actorId`, `recordKind`,
`recordEntityId`, `recordAttribute` and `value`; they check retained private facts from
any revision, not a privileged record lookup. `record_audit.json` demonstrates this.
Binary state includes the ledger, grant indexes, immutable pending pages, query payloads
and one last receipt per actor. `inspect --view record-history` is privileged diagnostics;
`inspect --view query --actor ID` displays that actor's last completed receipt.

## Resource flows and stacks

A recipe may declare empty `inputs` (source) or empty `outputs` (sink), but not both.
Its capability, workstation access/capacity and finite duration remain mandatory.
Signed recipe transactions reconcile physical totals against initialization plus
declared sources/sinks/transformations. Source outputs start at ServiceOutput and
require pickup. Sinks consume held inputs only after successful completion.

`SplitStack` uses `itemId` and positive `quantity` strictly smaller than that stack.
The selected amount becomes a new stack at the same location; the original ID keeps
the remainder. `MergeStacks` uses `itemId` for the retained stack and `otherItemId`
for the retired source. Both must be locally accessible, have the same resource and
occupy the same physical container/inventory. A held stack cannot absorb a ground
stack without pickup. Registered credential cards are protected from split/merge.
All these operations use `configuration.itemActionTicks` and ordinary atomic claims.

`initialState.work.stashes` declares `{ "id": 1, "projectId": 42, "position":
{ "x": 1, "y": 0, "z": 0 } }`. Stash locations must be completed tiles. The project
ID is an explicit association tag; operational project records arrive in H3.
`StoreInStash` uses `itemId` and stash `serviceId`; it deposits a whole held stack.
Use normal `PickUp` for withdrawal. Seeded `ProjectStash` item owners must reference
an existing stash. No deposit/withdrawal is performed by an administrative record.

Binary state includes stash definitions, active/scheduled stack intent parameters,
claims and the identified split/merge ledger. `inspect --view stacks` exposes stash
definitions and that ledger. Stack trace events carry source `itemId`, destination
`otherItemId` and `quantity`. Result metrics include `stackChanges` and `projectStashes`.
`resource_flow.json` demonstrates the full source/merge/split/haul/stash/pickup/sink
sequence; `stacked_order.json` demonstrates autonomous order-driven repair from a stash.

## Perception and wired sensors

The catalog accepts `perception` overrides, each with required `subject`, `attribute`
and `mode` (`Passive` or `InspectionOnly`). Supported local fact types are Tile
Location/TraversalTicks/Dirt, Item Location/Resource/Quantity/Present/LocationKind/LocationOwner,
Machine Location/Operational/RequiredResource/InstalledResource, and Actor Location.
Defaults are Passive except the two machine specification fields and Actor Location,
which are InspectionOnly. The resolved catalog contains fourteen fact types.
Duplicate or unsupported fact types are rejected, including in scenarios without
information systems. The resolved complete catalog is stored in every checkpoint.

Passive facts are learned at scheduled initial/arrival sensing. InspectionOnly facts
require finite `Inspect`, `InspectMachine`, or `InspectTile` completion; own action
effects and failures can still teach action-specific facts. `InspectTile` uses the
current tile and information `inspectTicks`; its target must match physical presence.
Rich item descriptions currently require work-enabled content. No getter observes.

`initialState.information.sensors` defines registered machine-status sensors. Required
fields are positive `id`, real `machineId`, nonnegative `firstTick`, and positive
`interval`. Optional `latency` defaults to 2 and must be positive; `enabled` defaults
to true; the only supported `channel` is `Authority`. The measured scope is exactly
the selected machine's boolean Operational fact. Sensors cannot read installed parts,
required specifications, inventories, people or private knowledge.

Sampling runs in phase 3, after physical completion, at `firstTick + n * interval`.
Immutable readings arrive in phase 4 after latency. A `SensorChange` disturbance
requires `tick`, `sensorId`, and `enabled`; phase 1 disables that tick's sample.
Re-enabling resumes the original schedule at or after its tick. Disabling prevents
new measurements but does not retract an already captured packet.

Authority beliefs retain `source: Sensor`, `sensorId`, `readingId`, observed/received
ticks and contradiction history. Human claims retain `source: Reported` and their
message ID. Latest delivery wins, including scheduler sequence ordering for ties;
delivery never rechecks physical truth. Citizens receive no automatic knowledge or
mailbox message from this channel. Authenticated human messages cannot set source.

`inspect --view sensors` exposes configuration and captured readings;
`--view perception` exposes resolved rules. Trace events `SensorObserved`,
`SensorDelivered`, `SensorStateChanged` and `BeliefChanged` carry sensor/reading IDs.
Metrics include `sensorReadings` and `sensorDeliveries`. Binary schema 15 includes
all schedules, toggles, in-flight payloads, provenance and belief history, and validates
scope and pending events. Failed-world checkpoints remain inspectable but cannot run.
See `sensor_status.json` and `content/sensors.json` for a complete executable example.

## Station day and roster routines

Optional `initialState.routines` requires the information system. Its `configuration`
defaults to `dayTicks: 864000`, `cohorts: 3`, `dutyTicks: 288000`,
`checkInWindow: 18000`, and `periodicCheckInTicks: 36000`. Cohorts are zero-based;
their start offsets are `cohort * dayTicks / cohorts`. Day length must divide evenly
by cohort count. Duty length is positive and at most one cohort spacing, with a real
off-duty interval; check-in window is positive and no longer than duty. The immutable
roster's offset is drawn in `[0, checkInWindow)` from the actor's own RNG stream.

`assignments` entries require `actorId` and `cohort`. An optional `restTile` must be
in the public layout. `initiallyKnown` defaults to true. Known initial schedules are
seeded with pinned version evidence; unknown initial schedules are published at tick
zero and must be learned by a real terminal read. Actors without a routine assignment
retain the earlier behavior. Cohort assignments are explicit content in this increment;
automatic role/facility/staffing-based template assignment remains outstanding.

A `RosterChange` disturbance requires `tick`, nonempty `operationKey`, and a `roster`
containing an existing routine `actorId`, `cohort` and optional `restTile`. It schedules
institutional publication and a delayed `RosterUpdated` message. `initiallyKnown` has
no effect on a change. The operation key is idempotent; conflicting reuse is rejected.
Older scheduled versions superseded before publication produce a trace but cannot
overwrite current records. The payload is generated by the institutional service;
ordinary citizen messages cannot forge it or the reserved RosterVersion attribute.

Roster rows use `RecordKind.Roster`, actor entity ID and authenticated account scope.
Each row has version, cohort, day/duty timing, check-in offset and optional rest tile.
Queries expose evidence through the existing finite record protocol. Only roster
message reading adopts a new routine; a developer getter or record query does not.

`IActorView.KnownRoster` contains the last read immutable schedule. Published revisions
and mailbox delivery never replace it. A read captures message IDs at start; cancellation,
terminal failure or credential revocation prevents adoption. Stale read versions cannot
roll back an already known version. Periodic check-ins run on duty; a shift-start visit
is due at the assigned offset, and after-duty reports/check-in precede rest travel.

Moving actors request ordinary cancellation at shift end and stop at the next arrival.
Finite local work can finish. Retained repair goals cannot begin new steps off duty,
but can recognize a known completed result. `OffDuty` is a placeholder held at a
known rest tile, with no sleep or need relief. `MissingRestDestination`, `NoKnownRestRoute`
and `RestPlanBudgetExhausted` explain unavailable rest. Berths are not synthesized.

Results include `routineConfiguration` and `rosterVersions`/`offDutyAtRest` metrics.
`inspect --view routines` shows private routine states and due ticks; `--view rosters`
shows publication history. Traces include `RosterPublished`, `RosterRead`,
`RosterPublicationSuperseded`, `DutyBoundary`, and `RoutineActivityChanged`.
Schema 15 includes roster payloads in messages, history, RNG continuation and routine
watchdogs, and stores check-in state even without a job system. `roster_routines.json`
runs six autonomous citizens over 130 ticks with an explicitly shortened 60-tick day.

## Authority report expectations

Optional `initialState.authority` enables report tracking and requires jobs, information
and a published work catalog. Its integer settings and defaults are:

```json
{"slackMinimum":1500,"slackMargin":250,"slackMaximum":4000,
 "graceTicks":3000,"emaNumerator":1,"emaDenominator":8,"unacknowledgedTicks":36000}
```

Slack uses permille. Minimum is positive, maximum is at least minimum, margin/grace
are nonnegative, and the positive EMA numerator cannot exceed its denominator.
`configuration.authorityNominalTicks` supplies nominal Manhattan travel cost per tile.
Registered role/account data and published roster versions are authority evidence;
live inventory, true routes and citizen read status are not inputs to estimation.

Dispatch starts a separate acknowledgment watch. Receipt of `OrderAccepted` starts
one expectation for the attempt, freezing its acknowledgment terminal, published
repair installation duration, nearest role-permitted Post terminal, unscaled estimate,
learned ratio, slack and roster version. Travel, login, post and latency contribute
to the estimate. The base deadline is receipt plus ceiling of estimate times slack
divided by 1000, plus grace. If it exceeds the end of the known shift whose start most
recently preceded receipt, one complete off-duty gap plus check-in window is added.
The same extension applies to the separate unacknowledged-order timeout. Missing
published evidence yields `Unestimated` with deadline -1 and no result watchdog.

`WorkCompleted`, `WorkBlocked` and `UnableToContinue` for the same authenticated attempt
satisfy responsiveness. Only the first qualifying result after acknowledgment trains
the integer EMA. Earlier results are recorded without retroactive learning. Existing
deadlines do not change with new samples or roster updates. Exact-deadline delivery
runs in phase 4 before the phase-5 watchdog. Cancellation removes pending watchdogs;
a later first result retains Cancelled status and can still supply one duration sample.

Overdue creates one `MissingWorkReport` incident, with status unknown. A late result
records its message ID without declaring physical task completion. Replacement dispatch
and patrol dispatch are described below. Account-scoped record kinds are
`ReportExpectation`, `AcknowledgmentWatch` and `MissingWorkReport`.

Assertions `Expectation`, `Acknowledgment` and `MissingReport` require an `attemptId`.
Expectation state is `Absent`, `AwaitingResult`, `Satisfied`, `Overdue`, `Cancelled` or
`Unestimated`; acknowledgment state is `Awaiting`, `Acknowledged`, `Rejected`, `Overdue`
or `Cancelled`; missing-report state is `Present` or `Absent`.
`inspect --view expectations` includes expectations and learned role/task ratios;
`acknowledgments`, `incidents` and `authority` expose the other immutable projections.
Results include `authorityConfiguration`, `reportExpectations`,
`reportExpectationsSatisfied` (any first result received, including after cancellation),
`missingReportsOpened` (historical count) and `acknowledgmentsOverdue` (current count).
Schema 15 stores configuration, registry, watchdogs, estimates, incidents and learning.
Use `authority_reports.json` for autonomous repair and `late_report.json` for the
explicitly scripted late, misleading claim with an off-duty extension.

## Replacement dispatch and incident admission

Authority settings additionally accept `incidentAdmissionPermille` (default 50, range
0..1000), `maximumReplacementDepth` (default 2, nonnegative) and `responseOrderTicks`
(default 1,728,000, positive). The last is an explicit replacement-order lease policy;
it does not depend on a test's shortened station day.

An incident fixes its cohort from known roster evidence at opening. Each fixed hour,
`floor(tick * tickDurationMs / 3600000)`, captures the known on-duty headcount for
each cohort and computes `max(1, floor(headcount * incidentAdmissionPermille / 1000))`.
For a nonintegral ticks-per-hour ratio, opening occurs at the first tick in the new
bucket. Cohort -1 represents missing roster evidence, with count zero and minimum
budget one; personnel without known rosters are not presumed on duty. Publications
within a bucket do not rewrite its count or budget. They can change current eligibility.

Review follows incident opened tick then opening event sequence. It chooses a known
qualified, on-duty responder with no unexpired pending/active administrative commitment,
excluding the overdue assignee. Received results/rejections release known commitments.
Nominal travel from the last received terminal visit, falling back to the known assigned
rest tile, ranks candidates with actor-ID ties. This is a potentially stale estimate.
No live position, action, credential availability, inventory or private read is consulted.

The first posted response charges one incident token. Ineligible incidents spend
nothing and retain queue age; eligible incidents beyond budget are EscalationDeferred.
Public availability boundaries, messages, publication, commitment expiry and new
buckets wake review. Explicit cancellation removes an uncommitted work-response goal.
Replacement IDs persist across all later reviews. A replacement is an ordinary work
attempt and requires physical reading, acknowledgment, execution and reporting.

`inspect --view admission` exposes bucket/cohort, opening tick, headcount, budget and
usage. `incidents` includes admission bucket/tick, replacement attempt ID, recursive
depth, work-response state, replacement result message and claimed work resolution.
Depth zero is the original incident; default depth two permits two replacement levels.
Work-response states are Pending, NoEligibleResponder, EscalationDeferred, Dispatched,
DepthLimited and Cancelled. WorkerStatusResponse is PendingPatrol, PatrolPosted,
ReportedFound, ReportedNotLocated or ReportedBlocked. WorkResolution is Unverified, ReportedComplete, ReportedBlocked or
ReportedUnableToContinue, learned only from the replacement attempt's first qualifying
result after its acknowledgment. These are claims, not physical verification.

`IncidentResponse` is an account-scoped record kind. Metrics include `incidentsAdmitted`,
`replacementOrders`, `replacementResultsReceived`, `escalationDeferred`,
`replacementResponseWaiting` and `workerStatusPending`, separate from historical
`missingReportsOpened`. Receiving a work result alone does not establish worker status.
Schema 15 persists the admission
ledger, queue state, tokens, child identity, depth and independent response evidence.
`replacement_response.json` demonstrates an autonomous replacement following an
explicitly scripted original-worker acknowledgment trigger.

## Patrol protocol and local actor inspection

`authority.patrolRole` defaults to `guard` and must be nonempty. Registered personnel
with this role are selected using the same known-roster/commitment boundary as repair
dispatch. Search candidates are, in order, the worker's last received terminal location
(or known rest fallback), published job site and assigned rest location, deduplicated.
Payloads permit one to eight candidates; the current policy produces at most three.
No current worker location enters the payload. `responseOrderTicks` also sets patrol
expiry. Initial-message injection of PatrolOrder is rejected; the authority creates it.

PatrolOrder has an immutable typed payload: incident ID, target actor ID, required role,
expiry and candidate coordinates. Finite reading pins the order fact and creates a
private local copy. The guard posts PatrolAccepted before searching. Each candidate
requires ordinary movement and finite InspectActor, with `targetActorId` on scripted
inspection commands and `information.configuration.inspectTicks` as duration. Failed
or cancelled reads/inspections grant no observation. At inspection completion, a
co-located, non-transit actor yields its local coordinate; otherwise the fact is
AbsentAt the inspected tile, including for an unknown identity. It never reveals
where the target actually is. The actor view and inspection getters remain pure.

Search stops on a positive observation or exhaustion of the payload. PatrolFound and
PatrolNotLocated carry the final local observation and its time; NotLocated retains a
negative-at-location claim, not global absence. PatrolBlocked carries an explained
failure/expiry claim. These reports need terminal login and posting. The authority
accepts only the assigned account/order and the first result; it never checks physical
truth. Repeat results and unrelated accounts cannot overwrite the first evidence.
Patrol uses an explicit lease; adaptive report-expectation tracking currently remains
specific to repair attempts. Automatic follow-up after a blocked/negative patrol and
explicit remote patrol cancellation/reassignment are not implemented.

Local states are Read, AcceptancePending, Searching, ResultPending and Completed.
The snapshot stores the read payload, candidate index, bounded observations, backoff,
decision/expiry events and tagged in-flight actions. Known expiry prevents new search
actions, stops movement on its next tile and queues a blocked report. Duty transitions
retain the search and its observations; finite inspection can finish after shift end.

`inspect --view patrols` shows institutional orders and received evidence;
`patrol-local --actor ID` shows that actor's read/search/report state. `WorkerStatus`
assertions use the original `attemptId` and one of the five response states above.
Metrics add `patrolOrders`, `patrolResultsReceived`, `patrolCandidatesInspected` and
`incidentsResolvedByReports`. The last requires both ReportedComplete work and
ReportedFound status; historical incidents remain inspectable. `workerStatusPending`
counts incidents without ReportedFound, including negative/blocked responses.
`escalationDeferred` includes either branch waiting for an admission token.
Patrol and replacement IDs share one token even when committed at different times.

## Cleanliness and physical cleanup

Optional `initialState.cleanliness` enables dirt on completed tiles with the case-sensitive
`Cleanable` capability. `dirt` entries contain positive `tileId` and nonnegative `units`;
unlisted Cleanable tiles start at zero. Duplicate entries and non-Cleanable tiles fail
validation. Defaults: `cleanUnitTicks: 300`, `meanSoilingTicks: 288000`, `spillUnits: 4`,
and `access.roles: ["cleaner"]`. Access also supports `toolResource` and `staffId` through
ordinary work claims. A tool must be a declared work resource and physically held.

`meanSoilingTicks: 0` disables automatic soiling for controlled fixtures; otherwise the
integer mean is 2..1,000,000,000. Each timer draws uniformly from floor(mean/2) through
floor(3*mean/2), inclusive, on the `soiling` stream. The first timer starts from tick 0;
each event adds one unit and draws its successor. Getters, tracing and spills draw nothing.
`Spill` disturbances require `tick` and a Cleanable `tile`; optional positive `dirtUnits`
overrides the configured spill size. Both disturbance kinds run before same-tick cleaning.

`Clean` is a finite local action at `target`, removing at most one unit on completion.
It checks location/role/tool/staff at both boundaries, observes arrival dirt, and rechecks
current dirt at completion. A clean tile produces `NoWorkNeeded`; concurrent completions
cannot remove the same unit twice. Cancelled/failed work removes no dirt. Tools are reserved
and released without consumption. Cleanup jobs connect these physical actions to ordinary
read-order acceptance, planning, cancellation and terminal outcome reports as described below.

Tile.Dirt is the fourteenth supported perception type and defaults to Passive. Ordinary
local sensing follows the configured perception rule; finite InspectTile also reveals
dirt. Physical cleanup performs its own local checks. No observation grants remote access
or changes authority beliefs. `inspect --view dirt` returns tile ID/location, current and
initial units, added/removed totals and next soiling tick. `Dirt` assertions require a
Cleanable `position` and nonnegative `quantity`. Metrics add `dirtUnits`, `dirtUnitsAdded`
and `dirtUnitsRemoved`.

Binary schema 15 retains cleanliness configuration, all balances, pending spills/soiling,
stream continuation and active cleaning/tool claims. Restore rejects inconsistent balances,
missing tiles, invalid timers and invalid active cleaning bindings. Older binary schemas
require their matching original build; the JSON envelope remains version 1.

## Autonomous cleanup orders

The immutable public work catalog includes Cleanable tile IDs/coordinates, allowed roles,
tool resource and unit duration. It publishes no dirt quantities. Actors learn dirt locally.
CleanTile orders carry the typed tile target and expiry; delivery does not create a local
goal. A completed read creates the private copy. Role/known tool/route feasibility is checked
with the bounded planner; only posting OrderAccepted enables physical work.

The planner uses the existing request/depth/node/candidate budgets. It can collect a known
tool from an accessible physical location, travel and perform finite InspectTile on arrival.
It replans after each Clean unit. A retained arrival-check marker is invalidated by movement
and suspension; new visits inspect again even if old knowledge says the tile is clean.
Missing known prerequisites produce an explained block/retry. Tools are not fabricated by
this domain. Multiple cleanup assignments wait in stable attempt order for the same citizen.

Local orders expose `kind`, `tileId`, `cleanupChecked` and `cleanedUnits`. Checked zero dirt
finishes the local goal. If this attempt removed zero units, its WorkCompleted claim is
`NoWorkNeeded`; otherwise it is `ReportedComplete`. The report still requires physical
travel, login and posting. The authority treats either as a completion claim without
checking dirt. A later original report retains its own attempt and cannot complete or
replace the child result. WorkAttempt record rows identify TaskKind and MachineId or TileId.

Cleanup expectations use nominal travel from the acknowledgment terminal to the published
tile, finite inspection plus `reportedUnits * cleanUnitTicks`, return travel to a permitted
post terminal, login/post and transport. Reported units come from the job record, not live
truth; the estimate freezes at acceptance receipt. Role/task duration learning separates
CleanTile and RepairMachine samples. Replacement qualification and patrol search candidates
use published cleanup rules and existing permitted evidence. Both branches share one token.

Schema 15 persists typed targets, public cleanup rules, reported workload, local progress
and existing pending events. It validates read provenance and target agreement across
job, attempt, posted message and private order. `missing_cleanup_report.json` contains 24
assertions and no scripted actor commands; it uses a seeded job/assignment. Facility-condition
reporting and task creation from received dirt observations are not implemented yet.

## Facility reports and generated work (schema 15)

Optional `initialState.facility` enables the condition-reporting duty. It requires
jobs, authority and published work rules. Fields are `reportingRoles` (default
cleaner/supervisor/guard/patrol), `dirtThreshold` (2), `repeatTicks` (36000), and
`orderTicks` (1728000). Threshold and durations must be positive. An empty role
list disables citizen reporting duties while preserving explicit configuration.

`FacilityCondition` targets Authority and carries Tile.Dirt (nonnegative number)
or Machine.Operational (false), an observed tick and nonempty operation key.
It carries no assignment, attempt, patrol or roster payload. Receipt checks the
registered reporting duty, published target, threshold and a nonfuture observation.
Claims are evidence, not permission to modify physical state. Idempotency uses
sender account plus operation key; exact repeats and conflicting reuse are ignored.

`inspect --view facility` exposes authority task evidence and status;
`inspect --view facility-local --actor ID` exposes that actor's private observation
and report-generation state. `FacilityTask` runner assertions use a positive `jobId`
and `state`: Absent, Pending, Assigned, NeedsAttention or ReportedComplete.
The canonical `scenarios/facility_cleanup.json` begins with no jobs or assignments
and contains 11 assertions covering observation through received completion.

Schema 15 adds the monotonic generated-job ID, facility configuration, local watches,
receipt identities, task provenance/completion and dispatch timer. Older snapshots
must use their original executable; the binary reader does not migrate them.

## Machine faults and wear (schema 16)

Work-enabled worlds accept a `MachineFault` disturbance with a known machine
`workServiceId`. It runs in disturbance phase 1. Optional `initialState.machineWear`
configures automatic wear via `meanIntervalTicks`, default 2592000 (D-77).
Omitting the object disables automatic wear. Zero disables timers explicitly;
otherwise the accepted mean range is 2..1000000000 ticks.

Wear intervals use the dedicated `wear` PCG stream. For mean m, intervals are
uniform integers in `[floor(m/2), 2*m-floor(m/2)]`, with exact arithmetic mean m.
Initial draws follow ascending machine ID; each wear event schedules the next draw.
Manual faults, inspection and getters do not draw or reschedule wear. Timers continue
when a slot is already empty or equipment is disabled.

A fault exhausts the one installed cartridge through an explicit resource sink and
leaves the slot empty. With no installed cartridge it consumes nothing. Held spares,
tools and pending installation inputs persist. Finite installation still requires
an ordinary physically available part. Registered credential cards cannot be used
as consumable cartridges. Service-disabled state remains independent of the fault.

`inspect --view wear` returns configuration, per-machine fault counts, consumed-part
counts, last fault/consumed item and next wear tick, plus resource sink balances.
These are privileged diagnostics. Metrics include `machineFaultEvents` and
`faultConsumedParts`. Snapshot viewer data exposes the same diagnostics.

Schema 16 adds wear configuration, registry/counters/timers, RNG continuation and
resource sink totals. Restoration validates machine identities, fault counts and
consumed IDs, timer/stream consistency, summed sink counts and physical accounting.
Older binary snapshots require their original executable.

## Sampled repair verification (schema 17)

Optional `initialState.qualityAssurance` requires authority, jobs and a work publication.
Fields: `samplePermille` (default 250, range 0..1000), `delayTicks` (18000, nonnegative),
`orderTicks` (1728000, positive), `supervisorRole` (supervisor, nonblank).

The first accepted WorkCompleted report per RepairMachine attempt draws one integer
0..999 from the independent `qa` stream. It is selected when below samplePermille.
Even rate 0/1000 records a draw; repeated completion IDs cannot resample. Other task
kinds do not enter QA. The due time is receipt time plus delay, not physical completion.
A due event marks the check pending; the next decision tick may post a supervisor order.
No eligible known on-duty uncommitted supervisor means retained PendingSupervisor.
The original assigned worker is excluded from supervising their own attempt.

`VerifyMachine` is a typed work kind carrying machineId and expiry. It uses ordinary
read, acknowledgment, lease, cancellation, suspension, check-in, deadline and result
handling. The bounded planner travels and performs a fresh finite InspectMachine;
prior status/specification knowledge never substitutes. Physical completion means
inspection finished, not that the inspected machine is operational.

A completed inspection captures operational status, required and installed resources,
time, actor, machine, action and verification attempt in private retained evidence.
No installer identity is revealed. WorkCompleted for this kind carries Machine.Operational
as a boolean with the captured observation time, through a real terminal post. It cannot
change the machine. Authority statuses ReportedClear/ReportedDiscrepancy describe the
received claim; inspection and report delivery remain separate events.

`inspect --view qa` exposes sampling, source repair receipt, due time, assignment and
result evidence. `inspect --view verifications --actor ID` exposes private captured
inspections. `QualityAssurance` assertions use repair `attemptId` and state Absent,
NotSampled, Scheduled, PendingSupervisor, Assigned, ReportedClear, ReportedDiscrepancy,
or NeedsAttention. Metrics: qaDecisions, qaSelected, qaReportedClear,
qaReportedDiscrepancy. The canonical quality_verification fixture has 12 assertions.

Schema 17 extends typed targets to VerifyMachine and stores QA policy, first-receipt
sampling decisions, due/review timers, assignments/results and private inspections.
Restore validates source attempt/account/receipt, sampling thresholds, timers,
assignment target, result ownership and local inspection bounds. Older snapshots
require their original executable. Discrepancy audit and case adjudication are pending.

## Discrepancy audits (snapshot schema 18)

A completed discrepant verification opens one private audit per verification attempt.
No new configuration is required. The existing `information.recordAccess` assignments
must grant the actor both WorkAttempt and WorkReport at the same Records-capable desk.
If more than one desk qualifies, ascending terminal ID determines the assigned desk.
Absent complete grants produce NeedsReview/NoAssignedRecordsTerminal.

States are QueryingAttempts, QueryingReports, ReadyToFile and NeedsReview. Queries use
history=true, entityId=0, limit=1, and a retained cursor. WorkAttempt and WorkReport each
freeze their first captured version; subsequent pages ignore newer revisions. This is
not an atomic snapshot across the two record kinds. RecordVersion is their maximum;
AttemptVersion and ReportVersion preserve the individual limits for later validation.
There are at most 64 pages and 32 retained relevant rows. Bound exhaustion or partial
knowledge disclosure produces NeedsReview without an attributable finding.

Record queries now expose RecordFieldCount metadata. WorkAttempt rows for machine work
include the declared RequiredResource; installed resources and execution truth remain
excluded. Retained audit rows copy only actually learned RecordQuery facts, paired with
the query action IDs. Account attribution requires a complete disclosed history.

The unique-assignment/v1 policy attributes WrongInstalledPart only when one repair
assignment matches the slot/specification and has a valid acceptance and successful
completion claim preceding inspection. Missing or malformed submissions, conflicting
amendments, cancellations and multiple repair assignments require review. An empty
slot or a fault with the required type is not attributed. The finding includes JobId,
AttemptId, MachineId, AccountId and immutable evidence versions. It is not a filed case,
reprimand, detention request, or assertion about who physically installed the part.

`inspect --view audits --actor ID` exposes private audit state. The viewer's snapshot
projection includes audits; specialized UI presentation is pending. Metrics include
`auditsReadyToFile` and `auditsNeedsReview`. `Audit` assertions take actorId, a positive
verification attemptId, and a state name or Absent. `discrepancy_audit.json` has nine
assertions covering finite disclosure and the complete audit path. Its worker is
scripted to install a fitting wrong part and submit a false completion through ordinary
physical/terminal actions; the supervisor is autonomous.

Schema 18 persists audit state, versions, bounded evidence, query action IDs, decisions,
backoff and findings. Older binary checkpoints are intentionally rejected. Scenario and
replay envelope schema versions are unchanged.

## Personnel case protocol (snapshot schema 19)

Optional `initialState.personnel` enables autonomous filing of attributable audits.
Fields: `supervisorRole` (default supervisor), `qualifyingWindow` (6048000 ticks,
positive), `detentionThreshold` (2, positive). Information and jobs are required.
The threshold is retained for the subsequent detention-request implementation; it
currently does not dispatch guards or change custody.

New message kinds are FileCase (citizen-postable) and CaseDecision (service-only).
WorkCompleted verification submissions can carry `inspectionReport` with actionId,
requiredResource and installedResource. These are authenticated submitted claims.
A FileCase carries `caseSubmission`: jobId, attemptId, machineId, accountId, violation,
policy, inspectionReportId, attemptVersion, reportVersion and 1..32 unique evidence
references (recordVersion, queryActionId). The current policy is unique-assignment/v1
and the supported violation is WrongInstalledPart. The machine ID identifies its one
cartridge slot, so the canonical key is job/attempt/machine-slot/violation.

The FileCase envelope targets Authority with an Attempt/CaseStatus subject, FileCase
text claim, matching job/attempt, relatedMessageId equal to inspectionReportId and a
nonempty operationKey. Personnel checks authenticated sender permission, an accepted
verification report, published resource types, authorized completed-query receipts,
complete history and both version cutoffs. A relevant work amendment after the cutoff
produces StaleWorkRecords. Missing proof, forged references and policy mismatches are
rejected. Current physical state is not consulted.

An accepted new canonical case appends one Reprimand. Identical retries or new filing
and verification-report IDs can return AlreadyAccepted for the same case without
incrementing the count. Conflicting reuse of an operation key returns IdempotencyConflict.
QueryDisclosure, Case and Reprimand are scope-filtered RecordKind values. QueryDisclosure
fields include QueryActionId, EvidenceVersion and QueriedVersion; RecordVersion remains
reserved metadata for the disclosure record itself.

A CaseDecision reply contains filingMessageId, caseId (zero if rejected), accountId,
qualifyingCount and reason. Delivery does not change citizen awareness. A completed
ReadInbox adopts the result into a local filing: Queued, Posted, Accepted or NeedsReview.
The count is fixed at service decision time. With no completed detentions yet modeled,
current counting includes accepted reprimands whose ticks lie in [now-window, now].
History is preserved after the window expires. Exclusion after completed detention,
RequestDetention posting and separate authority decisions remain pending.

Inspect views: `cases`, `reprimands`, `disclosures`, and `case-filings --actor ID`.
Metrics: acceptedCases, reprimands, caseDecisionsRead. Viewer snapshot projection exposes
these data; specialized presentation remains pending. CaseFiling assertions take actorId,
verification attemptId and state (or Absent). ReprimandCount assertions take accountId
and quantity and check total history, not the time-window count.

`personnel_case.json` has ten assertions proving filing/receipt/read separation.
`personnel_two_cases.json` has four assertions proving distinct cases for two slots on
one account. Workers use declared shared physical/terminal commands to trigger wrong
installations; all supervisor inspection/audit/filing/reading is autonomous.

Schema 19 persists all payloads, personnel policy, disclosure proofs, canonical cases,
unique reprimands, operation keys, replies and private filing state. It rejects older
binary checkpoints. JSON scenario and replay envelope versions remain unchanged.

## Detention request and approval (snapshot schema 20)

A supervisor who completes a read of an accepted CaseDecision with a qualifying count
at or above personnel.detentionThreshold queues RequestDetention. This happens even
when the approval policy is unavailable; the authority then records a reasoned wait.
An unread reply never queues the request.

Optional `initialState.detention` enables approval. It requires both personnel and
authority. Fields: sentenceTicks (864000, positive) and orderTicks (1728000, positive).
These define a sentence duration and authorization lifetime; sentence time does not
start until future physical intake. No guard roster or custody is created here.

RequestDetention carries detentionRequest {accountId, caseId, caseReplyMessageId}.
Its envelope targets Authority, uses Order/caseId/DetentionStatus, the RequestDetention
text claim, relatedMessageId equal to the referenced case reply, and a nonempty
operationKey. Citizen inputs cannot post DetentionDecision replies.

Authority requires an authenticated supervisor, an issued accepted-case reply addressed
to the requester and delivered before submission, and matching accepted case ownership.
It recalculates the qualifying count at request receipt. New authorization requires the
threshold and an unambiguous registered target identity. It does not consult current
physical state or the target's private knowledge.

Request decisions are Approved, Waiting or Rejected. Reasons include Approved,
ExistingAuthorization, DuplicateRequest, ThresholdNotMet, RequesterPermissionDenied,
InvalidAcceptedCaseEvidence, DetentionPolicyUnavailable, AmbiguousSubjectIdentity,
IdempotencyConflict and PriorRequestClosed. A missing service/policy leaves a wait.
An active authorization may be reused even after its original count evidence leaves
the window; reuse does not renew its duration or create another authorization.

An approved authorization stores ID, subject account and known actor ID, originating
request and case, issue tick, expiry, sentence duration, justifying case IDs and the
case cutoff used for counting. Its current states are Authorized and Expired.
Expiry is a scheduled administrative transition. The target remains physically free;
guard teams, escort, finite intake, custody, sentence/release and post-detention count
exclusion remain to be implemented. Authorization is not a detention slot reservation
or occupancy claim.

A DetentionDecision reply carries requestMessageId, accountId, authorizationId, state,
reason, qualifyingCount, expires and countThroughCaseId. The private request retains
Queued, Posted, Approved, Waiting or Rejected only through its own post/read actions.
An approved local copy keeps its known expiry even when the central record later expires.

New scoped record kinds: DetentionRequest (requester-owned) and DetentionAuthorization
(subject-owned). New inspect views: detention-requests, detention-authorizations and
detention-local --actor ID. Metrics: detentionRequests, detentionAuthorizations,
activeDetentionAuthorizations and detentionRequestsWaiting. DetentionAuthorizations
assertions take accountId, quantity and an optional Authorized/Expired state;
LocalDetentionRequest assertions take actorId, accountId and state (or Absent).
The canonical detention_authorization.json has ten assertions.

Schema 20 also adds countThroughCaseId and validationRecordVersion to personnel case
replies, and the validation cutoff to accepted cases. These preserve a decision made
before another acceptance or amendment later in the same tick. No timestamp-only
reconstruction can safely replace these cutoffs. Binary schema 19 checkpoints are
rejected; JSON scenario/replay envelope versions are unchanged.

## Local custody and escort groups (snapshot 24)

Guard-tagged `BeginEscort` uses an authentic read `guardOrderId`. It is a finite
local handoff using `information.configuration.inspectTicks`, with physical validation
at start and completion. The public input executor applies custody restrictions before
ordinary cancellation, logout or work-resumption inputs. Generated coupled Move actions
are caused by the local guard protocol rather than scenario scripts.

`inspect --view escort-groups` exports physical groups; `--view escorts` retains
institutional dispatches. An `EscortGroup` assertion uses an authorization `entityId`,
member `quantity` and state `Absent`, `Escorting`, `AwaitingIntake` or `Interrupted`.
Snapshot schema 24 is intentionally incompatible with earlier binary checkpoints;
replay bundles must match their recorded build. See `h3-escorts-progress.md` for the
current implementation boundary and verification evidence.

## Detention facilities and completed sentences (snapshot 25)

`initialState.detention.facilities` is an optional list of physical services. Each
entry requires `id`, `position` and positive bed `capacity`. Optional fields are
positive `serviceCapacity` (default 1), `intakeTicks` and `releaseTicks` (300 each),
`powered` and `connected` (true). A copied escort destination does not create a service.
Facility IDs are a separate namespace from terminal IDs. Receipt records use FacilityId.

A `DetentionServiceChange` disturbance requires `detentionFacilityId`, `enabled`
(power) and `connected`. It cancels affected local finite operations; a cancelled
intake releases its reservation, while an interrupted release retains occupancy.
Intake and release currently require both power and connectivity. Availability changes
can wake local waiters, and unavailable services retry using bounded scheduling.

`RegisterIntake` is a guard-planned finite local action. `ReleaseDetention` is generated
by the occupied facility's sentence timer; ordinary citizen requests cannot initiate
release. Reservations and occupants are separate. An intake or release operation also
claims service capacity. Slot occupancy starts on successful intake and ends on successful
release. Service-originated receipt/notice message kinds cannot be posted by citizens
or injected as initial messages.

Inspection adds `detention-facilities` and `detention-stays`. Assertions add:
- `DetentionStay`: authorization `entityId`, state Absent/Reserved/Cancelled/Detained/Releasing/Released.
- `DetentionFacility`: facility `entityId`, `quantity`, state Reservations/Occupants/Operations.
- `QualifyingReprimands`: `accountId` and `quantity`; unlike ReprimandCount, this applies
  the qualifying window and received completed-detention history.

Snapshot 25 persists actual service availability, reservations, occupancy, finite
operations, sentence timers, service receipts, received status, release notices and
current-order refresh messages. Historical detention decisions also retain their
record-version cutoff, so a later completed sentence cannot rewrite an earlier count.
See `h3-detention-services-progress.md` for verification and remaining requirements.

## Guard revocations and outcome reports (snapshot 26)

A RevokeDetention disturbance takes tick, detentionAuthorizationId and operationKey.
The ID must name an issued authorization at execution time. Reusing a key for the
same ID is idempotent; conflicting keys/IDs are rejected. Expired authorizations do
not generate new notices. Formal revocation is separate from individual awareness.

EscortRevoked is an authority-only mailbox notice. EscortOutcome is a citizen
report to the authority, with relatedMessageId equal to the assigned guard order,
subject Order/order-id/DetentionStatus, a text outcome, and canonical operation key
escort-result/{order-id}/{outcome}. Outcomes are TargetNotLocated, EscortInterrupted,
KnownOrderExpired, AuthorizationRevoked, EquipmentUnavailable, RendezvousUnreachable
and SearchUnreachable. Receipt authenticates the claim but does not prove its truth.

Inspection adds revocations, guard-revocations, guard-outcomes and guard-receipts.
KnownGuardRevocations assertions require quantity and optionally actorId.
GuardOutcomeReceived assertions require an outcome state, quantity and optionally
actorId. DetentionAuthorizations accepts state Revoked; EscortDispatch adds Cancelled
and NeedsAttention; LocalGuardOrder adds Revoked.

Snapshot 26 persists commands, notice references, individual learning proofs and
queued/posted/received outcomes. Prior binary snapshot schemas are not migrated.
Existing replay bundles require their recorded build. See
h3-guard-outcomes-progress.md for the implementation boundary and verification.

## Exact needs and critical interruptions (snapshot 27)

Optional initialState.needs enables five needs for every actor. An omitted initial
value starts at zero. Initial entries contain actorId, kind and exact q, bounded
between 0 and 3,600,000,000. A displayed point is 3,600,000 q units. No need relief
can be injected through a scenario command.

Defaults follow D-50..54. Optional rules must contain exactly one entry for each
NeedKind: Hunger, Bladder, SleepPressure, WorkStrain, Social. Each rule contains kind,
awakeRate, workingRate, sleepingRate and restingRate, in integer points per hour.
Optional seek, urgent and critical thresholds are displayed points 1..1000; zero
disables that threshold. Enabled thresholds must be ordered. Current physical
activities derive Awake/Working; the other rates await actual sleep/rest services.

NeedQ assertions use actorId, state (NeedKind) and quantity (exact q).
The needs inspection view and exported snapshots expose detached per-actor state.
Snapshots persist complete anchors, modes, rates, announced bands and next threshold
events. Version 27 requires a matching build; earlier binary snapshots are not migrated.
See h4-needs-progress.md for implemented behavior and remaining daily-life requirements.

## Physical need services (snapshot 28)

initialState.needs.services declares id, kind and position. Kinds are Cafeteria,
Toilet, RestSpot and Bed. Optional capacity defaults to one; beds require one.
durationTicks defaults to 9000/1800/6000/288000 respectively. Cafeteria mealResource
defaults to meal. Bed assignedActorId authorizes one registered citizen; duplicate
assignments to a citizen are rejected. An unassigned ordinary bed cannot be used
until an allocation mechanism authorizes it. detentionFacilityId scopes a service
to occupants of that declared co-located facility. enabled defaults to true.

Eat, UseToilet, Rest and Sleep require a local serviceId. Eat can optionally select
an itemId; otherwise the local provider reserves an available real meal unit.
Requests queue for capacity, then execute their full duration. Food consumption and
hunger relief occur only on completion. Toilet resets bladder only on completion.
Sleep/rest use exact elapsed rates, including interrupted activity. No keycard is
required. NeedServiceChange disturbances require needServiceId and enabled.

Actor views expose published service definitions only. Current occupancy, queues,
food and availability remain privileged physical state. Inspection adds need-services
and need-activities; metrics add mealsConsumed, needActivitiesCompleted and
needServiceQueued. NeedService assertions use entityId, quantity and state
Queued/Occupants. NeedActivities assertions count a valid activity status, optionally
filtered by actorId. NeedQ continues to compare the exact accumulator.

Snapshot 28 adds physical service claims, waiting order, activity histories,
admission timers and meal sinks. Binary schema migration is not implemented.
See h4-need-services-progress.md for verification and the remaining autonomous
daily-life requirements.

## Autonomous urgent relief (snapshot 29)

When initialState.needs is present, actors without scripted:true autonomously plan
urgent/critical relief using their own needs and published services. Explicitly
scripted actors keep exact need accumulation and physical service behavior, but
receive no autonomous need agent. No JSON command sequence is needed for relief.
Seek-only needs currently await future roster/optional-goal policies.

need-agents inspection and exported needAgents contain the actor's selected need,
service, owned physical action, next decision, retry clock, bounded backoff, last
plan outcome/cost, completion count and private service failure memories. A failure
memory records serviceId, actionId, tick, retryAt and reason. These diagnostics do
not provide live stock/occupancy/outage facts to citizen or authority planners.

The actor view adds NeedCustodyFacilityId: -1 for ordinary freedom, 0 for active
escort control, or the citizen's own detention facility ID after local intake.
This reflects physically established custody; it is not an institutional locator.

Schema 29 persists autonomous decision state and validates action ownership,
service compatibility, future timers and failure memory. See
h4-need-planning-progress.md for verification and scope limitations.

## Retained daily opportunities (snapshot 30)

Needs-enabled autonomous actors with a known roster derive meal/sleep/rest windows
from that version's day, duty, cohort and check-in offset. No additional JSON
footsteps are supplied. Window proportions scale with the configured day, while
service durationTicks remain explicit physical durations. Daily life requires a
day of at least 24 ticks and duty of at least four ticks. A short day must not be
presented as 24 hours; use declared short activity durations in such fixtures.

The daily-goals inspection view and exported dailyGoals include id (stable goal
sequence), actorId, rosterVersion, kind, opens, dueTick, closes, status, retryAt,
backoff, reason, serviceId, activeActionId, completedActionId and endedTick. Kinds
are PreDutyMeal, DutyMeal, AfterDutyMeal, ScheduledRest and Sleep. Status is Pending,
Completed, Unnecessary, MissedWindow or Superseded. Scheduled rest retains its due
goal across shift end. Meal/sleep admission requires an open opportunity, but a
finite activity may finish later. Meal eligibility additionally requires seek.

DailyGoals assertions use a valid status in state and a nonnegative quantity,
with optional actorId. Metrics add dailyGoalsCompleted, dailyGoalsMissed and
dailyGoalsBlocked. The latter counts pending goals whose retry clock is future.

Schema 30 persists routine calendars/goals, shared due-goal sequences, report and
check-in due stamps, and check-in retry clocks. Restoration verifies roster-derived
windows, event ownership, sequence uniqueness and physical completion evidence.
See h4-daily-goals-progress.md for tests and remaining release requirements.

## Berth allocation (snapshot 31)

Bed services add allocationTicks (positive; default 100) and
allocationRequiresCredential (default true). AllocateBerth uses the bed serviceId
and physical target, reserves the vacant berth, and grants assignment only after
finite authorized completion. Initial assignedActorId remains the immutable seed;
the inspected live AssignedActorId reflects completed allocations. Published
definitions never broadcast new assignments. OwnBerthId is a private receipt.

berths and berth-allocations inspection views expose physical housing state and
formal allocations. Exports include berths and berthAllocations. Metrics add
usableHousingBerths, vacantHousingBerths and berthAllocations. A usable vacant bed
counts toward housing capacity; assignment and occupancy do not increase it.

Snapshot 31 persists allocation configuration, live assignments, records, pinned
private receipts and reservations. Restore checks ownership, authorization identity,
record provenance and completion timers. See h4-berth-allocation-progress.md for
verification and remaining sleep fallback requirements.

## Explained sleep fallback (snapshot 32)

Urgent and scheduled sleep may select finite RestSpot activity after no known
eligible sleep plan is found. Rest remains Rest: it does not reduce sleep pressure
or complete a daily Sleep goal. Diagnostics and SleepUnmet trace entries explain
SleepUnmet/NoKnownUsableBerth. Search shares the primary request's remaining node
budget, and ordinary physical custody/location/capacity checks still apply.

Need-agent inspection/export adds sleepFallbackRetryAt, a shared bounded retry
clock set after completed fallback rest. Snapshot 32 persists and validates it,
accepts explained rest as an intermediate sleep-goal fallback, and rejects rest
as proof of completed scheduled sleep. No new scenario commands are necessary.
See h4-sleep-fallback-progress.md for verified behavior and remaining release work.

## Personal need policies (snapshot 33)

Need-agent diagnostics add toiletDueTick (-1 when absent), toiletSequence (zero
when absent), and urgentRetryAt. Bladder seek creates a retained due goal sharing
the due-time/sequence ordering of reports, check-ins and scheduled activities.
Physical toilet relief clears it. Work-strain seek permits additional finite rest
during known breaks or off-duty periods, after higher-priority goals and work.
Blocked urgent needs retain their separate retry while allowing lower useful work.
These policies require no scenario command sequence.

Snapshot 33 validates goal/stamp consistency, global sequence uniqueness, bounded
retry clocks and decision ownership. Exported trace.jsonl starts strictly after
the replay checkpoint's last processed tick; initialization and prior events are
already represented in that checkpoint. See h4-personal-needs-progress.md.

## Conversation (snapshot 34)

needs.conversations configures durationTicks (default 1800), partnerCandidates
(1..4, default 4), factsPerSpeaker (0..4, default 4), and explicitConsentActors
(unique registered IDs; default empty). Default autonomous consent uses own social
seek and known off-duty status and yields to eligible higher-priority activities.
Explicit advance willingness does not bypass physical availability or higher duties.

Converse commands use targetActorId and conversationTopic: Routine, WorkStatus,
WitnessedProblem or AllowedRequest. Facts are selected from actual speaker knowledge
under the topic policy; commands cannot inject a conversation fact payload. Both
participants hold finite coordination claims. Cancellation grants neither the
completion relief nor disclosure. Knowledge facts add sourceActorId: positive for
Heard speaker identity, zero for other evidence. Heard sourceId is the conversation.

The conversations view/export contains participants, their action IDs, position,
topic, start/completion/end ticks, Active/Completed/Cancelled status and exchanges.
Conversations assertions use state and quantity with optional actorId. Metrics add
conversationsCompleted and conversationPartnerCandidatesExamined. Need agents add
conversationCursor. Snapshot 34 validates claims, history, timers and Heard provenance.
See h4-conversations-progress.md for policy details and verification.

## Restricted identity check (snapshot 35)

information.recovery enables identityCheckTicks (positive; default 300) and a
capabilities list of actorId, accountId and retained (default true). Actor/account
pairs must match registered identities and actor IDs must be unique. These are
modeled retained capabilities, not real credentials. Terminals offer the flag
CredentialRecovery; normal role, locality, connection and capacity checks apply.

RecoveryLogin uses serviceId and target. It joins the ordinary terminal FIFO and
creates a finite-expiry CredentialRecovery session only after verification completes.
Session inspection/export includes scope: Normal or CredentialRecovery. Sessions
assertions use state, quantity and optional actorId. Restricted sessions cannot use
ReadInbox, PostMessage or QueryRecords; these fail with RecoverySessionRestricted.
IdentityVerificationFailed explains a missing or mismatched alternate capability.

Snapshot 35 persists configuration, capabilities and scope and validates recovery
identity, finite checks, queued claims and session expiry. The complete recovery
case/mailbox/issuance protocol is not implemented by this increment. See
h3-recovery-identity-progress.md for evidence and remaining work.

## Credential recovery protocol (snapshot 36)

information.recovery.desks maps terminalId to workstationId and recipeId. The
terminal must offer CredentialRecovery; the printer must be local and match the
recipe capability. The recipe must declare exactly one unit of one blank input
and one unit of a different card output. Its duration is the physical issuance
duration. content/credential-recovery.json supplies the 600-tick default recipe.
Current desk configuration rejects extra printer tool/staff dependencies.

PostLostCard and IssueReplacement use serviceId (terminal), itemId (the original
card identity, not a held-item requirement), and local target. ReadRecoveryInbox
uses serviceId and local target. These require a restricted session. Caller-supplied
recipes cannot override the desk recipe. A read authorization is necessary before
issuance. New cards occupy the configured printer's ServiceOutput until picked up.

Message kinds add LostCardReport and RecoveryReply; routing adds CredentialService
and RecoveryAccount. These routes are reserved for the scoped recovery protocol.
RecoveryReply has an immutable typed payload containing caseId, oldCredentialId,
issuedCardId, issuedCredentialId, issuedTick and outputPosition. Zero issued IDs
and issuedTick=-1 mean authorized but not issued when the reply was posted.
Replies never refresh from live state while unread. Recorded scope is included on
restricted terminal-session records; formal cases use RecordKind.CredentialRecovery.

recovery-cases inspection/export contains actor/account, old credential, first
request, acceptance tick, issued card/credential, issuance tick/action/terminal/recipe
and record version. recovery-mailbox uses the selected actor's account and excludes
ordinary mail. CredentialRecoveryCases assertions use Authorized/Issued in state,
quantity, and optional actorId. Metrics add credentialRecoveryCases and
replacementCardsIssued. Snapshot 36 validates the transport, disclosure, ownership,
claims, timing and conservation links. See h3-recovery-protocol-progress.md.

## Autonomous credential recovery (snapshot 37)

CardLoss disturbances require tick, actorId and itemId naming a registered holder
and card. A held card moves to the current committed tile without knowledge or
authority updates. Non-scripted actors participate in recovery when configured
information.recovery exists; scripted protocol fixtures retain explicit control.

Work access adds requiresCredential (default false). Published workstation and
equipment descriptions expose this declared requirement; physical checks enforce
it at start and completion. Existing content without the flag retains its behavior.

recovery-agents inspection/export contains actorId, pending, discoveredTick,
oldCredentialId, oldCardId, postedTick, knownCaseId, issuedCardId, nextDecision,
retryAt, backoff, activeActionId, plannedTick, plannedTerminal, plannedKind,
urgentCredentialBlocked, completed, reason, lastOutcome, lastCost and
unavailableTerminals. These are private progress/observations, not live service
stock or hidden item locations. Metrics add credentialRecoveriesCompleted and
credentialRecoveriesPending. RecoveryAgent assertions require actorId, use
Pending or Completed in state and compare quantity with completed count.

Snapshot 37 adds this private progress and terminal retry clocks, recovery decision
and command events, CardLoss events and work credential requirements. Restore
validates actor/card ownership, matching reported/read/issued protocol evidence,
active actions and scheduled decisions. Older snapshots require the original build.
See h3-autonomous-recovery-progress.md for test and replay evidence.

## Response-chain declarations and provenance (snapshot 38)

initialState.responseChainRoles is an optional list of named actor contracts.
Each entry has name, actorId, triggerKind (InitialCondition or Event), an optional
trigger selector required only for Event, requiredEvents in causal order, and
optional allowedTriggerCommands (tick and kind). A selector has type and optional
exact tick, actorId, itemId and detail. Omitted actorId matches any actor, including
institutional events with actorId=0. Event triggers must select exactly one event.
Required transitions must be ordered descendants, not merely later timestamps.
An initial-condition contract accepts initial policy roots but still checks action
origins. At least one required transition is mandatory.

Allowances must match exactly one declared and executed external command on the
role's actor, at or before an explicitly bounded trigger tick (tick 0 for initial
conditions). No implicit scripted-actor exemption exists. Post-release player input
allowances are not yet modeled. Unknown enum values, actors, duplicate role names,
vacuous contracts, unmatched allowances and inconsistent trigger fields are rejected.

Trace events add origin: None on ordinary events; ExternalCommand, Autonomous or
SystemProtocol on ActionRequested. Source classification is set by the shared
executor and its internal protocols. response-chains.json and RunResult.responseChains
report role, actorId, rootEventId, actionsChecked, transitionEventIds, failures and
passed. Failed audits appear in assertionFailures and set assertion_failed. Audited
compact runs collect causal events internally; trace.jsonl remains empty unless
tracing is requested. Full-history verification requires an initial checkpoint.

Snapshot 38 persists private recovery discovery/completion event references,
privileged card-loss event references and distinct external command events. It
validates the causal bounds and command-origin invariants. See
response-chain-progress.md for scope, limitations and replay evidence.

## Physical construction (snapshot 39)

Catalog tile definitions accept optional construction with recipeId, access
(ordinary roles/toolResource/staffId/requiresCredential), berths (default 0),
sleepTicks (288000), allocationTicks (100) and allocationRequiresCredential (true).
The recipe must consume material and have no item outputs. Its duration is the
finite work time. content/construction.json declares the D-35 habitat recipe.

initialState.construction.projects contains id, definitionId, site and tileId.
These are initial administrative reservations, not traversable tile instances or
physical capacity. Sites must be unique, empty and adjacent to completed tiles;
future tile IDs must be unique and unused. Construct uses serviceId=project ID,
target=completed standing neighbor and optional matching recipeId. The executor
binds the definition's actual recipe. Materials must be physically held.

construction inspection and constructionProjects export include id, definitionId,
site, tileId, status (Reserved/Building/Completed), activeActionId, completedTick,
completedActionId, completedActorId, completionEventId and berthIds. Construction
assertions use entityId and state; TileCompleted uses position and Absent/Completed;
HousingBerths compares quantity with actual usable ordinary beds. Metrics add
constructionProjects and constructionCompleted.

Snapshot 39 adds these definitions, projects and ConstructionSite claims. It
serializes real constructed Bed services, validates their provenance and rebuilds
derived site/service indexes. SubjectKind.Service is used for locally learned
constructed service locations. Unknown dynamic beds are excluded from ActorView;
physical completion does not change the initial public layout or authority beliefs.
See h3-physical-construction-progress.md for evidence and remaining autonomous work.
