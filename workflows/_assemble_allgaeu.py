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
    workflows/scenes/allgaeu-flyover/renders/allgaeu_v6_frame{NNNN}.png
    (4 frames: hand-placed cinematic positions, forward-look toward the distant
     ridge backdrop; sky + ridge horizon + broken cumulus in frame)

v6 changes vs v5:
  1. Cinematic camera framing: cameras placed at 1300–2200 m absolute, pitched
     ~22–35° below horizontal looking south. The AOI terrain only reaches 1685 m
     (no in-AOI peaks) — so a *backdrop ridge mesh* is added ~10 km south of the
     AOI, peaks ~2400–3200 m, hazy blue-grey, atmospheric-faded — it reads as the
     distant Alps on the horizon. Foreground terrain → midground rising terrain →
     backdrop ridge as the horizon line → broken cumulus → sky in the top ~25–30%.
  2. World Background Strength raised to 0.30 (was 0.15). At -1.5 EV exposure the
     Nishita sky is now clearly visible above the horizon without blowing the ground.
  3. Aerial haze domain volume added (light density) so distance fades and the
     backdrop ridge reads as far away.
  4. Cloud deck at 2400 m base (well above the 1685 m terrain), broken coverage,
     huge XY extent → cumulus visible in the upper sky band in most frames; the
     cameras (1300–2200 m) sit below the deck. Cirrus at 6500 m.
  5. All v5 goodness kept: exposure (sun 2 W, -1.5 EV, AgX Med-High Contrast),
     natural ortho colours, forest overlay on terrain, ortho-textured building roofs.
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

    # World Background Strength: v6 = 0.30 (was 0.15 in v5 → sky barely visible).
    # At -1.5 EV view exposure 0.30 makes the Nishita sky read as a proper afternoon
    # blue above the horizon without blowing out the ortho-textured ground.
    world = scene.world
    if world and world.use_nodes:
        for node in world.node_tree.nodes:
            if node.type == "BACKGROUND":
                node.inputs["Strength"].default_value = 0.30
                print(f"[assemble] World Background Strength = 0.30 (v6: sky visible)")

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
    print(f"[assemble] view exposure = -1.5 EV (v6)")

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
# 10a. Backdrop ridge — the AOI terrain only reaches 1685 m, so there is no
#      in-AOI mountain wall. We add a long procedural ridge mesh ~10 km south of
#      the AOI (beyond the terrain edge), peaks ~2400–3200 m, with a flat hazy
#      blue-grey material. Combined with the aerial haze it reads as the distant
#      Alps on the horizon and stops the camera from seeing into the void when it
#      looks past the terrain edge.
# ---------------------------------------------------------------------------

backdrop_obj_name = ""


