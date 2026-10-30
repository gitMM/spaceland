# Imperial Station Simulation — Implementation Handoff

**Document version:** 1.2.2
**Date:** 2026-10-28
**Supersedes:** 1.2
**Audience:** an LLM coding agent implementing the simulation and tools, and a human reviewer who gates each handoff
**Companion file (non-normative):** `imperial_station_infiltration_roadmap.md` (former §23)

## Fixed-station scope amendment — 2026-10-29

The user has removed station expansion from the first-release scope. The release
uses a fixed initial layout and population. This amendment takes precedence over
the historical construction requirements and handoff lists below: R-AUT-01..08,
T11, dynamic tile construction in R-TIL-05, construction-specific layout updates
in R-MOV-12, and construction generator/acceptance clauses are deferred. Existing
construction code and historical evidence may remain, but are not release gates.
Existing beds, allocation, occupancy, shortages and finite resources remain in scope.
General dispatch, reporting, blocked-work recovery and resource accounting remain required.

The next milestone combines finite recovery and concurrent institutional chains
in one fixed station: competing repairs, lost credentials and disciplinary cases,
shared personnel and terminals, continued meals/sleep/shifts, explained blocked work,
bounded retries, and drained backlogs after disruptions stop. Prove the same
multiday workload at 32 then 256 citizens (2,048 before the 2026-10-30 amendment), with conservation and exact checkpoint
continuation. This milestone does not silently close other release gates.

## Implementation status — 2026-10-30

This section records delivered work; it does not change the requirements below or
certify completion of the H1–H4 gates. **The first release is still in progress.**
The [acceptance matrix](../../../ImperialStation/docs/acceptance-current.md) is the
current requirement/gate map and supersedes outstanding-work lists in older reports.

The implementation lives in [`ImperialStation/`](../../../ImperialStation/README.md),
a standalone .NET solution outside `Assets`. It has no runtime dependency on
spaceland, DeathStar assets, Unity, rendering or network services, and can be copied
out and built independently. Contracts, decision/planning code (Mind), simulation
core, CLI runner and tests are separate projects.

### Implemented and exercised

| Area | Delivered behavior |
| --- | --- |
| Deterministic foundation | Ordered event phases; 3D weighted pathfinding and frozen movement timing; seeded random streams; finite actions, cancellation and resource accounting; canonical state digests, checkpoints and replay. |
| Planning and knowledge | Bounded GOAP and the decision policies in the isolated Mind assembly; bounded private knowledge with provenance and eviction; published layout, local sensing and inspection; scoped delayed sensors. Core gathers inputs and applies Mind's choices; the 2026-10-30 move was verified byte-identical, and decision inputs no longer read world truth (T08). |
| Communication and credentials | Physical credential-bound terminal sessions, queues, finite inbox reads/posts, delivery latency and separate authority beliefs; permission-filtered versioned record queries; autonomous known-card retrieval or HQ recovery with real replacement-card stock, pickup and renewed login; learned terminal outages. |
| Work and resources | Fabrication, installation, functional testing, resource claims, stack operations and stashes; autonomous repair alternatives; distinct jobs, attempts and locally read orders; acknowledgments, suspension/resumption, delayed reports and reassignment of silent orders. |
| Routines and authority | Staggered cohort rosters and check-ins; receipt-based expectations, off-duty extensions and learned timing correction; overdue incidents, bounded replacement dispatch, patrols with hourly admission accounting, repair priority with an evidence hold and cleanup helpers. |
| Maintenance and institutions | Soiling, spills, finite cleaning, machine faults and wear; local reports drive facility work; QA, record-based audits and reprimands; detention decisions, guard preparation/search, cooperative custody, escort movement and acknowledgment deadlines, finite intake, sentences, release and delayed receipts/revocations. Recovery from all negative institutional outcomes is not yet complete. |
| Daily life | Exact fractional need accumulation; threshold interrupts; finite meals, toilets, rest and assigned sleep with queues and food conservation; sleep budgeted against meals and toilet use; learned cafeteria waits; autonomous relief, retained daily goals, berth allocation, sleep fallback and finite consenting conversations. Sleep is sized from the known roster and sleep already taken. Calibration remains open. |
| Construction and housing (deferred) | Physical construction, expansion planning, construction orders and capacity surveys exist with historical evidence; expansion is outside first-release scope. |
| Verification and tools | CLI validation, execution, batch runs, replay and inspection; exported snapshots/traces and a static viewer; per-stage response times, classified missed routine windows, response-chain causal audits and an independent assignment audit; checkpoint, tracing, concurrency and metamorphic checks. Full viewer, observability and calibration gates remain open. |

### Current evidence

- Complete Release suite: **918 passed, 0 failed, 0 skipped**; zero build warnings;
  snapshot schema 53.
- The [fixed-station pressure workload](../../../ImperialStation/docs/pressure-station.md)
  (nine shortened days of overlapping faults, card losses, spills, terminal outages
  and disciplinary cases) passes at 32 and 256 citizens for seeds 42, 7, 91, 1234 and
  2026, with exact state and continuation-event replay and the independent audit.
  Backlogs clear from tick 138,000–150,000 against a required clear day by 288,000.
- Earlier milestones keep their own reports: the
  [two-day integrated station](../../../ImperialStation/docs/integration-baseline.md),
  [finite disruption and recovery](../../../ImperialStation/docs/recovery-station.md)
  and the [specialized institutional station](../../../ImperialStation/docs/institutional-station.md).
  Their 2,048-citizen measurements predate the 256-citizen population gate.

### Remaining first-release work

- Expansion work (R-AUT-01..08 and T11) is deferred by the fixed-station scope
  amendment. Existing bed allocation and occupancy remain required.
- Close the pressure milestone: calibrate response targets and the check-in bound,
  handle facility acknowledgments when
  the assignee goes off duty, and broaden concurrent institutional load (T15/T22/T26).
- Audit the remaining negative patrol/escort outcomes and reassignment variants (T20).
- Complete the remaining work domains and nested recipe cases (T10) and blocked-work
  recovery policies (T17).
- Run the every-fixture × 20-seed checkpoint campaign (T25), tuning sweeps,
  observability and viewer validation.

Passing the current suite is evidence for implemented behavior, not a claim that every
specified test exists. Detailed increment reports are linked from the
[standalone README](../../../ImperialStation/README.md).

**Current user instruction:** keep the computer running. The earlier shutdown
authorization was revoked on 2026-10-29; see the
[updated instruction](../../../ImperialStation/docs/completion-instruction.md).

## 0. How to use this document


### 0.1 Instructions to the implementing agent

You receive one handoff at a time (H1–H4, §4).

- Implement only the current handoff. Read the whole document first so that early code leaves room for later handoffs.
- Do not stub, fake or partially implement later handoffs beyond the extension points the current handoff names.
- Deliver working code, data, tests and CLI, not another architecture proposal.
- Work in small, verifiable increments. Prefer explicit state and small systems over a general framework. Keep code readable by another developer or agent.
- Never fabricate results. Record the commands you actually ran, their results, and environment limitations. Report using §11.
- When a choice is not covered here, take the simplest option consistent with these rules. Record it in `docs/decisions.md` with the related requirement ID, and continue.

### 0.2 Conventions

| Notation | Meaning |
| --- | --- |
| **R-XXX-nn** | Normative requirement. MUST / MUST NOT are binding. SHOULD is expected unless a reason is recorded in `docs/decisions.md`. MAY is optional. `R-XXX-*` means the whole family; `R-XXX-01..05` a range. |
| **D-nn** | Default value (§6). Configurable; scenarios may override. Fixtures state their own values. |
| **T-nn** | Acceptance test (§8). Each cites the requirements it verifies. |
| **H1–H4** | Handoff stage (§4). |
| *Rationale:* | Explanatory, not binding. |

Every requirement ID in scope for a handoff appears in that handoff's traceability matrix (§11.2).

### 0.3 Changes from v1.1

- **Delivery** split into four gated handoffs. Replay and the canonical digest move to H1.
- **Structure:** rules, defaults, tests and rationale are separated. Requirement IDs and a glossary are added.
- **Decision isolation:** decision code lives in an assembly that cannot reference world state (R-ARC-03, T00).
- **Determinism:** no hash-collection iteration in simulation logic; banned-API analyzer (R-DET-04..05).
- **Timing defaults:** terminal, inspection and service durations are now realistic (§6). Tiny values remain in fixtures only.
- **Planner:** symbolic state, binding sources, depth and budget are specified (R-ACT-05..09). New cost-sensitivity test T23.
- **Knowledge store:** schema, shared public knowledge, per-actor cap and eviction are specified (R-KNW-06..09).
- **Needs:** constants and integer accounting (§6.4, R-LIF-07), a priority ladder (R-LIF-11), and one assigned berth per citizen with no hot-bunking (R-LIF-09).
- **Report deadlines:**
  - Mandatory off-duty extension (R-AUT-12).
  - Slack factor and learned correction from received reports (R-AUT-11, R-AUT-13).
  - Incident dispatch caps (R-AUT-15).
  - New damping test T26.
- **Arbitration** of same-tick claims no longer favors low actor IDs (R-RES-09).
- **Task generators:** concrete sources for dirt, machine faults and QA verification (R-WF-01..04).
- **Detention lifecycle:** sentence, release and reprimand window (R-WF-36, R-WF-40..42). Player custody mode defined (R-WF-39).
- **Terminal load:** shift-start check-ins are staggered per actor (R-MSG-16).
- **Anti-scripting:**
  - Restricted scenario schema and response-chain purity check (R-VER-01..02).
  - Generators and reviewer holdouts (R-VER-03..04).
  - Metamorphic tests (T24) and snapshot fuzzing (T25).
- **Tooling:** a static trace viewer from H1 (R-OBS-08). Authority competence exposed as tuning profiles, with a `sweep` command (R-TUN, R-RUN-08).
- **Tests:** T15's window must include a cohort transition.
- **Roadmap:** former §23 moved to a separate, non-normative file.

### 0.4 Corrections in v1.2.1

- Preserve fractional need accumulation across activity changes; add T27 for exact accounting, thresholds and continuation.
- Define feature ownership and partial applicability for cross-cutting requirements, so early handoffs do not require later behavior.
- Qualify metamorphic properties by what each transformation actually preserves; increased travel may legitimately change decisions and incidents.
- Test incident admission/dispatch budgets separately from open incidents and deferred backlogs, with explicit hourly accounting.
- Count usable completed berths, including vacant ones, and permit unassigned citizens during a housing deficit.
- Protect due routine/check-in goals from ordinary work starvation; require scheduled rest independently of the optional strain threshold.
- Make fairness statistics diagnostic and require known-card retrieval only when it is the cheapest eligible plan.

This is a specification correction. Tests described here must still be implemented and executed; this revision is not evidence of a working simulation.

---

## 1. Premise and core principle

**The eventual game.** A first-person infiltration and sabotage game inside a large imperial space station. The scale reference is an installation like the Death Star; this is descriptive only, with no licensed names, assets or layouts.

**The station.** It is built from tile families on a 3D grid. Technology has a 1970s character: fixed terminals, credentials, records, and people who travel to operate equipment. There are no personal radios, mobile inboxes or push notifications.

**The people.** Thousands of persistent individual citizens live and work there. Each has an identity, a location, needs, an inventory, assignments and knowledge, and must travel to do their work.

**The authority.** A governing AI chooses projects, production, maintenance, construction, assignments and living arrangements. It has substantial authority but incomplete information, and depends on sensors and reports.

**The player.** The player appears to be an ordinary worker and performs the same tasks through the same rules. Secretly, the player works for rebels and aims to destroy the station. Ordinary employment provides access, information, tools and opportunities to interfere.

**The central mechanism** is the gap between:
1. **Physical truth** — what exists and has happened.
2. **Citizen knowledge** — what each person has observed, read or been told.
3. **Authority belief** — what the governing AI currently believes.
4. **Institutional records** — what is officially recorded or authorized. A record is a real institutional fact, but its claims about physical events may be false.

**Core principle.** The authority plans from its available information. Actions execute against the physical world. A report changes a record; it never makes the reported event physically happen.

**No LLM at runtime.** Runtime AI is conventional game logic: no LLM or remote-model calls in ticks, decisions or execution. An external coding LLM is a development tool. It creates scenarios, runs batches, inspects failures and changes code.

*Rationale: wrong estimates, late arrivals, false reports and delayed discovery are the gameplay. The physical rules and information boundaries must stay consistent so the gameplay is readable and exploitable.*

---

## 2. Glossary

| Term | Meaning |
| --- | --- |
| Actor / citizen | Persistent individual: worker, cleaner, supervisor, patrol, guard, or the player-actor |
| Player-actor | Actor driven by player input through the shared action API |
| Scripted actor | Actor flagged in a scenario whose actions come from a command list (R-VER-01) |
| Authority | The governing AI: goals, planner, dispatcher, beliefs |
| Institution / institutional service | Credential service, personnel service, detention service, etc. They own records and validate transitions |
| Tile | Placed one-cell station module of a family; traversable when completed |
| Service | Capability on a tile (Credential Desk, Records Terminal, Cafeteria, Toilet, Armory, Detention Intake, …) |
| Terminal | Fixed device on a tile providing sessions for reading and posting messages and querying records |
| Session | Terminal-bound authenticated state tied to actor, account, terminal and expiry |
| Message | Unit of transport: order, report, acknowledgment, service reply, peer message |
| Mailbox | Stored delivered messages; not knowledge |
| Order / order copy | Authority instruction; the copy an actor has actually read and carries in knowledge |
| Task (job) | Desired world condition |
| WorkAttempt | One assignment of a task to one actor/account |
| ReportExpectation | Required response for an attempt, with deadline |
| Incident | Persistent discrepancy or nonresponse with child goals/orders |
| Case | Disciplinary record keyed canonically (R-WF-34) |
| Reprimand | Accepted disciplinary outcome of a case |
| Escort group | Guards plus target moving together under an order |
| Claim | Atomic exclusive reservation of an item or capacity |
| Commitment | Administrative reservation of future capacity or budget; not physical |
| Fact | Entry in an actor's knowledge store (R-KNW-06) |
| Belief | Authority's current value for a key, with evidence history |
| Record | Institutional data entry with ownership and access rules |
| View | Restricted read interface given to decision code (R-ARC-04) |
| Policy | Rule turning known triggers into goals (R-ACT-14) |
| Disturbance | Scheduled environment event in a scenario (R-VER-01) |
| Fixture | Small test scenario; its values override defaults |
| Cohort | Group sharing a duty-shift template |
| Berth | One sleeping place; the unit of habitat capacity |
| Digest | Canonical hash of consequential state (R-SNP-04) |
| Replay bundle | Everything needed to reproduce a run (R-SNP-05) |

---

## 3. Scope

### 3.1 In scope across H1–H4

1. Load and validate a connected 3D tile station with all twelve families traversable.
2. Persistent individual actors moving through weighted adjacent tiles with exact timing.
3. Real resources, items, locations, jobs, claims and action progress.
4. A genuine bounded GOAP planner used for worker goals and coarse authority projects.
5. Separate truth, citizen knowledge, authority beliefs and institutional records.
6. Terminal-only communication with sessions, mailboxes, explicit reading and posting, latency and check-ins.
7. Misleading reports with delayed discovery, and optimistic authority estimates with later actual arrival.
8. Autonomous construction from an authority goal.
9. Lost-keycard recovery via headquarters with resumed work.
10. Missing accepted-work reports causing a patrol and a separate replacement attempt, with damping.
11. Wrong-cartridge inspection, records audit, distinct reprimands, detention request and approval, armed escort, intake and release.
12. Daily routines: shifts, cafeteria meals, sleep, rest, toilets, conversation.
13. Deterministic snapshots, replay, digest, batches, inspection, tracing, viewer, benchmarks and tuning sweeps.
14. Population runs with at least 256 persistent citizens (amended 2026-10-30 from 2,048 by user decision).

