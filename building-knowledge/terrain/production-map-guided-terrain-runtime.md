# Production map-guided terrain runtime

- **Lifecycle:** `active`
- **Evidence summary:** the production terrain foundation is `author-accepted`;
  bounded generation, full-height transfer, repeated terrain data, atlas-space
  stairs, water invariants, safe landings, walking-window continuity and
  status-gated site overlays are `mechanically verified`; individual sites,
  exhaustive traversal and final lighting remain separate acceptance scopes
- **Scope:** the full production atlas through bounded terrain, water, shore,
  material, biome, map, handoff and authored-site windows
- **Non-scope:** exhaustive address-by-address atlas review, prolonged
  traversal/collision/swimming review, or final terrain/reference fidelity
- **Last verified:** 2026-09-02
- **Supersedes:** `fast-map-guided-legacy-review.md` and whole-atlas compilation as
  the inner visual iteration loop
- **Superseded by:** none
- **Owning sources:** [`ProductionTerrainGuide.cs`](../../src/World/ProductionTerrainGuide.cs),
  [`Planner.cs`](../../src/World/Planner.cs), [`Terrain.cs`](../../src/World/Terrain.cs),
  [`ProductionTerrainWindow.cs`](../../src/World/ProductionTerrainWindow.cs),
  [`AtlasSectorReview.cs`](../../src/Tools/AtlasSectorReview.cs)

## Outcome

The accepted continent owns macro land, elevation, hydrology and biome. The
original terrain system owns the six-block local lattice, warped terrace discs,
nested high-ground crowns, cleanup, ledges, bank courses, underwater shelves,
column materials, vegetation, water shader and ink. This is now the normal
production terrain path rather than an opt-in legacy demo.

The ordinary ground remains a heightfield, but rare accepted highland/snow
addresses may now add a deterministic sparse erosion arch above it. Candidate
identity, dimensions, chips and materials are keyed in global atlas coordinates;
the accepted land, wetness, biome and elevation fields decide whether it may
exist. The rock body is an asymmetric, three-dimensionally tapered mass with an
arched aperture and terrain-owned cap/substrate, not a reusable architectural
portal. A separate conservative mesh ceiling makes the chunk mesher and
collision inspect the roof without replacing the walkable `HeightAt` beneath the
opening. Vegetation reads that mesh ceiling as occupied space. This is a reusable
overhang mechanism, not permission to proceduralise a significant authored
landmark.

The accepted elevation image stores dry land in a compressed band rather than in
world blocks. A measured smooth transfer maps its `.50..98` land range to
`26..166` before the old local terrace grammar runs. Global crossing ridge fields
then cut only the accepted high northern mass into spines, saddles and shoulders.
Natural terrain is capped at Y 191, leaving the 256-block runtime headroom for
authored monuments. Current representative windows range from 25..46 in the
southern lowlands, 25..118 at the central river, and 25..189 at the northern
mountain front and high coast.

Hydrology keeps the accepted wet identity but evaluates it on the displaced old
six-block lattice. Low coasts can grade through roughly sixty blocks of beach;
wet beds deepen through several visible courses over a 72-block reach. High
rivers and coasts retain monumental sheer cuts but selected stretches climb
through four broad, contour-warped land benches over roughly ninety blocks. The
first attempt used three tiny attached ledges and was rejected because it read as
architecture. Water keeps the original translucent moving shader and now adds
two very low-frequency, slowly advected colour/domain fields so a wide view does
not collapse into one repeated ripple print. The correct scale split is
camera-distance based: retain the exact fine ripple and sheet stack through 105
blocks, fade it through 260, and leave a small residual plus the non-periodic
broad fields beyond that. Removing the fine terms globally makes play-distance
water lifeless; retaining them at full power at 170–300 blocks aliases their
crossing directions into diagonal wallpaper.

The signed shore response uses a bounded 3/4 chamfer distance. The first direct
runtime port used four-neighbour flood distance; at sixty-to-ninety-block reaches
that expanded every beach, bed course and canyon shoulder as a visible diamond.
The chamfer approximation restores the old river/lake grammar's rounder Euclidean
edge without moving accepted wet cells or water-surface Y. Its 96-block support
fits inside the 192-block moving-window comparison margin.

