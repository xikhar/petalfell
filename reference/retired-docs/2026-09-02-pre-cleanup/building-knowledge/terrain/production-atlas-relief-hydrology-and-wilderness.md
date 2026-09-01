# Production atlas relief, hydrology, and wilderness

- **Lifecycle:** `active`
- **Evidence summary:** compiler 27 is the latest complete-atlas mechanical
  baseline. It preserves the accepted macro geography, resolves relief on a
  global per-profile cell lattice with bounded noise bands, uses continuous
  slope-bound contour shoulders outside wind-scoured profiles, adds a
  fixed-angle crossing anisotropic ridge network within wind-scoured profiles,
  and forms a sparse macro front with three synchronous noise-broken toe ledges.
  One mode pass plus one despeckle pass runs with 40 cells of transient support
  before hydrology is reapplied. All 192 sectors pass the hydrology invariant,
  all 356 exact seams match, and an independent deterministic rebuild passes.
  The complete compiler-15 negative audit, compiler-16 positive baseline, and
  targeted compiler-19-through-22 capture history remain evidence for their own
  versions.
  Live optional compiler source is version 28 and has not run a complete-atlas
  batch, so it does not inherit the version-27 proof. Compiler-27 snow-front,
  highland, dry-scarp and river far captures
  plus the normal-start comparison set have been visually inspected and show
  that the current terrain is materialised, but broad terraces, sparse
  wilderness and flat banks remain; materials, final reference parity,
  playability and author acceptance are open
- **Scope:** `general` production-atlas derived terrain procedure, with
  continent-wide compiler-27 mechanical evidence for terrain/hydrology
  determinism and exact persisted seams, current representative compiler-27
  visual review, and historical compiler-15-through-22 evidence; it does not
  author macro geography or reference-site geometry
- **Last verified:** 2026-08-30 mechanically for the complete compiler-27 atlas
  and visually for the current compiler-27 snow-front, highland, dry-scarp,
  river and normal-start captures; no author-acceptance claim is made by this
  entry; live compiler source 28 remains unverified as a whole atlas
- **Supersedes:** none
- **Superseded by:** none
- **Owning sources:**
  [`AtlasSectorCompiler.cs`](../../src/World/AtlasSectorCompiler.cs),
  [`AtlasSectorWindow.cs`](../../src/World/AtlasSectorWindow.cs),
  [`AtlasWildernessDressing.cs`](../../src/World/AtlasWildernessDressing.cs),
  [`AtlasWildernessAuthoring.cs`](../../src/Tools/AtlasWildernessAuthoring.cs),
  [`AtlasBatchAuthoring.cs`](../../src/Tools/AtlasBatchAuthoring.cs), and
  [`biomes.json`](../../content/chapter_01/biomes.json)

## Outcome

