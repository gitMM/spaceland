# Death Star / Orbital Foundry

Station lighting combines short-range screen-space GI with cached world-space
voxel cone tracing (40 m reach, two diffuse bounces). Controls are under
**Death Star → Lighting settings**. See [voxel GI](doc/voxel-gi.md) for the scale,
update model, limits and red-habitat checks (`tools/tile-lab.sh voxelgi`).

## Block Foundry — original sphere mapping

Open **Death Star → Open Block Foundry**, then press Play. There is one block type,
using the original fixed cube-to-sphere mapping. Build around the seed to form a
sphere. The Square selector, Square extrusion, Mixed example and type colors have
been removed.

- Click a face to add; Shift + click removes. The last seed cannot be removed.
- New corners use `BlockGuide.Map` directly. The guide does not recenter or resize
  during construction; existing shared corners remain fixed.
- New blocks appear at once and the view glides onto them. No shape recognition,
  global refit, delayed settling or surrounding preview mesh is used.
- Right drag orbits, middle drag pans, scroll zooms, Home frames and Tab hides controls.
  The requested 50× scroll sensitivity remains in place.
- Slab, Wedge, Orbital shell and Sphere examples all use sphere mapping.
- Ctrl+Z / Ctrl+Y restore exact geometry and the guide, rebuilding only around the
  edited cell as the edit did; New build is undoable.
- Ctrl+S saves `DeathStarRuns/BlockFoundry.json`. Save before stopping Play: there
  is no autosave. Version 5 stores cells, corners and the guide. Versions 1–4 remain
  readable with their stored geometry preserved. Old type metadata is accepted only
  for compatibility; all new placements follow the sphere grid.

`BlockMesh` emits exposed bilinear faces into 8³ chunks. Shared vertices keep joins
closed. Local Jacobian checks
reject folded/collapsed cells; they are not a nonlocal collision solver. The prototype
supports 32,768 blocks, with logical coordinates -64 through 63.

### Validation

**Death Star → Validate Block Foundry** checks the original mapping, unchanged old
corners during placement/removal, shared faces, winding, v5 round trips and legacy
save migration. It also grows a 125-cell sphere from the seed.

Run live checks only in a fresh Play session; they replace construction:

```powershell
unity command run_script --project-path C:\Transhuman\spaceland --file tools/death-star/VerifyBlockFoundry.cs --entry BlockFoundryPlayCheck.Run
unity command run_script --project-path C:\Transhuman\spaceland --file tools/death-star/VerifySphereSettling.cs --entry SphereSettlingPlayCheck.Run
```

## Original procedural planet

A self-contained procedural mechanical planet experiment for Unity 6 / URP.

Open `Assets/DeathStar/Scenes/DeathStar.unity` and press Play, or use **Death Star → Open Experiment**.
The scene, material, post-processing profile, runtime assembly, editor assembly, shaders and generator all live in this folder. There are no dependencies on Giant, Megacity, Reveal, Brackenford or Edsger, and no changes to shared render-pipeline assets or build-scene settings.

## Controls

| Input | Action |
|---|---|
| Left drag | Paint the selected treatment |
| Right or middle drag | Orbit |
| Scroll | Smooth zoom |
| Shift + scroll, or brackets | Brush size |
| 1–6 | Armor, raise, lower, trench, machinery, erase edits |
| Ctrl+Z / Ctrl+Y | Undo / redo stroke (32-stroke history) |
| R | Next seed, keeping painted overrides |
| Home | Return to orbital view |
| Tab | Hide/show controls |

Raise and lower apply once per cell per stroke. Erase restores procedural state. Reset Paint removes all overrides. Seed input accepts signed integers. Paint is session-only: stopping Play mode discards edits. Resolution/radius changes take effect on the next enable/Play session; 128 cells per face is the default, 256 is supported.

## Construction and rendering

