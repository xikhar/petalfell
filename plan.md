# Petalfell — Godot Rebuild and Long-Term Game Plan

## 1. Purpose of This Document

This document defines the creative, structural, and production direction for rebuilding **Petalfell** from scratch in Godot.

The existing Three.js project is the primary reference for:

- The pastel voxel-inspired visual identity.
- The camera framing and sense of scale.
- Terrain proportions and traversal feel.
- The clean light-and-dark outline language.
- The player character and dog companion.
- Swimming, click-to-move pathfinding, environmental motion, and interaction feedback.
- The balance between generated nature and deliberately placed landmarks.

The Godot version should not be a direct line-by-line port. It should preserve the parts that define Petalfell while establishing a clean foundation for larger maps, authored chapters, more characters, deeper gameplay, and future content.

This is a product and design plan. It intentionally describes what the game and its supporting systems should accomplish without prescribing technical implementation details.

The current implementation is tracked separately in [`CURRENT_STATE.md`](CURRENT_STATE.md).
That document records what exists now; it does not narrow or replace the goals here.

---

## 2. High-Level Vision

Petalfell is a quiet exploration adventure across a continent people have left.
The player travels with a dog companion through natural regions, old roads,
ruins and water, in a world that is soft, bright and beautiful — and almost
empty of anyone to meet.

It is not a ruined world in the grim sense. Nothing here is ash or rubble under
a dead sky. The blossom still comes, the water is clear, the light is warm; the
land is in excellent health. What is missing is people. Petalfell is what a
lovely place looks like a few generations after the last of them packed up and
went, and the interest of it is that the world plainly does not need them and is
carrying on without them.

### 2.1 Why it is empty

Two facts explain each other and between them supply most of the world's logic.

**They left slowly.** There was no catastrophe. Over generations the safe ground
shrank, and people fell back — a farmstead abandoned, then the valley above it,
then the whole upland. Houses were shuttered, not smashed. Valuables were taken.
Doors were closed on the way out. What remains is tidy, which is far sadder and
far stranger than wreckage, and it is also cheaper to author convincingly: an
empty room that was left neatly needs no story of violence written into it.

**Something drove them.** The wilds are not safe, and were not safe then. What
lives out there spread, and the retreat was a retreat FROM it. This is the
engine of the whole map: the deeper into the old country the player goes, the
longer it has been abandoned, the finer and stranger what is left standing, and
the worse what holds it. Danger, beauty, age and reward all increase along the
same axis, and that axis is geography rather than a level number.

### 2.2 Who is left

A handful of people, always in ones and twos, never in communities: a trader
working a surviving road, a hermit in a valley they refuse to give up, a
watchman at a crossing nobody crosses. They matter out of all proportion to
their number. **One hermit implies that everyone else is gone. Zero hermits
implies that nobody was ever there.** The few inhabitants are what makes the
emptiness read as loss rather than as unfinished world, and they should never be
so numerous that meeting one stops being an event.

### 2.3 Reference points, and what is taken from each

- **Shadow of the Colossus / Journey** — that empty, monumental and beautiful can
  carry a whole game without grimness. This is the tonal target.
- **Dark Souls / Elden Ring** — that abandonment can be read from architecture,
  and that danger concentrated in specific places gives a map its shape. The
  tone is *not* taken; Petalfell stays pastel and warm.
- **Rain World** — the ecology, and the sense that the world was not built for
  you. Taken more heavily than the others, and detailed in §22b.

What is deliberately NOT taken from Rain World is its framing of the player as
small prey inside a food chain. Petalfell has a human traveller, a dog, and a
near-isometric camera; the player is a walker through a place, not an animal in
it. The ecology is borrowed, the helplessness is not.

The world should feel authored rather than random, but still broad, organic, and
rich enough that procedural generation remains valuable. Major geography, the
places that were left, old roads, story locations, and important environmental
compositions are fixed. Natural detail between those places is generated
consistently from the fixed world plan.

The first complete target is a playable **Chapter 1**, built on systems that can
later support additional regions, maps, chapters, remnants, quests, items,
characters, and mechanics without rebuilding the game around each new addition.

---

## 3. Core Design Pillars

### 3.1 A Beautiful, Readable World

The world should remain visually clean at both close and default gameplay zoom. Terrain, structures, characters, roads, water, and interactable objects must be easy to distinguish without filling the scene with noise.

### 3.2 Exploration With Purpose

Travel should continually reveal meaningful destinations: overlooks, bridges, ponds, forest clearings, cliff paths, shrines, ruins, monuments, waystations, abandoned homes, and unusual natural formations. Generated scenery supports these places rather than replacing them.

With no settlements to structure the map, destinations carry the entire load of pacing and orientation, and there must be correspondingly more of them.

### 3.3 A Fixed World With Natural Variation

Petalfell should combine deliberate world design with deterministic procedural detail. Players should be able to recognize, revisit, discuss, and navigate the same world, while vegetation and small environmental details still feel organic.

### 3.4 Companionship

The dog is part of the identity of the game, not a decorative follower. Its movement, waiting, sitting, reactions, and future interactions should make travel feel shared.

In a world this empty the dog is the player's only constant company, which raises its importance rather than lowering it. It is also the natural carrier of warning: a dog that stops and stares at a treeline says more, and costs less, than any interface.

### 3.5 Gentle Presentation, Deeper Systems

The presentation should remain minimal and approachable even as the game gains inventories, equipment, crafting, trading, conversations, artifacts, combat-capable tools, and world progression.

### 3.6 Beautiful, Not Grim

Desolation here is melancholy, never bleak. The palette stays pastel, the light stays warm, the blossom still falls. The world is in good health and simply has nobody in it — a place that outlived its people rather than a place that was destroyed. Any asset, effect or system that reads as decay-for-horror is wrong for this game, however well it would suit the reference points.

### 3.7 Reusable Content Foundations

Every major Chapter 1 system should make future chapters easier to create. A new map should primarily require new geography, content, assets, characters, and chapter rules—not a duplicate version of the game.

---

## 4. Target Experience

The intended default experience is an isometric or elevated three-quarter view of a broad pastel landscape. The player can move directly or select a reachable destination with the mouse. The character follows an approximate natural path while the dog follows intelligently, with both able to traverse land, paths, bridges, steps, shallow routes, and appropriate water areas.

At the normal gameplay zoom, Petalfell should feel like a detailed miniature world:

- Large enough to invite real travel.
- Clear enough to read at a glance.
- Detailed enough to reward zooming in.
- Calm enough that effects and interface elements never overwhelm the world.
- Structured enough that roads and landmarks provide orientation.

Chapter 1 should introduce the world gradually, give the player reasons to visit multiple kinds of locations, and demonstrate the core loop of exploration, interaction, collection, travel, and companionship.

---

