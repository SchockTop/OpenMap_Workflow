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
    workflows/scenes/allgaeu-flyover/renders/allgaeu_v4_frame{NNNN}.png
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

# Line-buffer stdout so `[assemble]` progress is visible while Blender runs
# under `--background` with a piped stdout (otherwise prints only flush at exit).
try:
    sys.stdout.reconfigure(line_buffering=True)
except Exception:
    pass

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

# Render config — 1920×1080 with OIDN denoise.
# 64 spp + OIDN is equivalent to ~192 spp without denoise in wall-clock terms
# on a CPU render (OIDN runs in seconds vs 3× the render time for extra samples).
# For a quick look-check, override via env ALLGAEU_PREVIEW=1 (→ 960×540, 32 spp).
import os as _os
_PREVIEW = _os.environ.get("ALLGAEU_PREVIEW", "") == "1"
RENDER_WIDTH = 960 if _PREVIEW else 1920
RENDER_HEIGHT = 540 if _PREVIEW else 1080

# Cinematic camera: forward-look angle off nadir.
# 0 = straight down (nadir); 50 = 50° off nadir (strong forward look).
# Blender camera at rotation(0,0,0) points down -Z; rotating x by +50° tilts
# toward the horizon, giving a 50° off-nadir cinematic angle.
NADIR_FORWARD_TILT_DEG = 50.0

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
    # Prefer the nodata-cleaned heightmap (if present). The operator would
    # try to GDAL-mosaic all TIFs in the directory, which fails when there are
    # multiple TIFs (heightmap.tif + heightmap_clean.tif). Bypass GDAL by
    # calling geo_import.geotiff_metadata + terrain_setup.build_terrain_from_heightmap
    # directly on the single pre-processed file.
    heightmap = DATA_PROCESSED / "heightmap_clean.tif"
    if not heightmap.is_file():
        heightmap = DATA_PROCESSED / "heightmap.tif"
        print("[assemble] WARN: heightmap_clean.tif missing, using heightmap.tif")
    if not heightmap.is_file():
        raise FileNotFoundError(f"heightmap not found: {heightmap}")
    print(f"[assemble] heightmap: {heightmap.name}")

    geo_import_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.geo_import")
    terrain_setup_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.terrain_setup")

    meta = geo_import_mod.geotiff_metadata(heightmap)
    size_x = meta["size_meters_x"]
    size_y = meta["size_meters_y"]
    # Anchor: SW corner in UTM32N. Z = 0 (elevations in the heightmap are real
    # metres above sea-level; we only shift XY to avoid float32 precision loss).
    anchor = (meta["origin_x"], meta["origin_y"] - size_y, 0.0)
    scene["utm32n_anchor"] = list(anchor)
    scene["bbox_utm32n"] = [anchor[0], anchor[1],
                            anchor[0] + size_x, anchor[1] + size_y]

    terrain_obj = terrain_setup_mod.build_terrain_from_heightmap(
        str(heightmap),
        size_meters=(size_x, size_y),
        subdivisions=TERRAIN_SUBDIV,
        strength=1.0,
        anchor_utm32n=anchor,
    )
    scene["terrain_object_name"] = terrain_obj.name
    print(f"[assemble] terrain built: {terrain_obj.name} "
          f"({size_x:.0f}×{size_y:.0f} m, anchor {anchor[0]:.0f},{anchor[1]:.0f})")

_try("terrain", _do_terrain)

terrain_name = scene.get("terrain_object_name", "")
print(f"[assemble] terrain object: {terrain_name!r}")

# ---------------------------------------------------------------------------
# 2. Ortho drape — pre-processed UDIM tiles
#    FIX: after apply_ortho_drape, find the UDIM image datablock and set its
#    filepath to an absolute path using the <UDIM> token form Blender expects,
#    set source='TILED', and reload so Cycles can find the tiles.
# ---------------------------------------------------------------------------