- Six square face grids: 98,304 logical cells at the default resolution.
- Exact integer shared vertices, reciprocal face-edge transport and three-cell cube corners.
- Burst jobs generate deterministic 32-byte semantic cell plans from a seed and painted overrides.
- World-space district fields, a continuous equatorial trench, a secondary meridian trench, a recessed sensor array, joined raised decks, exposed walls and rigid equipment.
- Every edit regenerates the entire planet. Repeated samples are coalesced; a completed generation replaces the previous one atomically. Continuous strokes publish intermediate complete generations.
- Compute classifies neighbor heights and emits into bounded reservations of at most 12 primitives per cell. Only the higher cell emits a shared height-transition wall. Equal-height neighbors have no internal wall.
- GPU primitive records are 32 bytes. Surface panels follow the sphere; equipment uses tangent frames.
- GPU frustum and conservative solid-core occlusion checks, plus three projected-size detail tiers. Three indirect draw groups: skin, walls, equipment. No GameObject, MeshRenderer or collider per cell.
- Painting ray-marches the generated stepped skin; equipment is decorative rather than a picking target.
- Lighting uses analytical directional illumination, planet horizon shading and material contact darkening. The first prototype does not render building shadow maps, interiors, volumetric excavation, or smoothly cross-faded detail transitions.
- There is no per-frame geometry generation. Visibility is updated each frame; GPU diagnostic readbacks are asynchronous and limited to once per second.

The fixed reservations favor a simple bounded whole-generation pipeline over compacting every primitive. GPU buffers use about **46.1 MiB at 128**, or **184.5 MiB at 256**, including visibility lists and topology. A replacement generation temporarily increases memory; old generations are retired after queued draws and a GPU fence. These figures exclude Unity, render targets and driver allocations.

## Validation

**Death Star → Validate Topology and GPU Grammar** checks shader errors, reciprocal neighbors, exact shared edge vertices and winding, cube-corner valence, picking round trips, seeded generation, radial limits, authored seam changes, GPU ownership and primitive-count limits, and missing/duplicate exposed walls. It checks 400,536 directed edges across resolutions 4, 17 and 128. Its synchronous GPU readback is development-only.

For live checks, start a fresh Death Star Play session and run:

```powershell
unity command run_script --project-path C:\Transhuman\spaceland --file tools/death-star/VerifyRuntime.cs --entry DeathStarRuntimeCheck.Run
unity command run_script --project-path C:\Transhuman\spaceland --file tools/death-star/VerifyScale.cs --entry DeathStarScaleCheck.Run
```

These checks intentionally paint/reset the running experiment and restore seed 1977 and resolution 128. Run them only in a fresh test session. They cover painting across a face seam, complete-generation publication, regeneration during a continuous stroke, undo/redo, reset, seed changes, skin picking, repeated rebuilds and the 256-resolution path. Reports are written to `DeathStarRuns/` in the project root.

Measured on 2026-10-26 in the Editor on an NVIDIA GeForce RTX 4080 Laptop GPU: the initial warm runs were approximately 3–4 ms at 128 and 3.5–4.8 ms at 256. These are wall latencies from scheduling CPU jobs through submitting the replacement GPU generation, **not GPU execution timings or standalone frame-rate guarantees**. First use can be much slower because of Burst/shader warmup. The HUD's frame time is the running Editor frame interval.

## Files

- `Runtime/CubeSphere.cs`: topology, face mapping and picking coordinates.
- `Runtime/PlanetPlan.cs`: authored state and Burst generation.
- `Runtime/PlanetRenderer.cs`: GPU ownership, generation, visibility and drawing.
- `Runtime/DeathStarExperiment.cs`: regeneration scheduling, interaction, camera and HUD.
- `Shaders/PlanetGeometry.compute`: bounded geometry emission and GPU visibility.
- `Shaders/PlanetSurface.shader`: geometry reconstruction and industrial surface appearance.
- `Editor/DeathStarMenu.cs`: isolated scene/asset creation and opening.
- `Editor/DeathStarValidation.cs`: topology, plan and GPU grammar checks.


## Settlement tiles — phase 1: massing

