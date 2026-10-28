# Borrowing Button's lighting for Spaceland

Study and implementation plan — 28 October 2026.

Implementation status: unified station settings, corrected Button shadow filtering and SSILVB screen-space bounce/AO are retained. The experimental world-space GI, physical opening spill, test tint and their diagnostics were removed at the user's request. The remaining plan below is design history, not a description of installed world-space lighting.

## Recommendation

Borrow Button's contact-preserving soft shadows, SSILVB ambient occlusion, and
surface-attached indirect lighting. Adapt the latter to Spaceland's curved,
editable architecture and add a bounded update scheduler before enabling it on a
whole station. Keep screen-space GI optional until its contribution can be
separated from the world-space bounce.

The intended image is pale armour under a strong sun, softly illuminated shadow
faces, deep enclosed cavities, and warm light around inhabited openings. Button's
useful quality is the way light joins nearby surfaces while contacts remain dark.
Spaceland should retain its black sky, metallic reflections, and stronger exterior
light contrast.

## Evidence and scope

Inspected Button's current working files in `C:/Transhuman/Button`, its saved
`MainLighting.asset`, renderer and post-processing assets, shader implementations,
GI tests, and recorded comparison images. Both projects use Unity **6000.3.24f1**
and URP **17.3.0**, so the rendering APIs are closely aligned.

The live Button Editor first reported compiling, then an unnamed Play scene with
no active `ButtonLightingController` settings. Consequently, the saved profile is
the configuration reference, not a claim about the temporary scene currently on
screen. The inspected comparison images are older captures from 24 October; they
illustrate the earlier cascade/screen-space implementation, not a verified render
of today's surface-attached solver. No fresh performance measurements were taken.

Relevant reference images:

- `C:/Transhuman/Button/Artifacts/ScreenGI/SSILVB/cleanup-combined.png`
- `C:/Transhuman/Button/Artifacts/ScreenGI/DebugViews/cascades-only.png`

Button's README is partly behind its implementation: it describes cascades as the
main world-space solution and says the diagnostic lamp does not feed it. The
current saved profile selects surface-attached GI, and current shaders include
explicit finite-emitter lighting. Treat the code and serialized settings as the
source of truth. Capture source hashes when implementation starts because Button
is under active development.

## What Button actually does

| Layer | Current implementation | Transfer decision |
|---|---|---|
| Direct light and shadows | URP light/shadow inputs with a custom receiver-plane-corrected shadow filter. Near blockers retain tight contacts; separated blockers permit wider filtering. Saved softness: **1.55**. | Borrow the filter and controls; start with a narrower space-sun setting and compare. This is an artistic filter, not a physically calibrated solar angular diameter. |
| Ambient | Uniform ambient, saved intensity **0.23**, almost neutral/lilac tint. The controller disables environment reflections and fog. | Retain independent controls, but use Spaceland's low ambient and environment reflections. Do not copy Button's reflection shutdown. |
| World-space GI | The saved profile enables **surface-attached, first-bounce GI**, **0.4 m** sample spacing, **128 rays/sample**, smoothing **0.85**, display gain **3**. Analytic shapes and a spatial tree provide visibility and diffuse reflectance. | Primary GI candidate. Adapt geometry, receiver association, sources, and update budgets. Start gain at 1 for validation; artistic gain comes afterward. |
| Alternative world-space GI | Radiance cascades retain a volume-grid mode with feedback bounces. The saved five-bounce value does **not** turn surface mode into five-bounce GI. | Retain as a reference/fallback experiment, not an additional simultaneous GI layer. A whole-station bounding box wastes samples in empty space. |
| AO | Custom SSILVB, half-resolution, **4 slices × 8 steps × 2 sides**, **0.3 m** AO radius and **0.12 m** assumed surface thickness. Separate AO-only path. | Good first port. Replace Unity SSAO for participating cameras; never stack both. Thickness must be tested against armour and open trusses. |
| Screen-space GI | Same SSILVB trace, **0.8 m** radius, saved gain **1**. Extra source/receiver passes contain directly lit diffuse energy, excluding highlights, emission, ambient and previous GI. No temporal accumulation. | Optional close-detail supplement. Keep off during world-space GI validation to avoid duplicate bounce. It cannot supply off-screen lighting. |
| Local sources | Current SDF code evaluates finite emissive source proxies, visibility, and source-guided sampling. Direct fixture illumination remains a separate Unity-light responsibility. | Adapt for grouped windows/hangars. Replace furniture-specific bulb gain and spherical-source assumptions where needed. |
| Post-processing | Scene profile contains bloom: intensity **1.5**, threshold **1**, scatter **0.7**. Default volume tone mapping is off and grading is neutral. | Compare bloom carefully against Spaceland's existing ACES and bloom 0.35. Do not copy the whole exposure/tonemapping setup. |
| Material scattering | Cloth/felt transmission and fibre sheen are part of Button's material shading. | Omit from opaque armour. These effects are not atmospheric scattering and would be inappropriate as a global metal treatment. |

