# Generated macro-map working references

These two images are proposals made with Codex's built-in image-generation mode.
They are retained so the origin of the registered blockouts is inspectable:

- `map-hydrology-generated-v1.png` — raw hydrology/watershed proposal;
- `map-region-generated-v1.png` — raw six-region proposal.

They are not registered source and must never be sampled by the compiler. The
compiler reads the normalized files under `content/chapter_01/continent/`.

## Hydrology prompt

Inputs, in role order: `map-color.png` for the broad visual geography,
`map-line.png` for journey-scale relationships only, `map-elevation.png` for
drainage logic, accepted `land.png` for the immutable coastline, and accepted
`elevation.png` for the immutable relief hierarchy.

```text
Create a single orthographic, top-down hydrology authoring map for this exact
Petalfell continent. Preserve the accepted landmass silhouette, islands, lakes,
aspect ratio and registration. Infer a coherent watershed network from the
accepted elevation: many fine headwaters begin in the northern and north-western
mountains; tributaries merge downhill into a small number of readable trunk
rivers; the north-east/east has its own drainage; the central basin and lake are
a major confluence; and the trunks broaden into a branching drowned delta and
archipelago in the south. Rivers must follow valleys, never climb ridges, never
terminate arbitrarily on land, and never form decorative evenly spaced lines.

Render this as a clean grayscale terrain-data pass, not a fantasy illustration:
dark dry ridges and watershed interiors, progressively lighter wet valleys and
floodplains, bright permanent rivers/lakes/ocean, smooth wide influence around
major channels, fine connected tributaries, no labels, no roads, no settlement
icons, no compass, no border, no paper texture, no perspective, no shadows and no
3D lighting. Make the drainage hierarchy legible at full-continent zoom while
retaining enough regional structure for deterministic sector compilation.
```

The generated 1,448 × 1,086 proposal was resized to 1,536 × 1,152, converted to
8-bit grayscale, gently blurred and level-normalized. The accepted land mask was
then reapplied exactly and every non-land pixel was forced to `255`. That result
is `content/chapter_01/continent/water.png`.

## Region prompt

Inputs, in role order: `map-color.png`, `map-elevation.png`, accepted `land.png`,
accepted `elevation.png`, and normalized `water.png`.

```text
Create a single orthographic, top-down categorical region authoring map for this
exact Petalfell continent. Preserve the accepted coastline, islands, interior
water, aspect ratio and registration. Divide all land into exactly six large,
connected, organic territories whose boundaries follow watersheds, mountain
fronts, escarpments, basin rims, river corridors, wetland transitions and coast
systems rather than circles, straight lines or arbitrary polygons.

The six territories are: a cold northern and north-western shelf; a western
stone scarp and quarry belt; a central green waist built around the river
confluence and inland basin; a broad blossom-rich eastern reach; a south-western
fen; and a drowned southern shallows/delta/archipelago. Every land pixel belongs
to one territory. Use one flat, clearly separated colour per territory and black
for ocean/interior water. No gradients, texture, shading, antialiasing, labels,
roads, icons, borders, legend, paper, perspective, lighting or decorative marks.
Territories must read at full-continent zoom and remain geographically plausible.
```

The generated 1,448 × 1,086 proposal was resized to 1,536 × 1,152, quantized
without dithering to black plus the six manifest colours, and constrained exactly
to accepted `land.png`. Black river gaps on accepted land were filled outward
from the nearest painted territory because hydrology, not region, owns rivers;
five tiny isolated-island groups were assigned to their geographic north/south
territory. That result is `content/chapter_01/continent/region.png`.

Image generation supplied organic composition. Deterministic normalization and
the atlas audit own registration, encoding, mask agreement and legal values.
