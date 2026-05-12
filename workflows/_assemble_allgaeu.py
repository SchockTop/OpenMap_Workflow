"""_assemble_allgaeu.py — headless scene assembly for the Allgäu fly-over.

Runs inside:
    blender --background --python workflows/_assemble_allgaeu.py

Uses the installed `bl_ext.user_default.blender_tools` extension.
Calls individual operators in order so we can control the terrain
subdivision level (auto would pick subdiv 13-14 for a 1 m/px DGM1 →
~268 M quads; we cap at subdiv 10 = ~1 M quads which is fine for Eevee
preview at 9 km aerial scale).

Outputs:
    data/scene_allgaeu-forggensee.blend
    workflows/scenes/allgaeu-flyover/renders/allgaeu_v1_frame{NNNN}.png
    (4 frames at ~10 %, 35 %, 60 %, 90 % of the camera path)
"""
from __future__ import annotations

import importlib
import math
import os
import shutil
import sys
import time
from pathlib import Path

import bpy

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

WORKFLOW_ROOT = Path(__file__).resolve().parent.parent
DATA_PROCESSED = WORKFLOW_ROOT / "data" / "processed"
FLIGHT_PATH_SRC = WORKFLOW_ROOT / "data" / "flight_path.csv"
FLIGHT_PATH_DST = DATA_PROCESSED / "flight_path.csv"
# UTM32N pre-converted version (no pyproj in Blender Python — use Anaconda-preconverted)
FLIGHT_PATH_UTM = DATA_PROCESSED / "flight_path_utm.csv"
OUT_BLEND = WORKFLOW_ROOT / "data" / "scene_allgaeu-forggensee.blend"
RENDERS_DIR = WORKFLOW_ROOT / "workflows" / "scenes" / "allgaeu-flyover" / "renders"
RENDERS_DIR.mkdir(parents=True, exist_ok=True)

# Terrain subdivision: 10 → 1025 verts/side ≈ 1.05 M quads (9.2 km / 1024 ≈ 9 m/quad).
# A full-res 1 m/px DGM1 would be subdiv 14 (268 M quads) — far too heavy.
# Subdiv 10 is the preview-quality cap for this scene size.
TERRAIN_SUBDIV = 10

# Render config
RENDER_WIDTH = 960
RENDER_HEIGHT = 540

# ---------------------------------------------------------------------------
# Ensure flight_path.csv is in the processed folder (op looks there)
# ---------------------------------------------------------------------------

if FLIGHT_PATH_SRC.is_file() and not FLIGHT_PATH_DST.is_file():
    shutil.copy2(str(FLIGHT_PATH_SRC), str(FLIGHT_PATH_DST))
    print(f"[assemble] copied flight_path.csv → {FLIGHT_PATH_DST}")
elif FLIGHT_PATH_DST.is_file():
    print(f"[assemble] flight_path.csv already in processed folder")
else:
    print(f"[assemble] WARN: flight_path.csv not found at {FLIGHT_PATH_SRC}")

# ---------------------------------------------------------------------------
# Load extension
# ---------------------------------------------------------------------------

ext = importlib.import_module("bl_ext.user_default.blender_tools")
print(f"[assemble] extension loaded v{ext.__version__}")

# ---------------------------------------------------------------------------
# Clear default scene
# ---------------------------------------------------------------------------

bpy.ops.wm.read_factory_settings(use_empty=True)

# After factory reset the extension operators need to be re-registered.
# re-importing operators and calling register() is the safe path.
ops_mod = importlib.import_module("bl_ext.user_default.blender_tools.operators")
ops_mod.register()
print("[assemble] operators registered")

scene = bpy.context.scene

# ---------------------------------------------------------------------------
# Step tracking helpers
# ---------------------------------------------------------------------------

steps_done: list[str] = []
steps_warn: list[str] = []
t0_global = time.time()


def _try(name: str, fn, *args, **kwargs):
    t0 = time.time()
    try:
        fn(*args, **kwargs)
        dt = time.time() - t0
        steps_done.append(f"{name} ({dt:.1f}s)")
        print(f"[assemble] OK  {name} ({dt:.1f}s)")
    except Exception as exc:
        dt = time.time() - t0
        steps_warn.append(f"{name}: {exc}")
        print(f"[assemble] WARN {name}: {exc}")


# ---------------------------------------------------------------------------
# 1. Terrain — import_heightmap with explicit subdivision cap
# ---------------------------------------------------------------------------

