# Petalfell — Godot Engineering Architecture

Companion to `plan.md` (from the Three.js reference project). **`plan.md` is the product
and creative plan and stays complete — nothing is removed from it.** This document is
the engineering counterpart: it resolves the technical decisions `plan.md` deliberately
leaves open, and it is the file to argue with when an implementation choice is in
question.

Reference project: `~/Projects/pastel-game` (Three.js). Treat it as **frozen reference
material** — read it, port from it, do not develop it further.

The reference defines Petalfell's visual grammar, scale and traversal feel; it is not a
coordinate contract. Godot maps may—and Chapter 1 will—have new terrain, biome and
landmark layouts. Determinism means a Godot map remains stable for its own seed and map
plan, not that its blocks must coincide with the Three.js demo.

Its two supporting documents are required reading and their conclusions carry over
unchanged:

- `pastel-game/WORLDGEN.md` — the generator's design and its ~15 documented failure
  modes. The most valuable file in either project.
- `pastel-game/ART_DIRECTION.md` — palette discipline, the reference images, and the
  "why" behind the look.

---

## 0. Verified environment

Everything below was checked against this machine, not assumed.

| | |
|---|---|
| Godot | 4.7.1.stable, **Mono build** (`godot-mono`, `godot4-mono`, `godot4.7-mono`) |
| Renderer | Forward+ (`rendering_method=forward_plus`), Jolt physics already selected |
| Blender | 5.2.0 LTS, at `/etc/profiles/per-user/shikhar/bin/blender` |
| godot-ai MCP | live — session `petalfell@bd1e`, plugin/server 3.1.5 |
| .NET SDK | 8.0.423, runtime 8.0.29 — **C# verified working end to end** (see §6) |

Engine capabilities confirmed present via `ClassDB` (these are what the ink system
depends on, so they were checked rather than trusted):

- `CompositorEffect` + `RenderSceneBuffersRD` + `RenderSceneDataRD`.
- The **full raster path is exposed to GDScript**: `render_pipeline_create`,
  `vertex_buffer_create`, `vertex_format_create`, `framebuffer_create`,
  `draw_list_begin/bind_render_pipeline/bind_vertex_array/draw/end`,
  `draw_list_set_push_constant`, `uniform_set_create`, and
  `shader_compile_spirv_from_source` (so the ink GLSL can live in-tree as source and be
  compiled at load).
- `RenderingDevice.BLEND_OP_MAX = 5` — union coverage blending is available.
- `Mesh.ARRAY_CUSTOM0..3`, each up to `ARRAY_CUSTOM_RGBA_FLOAT` → **16 spare floats per
  vertex**.
- `WorkerThreadPool.add_task` / `add_group_task`, `ConcavePolygonShape3D`,
  `HeightMapShape3D`, `NavigationRegion3D`, `MultiMeshInstance3D`, `GPUParticles3D`.
- `BaseMaterial3D` has stencil support (4.5+): `stencil_mode`, `stencil_compare`,
  `stencil_reference`, `stencil_outline_thickness`. **We do not use the built-in stencil
  outline** — it is an inverted-hull silhouette and cannot express our per-edge
  light/dark rules. Stencil stays available for masking if a later effect needs it.

`CompositorEffect` callback stages are `PRE_OPAQUE`, `POST_OPAQUE`, `POST_SKY`,
`PRE_TRANSPARENT`, `POST_TRANSPARENT` — **all of them run before Godot's built-in
post-processing** (tonemap, glow, adjustments). That single fact shapes §2.

---

## 1. Camera — long-lens perspective, not orthographic

**Decision: keep the reference rig. Perspective, ~21° FOV, pitch ~33.5°, yaw snapped to
45°, dolly zoom over a 50–120 unit range, critically-damped follow springs with lead.**
Manual wheel zoom uses the ordinary damped target. K instead starts a linear
distance-per-second dolly to the current maximum distance; wheel input cancels
that move, and the tilde developer surface owns its live speed parameter. Camera
distance is exclusively player/author-owned: terrain and structure collision do
not pull the camera closer or animate it back out. Temporary occlusion beside a
large wall is preferable to an unsolicited zoom every time the player walks past
a pillar.

Reasoning:

