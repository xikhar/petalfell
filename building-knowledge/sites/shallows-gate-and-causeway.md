# Shallows Gate and Causeway (Reference 1) evidence ledger

- **Lifecycle:** `active`
- **Evidence summary:** source identity, the source-facing measurement audit,
  the strict L3 ground-plan audit, complete measurement coverage, keep-open
  checks, and source-tree/exclusion separation are `mechanically verified`;
  horizontal registration, integer bounds, the unmirrored runtime transform,
  locked-camera residuals, current atlas water/bed datum, and the canonical
  topology registration are `mechanically verified`; the inclusive authored
  context, three explicit water painters, four substantial source-traced shelf
  and shoreline continuations,
  and source-audit ownership counts are `mechanically verified`; hierarchy is
  `observed/source-measured`; the named measurement, exact L3-cell,
  locked-camera, vertical-course overlays, and v2 3D capture matrix have been
  `visually reviewed`; the v2 matrix proves registration but also rejects
  preserve-atlas site context and the later 3x3 pedestal correction. The full v4
  locked/top context set has also been `visually reviewed`: it preserves the
  broad registration, but its pale/maroon dry-ground quilt, broad rectangular
  shoreline treatment, sealed gate passage, blanket front masses, stepped roof
  cap, and tower-like buttress strips are rejected evidence, not proof for the
  current builder. The v5 top/locked set confirms that the named dry styles no
  longer produce that quilt, but rejects its painter-erased east lower court,
  broad elevated east slab, and six-course southern pit. The v6 locked/top set
  verifies that the east foreground court now survives at bridge level, the
  exact stair climbs to a smaller north shelf, and the southern stair exits onto
  connected land. It also rejects the current gate as too solid/coarse, the
  flanking ribs as too tall/heavy, and the outer shore/cliff silhouette as a
  blockout rather than a source trace. Gate courses, hidden support bases,
  precinct survivor heights, and the broad outer context remain `candidate`;
  nothing is `author-accepted`
- **Scope:** `site-specific` to the next `reference-1.png` gate-and-causeway
  reconstruction
- **Last verified:** 2026-08-30 by source inspection, pixel comparison,
  source-measurement audit, strict generic and Reference 1 plan audits, exact
  cell-overlay inspection, locked-camera/course overlay inspection, v2 matrix
  inspection, v4 and v5 top/context inspection, atlas sampling, current
  topology/world audit, current Python plan/camera audits, the current successful
  `dotnet build --no-restore`, and original-size v6 locked/top comparison
- **Supersedes:** none; rejected generic portal and southern tooling-fixture
  readings are retained below
- **Superseded by:** none
- **Owning sources:**
  [`reference-1.png`](../../world-new/reference-1.png),
  [`reference-1-top.png`](../../world-new/reference-1-top.png),
  [`shallows-gate-and-causeway-reference-1-measurement.json`](../../content/chapter_01/sites/shallows-gate-and-causeway-reference-1-measurement.json),
  [`shallows-gate-and-causeway-reference-1-plan.json`](../../content/chapter_01/sites/shallows-gate-and-causeway-reference-1-plan.json),
  [`shallows-gate-and-causeway-reference-1-camera.json`](../../content/chapter_01/sites/shallows-gate-and-causeway-reference-1-camera.json),
  [`shallows-gate-and-causeway-reference-1-vertical.json`](../../content/chapter_01/sites/shallows-gate-and-causeway-reference-1-vertical.json),
  [`shallows-gate-and-causeway-reference-1.json`](../../content/chapter_01/sites/shallows-gate-and-causeway-reference-1.json),
  [`reference-1-plan-overlay.py`](../../tools/reference-1-plan-overlay.py),
  [`reference-1-isometric-calibration.py`](../../tools/reference-1-isometric-calibration.py),
  [`topology.json`](../../content/chapter_01/topology.json), and
  [`shallows-gateway-domain.json`](../../content/chapter_01/domains/shallows-gateway-domain.json)

## Authority and coordinate frame

