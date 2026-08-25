# Ruins — the architectural language

> **What this file owns.** How built things look and are put together: the scale
> they must work at, the kit of parts, the rules that compose parts into places,
> how they fall down, and how the land takes them back.
>
> **What it does not own.** Where any of it goes ([WORLD.md](WORLD.md)) or how
> the map is authored ([MAP_PIPELINE.md](MAP_PIPELINE.md)).
>
> The reference images in `world-new/` are the target. They are cited throughout
> by number and should be looked at, not summarised.
>
> Read [AGENTS.md](../AGENTS.md) first if you have not.

---

## 1. The diagnosis this document exists to fix

The build before this direction produced ruins that read, in the author's words,
as *"a few bricks lying around."* That was accurate, and the cause was not art
quality. It was three things, in order of severity:

1. **The unit was wrong.** A ruin was one small building. In every reference
   image the unit is a *district* or at minimum a *precinct*.
2. **There was no vertical vocabulary.** No columns, no arches, no pylons, no
   stairs of consequence. The tallest element was a seven-block chimney.
   Everything that creates a silhouette was simply absent.
3. **Terrain and architecture were separate systems.** Structures were written on
   top of a finished heightfield. The references show them as the same material
   doing the same job.

---

## 2. Scale

Taking the player character as **two blocks tall**, measured off the reference
images:

| Element | Reference | Previous build |
|---|---|---|
| Wall stub | 2–5 | 1–4 ✓ |
| Column | 15–30 | *absent* |
| Freestanding arch | 12–20 tall, 8–14 span | *absent* |
| Pylon / stele | 15–25 | *absent* |
| Grand stair | 8–14 wide, 6–20 risers | 3 wide, 1–2 risers |
| Precinct | 60–120 across | ~22 |
| District | 250–350 across | *absent* |

The wall stubs were the only thing in range. **This table is the acceptance
criterion.** Anything built for this direction that does not reach these numbers
has not addressed the problem, however well weathered it is.

A note on why: the tall thin elements are not decoration. They are what makes a
site legible from a distance and therefore what makes it worth walking toward.
With a post-population world and no settlements on the horizon, monuments carry
the entire navigation load (`plan.md` §13), and a monument you cannot see is not
one.

---

## 3. The kit

Twelve parts. Small on purpose — every reference image is assembled from
essentially this set, which is what gives them a common hand.

| | |
|---|---|
| **Column** — standing / stump / **fallen full-length** | **Freestanding arch** (trilithon or voussoired) |
| **Pylon or stele** — standing / toppled, inscribed | **Grand stair** |
| **Terrace revetment** | **Long precinct wall** |
| **Paved court** with a *ragged* edge | **Sunken basin or cellar pit** |
| **Corbelled capital and cornice** | **Colonnade range** on a stylobate |
| **Circular floor emblem** | **Rubble field** |

### The three highest-value pieces

Because they are the most conspicuously missing and each is cheap:

**The fallen column, lying full length.** `reference-8` has one lying across the
ground as a single long horizontal element. A twelve-block horizontal does an
enormous amount for a site's composition and immediately reads as *something that
fell*, which no amount of standing rubble does.

**Ragged paving.** In `reference-7` and `reference-10` the paved court survives as
a *stain* with holes and frayed edges, sand taking the rest. Paving that stops in
a clean rectangle reads as a floor tile; paving that frays reads as time.

**Half-burial so only the plan reads.** `reference-8`'s concentric ring is legible
as *geometry* rather than as a building. Burying a structure until only its
footprint shows is the cheapest way to make something read as archaeological
rather than architectural.

### States, not variants

Every part exists in a small number of decay states and the state is chosen by
the site's age, not rolled per instance. A column is standing, a stump, or
fallen. A stele is upright or toppled. A wall is full height, stub, or a
foundation trace in the ground. This is what lets a whole site read as *one age*
while still varying.

---

## 4. Composition

The single largest gap between the previous build and the references is not
parts. It is that **the references are compositions and the previous build was a
scatter.**

Every reference image has four things. A site that lacks any of them will not
read, regardless of how good its parts are:

**An axis.** A direction the place is organised along, usually the direction you
approach from. `reference-4`'s processional stair, `reference-2`'s temple front,
`reference-11`'s stair sequence.

**A hierarchy of levels.** Two to five platform heights, connected by stairs.
Ground → court → terrace → sanctum. The vertical sequence is what turns a plan
into a journey.

**A boundary.** A precinct wall, a revetment, a change in paving. Something that
tells you when you have entered. `reference-3`'s precinct wall running through the
sakura woods is a whole extra layer of place for almost no geometry.

**A centre.** Something at the head of the axis that the rest defers to. An
altar, an arch, an emblem, a sanctum.

### The connective tissue matters more than the objects

`reference-9` is the most instructive image in the set. What sells it is not the
arches — it is the **long precinct walls running between them**, dividing the
plain into fields, with stelae posted along them. Remove the walls and the same
arches read as scatter.

This is why [WORLD.md](WORLD.md) §6 introduces **domains**: a large tract sharing
one culture, one axis grid and one wall system, inside which several sites sit.
A landscape says *one people lived across all of this* through its connective
geometry, not through its monuments.

### Composition is a grammar with a pinned seed

Site plans are generated from the site record, re-rollable while you are
iterating, and **pinned once good** so they never change again. See
[MAP_PIPELINE.md](MAP_PIPELINE.md) §2, L3. An individual precinct may always be
overridden with an authored layout.

---

## 5. Terrain is architecture

The most striking single fact across the references, and the one with the
deepest structural consequence.

In `reference-2` and `reference-11` the grass and sand terraces sit directly on
top of masonry retaining walls: the cliff face and the building face are the same
stone, in the same courses. `reference-2` goes further — the land has *grown over*
the ruined city, so terraces of ground cap what were clearly walls and pylons.

This cannot be produced by a structure pass stamping onto a finished heightfield.
Inside a site footprint the **site decides the terracing and the heightfield
conforms**.

There is already a small, working version of this idea: the footing system moves
earth for a single building — choosing a floor by minimising cut and fill,
cutting the uphill side, raising a plinth on the downhill side, banking soil
against the walls, splitting the plan across two levels where the ground breaks,
and guaranteeing a way in. See `plan.md` §11a and `ARCHITECTURE.md` §3a.

**What it has to become** is the same idea at precinct and district scale:
several polygons, several levels, revetment where levels meet, stairs where they
connect. That generalisation is the foundation everything else in this document
rests on, and it cannot be deferred even for the smallest first slice — a
`reference-10` courtyard already needs a raised pad whose revetment is the same
stone as its walls.

---

## 6. Decay and reclamation

Largely built already and working. `plan.md` §11a is the design contract;
`ARCHITECTURE.md` §3a describes the implementation.

The principles that must survive into the new scale:

**Reclamation is a field, not a material swap.** Growth is convincing only
because of *where* it is: damp, sheltered, low, turned away from the sun, and old.
A face turned from the sun keeps its moisture, which alone makes one side of a
ruin visibly greener than the other.

**Growth is a succession.** Moss and lichen first, then vines on surviving wall
heads, then ferns in shelter, then thicket in unroofed rooms, then saplings last
and rarely. Reading the succession should tell the player roughly how long a
place has been empty.

**Nothing that should read as a region may be drawn from a per-block hash.**
Moss patches, wall decay sections, roof holes — all of these have been got wrong
in this project by sampling a hash per block and producing confetti. Anything
that is supposed to look like a *region* is sampled from a field with a
wavelength. This has been the same bug three times; treat it as a standing rule.

**The contact line is what betrays a stamped-in building.** The clean horizontal
seam where masonry meets ground is the strongest "this was placed" signal there
is. Rubble at the foot of broken walls, growth thickening against the stone, and
earth banked against the outside are all required, not optional.

---

## 7. Resolution, and why Blender is deliberately later

An important finding from studying the references closely: **the kit is
achievable in voxels right now, and the reference images prove it.** Those images
*are* voxel renders. The columns are two- or three-wide stacks with one-block
insets for fluting. The arch voussoirs are stepped single blocks. The corbelled
capitals are two or three stepped courses. The inscribed pylons are flat faces
with a meander picked out one block deep.

