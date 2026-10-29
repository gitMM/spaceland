# Decisions

## Combined fixed-station pressure — recovery corrections

- The user approved explicit evidence-based review outcomes for insufficiently
  attributable cases. An unfinished audit, unread decision, missing grant or invalid
  provenance still fails. Every subject who qualifies for custody must complete
  release and return to duty; the clear-day requirement is unchanged.
- A cleaner retains a fresh zero-dirt observation as local completion immediately
  after inspection or cleaning. An intervening need break must not erase completed
  work and require another visit next shift. The completion report still travels
  through the ordinary queue and terminal; authority learns only on receipt.
  The regression fails before this correction and verifies exact continuation
  afterward. Recovery seed 7 now has no overdue report, so its coverage gate must
  reject it for that reason alone, like seed 91; seed 42 still exercises recovery.
- A formed team's finite search allowance is one configured roster cycle, capped
  by authorization expiry. This gives a late-shift formation its next duty interval
  after sleep. Schema 45 validates the revised deadline. The schema-44 one-shift
  behavior below is historical.
- Replacement searches lower the priority of an old terminal clue after receiving
  a negative search report newer than that visit. The clue remains a candidate;
  a newer authenticated visit restores its priority. Private search state is absent
  from this decision.
- Before starting custody, each guard forecasts their own already-urgent needs and due toilet visit
  through the known route and published intake duration. A predicted critical need
  permits ordinary relief first. Preparation persists through travel and relief;
  the target is reinspected afterward. Each co-located participant can refuse the
  handoff based on their own readiness. Failed relief retains its existing bounded retry;
  no remote partner needs, service occupancy or physical route truth are consulted.
- A local intake refusal due to full capacity, busy service, or outage remains
  known until a co-located service change wakes the waiting leader. Ordinary
  routine wakeups do not erase the refusal or generate repeated failed actions.
- A received original reply closes an unposted welfare search. An original
  completion also cancels an unposted replacement. Already-issued patrols and
  replacement attempts retain their independent received outcomes.
- Snapshot validation follows replacement ancestry only after all branches have
  been read, and validates the actual descendant report rather than inventing a
  direct child or original-worker response.
- Guards who leave an identified target or fail a co-located custody handoff must
  inspect again before attempting custody. A formed team's search has a local
  one-shift deadline that survives routine breaks. A guard reports `TeamNotReady`
  through ordinary terminals; only received terminal outcomes permit replacement.
  This adds `SearchAbandoned` to persisted states and advances the schema to 44.
- Audit failures retain increasing backoff until login or a records query succeeds
  at the audit's assigned terminal. Merely planning another attempt is not progress.
- Dispatch checks completed escort status before scanning received guard history.
  Both predicates are read-only; candidate lists, saved state and continuation
  events remain identical, while completed cases stop adding history-scan cost.
  Active guard checks use the ledger's existing record-kind history index, which
  is rebuilt on restore; no additional saved or authoritative state is introduced.
- The pressure fixture places assigned bedrooms and matching published rest tiles
  across the maintenance corridor from daily meals, providing ordinary observation
  coverage. Its gate requires a full configured day with backlogs remaining clear;
  the nine-day population run has not yet met that condition.
- Benchmark checkpoints use a schema-2 JSON manifest and a sibling binary payload.
  The population end state exceeded JSON's single-string limit when base64-encoded.
  Both payload SHA-256 and canonical-state digest are checked on restore. Inline
  schema-1 checkpoints/replay bundles remain supported. This container-format fix
  did not change Core schema 44 at introduction; the later guard-deadline change
  above advances current Core snapshots to 45.
- Rechecking an assigned task after release counts as resuming work even when
  another worker already completed the physical task. No extra dirt is injected
  solely to force the released worker to remove a unit.
- A released subject whose assignments are all terminal may return to idle duty:
  the workload requires a later inbox read and a later on-duty boundary. It reports
  this separately from resumed work and still requires actual resumed work by at
  least one released subject.
