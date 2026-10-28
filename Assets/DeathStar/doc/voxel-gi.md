# Station voxel cone tracing

The station combines short-range SSILVB with cached world-space diffuse voxel
cone tracing. The default voxel reach is 40 m (eight nominal 5 m tiles), with
two diffuse bounces. Screen-space GI, AO, contact shadows, material reflections,
and the existing lighting settings remain active.

## Why this representation

Tiles span roughly 2.3–5 m on the curved grid, while carving and contacts are
centimetres across. The gallery contains an approximately 80 × 30 × 60 m station
fragment plus distant family specimens. Its complete geometry spans about 192 m
after padding. A 128³ occupancy volume therefore has approximately 1.5 m voxels;
this is intentionally for architectural bounce, not carved-panel contacts.
SSILVB retains the latter. Bounds fit the actual generated geometry, independent
of the camera, with no preferred up direction.

The basis is Crassin et al., *Interactive Indirect Illumination Using Voxel Cone
Tracing* (2011):
https://research.nvidia.com/sites/default/files/pubs/2011-09_Interactive-Indirect-Illumination/GIVoxels-pg2011-authors.pdf

This implementation uses a bounded dense hierarchy instead of the paper's sparse
octree. It implements diffuse transport, premultiplied radiance/coverage,
footprint-dependent mip selection, front-to-back cone integration with step-size
opacity correction, directional filtering, and a separate second-bounce solve.
It does not implement the paper's glossy BRDF distribution or sparse streaming.

## Update and render path

1. Collect active station mesh renderers from the camera's scene and layer mask,
   including off-screen geometry. Select each LOD group's coarse mesh exactly
   once. Cache transformed triangle/material sources by mesh, transform and
   material state, including material slot positions; unchanged meshes need no
   repeat vertex extraction. Copy the cached arrays directly into a mapped GPU
   upload buffer, without a second scene-sized managed triangle list.
2. Conservatively project triangles along their dominant axis into a 64³ or
   128³ occupancy grid. A deterministic atomic owner key prefers broad walls
   over sub-voxel trim. Resolve occupancy; lighting kernels read reflectance,
   emission and normals directly from the owning triangle. This removes three
   full-resolution surface textures (48 MiB at 128³).
   Habitat tint comes from the same family UV channel and linear paint setting
   as the visible material. Planning/debug overlays do not color the volume.
3. Inject the configured sun with world-space voxel visibility and the unshadowed
   planet light (matching the current rig). Material emission is included;
   ambient illumination, reflections, screen-space GI and camera color are not
   injected, so there is no feedback loop.
4. Store six directional radiance/opacity lobes at half the occupancy resolution.
   Front-to-back compositing along each axis preserves wall coverage and facing.
   The lobes share a 3D atlas with independent mip chains. Compute copies cover
   all depth slices; sampling clamps footprints within each lobe.
5. Optionally gather a second bounce at occupied voxels into a separate base
   texture, then rebuild the directional hierarchy. This is one bounded solve,
   not recurrent frame-to-frame accumulation.
6. Gather six deterministic diffuse cones at half screen resolution, bounded by
   32 steps and the configured distance. Add the irradiance to the existing
   GI trace, use its depth/normal-aware reconstruction, and apply receiver diffuse
   reflectance in the existing composite. When SSILVB is enabled, voxel radiance
   fades in over the last quarter of the screen-space bounce radius. Near
   occluders still block distant light during this handoff.

The gather uses one cached 32-entry step schedule shared by every cone. Distance,
mip level, atlas clamp margin and screen-space handoff are computed when volume
scale/reach/handoff changes, rather than inside every shader sample. Origins and
steps are in normalized volume space; exactly zero directional weights skip
their texture fetches. Rationalized half-step opacity avoids cancellation at
tiny coverage. The combined pass reconstructs the receiving surface once for
both SSILVB and voxel GI. Cone count, reach and filtering resolution are unchanged.

