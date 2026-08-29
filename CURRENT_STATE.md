# Petalfell — Current Implementation State

> **Direction.** Petalfell is set on a continent people have left — see
> `plan.md` §2.1. Settlements now generate as holdouts, remnants, ruins and
> monuments rather than as intact populated places; that conversion is done.
>
> **The current effort is the layer above this file.** Ruins at reference scale
> and a canonical authored map — see [AGENTS.md](AGENTS.md) and
> [docs/ROADMAP.md](docs/ROADMAP.md). Everything below is the substrate that work
> builds on.

Last updated: 29 August 2026

This document records what is present in the Godot project today. It is a factual
snapshot, not a design target or implementation guide. Keep it that way: nothing
aspirational belongs here, and anything listed must have been seen working.

- [`plan.md`](plan.md) owns the product vision, game scope, and long-term goals.
- [`ARCHITECTURE.md`](ARCHITECTURE.md) owns the engineering decisions and intended
  system boundaries.
- This file owns implemented, partial, and missing status.

---

## 1. Runtime foundation

- Godot 4.7.1 Mono project using C# and the Forward+ renderer.
- Jolt is selected as the 3D physics engine.
- The game is assembled in code from `src/Main.cs`; `main.tscn` is the entry scene.
- The default world footprint is **3456×3456 columns** with a voxel height of 76 —
  roughly twenty times the area it began at. That growth was only possible because
  voxel storage became derived rather than dense; see §2. This is now a review
  fixture, not the production-map footprint.
- The authored production atlas contract is **12,288×9,216×192 blocks**, divided
  into sixteen by twelve 768-block sectors, with sea level at 40. Default play
  loads a local window of compiled sectors (the southern gateway domain) with
  collision and the traveller; it does not allocate continent-wide arrays.
  `--legacy-world` boots the 3,456-square review fixture.
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
- `AtlasSectorCompiler` version 4 compiles an arbitrary 768-block sector plus a
  24-block apron into a disposable `PTFLSEC2` artifact and PNG preview. Each cell
  carries terrain/bed height, optional absolute water surface, compiled land,
  authored water value, hydrology class, primary/secondary profiles and blend.
  It derives region transitions, floodplains, banks, oceans, enclosed lakes and
  high-altitude permanent-water cores in global coordinates. Sectors 0,0, 4,2,
  6,4, 8,8 and 15,11 rebuilt deterministically; every tested available east and
  south neighbor matched all 39,168 overlap cells. Channel surfaces are held
  below locally realised banks, including after profile relief.
- The artifact reader rejects stale compiler versions, source fingerprints,
  malformed metadata and invalid cell payloads. `review-sector` materialises one
  sector plus apron into a local `VoxelGrid` at its global atlas origin and uses
  the normal chunk mesher, ink, atmosphere, grade and water shader.
  `capture-sector` writes deterministic near, wide, reverse and far views.
  Default `godot-mono --path .` and `review-domain` spawn the traveller on compiled
  collision at true atlas coordinates.   Representative Cold Shelf (4,2), Scarp
  (3,5), Waist (7,5), Bloom Reach (12,5), Fen (3,9) and Shallows (8,8) windows have been walked
  and captured; biome profiles produce distinct relief, water fraction and grove
  density. The 3,456-square runtime remains available as `--legacy-world`.