- Initial misconduct actions are contiguous; the 2,048-citizen generator test
  verifies all 128 bad installations physically occur. Each guard starts with one
  finite kit in inventory and equips it autonomously. The combined workload tests
  shared staffing and custody rather than recall of remote equipment stock.

## Integration and scale — 2026-10-29

| Decision | Requirements | Reason |
| --- | --- | --- |
| One current acceptance matrix, historical increment reports retained | §11 | Separate existing test evidence from missing gate coverage without rewriting history. |
| Explicit one-hour configured day and physiological/service scaling | R-LIF-02, T22 | Exercise two recurring days quickly; preserve need thresholds and per-day accumulation. Work/terminal durations are separately declared. |
| Initial cleaning orders and sustained physical dirt backlog | T15, T22 | Verify every worker's real actions and routine priority under available work; do not script responses. Full population institutional chains remain open. |
| Per-request known-distance frontier reuse | R-KNW, R-MOV, R-ACT | Avoid repeated graph reconstruction/search for service candidates; cache only immutable allowed knowledge and discard after planning. |
| Derived actor timer and daily-goal indexes | R-CLK, R-SNP | Remove global scans while preserving canonical order and snapshot bytes. Reconstruct indexes on restore. |
| Bounded diagnostic trace retention in benchmark | R-OBS, R-DET | Drain each 1,000 ticks without changing event IDs or digests. Small daily causal proofs retain their full history separately. |
| Separate one-day population participation from two-day conversation acceptance | T15, T22 | A citizen lacking a completed conversation in the shorter run is reported, not incorrectly claimed to fail the longer gate. |

| Decision | Requirements | Reason |
| --- | --- | --- |
| Standalone solution under `ImperialStation/`, outside `Assets` | R-ARC-01..02 | The user explicitly requested an independent system. No Unity imports, scene changes or reuse of prototype simulation code. |
| Implement and verify the foundation first; keep the full release goal outstanding | §3–4 | H2–H4 build on determinism and information isolation. Later behaviors are not represented by stubs or claimed from empty data. |
| SDK 9.0.311, runner/tests net8.0, core libraries netstandard2.1/C# 9 | R-ARC-06..07 | SDK 10 is not installed; SDK 9 and the .NET 8 LTS runtime are available. No `init`, records or shims in shared assemblies. |
| PCG-XSH-RR 64/32, FNV-1a UTF-16-code-unit stream keys and SplitMix64 seeding | R-DET-02 | Explicit wrapping arithmetic, documented version, exact state persistence and independent named streams. Only ASCII stream keys are currently created. |
| Sorted dictionaries/sets and stable insertion sequences | R-DET-04, R-CLK-05, R-MOV-07 | No consequential hash enumeration. A* ties use insertion order; neighbors use the specified axis order. |
| A* execution; independent Dijkstra mode as oracle; no route cache | R-MOV-06..09 | Keep the first implementation auditable. Minimum traversal cost is recomputed on every cost edit. No cache can go stale. |
| Frozen route and current segment; later edge durations read at departure | R-MOV-03..05 | A cost disturbance does not interrupt a committed route or teleport the actor. A newly requested route uses current costs. |
| Full stacks transfer with finite duration; no split/merge/recipes in H1 | R-RES-01..03 | Transfers validate at start/completion and conserve quantities. Resource production, consumption and claims belong to H2. H1 completion ordering is not H2 arbitration fairness. |
| Canonical explicit binary encoding in JSON checkpoint envelope | R-SNP-01..04 | No arbitrary-object-graph serialization. UTF-8 strings, little-endian integers, fixed field order, sorted entities/events, explicit version and model identifiers. Core uses memory streams only; Runner owns files. |
| Debug names and visual keys excluded from consequential digest; logs excluded entirely | R-SNP-04..06, R-TIL-06 | Presentation changes and trace collection cannot change outcomes. Sequence counters still advance when tracing is disabled. |
| Snapshot retains remaining scheduled commands | R-SNP-01, R-SNP-05 | Replay bundle carries scenario/content and initial checkpoint; commands need no separate duplicate scheduling during replay. |
| Schema metadata lives in `initialState` | R-VER-01, R-RUN-03 | Root accepts only the five permitted sections. Unknown properties, explicit null, duplicate properties, invalid enum values and unsupported disturbances fail with paths. |
| Item locations admit only existing H1 owners | R-RES-01 | Project stash, machine slot and service output are enum extension values. Loader rejects them until the corresponding systems validate real owners. |
| Tile capabilities are metadata only in H1 | R-TIL-06..07 | Family-specific service rules, recipes, work capacities, equipment and reservations are later-owned. No service behavior is inferred from a family or fabricated. |
| `IAuthorityView`, `IInstitutionView`, `IRecordStore`, `IMessagePayload` are explicit minimal contracts | R-ARC-04, R-RES-03 | H1 has no institutional or knowledge behavior. Full schemas and access scoping arrive with H2/H3. |
| Local NuGet cache inside solution; exact package lockfiles | R-ARC-06 | The entire solution and tests can move independently. Normal restore audits dependencies; offline verification used `NuGetAudit=false` only as a command override after successful online restore. |
| JSON results distinguish assertion/runtime outcomes and ordinary action failures | R-RUN-02..05 | The exact fixture's early pickup deliberately fails but all assertions pass. Per-action path-budget exhaustion is not misreported as no-route. |
| Viewer shows an initial projection and replays recorded movements | R-OBS-08 | Browser floats are display-only. Canonical state remains in the core checkpoint. Large Int64 tick values beyond JS safe integers are rejected by the trace loader. |