The old river planner supplied a centreline normal and therefore a stable bank
side. The production atlas supplies only an accepted wet silhouette. For narrow
inland reaches, sample the signed chamfer gradient to find the inward normal,
walk that normal to measure half-width, and reject the frame unless the wet run
along its perpendicular is at least three widths long. Orient that perpendicular
toward lower mapped bank elevation, with one stable global fallback on flat
water, then add the old `side * 0.10` term to the coherent bank field. Wide lakes,
ocean, junctions and ambiguous shapes must return no frame and keep symmetric
lake/coast response. This restores local river asymmetry without constructing a
new reach graph or changing accepted water.

The accepted region image remains a categorical province-allocation source, not
a literal material boundary. For each bounded window, a chamfer distance field
finds the nearest unlike accepted province within the declared transition
support. Absolute-coordinate distance wander and a broader weave field then
choose between the two original discrete biome surface and vegetation grammars.
This produces an interlocking ecotone without editing the source image, inventing
a new blended material system, or making window ownership visible.

A requested atlas coordinate materialises one sector-aligned 1,536-block window.
Walking near its edge generates the adjacent window; Shift-click on the full atlas
map generates one around that address. A map landing must satisfy both the local
3x3 clearance test and a traversal-component test: at least 4,096 connected
surface cells with a 48-block Manhattan reach. Water participates at its actual
surface, so a small low island connected by swimming remains valid, while a tiny
high shelf recovers to the nearest supported dry cell. Exact walking handoffs do
not use this sideways recovery and still preserve the current cell. Every
noise/material lookup uses global atlas coordinates. Accepted water is sampled on
the old six-block lattice through a continuous displacement field; the resulting
wet shape receives the old gradual bank and depth grammar instead of tracing map
pixels literally. Every canonical site promoted to `Production` or `Accepted`
overlays that same grid when its full permanent footprint is loaded. `Planned`
and `Blockout` sites remain visible to dedicated authoring/review tools but
neither reserve vegetation nor alter normal-play terrain. Bloom Grove Court is
promoted, and Fallen Colossus is the second promoted site. Each absolute
review-height datum is translated to the natural surface under its permanent
origin; neither plan nor authored geometry is rescaled by terrain generation.

The retired fixture used an allocation-wide connectivity stair planner. That
planner cannot be used inside a moving atlas window: the same overlap can receive
different component labels when the allocation origin changes. The production
path retains the useful two-block tread, three-block width and downward cut
shape, but selects sparse candidates from a 72-block global atlas lattice.
Each candidate reads one immutable bounded source neighbourhood and all accepted
cuts merge by minimum height, so candidate order and window ownership cannot
change the result. The original post-water `Despeckle(20)` remains in both paths;
its maximum changed component is far smaller than the production overlap margin.

Wetland water beds and saturated low banks use the existing mud palette rather
than a single generic wet-sand vocabulary. The hydrology and shore algorithms are
unchanged by this material distinction; it prevents a permanent fen basin from
reading as a white beach while retaining the same translucent water.

Production route-following uses the existing Shift-walk speed while it owns the
controller; manual WASD remains full speed unless Shift is held. A dry waypoint
must retain two empty placed-voxel blocks above its terrain surface, keeping a
later tree trunk or authored wall out of both click A* and the physical smoke
route while water remains swimmable. A land waypoint is complete only after the
capsule enters its cell-centre radius at the waypoint
height while grounded. Swimming is the intentional exception because the body
floats below the nominal surface Y. This is necessary at monumental elevation:
the former 1.05-block arrival radius could skip an adjacent stair cell, and an
airborne auto-jump could mark a higher waypoint complete merely by passing
through its Y. Both failures let the route cut off a one-cell summit shelf even
though the terrain graph itself was valid.

## Evidence

