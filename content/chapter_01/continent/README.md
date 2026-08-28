# Chapter 1 authored macro layers

This directory will contain the registered L0/L1 images declared in
`../atlas.json`. Their format and meaning are owned by
[`docs/ATLAS.md`](../../../docs/ATLAS.md) §5.

All seven images use identical registration and dimensions: **1,536 × 1,152
pixels**, one pixel per eight atlas blocks. Do not resize, crop, rotate or
independently offset one layer. Change its `status` in `atlas.json` from
`Planned` to `Blockout` when a correctly sized review source exists. Change it
to `Accepted` only after it has been inspected with the complete atlas and at
least one compiled sector. The atlas audit checks both non-planned states; a
blockout remains a warning so nobody quietly treats an experiment as canon.

These files are authored source. The sector compiler may read them but must
never write them. Generated heightfields, biome fields, voxel columns, road
masks and navigation data belong under disposable derived output, not here.

## Editing the current sources

`land.png`, `elevation.png`, `water.png` and `region.png` were accepted on
2026-08-27. Editing any of them again reopens it: change its atlas status back to
`Blockout`, review and compile the affected sectors, then request acceptance again.

`water.png` and `region.png` began as image-generated compositions informed by
the accepted geography, then were deterministically normalized to this package's
exact registration, mask and palette. The proposals and prompt record are in
[`world-new/map/GENERATED.md`](../../../world-new/map/GENERATED.md). The accepted
normalized sources are canon; the raw proposals are provenance only.

Open the PNGs directly in Krita, GIMP, Affinity Photo or another editor that can
preserve grayscale depth or an exact indexed palette. Keep the canvas,
registration and PNG mode unchanged.

- `land.png` is 8-bit grayscale. Black is water and white is land. The current
  blockout is binary; the first compiler treats 50% gray as the shoreline.
- `elevation.png` is 16-bit grayscale. Black is height zero and white is height
  191; sea level is 40. Every white land-mask pixel should therefore remain
  visibly above roughly 21% gray in the elevation source.
- `water.png` is 8-bit grayscale. `0` is dry/no hydrology influence, `1–239`
  increases drainage, wet-valley, bank and floodplain influence, and `240–255`
  is the permanent-water band. Every pixel outside `land.png` must be `255`.
  Paint connected watersheds and river corridors, not disconnected decorative
  strokes. Compiler version 4 converts this intent into floodplain, bank,
  permanent-water bed and absolute water-surface fields. The runtime review
  window materialises those derived fields; edit this image and rebuild rather
  than editing a `.pfs` artifact or the resulting voxel columns.
- `region.png` is an exact categorical image: black outside land; Cold Shelf
  `#d9dcf1`; Scarp `#c7ae9e`; Waist `#aebf91`; Bloom Reach `#e6b6ca`; Fen
  `#8fae9e`; Shallows `#9eb9d8`. Do not antialias, shade or introduce gradient
  colours. Boundaries should follow drainage divides, escarpments, basins and
  coast systems. The compiler derives a primary profile, nearest secondary
  profile and smooth weight from these colours and each province's declared
  transition width.

Edit one macro question at a time: continent/coast/island silhouette, then large
mountain fronts and basins, then drainage hierarchy, then terrain-shaped region
masses. Do not paint roads, individual ruins, surface texture or erosion
scratches here. Those belong to later layers or smaller-scale authored plans.

After every edit:

```bash
./tools/world-authoring.sh audit
./tools/world-authoring.sh atlas-preview
./tools/world-authoring.sh atlas-topology-preview
./tools/world-authoring.sh preview-atlas-domain shallows-gateway-domain
./tools/world-authoring.sh sample-atlas 6400,6500
./tools/world-authoring.sh compile-sector 8,8
./tools/world-authoring.sh verify-sector 8,8
./tools/world-authoring.sh review-domain shallows-gateway-domain
./tools/world-authoring.sh capture-domain shallows-gateway-domain
```

Review `../shots/world-atlas.svg` first, then the topology/domain overlay and
representative sector PNG. The permanent topology lives in sibling
`../topology.json`; the generated preview never writes it. Keep a revised layer
at `Blockout` while iterating. `Accepted` is an author decision after the macro
read and representative sector terrain have both been approved; the compiler
never promotes a layer itself.

The domain review reads these accepted sources through the same ordinary sector
artifacts and assembles only the nine sectors touched by the current southern
domain. Its platforms, routes, ruins, frayed paving, reclamation and trees are
derived review data. `capture-domain` records fixed near, wide, reverse and far
views in both late-morning and night light through the game's ordinary day-cycle
rig. To change a significant location, level, collapse depth, reclamation limit,
connection or silhouette, edit `../topology.json` or the registered domain
plan—not a capture, `.pfs` file or runtime voxel window.