## 5. Scope of the Godot Rebuild

The rebuild should establish one authoritative Godot project containing:

- The core game flow.
- Player and dog behavior.
- The visual rendering language.
- The reusable world and map structure.
- A mixed authored/procedural environment pipeline.
- Reusable remnant, road, biome, landmark, character, item, and interaction concepts.
- Saving and persistent world state.
- Chapter 1 content.
- A foundation for future maps and chapters.

The current browser project remains a visual and behavioral reference throughout the rebuild. It should be used to compare scene composition, color, outlines, movement character, camera framing, vegetation density, and overall atmosphere.

---

## 6. Chapter and Map Structure

### 6.1 Game-Level Structure

Petalfell should distinguish between the persistent game systems and the content of an individual chapter.

Persistent systems include:

- Player movement and traversal.
- Dog companionship.
- Pathfinding and destination selection.
- Interaction and dialogue.
- Inventory and equipment.
- Collecting, crafting, trading, using, and throwing objects.
- Character and world state.
- Rendering, camera, audio, menus, settings, and saving.

Chapter content includes:

- Its fixed map plan.
- Biome layout.
- Remnants, holdouts and ruins.
- Roads and travel routes.
- Story and optional locations.
- NPC population and dialogue.
- Items, encounters, artifacts, and chapter-specific goals.
- Chapter-specific weather, ambience, flora, fauna, and visual variations.

### 6.2 Reusable Map Package

Each future map or chapter should be treated as a complete content package with a consistent set of information:

- Map identity and boundaries.
- Major terrain regions and elevation plan.
- Biome regions and transition zones.
- Water bodies and waterways.
- Settlement and landmark locations.
- Road network and route types.
- Authored environmental markers.
- Generated-detail rules.
- Spawn and arrival points.
- Navigation and travel rules.
- NPC and creature populations.
- Discoveries, interactions, and persistent changes.
- Visual, lighting, atmospheric, and audio character.

This structure should make it possible to create a future chapter by designing a new map package and adding content to the same core game.

### 6.3 World Persistence

The map should remember meaningful changes, including:

- Player position and progress.
- Dog state.
- Items collected, moved, consumed, crafted, bought, sold, or dropped.
- Opened or changed locations.
- NPC and quest progression.
- Discovered landmarks and travel points.
- Temporary and permanent environmental changes where appropriate.

Generated natural decoration does not need to become individually persistent unless gameplay gives it persistent meaning.

---

## 7. Mixed Authored and Procedural World Model

Petalfell should use a **mixed world model** rather than choosing between a completely handmade map and a completely random generator.

### 7.1 Fixed, Authored Macro World

The following should be deliberately planned and stable:

- Overall landmass and playable boundaries.
- Major elevations, cliffs, valleys, and plateaus.
- Biome regions.
- Lakes, larger ponds, rivers, wetlands, and important waterfalls.
- Remnants, holdouts, ruins, and monuments.
- Primary and secondary road routes.
- Bridges, gates, passes, and major stairs.
- Story locations and progression routes.
- Important scenic views and environmental compositions.
- Abandoned buildings, ruins, and future-use locations.
- Critical navigation corridors and blocked routes.

These fixed features give the world identity and allow gameplay, story, and navigation to be designed reliably.

### 7.2 Authored Markers and Environmental Stamps

Specific markers should shape important local areas without requiring every individual block to be placed by hand. These can define places such as:

- A pond in a meadow.
- A waterfall garden.
- A cliff overlook.
- A grove around a shrine.
- A stone crossing.
- A road bend around a rock formation.
- A forest clearing containing an abandoned home.
- A swamp island with a notable tree or artifact.
- A snowy ridge with a narrow pass.
- A remnant's yard, or the gateway of a ruin.

The current project's manually injected terrain ideas and landmark stamps should be treated as reference material for this approach.

### 7.3 Procedural Natural Infill

Procedural generation should fill and vary the natural spaces between fixed features. It should control appropriate amounts and arrangements of:

- Trees and undergrowth.
- Grass tufts and flowers.
- Stones, boulders, fallen branches, and ground clutter.
- Small terrain variation.
- Natural color variation.
- Minor ponds, wet patches, or snow clusters where allowed.
- Ambient fauna and decorative environmental motion.

Generated content must obey biome identity, authored exclusion areas, road clearance, remnant boundaries, navigation needs, and landmark compositions.

### 7.4 Deterministic Results

The same Chapter 1 world should remain recognizable and stable between sessions and builds unless the map design intentionally changes. Randomness should contribute natural variation without making important geography or gameplay unreliable.

---

## 8. Chapter 1 World Direction

Chapter 1 should feel like a connected region of a much larger abandoned
continent rather than a showcase island.

It should contain:

- Multiple major natural regions.
- At least one **holdout** — two or three people still living somewhere
  defensible, and the closest thing to a settlement in the chapter.
- At least one large **ruin complex** with a legible former purpose, standing as
  the chapter's principal destination.
- Several smaller remnants: a shuttered farmstead, a waystation, an overgrown
  mill, a chapel with the roof gone.
- A network of old roads, most of them partly reclaimed.
- Lakes, ponds, rivers, wetlands, cliffs, and crossings.
- Several minor locations and scenic landmarks.
- **Deep zones**: places abandoned longest, held by what drove people out,
  containing the best of what was left behind.
- Enough open wilderness for exploration and resource gathering.
- Multiple travel loops rather than a single linear corridor.
- Clear visual anchors that help the player understand where they are.

### 8.1 The gradient

Chapter 1's difficulty and reward curve is a MAP, not a progression track. Near
the coast and the surviving roads the land was held longest: remnants are recent,
roads are still walkable, the few living people are here, and little threatens
you. Inland and upward the abandonment gets older — roads fade into the grass,
buildings give way to earthworks and monuments, and the wilds are held.

The player should be able to read that gradient from the landscape itself before
anything tells them, and should be able to walk into trouble early if they
insist. Nothing should gate the world by fiat; the world should simply get
harder to survive the further from the shore it gets.

The specific story, names, characters, and final Chapter 1 objective should
remain open until the world layout and core interaction loop are established.

---

## 9. Biomes and Natural Regions

Biomes should be recognizable through terrain, vegetation, water, weather, props, creatures, sound, color, and what people once did there. They should transition naturally instead of appearing as hard, arbitrary zones.

Each province also has its own relationship with the retreat: how long ago it was given up, what was worth building there, and what holds it now. A biome is a climate and a chapter of the history at the same time.

### 9.1 Forests

- Denser tree canopies and undergrowth.
- Narrower visibility and more enclosed paths.
- Groves, clearings, fallen trees, mushrooms, shaded ponds, and woodland ruins.
- Forest-specific flora, ambient animals, and collectible materials.

### 9.2 Meadows

