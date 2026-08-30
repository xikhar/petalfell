# Bloom Grove Court (Reference 10) evidence ledger

- **Lifecycle:** `active`
- **Evidence summary:** current plan/builder are `mechanically verified`; the
  complete v13 matrix is `visually reviewed` for the corrected central occupied
  footprint, unchanged east-slab channels, supported reverse faces, modest far
  extent, and unchanged square shafts; the whole-site match is not
  `author-accepted`
- **Scope:** `site-specific` to `bloom-grove-court` / `reference-10.png`
- **Last verified:** 2026-08-30 mechanically and across all 19 v13 raw captures
  plus four derived comparisons
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
| Plan scale, mirror, IDs, visible terrain ownership, stair endpoints, support, connected components, exact rubble cells, and builder projection ownership pass current checks; the plan has 27 terrain records, 24 surface-patch groups covering 1,893 cells, 34 structure records including 11 rubble clusters, and 18 trees | `mechanically verified` | Current plan counts plus `preview-site-plan`, world audit, `dotnet build --no-restore`, and `git diff --check` after the 2026-08-30 central occupied-footprint correction | These checks do not establish image similarity |
| The earlier local terrain stacks read as parts rather than jagged rings; square shafts held their section through all four rotations | `visually reviewed` | `/home/shikhar/godot/shots/reference-10-plan-v11-full/`, inspected 2026-08-30 | Historical only: v11 predates the current east-upper three-slab topology |
| The current east upper region is three separate same-height slabs with lower-terrain channels; supported wall/rubble/paving records are split with them | `mechanically verified` | Current plan/builder and source-facing preview audit, 2026-08-30 | Mechanical checks do not establish its visual result |
| The central slab boundary is restored to its pre-widening outline; its occupied ruin reaches only source x=40 through a broken low L return and two interior low remnants, while the two lower channels remain at z=-10..-9 and z=17..20 | `mechanically verified` | Current plan source/runtime previews, exact plan counts, plan/world audit, builder parity, build, and diff check, 2026-08-30 | Mechanical checks do not establish its screen-space read |
| The intended central-right occupied spread reads without overextended terrain; the broken low L return and two interior remnants remain discrete; both channels stay open without a bridge or floating back; the rightward extent stays modest at far range; square shafts do not regress | `visually reviewed` | All 19 raw captures and four derived comparisons in `/home/shikhar/godot/shots/reference-10-plan-v13-full/`, inspected 2026-08-30 | This is claim-scoped topology evidence; whole-site fidelity, collision/playability, and author acceptance remain open |
| Current site matches Reference 10 one-to-one | `candidate` | Required evidence is missing | Needs material/light and remaining geometry refinement, playable collision review, and explicit author acceptance |

Do not copy transient cell coordinates into this ledger. The JSON is the
coordinate source of truth; this file records constraints, evidence, and
failures.

## Procedure and known-good constraints

- Use the [plan-first transcription](../workflows/plan-first-voxel-transcription.md):
  the JSON owns every X/Z cell; the builder owns unique vertical blocks,
  openings, and damage.
- Every visible architectural mass is site-owned. A range fill is serialization
  shorthand, never a reusable column, portal, stair, or ruin generator.
- Pillar shafts are sparse, constant square sections. Most are 1x1; a 2x2 shaft
  requires direct source evidence. See
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
- Broad paving/material breakup is explicit and deterministic; the shared rock
  shader supplies fine near-range weathering only. See
  [material breakup](../surfaces/material-and-weathering-breakup.md).
- The locked 135-degree isometric is the comparison angle. Other rotations test
  hidden completeness; they cannot replace the source-matching angle.

## Known failures and rejected interpretations

| Interpretation | Status | Why it failed | Replacement |
|---|---|---|---|
| Generic ruin-kit towers, pillars, stairs, and portals | `rejected/superseded` | Repeated parts and proportions produced a random kit yard rather than the source | Unique site-owned voxel masses |
| Dense forest of thick or variable 1x2/2x1 pillars | `rejected/superseded` | Wrong count, rhythm, thickness, and quarter-turn read | Sparse named 1x1 shafts; measured 2x2 only |
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

The present east-slab split and central occupancy correction have passed
plan/world/build checks. The complete v13 capture matrix demonstrates the
corrected right-side spread, unchanged channels, restored terrain boundary,
supported reverse faces, modest far extent, and unchanged square shafts. That
claim-scoped review does not prove collision/playability, whole-site fidelity,
or author acceptance. The overall site still needs stronger evidence for exact
terrain silhouette,
architecture count/placement, arch proportions, material texture, lighting,
tree silhouettes, rubble, all hidden faces, and collision/playability. No
explicit whole-site author acceptance exists.

## Update triggers and required next evidence

1. Regenerate the source-facing, runtime-facing, and source-pixel overlay plan
   artifacts after any further plan edit.
2. Preserve the current v13 locked/top evidence path above; recapture it after
   any geometry, camera, material, shader, or lighting change.
3. Preserve `/home/shikhar/godot/shots/reference-10-plan-v13-full/` as the current
   19-raw-plus-four-derived baseline. Regenerate and inspect every affected view
   after a geometry, camera, material, shader, or lighting change, using
   [the capture workflow](../rendering/capture-overlay-and-acceptance.md).
4. Test normal playable startup separately; capture review mode is nonplayable.
5. Update the evidence table above with the exact new shot directory and visible
   findings. Move only the demonstrated claims to `visually reviewed`.
6. Record `author-accepted` only if the author explicitly accepts the named
   revision and scope. Later changes invalidate acceptance for affected claims.

Any author correction to topology, pillar identity/section, terrain layers,
material, lighting, camera, or required views must be recorded here and in the
relevant generalized technique entry in the same work session.