def _do_ortho():
    udim_dir = DATA_PROCESSED / "ortho_udim"
    if not udim_dir.is_dir():
        raise FileNotFoundError(f"ortho_udim/ not found: {udim_dir}")
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

    # FIX: ensure the UDIM image datablock has an absolute filepath using the
    # <UDIM> token so Cycles can locate every tile in headless mode.
    # apply_ortho_drape loads ortho.1001.jpg as the base image and registers
    # tiles; the filepath must use the <UDIM> token form, not point at tile 1001.
    udim_abs = str(udim_dir.resolve())
    for img in bpy.data.images:
        # Match by name pattern (apply_ortho_drape loads ortho.1001.jpg).
        if "ortho" in img.name.lower():
            # Build the <UDIM> path: same directory, same stem pattern.
            udim_token_path = str(udim_dir.resolve() / "ortho.<UDIM>.jpg")
            img.filepath = udim_token_path
            img.source = "TILED"
            # Ensure all tiles are registered with absolute paths.
            if hasattr(img, "tiles"):
                for tile in img.tiles:
                    tile_num = tile.number
                    tile_file = udim_dir.resolve() / f"ortho.{tile_num}.jpg"
                    if tile_file.is_file():
                        tile.label = str(tile_file)
            try:
                img.reload()
                print(f"[assemble] UDIM image {img.name!r}: filepath={img.filepath!r}, "
                      f"tiles={len(img.tiles) if hasattr(img, 'tiles') else '?'}")
            except Exception as e:
                print(f"[assemble] WARN UDIM reload: {e}")
            break

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
# 6. Sky preset — bright afternoon/golden-hour Nishita sky for Cycles.
#    Sun 40° elevation (high afternoon, not dusk), SW azimuth for warm side-light.
#    World background strength = 1.0; AgX; Sun energy capped at 5 W.
#    FIX: was previously too dark/dusky; now uses a proper warm afternoon look.
# ---------------------------------------------------------------------------

def _do_sky():
    world_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.world_setup")
    # Afternoon look: sun 40° above horizon, azimuth ~225° (SW) → warm side-light.
    # Air/dust slightly elevated for a golden-hour haze on the Alps.
    world_mod.setup_multiple_scattering_sky(
        sun_elevation_rad=math.radians(40.0),
        sun_rotation_rad=math.radians(225.0),
        intensity=1.0,
        air=1.0,
        dust=1.2,
        ozone=1.0,
        exposure_ev=0.0,
    )
    # apply_sky_preset sets world Background strength to 0.2 (Eevee-tuned); we
    # already set the World via setup_multiple_scattering_sky and just need the
    # Sun light energy — skip apply_sky_preset to avoid overwriting our world.
    # Directly configure the Sun light to a warm afternoon colour and energy.
    for obj in bpy.data.objects:
        if obj.type == "LIGHT" and obj.data.type == "SUN":
            obj.data.energy = 5.0
            obj.data.color = (1.0, 0.95, 0.85)   # warm afternoon tint
            obj.data.angle = math.radians(0.53)   # solar disc angular size
            # Aim the sun light to match the sky's direction (SW, 40° elevation).
            import mathutils
            # Sun direction: azimuth 225° from +Y (N) → Blender Y-up convention.
            # Rotation in Euler: pitch (elevation from horizon) around local X,
            # then yaw (azimuth) around world Z.
            az_rad = math.radians(225.0)
            el_rad = math.radians(40.0)
            obj.rotation_euler = (
                math.pi / 2.0 - el_rad,   # tilt toward horizon
                0.0,
                az_rad,
            )
            print(f"[assemble] Sun: energy={obj.data.energy}, "
                  f"color={obj.data.color[:]}, az=225°, el=40°")
            break

    # Restore World Background Strength to 1.0 for the Cycles Nishita sky.
    world = scene.world
    if world and world.use_nodes:
        for node in world.node_tree.nodes:
            if node.type == "BACKGROUND":
                node.inputs["Strength"].default_value = 1.0
                print(f"[assemble] World Background Strength = 1.0")

    # AgX view transform + neutral exposure.
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0

_try("sky preset", _do_sky)

# ---------------------------------------------------------------------------
# 7. Quality preset — override to final 1920×1080 Cycles 128 spp + OIDN.
#    We call apply_quality just for its side-effects (clip settings etc),
#    then immediately override engine / samples / resolution / denoiser.
# ---------------------------------------------------------------------------

def _do_quality():
    result = bpy.ops.blender_tools.apply_quality("EXEC_DEFAULT", preset="preview")
    if result != {"FINISHED"}:
        raise RuntimeError(f"apply_quality returned {result}")

_try("quality preset", _do_quality)

# Headless Cycles — CPU path, no display needed. 128 samples + OIDN denoiser.
scene.render.engine = "CYCLES"
scene.cycles.samples = 32 if _PREVIEW else 128
scene.cycles.use_denoising = True
try:
    scene.cycles.denoiser = "OPENIMAGEDENOISE"
    print("[assemble] OIDN denoiser enabled")
except Exception as e:
    print(f"[assemble] WARN: OIDN denoiser not available: {e}")
    scene.cycles.use_denoising = False

