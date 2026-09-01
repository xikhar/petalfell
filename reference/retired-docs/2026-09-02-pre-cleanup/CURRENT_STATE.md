# Petalfell — Current Implementation State

> **Direction.** Petalfell is set on a continent people have left — see
> `plan.md` §2.1. Settlements now generate as holdouts, remnants, ruins and
> monuments rather than as intact populated places; that conversion is done.
>
> **The current effort is the layer above this file.** Finish visual review of
> the map-guided full-atlas terrain runtime, then build the second measured
> reference site and review the resulting two-site world — see [AGENTS.md](AGENTS.md) and
> [docs/ROADMAP.md](docs/ROADMAP.md). Everything below is the substrate that work
> builds on.

Last updated: 2 September 2026

This document records what is present in the Godot project today. It is a factual
snapshot, not a design target or implementation guide. Keep it that way: nothing
aspirational belongs here, and anything listed must have been seen working.

- [`plan.md`](plan.md) owns the product vision, game scope, and long-term goals.
- [`ARCHITECTURE.md`](ARCHITECTURE.md) owns the engineering decisions and intended
  system boundaries.
- [`building-knowledge/`](building-knowledge/README.md) owns evidence-labelled,
  reusable reference-reconstruction procedures and their failure history; it is
  not a status or design authority.
- This file owns implemented, partial, and missing status.

---

## 1. Runtime foundation

- Godot 4.7.1 Mono project using C# and the Forward+ renderer.
- Jolt is selected as the 3D physics engine.
- The game is assembled in code from `src/Main.cs`; `main.tscn` is the entry scene.
- The **3456×3456×76** circular generated world is an opt-in review fixture reached
  with `--legacy-world`; it is no longer normal startup.
- Normal startup is the complete 12,288×9,216 atlas through a moving
  1,536×1,536 bounded production window. `ProductionTerrainGuide` reads the
  accepted land, elevation, hydrology
  and region images at global atlas coordinates. The original `Terrain` path owns
  its six-block local lattice, terraces, cleanup, ledges, gradual banks,
  underwater shelves, columns, vegetation, translucent moving water and ink.
  Production shore response uses a bounded 3/4 chamfer distance, approximating
  the old river/lake Euclidean edge rather than expanding beaches and canyon
  shoulders as Manhattan diamonds; accepted wet identity and surface Y remain
  unchanged. Narrow inland reaches derive their local normal, half-width and
  downhill-oriented side from that same accepted silhouette. The original
  `side * 0.10` bank-selection bias then breaks the two banks differently;
  ocean, wide lakes and junctions retain the symmetric coast/lake grammar.
  A smooth measured transfer expands the accepted elevation band into lowlands,
  a central waist and the northern massif; global ridge fields and the old
  high-ground crowns shape the massif while natural terrain remains within Y191.
  Current verified windows range from 25..46 in the southern lowlands, 25..118
  at the central river and 25..189 in the northern mountain/high-coast windows.
  The categorical region painting still owns province identity, but its declared
  `transitionBlocks` now drives a bounded chamfer field and broad global noise
  that interlock the original discrete biome surfaces and vegetation instead of
  tracing an exact source-colour boundary.
  Low coasts receive broad beaches and visible submerged shelves; selected high
  coasts/rivers use four broad contour shoulders while other runs remain sheer.
  Rare dry highland/snow candidates can add a sparse, terrain-capped erosion
  arch above the heightfield. Its roof has a separate conservative mesh ceiling,
  so rendering/collision see it while landing and navigation retain the real
  floor beneath the aperture.
  The water shader retains legacy refraction, depth and movement and adds broad
  slowly moving variation that remains visible at wide zoom. Its legacy fine
  ripple and sheet stack is unchanged through ordinary play distance and fades
  from 105 to 260 camera blocks, where those sub-pixel wavelengths previously
  aliased into a repeated diagonal print; the broad world-space fields remain.
  Day and early-night wide/far production captures exercise this same shader:
  no special night material is substituted. Current terrain generation takes about 2.1–2.6
  seconds before sites/flora and chunk priming.
  Wetland water beds and saturated low banks use the existing mud palette rather
  than the generic pale sand used by the legacy world's single water vocabulary.
  Production stairs keep the old two-block tread, three-block width and downward
  cut, but their candidates are selected from a bounded global atlas lattice
  instead of the legacy fixture's allocation-wide connectivity labels. This
  makes the same stair repeat in every moving-window overlap while leaving the
  original post-water small-island cleanup in place.
  `--terrain-focus=X,Z` chooses another starting address.
- Bloom Grove Court and Fallen Colossus both have `Production` status and are
  overlaid by the generic production-site pass on the same normal-start terrain
  at `(9800,4600)` and `(10600,4600)`. In the current Bloom window their terrain
  offsets are -70 and -6 respectively; Bloom writes 10,236 surface cells and
  6,876 voxels, while Fallen writes 11,675 surface cells and 1,042 voxels. Their
  plans and sculpture scale are unchanged by the new macro relief.
  The unfinished Shallows Gate remains `Blockout`, so it is inspectable in site
  review but does not reserve vegetation, alter a production window or appear in
  normal play. Future `Production`/`Accepted` sites use this same pass without a
  site-id special case.
- The authored production atlas contract is **12,288×9,216×192 blocks**, divided
  into sixteen by twelve 768-block sectors, with its compiler datum at sea level
  40. The fast map-guided runtime deliberately retains the proven legacy water
  grammar at Y24 while consuming the same accepted horizontal hydrology; its
  terrain still stays below the atlas's Y192 natural bound. The prior
  full atlas map, Shift-click travel and automatic cardinal/diagonal walking
  handoff now regenerate the same old-terrain path at permanent coordinates.
  An observed east handoff rebuilt the neighbouring window and retained the
  exact landing surface. The earlier compiler-backed runtime remains available
  through `--compiled-atlas`; author play review of prolonged traversal,
  collision and swimming remains open.
