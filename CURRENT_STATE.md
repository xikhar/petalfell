# Petalfell — Current Implementation State

> **Direction change.** Petalfell is now set on a continent people have left —
> see `plan.md` §2.1. Settlement generation described below still produces intact,
> populated-looking places; converting those to holdouts, remnants and ruins is
> pending work, not a description of the current build.


Last updated: 23 August 2026

This document records what is present in the Godot project today. It is a factual
snapshot, not a design target or implementation guide.

- [`plan.md`](plan.md) owns the product vision, game scope, and long-term goals.
- [`ARCHITECTURE.md`](ARCHITECTURE.md) owns the engineering decisions and intended
  system boundaries.
- This file owns implemented, partial, and missing status.

---

## 1. Runtime foundation

- Godot 4.7.1 Mono project using C# and the Forward+ renderer.
- Jolt is selected as the 3D physics engine.
- The game is assembled in code from `src/Main.cs`; `main.tscn` is the entry scene.
- The current default map seed is `20260820` and the default world footprint is
  768×768 columns with a voxel height of 76.
- The project currently builds successfully with `dotnet build`. The remaining
  analyzer output is four existing `CA2014` warnings in `ChunkMesher` concerning
  `stackalloc` inside loops.

## 2. Map and world generation

### In place

- A validated JSON map-package format for authored macro geography.
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

## 3. Rendering and art direction

### Surfaces and lighting

- Central pastel palette and block registry with authored sRGB colours converted to
  linear space once at their source.
- Flat voxel faces with directional face tinting and baked vertex ambient occlusion.
- Perspective camera with a 21° field of view, 33.5° pitch, 45° orbit increments,
  smooth follow, movement lead, and a default distance of 75.
- Sky shader, depth and height fog, ACES tonemapping, SSAO, selective glow, sun and
  fill lights, and four-split directional shadows.
- Fullscreen display-space grade with lift, gamma, gain, split tint, saturation,
  contrast, highlight control, subtle grain, and no chromatic aberration.
- A world-sized stylized water plane with shoreline/depth colouring.

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

## 7. Tools and interface currently present

- A standalone developer overlay toggled with the tilde/backtick key.
- Developer sliders currently control outline width and the minimum and maximum camera
  zoom distances.
- A deterministic command-line capture rig writes named review screenshots and a
  top-down heightfield map.
- Boot diagnostics report generation time, height distribution, terrace types,
  surfaces, biomes, flora, noise range, and loaded chunk count.
- There is currently no game-facing HUD, pause menu, settings menu, inventory UI, or
  map UI in the active project.

## 8. Defined but not yet realized as gameplay content

The Chapter 1 map package already reserves and identifies several future features, but
the markers are not the same as completed locations.

- Settlement generation exists and produces terraced platforms, plazas, streets,
  markets, lots, cottages and palisades — but it builds them INTACT. The pivot to a
  post-population world (`plan.md` §2.1) needs the decay layer: reclamation, shuttering,
  collapse, and the holdout/remnant/ruin states described in `plan.md` §11.
- Road and trail markers exist; the complete authored road network is not rendered.
- Landmark markers exist and nothing consumes them. Under the new direction landmarks
  are the primary content layer (`plan.md` §13), so this is now the largest single gap
  between the plan and the build.
- Biome identities affect terrain, flora, ground detail, and airborne detail, but the
  complete biome-specific fauna, encounters, resources, audio, and weather do not exist.
- Generated river bridges exist, but the broader authored structure and building kits
  do not.

## 9. Major game systems not yet present

- The living: hermits, traders and named characters, plus dialogue and trading. Note
  that populations, schedules and crowd behaviour are no longer in scope at all — see
  `plan.md` §20.
- General NPC, structure, artifact, and contextual interaction systems beyond world-item pickup.
- Full inventory management, loadout assignment UI, consumables, tools, weapons,
  crafting, trading, and two-handed item behavior.
- Quests, chapter progression, discoveries, and finished Chapter 1 narrative content.
- Save files and persistent world-state changes.
- Audio and music systems.
- Pause, settings, accessibility, and final game-facing UI.
- Blender-authored production assets and their final import/outline pipeline.
- Ruins, abandoned buildings, monuments and the authored road network.
- The wilds: creature AI, damage, death, weapons, and everything else in `plan.md`
  §22b. None of it exists; it is milestone M5.
- The target compositor-based union-coverage outline renderer.
