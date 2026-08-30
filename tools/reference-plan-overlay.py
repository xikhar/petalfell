#!/usr/bin/env python3
"""Render the audited Bloom Grove Court plan over its 1254 px source frame.

The output has no background. Every source-plan coordinate uses the solved
overhead calibration exactly:

    u = 555 + 10 * x
    v = 646 - 10 * z

Terrain polygons are traced boundaries. Inclusive structure rectangles and
rubble cells are voxel centres, so their visible projection extends half a
voxel around each calibrated coordinate.
"""

from __future__ import annotations

import argparse
import html
import json
import re
import runpy
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
PLAN_PATH = (
    ROOT
    / "content"
    / "chapter_01"
    / "sites"
    / "bloom-grove-court-reference-10-plan.json"
)
AUDIT_TOOL = ROOT / "tools" / "reference-site-plan.py"

WIDTH = 1254
HEIGHT = 1254
ORIGIN_U = 555.0
ORIGIN_V = 646.0
VOXEL_PIXELS = 10.0


def source_u(x: float) -> float:
    return ORIGIN_U + VOXEL_PIXELS * x


def source_v(z: float) -> float:
    return ORIGIN_V - VOXEL_PIXELS * z


def number(value: float) -> str:
    if value.is_integer():
        return str(int(value))
    return f"{value:.3f}".rstrip("0").rstrip(".")


def safe_id(*parts: object) -> str:
    raw = "-".join(str(part) for part in parts)
    identifier = re.sub(r"[^A-Za-z0-9_.-]+", "-", raw).strip("-")
    if not identifier or not identifier[0].isalpha():
        identifier = f"item-{identifier}"
    return identifier


def title(value: str) -> str:
    return f"<title>{html.escape(value)}</title>"


def polygon_points(vertices: Iterable[list[int]]) -> str:
    return " ".join(
        f"{number(source_u(float(x)))},{number(source_v(float(z)))}"
        for x, z in vertices
    )


def voxel_rect_geometry(rect: list[int]) -> tuple[float, float, float, float]:
    """Map inclusive voxel-centre bounds to one exact source-pixel rectangle."""
    x0, z0, x1, z1 = rect
    half = VOXEL_PIXELS / 2.0
    left = source_u(float(x0)) - half
    top = source_v(float(z1)) - half
    width = (x1 - x0 + 1) * VOXEL_PIXELS
    height = (z1 - z0 + 1) * VOXEL_PIXELS
    return left, top, width, height


def voxel_rect(
    rect: list[int],
    css_class: str,
    element_id: str,
    stable_title: str,
) -> str:
    left, top, width, height = voxel_rect_geometry(rect)
    return (
        f'<rect id="{html.escape(element_id, quote=True)}" '
        f'class="{html.escape(css_class, quote=True)}" '
        f'x="{number(left)}" y="{number(top)}" '
        f'width="{number(width)}" height="{number(height)}">'
        f"{title(stable_title)}</rect>"
    )


def load_audited_plan() -> dict:
    data = json.loads(PLAN_PATH.read_text(encoding="utf-8"))
    if data.get("version") != 2:
        raise ValueError(f"expected plan version 2, found {data.get('version')!r}")
    if data.get("siteId") != "bloom-grove-court":
        raise ValueError(
            "overlay source must be the audited 'bloom-grove-court' plan"
        )

    audit_namespace = runpy.run_path(
        str(AUDIT_TOOL), run_name="reference_plan_overlay_audit"
    )
    errors = audit_namespace["audit"](data)
    if errors:
        detail = "\n".join(f"  - {error}" for error in errors)
        raise ValueError(f"ground-plan audit failed:\n{detail}")
    return data


