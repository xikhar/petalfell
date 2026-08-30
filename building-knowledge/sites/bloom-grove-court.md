# Bloom Grove Court (Reference 10) evidence ledger

- **Lifecycle:** `active`
- **Evidence summary:** current plan/builder are `mechanically verified`; the
  complete v13 matrix and v16 correction set remain historical `visually
  reviewed` evidence for their unchanged geometry claims; the complete current
  v17 matrix is `visually reviewed` only for the four annotated stair, passage,
  shaft-support, and threshold claims; the v2 lighting set remains `visually
  reviewed` only for locked day/night line hierarchy and readability; the
  whole-site match is not `author-accepted`
- **Scope:** `site-specific` to `bloom-grove-court` / `reference-10.png`
- **Last verified:** 2026-08-30 mechanically and across the complete current v17
  locked/top, day/night, four-distance, and four-rotation capture matrix
- **Supersedes:** none; rejected readings are retained below
- **Superseded by:** none
- **Owning sources:**
  [`reference-10.png`](../../world-new/reference-10.png),
  [`reference-10-top.png`](../../world-new/reference-10-top.png),
  [`bloom-grove-court-reference-10.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10.json),
  [`bloom-grove-court-reference-10-plan.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10-plan.json),
  [`Reference10GroveCourt.cs`](../../src/World/Sites/Reference10GroveCourt.cs)

## Authority and calibration

`reference-10.png` is the primary acceptance source. It owns the locked
isometric silhouette, player-relative scale, vertical hierarchy, openings,
damage, visible materials, lighting, and vegetation silhouettes.
`reference-10-top.png` is a strong supporting plan guide; it owns neither final
vertical geometry nor final acceptance.

The current site contract is one plan cell per voxel with one explicit runtime
X reflection. The visible traveller is two blocks tall. The locked comparison
camera is yaw 135 degrees, true-isometric pitch 35.26439 degrees, distance 190,
at the source resolution 1672x941. See
[reference measurement and coordinate calibration](../workflows/reference-measurement-and-coordinate-calibration.md)
before changing any of these values.

## Evidence

| Claim | State | Evidence | Remaining uncertainty / next evidence |
|---|---|---|---|
| Plan scale, mirror, IDs, visible terrain ownership, stair endpoints, support, connected components, exact rubble cells, and builder projection ownership pass current checks; the plan has 28 terrain records, 24 surface-patch groups covering 1,893 cells, 31 structure records including two stairs and 10 rubble clusters covering 119 exact cells, and 18 trees | `mechanically verified` | Current plan counts plus passing source/runtime plan audits, world audit, and `dotnet build --no-restore`, 2026-08-30 | These checks do not establish image similarity |
| The earlier local terrain stacks read as parts rather than jagged rings | `visually reviewed` | `/home/shikhar/godot/shots/reference-10-plan-v11-full/`, inspected 2026-08-30 | Historical only: v11 predates the current east-upper three-slab and pillar topology |
| The current east upper region is three separate same-height slabs with lower-terrain channels; supported wall/rubble/paving records are split with them | `mechanically verified` | Current plan/builder and source-facing preview audit, 2026-08-30 | Mechanical checks do not establish its visual result |
| The central slab boundary is restored to its pre-widening outline; its occupied ruin reaches only source x=40 through a broken low L return and two interior low remnants, while the two lower channels remain at z=-10..-9 and z=17..20 | `mechanically verified` | Current plan source/runtime previews, exact plan counts, plan/world audit, builder parity, build, and diff check, 2026-08-30 | Mechanical checks do not establish its screen-space read |
| The intended central-right occupied spread reads without overextended terrain; the broken low L return and two interior remnants remain discrete; both channels stay open without a bridge or floating back; and the rightward extent stays modest at far range | `visually reviewed` | All 19 raw captures and four derived comparisons in `/home/shikhar/godot/shots/reference-10-plan-v13-full/`, inspected 2026-08-30 | Historical evidence for unchanged terrain/occupancy claims; v13 predates the current pillar, passage, and threshold correction |
| Reference 10's current upright pillar family is consistently 2x2 on connected foundations; one-cell-wide remnants in the precinct are walls, shoulders, or stelae rather than mixed-width pillars | `observed/source-measured` | `reference-10.png`, `reference-10-top.png`, and the author's explicit 2026-08-30 correction | Some rear/occluded mass identities remain ambiguous |
| The four-point annotated correction removes the graded east stair-side shoulder/cap/rubble family; splits the south-west wall around an empty passage at x=-11..-10, z=-9..-6; starts all four central 2x2 shafts at y116 continuously over the y114 base and y115 stylobate course; and adds a y107 southern approach whose y107/y108/y109 tread surfaces make two rises into the y109 lower court | `mechanically verified` | Current plan/builder; passing source/runtime plan audits, world audit, and `dotnet build --no-restore`, 2026-08-30 | The audits prove topology, support declarations, and buildability, not the screen-space result |
| At the locked source angle and true top, the east stair bumps are absent, the south-west wall pass is open, the four 2x2 shafts meet their connected foundation without the former air slice, and the southern threshold reads as two low rises; all four play-distance rotations preserve those corrections | `visually reviewed` | Historical pre-lighting-change evidence: `/home/shikhar/godot/shots/reference-10-plan-v16-annotated/reference_match_day.png`, `reference_top_day.png`, and `site_play_r0.png` through `site_play_r3.png`, inspected 2026-08-30 | The later shared ink/lighting change requires a fresh top/play rotation set before making this a current-render claim |
| Under the current v17 render, the main stair remains free of the rejected side bumps, the south-west passage remains a real opening, the four square 2x2 shafts visibly grow from their connected foundation courses, and the southern threshold remains a two-rise stair at every reviewed distance and quarter-turn | `visually reviewed` | All 19 raw files and four derived comparisons in `/home/shikhar/godot/shots/reference-10-callouts-current-v17/`, inspected 2026-08-30: locked day/night, calibrated top, close/play/wide/far at r0-r3, and source/top overlays plus edge differences | This review establishes only the four annotated geometry claims; it does not establish whole-site fidelity, collision/playability, or author acceptance |
| The current shared renderer keeps the locked-day internal edge rhythm quieter than silhouettes and retains the court, stair, tree, and traveller hierarchy under the cooler readable night key | `visually reviewed` | `/home/shikhar/godot/shots/reference-10-ink-parity-v2/reference_match_day.png` and `reference_match_night.png`, inspected 2026-08-30 | This earlier pair establishes only its named lighting claim; the later complete v17 matrix was reviewed only for the four annotated geometry claims, not whole-site parity or author acceptance |
| Current site matches Reference 10 one-to-one | `candidate` | Required evidence is missing | Needs material/light and remaining geometry refinement, playable collision review, and explicit author acceptance |

