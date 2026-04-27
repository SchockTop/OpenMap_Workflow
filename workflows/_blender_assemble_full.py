"""_blender_assemble_full.py — Blender-side scene builder for full_pipeline.

Runs inside `blender --background --python`. Reads CLI args, loads the
extension, builds terrain + drape + buildings + camera + applies cinematic
preset, saves .blend, optionally renders one preview frame.
"""
import argparse, importlib, sys
from pathlib import Path
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--heightmap", required=True)
ap.add_argument("--ortho-dir", default="")
ap.add_argument("--cityjson", default="")
ap.add_argument("--waypoints-csv", default="")
ap.add_argument("--bbox-utm32n", nargs=4, type=float, required=True,
                metavar=("XMIN", "YMIN", "XMAX", "YMAX"))
ap.add_argument("--out-blend", required=True)
ap.add_argument("--render-png", default="")
ap.add_argument("--engine", default="BLENDER_EEVEE_NEXT")
ap.add_argument("--enable", nargs="*", default=[],
                help="Feature module names to apply, e.g. buildings-textured trees")
ap.add_argument("--camera-preset", default="cinematic-establishing",
                help="Named camera envelope from camera_presets.CAMERA_PRESETS")
ap.add_argument("--sky-preset", default="afternoon",
                choices=["noon", "golden-hour", "blue-hour", "dawn", "overcast", "afternoon"],
                help="Named time-of-day lighting mood from sky_presets.SKY_PRESETS")
args = ap.parse_args(argv)

ext = importlib.import_module("bl_ext.user_default.blender_tools")
print(f"[blender] extension v{ext.__version__}")

# Clean the default scene without unloading the extension.
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for c in list(bpy.data.collections):
    if c.name != "Collection":
        bpy.data.collections.remove(c)

terrain_setup       = importlib.import_module("bl_ext.user_default.blender_tools.terrain_setup")
citygml_import      = importlib.import_module("bl_ext.user_default.blender_tools.citygml_import")
waypoints_to_camera = importlib.import_module("bl_ext.user_default.blender_tools.waypoints_to_camera")
cinematic_preset    = importlib.import_module("bl_ext.user_default.blender_tools.cinematic_preset")
camera_presets      = importlib.import_module("bl_ext.user_default.blender_tools.camera_presets")

# 1. Sky + atmosphere domain cube.
xmin, ymin, xmax, ymax = args.bbox_utm32n
size_x, size_y = xmax - xmin, ymax - ymin
bpy.ops.blender_tools.setup_sky(preset="client-default")
bpy.ops.blender_tools.add_domain_cube(
    bbox=(size_x, size_y, max(300.0, size_y / 4)),
    preset="airbus-clean",
)

# 2. Terrain Subsurf + Displace from heightmap.
anchor = (xmin + size_x / 2, ymin + size_y / 2, 0.0)
plane = terrain_setup.build_terrain_from_heightmap(
    heightmap_exr=args.heightmap,
    size_meters=(size_x, size_y),
    subdivisions=11,
    strength=1.0,
    anchor_utm32n=anchor,
)
print(f"[blender] terrain built ({size_x:.0f} x {size_y:.0f} m)")

# 3. DOP ortho drape (if tiles available).
if args.ortho_dir and Path(args.ortho_dir).is_dir():
    terrain_setup.apply_ortho_drape(plane, args.ortho_dir)
    print(f"[blender] ortho drape from {args.ortho_dir}")

# 4. LoD2 buildings (if CityJSON available).
building_objs = []
if args.cityjson and Path(args.cityjson).is_file():
    building_objs = citygml_import.cityjson_to_blender(
        Path(args.cityjson),
        anchor_utm32n=anchor,
        terrain_object_name=plane.name,
    )
    print(f"[blender] {len(building_objs)} building(s) imported")

# 5. Camera fly-over (if waypoints CSV available).
if args.waypoints_csv and Path(args.waypoints_csv).is_file():
    curve = waypoints_to_camera.wgs84_csv_to_bezier(
        args.waypoints_csv, anchor_utm32n=anchor,
        curve_name="FlightPath", fps=25, speed_mps=50.0,
    )
    cam = waypoints_to_camera.attach_camera_rig(
        curve, camera_name="HeroCam", banking_max_deg=8.0,
    )
    cam.data.lens = 85.0
    cinematic_preset.set_camera_clip_for_large_scene(cam.data)
    bpy.context.scene.camera = cam
    print(f"[blender] flight camera attached (curve={curve.name}, cam={cam.name})")
    # Apply named camera envelope (overrides lens/altitude/speed per preset).
    camera_presets.apply_camera_preset(
        cam, args.camera_preset, scene=bpy.context.scene, curve_obj=curve,
        terrain_z=520.0,  # rough Munich elevation; refine later via heightmap sample
    )
    print(f"[blender] camera preset applied: {args.camera_preset}")

# 6. Cinematic preset.
cinematic_preset.apply_cinematic_preset(bpy.context.scene, render_engine=args.engine)
print(f"[blender] preset applied (engine={args.engine})")

# 6a. Sky / time-of-day mood preset (overrides cinematic_preset's sun defaults).
sky_presets = importlib.import_module("bl_ext.user_default.blender_tools.sky_presets")
sky_presets.apply_sky_preset(bpy.context.scene, args.sky_preset)
print(f"[blender] sky preset applied: {args.sky_preset}")

# 6b. Feature-registry hook: apply optional plug-in features.
if args.enable:
    features_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.features"
    )
    feat_context = {
        "bpy": bpy,
        "scene": bpy.context.scene,
        "terrain_obj": plane,
        "dop_image": None,
        "ortho_dir": Path(args.ortho_dir) if args.ortho_dir else None,
        "building_objs": building_objs,
        "bbox_utm32n": tuple(args.bbox_utm32n),
        "anchor_utm32n": anchor,
        "args": args,
    }
    features_mod.apply_enabled(args.enable, feat_context)

bpy.ops.wm.save_as_mainfile(filepath=args.out_blend)
print(f"[blender] saved scene: {args.out_blend}")

# 7. Optional preview render.
if args.render_png:
    bpy.context.scene.render.image_settings.file_format = "PNG"
    bpy.context.scene.render.filepath = args.render_png
    bpy.ops.render.render(write_still=True)
    print(f"[blender] rendered: {args.render_png}")
