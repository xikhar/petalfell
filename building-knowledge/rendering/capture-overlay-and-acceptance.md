# Capture, overlay, and acceptance

- **Lifecycle:** `active`
- **Evidence summary:** capture generation is `mechanically verified`; the full
  v13 Reference 10 matrix is historical `visually reviewed` evidence for its
  claim-scoped geometry; the complete current v17 matrix is `visually reviewed`
  only for Bloom's four annotated geometry corrections; the v2 locked day/night
  pair remains `visually reviewed` for line hierarchy and night readability
  only and is now historical because the author rejected that outline system;
  Reference 1's failed one-sector framing and corrected
  two-sector-per-axis framing are `visually reviewed`; no reconstruction is
  recorded here as `author-accepted`
- **Scope:** `tool-specific` to the current reference-site review rig; Reference
  10 camera numbers are `site-specific`
- **Last verified:** 2026-08-30
- **Supersedes:** single flattering screenshots and comparisons from the wrong
  isometric quadrant
- **Superseded by:** none
- **Owning sources:**
  [`AtlasSectorReview.cs`](../../src/Tools/AtlasSectorReview.cs),
  [`world-authoring.sh`](../../tools/world-authoring.sh),
  [`bloom-grove-court-reference-10.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10.json),
  [`shallows-gate-and-causeway-reference-1.json`](../../content/chapter_01/sites/shallows-gate-and-causeway-reference-1.json)

## Outcome

Every meaningful site revision is reviewed in one locked source-matching
isometric view, one calibrated overhead view when available, ordinary day and
night lighting, and four distances at all four cardinal rotations. Derived 50%
overlays and edge-difference images expose drift; they support human comparison
and never grant acceptance.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| A full Reference 10 capture emits 19 raw shots: locked day/night, calibrated top day, and 4 distances x 4 rotations, plus four derived comparison images | `mechanically verified` | tool-specific | `AtlasSectorReview.ReviewShots` and `WriteReferenceComparisons`; complete file list in `/home/shikhar/godot/shots/reference-10-plan-v13-full/` | File production alone does not prove anyone inspected them |
| The locked camera uses source resolution 1672x941, yaw 135, pitch 35.26439, and current distance 190 | `mechanically verified` | site-specific/tool-specific | site `referenceView`; capture viewport construction | These parameters can become stale if the source calibration changes |
| v11 was inspected at the locked angle, overhead, all distances, all rotations, and day/night | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-10-plan-v11-full/`, 2026-08-30 | Later slab edits are not covered; geometry/material match remains open |
| The corrected central occupied footprint, restored slab boundary, open channels, supported reverse faces, modest far extent, and unchanged square shafts were inspected across v13 | `visually reviewed` | site-specific | Historical pre-lighting-change evidence: all 19 raw captures and four derived comparisons in `/home/shikhar/godot/shots/reference-10-plan-v13-full/`, 2026-08-30 | These claims do not establish the current render, whole-site fidelity, collision/playability, or author acceptance |
| The rejected internal-softness renderer was inspected at locked day/night samples before the author rejected its outline result | `visually reviewed` | historical/site-specific | `/home/shikhar/godot/shots/reference-10-ink-parity-v2/reference_match_day.png` and `reference_match_night.png`, 2026-08-30 | Historical failure evidence only; it does not describe the restored legacy ink now active |
| The current Bloom v17 capture set contains all 19 raw shots and all four derived comparisons | `mechanically verified` | tool-specific | Complete file set in `/home/shikhar/godot/shots/reference-10-callouts-current-v17/`, 2026-08-30: locked day/night, calibrated top, close/play/wide/far at r0-r3, source overlay/edge difference, and top overlay/edge difference | Completeness alone does not prove inspection or visual correctness |
| Across the complete current Bloom v17 matrix, the stair-side bump family stays absent, the south-west passage stays open, the four 2x2 shafts visibly originate from their connected foundations, and the southern threshold reads as two rises | `visually reviewed` | site-specific | All 19 raw shots and four derived comparisons in `/home/shikhar/godot/shots/reference-10-callouts-current-v17/`, inspected 2026-08-30 at locked day/night, calibrated top, close/play/wide/far, and r0-r3 | Only these four annotated geometry claims were promoted; whole-site parity, playability, and author acceptance remain unproven |
| A site whose footprint fits one sector still receives a real 2x2 sector context for the fixed 14-chunk capture circle | `mechanically verified` | tool-specific | `SiteSectorBounds`; `dotnet build --no-restore`, 2026-08-30 | This code invariant alone does not prove the registered subject is in frame |
| One-sector framing clamped Reference 1 away from the site, while the 2x2 context put its player, causeway and gate into the locked and overhead frames | `visually reviewed` | tool-specific | Empty registered frames in `/home/shikhar/godot/shots/reference-1-blockout-v1/` compared with `reference_match_day.png` and `reference_top_day.png` in `/home/shikhar/godot/shots/reference-1-blockout-v2/`, 2026-08-30 | Correct framing exposes the blockout but does not establish its geometry or visual fidelity |

## Procedure

1. Finish the mechanical plan audit and build before launching a visual review.
2. Ensure the registered focus remains inside the capture stream margin. Even if
   the footprint fits one sector, compose at least two real sectors per axis for
   the fixed site capture circle; a clamped focus can produce a valid but empty
   screenshot. Capture the locked isometric and top view first. If large topology
   is wrong, return to the plan before spending time on the full matrix.
3. Use the source image's exact resolution and locked quadrant. Reference 10's
   current lock is yaw 135 degrees, pitch 35.26439 degrees, distance 190.
4. Generate the full matrix after topology survives the first comparison:
   close 62, play 96, wide 154, and far 240; each at r0/r1/r2/r3 in 90-degree
   increments. Include locked day/night and top day.
5. Inspect raw source and render side by side before the 50% overlay. Then use
   the overlay for alignment and edge difference for silhouette diagnostics.
6. Record every inspected artifact and separate findings by geometry, material,
   light, scale, and hidden-face completeness. Do not use colour mismatch to
   move geometry until edge/top evidence agrees.
7. After any geometry, material, camera, shader, or lighting change, recapture
   every view affected by that change. Old captures become historical evidence.
8. Record `author-accepted` only after the author explicitly accepts a named
   revision/artifact set and scope. A continued instruction or positive comment
   about one detail is not whole-site acceptance.

## Checks

### Mechanical

```bash
./tools/world-authoring.sh capture-site bloom-grove-court \
  res://../shots/reference-10-review
```

On the author's current Hyprland workstation, every Godot GUI launch must be
silent on workspace 5. Use the approved launcher shape rather than opening a
window on the active workspace:

```bash
env -u LD_LIBRARY_PATH hyprctl dispatch \
  'hl.dsp.exec_cmd("env -u LD_LIBRARY_PATH PATH=/run/current-system/sw/bin:/usr/bin:/bin XDG_DATA_HOME=/tmp/petalfell-capture /home/shikhar/godot/petalfell/tools/world-authoring.sh capture-site bloom-grove-court res://../shots/reference-10-review", { workspace = "5 silent" })'
```

The explicit `PATH` is required because a compositor-launched command does not
necessarily inherit the interactive shell path that contains `godot-mono`.
Confirm the process exits and that the expected raw/derived files have fresh
timestamps. The dispatcher returning `ok` proves only that it accepted the
launch request; it does not prove Godot started. This remains mechanical
evidence only.

### Visual

- `reference_match_day`: source-side relationships, outer silhouette, primary
  stair, arch/opening, counts, player-relative scale, and lighting direction.
- `reference_overlay_50` and edge difference: calibration and corresponding
  edges, not beauty or acceptance thresholds.
- `reference_top_day` and top overlay: footprint, empty channels, stair
  direction, tree anchors, and unintended bridges.
- close/play r0-r3: block thickness, shaft section, damage, paving, collision
  scale, and hidden backs.
- wide/far r0-r3: hierarchy, rhythm, density, terrain integration, and distant
  readability.
- locked night: shadow modelling and material separation; day and night must use
  the ordinary `DayCycle`, not a presentation-only rig.

## Scope and limits

Colour RMSE and mean edge delta have no acceptance threshold. They are useful
only when calibration and compared content are stable. A top view cannot prove
vertical silhouette; the locked view cannot expose every hidden face; four
rotations do not prove playability or collision unless the playable runtime is
tested separately. `--review-site` capture mode is nonplayable.

## Known failures

- One hero screenshot concealed hollow or malformed reverse geometry. The full
  four-rotation matrix is mandatory.
- A correct-looking different isometric angle reversed source ordering. Only the
  locked source quadrant is the acceptance comparison.
- Captures were previously described as accepted merely because they existed.
  Generation, inspection, and author acceptance are now separate evidence states.
- A workspace dispatcher returned `ok` while producing no process or files when
  its environment could not resolve `godot-mono`. Set the explicit system
  `PATH` in the dispatched command and verify fresh output files before waiting
  for or inspecting a capture.
- Late-night samples collapsed pale materials into navy silhouettes. Reference
  10 currently uses day-cycle time 0.80 for the locked night comparison; any
  change needs renewed visual evidence.
- Reference 1 initially emitted a complete, apparently valid matrix of water and
  distant terrain because its one-sector mosaic put the authored origin outside
  the 14-chunk meshable focus band. Do not move the calibrated camera to hide this
  symptom. Add real neighbouring sectors, recapture, and verify that the player
  and source landmarks occupy the registered pixels.

## Update triggers

Update for shot names/counts, distances, rotations, viewport resolution, camera
focus/yaw/pitch, day/night samples, overlay/difference algorithms, review-mode
playability, workspace-launch requirements, or acceptance criteria. Replace all
affected visual evidence rows after a new capture revision.