Do not copy routine transient cell coordinates into this ledger. The exact
passage and vertical levels above are retained because they are the author's
explicit correction invariants; the JSON remains the complete coordinate source
of truth. This file records constraints, evidence, and failures.

## Procedure and known-good constraints

- Use the [plan-first transcription](../workflows/plan-first-voxel-transcription.md):
  the JSON owns every X/Z cell; the builder owns unique vertical blocks,
  openings, and damage.
- Every visible architectural mass is site-owned. A range fill is serialization
  shorthand, never a reusable column, portal, stair, or ruin generator.
- The current named pillar family uses constant 2x2 shafts on connected
  foundations. One-cell walls, stair shoulders, and stelae remain separate
  structure classes rather than narrower pillars. See
  [square shafts](../structures/block-by-block-structures-and-square-shafts.md).
- The west upper silhouette contains an arch/facade with a measured opening; it
  is not a forward-curving pillar.
- Terrain breakup is local: three or four blocky layers in parts, detached
  fragments, and ordinary atlas terrain between them. It is not a nested ring
  field. See [terrain integration](../terrain/terrain-and-detached-slab-integration.md).
- The current east upper interpretation uses three detached y114 slabs. Its
  lower-terrain channels occupy source-facing z=-10..-9 and z=17..20;
  architecture and surface records must not bridge or float across them.
- Terrain reach and occupied ruin reach are separate measurements. The central
  slab already had the required extent. Its occupied ruin reaches only source
  x=40 through a broken low L return and two interior low remnants; do not widen
  the slab or turn those fragments into a continuous wall or pad.
- The main stair has no graded east-side shoulder, cap, or rubble family. Those
  marked bumps were an invented accompaniment rather than part of the stair.
- The south-west foundation wall is two named connected masses. Its plan gap at
  x=-11..-10, z=-9..-6 is a real lateral passage, not AIR carved through an
  otherwise continuous projection.
- Each central 2x2 pillar starts at y116 immediately above the y114 base and y115
  stylobate course. No unsupported y116 air slice may separate shaft and base.
- The southern threshold begins at a local y107 authored approach. Its walkable
  surfaces at y107, y108, and y109 make exactly two visible rises into the y109
  lower court; three tread surfaces do not mean three rises.
- Broad paving/material breakup is explicit and deterministic; the shared rock
  shader supplies fine near-range weathering only. See
  [material breakup](../surfaces/material-and-weathering-breakup.md).
- The locked 135-degree isometric is the comparison angle. Other rotations test
  hidden completeness; they cannot replace the source-matching angle.

## Known failures and rejected interpretations