The current binary snapshot schema is version 44. The historical version-9 increment included knowledge, terminals,
messages, beliefs, report/repair planning, public work catalog, resource claims and
physical work, attempts, local order copies, check-in policy, scoped record queries,
stack operations, physical stashes, resolved perception rules, sensor packets/schedules,
roster payloads and private routine watchdogs. Further state must extend encoding and
tests immediately. Earlier unpublished schemas are rejected.
No migration guarantee for unpublished development checkpoints is made.

## H2 decisions

| Decision | Reason |
| --- | --- |
| Typed immutable facts, explicit provenance, bounded deterministic LRU and pins | Citizen decision code cannot inspect physical truth or unread messages; pure getters cannot alter outcomes. |
| Separate physical state, mailbox delivery, learned knowledge and authority beliefs | A false report changes belief only after delivery and never moves a resource. |
| Holder-bound keycards and finite login/read/post with completion revalidation | Possession of another actor's card cannot impersonate its account; cancellation or failure cannot finish an operation silently. |
| A read captures delivered IDs at start | Mail arriving during a read requires a later operation. |
| Action-bound terminal queue grants and seeded same-tick login arbitration | Stale grants cannot consume another action's capacity; simultaneous contention is replayable. |
| Uniform-cost forward search with state/depth dominance and bounded terminal candidates | The planner finds cheaper feasible symbolic alternatives while retaining shorter paths needed by depth bounds. |
| Execute one report plan step and replan | Ordinary actions encounter actual physical state; hidden cost changes affect execution without leaking into decisions. |
| Pending goals, planner budgets, backoff and temporary pins are snapshot state | Continuation cannot reset contention, retry timing or knowledge retention. |
| Reporting and repair use one bounded search engine | Generic symbolic tests cover search mechanics; actual autonomous T10 chains now exercise the shared physical executor. |

## Physical work decisions

| Decision | Reason |
| --- | --- |
| Same-tick claim requests share a Fisher-Yates permutation from the arbitration stream | A controlled test covers all permutations for three requests; 64-actor diagnostics and replay test 1,000 seeds. Existing owners retain priority. |
| Work contention fails with an inspectable reason; terminal sessions retain FIFO queues | Work has no implicit background retries. Goal policy will decide retry/suspension, preserving original task identity. |
| Bind inputs/tools, validate everything, then acquire all claims | Partial failures cannot strand materials or reserve part of an impossible recipe. |
| Recipe inputs are carried and consumed only at completion; outputs require pickup | Physical presence and inventories remain meaningful, and cancellation never teleports or consumes carried materials. |
| Resource transactions identify recipe, actor, action and tick | Resource accounting reconciles against explicit transformations; restore checks the declared recipe deltas. |
| Installation distinguishes physical fit from required specification | Fitting wrong parts enter the slot and remain physically real. Replacement leaves the old part on the tile. Functional state follows the installed part. |
| Staff actors hold exclusive coordination claims during work | A staffed service cannot operate while its staff is elsewhere or simultaneously moving/working. |
| Recipes cannot consume registered credential cards | Credential lifecycle owns destruction/replacement and must preserve its record references. |
| Work-actions scenario remains explicitly scripted | It verifies executor behavior including wrong installation. Separate autonomous repair fixtures demonstrate T10/T23 choices without scripts. |