**Block Foundry now grows settlement architecture.** Choose a family in the construction panel and click a face.
The neighbourhood solver chooses orientation, joins compatible forms, and bends the result onto the shared sphere
grid. The **Settlement** example demonstrates pads, a hangar, vaults, turret towers, bridges, a trench and an arch.
Remove cells or undo to see assemblies split and reform. Saves retain family choices; old saves load as Hull.
Press **C** or click **Debug view** to cycle Off → Family → Shape. Family tints the architecture by family; swatches
beside the family buttons are the legend (Hull stays grey) and hovering shows the family, cell, axis and main face.
Shape tints each cell by the shape the sphere map gives it: grey box, red box with an edge under 3.2 m, orange
wedge (two faces folded flat into one roof, on the cube's edge diagonals), magenta apex (three faces, on the corner
diagonals). Hovering shows the shape, how it meets open space (ridge, half, keel, crown, spike, gable), the ridge's
axis and the edge lengths in metres. Both views apply to both LODs, survive placement and undo, and do not change
the gallery's colour setting. The **Ridges** example is a cap round one cube corner: three ridges meet at a crown,
with every family on a ridge, a keel inside, a half-covered roof and gables. The **Station** example is a whole
two-layer hollow sphere with every family on every shape:
- all 12 ridges, each in one family;
- 8 crowns in the 8 non-Hull solid families, each met by its own family's ridge, plus a sunken Hull crown in a pit;
- 2 × 2 landmarks at five face centres, with rows, towers, pits, pads, a corridor out into space, a conduit on
  saddles and a truss tower with a cantilevered boom;
- a half-covered roof, a gap with two gables, and a keel and a spike inside.

It takes several seconds to build. `BlockConstruction.Shape` and
`Exposure` classify cells from their eight corners; see `doc/curved-grid-tiles-plan.md`. Wedge and apex cells keep
the skin and relief on their open faces but build no family forms. Instead they build their shape's kit in curved
space from their real corners (`TileBuilder.Shapes.cs`, `TileMesh.BeginWorld`). The Hull kit: an armoured crest
spine with ribs and running lights along each ridge; a hub and beacon on a crown, with an arm along each seam into
its ridge's crest; an armour plate and hatch on open gables; a lit keel strip; chevron courses on open slopes. Habitat,
Defense and Command have kits of their own (`TileBuilder.Kits.cs`): a glazed A-frame gallery, a glazed lantern, a
glazed gable and window rows on slopes; a battlement rail with a turret on every other cell, a three-turret bastion, a
gun port and casemates; a phased-array spine, a spire with three dishes, an antenna mast and a bridge window strip.
Power, Life Support, Storage and Landing Pad follow in `TileBuilder.KitsB.cs`: a wave of tilted radiator fins, a
three-winged radiator round a glowing core, a transformer, radiator panels; a glazed greenhouse barrel, a faceted dome
on a windowed drum, a round window, squat tanks; a monorail of containers, three silos under a gantry, a hangar door,
racked crates; a docking cradle holding a pod over every pair of cells, a mooring mast with a pod docked nose-in,
an approach light bar, hoses and lights. Industrial (`TileBuilder.KitsC.cs`): twin crane rails with a gantry crane on
every other cell, a tripod derrick with a boom, a loading bay with a jib crane, a conveyor with chutes. Hull slopes
carry chevron armour courses and a hatch, and keel strips a hatch on every second cell. Crests join only within a
family. Corridors and conduits keep their tubes. Parts crossing into the next cell end on a section taken from the shared face's four corners, so ridges run
on without seams.

