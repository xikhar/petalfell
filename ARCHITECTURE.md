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

## 3. World representation — voxel storage, authored geography

**Decision: as requested, keep the reference model. A single dense voxel grid for the
whole map (one byte per block) plus per-chunk meshing, streaming, and unloading around
the player.**

- Chunk footprint 24×24, full world height, matching `CHUNK = 24`.
- Stream radius 8 chunks (`DEFAULT_STREAM_RADIUS`), exposed in the developer view per
  `plan.md` §25.
- Meshing on `WorkerThreadPool` tasks with a per-frame budget for uploads, mirroring the
  reference's `voxel-worker.js` + incremental `pump(deadline)`.
- Determinism: the in-tree `Rng` and value-noise implementation is the stable randomness
  boundary. Do not silently substitute an engine noise source: Chapter maps must remain
  stable for their own seed even though they do not match the browser demo's coordinates.

The voxel store is runtime representation, not map authorship. Each map is a content
package under `content/` whose normalized definition owns its boundary, macro elevation
zones, biome zones, major lakes and waterways, plus remnant, road and landmark anchors.
The planner consumes that fixed intent and supplies deterministic natural infill. Major
features are never inferred from map area unless that map explicitly requests additional
procedural counts.

Cost of this choice, stated plainly so it is not a surprise later: the dense grid is a
global allocation and it is what caps world size. At `WORLD.height = 76` that is ~45 MB
at 768 blocks square, ~80 MB at 1024, ~320 MB at 2048. **1024 is the practical ceiling**,
which at this block scale is roughly a square kilometre of playable region — comfortably
"considerably larger than the current world" per §8. If Chapter 1 ever needs to exceed
it, the escape hatch is already identified in `WORLDGEN.md`: keep the 2D layers (heights,
surface, wetness, plan) globally and materialise voxels per tile. We are not building
that now; we are keeping the mesher's inputs narrow enough that it stays possible.

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
- `Grid.Heights`, which the mesher, the vegetation apron and `Landmarks.Clear`
  all read. A cut LOWERS it, which no other pass in the project does.
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
  decks, swimmable water, step/drop limits, incremental with a time budget). Godot's
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
  *maximum* of the settings slider (`inkWidth` 0.16 ÷ `INK_BASE` 0.05). One of these is
  canon and everything downstream gets tuned against it.
- **Grass fringe.** `plan.md` §10 says there is no separate dark-green fringe at a grass
  ledge, but the block registry still defines `fringe`/`fringeColor`. Assumed dropped in
  the port unless corrected.
- **Block scale in metres**, which fixes character height, walk speed and the units every
  future asset is authored against.
- **Interiors** (§31) — seamless, separate scenes, or implied. Affects streaming and the
  remnant kits, so it is wanted before M4, not before M1.
- **Browser delivery** (§31) — choosing C# closes it. Confirm that is acceptable; it is
  the only decision in this document that is expensive to reverse later.
