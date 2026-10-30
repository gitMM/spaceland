# Local lamp lighting

Small emissive tile surfaces have a second, analytic source of diffuse illumination.
`StationLocalLightPass` draws instanced light volumes into a full-resolution HDR
irradiance texture, using the camera depth and normal buffers. The shared
`StationLighting.hlsl` applies that light through each material's diffuse
reflectance before post-processing. It does not multiply the lit camera image,
sample GI history or require the bulb itself to be on screen.

The controls are in **Death Star > Lighting settings > Local lamp lighting**:

- **Local Lights** enables the pass independently of screen-space and voxel GI.
- **Local Light Strength / Range** control the light pools. **Emission** also
  scales them, alongside the visible bulbs.
- **Local Light Distance / Budget** bound camera reach and visible sources.
- **Local Lamp Screen Gi** controls the bulbs' contribution to SSGI while the
  local-light pass is active. The default is zero to avoid sampling tiny bright
  sources twice. Bulb appearance and the existing voxel emission are preserved.

`StationLocalLightSource` reads connected emissive components from a specified
mesh material slot (default: tile `Light`). It welds coincident triangle vertices,
derives short point/strip descriptions and caches them per final mesh. Foundry
sources are attached after bending, once on the near renderer, so LOD changes
do not create duplicate lights. Gallery and interior meshes use the same source.
`StationKit` supplies instanced lamp placements and their growth directly.
Replacing or destroying geometry updates sources through their component lifetime;
call `StationLocalLights.Invalidate(mesh)` after an in-place mesh edit.

Each source also caches its world-space lamps by mesh, transform, material slot,
emission and mounting plane. A gather reads each shared material once and checks
camera scene, layer and distance immediately. The kit reuses the nearby lamp
indices collected during its per-camera draw, preserving original placement
order. Per-lamp filtering compacts the list without repeated shifting.
`tools/tile-lab.sh lightingcache` compares complete cached/reference lamp data
under mutations and reports alternating full-stage gather timings.

Tile and interior builders author each lens's emitting direction with
`Finish.Mounted(direction)`. Cylindrical beacons default to emitting away from
their base. Sparse CPU metadata follows vertices through mesh assembly, LOD
copies and curved placement. Extraction places each lamp's cutoff plane at the
back of its own lens, so lamps on different walls in one mesh remain independent.
Object transforms apply the inverse transpose to these planes. Rebuild generated
meshes after changing authored directions; cloning a Unity Mesh alone does not
copy this CPU metadata.

Other procedural systems can submit `StationLocalLights.LightData` through its
`Collect` event. Sources must respect camera scene/layer visibility; use
`VisibleTo`. Unsubscribe when disabled. The world-space mounting plane clips
light behind its supporting wall. Tile, interior and kit lamps supply it
automatically; a source component can also override it for a single mounting
surface. Unannotated external meshes keep their unrestricted point/strip light.

These are short-range diffuse approximations without shadow maps or specular
highlights. Intervening geometry does not cast local-light shadows, and a mounting
plane is not general occlusion. Keep ranges short. Transparent receivers are
excluded because their depth differs from the opaque depth used by the pass.
The existing renderer's non-XR base-camera and no-MSAA constraints apply.
Overlapping light volumes still cost fill rate. Sources are frustum/distance culled,
fade at their distance limit and use a nearest-source budget.

Run `tools/tile-lab.sh locallights` for the isolated lamp fixture, camera-motion,
off-screen-source, mounting-plane, disable and GI-source checks. It also audits
all lamps in the gallery and five curved examples, including interior and LOD
meshes, and renders front/back receivers for real generated mesh sources.
Captures and
measurements go to `DeathStarRuns/Tiles/local-lights/`. Run
`tools/tile-lab.sh report` for the complete settlement review.
