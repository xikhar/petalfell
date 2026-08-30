# Roadmap — decisions, order, and open questions

> **What this file owns.** What is settled, what is being built next and why in
> that order, and what is still undecided. This is the file that changes most
> often; the other three should be stable.
>
> Read [AGENTS.md](../AGENTS.md) first if you have not.

---

## 1. Settled decisions

These were decided explicitly by the author. **Do not re-litigate them without
being asked to.** If you believe one is wrong, say so in one paragraph and then
proceed as decided.

| Decision | Choice |
|---|---|
| **Map identity** | One canonical world, authored once. Not re-generated from a seed. |
| **Production extent** | 12,288 × 9,216 × 192 blocks, compiled as a 16 × 12 grid of deterministic 768-block sectors. The old 3,456-square generated world is legacy diagnostics, not the production map. |
| **Site count** | 30–60 sites for Chapter 1, mixed: a few great districts, more precincts, many small marks. |
| **Current starting point** | Paint and validate the production atlas, then place every significant site and route before producing more site geometry. |
| **Hero-site composition** | Faithful measured reconstruction of one supplied `world-new/reference-*.png`, placed at a compatible authored atlas location. No original or blended hero-site designs for now. |
| **Reconstruction implementation** | One site-owned explicit voxel blueprint per reference. No shared architectural kit or procedural tower, pillar, stair, portal, damage or dressing builder inside the measured footprint. |
| **Normal startup** | A fixed four-sector production-atlas mosaic at the active reconstruction, with collision, the player and ordinary chunk streaming inside that mosaic. Dynamic handoff to arbitrary neighbouring mosaics remains unbuilt. The old 3,456 generated world is legacy diagnostic mode only. |
| **Construction knowledge** | Evidence-scoped reconstruction methods live in [`building-knowledge/`](../building-knowledge/README.md). Agents read the relevant entries before building and update or supersede them in the same session when stronger evidence, a better method, or an author correction appears. The live reference always wins; only explicit author confirmation is `author-accepted`. |
| **Coordinates** | Significant content uses stable absolute block coordinates and stable IDs. |
| **Wilderness** | Deterministic procedural infill inside authored land, elevation, water, region, culture, abandonment and wilderness fields. Noise may vary a fact; it may not invent a major fact. |
| **Roads and erosion** | Roads are authored splines realised against terrain. Erosion changes geometry and material before shader treatment; it is not a surface-only effect. |
| **Direction** | Post-population. A continent left by slow decline, not catastrophe. (`plan.md` §2.1) |
| **Resolution** | Voxel for now. Authored meshes remain optional after the authored composition layer exists. |
| **First accepted macro sources** | Land, elevation, hydrology and categorical region are accepted as the canonical L0/L1 basis. Province polygons remain allocation guides only; `region.png` owns organic province shape and derived transitions. |

The reasoning is in [ATLAS.md](ATLAS.md), [MAP_PIPELINE.md](MAP_PIPELINE.md)
§1–3 and [RUINS.md](RUINS.md) §4 and §7; the production order is §3 below.

---

## 2. What already exists and works

Do not rebuild these. Full detail in [CURRENT_STATE.md](../CURRENT_STATE.md);
the engineering rationale is in [ARCHITECTURE.md](../ARCHITECTURE.md).

- **Rendering.** Voxel and character shaders, the explicit ink edge graph, water
  with reflection and refraction, sky, tonemapping and grade, a full day/night
  cycle with keyframed lighting.
- **World substrate.** Derived voxel storage, chunk meshing and streaming,
  collision, terracing, rivers, lakes, beaches, biomes, vegetation, sub-voxel
  ground detail, fauna, roads, bridges.
- **Footings.** Single-building cut-and-fill, plinths, talus, split-level plans,
  guaranteed access. (`plan.md` §11a, `ARCHITECTURE.md` §3a)
- **Reclamation.** The damp/shelter/aspect/age field, the material decay chain,
  and sub-voxel growth — moss, vines, ferns, thickets, saplings, rubble —
  rendered through the existing ground-detail mesh.
- **Player, camera, navigation, dog, inventory, interaction, developer tools.**

The low-level layer is considered done to a satisfactory standard. The work ahead
is the large structural layer above it.

---

## 3. The build order