- `review-domain` composes the first domain's nine ordinary sector artifacts
  into a temporary 2,352-square window (2,304-block core plus outer apron).
  `DomainPlanBlockout` then realises its exact route polylines, twenty-five platforms
  at Y102/104/105/106/108/110/114/124/144, five named terrain/collapse cutouts, eight fitted stairs,
  nine wall runs and sixty-seven measured landmarks including a through-opening
  `Gate` (span 18) at the grand-stair head, seated between two thin 144 overlay lips so the opening
  is a slot in the massif. Of 138,954 current platform cells, 103,943 retain a terrain or forced-grass
  cap, 24,795 use made paving or warm-stone courts and 10,216 use reclaimed caps; the remaining collapsed
  drowned-court cutout lowers 5,319 cells by its authored depth. A 6-deep 114 west terrace at plan z=78–84
  (plus a 4-deep east hug) steps the 124 south so that drop is two ~10-block masonry slabs with a tan shelf, rather than one 20-block palisade.
  The approach dais is a precinct-scale pale-masonry outer ring at Y105 (~82 across) with a raised Y110 inner court, a south
  notch stair, a gapped 3-high trace ring, an inscribed Standing pylon on the emblem, four buried
  basins on the outer ring, and an east lobe that seats the Broken side-shrine arch. A ragged
  pale-masonry stain (STONE_PALE/PATH with a rubble meander) covers atlas grass in a disk
  around plan (0,−8), cut off north of plan z=16 so it is half-burial of the shrine rather than
  a 104 runway toward the gate. Domain near
  looks at that dais (plan 0,−6, 118 units, pitch 42) so refs 6/7/8/10 are the walking-distance subject and the 124
  is a misted cliff behind them (south face at plan z=84, gate at z=88), not a second precinct in the same frame. The 114 east cheek
  is a short hug against the 124, not a 25-block wing; the 124 east return is an axis-aligned pier stub.
  The 144 crown is two overlay terrace lips behind the lintel (plan z=92–96; 72 and 112 cells) plus a
  north landing for the crown stair (72 cells, same polygon as the west lip), not a mesa or a connecting bar; the 124 court is a
  slot spine (south z=84, north z=140) with an 8-wide stair finger only — the north plateau frays except the
  gate slot around the opening, so it is a tan landform rather than an 18-wide masonry rib — a Y106 east ruin pad at z=100, a planar ~30-wide south face at plan z=84 whose cap is land except the through-slot floor, two 124 hinterland hills whose jagged south edges meet that face a few blocks behind z=84 (west to plan x≈86, east to x≈−88; sheer masonry, not ramped bleachers, and not a second planar south wall at z=84), and a 124 east bank whose south face is a planar east–west waterfront cliff at plan z=−148 with an OpensSouth cleft (20-wide south mouth at the Gate, west arm to the 102 bay) and a 114 shelf the full width of that face (the same 10-block cheek that breaks the gate palisade), a 114 east-face shelf toward the water, a 114 mid terrace that is a wide south shelf behind the near camera (x to −36) then a thin west-edge ledge to plan z=90 so the 520-unit frame sees 102→114→124 beside the cliff, and a 108 revetment hillside west of that ledge that ramps into the 102 plain; the 104 forecourt is a ~28-wide
  processional strip, and the causeway is a Y102 land spine with an eastern lobe of
  paving patches in the authored shallows (atlas water at ~6540,6740). Atlas samples put natural
  ground at ~102 around the gate, so the approach ribbon ramps its outer band into
  that hillside instead of standing as a 10-block canal lock; drowned-causeway rims stay masonry.
  124/144 Deep is one masonry — a per-column moss-stone mix inks a vertical
  between every material change. Slot and stair footprints stay intact. Ragged/submerged terraces, courses,
  buttresses, colonnade lintels, cutout rims, rubble,
  drum fallen-columns and coherent
  wall gaps are deterministic assistants inside authored edge contracts. Platform
  and cutout reclamation values allow the biome grammar to return only where the
  plan permits it. `AtlasDomainDressing` placed 1,653 biome-selected trees from a
  globally anchored lattice in the current nine-sector review, with blossom on the
  hillsides beside the massif and sparse blossom on the 124 hinterland plateaus, not on the 124 wing
  south lips, the 114 cheeks, or the drowned east lobe. Fixed captures
  exercise near, wide, reverse and far views at both late morning and night
  through the ordinary day-cycle rig. The traveller is present in those frames
  at playable scale.
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
  smooth follow, movement lead, and a default distance of 75.
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
  through camera zoom. The current default is 1.30 px.
- Dark ink is a dark plum-grey rather than black. Light ink is a restrained grey-white.
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

- Camera-relative WASD and arrow-key movement.
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

- A standalone developer overlay toggled with the tilde/backtick key.
- Developer sliders control outline width, the minimum and maximum camera zoom
  distances, and the time of day (which pauses the cycle and scrubs it manually).