- `verify-production-terrain X,Z` now exercises the normal moving-window builder
  headlessly. It validates every terrain-data cell, repeats and hashes the full
  1,536-block result, compares promoted-site and natural-formation statistics,
  resolves the same safe landing twice, then compares an adjacent window across
  the 192-block playable safety margin including trees/boulders. Natural
  overhang voxels are compared across the complete visible intersection. The
  mountain, high-coast, high-river, river, lowland, Bloom, Fallen and first erosion-arch
  representative addresses pass; all report zero water-step, severe-step and
  submerged-dry violations. The current bounded-stair matrix additionally covers
  the wet fen at `5107,6620`; fen, mountain, river and lowland each match 442,368
  adjacent-window cells exactly. From the mountain-front Y78 landing, the same
  gameplay surface graph reaches 795,698 land cells across Y42..142 while the
  window retains a separate Y182 summit. The arch case additionally matches 771
  overhang columns and 9,264 voxels in its neighbouring window.
- `audit-production-terrain` has built all 165 possible sector-aligned 2x2
  windows of the normal runtime. Its 3,072 global walking-margin chunks produced
  10,560 owner fingerprints; 5,984 safe terrain comparisons and 14,208 complete
  overhang comparisons matched, and every window retained zero water-step,
  severe-step and submerged-dry violations. The sweep observed the complete
  Y24..189 natural range, four promoted-site build observations and
  389,283,840 window cells, then produced manifest `4575d8bdc98b27bd` in
  415.3 seconds. This proves whole-map bounded ownership and seam behavior for
  the active runtime; it does not visually accept every location or replace the
  representative repeat-build and physical traversal checks.
- `verify-production-playability X,Z land|water` runs the actual playable atlas
  scene headlessly with collision-bearing chunks and the ordinary `Controller`.
  Production route ownership uses the existing cautious walk speed, rejects dry
  cells whose placed tree/site voxels remove two-block headroom, and consumes a
  land waypoint only after the capsule is grounded at its own surface; manual
  WASD remains full speed unless Shift is held. The Y156 summit case at
  `4500,1900` completes 21.57 blocks across Y142..158 in 664 frames, the lower
  mountain at `2692,2164` completes 44.15 blocks across Y72..78 in 571, and the
  high coast at `3904,1312` completes 34.68 blocks across Y47..53 in 585; all
  finish grounded. At the northern high river, `2728,1576` completes a
  36.31-block Y49..53 land leg in 553 frames and then swims 12.10 blocks in 182;
  the central river at `4999,3421` walks 42.40 blocks across Y25..36 in 570 and
  swims 14.05 blocks in 125. At `6400,7360` the capsule completes a 34.61-block
  Y25..27 land leg in 550 frames, enters a genuinely submerged atlas column,
  then swims 10.06 blocks in 203 frames while remaining buoyant at the Y24
  surface. This is
  representative physical evidence; prolonged author input and exhaustive
  terrain traversal remain open.
- The normal production capture matrix includes `atlas_night_wide` and
  `atlas_night_far`. The lowland set at `6400,7360` shows the broad moving depth
  pattern and submerged courses at early night; the northern set at `2728,1576`
  shows the Y24 river remaining legible at the base of the monumental canyon.
  These files were agent-inspected on 2026-09-01. Final reference-level lighting
  parity and author acceptance remain open.
- The project builds cleanly with `dotnet build`, with no warnings.

## 2. Map and world generation

### In place

- A validated JSON map-package format for authored macro geography.
- `content/chapter_01/atlas.json` now defines the production dimensions, sea
  level, sector and chunk registration, seven L0/L1 source layers, three selected
  map references, two generated working references and six province allocation
  polygons. `biomes.json` defines eight
  build profiles with relief wavelengths, surface roles, erosion response,
  vegetation/detail sets, atmosphere/shader profiles, road treatment and
  architecture palette bias.
- `./tools/world-authoring.sh audit` validates the production atlas/profile
  contract, its registered rectangular production topology and the separate
  review-map topology without generating terrain.
  `atlas-preview` writes an SVG of the registered elevation, hydrology and
  categorical-region sources with the 16×12 sector grid and faint province
  guides. `atlas-topology-preview` and `preview-atlas-domain` place permanent
  domains, routes and site envelopes over those same sources and expose affected
  sector addresses.
- `continent/land.png` and `elevation.png` are exact-registration L0 blockouts at
  1,536×1,152 pixels and were accepted by the author on 27 August 2026. The six
  province envelopes are approved working allocation guides rather than physical
  borders. `water.png` and `region.png` are normalized, exact-registration
  image-generated drafts accepted by the author on 27 August 2026; culture,
  abandonment and wilderness remain `Planned`. The audit checks dimensions, PNG encoding, the
  exact region palette and land-mask agreement for water and region.
- The current complete mechanically verified atlas batch uses
  `AtlasSectorCompiler` version 27. It compiles an arbitrary 768-block sector
  plus a 24-block apron into a disposable `PTFLSEC2` artifact and PNG preview.
  Each cell
  carries terrain/bed height, optional absolute water surface, compiled land,
  authored water value, hydrology class, primary/secondary profiles and blend,
  surface class, slope, aspect, curvature and wetness. It renormalises elevation
  over land texels and registers each profile's `cellSize` lattice in global
  coordinates. Bounded noise bands feed continuous non-wind shoulders or crossing
  anisotropic wind ridges. Sparse macro fronts enter the natural field, mode
  filtering and despeckling clean it, and three synchronous global-noise toe-ledge
  passes articulate realised rises as bounded block layers.
  Each independent sector is built with 40 cells of transient support; registered
  hydrology is reapplied after natural relief and cleanup within that supported
  field before the persisted sector is cropped, so oceans, enclosed lakes,
  permanent-water cores, floodplains and banks retain their accepted ownership.
  Every dry cardinal boundary is validated at least one
  voxel above adjacent water. The compiler-27 atlas audit measured 95,291,392 wet
  edges, 329,331 one-voxel stepped edges, zero severe steps (maximum one at
  `635,329`), zero submerged-dry boundaries and zero cross-sector violations.
  All 180 horizontal and 176 vertical seams compared 13,943,808 overlap cells
  with zero mismatches; the independent verifier compared 7,050,240 horizontal,
  6,893,568 vertical and 127,844,352 repeat-build cells without error. This is
  mechanical evidence only; terrain beauty and author acceptance remain open.
