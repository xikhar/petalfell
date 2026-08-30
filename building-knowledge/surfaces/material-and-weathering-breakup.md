# Material and weathering breakup

- **Lifecycle:** `active`
- **Evidence summary:** the material path is `mechanically verified`; Reference
  10's current placement is `visually reviewed` but not `author-accepted`
- **Scope:** existing voxel material path is `general`; exact palette placement
  is `site-specific`
- **Last verified:** 2026-08-30 in Reference 10 v11 day/night and distance
  captures
- **Supersedes:** flat single-tone stone and per-block random/checkerboard decay
- **Superseded by:** none
- **Owning sources:** [`Palette.cs`](../../src/Core/Palette.cs),
  [`voxel.gdshader`](../../shaders/voxel.gdshader),
  [`Reference10GroveCourt.cs`](../../src/World/Sites/Reference10GroveCourt.cs),
  [`bloom-grove-court-reference-10-plan.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10-plan.json)

## Outcome

Reference-like stone breakup is two-scale. Explicit authored cells and courses
carry the broad pale/cool/warm/moss pattern that must survive far zoom; the
existing world-space `PatternRock` shader adds restrained fine weathering at
near range. The shader never chooses the layout, damage, moss islands, or
architectural courses.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| `STONE`, `STONE_PALE`, `STONE_WARM`, `PAVING`, rubble, and `MOSS_STONE` use the existing rock-pattern material path | `mechanically verified` | general/tool-specific | Palette definitions and `voxel.gdshader`; Reference 10 material mapping in `WriteAuthoredSurfaceWear` | Does not establish that current colours exactly match the source grade |
| Fine pattern is world-space and fades from 40 to 130 world units, so far-read breakup must be authored at block scale | `mechanically verified` | general/tool-specific | `voxel.gdshader` pattern projection and fade uniforms | Runtime grade/fog/light can alter perceived contrast |
| Explicit paving islands and vertical stone accents remain visible across Reference 10 review distances | `visually reviewed` | site-specific | v11 close/play/wide/far day matrix and `reference_match_night.png` inspected 2026-08-30 | Current result is cleaner and less nuanced than the source; not accepted |

## Procedure

1. Separate macro placement from micro shading. Trace broad paving wear, warm
   stains, cool stones, and moss islands into exact plan cells or explicit
   structure courses.
2. Use `STONE_PALE` as the principal ruin fabric only where the source supports
   it. Add `STONE`, `STONE_WARM`, `PAVING`, and `MOSS_STONE` as coherent patches,
   courses, or face streaks—not evenly spaced decoration.
3. Keep surface-patch cells exact and non-overlapping. A deterministic runtime
   hash must not move their edges or convert a source stain into confetti.
4. Keep vertical breakup attached to the unique mass: base course, weathered
   face, damaged crown, or local moss streak. Do not apply one repeating course
   schedule to every pillar.
5. Let all stone variants use the shared `PatternRock` shader for fine organic
   variation. Do not add a site-specific bitmap or a second random material
   layout unless new visual evidence demonstrates a missing frequency band.
6. Preserve natural cap/sub/deep materials on reclaimed terrain. The green lip,
   warm soil seam, and regional cliff tone are part of terrain integration;
   only paved surfaces need pale masonry below them.
7. Judge material under the ordinary day cycle at locked day, night, and close
   play distance. Fix geometry and camera before tuning colour against an
   overlay.

## Checks

### Mechanical

Audit surface patches for ownership and overlap, build, and inspect palette
definitions to ensure each selected material still uses the expected pattern.
Mechanical checks prove deterministic assignment and shader selection, not
perceptual similarity.

### Visual

At close/play distance, inspect whether fine weathering breaks flat faces
without becoming a printed grid. At wide/far distance, ignore the faded shader
and check whether authored macro patches still describe age and material. At
night, verify that pale, cool, warm, and mossed masses remain separable without
turning the scene into uniformly dark violet. Compare geometry in an edge view
before blaming material for a silhouette mismatch.

## Scope and limits

The current shader is a shared world material, not a Reference 10-specific
solution. Its presence does not prove exact source texture. Changing its global
strength or fade affects every terrain and structure using it and needs broader
regression captures. The specific patch coordinates and courses must never be
copied to another site.

## Known failures

- Flat pale blocks looked unfinished even when topology improved. Explicit
  macro palette islands plus the existing rock shader provide two-scale breakup.
- Per-block random moss/weathering produced confetti. Coherent source-authored
  patches replace random selection.
- Repeated colour bands on every survivor exposed a reusable pattern. Variation
  is now tied to each unique structure.
- Increasing shader detail at far range caused shimmer and still could not fix
  composition. The shader fades; macro authored blocks own distant readability.

## Update triggers

Update for palette colour/pattern changes, shader strength/frequency/fade
changes, material-to-plan mapping changes, new source-authored patch categories,
day-cycle/grade changes that alter readability, or author corrections to the
material match. Global shader changes require evidence beyond one site.