> **Direction shift (author, 2026-08-27).** The part yard, flat precinct and
> summit sanctum proved individual vocabulary and one earthwork technique, but
> the far captures still read as a sparse isolated prop on a generic generated
> landscape. The references are authored domains: several precincts, routes,
> boundaries and landforms designed as one composition. The previous plan to
> generate a hero site from intent and postpone the map until last is reversed.
> **The canonical atlas and topology come first; remembered places are authored.**
>
> **Atlas scale decision (author, 2026-08-27).** The selected colour, line and
> elevation-map references establish a much larger 4:3 continent with large
> wilderness, strongly separated climate masses and a north-to-south relief
> hierarchy. Chapter 1 now targets the atlas in
> [ATLAS.md](ATLAS.md): 12,288 × 9,216 × 192, built as deterministic sectors.
> The current 3,456-square runtime cannot be stretched to that size because its
> remaining two-dimensional fields are global; it becomes a review fixture.
>
> **Reference reconstruction decision (author, 2026-08-29).** Stop composing
> original sites from the shared kit. Each production ruin is a faithful measured
> reconstruction of one supplied structural reference, placed where its visible
> terrain/water/biome relationship fits the canonical atlas. The rest of the map
> is normal deterministic biome, height and hydrology terrain; do not populate it
> with invented districts. The existing southern gateway plan is retained only as
> a compiler/tooling proof and must be replaced before it becomes production data.
>
> **Literal voxel transcription refinement (author, 2026-08-29).** The first
> attempted `reference-1` reconstruction still used generic portal, pillar,
> colonnade, wall and stair builders and produced large regular slabs. It was
> rejected. Production reconstructions now bypass that kit entirely: one unique,
> site-owned voxel blueprint records every visible mass and decay break. Work
> begins with `reference-10.png` in Bloom Reach, whose visible traveller gives an
> unambiguous two-block scale. Normal game startup moves to this atlas runtime;
> the old generated world is opt-in legacy diagnostics.

Each slice below leaves an artifact that the next one consumes.

### Slice A — canonical source and authoring tools ✅ built

Define versioned authored records for domains, named sites, entrances, route
nodes and route polylines. Build a fast audit and preview path that does not
generate the continent. Invalid IDs, coordinates, bounds or connections fail
loudly.

*Answers:* can the world be understood and checked as a document before it is
rendered?

*Status:* `src/World/CanonicalWorld.cs` provides the versioned L2 schema and
strictly audits domains, sites, entrances, graph nodes, routes, plan references,
bounds and required connectivity. Version 1 preserves the 3,456-square
`world.json` review fixture; version 2 uses explicit rectangular extent for the
permanent `topology.json`. `./tools/world-authoring.sh` audits both and writes
terrain-backed production topology previews without constructing `Planner` or
`Terrain`. The runtime overlay and authored road stamps still use the review
fixture only.

### Slice B — production atlas contract and preview ✅ built

Adopt the selected macro-map references without inheriting their generated
labels, contour numbers or theme-park density. Define the 12,288 × 9,216 logical canvas, global
coordinates, 768-block sectors, registered L0/L1 layers, province polygons and
biome build profiles. Audit and preview those sources without allocating the
production terrain.

*Answers:* can a huge world remain an inspectable, deterministic document rather
than one enormous startup generation?

*Status:* `content/chapter_01/atlas.json` and `biomes.json` describe the accepted
six-province macro basis and profile contracts. The authoring tool audits and
previews registered elevation, hydrology and categorical regions with the sector
grid; old province polygons render only as faint allocation guides.

### Slice C — Chapter 1 macro layers and topology ◐ started

Paint the registered land, elevation, water, region, culture, abandonment and
wilderness layers. Place all 30–60 site envelopes at permanent atlas coordinates.
Author the Spine, Strand, quarry road and Fen causeways as a named graph;
allocate every significant site to a domain and connect each required entrance.
Geometry may be placeholder, but geography and topology may not be procedural.

*Answers:* do we know what every remembered place is, where it is, what sees it,
and how the player reaches it?

