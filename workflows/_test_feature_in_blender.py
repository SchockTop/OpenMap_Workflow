"""_test_feature_in_blender.py — per-feature render harness."""
import argparse, importlib, sys
from pathlib import Path
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--feature", required=True)
ap.add_argument("--out-dir", required=True)
args = ap.parse_args(argv)

ext = importlib.import_module("bl_ext.user_default.blender_tools")
features_mod = importlib.import_module("bl_ext.user_default.blender_tools.features")


def build_synthetic_scene():
    """Reset to empty + add cube building + camera + sun."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Cube as fake building (10 m wide, 8 m tall).
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 4))
    cube = bpy.context.active_object
    cube.name = "CityJSON_TEST_001"
    cube.scale = (5.0, 5.0, 4.0)
    bpy.ops.object.transform_apply(scale=True)
    # Camera looking at it.
    bpy.ops.object.camera_add(location=(20, -20, 12),
                              rotation=(1.1, 0, 0.785))
    bpy.context.scene.camera = bpy.context.active_object
    # Sun.
    bpy.ops.object.light_add(type="SUN", location=(0, 0, 20))
    bpy.context.active_object.data.energy = 5
    bpy.context.scene.render.resolution_x = 512
    bpy.context.scene.render.resolution_y = 384
    # Engine name varies between Blender 4.2 and 5.x.
    valid = {item.identifier for item in
             bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        if candidate in valid:
            bpy.context.scene.render.engine = candidate
            break
    return cube


def render(out_path):
    bpy.context.scene.render.image_settings.file_format = "PNG"
    bpy.context.scene.render.filepath = str(out_path)
    bpy.ops.render.render(write_still=True)


out_dir = Path(args.out_dir)

# Render 1: baseline (no feature).
cube = build_synthetic_scene()
render(out_dir / "baseline")  # Blender appends .png

# Render 2: with feature.
cube = build_synthetic_scene()
context = {"bpy": bpy, "scene": bpy.context.scene,
           "terrain_obj": None, "dop_image": None, "ortho_dir": None,
           "building_objs": [cube], "bbox_utm32n": (-100, -100, 100, 100),
           "anchor_utm32n": (0, 0, 0), "args": args}
features_mod.apply_enabled([args.feature], context)
render(out_dir / "applied")

# Rename Blender's frame-numbered output.
for stem in ("baseline", "applied"):
    target = out_dir / f"{stem}.png"
    candidates = [p for p in out_dir.glob(f"{stem}*.png") if p != target]
    if candidates and not target.exists():
        candidates[0].rename(target)
print(f"[test-feature] rendered baseline + applied for {args.feature!r}")
