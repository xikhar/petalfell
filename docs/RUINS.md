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

### Composition is authored; grammar is an assistant

The axis, level hierarchy, boundary, centre, silhouette and major part placement
of a remembered site are authored facts. A generator may repeat a colonnade
between authored endpoints, fit a stair between authored levels, vary coherent
damage or dress rubble and paving; it may not invent the composition and ask the
author to re-roll until one happens to work. See [MAP_PIPELINE.md](MAP_PIPELINE.md)
§2, L3.

Sites are anchored in permanent atlas coordinates. A connected domain plan uses
one domain-local origin and axis so shared walls and causeways cannot drift apart;
each component still names its owning L2 site, and route sockets map exactly back
to authored graph nodes. A later site-specific refinement may subdivide that
plan without changing its frame. The sector compiler may split the derived
result across storage boundaries, but no sector boundary may split the
composition or become visible in its terrain, damage or dressing.

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

### 5a. The Massif process — how every site's ground is built

One available technique, arrived at after three failed shapes for the first site
(a small stamp, concentric terrace rings, an excavated mesa — each corrected by
the author against the reference). It is implemented as `src/World/Massif.cs`
and retained for sites or parts of sites whose landform actually has this
additive slab-stack character. It is not a substitute for authored platform
polygons, causeways, cliff works or domain-scale terrain plans.

The observation it encodes: in the references the monumental ground is **slabs
of stone stacked on a natural high point** — flat tops, sheer warped faces
gashed against each other, rising tier by tier, with the stairs notched through
the slab fronts and the surrounding land completely untouched. There are no
gradual slopes and there is no excavation.

The four rules:

1. **Additive, always.** Every column's final height is max(slab plan, existing
   ground). A site cannot dig, moat, or flatten a skirt around itself; where a
   slab meets rising ground it disappears into the hillside, which is how real
   masonry meets a real slope. This is also what makes placement safe — the
   land beyond the slabs is byte-for-byte what it always was.
2. **Slabs, not slopes.** A slab is a noise-warped rounded rectangle with one
   flat top, so height only ever changes at a slab boundary and every change of
   level is a sheer face. The "gashed together" clefts fall out of the geometry
   where two warped edges almost meet.
3. **One material.** Slab fill is the monument's own coursed pale masonry, so
   terrain and architecture are one thing (§5's central fact).
4. **Stairs are notches.** Stamped through the slab fronts after the slabs,
   replacing their heights along the strip — carved from the mass, never leaned
   against it.

A site is then: pick a summit with vertical headroom, cap it with a base tier a
few courses proud of the peak, stack mid and crown tiers, shed small satellite
slabs around the skirt, lay masonry decks where the monument stands, notch the
stairs, and only then place parts from the kit. A causeway is a long thin slab;
a cliff ledge is a slab against a mountainside; a district is many decks on few
slabs — the same four calls should shape any site in `world-new/`.

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

So the kit and authored composition plans can be built and judged now, and a
later swap to authored meshes changes the *parts* without disturbing the plans
above them.

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
the material decay chain and sub-voxel growth rendered through the ground-detail
layer. The twelve-part kit (`src/World/RuinKit.cs`) is parametric, including the
meander glyph of §9. `src/World/Massif.cs` implements one additive slab-stack
earthwork technique. `src/World/Sanctum.cs` is the review fixture that exposed
the failure of one isolated monument on generic terrain; canonical mode disables
it because it chooses its own summit.

**Authored and realised as a walkable blockout:** the first southern domain's
versioned L3 plan fixes twenty-five platform polygons at Y102/104/105/106/108/110/114/124/144, eight
stairs (notched into the high slab, one tread per block), nine wall runs, eight graph-bound route sockets and sixty-seven silhouette
placements, including a through-opening `Gate` with a hillside-through slot and a second Gate in a waterfront cleft that opens west to the bay, reached by a drowned 104 masonry spur.
Five named platform cutouts preserve hillside courts, a waterfront gate cleft, or expose one drowned-court collapse;
collapsed cutout depth
and platform/cutout reclamation density are authored, not decay chosen by the
compiler. The strict audit enforces domain/site bounds, socket identity,
reference paths and the scale table. The domain window composes its nine terrain
sectors, fits the authored levels and routes, repeats only named ranges, frays
named edges and derives terraces, coping, buttresses, cutout rims, rubble,
coherent wall gaps and surviving colonnade lintels within those
contracts. The biome-driven grove pass supplies surrounding scale and enters made
ground only where its authored reclamation value permits. Fixed late-morning and
night views run through the ordinary game lighting rather than a presentation-only
material rig. The traveller walks the compiled collision.

**Not accepted:** the fixed day/night captures read as a real district-scale
axis rather than a small fixture, and the denser edges and silhouette remain
legible at 900 units. The 124/144 massif is a cliff around the slot rather than
a 280-wide palace, and the causeway is a Y102 land spine with drowned paving patches in the authored shallows. The 144
crown is two overlay lips plus a north satellite, not a mesa or a connecting bar. Fallen columns are drums with gaps, standing shafts
carry a crystal core, the 144 crown wraps a through-slot at the 124 gate floor
with the grand stair notched into that face and an empty through-slot,
raised plates are tan/olive plateaus with a paving stain, the 104 mid-shelf is a 6-deep pad at the 124 toe,
revetment rims carry a sand
banding course, and the grove pass plants 1,653 trees on the nine-sector
window, but court-scale glyphs,
localized wall failure and L4 variation are still thinner than `reference-2`,
`reference-5` and `reference-9`, and the structure geometry is not persisted into per-sector
artifacts. Late-morning review mean colour now tracks `reference-1`; that is not
the same as an accepted walking-distance match. A working compiler and a
beautiful review frame are evidence of composition and scale, not evidence that
the production site matches the reference at walking distance.

See [ROADMAP.md](ROADMAP.md) for order and slice status.
