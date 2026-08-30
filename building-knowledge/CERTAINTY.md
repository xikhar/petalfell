# Evidence and certainty contract

This contract controls every claim in [`building-knowledge/`](README.md). It
exists because source inspection, a passing audit, a good screenshot, and the
author saying the result is right are four different facts.

## Lifecycle and evidence states

`Lifecycle` is not certainty: use `active` while the entry is current and
`superseded` when it must not guide new work. An active
entry then carries an `Evidence summary` and claim-specific rows using the
states below. This separation prevents one visually reviewed claim from making
every instruction in the entry look visually proven.

The states are not interchangeable, and the first four are not an automatic
promotion ladder. Attach a state to a **specific claim and scope**, not to a
whole site without qualification.

| State | What it proves | Minimum evidence | What it does not prove |
|---|---|---|---|
| `candidate` | A method is worth testing | Rationale and a named missing test | That the reference contains it or that it works |
| `observed/source-measured` | A feature or relation was read from a named source | Reference path, view, landmarks, and measurement/calibration method | That the interpretation survives in 3D or in the runtime |
| `mechanically verified` | A named invariant is enforced or a command passed | Command, date, relevant output/invariant, and owning code/data | Visual similarity, playability, or author approval |
| `visually reviewed` | A named rendered artifact was actually inspected for stated criteria | Artifact paths, camera/view/lighting, date, and remaining visible gaps | Author approval, hidden geometry correctness, or criteria not inspected |
| `author-accepted` | The author explicitly accepted the stated result and scope | The explicit decision, date, artifact/version accepted, and scope | Acceptance of later edits, other views, other sites, or a broader technique |
| `rejected/superseded` | The method or result must not be used as current guidance | Rejection/correction evidence, reason, and replacement link when one exists | That every underlying observation was useless |

`rejected/superseded` is terminal for that recorded method version. A useful
observation from it may be carried into a new entry with its own evidence.

## Scope labels

Every claim also declares one scope:

- `general`: demonstrated across enough distinct cases to be a project rule;
- `reference-family`: demonstrated for named references sharing the relevant
  geometry or material condition;
- `site-specific`: demonstrated only for one named reconstruction;
- `tool-specific`: an invariant of one named schema, audit, or capture rig.

Use the narrowest honest scope. Seeing a technique work on Reference 10 makes it
`site-specific`, even when it looks promising elsewhere. Widen scope only after
new named evidence.

## Operational rules

1. **No implicit promotion.** `dotnet build`, an audit, or `git diff --check`
   can establish only their stated mechanical claims. They can never establish
   `visually reviewed` or `author-accepted`.
2. **A render is not a review.** A capture command proves that an image was
   produced. `visually reviewed` additionally requires inspecting that exact
   image and recording the criteria and gaps.
3. **A metric is diagnostic.** Colour RMSE and edge delta help compare two
   captures with the same calibration. They do not decide whether topology,
   architecture, materials, or story match the reference.
4. **Only the author grants acceptance.** Do not infer it from "looks better,"
   continuation instructions, old status prose, or the absence of criticism.
   Record precisely what artifact and views were accepted.
5. **Acceptance can go stale.** Any geometry, coordinate, material, shader,
   lighting, camera, or source-reference change that affects the accepted scope
   returns the changed claims to the evidence state actually re-established.
6. **Negative evidence wins.** When a later author correction or capture
   contradicts an entry, mark the affected method `rejected/superseded`
   immediately; do not leave both instructions looking current.
7. **Current files win over remembered history.** Old captures remain useful
   evidence for why a method was chosen, but they do not prove the present
   worktree still has that property.

## Evidence record format

Use one row per claim:

| Claim | State | Scope | Evidence | Remaining uncertainty |
|---|---|---|---|---|
| Example: stair endpoints meet both named levels | `mechanically verified` | tool-specific | `./tools/world-authoring.sh preview-site-plan ...`; strict ground-plan audit; 2026-08-30 | Audit does not prove the rendered stair resembles the source |

Evidence should name stable repository sources first, then disposable artifacts:

- source reference and supporting view;
- authored plan or builder path;
- enforcing audit/test path and exact command;
- capture directory and exact images inspected;
- explicit author decision when, and only when, acceptance occurred.

Do not write "verified in screenshots" without paths and criteria. Do not cite a
different revision's capture as proof of the current geometry. If the current
revision has not been recaptured, say so.

## Conflict and update handling

When evidence conflicts, preserve both facts and narrow the conclusion. For
example, a plan can be mechanically valid while its locked isometric render is
visually wrong. Record the mechanical row as passing and the visual row as
failed; do not average them into "mostly verified."

When replacing a method:

1. add the failure to the old entry;
2. set its status to `rejected/superseded` for the affected scope;
3. create or update the replacement;
4. add reciprocal supersession links;
5. run fresh evidence for the replacement;
6. update any index or owner-doc link that named the old method as current.
