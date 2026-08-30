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
>
> Repeatable construction procedures, their evidence level and their rejected
> alternatives live in [`building-knowledge/`](../building-knowledge/README.md).
> Read the relevant entries before implementing a reference site and update them
> in the same session when a correction changes the method. This document keeps
> design language and acceptance criteria; it does not duplicate those recipes.

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

The wall stubs were the only thing in range. This table diagnoses the old scale
failure and gives a cross-reference range; it is not permission to inflate a
measured reconstruction. For supplied-reference sites, the visible player and
source geometry own the exact dimensions. Reference 10's current measured
pillar family is consistently 2×2 and seated on connected foundations; its
one-cell walls, stair shoulders and stelae are different structural classes,
not permission to mix 1×1 needles into that pillar family.

A note on why: the tall thin elements are not decoration. They are what makes a
site legible from a distance and therefore what makes it worth walking toward.
With a post-population world and no settlements on the horizon, monuments carry
the entire navigation load (`plan.md` §13), and a monument you cannot see is not
one.

---

## 3. The retired diagnostic kit

The following vocabulary remains useful for reading references and for legacy
fixtures, but it is not a production reconstruction API. The generic builders
proved scale ranges and then visibly failed: repeated capitals, identical piers,
regular walls and generated stairs turned the references into a sparse kit yard.
Each production site now writes its own voxel masses directly.

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

### Reconstruction contract

For the current production phase, neither composition nor visible architecture
is built from the kit. Each
site plan names one `world-new/reference-*.png` and reproduces that image's
visible district: footprint, relative level hierarchy, stair count and width,
wall/arch/column placement, silhouette, damage masses, terrain cuts, paving,
rubble and vegetation relationship. The whole reconstruction may be rotated or
uniformly scaled into a compatible permanent atlas location. It may not combine
several references, add a new centre, simplify a major mass, or replace a visible
detail with a generic part merely because the generic part already exists.
Every visible element is encoded uniquely in a site-owned voxel blueprint; even
two similar columns are authored as two different damaged masses. Procedural
reclamation and biome dressing stop at the measured footprint boundary.

The image does not reveal every back face. Hidden geometry completes only the
minimum obvious continuation needed for collision and viewing from the reverse;
uncertain areas remain plain rather than becoming opportunities for invention.
Outside the measured footprint, the biome/elevation/hydrology compiler owns the
landscape normally.

### How exactness is checked

Each reconstruction has one locked comparison view derived from its source
image. The visible player establishes the two-block scale where present; image
verticals and the two ground axes solve the isometric camera. Authoring then
proceeds from large to small: trace the visible terrain/platform silhouette,
record every level and stair, place the primary masses, then transcribe columns,
arches, wall failures, rubble, paving and vegetation exclusions.

The locked view uses the source's isometric quadrant. A cardinal orbit that
shows the same site from a more flattering side is supporting evidence, never a
reference comparison: screen-left/right relationships, stair direction and
occlusion order must agree before an overlay is meaningful. For
`reference-10.png` that solved view is yaw 135 degrees and the true isometric
pitch of 35.264 degrees; its other three quarter turns test the hidden geometry.

The review tool must render that locked camera and produce both a 50% overlay
and an edge-difference image against the reference. Acceptance requires:

- the outer site and terrain silhouette to coincide at the comparison view;
- every visible platform edge, stair, portal, arch, pylon and standing column to
  have a corresponding measured element;
- no invented major mass, precinct, centre or axis;
- material/lighting differences to be judged separately from geometry, so a
  flattering grade cannot hide the wrong shape;
- near, reverse and night views to remain collision-complete, while unseen
  geometry stays conservative.

The comparison overlay is derived review evidence, never an authored source.

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

### Composition and visible blocks are transcribed

The axis, level hierarchy, boundary, centre, silhouette and major part placement
of a remembered site are measured reference facts. No production generator may
repeat a colonnade, fit a stair, choose damage, scatter rubble, pave a court or
place vegetation inside that footprint. Low-level range writes are allowed only
as serialization shorthand for explicitly measured blocks. See
[MAP_PIPELINE.md](MAP_PIPELINE.md) §2, L3/L4.

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

