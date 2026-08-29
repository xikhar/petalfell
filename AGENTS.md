# Petalfell — start here

A pastel voxel exploration game in Godot 4.7.1 Mono / C#. Long-lens perspective
camera, inked outlines, high-key palette, a continent to walk across.

This file is the index. It tells you what the project is, what phase it is in,
which document owns which decision, and the rules that hold across all of them.
**Read it before touching anything, then read the one or two documents that own
what you are about to change.**

`CLAUDE.md` is a symlink to this file. There is one source of truth.

---

## What the game is

A world people have left. Not destroyed — **left**, by slow decline over
generations. It is beautiful, quiet and enormous, and almost nobody is in it. A
handful of hermits and traders remain, mostly in the cold north. Everything else
is what they built and stopped maintaining.

Wandering is the game. The reward for walking somewhere is the place itself.

## What phase the project is in

**The low-level layer is done.** Rendering, shaders, ink, water, lighting, the
day cycle, voxel storage, chunk streaming, collision, terrain, biomes,
vegetation, fauna, the player, the camera, the dog, inventory. These work to a
standard the author is satisfied with. Do not rebuild them.

**The large layer is beginning.** Three things, in this order, are the whole
current effort:

1. **The production atlas and its sector tools.** Chapter 1 is a 12,288 × 9,216
   logical continent, not a larger value fed to the current global-array
   generator. Painted macro layers and deterministic 768-block sector builds
   make that scale possible. Wilderness is derived inside authored limits.
2. **The canonical authored topology and its production tools.** Every significant
   domain, site, entrance, sightline and road connection has a permanent authored
   identity before detailed geometry is built. Seeds may dress that intent; they
   may not invent or move it.
3. **Ruins and monuments at the right scale**, matching the reference images in
   `world-new/`, produced as authored connected districts rather than isolated
   generated fixtures. The previous builds produced first *"a few bricks lying
   around"*, then one sparse sanctum where the references show domains.

---

## The reference images

`world-new/reference-1.png` … `reference-11.png` are the target for everything
architectural. **Look at them. Do not work from the summaries.** They are cited
throughout the documents by number, and they are tracked in the repository so
that instruction stays honest on a fresh clone.

The short version of what they establish: the unit of a ruin is a *district*, not
a building; the terrain and the architecture are the same stone doing the same
job; tall thin elements (columns, arches, pylons) carry the silhouette and are
entirely absent from the current build; and the whole vocabulary is about twelve
repeating parts.

`world-new/map/map-color.png`, `world-new/map/map-line.png` and
`world-new/map/map-elevation.png` are the selected macro-map references. The
first owns the broad landform/biome read, the second the simple journey-graph
read, and the third the mountain-front, basin and drainage hierarchy. None of
their generated labels, exact roads, contour numbers or landmark density are
canon. Their accepted and rejected lessons are in [docs/ATLAS.md](docs/ATLAS.md)
§1.

**The scenery baseline** — how landscape and architecture are *arranged* so a
large empty world reads — is Shadow of the Colossus, Elden Ring and Skyrim, taken
on the visual axis only and assembled into an original world. What each
contributes is in [docs/RUINS.md](docs/RUINS.md) §8. Petalfell's palette stays
pastel and high-key; none of those three inform colour, and a reference from them
that seems to argue for a darker world is being read wrong.

---

## Which document owns what

Read the one that owns what you are changing. They cross-link; follow the links.

### The new direction

| File | Owns |
|---|---|
| **[docs/WORLD.md](docs/WORLD.md)** | The **story layer**. The continent as a place: its regions, the history that explains their arrangement, the road network, and the rule by which sites are allocated. Sits *above* everything else and touches only parameters. |
| **[docs/ATLAS.md](docs/ATLAS.md)** | The **physical atlas layer**. Production extent, sectors, L0/L1 source formats, deterministic wilderness, and the biome/material/model contracts that realise the story map. |
| **[docs/MAP_PIPELINE.md](docs/MAP_PIPELINE.md)** | **How the map is made.** The canonical-map decision, the L0–L4 layer model, authored versus derived data, the iteration loop, and the authoring-surface options. |
| **[docs/RUINS.md](docs/RUINS.md)** | **How things are built.** Scale targets, the twelve-part kit, authored composition plans and their procedural assistants, terrain-as-architecture, decay and reclamation. |
| **[docs/ROADMAP.md](docs/ROADMAP.md)** | **Settled decisions, build order, open questions, standing lessons.** The file that changes most often. Start here if you are picking up work. |

### The existing project documents

| File | Owns |
|---|---|
| **[plan.md](plan.md)** | The product and creative plan. Long, and the authority on design intent — biomes, remnants, roads, landmarks, flora and fauna, the visual direction, and §11a, the contract for how built things meet the ground. |
| **[ARCHITECTURE.md](ARCHITECTURE.md)** | Engineering decisions and system boundaries. Camera, ink, voxel storage, collision, footings and reclamation, repo layout. |
| **[CURRENT_STATE.md](CURRENT_STATE.md)** | A factual snapshot of what is implemented today. Not a design target. |

