# Block-by-block structures and square shafts

- **Lifecycle:** `active`
- **Evidence summary:** `observed/source-measured`, `mechanically verified`, and
  `visually reviewed`; the overall Reference 10 reconstruction is not
  `author-accepted`
- **Scope:** `site-specific` to Reference 10, with the no-generic-kit rule
  `general` for supplied-reference production sites
- **Last verified:** 2026-08-30 in the v13 four-scale/four-rotation matrix
- **Supersedes:** reusable ruin-kit stamping, random survivor placement, and
  variable rectangular pillar shafts
- **Superseded by:** none
- **Owning sources:**
  [`Reference10GroveCourt.cs`](../../src/World/Sites/Reference10GroveCourt.cs),
  [`bloom-grove-court-reference-10-plan.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10-plan.json),
  [`docs/RUINS.md`](../../docs/RUINS.md)

## Outcome

Each source-visible wall, arch, stair shoulder, survivor, and rubble group is a
named, uniquely authored voxel mass. A pillar's shaft keeps one square cross
section for its full surviving shaft: normally 1x1, or 2x2 only where the source
shows a broad survivor. A connected wall/facade may be rectangular; it must not
be misread as a forest of thick pillars.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| The source uses sparse, mostly thin square survivors, with broad members only at specific connected masses | `observed/source-measured` | site-specific | `reference-10.png` and `reference-10-top.png`; explicit author correction on pillar thickness/cross-section | Some occluded rear members remain ambiguous |
| Named builder methods are bounded by their plan projections and must touch every declared cell | `mechanically verified` | tool-specific | `WriteNamed`, `Put`, and projection-course guards in `Reference10GroveCourt.cs`; successful v11 build/capture | Does not check cross-section above every cell automatically |
| Constant square shafts remain legible without regression across all four rotations at close/play/wide/far distances | `visually reviewed` | site-specific | 16 `site_{close,play,wide,far}_r{0..3}.png` images in `/home/shikhar/godot/shots/reference-10-plan-v13-full/` inspected 2026-08-30 | Composition, exact counts, material match, and author acceptance remain open |

## Procedure

1. Identify a structure from the source as one connected mass: facade, wall
   run, shaft, stair shoulder, stele, or rubble group. Do not decompose a wall
   into arbitrary pillars or merge separated survivors.
2. Trace its exact plan projection and support terrain before writing height.
3. Give the structure its own builder method. Similar neighbours still receive
   separate coordinates, heights, broken tops, missing blocks, and material
   accents when the source differs.
4. For a shaft, select a source-supported square section and keep it constant
   through the vertical run. Use 1x1 by default; use 2x2 only when measured.
   Widening bases/caps are separate courses, not a changing shaft.
5. Do not use a 1x2 or 2x1 vertical as a pillar. If the source shows two adjacent
   square survivors, author and damage them as two members. If it is continuous,
   classify it as a wall/facade instead.
6. Build arches as the visible connected facade and its measured opening. Match
   opening width/height, jamb positions, crown profile, and damage; never curve a
   forward pillar to fake an arch seen elsewhere in the image.
7. Author stairs as their exact tread rectangles and top levels. Author
   shoulders separately so stair width and route remain readable.
8. Author rubble as a small, source-placed connected cluster with exact cells
   and heights. Do not scatter it through an envelope.
9. Add pale/cool/warm/moss blocks only where they support measured breakup;
   material variation may not alter the mass or disguise a wrong silhouette.

## Checks

### Mechanical

Run the plan audit and build. The runtime projection guards should fail if a
named structure writes outside its footprint or leaves any declared projection
cell untouched. Inspect the plan for accidental 1x2/2x1 objects that are called
isolated survivors; current tooling does not fully prove shaft section through
height.

### Visual

At the locked source angle, compare count, spacing, thickness, height, arch
opening, and occlusion—not just overall whiteness. At r1/r2/r3, look for hollow
backs, floating blocks, variable shaft widths, and masses that only work from
the hero view. Close/play views own player-relative thickness; wide/far views
own rhythm and silhouette.

## Scope and limits

The 1x1/2x2 rule records the measured Reference 10 family of survivors, not a
universal ban on wider columns in other references. Every new source must be
measured. `Fill` remains acceptable as storage shorthand inside one explicitly
traced mass; reusable `Column`, `Portal`, `Stair`, or ruin-layout generators are
not production authoring surfaces.

## Known failures

- Generic ruin-kit pieces produced repeated capitals, stairs, and portals that
  read as a kit yard rather than the reference. Production sites now own every
  visible mass.
- Too many thick, randomly spaced pillars destroyed the source rhythm. The
  replacement is sparse named placement and source-count comparison.
- Variable rectangular shafts looked tapered or slab-like from quarter turns.
  The replacement is a constant square shaft plus separately measured base/cap.
- One west-upper shape was interpreted as a curved/forward pillar although the
  source showed an arch. Classify connected silhouette and opening first.

## Update triggers

Update when the author corrects a structure identity, count, position,
cross-section, opening, damage, or support; when projection enforcement changes;
or when a new reference demonstrates a different shaft family. Recapture all
four rotations after any hidden-face or cross-section edit.