# Enable GPU rendering (OptiX on RTX cards → massive speedup over CPU).
# Gracefully fall back to CPU if no compatible GPU is found.
def _enable_gpu():
    prefs = bpy.context.preferences.addons.get("cycles")
    if prefs is None:
        print("[assemble] Cycles addon not in preferences; using CPU")
        return
    cp = prefs.preferences
    for device_type in ("OPTIX", "CUDA", "METAL", "HIP", "ONEAPI"):
        try:
            cp.compute_device_type = device_type
            cp.get_devices()
            gpu_devs = [d for d in cp.devices if d.type != "CPU"]
            if gpu_devs:
                for d in gpu_devs:
                    d.use = True
                # Keep CPU off when GPU is present (avoids CPU/GPU memory contention).
                for d in cp.devices:
                    if d.type == "CPU":
                        d.use = False
                scene.cycles.device = "GPU"
                print(f"[assemble] GPU rendering: {device_type} "
                      f"({[d.name for d in gpu_devs]})")
                return
        except Exception:
            continue
    # No GPU: fall back to CPU.
    try:
        scene.cycles.device = "CPU"
    except Exception:
        pass
    print("[assemble] No compatible GPU found; using CPU")

_enable_gpu()
scene.render.resolution_x = RENDER_WIDTH
scene.render.resolution_y = RENDER_HEIGHT
scene.render.image_settings.file_format = "PNG"
# Slight colour management: AgX, neutral look, +0.3 EV exposure to pop the sky.
scene.view_settings.view_transform = "AgX"
scene.view_settings.exposure = 0.3
print(f"[assemble] engine={scene.render.engine}, device={scene.cycles.device}, "
      f"res={RENDER_WIDTH}x{RENDER_HEIGHT}, "
      f"samples={scene.cycles.samples}, denoise={scene.cycles.use_denoising}")

# ---------------------------------------------------------------------------
# 8. Ground shader — SKIPPED: the ortho drape already gives real DOP photo
#    texture on the terrain. The ground shader (GroundShader_Layered) replaces
#    the OrthoDrape material and uses DOPProjector Object-space coords + flat
#    projection, which only samples UDIM tile 1001 (UV 0-1 range), not the
#    full 10×11 UDIM grid → ground renders as solid grey from tile 1001 only.
#    The OrthoDrape material uses OrthoUV layer (scaled to 10×11) which works.
# ---------------------------------------------------------------------------

print("[assemble] ground shader: skipped (ortho drape material kept on terrain)")

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
# 10. Clouds — FIX: lower base to 2150 m so they're clearly in the forward
#     view with a 50° pitch camera; moderate coverage 0.45; denser than v3.
# ---------------------------------------------------------------------------

def _do_clouds():
    result = bpy.ops.blender_tools.add_clouds(
        "EXEC_DEFAULT",
        coverage=0.40,
        base_altitude_m=1700.0,    # well below camera (~2485m) → clouds below camera
        thickness_m=400.0,         # top at ~2100m, camera at ~2485m → clear above clouds
        density=0.04,
        detail=0.5,
        cirrus=False,
        cirrus_altitude_m=6500.0,
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"add_clouds returned {result}")

_try("clouds", _do_clouds)

cloud_objects = [obj for obj in bpy.data.objects
                 if any(k in obj.name.lower() for k in ("cloud", "cumulus", "cirrus"))]
print(f"[assemble] cloud objects: {[o.name for o in cloud_objects]}")

# ---------------------------------------------------------------------------
# 11. Camera rig from flight_path_utm.csv
# ---------------------------------------------------------------------------

def _do_camera():
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

    # XY offset fix — the terrain plane is centred at the Blender origin
    # (spanning ±size/2 in X and Y), but the flight-path CSV is anchor-
    # subtracted from the SW corner, so the path sits at local XY
    # (0..size_x, 0..size_y) — i.e. shifted by (+size_x/2, +size_y/2)
    # relative to the terrain.  Shift the curve object by (-half_x, -half_y)
    # so it overlays the terrain.
    half_x = half_y = 0.0
    t_obj = bpy.data.objects.get(terrain_name) if terrain_name else None
    if t_obj is None:
        t_obj = next((o for o in bpy.data.objects
                      if o.type == "MESH" and o.name.startswith("Terrain")), None)
    if t_obj is not None:
        xs = [v[0] for v in t_obj.bound_box]
        ys = [v[1] for v in t_obj.bound_box]
        half_x = (max(xs) - min(xs)) / 2.0
        half_y = (max(ys) - min(ys)) / 2.0
    for obj in bpy.data.objects:
        if obj.type == "CURVE" and obj.name.startswith("FlightPath"):
            obj.location = (-half_x, -half_y, 0.0)
            print(f"[assemble] FlightPath XY shift: ({-half_x:.0f}, {-half_y:.0f})"
                  " to overlay terrain")
            break

    result = bpy.ops.blender_tools.setup_camera_rig(
        "EXEC_DEFAULT",
        banking_max_deg=8.0,
        speed_mps=50.0,
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"setup_camera_rig returned {result}")

    if scene.camera:
        result = bpy.ops.blender_tools.apply_camera_preset(
            "EXEC_DEFAULT", preset="cinematic-establishing")
        if result != {"FINISHED"}:
            raise RuntimeError(f"apply_camera_preset returned {result}")

