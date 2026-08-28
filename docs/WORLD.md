# The World — the story layer

> **What this file owns.** The continent as a *place*: its regions, the history
> that explains their arrangement, how they connect, and what the player learns
> by walking from one to another. It also owns the rule by which sites are
> allocated to regions.
>
> **What it does not own.** How anything is built ([RUINS.md](RUINS.md)) or how
> the map is authored and generated ([MAP_PIPELINE.md](MAP_PIPELINE.md)). Those
> are lower layers and this one sits on top of them.
>
> Read [AGENTS.md](../AGENTS.md) first if you have not.

---

## 1. The one rule

**Geography and history are the same fact.**

A map does not tell a story because interesting things were placed on it. It
tells a story because its shape *is* the record of what happened. "There are
mountains in the east" is worth nothing on its own. "The mountains in the east
are where the stone came from, which is why the roads run that way, why the
quarries are cut into them, and why nothing was ever built up there that was not
first cut out of it" is the same sentence doing four jobs.

Every region below has to answer three questions, and if it cannot answer all
three it is scenery and should be redesigned:

1. **What is it, physically?** Landform, water, climate, vegetation.
2. **What was it FOR?** What did the people who are gone do here.
3. **What does crossing it teach the player?** One thing, statable in a sentence.

---

## 2. The direction of history

The continent has a **grain**. Something happened, people moved a particular
way, and the player can read that direction by walking without being told it.

The working premise, consistent with `plan.md` §2.1 (a world people left by slow
decline rather than catastrophe):

> **The water rose in the south over generations. People moved inland and up,
> along the roads, toward the cold. The great works are behind them, and the
> living are ahead.**

This yields a gradient the player feels rather than reads:

| Direction of travel | What changes |
|---|---|
| **Southward** | Older. Grander. Wetter. More drowned. More empty. The architecture gets larger and less explicable. |
| **Northward** | Newer. Smaller. Colder. Poorer. Occasionally still lit. The few remaining people are this way. |

That single gradient does an enormous amount of work. It makes the compass
meaningful, it gives the player a reason to choose a direction, it explains why
the roads are shaped as they are, and it means "how grand is this ruin" and "how
long ago was it left" are the same axis — which is exactly the axis the
reclamation and decay systems in [RUINS.md](RUINS.md) already run on.

**Corollary that must not be broken:** the grandest things are in the least
survivable places, and the places people still live are the least impressive.
The player should never find the best architecture next to the friendliest
inhabitant.

---

## 3. The regions

Six broad provinces arranged around the central river country. The selected
macro-map references fix their spatial read; names below remain working names
and are the author's to change. The *roles* are load-bearing.

### 3.1 South — the Shallows

**Physically.** A broad delta and warm shallow sea occupying the southern edge,
fragmented into drowned flats, long shoals and secondary islands. Sand and thin
soil lie over built stone. The coastline is indistinct: it is not clear where
the land stops or the old city begins.

**What it was for.** This is where the civilisation was largest. Everything of
consequence was built here, on the assumption that the sea was where it had
always been.

**What crossing it teaches.** That the water won, slowly, and that nobody
stopped it. This is the region of `world-new/reference-2` (a monumental quarter
standing in the sea), `reference-5` (a drowned headland) and `reference-9` (a
ruin field going on past the fog).

**Site character.** The great districts. Largest footprints, tallest orders,
highest age, most reclaimed. Colonnades that walk into the water. Causeways to
nowhere. Precinct walls that run out into the shallows and stop.

### 3.2 South-west — the Fen

**Physically.** Slow water, reed, mud, alder, standing mist. Almost no relief.
Ground that is not reliably ground.

**What it was for.** Crossed, not settled. The causeways that cross it are the
most engineered things in the world for their size, because keeping a road
across a bog is the hardest maintenance job there is — and they are the first
thing that stopped being maintained.

**What crossing it teaches.** That some things survive by being buried. The fen
preserves timber, cloth and rope that rotted away everywhere else, so this is
where the *ordinary* past survives while the monumental past is in the south.

**Site character.** Few monuments, many traces. Pile fields where a causeway
went. Sunken structures. Things that are intact but small. This is where a
player learns what these people actually used, as opposed to what they built.

### 3.3 West and upper interior — the Scarp and quarry belt

**Physically.** A broad belt of shelves and high ramparts running from the rocky
western coast toward the upper interior, cut by ravines and river heads. Bare
stone, scree and thin highland grass separate the cold crown from the central
basin.

**What it was for.** The quarry. Every pale stone in every ruin on the continent
came out of this wall.

