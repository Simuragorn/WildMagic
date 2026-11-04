"""Split the original Hunyuan troll sculpt into aligned damage layers.

The intact creature must retain the silhouette and anatomy of the generated
source. Segmentation therefore assigns the source triangles to body parts; it
never rebuilds the visible body from primitives.
"""

import bpy
import sys
from pathlib import Path
from mathutils import Vector


DECIMATE_RATIO = 0.40
FLESH_INSET = 0.010


def arguments():
    values = sys.argv
    return values[values.index("--") + 1 :] if "--" in values else []


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_modifier(obj, modifier):
    activate(obj)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def assign_material(obj, assigned_material):
    obj.data.materials.clear()
    obj.data.materials.append(assigned_material)


def material(name, color, roughness=0.75):
    result = bpy.data.materials.new(name)
    result.diffuse_color = color
    result.use_nodes = True
    principled = result.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = color
    principled.inputs["Roughness"].default_value = roughness
    return result


def classify(point):
    """Assign every source triangle exactly once, leaving no intact-state gaps."""
    x, _, z = point
    absolute_x = abs(x)

    # The generated troll faces -Y. Its arms slope outward as they descend, so
    # the shoulder/arm boundary follows that silhouette instead of a box cut.
    arm_boundary = 0.38 + max(0.0, 0.50 - z) * 0.29
    if z > -0.46 and absolute_x > arm_boundary:
        return "Arm_L" if x > 0.0 else "Arm_R"

    # The head cut sits inside the neck. Interlocking source triangles conceal
    # the seam while intact and create an organic edge after detachment.
    if z > 0.50:
        return "Head"

    # Keep the belly and loincloth with the torso; legs begin below the hips.
    if z < -0.31:
        return "Leg_L" if x > 0.0 else "Leg_R"

    return "Torso"


def split_source_mesh(source):
    vertices = [vertex.co.copy() for vertex in source.data.vertices]
    groups = {name: [] for name in ("Torso", "Head", "Arm_L", "Arm_R", "Leg_L", "Leg_R")}
    smooth = {}

    for polygon in source.data.polygons:
        center = sum((vertices[index] for index in polygon.vertices), Vector()) / len(polygon.vertices)
        segment = classify(center)
        groups[segment].append(tuple(polygon.vertices))
        smooth[(segment, len(groups[segment]) - 1)] = polygon.use_smooth

    results = {}
    for segment, faces in groups.items():
        used = sorted({index for face in faces for index in face})
        remap = {old: new for new, old in enumerate(used)}
        mesh = bpy.data.meshes.new(f"{segment}_Skin_Mesh")
        mesh.from_pydata(
            [vertices[index] for index in used],
            [],
            [tuple(remap[index] for index in face) for face in faces],
        )
        mesh.update(calc_edges=True)
        for index, polygon in enumerate(mesh.polygons):
            polygon.use_smooth = smooth[(segment, index)]

        obj = bpy.data.objects.new(f"{segment}_Skin", mesh)
        bpy.context.collection.objects.link(obj)
        obj["segment"] = segment.lower()
        obj["damage_layer"] = "skin"
        results[segment] = obj

    return results


def flesh_from_skin(skin, name):
    flesh = skin.copy()
    flesh.data = skin.data.copy()
    flesh.name = name
    flesh.data.name = f"{name}_Mesh"
    bpy.context.collection.objects.link(flesh)
    flesh.data.update()

    # Move the duplicate inward along the actual sculpt normals. This preserves
    # the face, fingers and musculature instead of replacing them with capsules.
    for vertex in flesh.data.vertices:
        vertex.co -= vertex.normal * FLESH_INSET
    flesh.data.update(calc_edges=True)
    flesh["segment"] = name.removesuffix("_Flesh").lower()
    flesh["damage_layer"] = "flesh"
    return flesh


def sphere(name, location, scale, subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions,
        radius=1.0,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def cylinder_between(name, start, end, radius, vertices=12):
    start = Vector(start)
    end = Vector(end)
    direction = end - start
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=direction.length,
        location=(start + end) * 0.5,
    )
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    return obj


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = objects[0]
    result.name = name
    result.data.name = f"{name}_Mesh"
    return result