def render_terrain(data: dict) -> list[str]:
    parts: list[str] = ['<g id="terrain-layer">']
    for terrain in data.get("terrain", []):
        terrain_id = terrain["id"]
        role = terrain.get("role", "surrounding-terrain")
        element_id = safe_id("terrain", terrain_id)
        stable_title = (
            f"terrain/{terrain_id}; surfaceY={terrain['surfaceY']}; "
            f"material={terrain.get('material', 'unspecified')}"
        )
        if "polygon" in terrain:
            parts.append(
                f'<polygon id="{element_id}" class="terrain terrain-{safe_id(role)}" '
                f'points="{polygon_points(terrain["polygon"])}">'
                f"{title(stable_title)}</polygon>"
            )
        else:
            parts.append(
                voxel_rect(
                    terrain["footprint"],
                    f"terrain terrain-{safe_id(role)}",
                    element_id,
                    stable_title,
                )
            )
    parts.append("</g>")
    return parts


def render_surface_patches(data: dict) -> list[str]:
    parts: list[str] = ['<g id="surface-patch-layer">']
    for patch in data.get("surfacePatches", []):
        patch_id = patch["id"]
        css_class = f"surface-patch surface-{safe_id(patch.get('material', 'other'))}"
        group_id = safe_id("surface", patch_id)
        parts.append(
            f'<g id="{group_id}">{title(f"surface/{patch_id}")}'
        )
        for index, footprint in enumerate(patch.get("footprints", []), start=1):
            stable_title = (
                f"surface/{patch_id}/footprint-{index:02d}; "
                f"terrain={patch.get('terrainId', 'unspecified')}; "
                f"material={patch.get('material', 'unspecified')}"
            )
            parts.append(
                voxel_rect(
                    footprint,
                    css_class,
                    safe_id(group_id, f"footprint-{index:02d}"),
                    stable_title,
                )
            )
        parts.append("</g>")
    parts.append("</g>")
    return parts


def render_stair(structure: dict) -> list[str]:
    structure_id = structure["id"]
    group_id = safe_id("structure", structure_id)
    parts = [
        f'<g id="{group_id}" class="stair-group">'
        f'{title(f"structure/{structure_id}; kind=stair")}'
    ]
    for index, tread in enumerate(structure.get("treads", []), start=1):
        tread_id = safe_id(group_id, f"tread-{index:02d}")
        top_y = tread["topY"]
        stable_title = (
            f"structure/{structure_id}/tread-{index:02d}; topY={top_y}"
        )
        parts.append(voxel_rect(tread["footprint"], "stair", tread_id, stable_title))
        left, top, width, height = voxel_rect_geometry(tread["footprint"])
        parts.append(
            f'<text id="{tread_id}-label" class="tread-label" '
            f'x="{number(left + width / 2.0)}" '
            f'y="{number(top + height / 2.0)}">'
            f'{title(stable_title + "/label")}y{top_y}</text>'
        )
    parts.append("</g>")
    return parts


def render_rubble(structure: dict) -> list[str]:
    structure_id = structure["id"]
    group_id = safe_id("structure", structure_id)
    parts = [
        f'<g id="{group_id}" class="rubble-group">'
        f'{title(f"structure/{structure_id}; kind=rubble-cluster")}'
    ]
    for index, (x, z) in enumerate(structure.get("cells", []), start=1):
        stable_title = (
            f"structure/{structure_id}/cell-{index:02d}; x={x}; z={z}"
        )
        parts.append(
            voxel_rect(
                [x, z, x, z],
                "rubble-cell",
                safe_id(group_id, f"cell-{index:02d}"),
                stable_title,
            )
        )
    parts.append("</g>")
    return parts