- The entire art direction was tuned against this rig. `ART_DIRECTION.md`'s "roughly 25
  blocks across the frame" is a *distance* statement under a 21° lens. Switching to
  orthographic invalidates every existing screenshot as a target.
- A long lens far back already gives the flattened model-railway parallax that reads as
  isometric, while keeping the depth cues that make the diorama feel like a physical
  object. True orthographic reads flat and, at this palette, reads as a tech demo.
- The atmosphere pass depends on perspective: ray reconstruction from depth, the
  analytic height-integrated mist along the view ray, and the AO world-radius →
  screen-pixels conversion (`focal = P[1][1] * height * 0.5`) all assume a perspective
  projection. Ortho needs different math for each.
- Orthographic's real wins — pixel-exact tile alignment, identical silhouettes anywhere
  on screen — matter for a tactics grid, not for a follow-camera explorer with free yaw.
- Dolly zoom keeps "outline width fixed in screen space" a well-posed requirement: the
  stroke is defined in framebuffer pixels and is independent of distance by construction.

Contextual framing (§16 of `plan.md`: conversations, discoveries, interiors) is a
target/distance/pitch override on the same rig, not a second camera mode.

---

## 2. Ink — the outline system

This is the defining feature and the single largest technical risk in the port. It gets
built properly.

### 2.1 The part that must be right: the edge graph

The look does **not** come from a post-process edge detector. It comes from *data*: for
every edge in the world we know its two owning faces, their normals, whether each face
is above the pale-luminance threshold, and whether the fold is concave. That is what
lets us honour `plan.md` §15.3 — light edges only between two camera-facing light
surfaces, concave always dark, dark lines stopping cleanly against light ones.

Port of `pastel-game/src/core/voxel.js:920-1075`, unchanged in spirit:

1. The chunk mesher emits, per face border, an entry keyed on the deduplicated unit
   edge, recording `{normal, isLightFace(color), concave, forceLight}`.
2. Edges with identical topology merge into **runs** — long straight strokes, not
   per-voxel segments.
3. Runs are classified: `light` iff exactly two owning faces, not concave, and either
   both faces pale or one explicitly promotes (grass tops).
4. Joint ownership: at a vertex where a pale run meets dark runs, the pale run keeps its
   round cap and every incident dark run has its cap suppressed, so a dark cap cannot
   protrude half a stroke into the pale line.

This runs inside the threaded chunk mesher and is chunk-local, with midpoint ownership
(`edgeBelongsToChunk`) deciding which chunk emits an edge on a boundary — the reference
already solved the "runs break at chunk seams" problem this way.

### 2.2 Delivery — GPU representation

One `ArrayMesh` of ink quads per chunk, built in the same worker task as the surface
mesh. Four vertices per run, expanded to a screen-space-width quad in the vertex shader.
The 16 spare custom floats hold everything the fragment shader needs:

| channel | contents |
|---|---|
| `ARRAY_VERTEX` | corner offset (±1, ±1) — the quad's local corner, not a world position |
| `CUSTOM0` | `run.start.xyz`, `capStart` |
| `CUSTOM1` | `run.end.xyz`, `capEnd` |
| `CUSTOM2` | `normalA.xyz`, `lightEligible` |
| `CUSTOM3` | `normalB.xyz`, spare (reserved for per-run ink tint / fade) |

Custom AABB is set per chunk since the vertex positions are not real positions.

Screen-space width, backface/normal-facing rejection, the waterline hard stop, and the
distance/mist fade all port from the reference vertex+fragment shader almost literally.

### 2.3 Delivery — where the ink lands in the frame

Three options were researched. What we build:

**Target architecture (the right way).** The 3D world renders into a `SubViewport`. A
`CompositorEffect` at `POST_TRANSPARENT` on that viewport:

- creates its own framebuffer over a persistent **RG8 coverage target** (R = union of
  dark coverage, G = union of pale coverage),
- builds a render pipeline with `blend_op = BLEND_OP_MAX` on that attachment, which makes
  overlapping round caps a mathematical **union** — junctions cannot accumulate, glow, or
  crack open,
- binds the scene depth texture so the ink can do its own tolerant depth test (the
  reference's manual test, which is what stops a stroke being eaten by the face it
  outlines at grazing angles),
- draws every loaded chunk's ink mesh in one draw list.

