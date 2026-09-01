# Reference-site construction

## Purpose

Production ruins are exact reconstructions of supplied
`world-new/reference-*.png` sources. They are not procedural variations, modular
kits or mood-board interpretations.

The player should read one continuous place: terrain establishes why a structure
is there, architecture follows its levels and routes, damage reveals age, and
vegetation reclaims the same joints.

## Source authority

Measure the visible reference:

- footprint and axes;
- relative levels and stair runs;
- wall, arch and pillar positions;
- major silhouette and negative space;
- breaks, collapsed masses and rubble;
- surrounding terrain cuts, water and vegetation;
- player-relative scale;
- locked isometric camera.

Hidden geometry may complete an obvious continuation only. Do not add a new
court, axis, tower or landmark behind the camera.

## Plan before volume

Every site begins with a top-view ground plan registered to source pixels and
runtime coordinates. Author:

1. natural terrain areas and cuts;
2. detached/stacked slab footprints and surface heights;
3. circulation and exact stair footprints;
4. foundations, walls, arches and pillars;
5. damage gaps and rubble zones;
6. vegetation exclusion/reclamation zones;
7. fine mesh anchors and player spawn;
8. locked reference view.

The plan is reviewable before vertical detailing. Correcting a footprint in 2D
is cheaper and more reliable than moving a finished facade by eye.

## Construction rules

- Each visible element is a site-owned record. Rectangular fills are storage
  shorthand, not reusable architectural designs.
- Pillars keep a measured square cross-section unless the reference shows joined
  shafts or buttresses.
- Stairs have explicit start, end, tread, width and surrounding cut; decorative
  bumps may not substitute for steps.
- Foundations rise from or cut into terrain. Floating slabs and unrelated flat
  pads are failures.
- Large monuments gain detail by using smaller blocks, recesses, broken courses,
  inlays and surface variation—not by scaling a few cubes.
- Terrain uses several separated layers where the reference shows them; avoid
  concentric rings and tidy perimeter walls.
- Damage is coherent. Missing courses reveal load and history; random holes are
  not decay.
- Surface breakup uses broad patches plus fine world-space weathering. Never use
  per-block colour confetti.

## Fine geometry

Voxel architecture remains the site frame. Author-supplied GLBs may represent
sculpture or forms that cannot be expressed faithfully as cubic blocks.

Before placement:

1. normalize and pivot deterministically;
2. strip source/environment meshes and baked materials;
3. apply Petalfell stone and controlled silhouette ink;
4. orient and scale against the player/reference;
5. add explicit collision matching the usable silhouette;
6. anchor it to a site-owned foundation.

Do not voxelize a supplied sculpture merely to make it match the storage format,
and do not let a fine mesh replace the surrounding measured site.

## Terrain integration

Site records operate after natural terrain and before vegetation. One measured
natural datum is translated onto the local production surface; the site keeps
its authored relative heights.

Use cuts, fills, worn caps, exposed substrates, detached slabs, stair approaches,
rubble toes and vegetation gaps to join the site to the land. The surrounding
terrain remains the accepted production grammar rather than a bespoke circular
arena.

## Review and acceptance

Required views:

- source-matched isometric angle;
- calibrated top view;
- close, play, wide and far distances;
- four 90-degree rotations;
- day and night where lighting/materials are in scope.

Compare structure, scale, negative space, ground layout, terrain integration,
surface breakup, lighting and player readability separately. A correct hero
angle cannot hide a hollow rear or wrong top plan.

Use the procedures and evidence vocabulary in
[`building-knowledge/`](../building-knowledge/README.md). A rendered capture is
not accepted until inspected; only an explicit author decision is
`author-accepted`.

## Current site order

1. Preserve and correct Bloom Grove Court against Reference 10.
2. Preserve and correct Fallen Colossus against Reference 12.
3. Build Shallows Gate and Causeway from Reference 1 as the next complete
   transcription.
4. Allocate further references only after these establish reliable structure,
   sculpture and monumental-water workflows.

The retired generic ruin kit and summit monument remain historical code only.
They are not production authoring tools.