## Autonomous repair decisions

| Decision | Reason |
| --- | --- |
| Publish work service contracts and recipes, never required/installed machine parts | Planning uses the actor view; inspection or other permitted knowledge supplies the specification. |
| Ground capped known item, tool, input, recipe and workstation candidates | No physical inventory enumeration enters Mind. Hypothetical states remain within 64 predicates; binding overflow is explicit budget exhaustion. |
| Model travel between every pair of bound locations with known costs | Collecting inputs and returning to fabrication are costed correctly; travel costs are not reused from the initial position. |
| Represent future output symbolically, execute only the first step, then replan | Fabrication must physically complete before a real item ID can be picked up or installed. |
| Direct recipes supply installable cartridges; batch outputs require separation | Single- and multi-cartridge outputs use the same physical executor. Nested recipe chains remain outstanding. |
| Finite inspection as a knowledge subgoal when the specification is unknown | The planner cannot bind a secret required resource from the machine's physical definition. |
| Report-first arbitration and a shared per-tick request budget | Two active goal types cannot issue competing intents for one actor; retained work resumes after posting. |
| Local failure teaches absence; remote existence is not disclosed | A nonexistent item and an item elsewhere produce the same local inspection/inventory result. |
| Preserve published catalog and repair policy state in snapshots | Restore cannot silently replace beliefs/catalog versions with newer physical truth or reset planning backoff. |

## Job and communication decisions

| Decision | Reason |
| --- | --- |
| Separate assignment, physical execution, private order copy and authority projection | R-RES-04..08/R-MSG-18: remote reassignment cannot stop distant work; false or delayed reports cannot change physical truth. |
| Acceptance starts work only at actual post completion | R-MSG-18: an accepted plan or mailbox read alone does not authorize the worker to leave. Authority learns acceptance on receipt. |
| Account-scoped report operation keys, persistent dispatch/cancel keys | R-INS-04: duplicate requests cannot create attempts or mutate report history twice. |
| Explicit own-clock check-ins, report priority, work-result read goals | R-MSG-15: unread messages do not trigger policy. Due visits wait for a safe action boundary. Shift staggering and awaiting-response policy belong to later roster/institution work. |
| Deadlines use the H3 roster and restricted authority evidence model | R-AUT-10..14: expectation creation, off-duty extension and report-based learning are implemented together; dispatch alone never starts the work-result deadline. |

## Record-query decisions

| Decision | Reason |
| --- | --- |
| Scope-bound `IRecordStore`, indexed current projections and immutable revision history | R-INS-02..05: callers cannot select their own account/permission override; every amendment has a stable idempotency key. |
| Own-account default; explicit actor/role/terminal grants for cross-account work audit | R-MSG-11: an arbitrary terminal or supervisor role alone grants no personnel/job audit access. |
| Capture the versioned result at query start, disclose only after completion revalidation | R-MSG-07..09: record changes during the finite operation do not silently enter its result; cancellation or lost authorization discloses nothing. |
| Teach revision fields in bounded ordinary knowledge; retain one cursor receipt | R-KNW-06..09: history does not become an unbounded private record cache, and query pagination remains available through the actor view. |
| Formal work rows exclude execution truth and private reads | R-RES-03/R-MSG-14: audit is evidence of submitted claims and administrative actions, not omniscience. |
| Shared terminal planner accepts a typed query and known target terminal | R-ACT-11: login and query use the common action executor. Autonomous institutional policy belongs to H3. |

