# Current state — 2026-09-02

This is a factual snapshot, not a design proposal.

## Accepted terrain foundation

The author has accepted the current production terrain level as the starting
foundation for further world and site work.

Normal startup opens the authored 12,288 × 9,216 atlas through a moving 1,536 ×
1,536 production window. The accepted land, elevation, water and region images
drive macro geography. The established low-level generator supplies layered
voxel ground, broken terrace edges, cliffs, gradual shores, submerged terrain,
biome materials, vegetation and sparse natural formations.

Water uses the shared animated translucent shader with visible depth,
refraction, broad movement and distance filtering. It is not a flat atlas plane.

All natural sampling is globally registered. A complete 165-window audit covers
every possible normal 2 × 2 ownership window and found matching safe terrain and
overhang ownership, no severe water steps and no submerged dry boundaries.
Representative land and water collision routes pass.

Walking near a window edge prebuilds the neighbour and preserves player, camera,
day and UI state. The current handoff compares the exact terrain/water and nearby
3 × 3 × 5 collision volume, removing the prior false invisible walls near water,
terraces and placed objects. Full-map Shift-click travel builds a distant window,
resolves a supported landing and closes the map on success.

## Current sites

- **Bloom Grove Court** is a promoted production voxel transcription of
  Reference 10.
- **Fallen Colossus** is a promoted production precinct for Reference 12. Its
  author-supplied head and legs GLBs are cleaned, assigned Petalfell stone and
  outline materials, placed on site-owned foundations and given collision. The
  legs use the approved 1.5× imported review scale.
- **Shallows Gate and Causeway** is measured/planned from Reference 1 but is not
  yet promoted as a finished production transcription.

The terrain foundation is accepted; the completed visual fidelity of individual
sites is not.

## Controls and review

- W/A/S/D or arrows: move
- Shift: slow walk
- Space: jump
- wheel: zoom
- K: linear auto-zoom to maximum
- Q/E: orbit in review modes
- M: production atlas map
- Shift-click map: travel and close map after success
- tilde: developer settings

Camera obstruction does not alter zoom. The earlier obstruction pull-in/recovery
behavior was removed.

## Source organization

`src/Main.cs` now only dispatches headless authoring commands and the production
runtime. The retired circular-world assembly, local map and summit monument are
preserved under `reference/retired-code/legacy-world/` with non-compiling file
extensions. They no longer have runtime flags.

The active low-level terrain, water, render, voxel, vegetation, player and camera
classes remain in `src/` because production uses them. The extracted local shelf
field is now named `ProductionTerrainGrammar`; its stable noise salt strings were
kept unchanged so the rename does not move accepted terrain.

The old 3,456-world topology is archived under `reference/retired-data/` and no
longer loads or audits at startup. The atlas declares only the four source layers
the runtime actually consumes; speculative culture, abandonment and wilderness
raster placeholders were removed.

## Verified commands

The current tree builds with zero warnings/errors. The production checks include:

- repeatable focused terrain generation and overlap comparison;
- all-window terrain ownership audit;
- scripted land and water traversal;
- walking-window handoff planning and collision continuity;
- map transport/open-state behavior;
- fixed camera distance and K auto-zoom.

These checks establish mechanics. The terrain-level author acceptance is the
author's explicit decision; future site and final lighting acceptance remain
separate.

## Open work

1. Finish Reference 1 as the next exact site transcription.
2. Continue improving Bloom and Fallen Colossus only against their references.
3. Traverse the accepted atlas for localized collision/route issues.
4. Tune final lighting, shadows, ink and atmosphere after site silhouettes are
   correct.
5. Build gameplay/story content on the accepted world foundation.