*Status:* the author accepted the exact-registration land and elevation sources
and the six working province envelopes on 2026-08-27. Image generation then
proposed connected hydrology and terrain-shaped province masses from those
accepted constraints. The normalized `water.png` and categorical `region.png`
were accepted by the author on 2026-08-27 and are canonical macro sources. The
province polygons are now only faint allocation guides. Culture, abandonment
and wilderness remain planned. Exact layer values and provenance are in
[ATLAS.md](ATLAS.md) §5 and §10.
`content/chapter_01/topology.json` now permanently places the first southern
gateway domain at the central delta threshold: four stable site envelopes, ten
nodes and nine connected route segments span the shelf, southward causeway,
local Strand and northbound Spine approach. The atlas audit checks its
12,288 × 9,216 extent and province references; the domain preview overlays the
accepted terrain/water/region layers and names its nine intersecting sectors.
This completes only the first connected fragment. The remaining 26–56 sites and
continent-scale Spine, Strand, quarry and Fen graph are still Slice C work.

### Slice D — sector compiler and review window ◐ started

Compile L0/L1 sources and deterministic wilderness for one 768-block sector plus
seam apron. Replace the current assumption that all two-dimensional terrain,
road and biome arrays exist globally. Load and inspect an arbitrary atlas sector
or small sector window without generating the whole continent.

*Answers:* can the production scale exist without multi-gigabyte global state,
and do independently built seams agree exactly?

*Status:* compiler version 4 emits a `PTFLSEC2` terrain artifact and PNG for any
addressed sector. It derives primary/secondary profile weights across authored
transition widths; compiles ocean, enclosed lakes and high-altitude river cores
with absolute water surfaces and beds; and lowers profile-controlled floodplains
and banks before terrace quantization without allowing local profile relief to
submerge compiled land. Sector data validates its own profile,
land, bed and surface invariants. Corner, boundary, mountain and drowned-south
sectors rebuilt deterministically; every available independently compiled east/
south edge matched all 39,168 overlap cells. A strict reader now loads current
artifacts into a sector-local `VoxelGrid`; the runtime review window uses the
ordinary chunk mesher, ink, atmosphere, grade and a multi-height water mesh at
true atlas coordinates. Fixed captures have been inspected for mountain,
confluence and drowned-south sectors. A review-only mosaic can now join a square
set of those artifacts without changing their persistence boundary; the first
domain uses sectors 7–9 in both axes. The active reconstruction now uses a
collision-enabled player and ordinary chunk streaming over its four-sector atlas
window. Dynamic handoff between arbitrary mosaics, multi-plane reflections and
persistent road/site overlays remain outside the sector artifact, so the slice
stays open.

### Slice E — first exact reference reconstruction

Reconstruct `world-new/reference-10.png` as a compact Bloom Reach grove court at
the accepted dry meadow around `9800,4600`. Measure its visible player-scaled
platform outline, two court levels, central stair, every wall, arch and pillar,
paving break, rubble mass and vegetation opening. The reconstruction is one
explicit site voxel blueprint. Procedural generation begins outside its measured
footprint, not inside it.

*Answers:* does the locked reference view align one-to-one, does the two-block
traveller establish the same scale, and does the site remain coherent at close,
play, wide and far distances from all four 90-degree rotations?

*Status:* active reconstruction; author acceptance remains open.
`bloom-grove-court` owns a unique `reference-10` voxel blueprint at `9800,4600`;
it does not call the shared ruin kit or superseded domain blockout. Its strict
one-cell-per-voxel ground plan currently owns the court levels, stairs, detached
upper slabs, structural projections, exact surface breakup, rubble and tree
anchors, while the site builder owns each vertical block and damage course. The
current v2 plan contains 28 terrain records, 24 surface-patch groups covering
1,893 cells, 31 structure records including two stairs and ten rubble clusters
covering 119 exact cells, and eighteen trees. Its pre-widening central slab
boundary is restored; the occupied ruin
reaches only source x=40 through a broken low L return and two interior remnants,
and neither it nor later surface work bridges the lower channels at z=-10..-9
and z=17..20. The current v16 correction removes the graded east stair-side
shoulder/cap/rubble family; divides the south-west wall around a true passage at
source x=-11..-10, z=-9..-6; seats the four constant 2×2 central pillar shafts at
y116 continuously over the y114 base and y115 stylobate; and introduces a y107
southern approach whose y107/y108/y109 walkable surfaces make two rises into the
y109 lower court. One-cell walls, shoulders, and stelae remain distinct from the
2×2 pillar family. Source/runtime plan audits, the world audit, and
`dotnet build --no-restore` pass for this revision.
Normal startup opens the fixed collision-enabled four-sector atlas mosaic;
`--legacy-world` is the only route to the old generated fixture. The current
evidence and remaining visual gaps are recorded without overstating acceptance
in the [Bloom Grove Court knowledge ledger](../building-knowledge/sites/bloom-grove-court.md).
The locked view remains the source's exact 1672×941 resolution, source-facing
135-degree quadrant and 35.264-degree isometric pitch. Slice E stays open until
the author accepts the match. The complete v13 matrix remains historical visual
evidence for the unchanged corrected footprint, channels, reverse support, and
far extent. The v16 locked-day, true-top, and four play-distance quarter-turn
captures have been inspected for the stair-side subtraction, open wall passage,
continuous 2×2 pillar foundations, and two-rise threshold. Those are
claim-scoped findings; current close/wide/far coverage, whole-site fidelity,
collision/playability review, and author acceptance remain open. No second
structural reference starts before author acceptance.