Compile ordinary atlas terrain from the accepted elevation, land, hydrology,
and province images in independently rebuildable sectors. All derived relief,
profile transitions, water levels, material variation, trees, and boulders are
functions of global coordinates and declared profile data, so a sector does not
change when its build order or neighbour changes. The method adds local voxel
character inside the authored macro geography; it never chooses a mountain,
river, coast, biome, permanent site, route, or architectural silhouette.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| Compiler 16 renormalises bilinear elevation over land texels instead of treating the source's water zeroes as terrain | `mechanically verified` | implementation-specific | `AtlasSectorCompiler.TrySampleLandElevation` accepts only contributors whose registered land texel is land and divides by accepted weight; the complete compiler-16 audit below reports `submerged-dry 0` across all 192 sectors instead of the former dry-bank collapse | Mechanical clearance does not establish visually plausible banks |
| The manifest-backed hydrology audit enumerates the complete cached atlas and detects severe water steps, submerged dry banks, and exact seam mismatches | `mechanically verified` | tool-specific | `./tools/world-authoring.sh audit-atlas-hydrology`, 2026-08-30, compiler-15 manifest SHA-256 `7bcc711cd5eb7348e1e7c208d8431ed3214b203b885d060fb9cc9bd415e4a91a`: 192 sectors, 95,291,392 wet/wet edges, 327,119 stepped, 1,329 severe `>1`, max `5@8555,2399`, submerged-dry `0`, cross-sector invariant `0`, 180 horizontal plus 176 vertical seams, 13,943,808 overlap cells, mismatches `0`; all exact violations remain in `/tmp/petalfell-atlas-hydrology-v15.log` | This is negative evidence against compiler 15, not a compiler-16 whole-atlas pass; disposable `/tmp` evidence may not survive another machine/session |
| Compiler 16 bounds the formerly failing connected-water sectors to one block per cardinal edge | `mechanically verified` | tool-specific | `compile-sector`, 2026-08-30, on every compiler-15 severe sector `4,3`, `5,2`, `6,3`, `7,2`, `7,8`, `8,3`, `8,4`, `10,2`, and `11,3`: every result reports `severe 0`, `max-step 1`, `submerged-dry 0`; representative artifact SHA-256 values include `233b14ac60232307728a471a81f5fd56565a515612d95bddf22f36d11426861c` for 7,2, `0705ff85ce6e74d6ab3edb3ee4766a0f00eb520a36efb502479d09d0528613fa` for 8,3, and `3b6011cee11e5f139796c087128a08efae57d519b5b7c29b31227fd05577b737` for 11,3; the complete batch row below confirms the bound across the continent | One-block closure is a safety invariant, not evidence that every visible river descent is well composed |
| Compiler 16 repeat builds and east/south aprons are exact in the known 7,2 failure, the worst-count 8,3 failure, and review sector 6,4 | `mechanically verified` | tool-specific | `verify-sector 7,2`, `8,3`, and `6,4`, 2026-08-30: repeat hashes `bcfbea401258de3daa20089df0259d406f2d80472439217fcac4b647fe1f9c07`, `47e25eb4762937f8f719a09dbbb409fa04ce194569e65cc6b7378a652097512f`, and `10a1916fbf6a44a5e7b12d2aa2b9afc3c450afed439ff67fd9dee62213fa9c4b`; each compares 39,168 east and 39,168 south overlap cells exactly; the complete batch row below supersedes their former representative-only limit | These hashes remain useful local diagnostics, but future compiler fingerprints require a fresh full verification |
| The compiler-16 component-aware river surface in sector 6,4 has no adjacent step above one block and no dry cardinal bank below adjacent water | `mechanically verified` | tool-specific | `compile-sector 6,4`, compiler 16, 2026-08-30: artifact SHA-256 `d48369a83815ec10828b3d9d3dfba97828e80f9d566c0fb0bdc580bbaa0e4173`; `water-steps 3043 severe 0 max-step 1@4831,3072`; `submerged-dry 0`; land height `106..124`; water surface `108..121`; the repeat/seam result is recorded separately above | This does not establish flow direction, authored waterfalls, every atlas water body, or visual parity |
| Moisture reach, registered bank geometry, and visible shore are separate in compiler 16 | `mechanically verified` | implementation-specific | Continuous per-block water still owns wetness and material reach; `GeometricWaterAt` samples height shaping on the atlas-wide six-block natural-edge lattice; macro cuts above profile `preserveCutRise` are retained; visible shore varies coherently from one to three blocks. Sector 6,4 reports `floodplain 21.5%`, `bank 17.0%`, but only `shore 0.2%` | The classifications and percentages do not prove that every bank looks or plays correctly |
| Natural cleanup is applied before registered hydrology, so it removes tiny pre-bank teeth without blurring the final bank edge | `mechanically verified` | implementation-specific | Compiler 16 performs one radius-one mode pass and `minArea: 12` despeckle on `naturalHeight`, then reapplies floodplain/bank shaping from `GeometricWaterAt` and finally enforces the adjacent dry-height invariant | That compiler-16 48-block rectilinear room grammar and its cap-material regions still require broad visual judgement; no positive visual claim is recorded here |
| Wet-to-wet changes in water height are closed by two-sided vertical water curtains while shore edges remain terrain-owned | `mechanically verified` | implementation-specific | `AtlasSectorWindow.BuildWater` emits a curtain only when east/south neighbouring wet cells have different non-zero surfaces; the current sector-6,4 data bounds those differences to one block | Mesh construction does not establish accepted colour, reflection, transparency, waterfall appearance, or day/night response |
| Atlas-only water can hide broad submerged bed-room quilting while retaining the accepted legacy lake material | `visually reviewed` | tool-specific | All five fixed captures at explicit global focus `4999,3421` were inspected: `/home/shikhar/godot/shots/terrain-v15-river-6-4-polish-v4/atlas_play.png`, `atlas_near.png`, `atlas_wide.png`, `atlas_reverse.png`, and `atlas_far.png`, 2026-08-30. `WaterAtlasBody` `#a4a9df`, one atlas ramp stop, `cell_tone 0`, full atlas absorption, local saturation `0.80`, and reduced sky fallback (`fresnel_floor 0.10`, `reflect_strength 0.55`) remove the v3 bed quilt and read as a materially closer pale periwinkle across those views | The surface is still flatter and cleaner than `world-new/reference-1.png`; bank reflection, local movement/detail, night response, other water bodies, playability, and author acceptance remain unreviewed |
| Compiler-16 runtime visible Shore caps are limited to persisted Shore cells whose derived slope is exactly zero, without narrowing persisted wetness | `mechanically verified` | implementation-specific | `AtlasSectorWindow.MaterialiseColumns` applies the Shore cap only for `AtlasTerrainSurface.Shore && Slope == 0`; `dotnet build --no-restore` passes with zero warnings/errors, 2026-08-30 | v4 still shows disconnected pale runs on flat high-bank cells, so flatness alone does not yet distinguish a coherent beach/shoal from a terrain cap |
| Wilderness candidates are globally registered and independent of sector build order or unrelated random draws | `mechanically verified` | implementation-specific | `AtlasWildernessDressing` enumerates per-set global lattices, uses separate position/acceptance/shape salts, sorts by kind/set/cell, filters through the rendered profile, and tests full conservative canopy/boulder footprints before placement. Compiler-16 `verify-wilderness 6,4` reports manifest `acf92889c09f4a58`, 562 trees/121 boulders from 982 candidates, exact repeat 665,856 columns/763,260 voxels, east 6,208/7,337 and south 6,208/6,588; `verify-wilderness 8,3` reports manifest `9f7c2dcc4a757748`, 472/85 from 879, exact repeat 665,856/745,394, east 6,208/6,865 and south 6,208/6,455 | Two compiler-16 sectors do not prove atlas-wide ownership or every profile family |
| Authored domains and reference sites are protected by whole-shape exclusion rather than a trunk-only point check | `candidate` | general | Current `AtlasWildernessDressing` tests a conservative tree or boulder footprint against `DomainPlanExclusion` or `ReferenceSiteExclusion` before placement; missing test: a targeted headless build that proves no voxel from a canopy or boulder enters every kind of authored mask | Source inspection and a successful build do not prove rotated plans, all landmark envelopes, or future shapes cannot leak |
| All 192 compiler-16 sectors satisfied the hydrology gate and all 356 atlas seams passed that historical batch verification | `mechanically verified` | tool-specific | `compile-atlas`/`audit-atlas-hydrology`, 2026-08-30, manifest `3f4efd7096e32c5d994087a0d7daa952b51cb1cac68171a5f957129ea3bbaa09`: 192 sectors, 95,291,392 wet/wet edges, 329,331 stepped, severe `0`, max `1@635,329`, submerged-dry `0`, cross-sector invariant `0`, 180 horizontal plus 176 vertical seams, 13,943,808 overlap cells, mismatches `0`. Independent `verify-atlas`: 192 deterministic sectors, 127,844,352 repeat-build cells, 180 horizontal and 176 vertical seams | This proves the compiler-16 persisted terrain/hydrology batch only; it does not prove wilderness ownership atlas-wide, live playability, visual quality, or any later compiler fingerprint |
| Compiler 19's continuous wind-scoured relief field does not by itself produce acceptable mountain grammar | `rejected/superseded` | tool-specific | All five fixed sector-4,2 views under `/home/shikhar/godot/shots/compiler19-snow-4-2-v1` were visually inspected on 2026-08-30; the continuous field resolves as broad parallel ribbons rather than overlapping ridge shoulders and saddles | This is targeted negative evidence for one snow/mountain sector, not a claim about non-wind-scoured profiles |
| Compiler 20's locally terraced wind-scoured result does not solve the stamped-landform failure | `rejected/superseded` | tool-specific | All five fixed sector-4,2 views under `/home/shikhar/godot/shots/compiler20-snow-4-2-v1` were visually inspected on 2026-08-30; detached rectangular rooms and slabs remain legible as placed forms | This is targeted negative evidence for one snow/mountain sector, not a claim about non-wind-scoured profiles |
| Compiler 21's safe one-block quantisation is too dense as a visible mountain grammar | `rejected/superseded` | tool-specific | All five fixed sector-4,2 views under `/home/shikhar/godot/shots/compiler21-snow-4-2-v1` were visually inspected on 2026-08-30; the surface resolves as dense one-block ribbons instead of three or four separated courses in selected areas | This is targeted negative evidence for one snow/mountain sector, not a claim about non-wind-scoured profiles |
| Compiler 22 is deterministic and preserves the targeted snow and non-wind sector gates | `mechanically verified` | tool-specific | `verify-sector 4,2`, 2026-08-30, repeat hash `db931c65572d8beec4005a3f138849501dde9ae363d2f515f1b7c2e0185ea1cd`; the targeted snow result reports severe `0`, max-step `0`, and submerged-dry `0`. `verify-wilderness 4,2` reports manifest `a1c315fa48bb9e13`, 72 trees, and 124 boulders. The same-day non-wind river check `verify-sector 6,4` repeats hash `fa3d5780c142f0f80a878cc217990e817d2cfe6560937a79c22a630e7953e5db` and compares 39,168 east plus 39,168 south apron cells exactly; `verify-wilderness 6,4` reports manifest `c0cbf43a0dca7243`, 567 trees and 124 boulders from 982 candidates with exact repeat and seam corridors | This does not inherit compiler 16's complete-atlas proof; compiler 22 still requires a fresh full batch, all seams, and broader profile-family verification |
| Compiler 22's 12-block wind-scoured course treatment was its targeted visual candidate | `visually reviewed` | tool-specific | All five fixed sector-4,2 views under `/home/shikhar/godot/shots/compiler22-snow-4-2-v1` were inspected on 2026-08-30. A 12-block lattice and broad slope/altitude-gated smooth bias toward four-block courses, followed by one safe one-block round, avoid the immediately detached compiler-20 slabs and the compiler-21 one-block density | It is not author-accepted. Broad repeated snow courses and barren material remain conspicuous in reverse and far views, and no broader atlas/profile review was completed for that version |
| Compiler 27 is the latest complete-atlas mechanical baseline | `mechanically verified` | tool-specific | `compile-atlas`/`audit-atlas-hydrology`, 2026-08-30, source fingerprint `a9c535796c83271a06d091691184d1369401b0876ff1fc3b42b3b69542d458a3`, manifest `44a9b2033bd10fa879de0aa18b100ec84d866c8e17dc9d00cc49671e33c350b0`: 192 sectors, 95,291,392 wet/wet edges, 329,331 stepped, severe `0`, max `1@635,329`, submerged-dry `0`, cross-sector invariant `0`, 180 horizontal plus 176 vertical seams, 13,943,808 overlap cells, mismatches `0`. Independent `verify-atlas`: 192 deterministic sectors, 7,050,240 horizontal seam cells, 6,893,568 vertical seam cells and 127,844,352 repeat-build cells compared exactly | This proves compiler-27 persisted terrain/hydrology determinism and seams only; atlas-wide wilderness ownership, live playability, visual quality and author acceptance remain open |
| Representative current captures show that compiler-27 terrain materialises in review and normal-start runtime paths | `visually reviewed` | tool-specific | `/tmp/petalfell-v27-snow-front/atlas_far.png`, `/tmp/petalfell-v27-highland/atlas_far.png`, `/tmp/petalfell-v27-scarp/atlas_far.png`, `/tmp/petalfell-v27-river/atlas_far.png`, and the four images under `/tmp/petalfell-v27-normal-start/` were inspected on 2026-08-30 | Broad terraces, sparse wilderness, flat banks, materials and final reference parity remain visually open; this is not author acceptance |

