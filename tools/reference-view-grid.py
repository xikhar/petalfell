#!/usr/bin/env python3
"""Write an SVG measurement grid for a locked Petalfell reference camera.

The output is a transparent overlay. Composite it over the source image; do not
edit it as authored world data. It exists to turn source-image pixels into the
same site-local coordinates consumed by a reference voxel transcription.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path


def add(a: tuple[float, float, float], b: tuple[float, float, float]):
    return tuple(x + y for x, y in zip(a, b))


def sub(a: tuple[float, float, float], b: tuple[float, float, float]):
    return tuple(x - y for x, y in zip(a, b))


def mul(a: tuple[float, float, float], scalar: float):
    return tuple(x * scalar for x in a)


def dot(a: tuple[float, float, float], b: tuple[float, float, float]):
    return sum(x * y for x, y in zip(a, b))


def cross(a: tuple[float, float, float], b: tuple[float, float, float]):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def normalized(a: tuple[float, float, float]):
    length = math.sqrt(dot(a, a))
    return mul(a, 1.0 / length)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--width", type=int, default=1672)
    parser.add_argument("--height", type=int, default=941)
    parser.add_argument("--yaw", type=float, default=135.0)
    parser.add_argument("--pitch", type=float, default=35.26439)
    parser.add_argument("--distance", type=float, default=190.0)
    parser.add_argument("--fov", type=float, default=21.0)
    parser.add_argument("--focus-y", type=float, default=112.0)
    parser.add_argument("--plan-scale", type=float, default=0.9)
    parser.add_argument("--sample", action="append", default=[],
                        help="source pixel x,y to intersect with --sample-plane")
    parser.add_argument("--sample-plane", type=float, default=109.0)
    args = parser.parse_args()

    yaw = math.radians(args.yaw)
    pitch = math.radians(args.pitch)
    focus = (0.0, args.focus_y, -9.0)
    offset = (math.sin(yaw) * math.cos(pitch), math.sin(pitch),
              math.cos(yaw) * math.cos(pitch))
    camera = add(focus, mul(offset, args.distance))
    forward = normalized(sub(focus, camera))
    right = normalized(cross(forward, (0.0, 1.0, 0.0)))
    up = normalized(cross(right, forward))
    focal_y = args.height / (2.0 * math.tan(math.radians(args.fov) / 2.0))

    for sample in args.sample:
        pixel_x, pixel_y = (float(value) for value in sample.split(",", 1))
        ray = add(forward, add(mul(right, (pixel_x - args.width / 2.0) / focal_y),
                               mul(up, (args.height / 2.0 - pixel_y) / focal_y)))
        travel = (args.sample_plane - camera[1]) / ray[1]
        world = add(camera, mul(ray, travel))
        print(f"pixel {pixel_x:g},{pixel_y:g} at y={args.sample_plane:g}: "
              f"local {(-world[0] / args.plan_scale):.2f},"
              f"{(world[2] / args.plan_scale):.2f}")

    def project(local_x: float, y: float, local_z: float):
        point = (-local_x * args.plan_scale, y, local_z * args.plan_scale)
        relative = sub(point, camera)
        depth = dot(relative, forward)
        if depth <= 0.01:
            return None
        screen_x = args.width / 2.0 + dot(relative, right) * focal_y / depth
        screen_y = args.height / 2.0 - dot(relative, up) * focal_y / depth
        return (screen_x, screen_y)

    lines: list[str] = []
    labels: list[str] = []
    planes = ((109.0, "#23b8ff", 0.52), (114.0, "#ff3aa7", 0.52))
    for plane_y, colour, opacity in planes:
        for x in range(-80, 81, 10):
            points = [project(x, plane_y, z) for z in range(-80, 81, 2)]
            data = " ".join(f"{p[0]:.2f},{p[1]:.2f}" for p in points if p)
            lines.append(f'<polyline points="{data}" fill="none" stroke="{colour}" '
                         f'stroke-opacity="{opacity}" stroke-width="1"/>')
            if x % 20 == 0:
                p = project(x, plane_y, 0)
                if p:
                    labels.append(f'<text x="{p[0]:.2f}" y="{p[1]:.2f}" '
                                  f'fill="{colour}" font-size="13" stroke="#3b3046" '
                                  f'stroke-width="2" paint-order="stroke">x{x}</text>')
        for z in range(-80, 81, 10):
            points = [project(x, plane_y, z) for x in range(-80, 81, 2)]
            data = " ".join(f"{p[0]:.2f},{p[1]:.2f}" for p in points if p)
            lines.append(f'<polyline points="{data}" fill="none" stroke="{colour}" '
                         f'stroke-opacity="{opacity}" stroke-width="1"/>')
            if z % 20 == 0:
                p = project(0, plane_y, z)
                if p:
                    labels.append(f'<text x="{p[0]:.2f}" y="{p[1]:.2f}" '
                                  f'fill="{colour}" font-size="13" stroke="#3b3046" '
                                  f'stroke-width="2" paint-order="stroke">z{z}</text>')

    origin = project(0.0, 109.0, -9.0)
    if origin:
        lines.append(f'<circle cx="{origin[0]:.2f}" cy="{origin[1]:.2f}" r="6" '
                     'fill="none" stroke="#ffffff" stroke-width="2"/>')
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{args.width}" '
           f'height="{args.height}" viewBox="0 0 {args.width} {args.height}">'
           + "".join(lines) + "".join(labels) + "</svg>")
    args.output.write_text(svg, encoding="utf-8")


if __name__ == "__main__":
    main()