A fullscreen `CanvasLayer` shader then composites: SubViewport colour → grade (lift /
gamma / gain, split tone, milk, highlight knee) → **ink mixed in last**. Because Godot's
compositor stages all run *before* tonemap and glow, this SubViewport-plus-canvas
structure is the only way to reproduce the reference's ordering, where ink is applied
after tone mapping and therefore can never become an emitter or be caught by bloom.

**Scaffold, days 1–3 of the milestone.** The same ink meshes drawn as ordinary
transparent-pass geometry with a `ShaderMaterial`, doing the manual depth test against
`hint_depth_texture` (transparent materials can *read* the depth texture; they merely do
not *write* into it), `blend_mix`, pale runs at a lower `render_priority` than dark runs
so dark wins at junctions. This is deliberately not throwaway: it shares the edge graph,
the mesh layout and ~90% of the shader with the target, and it gets pixels on screen —
and therefore art review — while the compositor pass is written. Two known deviations,
both retunable: ink is applied pre-tonemap, and overlaps saturate toward the ink colour
rather than unioning (harmless with `blend_mix`, since mixing twice toward the same
colour cannot overshoot it — but it does not give the exact joint behaviour).

**Rejected.** Inverted-hull outlines (no fixed screen width, no per-edge classification),
Sobel/normal-depth post-process outlines (silhouettes only, no light/dark rule, no clean
joints), and `stencil_outline_thickness` (same limits as inverted hull).

### 2.4 Ink for Blender-authored assets

`plan.md` §14 asks for authored buildings, characters, props and kits. **They do not get
edges for free** — the edge graph is a by-product of the voxel mesher, and a glTF mesh
carries none of that data. This is an unlisted dependency of the entire asset plan.

Deliverable, required before the first authored building ships: a Blender-side export
step (or Godot import post-process) that extracts edges by feature angle and emits the
same four-channel run format — the two adjacent face normals, the pale/dark class from
each face's base colour, and a concavity flag from the dihedral sign. Authored assets
then feed the identical ink pipeline, and characters get the §15.3 exception (silhouette
kept, fine internal edges dropped) as a flag on the exporter.

---

## 3. World representation — sector fields, derived voxels, authored geography

**Production decision:** Chapter 1 is a 12,288 × 9,216 × 192 logical atlas,
shown through deterministic sector-aligned bounded windows. Normal play generates
a moving 2×2-sector/1,536-block window with the proven low-level `Terrain`; the
historical compiler remains an explicit integration diagnostic. The atlas is
never represented by one dense continent-wide voxel or column grid. [docs/ATLAS.md](docs/ATLAS.md)
owns dimensions, source formats and sector semantics.

- Chunk footprint remains 24×24 blocks. A sector is exactly 32×32 chunks.
- The current stream radius remains a runtime quality/performance control; it
  does not change compilation or authored coordinates.
- Meshing remains worker-threaded with budgeted main-thread uploads.
- Every procedural field samples global atlas coordinates and an explicit stable
  key. Sector build order and neighbouring-sector availability cannot affect it.
- Runtime morphology uses absolute coordinates and bounded support. Walking or
  map travel replaces the whole window atomically before the old one is detached.

The current `VoxelGrid` already derives deep terrain blocks from per-column cap,
substrate and height plus a sparse placed-block overlay. That representation
remains correct **inside a loaded sector window**. A true overhang is the one
case where the column needs two vertical facts: `Heights` remains its playable
ground/top surface, while a conservative overhang ceiling tells the chunk
mesher/collision how high to inspect sparse roof voxels. This prevents an arch
roof from becoming a false landing or navigation surface. What cannot scale is the
current assumption that `Terrain`, `Planner`, `RoadNetwork` and `VoxelGrid` each
own several `size * size` arrays for the entire continent. At production extent
those arrays alone would occupy gigabytes. The 3,456-square executable is
therefore a review fixture until those fields become sector-local storage.

The voxel store is runtime representation, not map authorship. Each production
map is a content package under `content/`: the atlas manifest registers painted
macro fields and biome build profiles, while canonical topology owns domains,
significant sites, entrances and roads at absolute atlas coordinates. The
compiler consumes fixed intent, supplies deterministic natural infill and emits
disposable sector artifacts. Major features are never inferred from map area.