**Where they disagree, the newer five win on the new direction and `plan.md`
wins on creative intent.** If you find a real contradiction, say so rather than
picking silently.

---

## Standing rules

**Do not re-litigate settled decisions.** [ROADMAP.md](docs/ROADMAP.md) §1 lists
them. If you think one is wrong, say so in a paragraph and then proceed as
decided.

**The map is canonical.** Chapter 1 is one authored world, not a seed to re-roll.
Do not quietly reintroduce seed-variance for it. ([MAP_PIPELINE.md](docs/MAP_PIPELINE.md) §1)

**The atlas is sector-built.** The production extent is not permission to grow
the current global arrays. All natural fields sample global coordinates and are
compiled in deterministic 768-block sectors with seam aprons.
([ATLAS.md](docs/ATLAS.md) §2–4)

**Significant content is authored.** A generator may realise terrain, repeat a
range, fray paving, scatter rubble or dress wilderness. It may not choose the
location, connection graph, major levels, silhouette or centre of a remembered
place. ([MAP_PIPELINE.md](docs/MAP_PIPELINE.md) §2)

**Authored data is never written by the generator; derived data is never edited
by hand.** If those mix, reproducibility is silently lost.
([MAP_PIPELINE.md](docs/MAP_PIPELINE.md) §3)

**Anything that should read as a region must come from a field with a
wavelength, never a per-block hash.** This has produced confetti three separate
times in this project — wall decay, roof loss, moss patches. Treat it as a law.

**Scale is judged in the game, not in the code.** The previous ruins were an
order of magnitude too small and that was not obvious from reading the source. If
you build something spatial, look at it before reporting it done.

**Bisect and measure; do not reason from symptoms.** A performance problem here
was confidently attributed to the day cycle, measured at six percent, and turned
out to be chunk meshing. Reasoned-about fixes in this project have been wrong at
roughly twice the rate of measured ones.

**Comments carry the reasoning, not the mechanics.** The codebase's convention is
that a comment explains *why a value is what it is* and *what went wrong when it
was something else*. Match it.

### Two things that will bite you

**The colour pipeline.** Every authored colour is sRGB and is converted to linear
exactly once, at definition (`Palette.C`, `Character.Tone`). Skip the conversion
and the pale end of the palette is decoded as if it were already linear, lifts
through the ACES shoulder, and sage grass, cream sand and blossom all render as
white. The pale/dark ink classification is judged on the **sRGB** value, not the
linear one, because the 0.61 luminance threshold was tuned against the hex.

**The noise hash must be unsigned.** The original reference implementation used
JavaScript's `>>>`. Porting that to C#'s `>>` on an `int` sign-extends, clears
the top bit of every negative value, and the field never reaches the upper half
of its range — mean 0.25 instead of 0.5. Every threshold downstream then sits
above everything the field can produce, and the world comes out with one grass
tone, no biome variety and almost no trees.

---

## Keeping these documents true

**These documents are part of the work, not a report about it.** They are the
only context a future session gets, and a stale document is worse than a missing
one because it is believed. Updating them is not optional cleanup at the end —
it is part of finishing a change.

### When to update, and what

| What happened | Update |
|---|---|
| The author decided between options | [ROADMAP.md](docs/ROADMAP.md) §1, plus the doc that owns the subject |
| An open question got answered | Move it out of [ROADMAP.md](docs/ROADMAP.md) §4 into §1, then propagate the answer into the owning document |
| A slice was built and verified | [CURRENT_STATE.md](CURRENT_STATE.md) (what exists now) and [ROADMAP.md](docs/ROADMAP.md) §2–3 |
| A new fact was measured, or an assumption proved wrong | The document holding the wrong number, at source. Do not annotate — correct it |
| A bug class or hard-won lesson emerged | [ROADMAP.md](docs/ROADMAP.md) §5, and the standing rules here if it is general enough |
| The author shifted direction | **All of them, in one pass.** See below |
| A new document was added, or ownership moved | This file's ownership table |
| The project entered a new phase | This file's "What phase the project is in" |

### When direction shifts

The most important case, and the one most likely to be done badly. If the author
says *"we are changing from X to Y"*:

1. **Find every document that asserts X.** Search rather than guess. A direction
   change that leaves one contradictory paragraph behind will mislead the next
   session precisely because everything around it looks current.
2. **Update them all together, in the same session**, before moving on to
   implementation. Do not leave it for later.
3. **Record what changed and why.** Do not silently overwrite a decision — a
   future reader needs to know it was considered and reversed, not that it never
   existed. A settled decision that gets unsettled moves back to
   [ROADMAP.md](docs/ROADMAP.md) §4 with a note.
4. **Then say what you changed**, so the author can check you understood the
   shift the way they meant it.

### Rules for editing these files

- **One fact lives in one place.** Everywhere else links to it. If you are about
  to write the same paragraph into a second document, write a link instead.
- **Respect ownership.** Put a change in the document that owns the subject, per
  the table above. If nothing owns it, say so rather than filing it somewhere
  convenient.
- **Fix cross-references when you renumber.** Sections are cited by number across
  files; adding one silently breaks the others.
