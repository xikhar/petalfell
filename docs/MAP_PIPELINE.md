# The Map Pipeline — how the world is authored and made

> **What this file owns.** The canonical-map decision and everything that
> follows from it: the layer model from continent down to kit part, what is
> authored versus derived, where the data lives, the iteration loop, and the
> authoring-surface options with the chosen direction.
>
> **What it does not own.** What the world contains ([WORLD.md](WORLD.md)) or
> how structures are composed ([RUINS.md](RUINS.md)).
>
> Read [AGENTS.md](../AGENTS.md) first if you have not.

---

## 1. The decision

**Chapter 1 is one canonical world, authored once.** It is not re-generated from
a seed. The map is a *document you edit and re-render*, not a lottery you re-roll
until it is acceptable. Its production canvas and sector contract are in
[ATLAS.md](ATLAS.md); "canonical" applies equally to painted landform and named
topology.

This was decided deliberately and it is the root that everything else in this
file grows from. Do not quietly reintroduce seed-variance for the main map.

### What it buys

**Coordinates become permanent and namable.** `(2140, 1880)` currently means
nothing — change the seed and it is under water. On a canonical map a site has an
address that can be written into a quest, printed on a paper map, spoken in
dialogue, or bookmarked by a player. This alone justifies the change.

**A whole defensive layer disappears.** A large fraction of the existing world
code exists only to survive terrain moving underneath it — the "is this column
clear" tests, the "does this footprint fit" tests, the reject-and-retry scatter
loops that try dozens of candidate positions and silently give up. Every one of
those answers *what if the ground is not what I hoped*. On a canonical map the
answer is: **you fix the map.** That is not a workaround, it is the correct
response, and it removes a category of silent failure that has repeatedly
produced missing or malformed content.

**The world can be baked.** Once regions are frozen, the heightfield, region
assignment, road network and navigation data become shipped artifacts rather
than startup computation. Load becomes a file read.

**Determinism stops being load-bearing at runtime.** The existing discipline
(stable hashing, fixed iteration orders, never using platform-randomised hash
codes) still matters while authoring, because you need to be able to regenerate
and get the same answer. But it stops being the thing standing between the
player and a broken world.

### What it costs, stated plainly

The map is roughly twelve million columns. **You cannot hand-author that.** The
generator does not go away — its job changes from *inventing the world* to
*filling between the parts you designed*. The boundary between the two must be
explicit in the data and never fuzzy.

"Canonical" also must not mean "frozen too early." You need to keep regenerating
wilderness while designed sites stay pinned, for as long as authoring continues.
That requires a hard split — see §3.

---

## 2. The layer model

Five levels. The right tool and the right amount of human control differ sharply
at each, which is the whole reason to name them.

| | Level | Owns | Authored? |
|---|---|---|---|
| **L0** | Continent | Landmass silhouette, ranges, watersheds, sea level | Fully, as registered coarse images |
| **L1** | Regions | Biome/build profile, climate, snowline, abandonment, wilderness bias, **culture** | Fully, as registered coarse images and profile records |
| **L2** | Topology | Domains, named sites, absolute positions, extents, entrances, sightlines and route graph | Fully |
| **L3** | Site plan | Precincts, axes, platform polygons, levels, stairs, walls and major part placement | Authored for remembered places; assisted for repetition and minor marks |
| **L4** | Kit | The geometry of each part | Authored once, reused everywhere |

The thing to hold onto: **the author's control is total at L0–L3 wherever a
player is expected to remember the result.** Machines at L3 repeat ranges,
resolve fitted terrain, apply coherent damage and dress detail; they do not
choose the composition. L4 is a library.

L1 is where [WORLD.md](WORLD.md) attaches. L3 and L4 are where
[RUINS.md](RUINS.md) attaches. This file owns the plumbing between them.

---

## 3. Authored versus derived

The single most important structural rule in the pipeline:

> **Authored data is never written by the generator. Derived data is never
> edited by hand.**

