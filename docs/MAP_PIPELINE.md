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
until it is acceptable.

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
| **L0** | Continent | Landmass silhouette, ranges, watersheds, sea level | Fully |
| **L1** | Regions | Biome, climate, snowline, abandonment bias, **culture** | Fully |
| **L2** | Sites | A named record: id, position, orientation, extent, archetype, age | Fully |
| **L3** | Site plan | Precincts, axes, platform levels, stairs, part placement | Generated from L2, re-rollable, pinnable, overridable |
| **L4** | Kit | The geometry of each part | Authored once, reused everywhere |

The thing to hold onto: **the author's control is at L0–L2, and it is total. L3
is a machine that serves that intent and can always be overruled. L4 is a
library.**

L1 is where [WORLD.md](WORLD.md) attaches. L3 and L4 are where
[RUINS.md](RUINS.md) attaches. This file owns the plumbing between them.

---

## 3. Authored versus derived

The single most important structural rule in the pipeline:

> **Authored data is never written by the generator. Derived data is never
> edited by hand.**

Authored artifacts are the source of truth, live in version control as text or
images, and are the only thing a human touches. Derived artifacts are a pure
function of the authored ones plus pinned seeds, are regenerable at any time, and
are disposable.

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

Sketch, not specification — the exact shape should be designed *after* the
composition grammar tells us what a site plan actually contains. Designing the
format first is guessing at fields.

```
world/
  continent/        painted layers — height, biome, water, culture   (L0/L1)
  sites.toml        the site records                                 (L2)
  sites/<id>.toml   per-site plan pins and overrides                 (L3)
  kit/              part sources                                     (L4)
```

A site record needs roughly: a stable id and display name; position and
orientation; extent or footprint polygon; archetype and form; culture and era;
age; terrace-level count; what stands at the head of its axis; a pinned seed once
the layout is good; and an optional authored-blockout reference.

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

1. Change an authored record or paint a layer.
2. Regenerate *only what that change affects*.
3. See it in the actual game render, at the actual scale, under the actual
   lighting.
4. Adjust, or pin the seed and move on.

Step 3 is not negotiable. **Scale can only be judged in the game.** A layout that
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
and exported as a volume plus a socket list. Heavy and awkward for L0/L1, and
poor at re-rolling.

**D — In-game editor.** Fly camera, place and rotate sites, tweak parameters,
regenerate, see it immediately in the real render. **The shortest possible
iteration loop, which per §6 is the thing that matters most.** The most work of
the five, but much of the groundwork exists — map view, camera rig, developer
menu, teleport and chunk streaming are all already there.

**E — Hybrid.** The chosen direction.

### The chosen hybrid

- **L0/L1 painted** — coastline, relief, marsh, snowline, region and culture.
- **L2 a text record, placed in-game** — position and orient a site with a fly
  camera; the tool writes an *authored record* back to the file (§3).
- **L3 a grammar with a pinnable seed** — generated and re-rollable; once a
  layout is good its seed is pinned and it never changes again. Individual
  precincts may be marked authored and take a blockout instead.
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

---

## 9. Status

Nothing in this document is implemented. The current build is a seeded map with
procedural placement, described in [CURRENT_STATE.md](../CURRENT_STATE.md).

This is deliberate: [ROADMAP.md](ROADMAP.md) puts the part kit and composition
grammar first, so that the authoring format is designed once we know what a site
plan actually contains rather than before.