def _do_terrain():
    heightmap = DATA_PROCESSED / "heightmap.tif"
    if not heightmap.is_file():
        raise FileNotFoundError(f"heightmap.tif not found: {heightmap}")
    result = bpy.ops.blender_tools.import_heightmap(
        "EXEC_DEFAULT",
        filepath=str(heightmap),
        subdivisions=TERRAIN_SUBDIV,
        strength=1.0,
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"import_heightmap returned {result}")

_try("terrain", _do_terrain)

terrain_name = scene.get("terrain_object_name", "")
print(f"[assemble] terrain object: {terrain_name!r}")

# ---------------------------------------------------------------------------
# 2. Ortho drape — pre-processed UDIM tiles
# ---------------------------------------------------------------------------

def _do_ortho():
    udim_dir = DATA_PROCESSED / "ortho_udim"
    if not udim_dir.is_dir():
        raise FileNotFoundError(f"ortho_udim/ not found: {udim_dir}")
    # Find any jpg to pass as a file entry so the op finds the dir.
    jpgs = sorted(udim_dir.glob("ortho.*.jpg"))
    if not jpgs:
        raise FileNotFoundError(f"No ortho.*.jpg in {udim_dir}")

    terrain_setup_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.terrain_setup")
    terrain_obj = bpy.data.objects.get(terrain_name) if terrain_name else None
    if terrain_obj is None:
        for obj in bpy.data.objects:
            if obj.type == "MESH" and obj.name.startswith("Terrain"):
                terrain_obj = obj
                break
    if terrain_obj is None:
        raise RuntimeError("terrain object not found — cannot apply ortho drape")
    terrain_setup_mod.apply_ortho_drape(terrain_obj, str(udim_dir))
    scene["ortho_dir"] = str(udim_dir)

_try("ortho drape", _do_ortho)

# ---------------------------------------------------------------------------
# 3. Buildings
# ---------------------------------------------------------------------------

def _do_buildings():
    cityjson = DATA_PROCESSED / "buildings.cityjson"
    if not cityjson.is_file():
        raise FileNotFoundError(f"buildings.cityjson not found: {cityjson}")
    result = bpy.ops.blender_tools.import_buildings(
        "EXEC_DEFAULT",
        filepath=str(cityjson),
        collection_name="Buildings",
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"import_buildings returned {result}")

_try("buildings", _do_buildings)

building_count = 0
for coll in bpy.data.collections:
    if coll.name in ("Buildings", "CityJSON"):
        building_count = len([o for o in coll.objects if o.type == "MESH"])
        break
print(f"[assemble] building count: {building_count}")

# ---------------------------------------------------------------------------
# 4. Building textures (DOP roof projection)
# ---------------------------------------------------------------------------

def _do_bld_tex():
    # Only apply if buildings were imported.
    if building_count == 0:
        raise RuntimeError("no buildings — skip building textures")
    result = bpy.ops.blender_tools.apply_building_textures("EXEC_DEFAULT")
    if result not in ({"FINISHED"}, {"FINISHED"}):
        raise RuntimeError(f"apply_building_textures returned {result}")

_try("building textures", _do_bld_tex)

# ---------------------------------------------------------------------------
# 5. Cinematic preset — creates Sun light + World sky + AgX view transform.
#    Must run before apply_sky_preset, which needs a Sun light to reconfigure.
# ---------------------------------------------------------------------------

def _do_cinematic():
    cinematic_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.cinematic_preset")
    # Use CYCLES for the cinematic preset setup — Eevee needs a display context
    # in --background mode on Windows and silently produces empty renders.
    cinematic_mod.apply_cinematic_preset(
        scene,
        render_engine="CYCLES",
        resolution=(RENDER_WIDTH, RENDER_HEIGHT),
    )

_try("cinematic preset (Sun + World)", _do_cinematic)

# ---------------------------------------------------------------------------
# 6. Sky preset — afternoon (warm, southwesterly, cinematic default)
# ---------------------------------------------------------------------------

def _do_sky():
    result = bpy.ops.blender_tools.apply_sky_preset("EXEC_DEFAULT", preset="afternoon")
    if result != {"FINISHED"}:
        raise RuntimeError(f"apply_sky_preset returned {result}")

_try("sky preset", _do_sky)

# ---------------------------------------------------------------------------
# 7. Quality preset — preview (960×540, TAA 32 samples)
#    After this we override resolution and engine to ensure correct values.
# ---------------------------------------------------------------------------

def _do_quality():
    result = bpy.ops.blender_tools.apply_quality("EXEC_DEFAULT", preset="preview")
    if result != {"FINISHED"}:
        raise RuntimeError(f"apply_quality returned {result}")

_try("quality preset", _do_quality)