Authored artifacts are the source of truth, live in version control as text or
images, and are the only thing a human touches. Derived artifacts are a pure
function of the authored ones plus explicitly scoped dressing seeds, are
regenerable at any time, and are disposable. A seed may vary trees, rubble or a
repeated colonnade within authored limits; it may not move a site, reroute a
major road or choose a hero composition.

If those ever mix — if the generator writes back into an authored file, or a
human patches a derived one — the map stops being reproducible and the canonical
decision has silently been lost. Any tool that "places" something must write an
**authored record**, never a derived result.

---

## 4. Roads become authored

A consequence worth calling out because it reverses current behaviour.

Roads are currently routed by A* over a cost field with a braiding discount, then
smoothed. That is the correct approach for a seeded map, where you cannot know in
advance what the terrain will be. On a canonical map it is the wrong one: the
road network is a **story artifact** ([WORLD.md](WORLD.md) §4) whose shape has to
be legible as the record of an evacuation, and a cost-minimiser will not produce
that on purpose.

Roads should be authored as routes and the generator should *realise* them —
carrying width, class, construction and reclamation state, cutting them into the
ground, bridging where they cross water, and letting them decay by region. The
existing routing code stays useful for trails, which genuinely should be
"wherever feet would go."

---

## 5. Where the data lives

The topology format is deliberately small and versioned now. Detailed site-plan
records may add fields as the first connected domain proves them, without making
the location and connection graph wait.

```
content/chapter_01/
  map.json          current runtime/review-map entrypoint
  atlas.json        production extent, sectors, layers and provinces   (L0/L1 manifest)
  biomes.json       terrain/surface/vegetation/atmosphere profiles      (L1)
  world.json        domains, sites, entrances, graph nodes, routes      (L2)
  continent/        registered land/elevation/water/region fields       (L0/L1 images)
  sites/<id>.json   authored site plan                                  (L3)
  kit/              part sources and socket metadata                    (L4)
  derived/          disposable sector output; never hand-edited
```

The atlas is 12,288 × 9,216 blocks while the current runtime fixture is 3,456
square. `world.json` remains a review-window topology proof until the production
L0/L1 fields fix the southern coastline; it is then migrated once into permanent
atlas coordinates. Do not scale it implicitly at load time, because that would
make the supposed absolute coordinates depend on a runtime setting.

A site record carries a stable id and display name; absolute block position and
orientation; extent; domain, tier and archetype; culture and age; named
entrances bound to route nodes; optional sightline targets; build status; and an
optional authored plan path. Dressing seeds belong to the plan or derived build,
not to the site's identity.

The **stable id** is the important field. It is what lets a quest, a map marker,
a save file and a conversation all refer to the same place across every future
regeneration.

---

## 6. The iteration loop

This deserves as much design attention as the output format, because it is what
you will actually spend your time inside.

Full world generation currently takes several seconds and that is already
irritating during development. Authoring 30–60 sites means regenerating hundreds
of times. **Partial regeneration — rebuild one site's region without the whole
continent — pays for itself within a week of authoring and should be built early
rather than late.**

The loop that matters:

1. Change an authored record or paint a registered layer.
2. Audit the atlas registration, biome/profile references and topology.
3. Preview the whole atlas, sector grid and authored graph.
4. Regenerate *only the affected sectors*, including deterministic seam aprons.
5. See the affected domain in the actual game render, at the actual scale, under
   the actual lighting.
6. Adjust the authored plan; optionally regenerate only its subordinate detail.

Step 5 is not negotiable. **Scale can only be judged in the game.** A layout that
looks right in a top-down editor routinely reads as either toy-sized or
incomprehensibly vast when you stand in it, and the entire reason this direction
exists is that the previous ruins were an order of magnitude too small without
that being obvious from the code.

---

## 7. Authoring surfaces

Five options were considered. Summarised so the reasoning is not lost.

**A — Extended text records.** Free, diffable, deterministic, no tooling. Good
for L2 records. Hopeless for L3: nobody can compose a courtyard by typing
coordinates.

