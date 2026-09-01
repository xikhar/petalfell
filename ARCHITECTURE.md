# Architecture

This document defines production boundaries. Implementation details belong in
source comments; current evidence belongs in [`CURRENT_STATE.md`](CURRENT_STATE.md).

## Runtime composition

Normal startup has one path:

```text
Main
 ├─ WorldAuthoring (headless commands; exits)
 └─ production runtime
     ├─ accepted atlas sources
     ├─ bounded ProductionTerrainWindow
     ├─ promoted authored sites
     ├─ AtlasSectorWindow / VoxelGrid
     ├─ ChunkStreamer + collision
     ├─ water / materials / ink / atmosphere
     ├─ player + camera + developer controls
     └─ AtlasRuntimeHandoff + AtlasWorldMap
```

`Main` does not assemble an alternate world. Retired implementations are stored
outside the compilation tree under `reference/retired-code/`.

## World ownership

The atlas is 12,288 × 9,216 blocks. Four accepted images own land, elevation,
hydrology and region intent. Topology JSON owns domains, permanent site origins,
connections, status and reference-plan paths.

The images are macro controls, not block masks. `ProductionTerrainGuide` samples
them in global coordinates and supplies the existing local terrain grammar.
`Planner` and `Terrain` create shelves, broken terraces, banks, submerged beds,
materials and natural detail inside a bounded window. All randomness is a pure
function of world seed plus absolute atlas coordinates.

## Bounded windows

`ProductionTerrainWindow` owns a sector-aligned 2 × 2 window (1,536 blocks
square). It creates a local `VoxelGrid` with a global origin, applies production
sites whose complete footprints fit, then populates vegetation.

`AtlasRuntimeHandoff` chooses adjacent windows for walking and centred windows
for map travel. A replacement is built completely before it is installed.
Walking preserves the exact player transform and requires matching terrain,
water and nearby collision ownership; map travel may search for a supported
landing. Neither path changes canonical geography.

## Terrain and water

`Terrain` remains the low-level source of the accepted block language:

- six-block registered terraces and broken layered edges;
- deterministic stairs selected in atlas space;
- gradual bank courses and underwater continuation;
- translucent animated water with depth/refraction;
- biome cap, substrate and deep-column materials;
- sparse terrain-owned natural formations.

Production changes only its macro inputs. Do not replace these primitives with
literal map-pixel extrusion or a second terrain generator.

Water identity comes from accepted hydrology after the production displacement
and shore response. `AtlasSectorWindow` builds visible water geometry from that
data; the shared shader owns movement, translucency and depth. Collision and the
controller query the same window water columns.

## Sites

Canonical topology decides whether a site runs in production. `Production` and
`Accepted` sites are overlaid; `Planned` and `Blockout` sites do not reserve or
alter normal terrain.

Each reference plan owns its footprint, levels, voxels, meshes, surface patches,
stairs and exclusion area. `ReferenceSiteBuilder` writes the plan into the
window after natural terrain and before vegetation. Its vertical datum is
translated onto the natural surface, but its authored proportions are not
rescaled.

Fine sculpture GLBs are prepared deterministically, stripped of source
materials, assigned Petalfell stone/ink, and given explicit compound collision.
They supplement site-owned voxels; they do not replace the measured site plan.

## Rendering

`ChunkStreamer` materialises only nearby chunks. `ChunkMesher` reads the voxel
grid, ground detail and overhang ceiling, then optionally builds collision.

One material pipeline is shared across terrain and sites:

- `voxel.gdshader` — world-space colour breakup and material response;
- `voxel_ink.gdshader` — silhouette ink;
- `water.gdshader` — animated translucent depth/refraction;
- `DayCycle`/`Atmosphere` — ordinary day and night lighting;
- `DeveloperMenu` — review-only live parameters.

Authored sRGB colours are converted to linear exactly once. Shader noise and CPU
noise must remain globally registered so moving-window ownership is invisible.

## Player, camera and map

The controller uses terrain/collision for land and the active window callback
for water. Manual Shift is slow walk; route-owned travel uses the same cautious
speed. A dry route cell requires headroom in both terrain and placed voxels.

Camera distance is player-owned. Nearby geometry may occlude the traveller but
must not change zoom. Wheel input changes distance; `K` linearly moves to maximum
at the developer-configured speed.

`AtlasWorldMap` renders accepted macro sources and canonical topology. A
successful Shift-click transport closes it after committing the replacement;
failed transport leaves it open.

## Data boundaries

- Authored: atlas images, topology, reference plans, site meshes and settings.
- Derived in memory: terrain windows, collision, dressing and map textures.
- Historical: anything under `reference/`; never loaded by runtime.

Generators never rewrite authored data. Runtime correctness must not depend on a
persisted terrain cache.

## Verification boundary

Build success proves compilation. Headless terrain checks prove deterministic
data, overlap and scripted collision behavior. Captures prove only that an image
was rendered until someone inspects it. Only the author can accept visual work.