`MapDefinition.CanonicalAtlasPath` loads and audits the physical production
contract without allocating it. The atlas manifest's `TopologyPath` registers
the separate version 2 rectangular L2 source and validates it against the atlas
extent and province catalog. `MapDefinition.CanonicalWorldPath` still selects
the version 1 3,456-square topology review path; `RoadNetwork.BuildAuthored()`
rasterises those named polylines directly. The procedural settlement search,
significant-landmark scatter and location-seeking review fixtures are legacy-map
behaviour only. `tools/world-authoring.sh` audits and previews both topology
sources before `Planner` or `Terrain` exists; production previews composite L2
over accepted L0/L1 layers and the sector grid. `DomainPlanDefinition` is the
diagnostic domain-plan L3 boundary: a domain-local integer frame holds platforms, absolute
levels, stairs, walls, graph sockets and scale-checked landmarks, while the audit
maps every point back into permanent atlas/site bounds. It is authored input and
is not written by a compiler. Supplied-reference production sites instead use
`ReferenceSiteDefinition` plus `ReferenceSiteGroundPlan`: a strict v2
source-facing integer grid with one cell per voxel, an explicit runtime mirror,
named terrain ownership, exact surface cells, stair landings, thin connected
structure projections, rubble cells and acceptance-camera metadata. The
site-specific builder must touch every declared projection and may add only the
vertical blocks and damage authored for that named mass. Operational procedures
and their evidence limits live in
[`building-knowledge/`](building-knowledge/README.md), not in this engineering
boundary document. `AtlasSectorCompiler` remains the historical
production integration boundary: it reads registered land, elevation, hydrology and
categorical region sources and writes a disposable `PTFLSEC2` artifact for one
768-block sector plus apron. Its per-cell schema is terrain/bed height, optional
absolute water-surface height, land, authored water value, hydrology class,
primary profile, secondary profile and secondary weight. It derives profile
transitions, floodplains, banks and permanent-water beds in absolute coordinates;
the coarse atlas-wide water-component labels remain authoring metadata rather
than block arrays. Its strict reader refuses stale or malformed artifacts.
`ProductionTerrainWindow` is the normal production runtime boundary: it gives
the original `Planner`, `Terrain` and `Vegetation` a bounded atlas guide, wraps
their globally anchored `VoxelGrid` in `AtlasSectorWindow`, overlays contained
reference sites whose canonical status is `Production` or `Accepted`, and
supplies the old water/material system to the full-map handoff shell. Lower site
statuses remain authoring-only and do not reserve vegetation. For diagnostic compiler domain and site windows,
`AtlasSectorMosaic` joins a square set of ordinary artifacts into one temporary
window. The diagnostic southern domain is a 3×3-sector, 2,352-square window;
normal startup uses a moving 2×2-sector-sized generated window around Bloom Grove
Court. Neither path creates a continent-sized persistent allocation.

`DomainPlanBlockout` realises the superseded diagnostic L2/L3 fixtures: route polylines and platform levels,
named terrain/collapse cutouts, stairs, walls and landmarks into that local
voxel window. Cutouts are intentional negative-space records owned by the plan,
not holes inferred by decay. Their collapse depth and their reclamation density,
and every platform's reclamation density, are authored compositional parameters.
Its assistants may repeat authored ranges, fray an edge named `Ragged`, course
made paving, terrace platform margins and dress revetments; they never choose a
site, level, opening, connection or silhouette. None of those assistants are
allowed in a supplied-reference production footprint. Production sites use a
site-specific voxel blueprint and a trivial materialiser; the current southern
plans are superseded tooling fixtures, not designs to persist.
`AtlasDomainDressing` is a
preliminary review pass: registered biome vegetation sets select the grammar, a
global wavelength field selects grove density and a global lattice selects
trees. Authored reclamation changes only where that grammar may encroach on made
ground; the future wilderness source will modulate the surrounding density.
Diagnostic-domain L4 surface patches are authored envelopes: global wavelength
fields choose coherent earth/paving interiors and a sparse global lattice places
rubble, so that plan owns the affected area while generated cells remain
disposable. This assistant is prohibited inside a supplied-reference footprint;
Bloom Grove Court owns exact surface and rubble cells in its audited plan.
`GroundDetail.BuildAtlas` reads the materialised cap, selected profile vocabulary,
global origin and per-column water height from `AtlasSectorWindow`; it emits the
same per-chunk merged mesh and uses the same detail shaders as the legacy terrain
path without allocating a continent-sized `Terrain` graph.
`AtlasSectorReview` sends terrain, a diagnostic domain, or an explicit site
through the existing chunk mesher, ink, atmosphere, water, grade and `DayCycle`;
it never constructs `Planner`, `Terrain` or a continent-sized field. Explicit
review/capture launches are nonplayable and use the same sun, moon, sky and fog
at authored late-morning and night states, with shadow/fog ranges expanded only
far enough for the current review distance. Normal no-flag startup uses a moving
2×2-sector generated window with collision, the player and ordinary chunk
streaming. Before replacing a walking window it compares the terrain, water and
solid occupancy around the current capsule against the overlapping new owner.
That continuity check preserves legitimate water, terrace-edge and
object-adjacent states; teleport landing clearance is a separate contract.
Persistent per-sector structure/navigation artifacts and an author-accepted
walking-distance L4 finish remain outside this boundary.

