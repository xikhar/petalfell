# Block-by-block structures and square shafts

- **Lifecycle:** `active`
- **Evidence summary:** `observed/source-measured`, `mechanically verified`, and
  `visually reviewed`; the overall Reference 10 reconstruction is not
  `author-accepted`
- **Scope:** `site-specific` to Reference 10, with the no-generic-kit rule
  `general` for supplied-reference production sites
- **Last verified:** 2026-08-30 mechanically and in the v16 locked, top, and
  four play-distance rotation captures
- **Supersedes:** reusable ruin-kit stamping, random survivor placement, and
  variable rectangular pillar shafts
- **Superseded by:** none
- **Owning sources:**
  [`Reference10GroveCourt.cs`](../../src/World/Sites/Reference10GroveCourt.cs),
  [`bloom-grove-court-reference-10-plan.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10-plan.json),
  [`docs/RUINS.md`](../../docs/RUINS.md)

## Outcome

Each source-visible wall, arch, stair shoulder, survivor, and rubble group is a
named, uniquely authored voxel mass. Reference 10's current measured pillar
family uses constant 2x2 shafts carried continuously by connected foundations
or stylobates. A one-cell-wide wall, stair shoulder, or stele fragment remains
valid when the source identifies it as that mass; it must not be relabelled a
pillar merely because it is tall. A connected wall/facade may be rectangular;
it must not be misread as a forest of separate pillars.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| Reference 10's upright pillar family is a consistent 2x2 square family on connected foundations; one-cell strips in the same precinct are walls, shoulders, or stelae rather than mixed-width pillars | `observed/source-measured` | site-specific | `reference-10.png`, `reference-10-top.png`, and the author's explicit 2026-08-30 correction on the mixed pillar reading | Some occluded rear members remain ambiguous |
| The four central 2x2 shafts occupy their declared projection and start at y116 continuously above the y114 base and y115 stylobate course; named builders remain bounded by and cover their plan projections | `mechanically verified` | site-specific and tool-specific | Current plan/builder; passing source/runtime plan audits, world audit, and `dotnet build --no-restore`, 2026-08-30 | The plan audit does not itself test every vertical support course |
| The four equal-section shafts read as foundation-born masses without the former floating-slab gap, and the rejected graded east stair-side bump family is absent at the corrected side | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-10-plan-v16-annotated/reference_match_day.png`, `reference_top_day.png`, and `site_play_r0.png` through `site_play_r3.png`, inspected 2026-08-30 | Close/wide/far distances, complete composition, material match, and author acceptance remain open |

## Procedure

1. Identify a structure from the source as one connected mass: facade, wall
   run, shaft, stair shoulder, stele, or rubble group. Do not decompose a wall
   into arbitrary pillars or merge separated survivors.
2. Trace its exact plan projection and support terrain before writing height.
3. Give the structure its own builder method. Similar neighbours still receive
   separate coordinates, heights, broken tops, missing blocks, and material
   accents when the source differs.
4. For a shaft, select the source-supported square family and keep it constant
   through the vertical run. Reference 10 uses 2x2 for its current named pillar
   family; another reference must be measured independently. Widening bases and
   caps are separate courses, not a changing shaft.
5. Do not use a 1x2 or 2x1 vertical as a pillar. If the source shows a thin
   connected wall, shoulder, or stele, classify it as that structure. If it
   shows two adjacent square survivors, author and damage them as two members.
6. Build arches as the visible connected facade and its measured opening. Match
   opening width/height, jamb positions, crown profile, and damage; never curve a
   forward pillar to fake an arch seen elsewhere in the image.
7. Author stairs as their exact tread rectangles and top levels. Add a shoulder
   only where the source shows one; a generated graded cheek can read as a row
   of unrelated bumps even when it follows the stair mathematically.
8. Author rubble as a small, source-placed connected cluster with exact cells
   and heights. Do not scatter it through an envelope.
9. Add pale/cool/warm/moss blocks only where they support measured breakup;
   material variation may not alter the mass or disguise a wrong silhouette.

## Checks

### Mechanical

Run the plan and world audits and build. The runtime projection guards should fail if a
named structure writes outside its footprint or leaves any declared projection
cell untouched. Inspect the plan for accidental 1x2/2x1 objects that are called
isolated survivors. The current plan tooling does not prove that every vertical
shaft course touches the foundation below it, so inspect the site-owned vertical
ranges or add a targeted invariant before relying on the render.

### Visual

At the locked source angle, compare count, spacing, thickness, height, arch
opening, and occlusion—not just overall whiteness. At r1/r2/r3, look for hollow
backs, floating blocks, variable shaft widths, and masses that only work from
the hero view. Close/play views own player-relative thickness; wide/far views
own rhythm and silhouette.

## Scope and limits

Reference 10's constant 2x2 pillar family is site-specific, not a universal
column size. Its one-cell wall, shoulder, and stele fragments are classifications
of different connected masses, not exceptions that permit mixed pillar widths.
Every new source must be measured. `Fill` remains acceptable as storage shorthand
inside one explicitly traced mass; reusable `Column`, `Portal`, `Stair`, or
ruin-layout generators are not production authoring surfaces.

## Known failures

- Generic ruin-kit pieces produced repeated capitals, stairs, and portals that
  read as a kit yard rather than the reference. Production sites now own every
  visible mass.
- Too many thick, randomly spaced pillars destroyed the source rhythm. The
  replacement is sparse named placement and source-count comparison.
- The earlier Reference 10 interpretation mixed one broad 2x2 shaft with several
  1x1 needles. The author identified those needles as an inconsistent pillar
  family; the replacement keeps the named pillars 2x2 and reserves one-cell
  thickness for connected walls, shoulders, and stelae.
- Variable rectangular shafts looked tapered or slab-like from quarter turns.
  The replacement is a constant square shaft plus separately measured base/cap.
- Starting the central shafts at y117 above foundation courses y114 and y115
  left an actual air slice at y116. Starting each 2x2 shaft at y116 makes its
  support continuous without changing its plan section or surviving top.
- A generated graded shoulder and its caps/rubble produced the author's marked
  bumps along the east side of the main stair. Those site records were removed;
  the stair remains authored directly from its tread rectangles.
- Cutting AIR through one connected wall builder would leave the canonical plan
  falsely unbroken. The south-west pass instead splits the wall into two named,
  independently audited connected masses with an empty plan gap between them.
- One west-upper shape was interpreted as a curved/forward pillar although the
  source showed an arch. Classify connected silhouette and opening first.

## Update triggers

Update when the author corrects a structure identity, count, position,
cross-section, opening, damage, or support; when projection enforcement changes;
or when a new reference demonstrates a different shaft family. Recapture all
four rotations after any hidden-face or cross-section edit.
