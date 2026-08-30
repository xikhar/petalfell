#!/usr/bin/env python3
"""Audit and render the Reference 1 source-measurement plan over its top view.

This tool does not generate architecture. It freezes a source-facing one-cell
grid, dispersed registration evidence, and separately named component
envelopes so the eventual site builder cannot hide plan drift in local offsets.
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import runpy
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MEASUREMENT_PATH = (
    ROOT
    / "content"
    / "chapter_01"
    / "sites"
    / "shallows-gate-and-causeway-reference-1-measurement.json"
)
L3_PLAN_PATH = (
    ROOT
    / "content"
    / "chapter_01"
    / "sites"
    / "shallows-gate-and-causeway-reference-1-plan.json"
)
GENERIC_PLAN_TOOL = ROOT / "tools" / "reference-site-plan.py"


def fail(message: str) -> None:
    raise ValueError(message)


def is_number(value: object) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def is_half_grid(value: object) -> bool:
    return is_number(value) and math.isclose(float(value) * 2.0, round(float(value) * 2.0))


def read_png_size(path: Path) -> tuple[int, int]:
    with path.open("rb") as source:
        header = source.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n":
        fail(f"{path} is not a readable PNG")
    return struct.unpack(">II", header[16:24])


def resource_path(value: str) -> Path:
    prefix = "res://"
    if not value.startswith(prefix):
        fail(f"expected a res:// path, got {value!r}")
    return ROOT / value[len(prefix):]


def geometry_points(item: dict) -> list[tuple[int, int]]:
    if "bounds" in item:
        bounds = item["bounds"]
        if len(bounds) != 4 or not all(isinstance(v, int) and not isinstance(v, bool) for v in bounds):
            fail(f"{item.get('id', '<unnamed>')} bounds must be four integers")
        x0, z0, x1, z1 = bounds
        if x0 > x1 or z0 > z1:
            fail(f"{item.get('id', '<unnamed>')} bounds are not normalized")
        return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]
    polygon = item.get("polygon")
    if not isinstance(polygon, list) or len(polygon) < 3:
        fail(f"{item.get('id', '<unnamed>')} needs bounds or a polygon")
    points: list[tuple[int, int]] = []
    for point in polygon:
        if (
            not isinstance(point, list)
            or len(point) != 2
            or not all(isinstance(v, int) and not isinstance(v, bool) for v in point)
        ):
            fail(f"{item.get('id', '<unnamed>')} polygon points must be integer [x,z]")
        points.append((point[0], point[1]))
    return points


def audit(data: dict) -> list[str]:
    errors: list[str] = []

    def check(action) -> None:
        try:
            action()
        except (OSError, ValueError, KeyError, TypeError) as error:
            errors.append(str(error))

    if data.get("version") != 1:
        errors.append("measurement version must be 1")
    if data.get("kind") != "source-measurement-plan":
        errors.append("kind must be 'source-measurement-plan'")
    if data.get("siteId") != "shallows-gate-and-causeway":
        errors.append("siteId must be 'shallows-gate-and-causeway'")

    contract = data.get("coordinateContract", {})
    if contract.get("plotView") != "source-facing":
        errors.append("coordinateContract.plotView must be source-facing")
    if contract.get("oneCellIsOneVoxel") is not True:
        errors.append("coordinateContract.oneCellIsOneVoxel must be true")
    if contract.get("runtimeTransformStatus") != "unresolved":
        errors.append("runtime transform must remain explicitly unresolved at measurement stage")
    if not is_number(contract.get("pixelsPerVoxel")) or contract.get("pixelsPerVoxel", 0) <= 0:
        errors.append("coordinateContract.pixelsPerVoxel must be positive")

    primary_path = resource_path(data.get("primaryReferencePath", ""))
    check(lambda: (
        read_png_size(primary_path) == (1672, 941)
        or fail(f"{primary_path} size {read_png_size(primary_path)} != (1672, 941)")
    ))
    check(lambda: (
        hashlib.sha256(primary_path.read_bytes()).hexdigest() == data.get("primarySourceSha256")
        or fail(f"{primary_path} SHA-256 does not match the registered primary source")
    ))

    top_path = resource_path(data.get("overheadReferencePath", ""))
    expected_size = (
        contract.get("sourceWidthPixels"),
        contract.get("sourceHeightPixels"),
    )
    check(lambda: (
        read_png_size(top_path) == expected_size
        or fail(f"{top_path} size {read_png_size(top_path)} != {expected_size}")
    ))
    check(lambda: (
        hashlib.sha256(top_path.read_bytes()).hexdigest() == data.get("overheadSourceSha256")
        or fail(f"{top_path} SHA-256 does not match the registered overhead source")
    ))

    ids: set[str] = set()
    evidence_bounds = contract.get("broaderEvidenceBounds", [])
    if len(evidence_bounds) != 4 or not all(isinstance(v, int) for v in evidence_bounds):
        errors.append("coordinateContract.broaderEvidenceBounds must be four integers")
        evidence_bounds = [-10_000, -10_000, 10_000, 10_000]
    bx0, bz0, bx1, bz1 = evidence_bounds

    for group_name in ("terrainRegions", "components"):
        for item in data.get(group_name, []):
            item_id = item.get("id", "")
            if not item_id:
                errors.append(f"every {group_name} item needs an id")
                continue
            if item_id in ids:
                errors.append(f"duplicate measurement id {item_id!r}")
            ids.add(item_id)
            try:
                points = geometry_points(item)
            except ValueError as error:
                errors.append(str(error))
                continue
            for x, z in points:
                if x < bx0 or x > bx1 or z < bz0 or z > bz1:
                    errors.append(f"{item_id} point {x},{z} leaves broaderEvidenceBounds")
            if "bounds" in item:
                x0, z0, x1, z1 = item["bounds"]
                measured = item.get("measuredSize")
                actual = [x1 - x0 + 1, z1 - z0 + 1]
                if measured != actual:
                    errors.append(f"{item_id} measuredSize {measured} != inclusive bounds size {actual}")

    origin = contract.get("originPixel", {})
    origin_u = origin.get("u")
    origin_v = origin.get("v")
    cell = contract.get("pixelsPerVoxel")
    if not all(is_number(value) for value in (origin_u, origin_v, cell)):
        errors.append("originPixel u/v and pixelsPerVoxel must be numeric")
    else:
        for landmark in data.get("registrationLandmarks", []):
            landmark_id = landmark.get("id", "")
            if not landmark_id:
                errors.append("every registration landmark needs an id")
                continue
            if landmark_id in ids:
                errors.append(f"duplicate measurement id {landmark_id!r}")
            ids.add(landmark_id)
            source = landmark.get("sourcePixel", [])
            point = landmark.get("planPoint", [])
            if len(source) != 2 or not all(is_number(v) for v in source):
                errors.append(f"{landmark_id} sourcePixel must be numeric [u,v]")
                continue
            if len(point) != 2 or not all(is_half_grid(v) for v in point):
                errors.append(f"{landmark_id} planPoint must lie on the half-voxel grid")
                continue
            predicted_u = float(origin_u) + float(cell) * float(point[0])
            predicted_v = float(origin_v) + float(cell) * float(point[1])
            residual = math.hypot(source[0] - predicted_u, source[1] - predicted_v)
            limit = landmark.get("maxResidualPixels")
            if not is_number(limit) or residual > float(limit) + 1e-6:
                errors.append(
                    f"{landmark_id} residual {residual:.2f}px exceeds {limit!r}px"
                )

    player = contract.get("playerCell", [])
    if len(player) != 2 or not all(isinstance(v, int) for v in player):
        errors.append("coordinateContract.playerCell must be integer [x,z]")
    detail_ids: set[str] = set()
    for item in data.get("detailLandmarks", []):
        item_id = item.get("id", "")
        cell_point = item.get("cell", [])
        if not item_id or item_id in detail_ids:
            errors.append(f"detail landmark id is missing or repeated: {item_id!r}")
        detail_ids.add(item_id)
        if len(cell_point) != 2 or not all(isinstance(v, int) for v in cell_point):
            errors.append(f"detail landmark {item_id!r} needs integer cell")
    return errors


def safe_id(value: str) -> str:
    return "".join(ch if ch.isalnum() or ch in "_.-" else "-" for ch in value)


def number(value: float) -> str:
    return f"{value:.3f}".rstrip("0").rstrip(".")


def render(data: dict) -> str:
    contract = data["coordinateContract"]
    width = contract["sourceWidthPixels"]
    height = contract["sourceHeightPixels"]
    origin_u = contract["originPixel"]["u"]
    origin_v = contract["originPixel"]["v"]
    cell = contract["pixelsPerVoxel"]

    def u(x: float) -> float:
        return origin_u + cell * x

    def v(z: float) -> float:
        return origin_v + cell * z

    def rect(bounds: list[int], css: str, item_id: str, label: str) -> str:
        x0, z0, x1, z1 = bounds
        left = u(x0 - 0.5)
        top = v(z0 - 0.5)
        rect_width = (x1 - x0 + 1) * cell
        rect_height = (z1 - z0 + 1) * cell
        return (
            f'<rect id="{safe_id(item_id)}" class="{css}" x="{number(left)}" '
            f'y="{number(top)}" width="{number(rect_width)}" '
            f'height="{number(rect_height)}"><title>{html.escape(label)}</title></rect>'
        )

    def polygon(points: list[list[int]], css: str, item_id: str, label: str) -> str:
        geometry = " ".join(f"{number(u(x))},{number(v(z))}" for x, z in points)
        return (
            f'<polygon id="{safe_id(item_id)}" class="{css}" points="{geometry}">'
            f'<title>{html.escape(label)}</title></polygon>'
        )

    parts = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
        f'viewBox="0 0 {width} {height}" style="background:transparent" '
        f'data-calibration="u={origin_u}+{cell}*x;v={origin_v}+{cell}*z">',
        "<title>Reference 1 source-facing one-cell measurement overlay</title>",
        "<desc>Audited horizontal evidence only. Rectangular component envelopes are not fill instructions.</desc>",
        "<style>",
        ".minor{stroke:#1686dc;stroke-opacity:.18;stroke-width:.55}.major{stroke:#ff356f;stroke-opacity:.46;stroke-width:1.15}",
        ".axis{stroke:#fff;stroke-opacity:.88;stroke-width:2}.grid-label{font:11px ui-monospace,monospace;fill:#7d1640;paint-order:stroke;stroke:#fff;stroke-opacity:.7;stroke-width:2px}",
        ".terrain{fill:#9bc65f;fill-opacity:.14;stroke:#497c2c;stroke-opacity:.9;stroke-width:1.5;stroke-dasharray:5 3;vector-effect:non-scaling-stroke}",
        ".terrain-candidate{fill:#5579d9;fill-opacity:.12;stroke:#31569d;stroke-opacity:.85;stroke-width:1.5;stroke-dasharray:7 4;vector-effect:non-scaling-stroke}",
        ".component{fill:#71c5f2;fill-opacity:.21;stroke:#087db9;stroke-opacity:.9;stroke-width:1.25;vector-effect:non-scaling-stroke}",
        ".gate{fill:#9e8adf;fill-opacity:.26;stroke:#5c3db2}.passage{fill:#ffcb68;fill-opacity:.28;stroke:#a86608;stroke-width:2}",
        ".causeway{fill:#f2b2cc;fill-opacity:.20;stroke:#b43a72}.clear{fill:#fff;fill-opacity:.08;stroke:#fff;stroke-opacity:.86;stroke-dasharray:7 3}",
        ".precinct{fill:#7bc5ed;fill-opacity:.13;stroke-dasharray:5 3}.stair{fill:#f18e75;fill-opacity:.30;stroke:#ba452d;stroke-width:1.7}",
        ".component-label{font:700 10px ui-monospace,monospace;fill:#332743;text-anchor:middle;paint-order:stroke;stroke:#fff;stroke-opacity:.9;stroke-width:2.6px;pointer-events:none}",
        ".registration circle{fill:#fff;fill-opacity:.72;stroke:#d51d5b;stroke-width:1.5}.registration path{stroke:#d51d5b;stroke-width:1.25}.detail circle{fill:#0f719e;fill-opacity:.62;stroke:#fff;stroke-width:1.2}",
        ".tree circle{fill:#ef8fbe;fill-opacity:.18;stroke:#9d326e;stroke-width:1.2}.tree path{stroke:#9d326e;stroke-width:1}",
        "</style>",
        '<g id="grid">',
    ]

    min_x = math.floor((0 - origin_u) / cell) - 1
    max_x = math.ceil((width - origin_u) / cell) + 1
    min_z = math.floor((0 - origin_v) / cell) - 1
    max_z = math.ceil((height - origin_v) / cell) + 1
    for x in range(min_x, max_x + 1):
        boundary = u(x - 0.5)
        css = "major" if x % 5 == 0 else "minor"
        parts.append(f'<line class="{css}" x1="{number(boundary)}" y1="0" x2="{number(boundary)}" y2="{height}"/>')
        if x % 5 == 0 and 0 <= boundary <= width:
            parts.append(f'<text class="grid-label" x="{number(boundary + 3)}" y="15">x{x}</text>')
    for z in range(min_z, max_z + 1):
        boundary = v(z - 0.5)
        css = "major" if z % 5 == 0 else "minor"
        parts.append(f'<line class="{css}" x1="0" y1="{number(boundary)}" x2="{width}" y2="{number(boundary)}"/>')
        if z % 5 == 0 and 0 <= boundary <= height:
            parts.append(f'<text class="grid-label" x="3" y="{number(boundary - 3)}">z{z}</text>')
    parts.extend([
        f'<line class="axis" x1="{number(u(0))}" y1="0" x2="{number(u(0))}" y2="{height}"/>',
        f'<line class="axis" x1="0" y1="{number(v(0))}" x2="{width}" y2="{number(v(0))}"/>',
        "</g>",
        '<g id="terrain-regions">',
    ])
    for item in data.get("terrainRegions", []):
        css = "terrain-candidate" if item.get("certainty") == "candidate" else "terrain"
        label = f"{item['id']}; {item['role']}; Y={item.get('provisionalSurfaceY')} ({item.get('certainty')})"
        parts.append(polygon(item["polygon"], css, f"terrain-{item['id']}", label))
    parts.append("</g>")

    parts.append('<g id="components">')
    for item in data.get("components", []):
        role = item.get("role", "")
        css = "component"
        if item["id"].startswith("gate-"):
            css += " gate"
        if "passage" in role:
            css += " passage"
        if item["id"].startswith("causeway-"):
            css += " causeway"
        if "keep-open" in role:
            css += " clear"
        if "precinct" in item["id"] or "range" in item["id"] or "rib" in item["id"]:
            css += " precinct"
        if "stair" in item["id"]:
            css += " stair"
        label = f"{item['id']}; {role}; {item['measuredSize'][0]}x{item['measuredSize'][1]} ({item.get('certainty')})"
        parts.append(rect(item["bounds"], css, f"component-{item['id']}", label))
        overlay_label = item.get("overlayLabel")
        if overlay_label:
            x0, z0, x1, z1 = item["bounds"]
            centre_x = u((x0 + x1) / 2.0)
            centre_z = v((z0 + z1) / 2.0)
            parts.append(
                f'<text class="component-label" x="{number(centre_x)}" y="{number(centre_z)}">'
                f'{html.escape(overlay_label)}</text>'
            )
    parts.append("</g>")

    parts.append('<g id="registration-landmarks">')
    for item in data.get("registrationLandmarks", []):
        px, py = item["sourcePixel"]
        plan_x, plan_z = item["planPoint"]
        predicted_u = u(plan_x)
        predicted_v = v(plan_z)
        residual = math.hypot(px - predicted_u, py - predicted_v)
        label = f"{item['id']}; plan={plan_x},{plan_z}; residual={residual:.2f}px"
        parts.append(
            f'<g class="registration"><title>{html.escape(label)}</title>'
            f'<path d="M {number(px - 5)} {number(py)} H {number(px + 5)} M {number(px)} {number(py - 5)} V {number(py + 5)}"/>'
            f'<circle cx="{number(px)}" cy="{number(py)}" r="3.2"/></g>'
        )
    parts.append("</g>")

    parts.append('<g id="detail-landmarks">')
    for item in data.get("detailLandmarks", []):
        x, z = item["cell"]
        label = f"{item['id']}; cell={x},{z}; {item['role']}"
        parts.append(
            f'<g class="detail"><title>{html.escape(label)}</title>'
            f'<circle cx="{number(u(x))}" cy="{number(v(z))}" r="4"/></g>'
        )
    parts.append("</g>")

    parts.append('<g id="tree-anchors">')
    for index, (x, z) in enumerate(data.get("surroundingTreeAnchors", []), start=1):
        cx, cy = u(x), v(z)
        parts.append(
            f'<g class="tree"><title>tree-anchor-{index:02d}; cell={x},{z}</title>'
            f'<circle cx="{number(cx)}" cy="{number(cy)}" r="11"/>'
            f'<path d="M {number(cx - 4)} {number(cy)} H {number(cx + 4)} M {number(cx)} {number(cy - 4)} V {number(cy + 4)}"/></g>'
        )
    parts.extend(["</g>", "</svg>"])
    return "\n".join(parts) + "\n"


def generic_plan_api() -> dict:
    """Load the shared version-2 plan auditor without importing a hyphenated module."""

    return runpy.run_path(str(GENERIC_PLAN_TOOL))


def structure_cells(structure: dict, api: dict) -> set[tuple[int, int]]:
    rect_cells = api["rect_cells"]
    if structure.get("kind") == "stair":
        return set().union(
            *(rect_cells(tread["footprint"]) for tread in structure.get("treads", []))
        )
    if structure.get("kind") == "rubble-cluster":
        return {tuple(cell) for cell in structure.get("cells", [])}
    footprints = structure.get("footprints")
    if footprints is not None:
        return set().union(*(rect_cells(footprint) for footprint in footprints))
    return rect_cells(structure["footprint"])


def measurement_cells(item: dict, api: dict) -> set[tuple[int, int]]:
    if "bounds" in item:
        return api["rect_cells"](item["bounds"])
    return api["polygon_cells"](item["polygon"])


def audit_l3(measurement: dict, plan: dict, measurement_path: Path) -> tuple[list[str], dict]:
    """Audit Reference 1 registration and evidence coverage beyond the generic schema."""

    api = generic_plan_api()
    errors = [f"ground plan: {error}" for error in api["audit"](plan)]

    if plan.get("siteId") != measurement.get("siteId"):
        errors.append("plan and measurement siteId values must match")
    if plan.get("referencePath") != measurement.get("primaryReferencePath"):
        errors.append("plan referencePath must name the registered primary source")
    try:
        registered_measurement = resource_path(plan.get("sourceMeasurementPath", "")).resolve()
        if registered_measurement != measurement_path.resolve():
            errors.append("plan sourceMeasurementPath does not name the audited measurement file")
    except (OSError, ValueError) as error:
        errors.append(str(error))

    measured_contract = measurement.get("coordinateContract", {})
    plan_contract = plan.get("coordinateContract", {})
    if plan_contract.get("origin") != measured_contract.get("origin"):
        errors.append("plan origin must preserve the registered source origin")
    player_cell = measured_contract.get("playerCell", [])
    expected_player = (
        {"x": player_cell[0], "z": player_cell[1]}
        if isinstance(player_cell, list) and len(player_cell) == 2
        else None
    )
    if plan_contract.get("playerSpawn") != expected_player:
        errors.append("plan playerSpawn must preserve the registered source player cell")
    if plan_contract.get("runtimeTransformStatus") != "resolved":
        errors.append("Reference 1 runtime transform must use the resolved locked-camera solve")
    if plan_contract.get("runtimeMirrorX") is not False:
        errors.append("Reference 1 runtimeMirrorX must preserve source +X without reflection")
    if plan_contract.get("runtimePlanScale") != 3 or plan_contract.get("oneCellIsOneVoxel") is not False:
        errors.append("Reference 1 must expand each measured source cell to exactly 3x3 runtime voxels")
    if plan_contract.get("sourceViewYawDegrees") != 45:
        errors.append("sourceViewYawDegrees must be the locked 45-degree quadrant")
    if plan_contract.get("sourceViewPitchDegrees") != 35.26439:
        errors.append("sourceViewPitchDegrees must preserve the locked true-isometric pitch")
    if plan_contract.get("cameraCalibrationPath") != (
        "res://content/chapter_01/sites/"
        "shallows-gate-and-causeway-reference-1-camera.json"
    ):
        errors.append("Reference 1 plan must link the locked camera calibration")
    if plan_contract.get("verticalSchedulePath") != (
        "res://content/chapter_01/sites/"
        "shallows-gate-and-causeway-reference-1-vertical.json"
    ):
        errors.append("Reference 1 plan must link the visible vertical schedule")

    vertical = plan.get("provisionalVerticalContract", {})
    if vertical.get("status") != "candidate":
        errors.append("provisionalVerticalContract.status must remain 'candidate'")
    if not vertical.get("unsolved"):
        errors.append("provisionalVerticalContract must enumerate unresolved vertical facts")

    measurement_regions = {
        item["id"]: item for item in measurement.get("terrainRegions", []) if item.get("id")
    }
    measurement_components = {
        item["id"]: item for item in measurement.get("components", []) if item.get("id")
    }
    coverage = plan.get("measurementCoverage", {})
    region_coverage = coverage.get("terrainRegionIds", {})
    component_coverage = coverage.get("componentIds", {})
    if set(region_coverage) != set(measurement_regions):
        errors.append(
            "measurementCoverage.terrainRegionIds must cover every and only measured terrain region"
        )
    if set(component_coverage) != set(measurement_components):
        errors.append(
            "measurementCoverage.componentIds must cover every and only measured component"
        )

    terrain_by_id = {item.get("id"): item for item in plan.get("terrain", [])}
    structures_by_id = {item.get("id"): item for item in plan.get("structures", [])}
    open_by_id = {item.get("id"): item for item in plan.get("keepOpenRegions", [])}
    all_plan_ids = set(terrain_by_id) | set(structures_by_id) | set(open_by_id)
    for group_name, mappings in (
        ("terrainRegionIds", region_coverage),
        ("componentIds", component_coverage),
    ):
        for source_id, target_ids in mappings.items():
            if not isinstance(target_ids, list) or not target_ids:
                errors.append(f"measurementCoverage.{group_name}.{source_id} must name plan IDs")
                continue
            if len(target_ids) != len(set(target_ids)):
                errors.append(f"measurementCoverage.{group_name}.{source_id} repeats a plan ID")
            missing = set(target_ids) - all_plan_ids
            if missing:
                errors.append(
                    f"measurementCoverage.{group_name}.{source_id} names missing plan IDs {sorted(missing)}"
                )

    component_cell_sets = {
        item_id: measurement_cells(item, api)
        for item_id, item in measurement_components.items()
    }
    evidence_bounds = measured_contract.get("broaderEvidenceBounds", [])
    try:
        evidence_cells = api["rect_cells"](evidence_bounds)
    except ValueError as error:
        errors.append(f"broader evidence bounds: {error}")
        evidence_cells = set()

    projection_by_id: dict[str, set[tuple[int, int]]] = {}
    for structure in plan.get("structures", []):
        structure_id = structure.get("id", "")
        if structure.get("verticalStatus") != "provisional":
            errors.append(f"structure '{structure_id}' must keep verticalStatus provisional")
        clearance = structure.get("groundClearanceY")
        if clearance is not None and (
            not isinstance(clearance, int) or isinstance(clearance, bool) or clearance <= 0
        ):
            errors.append(f"structure '{structure_id}' groundClearanceY must be a positive integer")
        try:
            cells = structure_cells(structure, api)
        except (KeyError, TypeError, ValueError) as error:
            errors.append(f"structure '{structure_id}' projection: {error}")
            continue
        projection_by_id[structure_id] = cells
        if evidence_cells and cells - evidence_cells:
            errors.append(
                f"structure '{structure_id}' leaves broaderEvidenceBounds at {min(cells - evidence_cells)}"
            )
        component_ids = structure.get("measurementComponentIds", [])
        if not component_ids:
            errors.append(f"structure '{structure_id}' needs measurementComponentIds")
            continue
        missing_components = set(component_ids) - set(measurement_components)
        if missing_components:
            errors.append(
                f"structure '{structure_id}' names missing measured components {sorted(missing_components)}"
            )
            continue
        measured_envelope = set().union(*(component_cell_sets[item_id] for item_id in component_ids))
        outside = cells - measured_envelope
        if outside:
            errors.append(
                f"structure '{structure_id}' leaves its measured component envelope at {min(outside)}"
            )
        for component_id in component_ids:
            if structure_id not in component_coverage.get(component_id, []):
                errors.append(
                    f"structure '{structure_id}' is not listed by coverage for '{component_id}'"
                )

    for item in plan.get("keepOpenRegions", []):
        item_id = item.get("id", "")
        component_ids = item.get("measurementComponentIds", [])
        if len(component_ids) != 1 or component_ids[0] not in measurement_components:
            errors.append(f"keep-open region '{item_id}' must name one measured component")
            continue
        component_id = component_ids[0]
        if item_id not in component_coverage.get(component_id, []):
            errors.append(f"keep-open region '{item_id}' is absent from measurement coverage")
        try:
            keep_open_cells = api["rect_cells"](item["bounds"])
        except (KeyError, ValueError) as error:
            errors.append(f"keep-open region '{item_id}': {error}")
            continue
        if keep_open_cells != component_cell_sets[component_id]:
            errors.append(f"keep-open region '{item_id}' must exactly match '{component_id}'")
        allowed_ids = set(item.get("allowedStructureIds", []))
        missing_allowed = allowed_ids - set(structures_by_id)
        if missing_allowed:
            errors.append(f"keep-open region '{item_id}' allows missing structures {sorted(missing_allowed)}")
        for structure_id, cells in projection_by_id.items():
            structure = structures_by_id[structure_id]
            if structure.get("groundClearanceY", 0) > 0:
                continue
            overlap = cells & keep_open_cells
            if overlap and structure_id not in allowed_ids:
                errors.append(
                    f"structure '{structure_id}' obstructs keep-open region '{item_id}' at {min(overlap)}"
                )

    source_trees = [tuple(point) for point in measurement.get("surroundingTreeAnchors", [])]
    plan_trees = [tuple(point) for point in plan.get("surroundingTrees", [])]
    if plan_trees != source_trees:
        errors.append("surroundingTrees must preserve the measured source-facing anchor order and cells")
    if len(plan_trees) != len(set(plan_trees)):
        errors.append("surroundingTrees contains duplicate anchors")
    exclusion_ids: set[str] = set()
    excluded_cells: set[tuple[int, int]] = set()
    for region in plan.get("treeExclusionRegions", []):
        region_id = region.get("id", "")
        if not region_id or region_id in exclusion_ids:
            errors.append(f"tree exclusion id is missing or repeated: {region_id!r}")
        exclusion_ids.add(region_id)
        try:
            cells = api["polygon_cells"](region["polygon"])
        except (KeyError, ValueError) as error:
            errors.append(f"tree exclusion '{region_id}': {error}")
            continue
        if not cells:
            errors.append(f"tree exclusion '{region_id}' covers no cells")
        excluded_cells |= cells
    for tree in plan_trees:
        if tree in excluded_cells:
            errors.append(f"source tree anchor {tree} falls inside a tree exclusion")

    visible_owner: dict[tuple[int, int], str] = {}
    raw_terrain_cells: dict[str, set[tuple[int, int]]] = {}
    for terrain in plan.get("terrain", []):
        terrain_id = terrain.get("id", "")
        try:
            cells = api["shape_cells"](terrain)
        except ValueError:
            continue
        raw_terrain_cells[terrain_id] = cells
        for cell in cells:
            visible_owner[cell] = terrain_id
    visible_counts = {
        terrain_id: sum(1 for owner_id in visible_owner.values() if owner_id == terrain_id)
        for terrain_id in raw_terrain_cells
    }
    projection_union = set().union(*projection_by_id.values()) if projection_by_id else set()
    metrics = {
        "terrainShapes": len(plan.get("terrain", [])),
        "visibleTerrainCells": sum(visible_counts.values()),
        "siteOwnedTerrainCells": sum(
            count
            for terrain_id, count in visible_counts.items()
            if terrain_by_id[terrain_id].get("writeMode") == "author-surface"
        ),
        "structures": len(plan.get("structures", [])),
        "structureProjectionCells": len(projection_union),
        "stairs": sum(1 for item in plan.get("structures", []) if item.get("kind") == "stair"),
        "rubbleClusters": sum(
            1 for item in plan.get("structures", []) if item.get("kind") == "rubble-cluster"
        ),
        "keepOpenRegions": len(plan.get("keepOpenRegions", [])),
        "treeExclusions": len(plan.get("treeExclusionRegions", [])),
        "treeAnchors": len(plan_trees),
        "measuredTerrainRegions": len(measurement_regions),
        "measuredComponents": len(measurement_components),
    }
    return errors, metrics


def render_l3(measurement: dict, plan: dict) -> str:
    """Render exact authored cell projections in the registered top-source frame."""

    api = generic_plan_api()
    contract = measurement["coordinateContract"]
    width = contract["sourceWidthPixels"]
    height = contract["sourceHeightPixels"]
    origin_u = contract["originPixel"]["u"]
    origin_v = contract["originPixel"]["v"]
    cell = contract["pixelsPerVoxel"]

    def u(x: float) -> float:
        return origin_u + cell * x

    def v(z: float) -> float:
        return origin_v + cell * z

    def cell_path(cells: set[tuple[int, int]]) -> str:
        commands = []
        for x, z in sorted(cells, key=lambda point: (point[1], point[0])):
            commands.append(
                f"M{number(u(x - .5))} {number(v(z - .5))}h{number(cell)}v{number(cell)}h-{number(cell)}z"
            )
        return "".join(commands)

    def measured_rect(bounds: list[int], item_id: str, css: str, label: str) -> str:
        x0, z0, x1, z1 = bounds
        return (
            f'<rect id="{safe_id(item_id)}" class="{css}" x="{number(u(x0 - .5))}" '
            f'y="{number(v(z0 - .5))}" width="{number((x1 - x0 + 1) * cell)}" '
            f'height="{number((z1 - z0 + 1) * cell)}"><title>{html.escape(label)}</title></rect>'
        )

    def measured_polygon(points: list[list[int]], item_id: str, css: str, label: str) -> str:
        geometry = " ".join(f"{number(u(x))},{number(v(z))}" for x, z in points)
        return (
            f'<polygon id="{safe_id(item_id)}" class="{css}" points="{geometry}">'
            f'<title>{html.escape(label)}</title></polygon>'
        )

    parts = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
        f'viewBox="0 0 {width} {height}" style="background:transparent" '
        f'data-calibration="u={origin_u}+{cell}*x;v={origin_v}+{cell}*z">',
        "<title>Reference 1 strict source-facing L3 plan overlay</title>",
        "<desc>Exact one-cell-per-voxel horizontal ownership. All Y values and courses remain provisional.</desc>",
        "<style>",
        ".minor{stroke:#187ec8;stroke-opacity:.13;stroke-width:.45}.major{stroke:#e9366e;stroke-opacity:.32;stroke-width:1}",
        ".axis{stroke:#fff;stroke-opacity:.82;stroke-width:1.7}.grid-label{font:10px ui-monospace,monospace;fill:#7d1640;paint-order:stroke;stroke:#fff;stroke-opacity:.72;stroke-width:2px}",
        ".measurement-terrain{fill:none;stroke:#4f7f35;stroke-opacity:.45;stroke-width:1.2;stroke-dasharray:7 4}",
        ".measurement-component{fill:none;stroke:#067db8;stroke-opacity:.36;stroke-width:1;stroke-dasharray:4 3}",
        ".terrain-context{fill:#d1d8a4;fill-opacity:.045;stroke:none}.terrain-water{fill:#44a3e8;fill-opacity:.28;stroke:#176ca6;stroke-opacity:.45;stroke-width:.5}",
        ".terrain-cliff{fill:#b8a47e;fill-opacity:.32;stroke:#6c5b43;stroke-opacity:.52;stroke-width:.55}.terrain-site{fill:#d8c99c;fill-opacity:.38;stroke:#75623b;stroke-opacity:.58;stroke-width:.6}",
        ".patch{fill:#8aaf5a;fill-opacity:.24;stroke:#526f35;stroke-opacity:.6;stroke-width:.55}",
        ".structure{fill:#e58bc4;fill-opacity:.53;stroke:#782c67;stroke-opacity:.9;stroke-width:.8}.gate{fill:#a48ce1;fill-opacity:.58;stroke:#503196}",
        ".causeway{fill:#f19bb8;fill-opacity:.58;stroke:#9b315d}.west{fill:#92aee7;fill-opacity:.56;stroke:#39578f}.east{fill:#7cc9d7;fill-opacity:.56;stroke:#286f7b}",
        ".rubble{fill:#f3c681;fill-opacity:.68;stroke:#8c5c22}.stair{fill:#ef8c76;fill-opacity:.65;stroke:#9d3729}",
        ".keep-open{fill:#fff;fill-opacity:.12;stroke:#fff;stroke-opacity:.96;stroke-width:2;stroke-dasharray:7 3}.tree-exclusion{fill:none;stroke:#4e8c47;stroke-opacity:.72;stroke-width:1.3;stroke-dasharray:5 3}",
        ".tree circle{fill:#ef8fbe;fill-opacity:.25;stroke:#902f67;stroke-width:1.2}.tree path{stroke:#902f67;stroke-width:1}.player{fill:#176fbe;stroke:#fff;stroke-width:1.5}",
        ".registration path{stroke:#d51d5b;stroke-width:1.25}.registration circle{fill:#fff;fill-opacity:.76;stroke:#d51d5b;stroke-width:1.3}",
        ".site-label{font:700 12px ui-monospace,monospace;fill:#38253e;text-anchor:middle;paint-order:stroke;stroke:#fff;stroke-opacity:.85;stroke-width:3px}.legend{font:11px ui-monospace,monospace;fill:#34253a;paint-order:stroke;stroke:#fff;stroke-opacity:.92;stroke-width:3px}",
        "</style>",
        '<g id="grid">',
    ]
    min_x = math.floor((0 - origin_u) / cell) - 1
    max_x = math.ceil((width - origin_u) / cell) + 1
    min_z = math.floor((0 - origin_v) / cell) - 1
    max_z = math.ceil((height - origin_v) / cell) + 1
    for x in range(min_x, max_x + 1):
        boundary = u(x - .5)
        css = "major" if x % 5 == 0 else "minor"
        parts.append(
            f'<line class="{css}" x1="{number(boundary)}" y1="0" x2="{number(boundary)}" y2="{height}"/>'
        )
        if x % 5 == 0 and 0 <= boundary <= width:
            parts.append(f'<text class="grid-label" x="{number(boundary + 3)}" y="15">x{x}</text>')
    for z in range(min_z, max_z + 1):
        boundary = v(z - .5)
        css = "major" if z % 5 == 0 else "minor"
        parts.append(
            f'<line class="{css}" x1="0" y1="{number(boundary)}" x2="{width}" y2="{number(boundary)}"/>'
        )
        if z % 5 == 0 and 0 <= boundary <= height:
            parts.append(f'<text class="grid-label" x="3" y="{number(boundary - 3)}">z{z}</text>')
    parts.extend(
        [
            f'<line class="axis" x1="{number(u(0))}" y1="0" x2="{number(u(0))}" y2="{height}"/>',
            f'<line class="axis" x1="0" y1="{number(v(0))}" x2="{width}" y2="{number(v(0))}"/>',
            "</g>",
            '<g id="measured-envelopes">',
        ]
    )
    for item in measurement.get("terrainRegions", []):
        parts.append(
            measured_polygon(
                item["polygon"], f"measured-{item['id']}", "measurement-terrain", item["id"]
            )
        )
    for item in measurement.get("components", []):
        parts.append(
            measured_rect(
                item["bounds"], f"measured-{item['id']}", "measurement-component", item["id"]
            )
        )
    parts.extend(["</g>", '<g id="terrain-cells">'])

    owner: dict[tuple[int, int], str] = {}
    raw_terrain: dict[str, set[tuple[int, int]]] = {}
    for terrain in plan.get("terrain", []):
        cells = api["shape_cells"](terrain)
        raw_terrain[terrain["id"]] = cells
        for authored_cell in cells:
            owner[authored_cell] = terrain["id"]
    for terrain in plan.get("terrain", []):
        terrain_id = terrain["id"]
        cells = {authored_cell for authored_cell, owner_id in owner.items() if owner_id == terrain_id}
        role = terrain.get("role", "")
        if terrain.get("writeMode") == "preserve-atlas" and "water" not in role:
            css = "terrain-context"
        elif "water" in role:
            css = "terrain-water"
        elif "cliff" in role or "apron" in role:
            css = "terrain-cliff"
        else:
            css = "terrain-site"
        parts.append(
            f'<path id="terrain-{safe_id(terrain_id)}" class="{css}" d="{cell_path(cells)}">'
            f'<title>{html.escape(terrain_id)}; {len(cells)} visible cells; provisional Y={terrain.get("surfaceY")}</title></path>'
        )
    parts.extend(["</g>", '<g id="surface-patches">'])
    for patch in plan.get("surfacePatches", []):
        cells = set().union(*(api["rect_cells"](item) for item in patch.get("footprints", [])))
        parts.append(
            f'<path id="patch-{safe_id(patch["id"])}" class="patch" d="{cell_path(cells)}">'
            f'<title>{html.escape(patch["id"])}; {len(cells)} cells</title></path>'
        )
    parts.extend(["</g>", '<g id="authored-structure-projections">'])
    for structure in plan.get("structures", []):
        structure_id = structure["id"]
        cells = structure_cells(structure, api)
        css = "structure"
        if structure_id.startswith("gate-"):
            css += " gate"
        elif structure_id.startswith("causeway-"):
            css += " causeway"
        elif structure_id.startswith("west-"):
            css += " west"
        elif structure_id.startswith("east-"):
            css += " east"
        if structure.get("kind") == "rubble-cluster":
            css += " rubble"
        if structure.get("kind") == "stair":
            css += " stair"
        parts.append(
            f'<path id="structure-{safe_id(structure_id)}" class="{css}" d="{cell_path(cells)}">'
            f'<title>{html.escape(structure_id)}; {structure.get("kind")}; {len(cells)} projection cells; vertical provisional</title></path>'
        )
    parts.extend(["</g>", '<g id="keep-open-regions">'])
    for item in plan.get("keepOpenRegions", []):
        parts.append(measured_rect(item["bounds"], f"open-{item['id']}", "keep-open", item["id"]))
    parts.extend(["</g>", '<g id="tree-exclusions">'])
    for item in plan.get("treeExclusionRegions", []):
        parts.append(
            measured_polygon(item["polygon"], f"tree-exclusion-{item['id']}", "tree-exclusion", item["id"])
        )
    parts.extend(["</g>", '<g id="tree-anchors">'])
    for index, (x, z) in enumerate(plan.get("surroundingTrees", []), start=1):
        cx, cy = u(x), v(z)
        parts.append(
            f'<g class="tree"><title>tree-{index:02d}; cell={x},{z}</title>'
            f'<circle cx="{number(cx)}" cy="{number(cy)}" r="8"/>'
            f'<path d="M {number(cx - 3)} {number(cy)} H {number(cx + 3)} M {number(cx)} {number(cy - 3)} V {number(cy + 3)}"/></g>'
        )
    spawn = plan["coordinateContract"]["playerSpawn"]
    parts.extend(
        [
            "</g>",
            f'<circle class="player" cx="{number(u(spawn["x"]))}" cy="{number(v(spawn["z"]))}" r="5"><title>player spawn ({spawn["x"]},{spawn["z"]})</title></circle>',
            '<g id="registration-landmarks">',
        ]
    )
    for item in measurement.get("registrationLandmarks", []):
        px, py = item["sourcePixel"]
        parts.append(
            f'<g class="registration"><title>{html.escape(item["id"])}</title>'
            f'<path d="M {number(px - 4)} {number(py)} H {number(px + 4)} M {number(px)} {number(py - 4)} V {number(py + 4)}"/>'
            f'<circle cx="{number(px)}" cy="{number(py)}" r="2.8"/></g>'
        )
    parts.extend(
        [
            "</g>",
            f'<text class="site-label" x="{number(u(0))}" y="{number(v(-21) - 8)}">GATE</text>',
            f'<text class="site-label" x="{number(u(-24))}" y="{number(v(17) + 16)}">WEST PRECINCT</text>',
            f'<text class="site-label" x="{number(u(24))}" y="{number(v(18) + 16)}">EAST PRECINCT</text>',
            f'<text class="site-label" x="{number(u(0))}" y="{number(v(37) + 18)}">CAUSEWAY / SOUTH STAIR</text>',
            '<g class="legend"><text x="10" y="30">REFERENCE 1 L3 — SOURCE-FACING</text><text x="10" y="46">exact X/Z cells; every Y/course provisional</text></g>',
            "</svg>",
        ]
    )
    return "\n".join(parts) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Audit and render Reference 1 source measurement or strict L3 plan overlays."
    )
    parser.add_argument("output", nargs="?", type=Path, help="destination transparent SVG")
    parser.add_argument(
        "--measurement",
        type=Path,
        default=MEASUREMENT_PATH,
        help="measurement JSON (defaults to the tracked Reference 1 layer)",
    )
    parser.add_argument(
        "--l3-plan",
        type=Path,
        help=f"strict version-2 L3 plan (tracked plan: {L3_PLAN_PATH.relative_to(ROOT)})",
    )
    parser.add_argument(
        "--audit-only",
        action="store_true",
        help="run all applicable audits without writing an SVG",
    )
    args = parser.parse_args()
    if not args.audit_only and args.output is None:
        parser.error("output is required unless --audit-only is used")
    try:
        data = json.loads(args.measurement.read_text(encoding="utf-8"))
        errors = audit(data)
        if errors:
            fail("measurement audit failed:\n" + "\n".join(f"  - {error}" for error in errors))
        plan = None
        metrics = None
        if args.l3_plan is not None:
            plan = json.loads(args.l3_plan.read_text(encoding="utf-8"))
            errors, metrics = audit_l3(data, plan, args.measurement)
            if errors:
                fail("L3 plan audit failed:\n" + "\n".join(f"  - {error}" for error in errors))
            output = render_l3(data, plan)
        else:
            output = render(data)
    except (OSError, json.JSONDecodeError, ValueError) as error:
        parser.exit(1, f"ERROR: {error}\n")

    if not args.audit_only:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output, encoding="utf-8")
    contract = data["coordinateContract"]
    destination = "audit only" if args.audit_only else f"wrote {args.output.resolve()}"
    if plan is None:
        print(
            f"Audited {data['siteId']} measurement v{data['version']}; "
            f"grid={contract['pixelsPerVoxel']}px/source-cell, "
            f"landmarks={len(data['registrationLandmarks'])}, "
            f"components={len(data['components'])}; {destination}"
        )
    else:
        print(
            f"Audited {data['siteId']} strict L3 plan v{plan['version']}; "
            f"structures={metrics['structures']}, projectionCells={metrics['structureProjectionCells']}, "
            f"siteTerrainCells={metrics['siteOwnedTerrainCells']}, stairs={metrics['stairs']}, "
            f"rubble={metrics['rubbleClusters']}, coverage="
            f"{metrics['measuredTerrainRegions']} terrain/{metrics['measuredComponents']} components, "
            f"treeExclusions={metrics['treeExclusions']}, trees={metrics['treeAnchors']}; {destination}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
