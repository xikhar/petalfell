#!/usr/bin/env python3
"""Audit and draw the Reference 1 locked-camera and vertical-course evidence.

This is a calibration viewer, not a site builder. It projects the source-facing
one-cell plan through the exact Godot long-lens camera equations, draws dispersed
source landmarks, and (when supplied) annotates visible integer course spans.
It never fills, moves, mirrors, or invents architecture.
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import math
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CAMERA_PATH = (
    ROOT
    / "content"
    / "chapter_01"
    / "sites"
    / "shallows-gate-and-causeway-reference-1-camera.json"
)
VERTICAL_PATH = (
    ROOT
    / "content"
    / "chapter_01"
    / "sites"
    / "shallows-gate-and-causeway-reference-1-vertical.json"
)


def fail(message: str) -> None:
    raise ValueError(message)


def resource_path(value: str) -> Path:
    if not value.startswith("res://"):
        fail(f"expected a res:// path, got {value!r}")
    return ROOT / value[6:]


def read_png_size(path: Path) -> tuple[int, int]:
    with path.open("rb") as source:
        header = source.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n":
        fail(f"{path} is not a readable PNG")
    return struct.unpack(">II", header[16:24])


def number(value: float) -> str:
    return f"{value:.3f}".rstrip("0").rstrip(".")


def add(a: tuple[float, float, float], b: tuple[float, float, float]):
    return tuple(x + y for x, y in zip(a, b))


def sub(a: tuple[float, float, float], b: tuple[float, float, float]):
    return tuple(x - y for x, y in zip(a, b))


def mul(a: tuple[float, float, float], scalar: float):
    return tuple(x * scalar for x in a)


def dot(a: tuple[float, float, float], b: tuple[float, float, float]):
    return sum(x * y for x, y in zip(a, b))


def cross(a: tuple[float, float, float], b: tuple[float, float, float]):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def normalized(value: tuple[float, float, float]):
    length = math.sqrt(dot(value, value))
    if length <= 1e-9:
        fail("cannot normalize a zero vector")
    return mul(value, 1.0 / length)


class Projection:
    def __init__(self, camera: dict):
        view = camera["referenceView"]
        registration = camera["runtimeRegistration"]
        self.width = int(view["sourceWidth"])
        self.height = int(view["sourceHeight"])
        self.focal_y = self.height / (
            2.0 * math.tan(math.radians(float(view["verticalFovDegrees"])) / 2.0)
        )
        self.axis = math.radians(float(registration["axisDegrees"]))
        self.mirror_x = bool(registration["runtimeMirrorX"])
        self.runtime_scale = int(registration["runtimePlanScale"])
        focus = view["focus"]
        focus_x, focus_z = self.transform(float(focus["x"]), float(focus["z"]))
        self.focus = (focus_x, float(view["heightOffset"]) * self.runtime_scale, focus_z)
        yaw = math.radians(float(view["yawDegrees"]))
        pitch = math.radians(float(view["pitchDegrees"]))
        offset = (
            math.sin(yaw) * math.cos(pitch),
            math.sin(pitch),
            math.cos(yaw) * math.cos(pitch),
        )
        self.camera = add(self.focus, mul(offset, float(view["distance"])))
        self.forward = normalized(sub(self.focus, self.camera))
        self.right = normalized(cross(self.forward, (0.0, 1.0, 0.0)))
        self.up = normalized(cross(self.right, self.forward))

    def transform(self, source_x: float, source_z: float) -> tuple[float, float]:
        plan_x = (-source_x if self.mirror_x else source_x) * self.runtime_scale
        plan_z = source_z * self.runtime_scale
        cosine, sine = math.cos(self.axis), math.sin(self.axis)
        return (
            plan_x * cosine + plan_z * sine,
            -plan_x * sine + plan_z * cosine,
        )

    def project(self, source_x: float, y: float, source_z: float):
        world_x, world_z = self.transform(source_x, source_z)
        relative = sub((world_x, y * self.runtime_scale, world_z), self.camera)
        depth = dot(relative, self.forward)
        if depth <= 0.01:
            return None
        return (
            self.width / 2.0 + dot(relative, self.right) * self.focal_y / depth,
            self.height / 2.0 - dot(relative, self.up) * self.focal_y / depth,
            depth,
        )


def point3(item: object, label: str) -> tuple[float, float, float]:
    if not isinstance(item, dict) or set(item) != {"x", "y", "z"}:
        fail(f"{label} must contain exactly x/y/z")
    values = (item["x"], item["y"], item["z"])
    if not all(isinstance(value, (int, float)) and not isinstance(value, bool) for value in values):
        fail(f"{label} x/y/z must be numeric")
    return tuple(float(value) for value in values)


def audit(camera: dict) -> tuple[list[str], dict]:
    errors: list[str] = []
    if camera.get("version") != 1:
        errors.append("camera calibration version must be 1")
    if camera.get("kind") != "locked-reference-camera-calibration":
        errors.append("kind must be 'locked-reference-camera-calibration'")
    if camera.get("siteId") != "shallows-gate-and-causeway":
        errors.append("siteId must be 'shallows-gate-and-causeway'")
    try:
        source = resource_path(camera.get("referencePath", ""))
        view = camera.get("referenceView", {})
        size = read_png_size(source)
        expected = (view.get("sourceWidth"), view.get("sourceHeight"))
        if size != expected:
            errors.append(f"source size {size} != registered {expected}")
        digest = hashlib.sha256(source.read_bytes()).hexdigest()
        if digest != camera.get("referenceSha256"):
            errors.append("reference SHA-256 does not match the registered source")
    except (OSError, ValueError) as error:
        errors.append(str(error))

    try:
        definition_path = resource_path(camera.get("siteDefinitionPath", ""))
        definition = json.loads(definition_path.read_text(encoding="utf-8"))
        expected_definition = {
            "siteId": "shallows-gate-and-causeway",
            "builderId": "reference-1-gate-and-causeway-v1",
            "origin": {"x": 6400, "z": 6980},
            "axisDegrees": 0,
            "runtimePlanScale": 3,
            "verticalDatumY": 168,
            "footprintMin": {"x": -48, "z": -28},
            "footprintMax": {"x": 47, "z": 62},
            "playerSpawn": {"x": 0, "z": 20},
        }
        for key, expected in expected_definition.items():
            if definition.get(key) != expected:
                errors.append(f"site definition {key} must be {expected!r}")
        if definition.get("groundPlanPath") != camera.get("groundPlanPath"):
            errors.append("site definition and camera must name the same ground plan")
        definition_view = definition.get("referenceView", {})
        for key in (
            "focus", "heightOffset", "distance", "yawDegrees", "pitchDegrees",
            "sourceWidth", "sourceHeight",
        ):
            if definition_view.get(key) != camera.get("referenceView", {}).get(key):
                errors.append(f"site definition referenceView.{key} must match camera calibration")
    except (OSError, json.JSONDecodeError, ValueError) as error:
        errors.append(f"site definition: {error}")

    registration = camera.get("runtimeRegistration", {})
    if registration.get("origin") != {"x": 6400, "z": 6980}:
        errors.append("runtime origin must match canonical site centre 6400,6980")
    if registration.get("extent") != {"x": 336, "z": 408}:
        errors.append("runtime extent must match canonical site extent 336x408")
    if registration.get("runtimePlanScale") != 3:
        errors.append("runtimePlanScale must preserve the author-directed integer scale 3")
    if registration.get("axisDegrees") not in (0, 90, 180, 270):
        errors.append("runtime axisDegrees must be cardinal")
    if not isinstance(registration.get("runtimeMirrorX"), bool):
        errors.append("runtimeMirrorX must be an explicit boolean")
    if registration.get("southEntrance", {}).get("local") != {"x": 0, "z": 42}:
        errors.append("south entrance must remain at local 0,42")
    if registration.get("northEntrance", {}).get("local") != {"x": 0, "z": -23}:
        errors.append("north entrance must remain at local 0,-23")
    if registration.get("southEntrance", {}).get("global") != {"x": 6400, "z": 7106}:
        errors.append("scaled south entrance must resolve to global 6400,7106")
    if registration.get("northEntrance", {}).get("global") != {"x": 6400, "z": 6911}:
        errors.append("scaled north entrance must resolve to global 6400,6911")

    view = camera.get("referenceView", {})
    if view.get("projection") != "Perspective":
        errors.append("reference projection must use the ordinary Perspective camera")
    for key in ("verticalFovDegrees", "distance", "yawDegrees", "pitchDegrees", "heightOffset"):
        if not isinstance(view.get(key), (int, float)) or isinstance(view.get(key), bool):
            errors.append(f"referenceView.{key} must be numeric")
    if not 0 < float(view.get("verticalFovDegrees", 0)) < 90:
        errors.append("verticalFovDegrees must be between 0 and 90")
    if float(view.get("distance", 0)) <= 0:
        errors.append("distance must be positive")
    if not 0 < float(view.get("pitchDegrees", 0)) < 89:
        errors.append("pitchDegrees must be between 0 and 89")
    focus = view.get("focus", {})
    if set(focus) != {"x", "z"} or not all(
        isinstance(focus.get(key), (int, float)) and not isinstance(focus.get(key), bool)
        for key in ("x", "z")
    ):
        errors.append("referenceView.focus must contain numeric x/z")

    residuals: list[float] = []
    ids: set[str] = set()
    try:
        projection = Projection(camera)
    except (KeyError, TypeError, ValueError) as error:
        errors.append(f"camera projection: {error}")
        projection = None
    for landmark in camera.get("calibrationLandmarks", []):
        landmark_id = landmark.get("id", "")
        if not landmark_id or landmark_id in ids:
            errors.append(f"calibration landmark id is missing or repeated: {landmark_id!r}")
        ids.add(landmark_id)
        try:
            x, y, z = point3(landmark.get("localPoint"), landmark_id)
        except ValueError as error:
            errors.append(str(error))
            continue
        source_pixel = landmark.get("sourcePixel", [])
        if len(source_pixel) != 2 or not all(
            isinstance(value, (int, float)) and not isinstance(value, bool)
            for value in source_pixel
        ):
            errors.append(f"{landmark_id} sourcePixel must be numeric [u,v]")
            continue
        if projection is None:
            continue
        predicted = projection.project(x, y, z)
        if predicted is None:
            errors.append(f"{landmark_id} lies behind the camera")
            continue
        residual = math.hypot(predicted[0] - source_pixel[0], predicted[1] - source_pixel[1])
        residuals.append(residual)
        limit = landmark.get("maxResidualPixels")
        if not isinstance(limit, (int, float)) or residual > float(limit) + 1e-6:
            errors.append(f"{landmark_id} residual {residual:.2f}px exceeds {limit!r}px")

    if len(residuals) < 6:
        errors.append("at least six dispersed camera landmarks are required")

    polyline_ids: set[str] = set()
    for polyline in camera.get("evidencePolylines", []):
        polyline_id = polyline.get("id", "")
        if not polyline_id or polyline_id in polyline_ids:
            errors.append(f"evidence polyline id is missing or repeated: {polyline_id!r}")
        polyline_ids.add(polyline_id)
        points = polyline.get("points", [])
        if len(points) < 2:
            errors.append(f"{polyline_id} must contain at least two source-plan points")
            continue
        for index, point in enumerate(points):
            try:
                point3(point, f"{polyline_id}.points[{index}]")
            except ValueError as error:
                errors.append(str(error))
    if len(polyline_ids) < 5:
        errors.append("at least five dispersed evidence polylines are required")
    metrics = {
        "landmarks": len(residuals),
        "polylines": len(polyline_ids),
        "maxResidualPixels": max(residuals, default=0.0),
        "rmsResidualPixels": math.sqrt(
            sum(value * value for value in residuals) / len(residuals)
        ) if residuals else 0.0,
    }
    if projection is not None:
        focus_x = float(view.get("focus", {}).get("x", 0))
        focus_z = float(view.get("focus", {}).get("z", 0))
        focus_y = float(view.get("heightOffset", 0))
        origin = projection.project(focus_x, focus_y, focus_z)
        x_axis = projection.project(focus_x + 1, focus_y, focus_z)
        z_axis = projection.project(focus_x, focus_y, focus_z + 1)
        y_axis = projection.project(focus_x, focus_y + 1, focus_z)
        if all(value is not None for value in (origin, x_axis, z_axis, y_axis)):
            metrics["focusBasis"] = {
                "x": [x_axis[0] - origin[0], x_axis[1] - origin[1]],
                "z": [z_axis[0] - origin[0], z_axis[1] - origin[1]],
                "y": [y_axis[0] - origin[0], y_axis[1] - origin[1]],
            }
    return errors, metrics


def audit_vertical(vertical: dict, camera: dict) -> tuple[list[str], dict]:
    errors: list[str] = []
    if vertical.get("version") != 1:
        errors.append("vertical schedule version must be 1")
    if vertical.get("kind") != "visible-vertical-course-schedule":
        errors.append("vertical kind must be 'visible-vertical-course-schedule'")
    if vertical.get("siteId") != camera.get("siteId"):
        errors.append("vertical siteId must match the camera calibration")
    if vertical.get("cameraCalibrationPath") != (
        "res://content/chapter_01/sites/"
        "shallows-gate-and-causeway-reference-1-camera.json"
    ):
        errors.append("vertical schedule must name the locked camera artifact")
    for path_key, hash_key in (
        ("primaryReferencePath", "primarySourceSha256"),
        ("overheadReferencePath", "overheadSourceSha256"),
    ):
        try:
            path = resource_path(vertical.get(path_key, ""))
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            if digest != vertical.get(hash_key):
                errors.append(f"{hash_key} does not match {path_key}")
        except (OSError, ValueError) as error:
            errors.append(str(error))

    datum = vertical.get("absoluteDatum", {})
    expected_datum = {
        "thresholdLocalTopY": 0,
        "thresholdAbsoluteTopY": 168,
        "waterLocalTopY": -21,
        "waterAbsoluteTopY": 105,
        "sampledBedLocalTopY": -23,
        "sampledBedAbsoluteTopY": 99,
    }
    for key, expected in expected_datum.items():
        if datum.get(key) != expected:
            errors.append(f"absoluteDatum.{key} must be {expected}")
    expected_samples = {
        ((6400, 6980), 99, 105),
        ((6400, 7106), 99, 105),
    }
    actual_samples = set()
    for sample in datum.get("samplePoints", []):
        global_point = sample.get("global", [])
        if len(global_point) == 2:
            actual_samples.add(
                (tuple(global_point), sample.get("bedTopY"), sample.get("waterTopY"))
            )
    if actual_samples != expected_samples:
        errors.append("absolute datum must retain both water-anchored authored samples")

    level_ids: set[str] = set()
    for level in vertical.get("levels", []):
        level_id = level.get("id", "")
        if not level_id or level_id in level_ids:
            errors.append(f"vertical level id is missing or repeated: {level_id!r}")
        level_ids.add(level_id)
        local_y, absolute_y = level.get("localTopY"), level.get("absoluteTopY")
        if not isinstance(local_y, int) or isinstance(local_y, bool):
            errors.append(f"{level_id}.localTopY must be an integer course")
        elif absolute_y != 168 + 3 * local_y:
            errors.append(f"{level_id}.absoluteTopY must equal 168 + 3 * localTopY")
        uncertainty = level.get("uncertaintyCourses")
        if not isinstance(uncertainty, int) or isinstance(uncertainty, bool) or uncertainty < 0:
            errors.append(f"{level_id}.uncertaintyCourses must be a non-negative integer")
        anchor = level.get("anchor", {})
        if set(anchor) != {"x", "z"} or not all(
            isinstance(anchor.get(key), (int, float)) and not isinstance(anchor.get(key), bool)
            for key in ("x", "z")
        ):
            errors.append(f"{level_id}.anchor must contain numeric x/z")
    required_levels = {
        "gate-threshold-and-causeway-deck",
        "east-main-shelf",
        "south-processional-landing",
        "water-surface",
        "sampled-water-bed",
    }
    if not required_levels <= level_ids:
        errors.append("vertical schedule is missing required datum levels")

    span_ids: set[str] = set()
    for span in vertical.get("courseSpans", []):
        span_id = span.get("id", "")
        if not span_id or span_id in span_ids:
            errors.append(f"course span id is missing or repeated: {span_id!r}")
        span_ids.add(span_id)
        lower, upper = span.get("fromLocalY"), span.get("toLocalY")
        if not all(isinstance(value, int) and not isinstance(value, bool) for value in (lower, upper)):
            errors.append(f"{span_id} endpoints must be integer courses")
            continue
        if lower >= upper:
            errors.append(f"{span_id} fromLocalY must be lower than toLocalY")
        candidate_range = span.get("candidateRange", [])
        if (
            len(candidate_range) != 2
            or not all(isinstance(value, int) and not isinstance(value, bool) for value in candidate_range)
            or not candidate_range[0] <= upper - lower <= candidate_range[1]
        ):
            errors.append(f"{span_id} candidateRange must contain its visible span")
        try:
            point3({"x": span["at"]["x"], "y": lower, "z": span["at"]["z"]}, span_id)
        except (KeyError, ValueError) as error:
            errors.append(str(error))

    stair_ids: set[str] = set()
    for stair in vertical.get("stairSchedules", []):
        stair_id = stair.get("id", "")
        if not stair_id or stair_id in stair_ids:
            errors.append(f"stair schedule id is missing or repeated: {stair_id!r}")
        stair_ids.add(stair_id)
        if not isinstance(stair.get("x"), int) or isinstance(stair.get("x"), bool):
            errors.append(f"{stair_id}.x must be an integer source-plan coordinate")
        treads = stair.get("treads", [])
        if not treads or treads[0] != stair.get("from") or treads[-1] != stair.get("to"):
            errors.append(f"{stair_id} endpoints must equal its first and last treads")
            continue
        for previous, current in zip(treads, treads[1:]):
            if (
                current.get("z") != previous.get("z") - 1
                or current.get("localTopY") != previous.get("localTopY") + 1
            ):
                errors.append(f"{stair_id} must rise one integer course per northward tread")
                break
    if stair_ids != {"south-processional-stair", "east-side-stair"}:
        errors.append("vertical schedule must contain exactly the two visible source stairs")
    if len(vertical.get("provisionalUnknowns", [])) < 5:
        errors.append("vertical schedule must retain explicit hidden/provisional unknowns")

    return errors, {
        "levels": len(level_ids),
        "spans": len(span_ids),
        "stairs": len(stair_ids),
        "courses": sum(len(stair.get("treads", [])) for stair in vertical.get("stairSchedules", [])),
    }


def render(camera: dict, vertical: dict) -> str:
    projection = Projection(camera)
    width, height = projection.width, projection.height
    parts = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
        "<title>Reference 1 locked isometric camera calibration</title>",
        "<desc>Godot perspective projection of source-facing plan evidence; no architecture is generated.</desc>",
        "<style>",
        ".grid{fill:none;stroke-width:.65;stroke-opacity:.22}.major{stroke-width:1.2;stroke-opacity:.52}.label{font:11px ui-monospace,monospace;paint-order:stroke;stroke:#352746;stroke-width:2px}.target{stroke:#ff275f;stroke-width:1.7;fill:none}.predicted{stroke:#fff;stroke-width:1.7;fill:none}.residual{stroke:#ffdf62;stroke-width:1.2}.legend{font:12px ui-monospace,monospace;fill:#34253a;paint-order:stroke;stroke:#fff;stroke-width:3px}.vertical{stroke:#fff;stroke-opacity:.45;stroke-width:1}",
        "</style>",
    ]
    for plane in camera.get("overlayPlanes", []):
        plane_y = float(plane["y"])
        colour = html.escape(plane["colour"])
        plane_id = html.escape(plane["id"])
        x0, z0, x1, z1 = (int(value) for value in plane.get("bounds", [-50,-30,50,65]))
        grid_step = int(plane.get("gridStep", 5))
        major_step = int(plane.get("majorStep", 10))
        parts.append(f'<g id="plane-{plane_id}" stroke="{colour}" fill="{colour}">')
        for x in range(x0, x1 + 1, grid_step):
            points = [projection.project(x, plane_y, z) for z in range(z0, z1 + 1)]
            geometry = " ".join(
                f"{number(point[0])},{number(point[1])}" for point in points if point is not None
            )
            css = "grid major" if x % major_step == 0 else "grid"
            parts.append(f'<polyline class="{css}" points="{geometry}"/>')
            if x % 10 == 0:
                label = projection.project(x, plane_y, 0)
                if label:
                    parts.append(
                        f'<text class="label" x="{number(label[0] + 3)}" y="{number(label[1] - 3)}">x{x} y{number(plane_y)}</text>'
                    )
        for z in range(z0, z1 + 1, grid_step):
            points = [projection.project(x, plane_y, z) for x in range(x0, x1 + 1)]
            geometry = " ".join(
                f"{number(point[0])},{number(point[1])}" for point in points if point is not None
            )
            css = "grid major" if z % major_step == 0 else "grid"
            parts.append(f'<polyline class="{css}" points="{geometry}"/>')
            if z % 10 == 0:
                label = projection.project(0, plane_y, z)
                if label:
                    parts.append(
                        f'<text class="label" x="{number(label[0] + 3)}" y="{number(label[1] - 3)}">z{z} y{number(plane_y)}</text>'
                    )
        parts.append("</g>")

    for polyline in camera.get("evidencePolylines", []):
        projected = []
        for point in polyline["points"]:
            x, y, z = point3(point, polyline["id"])
            screen = projection.project(x, y, z)
            if screen is not None:
                projected.append(f"{number(screen[0])},{number(screen[1])}")
        colour = html.escape(polyline.get("colour", "#fff4a4"))
        parts.append(
            f'<polyline id="evidence-{html.escape(polyline["id"])}" points="{" ".join(projected)}" '
            f'fill="none" stroke="{colour}" stroke-width="2.5" stroke-opacity=".9"/>'
        )

    for span in vertical.get("courseSpans", []):
        x, z = float(span["at"]["x"]), float(span["at"]["z"])
        lower, upper = int(span["fromLocalY"]), int(span["toLocalY"])
        start, end = projection.project(x, lower, z), projection.project(x, upper, z)
        if start is None or end is None:
            continue
        parts.append(
            f'<g id="vertical-{html.escape(span["id"])}">'
            f'<line x1="{number(start[0])}" y1="{number(start[1])}" x2="{number(end[0])}" y2="{number(end[1])}" '
            'stroke="#ffffff" stroke-width="2" stroke-opacity=".8"/>'
        )
        for course in range(lower, upper + 1):
            tick = projection.project(x, course, z)
            if tick is not None:
                parts.append(
                    f'<line x1="{number(tick[0] - 4)}" y1="{number(tick[1])}" '
                    f'x2="{number(tick[0] + 4)}" y2="{number(tick[1])}" class="vertical"/>'
                )
        parts.append(
            f'<text class="label" x="{number(end[0] + 5)}" y="{number(end[1] - 4)}">'
            f'{html.escape(span["id"])} {lower}..{upper}</text></g>'
        )

    for stair in vertical.get("stairSchedules", []):
        points = []
        for tread in stair["treads"]:
            screen = projection.project(float(stair["x"]), tread["localTopY"], tread["z"])
            if screen is not None:
                points.append(f"{number(screen[0])},{number(screen[1])}")
        parts.append(
            f'<polyline id="stair-{html.escape(stair["id"])}" points="{" ".join(points)}" '
            'fill="none" stroke="#73ffbd" stroke-width="3" stroke-opacity=".9"/>'
        )

    for landmark in camera.get("calibrationLandmarks", []):
        x, y, z = point3(landmark["localPoint"], landmark["id"])
        predicted = projection.project(x, y, z)
        if predicted is None:
            continue
        target_x, target_y = landmark["sourcePixel"]
        residual = math.hypot(predicted[0] - target_x, predicted[1] - target_y)
        parts.append(
            f'<g id="landmark-{html.escape(landmark["id"])}"><title>{html.escape(landmark["id"])} residual {residual:.2f}px</title>'
            f'<line class="residual" x1="{number(target_x)}" y1="{number(target_y)}" x2="{number(predicted[0])}" y2="{number(predicted[1])}"/>'
            f'<path class="target" d="M{number(target_x - 5)} {number(target_y)}h10M{number(target_x)} {number(target_y - 5)}v10"/>'
            f'<circle class="predicted" cx="{number(predicted[0])}" cy="{number(predicted[1])}" r="4"/></g>'
        )

    view = camera["referenceView"]
    registration = camera["runtimeRegistration"]
    parts.extend(
        [
            '<g class="legend">',
            '<text x="12" y="28">REFERENCE 1 LOCKED CAMERA</text>',
            f'<text x="12" y="46">mirrorX={str(registration["runtimeMirrorX"]).lower()} axis={registration["axisDegrees"]} yaw={number(view["yawDegrees"])} pitch={number(view["pitchDegrees"])} distance={number(view["distance"])} fov={number(view["verticalFovDegrees"])}</text>',
            f'<text x="12" y="64">focus=({number(view["focus"]["x"])},{number(view["heightOffset"])},{number(view["focus"]["z"])}) red=source target white=projection</text>',
            "</g>",
            "</svg>",
        ]
    )
    return "\n".join(parts) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit and render Reference 1 camera evidence.")
    parser.add_argument("output", nargs="?", type=Path, help="destination transparent SVG")
    parser.add_argument("--camera", type=Path, default=CAMERA_PATH)
    parser.add_argument("--vertical", type=Path, default=VERTICAL_PATH)
    parser.add_argument("--audit-only", action="store_true")
    parser.add_argument(
        "--draft",
        action="store_true",
        help="write an explicitly unverified tuning overlay even when residual checks fail",
    )
    args = parser.parse_args()
    if args.output is None and not args.audit_only:
        parser.error("output is required unless --audit-only is used")
    try:
        camera = json.loads(args.camera.read_text(encoding="utf-8"))
        errors, metrics = audit(camera)
        vertical = json.loads(args.vertical.read_text(encoding="utf-8"))
        vertical_errors, vertical_metrics = audit_vertical(vertical, camera)
        errors.extend(vertical_errors)
        if errors and not args.draft:
            fail("camera audit failed:\n" + "\n".join(f"  - {error}" for error in errors))
        if errors:
            print("WARNING: unverified draft overlay:")
            for error in errors:
                print(f"  - {error}")
        svg = render(camera, vertical)
    except (OSError, json.JSONDecodeError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"ERROR: {error}\n")
    if not args.audit_only:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(svg, encoding="utf-8")
    basis = metrics.get("focusBasis", {})
    suffix = "audit only" if args.audit_only else f"wrote {args.output.resolve()}"
    print(
        f"Audited {camera['siteId']} camera; landmarks={metrics['landmarks']}, "
        f"polylines={metrics['polylines']}, "
        f"levels={vertical_metrics['levels']}, spans={vertical_metrics['spans']}, "
        f"stairs={vertical_metrics['stairs']}, stairCourses={vertical_metrics['courses']}, "
        f"rms={metrics['rmsResidualPixels']:.2f}px, max={metrics['maxResidualPixels']:.2f}px, "
        f"basisX={basis.get('x')}, basisZ={basis.get('z')}, basisY={basis.get('y')}; {suffix}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
