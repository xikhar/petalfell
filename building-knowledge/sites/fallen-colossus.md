# Fallen Colossus (Reference 12) evidence ledger

- **Lifecycle:** `active`
- **Evidence summary:** source relations are `observed/source-measured`; the
  permanent site, twenty-six-structure plan, Meshy normalization, broad worn
  precinct and runtime attachment are `mechanically verified`; the current v25
  locked and far images are `visually reviewed` for 1.5× leg scale, Petalfell
  material, silhouette ink, enlarged plinth and partial three-level outer slab
  stacks; the site is not `author-accepted`
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
| The current plan owns one preserved atlas context, seventeen terrain records, sixteen exact wear groups, six stairs, four broken foundation traces, four rubble fields, eight fixed 2×2 pillars and four sculpture projections | `mechanically verified` | strict runtime-facing plan preview reports 26 structures; plan/world/build/diff checks pass on 2026-08-31 | Audit does not prove image similarity or traversal |
| The broad precinct uses substantial orthogonal steps and only partial third layers; ordinary atlas terrain remains visible between its central court and four detached outer slab stacks | `visually reviewed` | `/home/shikhar/godot/shots/reference-12-v25-legs-1_5x/site_far_r0.png`, inspected at original size 2026-08-31 | The composition remains cleaner and sparser than the reference and is not accepted |
| The Meshy head and legs replace baked colour with Petalfell stone and carry a 0.009-unit plum silhouette outline; the forward-facing legs use 1.5× their first imported review scale and matching 1.5× collision while the head remains unchanged | `visually reviewed` | `/home/shikhar/godot/shots/reference-12-v25-legs-1_5x/reference_match_day.png` and `site_far_r0.png`, inspected at original size 2026-08-31 | Night response, hidden rotations and live collision remain open |
| The imported head/legs are isolated from the third Meshy debris asset; surrounding architecture now comes only from the site-owned voxel plan | `mechanically verified` | only two GLB paths are loaded by `Reference12SculptureDetail`; v25 captures inspected | Further foundations must remain plan-authored rather than reusing the debris model |
| The open-meadow tree halo is applied by both the normal map-guided terrain path and the compiled-atlas reviewer without changing boulders | `visually reviewed` | normal-start `reference_match_day.png` and `site_far_r0.png` in `/home/shikhar/godot/shots/reference-12-v26-normal-sparse-trees/`, plus the equivalent compiled-site pair in `/home/shikhar/godot/shots/reference-12-v26-sparse-trees/`, inspected 2026-08-31 | Atlas-wide density outside the 140-block falloff was not recaptured |
| The current site matches Reference 12 one-to-one | `candidate` | Required evidence is missing | Needs geometry/material refinement, all rotations, collision/play review and explicit author acceptance |

## Procedure and current limits

- Keep the permanent terrain window and ordinary-atlas channels. The current
  court is a deliberately broad painter's stack with an enlarged central plinth,
  four detached outer lower/upper/crown stacks, explicit stairs and site-owned
  foundations/rubble; do not put the discarded Meshy floor or debris beneath it.
- Keep the source's open-meadow silhouette beyond the strict plan boundary with
  the registration-owned tree-only sparse halo: 22% density at the footprint,
  easing smoothly to ordinary Bloom density over 140 blocks. Do not enlarge the
  architectural footprint or reduce boulders/the whole biome to create this view.
- Use [hybrid sculpture geometry](../structures/hybrid-voxel-and-fine-sculpture-geometry.md)
  for normalization, material replacement, ink and collision.
- The locked camera is source-facing yaw 0, true-isometric pitch 35.26439 and
  distance 158 at 1672x941. Other rotations test completeness, not source match.
- Current compound collision is conservative and invisible; live walking review
  remains required around crown teeth, cheek rubble and the gap between legs.
- The first doubled-leg v24 capture is superseded by the author's 1.5× correction.
  Preserve the enlarged plinth from that experiment; it prevents the 1.5× feet
  from returning to the cramped v21 support.

## Rejected attempts

| Attempt | Status | Failure | Replacement |
|---|---|---|---|
| Giant axis-aligned fill layers for the head and crown | `rejected/superseded` | Read as a fort/building; could not express the fallen plane or face | Author-supplied Meshy head with invisible collision |
| Broad protruding knee blocks | `rejected/superseded` | Made the legs robotic towers | Author-supplied Meshy leg subject |
| Provisional hand-authored fractional-cuboid head and legs | `rejected/superseded` | Better than giant cubes but still materially and anatomically behind the supplied Meshy geometry | Two independently normalized author-supplied GLBs |
| Meshy baked textures, ground slabs and surrounding debris asset | `rejected/superseded` | Imported lighting/material identity and unrelated geometry instead of belonging to the site | Strip materials/floor in Blender, omit debris, apply Petalfell stone and site-owned court |
| Camera yaw 135 with a plan measured in source screen axes | `rejected/superseded` | Reversed/cancelled player/head screen relations and cropped the site | Source-facing yaw 0 with the player/head on their measured sides |
| Ordinary dense forest immediately around the monument | `rejected/superseded` | Buried the source composition | Open authored precinct and ordinary atlas vegetation beyond its exclusion |
| Uniform 2× leg scale | `rejected/superseded` | The author corrected the scale before acceptance; it dominated the court and head | 1.5× leg scale with 1.5× compound collision over the already enlarged plinth |

## Update triggers

Update immediately after geometry/material/camera changes, compound fine
collision, a complete rotation/distance capture set, or any author correction
or acceptance decision.
