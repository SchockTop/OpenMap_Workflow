"""_test_feature_in_blender.py — per-feature render harness."""
import argparse, importlib, math, sys
from pathlib import Path
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--feature", required=True)
ap.add_argument("--out-dir", required=True)
args = ap.parse_args(argv)

ext = importlib.import_module("bl_ext.user_default.blender_tools")
features_mod = importlib.import_module("bl_ext.user_default.blender_tools.features")


def build_synthetic_scene(feature_name: str = ""):
    """Reset to empty + add lights + per-feature camera framing.

    Different features need different scene compositions:
      - buildings-textured: close-up of one cube building
      - trees: ground plane with low camera looking forward
      - ground-shader: ground plane tilted toward horizon
      - groundcover: ground plane with FPV camera at z=1.5
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # Scene-wide settings.
    scene = bpy.context.scene
    # Engine name varies between Blender 4.2 and 5.x.
    valid = {item.identifier for item in
             bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        if candidate in valid:
            scene.render.engine = candidate
            break
    scene.render.resolution_x = 512
    scene.render.resolution_y = 384
    try:
        scene.eevee.taa_render_samples = 32
    except AttributeError:
        pass
    try:
        scene.view_settings.view_transform = "AgX"
    except (TypeError, AttributeError):
        pass
    scene.view_settings.exposure = 1.0

    # Sun (always — fixes "everything is dark" bug).
    bpy.ops.object.light_add(type="SUN", location=(0, 0, 50))
    sun = bpy.context.active_object
    sun.data.energy = 5.0
    sun.data.angle = 0.05  # softer shadows
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))

    # World — give it a sky so background isn't solid black.
    world = bpy.data.worlds.new("ShowcaseWorld") if "ShowcaseWorld" not in bpy.data.worlds \
        else bpy.data.worlds["ShowcaseWorld"]
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.5, 0.6, 0.78, 1)  # daylight sky blue
        bg.inputs["Strength"].default_value = 0.4

    # Per-feature scene composition.
    if feature_name == "buildings-textured":
        # Add a ground plane so we have something below the building (avoids floating-cube look).
        bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
        ground = bpy.context.active_object
        ground.name = "GroundPlane"
        gmat = bpy.data.materials.new("GroundMat")
        gmat.use_nodes = True
        gbsdf = gmat.node_tree.nodes.get("Principled BSDF")
        if gbsdf:
            gbsdf.inputs["Base Color"].default_value = (0.28, 0.24, 0.18, 1)
            gbsdf.inputs["Roughness"].default_value = 0.95
        ground.data.materials.append(gmat)
        # ONE cube building 10m wide.
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 5))
        cube = bpy.context.active_object
        cube.name = "CityJSON_TEST_001"
        cube.scale = (5.0, 5.0, 5.0)
        bpy.ops.object.transform_apply(scale=True)
        # Camera closer + aimed via track-to so cube fills frame.
        bpy.ops.object.camera_add(location=(14, -14, 10))
        cam = bpy.context.active_object
        scene.camera = cam
        # Track-to constraint pointing at cube origin.
        con = cam.constraints.new("TRACK_TO")
        con.target = cube
        con.track_axis = "TRACK_NEGATIVE_Z"
        con.up_axis = "UP_Y"
        return cube

    if feature_name == "trees":
        # 100×100m plane.
        bpy.ops.mesh.primitive_plane_add(size=100, location=(0, 0, 0))
        plane = bpy.context.active_object
        plane.name = "TerrainPlane"
        # Add a base material so plane is brown-green not white.
        mat = bpy.data.materials.new("PlaneSoil")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (0.32, 0.25, 0.15, 1)
            bsdf.inputs["Roughness"].default_value = 0.95
        plane.data.materials.append(mat)
        # FPV-ish camera 5m off the ground, looking forward at the trees.
        bpy.ops.object.camera_add(location=(0, -30, 5),
                                  rotation=(math.radians(80), 0, 0))
        scene.camera = bpy.context.active_object
        return plane

    if feature_name == "ground-shader":
        # Subdivided plane so per-vertex displacement / shader detail can show variation.
        bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
        plane = bpy.context.active_object
        plane.name = "TerrainPlane"
        # Add a few hills for slope variation (the layered shader blends by slope).
        import bmesh as _bmesh
        bpy.ops.object.mode_set(mode="EDIT")
        bm = _bmesh.from_edit_mesh(plane.data)
        _bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=20, use_grid_fill=True)
        _bmesh.update_edit_mesh(plane.data)
        bpy.ops.object.mode_set(mode="OBJECT")
        # Apply noise displacement so slope varies.
        tex = bpy.data.textures.new("HillNoise", type="MUSGRAVE") if hasattr(bpy.data.textures, "new") else None
        try:
            disp = plane.modifiers.new("Hills", type="DISPLACE")
            if tex is not None:
                disp.texture = tex
            disp.strength = 8.0
        except Exception:
            pass
        # Camera lower + tilted so we see plane perspective + horizon.
        bpy.ops.object.camera_add(location=(0, -50, 4),
                                  rotation=(math.radians(82), 0, 0))
        scene.camera = bpy.context.active_object
        return plane

    if feature_name == "groundcover":
        bpy.ops.mesh.primitive_plane_add(size=100, location=(0, 0, 0))
        plane = bpy.context.active_object
        plane.name = "TerrainPlane"
        # GROUND-LEVEL camera (FPV walking) so the dense scatter is visible.
        bpy.ops.object.camera_add(location=(0, -10, 1.5),
                                  rotation=(math.radians(85), 0, 0))
        scene.camera = bpy.context.active_object
        return plane

    # Default: cube building (back-compat with old harness).
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 4))
    cube = bpy.context.active_object
    cube.name = "CityJSON_TEST_001"
    cube.scale = (5.0, 5.0, 4.0)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.camera_add(location=(20, -20, 12), rotation=(1.1, 0, 0.785))
    scene.camera = bpy.context.active_object
    return cube


def render(out_path):
    bpy.context.scene.render.image_settings.file_format = "PNG"
    bpy.context.scene.render.filepath = str(out_path)
    bpy.ops.render.render(write_still=True)


out_dir = Path(args.out_dir)

def _maybe_add_terrain_plane():
    """For features that need a terrain (ground-shader), add a plane both runs."""
    if args.feature in ("ground-shader", "groundcover"):
        # Only add if the per-feature scene didn't already add one.
        if "TerrainPlane" in bpy.data.objects:
            return bpy.data.objects["TerrainPlane"]
        bpy.ops.mesh.primitive_plane_add(size=100, location=(0, 0, 0))
        plane = bpy.context.active_object
        plane.name = "TerrainPlane"
        return plane
    return None


# Render 1: baseline (no feature).
cube = build_synthetic_scene(args.feature)
_maybe_add_terrain_plane()
render(out_dir / "baseline")  # Blender appends .png

# Render 2: with feature.
cube = build_synthetic_scene(args.feature)
context = {"bpy": bpy, "scene": bpy.context.scene,
           "terrain_obj": None, "dop_image": None, "ortho_dir": None,
           "building_objs": [cube], "bbox_utm32n": (-100, -100, 100, 100),
           "anchor_utm32n": (0, 0, 0), "args": args}

# Per-feature scene augmentation: ground-shader needs a terrain plane.
plane = _maybe_add_terrain_plane()
if plane is not None:
    context["terrain_obj"] = plane
    context["building_objs"] = []

features_mod.apply_enabled([args.feature], context)
render(out_dir / "applied")

# Rename Blender's frame-numbered output.
for stem in ("baseline", "applied"):
    target = out_dir / f"{stem}.png"
    candidates = [p for p in out_dir.glob(f"{stem}*.png") if p != target]
    if candidates and not target.exists():
        candidates[0].rename(target)
print(f"[test-feature] rendered baseline + applied for {args.feature!r}")