_try("camera rig", _do_camera)

# Post-camera: cinematic forward-look orientation.
#
# The cinematic-establishing preset sets tilt_pitch_deg=-45, which gives:
#   camera.rotation_euler.x = radians(90 + (-45)) = radians(45)  → 45° off nadir.
# We want 50° off nadir. Disable use_curve_follow so the rig only translates
# (not rotates), strip tracking constraints, and apply a fixed world-space pitch.
# Camera altitude ≈ terrain_z_max + 800 m AGL ≈ 2485 m absolute.

def _fix_camera_orientation():
    cam = scene.camera
    if cam is None:
        raise RuntimeError("no scene camera to fix orientation on")

    rig_empty = cam.parent
    if rig_empty is not None:
        for c in rig_empty.constraints:
            if c.type == "FOLLOW_PATH":
                c.use_curve_follow = False
                print("[assemble] disabled use_curve_follow on rig empty")
                break
        # Strip noise F-curve modifier that apply_camera_preset added; it was
        # calibrated for a forward-looking camera and jitters our down view.
        if rig_empty.animation_data:
            rig_empty.animation_data_clear()
        rig_empty.rotation_euler = (0.0, 0.0, 0.0)
        # apply_camera_preset lifted the rig Z by terrain_z + altitude_agl.
        # The curve itself is already lifted to cruise altitude, so zero the
        # rig location offset to avoid double-adding the Z.
        rig_empty.location = (0.0, 0.0, 0.0)

    # Remove tracking constraints and clear any keyframed rotation on the camera.
    for c in list(cam.constraints):
        if c.type in {"DAMPED_TRACK", "TRACK_TO", "LOCKED_TRACK"}:
            cam.constraints.remove(c)
    if cam.animation_data:
        cam.animation_data_clear()

    # Cinematic forward-look pitch: NADIR_FORWARD_TILT_DEG off nadir.
    # Blender camera at rotation (0,0,0) looks down -Z (nadir).
    # rotation_euler.x tilts it toward +Y (world north) by that many degrees.
    cam.rotation_euler = (math.radians(NADIR_FORWARD_TILT_DEG), 0.0, 0.0)
    # Vary Z altitude slightly across the path for organic feel: the camera
    # curve was lifted to a fixed altitude by apply_camera_preset; leave it.
    # A slight upward bank roll for the diagonal path legs: +5° on the Z axis
    # gives a mild banking feel without needing per-frame keyframes.
    cam.rotation_euler.z = math.radians(5.0)
    print(f"[assemble] camera cinematic pitch: {NADIR_FORWARD_TILT_DEG}° off nadir, "
          f"bank roll +5°, use_curve_follow=False")

_try("camera orientation fix", _fix_camera_orientation)

# Sync scene frame range to the flight-path curve duration.
for obj in bpy.data.objects:
    if obj.type == "CURVE" and obj.name.startswith("FlightPath"):
        pd = int(getattr(obj.data, "path_duration", 0) or 0)
        if pd > 1:
            scene.frame_start = 1
            scene.frame_end = pd
            print(f"[assemble] frame range synced to path_duration: 1-{pd}")
        break

cam_name = scene.camera.name if scene.camera else "(none)"
print(f"[assemble] scene camera: {cam_name}")
print(f"[assemble] frame range: {scene.frame_start} – {scene.frame_end}")

# ---------------------------------------------------------------------------
# Post-ortho check: verify the ortho UDIM image is correctly pointing at the
# tiles with an absolute path before saving / rendering.
# ---------------------------------------------------------------------------