- The artifact reader rejects stale compiler versions, source fingerprints,
  malformed metadata and invalid cell payloads. `review-sector` materialises one
  sector plus apron into a local `VoxelGrid` at its global atlas origin and uses
  the normal chunk mesher, ink, atmosphere, grade and water shader. Wet-to-wet
  height changes receive two-sided vertical water curtains while shore edges stay
  open. `capture-sector` writes deterministic play, near, wide, reverse and far
  views. The existing mountain and river capture sets are development
  evidence, not an accepted visual result. The legacy runtime hero capture was
  also checked after sharing its material construction with the review path.
- `AtlasBatchAuthoring` plus `compile-atlas` and `verify-atlas` can resume a
  192-sector build, write height/profile/surface/hydrology composites and compare
  all 356 neighbour seams. The completed compiler-27 batch source fingerprint is
  `a9c535796c83271a06d091691184d1369401b0876ff1fc3b42b3b69542d458a3` and its
  completed batch manifest is
  `44a9b2033bd10fa879de0aa18b100ec84d866c8e17dc9d00cc49671e33c350b0`.
  The live optional compiler source is version 28; no complete version-28 batch
  or independent atlas verification exists, so the checked-in version-27 cache
  is historical evidence and is rejected/rebuilt on demand by that path.
  The historical compiler-16 manifest
  `3f4efd7096e32c5d994087a0d7daa952b51cb1cac68171a5f957129ea3bbaa09`
  remains earlier mechanical evidence, not the current terrain result. Current
  hydrology, seam and independent deterministic verification are recorded in
  [docs/ATLAS.md](docs/ATLAS.md) and the linked terrain knowledge.
- Ordinary production wilderness now comes from data-backed vegetation and
  boulder sets on globally anchored lattices with independent position,
  acceptance and shape salts. It resolves the rendered profile and filters cap,
  water, shore, slope, wetness, height and occupied voxels. Domain-plan and
  reference-site review paths exclude the full conservative canopy/boulder
  footprint, and authored reclamation remains a separate pass. Current sector
  logs demonstrate placement, but a targeted exclusion test and fresh current
  wilderness seam matrix remain open.
- `review-domain` composes the first domain's nine ordinary sector artifacts
  into a temporary 2,352-square window (2,304-block core plus outer apron).
  `DomainPlanBlockout` then realises its exact route polylines, seven platforms
  at Y106/108/114/122, four named terrain/collapse cutouts, three fitted stairs,
  sixteen wall runs and forty-five measured landmarks. Of 185,979 current platform
  cells, 68,675 retain their compiled terrain cap, 96,135 use made paving and
  21,169 use reclaimed caps; two collapsed cutouts lower 11,820 cells by their
  authored depths. Ragged/submerged terraces, courses, buttresses, colonnade
  lintels, cutout rims and rubble are deterministic assistants inside authored
  edge contracts. Fourteen authored L4 surface envelopes now derive 100,194
  coherent reclaimed-earth/broken-paving cells and 85 sparse rubble clusters;
  recessed pylon glyphs and floor meanders remain geometry at far zoom. Platform,
  cutout and surface-envelope reclamation values allow the biome grammar
  to return only where the plan permits it. `AtlasDomainDressing` placed 892
  biome-selected trees from a globally anchored lattice in the current
  nine-sector review. Fixed captures exercise near, wide, reverse and far views
  at both late morning and early full night through the ordinary day-cycle rig.
  Atlas windows now use `GroundDetail` directly with global-coordinate draws,
  profile-owned detail vocabularies and per-column water surfaces; this is the
  same merged detail meshes and shaders used by ordinary play.
- `content/chapter_01/map.json` currently defines:
  - the playable boundary and chapter spawn;
  - six elevation zones and six authored biome zones;
  - two lakes and two connected waterways;
  - remnant and holdout markers;
  - a major road loop, an abandoned road, and a ridge trail;
  - ruin, overlook, shrine, and crossing markers.
- Deterministic RNG, unsigned value-noise hashing, and stable procedural fields.
- A planner that combines the authored map with natural regions and supports meadow,
  forest, plains, sakura, highland, snowy-hill, shore, and wetland biomes.
- Terrain height, land, wetness, river, stair and rock data per column. Blocks are
  DERIVED from a cap, a substrate and a height per column rather than stored; anything
  placed on top (bridges, buildings, canopies) lives in a sparse overlay.
- True natural overhangs retain the ordinary column height as the playable floor
  and carry a separate conservative mesh ceiling for sparse roof voxels. The
  chunk mesher/collision use the higher bound; landing and ground navigation do
  not mistake the roof for terrain.
- Shaped island boundary, macro elevation, stepped contours, shelves, plinths,
  channels, lakes, beaches, stairs, and cleaned terrain transitions.
- The standard walkable terrace is two blocks high. Grass terrain exposes a grass
  cap over either soil or stone; deliberate small changes can remain one block high.
- Biome-aware surface materials, ground detail, trees, canopy forms, flowers, grass
  tufts, petals, stones, and other small decoration.
- Procedural bridges, short approach paths, and lanterns generated from suitable
  river crossings.
- Natural decoration respects the reserved space around authored roads,
  remnants, landmarks, spawns, and generated structures.
