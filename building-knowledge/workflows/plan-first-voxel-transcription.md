# Plan-first voxel transcription

- **Lifecycle:** `active`
- **Evidence summary:** `mechanically verified` and `visually reviewed`; source
  interpretation remains not `author-accepted`
- **Scope:** `tool-specific` to the version-2 reference-site ground plan and
  `site-specific` to its current Reference 10 use
- **Last verified:** 2026-08-30 against the strict plan audit and current v13
  source/top artifacts
- **Supersedes:** ad-hoc structure coordinates without one reviewable ground plan
- **Superseded by:** none
- **Owning sources:**
  [`ReferenceSiteGroundPlan.cs`](../../src/World/ReferenceSiteGroundPlan.cs),
  [`reference-site-plan.py`](../../tools/reference-site-plan.py),
  [`bloom-grove-court-reference-10-plan.json`](../../content/chapter_01/sites/bloom-grove-court-reference-10-plan.json),
  [`Reference10GroveCourt.cs`](../../src/World/Sites/Reference10GroveCourt.cs)

## Outcome

A human-reviewable 2D plan owns every horizontal cell before vertical detail is
built. Terrain levels, stairs, structure projections, exact rubble cells,
surface patches, and tree anchors share the same coordinate frame. The
site-specific builder owns vertical courses, openings, and damage, but it may
not invent a second horizontal layout.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| The plan parser rejects missing/duplicate IDs, invalid shapes, unsupported structures, disconnected connected-runs, overlapping patches/rubble, and stairs that miss their levels | `mechanically verified` | tool-specific | `ReferenceSiteGroundPlan.Audit`; Python preview audit; passing `preview-site-plan` and world audit on 2026-08-30 | Audit checks topology, not resemblance to the image |
| Runtime structures cannot write outside or leave holes in their declared projection | `mechanically verified` | tool-specific | `Reference10GroveCourt.WriteNamed` and `Put` projection guards; successful build/capture of v11 | A fully occupied projection may still have the wrong silhouette above it |
| Separating horizontal plan from vertical transcription made the Reference 10 topology inspectable and correctable from overhead | `visually reviewed` | site-specific | Current source/runtime plan SVGs and `/home/shikhar/godot/shots/reference-10-plan-v13/reference_top_day.png`, top overlay, and top edge difference inspected 2026-08-30 | Locked/top review does not cover reverse faces, all distances, night, playability, or acceptance |

## Procedure

1. Trace the surrounding terrain/exclusion boundary and named platform/court
   regions first. Give each a level, material role, and explicit write mode.
2. Record the level graph: which terrain each stair leaves and reaches, landing
   rectangles, cardinal axis, and every tread top. A stair is connectivity, not
   decoration.
3. Trace structure projections as named, source-derived masses. Split separate
   components instead of bridging a visible terrain channel for convenience.
4. Record rubble as exact occupied cells inside a review envelope; do not let an
   envelope become permission to scatter randomly.
5. Record surface wear as exact, non-overlapping cell groups owned by visible
   authored terrain. Keep macro patches in the plan, not a runtime hash.
6. Record surrounding tree anchors only after architecture and terrain
   exclusions are stable.
7. Preview the plan source-facing. Compare its axis, boundaries, levels, stairs,
   openings, and empty channels with the overhead evidence.
8. Implement each named projection in the site-specific builder. Use range
   fills only as compact serialization of measured blocks.
9. Build in this order: authored terrain and stairs, authored surface wear,
   architecture/rubble, then surrounding vegetation. The voxel grid cannot
   safely replace a terrain column after sparse architectural edits touch it.
10. Re-run the plan audit after every horizontal edit and recapture top and
    locked-isometric views after every meaningful topology edit.

## Checks

### Mechanical

```bash
./tools/world-authoring.sh preview-site-plan bloom-grove-court /tmp/bloom-grove-source.svg
./tools/world-authoring.sh preview-site-plan bloom-grove-court /tmp/bloom-grove-runtime.svg --runtime-facing
./tools/world-authoring.sh audit
dotnet build --no-restore
git diff --check
```

Inspect audit output, not only exit status. These checks prove data integrity,
projection ownership, and compilation only.

### Visual

Rasterize and inspect the source-facing SVG before starting Godot. Confirm that
separate source masses remain separate and all stairs meet visible levels. Then
inspect the calibrated top capture for footprint drift and the locked isometric
capture for vertical/occlusion consequences.

## Scope and limits

The schema can prevent many impossible plans; it cannot decide whether a traced
polygon, mass, damage pattern, or tree anchor is faithful. Hidden back geometry
still requires conservative source reasoning. Do not copy Reference 10's plan
records into another site—the reusable knowledge is the ownership split and
verification loop.

## Known failures

- Direct C# coordinate edits without an overhead plan made random pillar
  placement and unsupported masses difficult to see. The plan is now canonical
  for X/Z.
- Broad footprint rectangles encouraged builders to fill empty source spaces.
  Use separate components and exact cells where the source separates them.
- A plan that passed audit was previously described too confidently. Mechanical
  validity is not a visual match; the states are now recorded separately.

## Update triggers

Update this entry when the plan schema, visible-owner/painter semantics, stair
rules, structure projection guards, surface patch rules, rubble representation,
write order, or preview commands change. Record any new audit invariant as
mechanical evidence only until captures are inspected.