The world-space solver supplies irradiance through a half-resolution screen cache,
then materials reconstruct it using depth and normals. Its source of lighting is
still world-space geometry, so off-screen objects can contribute. SSILVB is a
separate calculation that only sees the camera's depth and source buffers.

## Source map

Paths below are relative to `C:/Transhuman/Button/Assets/Button`.

| Borrowed responsibility | Files |
|---|---|
| Lighting controls and selective updates | `Runtime/ButtonLightingSettings.cs`, `Runtime/ButtonLightingController.cs` |
| Direct lighting, AO application, soft shadows | `Resources/ButtonCabinet/ButtonLighting.hlsl` |
| Screen GI/AO renderer and shaders | `Rendering/ButtonScreenGiFeature.cs`, `Resources/ButtonCabinet/ButtonScreenGi.shader`, `ButtonSsilvb.hlsl`, `ButtonSsilvbMath.hlsl`, `ButtonScreenGiSource.hlsl` |
| Geometry and visibility | `Runtime/SdfLightingWorld.cs`, `Runtime/SdfLightingShape.cs`, `Runtime/SdfLightingOccluder.cs`; `Resources/ButtonCabinet/ButtonSdfLighting.hlsl`, `ButtonSdfTrace.hlsl` |
| Surface GI | `Runtime/SurfaceProbeGi.cs`, surface-mode orchestration in `Runtime/SdfRadianceCascades.cs`; `Resources/ButtonCabinet/SdfSurfaceGi.compute`, `ButtonSurfaceGi.hlsl`, `ButtonGiTransport.hlsl`, `ButtonSdfEmission.hlsl` |
| Irradiance presentation | `Rendering/SdfIrradianceFeature.cs`; `Resources/ButtonCabinet/SdfIrradiance.shader`, `ButtonSdfSampling.hlsl`, relevant `ButtonRadiance*.hlsl` bindings |
| Existing regression ideas | `Tests/Editor/SsilvbTests.cs`, `SsilvbRenderingTests.cs`, `SdfRadianceTests.cs`, `FarLampSurfaceTests.cs`, `SurfaceGiReceiverReadback.cs` |

Extract the lighting-specific code into `Assets/DeathStar/Runtime/Lighting`,
`Rendering`, and `Shaders/Lighting`. Rename namespaces, resource paths, global
properties, shader tags and retained AO keywords consistently. Preserve provenance
and algorithm comments. Remove dependencies on furniture assemblies, room tools,
material inspectors, fabric and texture-reflectance baking; use a small geometry
registration interface and Spaceland's material slots instead. Some geometry helpers
share files with furniture-specific types and cannot simply be copied wholesale.

## Adaptations that determine whether this will work

### Geometry must match the bent station

Spaceland produces cell meshes, bends them through `CurvedTileMesh`, and has
separate rigid, tube and already-curved placements. An unbent box per cell would
block empty space and miss real surfaces at ridges, crowns and hollow connectors.

Emit a separate coarse lighting representation alongside tile construction:

- Closed hull volumes or watertight coarse shell pieces fitted to the actual cell
  corners. Preserve station interiors and openings; never fill the hollow sphere
  with one solid occluder.
- Boxes, cylinders and rounded shapes for rigid equipment using the same final
  placement as the render mesh.
- Piecewise shells/prisms or a purpose-built warped-cell primitive for bent hulls,
  wedge/apex cells and tubes. Button's triangle prisms are useful building blocks;
  validate shared boundaries before expanding their use.
- Actual large truss members and conduit runs, without filling their open cells.
- Fine carving, bolts, thin grilles and plate seams remain in rendered geometry
  and screen AO; do not create GI shapes for every detail triangle.

Register proxies from `FoundrySettlement.Build/Rebuild/Remove/Dispose` and the
gallery's construction path. Use stable cell/part IDs, independent of renderer
instance IDs and near/far LOD selection. Reuse the existing dirty-tile set for
geometry updates, but distinguish that set from the larger region whose lighting
may have changed.

### Receiver sampling needs an explicit design

Button finds the receiving proxy with a **0.3 m** backward normal ray, then samples
one of six local surface charts (with a separate planar gather for room triangles).
Spaceland's relief reaches roughly **0.8 m**, and its curved faces do not generally
match affine box charts. Copying this lookup would leave some visible surfaces
with missing GI or attach them to the wrong wall.