def _do_backdrop_ridge():
    global backdrop_obj_name
    bbox = scene.get("bbox_utm32n")
    anchor = scene.get("utm32n_anchor")
    if not bbox or not anchor:
        raise RuntimeError("bbox/anchor not set — cannot place backdrop ridge")
    # Scene-local AOI extents (terrain is centred at origin).
    size_x = float(bbox[2] - bbox[0])
    size_y = float(bbox[3] - bbox[1])
    half_x = size_x / 2.0
    half_y = size_y / 2.0
    # South edge of the terrain is at local Y = -half_y.
    south_edge_y = -half_y

    existing = bpy.data.objects.get("BackdropRidge")
    if existing is not None:
        bpy.data.objects.remove(existing, do_unlink=True)

    # Grid plane: wide in X (4× AOI), modest in Y, placed FAR south of the AOI edge
    # so it reads as a distant mountain range. The whole mesh uses a pure-emission
    # uniform haze-blue material → no shading contrast, so only its jagged TOP EDGE
    # silhouette is visible against the sky (exactly how distant mountains look in
    # atmospheric haze). Base near valley-floor level so the floor blends with the
    # haze-filled gap below it.
    width_x = size_x * 4.0
    depth_y = 2800.0                 # shallow → little floor area in frame
    ridge_center_y = south_edge_y - 11000.0   # ~11 km beyond the AOI edge
    bpy.ops.mesh.primitive_grid_add(
        x_subdivisions=160, y_subdivisions=16, size=1.0,
        location=(0.0, ridge_center_y, 0.0),
    )
    ridge = bpy.context.active_object
    ridge.name = "BackdropRidge"
    ridge.scale = (width_x, depth_y, 1.0)
    bpy.ops.object.transform_apply(scale=True)

    # Displace with two CLOUDS noise textures at different scales → a soft jagged
    # alpine silhouette.
    for old in ("BackdropRidgeNoise1", "BackdropRidgeNoise2"):
        t = bpy.data.textures.get(old)
        if t is not None:
            bpy.data.textures.remove(t)
    tex1 = bpy.data.textures.new("BackdropRidgeNoise1", type="CLOUDS")
    tex1.noise_scale = 0.45          # large, gentle masses
    tex1.noise_depth = 3
    tex2 = bpy.data.textures.new("BackdropRidgeNoise2", type="CLOUDS")
    tex2.noise_scale = 0.13          # mid-scale undulation
    tex2.noise_depth = 1

    m1 = ridge.modifiers.new("RidgeDisp1", type="DISPLACE")
    m1.texture = tex1
    m1.strength = 2300.0
    m1.mid_level = 0.0
    m1.direction = "Z"
    m1.texture_coords = "OBJECT"

    m2 = ridge.modifiers.new("RidgeDisp2", type="DISPLACE")
    m2.texture = tex2
    m2.strength = 650.0
    m2.mid_level = 0.5
    m2.direction = "Z"
    m2.texture_coords = "OBJECT"

    # Base near valley-floor level.
    ridge.location.z = 600.0

    sub = ridge.modifiers.new("RidgeSubsurf", type="SUBSURF")
    sub.levels = 1
    sub.render_levels = 2

    # Pure-emission haze-blue material — no shading, so the mesh renders as a flat
    # pale silhouette. Slight downward gradient (lighter toward the base) would be
    # ideal but a single flat colour reads fine through the haze.
    mat = bpy.data.materials.get("BackdropRidge_Haze")
    if mat is not None:
        bpy.data.materials.remove(mat)
    mat = bpy.data.materials.new("BackdropRidge_Haze")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (300, 0)
    emis = nt.nodes.new("ShaderNodeEmission")
    emis.location = (0, 0)
    emis.inputs["Color"].default_value = (0.60, 0.68, 0.80, 1.0)   # pale haze-blue
    emis.inputs["Strength"].default_value = 1.7
    nt.links.new(emis.outputs[0], out.inputs["Surface"])
    ridge.data.materials.append(mat)

    for poly in ridge.data.polygons:
        poly.use_smooth = True

    backdrop_obj_name = ridge.name
    print(f"[assemble] backdrop ridge: '{ridge.name}' centred at Y={ridge_center_y:.0f} "
          f"(~11 km S of AOI edge), base Z=600 m, peaks ~2300-2900 m, pure-emission haze-blue")

_try("backdrop ridge", _do_backdrop_ridge)

# ---------------------------------------------------------------------------
# 10b. Aerial haze — light volume scatter domain so distance fades and the
#      backdrop ridge reads as far away. Also softens the UDIM ortho seams.
# ---------------------------------------------------------------------------

def _do_haze():
    world_mod = importlib.import_module(
        "bl_ext.user_default.blender_tools.world_setup")
    bbox = scene.get("bbox_utm32n")
    if not bbox:
        raise RuntimeError("bbox not set — cannot size haze domain")
    size_x = float(bbox[2] - bbox[0])
    size_y = float(bbox[3] - bbox[1])
    # bbox_meters expects (x, y, z); use a generous Z for the haze column.
    # padding_fraction inflates ALL axes by (1+pf). X/Y big enough to reach the ridge
    # (~13.9 km south of centre); Z kept modest (~250-2200 m, a thin low haze layer)
    # so the upper sky stays clear AND the volume cost is small. Density kept very
    # light — just enough atmospheric depth-cue without milking the picture.
    pf = 2.0   # X/Y coverage = ±(half_extent × 3.0); for size_y=10376 → ±15564 m → reaches the ridge near edge
    haze = world_mod.add_domain_cube_volume(
        bbox_meters=(size_x, size_y, 1950.0 / (1.0 + pf)),
        density=2.5e-6,            # faint — ~2% over 9 km foreground, ~7% over ~26 km
        anisotropy=0.4,
        color_rgb=(0.60, 0.72, 0.87),
        object_name="AerialHaze",
        padding_fraction=pf,
    )
    # Centre the (now ~1950 m tall) haze slab at ~1225 m → covers ~250-2200 m.
    haze.location.z = 1225.0
    print(f"[assemble] aerial haze domain added: density 2.5e-6 (faint, low-level), AOI + backdrop")

_try("aerial haze", _do_haze)

# ---------------------------------------------------------------------------
# 10c. Clouds — v6: deck base 2400 m (well above the 1685 m AOI terrain), huge XY
#      extent so cumulus fills the upper sky band; broken coverage so landscape is
#      still visible; cameras (1300–2200 m) sit below the deck. Cirrus at 6500 m.
# ---------------------------------------------------------------------------