The compiler-15 severe-step histogram was 1,222 edges of drop 2, 25 of drop
3, six of drop 4, and 76 of drop 5. Failures were confined to sectors 8,3
(716), 8,4 (192), 6,3 (145), 11,3 (96), 7,8 (81), 10,2 (48), 7,2
(26), 5,2 (13), and 4,3 (12). The compiler-16 targeted matrix above covers
that complete failure set rather than a hand-picked subset.

`dotnet build --no-restore` also passed on 2026-08-30 with zero warnings and
zero errors. That establishes compilation only, not any visual claim above.

## Procedure

1. Keep the accepted L0/L1 images authoritative for land identity, elevation,
   water, and province. Sample them in global block coordinates. A derived pass
   may shape a source feature locally, but may not move or invent it.
2. Compile `sectorSize + 2 * apron`, then calculate shoreline distance, slope,
   aspect, curvature, and wetness on the current 40-cell transient support border.
   Crop the support away only after metrics are complete. This prevents a
   persisted apron from depending on which sector owned the calculation.
3. Resolve biome profiles from authored province, elevation, water, macro
   slope, and low-frequency patch fields. Anchor every noise field and lattice
   in global coordinates with stable named salts. Never seed from local sector
   indices or consume one shared random stream whose draw count can change.
4. Let accepted elevation own the macro height. Add profile noise, ridge
   response, terrace quantisation, and local ledges only within declared profile
   amplitudes. Anything intended to read as a region uses a wavelength field,
   not a per-block hash.
