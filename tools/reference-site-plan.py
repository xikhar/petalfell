#!/usr/bin/env python3
"""Audit and render one authoritative reference-site ground plan as SVG.

This is deliberately a plan viewer, not a ruin generator. It never chooses,
moves, mirrors or stamps geometry. Version 2 also proves the two facts that the
first blockout failed to prove: one authored cell reaches one runtime voxel, and
every stair physically joins the two named terrain levels.
"""

from __future__ import annotations

import argparse
import html
import json
from collections import deque
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
PLAN_DIR = ROOT / "content" / "chapter_01" / "sites"
Cell = tuple[int, int]


def resolve_plan(target: str) -> Path:
    candidate = Path(target)
    if candidate.is_file():
        return candidate.resolve()
    for path in sorted(PLAN_DIR.glob("*-plan.json")):
        data = json.loads(path.read_text(encoding="utf-8"))
        if data.get("siteId") == target:
            return path
    raise SystemExit(f"no authored ground plan found for '{target}'")


def valid_int(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def rect_size(rect: list[int]) -> tuple[int, int]:
    if len(rect) != 4 or not all(valid_int(value) for value in rect):
        raise ValueError(f"rectangle must contain four integer x0,z0,x1,z1 values: {rect!r}")
    if rect[0] > rect[2] or rect[1] > rect[3]:
        raise ValueError(f"rectangle bounds must be normalized x0<=x1,z0<=z1: {rect!r}")
    return rect[2] - rect[0] + 1, rect[3] - rect[1] + 1


def rect_cells(rect: list[int]) -> set[Cell]:
    rect_size(rect)
    x0, z0, x1, z1 = rect
    return {
        (x, z)
        for z in range(min(z0, z1), max(z0, z1) + 1)
        for x in range(min(x0, x1), max(x0, x1) + 1)
    }


def inside_polygon(x: float, z: float, vertices: list[list[int]]) -> bool:
    inside = False
    j = len(vertices) - 1
    for i, a in enumerate(vertices):
        b = vertices[j]
        if (a[1] > z) != (b[1] > z):
            crossing_x = (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]
            if x < crossing_x:
                inside = not inside
        j = i
    return inside


def polygon_cells(vertices: list[list[int]]) -> set[Cell]:
    if len(vertices) < 3:
        raise ValueError("polygon needs at least three vertices")
    if any(len(point) != 2 or not all(valid_int(value) for value in point) for point in vertices):
        raise ValueError(f"polygon vertices must be integer x,z pairs: {vertices!r}")
    min_x = min(point[0] for point in vertices)
    max_x = max(point[0] for point in vertices)
    min_z = min(point[1] for point in vertices)
    max_z = max(point[1] for point in vertices)
    return {
        (x, z)
        for z in range(min_z, max_z + 1)
        for x in range(min_x, max_x + 1)
        if inside_polygon(x + 0.5, z + 0.5, vertices)
    }


def shape_cells(item: dict) -> set[Cell]:
    if "footprint" in item:
        return rect_cells(item["footprint"])
    if "polygon" in item:
        return polygon_cells(item["polygon"])
    raise ValueError("shape needs either footprint or polygon")


def touches(a: set[Cell], b: set[Cell]) -> bool:
    if a & b:
        return True
    return any(
        (x + dx, z + dz) in b
        for x, z in a
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1))
    )


def connected(cells: set[Cell]) -> bool:
    if not cells:
        return False
    remaining = set(cells)
    queue = deque([remaining.pop()])
    while queue:
        x, z = queue.popleft()
        for neighbour in ((x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)):
            if neighbour in remaining:
                remaining.remove(neighbour)
                queue.append(neighbour)
    return not remaining


