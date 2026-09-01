# Petalfell product and creative plan

## Promise

Petalfell is a slow exploration game about crossing a beautiful abandoned
continent. The primary reward is finding and understanding a place through its
terrain, architecture, weather, light and remains.

The game should feel calm, lonely and enormous without becoming empty. Travel
has texture: shelves, rivers, stairs, old roads, swimming, companions, carried
objects, weather and changing light make the route itself meaningful.

## Visual direction

- pastel voxel landscape with selective fine geometry;
- long-lens isometric/perspective framing;
- high-key stone and ground with restrained dark ink;
- broad readable shapes at far zoom and tactile breakup at play distance;
- translucent moving water with visible beds and shore depth;
- day/night light that preserves form and colour separation;
- architecture and terrain sharing materials, levels and weathering.

References `world-new/reference-*.png` are binding for current site structure.
Broader scenery references guide scale and landscape rhythm, not site layout.

## World

The production continent is the accepted 12,288 × 9,216 atlas. Macro land,
elevation, hydrology and biome maps establish one connected geography. Local
terrain is organic and layered rather than a literal extrusion of map pixels.

The continent contains mountains, high coasts, green basins, rivers, enclosed
lakes, blossom regions, wetlands, shallows and islands. Significant sites are
sparse, permanent and joined by an authored route graph.

## Exploration loop

1. See or learn of a distant place.
2. Choose a route using terrain, water, roads and landmarks.
3. Walk, climb stairs, cross, swim or detour through changing biomes and light.
4. Arrive at a site whose composition explains why it exists there.
5. Discover useful objects, stories, companions or abilities without turning the
   world into a checklist.
6. Continue toward another visible or remembered destination.

Fast map travel is a development and accessibility tool for the current phase;
the final travel economy can be decided after traversal and site density are
known.

## Sites and remnants

Major sites are reconstructed from supplied references. Their plan, scale,
circulation, damage and surrounding terrain are authored. They may use voxel
structures, prepared sculpture meshes and small props, but must read as one
weathered place.

Every built thing meets ground through foundations, cuts, fills, stairs, rubble,
drainage and reclamation. A flat pad with objects placed on it is not a site.

The world should ultimately contain enough significant destinations for long
journeys without losing scarcity. Do not set a final count until the first
reference families establish real production cost and spacing.

## Player and companions

The player is small relative to the world and architecture. Movement must remain
reliable on voxel terrain: walking, cautious slow-walk, jumping, route following
and swimming. The camera stays readable and player-controlled rather than zooming
around obstructions.

Future scope includes a newly designed inventory and loadout, a pet companion,
flora and fauna. Their old fixture implementations do not define the production
design. Build them for the production world after world/site work is stable.

## Narrative delivery

History is environmental. Routes, thresholds, repairs, abandoned tools,
collapsed spans, sculpture fragments and patterns of vegetation carry meaning.
Text and dialogue can clarify individual lives but should not explain what the
landscape already communicates.

## Production priorities

1. Keep the accepted terrain foundation stable.
2. Complete exact reference-site transcriptions and their review tooling.
3. Traverse and polish the whole atlas at day/night.
4. Design and integrate narrative/gameplay systems with permanent locations.
5. Add further sites only through authored allocation and measured plans.

## Quality bar

A release-quality area must:

- read clearly at far zoom;
- hold up at player distance and four camera quarters;
- connect organically to its surrounding biome and route;
- have reliable collision, stairs, water and handoff behavior;
- remain deterministic across runtime windows;
- match its supplied structural reference where one exists;
- preserve colour and silhouette through day and night;
- be explicitly reviewed, with author acceptance recorded separately from
  mechanical verification.