- **Roads** as a network rather than a decoration: major, local and trail classes
  built on a coarse lattice with a minimum spanning tree plus deliberate loops,
  A* routed over a terrain cost field with wander noise and corner-cut smoothing
  so routes are not dead straight, bridged where they cross a channel, and
  reclaimed in proportion to how long the places they served have been empty. A
  road disappearing into a wood is a statement that its far end no longer matters.
- **Settlements** generate as remnants at four states — holdout, remnant, ruin,
  monument — assigned by rank so the distribution is controlled rather than
  emergent. A site is planned before roads (so routes can reach it) and built
  after: square, gates, streets, ring street, market, lots, palisade, well,
  signposts. Walls and roofs fail in coherent sections, never block by block.
  Holdouts keep only the two to four buildings nearest the square in repair.
- **Landmarks** are promoted to the primary content layer, since with no
  villages they carry the whole orientation load. Five forms — watchtower,
  standing stones, shrine, farmstead, cairn — each sited by what suits it
  (high ground, old open ground, water or a view, workable land, beside a
  route). Significant ones are planned before roads so trails can be run to
  them; cairns after, because their purpose is to sit beside one.
- **Fauna**: deer, rabbits, goats, birds, butterflies and fish, with hopping
  between terraces, a flee radius with a cooldown so animals are not permanently
  startled, and headings that match the project's facing convention.
- **Footings** (`src/World/Footing.cs`, `plan.md` §11a.1). Structures are no longer
  set on levelled pads. Each one reads the heightfield under its footprint and
  picks the floor that moves the least earth, with fill priced above cut so a
  building sits into its hill rather than on a podium; it cuts the uphill side,
  raises a masonry plinth on the downhill side, banks a soil talus against the
  outside, and cuts an approach ramp so the interior is always reachable. Where
  the footprint straddles a terrace edge the plan SPLITS into two floors joined
  by a step, and the split position is searched rather than assumed so the break
  lands on the terrace edge. On the current chapter map: 39 footings fitted, 12
  of them split, 564 blocks cut against 321 filled.
- **Reclamation** (`src/World/Reclaim.cs`, `plan.md` §11a.3–5). A field over damp,
  shelter, aspect and age, evaluated per block face after a structure is built.
  Two outputs: blocks walk down a material chain (`PLASTER → RUBBLE → MOSS_STONE`),
  and sub-voxel growth instances — moss cushions, hanging vines, ferns, thickets,
  saplings and wall-foot rubble — are bucketed per chunk and emitted by
  `GroundDetail` into the same mesh, material and wind shader as the meadow. Runs
  on every landmark and every settlement building that is not a lived-in holdout.
  Currently ~21,000 growth instances and ~20,600 weathered blocks per world.
- The `Farmstead` landmark is the worked example of both (`plan.md` §11a): a gate
  in a boundary wall that follows the land, a worn path across a yard, a doorway
  that decay may not close, one or two rooms, and a ridged roof that exists only
  over walls still standing to carry it.

### Streaming state

- The full deterministic world data is generated up front; visible chunks stream
  around the player afterward.
- Chunks are 24×24 columns. The default load radius is eight chunks.
- Nearby missing chunks are built nearest-first within a 5 ms per-frame budget.
- The initial area is synchronously primed before the player appears.
- Surface geometry, outline geometry, ground detail, and trimesh collision load as
  one chunk unit.
- Chunks unload beyond the active radius plus a three-chunk buffer.
- Chunk meshing currently runs on the main thread. This is incremental streaming,
  not background worker-thread generation yet.
- The mesher resolves a chunk's whole neighbourhood into a flat local window once
  rather than querying the grid per block. Deriving blocks instead of storing them
  had put roughly two hundred thousand derivations in the inner loop per chunk and
  pushed a chunk build to 15.3 ms against a 5 ms budget that is only checked
  *between* chunks; the window took it to 9.0 ms. Still above budget, so streaming
  a chunk while walking costs a frame — one hitch rather than three.

## 3. Rendering and art direction

### Surfaces and lighting

- Central pastel palette and block registry with authored sRGB colours converted to
  linear space once at their source.
- Flat voxel faces with directional face tinting and baked vertex ambient occlusion.
- Perspective camera with a 21° field of view, 33.5° pitch, 45° orbit increments,
  smooth follow, movement lead, and a default distance of 75. Only wheel input,
  K auto-zoom and the developer controls change camera distance; nearby terrain
  and objects never pull it toward the traveller or cause a delayed recovery.
- Sky shader, depth and height fog, ACES tonemapping, SSAO, selective glow, key and
  fill lights, and four-split directional shadows.
- Fullscreen display-space grade with lift, gamma, gain, split tint, saturation,
  contrast, highlight control, subtle grain, and no chromatic aberration.
- **A full day/night cycle.** One node owns the sun, moon, ambient, fog, glow
  threshold, sky and the shader globals, driven by an interpolated keyframe table
  of sky states with wraparound. A single key light swings through a sun arc built
  around the palette's authored sun direction, becoming the moon on the opposite
  side at night. The sky shader carries cell-hashed stars and a moon disc; lit
  windows and other emissive materials brighten after dark; the ink edge colour
  steps down at dusk and back up at dawn in discrete stages rather than fading
  continuously. Applied on a quantised clock so the sky radiance is not re-baked
  every frame.
- Ambient colour is deliberately cool and only partly sky-derived, with the sky's
  contribution varying across the day — at twilight the sky is a hot orange and
  letting it dominate ambient turned the whole world neon.
- **Water** is screen-space: depth-texture and screen-texture sampling with
  per-channel absorption, Schlick fresnel, a planar reflection viewport, and a
  wave-distortion filter over both the refracted bed and the reflection. Swimming
  has its own animation with transitions in and out.

### Outline system

- Terrain outlines come from an explicit deduplicated edge graph rather than a
  screen-space edge detector or inverted hull.