### 3.2 Deferred

None of these are prerequisites:
- Unity FPS controller and production graphics.
- Interiors and sub-tile geometry; doors, locks and spatial access restrictions; elevators; crowd avoidance.
- Detailed atmosphere, power, communications or structural engineering.
- Full social psychology, dialogue generation, all-pairs relationships.
- Open-ended investigation, interrogation, combat, resistance and escape.
- Impersonation (cards are holder-bound in v1); hot-bunking.
- Campaign and final destruction sequence.
- A universal planner; a separate approximate simulation for batches.

---

## 4. Handoff plan

Each handoff is a separate agent session ending at a gate. The gate is met when:
- the listed tests pass;
- all earlier handoffs' tests still pass;
- the report (§11) is submitted.

The reviewer runs holdout variants (R-VER-04) before accepting.

#### Feature ownership and cross-cutting requirements

| Owner | Functional scope first implemented |
| --- | --- |
| H1 | Clock, spatial/resource primitives, movement, reproducibility, minimal contracts and tools |
| H2 | Ordinary work and citizen planning; knowledge; terminals/messages; seeded credential validation and generic record/query infrastructure |
| H3 | Operational authority planning/dispatch, construction, credential recovery, disciplinary/security workflows, cohorts and rosters |
| H4 | Needs and full daily activities/conversation, population validation, tuning sweeps and integration |

Cross-cutting inventories apply only to features owned by the current or earlier handoffs. This qualifies R-ACT-10..11 and R-ACT-14 (domains/actions/policies), R-INS-01 (records), R-OBS-02 (events), R-SNP-01 (snapshot fields), R-LIF-01 (citizen fields), and the scenario/CLI inventories. Later-owned types may be declared as explicit extension contracts, but later behavior MUST NOT be faked or reported as implemented. Empty future data is not a working feature.

H2 proves citizen planning and periodic/work-result check-ins. H3 adds the authority domain, dispatcher, roster-driven visits and shift staggering (R-MSG-15..16). H4 adds needs-driven scheduling. Tests follow the same ownership: early T08 compares citizen decisions; H3 adds authority/dispatcher isolation. T21's cohort-contention variant starts in H3.

For a requirement spanning handoffs, the traceability matrix names the clauses implemented now and the later owner of the remaining clauses. The current gate judges only the applicable clauses and all earlier obligations. Each new feature immediately extends snapshots, digests, traces and continuation tests; do not wait until H4 to serialize H3 state.

### H1 — Foundation, movement, reproducibility

- **Requirements:** R-ARC-*, R-DET-*, R-CLK-*, R-TIL-*, R-MOV-*, R-RES-01..03, R-SNP-*, R-RUN-01..06 (operations `validate`, `run`, `batch`, `replay`, `inspect`), R-OBS-01, R-OBS-04..05, R-OBS-08 (tiles, actors, timeline), R-UNI-03, R-VER-01, R-VER-06..07.
- **Actor scope:** identity, position, movement, inventory. Scripted move/pickup/put-down commands only.
- **Extension points:** action interface and lifecycle types; view interfaces in Contracts (minimal); message and record type declarations.
- **Tests:** T00–T06; T12, T13, T25 on H1 state; T14.

*Rationale: digest and replay come first so hidden nondeterminism is caught from the first system onward.*

### H2 — Work, planning, knowledge, terminals

- **Requirements:** R-RES-*, R-ACT-*, R-KNW-*, R-MSG-* and R-INS-01..05 within the ownership rule above; R-OBS-02..03, R-OBS-06..07, R-OBS-08 (knowledge/belief layers, message stages), R-VER-03 for T07.
- **Tests:** T07, T08 (citizen/H2 variants), T09, T10, T16, T17 (work part), T21 except cohort contention and later security variants, T23; T12, T13, T25 extended.

### H3 — Authority, construction, institutions

- **Requirements:** R-AUT-*, R-INS-*, R-WF-*, R-LIF-01..05, R-TUN-01..03, R-VER-02..05.
- **Daily-life placeholder:** H3 implements the station clock, cohorts, duty/off-duty availability, rosters and the waking rule, without needs. Off-duty actors go to their assigned berth tile, or a known rest tile if unassigned, and hold a placeholder `OffDuty` activity, which H4 replaces. A missing berth/rest destination leaves an explained availability state; never fabricate an assignment or sleeping capacity.
- **Tests:** T11, T18, T19, T20, T08 (authority/dispatcher and H3 variants), T21 cohort contention and security variants, T24; T12, T13, T14, T25 extended.

### H4 — Daily life, population, tuning, integration

- **Requirements:** R-LIF-*, R-TUN-04, R-RUN-07..08, R-UNI-*.
- **Tests:** T15, T22, T26, T27.
- **Full regression:** T00–T27 with needs enabled. No test may pass only by disabling needs; fixture threshold raises must be declared, as T11 allows.
- **Deliverables:** measured baselines; Unity compatibility verification when available.

---

## 5. Normative rules

Rules are grouped by family. Each family's handoff assignment is in §4. Default values are referenced as D-nn and defined in §6.

### 5.1 ARC — Architecture and dependencies

