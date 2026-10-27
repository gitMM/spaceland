# Curved grid tiles: rewrite plan

2026-10-27. **Phase 0 is done** (see [Phase 0 results](#phase-0-results)); Phases 1–4 are not started. The
[design doc changes](#design-doc-changes) were agreed and applied to `modular-settlement-tile-design.md` the same day.

## Goal

Make settlement tiles look designed on the curved Foundry grid:

1. **No warped architecture.** Doors, windows, corridors, hatches, turrets and other equipment are the same size in
   metres everywhere and never shear.
2. **Odd cells look special on purpose.** The flattened cells on the cube-to-sphere diagonals get their own
   designed version per family: ridges, keels and crowns, not squashed boxes.
3. **It stays fast.** Edits rebuild no slower than today, and preferably faster.

## Constraints

- **The grid does not change:** `BlockGuide.Map`, corner positions, topology, picking colliders and the save
  format stay as they are. No save version bump; everything new is derived from the stored corners.
- **No global solver**, relaxation or WFC. All rules stay local, as in `SettlementSolver`.
- **Each phase lands on its own**, with `tools/tile-lab.sh report` green and new pictures. Any later phase can be
  stopped without leaving the build broken.

## What we measured

Python mirror of `BlockGuide.Map`, on a sphere grown from the seed, the Settlement example and the Orbital shell.

| Finding | Numbers |
|---|---|
| Face size depends on **direction**, not radius | Exposed faces 4.8 m at cube-face centres, 2.3–3.3 m toward the cube's edges and corners (2.1×). Inner skin at radius 4: 3.4 m median; outer skin at radius 8: 3.5 m |
| Cell depth varies too | 2.8–5.6 m, so anything sized by the cell's depth (decks, tower bodies) varies 2× |
| First shells round the seed are tapered | The seed is 2.9 m across; first-shell outer faces are 4.5 m |
| Diagonal cells are not boxes | Radius-8 skin: 1350 box, 180 wedge, 8 apex cells (12% special). Orbital shell: 18% |
| Folds are easy to detect | Angle between adjacent face normals: 0–35° (folded) or 50°+ (box), nothing between on spheres. Stretched guides (Slab, Settlement 8×6×8) have a few pairs at 35–50° |
| **Wedge** shape | A triangular prism with a near-equilateral ~5.5 m cross-section (slopes 5.6 m, roof 4.9–5.6 m across), 2.5–3.9 m along the ridge. The roof is a nearly flat band continuing the skin; the slopes meet underneath in a sharp 46–60° keel |
| **Apex** shape | A curved triangular crown with 4.3–4.8 m edges over a point ~5 m deep: a tetrahedron cap. There are 8 per sphere, where three ridges meet |
| Already metre-correct | Pads and hatches (`metricAnchor` → `CurvedTileMesh.Metric`), heights above a face (`Transform` adds height in metres), Habitat pane count and height (`TileBuilder.WindowRow`) |
| Not metre-correct | Turret, tower, dish and tank footprints (but not their heights, so proportions change); corridors and conduits completely (no surface frame, so the 2.8 m tube can shrink to ~1.4 m); portholes, collars, hangar doors, Industrial gantries |
| Cost today | Flat stage: 700 cells in 3.1 s (~4.4 ms per tile). Foundry example: 138 tiles in 2.5 s (~18 ms per tile). An edit rebuilds the edited cells plus a 2-cell halo |

The scripts are kept with the session, not the project. Phase 0 moves the measurements into the lab.

## Approach

There are three kinds of geometry.

1. **Box cells (~85%)** keep today's pipeline: build in station space, then `Bend`. Every fixed-size part
   is wrapped in an **anchor**, the mechanism pads and hatches already use, so it comes out at true size.
   Only stretchy parts follow the cell.
2. **Special cells** (wedge, apex) and **connectors** (Corridor, Conduit) are built directly in curved space from
   the cell's real corners, with no `Bend`. Their parts are placed in a rigid frame, so they are true size by
   construction, and this path is cheaper than bending.
3. **Families** get a **kit** for special cells. The 12 ridges and 8 crowns become structure the station is
   built around, not defects.

## 1. Cell shapes

```csharp
public enum CellShape { Box, Wedge, Apex }
public readonly struct ShapeInfo { CellShape kind; int spine; byte roof; Vector2 size; } // spine: lattice axis along a ridge; roof: merged faces; size: m
```

- **Rule:** two adjacent (not opposite) faces whose outward normals are within **40°** are merged. One merged pair
  is a Wedge; its spine is the axis neither face lies on. Three mutually merged faces are an Apex. Anything else is
  a Box.
- **Cost:** computed from the 8 corners (6 bilinear face normals and 12 dot products) when a cell is added.
  `BlockConstruction` caches it and drops it on removal. Corners never move while their cell exists, so the result
  never changes. It works the same for legacy saves and stretched guides; borderline 35–50° cells stay Box and look
  as they do today.
- **`size`** is the cell's usable face size in metres, from its edge lengths. Size classes and placement rules
  read it.

**Exposure** comes from neighbours at build time:

| Exposure | When | Built as |
|---|---|---|
| Ridge | Both roof faces open (the normal outer skin). The roof is a nearly flat band: the crest parts are what make it read as a ridge | Crest, plus open slopes and gables |
| Keel | Roof covered and slopes open (a wedge hanging into open space, e.g. one layer under the skin): the slopes meet in a sharp 46–60° edge pointing out | Slopes and the shared keel strip |
| Half | One roof face covered: the open half is the foot of the neighbour's wall | Shared fillet with family trim (like today's hull fillets) |
| Gable | An end face along the spine is open: the end of a ridge run | Gable part |
| Crown | Apex roof open | Crown part |
| Spike | Apex roof covered, slopes open: a three-sided point | Three slopes |

A single wedge never makes a V-groove. Inner surfaces of hollow spheres are smooth across the diagonals: the folded
cells there are empty.

## 2. Fixed-size and stretchy parts (box cells)

**Fixed parts never scale.** Sizes come from the design doc's scale table.

| Part | Size |
|---|---|
| Doorway | 1.0 × 2.1 m |
| Corridor opening and tube | 1.5–2 × 2.4 m opening; 2.8 m octagon tube; 4.3 m collar |
| Deck | 2.5 m |
| Window rows | Eye level: sill 0.9 m, head 1.85 m. Habitat: 0.6 / 2.1 m. Panes on a 1.25 m pitch |
| Hatch | Ø 1.2–1.4 m |
| Porthole | Ø 0.48 m |
| Railing | 1.1 m |
| Pad deck | 4.4 m single, 9 m 2 × 2 |
| Turret, dish, tank, fin bank, globe, mast | Size classes, below |

**Stretchy parts take up the variation:** wall skin, piers, plain edge bands, plinths, relief, armour carving,
fillets, buttresses and arch haunches.

How fixed parts are kept at true size:

- **Anchor scope.** `using(f.Fixed(at))` generalises the scope `Hatch` and `Pad` already open around `metricAnchor`
  (`TileBuilder.cs:229`, `:648`). `CurvedTileMesh.Metric` makes the part isometric at its anchor along the face;
  heights are already in metres. A run of parts across cells (window rows) shares one anchor per frontage, as
  `Forms` already does for Habitat, so rows stay lined up.
- **Count-fit.** Repeated parts (panes, mullions, portholes, ribs, fins, containers, hangar doors) take their count
  from the real length, as `WindowRow` already does for panes. Decks: depth under 4.2 m gets one deck, otherwise
  two.
- **Size classes.** Turret, dish, tank cluster, fin bank, sensor globe and mast come in S/M/L (for example a turret
  on a 2 / 3 / 4 m base). Each cell gets the largest class that fits its size at the anchor, minus a 0.6 m margin.
  Every M turret is identical everywhere; a small cell gets a smaller class, never a shrunken one.

What becomes fixed in each family:

| Family | Fixed parts |
|---|---|
| Hull | EVA hatches (already), small windows, maintenance doors |
| Habitat | Window rows and visors (finish the existing compensation), doors, ledges |
| Industrial | Control tower cab and its windows, gantry section, hatches |
| Power | Fin thickness and pitch (count-fit), fin bank size class |
| Life Support | Tank size class, vault ribs on a fixed pitch |
| Storage | Hangar door sizes (pod and shuttle openings), cargo doors |
| Defense | Turret size class, gun slits, barrel lengths |
| Command | Bridge window rows, globe and dish size class, mast |
| Pad | Deck and hatch (already) |
| Corridor, Conduit | See section 3 |
| All | Sockets and ports on faces a corridor or conduit plugs into (anchored, matching the fixed collar) |

## 3. Direct build path

Used for Wedge and Apex cells, and for Corridor and Conduit cells of any shape.

- **Frame.** `ShapeFrame` is built from the cell's real corners: origin, spine, across and out for a wedge; a 3-fold
  frame for an apex. Parts are emitted through `TileMesh`'s existing matrix stack in that rigid frame, so they are
  true size by construction.
- **Skin.** Roof, slopes and gables are quads and triangles on the real corner positions. A bent neighbour's face
  edge is the straight line between the same two corners, so seams match exactly.
- **Relief boundary.** `ReliefContinues` returns false toward special cells, so the neighbours' relief closes with
  its rim at the shared edge. The special skin carries its own 8 cm edge band.
- **No `Bend`.** `FoundrySettlement.Rebuild` skips `Bend` for these tiles. They still use `TileMesh`, so materials,
  colour coding and the far LOD work unchanged.
- **Connectors.**
  - The centreline is a quadratic curve through the curved centres of the corridor's linked faces.
  - The profile (octagon, ribs, portholes, collar and hatch) is swept along it in metres.
  - The frame at a shared face comes only from that face's 4 corners, so both cells agree.
  - Ribs and portholes are count-fit along the length.
  - The conduit is the same with a fixed pipe diameter.
  - Junctions are fixed-size hubs.
- **Carving on direct tiles** is decided by a spike in Phase 3:
  - Carve in the part's own frame if `TileMesh.Carving` takes a frame cheaply.
  - Otherwise leave slopes plain apart from bold fixed panel parts, which is cheaper and fine at 5 m.

## 4. Ridges, keels and crowns: family kits

Each family gets four parts:

- **Crest:** the roof of a wedge. It joins along the spine with same-family wedge neighbours and caps the ends of a
  run.
- **Slope:** one exposed slope. Used on ridges, and on both sides of a keel.
- **Gable:** an exposed triangular end, about 5.5 m equilateral.
- **Crown:** the apex roof. It links to up to three ridges of the same family, or to connectors.

Keel strips, half-fillets and spikes are shared parts with family colour trim. The keel strip is an armoured edge
with running lights and a hatch every second cell.

**Design intent:** the 12 ridge arcs and 8 crowns become the station's big structure, like the spine lines and
trench of the reference films. Crowns are landmarks that read on the limb from far away. Ridges have a rhythm
along their length, picked by coordinate hash (lit and dark bays, fin heights in a wave, turrets every other cell),
so a long ridge never reads as copies.

| Family | Crest (ridge) | Slope | Gable | Crown (apex) |
|---|---|---|---|---|
| Hull | Armoured keel spine, vertebra ribs on a 1.25 m pitch, running lights | Armour courses falling from the crest in chevrons; maintenance hatch | Triangular armour plate with a round EVA hatch | Three spines meet at a raised hub with a beacon |
| Habitat | A-frame gallery: a glazed ridge cap lit from inside | Eye-level window rows under a visor, count-fit panes | Tall triangular window wall with mullions and a railed balcony | Three-sided glazed lantern with a deck inside and a walkway round it |
| Industrial | Twin crane rails and a travelling gantry crane (one per run, hash-placed) | Conveyor along the eave with chutes | Loading bay door with a jib crane | Tripod derrick: lattice legs to the three crown corners, boom on top |
| Power | A spine of tall radiator fins, alternately tilted, heights in a wave | Radiator panels lying on the slope | Transformer box with warning lights | Triskelion: three radiator wings in a Y round a glowing core spike |
| Life Support | Glazed greenhouse barrel along the crest, fixed-pitch ribs, rounded run ends | Upright tank rows or scrubber vents | Barrel end with a round window | Geodesic glass dome (2-frequency: triangles suit the shape) |
| Storage | Monorail with clamped fixed-size containers, some missing | Racked cargo behind nets | Triangular hangar door with a pod-size opening | Three silos in a triangle under a shared gantry |
| Defense | Battlement rail with point-defence turrets (S/M) on every other cell | Sloped casemates with gun slits | Heavy gun port | Bastion: three turrets at 120° on a stepped triangular base |
| Command | Phased-array spine: fixed sensor panels angled to both sides | Sloped bridge window strip | Antenna mast with a globe | Spire with three dishes on arms at 120° and a top beacon |
| Pad | Docking cradle: arms from both slopes hold a pod on its side, gangway to a crest hatch (the pod appears on runs of 2+ cells, since it is 6 m long) | Service hoses and landing lights | Approach light bar | Mooring mast: a pod docks nose-in on a ring at the top |
| Corridor | Running along the spine, the tube becomes an A-frame glazed gallery profile; any other direction keeps the fixed octagon | n/a (open cell) | Docking collar with hatch | Three-way pressure node, collars at 120° |
| Conduit | Pipe bundle on saddles along the crest, expansion loops rising every few cells | n/a (open cell) | Capped flange | Y-manifold valve tower with wheel valves |

Per-part budget: about 3k triangles near. A special tile stays at or under 8k near and 2.5k far.

## 5. Placement rules

The checks run in `BlockConstruction.TryEdit` and reuse `LastError`, so the Foundry preview already shows the reason.

| Rule | Detail |
|---|---|
| Corridor | Needs ≥ 3.2 m across its cross-section |
| Pad (single) | Needs a ≥ 4.6 m face |
| 2 × 2 landmarks | Only on four Box cells |
| Other families | Never refused: size class S fits the smallest cells, and every family has a kit |

**Preview text** names the shape, size and form, for example "Ridge · 5.3 × 3.4 m · Habitat gallery". A refusal
gives the reason, for example "Pad needs a 4.6 m face; this one is 3.1 m".

## Performance

The curved path already costs ~4× the flat path per tile, and an edit rebuilds a 2-cell halo. So the guardrails are:

- **No per-frame work.** Everything happens at rebuild.
- **No new per-vertex work in `Bend`.** Anchors stay cached per `(anchor, frame)`, as today, with at most ~20 per
  tile.
- **The direct path replaces `Bend`** for special and connector tiles, so it should lower cost.
- **Shape info** is computed once per cell and cached.
- **New parts use existing primitives only** (box, prism, lathe, hexa, annulus, glass): no CSG, booleans or
  subdivision.
- **Phase 0 profiles first.** It splits rebuild time into solve, forms, carving, bend and mesh upload. If the top
  hotspot has a local fix, that lands in Phase 0. Candidates: `Holes()` rebuilds neighbours' forms just to read
  their cut-outs; tuple-keyed dictionaries hash a `Vector4`.

Lab budgets, asserted from Phase 0's numbers:

| Measure | Budget |
|---|---|
| Single edit, p95 over scripted edits | ≤ Phase 0 baseline |
| Full Foundry example rebuild | ≤ baseline + 15% |
| Stage triangles | ≤ baseline + 10% |
| Special tile | ≤ 8k triangles near, ≤ 2.5k far |

## Phase 0 results

Measured 2026-10-27 in the editor, which compiles C# in Debug mode (managed code runs unoptimised).

| | Foundry example | Ridges example |
|---|---|---|
| Cells (all) | box 84, small box 47; wedge: ridge 9, half 6, keel 2, buried 8; 14 open gables | box 126, small box 68; wedge: ridge 22, half 2, keel 3; crown 1; 9 open gables |
| Full rebuild | 1.94 s, 138 tiles (14 ms per tile) | 1.97 s, 222 tiles (8.9 ms per tile) |
| Split | bend 1095, carving and meshes 589, forms and relief 236, objects 11, solve 5 ms | bend 1133, carving and meshes 427, forms and relief 389, objects 15, solve 7 ms |
| Single edit (20 scripted) | p50 0.63 s, p95 0.97–1.15 s (runs vary ~15%); 51.5 tiles rebuilt per edit | |

- **Hotspot fixed:** `Bend` took 63% of a rebuild. It now checks the last cell and frame before hashing, and a far
  mesh copies the vertices it shares with its already-bent near mesh. Bending is 28% faster and the output is
  bit-identical (checksum over all 714k Foundry vertices, normals and tangents).
- **Still the largest cost:** `Bend` at ~56% of a rebuild, about 1.5 µs per vertex in Debug mode. The next
  performance step would be moving its per-vertex loop into a Burst job, since `DeathStar.Runtime` already
  references Burst. Not scheduled yet; budgets from Phase 1 on are asserted against the numbers above.
- **Validation:** the classifier matches the analytic rule (wedge where two coordinates tie for the largest
  magnitude, apex where three do) for every cell of a radius-3 ball grown from the seed.

## Phases

| Phase | Size | Work | Exit |
|---|---|---|---|
| **0. Baseline and shapes** (done) | S | `CellShape`/`ShapeInfo` cached in `BlockConstruction`; exposure helper. The C key cycles Off → Family → Shape (Box grey, Wedge orange, Apex magenta, too-small red). `SettlementExamples.Ridges()`: an octant of a radius-6 skin (three ridges and a crown) with a keel run underneath, a half-covered roof and gables, families in 2-cell runs (three ridges of 8 cells hold 12 runs). Lab: shape counts, timing splits, per-edit p50/p95 over 20 scripted edits. Fix the top hotspot if it is local | Numbers in `report.md`; no visual change; all existing checks pass |
| **1. Fixed-size parts** | M | Anchor scope, the per-family conversion list, count-fit, size classes, margins, placement rules and preview text | Fixed-part scale check passes; `foundry-scale.png` shows one family on a face-centre cell beside the same family near a ridge; budgets hold |
| **2. Direct path and connectors** | M | `ShapeFrame`, direct emission without `Bend`, relief boundary, corridor and conduit sweeps, fixed sockets and junctions | Shared-edge check: direct/bent seams match within 1 mm; corridor inner width ±2% everywhere; per-edit time ≤ baseline |
| **3. Generic special skins** | M | The Hull kit (crest, slope, gable, crown), shared keel strip, half-fillet and spike. Other families use the Hull kit with family trim until Phase 4. Carving spike decided | `ridges.png`, `ridges-keel.png` and a crown close-up from the Ridges example; no special cell goes through the box path |
| **4. Family kits** | L, 3 batches | A: Habitat, Defense, Command, Corridor/Conduit forms (most visible, human scale). B: Power, Life Support, Storage, Pad (cradle and pod). C: Industrial, Hull polish | Each batch adds `ridges-sheet.png` (each family's ridge run) and `crowns-sheet.png` (each family's crown on the same apex, rebuilding one tile per shot); triangle budget; scale check |

Phase 3 makes every special cell look intentional, so Phase 4 can stop after any batch.

## Lab additions

- **Shape counts and timing splits** in `report.md`: per-edit p50 and p95, plus the budget asserts above.
- **Fixed-part scale check.** Fixed parts are tagged with `m.FixedPart(name, nominal)`. After building every Foundry
  tile, each tagged part's curved size must be within ±5% of nominal: corridor inner width, hatch diameter, pane
  height, turret base.
- **Shared-edge check** between direct and bent tiles.
- **New pictures:** `foundry-shapes`, `ridges`, `ridges-shapes` and `ridges-keel` (Phase 0); `foundry-scale`,
  `ridges-sheet` and `crowns-sheet` later.

## Design doc changes

Agreed in chat and applied on 2026-10-27:

- **Scale and human reference:** replace "a later implementation needs to preserve … proportions" with the
  fixed-size parts list and the size classes.
- **Foundry neighbourhood solver**, last bullet: "locally metric-correct, not rigid flat inserts" becomes "fixed
  parts are true size at their anchor; stretchy parts follow the cell".
- **New section "Ridges, keels and crowns":** shape classes, exposures and the kit table.
- **Angled and round forms:** add the triangular forms (A-frames, gables, tripods, geodesic dome, triskelion).

## Not doing

| Idea | Why not |
|---|---|
| Smooth analytic interior map (`BlockGuide.Map` inside cells instead of trilinear) | Changes every tile, still creases on the diagonals, and only valid for guide-made corners. Revisit only if facets still show after Phase 3 |
| Armour plates in per-face rectified coordinates | High cost and breaks station-space continuity; plates are stretchy parts |
| Whole-form rigid cores (polar decomposition per tile) | Per-part anchors do the same job, already work and cost less |
| A different cube-to-sphere map or isotropic `FitGuide` | Changes the grid. Stretched guides (Slab, Settlement 8×6×8) will keep having more special cells |
| Per-family keel and spike specials beyond the kit's slopes | Rare and seen from inside; the shared keel strip is enough |
| Any global solver, relaxation or WFC | Not needed; local rules as today |

## Risks

| Risk | Mitigation |
|---|---|
| Isometric parts poke past a compressed cell edge | Size classes plus a 0.6 m margin; the scale check reports overflows |
| Direct and bent seams crack | Both sides use the same corners; shared-edge check in the lab |
| Borderline folds on stretched guides | Single 40° threshold; borderline cells stay Box (today's look); counts in the report |
| Carving on direct tiles costs too much | Spike decides; plain slopes with bold fixed panels as fallback |
| Phase 4 scope creep | Fixed kit of four parts per family, triangle budget per part, batches that can stop |