5. Keep compiler 27's accepted macro geography and globally registered
   per-profile cell lattice as the current complete-atlas mechanical baseline.
   Use bounded noise bands; form non-wind-scoured relief from continuous,
   slope-bound contour shoulders and wind-scoured relief from a fixed-angle
   crossing anisotropic ridge network. Add the sparse macro front through three
   synchronous noise-broken toe ledges, not positive-only tiles, stamps, anchors
   or isolated pads. This is mechanically verified and visually inspected, not
   an accepted visual result: broad terraces and material parity remain open.
6. Clean only the pre-hydrology natural height: one radius-one mode pass followed
   by replacement of components smaller than twelve cells. Do not add another
   smoothing pass merely because a registered bank corner remains visible. Apply
   hydrologic geometry after cleanup so the final bank is not blurred.
7. Label water components once at registered source-pixel resolution. Treat the
   edge-connected component as ocean and enclosed components as lakes whose
   level follows the lowest surrounding authored bank. At block scale, choose
   among bilinear source contributors by weight so an antialiased fringe cannot
   silently switch an inland lake to ocean.
8. Build each channel's valley guide from a local minimum of authored elevation
   and grade it toward sea level through the global ocean-distance field. Use
   the same guide on both sides of the independent land/hydrology antialias
   fringe; selecting an inland body-bank altitude on one branch and the valley
   guide on the other creates a false wet/wet wall.
   Label four-connected permanent-water support at source resolution using all
   land-mask water and hydrology source values at least `0.90`. Dilate those
   immutable labels by exactly one source pixel so every bilinear corner of a
   realised permanent-water cell participates. Assign each dilation pixel one
   existing owner and relax only equal-owner neighbours: touching fringes must
   never union disconnected lakes. Lower only guide edges above
   `blocksPerPixel` source-height blocks; leave already-valid slopes and flat
   lakes unchanged. Bilinear interpolation then changes by at most one water
   block per cardinal realised edge. A future authored waterfall must split or
   exempt its declared owner edge; it is not permission for unexplained severe
   steps elsewhere. Keep one shared water-surface offset and use profile
   `surfaceDrop` to deepen the bed, not to introduce a longitudinal surface step
   at a biome boundary.
