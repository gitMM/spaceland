# Architecture and extension points

`Contracts ← Mind` is the decision boundary. `Core` references both. `Runner` owns
I/O and hosts one or many independent worlds. Neither Core nor Mind can discover a
Unity object or filesystem asset. Assembly-reference tests and banned-API compilation
checks enforce the most important constraints.

## Decision boundary (2026-10-30)

All decision code lives in `Station.Mind`; Core gathers inputs, applies the returned
choice and owns every side effect. The split follows one pattern everywhere:

1. Core collects what the decider may know: an `IActorView` or `IAuthorityView`,
   candidate lists (`DispatchCandidate`), received records and small Contracts input
   types (`src/Station.Contracts/*Policy.cs`). Core status enums are mapped to Contracts
   mirrors (`KnownWorkOrderStatus`, `KnownGuardOrderStatus`, `EscortDispatchProgress`,
   `QualityCheckStage`) before they cross the boundary.
2. Mind returns a choice: a responder, a verdict and reason, an ordered list of search
   sites, a retry tick, a plan, or the first deferring check.
3. Core applies it: id counters, records, messages, scheduling, wakeups and `Emit`.

Short-circuiting priority checks whose evaluation wakes other goals are split the same
way. Mind owns the order of the checks as data (`GoalPriority.ReportGates`,
`CheckInGates`, `DailyGates`, `StartGates`; `GuardPolicy.Next` with lazily evaluated
`IGuardPreparationInputs`). Core evaluates each check, and so performs its wake, only
when Mind asks for it. Wake order, trace ids and the shared per-tick planning budget
(`SpendPlanningRequest`) are therefore unchanged. Snapshot restore validators call the
same Mind rules as live decisions (detention, cases and surveys), so the two cannot drift.

| Area | Mind types |
| --- | --- |
| Citizen goal priority and needs | `GoalPriority`, `NeedPriority`, `NeedPlanning`, `DailyPolicy`, `OffDutyPolicy`, `RoutinePlanning`, `ServicePreference`, `TerminalAvoidance`, `ConversationPolicy`, `ReportPlanning` |
| Citizen work, guards and recovery | `WorkOrderPolicy`, `WorkBackoff`, `RepairPolicy`, `RepairPlanning`, `CleanupPolicy`, `CleanupPlanning`, `RecoveryPolicy`, `CredentialPlanning`, `GuardPolicy`, `GuardPreparation`, `GuardSearchPolicy`, `GuardSearchPlanning` |
| Authority dispatch | `AuthorityDispatch`, `IncidentPolicy`, `EscortPolicy`, `FacilityPolicy`, `QualityPolicy`, `PatrolPlanning`, `AuthorityEstimates`, `ConstructionPlanning`, `ExpansionPlanning`, `VerificationPlanning` |
| Institutions | `DetentionDecisions`, `DetentionServicePolicy`, `ReprimandPolicy`, `CasePolicy`, `AccountabilityPolicy`, `AuditPlanning`, `GuardOutcomePolicy`, `RecoveryCasePolicy` |
| Shared search | `GoapPlanner`, `KnownNavigation`, `TravelEstimates` |

`StructuralTests.T00_SearchAndRankingStayInMind` fails if Core calls the planner,
builds symbolic actions or ranks by known travel cost. Core still iterates its review
queues in creation order, evicts least-recently-used knowledge (a memory model) and
co-locates guard teams (a physical handshake).

