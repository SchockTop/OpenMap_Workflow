"""_headless_progressive.py — render progressive layers using pip-installed
bpy (no Blender executable, no GPU).

Imports openmap_blender_tools as a direct sys.path module instead of going
through the extension-loader path (`bl_ext.user_default.blender_tools`),
because pip-installed bpy doesn't have the extension installed.

Renders Cycles CPU at low samples — this is a plumbing test, not a beauty
shot. We only care: does layer 02 (ortho drape) look different from layer
01 (flat plane)? And does layer 03 (heightmap displacement) add real shape?
"""
from __future__ import annotations
import math, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "openmap_blender_tools"))

import bpy  # noqa: E402
import terrain_setup  # noqa: E402

OUT = ROOT / "showcase" / "ground_layer_test_synth"
OUT.mkdir(parents=True, exist_ok=True)

HEIGHTMAP = ROOT / "data" / "synth" / "heightmap.png"
ORTHO_DIR = ROOT / "data" / "synth" / "ortho_udim"
SIZE_X, SIZE_Y = 1000.0, 1000.0  # synthetic 1km bbox
ANCHOR = (0.0, 0.0, 0.0)


# ---------------------------------------------------------------- scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 32  # cheap — this is plumbing not final art
scene.render.resolution_x, scene.render.resolution_y = 800, 480
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "AgX" if "AgX" in {
    e.identifier for e in
    scene.view_settings.bl_rna.properties["view_transform"].enum_items
} else "Standard"

# Sun.
bpy.ops.object.light_add(type="SUN", location=(0, 0, 500))
sun = bpy.context.active_object
sun.data.energy = 4.0
sun.data.angle = 0.05
sun.rotation_euler = (math.radians(50), 0, math.radians(30))

# Sky world background.
world = bpy.data.worlds.new("SkyWorld")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
bg.inputs["Color"].default_value = (0.45, 0.6, 0.85, 1.0)
bg.inputs["Strength"].default_value = 1.2

# Camera — same position for every frame so differences are caused by layers
# only, not by composition. ~600 m above the centre of the bbox, looking down
# at 60° tilt so we see both ground and a slice of sky.
bpy.ops.object.camera_add(location=(0.0, -700.0, 600.0),
                          rotation=(math.radians(60), 0.0, 0.0))
cam = bpy.context.active_object
cam.data.lens = 35.0
cam.data.clip_end = 50_000.0
scene.camera = cam


def render_layer(idx: int, name: str) -> Path:
    out = OUT / f"{idx:02d}_{name}.png"
    scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)
    print(f"[progressive] {idx:02d} {name:<20} -> {out.name} ({out.stat().st_size//1024} KB)")
    return out


# ---------------------------------------------------------------- 00 sky only
render_layer(0, "sky")


# ---------------------------------------------------------------- 01 flat plane
bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0, 0, 0))
plane = bpy.context.active_object
plane.name = "FlatPlane"
plane.scale = (SIZE_X, SIZE_Y, 1.0)
bpy.ops.object.transform_apply(scale=True)
flat_mat = bpy.data.materials.new("FlatGround")
flat_mat.use_nodes = True
bsdf = flat_mat.node_tree.nodes.get("Principled BSDF")
bsdf.inputs["Base Color"].default_value = (0.30, 0.27, 0.20, 1.0)
bsdf.inputs["Roughness"].default_value = 0.95
plane.data.materials.append(flat_mat)
render_layer(1, "terrain_flat")


# ---------------------------------------------------------------- 02 ortho drape
# This is THE diagnostic frame. If it looks identical to 01 the drape didn't
# land on the terrain.
plane.data.materials.clear()
# Need UVs on the flat plane for the drape's UVMap node to do anything.
bpy.ops.object.select_all(action="DESELECT")
plane.select_set(True)
bpy.context.view_layer.objects.active = plane
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=1.15)
bpy.ops.object.mode_set(mode="OBJECT")
terrain_setup.apply_ortho_drape(plane, ORTHO_DIR)
render_layer(2, "ortho_drape")


# ---------------------------------------------------------------- 03 heightmap displaced terrain
bpy.data.objects.remove(plane, do_unlink=True)
plane = terrain_setup.build_terrain_from_heightmap(
    heightmap_exr=str(HEIGHTMAP),
    size_meters=(SIZE_X, SIZE_Y),
    subdivisions=8,    # 257 verts per side — modest but visible
    strength=80.0,     # exaggerate hills so silhouette reads
    anchor_utm32n=ANCHOR,
)
terrain_setup.apply_ortho_drape(plane, ORTHO_DIR)
render_layer(3, "heightmap_plus_drape")


print(f"\n[progressive] done — {len(list(OUT.glob('*.png')))} frames in {OUT}")