9. Treat the elevation image's zeroes under water as no-data. For land cells,
   renormalise bilinear interpolation over only registered land contributors;
   for water-side profile resolution, use the continuous valley guide. Never
   allow a water zero to pull the first dry bank toward the world floor.
10. Shape ordinary floodplain and bank height from `GeometricWaterAt`, which is
    registered to the same six-block natural-edge lattice. Converge toward the
    declared profile targets from either side, but preserve an accepted macro cut
    whose source rise exceeds `preserveCutRise`. After the complete field exists,
    require every dry cardinal neighbour to be at least one voxel above the
    adjacent water surface, rounded upward by the local terrace course.
11. Use the broader water-distance/profile `ShoreWidth` response for wetness,
    reeds, and later ecological dressing. Restrict the visible shore material to
    its narrow voxel fringe. The current runtime also requires a zero-slope cap;
    treat that as a minimum safety gate, not a complete beach classifier. v4
    still leaves some flat high-bank dashes, so a later parity pass must test
    water-relative elevation and coherent beach continuity before displaying a
    pale cap. Do not narrow wetness merely to hide material, and do not classify
    the entire hydrologic bank as sand or pale shelf.
12. Classify a cliff from the derived height delta, but preserve the resolved
   biome cap and substrate on its horizontal top. Apply the profile cliff
   material only to the deep vertical body. A cliff is a shape/material-face
   distinction, not a biome replacement.
