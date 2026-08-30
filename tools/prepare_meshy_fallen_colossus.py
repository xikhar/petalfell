"""Normalize the author's Meshy Reference 12 exports for direct Godot use.

Meshy adds a two-metre calibration cube, image-derived ground slabs and an
arbitrary corner pivot. This removes those non-sculpture parts, strips the baked
source material, and gives each subject a bottom-centred pivot so Godot can use
Petalfell's world-space stone treatment on ordinary site terrain.
"""

from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[1]
ASSETS = {
    "head": (ROOT / "meshy" / "head.glb",
             ROOT / "assets" / "sites" / "fallen-colossus-head.glb"),
    "legs": (ROOT / "meshy" / "legs.glb",
             ROOT / "assets" / "sites" / "fallen-colossus-legs.glb"),
}


def remove_generated_base(subject, label: str) -> None:
    """Remove only disconnected image-floor pieces below the sculpture.

    Meshy's block surfaces are intentionally disconnected, so a largest-island
    test would also discard the sculpture. The unwanted bases have a much lower,
    measured vertical band: the head export's rectangular plate ends at z=-0.50
    and the leg export's stepped image pedestal ends at z=-0.60.
    """
    mesh = subject.data
    adjacency = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        adjacency[a].append(b)
        adjacency[b].append(a)

    seen = set()
    remove = set()
    threshold = -0.50 if label == "head" else -0.60
    for start in range(len(mesh.vertices)):
        if start in seen:
            continue
        stack = [start]
        seen.add(start)
        component = []
        while stack:
            vertex = stack.pop()
            component.append(vertex)
            for neighbour in adjacency[vertex]:
                if neighbour not in seen:
                    seen.add(neighbour)
                    stack.append(neighbour)
        if max(mesh.vertices[index].co.z for index in component) < threshold:
            remove.update(component)

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    for index in remove:
        mesh.vertices[index].select = True
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.delete(type="VERT")
    bpy.ops.object.mode_set(mode="OBJECT")
    if not mesh.polygons:
        raise RuntimeError(f"{label} cleanup removed the complete sculpture")


def prepare(label: str, source: Path, output: Path) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"{source} contains no mesh")
    subject = max(meshes, key=lambda obj: len(obj.data.polygons))
    if len(subject.data.polygons) < 100:
        raise RuntimeError(f"{source} has no detailed Meshy subject")

    bpy.ops.object.select_all(action="DESELECT")
    subject.select_set(True)
    bpy.context.view_layer.objects.active = subject
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    remove_generated_base(subject, label)
    subject.data.materials.clear()

    xs = [vertex.co.x for vertex in subject.data.vertices]
    ys = [vertex.co.y for vertex in subject.data.vertices]
    zs = [vertex.co.z for vertex in subject.data.vertices]
    centre_x = (min(xs) + max(xs)) * 0.5
    centre_y = (min(ys) + max(ys)) * 0.5
    bottom = min(zs)
    for vertex in subject.data.vertices:
        vertex.co.x -= centre_x
        vertex.co.y -= centre_y
        vertex.co.z -= bottom
    subject.name = f"FallenColossus{label.title()}"
    subject.data.name = f"FallenColossus{label.title()}Mesh"

    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    subject.select_set(True)
    bpy.context.view_layer.objects.active = subject
    bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_materials="NONE",
    )
    print(f"[meshy-reference-12] {label}: {source} -> {output}")


for asset_label, (asset_source, asset_output) in ASSETS.items():
    prepare(asset_label, asset_source, asset_output)
