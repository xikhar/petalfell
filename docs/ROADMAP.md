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
| **Production extent** | 12,288 × 9,216 × 192 blocks, compiled as a 16 × 12 grid of deterministic 768-block sectors. The 3,456-square runtime is a review fixture, not the production map. |
| **Site count** | 30–60 sites for Chapter 1, mixed: a few great districts, more precincts, many small marks. |
| **Current starting point** | Paint and validate the production atlas, then place every significant site and route before producing more site geometry. |
| **Hero-site composition** | Authored plan. Procedural systems assist with repetition, fitting, damage and dressing; they do not invent the composition. |
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
domain uses sectors 7–9 in both axes. Collision, player traversal, multi-plane
reflections and persistent road/site overlays remain outside the sector
artifact, so the slice stays open.

### Slice E — one connected southern domain

Build one 500–1000-block domain containing a 250–350-block primary district and
two or more connected precincts. Use the monumental gate, water causeway and
lower precinct references as one continuous composition. Author its platform
polygons, levels, walls, stairs, route sockets and silhouette; use generators
only to realise and dress those decisions.

*Answers:* can authored terrain, routes and architecture read as one world at
normal play distance and in the fixed 240-unit near, 440-unit wide/reverse and
1,000-unit far review views?

*Status:* L3 blockout compilation has started. `domains/shallows-gateway-domain.json`
fixes seven terrain/platform polygons at Y106/108/112/116, three fitted stair
connections, four named terrain/collapse cutouts, ten shared wall runs, eight
route sockets and thirty-nine measured silhouette placements across all four L2
sites. `DomainPlanDefinition` audits the local-to-atlas transform, plan/site
bounds, exact graph sockets, references, cutout containment and depth, authored
reclamation densities, level hierarchy and reference scale.
`preview-atlas-domain` composites the plan over accepted terrain. `review-domain`
now composes its nine normal sector artifacts and realises platforms, exact
routes, stairs, walls and landmarks into the ordinary voxel renderer;
deterministic terraces, courses, buttresses, coping, cutout rims, rubble and a
global-coordinate grove pass dress only authored or biome-owned intent. Eight
fixed late-morning/night captures have been inspected through the real day-cycle
rig. The district scale, connected level axis, long-range shadows and silhouette
now work. Slice E stays open because the court surfaces, wall damage, glyphs and
L4 ground detail still lack the density and local variation of the references.

### Slice F — local content compilation and partial regeneration

Compile authored sources into disposable terrain, road, structure, navigation
and chunk artifacts. Rebuilding one site or domain must leave unrelated tile
hashes unchanged.

### Slice G — production

Author the remaining great districts and precincts one connected domain at a
time. Template-assisted generation is reserved for minor marks and repeated
substructures; every generated result remains subordinate to an authored plan.

### Retained evidence from the retired order

- The twelve-part voxel kit in `src/World/RuinKit.cs` is reusable vocabulary.
- `src/World/Massif.cs` is a useful additive slab-stack operation, not the
  universal site representation.
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

**Nothing repeats.** Every stair in the references differs in width, length,
rise and what joins it; every column differs in weight, height and state. A
builder must take those as parameters, and a site must vary them on every
instance — one part stamped repeatedly is what "very basic" looks like.

**A valid platform polygon can still compile into a blank slab.** The first L3
runtime pass was topologically correct and visually wrong: a complete pale cap
greedily merged into one empty plane with isolated monuments on it. Made courses,
broad terrain-cap survival, authored openings and dense edge/reclamation tissue
must be judged in the first blockout, not deferred as cosmetic polish.