---

## 3a. Footing and reclamation — how structures join the terrain

`plan.md` §11a is the design contract. This is how it is wired.

Two passes, deliberately separate, because they answer different questions and
run at different times.

**`world/Footing.cs` — the ground contract.** Consumed by every structure
builder before it lays a block. `Footing.Fit()` samples the terrain heightfield
under a footprint and returns the decision: floor level, whether the plan splits
across two terraces, how much earth has banked against it. `Apply()` then
executes that decision against the voxel grid — cutting AIR out of the uphill
side, raising a masonry plinth on the downhill side, banking talus outside the
walls, and cutting an approach ramp so the interior is always reachable.

Three things it must keep in step, or downstream passes silently misbehave:

- `Grid` edits, which are the blocks themselves. A cut writes AIR explicitly;
  the sparse overlay stores it (see §3's note) precisely so a pad carved into a
  slope survives the derivation.
- `Grid.Heights`, which landing, ordinary placement and `Landmarks.Clear` read.
  A cut LOWERS it, which no other pass in the project does. True overhang roofs
  deliberately use `RaiseOverhangCeiling` instead: the chunk mesher/collision
  and vegetation apron use `MeshHeightAt`, while `Grid.Heights` remains the
  floor below the opening.
- `Terrain.Level`, the 2D heightfield that ground detail, fauna and navigation
  read. It follows the cut and fill so grass, tufts and animals behave on the
  new ground rather than on the ground that used to be there.

**`world/Reclaim.cs` — the reclamation field.** Runs AFTER a structure is built,
over its bounding volume. Evaluates damp / shelter / aspect / age per block face
(§11a.3) and does two things with the result: swaps the block's material along
the decay chain, and emits **sprigs** — sub-voxel growth instances — into a
per-chunk bucket.

Sprigs are not blocks. They are rendered by the existing ground-detail layer:
`GroundDetail.Build()` appends every sprig in the chunk to the same `Field` it
builds tufts and pebbles into, so vines and thickets arrive on the same mesh,
the same material and the same wind shader as the meadow, with no new plumbing
in the streamer. The cost is one dictionary lookup per chunk build.

The bucket is written once, during world construction in `Main._Ready()`, and is
read-only for the rest of the session — which is what makes it safe for the
mesher's worker threads to read without a lock.

**Material chain.** `PLASTER → RUBBLE → MOSS_STONE` and `STONE → MOSS_STONE`.
Plaster spalls off first and exposes the rubble core; moss takes the core last.
The chain is legible in-game, which is the point — the player can read how long
a wall has stood by what it is made of.

---

## 4. Collision — real bodies, per-chunk, from the mesh we already have

**Decision: proper physics collision for everything, generated from the mesher output.**

- **Terrain and structures.** Each chunk's opaque surface mesh already exists as vertex
  data; the same buffer becomes a `ConcavePolygonShape3D` on a `StaticBody3D` parented to
  the chunk, created in the worker task and attached on the same frame as the visual
  mesh. Cost is near zero because the triangles are already computed, and it is exact —
  no second definition of the world to drift out of sync. Jolt handles static trimesh
  well. Collision chunks stream with visual chunks; a chunk is never visible without its
  body (`plan.md` §25: "loading never visibly removes ground").
- **Not** one `BoxShape3D` per voxel. That is hundreds of thousands of bodies and it is
  the standard way to make a voxel game unshippable.
- **Player** is a `CharacterBody3D` with a capsule, `move_and_slide`, plus explicit
  step-up: `STEP_HEIGHT` follows the canonical two-block terrain terrace while
  `STEP_SMALL = 1.25` distinguishes a kerb from a full shelf. The split is
  game-feel-critical and is reimplemented rather than
  left to `floor_snap_length`. Acceleration, friction, coyote time, jump buffering,
  variable jump height and the buoyancy/swim model port from
  `pastel-game/src/player/controller.js` with their tuned constants intact.
- **Dog, NPCs, animals** keep the reference's approach: they ride the navigation surface
  and carry an `Area3D` for interaction, with no rigid body. A companion does not need
  gravity, and a second thing to de-penetrate every frame buys nothing.
- **Props, items, throwables** get real bodies — `RigidBody3D` for thrown/dropped
  objects, `StaticBody3D` + `Area3D` for interactables.
- **Navigation** stays the reference's multi-layer voxel-surface A* (terraces, bridge
  decks, swimmable water, step/drop limits, incremental with a time budget). A dry
  cell is traversable only when the placed-voxel overlay leaves two blocks of
  headroom, so a later tree trunk or authored wall cannot become a valid route
  waypoint merely because its underlying terrain column is connected. Godot's
  navmesh baker is the wrong tool here: it fights blocky terraces, it would need
  rebaking as chunks stream, and it cannot express "this character may swim but that one
  may not". `NavigationRegion3D` stays unused for now; per-agent traversal rules
  (§17.3) come from the surface's own cost function.