- Collinear voxel edges merge into runs, chunk-boundary ownership is deterministic,
  concave folds stay dark, and pale eligibility comes from both owning faces.
- The shader makes the final pale/dark decision from the live camera-facing state.
- Outline width is expanded in screen space and remains fixed in framebuffer pixels
  through camera zoom. The current reviewed default is 1.05 px.
- Dark ink is a soft plum-grey rather than black. Camera-facing internal turns are
  lifted toward the restrained pale family while exterior silhouettes retain the
  darker value; both families step with the ordinary day/night amount.
- Analytic one-pixel antialiasing keeps the line body opaque while avoiding jagged
  edges and overlap darkening.
- Junction metadata handles shared endpoints: dark runs normally win, while a vertex
  with at least two visible pale incident runs preserves their connected light joint.
- Manual depth comparison prevents hidden and perpendicular edges from cutting
  through visible faces.
- Underwater ink has a hard stop at the waterline with only a narrow antialiasing band.
- The traveller and dog use `InkBuilder` for selected external/model-defining edges;
  fine internal character edges are intentionally reduced.
- The current delivery path is transparent 3D ink geometry with ordered light and
  dark passes. The persistent coverage-buffer/compositor architecture described in
  `ARCHITECTURE.md` is not implemented.

### Environmental motion

- Biome-aware GPU ambient drift follows the active play area.
- Meadows, forests, plains, sakura groves, highlands, snowy hills, wetlands, and
  shores can select different small leaves, petals, flecks, or reed fluff.
- The effect checks for an appropriate nearby surface and uses small tapered pieces
  rather than the former large rectangular confetti.
- The traveller emits small voxel puffs while walking and separate bursts on jump and
  landing. Walking puffs are intentionally short-lived, light, and close to the feet.
- Campfires: placeable, lit, with their own fire shader, light contribution and
  particle behaviour, sited away from water and unsuitable ground.
- Fireflies at night, on their own shader.

## 4. Player, navigation, and camera

### Direct traversal

- Camera-relative WASD and arrow-key movement. Holding Shift limits grounded
  travel to a deliberate 5.4-block-per-second walk without changing normal speed.
- Acceleration, friction, air control, coyote time, jump buffering, variable manual
  jump height, terminal velocity, and capsule collision.
- Swimming activates in sufficiently deep water, with buoyancy, drag, surface
  movement, and Space used to rise.
- Traversable one- and two-block ledges trigger a physical automatic jump rather than
  teleporting the player to the upper surface.
- Automatic jumps lock their intended crossing direction through the arc. Collision
  response no longer turns the visible character sideways at takeoff.
- Physics interpolation and camera following share the interpolated player transform,
  avoiding the previous double-image/outline ghosting while moving.
- The procedural character has walking, idle, swimming, manual-jump, and auto-jump
  presentation without stretching its body during jumps.

### Destination travel

- Left-click raycasts onto terrain and structures, with water-plane fallback.
- A voxel-surface A* pathfinder supports cardinal and diagonal movement, terrain
  height limits, and water traversal.
- Clicking a reachable point assigns a route; clicking an unreachable point does not
  start false movement.
- A small white hemispherical pulse expands and disappears at the selected point.
- Q and E orbit the camera in 45° steps. The mouse wheel zooms between the active
  minimum and maximum distances.
- K starts a constant-speed camera move to the active maximum zoom distance.
  Manual wheel input cancels it, and the tilde developer panel exposes its
  speed in blocks per second alongside the editable zoom limits.

## 5. Dog companion

- A procedural voxel dog with a corrected forward-facing torso, compact head and
  muzzle, shortened non-clipping ears, four animated legs, collar, and wagging tail.
- The dog follows, heels, wanders, idles, and sits as part of its autonomous behaviour.
- It uses the shared voxel navigation surface when a direct route is blocked.
- Jumpable ledges use an animated arc; excessive rises are rejected and routed around
  instead of being height-lerped or teleported through.
- Forward terrain lookahead begins takeoff before the torso reaches an edge. Landing
  points are placed inside the destination shelf and the higher arc provides clearance.
- Legs tuck during the jump and resume their normal gait afterward.
- A far-distance recovery reposition remains for cases where the companion is lost far
  outside its useful following range.

## 6. Inventory and object gameplay

- A global inventory autoload owns 24 storage slots using stable item IDs and stack
  quantities, ready to be serialized independently of scene nodes.
- Four quick-loadout slots are separate from storage. Plain `1`–`4` selects a slot
  for the right hand; `Shift+1`–`Shift+4` selects it for the left hand.
- `Z` cycles the left hand through available loadout items and empty; `X` does the
  same for the right hand.
- The first concrete item is a stackable, one-handed stick. New games currently begin
  with one stick assigned to quick slot 1.
- The traveller displays equipped items at articulated hand anchors. `F` charges and
  throws the left-hand item; `G` does the same for the right hand.
- Throwing removes the physical copy from inventory and creates a rigid, colliding
  world item. Throw distance and lift follow the time the button was held.
- `R` performs the current shared interaction: collecting the nearest world item back
  into inventory. Its loadout assignment survives while its quantity is zero.
- `U` commands the dog to retrieve the latest thrown stick. The dog travels to it,
  carries it visibly in its mouth, returns, and drops it beside the player for pickup.
- A compact translucent four-socket cross sits in the lower-left. Its left and right
  circles show small vector icons for the currently held items; the top and bottom
  circles are quiet placeholders for future consumable slots.
- **Fishing**, as a system with its own interaction flow and camera handling,
  alongside the general interaction layer.
- **Skills.** A skill definition and catalogue with a system that presents itself
  through the same interaction layer as everything else. The first entry is
  building a campfire. `T` opens a compact transient chooser over the learned
  actions; skill state and execution stay in the system rather than the view.

## 7. Tools and interface currently present

