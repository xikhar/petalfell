# Map and world-data pipeline

## One canonical world

The production continent is authored once and addressed permanently. A seed may
drive subordinate texture/noise realization, but it never moves major geography,
routes or sites.

## Layer ownership

| Layer | Owns | Form |
|---|---|---|
| L0 | land mass and broad elevation | accepted raster sources |
| L1 | hydrology and province/biome intent | accepted raster sources + catalog |
| L2 | domains, route graph, site allocation/status | topology JSON |
| L3 | one site's measured ground plan and structure | reference plan JSON |
| L4 | fine surfaces, damage, sculpture meshes, props | site-owned JSON/GLB/assets |

Lower layers constrain higher layers. A generator may realize a layer but may
not rewrite the authored source beneath it.

## Terrain realization

At runtime, `ProductionTerrainGuide` reads L0/L1 in absolute atlas coordinates.
It supplies macro elevation, water and biome guidance to the proven low-level
terrain generator inside a bounded window. The generator contributes local
noise, terraces, broken layers, banks, beds, materials and vegetation.

This division is intentional: map pixels locate the mountain or river; terrain
grammar decides how its blocks erode, layer and meet water. Do not trace source
pixels as hard voxel boundaries and do not invent a replacement macro planner.

## Site workflow

1. Choose the supplied reference and inspect it directly.
2. Confirm a compatible permanent L2 location and connection.
3. Register source pixels, plan axes, runtime orientation and player scale.
4. Author the complete L3 top plan before adding vertical detail.
5. Build site terrain, stairs, walls, arches, pillars, rubble and exclusions as
   unique records.
6. Add L4 material patches and supplied fine meshes.
7. Review in the normal production terrain window.
8. Promote topology status only after the site should enter normal play.

No site builder chooses its own location. No reusable structure stamp designs a
production site.

## Fast iteration

Use a focused production window; do not generate the continent:

```bash
./tools/world-authoring.sh verify-production-terrain X,Z
./tools/world-authoring.sh review-production-terrain X,Z
./tools/world-authoring.sh capture-production-terrain X,Z ../shots/name atlas_play,atlas_wide
./tools/world-authoring.sh preview-site-plan SITE ../shots/site.svg --runtime-facing
./tools/world-authoring.sh review-site SITE
./tools/world-authoring.sh capture-site SITE ../shots/site-review
```

After a terrain primitive changes, run representative focused checks, then the
all-window audit. After a site-only change, verify its window, collision and
comparison captures; a continent-wide terrain run is unnecessary.

## Authored versus derived

Authored:

- accepted macro rasters and registration;
- biome/profile catalog;
- topology and route graph;
- reference ground plans, site records and source meshes.

Derived:

- runtime terrain windows and collision;
- surface/profile/hydrology arrays;
- vegetation and materialized site voxels;
- previews, captures, overlays and audit reports.

Derived output can be deleted and rebuilt. It must never become the only copy of
an author decision.

## Change discipline

When macro geography changes, edit and reaccept the owning raster/topology
source. When local ground looks wrong, change the low-level grammar without
moving accepted geography. When a site looks wrong, correct its measured plan or
records without reshaping unrelated wilderness.

Historical sector-compilation and circular-world workflows are archived under
`reference/`; they are not alternative authoring surfaces.