def _verify_ortho_udim():
    udim_dir = DATA_PROCESSED / "ortho_udim"
    tiles_on_disk = sorted(udim_dir.glob("ortho.*.jpg"))
    udim_token_path = str(udim_dir.resolve() / "ortho.<UDIM>.jpg")

    for img in list(bpy.data.images):
        if "ortho" not in img.name.lower():
            continue
        current_fp = img.filepath
        needs_fix = "<UDIM>" not in current_fp

        if needs_fix:
            # Image was loaded as a single tile (old code path) — fix by
            # replacing it with a properly-loaded UDIM image. Using load()
            # with the <UDIM> token path is the only approach that correctly
            # wires into Cycles' render-time tile lookup in headless mode.
            # Collect all material nodes referencing this image first.
            node_refs = []
            for mat in bpy.data.materials:
                if mat.use_nodes:
                    for node in mat.node_tree.nodes:
                        if node.type == "TEX_IMAGE" and node.image == img:
                            node_refs.append((mat, node))
            bpy.data.images.remove(img)
            new_img = bpy.data.images.load(udim_token_path, check_existing=False)
            new_img.colorspace_settings.name = "sRGB"
            new_img.source = "TILED"
            if hasattr(new_img, "tiles"):
                existing = {t.number for t in new_img.tiles}
                for tile_f in tiles_on_disk:
                    udim = int(tile_f.stem.split(".")[1])
                    if udim not in existing:
                        try:
                            new_img.tiles.new(tile_number=udim, label=tile_f.name)
                            existing.add(udim)
                        except Exception:
                            pass
            for _, node in node_refs:
                node.image = new_img
            img = new_img
            print(f"[assemble] ortho UDIM fixed (load+token): {img.name!r} "
                  f"fp={img.filepath!r} tiles={len(img.tiles) if hasattr(img, 'tiles') else '?'}")
        else:
            print(f"[assemble] ortho img OK: {img.name!r} fp={img.filepath!r} "
                  f"source={img.source} "
                  f"tiles={len(img.tiles) if hasattr(img, 'tiles') else '?'}")

_try("ortho UDIM path fix", _verify_ortho_udim)

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
# Render 4 stills — v4 naming, 1920×1080, 128 spp + OIDN
# ---------------------------------------------------------------------------

total_frames = scene.frame_end - scene.frame_start
render_frames = []
if total_frames > 0:
    for frac in (0.10, 0.35, 0.60, 0.90):
        f = int(scene.frame_start + round(frac * total_frames))
        f = max(scene.frame_start, min(scene.frame_end, f))
        render_frames.append(f)
else:
    render_frames = [1, 25, 50, 75]

print(f"[assemble] rendering frames: {render_frames} (total timeline: {total_frames})")
print(f"[assemble] render: {RENDER_WIDTH}×{RENDER_HEIGHT}, "
      f"{scene.cycles.samples} spp, denoise={scene.cycles.use_denoising}")

rendered_paths = []
scene.render.use_file_extension = True
scene.render.use_render_cache = False

for i, frame in enumerate(render_frames):
    # v4 naming.
    stem = f"allgaeu_v4_frame{frame:04d}"
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
            print(f"[assemble] WARN render frame {frame}: file not found after render "
                  f"({dt:.1f}s); tried {expected_path}")
    except Exception as e:
        print(f"[assemble] WARN render frame {frame}: {e}")

# Save again after rendering with frame set back to start.
scene.frame_set(scene.frame_start)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT_BLEND))
print(f"[assemble] final save → {OUT_BLEND}")

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

dt_total = time.time() - t0_global
print("\n" + "=" * 70)
print("ALLGÄU ASSEMBLY SUMMARY (v4)")
print("=" * 70)
print(f"Total time:       {dt_total:.0f}s")
print(f"Steps done:       {', '.join(steps_done)}")
if steps_warn:
    print(f"Warnings:         {'; '.join(steps_warn)}")
print(f"Terrain subdiv:   {TERRAIN_SUBDIV} → {2**TERRAIN_SUBDIV + 1} verts/side")
print(f"Terrain verts:    {terrain_verts:,} (base mesh)")
print(f"Buildings:        {building_count}")
print(f"Cloud objects:    {len(cloud_objects)}")
print(f"Camera pitch:     {NADIR_FORWARD_TILT_DEG}° off nadir")
print(f"Camera:           {cam_name}")
print(f"Frame range:      {scene.frame_start}–{scene.frame_end}")
print(f"Engine:           {scene.render.engine}")
print(f"Samples:          {scene.cycles.samples} spp + OIDN={scene.cycles.use_denoising}")
print(f"Resolution:       {scene.render.resolution_x}×{scene.render.resolution_y}")
print(f".blend:           {OUT_BLEND}")
print(f"Renders ({len(rendered_paths)}):")
for p in rendered_paths:
    print(f"  {p}")
print("=" * 70)
