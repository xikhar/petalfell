# Petalfell — Current Implementation State

> **Direction.** Petalfell is set on a continent people have left — see
> `plan.md` §2.1. Settlements now generate as holdouts, remnants, ruins and
> monuments rather than as intact populated places; that conversion is done.
>
> **The current effort is the layer above this file.** Ruins at reference scale
> and a canonical authored map — see [AGENTS.md](AGENTS.md) and
> [docs/ROADMAP.md](docs/ROADMAP.md). Everything below is the substrate that work
> builds on.

Last updated: 28 August 2026

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
  into sixteen by twelve 768-block sectors, with sea level at 40. Its first land
  and elevation blockouts and authoring-time sector compiler exist; the playable
  runtime still does not allocate production terrain.
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
- `./tools/world-authoring.sh audit` validates both the production atlas/profile
  contract and the review-map topology without generating terrain.
  `atlas-preview` writes an SVG of the registered elevation, hydrology and
  categorical-region sources with the 16×12 sector grid and faint province guides.
- `continent/land.png` and `elevation.png` are exact-registration L0 blockouts at
  1,536×1,152 pixels and were accepted by the author on 27 August 2026. The six
  province envelopes are approved working allocation guides rather than physical
  borders. `water.png` and `region.png` are normalized, exact-registration
  image-generated drafts accepted by the author on 27 August 2026; culture,
  abandonment and wilderness remain `Planned`. The audit checks dimensions, PNG encoding, the
  exact region palette and land-mask agreement for water and region.
- `AtlasSectorCompiler` version 3 compiles an arbitrary 768-block sector plus a
  24-block apron into a disposable `PTFLSEC2` artifact and PNG preview. Each cell
  carries terrain/bed height, optional absolute water surface, compiled land,
  authored water value, hydrology class, primary/secondary profiles and blend.
  It derives region transitions, floodplains, banks, oceans, enclosed lakes and
  high-altitude permanent-water cores in global coordinates. Sectors 0,0, 4,2,
  6,4, 8,8 and 15,11 rebuilt deterministically; every tested available east and
  south neighbor matched all 39,168 overlap cells. This remains an authoring
  artifact compiler, not playable voxel terrain, rendered water or a runtime window.
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
  What does NOT yet exist: authored L3 site plans or terracing from arbitrary
  authored polygons.
- **Canonical topology and its first tools exist.**
  `content/chapter_01/world.json` is a versioned absolute-coordinate source for
  domains, sites, entrances, graph nodes and routes. The first production-draft
  southern domain has four named site envelopes and seven connected routes.
  `src/World/CanonicalWorld.cs` performs a strict pre-generation audit;
  `./tools/world-authoring.sh audit|preview` checks it or writes an SVG without
  generating terrain. The in-game map overlays the authored domain and site
  envelopes. Canonical runtime mode stamps only authored route polylines and
  disables procedural settlements, significant landmarks and the sanctum
  fixture. The draft deliberately remains below the 30–60-site target and has
  no structure geometry yet. ([docs/MAP_PIPELINE.md](docs/MAP_PIPELINE.md))
- **The production atlas manifest, first macro blockouts and first sector compiler
  exist.** The selected colour, line and elevation maps are tracked under
  `world-new/map/`; atlas dimensions, sector grid, registered macro layers,
  province polygons and biome build contracts are machine-audited and
  previewable. Land, elevation, generated water and categorical region sources
  are accepted; culture, abandonment and wilderness remain planned. A
  deterministic compiler emits one disposable terrain/hydrology/profile sector
  plus apron and verifies neighbor overlap. The current runtime still owns global
  3,456-square fields; production route/site compilation, voxel-column and water-
  mesh materialisation, and a windowed runtime have not been built.
  ([docs/ATLAS.md](docs/ATLAS.md))
- **Authored site composition is not built.** The topology says where a site is
  and how it connects, but no L3 plan yet supplies its platform polygons,
  levels, walls, stairs or major silhouette. ([docs/RUINS.md](docs/RUINS.md) §4)
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