---

## 4a. The world is post-population — what that changes in code

`plan.md` §2.1 sets the world after a slow human withdrawal. The engineering
consequence is smaller than it sounds, and worth writing down because the instinct is
to assume a rewrite.

**The settlement planner survives almost intact.** Its site scoring — flat ground, fresh
water within walking distance, a province that will grow something — is a model of why
anyone would settle somewhere. Those reasons did not stop being true when the people
left. The same pass now decides where the RUINS are, which means the world's history and
its present come out of one piece of code and cannot contradict each other.

What changes:

- `SettlementKind` stops describing a size and starts describing a **state**: holdout,
  remnant, ruin, monument. Layout code is shared; a decay mask differs.
- The layout work already built (platform terracing, plaza, radial and ring streets,
  market, lots, palisade) is *more* useful ruined than intact — grass through a plaza and
  a wall with fallen sections is a better object than the tidy version, and it is the
  same generator plus reclamation.
- `RoadNetwork` keeps its graph but re-points its anchors at remnants and landmarks. The
  reclamation pass is new: road mask thinned and broken as a function of how long the
  place it served has been empty (`plan.md` §12.4).
- Landmarks are promoted from a `MapDefinition` marker nobody consumes to a primary
  generated layer, because with no settlements they carry orientation and pacing alone.
- `Fauna` gains weight: with nobody about, ambient life is a main carrier of "alive".
- **Deleted from scope:** village populations, NPC schedules, crowd behaviour, shop
  interiors. This is a large saving and it is the reason the pivot is affordable
  alongside §22b.

The one genuine addition is the threat layer, which is M5 and is costed there.

---

## 4b. Implementation status

The mutable build inventory is kept in [`CURRENT_STATE.md`](CURRENT_STATE.md). It is
separate so that this document remains a record of engineering decisions rather than
becoming a mixture of architecture, progress notes, and stale milestone claims.

---

## 5. Scope — build a slice, keep the plan whole

Nothing leaves `plan.md`. The build order below is what gets *implemented first*; every
other system in the plan keeps its section and its future slot.

**M0 — Project skeleton.** Folder layout, project settings (shadows, MSAA, colour),
palette + block registry as Godot `Resource`s ported from `core/palette.js` (single
source of truth, same rule: no colour hardcoded anywhere else), and the capture harness
(§6). Small and fast.

