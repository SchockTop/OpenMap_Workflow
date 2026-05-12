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
    workflows/scenes/allgaeu-flyover/renders/allgaeu_v5_frame{NNNN}.png
    (4 frames: hand-placed cinematic positions, forward-look toward the Alps)

v5 changes vs v4:
  1. Exposure fixed: sun energy 2.0 W, World strength 0.15, exposure -1.5 EV,
     AgX "Medium High Contrast" look — ortho must read as a real aerial photo.
  2. Trees hidden from render (GN modifier show_render=False); forest reads via
     the ortho DOP photo + a forest-overlay on the OrthoDrape material (darken +
     noise bump on forest pixels so hillsides read as canopy mass, not flat photo).
  3. Camera: 4 hand-placed keyframes on a south-facing arc at ~2200 m absolute,
     pitched 45° off nadir. Foreground lakes/fields → midground forest →
     Alps on the southern horizon → sky in top 25%.
  4. Clouds: base 2050 m, thickness 400 m, coverage 0.40 so the camera (~2200 m)
     skims through a broken cumulus deck; ≥2 frames show clouds in mid-distance.
  5. UDIM seams: less prominent at correct exposure; no further processing needed.
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
FLIGHT_PATH_UTM = DATA_PROCESSED / "flight_path_utm.csv"
OUT_BLEND = WORKFLOW_ROOT / "data" / "scene_allgaeu-forggensee.blend"
RENDERS_DIR = WORKFLOW_ROOT / "workflows" / "scenes" / "allgaeu-flyover" / "renders"
RENDERS_DIR.mkdir(parents=True, exist_ok=True)

# Terrain subdivision: 10 → 1025 verts/side ≈ 1.05 M quads (9.2 km / 1024 ≈ 9 m/quad).
TERRAIN_SUBDIV = 10

# Render config
import os as _os
_PREVIEW = _os.environ.get("ALLGAEU_PREVIEW", "") == "1"
RENDER_WIDTH = 960 if _PREVIEW else 1920
RENDER_HEIGHT = 540 if _PREVIEW else 1080

# v5 cinematic parameters
# Final strategy: use 4 varied compositions from different altitudes.
# Three near-nadir aerial frames (1600-1800m, 48-50mm, showing landscape detail)
# + one genuine horizon shot (2800m, 50mm, looking just past the terrain edge to get sky).
# The near-nadir frames look like real aerial photo missions; the horizon shot shows scale.
CAMERA_ALTITUDE_M = 1600.0   # default; overridden per-keyframe below
NADIR_FORWARD_TILT_DEG = 50.0  # approximate; overridden per-keyframe

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
# 1. Terrain
# ---------------------------------------------------------------------------

def _do_terrain():
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
# 2. Ortho drape — UDIM tiles
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

    # Ensure the UDIM image datablock has an absolute <UDIM> filepath
    udim_dir_resolved = udim_dir.resolve()
    for img in bpy.data.images:
        if "ortho" in img.name.lower():
            udim_token_path = str(udim_dir_resolved / "ortho.<UDIM>.jpg")
            img.filepath = udim_token_path
            img.source = "TILED"
            if hasattr(img, "tiles"):
                for tile in img.tiles:
                    tile_num = tile.number
                    tile_file = udim_dir_resolved / f"ortho.{tile_num}.jpg"
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
    if building_count == 0:
        raise RuntimeError("no buildings — skip building textures")
    result = bpy.ops.blender_tools.apply_building_textures("EXEC_DEFAULT")
    if result not in ({"FINISHED"}, {"FINISHED"}):
        raise RuntimeError(f"apply_building_textures returned {result}")

_try("building textures", _do_bld_tex)

# ---------------------------------------------------------------------------
# 5. Sky + cinematic preset
#    v5: sun energy 2.0 W (was 5.0), World Background Strength 0.15 (was 1.0),
#    exposure -1.5 EV (was +0.3). The DOP ortho must look like a real aerial
#    photo — visible greens, browns, blues — not a white wash.
# ---------------------------------------------------------------------------

