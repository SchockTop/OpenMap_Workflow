"""Blender-side scene assembler. Invoked by tile_to_blender_scene.py."""
import argparse
import importlib
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--heightmap", required=True)
ap.add_argument("--out", required=True)
args = ap.parse_args(argv)

ext = importlib.import_module("bl_ext.user_default.blender_tools")
print(f"[blender] extension v{ext.__version__}")

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for c in list(bpy.data.collections):
    if c.name != "Collection":
        bpy.data.collections.remove(c)

# Sky + atmospheric haze.
bpy.ops.blender_tools.setup_sky(preset="client-default")
bpy.ops.blender_tools.add_domain_cube(bbox=(1000.0, 1000.0, 200.0), preset="airbus-clean")

# Terrain from real DGM1.
terrain_setup = importlib.import_module("bl_ext.user_default.blender_tools.terrain_setup")
terrain_setup.build_terrain_from_heightmap(
    heightmap_exr=args.heightmap,
    size_meters=(1000.0, 1000.0),
    subdivisions=8,
    strength=30.0,
    anchor_utm32n=(691000.0, 5334000.0, 0.0),
)
print(f"[blender] terrain built from {args.heightmap}")

bpy.ops.wm.save_as_mainfile(filepath=args.out)
print(f"[blender] saved: {args.out}")