- **A world map view on `M`.** Renders the whole continent from a byte buffer with
  downsampling, drawing terrain, water, roads and site markers coloured by remnant
  state. `Shift`-click teleports the player to a safe spot at that location,
  priming chunks before the move.
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

- **The Massif process exists and one reference-exact site is built with it,
  as a review fixture, not as world content.** `src/World/Massif.cs` is the
  general additive slab-stack earthworks system ([docs/RUINS.md](docs/RUINS.md)
  §5a): flat-topped noise-warped slabs stacked on a natural summit, final
  height max(slab plan, existing ground) so a site can never dig or moat,
  masonry decks with frayed edges and parapet blocks, stairs notched through
  the slab fronts, fill in the monument's own coursed pale masonry, the land
  beyond the slabs untouched. The summit sanctum (`src/World/Sanctum.cs`) was
  its working case: three tiers (base capping the peak, mid +8, crown +16)
  with satellite slabs shed around the skirt, and the monument on the decks —
  17-wide arched apse with meander glyphs and crystal light, the glowing
  emblem inlaid flush at its foot, a two-flight axis stair down the tier
  fronts, a west deck with five unequal columns and a torn round tower, an
  east deck with two glyph pylons, side stairs off every tier, dressed slabs,
  a fallen column and rubble. Bare stone, no moss. Placed on the most
  prominent summit that leaves vertical headroom for the stack under the
  world ceiling of 76.
  Built from the parametric part library in `src/World/RuinKit.cs`, it was
  reviewed through the `sanctum*` capture shots. Canonical mode now disables it
  because it searches for its own summit; it remains available to legacy
  sandbox maps.
  The earlier kit YARD and flat hand-composed PRECINCT were retired in the
  2026-08 direction shift ([docs/ROADMAP.md](docs/ROADMAP.md) §3): the author
  judged them "very basic" against the references — sites must be multi-layered
  terrain with the landmarks integrated, one built to exact detail at a time.
  The production-domain window now compiles authored L3 polygons and terracing
  into playable collision at true atlas coordinates. What does NOT yet exist is
  an accepted reference-quality site, persisted per-sector structure/navigation
  artifacts, whole-atlas streaming, or final L4 reclamation/detail.
- **Canonical topology and its first tools exist.**
  `content/chapter_01/topology.json` is the version 2 permanent 12,288×9,216
  source for domains, sites, entrances, graph nodes and routes. Its first
  southern domain has four named site envelopes, ten nodes and nine connected
  route segments aligned to the accepted central-delta shelf, water and islands.
  `content/chapter_01/world.json` remains a version 1 3,456-square runtime fixture.
  `src/World/CanonicalWorld.cs` performs a strict pre-generation audit;
  the atlas audit also checks production extent and province references. SVG
  previews show the topology over accepted macro layers and sector boundaries
  without generating terrain. The in-game map and canonical runtime route stamps
  still use only the smaller fixture. The production source deliberately remains
  below the 30–60-site target. Its first domain is the default playable window;
  the remaining continent is not streamed.
  ([docs/MAP_PIPELINE.md](docs/MAP_PIPELINE.md))
- **The production atlas manifest, first macro blockouts and first sector compiler
  exist.** The selected colour, line and elevation maps are tracked under
  `world-new/map/`; atlas dimensions, sector grid, registered macro layers,
  province polygons and biome build contracts are machine-audited and
  previewable. Land, elevation, generated water and categorical region sources
  are accepted; culture, abandonment and wilderness remain planned. A
  deterministic compiler emits one disposable terrain/hydrology/profile sector
  plus apron and verifies neighbor overlap. A read-only production-sector window
  now materialises its voxel columns and multi-height water, with collision and
  the traveller, for the local domain window. The ordinary 3,456-square fields
  remain behind `--legacy-world`. Whole-atlas streaming and persistent route/site
  artifacts have not been built.
  ([docs/ATLAS.md](docs/ATLAS.md))
