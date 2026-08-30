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
| **Current starting point** | The fast moving-window full-atlas runtime is the production foundation for terrain and every new site. The four accepted maps own macro intent and the proven old-world primitives own local construction. Bloom Grove Court is promoted to `Production`; build and review `reference-1` as `Blockout`, then promote it onto this same world without creating a second terrain path. |
| **Hero-site composition** | Faithful measured reconstruction of one supplied `world-new/reference-*.png`, placed at a compatible authored atlas location. No original or blended hero-site designs for now. |
| **Reconstruction implementation** | One site-owned explicit voxel blueprint per reference. No shared architectural kit or procedural tower, pillar, stair, portal, damage or dressing builder inside the measured footprint. |
| **Current normal startup** | A moving 1,536-block window exposes the complete production map. It generates the proven terrain/water/runtime grammar at permanent global coordinates, begins at Bloom with Site 0, walks into neighbouring windows and supports full-map Shift-click travel. The same generic overlay realises every topology site whose status is `Production` or `Accepted`; lower statuses remain review-only. `--compiled-atlas` opens the earlier compiler runtime; `--legacy-world` opens the retired circular fixture. |
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

Reuse these mechanisms rather than replacing them wholesale. Their legacy-world
implementations work; production-atlas realization and later visual calibration
remain active. Full detail is in [CURRENT_STATE.md](../CURRENT_STATE.md); the
engineering rationale is in [ARCHITECTURE.md](../ARCHITECTURE.md).

- **Rendering mechanisms.** Voxel and character shaders, the explicit ink edge graph, water
  with reflection and refraction, sky, tonemapping and grade, a full day/night
  cycle with keyframed lighting.
- **Legacy-world substrate and shared storage.** Derived voxel storage, chunk meshing and streaming,
  collision, terracing, rivers, lakes, beaches, biomes, vegetation, sub-voxel
  ground detail, fauna, roads, bridges.
- **Footings.** Single-building cut-and-fill, plinths, talus, split-level plans,
  guaranteed access. (`plan.md` §11a, `ARCHITECTURE.md` §3a)
- **Reclamation.** The damp/shelter/aspect/age field, the material decay chain,
  and sub-voxel growth — moss, vines, ferns, thickets, saplings, rubble —
  rendered through the existing ground-detail mesh.
- **Player, camera, navigation, dog, inventory and interaction.** The map and
  developer tools are complete in the legacy runtime and mechanically wired to
  the production-atlas runtime; live post-handoff input/collision review remains
  Slice G work.

The shared low-level mechanisms are considered done to a satisfactory standard.
Their old global-array world is not evidence that production terrain, runtime
handoff or reference-parity ink is complete.

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
>
> **Terrain-first playable-world decision (author, 2026-08-30).** The existing
> sector compiler proved deterministic storage, macro-source registration and
> exact local seams, but its bilinear elevation plus profile noise is still a
> blockout rather than the finished world. Before another site is built, finish
> the complete 16 × 12 atlas terrain: preserve the accepted geography while
> porting the legacy circular world's successful surface, broken-edge, terrace,
> water-transition and material grammar into bounded global-coordinate sector
> passes, and add the missing mountain, cliff, hydrology and biome responses.
> Keep Bloom Grove Court in place. Then reconstruct the supplied
> `reference-1.png` bridge/cliff/gate at a compatible permanent location; whole-
> site acceptance of Bloom no longer blocks that second transcription. Next make
> the atlas a continuous playable runtime with a production map, Shift-click
> teleport and tilde developer controls. Only after those work does ink,
> shading, material, lighting and day/night parity become the active pass. This
> explicitly supersedes the previous order of completing every site envelope and
> accepting Reference 10 before starting any second structural reference.

> **Fast terrain iteration correction (author, 2026-08-30).** Do not rebuild and
> verify the continent for every visual change. The accepted production map is a
> macro guide; local ground, terraces, broken edges, shores, underwater terrain,
> water and ink come directly from the proven legacy runtime. Normal startup is a
> bounded map-guided production window. On the author's next correction this was
> connected directly to the full atlas map and moving-window handoff; the runtime
> must not fall back to a single crop or require a whole-atlas compile.
>
> **Production-foundation decision (author, 2026-08-30).** The resulting
> map-guided old-terrain path is the starting base for the new map and all future
> sites. A site is not hard-coded into that path: its canonical topology status
> promotes it. `Production`/`Accepted` sites overlay the generated terrain at
> permanent coordinates; `Planned`/`Blockout` sites remain isolated in review
> tools until deliberately promoted.

> **Fine sculpture decision (author, 2026-08-31).** Monumental figures and
> carved forms cannot be approximated by a few large axis-aligned voxel fills.
> Keep terrain contact, streaming and conservative collision in the site-owned
> voxel blueprint, but author continuous anatomy, facial planes, crowns, bevels
> and diagonal damage as reproducible Blender-built meshes. This is low-level
> geometry vocabulary, not permission for a reusable statue generator.
> `reference-12.png` establishes the first permanent Fallen Colossus application
> at `(10600,4600)`.

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