13. Materialise equal-height water tops greedily. Where two wet cells differ in
    height, close that wet-to-wet step with a two-sided vertical water curtain;
    do not close the shore, because terrain owns the bank silhouette.
14. Resolve wilderness from data-backed vegetation and boulder sets after the
    terrain and any authored blockout/site geometry exist. Enumerate candidates
    on globally anchored per-set lattices; give position, acceptance, and shape
    independent salts; use the same `BuildProfileAt` categorical transition as
    the rendered cap; then filter by cap, water, slope, wetness, local height,
    and occupied voxels.
15. Exclude the full conservative influence footprint before placement: canopy,
    not just trunk; boulder radius, not just centre. Keep ordinary wilderness
    and authored reclamation separate. Only an L3 reclamation value may invite
    growth back inside a planned ruin.
16. Write only derived artifacts and review outputs. Never write compiler or
    wilderness results back into authored image, topology, domain, or site
    sources.

For site-owned courts, shelves, and terrain cuts, use
[Terrain and detached slab integration](terrain-and-detached-slab-integration.md)
instead. Production relief may supply the surrounding land, but it does not
replace the source-measured site plan.

## Checks

### Mechanical

1. Run `dotnet build --no-restore` after compiler, profile, artifact, or
   dressing changes.
2. Run `./tools/world-authoring.sh verify-sector X,Z` in representative snow,
   highland, river, woodland, bloom, fen, and shallows sectors. The repeat hash
   must match and every existing east/south overlap must compare exactly across
   height, water surface, land, water, hydrology, primary/secondary profile,
   blend, surface, slope, aspect, curvature, and wetness.
3. Run `./tools/world-authoring.sh compile-sector X,Z` for every representative
   connected river, lake, estuary, and coast. For ordinary connected water, the
   current gate is `severe 0` and `max-step <= 1`; any intended waterfall needs
   an authored identity and a deliberately revised metric rather than an
   unexplained exception.
4. Run `./tools/world-authoring.sh verify-wilderness X,Z` across several profile
   families. It must reproduce the full same-sector materialised grid and match
   east/south canonical seam corridors after the declared safety halo. Record
   manifest, candidate/placement counts, compared columns, and compared voxels.
5. Add and run a targeted exclusion test before promoting the current exclusion
   claim: place worst-case tree and boulder candidates beside rotated plan,
   stair, wall, landmark, and reference-site boundaries, then compare every
   placed voxel against the authored mask.
6. Once representative gates pass and sources stop changing, run
   `./tools/world-authoring.sh compile-atlas`. Its manifest-backed hydrology gate
   must report zero severe wet/wet steps, zero submerged dry boundaries, zero
   cross-sector invariant violations, and zero seam mismatches before the batch
   is successful. `./tools/world-authoring.sh audit-atlas-hydrology` reruns that
   cache-only scan without rebuilding sectors and prints every exact violation.
7. Then run `./tools/world-authoring.sh verify-atlas`. For the 16x12 atlas,
   require 192 deterministic sectors, 180 horizontal seams, 176 vertical seams,
   exact artifact/core/manifest/composite hashes, and a fresh-build comparison
   of every apron-bearing cell. Record the actual output; those counts alone are
   not a claim that the current batch passed.
8. Run `git diff --check`. These checks prove deterministic data and ownership
   invariants, not beauty, collision, or playability.

