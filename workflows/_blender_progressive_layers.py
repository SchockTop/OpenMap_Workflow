"""_blender_progressive_layers.py — render the same camera with progressively
added layers, one PNG per layer.

Sequence (each frame is the previous frame plus ONE more layer):

    00_sky               — empty world + sky only
    01_terrain_flat      — bare flat plane (no displacement, no texture)
    02_ortho_drape       — DOP / orthophoto draped on the plane (THE GROUND)
    03_heightmap         — DGM heightmap displacement applied
    04_ground_shader     — layered slope/altitude shader on top of ortho
    05_groundcover       — scattered ground props (rocks/grass tufts)
    06_trees             — vegetation scatter
    07_buildings         — LoD2 buildings
    08_atmosphere        — haze / domain cube

The point of this test is to make it visually obvious whether each layer is
actually being applied. In particular: frame 02 must look DIFFERENT from
frame 01 — that difference is the orthophoto. If 01 and 02 look the same,
the ortho drape is not landing on the terrain.

Run as: blender --background --python this_script.py -- \\
    --heightmap <path> --ortho-dir <path> --cityjson <path> \\
    --bbox-utm32n XMIN YMIN XMAX YMAX --out-dir <path>
"""
from __future__ import annotations
import argparse, importlib, math, sys
from pathlib import Path

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--heightmap", required=True)
ap.add_argument("--ortho-dir", default="")
ap.add_argument("--cityjson", default="")
ap.add_argument("--bbox-utm32n", nargs=4, type=float, required=True,
                metavar=("XMIN", "YMIN", "XMAX", "YMAX"))
ap.add_argument("--out-dir", required=True)
ap.add_argument("--engine", default="BLENDER_EEVEE_NEXT")
ap.add_argument("--resolution", nargs=2, type=int, default=[960, 540])
args = ap.parse_args(argv)

OUT = Path(args.out_dir)
OUT.mkdir(parents=True, exist_ok=True)

ext = importlib.import_module("bl_ext.user_default.blender_tools")
terrain_setup    = importlib.import_module("bl_ext.user_default.blender_tools.terrain_setup")
citygml_import   = importlib.import_module("bl_ext.user_default.blender_tools.citygml_import")
cinematic_preset = importlib.import_module("bl_ext.user_default.blender_tools.cinematic_preset")
sky_presets      = importlib.import_module("bl_ext.user_default.blender_tools.sky_presets")
features_mod     = importlib.import_module("bl_ext.user_default.blender_tools.features")


# ---------------------------------------------------------------- scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