def _do_clouds():
    result = bpy.ops.blender_tools.add_clouds(
        "EXEC_DEFAULT",
        coverage=0.45,
        base_altitude_m=2050.0,    # camera 1900-2400 m → at/just below the deck base
        thickness_m=750.0,         # top at 2800 m
        density=0.16,              # punchy so cumulus clearly reads against the sky
        detail=0.5,
        cirrus=True,
        cirrus_altitude_m=6500.0,
    )
    if result != {"FINISHED"}:
        raise RuntimeError(f"add_clouds returned {result}")

    # --- v6 fix: clouds.py wires the Noise textures to "Object" texcoords assuming
    # the box mesh is 1×1×1, but _make_cloud_box applies the scale so the mesh is
    # ~14 km wide → Object coords are ±7000 → noise Scale 2.5 → ~17500 cycles =
    # incoherent per-voxel noise = no visible cloud blobs. We rescale the Noise
    # `Scale` inputs by 1/(mesh half-extent) so the original "N blobs across the box"
    # intent is restored.  Do this BEFORE enlarging the box.
    cum = bpy.data.objects.get("Clouds_Cumulus")
    if cum is not None:
        dims = cum.dimensions
        half_extent = max(dims.x, dims.y) / 2.0  # ~7000 m
        mat = next((m for m in cum.data.materials if m and "Cumulus" in m.name), None)
        if mat and mat.use_nodes:
            for node in mat.node_tree.nodes:
                if node.type == "TEX_NOISE":
                    try:
                        node.inputs["Scale"].default_value = (
                            node.inputs["Scale"].default_value / half_extent
                        )
                    except Exception:
                        pass
            print(f"[assemble] cumulus noise Scale rescaled by 1/{half_extent:.0f} "
                  "(clouds.py Object-coord bug workaround)")

    # Enlarge the cloud decks so they span the whole view incl. the backdrop ridge.
    # (Object-space noise is now relative to the mesh extent, so enlarging the box
    # would shrink the apparent blob count — instead we enlarge MUCH more modestly.)
    for nm in ("Clouds_Cumulus", "Clouds_Cirrus"):
        obj = bpy.data.objects.get(nm)
        if obj is not None:
            obj.scale = (obj.scale.x * 1.6, obj.scale.y * 1.6, obj.scale.z)

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
# 12. v6 Camera — 4 hand-placed keyframes, cinematic south-facing framing.
#
# Each keyframe is (camera position, look-at target, focal, bank) — far more
# robust than guessing Euler angles. Rotation is computed via to_track_quat so
# the camera's -Z points at the target and +Y points up (then we add a bank roll).
#
# AOI: terrain centred at Blender origin. X[-4612,4612], Y[-5188,5188], Z[762,1685].
#   Forggensee centre ≈ (-1500, +2800, ~780). Schwangau/castles ≈ (+200, +350, ~830).
#   AOI south edge at Y = -5188. The backdrop ridge sits centred at Y ≈ -16200,
#   base Z ~550 m, peaks ~2400-2700 m → the distant "Alps on the horizon" in the south.
#
# Composition: cameras at 1900-2400 m (above the 1685 m AOI top), aimed at the AOI
# terrain midground (Y -2800..-3400, Z 1100..1500) so the downward pitch is ~12-22°.
# Frame reads: foreground lake/meadows → midground forested hills → distant ridge on
# the horizon → broken cumulus → Nishita sky in the top ~25-35%.
# ---------------------------------------------------------------------------