def audit(data: dict) -> list[str]:
    errors: list[str] = []
    if data.get("version") != 2:
        errors.append("ground-plan version must be 2")
    if not data.get("siteId"):
        errors.append("siteId is required")
    contract = data.get("coordinateContract", {})
    if contract.get("oneCellIsOneVoxel") is not True:
        errors.append("coordinateContract.oneCellIsOneVoxel must be true")
    if contract.get("runtimePlanScale") != 1:
        errors.append("coordinateContract.runtimePlanScale must be exactly 1")
    if contract.get("runtimeMirrorX") is not True:
        errors.append("coordinateContract.runtimeMirrorX must explicitly preserve the solved source-facing orientation")
    if contract.get("plotView") != "source-facing":
        errors.append("coordinateContract.plotView must be 'source-facing'")

    terrain_by_id: dict[str, dict] = {}
    raw_terrain_cells: dict[str, set[Cell]] = {}
    owner: dict[Cell, str] = {}
    for terrain in data.get("terrain", []):
        terrain_id = terrain.get("id", "")
        if not terrain_id:
            errors.append("every terrain shape needs an id")
            continue
        if terrain_id in terrain_by_id:
            errors.append(f"duplicate terrain id '{terrain_id}'")
            continue
        if not valid_int(terrain.get("surfaceY")):
            errors.append(f"terrain '{terrain_id}' needs an integer surfaceY")
        try:
            cells = shape_cells(terrain)
        except ValueError as exc:
            errors.append(f"terrain '{terrain_id}': {exc}")
            continue
        if not cells:
            errors.append(f"terrain '{terrain_id}' covers no cells")
        terrain_by_id[terrain_id] = terrain
        raw_terrain_cells[terrain_id] = cells
        for cell in cells:
            owner[cell] = terrain_id

    visible_terrain_cells = {
        terrain_id: {cell for cell, owner_id in owner.items() if owner_id == terrain_id}
        for terrain_id in terrain_by_id
    }
    seen: set[str] = set(terrain_by_id)
    patch_owner: dict[Cell, str] = {}
    for patch in data.get("surfacePatches", []):
        patch_id = patch.get("id", "")
        if not patch_id:
            errors.append("every surface patch needs an id")
            continue
        if patch_id in seen:
            errors.append(f"duplicate authored id '{patch_id}'")
        seen.add(patch_id)
        terrain_id = patch.get("terrainId")
        if terrain_id not in terrain_by_id:
            errors.append(f"surface patch '{patch_id}' names missing terrain '{terrain_id}'")
        if not patch.get("material"):
            errors.append(f"surface patch '{patch_id}' needs a material")
        footprints = patch.get("footprints", [])
        if not footprints:
            errors.append(f"surface patch '{patch_id}' needs at least one footprint")
        cells: set[Cell] = set()
        for index, footprint in enumerate(footprints, start=1):
            try:
                footprint_cells = rect_cells(footprint)
            except ValueError as exc:
                errors.append(f"surface patch '{patch_id}' footprint {index}: {exc}")
                continue
            overlap = cells & footprint_cells
            if overlap:
                errors.append(
                    f"surface patch '{patch_id}' has overlapping footprints at {min(overlap)}"
                )
            cells |= footprint_cells
        if terrain_id in visible_terrain_cells:
            outside = cells - visible_terrain_cells[terrain_id]
            if outside:
                errors.append(
                    f"surface patch '{patch_id}' leaves visible terrain '{terrain_id}' at {min(outside)}"
                )
        for cell in cells:
            if cell in patch_owner:
                errors.append(
                    f"surface patches '{patch_owner[cell]}' and '{patch_id}' overlap at {cell}"
                )
            else:
                patch_owner[cell] = patch_id

    limits = data.get("acceptanceRules", {}).get(
        "isolatedSurvivorMaximumFootprint", [2, 2]
    )
    thin_wall_limit = data.get("acceptanceRules", {}).get("thinWallMaximumWidth", 4)
    for structure in data.get("structures", []):
        structure_id = structure.get("id", "")
        if not structure_id:
            errors.append("every structure needs an id")
            continue
        if structure_id in seen:
            errors.append(f"duplicate authored id '{structure_id}'")
        seen.add(structure_id)
        kind = structure.get("kind")

        if kind == "stair":
            from_id = structure.get("fromTerrain")
            to_id = structure.get("toTerrain")
            if from_id not in terrain_by_id or to_id not in terrain_by_id:
                errors.append(
                    f"stair '{structure_id}' must name existing fromTerrain and toTerrain"
                )
                continue
            axis = structure.get("axis")
            if axis not in ([1, 0], [-1, 0], [0, 1], [0, -1]):
                errors.append(f"stair '{structure_id}' axis must be one cardinal integer vector")
            try:
                from_landing = rect_cells(structure.get("fromLanding", []))
                to_landing = rect_cells(structure.get("toLanding", []))
            except ValueError as exc:
                errors.append(f"stair '{structure_id}' landing: {exc}")
                from_landing, to_landing = set(), set()
            from_terrain_cells = visible_terrain_cells.get(from_id, set())
            to_terrain_cells = visible_terrain_cells.get(to_id, set())
            outside_from = from_landing - from_terrain_cells
            outside_to = to_landing - to_terrain_cells
            if outside_from:
                errors.append(
                    f"stair '{structure_id}' fromLanding leaves '{from_id}' at {min(outside_from)}"
                )
            if outside_to:
                errors.append(
                    f"stair '{structure_id}' toLanding leaves '{to_id}' at {min(outside_to)}"
                )
            treads = structure.get("treads", [])
            if len(treads) < 2:
                errors.append(f"stair '{structure_id}' needs at least two authored treads")
                continue
            tread_cells: list[set[Cell]] = []
            tread_tops: list[int] = []
            for index, tread in enumerate(treads, start=1):
                try:
                    cells = rect_cells(tread.get("footprint", []))
                except ValueError as exc:
                    errors.append(f"stair '{structure_id}' tread {index}: {exc}")
                    continue
                top_y = tread.get("topY")
                if not valid_int(top_y):
                    errors.append(f"stair '{structure_id}' tread {index} needs integer topY")
                    continue
                tread_cells.append(cells)
                tread_tops.append(top_y)
            if len(tread_cells) != len(treads):
                continue
            for index in range(1, len(tread_cells)):
                if not touches(tread_cells[index - 1], tread_cells[index]):
                    errors.append(
                        f"stair '{structure_id}' treads {index} and {index + 1} do not touch"
                    )
                rise = tread_tops[index] - tread_tops[index - 1]
                if rise not in (0, 1):
                    errors.append(
                        f"stair '{structure_id}' rise {index}->{index + 1} is {rise}; expected 0 or 1"
                    )
            from_cells = visible_terrain_cells.get(from_id, set())
            to_cells = visible_terrain_cells.get(to_id, set())
            if from_landing and not touches(from_landing, tread_cells[0]):
                errors.append(f"stair '{structure_id}' first tread does not touch fromLanding")
            if to_landing and not touches(tread_cells[-1], to_landing):
                errors.append(f"stair '{structure_id}' last tread does not touch toLanding")
            if not touches(tread_cells[0], from_cells):
                errors.append(f"stair '{structure_id}' does not touch fromTerrain '{from_id}'")
            if not touches(tread_cells[-1], to_cells):
                errors.append(f"stair '{structure_id}' does not touch toTerrain '{to_id}'")
            if tread_tops[0] != terrain_by_id[from_id].get("surfaceY"):
                errors.append(f"stair '{structure_id}' first topY does not equal '{from_id}' surfaceY")
            if tread_tops[-1] != terrain_by_id[to_id].get("surfaceY"):
                errors.append(f"stair '{structure_id}' last topY does not equal '{to_id}' surfaceY")
            continue

        support_id = structure.get("supportTerrain")
        if support_id not in terrain_by_id:
            errors.append(
                f"structure '{structure_id}' must name an existing supportTerrain"
            )
        base_y = structure.get("baseY")
        if not valid_int(base_y):
            errors.append(f"structure '{structure_id}' needs an integer baseY")
        elif support_id in terrain_by_id and base_y != terrain_by_id[support_id].get("surfaceY"):
            errors.append(
                f"structure '{structure_id}' baseY {base_y} does not match "
                f"'{support_id}' surfaceY {terrain_by_id[support_id].get('surfaceY')}"
            )

        if kind == "rubble-cluster":
            try:
                envelope_cells = rect_cells(structure.get("envelope", []))
            except ValueError as exc:
                errors.append(f"rubble cluster '{structure_id}' envelope: {exc}")
                continue
            cells: set[Cell] = set()
            for index, point in enumerate(structure.get("cells", []), start=1):
                if (
                    not isinstance(point, list)
                    or len(point) != 2
                    or not all(valid_int(value) for value in point)
                ):
                    errors.append(
                        f"rubble cluster '{structure_id}' cell {index} must be integer [x,z]"
                    )
                    continue
                cell = (point[0], point[1])
                if cell in cells:
                    errors.append(f"rubble cluster '{structure_id}' repeats cell {cell}")
                cells.add(cell)
            if not cells:
                errors.append(f"rubble cluster '{structure_id}' needs exact cells")
            outside_envelope = cells - envelope_cells
            if outside_envelope:
                errors.append(
                    f"rubble cluster '{structure_id}' leaves its envelope at {min(outside_envelope)}"
                )
            if cells and not connected(cells):
                errors.append(f"rubble cluster '{structure_id}' cells are not 4-connected")
            if support_id in visible_terrain_cells:
                outside_support = cells - visible_terrain_cells[support_id]
                if outside_support:
                    errors.append(
                        f"rubble cluster '{structure_id}' leaves supportTerrain "
                        f"'{support_id}' at {min(outside_support)}"
                    )
            continue

        rectangles: list[list[int]] = []
        if "footprint" in structure:
            rectangles.append(structure["footprint"])
        rectangles.extend(structure.get("footprints", []))
        if not rectangles:
            errors.append(f"structure '{structure_id}' needs a footprint")
            continue
        cells: set[Cell] = set()
        for rect in rectangles:
            try:
                width, depth = rect_size(rect)
                cells |= rect_cells(rect)
            except ValueError as exc:
                errors.append(f"structure '{structure_id}': {exc}")
                continue
            if kind == "isolated-survivor" and (width > limits[0] or depth > limits[1]):
                errors.append(
                    f"isolated survivor '{structure_id}' is {width}x{depth}; "
                    f"maximum is {limits[0]}x{limits[1]}"
                )
            if kind == "connected-wall-run" and min(width, depth) > thin_wall_limit:
                errors.append(
                    f"wall segment in '{structure_id}' is {width}x{depth}; "
                    f"thin dimension may not exceed {thin_wall_limit}"
                )
        if support_id in visible_terrain_cells:
            outside_support = cells - visible_terrain_cells[support_id]
            if outside_support:
                errors.append(
                    f"structure '{structure_id}' leaves supportTerrain '{support_id}' "
                    f"at {min(outside_support)}"
                )
        if kind in (
            "connected-wall-run",
            "connected-facade",
            "connected-stair-shoulder",
        ) and not connected(cells):
            errors.append(f"connected structure '{structure_id}' contains disconnected footprints")
    return errors