### Slice C — Chapter 1 macro layers and topology ◐ paused after the first fragment

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
continent-scale Spine, Strand, quarry and Fen graph are still Slice C work, but
the author paused that allocation on 2026-08-30 until the complete terrain and
two-site playable-world milestone below is working.

### Slice D — complete production terrain and sector runtime ◐ active

Realise the accepted L0/L1 sources as finished deterministic terrain without
continent-sized arrays. Normal play now uses the original low-level terrain,
water, material and vegetation system in moving 1,536-block windows. The atlas
guide replaces only its old circular macro planner; every local feature samples
absolute coordinates so walking and map travel regenerate the same place.
Representative terrain and neighbour handoffs are the fast review loop. The
historical 192-sector compiler remains an optional mechanical integration tool,
not the normal runtime or prerequisite for a terrain screenshot.

*Answers:* can the production scale exist without multi-gigabyte global state,
do independently built seams agree exactly, and does every biome, coast, river,
mountain front, cliff and sudden level change read as deliberate terrain rather
than bilinear source pixels with noise?

*Status:* the fast map-guided moving-window runtime is the current playable visual
build. Bloom and a distant river address both generate without compiler artifacts;
automatic east handoff retained the exact current surface. Full-map Shift-click
travel is connected to the same builder. Compiler 27 remains the latest complete
historical whole-atlas mechanical baseline. It emits a `PTFLSEC2` artifact
and PNG for any addressed sector and resolves land-only elevation, profile
transitions and altitude/slope/moisture profile choice. Its current relief
grammar samples global profile-sized cell lattices and named noise fields,
articulates non-wind slopes with signed contour shoulders, cuts the cold massif
with two crossing wind-ridge families, and restricts large course changes to
sparse macro fronts followed by three synchronous noise-broken toe ledges. A
40-cell transient support border covers cleanup, ledges, shoreline reach and
derived metrics before the persisted apron is cropped. No production relief
pass authors circular or radial geography. Registered hydrology still owns
ocean, enclosed lakes and permanent channels; ordinary banks converge from both
directions while preserving authored macro cuts, sector validation rejects a
dry cardinal boundary below adjacent water, and the review water mesh closes
wet-to-wet one-block changes while leaving shores terrain-owned.

Compiler 16 remains the historical first complete mechanically verified atlas
baseline; its land-aware elevation, registered water guide, dry-bank invariant
and full-batch evidence are preserved in
[the terrain building-knowledge entry](../building-knowledge/terrain/production-atlas-relief-hydrology-and-wilderness.md).
Compiler 27 now supersedes it as the current whole-atlas mechanical baseline.
The resumable compiler-27 batch produced all 192 sectors under manifest
`44a9b2033bd10fa879de0aa18b100ec84d866c8e17dc9d00cc49671e33c350b0`.
Its atlas hydrology audit reports severe steps `0`, maximum wet/wet step `1`,
submerged dry boundaries `0` and cross-sector invariant failures `0`. All 180
horizontal and 176 vertical seams compare 13,943,808 overlap cells with zero
mismatches. An independent rebuild compares 127,844,352 apron-bearing cells
exactly. These are continent-wide mechanical facts, not a claim that terrain is
finished or author-accepted.

Normal terrain and vegetation use globally anchored candidates from the original
system, with permanent reference footprints excluded before planting. The
ordinary chunk mesher, collision, ink, atmosphere, grade and translucent moving
water render the resulting `VoxelGrid` at its true atlas origin. Compiler-backed
profile dressing and its strict artifact reader remain available only through
the explicit integration/review path.

The fixed compiler-27 snow, highland, scarp and river captures and normal-start
captures have been inspected by the agent. They establish the current visual
review boundary only: broad terraces, sparse surfaces, flat banks and
reference-level terrain/material treatment remain open. There is no author
acceptance, so Slice D is not finished. Also open are an atlas-wide wilderness
matrix, targeted exclusion proof, persistent reach direction/width and
talus/scree fields, author review, multi-plane reflections, and persistent
road/site overlays outside the sector artifact.

### Slice E — preserved first exact reference reconstruction ◐ carried baseline

Reconstruct `world-new/reference-10.png` as a compact Bloom Reach grove court at
the accepted dry meadow around `9800,4600`. Measure its visible player-scaled
platform outline, two court levels, central stair, every wall, arch and pillar,
paving break, rubble mass and vegetation opening. The reconstruction is one
explicit site voxel blueprint. Procedural generation begins outside its measured
footprint, not inside it.

*Answers:* does the locked reference view align one-to-one, does the two-block
traveller establish the same scale, and does the site remain coherent at close,
play, wide and far distances from all four 90-degree rotations?