- The legacy interface remains available only in the opt-in **legacy world**.
  Normal atlas startup begins in Bloom's four-sector mosaic and owns its own
  production map plus the existing developer controls; map teleport and ordinary
  walking can replace that bounded mosaic at another atlas address.
- A standalone legacy developer overlay toggled with the tilde/backtick key.
- Developer sliders control outline width, the minimum and maximum camera zoom
  distances, K auto-zoom speed, and the time of day (which pauses the cycle and
  scrubs it manually).
- **A legacy world map view on `M`.** Renders the 3,456-square fixture from a byte buffer with
  downsampling, drawing terrain, water, roads and site markers coloured by remnant
  state. `Shift`-click teleports the player to a safe spot at that location,
  priming chunks before the move.
- **A separate rectangular production-atlas map is wired into normal atlas
  runtime.** `M` opens the 12,288 × 9,216 surface without constructing legacy
  `Terrain`. It validates and reads the current batch profile/height composite
  when present, otherwise reconstructs the background from registered land,
  region, water and elevation sources. Permanent domain and rotated site
  envelopes, labels, routes and the traveller's global atlas position draw over
  that background. `Shift`-click emits exact global X/Z. An in-bounds address
  now reuses or recompiles an edge-clamped four-sector mosaic, applies the same
  reference-site and globally anchored wilderness passes, resolves a deterministic
  dry/traversable landing, primes collision, and swaps only after the replacement
  is ready. A successful Shift-click closes the map and returns input to play;
  rejected requests leave it open, and its pan/zoom plus developer-menu camera,
  ink and day values survive the reload. Water or blocked clicks use a fixed local search, then a registered
  dry-source hint and finally the authored Bloom spawn; every decision is logged.
  This map action remains a synchronous address teleport. Ordinary walking uses
  the stricter continuous handoff below and never invokes these fallback moves.
  The historical compiler-27 `atlas-map-preview` selected the derived
  `atlas-profile.png`. With live compiler source 28 and the checked-in version-27
  manifest, the current preview deliberately uses the registered-layer fallback
  background; `atlas-map-preview` exercises that fallback headlessly, and
  `verify-atlas-handoff` exercises address, edge and landing resolution without
  opening a window.
- A deterministic command-line capture rig writes named review screenshots and a
  top-down heightfield map.
- Boot diagnostics report generation time, height distribution, terrace types,
  surfaces, biomes, flora, noise range, loaded chunk count, remnant state
  breakdown, footing statistics and reclamation counts. **These have repeatedly
  exposed features that never ran** — read them.
- **An inventory view on `Tab`** — deliberately small and translucent, owning no
  item state: every equip and loadout change is delegated to the global
  inventory, so save data never depends on the arrangement of controls. Item
  icons are rendered rather than authored as art.
- **A skill selector on `T`** and a fishing-catch acknowledgement that rises near
  the lower centre and vanishes, so repeated fishing never becomes UI management.
- Both modals take input ahead of ordinary game input and report whether they
  consumed the event.
- Key bindings: `WASD`/arrows move, `Space` jumps, `Q`/`E` rotate the camera in
  45° steps, `1`–`4` set the right hand and `Shift`+`1`–`4` the left, `Z`/`X`
  cycle hands, `F`/`G` throw, `R` interacts, `U` sends the dog, `M` opens the
  map, `Tab` the inventory, `T` the skill selector, and tilde the developer
  overlay.
- There is still no pause menu or settings menu.

## 8. Defined but not yet realized as gameplay content

The three largest items that used to sit here — the settlement decay layer, the
road network, and landmarks as a content layer — are **built**, and are described
in §2. What remains is the layer above them, and it is the current effort. See
[AGENTS.md](AGENTS.md) and [docs/ROADMAP.md](docs/ROADMAP.md).

- **The old Massif/Sanctum path remains only as a legacy diagnostic.**
  `src/World/Massif.cs`, `src/World/Sanctum.cs` and `src/World/RuinKit.cs`
  demonstrated additive slab operations and reference-scale vocabulary, but the
  resulting summit fixture was never reference-exact production content. It
  searches for its own summit, stamps parametric parts and is disabled in
  canonical mode. Supplied-reference sites instead use measured plan-owned
  terrain and unique site-owned voxel blocks. The legacy path remains available
  only in the opt-in sandbox world; it must not be cited as the construction
  method or visual evidence for Bloom Grove Court.
- **Canonical topology and its first tools exist.**
  `content/chapter_01/topology.json` is the version 2 permanent 12,288×9,216
  source for domains, sites, entrances, graph nodes and routes. Its first
  southern domain has four named site envelopes, ten nodes and nine connected
  route segments aligned to the accepted central-delta shelf, water and islands.
  `content/chapter_01/world.json` remains a version 1 3,456-square runtime fixture.
  `src/World/CanonicalWorld.cs` performs a strict pre-generation audit;
  the atlas audit also checks production extent and province references. SVG
  previews show the topology over accepted macro layers and sector boundaries
  without generating terrain. Legacy route stamps still use only the smaller
  fixture; the production atlas has its separate full rectangular map described
  in §7. The production source deliberately remains below the 30–60-site target.
  Normal startup realises the explicit Bloom Grove
  Court over production terrain; the superseded southern-domain geometry remains
  a tooling fixture and does not enter normal play.
  ([docs/MAP_PIPELINE.md](docs/MAP_PIPELINE.md))