def plot_x(x: float, x_sign: int) -> float:
    return x * x_sign


def points(vertices: Iterable[list[int]], scale: float, x_sign: int = 1) -> str:
    return " ".join(
        f"{plot_x(x, x_sign) * scale:.2f},{-z * scale:.2f}" for x, z in vertices
    )


def svg_rect(
    rect: list[int], scale: float, css: str, label: str = "", x_sign: int = 1
) -> str:
    x0, z0, x1, z1 = rect
    if x_sign < 0:
        x0, x1 = -x1, -x0
    left = min(x0, x1) * scale
    top = -max(z0, z1) * scale
    width = (abs(x1 - x0) + 1) * scale
    height = (abs(z1 - z0) + 1) * scale
    title = f"<title>{html.escape(label)}</title>" if label else ""
    return (
        f'<rect class="{css}" x="{left:.2f}" y="{top:.2f}" '
        f'width="{width:.2f}" height="{height:.2f}">{title}</rect>'
    )


def item_center(item: dict) -> tuple[float, float]:
    if "footprint" in item:
        x0, z0, x1, z1 = item["footprint"]
        return (x0 + x1 + 1) / 2, (z0 + z1 + 1) / 2
    vertices = item["polygon"]
    return (
        sum(point[0] for point in vertices) / len(vertices),
        sum(point[1] for point in vertices) / len(vertices),
    )