def render_wall_projection(structure: dict) -> list[str]:
    structure_id = structure["id"]
    kind = structure.get("kind", "structure")
    group_id = safe_id("structure", structure_id)
    css_class = f"structure-projection structure-{safe_id(kind)}"
    rectangles = []
    if "footprint" in structure:
        rectangles.append(structure["footprint"])
    rectangles.extend(structure.get("footprints", []))
    parts = [
        f'<g id="{group_id}" class="structure-group">'
        f'{title(f"structure/{structure_id}; kind={kind}")}'
    ]
    for index, footprint in enumerate(rectangles, start=1):
        stable_title = (
            f"structure/{structure_id}/footprint-{index:02d}; kind={kind}; "
            f"baseY={structure.get('baseY', 'unspecified')}; "
            f"height={structure.get('height', 'unspecified')}"
        )
        parts.append(
            voxel_rect(
                footprint,
                css_class,
                safe_id(group_id, f"footprint-{index:02d}"),
                stable_title,
            )
        )
    parts.append("</g>")
    return parts


def render_structures(data: dict) -> list[str]:
    parts: list[str] = ['<g id="structure-layer">']
    for structure in data.get("structures", []):
        kind = structure.get("kind")
        if kind == "stair":
            parts.extend(render_stair(structure))
        elif kind == "rubble-cluster":
            parts.extend(render_rubble(structure))
        else:
            parts.extend(render_wall_projection(structure))
    parts.append("</g>")
    return parts


def render_tree_anchors(data: dict) -> list[str]:
    parts: list[str] = ['<g id="tree-anchor-layer">']
    for index, (x, z) in enumerate(data.get("surroundingTrees", []), start=1):
        anchor_id = safe_id("tree-anchor", f"{index:02d}")
        cx = source_u(float(x))
        cy = source_v(float(z))
        stable_title = f"tree-anchor/{index:02d}; x={x}; z={z}"
        parts.append(
            f'<g id="{anchor_id}" class="tree-anchor">{title(stable_title)}'
            f'<circle cx="{number(cx)}" cy="{number(cy)}" r="13"/>'
            f'<path d="M {number(cx - 6)} {number(cy)} H {number(cx + 6)} '
            f'M {number(cx)} {number(cy - 6)} V {number(cy + 6)}"/>'
            "</g>"
        )
    parts.append("</g>")
    return parts


def render_player(data: dict) -> list[str]:
    spawn = data["coordinateContract"]["playerSpawn"]
    x = spawn["x"]
    z = spawn["z"]
    cx = source_u(float(x))
    cy = source_v(float(z))
    stable_title = f"player-spawn; x={x}; z={z}"
    return [
        f'<g id="player-spawn" class="player">{title(stable_title)}',
        f'<circle cx="{number(cx)}" cy="{number(cy)}" r="8"/>',
        f'<path d="M {number(cx)} {number(cy - 15)} '
        f'L {number(cx - 7)} {number(cy - 4)} '
        f'L {number(cx + 7)} {number(cy - 4)} Z"/>',
        "</g>",
    ]