**Truss** (`TileBuilder.Truss.cs`) is an open family, like Corridor and Conduit: a space frame of slender members in
a clear hierarchy, after the station-truss reference: box chords 0.46 m on the trusses' outline (with splice bands),
0.22 m on seams across a face and 0.2 m inside, cast nodes half as big again as the chords they join, and X braces
of flat bars (0.25 m wide in the face on a lone cell, about a quarter as deep) with gusset plates. Trusses side by side make one frame: each chord and node is built once, by the first truss
round it, and pushed in from its lattice line until it is flush with the trusses' outer surface, so nothing reaches
into a neighbour. Faces onto open space are braced; faces between trusses stay open, so a run is a hollow girder; a
solid face beside a truss becomes a bearing plate of carved armour with no relief or family form
(`TileStructure.Covered`: a truss covers what it stands on, for main faces, joined rows and a wedge's exposure).
**Formations:** a lone braced face carries an X with a cross plate; braced faces filling an aligned 2 × 2 block (on
even station cells, like the 2 × 2 landmarks) carry one big X of 0.5 m bars across the 10 m panel; an aligned 4 × 4 block
(station cells in multiples of four) an X of 0.7 m bars across 20 m with a lighter diamond inscribed in it, each cell carrying
one diagonal of the pattern (`TileBuilder.FaceBlock`). Edits near a truss rebuild four cells round them
(`SettlementSolver.Dirty`) so a block's pattern stays whole. The frame is built from the cells' real corners at true
size (members thin in cells under 4.2 m), so a wedge becomes a triangular girder with a king post and an apex an
inverted pyramid. Its kit: an X-laced triangular lattice girder with cast nodes, a walkway and saddles along the
crest; an end flange on a gable, and a tapered 7.5 m X-laced jib where the next two cells along the ridge are clear; a
keel chord with lights; on a crown a 13 m lattice tower (three legs from the girders, X-braced, a platform with a
blockhouse, a mast and a beacon). Gussets, bands and cross plates are left out of the coarse far level.
Corridors don't link into trusses. The **Truss** example button loads `SettlementExamples.Truss`.
`Runtime/Tiles/SettlementSolver.cs` chooses orientations; `FoundrySettlement.cs` rebuilds dependent tiles;
`CurvedTileMesh.cs` maps their meshes into the eight-corner cells and preserves relief depth and local feature scale.

**True size on the curved grid.** Cells measure 2.3–5 m across, so parts people and craft use don't stretch with
them. Free-standing parts (turrets, the Command top, Industrial control towers, Life Support tanks, hatches, pads and
the 2 × 2 landmarks) are placed rigidly at true size (`TileMesh.BeginFixed`, `CurvedTileMesh.Rigid`); only their
lowest 0.3 m drops onto the curved surface. Equipment comes in three sizes, L, M and S (1, 0.78 and 0.6 of the
authored size): each part takes the largest that fits its face (`Face.Equip`). Parts set into the wall (window rows,
Hull windows) keep their place on the wall but take true dimensions from the local stretch. Repeated parts change in
number, not size: window panes, Power fins, vault rings and Storage ribs on a 0.83–1.25 m pitch, and Habitat walls
under 4.2 m get one deck of windows instead of two. Corridors and conduits are tubes (`TileMesh.BeginTube`): the
cross-section is rigid and true size, only the length follows the cells, and at a shared face both tiles take the
cross-section frame from that face's four corners, so runs meet exactly. Ribs, porthole rims and collars keep their
length along the run; rib count follows the run's length. Collars and the sockets they plug into share one radius
(2.15 m, down to 1.6 m on narrow faces); ports, junction hubs and sockets are rigid. Conduit elbows stay in station
space, with a flange at each end over the step to the true-size pipes. The flat gallery is unchanged. Every family can
be placed in every cell, out to the build limit. In the narrowest cells (2.4 m, round the cube's corners) a corridor's
2.8 m tube fills its slot, overhanging into its neighbours by at most 0.2 m, and its collars by at most 0.4 m. A landing
pad's deck takes the largest size class that overhangs the face by at most 0.4 m a side: L (4.4 m) from 3.6 m faces, M
from 2.6 m, S below that. A 2 × 2 pad takes its class from the block's width. The airlock hatch stays true size.
Hovering names the family, the cell's shape and the face size. The geometry check rejects folded and collapsed cells,
not flat ones: a ridge's roof flattens as the shell grows, so the check is scale-free (Jacobian over its three edge
lengths ≥ 0.001; the flattest cell, at shell 63, measures 0.0033). Before, it turned away the cube's corners from
shell 13 and its ridges a cell further each shell after that.

**Death Star → Open Tile Gallery** opens `Scenes/TileGallery.unity`: a floating station fragment (80 × 30 × 60 m) and
twelve specimens, one per family of `doc/modular-settlement-tile-design.md`, in space with no floor. The stage shows
silhouettes, armour, windows, equipment and how cubes combine. The stage is
generated on enable, in edit mode too; nothing generated is saved with the scene.

- **No up.** Every exposed face follows the same rules; a tile's own axis and side (any of the 24 cube
  orientations) orient its forms. The fragment has turrets and a tower hanging underneath and a turret pointing
  sideways out of a cantilever; each specimen sits in a different orientation.
