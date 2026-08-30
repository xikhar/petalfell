# Terrain and detached slab integration

- **Lifecycle:** `active`
- **Evidence summary:** local block layers and the three-slab separation are
  `observed/source-measured` and `mechanically verified`; the corrected occupied
  footprint and unchanged channels are `visually reviewed` across the complete
  v13 capture matrix, but whole-site fidelity is not `author-accepted`
- **Scope:** `site-specific` to Reference 10; the authored-site/ordinary-atlas
  ownership boundary is `general`
- **Last verified:** 2026-08-30 mechanically and across all 19 v13 raw captures
  plus four derived comparisons
- **Supersedes:** concentric/jagged terrain rings and one monolithic stamped pad
- **Superseded by:** none
- **Owning sources:**
  [`bloom-grove-court-reference-10-plan.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10-plan.json),
  [`Reference10GroveCourt.cs`](../../src/World/Sites/Reference10GroveCourt.cs),
  [`docs/RUINS.md`](../../docs/RUINS.md)

## Outcome

The site authors only the terrain needed to reproduce its visible courts,
revetments, blocky shelves, and cuts. Around it, the atlas remains normal
deterministic biome/elevation/hydrology terrain. Reference 10's surrounding
breakup is made from a few local, detached, flat block layers—often three or
four tiers in a part—not from nested rings around the whole site.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| Reference 10 shows local stepped shelves, detached fragments, and ordinary ground between parts rather than concentric rings | `observed/source-measured` | site-specific | Primary and overhead references plus explicit author correction: “3-4 blocky layers in parts, not jagged rings” | Exact hidden edges and current east-upper split remain under iteration |
| `preserve-atlas` terrain stays untouched while `author-surface` shapes write deterministic levels in plan order | `mechanically verified` | tool-specific | Plan write modes; visible-owner audit; `WritePlannedTerrainAndStairs`; successful v11 audit/build | Passing does not prove the chosen polygons match the source |
| Four localized stack areas and detached fragments read as parts rather than rings in v11 | `visually reviewed` | site-specific | v11 locked/top and full matrix inspected 2026-08-30 | Historical evidence for the unchanged local stacks; it predates the east-upper split |
| The east upper split reads as three same-height slabs with lower-terrain channels at source z=-10..-9 and z=17..20; no wall, rubble, or paving bridges either channel, and reverse views show no floating back | `visually reviewed` | site-specific | All 19 raw captures and four derived comparisons in `/home/shikhar/godot/shots/reference-10-plan-v13-full/`, inspected 2026-08-30 | Collision/playability and whole-site fidelity remain open |
| Reference 10 can spread across an already-correct slab by extending sparse paving and low site-owned ruins without moving the terrain edge | `mechanically verified` | site-specific | Current Reference 10 plan/builder: pre-widening central slab polygon restored; occupied footprint reaches only source x=40 through a broken low L return and two interior remnants; plan/world/build/diff checks pass, 2026-08-30 | Candidate for broader reuse; the appropriate amount and placement remain source- and site-specific |
| The corrected occupied spread reads on the intended right side without an overextended terrain silhouette and stays modest at far range | `visually reviewed` | site-specific | Complete v13 matrix above, inspected at all four rotations and distances plus locked day/night and top, 2026-08-30 | The overall reference match and author acceptance remain open |

## Procedure

1. Mark the broad surrounding polygon `preserve-atlas`. It is a grading,
   exclusion, and context boundary—not permission to flatten the landscape.
2. Trace each occupied court/platform as an explicit level and polygon. Use
   named stairs to connect levels rather than smoothing the height difference.
3. Trace local terrain breakup as separate, flat voxel layers. Keep a stack to
   the source-visible part; do not propagate its outline into a site-wide ring.
4. Where the source shows a separated upper region, divide it into independently
   supported slabs with visible lower-terrain channels. Split connected wall
   records at the same channels so no architecture floats across a gap.
5. If a composition feels too narrow, compare the terrain silhouette and the
   occupied ruin footprint separately. When the slab edge already matches the
   source, spread sparse paving, rubble, and low site-owned wall fragments across
   it; do not enlarge empty terrain to solve an architecture-density problem.
6. Use three or four layers only where the reference shows that depth. Other
   edges may be one layer, ordinary terrain, or a detached fragment.
7. Order terrain as a painter's stack from lower/broader to higher/narrower.
   Review the final visible-owner cells; later shapes intentionally replace
   earlier cells.
8. Preserve the atlas cap/sub/deep profile for natural/reclaimed terrace caps.
   Paving receives a masonry substrate. This keeps green fringes and warm soil
   seams attached to the land instead of producing pale boxes with grass tiles.
9. Place exact rubble, walls, trees, and paving only after the slab topology and
   stair connections survive source-facing and runtime-facing plan review.

## Checks

### Mechanical

The ground-plan audit must prove that authored patches remain on visible owning
terrain, every stair's first/last tread meets its named levels, structure bases
match support terrain, and separated connected runs remain 4-connected. Build
after the audit to exercise runtime painter order and projection guards.

### Visual

Use the top capture to look for rings, monolithic pads, channels closed by later
geometry, and overly neat outlines. Use the locked isometric to check that each
layer reads as a blocky slab, not a contour line, and that revetments integrate
with architecture. Use reverse views to find floating walls and unsupported
backs. Ordinary terrain must remain visible between local stacks.

## Scope and limits

The exact number, height, and outline of slabs is always source-owned. Reference
10's current local stacks are not a template for cliff cities, causeways, or
other references. The older additive `Massif` technique remains diagnostic for
landforms that genuinely show that character; it does not author production
site topology and must not replace measured polygons. The v13 matrix establishes
only the footprint/channel/support claims named above; it does not establish
collision, playability, whole-site fidelity, or author acceptance.

## Known failures

- A broad neat pad made the ruins look placed on terrain. Explicit courts,
  revetments, and nearby terrain layers now meet as one topology.
- Nested jagged rings were mechanically coherent but visually wrong. They were
  replaced by a few localized block stacks with ordinary land between them.
- Too many one-cell serrations read as noise rather than block layers. Prefer
  substantial orthogonal steps and detached pieces seen in the source.
- A single upper platform can erase source-visible channels. Split both terrain
  and any supported wall components; never leave a bridge or floating mass.
- Extending an already-correct slab to fix an empty-looking precinct made the
  empty pad larger and moved its silhouette away from the source. Restore the
  measured terrain edge, then extend only the source-supported occupied paving,
  rubble, or low wall footprint.

## Update triggers

Update whenever a platform polygon/level, terrain write mode, painter order,
stair connection, support relationship, detached channel, natural substrate
rule, or author correction changes. Any topology edit invalidates prior visual
evidence until top, locked, and reverse captures are inspected again.