`reference-1.png` is the primary source. It owns the locked isometric
silhouette, player-relative scale, vertical hierarchy, openings, visible
damage, materials, light, and vegetation silhouettes. The tracked 1254x1254
`reference-1-top.png` is supporting plan evidence: it owns visible X/Z
relationships but cannot settle height, hidden backs, underside construction,
or final acceptance.

Use a source-facing integer plan with one cell per voxel. Put `(0,0)` at the
gate/bridge threshold on the processional centreline, use `+X` toward image
right/east across the bridge, and `+Z` toward image bottom/south and the
foreground landing. Do not introduce a mirror or fractional scale merely to
improve one landmark. The solved runtime transform is orientation `0` with
`runtimeMirrorX: false`: source `+X` remains runtime `+X`, and source `+Z`
remains runtime `+Z`.

The current source registration is `u = 665 + 12.5x`,
`v = 456 + 12.5z`. It belongs only to the supporting top image. The measurement
JSON deliberately remains a measurement-stage source record and says
`runtimeTransformStatus: unresolved`; the strict version-2 plan now records the
separately solved runtime transform and links the camera/course evidence. The
measurement layer does not retroactively own atlas rotation or height.

The locked v1 review rig uses the ordinary perspective camera at 1672x941,
vertical FOV 21 degrees, yaw 45 degrees, pitch 35.26439 degrees, distance 145,
and local focus `(4,5,10)`. At the focus it projects one source cell as
`X=(+12.429,+7.176)` pixels, `Z=(-12.429,+7.176)` pixels, and one vertical
course as `(0,-14.352)` pixels. Its seven current source correspondences have
17.45 pixel RMS and 32.00 pixel maximum residual. The residual is retained
because the supplied overhead and isometric sources differ locally; it is not
hidden by a fractional plan scale.

The absolute integer datum is `absolute TopY = 126 + local Y`: threshold and
causeway deck `0/126`, south landing and candidate connected approach `-6/120`,
current water surface `-21/105`, and sampled bed `-23/103`. The east lower court
is candidate `0/126` south of the side-stair top (`Z >= -7`); the narrowed east
upper plateau is candidate `+8/134` north of it (`Z <= -8`) with two-course
uncertainty. Gate
passage `0..14`, attached buttress `0..18`, connected body `0..22`, and roof
parapet `22..24` are first-blockout candidates, not accepted dimensions.