| Claim | State | Evidence | Remaining uncertainty |
|---|---|---|---|
| The map-guided terrain is the accepted foundation for continued world and site work | `author-accepted` | The author stated “Everything looks good as of right now up to the terrain level” on 2026-09-02 and requested cleanup around that current state | Acceptance is terrain-level: it does not accept every atlas address, either existing site's full fidelity, Reference 1, or final lighting/ink parity |
| The final direct terrain is deterministic, internally valid and landable at representative altitudes | `mechanically verified` | `verify-production-terrain` on 2026-09-01 after the biome-transition, chamfer-shore and derived river-side passes: summit shelf `4500,1900` hash `f8ca0780...`, high coast `3904,1312` `50a43aed...`, mountain front `2692,2164` `11871fcb...`, high river floor `2728,1576` `ed7ec969...`, river `4999,3421` `9dc956cd...`, wet fen `5107,6620` `1dc058c7...`, lowland `6400,7360` `350d5f3d...`, and erosion arch `2354,5500` `b5ad330a...`; every repeat matched, water steps/severe/submerged-dry were zero, and every requested address resolved deterministically. The Y78 mountain-front landing reaches 795,698 connected land cells over Y42..142 while the same window retains a Y182 summit; the Y156 summit landing belongs to a 1,404,428-cell connected land surface spanning Y76..189. The exact Y51 high-river landing retains a 9,857-cell low traversal floor over Y43..53 while its complete window spans Y25..189. Bloom `9800,4600` is `e7050569...`; Fallen `10600,4600` is `f8f87fa0...`. | This is a representative matrix, not all 113 million atlas columns |
| Every normal-play terrain window covers the whole atlas with consistent bounded ownership | `mechanically verified` | `audit-production-terrain`, 2026-09-01 after the biome-transition, chamfer-shore and derived river-side passes: all 165 sector-aligned 2x2 windows; 3,072 global walking-margin chunks; 10,560 owner fingerprints; 5,984 safe terrain and 14,208 all-overlap overhang comparisons; 389,283,840 observed window cells; four promoted-site build observations; zero water-step, severe-step or submerged-dry violations in every window; Y24..189; manifest `4575d8bdc98b27bd`; 415.3 seconds | Each window is built once in this sweep. Representative repeat hashes and physical routes remain separate evidence; visual acceptance is still address-by-address |
| The normal path respects the 192-block natural atlas envelope while preserving monumental relief | `mechanically verified` | the same matrix reports lowland 25..46, river 25..118 and both northern windows 25..189 | Authored structures may use the separate 256-block runtime headroom |
| Moving-window overlap is exact for terrain and dressing inside the playable handoff region | `mechanically verified` | the focused matrix compares 442,368 cells per east overlap after the runtime 192-block safety margin, including every terrain/hydrology/profile/material field and all placed tree/boulder voxels. The first wet-fen run exposed one window-owned stair at global `5584,6336`; the atlas-space candidate pass removed that mismatch. The whole-map audit then matched 5,984 safe chunk-owner comparisons and 14,208 complete overhang owner comparisons across every possible window. `2354,5500` additionally reports 771 overhang columns and 9,264 arch voxels in its focused neighbouring window | Prolonged player-controlled handoff remains open |
| The actual controller can traverse summit, mountain, high-coast, high-river and lowland-water terrain on collision-bearing runtime chunks | `mechanically verified` | after grounded waypoint arrival, placed-voxel headroom checks and cautious atlas route speed: summit `4500,1900` moved 21.57 blocks across Y142..158 in 664 frames; mountain `2692,2164` moved 44.15 blocks across Y72..78 in 571; high coast `3904,1312` moved 34.68 blocks across Y47..53 in 585; high river `2728,1576` walked 36.31 blocks across Y49..53 in 553 then swam 12.10 blocks in 182 frames at surface Y24; central river `4999,3421` walked 42.40 blocks across Y25..36 in 570 then swam 14.05 blocks in 125 frames; lowland `6400,7360` walked 34.61 blocks across Y25..27 in 550 then swam 10.06 blocks in 203 frames. Every land case settled grounded and every water case remained swimming. | Representative scripted routes are not prolonged author-controlled play or exhaustive atlas traversal; manual WASD speed is unchanged |
| A true sparse overhang can retain playable ground below its roof | `mechanically verified` | `verify-production-terrain 2354,5500`: exact landing Y46, 2,110,208 connected land cells, two repeat-stable arches/21,554 voxels, formation hash `9689a725bb0b2c3b`; the adjacent `3,6..4,7` window matches the intersecting overhang byte-for-byte | Live collision underneath and on the aperture edge still needs author play |
| Bloom and Fallen Colossus remain deterministic production overlays | `mechanically verified` | `verify-production-terrain 9800,4600` hash `e2685219...`, exact landing Y39, sites `bloom-grove-court,fallen-colossus`; `10600,4600` hash `fc609039...`, exact landing Y38, site `fallen-colossus`; repeat overlay statistics match | Whole-site fidelity and author acceptance remain open |
| Neighbouring walking windows preserve the current physical state without invisible handoff walls | `mechanically verified` | live historical log: east `11,5..12,6 -> 12,5..13,6`, exact surface 33 retained; `verify-production-terrain 6400,7360` on 2026-09-02 compares the real trigger line and accepts all 1,148 cells by matching the 3×3×5 collision/water neighbourhood. The superseded teleport-style rule rejects 698 of those identical cells: 470 water and 228 dry terrace/object-adjacent cells | Prolonged author-controlled crossing of several real window boundaries remains open |
| Full-map Shift-click generates and installs distant old-terrain windows | `mechanically verified` | `/tmp/petalfell-full-atlas.log`: successful requests at `2974,1012`, `6129,2247`, `6248,7485`, `6614,7799` and `6310,8103` | These trips do not by themselves visually accept every biome |
| Successful Shift-click transport closes the atlas after committing the landing | `mechanically verified` | `verify-atlas-map-transport` opens the production atlas surface, commits a successful transport through the runtime-facing method, and requires both open state and visibility to clear | Rejected requests are preserved by control flow because they do not reach `FinishHandoff`; live mouse-input review remains open |
| Map travel does not strand the player on tiny monumental shelf fragments | `mechanically verified` | after adding traversal-component support, the formerly stranded northern requests `6172,1460`, `5980,1540` and `6332,1252` recover deterministically within 2, 3 and 4 blocks to components of 34,690, 34,690 and 4,681 cells. The first two span more than 1,300 Manhattan blocks; the third spans 137. The actual controller then walked 33.62 blocks across Y49..53 from the recovered `6172,1460` request and settled grounded. Valid high-river `2728,1576`, Bloom `9800,4600` and Fallen `10600,4600` spawns remain exact, and the walking planner remains 4 cardinal/4 corner/1 partial-outer with exact handoff policy unchanged. | These are deterministic map/recovery checks, not author-controlled exploration of every atlas click |
| Northern high ground reads as connected mountain-scale shelves and a monumental front rather than a flat old-world relief band | `visually reviewed` | `/tmp/petalfell-summit-current/atlas_wide.png` inspects the current occupied Y156 alpine shelf; `/tmp/petalfell-mountain-stairs-v1/atlas_wide.png` inspects the lower Y78 approach, deep river cut, exposed walls and summit shelves inside a 25..182 window; inspected 2026-09-01 | Other northern addresses and author acceptance remain open |
| A high ocean edge supports both broad climbable shoulders and a monumental sheer reverse face | `visually reviewed` | `/tmp/petalfell-highcoast-current/atlas_wide.png` and `atlas_reverse.png`, inspected 2026-09-01; the fixed reverse view is partly occluded by the cliff while the playable controller route remains grounded | The exact balance of sheer versus shouldered runs is still author-reviewable |
| A permanent northern river can occupy a playable low floor beneath monumental canyon walls | `visually reviewed` | `/tmp/petalfell-river-side-high/atlas_wide.png` and `atlas_far.png`, plus `/tmp/petalfell-night-high/atlas_night_wide.png` and `atlas_night_far.png`, inspected 2026-09-01 after the chamfer and river-side passes; the far frames expose the Y24 river below a Y49..53 floor and surrounding terrain rising to Y189 in day and early night without adding a trench stencil | The composition is agent-reviewed rather than author-accepted; permanent traversal from floor to summit remains future authored route work |
| Nearby terrain and objects do not change the player's selected or rendered camera distance | `author-directed; mechanically verified` | `verify-camera-obstruction` places a blocking body on the sight line, removes it, and requires both selected and rendered distance to remain 75 throughout | Large walls may temporarily occlude the traveller; the author explicitly prefers that to automatic pull-in/recovery |
| Lowland beach, submerged continuation and broad water variation survive play, wide zoom and early night | `visually reviewed` | `/tmp/petalfell-water-play-low/atlas_play.png` was inspected against `/tmp/petalfell-terrain-lowland-final/atlas_play.png` and retains the legacy close ripple/refraction read; `/tmp/petalfell-water-far-filter-low/atlas_wide.png`, `atlas_far.png`, `atlas_night_wide.png` and `atlas_night_far.png` were inspected 2026-09-01 against the earlier `/tmp/petalfell-euclidean-shore-low` pair and replace the uniform far fine-grain print with broad moving patches while retaining translucent stepped beds. A fresh physical water smoke separately walked 34.61 blocks and swam 10.06 blocks at surface Y24 | Final reference-level lighting parity and author acceptance remain open |
| Central river banks and translucent depth remain readable at wide zoom after local side bias | `visually reviewed` | `/tmp/petalfell-water-far-filter-central/atlas_wide.png`, `atlas_far.png`, `atlas_night_wide.png` and `atlas_night_far.png`, inspected 2026-09-01 against `/tmp/petalfell-river-side-central`; selected banks keep their cadence and the broad water field survives day/night without radial geometry bands or a repeated fine ripple print | Explicit reach identity/station and authored pool/cascade semantics are not implemented |
| Bounded production stairs retain the old terrain cut shape without obvious repeated stamps | `visually reviewed` | `/tmp/petalfell-mountain-stairs-v1/atlas_play.png` and `atlas_wide.png`, inspected 2026-09-01 after the global-candidate correction | This is one mountain address, not author acceptance of stair frequency across the atlas |
| Permanent wetland water reads as a saturated mud basin rather than a pale beach | `visually reviewed` | `/tmp/petalfell-fen-stairs-v3/atlas_play.png` and `atlas_wide.png`, inspected 2026-09-01 with the restored post-water cleanup | Broader fen vegetation and author acceptance remain open |
| Categorical province ownership no longer becomes an exact material and vegetation cut | `visually reviewed` | `/tmp/petalfell-atlas-coverage-v2/wide-contact.png` and `far-contact.png` across nine atlas addresses, plus `/tmp/petalfell-biome-transition-v1/atlas_wide.png` and `atlas_far.png` at the fen border, inspected 2026-09-01 after the bounded chamfer/global-weave pass | The old palettes remain discrete inside a broad interlocking ecotone; every border address and author acceptance remain open |
| The first terrain-owned erosion arch reads as capped land rather than a ruin-stone portal | `visually reviewed` | `/tmp/petalfell-arch-current/atlas_wide.png` and `atlas_reverse.png`, inspected 2026-09-01 after the bounded stair correction; the earlier flat-deck and monolithic-stone passes were rejected | Candidate silhouette and frequency are not author-accepted |
| Both promoted sites remain embedded in the same terrain path | `visually reviewed` | `/tmp/petalfell-terrain-bloom-v2/reference_match_day.png` and `/tmp/petalfell-terrain-fallen-v2/atlas_wide.png`, inspected 2026-09-01 | Site fidelity remains unaccepted |
| Topology status is the generic production promotion gate | `mechanically verified` | `/tmp/petalfell-production-foundation-bloom.log`: Bloom `Production`, one overlay; `/tmp/petalfell-production-foundation-blockout.log`: Shallows `Blockout`, zero overlays | Promotion does not visually accept either site |
| The current code builds cleanly | `mechanically verified` | `dotnet build`, zero warnings/errors | Live movement/collision still needs author play |