### Visual

Capture representative sectors with `capture-sector` through the required
workspace-5 silent launcher. Inspect play, near, wide, reverse, and far views, not
only the most flattering angle. Check:

- the accepted macro ridge, basin, river, lake, and coast remain legible;
- terraces read as a few overlapping blocky courses, not square pads, jagged
  contour rings, or hundreds of parallel stripes;
- cliff faces keep the local biome on top and expose the correct vertical body;
- a hydrologic bank stays ecological without becoming one broad pale shelf;
- trees and boulders form wavelength-scale groups, survive sector boundaries,
  remain out of authored footprints, and read at player, play, wide, and far
  distances;
- surrounding terrain looks integrated with a site without procedural dressing
  entering its measured composition.

Record exact image paths, camera views, and visible gaps. A completed capture
command is not a visual review, and agent inspection is not author acceptance.

## Scope and limits

This entry covers ordinary derived terrain and wilderness on the production
atlas. Exact relief amplitudes, shore widths, vegetation densities, palettes,
and suitability limits remain biome-profile data, not universal constants. It
does not cover legacy global-array terrain, authored site platforms, stairs,
walls, roads, landmark placement, hidden structure completion, map rendering,
runtime sector handoff, collision, shader parity, or day/night acceptance.

Compiler 15 has continent-wide negative hydrology evidence from one complete,
manifest-backed cache; it does not pass the ordinary-water invariant. Compiler
16 retains its historical positive continent-wide mechanical evidence from a
complete batch and independent deterministic verification. Sector 4,2 retains
the targeted capture history for compilers 19 through 22: 19, 20, and 21 are
rejected visual evidence, and 22 was a targeted visually inspected candidate
that never passed a complete-atlas rebuild or author review. Sector 6,4 retains
the older v4 water capture and its narrow compiler-15 pale-water/no-bed-quilt
material claim. Compiler 27 is now the latest complete-atlas mechanical
baseline. Its representative snow-front, highland, dry-scarp, river and
normal-start captures have been inspected, but broad terraces, sparse
wilderness, flat banks, materials and final reference parity remain open. No
terrain or wilderness claim in this entry is `author-accepted`.
Compiler source 28 is newer code, not a newer proof: its checked-in manifest is
still version 27 and it must complete the entire batch and independent verifier
before this entry can promote any version-28 atlas-wide claim.

## Known failures

- **Sector-local coordinates:** noise or candidate lattices keyed to a local
  window repeat or jump at seams. Use global coordinates and stable named salts.
- **Insufficient support:** computing slope, curvature, wetness, or shore
  distance only inside the persisted window changes edge cells. Compute on a
  transient support border, then crop.
- **Water zero treated as elevation:** the accepted elevation layer stores zero
  where the land mask is water. Ordinary bilinear interpolation mixed that no-data
  value into adjacent land and submerged dry banks. Renormalise over land texels.
- **Profile relief owning water altitude:** this produced adjacent channel
  jumps as large as 68 blocks. Permanent water now carves through profile
  relief and follows its authored valley guide.
- **Nearest-pixel valley lookup:** this produced a full-width artificial weir at
  each eight-block source-pixel edge. Bilinear guide interpolation replaced it.
- **Unweighted component lookup:** antialiased coastal/lake fringes could choose
  ocean merely because one neighbour had that label. Weighted bilinear
  contributors now choose water identity.
- **Body-bank/valley-guide branch split:** the land mask and hydrology mask have
  independent antialiased fringes. Using an enclosed body's bank altitude on the
  non-land branch and the valley guide on the water-valued land branch produced
  false wet/wet walls of two to five blocks. Both branches now use the same
  global guide.
- **Unbounded source-pixel guide slope:** bilinear interpolation removes
  eight-block weirs, but it cannot make a raw source guide whose neighbouring
  pixels differ by more than eight height blocks traversable. The complete
  compiler-15 cache contained 1,329 such realised steps across nine sectors.
  Compiler 16 labels four-connected permanent-water support, adds a non-merging
  one-pixel interpolation fringe, and lowers only equal-owner guide edges above
  `blocksPerPixel`; keep disconnected bodies separate and represent a future
  waterfall as an authored component edge, never as a global exception.