# Headless rendering: Eevee requires an OpenGL/GPU context which --background
# does not initialise on Windows; renders silently produce empty files.
# Use CYCLES for headless rendering (CPU path works without a display context).
# Set sample count low for preview speed (~64 samples is fast on CPU).
scene.render.engine = "CYCLES"
scene.cycles.samples = 32
scene.cycles.use_denoising = False  # denoising needs GPU; skip for preview
try:
    # CPU-only in headless mode.
    scene.cycles.device = "CPU"
except Exception:
    pass
scene.render.resolution_x = RENDER_WIDTH
scene.render.resolution_y = RENDER_HEIGHT
scene.render.image_settings.file_format = "PNG"
print(f"[assemble] engine={scene.render.engine}, res={RENDER_WIDTH}x{RENDER_HEIGHT}")

# ---------------------------------------------------------------------------
# 8. Ground shader
# ---------------------------------------------------------------------------

def _do_ground():
    result = bpy.ops.blender_tools.apply_ground_shader("EXEC_DEFAULT")
    if result != {"FINISHED"}:
        raise RuntimeError(f"apply_ground_shader returned {result}")

_try("ground shader", _do_ground)

# ---------------------------------------------------------------------------
# 9. Trees — with forest mask
# ---------------------------------------------------------------------------

def _do_trees():
    mask_tif = DATA_PROCESSED / "forest_mask.tif"
    mask_arg = str(mask_tif) if mask_tif.is_file() else ""
    if mask_arg:
        print(f"[assemble] forest mask: {mask_arg}")
    else:
        print("[assemble] WARN: no forest_mask.tif -- trees will scatter everywhere")
    result = bpy.ops.blender_tools.scatter_trees(
        "EXEC_DEFAULT", mask_geotiff=mask_arg)
    if result != {"FINISHED"}:
        raise RuntimeError(f"scatter_trees returned {result}")

_try("trees", _do_trees)

tree_count = sum(
    1 for obj in bpy.data.objects
    if obj.type == "EMPTY" and "tree" in obj.name.lower()
)
print(f"[assemble] approximate tree-root objects: {tree_count}")

# ---------------------------------------------------------------------------
# 10. Clouds — moderate coverage, base ~2300 m (above Allgäu terrain at 750–1800 m)
# ---------------------------------------------------------------------------

def _do_clouds():
    result = bpy.ops.blender_tools.add_clouds(
        "EXEC_DEFAULT",
        coverage=0.35,
        base_altitude_m=2300.0,
        thickness_m=400.0,
        density=0.03,        # light -- Cycles volumetric is expensive on CPU; keep sparse
        detail=0.4,
        cirrus=False,        # skip cirrus in this pass -- reduce render time
        cirrus_altitude_m=6500.0,
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"add_clouds returned {result}")

_try("clouds", _do_clouds)

cloud_objects = [obj for obj in bpy.data.objects
                 if any(k in obj.name.lower() for k in ("cloud", "cumulus", "cirrus"))]
print(f"[assemble] cloud objects: {[o.name for o in cloud_objects]}")

# ---------------------------------------------------------------------------
# 11. Camera rig from flight_path_utm.csv (UTM32N pre-converted — no pyproj
#     needed in Blender Python; the WGS84 version fails with ModuleNotFoundError).
# ---------------------------------------------------------------------------

def _do_camera():
    # Use the UTM32N pre-converted CSV (pyproj is not available in Blender's Python).
    csv_for_blender = FLIGHT_PATH_UTM if FLIGHT_PATH_UTM.is_file() else FLIGHT_PATH_DST
    if not csv_for_blender.is_file():
        raise FileNotFoundError(f"flight path CSV not found: {csv_for_blender}")
    print(f"[assemble] importing flight path: {csv_for_blender}")

    result = bpy.ops.blender_tools.import_csv_path(
        "EXEC_DEFAULT",
        filepath=str(csv_for_blender),
        name="FlightPath",
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"import_csv_path returned {result}")

    result = bpy.ops.blender_tools.setup_camera_rig(
        "EXEC_DEFAULT",
        banking_max_deg=8.0,
        speed_mps=50.0,
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"setup_camera_rig returned {result}")

    # Apply cinematic camera preset (lens + clip + altitude hints).
    if scene.camera:
        result = bpy.ops.blender_tools.apply_camera_preset(
            "EXEC_DEFAULT", preset="cinematic-establishing")
        if result != {"FINISHED"}:
            raise RuntimeError(f"apply_camera_preset returned {result}")

_try("camera rig", _do_camera)

cam_name = scene.camera.name if scene.camera else "(none)"
print(f"[assemble] scene camera: {cam_name}")
print(f"[assemble] frame range: {scene.frame_start} – {scene.frame_end}")

