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
>
> The evidence-labelled operational methods for measuring, planning, building
> and reviewing reference sites live in
> [`building-knowledge/`](../building-knowledge/README.md). This document owns
> data boundaries and iteration order; use the handbook for procedures and
> update/supersession rules.

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
| **L3** | Site plan | Measured transcription of one named structural reference: footprint, levels, entrances, sightline and all visible major masses | Authored uniquely per reconstruction; only minimum hidden continuations are inferred |
| **L4** | Blocks | Site-owned voxel runs for every visible stair, wall, arch, pillar, break, rubble mass and vegetation exclusion | Authored uniquely; no shared architectural generator inside the footprint |

The thing to hold onto: **the author's control is total at L0–L4 wherever a
player is expected to remember the result.** For supplied-reference sites,
machines only materialise explicit site-owned voxel data. They do not repeat
architectural ranges, fit stairs, generate damage or dress the measured
footprint.

For the current phase, “authored” at L3 does not mean free composition. The
author has selected direct reconstruction: every production plan names one
`world-new/reference-*.png` and transcribes its visible composition. L2 may
rotate and place the whole reconstruction to fit compatible atlas terrain; it
may not merge several references into a new site. Outside measured footprints,
only normal deterministic biome/elevation/hydrology terrain is built.

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
regenerable at any time, and are disposable. A seed may vary ordinary
wilderness outside a measured site; it may not vary trees, rubble, paving,
damage or architecture inside a supplied-reference footprint, move a site,
reroute a major road or choose a hero composition.

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

The topology format is deliberately small and versioned. A connected domain's
L3 plan is a separate record so platform and wall detail can grow without making
the permanent location and connection graph wait.

```
content/chapter_01/
  map.json          current runtime/review-map entrypoint
  atlas.json        production extent, sectors, layers and provinces   (L0/L1 manifest)
  biomes.json       terrain/surface/vegetation/atmosphere profiles      (L1)
  topology.json     permanent atlas domains, sites, nodes and routes     (L2)
  world.json        3,456-square runtime topology fixture                (legacy review)
  continent/        registered land/elevation/water/region fields       (L0/L1 images)
  domains/<id>.json connected platforms, levels, walls and silhouettes  (L3)
  sites/<id>.json   reference, atlas pairing and locked review metadata  (L3)
  sites/<id>-plan.json exact terrain, surfaces and block projections     (L3/L4)
  derived/          disposable sector output; never hand-edited
```

The atlas is 12,288 × 9,216 blocks while the current runtime fixture is 3,456
square. `topology.json` is the permanent rectangular source and `atlas.json`
registers it through `topologyPath`. `world.json` now belongs only to opt-in
legacy diagnostics. Normal startup composes the fixed production sectors around
the active site, but route/site/navigation results are not yet persisted into
sector artifacts. The two coordinate systems are never scaled into one another
at load time, because that would make supposedly absolute coordinates depend on
a runtime setting.

A site record carries a stable id and display name; absolute block position and
orientation; extent; domain, tier and archetype; culture and age; named
entrances bound to route nodes; optional sightline targets; build status; and an
optional authored plan path. Dressing seeds belong to the plan or derived build,
not to the site's identity.

The first L3 format is domain-local because the connective walls, causeway and
shared level hierarchy are the composition. Its origin and axis are authored in
atlas coordinates; every platform, platform cutout, stair, wall, route socket
and landmark uses integer offsets from that frame and names the L2 site it
belongs to. Cutouts name intentional terrain courts or collapsed voids inside a
platform. A collapsed cutout owns its exact depth; platforms and cutouts own a
0–1 reclamation density that says how strongly the biome may reclaim their made
ground. The compiler may dress edges and realise those values with coherent
fields, but may not invent, erase or move them. The audit transforms those
offsets back to atlas coordinates, checks site envelopes and exact route-node
sockets, and enforces the reference scale for columns, arches, pylons,
colonnades and grand stairs.

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

1. For reference-site work, read the relevant evidence-scoped methods and site
   ledger in [`building-knowledge/`](../building-knowledge/README.md), then
   inspect the live source and current captures.