- **The production atlas manifest, accepted macro sources and current sector compiler
  exist.** The selected colour, line and elevation maps are tracked under
  `world-new/map/`; atlas dimensions, sector grid, registered macro layers,
  province polygons and biome build contracts are machine-audited and
  previewable. Land, elevation, generated water and categorical region sources
  are accepted; culture, abandonment and wilderness remain planned. A
  deterministic compiler emits one disposable terrain/hydrology/profile sector
  plus apron and can verify neighbor overlap. A read-only production-sector window
  now materialises its voxel columns and multi-height water for visual review.
  The old game owns global 3,456-square fields only in `--legacy-world`. The
  active production site now has player traversal, collision and chunk streaming
  over a moving four-sector-sized old-terrain allocation. The atlas-map
  Shift-click path can generate and recenter that runtime at any in-bounds address. Its deterministic
  fallback rejects locally flat but stranded shelf fragments unless their
  traversal component contains at least 4,096 cells and reaches 48 blocks; low
  islands may still qualify through connected swimming water. A formerly
  stranded request at `6172,1460` now recovers two blocks away and the real
  controller walks 33.62 blocks across Y49..53 before settling grounded. Walking now
  requests the deterministic one-sector
  neighbour before the eight-chunk stream circle reaches an unsafe edge; corners
  shift both axes. Persistent route/site artifacts remain open.
  The fully batch-verified compiler-27 baseline includes land-aware elevation
  sampling, globally registered profile cell lattices, bounded noise bands,
  continuous non-wind shoulders, crossing anisotropic wind ridges, sparse macro
  fronts, mode filtering, despeckling, three synchronous global-noise toe-ledge
  passes, 40-cell transient support and hydrology reapplied after natural
  relief. It retains registered bidirectional bank shaping, absolute water
  surfaces, dry/water clearance validation, cap/cliff/shore classification,
  slope/aspect/curvature/wetness metrics and altitude/slope/moisture profile
  resolution. Its review path adds coherent cap-material regions and ordinary
  globally anchored tree/boulder dressing. Its 192-sector hydrology audit has no
  severe water step, submerged dry boundary or cross-sector invariant failure;
  all 356 seams match, and the independent full rebuild is deterministic.
  Targeted wilderness exclusion proof, an atlas-wide wilderness matrix,
  persistent reach direction/width and talus/scree fields, broad multi-biome
  visual review and author acceptance do not yet exist, so production terrain
  remains active rather than finished.
  ([docs/ATLAS.md](docs/ATLAS.md))
- **The rejected southern L3 tooling fixture remains available for diagnostics.**
  `content/chapter_01/domains/shallows-gateway-domain.json` supplies seven
  platform polygons, four named terrain/collapse cutouts, four absolute levels,
  three stairs, sixteen walls, eight route sockets and forty-five measured landmark
  placements for the connected southern domain. Its authored collapse depths
  and platform/cutout reclamation densities control where the deterministic
  assistants may lower, reclaim and plant made ground. Fourteen L4 surface
  envelopes author coherent earth caps, broken paving and collapse rubble without
  storing generated cells. `DomainPlanDefinition`
  validates it; the SVG renders it over accepted terrain; and the domain review
  realises the plan through the ordinary voxel renderer across sector seams.
  Fixed day/night captures prove the large axis, levels, context and long-range
  shadows exist. Atlas-native ground detail, stronger edge masonry, arches,
  colonnades, rubble, glyph recesses and reclamation make the district legible,
  but walking-distance microarchitecture and local decay remain below the target
  references. It is a developed blockout, not an accepted production site.
  The author superseded its blended original composition on 29 August 2026 and
  rejected the later generic `reference-1` portal attempt for the same visible
  kit-like regularity. Neither is canon. Production now begins with an explicit,
  site-specific `reference-10` voxel transcription in Bloom Reach; no generic
  architectural builder or procedural dressing is allowed inside its footprint.
  ([docs/RUINS.md](docs/RUINS.md) §4)
- **Reference 1 now has a strict source-facing plan and one canonical atlas
  identity; its 3D reconstruction is not built yet.** The former monumental
  gate, water causeway, lower precinct and stela-field topology records were
  retired on 30 August 2026. `topology.json` now owns the single
  `shallows-gate-and-causeway` district at `(6400,6980)`, with a measured south
  landing, north exit and one continuous Strand-to-Spine processional route.
  `shallows-gate-and-causeway-reference-1-plan.json` records twelve terrain
  shapes, five surface-patch groups, forty-four unique structure records,
  1,129 structure-projection cells, 3,033 visible site-owned terrain cells, two
  stairs, nine exact rubble clusters, keep-open regions, tree exclusions and
  twenty measured tree anchors at one cell per voxel. Its audited 1254×1254
  source overlay has been inspected against the supplied top view. Runtime
  rotation/reflection, the locked camera, vertical courses, site definition,
  voxel builder, collision and rendered fidelity remain open, so the canonical
  site status is honestly `Planned`.
- **Reference 12 is built as a permanent production-site hybrid.**
  `fallen-colossus` is registered at `(10600,4600)` in a dedicated Bloom Reach
  domain. Its current strict plan places four sculpture projections in a broad
  orthogonal ruined precinct: seventeen terrain records form ordinary-atlas
  channels, four detached lower/upper/crown stacks and a substantially enlarged
  central three-course leg plinth; six exact stairs, four site-owned broken
  foundation traces, four exact rubble fields and eight spread-out 2×2 pillars
  occupy those levels. Sixteen non-overlapping surface-patch groups provide the
  broad warm/cool/moss/worn breakup; the discarded Meshy ground/debris remains
  absent.
  `tools/prepare_meshy_fallen_colossus.py` removes the
  calibration cube, image-floor geometry and baked materials from the author's
  `meshy/head.glb` and `meshy/legs.glb`, then writes bottom-centred site assets.
  Normal startup, map reload and walking handoff attach those assets with the
  world-space Petalfell stone shader, inverted-hull plum outline and invisible
  compound box collision. The legs now use 1.5× their first imported review
  scale, matching 1.5× compound collision, while the head remains unchanged.
  The v25 locked and far captures were inspected for that scale, material
  replacement, reduced 0.009-unit silhouette ink, forward-facing leg yaw,
  enlarged support terrain, orthogonal partial slab stacks, surface wear and the
  spread outer pillar/foundation rhythm;
  traversal, all-angle fidelity and author acceptance remain open.
  Its authored registration also thins ordinary biome trees to 22% immediately
  outside the precinct and smoothly returns to ordinary Bloom density over 140
  blocks; boulders and vegetation elsewhere remain unchanged.