## Procedure

1. Run `godot-mono --path .`; normal startup centres Bloom Grove Court in the
   full atlas runtime.
2. Press `M` for the full map and Shift-click to generate/travel to another
   address, walk normally across moving-window boundaries, or use
   `--terrain-focus=X,Z` to choose a starting address.
   For a repeatable quick view, use `review-production-terrain X,Z` or
   `capture-production-terrain X,Z output atlas_play,atlas_wide`; launch either
   GUI command silently on workspace 5.
3. Keep local morphology inside the original `Terrain` implementation. The map
   may guide macro position and mass, but it must not replace old low-level
   terrain, water, material, vegetation, outline or lighting primitives.
   Keep the region source categorical too: derive the ecotone from the owning
   provinces' declared transition support and globally keyed regional fields;
   never blur or rewrite the accepted image and never choose a biome from a
   per-block hash.
   Keep the legacy stair *shape*, but never call the allocation-wide
   connectivity planner from a moving production window. Production candidates
   must remain globally registered, bounded, source-immutable and merged through
   a commutative rule before application.
4. Author and inspect a new site at `Blockout`. Its site-owned builder must first
   support the terrain-relative vertical datum used by this bounded runtime.
   Change its canonical topology status to `Production` only when it should
   reserve its footprint and enter normal play; do not add a site-id branch to
   `ProductionTerrainWindow`.
