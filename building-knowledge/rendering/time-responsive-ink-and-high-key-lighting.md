# Time-responsive ink and high-key lighting

- **Lifecycle:** `superseded`
- **Evidence summary:** the former shared implementation was `mechanically verified`;
  its locked Reference 10 late-morning and night captures are `visually reviewed`
  historical evidence, but the author rejected the resulting outline system on
  2026-08-30 and the earlier fixed legacy ink was restored
- **Scope:** `general` implementation path; present visual evidence is
  `site-specific` to Bloom Grove Court / Reference 10
- **Last verified:** 2026-08-30
- **Supersedes:** the single equally dark, temporally constant dark-ink response
- **Superseded by:** the restored `a4bee16` ink shader/defaults used by the
  [production map-guided terrain runtime](../terrain/production-map-guided-terrain-runtime.md)
- **Owning sources:**
  [`Palette.cs`](../../src/Core/Palette.cs),
  [`ink.gdshader`](../../shaders/ink.gdshader),
  [`WorldMaterials.cs`](../../src/Render/WorldMaterials.cs),
  [`DayCycle.cs`](../../src/Render/DayCycle.cs),
  [`Atmosphere.cs`](../../src/Render/Atmosphere.cs),
  [`DeveloperMenu.cs`](../../src/Tools/DeveloperMenu.cs)

## Outcome

**Do not reapply this procedure.** Its internal-softness split, 1.05 px default
and separate dark-family night response were rejected. The active path uses the
earlier 1.30 px legacy ink and its single pale-family night step.

Keep the explicit fixed-width edge graph, but give different weight to a
camera-facing internal turn and an exterior silhouette. Both dark and pale ink
families are re-inked at the day-cycle steps. The shared lighting rig keeps the
late-morning frame high-key and gives the post-sunset key the moon's colour as
well as its direction. This does not hide incorrect geometry, replace measured
materials, or establish one-to-one reference parity.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| All three ink passes receive the same 1.05 px default and internal-softness parameter; the developer overlay changes both on every pass; both pale and dark families consume `pf_night` | `mechanically verified` | general | `dotnet build --no-restore` with 0 warnings/errors; fixed capture successfully compiled and rendered the shader; 2026-08-30 | Build and shader compilation do not prove the chosen values are visually right at every distance |
| At the locked late-morning view, internal stair/terrace turns are quieter while architecture, terrain, and tree silhouettes remain continuous | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-10-ink-parity-v2/reference_match_day.png`, inspected beside `world-new/reference-10.png`, 2026-08-30 | Current geometry and stone weathering differ substantially from the source; shadows remain somewhat harder than the reference |
| At the locked night view, court levels, stair treads, pillars, trees, and the traveller remain separable without the former white-looking pale-edge dominance | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-10-ink-parity-v2/reference_match_night.png`, 2026-08-30 | No supplied night source exists; this proves readability, not reference matching or author acceptance |
| The night key is cool rather than brown-magenta because the single key now blends to the authored moon colour after twilight, not only to the moon direction | `visually reviewed` | site-specific | v1 versus v2 night captures under otherwise identical fixed camera/time; `/home/shikhar/godot/shots/reference-10-ink-parity-v1/reference_match_night.png` and `/home/shikhar/godot/shots/reference-10-ink-parity-v2/reference_match_night.png`, inspected 2026-08-30 | Other biomes, water, Reference 1, deep midnight, and live clock transitions remain unreviewed |

## Procedure

1. Preserve the edge graph, depth test, endpoint union rules, and opaque analytic
   stroke body. Do not replace the system with a Sobel pass or inverted hull.
2. Classify a visible edge with the existing adjacent-face test. When both faces
   face the camera, lift a permanently dark internal turn toward the pale ink by
   the shared `internal_softness`; when either face turns away, retain the darker
   silhouette colour.
3. Apply the quantised `pf_night` response to both ink families. Scaling only the
   pale family leaves every permanent-dark edge fixed across the entire day and
   is the source of the temporally constant ruled-line read.
4. Tune width and internal softness through the shared palette/material path so
   legacy play, atlas play, site review, characters, and props do not fork into
   separate looks. Expose both values in the tilde developer overlay.
5. Keep the high-key result in the shared grade and day-cycle rig: restrained
   saturation/contrast, lower shadow opacity, broader filtered shadow blur, and
   enough night ambient/fill to retain surface colours.
6. When the key direction swaps from sun to moon, also move its colour toward
   `Palette.MoonColor` after twilight. Moving only the direction leaves the
   terrain illuminated by the sunset colour under a night sky.
7. Recapture the same source-locked day and night views. Judge line hierarchy
   separately from geometry, material breakup, and camera registration.

## Checks

### Mechanical

```bash
dotnet build --no-restore
./tools/world-authoring.sh audit
```

On Hyprland, generate the fixed evidence only through the required silent
workspace-5 launcher:

```bash
env -u LD_LIBRARY_PATH hyprctl dispatch \
  'hl.dsp.exec_cmd("env -u LD_LIBRARY_PATH PATH=/run/current-system/sw/bin:/usr/bin:/bin XDG_DATA_HOME=/tmp/petalfell-ink-parity /home/shikhar/godot/petalfell/tools/world-authoring.sh capture-site bloom-grove-court res://../shots/reference-10-ink-parity reference_match_day,reference_match_night", { workspace = "5 silent" })'
```

Confirm fresh file timestamps and process exit. A successful dispatch is not a
successful capture.

### Visual

- Compare `reference_match_day` directly with the source before looking at the
  overlay. Inspect outer silhouettes, internal stair rhythm, tree crowns, and
  whether distant terrain becomes a tangle of equal-weight lines.
- Inspect `reference_match_night` for readable surface turns without bright
  contour wireframes, crushed terrain, or loss of the player silhouette.
- Use the same camera, resolution, and time samples for before/after comparisons.
  A colour metric across different geometry is diagnostic only.
- After acceptance at the locked view, inspect play/far distances and all four
  rotations. Those were not recaptured for the present evidence.

## Scope and limits

The implementation is shared, but current visual evidence covers only the
Reference 10 locked camera at time 0.41 and 0.80. Reference 1 is not yet built,
no supplied night reference exists, and the current material/weathering and
site geometry remain visibly different from Reference 10. `plan.md` section
15.3 still names a much thicker approximately 3.2-width starting point, while
the current visually reviewed implementation uses 1.05 px; this pass does not
settle that author-owned contradiction.

## Known failures

- Lowering line alpha to make ink soft causes transparent-pass overlaps and
  endpoints to accumulate. Keep the analytic body opaque and soften by family
  colour and classification.
- Giving every edge the lifted internal colour erases the silhouette at night.
  Only camera-facing internal turns receive the lift.
- Scaling pale ink at night while leaving dark ink constant makes most masonry
  change but leaves structural/concave runs frozen through the day.
- Moving the night key to the moon direction while retaining the dusk sun colour
  produces brown-magenta ground under a blue sky.
- A softer line cannot repair incorrect geometry or absent weathering. Judge and
  track those categories separately.

## Update triggers

Update after any ink colour, width, internal classification, night-scale,
day-cycle key colour, shadow opacity/blur, grade saturation/contrast, fixed
capture time/camera, compositor delivery, or explicit author decision. Replace
the visual evidence after reviewing Reference 1, other biomes, far distances,
four rotations, and live clock transitions.