- **No cube seams.** Faces against solid neighbours are not built, and `GreebleField` is one relief pattern (0.1–0.8 m)
  defined in station space on every axis plane. Its top level is a Pythagorean tiling of 32 m districts and 12 m
  squares, each row stepping against the next and offset off the 5 m lattice; districts are cut unevenly into two to
  four plates, and rimmed plates carry rims on some edges only, so no large-scale line runs on for more than 44 m and
  nothing reads as a grid from far away. Leaves use the carved armour's plate shapes at relief scale too: corners cut
  away at 45 degrees, diagonal and jogging splits into two parts a level apart. Each face cuts its window out of it;
  relief closes only at convex edges and where a neighbour's family form takes over.
- **Families by mass:** Hull is relief, with small windows and round EVA hatches on some faces; Habitat two rows of
  tall windows under angled visors that join along rows; Industrial a terraced pit with gantry and a control tower; Power
  thick fin banks; Life Support ringed half-cylinder vaults; Storage a deep bay between sloped lips that merges into a
  hangar slot; Defense sloped glacis and a long-barrelled turret; Command a stepped tower with a sloped bridge window,
  globes and mast; Corridor an open ribbed tube with portholes and a docking collar with a hatch where it ends in
  space; Landing Pad a round deck on a pedestal and angled struts with an airlock hatch in its centre (four pads on
  even station cells make one 9 m pad); Conduit an open cell carrying a bundle of big pipes on saddles that links to
  its neighbours, turns on elbows and plugs into the tiles it reaches. A conduit group that would plug in nowhere (a
  closed loop, or runs ending in space) is tethered: its run ends turn into the solid tiles beside them and its elbows
  branch into them, while straight runs stay clean; Truss an open space frame of slender box chords, cast nodes and
  X-braced faces, big X patterns on 2 × 2 and 4 × 4 blocks, standing on bearing plates (see above). Corridor, Conduit and Truss are not solid.
- **2 x 2 landmarks.** Four tiles of one family on even station cells, all showing the same main face, merge into
  one landmark: Command a 9 m dish, Power a reactor dome between radiator wings, Life Support a sphere tank, Storage
  one big hangar mouth, Landing Pad one 9 m pad. On their own, Life Support tiles show grouped upright tanks and tank
  tops (vaults only in rows), and Defense is a tower whose armour courses step in towards its turret.
- **Armour carving.** Flat armour faces of family forms (`TilePattern.Armour`) are cut into real plate geometry
  (`TileMesh.Carving.cs`), raised 4-13 cm with bevelled tops and seams down to the face. Courses stack along the tile's
  axis (its side on the faces across the axis), so they wrap round a tower and every side matches. Station space is
  split into 8 m zones: busy ones have 0.62 m courses of 0.31-1.24 m plates, calm ones (40%) 1.24 m courses of mostly
  long, whole plates up to 2.48 m. Some plates have a corner cut away at 45 degrees, some are split in two by a wide,
  deep diagonal cut or one that jogs diagonally between two levels (halves 5 and 12 cm proud), some are sunk panels in
  a raised frame, and whole ones can be hatches (hinged doors with a handle, or round doors with a hub). An 8 cm plain
  band runs along each face's edges except tile seams, so corners read solid and edges leave no slivers. Laid out in
  station space so it runs on across tiles; round forms and the walls under the relief stay smooth. The relief's
  curves are wide arcs with ribs, blocks, drums and buttresses, some running into round wells, set in a low bed where
  the plates under them flatten.