- Open, flower-rich terrain with gentle elevation.
- Strong visibility toward landmarks.
- Frequent petals, grasses, small ponds, and peaceful wildlife.
- A welcoming region suitable for early exploration, and the last country to be abandoned.

### 9.3 Plains

- Broad, readable spaces with sparse clusters of trees and rocks.
- Longer roads, farmland possibilities, old fences, isolated buildings, and distant views.
- Strong use of weather, shadows, and moving vegetation to keep open land alive.

### 9.4 Snowy Hills

- Higher elevations, snow cover, exposed stone, colder water, and reduced vegetation.
- Ridges, narrow passes, steep drops, caves or shelters, and distant overlooks.
- Region-specific structures, clothing, animals, and resources.

### 9.5 Lakes and River Regions

- Large water surfaces, islands, banks, bridges, docks, reeds, and shoreline paths.
- Opportunities for swimming, crossings, fishing-related future content, and waterside remnants.
- Clear depth, shoreline, and traversal readability.

### 9.6 Swamps and Wetlands

- Shallow water, muddy ground, reeds, roots, small islands, fog pockets, and twisted vegetation.
- Slower or constrained routes without making traversal frustrating.
- Distinctive materials, creatures, resources, ruins, and environmental hazards.

### 9.7 Biome Transitions

Transition regions should mix the visual and ecological features of neighboring biomes. Roads, rivers, elevation changes, old field boundaries, and major vegetation lines can help make transitions feel intentional.

---

## 10. Terrain Language

Terrain should preserve the block-based, stepped character of the reference game while supporting larger, more deliberate geography.

The world should include:

- Readable elevation layers.
- Walkable terraces and plateaus.
- Cliffs that create natural boundaries and views.
- Designed stairs, ramps, passes, and crossings.
- Terrain shapes that guide movement without always appearing artificial.
- Local irregularity that prevents long surfaces from feeling sterile.
- Clear separation between safe traversal, difficult traversal, water, and inaccessible areas.

Grass-topped terrain should follow the established visual direction:

- The top of grass terrain remains light and clean.
- The upper part of an exposed grass ledge is green rather than brown.
- A standard terrain step is two blocks high: one green grass block over one substrate block.
- The substrate is dirt or stone according to the area and geological context.
- Deliberate one-block changes show only their single local material; deeper bands are reserved for authored cliffs and cuts.
- There is no separate dark-green fringe at the edge.
- Grass edges should read lighter than ordinary dark structural edges.

Terrain generation should avoid isolated single-block grass patches that contradict their surrounding region unless they are deliberately placed details.

---

## 11. Remnants, Holdouts, and Ruins

There are no villages, towns or cities in Petalfell. What the world has instead
is a graded series of **remnants** — the same kinds of place, at different
stages of being given up. They should share a reusable vocabulary while keeping
individual identity, and their design should respond to geography, old roads,
resources, biome, and the history of the retreat.

The generator chooses where they are using the same reasoning that chose where
settlements went: flat ground to build on, fresh water in walking distance, a
province that would grow something. Those were the reasons people settled there
ONCE. The world's history and its present come out of the same pass.

### 11.1 Holdouts

Somewhere two or three people still live. A holdout is small, defensible,
maintained, and obviously outnumbered by the emptiness around it.

- Two to four buildings, kept in repair, with the rest of the site derelict.
- A working palisade — patched, shorter than it once was, enclosing less than
  the original wall did.
- Smoke, lamplight, a garden actually being tended, a dog or goats.
- The remains of a much larger place around it, unmaintained.

The contrast is the whole point: a holdout should look like the last lit room in
a large dark house.

### 11.2 Remnants

Places nobody lives any more but which have not yet gone back to the land.

- Shuttered buildings with roofs mostly intact and doors closed.
- Streets and yards still legible under grass and drift.
- Wells, ovens, walls, fences, carts left where they stopped being useful.
- Almost nothing valuable, because it was carried out.
- Vegetation reclaiming from the edges inward.

### 11.3 Ruins

Older abandonment, where the structure has begun to lose.

- Roofs open, walls partial, floors under moss and blossom drift.
- Trees growing through and out of buildings.
- Collapsed sections that changed the shape of the place.
- Enough surviving form that its FORMER PURPOSE is readable — a mill without its
  wheel, a chapel without its roof, a gate with nothing behind it. A generic
  broken wall is noise; a ruin is only a ruin if the player can tell what it was.

### 11.4 Monuments and deep works

The oldest layer, and the reason to go inland: things built to outlast their
builders.

- Earthworks, causeways, terraced hillsides, retaining walls holding up nothing.
- Standing stones, arches, statuary, sealed doors.
- Structures whose scale plainly exceeded any use the surrounding land could
  have had for them.
- Work that is legibly PURPOSEFUL and whose purpose is not available to the
  player: channels that carry nothing, alignments that point at nothing, doors
  sized for something other than a person. The strongest version of this idea —
  Rain World's — is that the world was built by and for something that had its
  own reasons, and the player is a late visitor who does not get told what they
  were.
- Sites that are held, and dangerous, and worth it.

### 11.5 Population

Residents are rare, isolated, and never grouped. A chapter may contain:

- A hermit who will not leave a particular valley.
- A trader working a surviving stretch of road.
- A watchman, a scavenger, a cartographer, a keeper of something.
- Named characters tied to story or systems.
- Their animals.

Density is a design constraint, not an accident: meeting a person must remain an
event. If two inhabitants can be seen at once, there are too many.

---

## 11a. How built things meet the ground

Everything in §11 assumes the player believes the place was BUILT and then LEFT.
Neither half of that is carried by the building's own geometry. Both are carried
by the joint between the building and the land, and this section is the contract
for that joint.

Two failures produced the first build, and they are worth naming because they
are the default outcome of any generator that does not explicitly forbid them.

**The generator flattened the ground so the buildings would not have to deal with
it.** Every site levelled a disc and set boxes on it. That is the exact inverse
of what a real site does — a builder reads the slope and answers it, and the
answer is the most characterful thing about the building. A flattened site
throws away the only free source of variety the world has.

**Nothing was ever allowed to intersect the terrain.** Structures were placed ON
the surface, never in it, so no wall was ever half-buried, no floor was ever cut
into a bank, and nothing had ever settled. A wall stub emerging from a hillside
reads as centuries old. The identical stub resting on grass reads as placed this
morning. The generator could only produce the second one.

### 11a.1 The ground contract

A structure does not get flat ground. It gets a **footing**: a decision about
how this particular building answers this particular slope.

- **Cut and fill, never level.** The floor sits at the height that minimises
  earth moved, weighted so that FILLING is dearer than CUTTING — a building
  should sit into its hill rather than on a podium. The uphill side is cut into;
  the downhill side is carried on masonry.