def build_bones(segment):
    if segment == "Head":
        parts = [
            sphere("Cranium", (0.0, 0.02, 0.70), (0.27, 0.22, 0.24), 2),
            sphere("Jaw", (0.0, -0.08, 0.55), (0.22, 0.16, 0.13), 1),
        ]
    elif segment == "Torso":
        parts = [cylinder_between("Spine", (0.0, 0.08, -0.28), (0.0, 0.08, 0.52), 0.045)]
        for index, z in enumerate((-0.08, 0.08, 0.24, 0.40)):
            width = 0.30 + index * 0.018
            parts.append(cylinder_between(f"Rib_{index}", (-width, 0.02, z), (width, 0.02, z), 0.028, 10))
        parts.append(cylinder_between("Pelvis", (-0.28, 0.02, -0.27), (0.28, 0.02, -0.27), 0.055))
    elif segment.startswith("Arm"):
        sign = 1.0 if segment.endswith("L") else -1.0
        shoulder = (0.43 * sign, 0.03, 0.43)
        elbow = (0.67 * sign, 0.03, 0.12)
        wrist = (0.80 * sign, 0.03, -0.18)
        hand = (0.84 * sign, 0.01, -0.34)
        parts = [
            cylinder_between("Humerus", shoulder, elbow, 0.038),
            sphere("Elbow", elbow, (0.052, 0.052, 0.052), 1),
            cylinder_between("Radius", elbow, wrist, 0.026, 10),
            cylinder_between("Palm", wrist, hand, 0.045, 10),
        ]
    else:
        sign = 1.0 if segment.endswith("L") else -1.0
        hip = (0.22 * sign, 0.04, -0.26)
        knee = (0.28 * sign, 0.03, -0.58)
        ankle = (0.31 * sign, 0.00, -0.83)
        foot = (0.31 * sign, -0.16, -0.89)
        parts = [
            cylinder_between("Femur", hip, knee, 0.045),
            sphere("Knee", knee, (0.058, 0.058, 0.058), 1),
            cylinder_between("Tibia", knee, ankle, 0.034, 10),
            cylinder_between("Foot", ankle, foot, 0.050, 10),
        ]

    bone = join(parts, f"{segment}_Bone")
    bone["segment"] = segment.lower()
    bone["damage_layer"] = "bone"
    return bone


def look_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_preview(objects, output_path):
    bpy.ops.object.camera_add(location=(0.0, -4.0, 0.0))
    camera = bpy.context.object
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 2.25
    look_at(camera, (0.0, 0.0, 0.0))
    bpy.context.scene.camera = camera

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.studio_light = "paint.sl"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "WORLD"
    scene.display.shading.curvature_ridge_factor = 1.5
    scene.display.shading.curvature_valley_factor = 1.0
    scene.render.resolution_x = 700
    scene.render.resolution_y = 700
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = True
    scene.render.filepath = str(output_path)

    for obj in objects:
        obj.hide_render = not obj.name.endswith("_Skin")
    bpy.ops.render.render(write_still=True)


def main():
    source_path, output_path, preview_path = map(Path, arguments())
    output_path.parent.mkdir(parents=True, exist_ok=True)
    preview_path.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source_path))
    source = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
    source.name = "Source_Hunyuan_Troll"
    activate(source)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    decimate = source.modifiers.new("Production_Decimate", "DECIMATE")
    decimate.ratio = DECIMATE_RATIO
    decimate.use_collapse_triangulate = True
    apply_modifier(source, decimate)

    skin_material = material("MAT_Troll_Skin", (0.26, 0.43, 0.22, 1.0), 0.82)
    flesh_material = material("MAT_Troll_Flesh", (0.55, 0.035, 0.025, 1.0), 0.64)
    bone_material = material("MAT_Troll_Bone", (0.78, 0.68, 0.48, 1.0), 0.76)

    skins = split_source_mesh(source)
    bpy.data.objects.remove(source, do_unlink=True)
    export_objects = []
    for segment, skin in skins.items():
        flesh = flesh_from_skin(skin, f"{segment}_Flesh")
        bone = build_bones(segment)
        assign_material(skin, skin_material)
        assign_material(flesh, flesh_material)
        assign_material(bone, bone_material)
        export_objects.extend((skin, flesh, bone))

    render_preview(export_objects, preview_path)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in export_objects:
        obj.hide_render = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = skins["Torso"]
    bpy.ops.export_scene.fbx(
        filepath=str(output_path),
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
    )

    print(f"Exported {len(export_objects)} layered objects to {output_path}")
    for segment, skin in skins.items():
        print(f"{segment}: {len(skin.data.polygons):,} skin triangles")


if __name__ == "__main__":
    main()