def _do_cinematic():
    cinematic_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.cinematic_preset")
    cinematic_mod.apply_cinematic_preset(
        scene,
        render_engine="CYCLES",
        resolution=(RENDER_WIDTH, RENDER_HEIGHT),
    )

_try("cinematic preset (Sun + World)", _do_cinematic)


def _do_sky():
    world_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.world_setup")
    # Mid-morning look: sun ~50° elevation (higher = harder shadows from above,
    # but less sidewash blowout than afternoon 40°), azimuth ~150° (SSE) so the
    # sun comes from behind the camera for the south-facing shots → front-lit Alps.
    world_mod.setup_multiple_scattering_sky(
        sun_elevation_rad=math.radians(50.0),
        sun_rotation_rad=math.radians(150.0),
        intensity=1.0,
        air=1.0,
        dust=0.8,    # v5: less dust/haze → less sky glow → darker ground
        ozone=1.0,
        exposure_ev=0.0,
    )

    # Configure Sun light: v5 energy = 2.0 W (was 5.0 in v4 — halved to fix blowout).
    for obj in bpy.data.objects:
        if obj.type == "LIGHT" and obj.data.type == "SUN":
            obj.data.energy = 2.0          # was 5.0 → massive overexposure in v4
            obj.data.color = (1.0, 0.98, 0.95)  # nearly white, slight warm tint
            obj.data.angle = math.radians(0.53)
            az_rad = math.radians(150.0)
            el_rad = math.radians(50.0)
            obj.rotation_euler = (
                math.pi / 2.0 - el_rad,
                0.0,
                az_rad,
            )
            print(f"[assemble] Sun v5: energy=2.0 W, az=150°, el=50°")
            break

    # World Background Strength: v5 = 0.15 (was 1.0 → blown sky and GI bounce blowout).
    # A strength of 0.15 gives a well-lit but not overexposed scene in Cycles Nishita.
    world = scene.world
    if world and world.use_nodes:
        for node in world.node_tree.nodes:
            if node.type == "BACKGROUND":
                node.inputs["Strength"].default_value = 0.15
                print(f"[assemble] World Background Strength = 0.15 (v5 fix)")

    # AgX with "Medium High Contrast" look for punchier midtones.
    scene.view_settings.view_transform = "AgX"
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
        print("[assemble] AgX look: Medium High Contrast")
    except Exception:
        try:
            scene.view_settings.look = "Medium High Contrast"
        except Exception:
            pass  # look string varies by Blender version; fallback to base AgX

    # v5: exposure = -1.5 EV (was +0.3 in v4 → additional 1.5 stop blowout).
    scene.view_settings.exposure = -1.5
    scene.view_settings.gamma = 1.0
    print(f"[assemble] view exposure = -1.5 EV (v5)")

_try("sky preset", _do_sky)

# ---------------------------------------------------------------------------
# 6. Quality preset + GPU
# ---------------------------------------------------------------------------

def _do_quality():
    result = bpy.ops.blender_tools.apply_quality("EXEC_DEFAULT", preset="preview")
    if result != {"FINISHED"}:
        raise RuntimeError(f"apply_quality returned {result}")

_try("quality preset", _do_quality)

scene.render.engine = "CYCLES"
scene.cycles.samples = 32 if _PREVIEW else 128
scene.cycles.use_denoising = True
try:
    scene.cycles.denoiser = "OPENIMAGEDENOISE"
    print("[assemble] OIDN denoiser enabled")
except Exception as e:
    print(f"[assemble] WARN: OIDN denoiser not available: {e}")
    scene.cycles.use_denoising = False


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
                for d in cp.devices:
                    if d.type == "CPU":
                        d.use = False
                scene.cycles.device = "GPU"
                print(f"[assemble] GPU rendering: {device_type} "
                      f"({[d.name for d in gpu_devs]})")
                return
        except Exception:
            continue
    try:
        scene.cycles.device = "CPU"
    except Exception:
        pass
    print("[assemble] No compatible GPU found; using CPU")

_enable_gpu()
scene.render.resolution_x = RENDER_WIDTH
scene.render.resolution_y = RENDER_HEIGHT
scene.render.image_settings.file_format = "PNG"
# Re-apply exposure after quality preset (which may reset it).
scene.view_settings.view_transform = "AgX"
try:
    scene.view_settings.look = "AgX - Medium High Contrast"
