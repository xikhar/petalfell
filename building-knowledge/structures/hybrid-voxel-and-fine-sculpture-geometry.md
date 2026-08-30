# Hybrid voxel and fine sculpture geometry

- **Lifecycle:** `active`
- **Evidence summary:** author-supplied GLB cleanup, bottom-centred placement,
  baked-material replacement, world-space stone, inverted-hull silhouette ink
  and compound collision are `mechanically verified`; the Reference 12 v21
  application is `visually reviewed`; broader reference-family use remains a
  `candidate`
- **Scope:** `site-specific` evidence for Reference 12; candidate low-level
  method for future statuary, collapsed diagonals and carved monumental forms
- **Last verified:** 2026-08-31 with passing build/audit and inspected v21 capture
- **Supersedes:** the rejected all-integer sculpture and the provisional
  hand-authored fractional-cuboid Blender sculpture
- **Superseded by:** none

## Why this layer exists

Voxel style does not require every visible form to be a one-metre axis-aligned
cube. Terrain, foundations and load-bearing masses benefit from the integer
`VoxelGrid`: they stream, collide, mesh and seam with the world. A face, bent
crown, ankle, diagonally fallen beam or broken sculptural plane needs finer
units. Enlarging one integer box for every feature produces towers, masks and
forts instead of sculpture.

The current proven split is:

1. Keep the clearing, stairs/plinths and stone court in the strict site-owned
   voxel plan; do not bake the imported object's accidental ground into terrain.
2. Run `tools/prepare_meshy_fallen_colossus.py`. It selects the detailed Meshy
   subject rather than the calibration cube, removes disconnected image-floor
   components below measured vertical cutoffs, clears source materials and
   writes a bottom-centred GLB. Never select only the largest loose island:
   Meshy's voxel-like subjects intentionally contain hundreds of islands.
3. Attach each subject independently at the permanent atlas origin. Scale and
   yaw remain explicit site measurements; the import step does not choose them.
4. Override every imported `MeshInstance3D` with Petalfell's world-space
   sculpture stone. Add a restrained inverted-hull plum outline because the
   ordinary voxel ink mesh expects edge-run custom channels an arbitrary GLB
   does not contain.
5. Keep collision invisible and conservative with site-owned compound shapes;
   a visible voxel core will poke smooth slabs through the imported model.
6. Rebuild the fine node with every moving atlas window that contains the
   production site. It is derived runtime geometry, never a floating preview.

This is low-level geometric vocabulary only. It must not become a reusable
statue, head, stair, arch or ruin generator. Each reference continues to own its
part dimensions, transforms, damage and silhouette.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| Fine geometry is attached in initial production play and both map/walking window replacements | `mechanically verified` | tool-specific | `Reference12SculptureDetail.cs`, `AtlasSectorReview.AttachFineSiteGeometry`, passing `dotnet build --no-restore`, 2026-08-31 | Live player-controlled handoff still needs review |
| Meshy calibration cubes, low image-floor components and baked materials are absent from the normalized head/legs assets | `mechanically verified` | import-specific | `tools/prepare_meshy_fallen_colossus.py`; head source `2abd6a29…`, legs source `0820092a…`; successful Godot reimport, 2026-08-31 | A differently generated asset needs new measured cutoffs |
| Petalfell stone and a 0.009-unit silhouette outline preserve the imported facial/crown and anatomical leg geometry over a massive site-owned voxel foundation; the legs face the source-forward axis at yaw 0 | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-12-v21-meshy/reference_match_day.png`, inspected at original size 2026-08-31 | Night response and hidden rotations still need author review |
| Hybrid sculpture is suitable for every future reference | `candidate` | reference-family | Reference 12 proves one statuary case | Needs a second distinct reference and collision/play review |

## Checks

- Build and run the strict site-plan/world audit. Fine geometry may elaborate a
  declared structure but may not create a new footprint or composition centre.
- Capture the locked view, player-scale view, far view and four rotations. Check
  silhouette first, then facial/ornamental planes, then ink density.
- Confirm the permanent atlas startup and a neighbouring-window rebuild both
  recreate the node. A review-only mesh is not an implementation.
- Walk against and onto the form. Compound boxes exist for both legs and the
  head, but live traversal is required before claiming collision acceptance.

## Known failures

- **Large cuboid facial layers:** read as a small building or mask on a slab.
  Replaced by the author's Meshy head.
- **Metre-scale protruding kneecaps:** read as robot joints. Replaced by a
  single authored Meshy leg subject.
- **Visible voxel collision shells:** intersected the imported model as broad
  smooth slabs. Replaced by invisible compound collision.
- **Baked Meshy colour and image floor:** looked detached from Petalfell and
  introduced rectangular bases. The normalizer strips both; runtime owns stone.
- **Meshy debris/pillars around the subjects:** explicitly removed from the
  current blockout at the author's request. The unused source is not placed.

## Update triggers

Update this entry when compound fine collision is implemented, a second
reference uses the method, the outline/material path changes, or the author
rejects the current sculptural read.