**R-ARC-01 — Repository layout.** Use these projects/directories (rename consistently only to match an existing repository's conventions):

| Project / directory | Responsibility |
| --- | --- |
| `src/Station.Contracts` | Typed IDs, coordinates, enums, message payloads, knowledge fact types, view interfaces, record-store interfaces, intent types |
| `src/Station.Mind` | All decision code: citizen goal selection and planning, policies, authority planner, dispatcher, institutional decision services |
| `src/Station.Core` | World state, topology, movement, action execution, resources, terminals/transport, record storage, clock, scheduler, snapshots, view implementations |
| `src/Station.Runner` | CLI, scenario loading, scripted actors, batches, assertions, diagnostics, result files |
| `tests/Station.Tests` | Unit, fixture, generated, metamorphic, replay, isolation tests |
| `tools/viewer` | Static trace viewer (R-OBS-08) |
| `content` | Tiles, recipes, services, credentials, communication rules, needs, policies, tuning profiles |
| `scenarios` | Fixtures, generators (`scenarios/generators`), population workloads |
| `docs` | Architecture, data formats, commands, decisions log, measured performance, extension points |
| `artifacts` | Generated results, replay bundles, traces, checkpoints (large outputs not in source control) |
| `unity` | Optional adapter or integration notes; never required by the standalone build |

**R-ARC-02 — Engine independence.** Contracts, Mind and Core MUST NOT depend on `UnityEngine`/`UnityEditor`, MonoBehaviours, GameObjects, Transforms, prefabs, scenes, ScriptableObjects, Unity physics/NavMesh/coroutines/frame time, Unity Jobs/Burst/Entities/Unity collections, network services, model inference, UI, graphics, audio, or an installed editor. Use simulation-owned IDs and coordinate types. The core never loads a prefab to learn gameplay properties.

**R-ARC-03 — Decision isolation boundary.** `Station.Mind` MUST reference only `Station.Contracts` and the base class library. It MUST NOT reference `Station.Core`. Structural test T00 MUST fail if Mind's assembly references anything else. Leaking world state into decisions must be a build error, not only a test failure.

*Rationale: hidden-state isolation (T08) is the premise of the game. A compile-time boundary prevents a single helper from quietly reading world truth.*

**R-ARC-04 — Views.** Core implements view interfaces declared in Contracts and passes them to Mind:

| View | Exposes | Never exposes |
| --- | --- | --- |
| `IActorView` | The actor's own state, needs, inventory, credentials, session, knowledge store, passive facts on the current tile, results of its own completed actions, shared public knowledge (R-KNW-07) | World state, other actors' private state, mailbox contents not yet read, traces |
| `IAuthorityView` | Its beliefs and evidence, records it owns, received messages and receipts, believed layout, rosters, its own orders/commitments, its RNG stream | Live positions, actual inventories, machine truth, citizens' private knowledge, mailbox read state without a receipt |
| `IInstitutionView` (per service) | That service's record store, submissions received by it, its policy configuration | Physical truth, other services' records unless granted |

Developer inspection APIs live in Core/Runner and MUST NOT be reachable through any view.

**R-ARC-05 — Intents only.** Mind returns intents: action requests, messages to post, requested record transitions. Core validates and executes them. Mind never mutates state.

**R-ARC-06 — Targets.** Contracts, Mind and Core target `netstandard2.1`. Runner and Tests target `net10.0` when available, otherwise the newest supported LTS. Pin the SDK with `global.json` and pin package versions.

**R-ARC-07 — Language level.** Shared assemblies use C# 9-compatible syntax (Unity 6.0 baseline). Do not use `init` or `record` unless an `IsExternalInit` shim is included and verified. Runner may use newer APIs.

**R-ARC-08 — Data orientation.** Prefer compact state, typed IDs, shared immutable definitions, explicit systems, spatial indexing, and low allocation in hot paths. Avoid a class per empty grid cell, inheritance trees per tile family, and whole-world copies per planning search.

**R-ARC-09 — Threading.** Each world executes single-threaded and deterministically. Parallelism happens only across worlds, in the runner.

**R-ARC-10 — I/O.** File access, console output and wall-clock measurement exist only in Runner, Tests and tools.

### 5.2 DET — Determinism

**R-DET-01 — Guarantee.** Same build, same supported runtime/backend, same content, same initial scenario, same seed, same settings and same timed external commands produce the same consequential state and events. Cross-runtime equality (.NET vs Unity Mono vs IL2CPP) is not assumed; it is tested separately (R-UNI-07).

**R-DET-02 — PRNG.** Implement a documented PRNG (e.g. PCG32 or xoshiro256**) in Core. Its algorithm and version are part of the snapshot format. `System.Random` is forbidden. Derive independent streams from the scenario seed and a stable stream key: one per system (authority, arbitration, soiling, wear, QA, …) and one per actor. A new random draw in one system MUST NOT change another stream.

**R-DET-03 — Numbers.** Coordinates, ticks and resource quantities are integers. Needs use the exact integer accumulator in R-LIF-07 with integer display values. Tick duration is a positive integer number of milliseconds. Rounding is defined wherever a value affects timing or decisions. Floating point is allowed only in presentation-facing outputs.

**R-DET-04 — Collection iteration.** Simulation and Mind code MUST NOT let the iteration order of hash-based collections (`Dictionary`, `HashSet`, `ConcurrentDictionary`, their `Keys`/`Values`) affect state, events or decisions. Use sorted structures, arrays indexed by dense IDs, or insertion-ordered collections. Hash lookup is fine.

**R-DET-05 — Banned APIs.** Enable `Microsoft.CodeAnalysis.BannedApiAnalyzers` on Contracts, Mind and Core, with violations as errors. `BannedSymbols.txt` covers at least:
- `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `Stopwatch`, `Environment.TickCount`
- `System.Random`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`
- Enumerators of `Dictionary<,>`, `HashSet<>` and their key/value collections

Any suppression requires an inline justification and an entry in `docs/decisions.md`.

**R-DET-06 — No global mutable state.** No process-wide statics holding mutable state, entity counters, RNGs or caches. Everything is scoped to a world.

**R-DET-07 — Ordering sources.** Ordering never depends on object hashes, memory addresses or thread completion order.

### 5.3 CLK — Clock and event ordering

**R-CLK-01 — Time ownership.** Core owns simulation time. Wall clock, frame rate, `Time.deltaTime`, real sleeping or task delays never determine gameplay progression. A headless run advances the same logical time as a Unity-hosted run.

**R-CLK-02 — Initialization.** Creation loads initial state and tick-0 commands/events without processing them; `lastProcessedTick = -1`.
- `AdvanceTo(T)` processes each due event through T exactly once.
- The first `AdvanceTo(0)` processes tick-0 items.
- Repeated `AdvanceTo` with an already processed T is a no-op.
- External commands targeting an already processed tick are rejected.
- The processed-time marker is snapshotted.

**R-CLK-03 — Chunk equivalence.** One large advance and many small advances ending at the same tick produce identical consequential state.

**R-CLK-04 — Phases within a tick.**
1. Scheduled scenario/environment changes (disturbances).
2. Due physical completions: arrivals, completed work, completed terminal operations.
3. Scheduled sensing and local observation updates.
4. Mailbox deliveries; automatic authority/institution processing; permitted belief/record updates.
5. Goal/policy evaluation, due deadline checks, planning requests.
6. Validation, arbitration (R-RES-09), and commitment of action intents.

**R-CLK-05 — Within-phase order.** Order by simulation-local sequence ID. If more tie-breaking is needed, use stable actor/job IDs, never memory addresses or collection order.

**R-CLK-06 — Uniform validation.** Player, NPC and scripted intents all pass through phase 6 validation.

**R-CLK-07 — No backward information.** Action effects may produce local observations. Message latency is at least 1 tick, so nothing reaches an earlier phase of the same tick.

**R-CLK-08 — Deliveries before deadlines.** A qualifying response delivered at its deadline tick is on time. An unsatisfied expectation becomes overdue in phase 5 of the deadline tick. Institutional transitions are atomic and idempotent.

**R-CLK-09 — Delivery is not knowledge.** Mailbox delivery changes no citizen knowledge. Only a completed reading/query action does (R-MSG-07).

**R-CLK-10 — No zero-time loops.** Repeated scheduled activities advance at least one tick. A per-world, per-tick event cap (D-90) converts runaway loops into `runtime_error` with a focused trace.

**R-CLK-11 — Event-driven skipping.** A scheduler may skip intervals with no due transitions, provided semantics are identical. It MUST NOT skip: tile arrivals, message deliveries, deadlines, institutional transitions, resource collection/consumption, needs thresholds, work/construction completion, scheduled changes or commands.

**R-CLK-12 — Budgets.** Planning and other budgets are allocated by simulation time and configured rules, never by the number of `AdvanceTo` calls.

### 5.4 TIL — Tiles and content

**R-TIL-01 — Families.** The catalog contains exactly these families: `Habitat`, `Industrial`, `Power`, `LifeSupport`, `Storage`, `Defense`, `Command`, `Corridor`, `LandingPad`, `Conduit`, `Hull`, `Truss` (display names: Habitat, Industrial, Power, Life Support, Storage, Defense, Command, Corridor, Landing Pad, Conduit, Hull, Truss). Supply at least one definition per family. Document which family-specific capabilities are implemented.

**R-TIL-02 — Traversability.** Every completed placed tile of every family is traversable, including Hull, Truss and Conduit. Empty coordinates are not tiles and cannot be crossed. A valid initial station is one connected component; a populated scenario cannot have an empty station. Reachability never implies teleporting.

**R-TIL-03 — Adjacency.** 3D orthogonal integer grid with X and Z horizontal and Y vertical. Neighbor order is `+X, -X, +Y, -Y, +Z, -Z`. No diagonals. Adjacent completed tiles connect regardless of family or orientation; vertical edges follow the same rules. Stairs, elevators, doors and interiors are not modeled.

**R-TIL-04 — Shared occupancy.** Any number of actors may share a tile or edge. There are no movement reservations, crowd collisions or corridor queues. Items, beds, machines, seats, stalls and work positions may have exclusive claims or capacity.

**R-TIL-05 — Construction topology.**
- An unbuilt site is a project reservation, not a traversable tile.
- A new tile must attach to at least one completed tile and is built by a worker standing on a completed neighbor.
- It joins the graph only on physical completion.
- v1 never removes tiles or disconnects regions.

**R-TIL-06 — Definition, instance, presentation.**
- *Definition:* stable ID, family, traversal ticks, capabilities/services, work positions/capacities, construction recipe and duration, outputs such as berth capacity, and an opaque visual key.
- *Instance:* stable ID, definition ID, coordinate, completed state, optional traversal override, installed equipment and state, contents, service reservations.
- Changing only visual mappings never alters outcomes.

**R-TIL-07 — Services are capabilities.** A service such as Credential Desk, Records Terminal, Detention Intake, Cafeteria, Bed, RestSpot, Toilet, Armory or Workstation is a capability on a placed tile, not a family. One tile may host several.

**R-TIL-08 — Footprint.** One cell per tile in v1. Presence on a tile is sufficient proximity for its services, subject to the service's own rules.

**R-TIL-09 — Data format.** Versioned JSON (`schemaVersion`). Unknown fields, unknown IDs, invalid enums, invalid coordinates and invalid durations produce clear validation errors naming the file and path.

**R-TIL-10 — Duration validation.** Traversal, work, identity checks, record queries and service operations are positive integer ticks. Message latency is ≥ 1 tick.

**R-TIL-11 — Credentials do not gate adjacency.** Credentials gate actions and services only. Losing a card never strands anyone. Custody restricts an actor's permitted actions without changing connectivity.

### 5.5 MOV — Movement, routing, estimates

**R-MOV-01 — Edge cost.** For adjacent tiles a, b: `edgeTicks(a,b) = ceil((T(a) + T(b)) / 2)`. Compute in 64-bit checked arithmetic. Every edge costs ≥ 1.

**R-MOV-02 — Route time.**
- Route time is the sum of its edge costs.
- Same-tile travel costs 0.
- No extra charge at start or arrival.
- Reverse edges cost the same.
- Vertical edges use the same formula.

**R-MOV-03 — Movement state.** Persist at least: remaining route, source tile, destination tile, segment departure tick, segment arrival tick, route index, and owning action.

**R-MOV-04 — Transit.** Segment progress is exposed as an exact rational `(now − departure) / (arrival − departure)`. An actor in transit has an edge position and is not at the destination. Local interactions require arrival.

**R-MOV-05 — Frozen segments.** A segment's duration is fixed when it starts. Cost changes affect only later segments. The movement model and configuration are recorded in replay metadata.

**R-MOV-06 — Routing algorithm.**
- Deterministic weighted shortest path over completed tiles. A* or Dijkstra; Dijkstra serves as the reference oracle in tests.
- Heuristic = Manhattan distance × `minEdgeCost`, where `minEdgeCost` is the smallest traversal ticks among completed tiles in the current topology/cost version.
- Recompute `minEdgeCost` whenever that version changes, since overrides can lower it.
- Never inflate the heuristic.

**R-MOV-07 — Tie-breaking.** Order the open set by lower f, then by insertion sequence number. Expand neighbors in R-TIL-03 order.

**R-MOV-08 — Outcomes.** `PathFound`, `NoRoute`, `BudgetExhausted`, `InvalidDestination`. Budget exhaustion is never reported as `NoRoute`. `NoRoute` between completed tiles in v1 indicates invalid input or a defect.

**R-MOV-09 — Route caches.** Cache keys include the topology/cost version. Construction or cost edits invalidate affected entries.

**R-MOV-10 — Authority travel estimate.** `estimatedTravelTicks = Manhattan(lastKnownTile, target) × D-03`, where `lastKnownTile` comes from the authority's evidence (e.g. the terminal a report was posted from). This underestimate is intentional. The estimator MUST NOT be replaced by actual route durations.

**R-MOV-11 — Citizen travel estimate.** A citizen's planner estimates travel as route cost over that citizen's known layout and known traversal costs:
- Known layout = the published layout version the citizen knows, plus tiles they have traversed or observed.
- Execution always routes on the true completed graph.
- Execution route results are not written into anyone's knowledge except as tiles actually traversed.

**R-MOV-12 — Authority layout belief.** The authority starts with the initial public layout. Its believed layout changes only on accepted completion evidence. Placement (R-AUT-04) uses the believed layout.

### 5.6 RES — Resources, jobs, attempts, claims

**R-RES-01 — Single physical location.** Every item or stack has exactly one physical location: tile container, actor inventory, project stash, machine slot, or service output.

**R-RES-02 — Conservation.** Splits, merges, production and consumption are explicit. Resource balance reconciles against declared sources, sinks and recipes. Accounting is checked in tests and population runs.

**R-RES-03 — Administrative records are separate.** Administrative inventory or allocation records never move or create items.

**R-RES-04 — Task records.**

| Record | Meaning |
| --- | --- |
| Task (Job) | Desired world condition, e.g. tile clean, machine has correct cartridge |
| WorkAttempt | One assignment of a task to one actor/account with posted/read/accepted state and its own reports |
| ReportExpectation | Required response for an attempt, its deadline, and satisfaction state |
| Incident | Persistent discrepancy or nonresponse, with identified child goals/orders |

Actual execution state (Pending, Assigned, InProgress, Waiting, Blocked, CompletedPhysically, Cancelled, Failed) is stored separately from the authority's belief about the task.

**R-RES-05 — Reports have no physical effect.** A completion report never moves items, installs components, repairs machines, finishes construction, grants capacity, or creates/consumes materials. Authority responses (cancel, reassign) are explicit orders through the normal system.

**R-RES-06 — Attempt identity.** Reassignment creates a new `AttemptId` under the same task. Reports carry task and attempt IDs. A report is never reattributed to another attempt.

**R-RES-07 — Orders are messages.** Dispatch and cancellation produce messages. A citizen learns an order by reading it (R-MSG-07), then accepts or rejects it under policy and posts an acknowledgment. Superseding an attempt does not stop distant work or reclaim carried items.

**R-RES-08 — Suspension.** Suspension retains the task/attempt or a resume reference, releases unused exclusive claims, and leaves carried items in inventory. A work-position lease may expire and free the position, but never teleports items. Resumption revalidates.

**R-RES-09 — Claims and arbitration.**
- Exclusive items and capacity-limited services use atomic claims.
- A promised allocation is not proof of presence; pickup or use validates current physical conditions.
- No two plans can consume the same component.
- Conflicting intents in phase 6 are ordered by earliest claim-request tick (FIFO).
- Same-tick ties are ordered by a permutation drawn from the world's `arbitration` RNG stream for that tick, never by actor ID.
- Cancellation releases claims by a documented rule. Expiry, if used, is in simulation time and snapshotted.

*Rationale: stable-ID tie-breaking systematically starves high-ID actors in population runs.*

**R-RES-10 — No movement reservations** in v1.

**R-RES-11 — Failure reasons.** Return inspectable reasons, at least:
- `MissingItem`, `ResourceClaimed`, `InsufficientMaterials`, `ServiceBusy`, `NotAtRequiredTile`, `NotQualified`
- `PlanBudgetExhausted`, `UnknownRequiredFact`, `InvalidConstructionSite`
- `MissingCredential`, `CredentialRevoked`, `RequiredServiceNotPresent`, `SessionRequired`
- `RecordAccessDenied`, `EvidenceInsufficient`, `NoEligibleReplacement`, `NoEligiblePatrol`
- `AwaitingCaseAcceptance`, `AwaitingMessageRead`, `AwaitingGuardTeam`, `TargetNotLocated`, `DetentionFull`
- `EscortInterrupted`, `EscalationDeferred`, `InCustody`

A legitimate shortage is recorded distinctly from an internal exception.

**R-RES-12 — Fit versus suitability.**
- Cartridges have identity and type.
- `CanInsert(slot, cartridge)` checks physical fit, presence, tools, authorization and claims.
- `MeetsSpecification(machine, cartridge)` checks required type or rating.
- The ordinary install action (finite duration, D-31) accepts a fitting but unsuitable cartridge and moves it into the slot.
- A functional rule determines operational state; the machine's status light is a passive fact.
- The executor MUST NOT reject fitting unsuitable parts. NPCs normally plan to meet the known specification.

**R-RES-13 — Service contracts.** A service declares capability, tile, finite duration, access rules, capacity, inputs/outputs and record permissions. Physical presence is required at start and completion. Outputs appear at the service output location and require ordinary pickup.

**R-RES-14 — Staffing.** Automated kiosks need no staff. A service declared staffed requires an available staff actor.

### 5.7 ACT — Actions, planning, policies

**R-ACT-01 — Shared actions.** Player, NPC and scripted actors issue the same validated world actions with the same durations, inventory consequences and reporting semantics. The player does not use the NPC planner.

**R-ACT-02 — Lifecycle.** Requested → Validated/Started → Running → Completed | Failed | Cancelled.

**R-ACT-03 — Validation and failure.** Conditions are validated at start and at completion. A failure may yield local observations and new goals, such as a report goal. It never updates the authority directly.

**R-ACT-04 — Predicted vs actual effects.** Predicted effects exist only in hypothetical planning states. Physical effects occur only through execution at the defined ticks.

**R-ACT-05 — Planner.** A bounded GOAP planner: best-first search over symbolic states using actions with preconditions, effects and costs. Forward or regressive search is the implementer's choice; record it in `docs/decisions.md`. Do not hardcode a sequence and call it GOAP.

**R-ACT-06 — Symbolic state.** Each planning problem has:
- A small table of bound variables (self, goal parameters, candidate bindings).
- A fixed-capacity predicate bitset (≤ 64 predicates) over those variables.

Predicates are derived only from the requester's view. Examples: `At(self,?loc)`, `Holds(self,?item)`, `HasValidCredential(self)`, `SessionAt(self,?terminal)`, `KnowsOrder(self,?attempt)`, `SlotHas(?machine,?item)`, `ItemAt(?item,?loc)`, `ReportPosted(?report)`.

**R-ACT-07 — Binding sources.** Variables bind only from:
- Goal parameters.
- The requester's knowledge facts.
- The shared public service directory filtered by required capability.
- Recipe definitions.

Candidates per variable are capped at K (D-82), ranked by estimated cost. Never ground actions against all entities.

**R-ACT-08 — Limits.**
- Maximum plan depth D-80; node budget per request D-81; requests per tick per world D-83 (excess queued FIFO).
- Outcomes are `PlanFound`, `NoPlan` or `BudgetExhausted`.
- Retry after exhaustion uses deterministic backoff (D-84).

**R-ACT-09 — Costs.** Action cost = the requester's estimated duration in ticks: travel per R-MOV-11 for citizens, R-MOV-10 for the authority; other durations from the catalog. When known costs change, the chosen plan MUST change accordingly (T23).

**R-ACT-10 — Two planning domains.** The same planner implementation serves citizen goals (H2) and coarse authority goals (H3). Authority actions operate at project/order scale and never search over footsteps. H2 is not required to implement operational authority behavior to demonstrate the citizen domain.

**R-ACT-11 — Worker action set.** The complete set below is required by H4; each action becomes mandatory with its owning system in §4. At least:
- Movement and items: go to tile, pick up, put down/deliver.
- Work: inspect, produce recipe, repair/install, functional test, build neighbor tile, clean.
- Needs: eat, sleep, rest, use toilet, converse.
- Terminals: login, read inbox, post message/report, query records, logout.
- Credentials: check inventory/credentials, recovery login, request and collect a replacement card.
- Discipline and security: file case, read decision, request detention, search location, fetch/equip equipment, identify co-located target, begin escort, escort move, register intake.

**R-ACT-12 — Movement as one action.** The planner treats movement as one action; the movement system executes segments.

**R-ACT-13 — Deterministic scheduling.** Synchronous bounded searches with a stable request queue. No wall-clock deadlines; no thread-order effects. If a search spans ticks, its state is snapshotted or restarted by a deterministic rule.

**R-ACT-14 — Policies.** Policies have explicit triggers, required evidence, priority, deduplication key and version. Policies create goals; planners choose steps; protocol state machines (sessions, cases, escorts) validate conditions and never advance because a narrative step is "due".

| Known/recorded trigger | Goal created |
| --- | --- |
| Required credential missing | `RestoreUsableCredential`, then reconsider retained work |
| Check-in due or report to file | Visit a suitable terminal |
| Routine window or need threshold (R-LIF) | Corresponding physical activity |
| Accepted-work report overdue | `EstablishWorkerStatus` + `EnsureTaskCompleted` |
| Inspection contradicts a report | `ResolveJobDiscrepancy` |
| Accepted case reaches reprimand threshold | Request detention |
| Detention approved | Orders for an armed team to locate, escort, register |
| Facility condition observed by a duty role | Report condition |

**R-ACT-15 — Event-driven re-evaluation.** Re-evaluate on relevant observations, reads, deliveries, deadlines and known availability changes. Index records by subject/job/attempt. Schedule watchdogs by due tick. No per-tick scans of all citizens or records.

**R-ACT-16 — Demonstrated planning.** Demonstrate at least one multi-step chain and at least two alternative ways to obtain a prerequisite (T10, T23).

**R-ACT-17 — Scripted actors** receive only their actor view.

### 5.8 KNW — Knowledge, perception, beliefs, sensors

**R-KNW-01 — Four boundaries.**
- **World truth:** physical state used by execution.
- **Citizen knowledge:** what an actor observed, read, or heard.
- **Authority beliefs:** received evidence, assumptions and known commitments.
- **Institutional records:** owned by defined services (credential registry, job history, cases, reprimands, orders).

A private observation and a developer trace are not records.

**R-KNW-02 — Restricted inputs.** The authority planner and dispatcher use `IAuthorityView`. Citizens use `IActorView`. Physical validation may legitimately reveal local failures to the acting citizen.

**R-KNW-03 — Mailboxes are storage.** A message can wait unread while its recipient travels, works, sleeps or lacks a card.

**R-KNW-04 — Local perception.**
- Each fact type in content declares `Passive` (perceived on arrival and at scheduled sensing on the current tile) or `InspectionOnly` (requires a finite inspection).
- A failed action may reveal facts specified by that action.
- Camera position, rendering and UI polling never cause observation.
- View cones and occlusion are deferred.

**R-KNW-05 — Face-to-face.** Co-located interactions (identification, escort instructions, conversation) inform only the participants and transmit nothing elsewhere.

**R-KNW-06 — Fact schema.** A knowledge fact has:
- key `(SubjectId, AttributeKind)` and a typed value;
- `sourceKind` ∈ {`Direct`, `Inspection`, `ActionFailure`, `MessageRead(messageId)`, `Heard(actorId, conversationId)`, `Seeded`};
- `observedTick` and `learnedTick`.

Each actor holds one current entry per key. `Heard` and `MessageRead` facts are never treated as direct observation.

**R-KNW-07 — Shared public knowledge.** The published layout, public service directory and catalog form one immutable, versioned object shared by reference. An actor stores only which version it knows. It is never copied per actor.

**R-KNW-08 — Capacity and eviction.**
- Private facts per actor are capped at D-85.
- When full, evict the least-recently-used non-pinned fact.
- Pinned facts: own credentials, assignment, berth, known roster version, active orders/attempts, pending reports, and facts referenced by an active plan or pending report.
- Eviction is deterministic and traced at detail level.

**R-KNW-09 — Mobile subjects.** Facts about other actors' locations are "last known at tick". Planners use them as search candidates, not as current positions. Facts older than D-87 are flagged stale.

**R-KNW-10 — Authority belief policy.**
- Retain evidence history per belief key; the latest applicable evidence sets the current belief.
- Same-tick ties are broken by message sequence.
- Keep source and observation time.
- Label human claims `Reported` and registered sensor output `Sensor`.
- Record contradictions without consulting truth.
- Absence at one place never reveals location elsewhere.
- This policy does not govern credentials, reprimands, official history or detention (see R-INS).

**R-KNW-11 — Sensors.** A sensor has explicit scope, enabled state, schedule and wired channel, and emits only permitted observations. Fixtures disable sensors that would reveal hidden facts prematurely.

**R-KNW-12 — Pure getters.** Diagnostic getters, rendering, snapshot inspection and UI polling never:
- advance time, trigger sensing, consume randomness or replan;
- deliver or mark messages;
- mutate knowledge.

**R-KNW-13 — Gameplay reading is separate.** `ReadInbox` and `QueryRecords` are explicit finite gameplay actions with an API separate from developer inspection.

### 5.9 MSG — Terminals, sessions, messages

**R-MSG-01 — Terminal-only remote communication.** All remote citizen↔citizen and citizen↔authority communication uses fixed terminals. No radio, no implicit AI→citizen knowledge updates. An inspector cannot report from a machine unless a terminal is on that tile.

**R-MSG-02 — Shared transport.** Orders, reports, acknowledgments, service replies and peer messages share one transport. A message retains:
- ID, kind, authenticated sender account, recipient account/service;
- submission terminal/session and submission tick;
- delivery tick and first read tick;
- acceptance/ack references;
- subject and typed payload/claim;
- observation time and evidence source;
- related task, attempt, expectation, incident, case, order and prior-message IDs.

**R-MSG-03 — Stages.** `Posted → DeliveredToMailbox → Read → Accepted/ActedUpon` are distinct; the last stage applies only to actionable messages. Delivery proves neither awareness nor obedience.

**R-MSG-04 — Typed payloads.** Behavior depends only on typed payloads; display text is optional. No natural-language parsing in the simulation. A civilian message never becomes an authority order by its wording.

**R-MSG-05 — False reports and provenance.** The player may post false factual claims. Provenance (account, terminal, time) is authentic. Physical `ActorId` and authenticated `AccountId` are separate fields even when they currently match. No truth flag is visible to the authority.

**R-MSG-06 — Terminals.** A terminal has ID, tile, enabled/connected state, capabilities, permitted roles and session capacity (D-15). Normal login requires a valid held, holder-bound keycard.

**R-MSG-07 — Session sequence.**
1. Travel to a known suitable terminal and claim capacity.
2. `Login` (D-11): the connected service verifies the held credential and creates a session bound to actor, account, terminal and expiry.
3. `ReadInbox` (D-12): at start, capture the ordered IDs of already-delivered eligible messages. At completion, expose their permitted contents and mark them read. Messages arriving during the read need another read.
4. `PostMessage`/`PostReport` (D-13): at completion, post and schedule delivery at `completionTick + D-10`.
5. `QueryRecords` (D-14), when permitted: requires a valid session and presence through completion.
6. Logout, departure or expiry ends the session. Logout is instantaneous bookkeeping. Authorization and terminal operation are revalidated at start and at completion of each operation.

**R-MSG-08 — Cancellation and locality.** A cancelled read discloses nothing and marks nothing. A cancelled post transmits nothing. A session never follows its owner, and no later user inherits it. Leaving does not erase information already learned.

**R-MSG-09 — Late replies.** A reply arriving after logout or departure stays unread in the mailbox or response store. There are no off-terminal callbacks. Pending reads/queries, with their captured IDs, progress and session binding, are snapshotted.

**R-MSG-10 — Backends.** The authority and institutional services process their incoming messages automatically in phase 4, without terminal sessions. Wired sensors are separate channels and grant no access to people.

**R-MSG-11 — Channels.**

| Operation | Access rule |
| --- | --- |
| Read ordinary inbox; send work, patrol, peer messages | Known operational terminal permitting the account; valid normal session |
| Lost-card report and recovery | Headquarters recovery terminal/desk with scoped alternate identity verification |
| Job/personnel audit; disciplinary filing and requests | Supervisor's assigned Records Terminal plus role permission |
| Detention registration and completion receipt | Detention Intake service after physical arrival |

**R-MSG-12 — No bypass.** Generic messages never substitute for formal services. An informal accusation is not a reprimand; an informal loss claim does not issue a card.

**R-MSG-13 — Receipts.** Wired receipts (account, terminal, tick) for authentication or reads exist only where a fixture declares them. They reveal account use at that place and time only. A missing-card failure before authentication never automatically reports the loss.

**R-MSG-14 — Read state vs evidence of reading.** A citizen's mailbox read marker and the authority's evidence that an order was read are separate. Without a receipt or authorized audit, the dispatcher cannot infer awareness.

**R-MSG-15 — Check-in triggers.** Citizens visit terminals:
- at shift start;
- after finishing or failing work;
- when a report must be filed;
- periodically (D-19);
- while awaiting instructions or a response (D-20).

Triggers use only the actor's own clock and knowledge. An unread incoming message never triggers a visit or sets an awareness flag. Idle workers and guards still follow their check-in schedule.

**R-MSG-16 — Staggered check-ins.** Each actor's shift-start check-in is offset by a deterministic per-actor value in `[0, D-18)`, drawn from the actor's RNG stream at roster assignment.

*Rationale: without staggering, a cohort's shift start produces a single synchronized spike at every terminal.*

**R-MSG-17 — Terminal contention.**
- Claims queue FIFO per terminal.
- A session lasts at most D-16.
- An actor waiting for a reply releases the terminal after D-17 and retries at its next check-in.
- Shortages produce visible waiting and late reports.

**R-MSG-18 — Offline orders.**
- A dispatch creates an administrative assignment and posts an order.
- Default acknowledgment policy: post `OrderAccepted` before leaving to work, or a reasoned rejection when known prerequisites prevent acceptance.
- An actor follows the last order it actually read, including known expiry.
- Cancellations, reassignments and revocations are messages and have no effect until read.
- Offline guards check the authenticity and known expiry of their own order copy.
- Connected operations (intake, terminal queries) may check current status and legitimately reveal changes.

### 5.10 INS — Institutional records

**R-INS-01 — Records.**

| Record | Required information |
| --- | --- |
| Credential | Credential ID, account/holder, permissions, generation, active/revoked; linked physical card item |
| Terminal session | Terminal, account, actor, expiry, capacity claim, read/query/post progress |
| WorkAttempt | Task and attempt IDs, actor/account, posted/read/accepted state, reported outcomes |
| ReportExpectation | Attempt, qualifying response kinds, deadline, satisfaction evidence, timeout incident |
| Incident / Case | Stable cause key, evidence references, related task/attempt, attribution state, child goals/orders |
| Reprimand | Subject account, unique accepted case, policy, acceptance tick |
| Detention request / order | Requester, subject, evidence, threshold decision, roster, known search locations, destination, expiry, sentence |
| Escort group | Members, leader, each member's order copy, custody state, shared movement progress, intake claim |

"Record" means a data record, not C# `record` syntax.

**R-INS-02 — Storage.** Records are plain structs/classes in indexed collections behind `IRecordStore` interfaces in Contracts, with per-service access scoping.

**R-INS-03 — History.** Use identified append/amend entries plus indexed current projections. Full event sourcing is not required.

**R-INS-04 — Idempotency.** Every mutation carries an idempotency key. Duplicate deliveries never issue extra cards, count extra reprimands, duplicate orders, or occupy extra detention slots.

**R-INS-05 — Queries.** Queries return versioned, access-filtered subsets.

**R-INS-06 — Accountability ≠ guilt.** Records implicate authenticated accounts under policy. An inspection can reveal a wrong part without revealing its installer.

**R-INS-07 — Validation.** Institutional transitions validate permissions, evidence shape and provenance, policy and prior state. They never consult physical truth.

**R-INS-08 — Evidence transfer.** A submission copies permitted observation claims with provenance, or references evidence already formally submitted. A reference never grants access to a private knowledge store.

### 5.11 AUT — Authority, dispatch, deadlines, construction

**R-AUT-01 — Capacity goal.** Let `P` be registered population and `reserveBerths = ceil(P × reservePermille / 1000)`, with reservePermille from D-74. Require believed completed usable berths (including vacant/unassigned berths) plus pending committed berth capacity ≥ `P + reserveBerths`. Count each identified berth/project once. Assignments and current occupancy do not reduce usable capacity or create capacity. Berth semantics: R-LIF-09.

**R-AUT-02 — Growth flow.** When the goal is unmet:
1. The authority planner selects a Habitat expansion action.
2. The placement policy proposes a site.
3. A project is created and the site and material budget are committed administratively.
4. The dispatcher assigns a qualified worker using authority estimates.
5. The worker reads the order, acknowledges it, and collects real materials.
6. The worker travels to a completed neighbor and performs finite work.
7. The tile completes and joins the graph.
8. The worker reports completion from a terminal.
9. Beliefs update on receipt.

**R-AUT-03 — Running projects.** A project action may stay running while awaiting evidence. Predicted effects never cause physical or belief changes.

**R-AUT-04 — Placement.** Enumerate empty coordinates adjacent to the believed layout. Reject duplicate committed sites. Apply scenario construction bounds. Rank by declared costs with stable coordinate tie-breaking. Revalidate the real site when work starts.

**R-AUT-05 — Capacity values.** Track actual completed capacity, believed existing capacity and committed pending capacity separately. Commitments prevent duplicate projects. Committed capacity is never usable as a berth.

**R-AUT-06 — Reconciliation.**
- On accepted completion evidence, in one deterministic operation keyed by project: add the project's capacity to believed capacity and retire its commitment.
- Totals derive from identified project/tile records.
- Duplicate evidence changes nothing.
- Later contradictory evidence updates the belief record only.

**R-AUT-07 — False construction reports** may deceive records but never create tiles.

**R-AUT-08 — Habitat recipe.** D-35 materials, one qualified worker, D-35 work ticks, one empty adjacent coordinate reached from a completed tile, D-35 berths produced.

**R-AUT-09 — Dispatcher.** Chooses among known qualified personnel using authority estimates and known availability (rosters, known commitments). Assignments are posted as orders.

**R-AUT-10 — Expectation creation.** A work-result expectation is created when an `OrderAccepted` acknowledgment is received, not on dispatch. Unread or unacknowledged orders are tracked separately (R-AUT-18).

**R-AUT-11 — Deadline.**
```
est = travelToJob + workDuration + travelJobToTerminal + login + post + latency
      (authority estimates; travelToJob from the acknowledgment's terminal;
       travelJobToTerminal to the nearest known terminal by R-MOV-10)
deadline = ackReceiptTick + ceil(est × slack / 1000) + D-63
slack = clamp(learnedRatio + D-61, D-60, D-62)        (permille)
```

**R-AUT-12 — Off-duty extension (mandatory).** If the deadline falls after the assignee's known shift end, add the known off-duty gap until the next shift start plus D-18.

**R-AUT-13 — Learned correction.**
- Keep an integer EMA per (role, task kind), initially 1000 permille. Store the positive unscaled `est` and role/task key on each expectation at creation.
- On the first qualifying result received for an attempt, compute `sample = floor((resultReceiptTick − ackReceiptTick) × 1000 / est)`. Duplicate/late repetitions for the same attempt never add another sample.
- Represent α = D-64 as positive integer numerator/denominator `a/b` with `a <= b`. Compute `newRatio = floor(((b − a) × oldRatio + a × sample) / b)` in checked Int64 arithmetic. D-64 defaults to `1/8`.
- Use only received report timestamps and the stored estimate. No live route or private state is consulted. New estimates use the updated ratio; existing deadlines change only through an explicit permitted expectation policy.
- Snapshot the ratios, stored estimates and sampled-attempt IDs.

**R-AUT-14 — Overdue response.**
- An unsatisfied expectation opens exactly one `MissingWorkReport` incident, meaning "overdue; status unknown".
- The incident creates two independent goals:
  - `EstablishWorkerStatus` → patrol order.
  - `EnsureTaskCompleted` → replacement attempt, excluding the original assignee.
- Child-order IDs persist, so re-evaluation never dispatches again.
- Either branch proceeds when the other role is unavailable.

**R-AUT-15 — Escalation damping.**
- Use fixed simulation-hour buckets: `bucket = floor(simulatedMilliseconds / 3_600_000)`, with origin at tick 0.
- At each bucket's opening, capture the cohort's on-duty headcount from the authority's known roster/registration records. Never query live sleeping, positions or private needs. The snapshot of this count determines `budget = max(1, floor(headcount × D-65 / 1000))` for that bucket; record the count, budget and usage.
- At most `budget` unique incidents are admitted for a response in that cohort/bucket. Charge an incident once when its first response order is committed. Its linked patrol/replacement orders and delayed completion of either branch use the same admission token; they are not charged again.
- No eligible responder means no committed response order and no spent token. An unadmitted incident waits; budget-exhausted incidents remain open as `EscalationDeferred`.
- Admit eligible deferred incidents FIFO by opened tick and stable event sequence as new budget becomes available. Skip currently ineligible incidents with a recorded reason; they retain their queue age. A record-resolution/cancellation can remove an incident from this queue explicitly.
- Open-incident count and deferred backlog are measured separately and are not bounded by the per-hour admission budget.
- An overdue replacement may escalate at most D-66 levels.

Persist bucket counters, headcount snapshots, admission tokens and deferred queue state. Roster updates during a bucket affect its next budget, not an already-issued token. Cohort membership for an incident is fixed from the overdue attempt's known assignment when the incident opens.

*Rationale: optimistic estimates combined with an overdue response that adds two workers form a positive feedback loop at population scale.*

**R-AUT-16 — Satisfying responses.** `WorkCompleted`, `WorkBlocked` or `UnableToContinue` for the same attempt satisfy responsiveness. Acknowledgments, unrelated messages and other attempts' reports do not. Progress messages extend a deadline only if policy allows; extensions are deduplicated and bounded.

**R-AUT-17 — Deterministic authority choices.** Ties in dispatch and placement use stable ordering or the authority RNG stream; never hidden state.

**R-AUT-18 — Unacknowledged orders.** If no acknowledgment arrives within D-21 after posting (plus off-duty extension), the authority may repost or reassign under its own policy. This is distinct from `MissingWorkReport`.

### 5.12 LIF — Daily life, needs, routines

**R-LIF-01 — Citizen state.** At least:
- Identity: ID, debug name, role, qualifications.
- Physical: tile/segment, inventory, current action and progress.
- Access: credentials/account, permissions, known terminal/service locations, session.
- Work: current attempt, retained/suspended work, claims, case/order references.
- Needs: hunger, sleep pressure, work strain, bladder, social.
- Assignments: workplace, berth, routine cohort, known roster version.
- Information: knowledge facts, pending reports, mailbox reference, check-in schedule, next scheduled need thresholds.
- Custody/escort state.

Roles share all physical, need, action and information rules.

**R-LIF-02 — Station day.**
- Day length is D-04.
- Tests may configure a shorter day with explicitly scaled rates and windows; traversal and action semantics stay unchanged.
- Results report the configured day length. A shortened day is never described as 24 hours.

**R-LIF-03 — Cohorts and templates.**
- D-05 cohorts start 8 hours apart, with smaller staggered break groups inside each.
- The authority assigns templates deterministically from roles, known facilities and declared staffing targets; full workforce optimization is deferred.
- Daily footsteps are never scripted.

Template relative to shift start:

| Window | Goal |
| --- | --- |
| Hour before duty | Wake, toilet, cafeteria meal, terminal check |
| 8-hour duty | Read/accept work, tasks, staggered meal/toilet/rest breaks |
| After duty | Finish or suspend work safely, terminal check and reports, cafeteria |
| Off duty | Rest, converse, personal needs |
| 8-hour sleep window ending before pre-duty | Travel to own berth and sleep |

These are opportunities and priorities, not instantaneous state changes. Never double-book mandatory activities.

From H4, each duty shift includes at least one staggered, finite scheduled RestSpot break. That scheduled goal is independent of work-strain threshold D-53; the threshold governs additional strain-driven rest. Meals require their seek threshold, while the scheduled sleep window creates the sleep goal. A due routine goal can be deferred by higher-priority safety/urgent activity, but not by an unlimited supply of ordinary work (R-LIF-11). H3 may publish these windows as roster data without implementing the future physical activities.

**R-LIF-04 — Rosters.** A published roster is an institutional record. A new version reaches a citizen as a message and changes their plans only after reading. Initial rosters may be seeded as known. The authority knows intended schedules, not actual positions or needs.

**R-LIF-05 — Waking.** Messages never wake sleepers. Only a local observation or an explicitly modeled physical alarm can interrupt sleep externally.

**R-LIF-06 — Activities.**

| Activity | Place and prerequisites | Consequence |
| --- | --- | --- |
| Eat | Cafeteria service, available meal, free seat | Consumes a real meal; reduces hunger after D-55 |
| Sleep | Own assigned berth (or authorized berth in custody) | Reduces sleep pressure and strain pro rata |
| Rest | RestSpot with capacity | Reduces work strain pro rata; not sleep pressure |
| Use toilet | Toilet with free stall | Resets bladder after D-55 |
| Converse | Two available co-located consenting actors | Reduces social need; may share permitted facts |

Cafeteria, Bed, RestSpot and Toilet are service capabilities. Emergency rations are a separate configured action.

**R-LIF-07 — Need accounting.**
- Displayed needs are integers 0–1000; the authoritative value retains fractional progress in an integer accumulator. Rates/thresholds come from D-50..D-54.
- Let `H = 3_600_000` milliseconds per hour. Store `anchorQ` in `[0, 1000 × H]`, `anchorTick`, and the signed integer `ratePerHour`. For the current rate segment:

~~~text
Q(t) = clamp(
    anchorQ + ratePerHour × (t − anchorTick) × TickDurationMs,
    0,
    1000 × H)
displayedNeed(t) = floor(Q(t) / H)
~~~

- Use checked Int64 arithmetic. TickDurationMs is a positive integer; an hour need not be an integer number of ticks.
- At a rate change, re-anchor to the full `Q(t)` and the current tick before applying the new rate. Never round the accumulator or discard its remainder on sleep/wake, rest, work or other mode changes.
- A completed relief of x points subtracts `x × H` from Q and clamps; toilet relief sets Q to zero. Clamp at actual bounds, without storing hidden accumulation beyond a bound.
- Thresholds compare `Q(t) >= threshold × H`. Schedule the first tick satisfying the threshold under the current rate using integer ceiling division; reschedule/invalidate that event when the rate or accumulator changes. A threshold already active does not create repeated zero-time events.
- Snapshot the full accumulator anchor, rate, tick and clock configuration. Pure getters and serialization compute but never mutate/re-anchor.
- Chunked advancement preserves every crossing. T27 verifies short alternating activities, same-rate partitions, relief, clamping and continuation.

**R-LIF-08 — Real relief only.**
- Relief comes only from completed activities at real services with finite stock and capacity. Sleep and rest give pro-rata relief by elapsed ticks.
- Reaching a calendar time grants nothing.
- Meals, seats, stalls and berths use claims and queues.

**R-LIF-09 — Berths.**
- Each registered citizen has at most one assigned berth, and each berth has at most one assigned citizen. The housing policy aims to assign one to every citizen; unassigned citizens are valid during a capacity deficit. No hot-bunking in v1. The capacity unit is a usable completed berth, whether assigned or vacant.
- Assignment is distinct from the occupancy claim held while sleeping.
- Unassigned citizens seek a known unassigned free berth and obtain an authorized allocation before sleeping, or rest instead with an explained unmet sleep need. A vacant berth already contributes to capacity before it is assigned. A pending construction commitment contributes no physical sleeping place.

**R-LIF-10 — Basic services without card.** Cafeteria, rest and toilet use need no keycard. Custody, location and capacity still apply.

**R-LIF-11 — Priority and routine protection.** Custody, committed movement segments and declared indivisible actions are execution constraints, not competing goals. Re-evaluate priorities at the next permitted safe boundary (R-LIF-12); a critical need never teleports an actor or grants an unfinished action's result.

Highest eligible priority first:
1. Critical need (≥ critical threshold).
2. Known urgent orders (escort, patrol, detention response).
3. Urgent need (≥ urgent threshold).
4. Credential restoration needed for a permitted next activity.
5. Due required reports, due terminal check-ins, and due eligible scheduled meal/sleep/rest goals.
6. Ordinary duty work.
7. Optional additional rest and conversation.

Within priority 5, use earliest due tick, then a stable goal sequence. Retain a due goal across ordinary work choices until completed, explicitly found unnecessary, superseded by a read roster/order, or blocked with a reason. A blocked goal can permit other useful activity and is retried on its relevant availability/timing event; it must not freeze the actor forever.

Meal goals use their seek threshold. Scheduled rest does not require D-53; sleep uses the known sleep window. Periodic on-duty check-ins apply while awake and on duty; pre/post-shift visits follow the known roster. A private incoming message never makes a check-in due, and routine polling does not wake a sleeper.

Given an awake on-duty actor, an operational reachable terminal with available bounded service capacity, and no higher-priority interruption, a due check-in must be selected at the next safe boundary despite an unlimited ordinary-work backlog. Travel and queueing can still delay its completion. T22 includes this busy-worker case.

**R-LIF-12 — Interruption.**
- Committed movement segments complete before rerouting.
- Interrupted indivisible actions never grant their full result; partial progress follows each action's declared rule.
- Interrupted sleep and rest give pro-rata benefit.
- A guard reaching a critical need during escort produces `EscortInterrupted` at the next safe boundary (R-WF-38).

**R-LIF-13 — Conversation.**
- A finite `Converse` action with both participants present and holding a coordination claim. Nobody is in incompatible activities at once.
- Typed topics: routine, work status, witnessed problem, allowed request.
- Speakers share only facts they know and policy permits; recipients store them as `Heard`.
- Conversation never informs the authority or substitutes for formal channels.

**R-LIF-14 — Partner search.** Use the local spatial index with at most D-86 candidates. No all-pairs evaluation.

**R-LIF-15 — Shortages.** Shortages produce queues, known alternatives, report goals, or explained unmet needs. Never fabricate food, berths or capacity, and never freeze needs.

**R-LIF-16 — Coverage gaps.** Staffing gaps from delays are exposed in metrics and never fixed by spawning actors.

**R-LIF-17 — Custody life.** Detainees keep needs and use in-facility services under custody rules.

**R-LIF-18 — Player.** The player uses the same services and needs and may neglect them with the modeled consequences.

**R-LIF-19 — Authority ignorance of needs.** The authority learns need states only through configured reports or sensors.

### 5.13 WF — Task generators and institutional workflows

Names describe roles and capabilities. No workflow code may reference specific actor IDs, names, coordinates or fixture IDs.

#### Task generators

**R-WF-01 — Dirt.**
- Tiles with `Cleanable` receive soiling events at intervals drawn from the `soiling` stream (uniform in `[D-75/2, 3·D-75/2]`) and from `Spill` disturbances.
- Dirt is a nonnegative integer and a passive fact.

**R-WF-02 — Facility condition reporting.**
- Roles with the `ReportsFacilityCondition` duty (cleaners, supervisors, patrol) form a report goal on observing dirt ≥ D-76 or a faulty status light.
- The authority creates a cleaning or repair task on receiving the report.
- Optional cleanliness sensors may exist where declared.

**R-WF-03 — Machine faults.** `MachineFault` disturbances or wear events (`wear` stream, mean interval D-77) put a machine into a state requiring a part. The status light is passive; the required part type is `InspectionOnly`.

**R-WF-04 — QA verification.** After accepting a `WorkCompleted` report for a machine-repair task, the authority schedules a supervisor verification order with probability D-72 (drawn from its stream) after D-73. Fixtures may set D-72 to 1000‰.

#### Lost keycard

**R-WF-10 — Loss.** A card leaves inventory only through a modeled disturbance or action. Loss neither revokes the credential nor informs the authority.

**R-WF-11 — Discovery.** Discovery happens through an inventory/credential check (at check-in preparation or before a credential-required action) or through a failed credential-required action or login.

**R-WF-12 — Response.**
- Create `RestoreUsableCredential`.
- Retain the outstanding task: unused claims are released per R-RES-08; carried items stay.
- Finish the committed segment.
- Travel to a known headquarters Credential Desk.
- If a still-valid card has a known location, retrieving it is an alternative plan.

**R-WF-13 — Restricted recovery session.** Headquarters offers alternate identity verification (employee ID plus a retained recovery secret, modeled as a capability; no real credentials). The finite check (D-36) creates a terminal-bound session limited to that account's recovery case. It cannot read the ordinary inbox, query unrelated records, operate equipment, or act as another account.

**R-WF-14 — Recovery sequence.**
1. Restricted login.
2. Post a formal lost-card request.
3. The credential service validates and creates or reuses the case keyed by (old credential, account).
4. Acceptance revokes the old credential and authorizes exactly one replacement.
5. The reply is delivered to the recovery mailbox; the worker must read it.
6. Finite issuance (D-37) consumes one blank card and creates a uniquely identified card at the service output.
7. Physical pickup.
8. Normal login and read of current orders.
9. Resume or replan retained work. A cancellation read now takes precedence.

**R-WF-15 — Recovery invariants.**
- The formal report never creates or teleports a card.
- The old card item persists wherever it is; its revoked credential fails connected checks.
- Retries return the same case and issued-card reference.
- Missing stock, service or verification produces an explained wait or escalation.
- Recovery access never widens.

#### Missing cleanup report

**R-WF-20 — Communication states.** Distinguish:
- posted/delivered but unread;
- read but not acknowledged;
- accepted with the result outstanding.

Only the last creates a work-result expectation (R-AUT-10).

**R-WF-21 — Overdue response.** Per R-AUT-14 and R-AUT-15. Orders reach patrol and replacement staff only through their routine check-ins and reads. Posting is not responding.

**R-WF-22 — Patrol.**
- Destinations come from permitted information: last known location, job site, assigned workplace, berth, or a defined search route.
- Each candidate is inspected locally; an empty location yields a negative observation only.
- Findings reach the authority only when posted from a terminal.

**R-WF-23 — Replacement.** The replacement physically visits the tile and rechecks dirt on arrival and completion. If the tile is already clean, they report "no work needed". Dirt never goes below zero and effects are never duplicated.

**R-WF-24 — Late original report.** A late report stays on the original attempt. It may update the parent task belief and motivate a cancellation to the replacement. It never completes the replacement's attempt.

**R-WF-25 — Separate resolutions.** Welfare/investigation resolution and work resolution are separate states. Finding the worker does not prove the tile is clean, and vice versa.

**R-WF-26 — Offline supersession.** A superseded worker continues on their last read order until they read the cancellation. Lease expiry may free a work position but never their inventory.

#### Wrong cartridge, audit, reprimand

**R-WF-30 — Wrong installation.** Possible through the ordinary install action (R-RES-12). The machine stays faulty or fails its functional test. A contradictory completion claim changes nothing physical.

**R-WF-31 — Verification trigger.** Only a legitimate failure report, an authorized sensor observation, or a QA order (R-WF-04) creates the supervisor's verification need. Hidden truth never summons anyone. Orders are learned at terminals.

**R-WF-32 — Inspection.** The supervisor travels and performs a finite inspection (D-30), revealing the installed part type and the required specification. This creates `ResolveJobDiscrepancy`. It never reveals the installer.

**R-WF-33 — Audit.**
- The supervisor travels to their assigned Records Terminal, logs in, and queries (D-14) the permitted job history: attempt, slot, required part, assigned account, acceptance/completion submissions, timestamps, report IDs, amendments.
- The accountability policy links evidence to the responsible account.
- Missing records or ambiguous handoffs yield `NeedsReview`.
- Gaps are never filled from developer logs.

**R-WF-34 — Canonical case key.** `(JobId, AttemptId, TargetSlotId, ViolationType)`. Reinspection or new report IDs reuse the case.

**R-WF-35 — Filing and acceptance.**
- The supervisor posts the case with evidence references and the queried record version.
- The personnel service validates permissions, evidence shape and provenance, and policy, without checking the world.
- Acceptance appends exactly one reprimand.

**R-WF-36 — Qualifying count.** Count accepted reprimands within the window D-68 that were accepted after the subject's last completed detention.

**R-WF-37 — Request vs approval.**
- The service replies with the accepted case and the count.
- The supervisor reads it and, when count ≥ D-67, posts `RequestDetention`.
- The authority separately validates requester permission, accepted records, threshold and existing orders, then approves or records a reason to reject or wait.
- Duplicate requests never create duplicate teams.

#### Escort and detention

**R-WF-38 — Escort protocol.** The order contains: subject account and known identity, justification, expiry (D-71), roster (D-70 guards), rendezvous, known search candidates, destination facility and sentence (D-69). It has no live tracker.

States: `Authorized`, `AwaitingReaders`, `TeamForming`, `Searching`, `Escorting`, `AwaitingIntake`, `Detained`, `Released`, `Blocked`, `Cancelled`. Each participant's awareness is tracked separately.

1. Each guard reads and acknowledges at a terminal. Guards who have not read the order cannot be pulled in.
2. Guards obtain required equipment through real inventory actions (D-38).
3. Travel to the rendezvous or search area; identify the target by local observation.
4. Begin escort only when the required guards and the target are co-located, the guards hold authentic unexpired order copies, and physical prerequisites hold.
5. The target's work is suspended and custody restricts its actions. Central dispatch never freezes a distant target.
6. Coupled movement: all members depart and arrive each edge together with shared frozen timing. Members, route, ticks and progress are persisted.
7. Finite intake (D-39) at the actual detention service with a current authorization check and capacity.
8. `Detained` and slot occupancy begin only at successful intake. A receipt then informs the authority.

- A deterministic leader selects routes; others follow the protocol they read.
- Offline guards act on their copy.
- A learned revocation or expiry stops custody at a safe boundary, followed by a terminal report.
- Stale locations produce searches.
- Unavailable guards leave an explained pending order.
- A full facility means waiting under guard or an explicit alternative policy. Reservation is not occupancy.
- A disrupted group or a critical need yields `EscortInterrupted`; movement never continues with silently missing members, and intake never completes remotely.

**R-WF-39 — Player in custody.** When the player-actor is the target, its input switches to auto-cooperate: player intents are filtered to the custody-permitted set, and escort movement drives it. Resistance gameplay is deferred.

**R-WF-40 — Sentence and release.** At intake, schedule release at `intakeTick + sentence`. Release is a finite intake-service action:
- frees the slot;
- ends custody at the facility tile;
- posts a release notice to the detainee's mailbox.

Work resumes only after the detainee reads current orders.

**R-WF-41 — Credentials during detention.** Credentials remain valid but custody forbids terminal use except in-facility services declared for detainees.

**R-WF-42 — Reprimand history.** Reprimands remain in history. The qualifying count follows R-WF-36.

**R-WF-43 — Cooperative target in v1.** Resistance and combat are deferred and produce explicit pending states, never remote completion.

### 5.14 SNP — Snapshots, replay, digest

**R-SNP-01 — Contents.** Snapshots capture everything needed to continue:
- World and content identity; tiles, equipment, items, inventories.
- Citizens, movement, actions, needs and their next scheduled thresholds.
- Clock, cohorts, roster versions, routine progress, service queues, conversations.
- Jobs, attempts, expectations, incidents and child orders; authority order records, beliefs, evidence and learned ratios; commitments and claims.
- Citizen knowledge, including eviction recency; full need accumulators, rate anchors and pending threshold events.
- Pending reports, sensors, mailboxes and read state, sessions, terminal claims, pending reads/posts/queries with captured IDs and scope, check-in schedules.
- Credential registry, card links, recovery cases.
- Cases, reprimands, requests and decisions, each actor's order copies, escort groups, equipment, custody, detention claims and occupancy, release schedules.
- Scheduled events, sequence counters, all RNG states, pending planning requests, processed-time marker.

**R-SNP-02 — Format.** An explicit, versioned, hand-written or generated serializer. No reflection over arbitrary object graphs.

**R-SNP-03 — Round-trip stability.** serialize → deserialize → serialize is byte-identical.

**R-SNP-04 — Canonical digest.** A SHA-256 over a canonical encoding of consequential state with deterministic ordering. It excludes wall-clock metrics, rendering details and log buffers.

**R-SNP-05 — Replay bundle.** Contains scenario and schema version, content digest, build ID, seed and configuration, external command stream, starting checkpoint if any, and the expected failure or event of interest. A seed alone is not a replay bundle.

**R-SNP-06 — Tracing neutrality.** Enabling tracing never changes sequence IDs, RNG consumption or outcomes.

### 5.15 RUN — Runner and automation

**R-RUN-01 — Operations.** `validate`, `run`, `batch`, `replay`, `inspect`, `benchmark`, `sweep`. Inspect covers checkpoints, actors, mailboxes, sessions, attempts, incidents, cases, escorts and other records, without changing gameplay.

**R-RUN-02 — Exit codes.** 0 when required checks pass; nonzero for invalid input, failed assertions, runtime errors or imposed limits. The precise category appears in structured output.

**R-RUN-03 — Scenario contents.**
- Schema version and ID; catalog references; tiles or generator settings.
- Citizens and roles; resources and equipment.
- Initial public knowledge and authority records; sensors and reporting rules; terminals, services, credentials, mailbox contents and declared receipts.
- Check-in schedules, permissions, deadline policies, institutional records; schedules, staffing, need settings, facilities, conversation policy.
- Goals, policies, tuning profile.
- Disturbances, scripted actors, horizon, assertions, metrics.

The schema obeys R-VER-01.

**R-RUN-04 — Statuses.** `completed`, `assertion_failed`, `runtime_error`, `budget_exceeded`, `invalid_scenario`. Failed jobs, false reports, missed deadlines or sabotage success are outcomes, not errors.

**R-RUN-05 — Results.**
- One JSON object per run (JSONL) on stdout or file, with a versioned schema.
- Fields: run ID, scenario/version, seed, build/content IDs, effective configuration and tuning values, population, completed tiles, simulated duration, status, assertion failures, gameplay metrics (§9), performance, reproduction info, artifact paths.
- Human diagnostics go to stderr.

**R-RUN-06 — Batch.**
- Build once; run worlds in-process with bounded parallelism (default: logical cores).
- Worlds share only immutable definitions.
- A seed's result is identical whether run alone, sequentially, or concurrently.

**R-RUN-07 — Benchmark.** Runs declared workloads and reports §9.3 information.

**R-RUN-08 — Sweep.** Runs a scenario across a grid of tuning-knob values (R-TUN) and seeds and emits per-cell aggregated metrics.

### 5.16 OBS — Observability

**R-OBS-01 — Events.** Every event has ID, tick, type and relevant entity IDs. Payload fields are added only where useful.

**R-OBS-02 — Event types.** At least:
- Planning and orders: `OrderIssued`, `PlanSelected`, `PlanUnavailable`.
- Movement and items: `MovementStarted`, `TileArrived`, `ItemClaimed`, `ItemMoved`, `MaterialConsumed`, `ActionFailed`.
- Information: `ObservationRecorded`, `ReportSubmitted`, `ReportReceived`, `BeliefChanged`, `KnowledgeEvicted`.
- Construction and jobs: `ProjectCommitted`, `ConstructionCompleted`, `JobBlocked`, `JobCompleted`.
- Terminals: `TerminalLoginSucceeded`, `TerminalSessionEnded`, `MessagePosted`, `MailboxDelivered`, `MessageRead`, `OrderAcknowledged`.
- Expectations: `ReportExpectationCreated`, `ReportOverdue`, `IncidentOpened`, `EscalationDeferred`.
- Credentials: `CredentialLossObserved`, `RecoveryAccepted`, `CredentialRevoked`, `CredentialIssued`.
- Discipline: `RecordQueryCompleted`, `DiscrepancyLinked`, `ReprimandAccepted`, `DetentionRequested`, `DetentionAuthorized`, `GuardOrderRead`.
- Daily life: `RoutinePublished`, `RoutineRead`, `NeedThresholdReached`, `NeedActivityCompleted`, `ConversationCompleted`.
- Custody: `TargetLocated`, `EscortStarted`, `EscortInterrupted`, `IntakeCompleted`, `Released`.

**R-OBS-03 — Causal links.** Events carry cause IDs linking order → read → action → observation → report → belief → next order. For T18–T22 decisive transitions, causal links are mandatory and include:
- policy ID and version;
- evidence/knowledge IDs;
- actor vs account;
- terminal, session and message stage.

**R-OBS-04 — Privilege marking.** Trace entries mark whether information is privileged truth or agent-visible.

**R-OBS-05 — Trace levels.** Batches use compact metrics and cheap assertions. Replays use detailed tracing. Full event serialization is optional.

**R-OBS-06 — Answerable questions.** Inspect and trace tools answer:
- Why was this goal selected, and which facts did the planner use?
- Which report introduced a belief?
- What were the predicted and actual travel times?
- Why is the actor waiting, and which job owns the item?
- What changed when the plan failed, and which evidence triggered the response?
- Was an order posted, delivered, read or accepted, and through which terminal/session?
- Which case caused this reprimand, and which order authorized the escort?

**R-OBS-07 — Causal chain export.** `replay --trace focused --chain <eventId>` outputs the causal ancestry and descendants of an event as JSON.

**R-OBS-08 — Trace viewer.**
- `tools/viewer`: a static HTML+JS page with no build step and no network access. It loads trace JSONL plus snapshot JSON.
- It shows a top-down grid per Y layer and a timeline scrubber.
- Actor positions are interpolated from segment data; clicking an entity shows its event history.
- From H2: layer toggles for truth, authority belief and selected-actor knowledge, plus message stages.
- The viewer is a development aid. No test depends on it.

### 5.17 TUN — Tuning profiles

**R-TUN-01 — Knobs.** Authority competence is exposed as named profiles in `content/tuning/*.json`:
- sensor density and enabled sets;
- deadline slack min/margin/max and grace;
- QA sample rate and delay;
- reprimand threshold and window;
- check-in intervals;
- incident cap;
- patrol and guard staffing targets;
- detention sentence;
- soiling and wear rates.

**R-TUN-02 — Profiles are data.** No code branches on profile names.

**R-TUN-03 — Overrides.** Runner may override knobs per run; results record effective values.

**R-TUN-04 — Sweep.** `sweep` (R-RUN-08) maps knob ranges to metrics such as false-overdue rate, discovery latency, detention frequency and job throughput.

*Rationale: the truth–belief gap is the game's difficulty dial; tuning it needs measurement.*

### 5.18 UNI — Unity integration contract

**R-UNI-01 — Single authority of rules.** Unity consumes the same core and content. It is never the source of truth for task success, arrival or knowledge.

**R-UNI-02 — Exposed state.** Tile visual keys, actor segment progress, action type/target/progress, inventories, orders and reports, terminal and inbox state, case messages, credential changes, escort/custody progress, and developer views of actual vs believed state. Player action requests go through R-ACT-01.

**R-UNI-03 — Packaging.**
- Preferred: prebuilt `netstandard2.1` DLLs in `Assets/Plugins`.
- If source-shared: asmdefs with `noEngineReferences: true`, and the Mind asmdef referencing only the Contracts asmdef.
- Include an `IsExternalInit` shim only if `init` is used.

**R-UNI-04 — Interpolation.** Rendering converts rational segment progress to floats. Float results never feed back into simulation.

**R-UNI-05 — Future spatial changes.** Sub-tile geometry, collision or different movement costs are explicit versioned changes with new equivalence tests. The frontend never applies its own travel or observation rules. The all-traversable abstraction holds until explicitly replaced.

**R-UNI-06 — Minimal integration path.** A minimal adapter or precise instructions to load a recorded scenario or snapshot and display it. Unity is never required for tests.

**R-UNI-07 — Cross-runtime check.** When Unity is available, compare digests of a fixture set across .NET, Unity Mono and IL2CPP. Report mismatches; do not assume equality.

---

## 6. Defaults

All values are prototype defaults: configurable, tunable, and not measured. Fixtures state their own values explicitly; the tiny timings in T03/T07 are fixture values, not defaults. At the default 100 ms tick, one hour = 36,000 ticks. Need integration uses exact elapsed milliseconds under R-LIF-07.

### 6.1 Time and movement

| ID | Setting | Default |
| --- | --- | --- |
| D-01 | Tick duration | 100 ms simulated |
| D-02 | Tile traversal (all families) | 40 ticks (uniform placeholder; fixtures must demonstrate family and instance overrides) |
| D-03 | Authority nominal traversal per step | 40 ticks |
| D-04 | Station day | 864,000 ticks |
| D-05 | Duty cohorts | 3, shift starts at 0 / 288,000 / 576,000 |

### 6.2 Terminals and messaging

| ID | Setting | Default |
| --- | --- | --- |
| D-10 | Message transmission latency | 2 ticks |
| D-11 | Login | 30 ticks |
| D-12 | Read inbox | 40 + 10 per captured message, max 200 ticks |
| D-13 | Post message/report | 60 ticks |
| D-14 | Query records | 150 ticks |
| D-15 | Session capacity per terminal | 1 |
| D-16 | Max session duration | 1,200 ticks |
| D-17 | Max wait at a terminal for a reply | 300 ticks |
| D-18 | Shift-start check-in window | 18,000 ticks |
| D-19 | Periodic on-duty check-in interval | 36,000 ticks |
| D-20 | Check-in interval while awaiting a response | 6,000 ticks |
| D-21 | Unacknowledged-order repost/reassign after | 36,000 ticks |

*Rationale: v1.1's 1-tick terminal operations made terminals effectively free, so contention could never appear. Fixtures still use 1-tick values where specified.*

### 6.3 Physical work and services

| ID | Setting | Default |
| --- | --- | --- |
| D-30 | Equipment inspection | 100 ticks |
| D-31 | Install cartridge (incl. functional self-test) | 300 ticks |
| D-32 | Pickup / put down | 10 ticks |
| D-33 | Fabricate component | 1,200 ticks |
| D-34 | Clean one dirt unit; dirt per spill | 300 ticks; 4 units |
| D-35 | Habitat construction | 4 material units, 18,000 work ticks, 8 berths |
| D-36 | Headquarters identity recovery check | 300 ticks |
| D-37 | Card issuance | 600 ticks |
| D-38 | Equip weapon/equipment | 50 ticks |
| D-39 | Detention intake / release | 300 ticks |

### 6.4 Needs (scale 0–1000, rates per hour)

| ID | Need | Accrual | Relief | Seek | Urgent | Critical |
| --- | --- | --- | --- | --- | --- | --- |
| D-50 | Hunger | +60 always | Meal −500 | 300 (in meal window) | 700 | 900 |
| D-51 | Bladder | +100 awake, +40 asleep | Toilet → 0 | 500 | 800 | 950 |
| D-52 | Sleep pressure | +50 awake | Sleep −100/h | sleep window | 850 | 1000 |
| D-53 | Work strain | +60 while working | Rest −120/h, sleep −60/h | 500 (on break) | — | — |
| D-54 | Social | +20 always | Conversation −300 | 400 (off duty) | — | — |

| ID | Activity durations | Default |
| --- | --- | --- |
| D-55 | Eat / toilet / rest block / conversation / sleep window | 9,000 / 1,800 / 6,000 / 1,800 / 288,000 ticks |

Sanity check: 16 h awake × 50 = 800 sleep pressure, cleared by 8 h × 100. 24 h × 60 = 1,440 hunger against 3 meals × 500.

D-53's seek threshold governs additional strain-driven rest. Each normal shift also supplies a scheduled rest break under R-LIF-03, so a rested worker does not need to exceed 500 strain to take the required break.

### 6.5 Authority and institutions

| ID | Setting | Default |
| --- | --- | --- |
| D-60 | Deadline slack minimum | 1,500‰ |
| D-61 | Slack margin over learned ratio | 250‰ |
| D-62 | Slack maximum | 4,000‰ |
| D-63 | Deadline grace | 3,000 ticks |
| D-64 | Learned-ratio EMA α | 1/8 (integer shift) |
| D-65 | Incident admission/dispatch cap | 50‰ of known cohort on-duty headcount per fixed hour bucket; floor, min 1 (R-AUT-15) |
| D-66 | Max replacement escalation depth | 2 |
| D-67 | Reprimand threshold for detention request | 2 |
| D-68 | Reprimand qualifying window | 7 days (6,048,000 ticks) |
| D-69 | Detention sentence | 1 day (864,000 ticks) |
| D-70 | Guards per escort | 2 |
| D-71 | Detention order expiry | 2 days (1,728,000 ticks) |
| D-72 | QA verification sample rate | 250‰ (fixtures: 1,000‰) |
| D-73 | QA verification delay | 18,000 ticks |
| D-74 | Berth reserve | 50‰ of registered population, rounded up (T11: 0) |
| D-75 | Mean soiling interval per Cleanable tile | 288,000 ticks |
| D-76 | Dirt reporting threshold | 2 units |
| D-77 | Mean machine wear interval | 2,592,000 ticks (3 days) |

### 6.6 Planner and knowledge

| ID | Setting | Default |
| --- | --- | --- |
| D-80 | Max plan depth | 8 |
| D-81 | Node expansion budget per request | 512 |
| D-82 | Candidates per bound variable (K) | 3 |
| D-83 | Planning requests per tick per world | 64 (excess queued FIFO) |
| D-84 | Retry after BudgetExhausted | 50 ticks, doubling, cap 1,000 |
| D-85 | Private facts per actor | 256 |
| D-86 | Conversation partner candidates | 4 |
| D-87 | Staleness threshold for other actors' locations | 6,000 ticks |

### 6.7 Safety limits

| ID | Setting | Default |
| --- | --- | --- |
| D-90 | Max events per tick per world | 1,000,000 → `runtime_error` |

### 6.8 Station guidelines (content, not engine constants)

- Reference station: at least 1 ordinary terminal per 8 on-duty citizens. Terminals sit in some work areas, not all.
- Population workload: declare the terminal ratio; start at about 1 per 25 on-duty citizens.
- Cafeteria seats, stalls and berths sized for the normal-day fixture with a declared margin.

---

## 7. Verification framework (anti-scripting)

Passing fixtures must demonstrate ordinary policies, planning and execution producing every intermediate step. These rules make that checkable.

**R-VER-01 — Restricted scenario schema.** A scenario has only these sections:
- `initialState`
- `disturbances`: catalog environment events only, e.g. `Spill`, `MachineFault`, `CardLoss`, `TraversalChange`, `SensorToggle`, `StockChange`
- `scriptedActors`: actor IDs flagged scripted, with action commands through the shared action API
- `assertions`
- `metrics`

The loader MUST reject:
- post-tick-0 commands to non-scripted actors;
- record mutations after tick 0;
- knowledge injection after tick 0;
- unknown disturbance types.

**R-VER-02 — Response-chain purity.** Fixtures for T18–T22 and T26 declare `responseChainRoles`. The harness walks the causal chain from the triggering disturbance/initial condition (R-OBS-03) and fails if a response action was driven by a scripted command. The T20 player's initial installation/false report are permitted triggers. System-driven custody movement is not a scripted response; it must be caused by the guard protocol. For an extended player-target case, post-release player inputs may test the permitted-action boundary but cannot create the institution's approval, escort or release.

**R-VER-03 — Generators.** Every institutional fixture (T07, T11, T18–T22) is produced by a parameterized generator in `scenarios/generators`. Parameters include:
- layout permutation and service placement;
- actor ID permutation;
- traversal and duration overrides;
- staffing levels;
- terminal placement.

Tests run at least 20 generated variants per fixture alongside the canonical one. Each positive generator preserves and validates the prerequisites and temporal/resource bounds required for its declared outcome. A transform that removes a prerequisite becomes a labeled negative variant or an invalid fixture; it must not retain an unconditional success assertion. Outcome-changing timing variants use T24's actual property contract.

**R-VER-04 — Holdout.**
- The reviewer runs generators with private seeds and parameter sets.
- Core and Mind MUST NOT contain fixture IDs, actor names, coordinates or scenario-specific branches. T00 greps for scenario IDs and fixture actor names.
- Recognizing a fixture by any means is a defect.

**R-VER-05 — Metamorphic testing.** See T24.

**R-VER-06 — Snapshot fuzzing.** See T25.

**R-VER-07 — Structural checks.** See T00.

---

## 8. Acceptance tests

Each test lists the requirements it verifies. Handoff assignment is in §4.

Separate three kinds of result:
1. Implementation correctness.
2. Intended simulation behavior (late arrivals, wrong beliefs).
3. Game quality.

Intended AI weaknesses are not invariant failures. Do not assert that every job finishes except where a fixture guarantees adequacy.

### T00 — Structural checks
*R-ARC-02, R-ARC-03, R-DET-05, R-VER-04*
- Mind's assembly references only Contracts and the BCL.
- Contracts, Mind and Core reference no Unity or network assemblies.
- The banned-API analyzer is active (a deliberately banned call in a test-only compile check fails).
- Core and Mind contain no scenario IDs or fixture actor names.

### T01 — All families traversable
*R-TIL-01..03*
- A connected path contains every family; every tile is reachable, including Hull, Truss and Conduit.
- Vertical adjacency works.
- Diagonal jumps and movement through absent cells are rejected.

### T02 — Connectivity validation
*R-TIL-02, R-TIL-09*
- Disconnected stations fail validation, naming offending components and coordinates.
- An empty station fails for populated scenarios.

### T03 — Exact movement timing
*R-MOV-01..04, R-CLK-02..03*
- Fixture: A(0,0,0) T=2, B(1,0,0) T=6, C(2,0,0) T=10.
- Depart A at 0; arrive B at 4, C at 12.
- At tick 11 a local action at C fails with `NotAtRequiredTile`.
- Edge costs are symmetric and same-tile travel is zero.
- Results are identical under repeated and chunked advancement.

### T04 — Cheaper detour wins
*R-MOV-06..07*
- Tiles (0,0,0)=2, (1,0,0)=100, (2,0,0)=2, (0,0,1)=2, (1,0,1)=2, (2,0,1)=2.
- Route (0,0,0)→(2,0,0): direct costs 102, detour costs 8; require the detour.
- Matches the Dijkstra oracle.
- Includes an override that lowers `minEdgeCost` mid-run; routes stay optimal.

### T05 — Wrong estimate, correct movement
*R-MOV-10*
- T03 fixture, authority nominal = 2, last known tile A.
- Estimate to C = 4; actual arrival = 12.
- Premature arrival is a defect.

### T06 — Frozen segment
*R-MOV-05, R-SNP-01*
- A cost change during a segment leaves its arrival tick unchanged; later segments use the new cost.
- Holds across snapshot/restore (H1).

### T07 — False delivery, delayed discovery via terminals
*R-RES-05, R-MSG-03, R-MSG-07..08, R-KNW-04, R-KNW-10, R-CLK-07*

Fixture:
- A–B–C in a line, each T=5, each with its own terminal. Player starts at storage A, inspector at C; delivery destination is B.
- The component stays at A.
- Orders are delivered but unread at start. Login/read/inspect/post = 1 tick; latency = 2. No sensors.
- This is a scripted timing fixture: both actors are scripted through shared actions.

| Tick | Player (A) | Inspector | Authority |
| --- | --- | --- | --- |
| 0–1 | Login | Login at C | — |
| 1–2 | Read order | Read order | — |
| 2–3 | Post false completion | Depart C at 2 | — |
| 5 | — | In transit | Receives false report |
| 7 | — | Arrives B | Believes delivery |
| 7–8 | — | Inspect B | — |
| 8–9 | — | Login at B | — |
| 9–10 | — | Post correction | — |
| 12 | — | — | Receives correction |

Require:
- Neither actor knows the order before tick 2.
- The false belief holds over ticks 5–11.
- The component never moves.
- The inspector discovers at 8; the authority learns at 12.
- The C session is invalid at B.
- Absence at B reveals nothing about A.

### T08 — Hidden-state isolation
*R-ARC-03..04, R-KNW-02, R-MOV-10, R-AUT-09*

Pairs of worlds have identical permitted inputs and RNG state for the decision maker being observed, differing only in hidden state. Each fixture must provoke a meaningful decision before the hidden difference becomes observable. Compare that decision maker's actual decision/intent trace until a permitted observation differs; the traces must match. An idle actor or authority is not a passing demonstration.

- **H2 variants (citizen planner):** an unobserved external item's location, with the same actor knowledge; a citizen with vs without an unread mailbox message. Decisions match until a permitted local observation or completed read.
- **H3 variants (authority planner and dispatcher):** an unobserved item's location; an unreported lost card; an overdue cleaner's hidden position; the true installer behind a disputed record; a guard target's live location. Include availability helpers as well as planner inputs.

### T09 — Exclusive competition
*R-RES-09, R-RES-11*
- Two jobs need one component; exactly one obtains it.
- The other has an inspectable waiting, blocked, failed or cancelled state.
- Release-on-cancel follows the documented rule.
- **Arbitration correctness:** controlled test RNG draws demonstrate that low- and high-ID contenders can win, exactly one exclusive winner is selected, and each generated contender permutation contains every eligible request once. Replay reproduces each outcome.
- **Fairness diagnostic:** for 64 same-tick claims over 1,000 documented seeds, report win counts and chi-square statistic/p-value. A single statistical cutoff is not an automatic correctness gate; flag evidence of bias for investigation alongside the deterministic arbitration checks.

### T10 — Alternative prerequisite chains
*R-ACT-05..08, R-ACT-16*
- (a) Repair using an existing component.
- (b) Seeded knowledge says no finished component is available but recipe inputs are known → fabricate, then repair. The planner never queries hidden inventory.
- (c) Discovery variant: a failed pickup or inspection triggers a replan.
- (d) Neither route possible → meaningful `NoPlan` or waiting reason, with no fabricated resources.

### T11 — Autonomous construction
*R-AUT-01..08, R-TIL-05, R-RES-02*

Fixture:
- One Habitat with 8 berths; 12 registered citizens; reserve 0.
- Exactly enough material for one Habitat; at least one legal site; positive report latency.
- Needs thresholds may be set beyond the horizon; this must be declared in the fixture.

Require:
- One project is committed through planning.
- A real worker reads the order, collects materials, travels and works.
- No tile exists before completion; materials are consumed once; the new tile is reachable.
- Actual berths = 16 at completion.
- Believed capacity changes only on evidence.
- No duplicate project while the report is pending.
- Duplicate completion evidence credits nothing extra and leaves no stale commitment.
- Growth stops once the goal is met.

**Vacant-capacity control:** 20 citizens, 24 believed/actual usable completed berths, 20 assignments, four vacancies, no pending project, and default 50‰ reserve. Target capacity is 21, so no expansion is initiated. T11's original twelve-citizen/eight-berth fixture remains valid with four initially unassigned citizens.

### T12 — Snapshot continuation
*R-SNP-01..04, R-CLK-02*
- An uninterrupted run and a save/restore run produce equal digests.
- Required checkpoints (by handoff):
  - Mid-segment (H1).
  - Pending inbox read; pending post; claimed terminal (H2).
  - Restricted recovery session; overdue investigation with deferred incident; case awaiting reply; partially informed guard team; mid-escort segment (H3).
  - Mid-sleep; mid-conversation; pending routine boundary with unread roster (H4).

### T13 — Advancement and presentation independence
*R-CLK-03, R-KNW-12, R-SNP-06*
Compare these execution modes; final digests must match:
- one large advance vs many small;
- frequent vs no pose/observation/mailbox getter calls;
- tracing on vs off.

Include an unread cancellation and a delayed service reply. Only gameplay actions mark mail read.

### T14 — Batch isolation
*R-RUN-06, R-DET-06*
- The same seeds run alone, sequentially, and at concurrency 2, 4 and 8 produce identical per-run results.
- From H3, include credentials, mailboxes, cases, discipline and escorts.

### T15 — Population persistence and throughput
*R-LIF-01, R-RES-02, R-ACT-15, R-AUT-15*

Setup:
- At least 256 registered citizens (amended 2026-10-30 from 2,048); declared active workforce at least 128; horizon 36,000 ticks.
- The start tick is chosen so a cohort shift boundary falls inside the window.
- Terminal ratio declared.
- Enough work, facilities and supplies are provisioned.

Require:
- Every counted active worker uses the real planning/execution path, completes movement, and attempts a real work or transfer action.
- Completed, blocked and failed outcomes are counted separately.
- Needs and activities progress for everyone; routines are not disabled.
- Unique actor counts are reported for planning, movement, resource interaction and work.
- Resource accounting holds throughout, including under simultaneous requests.
- Several recovery, overdue-response and audit/escort instances occur, with participants and terminal/service operations counted.

### T16 — Bounded planning failure
*R-ACT-08, R-ACT-13*
- A forced tiny budget yields `BudgetExhausted`, deterministic backoff, and continued simulation.
- It is never reported as `NoPlan`.

### T17 — Normal operation and blocked-work explanations
*R-RES-11, R-OBS-06*
- With adequate supplies, useful work occurs.
- Every unfinished active job is progressing or carries an inspectable reason.

### T18 — Lost keycard recovery
*R-WF-10..15, R-MSG-07, R-MSG-13, R-RES-08, R-VER-02*

Fixture:
- A worker holds a previously accepted credential-required task.
- A `CardLoss` disturbance; no known card location.
- Headquarters with blank stock.
- The authority has not been told.

Required chain: discover → retain work → travel to HQ → restricted login → post loss → acceptance and revocation → read reply → issuance → pickup → normal login/read → resume.

Assert:
- No scripted step in the chain.
- Loss alone neither revokes nor informs.
- Normal login fails without the card; recovery succeeds without it.
- The recovery session exposes only its case.
- A delivered but unread authorization gives no knowledge.
- Issuance consumes stock once and obeys service timing.
- The old card item persists; its credential fails later connected checks.
- Retries create no extra active card.
- Inventory, claims and the task stay accounted for.

Negative controls:
- Valid card → no trip.
- No stock or service → explained block.
- Known location of a still-valid card, with strictly lower estimated retrieval cost than every eligible replacement plan → retrieval chosen. Other placements follow ordinary cost selection; knowing a location alone does not require the longer plan.
- A cancellation waiting in the inbox is applied after access is restored.

Generated variants per R-VER-03.

### T19 — Missing cleanup report, two responses
*R-AUT-10..17, R-WF-20..26, R-MSG-15, R-VER-02*

Fixture:
- Cleaner A reads and acknowledges at a terminal; the work tile has no terminal.
- A's actual route is much slower than the authority's estimate; the authority cannot see why.
- Cleaner B and patrol staff are on normal check-in schedules.

Require:
- One expectation goes overdue after same-tick deliveries are processed.
- One incident creates two goals and two orders; the replacement is not A.
- Recipients act only after reading and acknowledging.
- B visits and cleans or finds the tile clean.
- Patrol searches a location justified by permitted information.
- Both post results from real terminals.

Assert:
- No orders before the deadline; a qualifying result received exactly at the deadline suppresses escalation.
- An unread assignment is not treated as missing accepted work.
- Wrong-attempt, unrelated or duplicate messages neither satisfy nor extend.
- No duplicate dispatches on re-evaluation.
- Each branch proceeds alone when the other role is unavailable.
- Changing only A's hidden position leaves the initial orders and search destinations unchanged.
- A's late report stays on A's attempt.
- No negative dirt or double consumption.
- An offline cancellation has no effect until read.
- Welfare and work resolution are independent.
- Off-duty variant: A accepts near shift end; R-AUT-12 extension prevents a false overdue.
- At least one variant leaves an order unread for a measurable period; results distinguish posted from responding.

### T20 — Wrong cartridge → audit → second reprimand → detention
*R-RES-12, R-WF-30..43, R-INS-04..08, R-MSG-11, R-VER-02*

Fixture:
- A scripted player installs a fitting wrong cartridge and posts a false completion through shared actions.
- QA sample rate 1,000‰.
- A supervisor with an assigned Records Terminal.
- One unambiguous attempt and completion account.
- One prior distinct reprimand within the window.
- Threshold 2; 2 available guards; equipment in an armory; detention capacity.
- The supervisor's prior knowledge is insufficient to skip the audit.

Required chain:
1. Verification order read at a terminal.
2. Travel and inspect.
3. Mismatch known locally, installer unknown.
4. Travel to the Records Terminal, log in, query.
5. Link evidence to the account.
6. Post the case; exactly one new reprimand.
7. Read the count; post `RequestDetention`.
8. Authority approves separately; posts guard orders.
9. Guards read, acknowledge, equip and rendezvous.
10. Locate the target via permitted information.
11. Establish custody, escort, intake.
12. Receipt delivered to the authority.

Assert:
- No hidden provenance used.
- Remote lookup fails; a query result after departure gives no knowledge.
- Reinspection and new report IDs add no reprimand.
- Request and approval are distinct transitions.
- No spawned guards; guards with unread orders are not pulled in.
- Dispatch does not freeze the target.
- Escort members have coherent progress, equipment and order copies.
- Offline guards cannot learn status changes; connected intake can.
- `Detained` and occupancy occur once, at intake. Authority belief may lag.
- Player custody uses auto-cooperate (R-WF-39).
- Extended horizon variant: release is scheduled at sentence end and completes after the finite release action; the slot is freed on completion. Current-order reading precedes resumed work. Use an autonomous target for the complete resume chain, with the bad-installation trigger separately proven through shared actions, or use declared player inputs only to test post-release permissions under R-VER-02. A subsequent single reprimand does not trigger detention (R-WF-36).

Negative controls:
- Correct cartridge → no case.
- No prior reprimand → count 1, no detention.
- Ambiguous records → `NeedsReview`.
- Stale location → search.
- No guards or full detention → explained pending.
- Interrupted escort → no remote completion.

### T21 — Terminals, awareness, privacy, offline instructions
*R-MSG-01..18, R-KNW-12..13*
- **Delivery vs awareness:** traces match a world without the message until a routine visit and completed read. No hidden trigger.
- **Read snapshot:** M1 is present at read start; M2 arrives during the read; only M1 is disclosed and marked.
- **Locality/authentication:** login away from a terminal fails; read or post without a session fails; departure, terminal failure or revocation before completion blocks disclosure or posting.
- **Capacity/privacy:** no concurrent exclusive sessions; a second user sees only their own data.
- **Replies:** an approval in the mailbox enables nothing before reading; a pending result survives logout unread.
- **Offline orders:** a central cancellation does not affect an offline cleaner or escort; known expiry or a connected check can.
- **Pure inspection:** diagnostic reads mark nothing and consume no randomness.
- **Peer messages:** remote content becomes knowledge only via the sender's post and the recipient's read.
- **Contention (H3 onward):** a cohort shift start with staggered offsets (R-MSG-16), enough capacity and a declared arrival horizon produces finite, draining queues; report maximum queue length and wait. H2 tests terminal contention without requiring cohorts/rosters.

Use meaningful work/escort decisions in the isolation cases.

### T22 — Daily station life
*R-LIF-01..19, R-MSG-15..16, R-VER-02*

Fixture:
- A connected small station with staggered cohorts and known starting rosters.
- Adequate food, berths, seats, rest areas, toilets, terminals and conversation partners.
- At least two full configured days. A shortened day is allowed only per R-LIF-02, and the day length is reported.

Require, per eligible citizen per configured day:
- eats and sleeps for positive time;
- over the horizon also rests, uses a toilet and converses.

Also require:
- Windows and cohort transitions recur on day 2.
- Activities happen at real services with finite duration; food stock decreases.
- Berth assignment and occupancy stay distinct; capacities are respected.
- Rest cannot replace sleep.
- Each known duty shift supplies its scheduled rest opportunity even when strain stays below D-53. Ordinary queued work cannot continually outrank that due break.
- Conversations involve awake co-located participants sharing only known, permitted facts.
- Work continues across cohorts.
- Inventory, job progress, pending reports and orders survive interruptions.
- Per-citizen participation, unmet needs, queues, lateness and staffing coverage are reported.

Variants:
1. Shortage → queues, alternatives, report goals; no resets without the real activity.
2. Urgent interruption → pause at a valid boundary, satisfy the need, resume.
3. Unread roster while asleep → no waking, no change until read.
4. Knowledge boundary → needs and conversations don't inform the authority.
5. Custody → a detainee's needs continue with in-facility services.
6. Replay → checkpoint mid-sleep and mid-conversation with a pending boundary and unread roster; chunked continuation matches.
7. Sustained work → keep an ordinary work backlog available, with an awake on-duty actor and adequate reachable services. A due check-in and eligible scheduled break are selected at their next permitted safe boundaries, then executed through actual travel/service actions. No unread message triggers the visit. Record any delay caused by a higher-priority obligation, committed action or service queue.

### T23 — Planner cost sensitivity
*R-ACT-06..09, R-MOV-11*
- (a) An actor must post a report and knows two terminals. Swapping their known travel costs (via known traversal overrides) changes the chosen terminal. Equal costs give the same deterministic choice across runs.
- (b) A repair has a distant existing component vs nearby fabrication inputs. Varying distance flips the choice at the predicted crossover.
- (c) A cost change the actor does not know about does not change the plan.

### T24 — Metamorphic properties
*R-VER-03..05*

For T07, T11, T18, T19 and T20, each variant declares its transformed inputs, preconditions, expected property and comparison normalization. General correctness is distinct from demanding an unchanged outcome.

| Transformation | Required invariance |
| --- | --- |
| Coordinate translation | Translate all spatial inputs: layout, services, positions, bounds, disturbances, commands and assertions. Require exact semantic trace equality after inverse translation. |
| Actor ID renaming | Consistently rename references and preserve each logical actor's RNG stream/state. Require causal equivalence only where all causally relevant ordering/ties are preserved; use no-ID-sensitive-tie fixtures when needed. Other ID permutations are generated cases checked for ordinary correctness. |
| 90° rotation about Y; X mirror | Transform all spatial data and require connectivity, adjacency, accounting and information boundaries. Require the same decisive causal chain only in controlled fixtures with unique relevant routes and unchanged encounter/observation opportunities. |
| Uniform traversal scaling ×k | Validate positive recomputed edge timing, frozen current segments, adjacency, accounting and information isolation. Changed decisions, deadlines, encounters and incident chains are allowed. |

If a test deliberately changes an actor's RNG key or an ID-dependent tie order, it cannot demand that actor's original choices. Do not add scenario-aware behavior to make a transformation pass.

Traversal-only scaling is not a change of time units: work durations, check-ins, expiry, thresholds and horizons can remain fixed. T26 tests the authority's response to hidden slow travel. Any stronger full-time-scaling test must explicitly scale all relevant temporal/rate inputs and satisfy integer-rounding constraints; it is not required here.

### T25 — Snapshot fuzzing
*R-SNP-01..04*
- For every fixture and 20 generated seeds, pick 5 ticks from the test RNG.
- Snapshot → restore → continue to the horizon; the final digest equals the uninterrupted run.
- Every snapshot is byte-identical across serialize → deserialize → serialize.

### T26 — Escalation caps, backlog recovery, and calibration
*R-AUT-11..15*

1. **Dispatch budget:** induce more overdue incidents than one cohort/hour budget. For each fixed simulation-hour bucket, use the headcount/budget snapshot from R-AUT-15 and count unique incidents whose first response order was committed in that bucket. Require count ≤ budget. The two linked response orders share one token. Incident creation, unresolved count and deferred backlog are separate metrics and may exceed that rate limit.
2. **Finite-burst recovery:** end the declared triggering burst, provide eligible staff/services, and allow a declared sufficient drain horizon. Deferred incidents are admitted FIFO when eligible or explicitly resolved/cancelled. No duplicates and no indefinite starvation of a remaining eligible incident. Report backlog and oldest age over time. A transient increase or a flat empty queue is not a failure.
3. **Controlled learning:** use repeated real work and terminal reports with stable duty/service conditions, true traversal costs ×3 unknown to the authority, and enough completed attempts for calibration and evaluation blocks. Choose documented fixture settings with nonzero initial deadline errors and feedback; zero-error/insufficient-feedback setup is invalid for this calibration fixture. Verify every EMA update against R-AUT-13 and confirm duplicate reports add no samples. The evaluation block must show the predeclared improvement for this controlled workload. Population-wide improvement in every random world's second half is not promised.
4. **Normal population metrics:** report overdue-then-completed attempts, completed-job denominator, unfinished attempts, open/deferred incidents, queue age, ratios and effective budgets. A hard false-overdue bound (default 50‰ when used) applies only to a named adequately provisioned fixture with a declared warm-up and measurement window. A zero-error result is valid for the normal-operation fixture.

Use ordinary policies for all response orders. Fixtures may configure initial state and a finite environment disturbance schedule, but may not close records or script responses. Calibration/drain horizons may be longer than T15's one-hour workload; report them explicitly.

### T27 — Exact need integration across mode changes
*R-LIF-07..08, R-LIF-12, R-SNP-01, R-CLK-03*

- **Short alternating modes:** with 100 ms ticks and initial sleep pressure 500, alternate 100 one-tick sleep segments (−100/hour) and 100 one-tick awake segments (+50/hour), without hitting a clamp. Require the exact final accumulator `500 × 3_600_000 − 500_000 = 1_799_500_000`; displayed floor is 499. Re-anchoring to 400 points is a failure.
- **Partition invariance:** split a rate segment into many subsegments without changing its rate or total duration. The complete accumulator and threshold schedule match the unsplit run. Mixed-rate cases match the exact sum of each segment's rate × elapsed milliseconds when no clamp intervenes.
- **Relief and bounds:** discrete meals/toilet actions modify the same accumulator exactly. Test upper/lower clamps and mode changes after saturation; no hidden overshoot survives beyond a clamp. Different orderings that hit a clamp need not have the same result.
- **Threshold event:** from Q = 0 at +60/hour with 100 ms ticks, threshold 1 is first reached at tick 600, not tick 599. Rate changes invalidate/reschedule old crossings, and an already-active threshold does not create a zero-time loop.
- **Continuation:** checkpoint with a nonzero fractional remainder and pending threshold, restore, and change modes. Full accumulators, threshold events and digests match uninterrupted and differently chunked advancement. Getters and serialization do not re-anchor.

---

## 9. Measurements and workloads

### 9.1 Metrics

**Run basics**
- Simulated ticks; wall-clock time; simulated seconds per wall second.
- Population; completed tiles; declared workforce and unique active actors per category.
- Memory, with method labeled; allocations where supported.

**Planning and routing**
- Planner requests and expansions, split by outcome.
- Path requests and expansions.

**Work**
- Jobs completed, blocked, cancelled and failed; blocked reasons.

**Information**
- Travel estimate error.
- Discovery-to-receipt latency.
- Terminal visits, queue length and wait distribution, login/read/post counts, unread-message age.
- Posted/read/accepted orders.
- Overdue expectations; false-overdue rate; deferred incidents; learned ratios.

**Institutions**
- Independently resolved response branches; recovery and resumption.
- Unique cases and reprimands; detention, escort and release progress.

**Daily life**
- Meal, sleep, rest, toilet and conversation completions per citizen; unmet needs; service queues.
- Routine lateness; planned vs actual staffing coverage.
- Due routine/check-in delay and its reason, including committed actions, urgent obligations and service queues.

**Health**
- Knowledge evictions.
- Outstanding projects and claims.
- Accounting failures; assertion and runtime failures.

Wall-clock values are diagnostics. They never affect decisions or digests.

### 9.2 Workload tiers

1. Tiny exact fixtures.
2. Small generated stations, at least 1,000 seeds.
3. Medium interaction scenarios.
4. Population: at least 256 citizens (T15/T26; amended 2026-10-30 from 2,048).
5. Endurance runs (multi-day), invoked separately.

### 9.3 Performance reporting

Record:
- Hardware/environment, OS, runtime, Release configuration.
- Population and tiles, simulated duration.
- Scenario, content and tuning versions.
- Concurrency and trace level.
- Measured results.

No speed target is promised. Establish a baseline before optimizing.

Likely priorities after profiling:
- Avoid per-citizen ticking; event-driven replanning and threshold scheduling.
- Bounded candidate generation.
- Versioned navigation reuse.
- Compact state and fewer temporary allocations.
- Spatial lookup.
- Selective tracing.
- Parallel worlds.

Optimizations must preserve T03–T08, T12, T13, T24, T25 and T27.

---

## 10. Reference station and fixtures

### 10.1 Reference station

Connected, with 12–32 citizens, finite supplies, and enough travel distance for location to matter. It includes:

- Storage with food, construction material, correct and wrong-but-fitting cartridges, blank cards, and guard equipment (or a Defense armory).
- Habitat berths, cafeteria (meals and seats), rest spots, toilets.
- An Industrial workstation with a fabrication recipe, and machines with cartridge slots.
- Headquarters on a Command tile with a Credential Desk and recovery terminal.
- Ordinary terminals in some, not all, work areas (§6.8).
- A supervisor's assigned Records Terminal.
- A Defense tile with armory and Detention Intake with capacity and detainee services.
- Corridors and a valid adjacent construction site.
- Workers, at least two cleaners, a supervisor, patrol staff, and at least two guards.
- Public service directory, credentials, roles, check-in schedules, explicitly scoped authority knowledge.
- Staggered rosters and staffing targets.
- Power and Life Support with simple declared operational state. All families present and traversable.

### 10.2 Fixtures

Canonical fixtures, each with a generator per R-VER-03:
- `movement_*` (T03–T06)
- `false_delivery` (T07)
- `resource_competition` (T09)
- `alternative_planning` (T10)
- `planner_costs` (T23)
- `autonomous_construction` (T11)
- `lost_keycard` (T18)
- `missing_cleanup_report` (T19)
- `wrong_cartridge_detention` (T20)
- `terminal_messages` (T21)
- `daily_station` (T22)
- `population_hour` (T15/T26)
- `escalation_burst` and `deadline_learning` (T26)
- `need_integration` (T27)

Positive fixtures have the resources, time and terminal access to complete. Shortage and ambiguity variants assert appropriate blocking.

Fixtures initialize state and disturbances only. They never schedule response chains or per-person daily actions (R-VER-01..02).

---

## 11. Definition of done and report-back

### 11.1 Per-handoff gate

A handoff is done when:
- A clean checkout builds without Unity.
- `dotnet test` passes every test listed for this handoff and all earlier handoffs.
- Generated variants for the listed fixtures pass.
- The README documents build, test, run, batch, replay, inspect (and later benchmark/sweep) for the implemented scope.
- `docs/decisions.md` records every choice not dictated here.
- Unimplemented and deferred items are listed explicitly.
- Nothing is claimed without evidence.

H4 additionally requires:
- T00–T27 pass with needs enabled, subject to declared fixture thresholds and the stage applicability rules.
- At least 1,000 small seeded runs and one 256-citizen workload measured (amended 2026-10-30 from 2,048), if the environment supports them; otherwise state why.
- Unity compatibility verified or precise instructions provided.

### 11.2 Report-back contents

1. Repository structure.
2. Exact commands used, with their actual output summaries.
3. **Traceability matrix:** every requirement ID in scope → implementing files → tests → status (`pass` / `fail` / `not run (reason)` / `partial (detail)`).
4. Test results, including failed or unexecuted checks.
5. Measured batch and population results with §9.3 metadata (H4).
6. Demonstration traces for the handoff:
   - H2: T07 false report and delayed correction; T21 offline/unread evidence.
   - H3: T11 construction; T18–T20 causal chains with terminal visits and intermediate states; duplicate-record evidence.
   - H4: T22 per-citizen routines, shortages and interruptions; T26 damping.
7. How to reproduce and inspect a selected run.
8. Simplifications, unresolved design issues, open questions, and the current versus later-owned clauses of cross-cutting requirements.
9. The next concrete implementation step.

Headless tests do not establish whether controls feel good, whether information is legible to players, or whether the game is fun. Those require a playable presentation and human evaluation.

---

## 12. References

1. [Unity 6.0 — .NET profile support](https://docs.unity3d.com/6000.0/Documentation/Manual/dotnet-profile-support.html)
2. [Unity 6.0 — C# compiler and language version](https://docs.unity3d.com/6000.0/Documentation/Manual/csharp-compiler.html)
3. [Microsoft — Target frameworks in SDK-style projects](https://learn.microsoft.com/en-us/dotnet/standard/frameworks)
4. [Microsoft — System.Random](https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0) (reproducibility limitations)
5. [Microsoft — BannedApiAnalyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md)
6. [Jeff Orkin — Agent Architecture Considerations for Real-Time Planning in Games](https://ojs.aaai.org/index.php/AIIDE/article/view/18724)

The traversal formula, timing defaults, schemas and acceptance cases are project design decisions, not facts from these references.