So the kit and the composition grammar can be built and judged now, and a later
swap to authored meshes changes the *parts* without disturbing the grammar above
them.

When that swap is wanted, the route is already proven: the ink system exists
specifically so non-voxel geometry can join it — characters are non-voxel and
take identical outlining to the terrain they stand on (`ARCHITECTURE.md` §2.4).
Structures leaving the voxel grid for meshes with arbitrary yaw and lean is
`plan.md` §11a.6 and §14.3, and it is justified only after the composition layer
exists — because a beautifully authored ruin sitting on a badly-fitted pad looks
exactly as placed as a blocky one does.

---

## 8. Visual reference points

Three games are the scenery baseline. **This is a visual and compositional
reference only** — not tone, not gameplay, not story, and emphatically not
palette. Petalfell's own story and world are original; what is borrowed is how
landscape and architecture are *arranged* so that a large empty world reads.

`plan.md` §2.3 holds the separate, older list of *tonal* reference points. The
two lists overlap by name and do not overlap in what is taken. Keep them
distinct.

### Shadow of the Colossus — emptiness is the content

The closest of the three to what `world-new/` shows. A vast, near-empty
landscape whose entire justification is the handful of things in it that are
enormous enough to be worth crossing it for. Long traversal with almost nothing
in it is not dead time; it is what gives arrival its weight.

Two specifics worth stealing directly. Its architecture is plainly built by
someone with different proportions and different purposes — stairs too broad,
doorways too tall, alignments pointing at nothing — which is exactly `plan.md`
§11.4's "legibly purposeful and not explained." And its atmosphere does real
structural work: haze grading distance is what lets a landscape read as *large*
rather than merely *big*.

### Elden Ring — the horizon does the pulling

Something enormous and unexplained on the skyline gives a player a direction
without a quest marker. In a world with no settlements and no population, that
is the primary navigation mechanism Petalfell has, and `plan.md` §13 already
puts the whole orientation load on landmarks.

The second lesson is **hierarchy between site tiers**: compact, repeatable minor
sites against a few unique major ones that read as a single vast composition
rather than a cluster of buildings. That maps directly onto the district /
precinct / mark tiering in [WORLD.md](WORLD.md) §6. Also worth noting is how
consistently its ruins are *half-swallowed* — by earth, water, or growth — which
is the same instinct as §3's half-burial rule.

### Skyrim — landmark density and sightlines

Its scenery lesson is not its architecture; it is that the world is **legible
because things are visible from other things.** You crest a ridge and there is a
barrow, a watchtower, a standing stone. Ruins are normal features of the terrain
rather than special set pieces, and roads *pass by* things rather than merely
connecting endpoints.

That gives the small-site tier its job: not to be impressive, but to be
frequent, visible from each other, and reliable enough that a player learns to
navigate by them. `world-new/reference-9` is this idea at Petalfell's scale.

### The tension to hold onto

All three are desaturated, muted, or dark. **Petalfell is pastel and high-key,
and that does not change.** What is taken from them is composition, scale,
sightline and emptiness — structure. What is taken from `world-new/` is surface:
palette, material, light and the shape of the parts.

If a reference from these games ever seems to argue for making the world darker
or grimmer, it is being read on the wrong axis.

---

## 9. Motifs

The meander or key glyph appears on pylons, stelae, floor emblems and lintels
throughout the references and functions as the civilisation's signature. It is
worth treating as a real content element rather than texture: a motif that
recurs across a continent is how a player concludes that scattered sites were
built by the same hand.

`reference-6`'s crystal-inlaid columns read as a *different* builder from
`reference-3`'s plain arches. Whether that is one culture in two eras or two
cultures is an open decision recorded in [WORLD.md](WORLD.md) §7.

---

## 10. Status

**Built and working:** footings at single-building scale, the reclamation field,
the material decay chain, sub-voxel growth rendered through the ground-detail
layer. See [CURRENT_STATE.md](../CURRENT_STATE.md).

**Not built:** the entire part kit, the composition grammar, precinct- and
district-scale terracing, domains, motifs.

See [ROADMAP.md](ROADMAP.md) for order.