valid = {item.identifier for item in
         bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
for candidate in (args.engine, "BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    if candidate in valid:
        scene.render.engine = candidate
        break
scene.render.resolution_x, scene.render.resolution_y = args.resolution
scene.render.image_settings.file_format = "PNG"
try:
    scene.view_settings.view_transform = "AgX"
except (TypeError, AttributeError):
    pass


# ---------------------------------------------------------------- camera (fixed)
xmin, ymin, xmax, ymax = args.bbox_utm32n
size_x, size_y = xmax - xmin, ymax - ymin
anchor = (xmin + size_x / 2, ymin + size_y / 2, 0.0)

# Camera looks down at ~25° tilt from a 600 m altitude — high enough to see
# the whole bbox, low enough that ortho detail is recognisable.
cam_alt = max(400.0, 0.4 * max(size_x, size_y))
bpy.ops.object.camera_add(
    location=(0.0, -0.5 * size_y - 200, cam_alt),
    rotation=(math.radians(60), 0, 0),
)
cam = bpy.context.active_object
cam.data.lens = 35.0
cam.data.clip_end = 50_000.0
scene.camera = cam


# ---------------------------------------------------------------- helpers
def render_layer(idx: int, name: str) -> Path:
    out = OUT / f"{idx:02d}_{name}.png"
    scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)
    print(f"[progressive] layer {idx:02d} {name!r} -> {out.name}")
    return out


# ---------------------------------------------------------------- 00 sky only
bpy.ops.blender_tools.setup_sky(preset="client-default")
sky_presets.apply_sky_preset(scene, "afternoon")
render_layer(0, "sky")


# ---------------------------------------------------------------- 01 flat terrain
# Bare plane at z=0, NO heightmap displacement, NO ortho drape.
bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0, 0, 0))
plane = bpy.context.active_object
plane.name = "TerrainPlane"
plane.scale = (size_x, size_y, 1.0)
bpy.ops.object.transform_apply(scale=True)
flat_mat = bpy.data.materials.new("FlatGround")
flat_mat.use_nodes = True
bsdf = flat_mat.node_tree.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = (0.30, 0.27, 0.20, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.95
plane.data.materials.append(flat_mat)
render_layer(1, "terrain_flat")


# ---------------------------------------------------------------- 02 ortho drape
# THE critical frame: same plane, same camera, but DOP draped on top.
# If this frame is identical to 01, the orthophoto is not making it onto the
# terrain — that is the bug the user is chasing.
if args.ortho_dir and Path(args.ortho_dir).is_dir():
    plane.data.materials.clear()
    terrain_setup.apply_ortho_drape(plane, args.ortho_dir)
    render_layer(2, "ortho_drape")
else:
    print(f"[progressive] WARN: --ortho-dir missing or empty, layer 02 skipped")


# ---------------------------------------------------------------- 03 heightmap
# Replace the flat plane with the real DGM-displaced terrain (keeps ortho mat).
bpy.data.objects.remove(plane, do_unlink=True)
plane = terrain_setup.build_terrain_from_heightmap(
    heightmap_exr=args.heightmap,
    size_meters=(size_x, size_y),
    subdivisions=11,
    strength=1.0,
    anchor_utm32n=anchor,
)
if args.ortho_dir and Path(args.ortho_dir).is_dir():
    terrain_setup.apply_ortho_drape(plane, args.ortho_dir)
render_layer(3, "heightmap")


# ---------------------------------------------------------------- shared feature context
def feat_ctx() -> dict:
    return {
        "bpy": bpy, "scene": scene,
        "terrain_obj": plane, "dop_image": None,
        "ortho_dir": Path(args.ortho_dir) if args.ortho_dir else None,
        "building_objs": [],
        "bbox_utm32n": tuple(args.bbox_utm32n),
        "anchor_utm32n": anchor,
        "args": args,
    }


# ---------------------------------------------------------------- 04 ground-shader
try:
    features_mod.apply_enabled(["ground-shader"], feat_ctx())
    render_layer(4, "ground_shader")
except Exception as e:
    print(f"[progressive] WARN: ground-shader failed ({e})")


# ---------------------------------------------------------------- 05 groundcover
try:
    features_mod.apply_enabled(["groundcover"], feat_ctx())
    render_layer(5, "groundcover")
except Exception as e:
    print(f"[progressive] WARN: groundcover failed ({e})")


# ---------------------------------------------------------------- 06 trees
try:
    features_mod.apply_enabled(["trees"], feat_ctx())
    render_layer(6, "trees")
except Exception as e:
    print(f"[progressive] WARN: trees failed ({e})")


# ---------------------------------------------------------------- 07 buildings
building_objs = []
if args.cityjson and Path(args.cityjson).is_file():
    building_objs = citygml_import.cityjson_to_blender(
        Path(args.cityjson),
        anchor_utm32n=anchor,
        terrain_object_name=plane.name,
    )
    print(f"[progressive] {len(building_objs)} building(s) imported")
    try:
        ctx = feat_ctx()
        ctx["building_objs"] = building_objs
        features_mod.apply_enabled(["buildings-textured"], ctx)
    except Exception as e:
        print(f"[progressive] WARN: buildings-textured failed ({e})")
render_layer(7, "buildings")


# ---------------------------------------------------------------- 08 atmosphere
bpy.ops.blender_tools.add_domain_cube(bbox=(size_x, size_y, 80.0),
                                      preset="airbus-clean")
cinematic_preset.apply_cinematic_preset(scene, render_engine=scene.render.engine,
                                        quality="preview")
sky_presets.apply_sky_preset(scene, "afternoon")
render_layer(8, "atmosphere")

print(f"[progressive] done — {len(list(OUT.glob('*.png')))} frames in {OUT}")
