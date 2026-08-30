# The World Atlas — scale, sectors, biomes and natural infill

> **What this file owns.** The physical production canvas below the story map:
> atlas dimensions, coordinates, sectors, L0/L1 source layers, the boundary
> between authored landform and derived wilderness, and the profile contract by
> which a biome selects terrain, surfaces, vegetation, atmosphere and road
> treatment.
>
> **What it does not own.** What happened in each province
> ([WORLD.md](WORLD.md)), where named sites and roads go
> ([MAP_PIPELINE.md](MAP_PIPELINE.md)), or how a district is composed
> ([RUINS.md](RUINS.md)).

---

## 1. The three map references and what is accepted

The author selected `world-new/map/map-color.png`, `world-new/map/map-line.png`
and `world-new/map/map-elevation.png` as the reasonable macro-map direction.
They are **macro composition references**, not raster sources to trace literally.
This applies only to the three map images. The structural
`world-new/reference-*.png` set has the opposite contract for the current phase:
each production site is a measured reconstruction of one of those images.

`map-color.png` establishes:

- a broad 4:3 continent rather than a showcase island;
- large, immediately readable climate and material masses;
- a drowned and fragmented south, a cold northern crown, a stone/quarry belt,
  a green river basin and a blossom-rich eastern country;
- enough land and water for long wilderness intervals between built domains.

`map-line.png` establishes:

- a small number of strong named anchors connected by a legible route graph;
- a north-south historical grain, with the drowned delta as the old southern
  threshold and the surviving country toward the north;
- secondary islands and coastal fragments that can hold optional journeys;
- the value of seeing the complete world as a simple document before rendering.

`map-elevation.png` establishes:

- a real altitude hierarchy rather than a north-to-south colour gradient: the
  north-west and north-central crowns are highest, the north-east quarry basin
  is secondary, and the central/eastern inhabited country stays lower;
- mountain fronts and basin rims as connected masses with rivers occupying
  legible cuts between them;
- a staged descent through central basins into a very low drowned south, where
  archipelago fragmentation follows the relief rather than random coast noise;
- the need to judge routes against slope and drainage instead of drawing a
  graph over terrain after the fact.

The generated labels, towns, exact coastline, contour values, spoke count and
uniform landmark density are **not accepted as canon**. Petalfell is emptier
than these images. Routes may converge, but the complete network must not become
a theme-park hub where every road is a spoke to one centre. The story roles and
names remain the ones owned by [WORLD.md](WORLD.md).

---

## 2. Production extent and coordinates

The working Chapter 1 production atlas is:

| Property | Value |
|---|---:|
| Width | **12,288 blocks** |
| Depth | **9,216 blocks** |
| Vertical extent | **192 blocks** |
| Sea level | **40 blocks** |
| Aspect | **4:3** |
| Sector size | **768 × 768 blocks** |
| Sector grid | **16 × 12** |
| Existing chunk size | **24 × 24 blocks** |
| Coarse source resolution | **8 blocks per pixel** |
| L0/L1 source image size | **1,536 × 1,152 pixels** |

At the current one-block-to-roughly-one-metre reading, this is a large walking
continent rather than a single level. The exact metre fiction is less important
than the ratios: a 250–350-block district is visible but small on the atlas, a
768-block sector can contain one connected domain, and wilderness can occupy
several sectors without becoming filler.

The coordinate contract is permanent:

- origin `(0, 0)` is the north-west corner;
- `+X` is east/right on the atlas;
- `+Z` is south/down on the atlas;
- orientation is clockwise from `+Z`;
- all authored sites, route nodes and domain vertices use atlas block
  coordinates, never percentages or sector-local addresses.

A sector address is only a build and cache key. Moving a sector boundary must
never move a place.

---

## 3. Why the atlas is sector-built

The current 3,456-square runtime keeps many two-dimensional arrays for the whole
map: height, land, water distance, wetness, road masks, biome fields and column
descriptors. Extending those arrays to this atlas would require several
gigabytes before meshes, structures or navigation existed. A larger number in
`Main.WorldSize` is therefore not a production implementation.

The atlas is compiled in 768-block sectors with an apron wide enough for noise,
hydrology, roads and structures to cross a seam. A sector build receives global
coordinates, samples the same authored layers and stable fields as its
neighbours, then discards the apron. Adjacent sectors must produce identical
shared-edge columns without communicating or depending on build order.

Each derived sector is keyed by the hashes of:

- the atlas manifest and relevant source-layer pixels;
- intersecting region, route, domain and site records;
- the biome and material profiles they reference;
- the explicitly scoped wilderness/dressing seed;
- the compiler version.

Changing one site rebuilds the sectors its envelope and approach routes touch.
It must not alter a distant wilderness sector. The existing 3,456-square world
remains a review fixture until the sector compiler and windowed runtime replace
its global arrays; it is not the production atlas at a smaller scale.

---

## 4. Authored land, derived wilderness

The rule is not "authored sites on procedural terrain." At macro scale, terrain
already carries intent.

**Authored:**

- land/sea silhouette and principal islands;
- coarse elevation masses, mountain fronts and large basins;
- major watersheds, lakes, deltas and river corridors;
- province and culture fields;
- wilderness-intensity and abandonment fields;
- every named road, causeway, domain, site, entrance and sightline;
- site levels, platform polygons and terrain directives inside remembered
  places.

**Derived deterministically:**

- local relief below the authored elevation wavelength;
- terrace-edge irregularity, scree, boulders and small erosion channels;
- biome-valid minor ponds, wet patches and snow pockets;
- vegetation, flowers, fallen branches, ambient fauna and other wilderness
  dressing;
- coherent wear, rubble and reclamation within authored limits;
- unbuilt foot trails where the story layer explicitly allows them.

Noise never decides whether a mountain, road, river, district or landmark
exists. It varies the surface of a fact already present. Anything intended to
read as an area uses a continuous field with a declared wavelength; per-block
hash thresholds are reserved for genuinely granular detail.

---

## 5. L0/L1 source layers

The source layers are small, versioned images edited in an ordinary paint tool.
They are not screenshots of generated terrain.

| Layer | Format | Meaning |
|---|---|---|
| `land` | 8-bit grayscale PNG | ocean, coast transition and land |
| `elevation` | 16-bit grayscale PNG | coarse absolute terrain target |
| `water` | 8-bit grayscale PNG | standing water and watershed influence |
| `region` | indexed/RGB PNG with manifest palette | primary province/build profile |
| `culture` | indexed/RGB PNG with manifest palette | architectural culture influence |
| `abandonment` | 8-bit grayscale PNG | age/reclamation bias |
| `wilderness` | 8-bit grayscale PNG | permitted natural-detail intensity |

All use the same 1,536 × 1,152 pixel registration. The production compiler must
sample them in global atlas coordinates, blend region boundaries over a declared
transition width and add only profile-bounded variation. A painted layer is
authored and is never rewritten by generation. The current compiler implements
global sampling, transition-weight derivation and hydrology shaping; §10 records
the exact boundary it has reached.

The current `water` contract is continuous rather than a binary river mask:

- `0` is dry land with no hydrology influence;
- `1–239` is increasing drainage, wet-valley, bank and floodplain influence;
- `240–255` is reserved for permanent-water cores; and
- every pixel outside the accepted land mask is exactly `255`.

The current `region` contract is categorical. Black is ocean/unassigned and the
six province values are `#d9dcf1` Cold Shelf, `#c7ae9e` Scarp, `#aebf91` Waist,
`#e6b6ca` Bloom Reach, `#8fae9e` Fen and `#9eb9d8` Shallows. The atlas audit
rejects other colours, unassigned land and assigned ocean. Smooth province
transitions are derived during sector compilation from this categorical intent
and each province's `transitionBlocks`; the source itself stays exact and editable.

Compilation keeps the two kinds of derived information explicit:

- a region cell stores primary profile, secondary profile and secondary weight;
  the weight reaches one half at the categorical boundary and falls to zero over
  the owning province's `transitionBlocks`;
- a hydrology cell stores the continuous authored water value, a dry/floodplain/
  bank/channel class, terrain or bed height, and an optional absolute water-surface
  height;
- edge-connected black in `land.png` is ocean at sea level; enclosed black
  components are lakes held below their lowest surrounding authored bank; and
- white land at `water >= 240` becomes a permanent river/lake core whose surface
  follows the local authored valley before profile noise. Lower values only lower
  floodplains and banks toward that guide; they never raise terrain.

Biome profiles own floodplain/bank thresholds, rises, surface drop and bed depth.
Those parameters vary how a province realises the accepted watershed; they do
not move it.

Each registered layer has an explicit review lifecycle: `Planned` means no
usable source exists, `Blockout` means the source can be compiled and judged but
is not canon, and `Accepted` means the author approved it in the whole-atlas and
sector review loops. Both non-planned states must exist at the registered path
and exact dimensions. The audit continues to warn on blockouts so an unfinished
coastline or field cannot become production truth by accident.