- **The plinth is architecture.** The retaining wall that makes up the fall on
  the downhill side is not a repair to a placement error, it is the part of the
  building the player sees first and remembers. It varies with the slope, so no
  two instances are the same shape, at no authoring cost.
- **Steep ground splits the plan.** Past a threshold, a structure does not get a
  taller plinth — it breaks into two terraces at different levels with a step
  between them. At that point the terrain has determined the floor plan, which
  is the whole ambition of this section stated as a mechanism.
- **Nothing is ever sealed.** Wherever the floor stands above the land outside
  it, the footing owes the player a way up: a stair, a ramp, or a collapse that
  functions as one. A ruin you cannot walk into is worse than no ruin.

### 11a.2 Settling

Time banks earth against a wall. The footing raises a **talus** — a taper of
soil against the outside of the walls, deepest at the masonry and gone within
three or four blocks, scaled by how long the site has been given up. It buries
the lowest courses from outside without ever touching the interior floor, so the
building gains age while staying walkable.

This is the cheapest age signal in the project and it should be used everywhere,
including on things that are not buildings.

### 11a.3 Reclamation is a field, not a material swap

Moss is not a block type that a ruin sometimes uses instead of stone. Moss is
what grows where it is **damp, sheltered, low and undisturbed**, and it reads as
growth only because it appears in those places and not others. The generator
evaluates, per block face:

- **Damp** — standing water nearby, the province's moisture, and height above
  the building's base. The bottom of a wall is always damper than the top.
- **Shelter** — how enclosed the block is by its neighbours, and whether
  anything covers it. Crevices, inside corners and the underside of surviving
  roofs hold growth; an exposed parapet does not.
- **Aspect** — a face turned away from the sun keeps its moisture. This alone
  makes one side of a ruin visibly greener than the other, which is the single
  most convincing detail available.
- **Age** — the site's own abandonment, which everything else is multiplied by.

### 11a.4 What grows, and in what order

Reclamation is a **succession**, and reading it should tell the player roughly
how long the place has been empty.

1. **Moss and lichen** — first, on damp shaded stone, in the crevices, at the
   base. Never on the sunlit south face of a parapet.
2. **Vines** — on standing wall faces, hanging DOWN from the broken top course
   and from window heads. Vines need a wall to have survived, so a heavily
   vined ruin is one whose walls stood long enough to be climbed.
3. **Ferns and low scrub** — in the shelter of walls and in the corners of
   floors, where litter collects.
4. **Thickets** — out in the open floor once the roof is gone and the light gets
   in. A roofed room stays clear; an unroofed one fills.
5. **Saplings** — last, and only in the oldest sites, standing in what used to
   be a room. A tree growing out of a building is the end state of the sequence
   and should be rare enough to be an event.

### 11a.5 The contact line

The single strongest "this was stamped in" signal is the clean horizontal seam
where a structure meets the ground. Three things erase it, and all three are
required:

- **Rubble** at the foot of every broken wall, in that wall's own material,
  because a wall that lost its top courses put them on the floor.
- **Growth along the seam** — the ground detail layer thickens against masonry
  rather than stopping at it.
- **Talus**, per §11a.2.

### 11a.6 Resolution, and what this section does not fix

Everything above is achievable at one-metre voxels and should be built there
first, because it is cheap and because it is what actually determines whether a
ruin reads. It does not fix silhouette: a wall is still an axis-aligned stack of
metre cubes, and no amount of moss changes that.

The fix for silhouette is §14.3's modular kits — structures leaving the voxel
grid for meshes in their own transform, at sub-voxel resolution, with arbitrary
yaw and lean. That work is justified only AFTER the footing and reclamation
passes exist, because if the ground contract is still wrong then a beautifully
authored ruin sits on a pancake exactly like a blocky one does. `InkBuilder`
already proves the render side works — characters are non-voxel and take the
same ink as the terrain they stand on — so the route is open when it is wanted.

---

## 12. Roads and Travel Network

Roads connect geography, remnants, and gameplay. They are both the player's main orientation aid and the single richest piece of environmental storytelling in the world — a route is the shape of a journey somebody used to make.

Because the places they served are gone, the network is now a record rather than an amenity. It should be generated as the network that once existed, and then reclaimed.

The road system should include multiple types:

### 12.1 Major Roads

- Connect towns, cities, major gates, and regional destinations.
- Broad, well-maintained, and easy to follow.
- Supported by bridges, signs, lighting, rest points, and regular landmarks.

### 12.2 Local Roads and Approaches

- Once connected homes, fields, workshops, resources, and neighbouring places.
- Narrower and less formal than major roads.
- Adapt closely to local terrain.

### 12.3 Trails

- Lead through forests, hills, meadows, wetlands, and overlooks.
- May be lightly marked by worn ground, stones, plants, or occasional signs.
- Reward exploration without always appearing on the main route.

### 12.4 Reclaimed Roads

**This is now the dominant road class, not a rarity.** Most of the network is
in some state of being taken back:

- Surface broken by grass, roots, and drift, thinning to a trace.
- Sections lost entirely to slides, floods, and growth, leaving the route to be
  inferred across a gap.
- Bridges down, with the abutments still standing on both banks.
- Waymarkers and milestones surviving where the road itself has not.

A road that leads somewhere which no longer exists is one of the strongest
pieces of environmental storytelling available here, and it costs nothing: the
route was generated to connect places, and then the places were left.

Reclamation should scale with the gradient in §8.1 — near the coast a road is
merely shabby; deep inland it is a line of paving stones in a meadow.

### 12.5 Settlement Streets

- Reflect the scale and construction of the settlement.
- Range from rough farm lanes to the paved approaches of the larger works.

Roads should create loops, shortcuts, intersections, scenic approaches, and meaningful decisions. They should also provide reliable paths for the player, dog, NPCs, and future travel systems.

---

## 13. Landmarks and Discoveries

With no settlements to structure the map, **landmarks carry the whole burden of
orientation, pacing and reward.** They are promoted from set dressing to the
primary content layer, and there should be many more of them than the previous
plan implied.

Examples include:

- Abandoned houses and workshops.
- Collapsed bridges and unused gates.
- Old watchtowers with something still visible from the top.
- Ruined walls beneath vegetation.
- Empty shrines and artifact sites.
- Sealed caves and inaccessible passages.
- Forgotten roadside structures, waymarkers, and graves.
- Strange formations around water and cliffs.
- Caches: what somebody hid, or could not carry, or came back for and missed.

Each should answer at least one of: **tell me where I am**, **tell me what
happened here**, or **give me a reason to have walked over.** A landmark that
does none of the three is scenery, and scenery belongs to the procedural layer.

Some can support Chapter 1 discoveries, while others can remain hooks for later
quests, updates, chapters, or new gameplay systems. They should still belong
naturally to the current world and not feel like obvious placeholders.

