# Roadmap

## Settled decisions

1. The 12,288 × 9,216 authored atlas is the only production world.
2. Accepted macro maps own continent shape; the established terrain system owns
   local blocks, shores, water, materials and vegetation.
3. Normal play uses bounded moving windows and requires no terrain cache or
   whole-atlas compile.
4. The terrain foundation reached author acceptance on 2026-09-02.
5. Significant sites and their connections are authored, deterministic and
   permanent.
6. Supplied reference images are reconstruction sources, not mood boards.
7. Each production site owns its structure and terrain integration; no shared
   ruin generator designs it.
8. Camera zoom never reacts to obstruction.
9. Retired runtime implementations live under `reference/` and have no launch
   flags.

## Current order

### 1. Repository and documentation cleanup

- keep one production entry point and one documented world path;
- preserve retired implementation snapshots outside compilation;
- remove obsolete flags and current-doc references to abandoned pipelines;
- retain only checks that exercise accepted production behavior.

### 2. Shallows Gate and Causeway — Reference 1

- use the measured top plan and isometric source;
- choose/confirm its permanent compatible atlas location;
- transcribe terrain shelves, water cuts, bridge, stairs, gate, walls, pillars,
  damage and surrounding vegetation block by block;
- compare at the locked source angle plus top, four rotations and four scales;
- promote only after the author accepts the site.

### 3. Existing site fidelity

- compare Bloom Grove Court with Reference 10;
- compare Fallen Colossus with Reference 12;
- correct only measured discrepancies in scale, layout, surfaces, sculpture,
  terrain integration and vegetation exclusion;
- keep explicit site knowledge current.

### 4. Atlas traversal review

- walk long routes across several window boundaries;
- inspect representative mountain, river, coast, fen, lowland and island areas;
- fix localized collision or route errors without changing accepted macro maps;
- preserve exact handoff ownership.

### 5. Visual parity

- tune stone/ground breakup, ink weight, shadows, atmosphere and day/night light
  against supplied references;
- preserve high-key pastel readability and translucent moving water;
- judge at play, wide and far distances.

### 6. Content and gameplay

Allocate further sites and narrative routes only after the first three reference
families establish a reliable transcription vocabulary. Later product scope
includes newly designed inventory/loadout, pet, flora and fauna systems; their
old fixture implementations are not production specifications.

## Open questions

- Which permanent atlas coordinate best fits Reference 1 after terrain and
  hydrology are viewed together?
- Which remaining references establish the minimum structural vocabulary before
  allocating the wider chapter site set?
- Which lighting parameters close reference parity without crushing night ink?

Everything else should be answered by the current owning documents or direct
inspection, not by reviving a retired plan.

## Required gate for each spatial change

1. Build succeeds.
2. Focused production terrain/site data repeats exactly.
3. Neighbour ownership matches in the walking margin.
4. Relevant land/water collision smoke passes.
5. The required captures are generated and inspected.
6. Building knowledge records the repeatable method or rejected interpretation.
7. Author acceptance is recorded only after an explicit author decision.