**B — Painted layers.** Author L0/L1 as image layers at a coarse blocks-per-pixel
ratio, edited in any paint program. A brush is the correct instrument for a
coastline, a mountain range or a marsh, and you get the whole continent legible
at once. Costs nothing to build. Cannot carry semantics — a colour cannot hold a
rotation or a name.

**C — Blender.** Excellent for L4, and genuinely good for hand-authored L3
blockouts: a district can be laid out at one unit per block with instanced parts
and exported as a volume plus a socket list. Heavy and awkward for L0/L1.

**D — In-game editor.** Fly camera, place and rotate sites, tweak parameters,
regenerate, see it immediately in the real render. **The shortest possible
iteration loop, which per §6 is the thing that matters most.** The most work of
the five, but much of the groundwork exists — map view, camera rig, developer
menu, teleport and chunk streaming are all already there.

**E — Hybrid.** The chosen direction.

### The chosen hybrid

- **L0/L1 painted and registered** — the 1,536 × 1,152 land, elevation,
  water, region, culture, abandonment and wilderness fields described in
  [ATLAS.md](ATLAS.md) §5.
- **L2 a text record with map and in-game views** — position and orient a site,
  name its entrances, and draw the routes between graph nodes. Every tool writes
  an *authored record* (§3).
- **L3 authored plans with procedural assistants** — hero districts and
  precincts own their platform polygons, level hierarchy, walls, stairs and
  major silhouettes. Assistants repeat parametric parts, conform terrain and
  apply dressing without changing the plan.
- **L4 Blender**, eventually. Not yet — see [RUINS.md](RUINS.md) §7: the part kit
  is achievable in voxels today and the reference images prove it, so the
  composition layer can be built and judged before any asset pipeline exists.

---

## 8. What has to change in the existing code

Not a task list — a statement of where the strain will land, so nobody is
surprised.

**Terrain must be able to obey a site.** Today terrain builds a heightfield and
later passes write blocks on top of it. The references require the reverse inside
a site footprint: the site decides its terracing and the heightfield conforms.
This is the deepest change and everything else rests on it.

**`MapDefinition` describes zones, not places.** It can say "a biome region is
around here." It cannot say "*this* site, at *this* coordinate, oriented *this*
way." L2 is that missing vocabulary.

**Placement-by-rejection goes away.** Scatter-and-retry is the seeded-map idiom.
Authored placement replaces it for everything that matters, and rejection becomes
an authoring-time error you are told about rather than a silent absence.

**Region assignment becomes a lookup.** Painted L1 layers replace the current
region-cell computation for the canonical map.

**Whole-map arrays become sector fields.** The current runtime's global
height/water/road/biome arrays are viable for its 3,456-square review map and not
for the 12,288 × 9,216 production atlas. Compilation and runtime access must use
global coordinates over sector-local storage. See [ATLAS.md](ATLAS.md) §3.

---

## 9. Status

The versioned L2 source, strict audit, SVG preview, in-game source overlay and
authored-route runtime path are implemented for the review map. The production
atlas manifest, biome-profile catalog and exact-registration land/elevation,
water and categorical-region sources are implemented without allocating
production terrain. Land, elevation, water and region are accepted.
An authoring-time compiler emits disposable `PTFLSEC2` terrain artifacts with
bed/terrain height, absolute water surface, hydrology class and primary/secondary
profile blend. It derives transition widths and hydrology shaping entirely in
global coordinates and has passed repeat-hash and independent-neighbour overlap
checks at atlas corners, a province boundary, high terrain and the drowned south.
Canonical runtime mode no longer runs the procedural
settlement/significant-landmark searches or the summit-seeking sanctum fixture.
Culture, abandonment, wilderness, the playable sector window, full atlas
topology and detailed L3 plans remain unbuilt. See [ROADMAP.md](ROADMAP.md) §3
and [CURRENT_STATE.md](../CURRENT_STATE.md) §8.
