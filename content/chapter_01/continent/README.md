# Chapter 1 macro sources

`land.png`, `elevation.png`, `water.png` and `region.png` are the accepted L0/L1
sources for the production continent. All four are 1,536 × 1,152 pixels with one
pixel representing eight atlas blocks. Never resize, crop, rotate or offset one
layer independently.

These images are authored input. Runtime terrain windows may read them but never
write them. Voxel columns, collision, vegetation, captures and previews are
derived and disposable.

## Formats

- `land.png`: 8-bit grayscale land/water mass.
- `elevation.png`: 16-bit grayscale broad height hierarchy.
- `water.png`: 8-bit grayscale hydrology; `240–255` is permanent water and every
  pixel outside land must be `255`.
- `region.png`: exact categorical province colours; no antialiasing or gradients.

The accepted files locate continent shape, mountain fronts, basins, rivers,
coasts and province intent. They do not dictate literal block boundaries. The
production terrain grammar displaces and realizes their shapes into organic
terraces, shores, submerged beds, materials and vegetation.

## Editing

Editing an accepted source reopens that layer. Set its atlas status to `Blockout`,
preserve canvas/mode/registration, change one macro question at a time, and
request author acceptance after inspecting the full map plus representative
production terrain windows.

Do not paint roads, individual sites, surface texture or erosion scratches here.
Those belong to topology and site plans.

After an edit:

```bash
./tools/world-authoring.sh audit
./tools/world-authoring.sh atlas-preview
./tools/world-authoring.sh atlas-topology-preview
./tools/world-authoring.sh preview-atlas-domain shallows-gateway-domain
./tools/world-authoring.sh verify-production-terrain 6400,6500
./tools/world-authoring.sh review-production-terrain 6400,6500
```

Then inspect other affected biomes/coasts and run `audit-production-terrain`
before accepting a terrain-system-wide change. The provenance of image-generated
map proposals remains in `world-new/map/GENERATED.md`; only these normalized
accepted sources are canon.
