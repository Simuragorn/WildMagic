"""Rebuild the troll arm's flesh from its skin and safely inset the skeleton.

Run with Blender in background mode:
blender --background --python repair_troll_arm_layers.py -- input.fbx output.fbx
"""

import bpy
import sys
from mathutils import Matrix
from pathlib import Path


def arguments():
    values = sys.argv
    return values[values.index("--") + 1 :] if "--" in values else []


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


source_path, output_path = map(Path, arguments())
output_path.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(source_path))

skin = bpy.data.objects.get("Arm_L_Skin")
old_flesh = bpy.data.objects.get("Arm_L_Flesh")
bone = bpy.data.objects.get("Arm_L_Bone")
if skin is None or old_flesh is None or bone is None:
    raise RuntimeError("The source FBX does not contain all three arm layers.")

# The old independent flesh volume could leave empty gaps behind irregular skin.
# An evenly inset copy guarantees continuous flesh behind the complete silhouette.
flesh = skin.copy()
flesh.data = skin.data.copy()
flesh.name = "Arm_L_Flesh_Rebuilt"
bpy.context.collection.objects.link(flesh)
flesh.data.materials.clear()
for material in old_flesh.data.materials:
    flesh.data.materials.append(material)

activate(flesh)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.transform.shrink_fatten(value=-0.022, use_even_offset=True)
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode="OBJECT")

bpy.data.objects.remove(old_flesh, do_unlink=True)
flesh.name = "Arm_L_Flesh"
flesh.data.name = "Arm_L_Flesh_Mesh"

# Pull the few protruding bone tips inside the new flesh volume.
bone.data.transform(Matrix.Scale(0.92, 4))

bpy.ops.object.select_all(action="DESELECT")
for obj in (skin, flesh, bone):
    obj.hide_viewport = False
    obj.hide_render = False
    obj.hide_set(False)
    obj.select_set(True)
bpy.context.view_layer.objects.active = skin

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