| Interpretation | Status | Why it failed | Replacement |
|---|---|---|---|
| Generic ruin-kit towers, pillars, stairs, and portals | `rejected/superseded` | Repeated parts and proportions produced a random kit yard rather than the source | Unique site-owned voxel masses |
| Dense forest of thick, variable, or mixed 2x2/1x1 pillars | `rejected/superseded` | Wrong family consistency, rhythm, and quarter-turn read; the one-cell strips were different mass classes | Constant 2x2 named pillars on connected foundations; one-cell walls, shoulders, and stelae stay separately classified |
| Four central shafts beginning at y117 above courses y114-115 | `rejected/superseded` | Left an actual one-voxel air slice at y116 and made the uprights read as hovering slabs | Begin every 2x2 shaft at y116 while retaining its measured top |
| Graded east stair shoulder with upper caps and foot rubble | `rejected/superseded` | Read as the author's marked extra bumps along the stair | Remove that complete east-side bump family; keep the authored main treads |
| One continuous south-west wall with a cosmetic runtime cut | `rejected/superseded` | A runtime carve would disagree with the canonical plan and preserve one false connected mass | Split the wall into two audited masses around the exact passage |
| Threshold from y108 directly into the y109 court | `rejected/superseded` | Produced only one visible rise in the marked entrance area | Add the y107 approach and two successive rises to y109 |
| West-upper arch read as a curved/forward pillar | `rejected/superseded` | Contradicted the source opening and connected facade | Trace arch projection/opening first, then vertical damage |
| Site-wide jagged or concentric terrain rings | `rejected/superseded` | Looked like contour bands instead of separated Minecraft-like block layers | Local three/four-tier stacks with ordinary land between |
| One neat monolithic platform around the whole ruin | `rejected/superseded` | Made architecture look stamped onto terrain and erased source gaps | Explicit courts, local shelves, detached slabs/channels |
| Widening the central slab because its occupied ruin looked too narrow | `rejected/superseded` | Enlarged an already-empty pad and overshot the measured terrain silhouette | Restore the prior slab polygon; extend sparse low masonry and paving only |
| Fractional plan scale, rounding, and group offsets | `rejected/superseded` | Drift/collapsed cells made overlay correction ambiguous | One source cell to one voxel plus one declared mirror |
| Comparison from a convenient different isometric quadrant | `rejected/superseded` | Reversed screen relationships and hid occlusion errors | Locked yaw 135 / pitch 35.26439 source view |

## Checks

Mechanically regenerate both plan previews and the source overlay, then run the
world audit, build, and diff check. Visually inspect the locked and overhead
captures first; only then run the full matrix. The exact commands and what each
can prove live in
[plan-first transcription](../workflows/plan-first-voxel-transcription.md) and
[capture/acceptance](../rendering/capture-overlay-and-acceptance.md). Godot GUI
commands must use silent Hyprland workspace 5. Normal playable startup is a
separate check because capture review mode is nonplayable.

## Scope and limits

The present east-slab split, central occupancy correction, constant 2x2 pillar
family, stair-side subtraction, wall passage, continuous stylobate support, and
two-rise southern threshold have passed plan/world/build checks. The complete
v13 matrix remains evidence only for the unchanged central occupied footprint,
channels, reverse support, and far extent. The six inspected v16 raw views prove
the four annotated corrections only at the locked, top, and play-distance
quarter-turn views under the prior shared lighting. The complete current v17
matrix re-establishes those same four claims at the locked day/night and top
views and at close/play/wide/far distance for all four rotations; it does not
promote any other site claim. The v2 locked day/night captures establish only
line hierarchy and night readability. These claim-scoped reviews do not prove
collision/playability, whole-site fidelity, or author acceptance. The
overall site still needs stronger evidence for exact terrain silhouette,
architecture count/placement, arch proportions, material texture, lighting,
tree silhouettes, rubble, all hidden faces, and collision/playability. No
explicit whole-site author acceptance exists.

## Update triggers and required next evidence

1. Regenerate the source-facing, runtime-facing, and source-pixel overlay plan
   artifacts after any further plan edit.
2. Preserve `/home/shikhar/godot/shots/reference-10-plan-v16-annotated/` as
   historical claim-scoped locked/top/play-rotation evidence. Preserve
   `/home/shikhar/godot/shots/reference-10-ink-parity-v2/` as the narrower
   locked-day/night lighting review. The complete current-render evidence for
   the four annotated geometry claims is
   `/home/shikhar/godot/shots/reference-10-callouts-current-v17/`; recapture the
   affected matrix after any later geometry, camera, material, shader, or
   lighting change.
3. Preserve `/home/shikhar/godot/shots/reference-10-plan-v13-full/` as historical
   full-matrix evidence for the unchanged central footprint and channels. Do not
   use the newer v17 matrix to promote close/wide/far claims beyond the four
   explicitly reviewed annotated corrections; broader claims require their own
   named inspection criteria using
   [the capture workflow](../rendering/capture-overlay-and-acceptance.md).
4. Test normal playable startup separately; capture review mode is nonplayable.
5. Update the evidence table above with the exact new shot directory and visible
   findings. Move only the demonstrated claims to `visually reviewed`.
6. Record `author-accepted` only if the author explicitly accepts the named
   revision and scope. Later changes invalidate acceptance for affected claims.

Any author correction to topology, pillar identity/section, terrain layers,
material, lighting, camera, or required views must be recorded here and in the
relevant generalized technique entry in the same work session.