**M1 — The reference scene** (`plan.md` §29). Grass-over-dirt/stone terraces, a cliff, stairs,
concave edges, a tree, a wooden and a stone structure, road, shoreline, shallow and deep
water, player and dog at gameplay distance, lighting/shadow/fog/grade — and the ink,
scaffold first then the compositor pass. This is the visual acceptance gate; nothing else
starts until a capture of this scene stands next to `shots/` and holds up.

**M2 — World foundation.** Port `rng` → `planner` → `terrain-shape` → `terrain` →
`props` → `stamps` → `vegetation`, in that order (the order is load-bearing; see
`WORLDGEN.md` §"Stage ordering"). Streaming, collision, persistence hooks.

**M3 — Traversal.** Controller, swimming, click-to-move with the white hemispherical
pulse, the dog, camera polish, pause/settings/developer view.

**M4 — First authored content.** The Blender edge-export tool (§2.4), the wooden building
kit in intact/shuttered/ruined variants, one remnant, one road loop. This is where the
asset pipeline gets proven on something small before kit production scales up.

**M5 — The wilds** (`plan.md` §22b). One creature, one region of the map, taken all the
way to finished: perception, needs and routine, approach, attack, disengage, and
behaviour toward other creatures; player and creature damage, death and recovery; one
weapon; the dog's warning behaviour; the audio to carry it. This is the largest addition
the plan has taken on and none of it exists today, so it gets its own milestone.

Note the shape this implies. The plan's creature model is ecological rather than
territorial, so `Fauna` is not a separate system from the threat layer — it is the same
system with needs and consequences added. That argues for growing the existing streamed
`Fauna` into it rather than building a parallel enemy system beside it, and for the
streaming radius and population budget being revisited at the same time, since creatures
that have lives must keep having them slightly beyond where the player can see.

Interaction, dialogue, inventory, crafting, trading, map, audio and Chapter 1 story
content stay in `plan.md` and are scheduled after M4.

---

## 6. Tooling and workflow

**godot-ai MCP is the primary interface to the editor** — scene creation and node
wiring, script/shader attachment, running the project, reading logs and errors,
performance monitors, editor screenshots, `game_eval` against the running game, and the
test runner. Bulk authoring of source and `.gdshader` files goes through ordinary file
writes, because those want to be reviewable diffs rather than tool calls; everything that
touches *editor state* goes through the MCP. See §6.1 for the two places the MCP does not
reach under a C# codebase.

**Capture harness — build this in M0, before the second shader is written.** The Godot
equivalent of `pastel-game/tools/shoot.mjs`: a scene that takes a shot list on the
command line, places the camera at named fixed viewpoints (`hero, wide, close, bridge,
river, canopy, cliffs, lowsun`), waits for streaming to settle, and writes PNGs via
`get_viewport().get_texture().get_image().save_png()`. Fixed seed, fixed time of day,
fixed camera values. The reference project's look got where it did *because* this loop
existed; losing it is the most likely way to lose the art direction.

### 6.1 Language policy — C# primary

**Decision: C# is the primary language. GDScript is retained only for `@tool` editor
helpers, the MCP-visible `res://tests/test_*.gd` integration suites, and small
developer-view glue. GDExtension is no longer needed.**

This was measured on this machine, not assumed. The same two loops, run in Godot 4.7.1
headless, one in GDScript and one in C#:

| workload | GDScript | C# | ratio |
|---|---|---|---|
| 5.2 M byte writes + reads | 188.6 ms | 9 ms | **21×** |
| Mesher-shaped face scan, 1.3 M voxels with neighbour lookups and branching | 152.3 ms | 5.7 ms | **27×** |

Verified working end to end: `dotnet build` → `godot-mono --headless` runs a C# `Node`,
.NET runtime 8.0.29, `RenderingDevice.BlendOperation.Max` and `Mesh.ArrayCustomFormat`
reachable from C#. (`RenderingServer.GetRenderingDevice()` returns null under
`--headless` because the dummy driver has no device — expected, not a C# limitation.)

27× removes the entire reason GDExtension was on the table. The mesher, the edge graph,
the generator and the A* are all comfortably affordable in C#.

**Everything is C#, not just the hot parts.** The boundary between hot and cold moves —
dog behaviour is cheap until there are forty NPCs — and a split codebase means
relitigating that boundary constantly while paying variant marshalling at every seam.
One language, one idiom.