except Exception:
    try:
        scene.view_settings.look = "Medium High Contrast"
    except Exception:
        pass
scene.view_settings.exposure = -1.5
print(f"[assemble] engine={scene.render.engine}, device={scene.cycles.device}, "
      f"res={RENDER_WIDTH}x{RENDER_HEIGHT}, "
      f"samples={scene.cycles.samples}, denoise={scene.cycles.use_denoising}")

# ---------------------------------------------------------------------------
# 7. Ground shader — SKIPPED (ortho drape material kept on terrain)
# ---------------------------------------------------------------------------

print("[assemble] ground shader: skipped (ortho drape material kept on terrain)")

# ---------------------------------------------------------------------------
# 8. Trees — scatter with forest mask, then HIDE FROM RENDER for the flyover.
#    v5: at ~2200 m AGL the decimated 3D tree meshes read as dark noise specks
#    (0.5–5 px each). We keep the GN modifier in the .blend for close-up use
#    but set show_render=False. Forest will read via the ortho photo + the
#    forest-overlay material tweak in step 9.
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

# Hide the GN tree scatter from render — at flyover altitude they render as
# dark noise specks, not as recognisable trees. The modifier stays in the
# .blend for someone who opens it and wants to fly low.
terrain_obj_for_trees = bpy.data.objects.get(terrain_name) if terrain_name else None
if terrain_obj_for_trees is None:
    terrain_obj_for_trees = next(
        (o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Terrain")),
        None)
if terrain_obj_for_trees is not None:
    for mod in terrain_obj_for_trees.modifiers:
        if mod.type == "NODES" and "tree" in mod.name.lower():
            mod.show_render = False
            print(f"[assemble] v5: tree GN modifier '{mod.name}' hidden from render "
                  "(too small at 2200 m; forest reads via DOP ortho + overlay)")

tree_count = sum(
    1 for obj in bpy.data.objects
    if obj.type == "EMPTY" and "tree" in obj.name.lower()
)
print(f"[assemble] approximate tree-root objects: {tree_count}")

# ---------------------------------------------------------------------------
# 9. Forest overlay on the OrthoDrape terrain material.
#    Load forest_mask.tif (Non-Color), sample it via OrthoUV, and use it to:
#    (a) slightly darken the ortho on forest pixels (forests absorb light)
#    (b) add a small noise bump perturbation so canopy reads as texture, not flat.
#    The overlay is subtle — the DOP photo already shows the real forest pattern;
#    we just enhance contrast and add micro-relief so it reads as a canopy mass
#    from 2200 m altitude.
# ---------------------------------------------------------------------------

def _do_forest_overlay():
    mask_tif = DATA_PROCESSED / "forest_mask.tif"
    if not mask_tif.is_file():
        raise FileNotFoundError(f"forest_mask.tif not found: {mask_tif}")

    # Find the OrthoDrape material on the terrain.
    terrain_obj = bpy.data.objects.get(terrain_name) if terrain_name else None
    if terrain_obj is None:
        terrain_obj = next(
            (o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Terrain")),
            None)
    if terrain_obj is None:
        raise RuntimeError("terrain object not found")

    mat = terrain_obj.active_material
    if mat is None or not mat.use_nodes:
        raise RuntimeError(f"terrain has no node material: {mat}")

    nt = mat.node_tree
    nodes = nt.nodes
    links = nt.links

    # Locate the existing chain: UVMap(OrthoUV) → TexImage(ortho) → PrincipledBSDF → Output.
    bsdf = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
    out_node = next((n for n in nodes if n.type == "OUTPUT_MATERIAL"), None)
    if bsdf is None or out_node is None:
        raise RuntimeError("expected PrincipledBSDF + OutputMaterial in OrthoDrape mat")

    ortho_tex = None
    for n in nodes:
        if n.type == "TEX_IMAGE" and n.image is not None:
            if "ortho" in (n.image.name or "").lower():
                ortho_tex = n
                break

    uv_node = None
    for n in nodes:
        if n.type == "UVMAP" and n.uv_map == "OrthoUV":
            uv_node = n
            break

    if ortho_tex is None or uv_node is None:
        raise RuntimeError("Could not locate ortho TexImage or OrthoUV node in OrthoDrape")

    # Load the forest mask as a Non-Color image.
    mask_img_name = "ForestMask_Overlay"
    existing = bpy.data.images.get(mask_img_name)
    if existing:
        bpy.data.images.remove(existing)
    mask_img = bpy.data.images.load(str(mask_tif.resolve()), check_existing=False)
    mask_img.name = mask_img_name
    mask_img.colorspace_settings.name = "Non-Color"

    # Build the overlay chain after the existing ortho texture node.
    # Layout offsets so the new nodes are to the right of the existing chain.
    ox = ortho_tex.location.x + 400
    oy = ortho_tex.location.y

    # Forest mask TexImage — reuse the OrthoUV node for consistent sampling.
    n_mask = nodes.new("ShaderNodeTexImage")
    n_mask.location = (ox, oy - 280)
    n_mask.image = mask_img
    n_mask.extension = "EXTEND"
    links.new(uv_node.outputs["UV"], n_mask.inputs["Vector"])

    # Separate RGB of the mask → take R channel as the forest weight (0 or 1).
    n_sep = nodes.new("ShaderNodeSeparateColor")
    n_sep.location = (ox + 240, oy - 280)
    links.new(n_mask.outputs["Color"], n_sep.inputs["Color"])
    forest_weight = n_sep.outputs["Red"]  # Float: 0 = non-forest, 1 = forest

    # Darken: multiply ortho colour by a forest-darkening factor on forest pixels.
    # Factor 0.68 → ~0.5 EV darker on forest pixels (forests absorb ~30% more light).
    n_darken_val = nodes.new("ShaderNodeMath")
    n_darken_val.operation = "MULTIPLY"
    n_darken_val.location = (ox + 240, oy - 60)
    n_darken_val.inputs[0].default_value = -0.32   # −32% offset (1.0 → 0.68)
    links.new(forest_weight, n_darken_val.inputs[1])

    # Add offset to 1.0 base → darken_scale
    n_darken_scale = nodes.new("ShaderNodeMath")
    n_darken_scale.operation = "ADD"
    n_darken_scale.location = (ox + 440, oy - 60)
    n_darken_scale.inputs[0].default_value = 1.0
    links.new(n_darken_val.outputs["Value"], n_darken_scale.inputs[1])

    # Mix ortho colour with the darkened version using the forest weight.
    n_mul_color = nodes.new("ShaderNodeMixRGB")
    n_mul_color.blend_type = "MULTIPLY"
    n_mul_color.location = (ox + 600, oy)
    n_mul_color.inputs["Color2"].default_value = (0.68, 0.72, 0.62, 1.0)
    # Fac = forest_weight so only forest pixels are darkened.
    links.new(forest_weight, n_mul_color.inputs["Fac"])
    # Connect the ortho output → Color1
    if ortho_tex.outputs["Color"].links:
        # Break existing link from ortho to BSDF and re-route through the mix.
        old_link = ortho_tex.outputs["Color"].links[0]
        links.remove(old_link)
    links.new(ortho_tex.outputs["Color"], n_mul_color.inputs["Color1"])

    # Canopy bump: low-strength noise texture on forest pixels → small height perturbation
    # that breaks the flat-photo look. Scale = ~crown size at terrain UV scale.
    n_noise = nodes.new("ShaderNodeTexNoise")
    n_noise.location = (ox, oy - 500)
    n_noise.inputs["Scale"].default_value = 120.0   # ~tree-crown spacing in UV units
    n_noise.inputs["Detail"].default_value = 3.0
    n_noise.inputs["Roughness"].default_value = 0.6
    n_noise.inputs["Distortion"].default_value = 0.1
    links.new(uv_node.outputs["UV"], n_noise.inputs["Vector"])

    # Gate the noise by the forest weight → bump only on forest.
    n_gate = nodes.new("ShaderNodeMath")
    n_gate.operation = "MULTIPLY"
    n_gate.location = (ox + 240, oy - 500)
    links.new(n_noise.outputs["Fac"], n_gate.inputs[0])
    links.new(forest_weight, n_gate.inputs[1])

    # Remap gate output to a gentle height bump (0–0.04 range, centred at 0.02).
    n_remap = nodes.new("ShaderNodeMapRange")
    n_remap.location = (ox + 440, oy - 500)
    n_remap.inputs[1].default_value = 0.0    # From Min
    n_remap.inputs[2].default_value = 1.0    # From Max
    n_remap.inputs[3].default_value = -0.02  # To Min (slight depression)
    n_remap.inputs[4].default_value = 0.04   # To Max (slight raise)
    links.new(n_gate.outputs["Value"], n_remap.inputs[0])

    # Use the bump node to perturb surface normals.
    n_bump = nodes.new("ShaderNodeBump")
    n_bump.location = (ox + 660, oy - 500)
    n_bump.inputs["Strength"].default_value = 0.25   # subtle, doesn't distort lighting
    n_bump.inputs["Distance"].default_value = 1.0
    links.new(n_remap.outputs["Result"], n_bump.inputs["Height"])

    # Connect the output: darkened colour → BSDF Base Color; bump → BSDF Normal.
    links.new(n_mul_color.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(n_bump.outputs["Normal"], bsdf.inputs["Normal"])

    print(f"[assemble] forest overlay: darken + bump wired to OrthoDrape material "
          f"(mask={mask_img.name!r})")

_try("forest overlay", _do_forest_overlay)

# ---------------------------------------------------------------------------
# 10. Clouds — v5: base 2050 m so camera at ~2200 m skims through broken deck.
#     Coverage 0.40, thickness 400 m (top at ~2450 m) → camera is inside/just
#     above the layer. At least 2 of the 4 frames should show cumulus in mid-distance.
#     Cirrus enabled at 6000 m for sky interest.
# ---------------------------------------------------------------------------

def _do_clouds():
    result = bpy.ops.blender_tools.add_clouds(
        "EXEC_DEFAULT",
        coverage=0.40,
        # v5e: multi-altitude camera setup (1600-2800m). Cloud deck 2100-2500m.
        # Frames 1-3 at 1600-1800m are BELOW clouds (cloud mass visible above).
        # Frame 4 at 2800m is ABOVE clouds (puffy tops visible below toward valley).
        # Coverage 0.40 = broken deck; the near-horizon frame (shot 4) should show
        # cloud tops + sky above.
        base_altitude_m=2100.0,
        thickness_m=400.0,         # top at 2500m
        density=0.05,
        detail=0.5,
        cirrus=True,
        cirrus_altitude_m=6000.0,
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"add_clouds returned {result}")

_try("clouds", _do_clouds)

cloud_objects = [obj for obj in bpy.data.objects
                 if any(k in obj.name.lower() for k in ("cloud", "cumulus", "cirrus"))]
print(f"[assemble] cloud objects: {[o.name for o in cloud_objects]}")

# ---------------------------------------------------------------------------
# 11. ORTHO UDIM path verification (run after all materials are built)
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
            print(f"[assemble] ortho UDIM fixed: {img.name!r} "
                  f"fp={img.filepath!r} tiles={len(img.tiles) if hasattr(img, 'tiles') else '?'}")
        else:
            print(f"[assemble] ortho img OK: {img.name!r} fp={img.filepath!r} "
                  f"source={img.source} "
                  f"tiles={len(img.tiles) if hasattr(img, 'tiles') else '?'}")

_try("ortho UDIM path fix", _verify_ortho_udim)

# ---------------------------------------------------------------------------
# 12. v5 Camera — 4 hand-placed keyframes for a genuine cinematic composition.
#
# Goal: foreground landscape (Forggensee / fields / Schwangau) in the lower
# 2/3rds; the Alps (Säuling ~2047 m, Tegelberg ~1720 m) visible on the southern
# horizon; sky in the top 25%; broken cumulus visible mid-distance.
#
# AOI (EPSG:25832): SW=(628971, 5268342), NE=(638194, 5278717). Centre ~(633583, 5273530).
# Terrain is centred at Blender origin. So in Blender-local coords:
#   Scene centre = (0, 0).
#   South edge (Alps) ≈ Y = −5188 m from centre (the low-latitude / small-Y end).
#   North edge (Forggensee north shore) ≈ Y = +5188 m.
#   East edge ≈ X = +4606 m.
#   West edge ≈ X = −4606 m.
# Forggensee is roughly in the north-western quadrant.
# The castles (Neuschwanstein / Hohenschwangau) are at the eastern-central area
# (Schwangau, ~633700 E, 5273700 N → Blender local ≈ +117, +170).
#
# Strategy: 4 camera positions along a south-facing arc, each at 2200 m absolute Z.
# Camera pitched 45° forward (off nadir). The arc starts NW (over Forggensee),
# sweeps NE, then E, then SE toward the mountains.
# Each position is offset so the forward view looks toward the Alps.
#
# Camera rotation convention in Blender:
#   (0,0,0) = looking down -Z (nadir / straight down).
#   rotation_euler.x = +45° → tilts 45° toward +Y (world north).
#   To look SOUTH (toward Alps at -Y in scene), we want to tilt toward -Y:
#     rotation_euler.x = -(90° - 45°) = -45°  →  cam looks south at 45° off nadir.
#     Actually: cam forward = -Z when x=0. Rotating x by -45° tilts the forward
#     vector from -Z toward +Y (counterintuitive). Let's think:
#       Blender Euler XYZ on camera: x=0,y=0,z=0 → camera looks DOWN (-Z world).
#       Rotate X by +90° → camera looks toward +Y (world north = scene north).
#       Rotate X by -90° → camera looks toward -Y (world south = Alps direction).
#       Rotate X by (90°-45°)=+45° → camera looks 45° off nadir toward north.
#       Rotate X by -(90°-45°)=-45° → camera looks 45° off nadir toward south (Alps).
#   So: rotation_euler = (-math.radians(45), 0, 0) → forward-look south.
#   To face south-east: rotation_euler = (-math.radians(45), 0, -math.radians(45)).
#   To face south-west: rotation_euler = (-math.radians(45), 0, +math.radians(45)).
#
# The 4 keyframe positions (Blender local coords, Z = absolute altitude in m):
#   Frame 1: NW quadrant, looking SSW toward the mountains (Forggensee in foreground)
#   Frame 2: N quadrant, looking S toward castles + Alps
#   Frame 3: NE quadrant, looking SSE toward Tegelberg / alpine ridge
#   Frame 4: E centre, looking SW across the valley + Alps
# ---------------------------------------------------------------------------

def _do_camera_v5():
    # Remove any existing camera objects / curves from step 11 (old camera rig).
    for obj in list(bpy.data.objects):
        if obj.type in ("CAMERA", "CURVE", "EMPTY") and (
            obj.name.startswith("FlightPath") or
            obj.name.startswith("Camera") or
            obj.name.startswith("CameraRig")
        ):
            bpy.data.objects.remove(obj, do_unlink=True)

    # Create a new camera.
    bpy.ops.object.camera_add(location=(0, 0, CAMERA_ALTITUDE_M))
    cam = bpy.context.active_object
    cam.name = "Camera_v5"
    scene.camera = cam

    # 50mm lens for the main shots: narrow FOV keeps compositions tight.
    cam.data.lens = 50.0
    cam.data.clip_start = 10.0
    cam.data.clip_end = 200_000.0

    # 4 keyframe positions: (frame, x, y, z, rot_x_deg, rot_z_deg, focal_mm)
    # AOI: terrain centred at origin, X=[-4611..+4611], Y=[-5188..+5188].
    # Forggensee centre: approx Blender local (-1500, +2800).
    # Schwangau/castles: approx Blender local (+200, +350).
    # Frame 1: Wide aerial view of Forggensee lake + Füssen. Camera NW of lake,
    #   looking east over the lake and its southern shore. Low angle to see topography.
    #   This matched v5b frame 0060 which showed the best content.
    kf = [
        # (frame, x,     y,    z,    rx_deg,  rz_deg, focal_mm)

        # Shot 1: Forggensee bay + Füssen shoreline — looking SSW.
        # Camera at the NW quadrant, looking south so the lake is in the lower-left
        # and the southern Allgäu hills fill the right/foreground.
        # rz=+18° = slight westward yaw so Forggensee is not cut off at the left edge.
        (  1,  -2000,  3000,  1600,  -52.0,  18.0,  50),

        # Shot 2: Schwangau + forested slopes — best composition from earlier tests.
        ( 60,   -200,  2500,  1800,  -50.0,   5.0,  50),

        # Shot 3: Forggensee delta + sandbar — best composition from earlier tests.
        (120,   1000,  1500,  1700,  -48.0, -15.0,  50),

        # Shot 4: High aerial overview — camera at 2400m, looking SSW over the whole valley.
        # At 48° pitch from 2400m, center of frame hits 2400/tan(42°) = 2665m → Y = 2000-2665 = -665m.
        # More elevated = wider context, less detail = provides variety in the set.
        (180,  -1000,  2000,  2400,  -48.0,  12.0,  35),
    ]

    scene.frame_start = 1
    scene.frame_end = 180

    for (frame, x, y, z, rx, rz, focal) in kf:
        scene.frame_set(frame)
        cam.location = (x, y, z)
        cam.rotation_euler = (
            math.radians(rx),
            0.0,
            math.radians(rz),
        )
        cam.data.lens = focal
        cam.keyframe_insert(data_path="location", frame=frame)
        cam.keyframe_insert(data_path="rotation_euler", frame=frame)
        cam.data.keyframe_insert(data_path="lens", frame=frame)
        print(f"[assemble] camera kf frame {frame}: loc=({x},{y},{z:.0f}) "
              f"rx={rx}° rz={rz}° focal={focal}mm")

    # Use Bezier interpolation for smooth transitions.
    if cam.animation_data and cam.animation_data.action:
        for fcurve in cam.animation_data.action.fcurves:
            for kp in fcurve.keyframe_points:
                kp.interpolation = "BEZIER"

    print(f"[assemble] v5 camera: 4 keyframes, lens=28mm, alt={Z:.0f}m, "
          f"pitch≈45° off-nadir, south-facing toward Alps")

_try("camera v5", _do_camera_v5)

cam_name = scene.camera.name if scene.camera else "(none)"
print(f"[assemble] scene camera: {cam_name}")
print(f"[assemble] frame range: {scene.frame_start} – {scene.frame_end}")

# ---------------------------------------------------------------------------
# Scene stats
# ---------------------------------------------------------------------------

all_objs = list(bpy.data.objects)
mesh_objs = [o for o in all_objs if o.type == "MESH"]
terrain_obj_stat = next((o for o in mesh_objs if o.name.startswith("Terrain")), None)
terrain_verts = len(terrain_obj_stat.data.vertices) if terrain_obj_stat else 0
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
# Render 4 stills — v5 naming, 1920×1080, 128 spp + OIDN
# v5: render at the 4 keyframe frames (1, 60, 120, 180) for the best compositions.
# ---------------------------------------------------------------------------

render_frames = [1, 60, 120, 180]

print(f"[assemble] rendering frames: {render_frames} (v5 keyframe positions)")
print(f"[assemble] render: {RENDER_WIDTH}×{RENDER_HEIGHT}, "
      f"{scene.cycles.samples} spp, denoise={scene.cycles.use_denoising}")

rendered_paths = []
scene.render.use_file_extension = True
scene.render.use_render_cache = False

for i, frame in enumerate(render_frames):
    stem = f"allgaeu_v5_frame{frame:04d}"
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

# Save again after rendering.
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT_BLEND))
print(f"[assemble] final save → {OUT_BLEND}")

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

dt_total = time.time() - t0_global
print("\n" + "=" * 70)
print("ALLGÄU ASSEMBLY SUMMARY (v5)")
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
print(f"Samples:          {scene.cycles.samples} spp + OIDN={scene.cycles.use_denoising}")
print(f"Resolution:       {scene.render.resolution_x}×{scene.render.resolution_y}")
print(f".blend:           {OUT_BLEND}")
print(f"Renders ({len(rendered_paths)}):")
for p in rendered_paths:
    print(f"  {p}")
print("=" * 70)
