"""Build a low-poly modular troll with skin, flesh and bone per body segment."""

import bpy
import sys
from pathlib import Path
from mathutils import Vector


def arguments():
    values = sys.argv
    return values[values.index("--") + 1 :] if "--" in values else []


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_scale(obj):
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)


def ico(name, location, scale, subdivisions=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    apply_scale(obj)
    return obj


def cylinder_between(name, start, end, radius, vertices=10):
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
    apply_scale(obj)
    return obj


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = objects[0]
    result.name = name
    activate(result)
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    for polygon in result.data.polygons:
        polygon.use_smooth = False
    return result


def flesh_from_skin(skin, name):
    flesh = skin.copy()
    flesh.data = skin.data.copy()
    flesh.name = name
    bpy.context.collection.objects.link(flesh)
    activate(flesh)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.transform.shrink_fatten(value=-0.0025, use_even_offset=True)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return flesh


def make_segment(name, skin_parts, bone_parts):
    skin = join(skin_parts, f"{name}_Skin")
    flesh = flesh_from_skin(skin, f"{name}_Flesh")
    bone = join(bone_parts, f"{name}_Bone")
    for obj, layer in ((skin, "skin"), (flesh, "flesh"), (bone, "bone")):
        obj["segment"] = name.lower()
        obj["damage_layer"] = layer
    return skin, flesh, bone


output_path = Path(arguments()[0])
output_path.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

segments = []

# Torso: broad shoulders, heavy belly, spine, ribs and pelvis.
torso_skin = [
    ico("Chest", (0.0, 0.0, 1.95), (0.76, 0.42, 0.70), 2),
    ico("Belly", (0.0, 0.02, 1.48), (0.64, 0.40, 0.68), 2),
    ico("BackHump", (0.0, 0.22, 2.25), (0.55, 0.28, 0.36), 1),
]
torso_bones = [cylinder_between("Spine", (0, 0.08, 1.20), (0, 0.08, 2.42), 0.085, 10)]
for index, height in enumerate((1.62, 1.82, 2.02, 2.22)):
    width = 0.42 + (height - 1.62) * 0.18
    torso_bones.append(cylinder_between(f"Rib_{index}", (-width, 0, height), (width, 0, height), 0.055, 8))
torso_bones.extend([
    cylinder_between("Pelvis", (-0.36, 0.02, 1.22), (0.36, 0.02, 1.22), 0.10, 10),
    ico("Sternum", (0, -0.05, 1.95), (0.11, 0.08, 0.48), 1),
])
segments.append(make_segment("Torso", torso_skin, torso_bones))

# Head: oversized low-poly skull, jaw, nose and pointed ears.
head_skin = [
    ico("Cranium", (0, -0.02, 2.88), (0.48, 0.40, 0.52), 2),
    ico("Jaw", (0, -0.17, 2.62), (0.39, 0.31, 0.28), 1),
    ico("Nose", (0, -0.43, 2.88), (0.13, 0.18, 0.17), 1),
    ico("Ear_L", (-0.48, -0.01, 2.91), (0.16, 0.10, 0.22), 1),
    ico("Ear_R", (0.48, -0.01, 2.91), (0.16, 0.10, 0.22), 1),
]
head_bones = [
    ico("Skull", (0, 0.0, 2.88), (0.34, 0.29, 0.38), 2),
    ico("Mandible", (0, -0.10, 2.62), (0.29, 0.22, 0.15), 1),
]
segments.append(make_segment("Head", head_skin, head_bones))


def arm(side, sign):
    shoulder = (0.62 * sign, 0.0, 2.23)
    elbow = (0.96 * sign, 0.0, 1.65)
    wrist = (1.06 * sign, -0.02, 1.08)
    hand = (1.07 * sign, -0.10, 0.91)
    skin = [
        cylinder_between(f"{side}_UpperArm", shoulder, elbow, 0.235, 10),
        ico(f"{side}_Elbow", elbow, (0.25, 0.22, 0.25), 1),
        cylinder_between(f"{side}_Forearm", elbow, wrist, 0.20, 10),
        ico(f"{side}_Hand", hand, (0.24, 0.20, 0.30), 1),
    ]
    bones = [
        cylinder_between(f"{side}_Humerus", shoulder, elbow, 0.07, 8),
        ico(f"{side}_ElbowJoint", elbow, (0.10, 0.10, 0.10), 1),
        cylinder_between(f"{side}_Radius", elbow, wrist, 0.055, 8),
        ico(f"{side}_HandBone", hand, (0.12, 0.09, 0.18), 1),
    ]
    return make_segment(side, skin, bones)


segments.append(arm("Arm_L", -1))
segments.append(arm("Arm_R", 1))


def leg(side, sign):
    hip = (0.34 * sign, 0.02, 1.23)
    knee = (0.42 * sign, 0.02, 0.62)
    ankle = (0.45 * sign, 0.01, 0.05)
    foot = (0.45 * sign, -0.20, -0.11)
    skin = [
        cylinder_between(f"{side}_Thigh", hip, knee, 0.29, 10),
        ico(f"{side}_Knee", knee, (0.27, 0.25, 0.25), 1),
        cylinder_between(f"{side}_Shin", knee, ankle, 0.23, 10),
        ico(f"{side}_Foot", foot, (0.29, 0.43, 0.19), 1),
    ]
    bones = [
        cylinder_between(f"{side}_Femur", hip, knee, 0.075, 8),
        ico(f"{side}_KneeJoint", knee, (0.105, 0.105, 0.105), 1),
        cylinder_between(f"{side}_Tibia", knee, ankle, 0.06, 8),
        ico(f"{side}_FootBone", foot, (0.15, 0.24, 0.075), 1),
    ]
    return make_segment(side, skin, bones)


segments.append(leg("Leg_L", -1))
segments.append(leg("Leg_R", 1))

bpy.ops.object.select_all(action="DESELECT")
for segment in segments:
    for obj in segment:
        obj.select_set(True)
bpy.context.view_layer.objects.active = segments[0][0]

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
