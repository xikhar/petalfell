# Fallen Colossus (Reference 12) evidence ledger

- **Lifecycle:** `active`
- **Evidence summary:** source relations are `observed/source-measured`; the
  permanent site, twelve-structure plan, Meshy normalization, stone-court
  blueprint and runtime attachment are `mechanically verified`; the current v21
  locked image is `visually reviewed` for scale, Petalfell material, silhouette
  ink and three-course blocking; the site is not `author-accepted`
- **Scope:** `site-specific` to `fallen-colossus` / `reference-12.png`
- **Last verified:** 2026-08-31
- **Supersedes:** the v6/v7 provisional hand-built sculpture; rejected attempts
  remain below
- **Superseded by:** none
- **Owning sources:**
  [`reference-12.png`](../../world-new/reference-12.png),
  [`fallen-colossus-reference-12.json`](../../content/chapter_01/sites/fallen-colossus-reference-12.json),
  [`fallen-colossus-reference-12-plan.json`](../../content/chapter_01/sites/fallen-colossus-reference-12-plan.json),
  [`Reference12FallenColossus.cs`](../../src/World/Sites/Reference12FallenColossus.cs),
  [`Reference12SculptureDetail.cs`](../../src/World/Sites/Reference12SculptureDetail.cs)

## Source constraints

The source owns a four-course stepped pedestal, two separated trunkless
colossus legs, a much larger-than-player crowned head fallen to the lower-right,
the player to the lower-left, a collapsed west beam, sparse survivors/rubble,
and blossom trees in an open meadow. There is no torso bridge. The head is a
tilted carved object, not an upright square building, and each leg reads through
foot, ankle, calf, knee and severed thigh rather than random protruding boxes.

## Evidence

| Claim | State | Evidence | Remaining uncertainty |
|---|---|---|---|
| The site is permanently registered at `(10600,4600)`, status-gated into the map-guided production runtime and rebuilt during window handoff | `mechanically verified` | topology/source paths, passing world audit/build, and v21 production log, 2026-08-31 | Live player-controlled handoff has not been author-reviewed |
| The current plan owns one preserved atlas context, a flattened clearing, two broken stone courts, a four-block central three-course plinth, five detached two-course foundations, eight fixed 2×2 pillars and four sculpture projections | `mechanically verified` | strict plan preview reports twelve structures; v21 reports 5,194 surface writes and 416 pillar voxels | Audit does not prove image similarity |
| The Meshy head and legs replace baked colour with Petalfell stone, retain monument scale and carry a reduced 0.009-unit plum silhouette outline; the legs and matching collision face straight forward at yaw 0 | `visually reviewed` | `/home/shikhar/godot/shots/reference-12-v21-meshy/reference_match_day.png`, inspected at original size 2026-08-31 | Exact source framing, night response and other rotations remain open |
| The imported head/legs are isolated from the third Meshy debris asset; surrounding architecture now comes only from the site-owned voxel plan | `mechanically verified` | only two GLB paths are loaded by `Reference12SculptureDetail`; v21 capture inspected | Further foundations must remain plan-authored rather than reusing the debris model |
| The current site matches Reference 12 one-to-one | `candidate` | Required evidence is missing | Needs geometry/material refinement, all rotations, collision/play review and explicit author acceptance |

## Procedure and current limits

- Keep the permanent terrain window and open clearing. The current court is a
  deliberate broad three-course voxel blockout with detached foundations; do
  not put the discarded Meshy floor or debris back beneath it.
- Use [hybrid sculpture geometry](../structures/hybrid-voxel-and-fine-sculpture-geometry.md)
  for normalization, material replacement, ink and collision.
- The locked camera is source-facing yaw 0, true-isometric pitch 35.26439 and
  distance 158 at 1672x941. Other rotations test completeness, not source match.
- Current compound collision is conservative and invisible; live walking review
  remains required around crown teeth, cheek rubble and the gap between legs.

## Rejected attempts

| Attempt | Status | Failure | Replacement |
|---|---|---|---|
| Giant axis-aligned fill layers for the head and crown | `rejected/superseded` | Read as a fort/building; could not express the fallen plane or face | Author-supplied Meshy head with invisible collision |
| Broad protruding knee blocks | `rejected/superseded` | Made the legs robotic towers | Author-supplied Meshy leg subject |
| Provisional hand-authored fractional-cuboid head and legs | `rejected/superseded` | Better than giant cubes but still materially and anatomically behind the supplied Meshy geometry | Two independently normalized author-supplied GLBs |
| Meshy baked textures, ground slabs and surrounding debris asset | `rejected/superseded` | Imported lighting/material identity and unrelated geometry instead of belonging to the site | Strip materials/floor in Blender, omit debris, apply Petalfell stone and site-owned court |
| Camera yaw 135 with a plan measured in source screen axes | `rejected/superseded` | Reversed/cancelled player/head screen relations and cropped the site | Source-facing yaw 0 with the player/head on their measured sides |
| Ordinary dense forest immediately around the monument | `rejected/superseded` | Buried the source composition | Larger permanent site exclusion plus nine explicit trees |

## Update triggers

Update immediately after geometry/material/camera changes, compound fine
collision, a complete rotation/distance capture set, or any author correction
or acceptance decision.