def render(data: dict, runtime_facing: bool = False) -> str:
    width, height = 1200, 930
    cx, cy = width / 2, 430
    scale = 6.0
    contract = data.get("coordinateContract", {})
    x_sign = -1 if runtime_facing and contract.get("runtimeMirrorX") is True else 1
    player = data.get("coordinateContract", {}).get("playerSpawn", {"x": 0, "z": 0})
    player_x = plot_x(player.get("x", 0), x_sign) * scale
    player_y = -player.get("z", 0) * scale
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
        "<style>",
        "text{font-family:Inter,ui-sans-serif,system-ui,sans-serif;fill:#3f3947}",
        ".title{font-size:25px;font-weight:750}.subtitle{font-size:14px;fill:#6a6272}",
        ".grid{stroke:#655e6d;stroke-opacity:.11;stroke-width:1}.axis{stroke:#514a59;stroke-opacity:.42;stroke-width:1.5}",
        ".terrain-surrounding-terrain{fill:#f5f4ef;stroke:#36a35a;stroke-width:2.2}",
        ".terrain-eroded-platform-shelf{fill:#d8dbaa;stroke:#87915c;stroke-width:1.35}",
        ".terrain-local-terrace-stack{fill:#d8dbaa;stroke:#87915c;stroke-width:1.35}",
        ".terrain-detached-terrain-blocks{fill:#cbd08f;stroke:#78834d;stroke-width:1.25}",
        ".terrain-intermittent-lower-apron{fill:#d2d5a0;stroke:#818a58;stroke-width:1.25}",
        ".terrain-lower-court{fill:#dfe3e6;stroke:#7e868c;stroke-width:1.7}",
        ".terrain-raised-platform{fill:#eadcf0;stroke:#d45be3;stroke-width:1.8}",
        ".surface-patch{stroke-width:.8;stroke-opacity:.65}",
        ".surface-worn-paving{fill:#f2e8e8;stroke:#9f8797}",
        ".surface-warm-paving{fill:#e6d1b5;stroke:#a28168}",
        ".surface-cool-paving{fill:#c9c3db;stroke:#817994}",
        ".surface-moss-paving{fill:#a9b58a;stroke:#6f7f59}",
        ".surface-reclaimed-turf{fill:#cbd19d;stroke:#788557}",
        ".stair{fill:#f3d6d1;stroke:#ed7268;stroke-width:1.4}",
        ".connected-facade,.connected-wall-run,.connected-stair-shoulder{fill:#dcebf5;stroke:#4ca5e8;stroke-width:1.45}",
        ".isolated-survivor{fill:#eef6fb;stroke:#277fbf;stroke-width:1.6}",
        ".rubble-cluster{fill:#c9b9d0;stroke:#695d73;stroke-width:.7}",
        ".tree{fill:#efb4cc;fill-opacity:.55;stroke:#713b58;stroke-width:1.2}",
        ".player{fill:#1c6c9c;stroke:#123f59;stroke-width:1.3}",
        ".label{font-size:12px;font-weight:700;paint-order:stroke;stroke:#faf9f5;stroke-width:3px}",
        ".zone{font-size:13px;font-weight:750;paint-order:stroke;stroke:#faf9f5;stroke-width:4px}",
        ".legend{font-size:13px}.rule{font-size:13px;font-weight:700;fill:#554b5d}",
        "</style>",
        '<rect width="1200" height="930" fill="#faf9f5"/>',
        f'<text class="title" x="36" y="42">{html.escape(data["siteId"])} — canonical top view</text>',
        f'<text class="subtitle" x="36" y="67">{"Runtime-facing mirrored footprint" if runtime_facing else "Overhead source-facing footprint"} · one square = one runtime voxel · north is up</text>',
        f'<g transform="translate({cx:.1f} {cy:.1f})">',
    ]
    for value in range(-80, 81, 10):
        css = "axis" if value == 0 else "grid"
        parts.append(f'<line class="{css}" x1="{-80 * scale}" y1="{-value * scale}" x2="{80 * scale}" y2="{-value * scale}"/>')
        parts.append(f'<line class="{css}" x1="{value * scale}" y1="{-70 * scale}" x2="{value * scale}" y2="{70 * scale}"/>')

    for terrain in data.get("terrain", []):
        css = f'terrain-{html.escape(terrain.get("role", "surrounding-terrain"))}'
        label = f'{terrain["id"]} at y={terrain["surfaceY"]}'
        if "polygon" in terrain:
            parts.append(f'<polygon class="{css}" points="{points(terrain["polygon"], scale, x_sign)}"><title>{html.escape(label)}</title></polygon>')
        else:
            parts.append(svg_rect(terrain["footprint"], scale, css, label, x_sign))

    for patch in data.get("surfacePatches", []):
        css = f'surface-patch surface-{html.escape(patch.get("material", "worn-paving"))}'
        for footprint in patch.get("footprints", []):
            parts.append(
                svg_rect(footprint, scale, css, patch.get("id", "surface patch"), x_sign)
            )

    for structure in data.get("structures", []):
        kind = structure.get("kind", "structure")
        structure_id = structure.get("id", "structure")
        if kind == "stair":
            for index, tread in enumerate(structure.get("treads", []), start=1):
                parts.append(svg_rect(tread["footprint"], scale, "stair", f'{structure_id}: tread {index}, top y={tread["topY"]}', x_sign))
        elif kind == "rubble-cluster":
            for cell in structure.get("cells", []):
                parts.append(
                    svg_rect([cell[0], cell[1], cell[0], cell[1]], scale,
                             "rubble-cluster", structure_id, x_sign)
                )
        else:
            rectangles = ([structure["footprint"]] if "footprint" in structure else []) + structure.get("footprints", [])
            for footprint in rectangles:
                parts.append(svg_rect(footprint, scale, kind, structure_id, x_sign))

    unlabeled_terrain_roles = {
        "surrounding-terrain",
        "eroded-platform-shelf",
        "local-terrace-stack",
        "detached-terrain-blocks",
        "intermittent-lower-apron",
    }
    for terrain in data.get("terrain", []):
        if terrain.get("role") in unlabeled_terrain_roles:
            continue
        x, z = item_center(terrain)
        parts.append(f'<text class="zone" text-anchor="middle" x="{plot_x(x, x_sign) * scale:.2f}" y="{-z * scale:.2f}">{html.escape(terrain["id"].upper().replace("-", " "))}</text>')

    for x, z in data.get("surroundingTrees", []):
        parts.append(f'<circle class="tree" cx="{plot_x(x, x_sign) * scale}" cy="{-z * scale}" r="{2.4 * scale}"><title>blossom tree {x},{z}</title></circle>')

    parts.extend([
        f'<circle class="player" cx="{player_x}" cy="{player_y}" r="8"><title>player spawn</title></circle>',
        f'<path class="player" d="M{player_x} {player_y - 18} L{player_x - 7} {player_y - 5} L{player_x + 7} {player_y - 5} Z"/>',
        f'<text class="label" x="{player_x + 12}" y="{player_y + 18}">PLAYER {player.get("x", 0)},{player.get("z", 0)}</text>',
        '</g>',
        '<g transform="translate(38 810)">',
        '<rect class="terrain-surrounding-terrain" x="0" y="0" width="22" height="22"/><text class="legend" x="31" y="16">surrounding terrain</text>',
        '<rect class="terrain-local-terrace-stack" x="190" y="0" width="22" height="22"/><text class="legend" x="221" y="16">local block terraces</text>',
        '<rect class="terrain-lower-court" x="440" y="0" width="22" height="22"/><text class="legend" x="471" y="16">lower court y109</text>',
        '<rect class="terrain-raised-platform" x="620" y="0" width="22" height="22"/><text class="legend" x="651" y="16">raised platforms y114</text>',
        '<rect class="connected-wall-run" x="835" y="0" width="22" height="22"/><text class="legend" x="866" y="16">thin ruin runs</text>',
        '<rect class="stair" x="1010" y="0" width="22" height="22"/><text class="legend" x="1041" y="16">stairs</text>',
        '<text class="rule" x="0" y="58">Audit: exact 1:1 grid; both stair endpoints and heights join named terrain; ordinary wall runs stay at most two voxels thick.</text>',
        '<text class="subtitle" x="0" y="85">The preview draws authored data only. Runtime geometry may weather these masses, but may not move or add them.</text>',
        '</g>',
        '</svg>',
    ])
    return "\n".join(parts)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("target", help="site id or plan JSON path")
    parser.add_argument("output", nargs="?", help="SVG output path")
    parser.add_argument("--audit-only", action="store_true")
    parser.add_argument(
        "--runtime-facing",
        action="store_true",
        help="mirror the source-facing plot exactly as the runtime coordinate contract does",
    )
    args = parser.parse_args()
    plan_path = resolve_plan(args.target)
    data = json.loads(plan_path.read_text(encoding="utf-8"))
    errors = audit(data)
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return 1
    print(f"Ground plan audit passed for '{data['siteId']}' ({len(data.get('structures', []))} structures).")
    if args.audit_only:
        return 0
    suffix = "-runtime-facing" if args.runtime_facing else ""
    output = (
        Path(args.output)
        if args.output
        else ROOT.parent / "shots" / f"site-plan-{data['siteId']}{suffix}.svg"
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(render(data, runtime_facing=args.runtime_facing), encoding="utf-8")
    print(output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