### 5a. The legacy Massif operation and its boundary

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

The legacy diagnostic assembled a site by choosing a summit, stacking broad
noise-warped slabs, notching stairs and then stamping parts from the kit. That
sequence is not used by Reference 10 and must not be generalized to
`world-new/`. Production reconstruction ground is traced as explicit court,
platform, shelf, channel and stair cells, with ordinary atlas terrain between
the source-visible interventions. The current repeatable method and rejected
ring/pad failures are documented in
[terrain and detached slab integration](../building-knowledge/terrain/terrain-and-detached-slab-integration.md).

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

Three games remain the wilderness-spacing and sightline baseline. **They do not
author production-site geometry.** Structural composition is transcribed from
the supplied `world-new` images under §4. These games contribute only how broad
empty terrain frames those reconstructions—not tone, gameplay, story or palette.

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

**Authored and realised as a review blockout:** the first southern domain's
versioned L3/L4 plan fixes seven platform polygons at four absolute levels, three
stairs, sixteen wall runs, eight graph-bound route sockets and forty-five
silhouette placements. Four named platform cutouts preserve terrain or expose a
collapse; collapsed cutout depth and platform/cutout reclamation density are
authored, not decay chosen by the compiler. The strict audit enforces domain/site
bounds, socket identity, reference paths and the scale table. The domain review
composes its nine terrain sectors, fits the authored levels and routes, repeats
only named ranges, frays named edges and derives terraces, coping, buttresses,
cutout rims, rubble and surviving colonnade lintels within those contracts.
Fourteen surface envelopes author broad earth caps, broken paving and collapse
areas; their interiors are derived from global wavelength fields, including 85
sparse rubble clusters in the current build. Recessed meanders and the floor
emblem are geometry rather than tint. The biome-driven grove pass supplies
surrounding scale and enters made ground only where authored reclamation permits
it. Fixed late-morning and early-full-night views run through the ordinary game
lighting rather than a presentation-only material rig.

**Not accepted:** the fixed day/night captures now read as a real district-scale
axis rather than a small fixture, and the denser edges, surface breakup and
silhouette remain legible at 1,000 units. Atlas-native ground detail, coherent
wall failure, glyph geometry and L4 variation exist, but walking-distance
microarchitecture and localized decay remain materially thinner than
`reference-2`, `reference-5` and `reference-9`; structure geometry is also not
persisted into per-sector artifacts. A working compiler and a beautiful review
frame are evidence of composition and scale, not evidence that the production
site matches the reference at walking distance.

The author superseded this blended composition on 29 August 2026. A subsequent
generic `reference-1` portal attempt was also rejected because it still stamped
kit-like architecture onto broad slabs. Both remain evidence only.

**Active Blockout under reconstruction:** the first production transcription is
the unique `reference-10.png` Bloom Reach grove court. It owns its blocks,
terrain cuts, paving damage, moss faces, blossom silhouettes and nearby stone
falls directly; none are shared ruin-kit placements. Its strict plan and capture
rig exist, but current visual evidence and open corrections are claim-scoped in
the [Bloom Grove Court knowledge ledger](../building-knowledge/sites/bloom-grove-court.md).
The complete v13 matrix remains historical evidence for the unchanged corrected
central occupied footprint: both lower channels remain open, reverse views show
no floating back, and far views keep the new extent modest. The later v16
annotated correction
removes the invented graded bumps from the main stair's east side, opens a real
passage by splitting the south-west foundation wall, seats the constant 2×2
pillar family continuously on its stylobate, and gives the southern approach two
low visible rises into the court. Its locked, top and four play-distance
quarter-turn views have been inspected for those four claims. These
claim-scoped findings are not complete multi-distance evidence, whole-site
fidelity, or author acceptance.
Normal startup places the traveller in its collision-enabled fixed four-sector
production-atlas mosaic. Only explicit author confirmation can close this
Blockout and allow the next structural reference to start.

See [ROADMAP.md](ROADMAP.md) for order and slice status.