## Resource-flow decisions

| Decision | Reason |
| --- | --- |
| Finite split/merge at the same physical location with an explicit ledger | R-RES-01..02: quantities stay exact, no transfer is hidden inside merging, and identities/claims survive continuation. |
| Same-resource, same-container merge; both item claims acquired together | R-RES-09: overlapping requests cannot partially claim or consume the same stack. |
| Credential card IDs cannot be split/merged | Credential lifecycle owns changes to identified cards; stacking must not invalidate its references. Cards can still be physically carried/stored. |
| Empty input/output sides declare sources/sinks through ordinary recipes | R-RES-02,13..14: access, work duration, staffing, capacity, cancellation and explicit accounting still apply. |
| Project stash is a real container with a project association tag | R-RES-01,03: a tag does not create a project or teleport allocated materials. H3 will own operational construction/project records. |
| Repair separation retains one cartridge on the bound source ID | The remainder gets a fresh ID at physical completion. No invented future ID reaches the executor; replanning binds actual produced stacks. |

## Perception and sensor decisions

| Decision | Reason |
| --- | --- |
| Resolve supported local fact types to Passive or InspectionOnly, with content overrides | R-KNW-04: observation is an explicit rule; finite item/machine/tile inspections teach at completion with inspection provenance. |
| Register one-machine operational-status sensors with explicit schedule and authority channel | R-KNW-11: scope is narrow and inspectable; no implicit cameras, private citizen state or live inventory feed. |
| Capture immutable samples before delayed delivery | R-KNW-10..11: evidence retains the observation time and may already be stale when received. Receipt never checks truth. |
| Disabling stops future measurements; re-enabling retains the original schedule | Phase-1 controls precede sampling; in-flight packets remain evidence of real prior observations. |
| Receive human and sensor evidence through one history projection, preserving distinct provenance | R-KNW-10: latest arrival/sequence wins and contradictions compare prior evidence; human posts cannot claim sensor provenance. |
| Store pending sensor work and complete perception rules in schema 8 | R-SNP/R-KNW-12: continuation preserves payloads, delivery order and future sampling, while getters do not observe or consume randomness. Failed snapshots are diagnostic only. |

## Initial H3 routine decisions

| Decision | Reason |
| --- | --- |
| Configure station day, cohort spacing, duty duration and check-in windows explicitly | R-LIF-02..03: default day is 864,000 ticks; fixtures report their shortened day rather than calling it 24 hours. |
| Keep institutional roster history separate from the citizen's read payload | R-LIF-04/R-MSG-07: publication and delivery grant no awareness. Initial known versions are explicit; later changes require authenticated finite reads. |
| Pin one known roster version; carry its immutable schedule through the restricted actor view | R-KNW-08/R-ARC-04: private planning uses the read version, not the latest institutional record. |
| Draw the shift-start offset from the actor stream at assignment | R-MSG-16: assignments are reproducible, idempotent and staggered; queries do not draw randomness. |
| Stop movement at a tile boundary and allow finite local work to finish | R-LIF-03: no teleporting or abandoned claimed resources. Retained goals resume on duty; known completed repairs can still be recognized off duty. |
| Go to an explicitly known rest tile and hold OffDuty | H3 placeholder: no invented berth, capacity, sleep or need relief. Full needs and assigned-berth behavior remain later work. |
| Check in after duty; if no credential/known route exists, preserve failure and proceed to rest | Existing finite communication rules remain authoritative. This is not credential recovery or proof that a report was submitted. |
| Newer roster versions supersede older scheduled publications | R-INS-03..05: stale callbacks cannot roll back current records or citizen schedules. Idempotency keys preserve RNG and version identity. |

## Authority expectation decisions