Only the directional volume persists on the GPU: about **13.7 MiB at 128³** or
**1.7 MiB at 64³**, plus the existing screen-space render targets. Source triangle
arrays are also cached on the CPU (approximately 22 MiB for the gallery).
Voxelization buffers and bounce/mip scratch textures are released after a
rebuild. A single non-mipped 12 MiB scratch atlas handles every occupancy and
directional mip, including the second bounce. A replacement volume is published
only after the solve has been submitted. Rebuild peak memory is higher than the
retained cache.

Foundry placement/removal/undo and gallery generation invalidate the volume.
Camera motion does not. Invalidation requests a signature check; an unchanged
scene, or a layer-mask change affecting no sources, reuses the volume. A
half-second scene/material signature check catches
transform, enable, lighting and material changes. Code modifying vertices or
colors in an existing mesh must call `StationVoxelGi.Invalidate(true)`; code
replacing meshes can use `Invalidate()`. Relighting and edits reuse unchanged
CPU source arrays, although the GPU volume is rebuilt as a whole.
With the second bounce disabled, adjusting reach only changes the final gather
and does not rebuild the radiance cache. Resource allocation and solve failures
release the temporary resources and leave the screen-space pass available.

## Controls and validation

**Death Star → Lighting settings** exposes voxel enable, strength, reach,
resolution, second bounce, and `Voxel Gi Only`. Existing SSGI-only and AO debug
views retain their original behavior. Suggested starting point: voxel reach
40 m, strength 1, resolution 128, second bounce enabled; screen-space reach 1 m.

Run from Bash with the spaceland Editor in Edit mode:

```bash
tools/tile-lab.sh voxelgi
tools/tile-lab.sh voxelbench
tools/tile-lab.sh report
```

`DeathStarRuns/Tiles/voxel-gi/` contains the real red-habitat trench with SSGI
alone, combined lighting, isolated voxel GI, and isolated SSGI. A controlled
fixture uses the same habitat paint/shader, a 10 × 10 m source, and a neutral
receiver 20 m away with the source behind the camera. GPU probes check red
irradiance at 5, 10 and 19.9 m, second-bounce return light, and an intervening
black wall. The report also checks exact SSGI preservation, cache reuse, material
slot changes, unused layer masks, explicit invalidation after editing mesh colors
in place, and distant red transport in the lower-memory 64³ mode.

`voxelbench` compares the optimized gather with a frozen Editor-only reference
on 32,768 actual station mesh surface samples and the same live radiance texture.
It validates 64³/128³ volumes, 5–80 m reach and the near screen-space handoff.
`throughput.md` records alternating reference/optimized batches (64 dispatches
each), with a one-element readback to wait for GPU completion. This isolates
cone-kernel throughput from rebuilding and image readback; it includes amortized
submission/synchronization overhead and is not a whole-frame GPU timestamp.

`report.md` distinguishes synchronous render/readback timings from GPU timings.
The attempted GPU capture returned stale timestamps, and a later asynchronous
capture was interrupted by a concurrent script reload; `gpu-timing.txt` records
that limitation. There is no isolated GPU-cost claim. These are local Editor
measurements, not standalone frame-rate guarantees. CPU build/submit timings
exclude GPU completion.

## Practical limits

- This targets the stationary procedural station and its sun/planet rig. It is
  not a general voxelizer for skinned meshes, arbitrary URP materials, textures,
  property-block paint, point lights or rapidly moving geometry. Tile glass is
  an opaque blocker in the voxel representation; its procedural room lighting
  remains in the visible material.
- A single bounded volume covers the scene. Large builds make its voxels coarser;
  thin geometry, very narrow gaps and sub-voxel color boundaries remain approximate.
  The dominant-surface owner and six-cone quadrature trade accuracy for stable,
  bounded work. There are no clipmaps or sparse octree levels.
- Geometry/light changes rebuild the GPU volume synchronously; the first CPU
  source extraction can hitch. Static frame cost is separate from rebuild cost.
  More than 4,194,240 coarse triangles disables voxel GI with a diagnostic while
  leaving screen-space GI available.
- As with the existing screen GI feature, the integration is for non-XR base
  cameras without MSAA. Reflection and preview cameras are excluded. GPU compute
  and 3D texture support are required.
