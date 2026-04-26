"""_render_with_preset.py - apply preset to camera + render one frame."""
import argparse, importlib, sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--preset", required=True)
ap.add_argument("--out", required=True)
args = ap.parse_args(argv)

cp = importlib.import_module("bl_ext.user_default.blender_tools.camera_presets")
cam = bpy.context.scene.camera
if cam is not None:
    curve = next((o for o in bpy.data.objects if o.type == "CURVE"), None)
    cp.apply_camera_preset(cam, args.preset, scene=bpy.context.scene,
                           curve_obj=curve, terrain_z=520.0)
else:
    print("[render-preset] no scene camera found; rendering with whatever exists")

bpy.context.scene.render.resolution_x = 480
bpy.context.scene.render.resolution_y = 270
bpy.context.scene.render.image_settings.file_format = "PNG"
bpy.context.scene.render.filepath = args.out
bpy.ops.render.render(write_still=True)
print(f"[render-preset] {args.preset} -> {args.out}")