5. Capture only the view needed for the current question. The production matrix
   exposes `atlas_night_wide` and `atlas_night_far` through the ordinary
   `DayCycle`; use them when water depth, shoreline ink or cliff readability at
   night is in scope. Do not run a continent-wide batch merely to judge a local
   visual rule.
6. Run `./tools/world-authoring.sh verify-production-terrain X,Z` for every
   representative address changed. It builds the normal 1,536-block window
   twice, validates every terrain data cell, compares the full content hash and
   site/formation statistics, resolves the same safe landing twice, then builds
   an adjacent window and compares the safe overlap including placed voxels.
7. Before treating a terrain revision as atlas-integrated, run
   `./tools/world-authoring.sh audit-production-terrain`. It builds all 165
   normal-play windows once and compares compact global chunk fingerprints; it
   is not a substitute for focused repeat, capture or playability checks.
8. A natural overhang writes sparse voxels but must not raise the gameplay
   heightfield beneath its aperture. Call `RaiseOverhangCeiling` so meshing and
   collision inspect the roof; keep `HeightAt` on the actual floor; make
   vegetation treat `MeshHeightAt` as occupied; compare all intersecting
   overhang voxels across a neighbouring window.
9. Keep generation bounded. Never allocate continent-sized runtime arrays or
   require a whole-world compile to enter the world.