**What crossing it teaches.** That the works had a cost and a source. A player
who has walked the southern districts and then finds the extraction faces,
half-split blocks still in the bedrock, spoil heaps, and a monolith abandoned
half-finished on its sledge road has been told the whole industrial story
without a word of text.

**Site character.** Working sites, not sacred ones. Cut faces, ramps, dressing
floors, abandoned blanks. Different vocabulary from everywhere else: this is the
only region where the architecture is *unfinished* rather than *ruined*, which
is a distinct and valuable second flavour of emptiness.

### 3.4 North and north-west — the Cold Shelf

**Physically.** High country above the snowline. Sparse, thin, bright. Long
sight lines.

**What it was for.** Nothing, originally. It is where people ended up.

**What crossing it teaches.** That somebody is still here. This is the only
region where a lit window, a tended fire, a kept path or a living animal in a
pen is a normal sight rather than an event — and even here it is rare.

**Site character.** The smallest sites and the only holdouts. Shrines that are
still maintained (`reference-6`, `reference-8`: snowbound platforms, half-buried
rings, arches with the offerings still on them). Reused older work — a newer,
poorer structure built inside a much older one, which is the clearest possible
statement of decline.

### 3.5 Centre — the Waist

**Physically.** The neck of the continent where the south's plains, the fen's
edge, the scarp's foot and the road north all converge. Mixed, transitional,
none of the other four regions' character in full.

**What it was for.** Everything passed through here. It is not a destination; it
is the place a destination would have been.

**What crossing it teaches.** Scale, by convergence. Every major road meets here
and none of them ends here.

**Site character.** The junction. The single largest authored work on the
continent belongs here, and it should be the thing the roads were built to serve
— and it should be **incomplete, failed, or unexplained**. `plan.md` §11.4 is
explicit that the deepest works should be legibly purposeful and *not*
explained; the Waist is where that idea gets its largest expression.

### 3.6 East — the Bloom Reach

**Physically.** Broad warmer shelves and river-cut meadows running to an islanded
eastern coast. Blossom canopy forms the largest single colour mass on the atlas,
but it breaks around long grass clearings, old field boundaries and pale stone.

**What it was for.** The last great cultivated country: orchards, ceremonial
groves, water gardens and managed roads rather than a surviving city. Its beauty
is work people once performed, now continuing without them.

**What crossing it teaches.** That abandonment can look abundant. The Reach is
not less empty because it is flowering; the regularity of grove lines, terraces
and irrigation is how the player understands that the apparently natural beauty
is inherited infrastructure.

**Site character.** Open precincts, grove shrines, water stairs, field walls and
tall isolated pylons visible across the canopy. It has fewer giant districts
than the Shallows and more long sightline marks. It is the later culture's
clearest independent vocabulary before that culture retreats north.

---

## 4. Landscape legibility

A structural requirement that sits between the regions and the roads, and the
main thing the scenery references contribute at *this* layer. The full list of
those references and what each is for is in [RUINS.md](RUINS.md) §8; what
follows is only the part that shapes geography rather than architecture.

**Emptiness is content, not filler.** Long stretches with almost nothing in them
are what give arrival its weight. The instinct to fill a quiet valley because it
looks empty on a map is wrong — a world where something is always in view has no
horizon to walk toward. Density must be uneven on purpose (§6).

**Things must be visible from other things.** A world is navigable when cresting
a ridge shows you the next thing. This is not a rendering concern, it is a
*siting* concern: high ground, ridgelines, headlands and the heads of valleys
are where visible things go, and that has to be decided when a site is placed,
not discovered afterwards. With no settlements and no population, landmarks
carry the whole orientation load (`plan.md` §13).

**The horizon pulls.** At least one thing per region should be large enough and
strange enough to be seen from most of that region, and it should be visible
long before it is reachable. That is how a player chooses a direction without
being given a marker.

**Distance must read as distance.** Atmospheric depth is doing structural work,
not decoration — it is what makes a landscape read as *large* rather than merely
*big*, and it is what makes a far silhouette legible as far rather than small.

---

## 5. The roads

Roads are the second half of the story layer, and on a canonical map they are
**authored, not routed**. See [MAP_PIPELINE.md](MAP_PIPELINE.md) §4 for why that
changes.

Their job is threefold and all three matter equally:

1. **Navigation.** A player who finds a road knows which way is somewhere.
2. **Pacing.** Roads decide how long it takes to get between sites, so they set
   the rhythm of the whole chapter.
3. **Exposition.** The network's *shape* is the record of the evacuation. It
   should be legible as one.

### The network, in outline

- **The Spine.** South to north, coast to cold shelf, through the Waist. The
  evacuation route. Best-preserved at its northern end (still walked) and
  drowned at its southern end. Following it in either direction is the single
  clearest statement the map makes.
