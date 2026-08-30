# Production map-guided terrain runtime

- **Lifecycle:** `active`
- **Evidence summary:** adapter, build, startup, distant-window generation,
  automatic neighbouring-window replacement and the status-gated site overlay are
  `mechanically verified`; central-river and Bloom captures are `visually reviewed`
  for integration and path selection; terrain quality is not `author-accepted`
- **Scope:** the full production atlas through bounded terrain, water, shore,
  material, biome, map, handoff and authored-site windows
- **Non-scope:** prolonged traversal/collision/swimming review or final terrain/reference fidelity
- **Last verified:** 2026-08-30
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
cleanup, ledges, bank courses, underwater shelves, column materials, vegetation,
water shader and ink. This is now the normal production terrain path rather than
an opt-in legacy demo.

A requested atlas coordinate materialises one sector-aligned 1,536-block window.
Walking near its edge generates the adjacent window; Shift-click on the full atlas
map generates one around that address. Every
noise/material lookup uses global atlas coordinates. Accepted water is sampled on
the old six-block lattice through a continuous displacement field; the resulting
wet shape receives the old gradual bank and depth grammar instead of tracing map
pixels literally. Every canonical site promoted to `Production` or `Accepted`
overlays that same grid when its full permanent footprint is loaded. `Planned`
and `Blockout` sites remain visible to dedicated authoring/review tools but
neither reserve vegetation nor alter normal-play terrain. Bloom Grove Court is
the first promoted site. Only its absolute review-height datum is translated to
the smaller terrain range; its plan and voxel geometry are unchanged.

## Evidence

| Claim | State | Evidence | Remaining uncertainty |
|---|---|---|---|
| The Bloom 1,536-block production window builds without a sector batch | `mechanically verified` | `/tmp/petalfell-full-atlas.log`: plan 146 ms, terrain 2,009 ms, Site 0 705 ms, flora 429 ms, 197 primed chunks | Timing varies by host |
| A distant river window uses the same full-atlas path | `mechanically verified` | `/tmp/petalfell-full-atlas-river.log`: window `6,3..7,4`, 197 chunks, no compiler/cache load | Headless startup does not visually review the river |
| Neighbouring walking windows preserve the exact current surface | `mechanically verified` | live log: east `11,5..12,6 -> 12,5..13,6`, exact surface 33 retained | Prolonged author-controlled walking/collision remains open |
| Full-map Shift-click generates and installs distant old-terrain windows | `mechanically verified` | `/tmp/petalfell-full-atlas.log`: successful requests at `2974,1012`, `6129,2247`, `6248,7485`, `6614,7799` and `6310,8103` | These trips do not by themselves visually accept every biome |
| Central accepted hydrology becomes broken old-style banks, underwater shelves and moving translucent water | `visually reviewed` | `/tmp/petalfell-production-river-shots/map.png` and `wide.png` | Full coastline and night review remain open |
| The accepted region map changes the local biome grammar | `visually reviewed` | `/tmp/petalfell-production-bloom-shots/map.png` and `wide.png`; log reports Sakura 100% | Province transitions need adjacent-window review |
| Bloom Grove Court builds at `(9800,4600)` on this terrain path | `mechanically verified` | `/tmp/petalfell-full-atlas.log`: offsetY -76, 10,236 surface cells, 6,876 voxel writes | Site fidelity remains unaccepted |
| Topology status is the generic production promotion gate | `mechanically verified` | `/tmp/petalfell-production-foundation-bloom.log`: Bloom `Production`, one overlay; `/tmp/petalfell-production-foundation-blockout.log`: Shallows `Blockout`, zero overlays | Promotion does not visually accept either site |
| The current code builds cleanly | `mechanically verified` | `dotnet build`, zero warnings/errors | Live movement/collision still needs author play |

## Procedure

1. Run `godot-mono --path .`; normal startup centres Bloom Grove Court in the
   full atlas runtime.
2. Press `M` for the full map and Shift-click to generate/travel to another
   address, walk normally across moving-window boundaries, or use
   `--terrain-focus=X,Z` to choose a starting address.
3. Keep local morphology inside the original `Terrain` implementation. The map
   may guide macro position and mass, but it must not replace old low-level
   terrain, water, material, vegetation, outline or lighting primitives.
4. Author and inspect a new site at `Blockout`. Its site-owned builder must first
   support the terrain-relative vertical datum used by this bounded runtime.
   Change its canonical topology status to `Production` only when it should
   reserve its footprint and enter normal play; do not add a site-id branch to
   `ProductionTerrainWindow`.
5. Capture only the view needed for the current question. Do not run a 192-sector
   batch merely to judge a local visual rule.
6. Keep generation bounded. Never allocate the continent-sized legacy arrays or
   require a 192-sector compile to enter the world.

## Checks

- `dotnet build --no-restore`
- startup log contains `[production-terrain]`, `[production-site]` when Bloom is
  loaded, and `[atlas-runtime]`
- a `Blockout` site address produces ordinary terrain, while a promoted site
  prints `[production-site]` and survives a neighbouring-window replacement
- inspect the generated `map.png` together with the corresponding world view
- confirm all Godot GUI launches are silent on Hyprland workspace 5
- never promote a generated capture to `author-accepted`

## Known failures

- Reimplementing the old look inside a new compiler produced hard boundaries,
  flat opaque water and slow feedback.
- Feeding accepted source pixels directly to block ownership made the atlas look
  cut out. Six-block sampling plus continuous atlas-space displacement and the
  old bank/bed formulas preserve macro intent without literal pixel edges.
- Snapping a quick window to one sector placed Bloom on its edge and clipped the
  site. Runtime windows now span 2×2 sectors and choose the nearest aligned centre;
  the original floor-biased centre caused an immediate handoff on startup.
- The reference blueprint was authored around Top-Y 108 while this terrain uses
  the old 76-block relief range. Translating its datum onto the natural centre
  column preserves all relative site geometry without creating a 78-block plinth.

## Update triggers

Update when neighbouring-window support/handoff is added, the production map UI
targets this runtime, another permanent site is overlaid, focus/size syntax
changes, or the author accepts/rejects a named capture.