For the pilot, give the large bent faces stable surface charts and sampled final
positions/normals; associate visible geometry with those charts explicitly. Use
visibility-checked interpolation and matching normals to prevent light crossing
thin walls. Give equipment its own charts. Preserve the existing UV0–UV5 meanings;
choose a dedicated additional attribute/buffer for lighting association. Verify
both LODs sample the same lighting field. Do not solve missing associations by
simply increasing the backward ray distance through neighbouring structure.

### Bound work and memory before scaling up

Button caches unchanged solutions, but a geometry or source change currently
re-solves transport across its surface probes. Its **65,536-ray batch** limits
scratch allocation, not total work in that update: all batches are dispatched in
one call. Charts cap each axis at 33 samples, but there is no equivalent global
station probe budget. The cascade mode separately limits its base grid to 12,000
probes by increasing spacing, which can lose metre-scale detail over a station.

Spaceland needs explicit limits for proxy count, probe count, source count, memory
and rays dispatched per frame. Use finer samples in the working area and coarser
samples elsewhere, including off-screen contributors. Keep coarse occluders for
the whole station; camera culling must not make a hidden wall transparent to light.

Coalesce construction edits, retain completed lighting while a replacement is
calculated, and reject results from superseded geometry revisions. Start with
bounded global recomputation for the small pilot. For large builds, schedule dirty
regions and their lighting dependants; a removed blocker can change distant sun
visibility, so updating only immediate neighbours is insufficient. Sun changes
invalidate transport globally. Idle scenes should dispatch no transport work.

Group nearby window sources and spatially select relevant emitters. Button loops
over emitters in several stages; one emitter per pane would scale poorly.

### Preserve the lighting energy and camera contracts

- Use material **diffuse reflectance**, not the entire base colour, for outgoing
  bounce. Painted hull can bounce light; bare metal should not become a strong
  diffuse emitter. Keep specular/environment reflection on its own path.
- Debug family/shape colours must not enter source radiance or change the lighting
  world. Factor physical surface evaluation separately from display overlays and
  share it between visible and source passes.
- Screen-space GI and world-space GI overlap. First ship AO with world-space GI;
  add screen GI only at a measured, limited strength if it recovers missing detail.
- Keep direct source illumination separate from reflected energy. Link visual
  emission, any Unity fixture light and transport-source intensity through one
  source definition; do not multiply Button's furniture bulb scale into our values.
- Treat planetshine as a broad directional environment source with visibility.
  Button's constant sky radiance does not encode that direction. Evaluate a planet
  lobe/disk on escaping rays and account for its illumination of source surfaces.
  Remove the overlapping unshadowed rim contribution only once the new contribution
  is validated, so the planet is not counted twice.
- The screen feature requires single-sample base cameras; both projects' PC assets
  already have MSAA 1. Keep build-safe custom AO variants when removing Unity SSAO.
- Tile Lab uses a temporary preview **scene** and explicit `Camera.Render()`.
  Button's active-scene controller and singleton world cannot be transplanted
  unchanged. Bind settings/world per participating camera, including the lab's
  camera, restore prior globals on completion, and explicitly synchronize the
  solver before capture. Verify camera type instead of blindly disabling preview
  checks for every editor camera.
- Preserve buffer lifetime and per-camera reset guards. Scene switches, disabled
  features and disposed lab rigs must never leave shaders reading stale buffers.

## Implementation sequence

### 1. Establish comparison captures and shared controls

Create a DeathStar lighting profile with independent direct, ambient, planet,
world-GI, screen-GI, AO and reflection controls. Keep current `TileLook` values as
the baseline preset. Prepare DeathStar-owned renderer/pipeline assets for A/B
selection without overwriting the existing PC renderer. Apply the same profile in
Foundry, the gallery, and Tile Lab.

Extend the headless lab with fixed lighting comparisons: sunlit armour, a deep
trench, a hangar opening, a thin truss, a ridge/crown, station interior and orbital
view. Capture normal, clay, direct-only, world-GI-only, screen-GI-only and AO-only
views where the stage supports them. Use locked exposure and identical cameras.

**Exit:** baseline is reproducible, and every effect can be isolated without
changing the light sources feeding the GI calculation.

### 2. Port soft shadows and SSILVB AO

Extract Button's shadow filtering and shared light access into the tile surface
shader; retain URP material response and Spaceland reflections. Start sun softness
at 1 and compare against 1.25 and Button's 1.55. Spaceland's current 600 m shadow
distance versus Button's 50 m is significant: inspect cascade texel size, contact
quality and camera distance before attributing differences to the filter.

Port SSILVB in **AO-only** mode. Start with 4×8 sampling, 0.3 m radius, 0.12 m
thickness and direct-AO strength 0–0.1. These are experiment settings, not final
tuning. Disable Unity SSAO for this renderer. Retain depth/normal reconstruction,
background handling, custom build keyword and the lean AO-only allocation path.

