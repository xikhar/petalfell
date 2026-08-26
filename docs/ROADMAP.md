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

> **Direction shift (author, 2026-08).** The first pass at slices 1–3 — a part
> yard and a flat hand-composed courtyard — was reviewed against the reference
> images and judged **"very basic"**: simple structures placed together on flat
> ground. The correction, in the author's terms: the references are complex,
> intricate, MULTI-LAYERED TERRAIN with the landmarks built on top of and out of
> it; parts are not one component reused (every stair differs in width, length,
> rise and what joins it); and the mossy green look is dropped — the stone is
> bare pale grey, reclamation returns later as its own layer. The new order:
> **build one full site to exact reference detail, integrated with sculpted
> terrain, walk it, then move to the next.** The yard and the flat precinct were
> retired; their part builders live on as the library. Slices 1–3 below are
> therefore superseded by the one-site-at-a-time slice, kept for the record.

### Slice 1 — the part kit ✅ built, yard retired

Build the twelve parts of [RUINS.md](RUINS.md) §3 in voxels, at the scale in §2
of that file. Nothing composed yet; just the vocabulary, placeable and
inspectable.

*Answers:* do columns, arches, pylons and grand stairs read at reference scale in
this palette and this ink system?

*Status:* built in `src/World/RuinKit.cs`; the parts read at scale and the
meander carves legibly. The review YARD was retired in the direction shift —
parts in a row proved nothing about composition — and the builders became
parametric (column weights 1×1 / 2×2 / 3×3, any height, break states; stairs of
any width and flight plan) so no two placed instances need be alike.

### Slice 2 — precinct terracing ◐ one strong case, not generalised

Generalise footings from one rectangle and one or two floors to several polygons
and several levels, with revetment where levels meet and stairs where they
connect. ([RUINS.md](RUINS.md) §5)

*Answers:* can the terrain conform to a designed multi-level plan, and does the
result read as terrain-that-is-architecture?

*Status:* generalised as the **Massif process**
([RUINS.md](RUINS.md) §5a, `src/World/Massif.cs`): a site's ground is an
additive stack of noise-warped flat-topped slabs on a natural summit — final
height is max(slab plan, existing ground), so a site can never dig or moat —
with masonry decks on the tiers and stairs notched through the slab fronts
before any block is placed. Fill is the monument's own coursed masonry. The
sanctum is the working case. Arbitrary authored polygons
are still to do.

### Slice 3 — one site, built to reference detail ◐ built, awaiting the walk

The reformulated slice: pick ONE reference image and build it to exact detail —
terrain and monument as a single thing, placed where the land wants it — then
**stand in it**, and only then move to the next site.

*Status:* the summit sanctum (`src/World/Sanctum.cs`), corrected against the
reference three times: too small (rebuilt at ~3×), then concentric terrace
rings, then an excavated mesa sitting in a carved-out basin — the author called
out each in turn, the last with the key instruction: *pick a high spot and
build ON it; no gradual slopes, no cutting holes — steep slabs gashed against
each other.* That correction became the Massif process (slice 2 above,
[RUINS.md](RUINS.md) §5a), and the current sanctum is a slab plan built with
it: three tiers (base capping the summit, mid at +8, crown at +16) of warped
sheer-faced slabs with satellite blocks shed around the skirt, masonry decks
on the tiers, and the monument on the decks — 17-wide arched apse with meander
glyphs and crystal light, the glowing emblem inlaid flush at its foot, the
unequal column cluster and the torn round tower on the west deck, the two
glyph pylons on the east. One axis organises it: base court → broad flight →
mid landing → broader flight → emblem → apse, with side stairs leaving every
tier at their own widths and angles, dressed slabs and a fallen column in the
court, and shed stone below. Bare stone throughout — no moss. Built on every
boot on the most prominent summit that leaves vertical headroom for the stack
(the world ceiling is 76), marked on the world map (minty diamond, shift-click
to stand in it), captured through the `sanctum*` shots. The author's walk is
the acceptance test; the site is not yet pinned to a canonical coordinate.

### Slice 4 — the composition grammar

Turn the hand-composed site into a generator driven by a site record: axis,
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

**Parts in a yard prove nothing; composition is the product.** A twelve-part kit
laid out in rows captured well and was still judged "very basic" the moment it
was compared to a reference, because the references are terrain, massing and
variety — not parts. Judge work as a composed place or not at all.

**Nothing repeats.** Every stair in the references differs in width, length,
rise and what joins it; every column differs in weight, height and state. A
builder must take those as parameters, and a site must vary them on every
instance — one part stamped repeatedly is what "very basic" looks like.
