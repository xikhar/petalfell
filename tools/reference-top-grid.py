#!/usr/bin/env python3
"""Write a voxel-coordinate overlay for an overhead reference capture.

The input image remains reference evidence, never authored map data.  This tool
only writes a transparent SVG whose cell centres can be read in the same
site-local coordinates consumed by a reference-site blueprint.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path


def pair(value: str) -> tuple[float, float]:
    left, right = value.split(",", 1)
    return float(left), float(right)


def main() -> None:
    parser = argparse.ArgumentParser(
        description=(
            "Write the calibrated one-voxel grid for "
            "world-new/reference-10-top.png."
        )
    )
    parser.add_argument("output", type=Path)
    parser.add_argument("--width", type=int, default=1254)
    parser.add_argument("--height", type=int, default=1254)
    parser.add_argument("--cell-pixels", type=float, default=10.0)
    parser.add_argument("--origin-pixel", type=pair, default=(555.0, 646.0),
                        help="pixel centre corresponding to --origin-local")
    parser.add_argument("--origin-local", type=pair, default=(0.0, 0.0))
    parser.add_argument("--major-every", type=int, default=5)
    parser.add_argument("--sample", action="append", default=[],
                        help="print local x,z for source pixel x,y")
    args = parser.parse_args()

    origin_x, origin_y = args.origin_pixel
    local_x, local_z = args.origin_local
    cell = args.cell_pixels

    for sample in args.sample:
        pixel_x, pixel_y = pair(sample)
        x = local_x + (pixel_x - origin_x) / cell
        z = local_z + (origin_y - pixel_y) / cell
        print(f"pixel {pixel_x:g},{pixel_y:g}: local {x:.2f},{z:.2f}")

    min_x = math.floor(local_x + (0.0 - origin_x) / cell) - 1
    max_x = math.ceil(local_x + (args.width - origin_x) / cell) + 1
    min_z = math.floor(local_z + (origin_y - args.height) / cell) - 1
    max_z = math.ceil(local_z + origin_y / cell) + 1

    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{args.width}" '
        f'height="{args.height}" viewBox="0 0 {args.width} {args.height}">'
    ]
    for x in range(min_x, max_x + 1):
        boundary = origin_x + (x - local_x - 0.5) * cell
        major = x % args.major_every == 0
        parts.append(
            f'<line x1="{boundary:.2f}" y1="0" x2="{boundary:.2f}" '
            f'y2="{args.height}" stroke="#{"ff3a72" if major else "218cff"}" '
            f'stroke-opacity="{0.52 if major else 0.25}" '
            f'stroke-width="{1.2 if major else 0.55}"/>')
        if major and 0 <= boundary <= args.width:
            parts.append(
                f'<text x="{boundary + 3:.2f}" y="15" fill="#7d1640" '
                f'font-family="monospace" font-size="11">x{x}</text>')

    for z in range(min_z, max_z + 1):
        boundary = origin_y - (z - local_z - 0.5) * cell
        major = z % args.major_every == 0
        parts.append(
            f'<line x1="0" y1="{boundary:.2f}" x2="{args.width}" '
            f'y2="{boundary:.2f}" stroke="#{"ff3a72" if major else "218cff"}" '
            f'stroke-opacity="{0.52 if major else 0.25}" '
            f'stroke-width="{1.2 if major else 0.55}"/>')
        if major and 0 <= boundary <= args.height:
            parts.append(
                f'<text x="3" y="{boundary - 3:.2f}" fill="#7d1640" '
                f'font-family="monospace" font-size="11">z{z}</text>')

    parts.append(
        f'<circle cx="{origin_x:.2f}" cy="{origin_y:.2f}" r="5" '
        'fill="none" stroke="#ffffff" stroke-width="2"/>')
    parts.append("</svg>")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("".join(parts), encoding="utf-8")


if __name__ == "__main__":
    main()
