"""mnpreview: Workbench renders of the Blender scene a model script just built, so geometry can be judged in seconds without a Unity build.
Flat material colours, a studio light with shadows and cavity: this checks silhouettes, layering and scale, not the final look (that is Unity).
Views are (name, camera position, look-at) in Blender ship space (x right, y forward, z up)."""
import math
import os

import bpy
import mathutils

DEFAULT_COLOURS = {"mn_earth_plate": (0.16, 0.16, 0.18, 1), "mn_earth_trim": (0.5, 0.5, 0.55, 1), "mn_earth_lights": (1.0, 0.95, 0.8, 1), "mn_earth_engine": (1.0, 0.5, 0.2, 1),
                   "mn_earth_plate_b": (0.09, 0.09, 0.10, 1), "mn_earth_plate_c": (0.26, 0.26, 0.28, 1), "mn_earth_plate_d": (0.40, 0.40, 0.42, 1), "mn_earth_plate_e": (0.55, 0.55, 0.58, 1),
                   "mn_earth_chrome": (0.62, 0.64, 0.68, 1), "mn_earth_plume": (0.5, 0.65, 1.0, 1), "mn_earth_wear": (0.16, 0.16, 0.18, 1), "mn_earth_paint": (0.16, 0.16, 0.18, 1)}


def render_views(out_dir, views, size=(1280, 720), sun=(0.4, 0.55, -0.75), colours=None, lens=50.0, hide=("_COL",), only_lod=0, show=(), clip=(0.5, 5000.0)):
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    sh = scene.display.shading
    sh.light = "STUDIO"
    sh.color_type = "MATERIAL"
    sh.show_shadows = True
    sh.shadow_intensity = 0.55
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.background_type = "VIEWPORT"
    scene.display.light_direction = mathutils.Vector(sun)
    try:
        scene.world = scene.world or bpy.data.worlds.new("w")
        scene.world.color = (0.01, 0.012, 0.02)
    except Exception:
        pass
    for name, col in (colours or DEFAULT_COLOURS).items():
        m = bpy.data.materials.get(name)
        if m:
            m.diffuse_color = col
    # which objects to show: one LOD, the subsystems, damage-free
    for o in bpy.data.objects:
        n = o.name
        vis = True
        if any(h in n for h in hide):
            vis = False
        if "_LOD" in n and not n.endswith(f"_LOD{only_lod}"):
            vis = False
        if "__destroyed" in n or "__damaged" in n or "__debris" in n or "__breach" in n:
            vis = any(s in n for s in show)
        o.hide_render = not vis
    cam_data = bpy.data.cameras.new("pv")
    cam_data.lens = lens
    cam_data.clip_start, cam_data.clip_end = clip
    cam = bpy.data.objects.new("pv", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, pos, target, *rest in views:
        cam_data.lens = rest[0] if rest else lens
        cam.location = mathutils.Vector(pos)
        d = mathutils.Vector(target) - cam.location
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
        print(f"[MN] preview {scene.render.filepath}")