| Decision | Reason |
| --- | --- |
| Create expectations only from authenticated received acceptance | R-AUT-10: posting, delivery to a citizen and private reads are different events. |
| Estimate using the acknowledgment terminal and published service contracts | R-AUT-11: no real path, live machine state, inventory or attendance enters authority planning. Missing evidence produces Unestimated without a watchdog. |
| Freeze estimate, learned ratio and roster version at receipt | Later roster publications and learned corrections affect new expectations only. |
| Add one complete known off-duty gap plus check-in window | R-AUT-12: compare the base deadline with the end of the shift whose start most recently preceded receipt; do not repeatedly stretch multi-day deadlines or infer actual attendance. |
| Apply checked integer EMA once per attempt's first qualifying result after acceptance | R-AUT-13/16: repeated and unrelated reports do not add samples. Results arriving before acceptance remain recorded claims without a retroactive duration sample. |
| Keep responsiveness separate from claimed outcome and physical truth | R-AUT-14/16: a blocked or misleading result can satisfy reporting; overdue means status unknown. |
| Preserve one incident history row per overdue attempt | Late results update its response evidence. Patrol/replacement admission, dispatch and closure policy are not invented by this increment. |
| Persist authority state and validate watchdog ownership in schema 10 | Continuation retains deadlines, estimate provenance, EMA samples, acknowledgment watches and incident identity. |

## Incident response decisions

| Decision | Reason |
| --- | --- |
| Choose replacements from registered role, known roster, administrative commitments and received terminal/assigned-rest coordinates | R-AUT-09/17: nominal cost and stable actor-ID ties avoid live positions, private reads, needs and inventory. |
| Treat unknown roster or origin as ineligible | Missing availability evidence is not permission to assume on-duty presence. |
| Snapshot cohort budgets at fixed millisecond-based hour openings | R-AUT-15: mid-bucket publications affect eligibility and future budgets, not existing headcount snapshots or tokens. |
| Spend one incident token only on its first posted response | No eligible responder spends nothing; linked child identity persists through retries and continuation. Patrol can later share the existing token. |
| Review by opening tick/sequence, skipping ineligible entries without resetting age | Busy or unavailable roles do not starve eligible later incidents. Budget exhaustion defers rather than duplicates dispatch. |
| Post replacement and cancellation through ordinary transport | R-WF-21/26: dispatch neither grants awareness nor cancels distant physical work. |
| Cap recursive replacement depth independently of unresolved incident count | R-AUT-15: a missing report cannot grow an unlimited worker chain. |
| Keep claimed replacement result separate from PendingPatrol | R-WF-25: a repaired machine does not establish the original worker's status. Patrol execution remains outstanding. |
| Serialize queue, buckets, tokens, ancestry and result evidence in schema 11 | R-SNP: restored admission cannot reset its budget, spend twice or attach a late original report to a child. |

## Patrol decisions

| Decision | Reason |
| --- | --- |
| Bind a bounded immutable search list into the posted order | R-WF-22: received terminal evidence, job site and assigned rest location are permitted clues; live worker positions are absent. |
| Read before adoption and post acceptance before search | R-WF-21/R-MSG: delivery cannot grant awareness, and a plan to acknowledge is not a completed post. |
| Use ordinary movement and finite InspectActor with a typed target identity | R-ACT/R-WF-22: target presence is checked locally at completion; transit and remote targets yield only AbsentAt here. |
| Keep Actor.Location inspection-only by default | R-KNW: optional passive mode observes co-located actors; the default avoids both ambient tracking and an unnecessary population scan. |
| Keep local search evidence bounded by the order's candidates | Failed, cancelled or interrupted observations never create a positive result; observations survive shift boundaries and continuation. |
| Report findings through the shared terminal planner | R-WF-22/25: only receipt updates institutional status; finding a worker does not prove work completion. |
| Share one admission token across independent patrol/replacement children | R-AUT-14/15: either can proceed first; the later child does not spend another token or get blocked by a new bucket's budget. |
| Treat negative and blocked patrol results as unresolved worker status | A finite unsuccessful search is neither proof of global absence nor a completed welfare investigation. Automatic follow-up is still future policy. |
| Preserve causal order/inspection/report links and distinct branch result keys | Replay traces explain the response chain; simultaneous repair and patrol receipts cannot collide in the record ledger. |
| Snapshot the typed payload, private read state, tagged action and expiry in schema 12 | Restore cannot manufacture awareness, reset the search or charge another admission token. |