- **Never update a document to match broken code.** Documents record decisions
  and intent. If the code disagrees, the code is wrong until the author says the
  decision changed.
- **[CURRENT_STATE.md](CURRENT_STATE.md) is a factual snapshot, never
  aspirational.** Only put things in it that exist and have been seen working.
- **If a change contradicts a settled decision, surface it — do not rewrite it.**
  [ROADMAP.md](docs/ROADMAP.md) §1 is the author's, not yours.
- **Write for someone cold.** The next reader will not have had the conversation
  you just had. State the reasoning, not only the conclusion.
- **High level, not implementation.** These documents say what and why. How lives
  in the code and its comments.

---

## Practical

Run the game:

```bash
godot-mono --path .
```

Default play loads the production atlas window for `shallows-gateway-domain`
with collision and the traveller. `--legacy-world` boots the 3,456-square
fixture. `--play-atlas` and `--play-atlas-domain <id>` select the same path
explicitly.

Audit the authored world topology without generating terrain, or write its SVG
preview:

```bash
./tools/world-authoring.sh audit
./tools/world-authoring.sh atlas-preview
./tools/world-authoring.sh atlas-topology-preview
./tools/world-authoring.sh preview-atlas-domain shallows-gateway-domain
./tools/world-authoring.sh sample-atlas 6400,6500
./tools/world-authoring.sh preview
./tools/world-authoring.sh preview-domain shallows-gateway-domain
./tools/world-authoring.sh compile-sector 8,8
./tools/world-authoring.sh verify-sector 8,8
./tools/world-authoring.sh review-sector 8,8
./tools/world-authoring.sh capture-sector 8,8
./tools/world-authoring.sh review-domain shallows-gateway-domain
./tools/world-authoring.sh capture-domain shallows-gateway-domain
./tools/world-authoring.sh play-atlas
./tools/world-authoring.sh play-domain shallows-gateway-domain
```

The atlas and production-topology previews default under `../shots/`; the domain
preview crops the accepted terrain layers to one permanent L2 domain and labels
its intersecting sectors and, when present, its audited L3 plan. `sample-atlas`
reports the deterministic terrain/water/profile values at one global coordinate.
`preview` and `preview-domain` remain the smaller runtime fixture. A sector
compile writes its disposable `.pfs` artifact under
`content/chapter_01/derived/` and a PNG to `../shots/`. The verifier rebuilds the
sector twice and compares every overlapping apron cell with its east and south
neighbours. A topology warning means the authored production draft is incomplete;
an error means IDs, coordinates or connections are invalid.
`review-sector` opens the compiled terrain at its true atlas coordinates with
walkable collision; use WASD to walk, Space to jump, Q/E to orbit and the wheel
to zoom. `capture-sector` runs the fixed near, wide, reverse and far views and
exits. Both rebuild a missing or stale derived artifact automatically; neither
edits an authored source layer.
`review-domain` and `capture-domain` compose only the sectors intersecting one
audited domain, realise its L2 routes and L3 plan into a disposable blockout,
and add biome-driven wilderness dressing within the plan's authored reclamation
limits. The traveller spawns on compiled ground. The domain capture writes fixed
near, wide, reverse and far views in both late-morning and night light through
the ordinary `DayCycle`; its extended shadow and fog distances are review
framing, not a second art pipeline. The mosaic and geometry remain derived
review data; they never rewrite `topology.json`, a domain plan or an L0/L1 image.

Compile without exporting:

```bash
dotnet build
```

**On NixOS**, the engine's bundled `Godot.NET.Sdk` lives in the nix store under a
content hash that changes on every system rebuild. When the build fails with
`MSB4236: The SDK 'Godot.NET.Sdk/4.7.1' could not be found`, this is why:

```bash
bash tools/setup-nuget.sh
```

For a playable Linux x86-64 release build:

```bash
./tools/build-linux.sh && ./tools/run-linux.sh
```

The export lands in `build/`, which is git-ignored. On NixOS the build script
links the matching Mono export templates out of the nix store into Godot's
per-user template directory, and the run script exposes the package's isolated
graphics and audio libraries to the portable executable — a normal distribution
resolves those through the system linker, NixOS does not.

### The capture rig

The look got as far as it did because every change was judged through a fixed set
of screenshots. Keep that loop:

```bash
godot-mono --path . -- --shots res://../shots/<name> --only farmstead,ruin,tower
```

Omit `--only` for the full set. Shot names and camera framings are in
`src/Tools/Capture.cs`; output lands in `../shots/<name>/` relative to the project
root, alongside `map.png`, a top-down view of the finished heightfield — the
fastest way to tell "the generator is flat" from "the camera happens to be
standing on a shelf". Fixed seed, fixed cameras, fixed frame counts: it must stay
deterministic or it is not a review tool.

The boot log prints world generation timings, remnant counts, footing statistics
and reclamation counts. Read it — it has repeatedly exposed features that never
ran.

### Language and layout

C# is the primary language, measured 21–27× faster than GDScript on the mesher
and generator loops ([ARCHITECTURE.md](ARCHITECTURE.md) §6.1). Repo layout is
[ARCHITECTURE.md](ARCHITECTURE.md) §7.