10. For physical traversal, require routed land waypoints to be grounded at their
   own surface before advancing. Use cautious route speed in the production
   atlas, but do not lower ordinary keyboard speed or silently change the legacy
   fixture's configured click-route speed.

## Checks

- `dotnet build --no-restore`
- `./tools/world-authoring.sh verify-camera-obstruction`
- `./tools/world-authoring.sh audit-production-terrain`
- `./tools/world-authoring.sh verify-production-terrain 4500,1900`
- `./tools/world-authoring.sh verify-production-playability 2692,2164 land`
- `./tools/world-authoring.sh verify-production-playability 4500,1900 land`
- `./tools/world-authoring.sh verify-production-playability 3904,1312 land`
- `./tools/world-authoring.sh verify-production-playability 6400,7360 water`
- repeat at a high coast, inland water, low coast and each promoted-site address
- for an overhang focus, require non-zero overhang columns/voxels in the reported
  adjacent overlap and inspect wide plus reverse captures
- startup log contains `[production-terrain]`, `[production-site]` when Bloom is
  loaded, and `[atlas-runtime]`
- a `Blockout` site address produces ordinary terrain, while a promoted site
  prints `[production-site]` and survives a neighbouring-window replacement
- inspect the generated `map.png` together with the corresponding world view
- use `atlas_follow` to inspect the same fixed-distance playable camera; nearby
  collision must not reframe it
- confirm all Godot GUI launches are silent on Hyprland workspace 5
- never promote a generated capture to `author-accepted`

## Known failures

- The first walking handoff reused the map teleport's exact-landing validator.
  That test requires dry ground and a nearly flat, clear 3×3 spawn, so ordinary
  water, two-block terraces and nearby trees or ruins refused an otherwise exact
  overlap. The refusal armed a positional clamp 192 blocks inside the window and
  appeared as a random invisible wall. An already-physical player needs collision
  continuity, not a new spawn: compare terrain, water and the 3×3×5 solid
  neighbourhood between owners, and reserve the clamp for a real mismatch or the
  continent's outer edge.
- The obstruction ray introduced to protect high-cliff teleports was rejected by
  the author: walking near ordinary pillars pulled the view close, then walking
  away produced a slow outward drift. Camera distance is now controlled only by
  wheel input, K auto-zoom and developer settings. Do not restore collision-driven
  reframing to solve an occluded angle.
- Reimplementing the old look inside a new compiler produced hard boundaries,
  flat opaque water and slow feedback.
- Feeding accepted source pixels directly to block ownership made the atlas look
  cut out. Six-block sampling plus continuous atlas-space displacement and the
  old bank/bed formulas preserve macro intent without literal pixel edges.
