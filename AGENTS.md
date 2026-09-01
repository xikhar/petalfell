# Petalfell — agent guide

Petalfell is a pastel voxel exploration game in Godot 4.7.1 Mono/C#. The world
is quiet, enormous and mostly abandoned; walking to a place is the reward.

Read this file before editing. Then read the owning document for the change:

| Subject | Owner |
|---|---|
| Product and visual intent | [`plan.md`](plan.md) |
| Runtime boundaries | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| Implemented facts | [`CURRENT_STATE.md`](CURRENT_STATE.md) |
| Story geography | [`docs/WORLD.md`](docs/WORLD.md) |
| Atlas and terrain contract | [`docs/ATLAS.md`](docs/ATLAS.md) |
| Authored/derived data workflow | [`docs/MAP_PIPELINE.md`](docs/MAP_PIPELINE.md) |
| Reference-site construction | [`docs/RUINS.md`](docs/RUINS.md) |
| Current order and open work | [`docs/ROADMAP.md`](docs/ROADMAP.md) |
| Repeatable building methods | [`building-knowledge/README.md`](building-knowledge/README.md) |

`CLAUDE.md` is a symlink to this file.

## Current foundation

Normal startup has one world path. It opens a bounded 1,536-block window into
the authored 12,288 × 9,216 atlas, generates local ground with the proven
terrain/water/vegetation grammar, and overlays every promoted reference site in
the window. Walking and full-map Shift-click travel replace that window while
preserving global coordinates and player state.

The author accepted this terrain foundation on 2026-09-02. Treat its landform
language, layered block breakup, gradual shores, translucent moving water,
biome materials, vegetation and moving-window continuity as the baseline. This
does not accept every address, every site, or final lighting parity.

The former circular fixture and the separate compiled-sector terrain experiment
are not runtime alternatives. Retired implementation snapshots live under
[`reference/retired-code/`](reference/retired-code/) and old documentation under
[`reference/retired-docs/`](reference/retired-docs/). Do not restore their flags
or copy their macro planners back into production. The useful low-level terrain,
water, voxel, render, player and camera primitives remain active in `src/`.

## Non-negotiable rules

- The accepted land, elevation, hydrology and region maps own macro geography.
  They guide local construction; their pixels are not literal block boundaries.
- The continent is deterministic. Never re-roll significant geography or site
  placement from a seed.
- Runtime allocations stay bounded. Never allocate continent-sized voxel or
  height arrays.
- Significant sites are authored transcriptions of `world-new/reference-*.png`.
  Do not generate, remix or “improve” their composition.
- A site owns every visible stair, wall, pillar, arch, break, rubble mass,
  sculpture placement and terrain intervention. Reusable helpers may write
  voxels; they may not design the site.
- Terrain and architecture must meet through measured levels, cuts, fills,
  broken slabs and reclamation—not a flat stamped pad.
- Anything that reads as a region comes from a wavelength field, never a
  per-block hash.
- Authored sources are never written by generators. Derived output is never
  edited by hand.
- Colours are authored in sRGB and converted to linear exactly once. Ink
  classification uses the sRGB value.
- Noise hashes use unsigned shifts. A signed right shift destroys the intended
  distribution.
- Scale and visual fidelity are judged in-game at the locked isometric reference
  view plus multiple distances and quarter rotations.
- Only the author can mark visual work `author-accepted`. Follow
  [`building-knowledge/CERTAINTY.md`](building-knowledge/CERTAINTY.md).

## Reference sites

Before changing a site, inspect the actual reference image, its current plan and
all relevant building-knowledge entries. Keep evidence and corrections current
in the same session.

Current permanent sites:

- **Bloom Grove Court / Reference 10** — production terrain-integrated voxel
  transcription; preserved but not accepted as a complete site.
- **Fallen Colossus / Reference 12** — production terrain-integrated precinct
  using authored voxel foundations and the cleaned author-supplied head/legs
  GLBs; not accepted as a complete site.
- **Shallows Gate and Causeway / Reference 1** — next structural transcription;
  its measured source plan is authoring data, not permission to use a generic
  ruin kit.

## Code boundaries

`src/Main.cs` only dispatches authoring commands and starts production runtime.
The active world path is:

`ProductionTerrainGuide` → `Planner`/`Terrain` → `ProductionTerrainWindow` →
`AtlasSectorWindow` → renderer/collision → `AtlasRuntimeHandoff`.

The original low-level generator contains some historical vocabulary because
production deliberately reuses it. Code is sunset only when no production path
depends on it; archive such code with a non-`.cs` extension and document why.
Do not confuse “old origin” with “unused.”

## Documentation discipline

Update the owning document with every implemented change. `CURRENT_STATE.md`
contains facts only; `ROADMAP.md` contains only current order and open work.
Historical mechanisms belong in the reference archive or an explicitly
superseded building-knowledge entry, not in current instructions.

When a repeatable building method works, gains a stronger check, or is rejected
by the author, update `building-knowledge/` immediately. Preserve rejected
lessons there only when they prevent a likely repeat failure.

## Commands

Build:

```bash
dotnet build
```

Run normal production play:

```bash
godot-mono --path .
godot-mono --path . -- --terrain-focus=4500,1900
```

Core headless checks:

```bash
./tools/world-authoring.sh audit
./tools/world-authoring.sh atlas-preview
./tools/world-authoring.sh atlas-topology-preview
./tools/world-authoring.sh atlas-map-preview
./tools/world-authoring.sh verify-production-terrain 4500,1900
./tools/world-authoring.sh verify-production-playability 2692,2164 land
./tools/world-authoring.sh verify-production-playability 6400,7360 water
./tools/world-authoring.sh audit-production-terrain
./tools/world-authoring.sh verify-atlas-walking-handoff
./tools/world-authoring.sh verify-camera-auto-zoom
./tools/world-authoring.sh verify-camera-obstruction
./tools/world-authoring.sh verify-atlas-map-transport
./tools/world-authoring.sh preview-site-plan bloom-grove-court ../shots/bloom.svg
```

Visual review:

```bash
./tools/world-authoring.sh review-production-terrain 4500,1900
./tools/world-authoring.sh capture-production-terrain 4500,1900 ../shots/north atlas_play,atlas_wide
./tools/world-authoring.sh review-site bloom-grove-court
./tools/world-authoring.sh capture-site bloom-grove-court ../shots/reference-10
```

Every Godot GUI launch on the author's Hyprland machine must be silent on
workspace 5. Use the launcher in
[`building-knowledge/rendering/capture-overlay-and-acceptance.md`](building-knowledge/rendering/capture-overlay-and-acceptance.md).
Headless commands are unaffected.

On NixOS, if the Godot SDK path changed:

```bash
bash tools/setup-nuget.sh
```

Playable Linux build:

```bash
./tools/build-linux.sh && ./tools/run-linux.sh
```