- **The first explicit reference transcription is an active Blockout under
  reconstruction; author acceptance remains open.** `bloom-grove-court` owns a
  strict source-facing v2 ground plan and unique voxel builder at atlas
  coordinate `9800,4600`. The current plan has 28 terrain records, 24 explicit
  surface-patch groups covering 1,893 cells, 31 structure records (including two
  stairs and ten exact rubble clusters covering 119 cells), and eighteen tree
  anchors. Its raised east precinct is three detached y114 slabs with
  lower-terrain channels at source z=-10..-9 and z=17..20. The central slab keeps
  its pre-widening boundary, while the occupied ruin reaches only source x=40
  through a broken low L return and two interior low remnants. Structures and
  surfaces do not bridge either channel.

  The current v16 correction removes the graded east stair-side shoulder, caps,
  and rubble; splits the south-west wall into two connected masses around the
  open passage at source x=-11..-10, z=-9..-6; makes all four central 2×2 pillar
  shafts start at y116 continuously above the y114 base and y115 stylobate
  course; and adds a y107 southern approach whose y107/y108/y109 walkable
  surfaces produce two rises into the y109 lower court. One-cell-wide wall,
  shoulder, and stele remnants remain separate structure classes rather than
  mixed-width pillars. Source/runtime plan audits, the world audit, and
  `dotnet build --no-restore` pass for this current data and builder.

  Python and C# audits enforce one cell per voxel, the explicit runtime mirror,
  terrain ownership, stair connections, thin connected runs and runtime
  projection parity. The locked comparison is 1672×941 at yaw 135 degrees and
  true-isometric pitch 35.264 degrees. The complete v13 matrix remains historical
  evidence for the unchanged channels, central occupied footprint, reverse
  support, and far extent. The v16 locked day, true top, and four play-distance
  quarter-turn views have been inspected for the four corrections above. Those
  findings are claim-scoped; a current complete close/play/wide/far matrix,
  whole-site fidelity, collision/playability review, and author acceptance remain
  open.
  The current evidence and remaining visual
  uncertainty live in the
  [site knowledge ledger](building-knowledge/sites/bloom-grove-court.md);
  no whole-site `author-accepted` claim exists.
- **Production map/developer integration and bounded continuous walking handoff
  are mechanically wired.** The Bloom startup owns the
  rectangular atlas map above and attaches the existing tilde developer menu to
  its actual ink materials, camera and day cycle. In-bounds global Shift-click
  can atomically replace the four-sector window and move the existing player to
  a deterministic safe surface; authored-footprint containment prevents a reload
  inside Bloom from clipping its voxel blueprint across the wrong sector pair.
  Ordinary walking triggers 192 blocks before a core edge and atomically shifts
  the window by one sector, or diagonally at a corner. It preserves the exact
  global transform, velocity, input, camera, day, map and developer nodes; primes
  collision first; and requires the old and new owners to describe the same
  terrain, water and 3×3×5 solid neighbourhood around the current body. It does
  not apply the map teleport's dry, flat 3×3 spawn test to an already-moving or
  swimming player. A genuine continuity mismatch retains the old window and
  latches that safety boundary until the player returns through a 288-block
  rearm band; a 45-frame cooldown prevents thrash.
  Build plus headless map-teleport checks pass at Bloom's `9800,4591` and the
  Reference 1 address `6400,6980`. The walking verifier passes four cardinal and
  four corner transitions, one partial outer-edge transition, one outer refusal,
  90 cooldown-suppressed repeats and one rearmed return. At `6400,7360`, the
  production overlap audit accepts all 1,148 real trigger-line cells; the old
  spawn validator would have rejected 698 of them (470 water and 228 dry), which
  is the measured source of the former invisible handoff walls. The live overlay/gesture
  and post-reload collision/movement path have not yet received GUI review.
- **A first shared time-responsive ink/high-key-lighting pass is built and
  visually reviewed, but not reference-parity or author-accepted.** Internal
  camera-facing turns are quieter than silhouettes, the default line is 1.05 px,
  both ink families re-ink through dusk/night, shadow opacity and grade contrast
  are lower, and the post-twilight key now takes the moon's colour as well as its
  direction. The fixed Bloom v2 late-morning/night captures were inspected for
  line hierarchy and night readability. Current site geometry, stone weathering,
  shadow softness at the source view, other biomes/distances, and Reference 1
  remain outside that evidence.
- **The story layer.** Regions with roles, domains, and site allocation by
  meaning rather than by fit. ([docs/WORLD.md](docs/WORLD.md))
- Biome identities affect terrain, flora, ground detail, and airborne detail, but the
  complete biome-specific encounters, resources, audio, and weather do not exist.

## 9. Major game systems not yet present

- The living: hermits, traders and named characters, plus dialogue and trading. Note
  that populations, schedules and crowd behaviour are no longer in scope at all — see
  `plan.md` §20.
- General NPC, structure and artifact interaction beyond world-item pickup,
  fishing and campfire building.
- Consumables, tools, weapons, crafting, trading, and two-handed item behaviour.
- Quests, chapter progression, discoveries, and finished Chapter 1 narrative content.
- Save files and persistent world-state changes.
- Audio and music systems.
- Pause, settings and accessibility.
- Blender-authored production assets and their final import/outline pipeline —
  deliberately deferred until the composition layer exists
  ([docs/RUINS.md](docs/RUINS.md) §7).
- The wilds: creature AI, damage, death, weapons, and everything else in `plan.md`
  §22b. None of it exists; it is milestone M5.
- The target compositor-based union-coverage outline renderer.
