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
domain uses sectors 7–9 in both axes. The same window is now the default playable
game: chunk collision is on, the traveller walks and swims on compiled columns,
and the ordinary camera and day cycle run at true atlas coordinates.
`--legacy-world` still boots the 3,456-square fixture. Multi-plane reflections,
inventory/fauna systems that require `Terrain`, and persistent road/site
artifacts remain outside the sector format, so the slice stays open.

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
fixes twenty-five terrain/platform polygons at Y102/104/105/106/108/110/114/124/144, eight fitted
stair connections (a dais notch onto the inner shrine, processional, east cliff, crown, waterfront, east face, cleft, and lower precinct), five named
cutouts (two 124 hillside courts, one drowned-court collapse, one lower terrain bay, one waterfront gate cleft that opens west to the 102 bay), nine wall runs, eight route sockets and
sixty-seven measured silhouette placements across all four L2 sites, including a
through-opening `Gate` (span 18, height 20 flush with the 144 cap, one-block jamb and lintel) at the head of the grand stair
and a raised Y110 inner court on a precinct-scale pale-masonry approach dais at the traveller spawn, with an inscribed Standing pylon, four buried basins, a Broken arch, Broken/Stump columns and a gapped 3-high trace ring.
Domain near looks at that dais (plan 0,−6, 118 units, pitch 42); wide 520 / far 700 look at the drowned 104 spur into the waterfront cleft (plan −122,−168 / −122,−180) with FogBegin 1.40× so that opening is the subject (reference-1/2) and the inland massif is hinterland.
The 144 crown is two overlay lips behind the lintel (plan z=92–95) plus a north landing for the crown stair, not a connecting bar; the 124
south edge at the slot is a planar ~30-wide face at plan z=84 (x=−8…22) with an 8-wide stair finger and a north tan plateau to plan z=140 that frays except the gate slot (an 18-wide un-frayed rib was a palace wall), and a Y106 east ruin pad rather than a 42-wide plinth, the 104
forecourt is a
processional strip, and the causeway is a Y102 land spine with an eastern lobe of
paving patches in the authored shallows plus a Y114 east scarp that shelves the 124 waterfront south face (plan z=−150 to −174) except a channel notch (plan z=−150 to −206) aligned to a 104 cleft in that face — south mouth at the Gate, west arm to the 102 bay — and a Y104 masonry spur through that channel, so a through-opening Gate sits in the cliff (reference-1/2) rather than on the shelf or in a closed alcove. Two 124 hinterland hills pull south to the slot as jagged sheer slabs (south edges set back from z=84 so they are not a second palace wall; |X|>22 stays masonry cliff, not ramped bleachers). A 124 east bank presents a planar east–west south face at plan z=−148 over the shallows, fused north into the east hill, with a 114 east-face shelf toward the water, a 114 mid terrace (wide south shelf behind the near camera, then a thin west-edge ledge to plan z=90) and a 108 grass hillside west of that ledge, so wide/far share gate, cliff, causeway and pool (reference-1/2) rather than a north–south curtain wall. Camera-facing revetment
aprons are skipped; 124/144 camera faces keep one masonry Deep so the cliff and
the gate are the same stone. Grass Deep on those drops inked as a ribbed palisade.
The slot/stair cheeks stay masonry so the grand flight is a notch in stone.
A 6-deep 114 west terrace at plan z=78–84 (plus a 4-deep east hug) steps the 124 south face so that drop is two
~10-block masonry slabs with a tan shelf; living Deep stays on the 104/108 courts.
The 144 opening stays dressed stone; the outer lobes grow over. The 104 shelf ramps its outer band into the hillside so it is not a canal lock. Stairs are
Massif notches (one tread per block into the high slab): the grand and side
flights bite 104→124, the dais flight bites 105→110, and the crown flight runs behind the gate along plan z=94.
A lerp along the whole authored segment had raised a free-standing ramp whose
sides read as a vertical cutaway. Domain play streams radius 8 with an 8ms mesh
budget so walking does not hitch on the capture radius. The 144 lobes sit beside the overlay lintel; processional columns stay at
the 124 floor so the opening is a hole in the cliff. Standing columns and pylons carry a crystal core. Domain night
review retints sky/fog toward `reference-5` lavender without changing play midnight.
Precinct-link walls stand 8–9 as Broken runs with short stelae posted only on those
links (8-high broken shafts every 88 blocks, so they mark the wall without a second skyline from 520u). Grove traces along the approach were removed so wide/far do not read as a
walled boulevard. Authored water pylons are the drowned posts.
Inscribed stelae paint a 5-wide running meander in rubble (centre-gap rows alternating with end-gap rows) so the motif turns at 118 units rather than reading as one inset panel or a stair zig-zag; other pylons recess the sparse meander one block into the camera face. Surviving paving carries a meander inlay. The approach dais stains that inlay in rubble, and atlas grass around the emblem is a ragged pale-masonry disk cut off north of plan z=16. Broken precinct-link walls leave 2-high stumps in their collapse hem.
Shallow water uses the
shader's lilac stops. `DomainPlanDefinition` audits the local-to-atlas transform,
plan/site bounds, exact graph sockets, references, cutout containment and depth,
authored reclamation densities, level hierarchy and reference scale.
`preview-atlas-domain` composites the plan over accepted terrain. Default play
and `review-domain` compose its nine normal sector artifacts, realise platforms,
exact routes, stairs, walls and landmarks into the ordinary voxel renderer with
collision, and spawn the traveller on compiled ground. Deterministic terraces,
courses, cutout rims, court rubble and a globally anchored grove
pass dress only authored or biome-owned intent. Eight fixed late-morning/night
captures have been inspected through the real day-cycle rig (near 118 at yaw 23 / pitch 42
/ wide 520 / far 700; domain night uses the existing 0.83 twilight key so the
frame stays lavender rather than midnight-blue, and `SetNightDarkness` marks the
cycle applied so the next frame cannot wipe the exposure).
Near aims at the approach dais so walking-distance kit fills the 118-unit frame
(play stays 150);
wide/far keep that yaw at 520/700 so the 124 is a solid mass and 6540,6740 sits in the foreground. 720u put the look-at in the fog ramp and the cliff was ~1° of the frame.
Terrain and grass caps outnumber made courts on the current 138,954 platform
cells (103,943 terrain / 24,795 paved / 10,216 reclaimed); the district scale, connected
level axis and long-range shadows work. Raised plates are tan/olive plateaus
with a broken paving stain; the 104 mid-shelf is a 6-deep pad at the 124 toe
(plan z=76–82); the gate sits at plan z=88 so near is shrine and mist rather than
a second precinct. Massif stair treads keep land caps (routes no longer restamp them). Masonry-face bands follow the authored gate Z. Masonry lives on the
cliff faces and the gate overlay.
Natural ground around the gate is ~102, so the 104 approach ramps into that hillside.
124/144 wings fray on a Massif-scale wavelength. Revetment rims carry a sand banding course; the domain
window plants 1,653 trees and streams the ordinary ground-detail mesh. Domain night review
hue now tracks `reference-5` (near RGB 112,71,186 against that image's
122,84,188) without changing play midnight. Late-morning near tracks
`reference-8` in mean colour (205,177,210 against 205,183,238) via a review-only
shrine sky/fog retint; wide/far keep the `reference-1` morning retint. Play day is unchanged.
Atlas samples at the gate and 80 blocks north are height 102, so the 124 cannot
borrow a natural scarp. Wide/far look at the drowned spur into the waterfront cleft (plan −122,−168 / −122,−180)
with FogBegin 1.40× so the E–W cliff, the pool and the inland gate stay solid, not a
haze behind the look-at. Slice E stays open because the site is
not yet an accepted walking-distance match for
`world-new/reference-1`…`reference-11`.

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