Consequences, all of which are accepted rather than worked around:

- **Restart the editor with `godot-mono`.** The editor the MCP is currently attached to
  is the old non-Mono build and cannot open a C# project.
- **NuGet on NixOS needs a `NuGet.config`.** Without it the build fails with
  `MSB4236: The SDK 'Godot.NET.Sdk/4.7.1' could not be found`. The working configuration
  clears `packageSources` and adds the engine's package folder as a
  `fallbackPackageFolders` entry. That path contains a nix store hash and **will change
  on every nixpkgs update**, so it is generated from `$(dirname $(readlink -f $(command
  -v godot-mono)))` in a checked-in setup script rather than hardcoded.
- **The MCP's script surface is GDScript-only.** `script_create` writes `.gd`, and
  `test_run` discovers `test_*.gd`. C# is authored with ordinary file writes (which is
  what we wanted anyway — reviewable diffs) and unit-tested with `dotnet test`; the
  MCP-visible GDScript suites become integration tests that drive C# nodes through the
  scene tree. Everything else the MCP does — scenes, nodes, running, logs, monitors,
  screenshots, `game_eval` — is unaffected.
- **Browser delivery is effectively closed.** Godot 4 cannot export C# to the web; the
  feature was dropped from the .NET 9 milestone and is not scheduled. Only an unofficial
  community build offers it. `plan.md` §31 lists browser delivery as an open question —
  **this decision answers it "no" unless you say otherwise.** Given the reference project
  *is* a browser game, this is the one consequence worth a deliberate yes.

---

## 7. Repo layout

As built. This is the actual tree, not an intended one — the previous version of
this section described a `res://core/ render/ world/` layout that never existed.

```
src/Core/       rng + value noise, palette and block registry, voxel grid,
                chunk mesher, ink edge graph, ink for non-voxel meshes
src/World/      planner, terrain shaping, terrain, roads, settlements,
                landmarks, footings, reclamation, vegetation, ground detail,
                fauna, props, campfires, chunk streaming, map definition
src/Render/     camera rig, atmosphere, day cycle, planar reflection, fire
src/Player/     controller, character, dog, navigation, movement puffs
src/Items/      inventory, item definitions, visuals, world items
src/Gameplay/   interaction layer, fishing
src/Skills/     skill catalogue and system
src/UI/         world map, inventory view, skill selector, HUDs, icon renderer
src/Tools/      capture rig, developer menu
shaders/        voxel, ink, water, waterdetail, sky, grade, character,
                detail, fire, firefly, pulse
content/        chapter/map packages (plan.md §6.2)
docs/           the direction documents — see AGENTS.md
world-new/      reference images. TRACKED on purpose; the documents tell
                readers to look at them rather than trust a summary
addons/         third-party Godot addons
tools/          build, run and NuGet setup scripts
AGENTS.md       the index. CLAUDE.md is a symlink to it
plan.md         product and creative plan
petalfell.csproj
NuGet.config    generated by tools/setup-nuget.sh — see §6.1, nix-store-bound
```

`build/` and `shots/` are generated and git-ignored.

---

## 8. Still open

- **Ink width.** `plan.md` §15.3 calls the ~`3.2` reference the visual starting point,
  but the shipped default in the reference build is `CORE_WIDTH = 1.85` — `3.2` is the
  *maximum* of the settings slider (`inkWidth` 0.16 ÷ `INK_BASE` 0.05). The present
  shared Godot candidate is 1.05 px after a locked Reference 10 day/night review;
  it is not author-accepted, so the canonical default remains open.
- **Grass fringe.** `plan.md` §10 says there is no separate dark-green fringe at a grass
  ledge, but the block registry still defines `fringe`/`fringeColor`. Assumed dropped in
  the port unless corrected.
- **Block scale in metres**, which fixes character height, walk speed and the units every
  future asset is authored against.
- **Interiors** (§31) — seamless, separate scenes, or implied. Affects streaming and the
  remnant kits, so it is wanted before M4, not before M1.
- **Browser delivery** (§31) — choosing C# closes it. Confirm that is acceptable; it is
  the only decision in this document that is expensive to reverse later.