2. Change an authored record or paint a registered layer.
3. Audit the atlas registration, biome/profile references and topology.
4. Preview the whole atlas, sector grid and authored graph.
5. Regenerate *only the affected sectors, site or review window*, including
   deterministic seam aprons.
6. See the affected domain or site in the actual game render, at the actual scale, under
   the actual lighting.
7. Adjust the authored plan; optionally regenerate only its subordinate detail.
8. When the work produces a repeatable success, stronger check, scope limit or
   corrected failure, update or supersede the owning building-knowledge entry
   with the exact evidence inspected in this session.

Step 6 is not negotiable. **Scale can only be judged in the game.** A layout that
looks right in a top-down editor routinely reads as either toy-sized or
incomprehensibly vast when you stand in it, and the entire reason this direction
exists is that the previous ruins were an order of magnitude too small without
that being obvious from the code.

---

## 7. Authoring surfaces

Five options were considered. Summarised so the reasoning is not lost.

**A — Extended text records alone.** Free, diffable and deterministic. Good for
L2, but inadequate as the only L3 surface: typed coordinates need an immediate
terrain-backed plan view or nobody can judge the courtyard they describe.

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
- **L3/L4 explicit site blueprints** — supplied-reference sites own their
  terrain silhouette plus direct voxel runs for every visible architectural and
  decay mass. The first acceptance target is `reference-10`; the old generic
  domain compiler remains diagnostic only.
- **L4 Blender**, eventually, for content not already being transcribed as voxel
  reference sites. It does not replace or simplify the current explicit pass.

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

The versioned L2 schema supports both the legacy square fixture and rectangular
production coordinates. The strict atlas audit now validates the permanent
`topology.json` against the 12,288 × 9,216 extent and registered province ids;
the topology preview draws domains, site envelopes, routes and affected sector
addresses directly over the accepted elevation, water and region sources. The
first southern gateway domain is permanently placed at the central delta
threshold. Its L3/L4 domain plan fixes seven platform polygons at Y106/108/
114/122, four named platform cutouts, three stairs, sixteen connective wall runs,
eight exact route sockets, forty-five scale-checked silhouette placements and
fourteen coherent surface-treatment envelopes.
The terrain-backed domain preview shows those records over the accepted delta
shelf and water. A review compiler now composes the nine affected sector
artifacts, realises the exact L2/L3 records as terrain-backed voxel geometry and
applies globally deterministic paving-edge, surface-damage, reclamation,
ground-detail and biome-vegetation assistants within the authored densities.
Fixed late-morning and early-full-night captures
use the ordinary game lighting and atmosphere at all four review distances. The
authored sources remain untouched. The old source overlay and authored-route
playable runtime still consume `world.json` only.
An authoring-time compiler emits disposable `PTFLSEC2` terrain artifacts with
bed/terrain height, absolute water surface, hydrology class and primary/secondary
profile blend. It derives transition widths and hydrology shaping entirely in
global coordinates and has passed repeat-hash and independent-neighbour overlap
checks at atlas corners, a province boundary, high terrain and the drowned south.
The current artifact reader rejects stale or malformed derived data. A read-only
runtime review window materialises one sector plus apron, or a temporary square
mosaic of ordinary sector artifacts, into local voxel storage at true atlas
coordinates. Fixed sector and domain captures render through the ordinary
terrain, ink, atmosphere, grade and water paths without allocating the
continent.
Canonical runtime mode no longer runs the procedural
settlement/significant-landmark searches or the summit-seeking sanctum fixture.
The current southern plan is a superseded multi-reference tooling fixture; it is
not production content under the reconstruction contract above. The active
Reference 10 site has a strict source-facing v2 plan, explicit one-cell-per-voxel
runtime parity, source/runtime SVG previews, a locked isometric/top capture rig,
and collision-enabled player traversal inside its fixed four-sector normal-start
mosaic. Culture, abandonment, the authored wilderness density source, dynamic
mosaic handoff, the remaining 26–56 site envelopes and continent-scale road
graph, persistent per-sector structure/navigation output, author-accepted
realisation of the first L3/L4 plan, and all later site plans remain unbuilt. See
[ROADMAP.md](ROADMAP.md) §3
and [CURRENT_STATE.md](../CURRENT_STATE.md) §8.