- **The Strand.** The old coastal road in the south, now intermittent — long
  stretches under water, reappearing as causeway stubs and bridge piers. Reads
  as a road that the sea ate.
- **The quarry road.** Along the Scarp and quarry belt, wide, ramped, and heavily
  built because it carried stone. Overbuilt for a footpath, which is the clue.
- **The Fen causeways.** In the west, on piles, mostly gone. Crossing the fen
  should feel like following a rumour of a road.
- **Trails.** Everything else: not built, only worn, and worn by very few. These
  connect the small sites and are how the player finds anything off the network.

### Reclamation varies by region

The same road is a different object in each region, and that variation is free
storytelling: still-swept flags in the north, sand-drifted paving in the south,
piles standing in water in the west, rockfall on the scarp. The existing
abandonment field already drives road reclamation; the story layer just says
what its regional bias should be.

---

## 6. How sites are allocated

This is the seam between this document and the rest of the project. **The story
layer decides which kind of site goes where and how old and how large it is. It
does not decide how any of them are built.**

The allocation rule is the gradient of §2 expressed as parameters:

| Parameter | Rule |
|---|---|
| **Scale** | Largest in the south, smallest in the north. |
| **Age** | Oldest in the south, newest in the north. |
| **Reclamation** | Heaviest in the wet south and fen, lightest on the scarp and above the snowline. |
| **Archetype** | Sacred/monumental south · trace/preserved south-west · working/unfinished stone belt · grove/water east · small/maintained north · junction/inexplicable centre. |
| **Culture** | See §7. |
| **Population** | Effectively zero except in the north, and rare there. |

Two rules that keep it from becoming a formula:

- **Every region needs one exception to its own rule**, sited deliberately. One
  small, sharp, recent thing deep in the south. One enormous older work up on
  the shelf. The exception is what stops the gradient reading as a difficulty
  curve.
- **Density is uneven on purpose.** `reference-9` shows sites clustering into a
  *field* with connective walls between them, not spacing evenly. Long empty
  stretches are load-bearing: they are what makes arriving somewhere feel like
  arriving.

### Domains

There is a level between "region" and "site" that the current codebase has no
name for and that `reference-9` makes necessary: a **domain** — a large tract
(roughly 500–1000 blocks) carrying one culture, one axis grid, and one shared
boundary-wall system, inside which several sites sit.

What sells `reference-9` is not the arches. It is the **precinct walls running
between them**, dividing the plain into fields. Without the connective tissue
the same objects read as scatter. Domains are how a landscape says *one people
lived across all of this*, and they are the main structural idea the story layer
contributes to the builders.

---

## 7. Cultures

An open decision, recorded here because it belongs to this layer.

The reference images are not all by one hand. The plain arches and meander
glyphs of `reference-3`, `reference-7` and `reference-11` read as one builder.
The crystal-inlaid columns of `reference-6` read as another.

- **One culture** is cheaper, more coherent, and makes the meander motif a
  strong single signature.
- **Two or three** give the map real regional identity and answer "why does the
  north look different" with something better than "it is colder."

Working assumption until decided: **two**. An older, larger, southern builder
responsible for everything monumental, and a later, smaller, northern one that
reused their stone and did not understand it. That reuse — new poor work built
inside old great work — is the single most efficient way to state decline, and
it needs at least two vocabularies to exist at all.

---

## 8. What this layer must not touch

Stated explicitly so it stays true as both sides evolve.

**The story layer reaches into:** site allocation (which archetype, what scale,
what age, what culture, where), region parameters (biome, snowline, water level,
abandonment bias), road network shape, and domain boundaries.

**It must never reach into:** the part kit, authored composition plans, terracing
and footings, decay mechanics, reclamation, or anything in rendering. Those are
[RUINS.md](RUINS.md)'s and they are parameterised, not special-cased. If a
region seems to need a new *mechanism* rather than new *parameters*, that is a
signal the mechanism is under-general — fix it there, not here.

---

## 9. Status

The slow northward withdrawal and the six province roles are the accepted story
reading of the selected macro-map references. Exact coastlines, province borders
and names remain authored working material; generated labels in the references
are not canon. [ATLAS.md](ATLAS.md) owns their physical blockout and formats.

The first four-site southern gateway domain now has permanent production-atlas
coordinates at the central delta threshold. Its gate and side precincts occupy
the last coherent shelf; its processional axis descends through a water causeway
to a local authored segment of the drowned Strand, while the Spine continues
north. This is the first connected fragment, not the complete network: Chapter 1
still needs the remaining 26–56 site envelopes and the full Spine, Strand, quarry
and Fen graph. See
[ROADMAP.md](ROADMAP.md) §3.