---

## 6. Province and biome build profiles

A province is a story region; a biome profile is how it is realised. They are
related but not interchangeable. One province may blend two profiles, and one
profile may appear as a small exception elsewhere.

The first atlas blockout carries six working province roles:

| Province role | Atlas position | Primary realisation |
|---|---|---|
| **Cold Shelf** | northern and north-west crown | snow, exposed pale stone, sparse alpine growth |
| **Scarp and quarry belt** | western and upper stone country | highland shelves, cut faces, scree, extraction roads |
| **Waist** | central river confluence | meadow, forest margins, navigable water and the Great Work |
| **Bloom Reach** | eastern country | sakura canopy masses, meadow clearings, long sightlines |
| **Fen** | south-western low country | wetland, slow water, reed islands and pile causeways |
| **Shallows** | southern delta and archipelago | shore, drowned paving, warm shallow water and monumental domains |

These are working spatial roles. [WORLD.md](WORLD.md) owns their final names and
history.

Every biome build profile selects, by semantic ID rather than hard-coded shader:

- macro and local relief ranges and noise wavelengths;
- terrace step and erosion response;
- cap, substrate, cliff, shore and underwater surface sets;
- vegetation and ground-detail sets with density ranges;
- water, fog, wind and airborne-detail parameter profiles;
- road-edge and reclamation treatment;
- architecture palette bias, without changing the culture's part vocabulary.

Profiles parameterise shared systems. There is not one terrain shader per biome,
one road shader per province, or a forked generator for the snow. A new mechanism
belongs in the common system; a different look belongs in profile data.

---

## 7. Roads, erosion and the ground contract

**Road geometry is authored.** A route record owns its nodes, polyline, class,
width and construction profile. The sector compiler realises its cut, fill,
steps, bridge or causeway and reclamation against local terrain. Noise may fray
an edge or vary damage; it may not reroute the road.

**Erosion is geometry first, material second, shader last.** A washed bank or
collapsed road must change the walkable silhouette and expose the correct
substrate in the voxel field. Material selection supplies stone, soil, moss,
snow and wetness. Shaders add continuous wetness, wind, water response and
distance-stable surface variation; they do not fake a missing ravine or repair
an implausible road cut.

The same applies where architecture meets terrain. Site plans own levels and
platform boundaries; the compiler resolves masonry, bedrock, banked soil,
stairs and rubble as one ground operation before dressing either side.

For supplied-reference footprints, the repeatable procedure and its evidence
limits are maintained in the
[terrain-integration building knowledge](../building-knowledge/terrain/terrain-and-detached-slab-integration.md).
This atlas document owns the physical boundary; the live reference and audited
site plan own the particular shelves, channels and revetments.

---

## 8. Geometry, material and shader formats

| Content | Authored source | Derived/runtime form | Rendering contract |
|---|---|---|---|
| Wilderness terrain | painted L0/L1 layers + biome profile | sector height/surface fields and voxel columns | existing voxel shader and palette/material table |
| Roads and ordinary causeways | route spline + construction profile | fitted voxel surface, cuts/fills and crossing records | shared voxel/wetness/reclamation path |
| Supplied-reference site | unique site blueprint plus locked reference camera | explicit terrain and voxel runs, collision and navigation | no reusable architectural generator inside the measured footprint |
| Minor non-reference architecture | authored template or later Blender source | voxel placement or imported `.glb` with socket metadata | shared material family, outside reconstruction footprints only |
| Vegetation and props | reusable set definition; Blender source where needed | instanced geometry/MultiMesh and block forms | shared wind/ground-detail shaders, biome-selected parameters |
| Water | painted water/hydrology intent | sector water surfaces and crossing data | existing water shader, profile-selected colour/fog response |
| Decals and tiny dressing | profile ranges and stable field keys | instanced local detail | subordinate to silhouette and ink readability |

`.glb` is the runtime exchange format for Blender-authored geometry. Blender
source remains editable source, and socket/semantic metadata remains text. No
hero place is allowed to become one opaque mesh with its terrain and connections
trapped inside it.

---

## 9. Authoring and acceptance loop

1. Edit atlas metadata or a registered source layer.
2. Audit dimensions, palettes, profile references and coordinates.
3. Preview the complete atlas with its 16 × 12 sector grid and province overlay.
4. Compile only affected sectors plus their seam aprons.
5. Inspect the domain in the game at walking distance and the fixed far cameras.
6. Accept or revise the authored source; never patch the derived sector.