## Cleanup foundation decisions

| Decision | Reason |
| --- | --- |
| Enable cleanliness explicitly on completed Cleanable tiles | R-WF-01; existing fixtures keep their prior physical scope, and unsupported tiles cannot receive spills. |
| Draw each bounded timer from the named soiling stream | Dirt generation stays deterministic and independent of movement arbitration and diagnostic reads. |
| Clean one unit per finite action; recheck at completion | R-WF-23; stale orders and simultaneous cleaners cannot remove absent dirt. |
| Reserve tools/staff without exclusively locking the tile | Existing claim ownership protects tools while completion checks handle overlapping workers. |
| Track initial, added and removed units per tile | Nonnegative integer conservation remains inspectable without an unbounded per-unit ledger. |
| Keep the physics fixture distinct from missing_cleanup_report | The canonical T19 gate also requires autonomous cleanup orders, acceptance and terminal results, which are still pending. |

## Cleanup order decisions

| Decision | Reason |
| --- | --- |
| Add typed CleanTile targets alongside RepairMachine | Tile work uses its own semantics while retaining common attempts, read copies, reports and cancellation. |
| Publish cleanup rules but never live dirt | Planners and authority dispatch receive capabilities and declared durations without access to hidden state. |
| Estimate from recorded reportedUnits and freeze on acknowledgment receipt | A spill or changed physical route cannot silently extend an institutional expectation. |
| Plan known tool pickup and one finite inspection/cleaning step at a time | Bounded search stays grounded in observed state and rechecks changes at completion. |
| Invalidate arrival checks on movement and suspension | An old clean observation cannot skip a new visit's local verification. |
| Report NoWorkNeeded on the original or replacement's own attempt | A clean tile consumes no dirt; late original evidence cannot complete the child attempt. |
| Serialize concurrent retained cleanup goals by attempt ID | One citizen issues one physical cleanup intent at a time. |
| Keep negative patrol results unresolved | Completing cleanup does not establish the missing worker's location or welfare. |
| Run missing_cleanup_report without scripted actors | T19's orders, acceptance, movement, local work and reports now arise through ordinary policies and actions. |

## Facility workflow decisions

| Decision | Reason |
| --- | --- |
| Make facility reporting an explicit optional duty | Existing fixtures remain scoped; reporting roles are registered facts. |
| Trigger only on successful local observation | Hidden state and secondhand claims cannot silently generate citizen goals. |
| Retain report evidence through normal terminal work | Observation, posting, receipt and worker acknowledgment remain distinct. |
| Deduplicate account/key and merge by typed target | Multiple observers do not create competing tasks for the same active condition. |
| Compare adverse and completion observation times | Transport order alone cannot turn stale completion into current resolution. |
| Preserve failed tasks until fresh condition evidence | This increment avoids unbounded immediate retry loops; broader recovery remains pending. |

## Machine fault decisions

| Decision | Reason |
| --- | --- |
| Exhaust the installed cartridge through a declared sink | A failure requires real replacement stock and cannot recycle a failed item as a valid spare. |
| Preserve inventory, pending installation inputs and tools | Disturbance effects are local to the installed slot and preserve ordinary work ownership. |
| Count empty-slot failures without consuming material | Repeated or simultaneous faults do not duplicate resource effects. |
| Use an independent wear stream with symmetric integer intervals | D-77's mean is exact even for odd scenario overrides; other streams remain independent. |
| Keep manual faults separate from wear scheduling | Scenario disturbances do not silently alter future random draws or timer phase. |
| Retain phase-one fault / phase-two completion order | A replacement completed after the fault in the same tick is a new physical repair. |
| Do not allow credential cards in consumable slots | Credential lifecycle owns these physical identities; machine wear must not destroy its references. |

## QA verification decisions

