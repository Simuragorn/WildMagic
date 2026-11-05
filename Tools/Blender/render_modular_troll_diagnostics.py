"""Render color-coded orthographic views of a modular troll FBX."""

import bpy
import sys
from pathlib import Path
from mathutils import Vector


def arguments():
    values = sys.argv
    return values[values.index("--") + 1 :] if "--" in values else []


def look_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


source_path, output_dir = map(Path, arguments())
output_dir.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(source_path))

segment_colors = {
    "Torso": (0.25, 0.48, 0.22, 1.0),
    "Head": (0.78, 0.30, 0.18, 1.0),
    "Arm_L": (0.18, 0.48, 0.78, 1.0),
    "Arm_R": (0.16, 0.70, 0.70, 1.0),
    "Leg_L": (0.78, 0.62, 0.16, 1.0),
    "Leg_R": (0.72, 0.28, 0.68, 1.0),
}

skins = []
for obj in bpy.context.scene.objects:
    if obj.type != "MESH":
        continue
    obj.hide_render = not obj.name.endswith("_Skin")
    if obj.hide_render:
        continue
    skins.append(obj)
    for segment, color in segment_colors.items():
        if obj.name.startswith(segment):
            obj.color = color
            break

corners = [obj.matrix_world @ Vector(corner) for obj in skins for corner in obj.bound_box]
minimum = Vector(tuple(min(point[index] for point in corners) for index in range(3)))
maximum = Vector(tuple(max(point[index] for point in corners) for index in range(3)))
center = (minimum + maximum) * 0.5
size = maximum - minimum

bpy.ops.object.camera_add()
camera = bpy.context.object
camera.data.type = "ORTHO"
camera.data.ortho_scale = max(size.x, size.z) * 1.16
bpy.context.scene.camera = camera

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.studio_light = "paint.sl"
scene.display.shading.color_type = "OBJECT"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = "WORLD"
scene.render.resolution_x = 700
scene.render.resolution_y = 700
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = True

views = {
    "front": center + Vector((0.0, -4.0, 0.0)),
    "side": center + Vector((4.0, 0.0, 0.0)),
    "back": center + Vector((0.0, 4.0, 0.0)),
}
for name, location in views.items():
    camera.location = location
    look_at(camera, center)
    scene.render.filepath = str(output_dir / f"segmentation_{name}.png")
    bpy.ops.render.render(write_still=True)