- **The first authored L3 composition source has a review compiler.**
  `content/chapter_01/domains/shallows-gateway-domain.json` supplies twenty-five
  platform polygons (including a mid-shelf slab at the same 104 as the forecourt, a precinct-scale pale-masonry approach dais with a raised Y110 inner court at the traveller spawn so the near frame holds the shrine, Broken columns and fallen drum, three 114 Massif cheeks against the 124 south face, a narrow 124 slot spine with a west stair finger, two 124 hinterland hills pulled south to the slot as jagged sheer slabs, a 124 east bank with a planar south face at plan z=−148 over the shallows, fused north into the east hill, a 114 east-face shelf (gate-east-face) stepping that bank down toward the water, a 114 mid terrace (gate-east-mid) that is a wide south shelf behind the near camera then a thin west-edge ledge to plan z=90, and a 108 grass hillside (gate-east-rise) west of that ledge, a Y106 east ruin pad, three 144 masses, and   a Y114 east scarp that shelves the 124 waterfront with a channel notch (plan z=−150 to −206) aligned to an OpensSouth terrain cleft in that face — a 20-wide south mouth at the Gate plus a west arm to the 102 bay — and a Y104 masonry spur (`causeway-gate-pier`, 700 cells) through that channel so a drowned processional meets the Gate floor, so a through-opening `Gate` (span 18, height 20 from the 104 cleft floor to the 124 cap) sits in the cliff rather than as an arch on the shelf), five named
  cutouts (two hillside terrain courts on the 124 plateau, one drowned-court collapse, one lower-precinct terrain bay, one waterfront gate cleft),
  eight absolute levels, eight stairs (a 105→110 dais notch, a 104→124 processional notch, a 104→124 side
  notch at plan x=20, a 114→124 waterfront notch into the east-bank south face, a 114→124 east-face notch into that same south face from the water, a 104→124 cleft notch up the north wall of that opening, a 124→144 cheek stair behind the inland gate along plan z=94, and the lower-precinct
  flight), nine wall runs (precinct links with posted stelae, a 6-high Broken trace on the 114 waterfront shelf, wing stubs behind the gate, lower-precinct and stela runs, and a gapped 3-high trace ring around the approach dais), eight route sockets and
  sixty-seven measured landmark placements including a through-opening `Gate` at
  the grand-stair head and a second through-opening `Gate` in a 104 cleft of the east-bank south face, a Stump pylon and fallen drum in that cleft, Stump pylons on the 124 cheeks beside the opening, a buried emblem on the inner dais, an inscribed Standing pylon
  on that emblem, four buried basins on the outer ring, a Broken arch beside
  that dais, and Standing/Broken/Stump columns around the court. Stairs are bitten into the high slab one tread per
  block; they no longer lerp a free-standing ramp across the 104 court. Kit sits
  on the column under its centre, not on the neighbouring terrace. The 124/144 massif is a cliff around the slot with
  two 144 overlay lips plus a north crown satellite rather than a connecting bar; the 104
  forecourt is a ~28-wide processional strip; the causeway is a Y102 land spine with
  an eastern lobe of paving patches in the authored shallows and a Y114 masonry
  scarp standing in that water (atlas samples at 6540,6740 are height 102, water surface 105).
  Camera-facing revetment aprons are skipped so
  downhill of a terrace is hillside, not a second wall. Mid-shelf and upper-court
  outlines are jagged rather than rectangular slabs. The 124 court is a slot plus
  north plateau (plan z=140) so the aerial cap can read as land; 124/144 camera faces keep one masonry Deep (the cliff and the gate
  are the same stone) — grass Deep on a 12-block drop inks as a ribbed palisade,
  and pulling the slab away only revealed the atlas scarp. Massif stair treads
  (high slab ≥120) keep land caps so the overhead near frame is a tan notch;
  masonry stays on the riser Deep. A processional route stamp used to overwrite
  those treads and the 104 mid-shelf with STONE_PALE. 144 drops
  keep mossed masonry at the opening and grow over on the outer lobes. Camera-facing revetment
  lips ramp into the hillside rather than excavating pits. The 124 south edge is a
  hillside bay around the slot, not a 52-wide plinth, and the grand stair is notched through that face.
  A 6-deep 114 west terrace steps that bay so the camera-facing drop is two masonry slabs
  rather than one 20-block palisade; living Deep and hillside ramps stay on the
  104/108 courts. The 144 crown is two overlay lips beside the lintel plus a north satellite for the crown stair rather than a
  connected bar; processional
  columns stay at the 124 gate floor so overlay lintel and masonry cheeks read
  as a hole in the cliff rather than a U on a terrace. The grand stair notches
  that face through to the opening; roof access is a side stair off the axis.
  Standing columns, pylons and arch piers carry a crystal core behind a
  camera-facing groove; inscribed stelae keep a solid face and paint a
  greek-key in rubble so the motif reads at 118 units (STONE on STONE_PALE
  vanished; two-row bars filled the face into a panel, one-gap-per-row read as stairs). Other
  pylons recess the sparse meander one block so ink has something to draw. The gate opening is empty
  through (span 18, height 20 flush with the 144 cap, one-block jamb and lintel) with crystal on the inner jambs only, so the
  camera still sees landscape through the slot. Fallen columns
  are drums with gaps and a displaced capital, not flat bars on the cap.
  Precinct-link walls stand 8–9 as Broken runs with short stelae posted only on those
  links, and their collapse hem leaves 2-high stumps rather than a clean missing run. Grove traces along the causeway were removed; authored water pylons are
  the drowned posts. Surviving paving carries a meander inlay, rubble on the approach dais and on the ragged pale stain around it. Shallow water uses the shader's
  lilac stops (`0x9b8ad4`) so the basin belongs to the same stone as the cliffs.
  Authored collapse depths and platform/cutout reclamation densities
  control where the deterministic assistants may lower, reclaim and plant made
  ground. `DomainPlanDefinition` validates it; the SVG renders it over accepted
  terrain; and the domain window realises the plan through the ordinary voxel
  renderer across sector seams with walkable collision. Fixed late-morning and
  night captures (near/wide/reverse/far) exist under `../shots/atlas-domain-goal/`.
  Atlas samples put drowned water at 6540,6740 — about 280 south and 140 east of
  the gate. Near looks at the approach dais (plan 0,−6; yaw 23, 118 units, pitch 42) so
  walking-distance kit is the subject; the 124 south face sits at plan z=84 (gate z=88) so that
  massif is haze, not a second precinct. Wide 520 and far 700 keep the same yaw and look at the drowned 104 spur into the waterfront cleft (plan −122,−168 and −122,−180) so that opening is the subject (reference-1/2) and the inland massif is hinterland haze; FogBegin stays 1.40× distance. The SW-corner look-at (plan −70,−72) held the E–W cliff, the pool and the inland gate as two monuments 230 apart. Look-at on the waterfront face (plan −38,−154) made the pool the subject and fogged the gate; look-at on the slot (plan −28,24) looked along the N–S west face as a curtain wall; (−90,−20) sat on the ridge. 720u with the look-at on the pool midpoint put a 22-block cliff ~1° into the fog ramp. Raised plates keep the
  tan/olive plateau of reference-1/10; masonry lives on the vertical faces, the
  gate overlay and a broken paving stain. The 104 mid-shelf is a 6-deep pad at the 124
  toe (z=76–82), not a runway behind the dais. Revetment rims carry a sand banding course.
  Court rubble is a few warm stones rather than a lavender cube
  carpet; the grove pass plants 1,653 trees on this window and the chunk
  streamer builds the ordinary ground-detail mesh (tufts, petals, lichen) on
  atlas columns. Domain play streams radius 8 (same as the ordinary game);
  capture still primes the wider review radius. Domain night review uses the existing 0.83 twilight key
  (near mean RGB 112,71,186 against late-morning 205,177,210, vs `reference-5`'s
  122,84,188). Late-morning *near* review retints sky/fog toward `reference-8`
  (205,183,238); wide/far keep the `reference-1` morning retint (175,147,196). Play day is unchanged. Atlas
  samples around the gate are uniformly height ~102, so the 124 is additive architecture
  on a flat plain. The site is a developed walkable blockout, not an accepted production match for
  `world-new/reference-1`…`reference-11`.
  ([docs/RUINS.md](docs/RUINS.md) §4)
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