**Exit:** clearer contacts without black halos, sealed seams without light leaks,
open truss gaps intact, stable orbiting, and exactly one AO path in a Player build.

### 3. Prove surface-attached GI on a bounded station fragment

Use a small hull/trench/hangar assembly with a ridge and truss alongside it. Add
lighting proxies, explicit receiver charts, one-bounce transport and the
half-resolution irradiance presentation pass. Start at gain 1 and compare
0.4/0.8 m sample spacing and 64/128 rays. Keep screen GI off and sky input black
for the initial sunlight-only diagnostic.

Port the relevant numerical and GPU regression fixtures with small synthetic
geometry. Show that a sunlit wall lights its neighbour, an opaque divider stops
it, removing a blocker updates it, and an off-screen source still contributes.
Confirm idle caching, edit cancellation and bounded dispatch before enabling the
larger examples. If chart/proxy fidelity fails, fix that representation before
compensating with gain or smoothing.

**Exit:** the fragment has useful, stable bounce through edits and all orientations;
the implementation exposes measured cost and hard resource limits.

### 4. Scale up and add inhabited light sources

Integrate chunk/region scheduling and coarse/fine lighting coverage with the full
station examples. Add clustered warm window/hangar sources using the same curved
placement as their visible openings. Button's finite-source treatment provides a
starting point; wide windows need oriented emitters or bounded source clusters,
not a single isotropic bulb shining through the wall behind them. Test occultation
with the sun off so ambient cannot hide leaks.

Add visibility-aware planet illumination. Only after world-space lighting is
working, expose optional SSILVB GI by adding physical diffuse source/receiver
passes to `TileSurface`. Handle opaque interior-mapped `TileGlass` deliberately:
it is depth-visible but its bright room pixels are a visual illusion, so their
physical light comes from the registered opening sources rather than blindly
treating the entire shader output as radiance.

**Exit:** full-station construction remains responsive, dark-side habitation reads,
and no panes, concealed lights or debug overlays produce unintended energy.

### 5. Finish the look and verify delivery

Tune ambient downward as actual bounce becomes available, retaining enough fill
for construction readability. Balance neutral/slightly warm sunlight, cool planet
light and restrained warm habitation. Compare existing ACES, contrast and bloom
with Button-inspired settings at fixed exposure; do not use bloom to hide missing
transport. Keep reflections, and assess local reflection probes as a separate
enhancement rather than attributing that feature to Button. No global fog or
cloth scattering is required for this transfer.

Run `tools/tile-lab.sh report` from Bash against Spaceland, in Edit mode, for the
final headless comparisons; include the curved station/ridges/truss captures as
well. The script's 2× renders are for visual review, so profile separately at the
native target resolution. Verify shaders and Player variants, then compare GPU
pass medians/p95, total frame time, GI update latency, buffers, and repeated-edit
behaviour on the same hardware and scenes.

Button's archived 1600×1000 study recorded about **0.30 ms AO-only** and **0.76 ms
combined screen GI/AO**, excluding depth/normal prerequisites and world-space GI.
These are reference measurements only, not a Spaceland budget or a measurement of
the current surface solver. Its README also records an unresolved DX12 error in a
later rapid render/readback validation; use bounded captures and retain logs if
reproduced. Do not rerun that stress workload as part of ordinary visual review.

## Acceptance checks

- Adding, removing, undoing and loading cells updates proxies and illumination;
  replacing the station frees the old resources.
- A sealed dark cavity stays dark with ambient and emitters disabled; a lit opening
  admits light, and closing it removes that contribution.
- A neutral receiver beside a coloured source changes colour with the source;
  source-off and source-albedo tests distinguish bounce from receiver tint.
- Rotating the assembly through all construction orientations introduces no
  world-up lighting assumption. Ridge/crown seams and hollow interiors remain valid.
- Camera orbit, screen edges and near/far LOD switches do not make world-space GI
  disappear or change its source energy. Screen-space limitations remain confined
  to the optional screen contribution.
- Neither grazing shadows nor AO seal genuine truss openings or turn close seams
  into bright leaks. Material roughness and metallic response remain distinct.
- Inactive cameras and lab teardown clear their lighting bindings. Unsupported
  cameras use a defined baseline. Player builds retain the required shader variants.
- A static scene performs no GI transport dispatch; rapid edits remain within the
  chosen work/memory limits and do not publish stale lighting.

**First deliverable:** identical-camera comparisons of the existing station
lighting against Button-style shadows and AO, followed by a small fragment with
working surface GI. Promote that path to the full station only after the curved
proxy, receiver and scheduling checks pass.