- **Biome-specific surface drop:** applying `surfaceDrop` to water surface made
  one connected river step at profile changes. It now contributes to bed depth.
- **No coastal grade:** a high inland guide met the ocean in one abrupt wall.
  Global source-pixel ocean distance now grades the approach.
- **Bank equals shore:** treating every bank cell as visible shore turned large
  parts of river sectors into pale shelves. Moisture remains broad; the visible
  shore is narrow.
- **Lowering-only bank shaping:** a bank pass that could only lower left noisy
  low terrain beneath adjacent water. Ordinary banks now converge on registered
  profile targets from both directions; `preserveCutRise` protects genuine
  source-authored scarps, and validation rejects any submerged dry boundary.
- **Filtering the completed bank:** smoothing after hydrology makes a decisive
  bank into long muddy staircases and can violate its water clearance. Clean the
  natural field once, then reapply registered hydrology.
- **Horizontal water tops without step walls:** one-block channel descent exposed
  the wet bed's cliff material between planes. Close only wet-to-wet height
  changes with vertical water curtains; leave shore edges open.
- **Depth-coloured atlas water exposing the bed plan:** retaining the legacy
  lake's transparent depth ramp made the atlas river show each broad submerged
  terrain room as a quilt. The v4 sector-6,4 review uses one opaque, low-chroma
  atlas body, removes per-cell tone, and reduces the reflection-less sky
  fallback. Keep this branch atlas-only; the accepted legacy lake still uses its
  depth palette and planar reflection.
- **Flatness treated as a complete beach test:** requiring `Slope == 0` removes
  pale caps from the steep bank itself but still admits disconnected flat
  high-bank runs in v4. Preserve that minimum gate while the later parity pass
  adds evidence-backed water-relative elevation/continuity; do not hide the
  symptom by erasing the broader wetness field.
- **Cliff class replaces cap:** assigning cliff material to the top block made
  snow and grass shelves look like pale masonry. Keep cap/substrate and use the
  cliff material for the deep face.
- **Single dense pad or contour rings:** isolated squares, broad stacked slabs,
  and nested jagged boundaries looked generated rather than block-built. Do not
  reintroduce positive-only tiles, stamps, anchors, or nested boundary grammar.
- **Continuous field mistaken for finished mountain grammar:** compiler 19
  removed discrete stamps but resolved into broad parallel ribbons in sector
  4,2. Continuity is necessary but does not by itself create ridge shoulders,
  saddles, or locally separated courses.
- **Local terrace mask recreating stamps:** compiler 20's selected terrace areas
  read as detached rectangular rooms/slabs. A smooth mask is not sufficient if
  its visible result has hard isolated footprints.
- **Safe one-block course everywhere:** compiler 21 preserved the mechanical
  step invariant but produced dense one-block ribbons. Keep one-block rounding
  as the safety output, not as the dominant visible course grammar.
- **Candidate promoted before distant review closes:** compiler 22 was a better
  targeted candidate, not an accepted result. Its reverse/far views still show
  broad repeated snow courses and barren material; preserve those gaps until a
  wider visual pass and explicit author decision close them.
- **Trunk-only site exclusion:** a legal trunk could still push canopy into an
  authored court. Test the conservative full influence footprint first.
- **Coupled random draws:** adding one candidate family could move every later
  object. Position, acceptance, and shape now use independent salts and a stable
  candidate order.
- **Equating a representative pass with atlas completion:** exact seams in one
  sector pair do not prove 192 sectors. Compiler 16 only reached continent-wide
  mechanical evidence after the complete manifest-backed batch and independent
  full verification above; every later compiler fingerprint must repeat that
  gate rather than inherit the result. Compiler 27 is the latest fingerprint to
  repeat it.

## Update triggers

Update this entry whenever compiler version, authored source registration,
sector/apron/support size, relief grammar, water-body labelling, valley/coastal
grading, shore/wetness classification, surface materialisation, biome profile
schema, wilderness candidate ownership, exclusion shape, reclamation ordering,
verification metrics, or capture framing changes. Add fresh evidence when a
new profile family, whole-atlas batch, exclusion test, playable runtime, visual
review, author correction, or author acceptance narrows or strengthens a claim.