*Status:* preserved and playable; whole-site author acceptance remains open, but
the reconstruction pass is paused while Slice D is active.
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
Normal startup opens the complete map through a bounded moving old-terrain window,
overlays the site at its permanent coordinate, and uses the atlas map/walking
handoff to regenerate neighbouring windows. The earlier compiler-backed mosaic
remains available through `--compiled-atlas`. `--legacy-world` is the only
route to the old circular fixture. The current
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
collision/playability review, and author acceptance remain open. The author's
2026-08-30 terrain-first decision removed the former rule that a second
structural reference must wait for Bloom's whole-site acceptance.

### Slice F — `reference-1` bridge, cliff and gate reconstruction

The production baseline also contains the author-requested `reference-12.png`
Fallen Colossus at `(10600,4600)`. Its current fast integration uses the
author's cleaned Meshy leg and head GLBs only, over a strict site-owned broken
stone precinct. Its seventeen terrain records, six stairs, four broken
foundation traces, four exact rubble fields, sixteen surface-patch groups and
eight 2×2 pillars spread across four detached partial three-level slab stacks
and an enlarged central plinth, with atlas terrain retained between them. The
legs use 1.5× their first imported review scale and matching collision while the
head remains unchanged. Their baked source materials and image
floor meshes are removed; Petalfell stone, silhouette ink and compound collision
are attached in every production window. The v25 locked and far captures have
been inspected, but live traversal, complete rotational fidelity and author
acceptance remain open. This targeted site does not replace the next large `reference-1`
district slice.

After Slice D passes, choose a compatible permanent cliff-and-water location and
author the second site from `world-new/reference-1.png` plus its supplied top
view. The long causeway, southern stair, submerged bridge supports, stepped cliff
shelves, side precincts, rubble, vegetation exclusions and monumental north gate
must be measured as one very large composition at player scale. Its site-owned
terrain may adjust the chosen atlas landform inside the measured footprint; the
ordinary compiler owns everything outside it. Locked top and source-isometric
views plus four rotations and play/far distances are required.

### Slice G — continuous playable atlas, map and developer surfaces

Provide deterministic neighbouring-sector handoff over the production atlas's
bounded four-sector runtime. Render the in-game map against production
terrain, show Bloom and the Reference 1 site at their permanent coordinates, and
retain Shift-click teleport. Route the existing tilde developer controls—time of
day, zoom and outline among them—through atlas runtime rather than leaving them
available only in the legacy fixture.

*Status:* map-triggered reload and bounded continuous walking handoff are
mechanically built. The Bloom runtime opens a rectangular 12,288 × 9,216 map
from a fingerprint-matched
batch composite or registered-layer fallback, draws permanent routes, domains,
site envelopes/labels and global player position, and routes its actual camera,
ink and day cycle through the existing developer menu. Shift-click supplies
exact global X/Z and can synchronously reuse or compile an edge-clamped four-sector
mosaic anywhere inside the atlas. Replacement uses the same reference-site and
global wilderness passes, deterministic dry/traversable fallback, collision
priming and existing player/camera/material/day nodes; the map and developer
surface keep their open state and values. Headless checks pass for exact land,
water and blocked-site fallback, opposite atlas edges and the rebuilt Bloom site.
Walking triggers while the old eight-chunk stream circle is still safe, chooses
the cardinal or diagonal one-sector neighbour, primes its collision, and swaps
without changing exact global X/Z, velocity, camera, day, map or developer state.
It accepts only the identical exact safe cell and surface height—never a fallback
teleport—and a cooldown plus deeper rearm band prevents boundary thrash. The
headless planner passes east/west/north/south, all four corners, partial and
refused atlas-edge cases, repeated suppression and a rearmed return. Live
input/overlay and post-reload collision review remain open.

### Slice H — reference-parity lighting, materials and ink

With terrain, both sites and traversal tools working, tune the shared render path
against fixed day/night captures. The high-key pastel surfaces, faded stone,
soft long shadows, atmosphere and water must remain legible at play and far
distance. Ink strength and classification must respond to lighting/time rather
than crushing every edge with one dark value throughout the day. This is one
shared production pipeline, not per-site presentation lighting.

The first shared pass now separates quieter camera-facing internal turns from
preserved silhouettes, gives both ink families a stepped night response, softens
the shared grade/shadow defaults, and hands the post-twilight key the moon colour
as well as direction. Locked Bloom day/night captures have been inspected for
those narrow claims. Slice H remains active: material/weathering parity,
source-soft shadows, other distances/biomes, Reference 1, live transitions, and
author acceptance are still open.

### Slice I — local compilation and remaining production

Persist authored terrain, road, structure, navigation and chunk artifacts so
rebuilding one site or sector leaves unrelated core hashes unchanged. Resume the
remaining topology and supplied-reference reconstructions one connected site at
a time. Templates remain reserved for minor non-reference marks outside measured
footprints.

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