def _do_camera_v6():
    import mathutils

    # Remove any existing camera objects / curves.
    for obj in list(bpy.data.objects):
        if obj.type in ("CAMERA", "CURVE", "EMPTY") and (
            obj.name.startswith("FlightPath") or
            obj.name.startswith("Camera") or
            obj.name.startswith("CameraRig")
        ):
            bpy.data.objects.remove(obj, do_unlink=True)

    bpy.ops.object.camera_add(location=(0, 0, CAMERA_ALTITUDE_M))
    cam = bpy.context.active_object
    cam.name = "Camera_v6"
    scene.camera = cam
    cam.data.clip_start = 5.0
    cam.data.clip_end = 200_000.0

    # Strategy: cameras sit at 1900-2400 m (above the AOI terrain top of 1685 m), so
    # the AOI terrain — its high forested ground around Y -1000..-4000 — is the
    # midground subject, with the distant backdrop ridge (peaks ~2400 m, ~16 km S)
    # on the horizon behind it, broken cumulus above (deck 2200-2800 m), and Nishita
    # sky filling the top ~25-35%. We aim each target at the AOI terrain midground so
    # the downward pitch lands ~12-25° — enough to show foreground + midground +
    # horizon + sky in one frame.
    #
    # keyframes: (frame, cam_xyz, target_xyz, focal_mm, bank_deg)
    kf = [
        # Shot A — over the NW Forggensee shore, looking S over the lake toward the
        # forested hills + the distant ridge. Camera 2000 m (above the ~780 m lake);
        # target on the AOI terrain SSW (the hill near Füssen / Tegelberg-side foothill).
        # Wide 28mm for the lake→hills→mountains sweep, slight bank.
        (  1,
         (-2100.0,  3700.0, 2000.0),     # camera: NW quadrant
         ( -700.0, -3200.0, 1100.0),     # target: AOI terrain, SSW
         28.0, -4.0),

        # Shot B — over Schwangau, looking S toward the forested ridge-line and the
        # distant mountains beyond. Camera 1950 m; target on the high AOI ground due S.
        # 35mm, level.
        ( 60,
         (  300.0,  2300.0, 1950.0),
         (  300.0, -3000.0, 1500.0),
         35.0, 0.0),

        # Shot C — banking over the eastern forested ridge, looking SW across the
        # valley toward the AOI's western forested high ground + the distant ridge.
        # Camera 1900 m; target SW on the AOI terrain. 30mm wide, a stronger bank for
        # a dynamic "flying" feel.
        (120,
         ( 2700.0,  1900.0, 1900.0),
         (-2200.0, -2800.0, 1200.0),
         30.0, -9.0),

        # Shot D — high establishing, camera 2400 m near the N edge, looking S over
        # the whole AOI (Forggensee → fields/forest → AOI hills → distant ridge → sky).
        # 35mm. The "scale" shot.
        (180,
         (-1000.0,  4300.0, 2400.0),
         ( -300.0, -3400.0, 1300.0),
         35.0, 0.0),
    ]

    scene.frame_start = 1
    scene.frame_end = 180

    for (frame, cam_pos, target, focal, bank_deg) in kf:
        scene.frame_set(frame)
        cam.location = mathutils.Vector(cam_pos)
        # Direction from camera to target.
        direction = mathutils.Vector(target) - mathutils.Vector(cam_pos)
        # Camera's -Z looks at the target, +Y is up.
        rot_quat = direction.to_track_quat('-Z', 'Y')
        rot_euler = rot_quat.to_euler()
        # Apply bank as a roll about the view axis (camera local Z).
        if abs(bank_deg) > 1e-3:
            roll = mathutils.Matrix.Rotation(math.radians(bank_deg), 4, 'Z')
            m = rot_euler.to_matrix().to_4x4() @ roll
            rot_euler = m.to_euler()
        cam.rotation_euler = rot_euler
        cam.data.lens = focal
        cam.keyframe_insert(data_path="location", frame=frame)
        cam.keyframe_insert(data_path="rotation_euler", frame=frame)
        cam.data.keyframe_insert(data_path="lens", frame=frame)
        # Report the effective pitch.
        d = direction.normalized()
        pitch = math.degrees(math.asin(max(-1.0, min(1.0, d.z))))
        print(f"[assemble] cam v6 kf {frame}: pos={tuple(round(c) for c in cam_pos)} "
              f"target={tuple(round(t) for t in target)} pitch={pitch:.1f}° "
              f"focal={focal}mm bank={bank_deg}°")

    print(f"[assemble] v6 camera: 4 look-at keyframes, cameras 1900–2400 m, "
          f"aimed at the AOI midground (pitch ~12–22° down); ridge+clouds+sky in upper frame")

_try("camera v6", _do_camera_v6)

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
# Render 4 stills — v6 naming, 1920×1080, 128 spp + OIDN
# v6: render at the 4 keyframe frames (1, 60, 120, 180) for the best compositions.
# ---------------------------------------------------------------------------

render_frames = [1, 60, 120, 180]

print(f"[assemble] rendering frames: {render_frames} (v6 keyframe positions)")
print(f"[assemble] render: {RENDER_WIDTH}×{RENDER_HEIGHT}, "
      f"{scene.cycles.samples} spp, denoise={scene.cycles.use_denoising}")

rendered_paths = []
scene.render.use_file_extension = True
scene.render.use_render_cache = False

for i, frame in enumerate(render_frames):
    stem = f"allgaeu_v6_frame{frame:04d}"
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
print("ALLGÄU ASSEMBLY SUMMARY (v6)")
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