---

## 14. Blender and Authored 3D Asset Plan

Petalfell should use Blender-authored assets where deliberate form, silhouette, animation, or repeated production quality matters. It should not attempt to procedurally construct every building, character, and prop from basic blocks at runtime.

### 14.1 Assets Best Authored in Blender

- Modular wooden building pieces, in intact and ruined variants.
- Modular stone town and city building pieces.
- Roofs, doors, windows, balconies, supports, stairs, and architectural trim.
- Wooden spike walls, fences, gates, towers, bridges, docks, and market structures.
- Signature buildings and unique landmarks.
- Ruins and abandoned-building variations.
- The player character and clothing or equipment variations.
- The dog and other important animals.
- Reusable NPC bodies, heads, hair, clothing, and accessories.
- Handheld items, weapons, tools, consumables, artifacts, containers, and trade goods.
- Carts, signs, lanterns, furniture, market stalls, and environmental props.
- Hero trees, unusual plants, statues, shrines, and focal scenery.

### 14.2 Assets That Can Remain Generated or Assembled

- Large-scale terrain.
- Repeated natural ground structure.
- Common tree and vegetation placement.
- Grass, flowers, reeds, stones, and ambient clutter distribution.
- Repeated road surfaces and broad route dressing.
- Minor color and material variation.
- Biome-dependent combinations of reusable authored assets.

### 14.3 Modular Asset Kits

Blender production should favor coordinated kits rather than isolated finished buildings. Each kit should support multiple combinations while maintaining a coherent style.

Planned kits should include:

- Wooden building kit (intact / shuttered / ruined).
- Stone monument and deep-works kit.
- Rural farm and roadside kit.
- Bridge and waterside kit.
- Defensive wall and gate kit.
- Market and public-space kit.
- Ruin and abandoned-structure kit.
- Biome-specific nature kits.
- Interior kit if enterable interiors become part of the chapter.

Unique structures can build on these kits and add bespoke parts where their silhouette or narrative role requires it.

### 14.4 Character Asset Direction

The player, NPCs, and dog should retain the simplified block-like proportions of the current game while gaining enough authored form to animate clearly and carry items convincingly.

A shared character asset family should make it possible to create visual variety through:

- Body and height variations.
- Faces and hair.
- Clothing layers.
- Regional and occupational outfits.
- Backpacks, hats, tools, weapons, and accessories.
- Color and material variants.

Important named characters may receive unique silhouettes while still belonging to the same visual world.

### 14.5 Texture and Material Direction

Textures should remain restrained. The world should rely primarily on shape, palette, lighting, subtle surface variation, and outlines rather than detailed realistic textures.

Authored textures are appropriate for:

- Subtle wood, stone, dirt, snow, cloth, and metal character.
- Small signs, symbols, markings, and decorative motifs.
- Controlled wear on roads, ruins, and frequently used structures.
- Item identity and readable interactive details.
- Soft natural variation that does not fight the block forms.

Textures should support the pastel look and remain readable at the normal gameplay camera distance.

---

## 15. Visual Rendering Direction

### 15.1 Overall Look

The Godot version should preserve the reference game's soft, pastel, miniature-diorama appearance:

- Light, harmonious palettes.
- Clean shapes and silhouettes.
- Gentle atmospheric depth.
- Soft but readable shadows.
- Restrained bloom and highlights.
- Petals and subtle environmental motion.
- Clear separation of surfaces without harsh realism.

### 15.2 Custom Shader Plan

Custom shaders remain an important part of Petalfell's identity. They should cover the visual behaviors that generic materials cannot express consistently, including:

- Stylized voxel and terrain surface color.
- Subtle face shading and baked surface variation.
- Grass, dirt, stone, snow, and biome-specific surface treatment.
- Water depth, color, motion, shoreline behavior, and underwater transitions.
- Vegetation movement.
- Petals and other lightweight environmental particles.
- Atmospheric grading and distance treatment.
- The explicit outline system.

The existing shader math and visual results are references, but the Godot shaders should belong naturally to the Godot rendering pipeline rather than imitate the structure of the Three.js code.

### 15.3 Outline Art Direction

The outline system is a defining feature and a visual acceptance requirement for the rebuild.

It should preserve these rules:

- Edges shared by two camera-facing light surfaces can receive a subtle light outline.
- Pale grass tops, tree tops, snow, and similarly light surfaces can participate in the light-edge treatment.
- Light outlines are slightly white, restrained, and never emissive or glowing.
- Remaining structural edges use dark outlines.
- Concave edges use dark outlines so recesses remain readable.
- Multiple edges meeting at a point form a single seamless connection.
- Lines do not visibly break at corners or intersections.
- Dark lines terminating against light lines stop cleanly and do not overextend through the connection.
- Outline width is fixed in screen space at every camera zoom and viewing distance.
- The established thicker default appearance, approximately equivalent to the current 3.2-width reference, is retained as the visual starting point.
- Lines remain clean and antialiased at the default gameplay resolution and zoom.
- Large scenes do not turn into dense tangles of unnecessary internal lines.

Characters require a specific variation of this rule:

- Their exterior silhouette remains clearly outlined.
- Fine internal edges between small body blocks are reduced or removed when they create clutter.
- Character simplification must not alter terrain, buildings, props, or the global outline behavior.

Water interaction with outlines should be deliberate and crisp. Where submerged outlines are hidden or changed, the transition at the waterline should appear as a hard visual stop with only the minimum softness needed for clean antialiasing.

### 15.4 Lighting and Shadows

Lighting should remain soft and flattering while providing stronger grounding than a flat pastel scene.

- Shadows should be high quality at the normal gameplay view.
- The visual baseline should be comparable to the current 3K shadow setting.
- Shadows should be slightly darker than the earlier washed-out version, without becoming harsh.
- Important characters and structures should remain grounded at all intended camera distances.
- Lighting quality should remain stable as the world streams around the player.

### 15.5 Water

Water should be treated as part of traversal and world composition, not simply a flat decorative plane.

It should support:

- Clear readable shorelines.
- Depth variation and soft pastel color.
- Lakes, rivers, ponds, wetlands, and waterfalls.
- Swimming and emerging from water.
- Visible but restrained motion.
- Appropriate interaction with terrain outlines, reflections, particles, and atmospheric color.
- Regional variation between clean lakes, streams, cold water, and swamp water.

The new Godot water direction should be based on the desired final appearance rather than preserving obsolete water code from the earlier prototype.

### 15.6 Atmosphere and Environmental Effects

The world may use subtle fog, aerial color, particles, and weather to create depth and biome identity. These should never obscure navigation or wash out the outline system.

Fog should remain optional through a player-facing setting. Development controls may expose broader visual adjustments for comparison and tuning.