## Evidence

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| The supplied isometric attachment is exactly the tracked `world-new/reference-1.png` source at 1672x941 | `mechanically verified` | site-specific | `compare -metric RMSE attachment/image-1.png world-new/reference-1.png null:` returned `0 (0)` on 2026-08-30; both images identify as 1672x941 | Pixel identity does not prove any geometric interpretation |
| The tracked overhead source is byte-identical to the supplied 1254x1254 attachment | `mechanically verified` | site-specific | Both files SHA-256 `2c2a32995758cf1c8a2e6a0f3e60b73566f382c570c023c68414eab8ce818534`; the overlay audit checks the hash and PNG dimensions on 2026-08-30 | Identity does not prove geometric interpretation |
| The overhead registration is one voxel per 12.5 source pixels with origin `(u=665,v=456)`, `+X` right and `+Z` down; all eight dispersed landmarks pass their declared residual limits and the maximum observed residual is 3.61 pixels at the traveller | `mechanically verified` | tool-specific | `python3 tools/reference-1-plan-overlay.py /home/shikhar/godot/shots/reference-1-plan-overlay.svg`; threshold, traveller, both south deck corners, both stair-toe corners, west remote survivor, and east remote stair in the measurement JSON | A passing registration audit cannot resolve height parallax or the runtime transform |
| The registered source-facing overlay keeps the gate/passage, bridge edges, southern stair, west/east precinct hierarchy, east remote stair, player, and surrounding terrain in their corresponding source locations | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-1-plan-composite.png` inspected at 1254x1254 on 2026-08-30; transparent source is `reference-1-plan-overlay.svg` | Precinct rectangles remain measured envelopes rather than final occupied cells; no 3D or locked-isometric evidence exists |
| The strict L3 source plan is structurally valid and covers every measured source group: 16 terrain shapes, 5 surface patches, 45 unique site structures (20 connected thin-wall runs, 5 connected facade masses, 7 isolated survivors, 9 exact rubble clusters, 2 stairs, and 2 side-support groups), 3 exact keep-open regions, 9 tree exclusions, and 20 source tree anchors | `mechanically verified` | site-specific | `python3 tools/reference-site-plan.py content/chapter_01/sites/shallows-gate-and-causeway-reference-1-plan.json --audit-only` and `python3 tools/reference-1-plan-overlay.py --l3-plan content/chapter_01/sites/shallows-gate-and-causeway-reference-1-plan.json --audit-only`; 1,129 unique structure-projection cells, 6,393 final dry terrain cells, and complete mapping of 10 terrain regions plus 27 components on 2026-08-30 | Counts prove authored topology and evidence linkage, not whether provisional heights reconstruct the isometric source |
| The measured 96x91 context is inclusively authored as exactly 8,736 final cells: 6,393 dry and 2,343 water; dry terrain is one connected component and water is four components of 1,438, 770, 132, and 3 cells. All 20 measured tree anchors resolve to dry terrain, and 87 exact stair cells override their broad terrain owner | `mechanically verified` | site-specific | Current plan painter audit using `tools/reference-site-plan.py` cell rasterisation on 2026-08-30; source measurement owns the irregular west outer shelf, east middle shelf, connected south/east shoreline, far-east shelf, narrowed east levels, and connected south approach rather than tree-base rectangles | The registered top source owns the visible outlines, but their hidden vertical transitions and current runtime appearance remain uncaptured; the retained three-cell east-water fragment belongs to the later broad shoreline/connectivity retrace, outside this P1 |
| The west basin now includes local `x=-10,z=15..36` and touches final under-causeway water at `x=-9` for the complete run without changing any of the 555 causeway-deck cells | `mechanically verified` | site-specific | Plan raster/component audit and `AssertMeasuredContextPlan`; the former isolated six-cell east shoreline puddle was closed by the source-traced south/east continuation | Three substantial final water components remain separated at plan level by the dry causeway and traced shelf; a retained three-cell east-water fragment and the broad shoreline/connectivity retrace remain open outside this P1 |
| The exact L3-cell overlay registers the gate roof/body and open passage, 15-cell causeway and 13-cell clear deck, both stairs, west/east occupied ranges and survivors, irregular water/cliff context, exclusions, and all 20 tree anchors over their corresponding top-source evidence | `visually reviewed` | site-specific | `/home/shikhar/godot/shots/reference-1-l3-plan-composite.png` inspected at 1254x1254 on 2026-08-30; transparent source is `reference-1-l3-plan-overlay.svg` | The overhead image cannot establish courses, hidden backs, cliff faces, bridge underside, or the runtime transform; individual damage cells may narrow after the locked isometric solve |
| Primary masonry occupies `X=-30..34`, `Z=-20..43`; broader visible terrain/tree evidence occupies `X=-48..47`, `Z=-28..62` | `observed/source-measured` | site-specific | Registered top overlay and `coordinateContract` bounds | Crop-edge scenery is not permission to author all terrain in the larger box |
| The continuous causeway slab is 15 cells outside-to-outside with a 13-cell clear axis and 37-cell level run; local abutments widen it to 19 cells; the centred southern stair occupies 6x7 cells for six rises, and threshold-to-lower-landing travel is 43 cells | `observed/source-measured` | site-specific | Registered deck `[-7,0]..[7,36]`, clear axis `[-6,0]..[6,36]`, abutment `[-9,34]..[9,37]`, and stair `[-3,36]..[2,42]` in the measurement overlay | Individual parapet breaks, stair block damage, and final Y values still require transcription |
| The gatehouse ground envelope is 23x18 cells, its elevated roof projection is 25x13, and its visible central passage is 7x9; west/east front masses and the rear spanning mass remain separately named | `observed/source-measured` for the visible bounds; `candidate` for the hidden continuation | site-specific | Registered gate ground `[-11,-18]..[11,-1]`, roof `[-12,-20]..[12,-8]`, observed portal `[-3,-8]..[3,0]`, and separately labelled minimum hidden continuation `[-3,-18]..[3,-9]` | The primary isometric shows daylight through the gate, but the overhead source cannot measure the hidden continuation's exact depth; roof projection also contains height parallax |
| The dense west precinct is a 20x27 component envelope and the cleaner east precinct plus remote side stair is a 23x39 envelope; four west and four east subcomponent envelopes prevent either side becoming one filled slab | `observed/source-measured` | site-specific | Named `west-*` and `east-*` component bounds in the measurement JSON and inspected top composite | Each envelope still needs its internal occupied wall/rib/rubble cells traced from both sources before 3D writing |
| The gate is a roofed megalithic mass with four heavy corner/buttress groups, long lintel/roof courses, and a recessed central passage; the bridge is a massive causeway/abutment rather than a thin suspended span | `observed/source-measured` | site-specific | `reference-1.png` and overhead source, including visible roof, front passage, cliff faces and bridge side wall | Hidden roof interior, passage back, bridge underside and submerged supports are not shown completely |
| The absolute blockout datum is threshold/deck local `0`, absolute TopY `126`; current water is `-21/105`, sampled bed `-23/103`, and the six-rise south landing `-6/120` | `mechanically verified` for current atlas sample and integer mapping; `observed/source-measured` for the south stair | site-specific | Vertical schedule audit; current atlas samples at global `(6400,6980)` and `(6400,7022)` both report bed 103 / water 105; visible south stair has six rises | Threshold 126 is the authored site registration, not a claim that ordinary terrain chose that elevation |
| The first silhouette schedule uses passage `0..14`, attached buttress `0..18`, gate body `0..22`, roof parapet `22..24`, east lower court `0`, and the narrowed north east plateau `+8` | `candidate` | site-specific | Counted-course diagram `/home/shikhar/godot/shots/reference-1-camera-vertical-locked-v1-composite.png` inspected on 2026-08-30; the north plateau keeps a two-course range and every structure retains `verticalStatus: provisional` | Unique authored geometry and a source-camera capture may narrow these values; hidden roof/support courses remain unknown |
| The current P1 terrain makes the east lower court survive painter order at local `Y=0`, `Z=-7..18` (592 authored / 546 effective cells), restricts the local `Y=8` upper plateau to `Z=-20..-8` (322 authored / 283 effective cells), and preserves nine five-cell treads with tops `0..8`. Nine ribs/survivors/rubble groups are assigned to lower support and the three north room/rubble groups remain upper. The local `Y=-6` south approach is one 440-cell irregular polygon spanning `X=-21..9`, `Z=43..61`; unrelated east shoreline cells remain local `Y=0` | `mechanically verified` for plan topology and painter ownership; `visually reviewed` for the named level/exit correction; still `candidate` overall | site-specific | Current measurement, vertical, camera, and strict L3 plan data; both Python plan audits and `dotnet build --no-restore` pass on 2026-08-30. `/home/shikhar/godot/shots/reference-1-v6-context/reference_match_day.png`, `reference_top_day.png`, and both 50-percent overlays were inspected at original size | The level split and connected exit read correctly, but source-hidden east support courses, the final south-approach outline, and the broad shoreline retrace remain open |
| The locked review rig is perspective FOV 21, yaw 45, pitch 35.26439, distance 145, focus `(4,5,10)`, orientation 0 and no reflection; 7 current landmarks pass with RMS 17.45 px and maximum 32.00 px | `mechanically verified` for the recorded projection and residuals; `visually reviewed` for the unchanged source alignment | tool-specific | `python3 tools/reference-1-isometric-calibration.py --audit-only`; `/home/shikhar/godot/shots/reference-1-isometric-calibration-composite.png` inspected by the root agent and the course-annotated composite inspected on 2026-08-30 | The stale broad-shelf south landmark was removed rather than forcing a false local `Y=8` claim; this is a reproducible comparison rig, not knowledge of the source renderer's unpublished camera |
| The v2 3D matrix keeps the gate, causeway, south stair and player in strong top/locked registration, but its surrounding atlas land/water is visibly unrelated to the source and the first deep faces were too dark | `visually reviewed` | site-specific | All locked day/night, top, and close/play/wide/far r0-r3 images in `/home/shikhar/godot/shots/reference-1-blockout-v2/`, plus `reference_top_overlay_50-analysis.png` and `reference_top_edge_difference-analysis.png`, inspected at original size on 2026-08-30 | Current post-v2 roof/rib/material and v3 authored-context edits invalidate visual proof for the present builder; no v3 capture exists yet |
| The v4 authored context keeps the monument and major wet/dry regions registered, but its dry ground reads as a huge pale/maroon quilt, the shore remains dominated by long rectangular runs, the south landing forms a severe pit, and the surrounding tree trace is incomplete | `visually reviewed` for the named failures | site-specific | `/home/shikhar/godot/shots/reference-1-v4-context/reference_top_day.png`, `reference_top_overlay_50.png`, and `reference_top_edge_difference.png` inspected at original size on 2026-08-30 against `reference-1-top.png` | The v5 capture supersedes only the material-routing failure; terrain levels, shoreline geometry, and tree placement still require later evidence |
| The v5 capture removes the v4 pale/maroon dry-ground quilt, but the later local `Y=8` east painter erased 597 of 679 intended lower-court cells, leaving nearly the whole east precinct on one broad elevated slab. The local `Y=-6` south landing still reads as a closed six-course pit instead of continuing below and around the stair toe | `visually reviewed` for the named failures; `mechanically verified` for the rejected painter overlap | site-specific | `/home/shikhar/godot/shots/reference-1-v5-context/reference_top_day.png`, `reference_top_overlay_50.png`, `reference_match_day.png`, and `reference_overlay_50.png` inspected at original size on 2026-08-30; pre-P1 painter raster counted 597 overwritten lower-court cells | Superseded for these two named failures by the v6 level/approach correction; broad shoreline and tree retracing remain deliberately outside that P1 |
| The v6 capture verifies the level correction but still misses the source hierarchy at monument scale: the gate reads as one coarse solid mass, the front opening is visually pinched, both precincts are dominated by tall repeated ribs, and the enclosing terrain consists of broad rectangular shelves rather than the traced asymmetric gorge and islands | `visually reviewed` for the named successes and failures | site-specific | All six files in `/home/shikhar/godot/shots/reference-1-v6-context/` inspected at original size on 2026-08-30; the locked and top 50-percent overlays make the current/source silhouettes directly comparable | Next work must lower and group side survivors, refine the gate opening/roof/pylon silhouette, and trace the broad source shoreline before fine damage or material parity can be judged |
| The v4 primary gate preserves its registered outer envelope, but the full rear fill seals the source-visible through-passage, the two blanket 8x8 front fills collapse four attached pylons into a monolith, the upper corner masses and stepped cap overstate the roof, and both side strips read as solid towers rather than attached rib groups | `visually reviewed` for the rejection; replacement plan constraints `mechanically verified` | site-specific | All six files in `/home/shikhar/godot/shots/reference-1-v4-context/` inspected at original size on 2026-08-30; current strict audits pass with separate west/east gate bodies and observed/candidate keep-open regions | The revised builder is present in v5/v6 and restores an open passage and simpler roof, but the locked comparison still reads too solid and coarse; its opening, pylon widths, backing and roof damage remain candidates |
| Canonical topology now contains one Reference 1 site `shallows-gate-and-causeway` at `(6400,6980)`, extent `112x136`, orientation 0, with entrances local/global Z `+42/7022` and `-23/6957`; its site definition links the L3 plan and builder `reference-1-gate-and-causeway-v1` | `mechanically verified` | site-specific | Current topology audit reports 2 sites, 6 nodes, 5 routes; `shallows-gate-and-causeway-reference-1.json`, dispatch registration, and camera/site registration match the canonical centre, extent and entrance coordinates | Registration/buildability does not establish visual fidelity, collision, or author acceptance |
| The shallows site envelope does not overlap Bloom Grove Court's current domain | `mechanically verified` | tool-specific | Shallows domain uses sectors 7–9 in both axes; Bloom domain uses sectors 12–13 / 5–6 and centres its site at `(9800,4600)` | Separation does not prove either site's final terrain transition or dynamic mosaic handoff |

## Source hierarchy and transcription constraints

- Keep the central processional axis open. The deck contains only a small number
  of deliberate posts, plinths, and rubble masses; it is not a random pillar
  field.
- Keep the observed `[-3,-8]..[3,0]` portal distinct from the candidate
  `[-3,-18]..[3,-9]` hidden continuation. The latter prevents a source-visible
  daylight path from being sealed; it is not an overhead measurement of rear
  depth.
- Treat most tall elements immediately beside the gate as attached wall ribs or
  buttresses. Do not turn each visible vertical rhythm into a freestanding
  pillar.
- The west flank is the denser broken precinct: parallel wall/colonnade remnants,
  clustered rubble, tall narrow survivors, and a lower shelf. The east flank is
  cleaner and includes the secondary stair, gate buttress, broken wall ribs, and
  a smaller freestanding cluster.
- Model the water cut as an irregular inlet/gorge with a coherent north shelf,
  steep voxel faces, a wider south basin, local notches, and a few detached low
  shelves. Do not excavate a symmetric rectangular trench.
- Preserve the strong tree exclusion over masonry, rubble, cliff walls, and the
  immediate approach. Visible trees are individually anchored loose perimeter
  groups, not a grid or site-internal biome scatter.
- Outside the measured site-terrain boundary, ordinary deterministic atlas
  elevation, hydrology, biome, and vegetation remain authoritative.

## Procedure and next calibration evidence

1. Preserve the audited source registration and its eight dispersed landmarks.
   If any dimension changes, change the registration or component bound at its
   source rather than hiding residual error in a group offset.
2. Preserve the audited production version-2 source plan. Its occupied wall/rib
   cells, openings, stairs, rubble, terrain cuts, tree exclusions and source
   anchors are separate site-owned records. Regenerate the calibrated
   source-facing and runtime-facing overlays after any edit.
3. Preserve the locked camera artifact and its passing landmark limits. Run the
   camera/course audit after every transform or course change; do not tune the
   review camera independently in a builder.
4. Build the first unique site-owned integer silhouette from the L3 cells and
   vertical schedule. Keep candidate courses provisional and diagnose camera or
   frame errors before changing architecture.
5. Complete only obvious hidden continuations needed for support, collision and
   four-rotation coherence. Do not invent a rear court, second axis, vault
   system or additional precinct.
6. After the locked and top views align, run day/night and close/play/wide/far
   quarter-turn captures. Record the exact inspected artifacts and remaining
   gaps before promoting any claim to `visually reviewed`.

Follow [reference measurement and coordinate calibration](../workflows/reference-measurement-and-coordinate-calibration.md),
[plan-first voxel transcription](../workflows/plan-first-voxel-transcription.md),
and [capture, overlay, and acceptance](../rendering/capture-overlay-and-acceptance.md).
Their procedures are reusable; their Reference 10 numbers and site-specific
structure rules are not.

## Checks

### Mechanical

- Pixel identity: repeat the ImageMagick `identify`, SHA-256, and
  `compare -metric RMSE` checks above if either source file changes.
- Run `python3 tools/reference-1-plan-overlay.py
  /home/shikhar/godot/shots/reference-1-plan-overlay.svg`; the command audits
  source identity, dimensions, half-cell registration points, residual limits,
  normalized integer bounds, inclusive sizes, unique IDs, and evidence bounds.
- Run `python3 tools/reference-site-plan.py
  content/chapter_01/sites/shallows-gate-and-causeway-reference-1-plan.json
  --audit-only` and `python3 tools/reference-1-plan-overlay.py --l3-plan
  content/chapter_01/sites/shallows-gate-and-causeway-reference-1-plan.json
  --audit-only`. To inspect exact source registration, add output
  `/home/shikhar/godot/shots/reference-1-l3-plan-overlay.svg` before
  `--l3-plan`. The Reference 1 audit additionally requires full 9/27
  measurement coverage, source-envelope containment, an unobstructed portal
  and 13-cell deck except for named deliberate masses, candidate-only vertical
  claims, the exact 20 source anchors, and their separation from exclusions.
- Run `python3 tools/reference-1-isometric-calibration.py
  /home/shikhar/godot/shots/reference-1-isometric-calibration.svg`; it audits
  source hashes, site registration, locked camera, five evidence polylines,
  eight landmarks, datum arithmetic, eight level anchors, eight course spans,
  and both integer stair schedules. Generate both source-facing and
  runtime-facing plan previews, then run `./tools/world-authoring.sh audit`,
  `dotnet build --no-restore`, and `git diff --check`.
- Confirm the proposed domain/site envelopes and route nodes remain in bounds
  and do not overlap Bloom Grove Court. These checks establish only data and
  build invariants.

### Visual

- Inspect the registered overhead overlay for footprint, clear axis, openings,
  stair travel, terrain cuts, rubble cells, and tree exclusions.
- Inspect the locked 1672x941 isometric overlay for player scale, gate/bridge
  hierarchy, cliff drop, silhouette, occlusion, materials, light and shadows.
- Inspect all four rotations at close/play/wide/far for hidden support and mass
  classification, plus ordinary day/night lighting. Captures are not acceptance;
  record `author-accepted` only after an explicit author decision.

## Known failures and rejected interpretations

| Interpretation | Status | Why it failed | Replacement |
|---|---|---|---|
| Generic Reference 1 portal assembled from reusable portal, pillar, colonnade, wall, stair or tower builders | `rejected/superseded` | The author rejected the kit-like portal attempt; it produced repeated architecture and broad regular slabs rather than the source hierarchy | Unique site-owned plan and voxel masses measured from both Reference 1 views |
| Existing southern gateway geometry as production source | `rejected/superseded` | `shallows-gateway-domain.json` declares `sourceMode: ToolingFixture`, blends References 2/5/9, and governing docs retain it only as compiler/tool evidence | Preserve the compatible permanent topology/region allocation; replace the fixture geometry with this Reference 1 transcription |
| Two freestanding pillars and one lintel as the gate | `rejected/superseded` | Erases the deep roofed gatehouse, corner masses, recessed passage, and attached buttress rhythm visible in both sources | Transcribe the complete gatehouse footprint and vertical courses |
| Random freestanding pillars along the bridge and gate flanks | `rejected/superseded` | Confuses attached wall ribs and deliberate survivors with scatter and obstructs the processional axis | Classify each visible mass from plan continuity before vertical authoring |
| Symmetric trench or broad stamped platform around the site | `rejected/superseded` | Contradicts the asymmetric gorge, local shelves, water basin and ordinary terrain gaps | Site-owned irregular cliff/shelf cells inside measured bounds; atlas terrain outside |
| Preserving compiled atlas terrain/water inside the measured Reference 1 crop | `rejected/superseded` | The v2 top comparison registered the monument while showing the wrong surrounding land and water; six measured trees would also become submerged when the traced channels were first materialised | Inclusive site-owned context, exact surface/bed water painters, and substantial source-traced shelf/shoreline continuations containing the six anchors |
| Isolated 3x3 full-height dry pedestals beneath conflicting tree anchors | `rejected/superseded` | They fixed data ownership but visibly replaced connected source shelves and shorelines with small rectangular islands | Four irregular source-measured continuations within the registered west, east-middle, south/east, and far-east bounds; tree anchors are assertions on those shapes, not geometry |
| Boolean paving-versus-`NaturalCap` routing for authored dry terrain | `rejected/superseded` | The former builder cloned cap/substrate before site painting, then reused those compiled values for every non-paving dry shelf. A shelf replacing atlas water could therefore inherit drowned paving or wetland mud; the inspected v4 top view exposed that mechanism as a huge pale/maroon quilt. `reclaimed-turf` repeated the same pre-site cap lookup | Resolve every plan label to a complete site-owned profile: Drowned Shallows light grass over one sand or soil course and pale stone; explicit cliff/reclaimed variants; explicit paving/gate stone; explicit dry reclaimed turf. Keep authored water independently fixed at local surface `-21` and bed `-23` |
| One broad local `Y=8` east shelf painted after an intended local `Y=0` court | `rejected/superseded` | It erased 597 of 679 lower-court cells and the inspected v5 top/locked captures read as one elevated east slab, not the source's lower ribs/ranges below a smaller north plateau | Split painter ownership at the side stair: effective local `Y=0` lower court only at `Z >= -7`, local `Y=8` upper plateau only at `Z <= -8` plus its exact landing; keep only the north room/survivor/rubble upper and rebase the other nine east groups by eight courses |
| A six-course south landing stopping at the stair footprint | `rejected/superseded` | The v4/v5 captures read as a closed rectangular pit, while the source continues a lower irregular approach below and around the stair toe | Preserve the measured six-rise stair and extend its local `Y=-6` landing as one connected candidate approach; keep the unrelated east shoreline at local `Y=0` and defer its broad retrace |
| Treating the explicit east stair landing as if it must be a strict subset of the broader irregular court | `rejected/superseded` | The exact five-cell-wide landing deliberately projects two cells beyond the court outline. Generic plan and camera audits passed, but the first two v6 runtime launches were stopped by an over-strong builder assertion before capture | Assert the landing's measured `[30,0]..[34,2]` footprint, local `Y=0`, and stair linkage directly. Keep the landing as the stair's `fromTerrain`; do not weaken the plan or relabel it as the broader court |
| V4 gate as a solid rear span, two blanket front blocks, broad upper corner towers, stepped cap fragments, and full-height side strips | `rejected/superseded` | The locked source requires a continuous daylight passage and four legible attached pylons under one broad roof; the v4 capture instead reads as one sealed central monolith with invented side towers | Split west/east connected bodies around a separately labelled candidate hidden passage; use low front backing plus exactly four principal pylons; reduce rear shoulders; keep one broad local-Y20 roof with a thin damaged rear parapet and returns; break only attached ribs above low buttress backing |

## Explicit unknowns and scope limits

The sources do not settle the gate's rear openings or roof interior, exact
passage depth details, hidden north approach, underside support/vault pattern,
backs of west/east wall ribs, submerged pier bases, far faces of cliff shelves,
or submerged rubble. Tree counts at crop edges are approximate. None may be
invented as source fact.

A strict Reference 1 source-measurement layer, production version-2 plan,
resolved runtime transform, authored site registration, locked review camera,
visible integer course schedule, unique site voxel builder, and inspected v2 3D
matrix now exist. The current source plan additionally owns the full context,
water bed/surface, and four substantial irregular dry shelf/shoreline
continuations. The v4 context capture is negative visual evidence for its former
material routing and surrounding-terrain treatment; v5 verifies that the named
dry profiles render without the former quilt while rejecting its captured
east-level painter order and closed south landing. The v6 locked/top set verifies
the narrow east-level split, nine-group vertical rebase, and connected south
approach, while rejecting the current gate/precinct hierarchy and broad outer
terrain silhouette. The current plan also splits the
gate body west/east around the observed portal and a deliberately `candidate`
minimum hidden continuation; this protects passage continuity without claiming
that the overhead source measured its rear depth. The revised gate builder is
present in v5/v6 but remains visually rejected at its current massing. No
playable collision test or author acceptance exists. The plan settles horizontal ownership and the
schedule settles the first provisional silhouette datum; neither establishes a
one-to-one reconstruction before the current revision is captured and compared.

## Update triggers

Update this ledger when either source image is replaced or tracked, the grid
registration changes, integer bounds or dimensions are refined, camera
quadrant/focus/distance is solved, atlas origin/axis changes, terrain ownership
is authored, hidden geometry receives stronger evidence, a plan/build audit is
added, a capture is inspected, or the author corrects or accepts any named
scope. Replace provisional claims with narrower measured values rather than
leaving conflicting calibrations active.