The macro map passes when the province masses, drowned south, route grain and
intentional empty intervals read without labels. A sector passes when its seams
are invisible and rebuilding it leaves non-intersecting sector hashes unchanged.
A domain passes only when its terrain, roads and structures read as one place at
normal play distance and at the established far review distances.

Reference-site work additionally follows the scoped measurement, plan and
capture methods indexed in
[`building-knowledge/`](../building-knowledge/README.md). Those entries must be
updated with fresh evidence when a correction changes a site; an old capture
cannot prove the current authored plan.

---

## 10. Current status

The three reference maps are tracked. The exact-registration land and elevation
images, with sea level 40, were accepted by the author on 2026-08-27 as the L0
basis. The six province envelopes were accepted as working allocation guides,
not as physical borders; the painted `region` layer now owns the organic province
masses. The atlas audit validates dimensions and PNG encoding, and
the whole-atlas preview uses the registered elevation source rather than the
reference once that source exists.

The manifest also registers the separate version 2 `topology.json`. Its audit
requires the same 12,288 × 9,216 extent and valid province ids. The first
southern gateway domain is permanently placed across sectors 7–9 in both axes at
the central delta threshold. `atlas-topology-preview` draws all L2 records over
the accepted macro sources; `preview-atlas-domain` crops that composite and
labels every intersecting sector. Sector boundaries report build ownership only
and do not alter the domain boundary, site axes or route geometry.

The built-in image-generation tool used the three references plus the accepted
land/elevation sources to propose a connected hydrology system, then used that
result to propose six terrain-shaped regions. Those proposals were deterministically
resized, constrained to the accepted land mask and reduced to the contracts in
§5. The raw proposals are retained under `world-new/map/` for provenance; the
registered `water.png` and `region.png` were accepted by the author on 2026-08-27.
Their exact prompt set and normalization record live beside them in
`world-new/map/GENERATED.md`; the generated proposals themselves are not canon.

Compiler version 4 consumes land, elevation, water and region, blends
profile-bounded relief in global coordinates, shapes floodplains and banks, and
emits the `PTFLSEC2` artifact plus PNG. Each cell carries terrain/bed height,
optional absolute water-surface height, compiled land, authored water value,
hydrology class, primary profile, secondary profile and secondary weight. Region
transitions use a deterministic coarse distance field expanded beyond the sector
by the largest declared transition, with a 96-block continuous modulation limited
to 24 blocks so the eight-block source pixels do not become visible bands.

Enclosed authored water bodies receive altitude from their surrounding accepted
elevation; generated permanent-water cores follow a five-source-pixel local
valley guide. The floodplain and bank passes lower terrain toward that guide
before terrace quantization. A channel surface is also constrained below the
locally realised valley, so profile relief cannot leave compiled land submerged.
Sectors 0,0, 4,2, 6,4, 8,8 and 15,11 rebuild to
stable hashes. Every independently compiled east/south neighbor that exists
matched all 39,168 overlapping apron cells per edge, including every new field.
The coarse atlas-wide water-body label is authoring-time source metadata, not a
continent-scale block array.

The strict artifact reader rejects old compiler versions, stale source
fingerprints, malformed dimensions and invalid cell payloads. `review-sector`
materialises one artifact plus apron into a sector-local `VoxelGrid` at its true
global atlas origin, resolves profile surface IDs, streams the ordinary voxel
and ink meshes, and renders all compiled water elevations through the existing
water shader. `capture-sector` judges the same window at four fixed distances.
Mountain, confluence and drowned-south windows have been inspected in the game;
the legacy 3,456-square runtime also passed its fixed hero capture after the
shared material construction was extracted.

Culture, abandonment and wilderness images remain planned. Normal startup now
opens a fixed 2×2/four-sector production-atlas mosaic at Bloom Grove Court with
the player, collision and ordinary chunk streaming inside that mosaic; the old
3,456-square generated world is available only through `--legacy-world`.
Explicit sector/domain/site review and capture modes remain nonplayable review
surfaces. Dynamic handoff to arbitrary neighbouring mosaics, persistent
route/site/navigation output, an accepted wilderness-density source and
multi-elevation planar reflections do not yet exist. `sample-atlas <x,z>`
compiles one addressed source point on demand so authored absolute platform
levels can be chosen against deterministic terrain rather than the grayscale
map.
