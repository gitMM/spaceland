# H1 requirement traceability

Scope follows build spec 1.2.1 §4 feature ownership. `pass` means the implemented H1
clauses have passing automated evidence; it does not claim later-owned functionality.
`partial` explicitly identifies the missing clause or later owner. Test names below
refer to methods in `tests/Station.Tests`; T00/T03/etc. are the specification test IDs.

File abbreviations: **Contracts** = `src/Station.Contracts/Primitives.cs`; **Mind** =
`src/Station.Mind/TravelEstimates.cs`; **Model**, **Topology**, **World**, **Snapshots**,
**Pcg32** = corresponding files in `src/Station.Core`; **Scenario**, **Artifacts**,
**Program** = corresponding files in `src/Station.Runner`.

| Requirement | Implementation | Verification | Status / ownership |
| --- | --- | --- | --- |
| R-ARC-01 | solution, src/tests/content/scenarios/docs/tools/unity | standalone build; repository layout | pass |
| R-ARC-02 | project references; core data types | T00 assembly/source checks; standalone build | pass |
| R-ARC-03 | Mind csproj, Contracts | T00 assembly/reference checks | pass |
| R-ARC-04 | Contracts, World.ActorView | T00; T03 no local tile during transit | partial: minimal H1 actor view; full knowledge/authority/institution views H2/H3 |
| R-ARC-05 | Contracts.ActionIntent; World.Submit | T03; finite item transfer | pass for H1 intents; operational Mind H2 |
| R-ARC-06 | global.json, csproj files, lockfiles | Release build | pass; SDK 9/newest locally supported LTS runtime 8 |
| R-ARC-07 | Directory.Build.props; shared sources | C# 9 build | pass |
| R-ARC-08 | typed IDs, shared definitions, sparse topology | source review; 1,000 generated worlds | pass for H1; hot-path inventory indexing later |
| R-ARC-09 | World.AdvanceTo; Program.batch | T14 | pass |
| R-ARC-10 | Runner I/O; core in-memory serializer | T00 source checks; build | pass |
| R-DET-01 | World, Snapshots | T12, T13, T14, T25 | pass for H1 state |
| R-DET-02 | Pcg32; World streams; Snapshots | fixed vector, stream independence, restore | pass |
| R-DET-03 | integer coordinates/ticks/quantities; checked arithmetic | T03; arithmetic/edge checks | pass; exact need accumulators H4 |
| R-DET-04 | sorted collections; topology insertion order | T04 tie check; T14 | pass |
| R-DET-05 | BannedSymbols.txt; Directory.Build.props | T00 deliberately banned compile fails RS0030 | pass |
| R-DET-06 | instance-owned state/RNG/counters | T14 alone/sequential/2/4/8 concurrency | pass |
| R-DET-07 | sequence and coordinate ordering | T04, T14 | pass |
| R-CLK-01 | World.AdvanceTo | T03, T13 | pass |
| R-CLK-02 | processed marker; Submit; Snapshots | T03 repeated zero and rejected late command; T12 | pass |
| R-CLK-03 | event-driven scheduler | T13, T25 | pass |
| R-CLK-04 | EventPhase; sorted event queue | T06; arrival plus same-tick pickup | partial: disturbance/completion/intent live; sensing/delivery/decision H2 |
| R-CLK-05 | ScheduledEvent.Sequence | T03, T06, T13 | pass |
| R-CLK-06 | World.Submit/Start | T03; scripted JSON fixture | pass for H1 action sources; NPC planner H2 |
| R-CLK-07 | phase-order guard; positive action durations | T03; finite transfer | partial: local H1 phase guard; message latency H2 |
| R-CLK-08 | phase enum reserves delivery before decision | structural source | partial: deadline behavior H3 |
| R-CLK-09 | no mailbox/knowledge behavior in H1 | declared ownership | partial: H2 |
| R-CLK-10 | MaxEventsPerTick; positive duration validation | EventCapFailsExplicitlyAndArithmeticIsChecked | pass |
| R-CLK-11 | scheduler skipping | T13; T25 | pass for all H1 transitions |
| R-CLK-12 | configured path node budget per request | T04 budget distinction; T13 | pass for H1; planning queue H2 |
| R-TIL-01 | TileFamily; content/tiles.json | T01; catalog loader | pass; only traversal implemented for families |
| R-TIL-02 | Topology.ValidateConnected/Contains | T01, T02 | pass |
| R-TIL-03 | Coord.Neighbors; Topology | T01; T04 tie order | pass |
| R-TIL-04 | no movement occupancy reservations | model/source; transfer revalidation control | pass for H1; finite service capacity H2+ |
| R-TIL-05 | uncompleted tiles excluded; no removal API | topology rules | partial: physical construction H3 |
| R-TIL-06 | TileDefinition, Tile, snapshots | presentation digest test; T01 overrides | partial: ID/family/cost/visual/capability metadata present; recipes, equipment, capacity, reservations H2/H3 |
| R-TIL-07 | definition capability metadata independent of family | model/catalog | partial: actual services H2+ |
| R-TIL-08 | Coord/tile-per-cell validation | T01; duplicate-coordinate loader | pass |
| R-TIL-09 | JsonData; Scenario validator | RunnerTests malformed schema/IDs/enums/coordinates | pass |
| R-TIL-10 | positive cost and action durations | RunnerTests; WorldConfig; T03 | pass for H1 durations; terminal/services H2 |
| R-TIL-11 | adjacency uses only completion and coordinates | T01 | pass; credential/custody semantics H2/H3 |
| R-MOV-01 | Topology.EdgeTicks | T03; checked-arithmetic control | pass |
| R-MOV-02 | A* cost sum; same tile | T03, T04 | pass |
| R-MOV-03 | ActiveAction route/index/segment/owner | T06, T12, T25 | pass |
| R-MOV-04 | World.InspectActor/ActorView | T03 rational progress and locality | pass |
| R-MOV-05 | StartSegment; persisted frozen ticks | T06 with restore | pass |
| R-MOV-06 | Topology.FindPath/MinEdgeCost | T04 plus 1,000 A*/Dijkstra comparisons | pass |
| R-MOV-07 | open-set sequence; neighbor ordering | T04 tie fixture | pass |
| R-MOV-08 | PathOutcome; executor failure mapping | T01 no-route/invalid; T04 budget | pass |
| R-MOV-09 | topology version, no cache | T04 changed cost/minimum | pass by no-cache implementation |
| R-MOV-10 | Mind.TravelEstimates.Authority | T05 estimate 4/arrival 12 | pass |
| R-MOV-11 | no operational citizen planner yet | H1 scope boundary | partial: known-layout estimator/information updates H2 |
| R-MOV-12 | no operational authority yet | H1 scope boundary | partial: initial/public and evidence-driven layout H3 |
| R-RES-01 | ItemLocation; item owner validation | finite transfers; duplicate pickup control | pass for H1 owners; stash/slot/service owners H2/H3 |
| R-RES-02 | AssertAccounting; single-owner transfers | generated worlds; T25; transfer tests | pass for H1; explicit recipes/sources/sinks H2 |
| R-RES-03 | Contracts record-store extension; no record-to-item effects | structural boundary; transfer-only executor | pass for H1 separation; administrative records H2 |
| R-SNP-01 | explicit complete H1 encoding | T06, T12, T25 | pass for H1 fields; extend at each later handoff |
| R-SNP-02 | Snapshots hand-written encoder/decoder | T12; format/model checks | pass |
| R-SNP-03 | deterministic field/collection order | T12, T25, checkpoint envelope roundtrip | pass |
| R-SNP-04 | SHA-256 consequential encoding | T13; presentation mapping control | pass |
| R-SNP-05 | ReplayBundle; initial checkpoint and embedded data | RunnerTests self-contained replay; manual CLI replay | pass |
| R-SNP-06 | trace-independent event counter | T13; resumed trace IDs | pass |
| R-RUN-01 | Program operations | manual validate/run/batch/replay/inspect | pass for H1; benchmark/sweep H4 |
| R-RUN-02 | Program exit/status handling | manual CLI checks; RunnerTests invalid scenarios | pass |
| R-RUN-03 | Scenario DTO/schema | canonical fixture; strict rejection tests | pass for H1 inventory; later inventories H2–H4 |
| R-RUN-04 | RunResult.Status; errors distinct from failed actions | exact fixture early-pickup outcome; negative tests | pass for H1; imposed global limits later |
| R-RUN-05 | RunResult; JSONL; labeled diagnostics | manual run/batch artifacts | pass for H1 metrics; tuning/life metrics later |
| R-RUN-06 | in-process bounded Parallel.For | T14; 1,000-seed CLI batch | pass |
| R-OBS-01 | TraceEvent; World.Emit | exact/replay trace equality | pass |
| R-OBS-04 | explicit visibility field | emitted trace schema | pass |
| R-OBS-05 | TraceEnabled; compact batch output | T13; detailed replay/trace-none batch | pass |
| R-OBS-08 | tools/viewer HTML/CSS/JS | JavaScript syntax check | partial: implemented H1 grid/layers/interpolation/history; visual QA blocked by browser file-URL policy; H2 information layers later |
| R-UNI-03 | netstandard DLLs; unity/README.md | builds without Unity | pass packaging instructions; no Unity installation/execution claimed |
| R-VER-01 | restricted Scenario schema, actor flags | unknown sections/disturbances and nonscripted command rejection | pass |
| R-VER-06 | snapshot fuzz tests | T25 canonical/generated, 20 seeds × 5 each | pass for H1 fixtures |
| R-VER-07 | StructuralTests | T00 assembly/source/analyzer checks | pass |

Not yet accepted by a separate reviewer. T07–T11, T15–T24, T26–T27 and later-state
variants of T12–T14/T25 are not run because their systems are not implemented.
No H4 population, institutional chain, gameplay or cross-runtime claim is made.