def render(data: dict) -> str:
    parts = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{WIDTH}" '
        f'height="{HEIGHT}" viewBox="0 0 {WIDTH} {HEIGHT}" '
        'style="background:transparent" '
        'data-calibration="u=555+10*x;v=646-10*z">',
        "<title>Bloom Grove Court reference-plan source-pixel overlay</title>",
        "<desc>Transparent audited v2 plan overlay for world-new/reference-10-top.png.</desc>",
        "<metadata>",
        json.dumps(
            {
                "siteId": data["siteId"],
                "planVersion": data["version"],
                "sourceWidth": WIDTH,
                "sourceHeight": HEIGHT,
                "calibration": {
                    "u": "555 + 10*x",
                    "v": "646 - 10*z",
                    "voxelPixels": VOXEL_PIXELS,
                },
            },
            separators=(",", ":"),
        ),
        "</metadata>",
        "<style>",
        ".terrain{stroke-linejoin:round;vector-effect:non-scaling-stroke}",
        ".terrain-surrounding-terrain{fill:#cce8a6;fill-opacity:.10;stroke:#2f9f52;stroke-opacity:.86;stroke-width:2}",
        ".terrain-eroded-platform-shelf{fill:#b7c36f;fill-opacity:.22;stroke:#6f7d3f;stroke-opacity:.88;stroke-width:1.25}",
        ".terrain-local-terrace-stack{fill:#b7c36f;fill-opacity:.22;stroke:#6f7d3f;stroke-opacity:.88;stroke-width:1.25}",
        ".terrain-detached-terrain-blocks{fill:#a8b95d;fill-opacity:.28;stroke:#657437;stroke-opacity:.9;stroke-width:1.15}",
        ".terrain-intermittent-lower-apron{fill:#b2bf68;fill-opacity:.24;stroke:#6c783d;stroke-opacity:.9;stroke-width:1.15}",
        ".terrain-lower-court{fill:#cfd5dc;fill-opacity:.20;stroke:#53616e;stroke-opacity:.92;stroke-width:1.8}",
        ".terrain-raised-platform{fill:#dbbde6;fill-opacity:.18;stroke:#c440d5;stroke-opacity:.92;stroke-width:1.8}",
        ".surface-patch{stroke-width:.8;stroke-opacity:.74;vector-effect:non-scaling-stroke}",
        ".surface-worn-paving{fill:#fff2e6;fill-opacity:.28;stroke:#9d788b}",
        ".surface-warm-paving{fill:#e5bb79;fill-opacity:.28;stroke:#9d6a3a}",
        ".surface-cool-paving{fill:#aaa5cc;fill-opacity:.32;stroke:#676188}",
        ".surface-moss-paving{fill:#90a061;fill-opacity:.34;stroke:#58663f}",
        ".surface-reclaimed-turf{fill:#a7b96c;fill-opacity:.24;stroke:#687945}",
        ".structure-projection{fill:#7fc8f3;fill-opacity:.42;stroke:#087ebf;stroke-opacity:.95;stroke-width:1.3;vector-effect:non-scaling-stroke}",
        ".structure-connected-facade{fill:#68bdf2;fill-opacity:.50;stroke-width:1.7}",
        ".structure-isolated-survivor{fill:#b7e3fb;fill-opacity:.58;stroke:#046ba5;stroke-width:1.6}",
        ".stair{fill:#f08b7e;fill-opacity:.44;stroke:#c74a3d;stroke-width:1.25;vector-effect:non-scaling-stroke}",
        ".tread-label{font:700 9px ui-monospace,SFMono-Regular,monospace;fill:#67281f;text-anchor:middle;dominant-baseline:middle;paint-order:stroke;stroke:#fff;stroke-opacity:.88;stroke-width:2px;pointer-events:none}",
        ".rubble-cell{fill:#7d6688;fill-opacity:.56;stroke:#4d3d57;stroke-width:.65;vector-effect:non-scaling-stroke}",
        ".tree-anchor circle{fill:#ef8fbe;fill-opacity:.18;stroke:#9d326e;stroke-width:1.4;vector-effect:non-scaling-stroke}",
        ".tree-anchor path{fill:none;stroke:#9d326e;stroke-width:1.2;vector-effect:non-scaling-stroke}",
        ".player circle,.player path{fill:#086b9f;fill-opacity:.76;stroke:#053f60;stroke-width:1.4;vector-effect:non-scaling-stroke}",
        "</style>",
    ]
    parts.extend(render_terrain(data))
    parts.extend(render_surface_patches(data))
    parts.extend(render_structures(data))
    parts.extend(render_tree_anchors(data))
    parts.extend(render_player(data))
    parts.append("</svg>")
    return "\n".join(parts) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Render the audited Bloom Grove Court v2 plan as a transparent "
            "1254x1254 source-pixel SVG overlay."
        )
    )
    parser.add_argument("output", type=Path, help="destination SVG path")
    args = parser.parse_args()

    try:
        data = load_audited_plan()
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        parser.exit(1, f"ERROR: {exc}\n")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(render(data), encoding="utf-8")
    print(
        f"Audited {data['siteId']} v{data['version']}; wrote transparent "
        f"{WIDTH}x{HEIGHT} overlay to {args.output.resolve()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
