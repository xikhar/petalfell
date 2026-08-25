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
| **Site count** | 30–60 sites for Chapter 1, mixed: a few great districts, more precincts, many small marks. |
| **Starting point** | Scale and the part kit first — before any authoring tooling. |
| **Direction** | Post-population. A continent left by slow decline, not catastrophe. (`plan.md` §2.1) |
| **Resolution** | Voxel for now. Authored meshes deferred until the composition layer exists. |

The reasoning behind the first three is in [MAP_PIPELINE.md](MAP_PIPELINE.md) §1
and [RUINS.md](RUINS.md) §7; the reasoning behind the ordering is §3 below.

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

Each slice is chosen to answer a question. The order exists so that expensive
decisions are made *after* the cheap experiments that inform them.

### Slice 1 — the part kit

Build the twelve parts of [RUINS.md](RUINS.md) §3 in voxels, at the scale in §2
of that file. Nothing composed yet; just the vocabulary, placeable and
inspectable.

*Answers:* do columns, arches, pylons and grand stairs read at reference scale in
this palette and this ink system?

### Slice 2 — precinct terracing

Generalise footings from one rectangle and one or two floors to several polygons
and several levels, with revetment where levels meet and stairs where they
connect. ([RUINS.md](RUINS.md) §5)

*Answers:* can the terrain conform to a designed multi-level plan, and does the
result read as terrain-that-is-architecture?

*Cannot be deferred* — even the smallest first composition needs it.

### Slice 3 — one precinct, hand-composed

Build a single `world-new/reference-10`-style courtyard, roughly 80 blocks: a
bounded court, a level change, a broad stair, a standing arch at the axis head, a
colonnade range on one flank, ragged paving, wall stubs tracing rooms. Placed at
a fixed coordinate. Then **stand in it**.

*Answers:* the whole question. Does the scale feel right? Does the composition
read as a place? Is it worth walking through?

*Why this and not a district:* it is the smallest thing containing every
compositional idea, and if it fails we learn that in a day rather than a week. A
district is the same grammar with more precincts on an axis.

### Slice 4 — the composition grammar

Turn the hand-composed precinct into a generator driven by a site record: axis,
level hierarchy, boundary, centre, part ranges, decay state. Re-rollable, with
pinnable seeds.

*Answers:* how much of a site plan can be generated from intent, and what does a
site record actually need to contain — which is the input to the authoring
format.

### Slice 5 — one district

Four to six precincts on a shared axis at 250–350 blocks, `reference-11` scale.

### Slice 6 — the map and story layer

Only now: painted continent layers, region definition, the site record format,
authored roads, domains. [WORLD.md](WORLD.md) and
[MAP_PIPELINE.md](MAP_PIPELINE.md) become implementable.

*Why last:* the authoring format should be designed once we know what a site plan
contains. Designing it first is guessing at fields.

### Alongside, early — partial regeneration

Rebuild one site's region without regenerating the continent. Not a slice of its
own but it should land early: authoring 30–60 sites means hundreds of
regenerations and the full path already takes several seconds.
([MAP_PIPELINE.md](MAP_PIPELINE.md) §6)

---

## 4. Open questions

Genuinely undecided. Do not silently pick an answer; raise them.

**Block scale in metres.** The references put the player at two blocks. If a
block is a metre then a 250-block district is 250 m, which is right for a real
archaeological site, and the current map is about 3.4 km. Is that enough for 30–60
sites plus meaningful wilderness, or should the canonical map be larger now that
voxel storage is derived rather than dense?

**World height.** Currently 76. A five-level district with 30-block columns and a
mountain behind it starts to squeeze. Worth checking before the vertical
vocabulary is committed to.

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