---

## 16. Camera and Presentation

The camera should preserve the current elevated perspective and the feeling of looking into a miniature world.

It should support:

- A stable default gameplay distance.
- Controlled zoom without changing apparent outline thickness.
- Smooth player following.
- Clear composition around the player and dog.
- Good visibility of upcoming roads, terrain levels, and landmarks.
- Contextual framing for conversations, discoveries, interiors, or major locations when appropriate.

The default gameplay camera is the primary art target. Close zoom is useful for appreciation and interaction, but the game must remain beautiful and readable at the wider distance where it will normally be played.

---

## 17. Player Movement and Navigation

The Godot rebuild should preserve and refine the established movement identity:

- Direct player movement.
- Grounded acceleration and stopping.
- Traversal over block steps and designed elevation changes.
- Jumping where appropriate.
- Swimming with a readable waterline and buoyant movement.
- Reliable movement across roads, terrain, bridges, and settlement spaces.

### 17.1 Click-to-Move

Clicking a reachable point on terrain or other valid walkable surfaces should cause the player to travel there automatically along an approximate nearest valid path.

The experience should include:

- A destination chosen directly from the visible world.
- Graceful selection of the nearest reachable result when the exact point is unsuitable.
- Movement through terrain, stairs, roads, bridges, and appropriate water routes.
- Cancellation or replacement when the player gives new movement input.
- Clear failure behavior when no reasonable path exists.

### 17.2 Click Feedback

A click destination should produce a brief, subtle white hemispherical pulse at the selected world point. It should expand and disappear quickly, confirming the input without becoming a persistent marker or intrusive effect.

### 17.3 Shared Navigation Needs

The same world should support navigation for:

- The player.
- The dog.
- Townspeople and travelers.
- Wildlife where needed.
- Future moving enemies, escorts, or companions.

Different character types may have different traversal abilities, destinations, and behaviors while still understanding the same authored roads and terrain.

---

## 18. Dog Companion

The dog should carry forward as a permanent core feature.

Its baseline behaviors should include:

- Following the player using the world navigation system.
- Choosing natural nearby positions instead of occupying the player's exact path.
- Catching up when separated without creating distracting behavior.
- Sitting and remaining in place when instructed.
- Resuming following when called or commanded.
- Handling roads, steps, bridges, settlement spaces, and appropriate water traversal.
- Remaining readable and visually uncluttered at the default camera zoom.

Future companion growth can include reactions to NPCs, discoveries, danger, objects, locations, weather, and player actions. These extensions should build on the same companion identity rather than turning the dog into a generic combat unit.

---

## 19. Interaction System

The game needs one consistent interaction language for people, objects, animals, structures, and discoveries.

Potential interaction targets include:

- NPCs and traders.
- Artifacts and story objects.
- Containers and collectible resources.
- Doors, gates, levers, signs, shrines, and workstations.
- The dog and other animals.
- World objects that can be picked up, carried, used, thrown, or examined.

The interface should communicate available interactions with minimal text and avoid surrounding every object with permanent labels.

Interactions should be able to produce dialogue, items, choices, world-state changes, crafting access, trading, discoveries, and chapter progression.

---

## 20. The Living, and Dialogue

There is no ambient population. Every person in Petalfell is a specific
individual in a specific place, and the systems should be built for depth over
count rather than for crowds.

The foundation should allow for:

- Named individuals with a reason to be exactly where they are.
- Contextual activity tied to their situation rather than a daily schedule.
- Conversations and short ambient remarks.
- Trading and services, from people who have chosen to stay near a route.
- Reactions to player progress or local events.
- Giving, receiving, requesting, or recognizing items.
- A small territory they move within, not a settlement to walk around.

Because encounters are rare, each one carries far more weight than it would in a
populated world. It is worth spending on a handful of characters what a
populated plan would have spread across a hundred.

**The dead outnumber the living, and also talk.** Most of what the player learns
should come from places rather than people: what was left in a house, what a
grave says, what a route implies, what somebody wrote on a wall before leaving.
Environmental storytelling is not a supporting system here; it is the main one.

Dialogue presentation should fit the minimal visual language: brief, readable,
characterful, and free from unnecessary interface framing.

---

## 21. Inventory and Item Ecosystem

The future inventory should support meaningful objects appropriate to exploration, crafting, trading, utility, and combat-capable interactions.

Item categories may include:

- Natural resources.
- Crafted materials.
- Food and consumables.
- Tools.
- Weapons.
- Throwable objects.
- Quest and story items.
- Artifacts.
- Trade goods.
- Clothing, accessories, and equipment.
- Gifts and dog-related items.

Each item should have a clear purpose or identity. The game should avoid accumulating large numbers of visually different but functionally meaningless objects.

### 21.1 Collecting

Objects can come from exploration, gathering, discoveries, NPCs, trading, crafting, containers, and chapter rewards.

### 21.2 Crafting

Crafting should connect natural exploration with useful outcomes. Recipes, workstations, materials, and regional resources should reinforce the world rather than exist as an isolated menu system.

### 21.3 Trading

Trading should reflect who the trader is, what route they work, local resources, scarcity, and chapter progression. Because traders are individuals rather than shops, each one's stock is a characterisation as much as an inventory.

### 21.4 Quick Loadout

The player should have a compact hot loadout for frequently used objects without keeping the full inventory visible during ordinary exploration.

### 21.5 Left and Right Hands

The player should be able to assign appropriate objects to the left hand, right hand, or both where the object requires it.

Held objects may support actions such as:

- Using a tool.
- Consuming an item.
- Applying an item's property.
- Attacking or defending with an appropriate object.
- Throwing an object into the world.
- Giving or presenting an item during an interaction.

These behaviors should belong to a shared item language so that future content can expand them consistently.

---

## 22. Flora, Fauna, and Environmental Life

Nature is the world's health, and it is doing the heavy lifting that a
population used to do. With nobody about, **the landscape itself has to carry
"alive"** — through growth, weather, light, water, and animals going about
business that has nothing to do with the player.

Each biome should have a thematic set of flora and fauna.

Flora should define:

- Tree families and canopy density.
- Ground plants, flowers, grass, reeds, fungi, and shrubs.
- Seasonal or regional color.
- Gatherable resources.
- Landmark plants and rare variations.
- **Reclamation**: what specifically grows on, through and over abandoned work,
  and how that changes with how long the place has been left.

Fauna should define:

- Ambient birds, insects, and small animals.
- Water and wetland life.
- Larger regional animals where appropriate.
- **Feral descendants** of what people kept — goats on a ruined terrace, cats
  around a shuttered farm, overgrown orchards still fruiting.
- Creatures related to resources, interactions, or story.

Not every creature needs deep gameplay in Chapter 1. Ambient life can establish
the ecology first, while the shared content structure leaves room for later
behavior and interaction.