- The first broad production bank pass measured distance with a four-neighbour
  flood. It was bounded and seam-safe, but its level sets were Manhattan diamonds
  at exactly the scale beaches and canyon shoulders are judged. Use the bounded
  3/4 chamfer approximation; do not change accepted wet identity to repair the
  presentation of the derived bank.
- Applying one random bank threshold to both sides of an atlas channel retained
  the water map but lost the old river grammar's one-sided cadence. Do not invent
  a seed-planned centreline. A bounded signed-distance frame can recover normal,
  width and downhill-oriented side on narrow reaches; reject wide or ambiguous
  shapes back to the shared lake/coast rule.
- Reusing the old `(elevation-.34)*11` relief compressed the continent to a few
  courses. Directly using the 256-block runtime ceiling then overcorrected and
  stole authored headroom. The measured transfer plus a Y191 natural ceiling
  gives the north a 164-block dry range while preserving the atlas contract.
- The first high-coast correction attached three narrow ledges to a single tall
  wall. Four broad displaced contour benches now shape selected runs, while
  other views keep a true sheer cliff.
- Production wet columns originally retained the legacy generic-wet hydrology
  code and some beds ended exactly on the water plane. The direct-window
  validator exposed both: water now has class 3 and every non-ford wet bed is at
  least one block below its translucent surface.
- Small ripple sums looked repeated at far zoom. Broad low-power shader fields
  now vary colour and ripple domain without feeding the high-power normal or
  specular paths.
- Raising the ordinary column height to make an overhang render turned its roof
  into the landing/navigation surface. Leaving it untouched made the chunk
  mesher stop at the ground and omit the arch. A separate conservative mesh
  ceiling preserves both truths.
- The first overhang was a flat rectangular deck with regular holes; the second
  was a symmetric monolithic stone portal. Three-dimensional taper, unequal
  shoulders, coherent edge erosion and terrain-owned cap/substrate replaced both
  rejected readings. That shape remains a candidate until the author accepts it.
- Snapping a quick window to one sector placed Bloom on its edge and clipped the
  site. Runtime windows now span 2×2 sectors and choose the nearest aligned centre;
  the original floor-biased centre caused an immediate handoff on startup.
- Reference blueprints retain their authored local Y relationships. Translating
  each datum onto the natural origin column preserves that geometry across the
  25..189 production relief range without creating a terrain-scale plinth.
- The legacy `CarveStairs` pass labelled the complete current allocation. Two
  overlapping 2x2-sector windows therefore disagreed at `5584,6336`: one saw a
  stair and pale cap while the other saw ordinary fen ground. Increasing overlap
  or weakening verification would hide the symptom, not fix ownership. Retain
  the old cut primitive, but derive production candidates from the same global
  lattice and bounded immutable neighbourhood in every window, then merge cuts
  by minimum height.
- The first physical summit route exposed two route-following assumptions that
  were harmless in the shallow fixture. A 1.05-block arrival radius accepted a
  cardinal waypoint from the neighbouring cell, while height-only arrival let
  an auto-jump consume a landing waypoint in mid-air. The body then drifted from
  a Y154 shelf to Y150 and could not climb back to the remaining route. Require
  cell-centred, grounded land arrival and use cautious production route speed;
  bounded settling after the final waypoint may wait through the remainder of a
  legitimate auto-jump arc, but must still fail if no floor appears.
- After the Euclidean-style shoreline pass, the lower-mountain collision smoke
  stopped on a flat Y76 waypoint. The terrain graph was valid; a generated tree
  occupied the route cell because `Navigation.Walkable` had treated every in-bounds
  column as open after vegetation. Dry route selection must inspect two blocks of
  placed-voxel headroom. Fixing only the smoke would leave real click navigation
  pushing forever at the same trunk.
- The old world had one generic wet surface vocabulary, so using sand for every
  production wet bed turned the fen at `5107,6620` into a beach. Surface palette
  is biome meaning, not hydrology identity: wetland beds/banks use mud while the
  existing water and shore geometry remain shared.

## Update triggers

Update when the elevation transfer or natural ceiling changes, shore/depth
distances change, representative hashes change, neighbouring-window support or
handoff changes, the global stair lattice/support/cadence changes, overhang
vocabulary or frequency changes, another permanent site is overlaid, focus/size
syntax changes, or the author accepts/rejects a named capture.