### Slice F — local content compilation and partial regeneration

Compile authored sources into disposable terrain, road, structure, navigation
and chunk artifacts. Rebuilding one site or domain must leave unrelated tile
hashes unchanged.

### Slice G — production

Author the remaining supplied references one connected site at a time, only
after the author accepts the completed first site. Templates remain reserved for
minor non-reference marks; they do not enter a reconstruction footprint.

### Retained evidence from the retired order

- The twelve-part voxel kit in `src/World/RuinKit.cs` is legacy diagnostic
  vocabulary, not a production reconstruction API.
- `src/World/Massif.cs` is a legacy additive slab operation. It may inform a
  measured landform that visibly has that character, but it does not own or
  generate production-site ground.
- `src/World/Sanctum.cs` is a review fixture and source of measured failures. It
  is not canonical content until a site record places it and a connected domain
  absorbs it.

---

## 4. Open questions

Genuinely undecided. Do not silently pick an answer; raise them.

**How many cultures.** One is cheaper and makes the meander motif a strong single
signature; two or three give the map real regional identity. Working assumption
is two — an older southern builder and a later northern one that reused their
stone. ([WORLD.md](WORLD.md) §7)

**Interiors.** `reference-7` has an open cellar; `reference-2` a dark temple
doorway. Listed as open in `ARCHITECTURE.md` §8. If districts are the main
content, "can I go in" becomes a much larger question than it was.

**Region names.** Everything in [WORLD.md](WORLD.md) §3 is a working name over a
considered role. The roles are meant to be argued with; the names are
placeholders.

---

## 5. Standing lessons

Hard-won in this project, recorded so they are not re-learned.

**Bisect and measure; do not reason from symptoms.** Several fixes here were
wrong twice when reasoned about and right immediately when bisected or profiled.
A performance problem was once confidently attributed to the day/night cycle,
measured at six percent, and turned out to be chunk meshing.

**Anything that should read as a region must come from a field with a
wavelength, never a per-block hash.** This has produced confetti three separate
times — wall decay, roof loss, and moss patches.

**Values tuned under one set of conditions become wrong when the conditions
change.** Water tuned against pale water, grade tuned against flat lighting, the
vegetation apron tuned for a populated world. When you change a system, re-check
the constants that were tuned against its old behaviour.

**A gate written to protect a weak system should be removed when the system gets
strong.** Landmarks required near-flat ground because the builder could only set
boxes on level terrain. Once footings could read a slope, that gate was throwing
away every site where the new capability had anything to say.

**Diagnostics in the boot log earn their keep.** "Thirty-nine footings fitted and
zero of them split" immediately exposed a feature that had never once run.

**Parts in a yard prove nothing; composition is the product.** A twelve-part kit
laid out in rows captured well and was still judged "very basic" the moment it
was compared to a reference, because the references are terrain, massing and
variety — not parts. Judge work as a composed place or not at all.

**Nothing visible is stamped.** Every stair in the references differs in width,
length, rise and what joins it; every column differs in weight, height and state.
Each instance is authored from the source, even where several instances share a
source-observed grammar such as a constant square shaft. A reusable builder with
different parameters is still the wrong production boundary if it replaces
block-by-block transcription.

**A valid platform polygon can still compile into a blank slab.** The first L3
runtime pass was topologically correct and visually wrong: a complete pale cap
greedily merged into one empty plane with isolated monuments on it. Made courses,
broad terrain-cap survival, authored openings and dense edge/reclamation tissue
must be judged in the first blockout, not deferred as cosmetic polish.