---

## 22b. The Wilds

Something drove the people out, and it is still here. This is the chapter's
threat layer and the reason its geography has a difficulty gradient.

### 22b.1 Design intent — ecology, not encounters

The wilds are **not a bestiary and not an army.** The model to follow is Rain
World's: creatures are not placed to be fought, they are living somewhere, and
danger is what happens when the player's route crosses theirs.

This is a genuine correction to the obvious design rather than a flourish. The
obvious design makes creatures territorial guards standing on treasure, which is
readable but dead — the creature exists only in relation to the player and does
nothing when unobserved. An ecological one has animals hunting, drinking,
sheltering, avoiding each other and moving between places on their own business.
It generates incident without scripting any, it makes the same creature feel
different on two visits, and — most importantly for this project — **it is the
best available answer to the central risk of an emptied world**, which §22 names:
with no people, something has to make the place feel inhabited, and an ecology
that carries on regardless of the player does exactly that.

Guiding constraints:

- **Creatures have lives.** Ranges, routines, needs, and relationships with each
  other. A predator that also avoids something bigger is worth ten that only
  notice the player.
- **Danger concentrates where abandonment is oldest.** Not because a level
  gate says so, but because those are the places nothing has been driven out of.
  This is what makes §8.1's gradient legible from the landscape.
- **Announce before engage.** A held place should be readable as held from
  outside it: signs, sounds, damage, and above all *the absence of ordinary
  fauna*. A silent wood is information.
- **Retreat must always work.** Leaving is a valid answer to everything. Nothing
  should pursue the player across a province.
- **Indifference over malice.** Nothing in the wilds is evil, and nothing is
  waiting for the player specifically. That is what keeps the world beautiful
  while it is also dangerous.

### 22b.2 What this commits the project to

This is the largest single addition to scope in this plan and it should be
costed honestly. It requires:

- Damage, health, and death for the player and for creatures.
- A recovery or consequence model for dying.
- Weapons and defensive items as real item categories, with the reach, timing
  and feedback that implies.
- Creature AI beyond the ambient-fauna wander: perception, needs, routine,
  approach, attack, disengage, and behaviour toward OTHER creatures.
  An ecological model costs more in AI than a territorial one and much less in
  authored content, because the incidents are emergent rather than placed. That
  trade is the right way round for a world this size.
- Animation sets for all of the above, on both sides.
- Audio to carry warning and impact.
- A pass over the dog, who becomes a companion who reacts to danger rather than
  purely a wandering friend.

None of this exists today. It should be built after the world, the exploration
loop and the asset pipeline are solid, and it should start with **one** creature
in **one** held ruin, taken all the way to finished, before a second is designed.

### 22b.3 A world rhythm — open question

Rain World's other structural idea is the cycle: a world-level event everything
obeys, which gives the day a shape and drives every creature including the player
to act. Petalfell has no equivalent and may want one — weather that closes in,
light that fails, something seasonal — because it would supply pacing that an
open exploration map otherwise has to get from quests.

Explicitly left open. It is a large systemic commitment, it interacts with
streaming and save state, and it should not be decided before the exploration
loop is playable.

### 22b.4 The dog

The dog's role changes and should be handled with care, because the dog is the
player's only steady company in an empty world. It should notice danger before
the player does, and its behaviour — stopping, staring, refusing ground — is one
of the best warning systems available and costs no interface at all. It should
not become a weapon.

---

## 23. User Interface and Menus

The interface should be minimal, modern, game-like, and visually quiet.

### 23.1 Visual Style

- White or very light translucent glass-like surfaces.
- Soft separation from the world without opaque heavy panels.
- Restrained typography and icon use.
- No unnecessary instructional text.
- No permanent display of controls such as map, menu, or movement keys.
- Clear focus states and readable contrast.

### 23.2 In-Game HUD

The normal exploration view should contain little or no persistent HUD. Information should appear only when useful, such as:

- A relevant interaction.
- A selected quick item.
- A brief status change.
- A conversation.
- A discovery or collected object.

### 23.3 Pause Menu

Escape should open a clean pause menu with only necessary options, such as:

- Resume.
- Settings.
- Save or load access where appropriate.
- Return to title.
- Quit.

### 23.4 Developer View

Developer rendering and world controls should be separated from player menus and toggled with the tilde key.

This view may expose tuning information such as:

- Outline appearance.
- Camera and zoom values.
- World loading distance.
- Level-of-detail behavior.
- Shadow quality.
- Fog and post-processing.
- Performance and streaming diagnostics.

Developer controls are production tools and should not define the final player-facing interface.

### 23.5 Map

If a world map remains part of the game, it should be an intentional exploration feature rather than a permanent HUD reminder. Its availability, detail, discoveries, and markers should fit the Chapter 1 progression.

---

## 24. Settings

Player-facing settings should remain understandable and limited to meaningful choices.

Likely categories include:

- Display and resolution.
- Overall visual quality.
- Shadow quality.
- Fog or atmospheric effects.
- Audio levels.
- Input and controls.
- Accessibility and text presentation.

Technical tuning controls such as exact outline width, loading radius, or diagnostic level-of-detail values belong in the developer view unless there is a strong player need.

---

## 25. World Scale, Streaming, and Performance Goals

Chapter 1 will be larger than the current world, so the game should treat the map as a collection of manageable regions rather than one permanently active scene.

The intended experience is:

- Nearby terrain, structures, details, characters, and gameplay remain fully present.
- Upcoming areas load in the background before the player reaches them.
- Distant areas leave active memory and processing when no longer needed.
- Returning to an area restores the same authored world and relevant persistent state.
- Loading never visibly removes ground or important objects around the player.
- Stones, plants, props, and other biome details remain correctly associated with their terrain patches.
- The loading area is configurable for development and quality tuning.
- The current reference of eight surrounding chunks is a useful starting baseline, not a permanent design restriction.

The target is a stable 60 frames per second at the intended default gameplay view and visual quality on the chosen baseline hardware. Performance improvements should preserve the visual identity rather than progressively removing the features that make the game attractive.

World generation and loading should feel fast enough that ordinary travel is never dominated by visible construction, missing decoration, or delayed collisions.

---

## 26. Audio Direction

Audio should reinforce the calm world, regional identity, and clarity of interaction.

The long-term audio foundation should support:

- Biome ambience.
- Water, wind, foliage, weather, and settlement sound.
- Footsteps and swimming appropriate to surfaces.
- Dog movement and reactions.
- NPC and crowd presence.
- Interaction, inventory, crafting, and trading feedback.
- Music that can change by location, situation, and chapter progress.

Audio should remain spacious and restrained enough that the natural world continues to feel peaceful.

---

## 27. Chapter 1 Content Layers

Chapter 1 can be planned through several overlapping content layers:

### 27.1 Critical Path

The required locations, characters, interactions, and travel needed to complete the chapter.

### 27.2 Regional Exploration

Optional roads, biome areas, overlooks, ponds, caves, ruins, and small settlements that broaden the world.

### 27.3 Character Content

The few living people, their concerns, trading relationships, dog moments, and recurring characters. Depth over count: a handful of individuals, each written properly, rather than a population.

### 27.4 Collection and Crafting

Regional materials, useful recipes, tools, consumables, trade items, and artifacts.

### 27.5 Environmental Storytelling

**The primary narrative layer, not a supporting one.** Architecture, abandoned places, reclaimed roads, old defences, unusual terrain, and object placement that imply history without explaining it directly. In a world with almost nobody to talk to, most of what the player learns has to be found rather than told.

### 27.6 Future Hooks

Locations, objects, characters, and unresolved details that can support later chapters without preventing Chapter 1 from feeling complete.

---

## 28. Production Phases

These phases describe outcomes rather than implementation procedures.

### Phase 1 — Visual and Movement Foundation

- Establish the Godot project identity and core scene presentation.
- Match the reference camera, scale, palette, terrain proportions, and movement feel.
- Reproduce the light/dark fixed-width outline language.
- Establish representative lighting, shadows, vegetation motion, particles, and water.
- Confirm that the player and dog remain readable at the default gameplay zoom.

### Phase 2 — Reusable World Foundation

- Define the map and chapter content structure.
- Establish fixed geography combined with procedural natural infill.
- Support large-region loading, unloading, persistence, and revisiting.
- Establish terrain, biome, road, settlement, and landmark vocabularies.
- Create a small representative region that includes all major content types.

### Phase 3 — Core Exploration Gameplay

- Complete direct movement, swimming, click-to-move, destination feedback, and dog behavior.
- Establish world interactions, NPC conversations, discoveries, and map progression.
- Establish saving and persistent world state.

### Phase 4 — Asset and Settlement Production

- Build the primary Blender modular kits.
- Establish player, dog, NPC, animal, item, and prop asset families.
- Produce the remnant, ruin and monument visual languages.
- Produce road, bridge, gate, market, ruin, and waterside content.

### Phase 5 — Inventory and Object Gameplay

- Establish collecting, inventory, quick loadout, and left/right-hand use.
- Add representative consumables, tools, throwable objects, weapons, crafting materials, and trade goods.
- Connect items to NPCs, resources, crafting, trading, and world interactions.

### Phase 6 — Chapter 1 World Production

- Finalize the fixed regional map.
- Build the major biomes, settlements, roads, waterways, and authored landmarks.
- Add generated natural dressing according to each region.
- Populate the world with NPCs, fauna, items, discoveries, and abandoned locations.
- Establish Chapter 1's main progression and optional exploration content.

### Phase 7 — Cohesion and Completion

- Refine pacing, visual consistency, travel times, navigation, and world density.
- Complete dialogue, audio, effects, menus, settings, saving, and accessibility needs.
- Balance visual quality, loading speed, memory, and frame rate.
- Ensure Chapter 1 feels complete while clearly supporting later expansion.

---

## 29. Reference Scene for Visual Approval

Before full production, one compact Godot reference scene should represent the visual standard for the whole project. It should contain:

- Grass terrain with dirt and stone lower ledges.
- A cliff, stairs, and concave terrain edges.
- Light and dark connected outlines.
- A tree with a pale canopy and dark trunk.
- The player and dog at default gameplay distance.
- A wooden structure and a stone structure.
- A road, shoreline, shallow water, and deeper water.
- Vegetation, stones, flowers, and petals.
- Representative lighting, shadows, fog, and post-processing.
- Several camera zoom levels for outline comparison.

This scene becomes the shared visual reference for new assets, shaders, biomes, settlements, and performance decisions.

---

## 30. Chapter 1 Definition of Success

Chapter 1 is successful when:

- The world feels intentionally designed rather than randomly generated.
- It is substantially larger and richer than the prototype without feeling empty.
- Its biomes, settlements, roads, and landmarks are distinct and connected.
- The player can navigate reliably through direct movement or click-to-move.
- The dog behaves as a convincing companion throughout ordinary travel.
- Swimming and water regions are meaningful parts of traversal.
- The outline system remains clean, connected, antialiased, restrained, and fixed-width at every supported zoom.
- Characters do not become cluttered by internal outlines at the normal camera distance.
- The game maintains its pastel atmosphere without glow-heavy light edges or washed-out depth.
- The interface stays minimal and does not distract from the world.
- The player can interact with people and objects through a consistent language.
- Inventory, equipment, item use, crafting, and trading have a coherent initial role.
- The world loads and unloads seamlessly during normal travel.
- High-quality shadows and environmental detail remain compatible with the performance target.
- The chapter contains a satisfying main progression, worthwhile optional exploration, and hooks for future content.
- A second map or chapter can be planned using the same foundations without restructuring the whole project.

---

## 31. Decisions to Make Before Full Chapter Production

The following creative decisions should be resolved after the reference scene and reusable world foundation are proven:

- Final Chapter 1 story premise and objective.
- Player identity and degree of character customization.
- **Resolved:** the wilds are hostile and hold the abandoned country. What remains open is the exact combat model, the consequence of death, and how many creature families Chapter 1 needs (start with one, finished, before designing a second).
- **Resolved:** the continent emptied by slow withdrawal, not catastrophe. What remains open is the timeline — how many generations, and which provinces went first.
- Whether interiors are seamless, separate spaces, limited to key locations, or mostly implied.
- Final world size and expected chapter duration.
- The names, cultures, histories, and functions of the peoples who left, and of the holdout that remains.
- The role of money, barter, or other trading values.
- Crafting depth and the number of useful item families.
- Whether the map is immediately available or discovered gradually. Discovery suits this world better and is worth testing early, since the map view is already built from world data.
- Travel options beyond walking, swimming, and roads.
- Target desktop platforms and whether browser delivery remains a requirement.

These decisions should refine the plan without changing its central model: a fixed authored world, procedurally enriched nature, Blender-authored modular content, custom stylized shaders, reusable gameplay foundations, and chapter-based expansion.

---

## 32. Guiding Rule

When choosing between more generation and more authored content, use the following rule:

> Author the things players remember; generate the variation that makes those things feel naturally embedded in a living world.

Petalfell's towns, roads, landmarks, characters, major geography, and story moments should be memorable and deliberate. Its forests, flowers, stones, terrain variation, ambient life, and small environmental details should make the journey between them feel broad, organic, and alive.

**And the rule this pivot adds:** the world is empty, not dead. Everything built
for it should make the place feel more alive and more beautiful — the emptiness
is supplied by the absence of people, and never by making the world itself
poorer.