- **Triangle budget.** Faces lying on a tile's own wall and facing in are not built, and carving waits until the tile
  is done so plates under other boxes and hexahedra are left out; so are faces that look at another solid within a
  metre over most of their area (a radiator fin's side), which are only seen edge-on. Each tile gets a LOD group:
  from about 110 m (a 5 m cube under ~45 px at 1080p) it swaps to a far mesh with the armour plain and its family
  forms, wedge and apex kits and truss frames built coarse (`TileMesh.Detail`: a third of the sides on round parts,
  half the steps on domes, no chamfers). Every tile stays within 8k triangles near and 2.5k far
  (`TileBuilder.NearBudget`, `FarBudget`): a tile the carving would take past 8k switches armour faces to calm plates,
  those that save most first, until it fits; a far mesh still over 2.5k is built coarser again (a fifth of the sides).
  The showcase is ~740k triangles near and ~315k far.
- **Connections and slopes.** A corridor arriving at a face plugs into an octagonal socket on it, and a conduit into
  a manifold block; either takes that face over. Hull walls rising from a surface get a sloped fillet along the foot,
  continued along neighbours, so steps read as battered walls.
- **Craft for scale** (`Runtime/Tiles/Craft.cs`, not tiles): a 6 m pod and an 11 m shuttle, each with a belly hatch
  ring that sits over a pad's airlock. The showcase docks pods on a pad, on the underside pad and nose-in at a hangar,
  and the shuttle on the 2 x 2 pad.
- **Decks and windows.** Inside, a tile's axis is its up: two 2.5 m decks, so window rows sit at eye height on each
  deck, on a 1.25 m station pitch, and line up across neighbours. Every pane shows a lit room (interior-mapped glass).
- **Breaking the blocks:** the relief adds ramps, drums, domes, rings and big arcs several tiles across; towers and
  turret mounts get angled buttress fins where they stand on a surface.

In Play: 1–9, -, = and Backspace specimens, 0 the fragment, O the whole station, T down the trench, U underneath. Right drag tumbles
(about the camera's own axes), Q/E roll, middle drag pans, scroll zooms. C toggles the family colour code (on by default; Hull stays grey, the panel shows the legend), L labels,
G greyscale, Tab the panel. Colours: Habitat amber, Industrial orange, Power red, Life Support green, Storage teal,
Defense violet, Command blue, Corridor pink, Truss yellow (`TileLook.FamilyColor`, carried per vertex in uv3).

- `Runtime/Tiles/TileStructure.cs`: cells, placements (axis and side), and the showcase layout.
- `Runtime/Tiles/TileBuilder.cs`: per-tile faces and family forms. `Runtime/Tiles/GreebleField.cs`: the relief.
- `Runtime/Tiles/TileMesh.cs`: primitives (chamfered boxes, walls with openings, lathes, prisms).
- `Runtime/Tiles/TileLook.cs`: materials, sun, planetshine, ambient, reflection; the menu rewrites the assets from it.
- `Shaders/TileSpace.shader`: stars and a planet lit by the same sun. `TileSurface`/`TileGlass` are reused.

### Tile lab

Judge the tiles headless instead of playing (edit mode only; it refreshes and compiles first):

```bash
tools/tile-lab.sh report
tools/tile-lab.sh validate
tools/tile-lab.sh foundry
tools/tile-lab.sh limit
tools/tile-lab.sh truss
tools/tile-lab.sh budget
tools/tile-lab.sh snap view=Trench
tools/tile-lab.sh snap family=Defense back=1
```

It writes `DeathStarRuns/Tiles/`: `report.md` (stage size, triangles near and far and per view at the LOD each tile
picks, triangles and size per specimen, checks) and pictures:
`station`, `fragment` (construction distance), `trench`, `under`, `surface`, `end`, `dish`, `hangars`, `craft`, `pod`, `seams`
(a wide area of plain relief), `-coded` versions of station, fragment, trench and surface, and 3×3 sheets `specimens.png`,
`specimens-coded.png` and `specimens-back.png`, rendered at 2× and averaged down.
The report also includes `foundry.png`, `foundry-coded.png`, `foundry-trench.png`, `foundry-under.png` and
`foundry-back.png`, plus `foundry-pad-complete.png` / `foundry-pad-incomplete.png`, using automatically solved
family choices on curved cells. `validate` exercises the real
Foundry placement/undo/picking path and geometric invariants without entering Play mode; `foundry` runs the
settlement checks and renders every curved example (not the gallery stage); `ridges`, `limit`, `station` or `truss`
renders that example alone. Each example is built once and every check (fixed-part sizes, tube widths and joints,
kits, the triangle budget) reads that build. `validate` and `budget` skip the shader warmup.
`foundry-coded.png` and `foundry-coded-far.png` exercise the Foundry's own debug switch, with image checks that
the colours actually change the render on both detail levels. `foundry-shapes.png`, `ridges.png`,
`ridges-shapes.png` and `ridges-keel.png` show the cell shapes; `foundry-scale.png` shows Defense and Command on a
cube-face-centre cell and beside a ridge. The report's "Foundry: cell shapes and rebuild time" section counts shapes
and exposures, splits a full rebuild into solve, forms, carving and meshes, bend and scene objects, times 20 scripted
edits (p50/p95), and lists how every fixed-size part came out (true size over authored size, by part and size
class). Checks hold those parts within 5% and the rebuild, edit and triangle counts within budget; corridor and
conduit cross-sections must come out within 2% of true size at every tube end, and tubes meeting across a face within
1 mm (`ridges-connectors.png` shows a corridor and a conduit run along a ridge); every open wedge and apex cell must
build its kit and no box forms (`ridges-crown.png` shows the crown, `ridges-sheet.png` every ridge run close up,
`crowns-sheet.png` the crown in each family with a kit, `crowns-far.png` the same at the far level, `ridges-lod.png`
near and far side by side). Every tile of every example and of the gallery, and the crown in every family, must stay
within 8k triangles near and 2.5k far; the report lists the most of each family and shape, and the tiles that needed
calm plates or the coarser far level, shown close up in `budget-capped.png` (gallery) and `station-capped.png`
(`tools/tile-lab.sh budget` runs just the budget). `station.png`, `station-coded.png`, `station-shapes.png`,
`station-back.png` and `station-inside.png` show the Station example; the validation checks that it holds every
shape and exposure, every family on a ridge and every solid family on a crown, and that every family can be put
back into every surface cell of a ball (boxes, small boxes, wedges, apexes) and the corners and ridges stay valid out
to shell 63. The Limit example is a cap round a cube corner at shell 63, where cells are narrowest and ridges flattest:
`limit.png` along its surface, `limit-plan-coded.png` / `limit-plan-shapes.png` from above (a corridor beside the x
ridge, single pads and a 2 × 2 pad on small boxes), `limit-narrow.png` down the corridor's 2.4 m slot; its checks
cover fixed-part sizes, tube widths and joints, kits and the pads' smaller classes. `tools/tile-lab.sh limit` runs just
that example. The Truss example (`tools/tile-lab.sh truss` runs just it): `truss.png` and `truss-coded.png` over the
cap, `truss-crown.png` the lattice tower, `truss-ridge.png` the girder and jib, `truss-boom.png` a tower two cells
square with 2 × 2 stages and a boom of the same section, `truss-4x4.png` a 4 × 4 frame on the surface (big X round a
diamond), `truss-details.png` the pit, the bridge, the frame's edge against a Habitat wall and the jib; its checks: every wedge and apex
builds its kit, every truss tile its frame and no fixed-size parts, at most 12k triangles a truss tile. The script submits the lab as a
detached editor job (`unity job wait`), because a direct call is aborted after 30 s on the editor's main thread.

### Unified station lighting

**Death Star → Lighting settings** opens the dedicated **Station Lighting** window,
editing `Resources/StationLighting/MainLighting.asset`. Bounce distance and quality
controls are at the top; the same controls also appear in the asset's Inspector.
Foundry, Tile Gallery and Tile Lab share its sun, planetshine, ambient, reflection,
emission, shadow softness, screen-space bounce/AO and image controls. Changes apply
in Edit/Play mode; they do not regenerate construction. The sky directions and
synthetic reflection environment follow the same light settings.

For broader SSILVB bounce, the shared Inspector exposes **Bounce Distance (m)**
(0.1–30), **Sampling Directions** (2–12), **Samples Per Side** (4–32), and
**Assumed Surface Thickness (m)** (0.01–3). **AO Distance (m)** (0.05–5) remains
independent. A starting experiment is 5 m bounce, 0.75 m AO, 6 directions and 24
samples per side; leave thickness unchanged initially. Use **Lighting Debug View →
Screen Space Gi Only** to isolate bounce. Larger sampling counts cost more GPU
time, and longer reach still cannot recover hidden/off-screen sources. Existing
saved values are unchanged; these controls update while playing.

`StationPipeline` and `StationRenderer` are DeathStar-owned copies of the PC setup.
The sun uses a 4096 shadow atlas with four cascades at 2.5%, 10% and 30% of
the 600 m range, concentrating detail near construction. The wider shadow filter
corrects each tap for the receiving plane and the hardware PCF footprint, avoiding
texel-grid self-shadowing on sloping panels. Blocker depth is compared at actual
texel centres so the softness transition stays stable within a texel.
The renderer uses Button-derived SSILVB in place of Unity SSAO, with a separate
AO-only path when bounce is disabled. Tile surface source shading excludes debug
overlays, specular and emission; opaque window surfaces block source visibility.
A dedicated source depth target handles Forward+ independently of its partial
prepass. The older indirect-drawn planet shader is outside the settlement-material
lighting path.

Unity's SSAO feature and its subassets are removed from both the station renderer
and the PC template renderer. Reinstalling unified lighting also removes any
copied SSAO feature. The shared rig applies lights, ambient, sky, reflections and
post-processing together, including volumes added after the rig. The Inspector's
Occlusion controls belong to the station's SSILVB feature.
`tools/death-star/VerifyUnifiedLighting.cs` checks this shared control path and
renderer setup without rendering a large scene.

The experimental world-space GI, physical window/hangar spill and
associated diagnostics have been removed. The installed lighting is the shared
sun/planet rig, ambient and reflections, corrected shadows, screen-space bounce/AO,
and the original emissive windows with bloom.
**Red Habitat paint** is enabled again, with its color controlled by **Habitat Tint**.
This changes diffuse reflectance in both visible shading and SSILVB source lighting,
so the color can participate in screen-space bounce. It does not restore the removed solver.

Tile surface base reflectance is stored as a **linear Vector shader property**.
Do not change it to a Color property without converting the authored material data:
Unity's sRGB decoding would turn 0.36 reflectance into approximately 0.106, weakening
both the emitting and receiving sides of diffuse bounce. `tools/death-star/VerifyScreenBounce.cs`
(entry `VerifyScreenBounce.Run`) renders a small fixed-camera tile scene, compares
bounce on/off, and checks visible red bounce on neutral surfaces. Its captures go
to `DeathStarRuns/BounceRepair`; it does not run the full station stress workload.

Use the profile's screen-bounce-only, AO-only and diffuse-source diagnostic views
to inspect the contributions. Post-processing still applies in these views.
`tools/death-star/VerifyLighting.cs` (entry `LightingCompare.Run`) writes the
existing lighting comparisons to `DeathStarRuns/Lighting`.