The move was verified as behaviour-identical: the 32-citizen pressure, recovery and
institutional benchmarks produce byte-identical
checkpoints and traces before and after. Known information leaks, where Core still
reads world truth to build a decision's inputs, are listed under T08 in the
[acceptance matrix](acceptance-current.md#acceptance-gates); fixing them changes
behaviour and is separate work.


## World and execution

The world owns topology, actors, physical items, configuration, seeded PRNG streams,
sequence counters and a queue ordered by `(tick, phase, sequence)`. Construction
does not run in H1: uncompleted cells are absent from navigation. Completed tiles
can share unlimited actors and items. There are no path reservations or collisions.

`AdvanceTo` consumes due events and can skip empty intervals. A tile-cost disturbance
runs before physical completion at the same tick. Completion runs before new intents,
so an arrival and a new local action at the same tick are valid. The processed marker
starts at -1, persists in snapshots and prevents submission into processed time.
Phases 3–5 now process local sensing, delayed message delivery and bounded report
planning. Physical intents remain phase 6; each selected plan step uses that executor.

Movement is one action containing a route and individual committed segments. A* uses
the real completed graph. Each departure freezes its edge cost and schedules arrival.
An actor in transit retains its last arrived tile for diagnostics, but its actor view
has no current interaction tile. Its exact progress is a rational numerator/denominator.

Item ownership is a single `(LocationKind, owner ID)` per stack. Inventory queries
derive from that state, so two representations cannot disagree. Recipe consumption and
production occur atomically at completion and create explicit signed transactions.
Balances reconcile to initialization plus these declared recipe transformations.
Outputs occupy a service output location until picked up. Installation moves one
cartridge into a machine slot and returns a displaced part to the local tile.
Declared empty-input recipes represent sources; empty-output recipes represent sinks.
Both still require a real workstation, finite work, access and atomic commit. A recipe
with neither inputs nor outputs is invalid.

Finite stack splitting creates a new monotonic item ID at the same physical location;
the source retains its ID and remaining quantity. Merging requires identical resources
in the same container/inventory and claims both stacks atomically. Its source ID is
retired. Identified credential cards cannot be split or merged. Explicit stack-change
history complements the signed recipe ledger without creating resource deltas.
Project stashes are physical containers on completed tiles with explicit project tags.
Deposit requires a held item and presence; ordinary pickup retrieves the actual stack.
This physical container does not imply that H3 project planning/construction exists.

Item transfers and work operations acquire atomic claims. Prior-tick owners retain
their resources; same-tick requests use a seeded Fisher-Yates permutation. Busy item
or workstation requests return inspectable failures. Terminal sessions retain their
existing FIFO waiting queue. Work cancellation/failure/completion releases its claims;
carried items stay put and incomplete recipes consume nothing. Staff are physically
present and reserved during a staffed operation. No movement reservation is introduced.
Inventories still need population-scale indexing; operations do not introduce global
allocation promises or automatic remote transfers.

Snapshots explicitly encode physical and information/planning state. Sorted encoding plus SHA-256 gives a
canonical digest. Runtime diagnostics, trace buffers, debug names and visuals are
excluded. Failed worlds can be inspected but cannot resume; their error is persisted.

`IActorView` exposes immutable private knowledge, published layout/services, own
inventory and own session. It never exposes a `World`, trace collection, unread
mailbox content or remote physical inventory lookup. Reads of the view do not mutate
knowledge recency. `Mind` returns intents; only Core may mutate physical state.

Terminal login/read/post consume finite time. A read captures eligible message IDs
at its start and teaches their content only at completion. Queue arbitration is seeded;
queued grants are bound to the requesting action. Sessions require a held credential
bound to the actor and account. Messages remain distinct from knowledge, and authority
beliefs follow received claims even when claims disagree with physical truth.

A resolved perception catalog gates scheduled passive observations by fact type.
Finite inspections teach inspection-only facts at completion with action provenance;
own actions can reveal their specific effects. Registered wired sensors form a separate
capability: one machine's operational boolean, an explicit sampling schedule and a
delayed authority channel. They capture truth only at measurement time and deliver
the immutable reading even if truth changes in transit. Disable/enable disturbances
control sampling; queued readings remain deliverable. Belief history records sensor
versus reported evidence without consulting truth during receipt. This channel does
not notify citizens or expose private knowledge, and diagnostic getters remain pure.

The record ledger maintains append/amend revisions and indexed current projections.
Every mutation has a persistent idempotency key. Its `IRecordStore` interface is bound
to an authenticated account/service scope; queries cannot choose their own permission
override. Normal records access exposes own credentials, sessions and work records.
Cross-account work audit requires an explicit actor/role/terminal/kind grant. No grant
exposes another actor's private knowledge, mailbox read state or live physical work.

`QueryRecords` captures an immutable versioned page at start, then revalidates presence,
terminal, session and credential at completion. Cancelled/failed operations disclose
nothing. Each disclosed revision teaches typed facts in the ordinary capped knowledge
store; a single private receipt retains the query cursor, version and completion time.
There is no unbounded per-actor record cache. History revisions have distinct knowledge
IDs, so later queries do not overwrite the original evidence. Report query fields retain
the authenticated account, terminal, submitted/received ticks and typed claim. Shared
GOAP terminal planning can plan login/query at a specified published Records Terminal;
autonomous institutional audit policy remains H3 work.

Report goals use bounded uniform-cost symbolic search over known navigation and
terminal candidates. Core executes one selected step, then replans at a safe boundary.
Planner budgets, retries, temporary knowledge pins and pending goals survive snapshots.
The same bounded search now serves repair goals. Its published work catalog contains
resource/recipe definitions and service contracts, excluding machine requirements,
installed contents and live availability. Required part types come from private facts
or finite inspection. The planner binds known part/tool/input candidates, estimated
travel between bound locations, recipe transformations and installation actions.
Stacked cartridges gain an explicit separation step before installation, including
batch-manufactured output and known parts in project stashes. Predicted outputs have
symbolic bindings until production executes; the next search
binds actual observed output IDs. No hypothetical item is submitted to the executor.

Report and repair requests share a world budget. Pending reports have explicit priority;
the repair goal remains retained, and report completion wakes it. Relevant learned
facts wake waiting repairs. Failed local pickup teaches absence at that location, then
replanning can choose another known prerequisite route. Goal satisfaction is based on
the actor's knowledge. Job records separately retain physical execution, administrative
assignment and received authority reports. Reassignment creates a new attempt; a late
report remains attached to its original attempt. Offline orders change only after a
finite read. Acceptance must be posted before the retained repair goal can run.
Suspension releases work claims and retains carried inventory; resumption revalidates
the next action. Periodic check-ins and work-result visits are own-clock/knowledge
policies, never unread-delivery callbacks. Institutional policies remain outstanding.
There is no runtime LLM or fixture-specific decision code.

Routine state separates published institutional roster versions from the immutable
version a citizen has actually read. The actor view exposes only that known payload;
publication and mailbox delivery never update it. The known version controls duty
boundaries and shift-start check-ins. Offsets are drawn from each actor's deterministic
stream at assignment. Periodic check-ins pause off duty; an end-of-shift visit and
pending reports take priority before the off-duty placeholder. A failed credential/route
plan for that visit does not invent a successful read or prevent travel to known rest.

At shift end, a moving actor requests ordinary cancellation and reaches its next tile
before stopping. Finite local work may finish, preserving its inventory and result;
another repair step waits for duty. Goals remain retained and wake at the next known
duty boundary. Rest travel uses the same bounded planner and physical executor. The
placeholder provides no need relief or sleeping capacity. When no destination is
known, the activity reports MissingRestDestination.
Known rest tiles are current configuration; berth assignment and full routine windows
remain future work. Boundary, check-in and decision watchdogs replace per-tick scans.

The restricted authority view exposes registered personnel, published roster versions,
public service contracts and nominal durations. Report estimates use Manhattan travel
from the acknowledgment's terminal to the published job location and nearest permitted
reporting terminal. Live routes, inventories, attendance and private reads are absent.
The current repair work contract uses the published installation duration; prerequisite
collection and terminal queues are uncertainty handled by slack and received feedback.

Dispatch creates an acknowledgment watch. Only received acceptance creates a work-result
expectation. Phase-4 results precede phase-5 deadlines, so exact-deadline receipt is on
time. The first qualifying result trains an integer EMA keyed by registered role and
task kind. Existing estimates/deadlines remain immutable. Overdue opens one historical
incident per attempt, without changing physical execution. A late response satisfies
responsiveness while the incident records that the task remains unverified. Cancellation
removes watchdogs, retaining history and one possible later duration sample. Authority
project planning remains outstanding.

Each missing-report incident retains a work-response goal and an independent
worker-status goal. Work dispatch uses registered qualifications, known on-duty rosters,
administrative commitments and an estimated origin from received terminal metadata or
the assigned rest location. It excludes the original assignee, ranks nominal travel,
then breaks ties by actor ID. Physical location, current action, sleep, inventory and
private order reads are absent from this decision. Dispatch posts an ordinary delayed
order; supersession posts a cancellation without changing the original worker remotely.

Fixed simulation-hour buckets capture cohort headcounts and budgets once at opening.
The first committed response spends one incident token. FIFO review skips ineligible
incidents without resetting their age and defers eligible incidents when the bucket is
full. Public roster boundaries, publication, received messages, commitment expiry and
new buckets wake the queue. Persisted replacement IDs prevent duplicate dispatch.
Replacement depth bounds recursive incidents. A replacement result records its claimed
outcome without resolving the worker-status goal. Patrol can proceed without replacement
availability, and vice versa. Both children retain one shared admission token, even if
the second is posted in a later bucket.

Patrol orders contain immutable, bounded candidate locations from permitted terminal,
job-site and assigned-rest evidence. Delivery grants no awareness. A completed read
creates the guard's local order; posting acceptance permits search. The shared GOAP
engine plans travel and finite InspectActor actions. Inspection observes only the
current tile at completion and treats actors in transit as absent from that tile.
Negative observations never disclose another location. Default Actor.Location sensing
is inspection-only; optional passive sensing remains local.

The guard retains bounded local observations and reports through ordinary terminal
planning. Only a received, authenticated result updates authority status. ReportedFound,
ReportedNotLocated and ReportedBlocked describe claims, not global truth. Work and worker
status remain separate; both positive reports yield an incident resolved by reports.
Expiry stops search at safe boundaries and queues a blocked report. Finite inspection
can finish across shift end; further search waits for the guard's known next duty.

No decisions, action outcomes or transitions are selected by a scenario name. The
runner supports explicit scripted commands for controlled fixtures and autonomous
report goals for policy execution. Trace causes identify the latter's planning events.

Cleanliness is an optional physical subsystem over completed tiles whose content declares
`Cleanable`. Its single named `soiling` stream draws bounded integer intervals in stable
tile-ID order, independently of actor/arbitration randomness. Disturbances add dirt in
phase 1; finite cleaning subtracts at most one currently present unit in phase 2. Local
observations follow in phase 3. Remote actors and authority beliefs receive no implicit
update. Tools/staff use existing claims; cleaning does not consume the tool or exclusively
reserve the tile. Per-tile initial/add/remove counters prove conservation without a growing
dirt transaction ledger. Schema 15 stores these counters, configuration, timers and RNG.

Jobs and private orders distinguish RepairMachine and CleanTile targets. Published cleanup
rules contain positions, roles, tools and unit durations, never live dirt. Cleanup's
bounded Mind planner chooses known tool pickup, travel, a finite arrival inspection and
one cleaning step at a time. Each new movement invalidates its prior arrival check.
The local goal finishes only from checked, known zero dirt; it posts NoWorkNeeded if it
removed nothing itself. Multiple retained cleanup orders execute in stable attempt order.
Suspension/expiry/cancellation preserve inventory and use existing safe-boundary behavior.

Authority cleanup estimates use the job's reported workload and public action/travel
durations, with the same received-acceptance deadlines, cohort admission and independent
patrol branch as repair. Changed physical dirt or traversal cannot reset the estimate.
The canonical missing-cleanup fixture is autonomous. Facility-condition reporting and
automatic task creation from received observations remain pending, as do general
authority capacity/project planning and the later institutional workflows.

## Facility observation to assignment

Local successful Direct/Inspection learning invokes the configured reporting duty.
A newly adverse condition queues an ordinary report goal. Re-observation after the
repeat interval can queue another report; an already pending report suppresses
repeats. Locally observing recovery resets this condition transition. Seeded,
received, queried and heard facts cannot trigger this duty. The citizen must reach
an accessible terminal, authenticate and finish posting before any authority receipt.

Receipt deduplicates account/operation keys and merges active tasks by typed subject.
A new task attaches an existing compatible open job or allocates a monotonic ID.
Phase-five dispatch uses published qualification, roster and known commitments and
origins; it does not inspect actual dirt, faults, tools or worker positions. Assignment
uses the existing mailbox/read/acknowledgment/work/result path. Pending tasks wake on
known roster boundaries, commitment expiry and institutional updates.

A current-attempt completion resolves the task only if its observation is at least
as recent as all received adverse observations. Otherwise another attempt is queued.
Late adverse evidence at or before reported completion does not reopen work; newer
evidence creates a new job. Rejection/blockage retains NeedsAttention until fresh
condition evidence arrives. Ordinary facility dispatch does not consume overdue
incident admission tokens. This is a bounded dispatcher, not capacity/project GOAP.

## Machine fault physics

MachineFault and automatic wear are phase-one disturbances. Their sole material
change is exhausting the current single-unit installed cartridge into the declared
fault sink. Faulting an empty slot still records the event without another material
effect. This avoids representing a failed cartridge as an apparently usable spare.
Fault sinks reconcile by resource with initial stock and recipe transactions; decimal
accounting intermediates avoid overflow when lifetime production exceeds Int64 while
current stock remains valid. Stored physical quantities and sink counters remain Int64.

This event cannot change an authority belief, create a task or remotely update a
citizen's knowledge. Colocated, stationary observers receive normal passive sensing;
existing sensors sample through their configured phase and latency. RequiredResource
remains inspection-only by default. The facility duty then uses ordinary local
knowledge and terminal reports. Phase-one faults precede same-tick installation
completion, so a newly completed replacement survives that earlier fault.

A separate named wear stream owns deterministic bounded intervals. No diagnostic or
manual-fault path advances it. Automatic wear is explicitly configured, keeping
small scenarios free to state their own scope and timings. QA scheduling and finite
supervisor verification remain a separate next implementation step.

## QA receipt policy and supervisor verification

QA samples only accepted repair completion receipts, once per repair attempt, without
reading machine truth or the worker's private state. A separate qa stream and persisted
sampling decision make retries deterministic. The retained due check selects a known
eligible supervisor using role, roster, prior terminal/rest origin and commitments.
This is the current bounded authority dispatcher; broader capacity GOAP remains pending.

VerifyMachine integrates with generic work attempts and ordinary mailbox delivery.
Its two-predicate bounded GOAP search requires navigation plus a finite inspection.
A private result is captured only when all three relevant machine facts were learned
by that completed inspection action. Physical execution, local captured evidence and
authority result receipt remain distinct. A later fault cannot rewrite an earlier
inspection already being carried to a terminal.

Captured discrepancies retain ResolveJobDiscrepancy on the local order and open a
private audit goal. Overdue verification uses the existing incident budget and
qualified supervisor replacement path, never a repair action.

## Discrepancy audit and accountability

An audit uses the lowest-ID explicitly assigned desk with both work-record grants.
The citizen plans ordinary travel, authentication and finite queries through Mind's
terminal GOAP planner. Reports and check-ins take priority; known duty constrains
new audit steps. Action completion/failure and duty boundaries wake retained goals.
Terminal release preserves a session needed by an active audit and releases it when
the audit concludes. Inaccessible desks use ordinary planner/executor retry behavior.

WorkAttempt records include the declared required resource, never installed-part
truth. Each learned record includes its field count so a partially learned row cannot
silently become complete evidence. The audit reads only facts disclosed by the current
completed query. It retains at most 32 relevant immutable rows and their query action
IDs; the whole scan allows at most 64 one-row pages. Exceeding a bound ends in review.

Each record kind has a cutoff fixed by its first completed query's captured version.
These are two explicit versioned scans, not one atomic cross-kind transaction. The
finding retains both versions and their maximum for later service validation.
Snapshot validation checks original record fields, limits, timers, assessment and
complete retained history for a finished finding. It does not execute citizen queries.

The pure Mind policy unique-assignment/v1 takes captured required/installed types,
inspection time, and disclosed administrative records. It requires one repair
assignment, matching declared specification, an authenticated acceptance and positive
completion before inspection, and no conflicting amendments. Multiple historical
repair assignments conservatively mean AmbiguousHandoff. Missing parts or a fault with
the correct installed type are not attributed by this policy. A positive finding
identifies assigned-account responsibility, never claims to identify a physical
installer. ReadyToFile is private evidence. With personnel configured, it queues a
separate filing action; the audit itself does not append a reprimand.

## Personnel case service

Finite work-record queries can produce institutional disclosure receipts identifying
the querying actor/account, terminal, action, disclosed revision, captured version and
completion time. These prove service disclosure, not continued citizen memory or the
truth of a worker's claims. Citizen audit decisions still use only their learned facts.
The registry is separate from developer traces and is included in save state.

Verification results include a typed authenticated inspection submission when personnel
is enabled. The private filing goal combines its own posted inspection-report reference,
audit cutoffs and query references. It uses the ordinary report planner and post action.

On receipt, personnel validates requester role, authentic transport, the accepted
verification report, known resource types, assigned-desk disclosure provenance, complete
relevant history, unchanged relevant work records and the pure accountability policy.
It never checks current machine state, physical activity, inventory or private audit
state. Authorized reports remain claims; validation establishes provenance and policy,
not physical truth or guilt.

The case key is job/attempt/single-machine-slot/violation. Accepted cases and reprimands
are indexed separately and appended to scoped institutional records. Operation keys
detect conflicting retries; a canonical case appends at most one reprimand across new
filing and verification-report IDs. The service replies through an ordinary delayed
account mailbox. Only a completed authenticated read updates the private filing result
and receipt-time qualifying count. Counts currently use the configured inclusive time
window; completed-detention exclusions will be integrated with the custody lifecycle.

Snapshots retain disclosure proofs, local report/filing references, canonical cases,
unique reprimands and decision replies. Restore revalidates accepted cases and replies
against the institutional history at the decision tick, so later amendments do not
erase an already accepted historical case. Decision-time case and record cutoffs also
preserve ordering when acceptance and amendments share the same tick.

## Detention request and authorization

A completed read of an accepted case reply whose count reaches the configured threshold
creates a private RequestDetention report goal. Delivery alone creates no goal. Normal
GOAP posting, session validation and transport timing apply; an authority decision is
separate from request submission and from the requester's eventual read of its reply.

Authority validates requester permission, the issued accepted-case reply addressed to
that account, accepted case ownership, the current qualifying count and existing
authorizations. It uses institutional records and registered identity, without reading
the target's activity or position. Missing approval policy records an explicit wait.
The time-window count is recalculated at receipt, so a previously qualifying reply can
produce ThresholdNotMet. One unexpired authorization per subject account is retained.
Duplicate/fresh request IDs can reuse it without extending its expiry or sentence.

An authorization stores its known target identity, source request/case, justifying case
IDs, issue-time case cutoff, sentence duration and expiry. Expiry closes that record.
An identical retry of a closed request cannot open another authorization; a fresh
operation may be evaluated after expiry. The old expiry timer cannot remove a new
authorization issued in the same tick. These records do not yet form guard teams,
start custody, occupy detention space or schedule a release. Escort fulfillment must
bind to this unique authorization in the next implementation stage.

Snapshot state includes request operation keys and decisions, independent private
request/reply progress, authorizations and expiry timers. Historical counts use the
case cutoff captured at each decision, rather than all cases accepted during that tick.
Personnel case validation likewise retains its exact institutional record cutoff.