| Decision | Reason |
| --- | --- |
| Sample the first accepted completion per repair attempt | Receipts trigger QA; hidden work and duplicate message IDs do not. |
| Always record one qa-stream draw per eligible attempt | Rate boundaries and retries have explicit reproducible evidence. |
| Exclude the source worker from supervisor selection | Verification uses an independent known eligible responder. |
| Add VerifyMachine to ordinary typed work attempts | Existing order/read/acknowledgment/cancellation/lease machinery retains its meaning. |
| Require a new finite inspection for every verification attempt | Stale or seeded knowledge cannot satisfy the physical verification obligation. |
| Capture inspection facts and observation time before terminal travel | Later physical changes cannot mutate evidence already collected. |
| Keep discrepancy evidence without attributing the installer | The following audit must earn that attribution through permitted records. |
| Route overdue verification to another supervisor | Missing inspection reports must not silently become repair jobs. |

## Discrepancy audit decisions

| Decision | Reason |
| --- | --- |
| Use explicit paired record grants at one assigned desk | An audit cannot widen its access by selecting a convenient terminal. |
| Retain only complete actually learned rows | Knowledge eviction or partial disclosure cannot manufacture evidence. |
| Bound pages and retained evidence separately | Large histories produce review rather than an incomplete accusation or unbounded scan. |
| Freeze each record kind's captured version | Concurrent new records cannot make a scan run forever; both versions remain explicit. |
| Keep accountability policy in Mind | It cannot inspect physical actors, machine state, message internals or developer logs. |
| Require one unambiguous assignment and authenticated submissions | Assignment responsibility follows institutional evidence and incomplete histories remain reviewable. |
| Treat missing parts as inconclusive | Wear can empty a slot without proving a worker installed the wrong part. |
| Stop at ReadyToFile | Case identity, provenance validation and exactly-once personnel consequences need a separate service protocol. |

## Replacement-chain outcome decisions (2026-10-29)

A received replacement outcome also resolves ancestors linked by explicit
replacement-attempt IDs for the same job. Ancestors reference the authentic
descendant message in ReplacementResultMessageId and label WorkResolution with
ByReplacementChain. Original-worker ResponseMessageId, attempt status and physical
state remain unchanged. This fixes stale root incidents after a second replacement;
an unrelated report for the same machine is not sufficient. Existing fields persist
the result, so no snapshot schema change is needed.

## Personnel service decisions

| Decision | Reason |
| --- | --- |
| Retain service disclosure receipts independently of private memory | Personnel can verify queried references without reading citizen knowledge or developer traces. |
| Treat typed inspection reports as authenticated claims | Provenance and policy are verifiable without consulting live machine truth. |
| Validate complete relevant history and reject stale work cutoffs | A filing cannot omit an amendment or hide a competing assignment. |
| Canonicalize by job/attempt/single machine slot/violation | Reinspection and new filing IDs reuse one disciplinary case. |
| Keep operation-key conflict checks as well as canonical keys | Different retries cannot silently change the original request. |
| Append one unique reprimand for each accepted case | Message retries do not multiply disciplinary consequences. |
| Return counts through delayed replies and actual inbox reads | Institutional acceptance does not grant private awareness. |
| Revalidate snapshots at historical decision time | Later administrative amendments do not retroactively invalidate saved accepted cases. |
| Keep detention requests and approval separate | A reprimand count alone does not create a guard team or physical custody. |

## Detention approval decisions

| Decision | Reason |
| --- | --- |
| Queue a request only after a qualifying case reply is read | Mailbox delivery cannot grant citizen awareness. |
| Recompute the count at request receipt | A stale qualifying reply cannot authorize a new detention after its evidence expires. |
| Keep requester, authority and private reply states separate | Posting a request is not approval, and approval is not immediately known. |
| Retain one unexpired authorization per account | Duplicate requests cannot multiply later guard teams. |
| Reuse an existing authorization without extending its expiry | Retries cannot silently renew detention authority. |
| Close expired request retries; evaluate fresh operations separately | Replayed old operations do not create a new authorization. |
| Keep authorization administrative | Guards must still read, form a team, travel, establish custody and complete intake. |
| Persist count and record cutoffs as well as ticks | Later events in the same tick must not rewrite a prior institutional decision. |
| Validate expiry arithmetic before allocating an authorization ID | Overflow cannot leave a partial authorization or corrupt its sequence. |
