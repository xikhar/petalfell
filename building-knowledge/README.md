# Building knowledge

This folder is Petalfell's durable, evidence-labelled handbook for **how to
reconstruct supplied reference sites**. It records methods that worked, the
conditions under which they worked, and failures that must not be rediscovered.

It does not decide what to build. Design authority remains in
[`docs/RUINS.md`](../docs/RUINS.md), map/data ownership remains in
[`docs/MAP_PIPELINE.md`](../docs/MAP_PIPELINE.md), settled sequencing remains in
[`docs/ROADMAP.md`](../docs/ROADMAP.md), and implementation truth remains in the
current source and generated review artifacts. An entry here is reusable
operational knowledge, not permission to replace a measured reference with a
recipe.

## Mandatory workflow

For any reference-site plan, builder, material, or comparison-tool change:

1. Read [`AGENTS.md`](../AGENTS.md), the owning design document, this index, and
   [`CERTAINTY.md`](CERTAINTY.md).
2. Read every entry below that touches the work. Check its scope, evidence date,
   known failures, and supersession links before using its procedure.
3. Inspect the live reference, current authored plan/source, and latest named
   captures. The handbook shortens investigation; it never substitutes for
   current evidence.
4. Implement in the narrowest applicable scope. A Reference 10 technique is not
   automatically a rule for every reference family.
5. Re-run the entry's mechanical and visual checks. Record the actual artifacts
   inspected and the date; do not merely copy an old evidence line.
6. If the work discovers a repeatable method, a stronger check, a scope limit,
   or a failed approach, create or update the relevant entry **in the same work
   session**. A correction from the author changes the method immediately.
7. Report the highest evidence state actually reached. Only an explicit author
   decision can set `author-accepted`.

## Index

| Entry | Use it for | Current scope |
|---|---|---|
| [Evidence and certainty](CERTAINTY.md) | Status vocabulary, evidence rules, promotion and supersession | All building-knowledge entries |
| [Entry template](TEMPLATE.md) | Creating a new technique record | All new entries |
| [Reference measurement and coordinate calibration](workflows/reference-measurement-and-coordinate-calibration.md) | Turning source pixels and a locked camera into one integer voxel frame | Reference 10 proven; adaptable with fresh calibration |
| [Plan-first voxel transcription](workflows/plan-first-voxel-transcription.md) | Authoring topology before vertical detail and keeping plan/runtime aligned | Reference-site ground plans v2 |
| [Block-by-block structures and square shafts](structures/block-by-block-structures-and-square-shafts.md) | Unique walls, arches, stairs, rubble, and constant-section survivors | Reference 10 proven constraints |
| [Hybrid voxel and fine sculpture geometry](structures/hybrid-voxel-and-fine-sculpture-geometry.md) | Cleaning author-supplied GLBs, replacing baked materials, adding silhouette ink and placing them over site-owned voxel foundations | Reference 12 Meshy integration visually reviewed; broader use remains candidate |
| [Production atlas relief, hydrology, and wilderness](terrain/production-atlas-relief-hydrology-and-wilderness.md) | Deterministic sector relief, land-aware elevation, registered banks, stepped-water closure, wilderness ownership, and seam checks | Compiler 27 terrain/hydrology mechanically verified across 192 sectors and 356 seams; representative current captures inspected, but author acceptance and atlas-wide wilderness proof remain open |
| [Production map-guided terrain runtime](terrain/production-map-guided-terrain-runtime.md) | Fast bounded production terrain using accepted macro maps and the proven low-level terrain/water grammar | Generic status-gated site overlay mechanically verified for promoted Bloom and unpromoted Shallows; river and Bloom integration inspected; author acceptance open |
| [Terrain and detached slab integration](terrain/terrain-and-detached-slab-integration.md) | Making site ground part of the terrain without rings or a stamped pad | References 10 and 12 visually reviewed applications |
| [Material and weathering breakup](surfaces/material-and-weathering-breakup.md) | Macro colour placement plus existing fine stone weathering | Existing voxel material path; References 10 and 12 applications |
| [Capture, overlay, and acceptance](rendering/capture-overlay-and-acceptance.md) | Fixed isometric/top comparisons and multi-scale/rotation review | Current reference-site review rig |
| [Time-responsive ink and high-key lighting](rendering/time-responsive-ink-and-high-key-lighting.md) | Rejected internal-softness/night-ink experiment and its historical evidence | Superseded; do not reapply |
| [Bloom Grove Court evidence ledger](sites/bloom-grove-court.md) | Site-specific constraints, rejected readings, and current evidence gaps | Reference 10 only |
| [Shallows Gate and Causeway evidence ledger](sites/shallows-gate-and-causeway.md) | Reference 1 source calibration, measured plan hierarchy, placement hypothesis, and rejected fixture readings | Reference 1 only; source audit before implementation |
| [Fallen Colossus evidence ledger](sites/fallen-colossus.md) | Reference 12 1.5× leg scale, Meshy normalization, broad worn slab precinct, current evidence and rejected cuboid/2× readings | Reference 12 only; current build unaccepted |

Put new reusable methods under `workflows/`, `terrain/`, `structures/`,
`surfaces/`, or `rendering/`. Put per-site evidence and correction history under
`sites/`. A site ledger links to generalized methods and records only its own
calibration, constraints, failures and evidence; it must not copy the method or
its live JSON coordinates. Add every new entry to this index.

## When to create, update, or supersede

Create an entry when a technique is likely to be reused and has at least one
named observation or mechanical result. Use [`TEMPLATE.md`](TEMPLATE.md). Do not
create a confident recipe from an untested idea; record it as `candidate` and
state the test that is missing.

Update an entry immediately when any of these happens:

- the author corrects the interpretation or rejects the result;
- a new capture exposes a view, distance, lighting, or topology failure;
- an audit starts enforcing a previously manual invariant;
- a method works only for a narrower or broader scope than recorded;
- an evidence artifact, command, or owning implementation path changes;
- a later technique makes part of the entry obsolete.

Supersede rather than silently rewrite a materially different method. Mark the
old entry `rejected/superseded`, link its `Superseded by` field to the new entry,
and link back from the replacement's `Supersedes` field. Keep the old failure
record because it is exactly what prevents repetition. Small refinements that
preserve the method belong in the existing entry and its evidence table.

## Required shape of every technique entry

Every entry must contain, near the top:

- **Lifecycle:** `active` or `superseded`;
- **Evidence summary** using only evidence states defined in
  [`CERTAINTY.md`](CERTAINTY.md), followed by claim-specific evidence rows;
- **Scope** and explicit non-scope;
- **Last verified** date;
- **Evidence** with named sources, commands, and artifacts;
- **Supersedes** and **Superseded by** links, using `none` when applicable;
- **Procedure** precise enough to repeat;
- **Checks** that distinguish mechanical from visual evidence;
- **Known failures** and what replaced them;
- **Update triggers** specific to that technique.

An active entry may carry several evidence states for different claims. State
those claims separately. Never compress `observed`, `mechanically verified`,
`visually reviewed`, and `author-accepted` into one vague word such as
"validated."
