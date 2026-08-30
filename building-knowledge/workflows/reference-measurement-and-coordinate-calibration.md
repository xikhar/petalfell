# Reference measurement and coordinate calibration

- **Lifecycle:** `active`
- **Evidence summary:** `observed/source-measured`, `mechanically verified`, and
  `visually reviewed`; not `author-accepted`
- **Scope:** Reference 10 is `site-specific`; the calibration sequence is a
  candidate for other references and must be solved again for each one
- **Last verified:** 2026-08-30 against the current v13 locked/top Reference 10
  captures
- **Supersedes:** the rejected fractional-scale/group-offset placement described
  under [Known failures](#known-failures)
- **Superseded by:** none
- **Owning sources:**
  [`world-new/reference-10.png`](../../world-new/reference-10.png),
  [`world-new/reference-10-top.png`](../../world-new/reference-10-top.png),
  [`bloom-grove-court-reference-10-plan.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10-plan.json),
  [`bloom-grove-court-reference-10.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10.json)

## Outcome

All site geometry uses one named, integer, source-facing coordinate frame. The
locked isometric source owns final silhouette and occlusion; an overhead source
helps measure plan topology but does not overrule the isometric image. Runtime
rotation/reflection is declared once in data rather than being re-invented in
each builder method.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| Reference 10 uses a two-block traveller scale and source quadrant with yaw 135 degrees and true-isometric pitch 35.26439 degrees | `observed/source-measured` | site-specific | Visible traveller and ground axes in `reference-10.png`; plan `coordinateContract`; site `referenceView` | Perspective/occlusion still makes some hidden depths uncertain |
| One source-plan cell maps to one voxel, with scale 1 and one explicit X reflection | `mechanically verified` | tool-specific | Ground-plan audit requires `oneCellIsOneVoxel` and scale 1; `Reference10GroveCourt.LocalCell` applies `runtimeMirrorX` once | Does not prove landmark coordinates were interpreted correctly |
| The locked and overhead calibrations render in the expected orientation | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-10-plan-v13/reference_match_day.png`, `reference_overlay_50.png`, `reference_top_day.png`, `reference_top_overlay_50.png`, and both edge differences inspected 2026-08-30 | Geometry and material match remain visibly incomplete; no author acceptance |

## Procedure

1. Name one primary structural reference. Treat other angles or generated top
   views as supporting evidence and say which facts they may own.
2. Establish voxel scale from the player when visible; in Petalfell the player
   is two blocks tall. Cross-check stair risers, wall thickness, and tree trunks
   before freezing the scale.
3. Solve the comparison quadrant from screen-left/right relationships, both
   ground axes, and occlusion order. Use the true-isometric pitch only when the
   source axes support it.
4. Pick at least three dispersed calibration landmarks: player/spawn, a major
   stair corner, and a remote facade or platform corner. A calibration that fits
   only the centre is underconstrained.
5. Declare origin, mirror, rotation, scale, source yaw/pitch, and player spawn in
   the plan/site records. For Reference 10 these are `runtimePlanScale: 1`,
   `runtimeMirrorX: true`, yaw 135, and pitch 35.26439.
6. Use integer cells from that point onward. Do not round separately inside
   structure groups or apply private offsets to make one mass look better.
7. Generate both source-facing and runtime-facing plan previews. Then capture
   the locked isometric and calibrated top views before adding fine detail.
8. If the two views disagree, diagnose the frame first—mirror, axis, origin,
   focus, scale, then camera—before moving architecture.

## Checks

### Mechanical

```bash
./tools/world-authoring.sh preview-site-plan bloom-grove-court /tmp/bloom-grove-source.svg
./tools/world-authoring.sh preview-site-plan bloom-grove-court /tmp/bloom-grove-runtime.svg --runtime-facing
./tools/world-authoring.sh reference-plan-overlay /tmp/bloom-grove-overlay.svg
./tools/world-authoring.sh audit
```

These commands prove schema/coordinate invariants and produce calibrated plan
artifacts. They do not prove visual correspondence.

### Visual

Inspect the source-facing preview against the overhead source, then inspect
`reference_match_day.png` and `reference_overlay_50.png` against the primary
isometric source. Check dispersed landmarks, screen-side ordering, stair travel,
platform edges, and the player-relative scale. An attractive different quadrant
is not a match.

## Scope and limits

The numeric calibration above belongs only to Reference 10. The procedure is
reusable, but every new source needs its own camera solve and landmarks. The
overhead image is a strong topology guide, not a one-to-one authority for
vertical silhouette, openings, materials, shadows, or source-visible damage.

## Known failures

- A rejected pass used a roughly 0.90 runtime scale, rounding, and group-local
  offsets. Cells collapsed or drifted and overlays could not identify the real
  error. The replacement is one cell to one voxel plus one declared reflection.
- Comparing from a convenient cardinal angle reversed source relationships and
  made wrong geometry appear close. The locked source quadrant now owns the
  comparison.
- Treating the overhead image as final truth produced plausible plan geometry
  with the wrong vertical silhouette. The isometric source remains primary.

## Update triggers

Update this entry if the source image, site origin/axis, mirror, plan scale,
camera focus/distance/yaw/pitch, top-image registration, player scale, or any
calibration landmark changes. A new capture is required before retaining the
`visually reviewed` state.