# ---------------------------------------------------------------------------
# Scene stats
# ---------------------------------------------------------------------------

all_objs = list(bpy.data.objects)
mesh_objs = [o for o in all_objs if o.type == "MESH"]
terrain_obj = next((o for o in mesh_objs if o.name.startswith("Terrain")), None)
terrain_verts = len(terrain_obj.data.vertices) if terrain_obj else 0
print(f"[assemble] total objects: {len(all_objs)}")
print(f"[assemble] mesh objects: {len(mesh_objs)}")
print(f"[assemble] terrain verts (base mesh before Subsurf): {terrain_verts}")
print(f"[assemble] buildings: {building_count}")

# ---------------------------------------------------------------------------
# Save .blend
# ---------------------------------------------------------------------------

OUT_BLEND.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT_BLEND))
print(f"[assemble] saved .blend → {OUT_BLEND}")

# ---------------------------------------------------------------------------
# Render 4 stills
# ---------------------------------------------------------------------------

total_frames = scene.frame_end - scene.frame_start
render_frames = []
if total_frames > 0:
    for frac in (0.10, 0.35, 0.60, 0.90):
        f = int(scene.frame_start + round(frac * total_frames))
        f = max(scene.frame_start, min(scene.frame_end, f))
        render_frames.append(f)
else:
    # Fallback: no curve-based timeline — use static frames.
    render_frames = [1, 25, 50, 75]

print(f"[assemble] rendering frames: {render_frames} (total timeline: {total_frames})")

rendered_paths = []
# Render output: Blender will append frame number if use_file_extension=True.
# To get predictable filenames, set the output to a directory and let Blender
# add NNNN.png, OR disable file extension and set the full path per frame.
# Strategy: use RENDERS_DIR as the base and set filepath to the full path
# with the frame number already embedded. Disable file-extension appending
# and disable frame padding to get exactly the path we specified.
scene.render.use_file_extension = True   # keep .png extension
scene.render.use_render_cache = False

for i, frame in enumerate(render_frames):
    # Set filepath WITHOUT extension — Blender adds it when use_file_extension=True.
    # Pattern: allgaeu_v1_frame0026 → Blender writes allgaeu_v1_frame0026.png
    stem = f"allgaeu_v1_frame{frame:04d}"
    out_path_no_ext = str(RENDERS_DIR / stem)
    expected_path = str(RENDERS_DIR / f"{stem}.png")

    scene.frame_set(frame)
    if scene.camera is None:
        print(f"[assemble] WARN: no scene camera for frame {frame}, skipping render")
        continue

    scene.render.filepath = out_path_no_ext
    t_render = time.time()
    try:
        bpy.ops.render.render(write_still=True)
        dt = time.time() - t_render
        # Check both the expected path and the path-with-frame-number appended.
        actual = None
        for candidate in [expected_path, f"{out_path_no_ext}.png",
                           f"{out_path_no_ext}0001.png"]:
            if Path(candidate).is_file():
                actual = candidate
                break
        if actual:
            print(f"[assemble] rendered frame {frame} → {actual} ({dt:.1f}s)")
            rendered_paths.append(actual)
        else:
            print(f"[assemble] WARN render frame {frame}: file not found after render ({dt:.1f}s); "
                  f"tried {expected_path}")
    except Exception as e:
        print(f"[assemble] WARN render frame {frame}: {e}")

# Save again with frame set to start.
scene.frame_set(scene.frame_start)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT_BLEND))
print(f"[assemble] final save → {OUT_BLEND}")

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

dt_total = time.time() - t0_global
print("\n" + "=" * 70)
print("ALLGÄU ASSEMBLY SUMMARY")
print("=" * 70)
print(f"Total time:       {dt_total:.0f}s")
print(f"Steps done:       {', '.join(steps_done)}")
if steps_warn:
    print(f"Warnings:         {'; '.join(steps_warn)}")
print(f"Terrain subdiv:   {TERRAIN_SUBDIV} → {2**TERRAIN_SUBDIV + 1} verts/side")
print(f"Terrain verts:    {terrain_verts:,} (base mesh)")
print(f"Buildings:        {building_count}")
print(f"Cloud objects:    {len(cloud_objects)}")
print(f"Camera:           {cam_name}")
print(f"Frame range:      {scene.frame_start}–{scene.frame_end}")
print(f"Engine:           {scene.render.engine}")
print(f"Resolution:       {scene.render.resolution_x}×{scene.render.resolution_y}")
print(f".blend:           {OUT_BLEND}")
print(f"Renders ({len(rendered_paths)}):")
for p in rendered_paths:
    print(f"  {p}")
print("=" * 70)
