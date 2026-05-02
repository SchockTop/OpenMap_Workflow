"""_render_slot.py — render one slot of the visual test matrix.

Run inside Blender:
  blender -b scene.blend --python _render_slot.py -- \
      --slot close_tree --altitude 5 --framing close --preset close-tree \
      --out path/to/close_tree.png

Cameras are anchored on the scene bbox center (computed from CityJSON_*
buildings + Terrain meshes), not world origin — real-world UTM scenes have
non-zero centers. clip_end is set high enough to cover aircraft-approach
altitude + scene span.
"""
from __future__ import annotations
import argparse
import math
import sys

import bpy
import mathutils


def _scene_bbox_center():
    """Return (cx, cy, cz_ground) computed from the Terrain mesh only.

    LoD2 imports may extend well beyond the terrain (whole-tile bbox), which
    drags the average off-terrain. Anchoring on terrain keeps the camera on
    the actual ground footprint.
    """
    xs, ys, zs = [], [], []
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        if "Terrain" not in obj.name:
            continue
        for v in obj.bound_box:
            wv = obj.matrix_world @ mathutils.Vector(v)
            xs.append(wv.x); ys.append(wv.y); zs.append(wv.z)
    if not xs:
        return (0.0, 0.0, 0.0)
    return ((min(xs)+max(xs))/2, (min(ys)+max(ys))/2, max(zs))

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--slot", required=True)
ap.add_argument("--altitude", required=True, type=float)
ap.add_argument("--framing", required=True, choices=("wide", "close"))
ap.add_argument("--preset", required=True)
ap.add_argument("--out", required=True)
args = ap.parse_args(argv)

scene = bpy.context.scene

# === Determinism pin (Blender 5.1 EEVEE Next) ===
scene.render.resolution_x = 768
scene.render.resolution_y = 512
try:
    scene.eevee.taa_render_samples = 32
    scene.eevee.use_motion_blur = False
except AttributeError:
    pass
try:
    scene.cycles.seed = 0
    scene.cycles.device = "CPU"
except AttributeError:
    pass
scene.frame_set(1)

# === Camera setup per slot — anchored on scene bbox ===
cx, cy, cz_ground = _scene_bbox_center()

cam_data = bpy.data.cameras.new(f"VisualCam_{args.slot}")
# Far-clip must cover aircraft-approach altitude (2000m+) + scene span. 20km is safe.
cam_data.clip_start = 0.1
cam_data.clip_end = 20000.0
cam = bpy.data.objects.new(f"VisualCam_{args.slot}", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

if args.framing == "wide":
    if args.altitude < 10.0:
        # FPV / walk: pull camera to the terrain edge so we're not inside the
        # building cluster, and look across it from outside.
        terrain = next((o for o in bpy.data.objects
                        if o.type == "MESH" and "Terrain" in o.name), None)
        if terrain is not None:
            ys = []
            for v in terrain.bound_box:
                wv = terrain.matrix_world @ mathutils.Vector(v)
                ys.append(wv.y)
            edge_y = min(ys) - 30.0
        else:
            edge_y = cy - 200.0
        cam.location = (cx, edge_y, cz_ground + args.altitude)
        direction = mathutils.Vector((cx, cy, cz_ground - 5.0)) - mathutils.Vector(cam.location)
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    else:
        cam.location = (cx, cy - args.altitude * 0.6, cz_ground + args.altitude)
        pitch = math.radians(60.0) if args.altitude > 80 else math.radians(45.0)
        cam.rotation_euler = (pitch, 0.0, 0.0)
    cam_data.lens = 35.0
else:
    target = None
    if args.preset == "close-building":
        target = next((o for o in scene.objects
                       if o.name.startswith("CityJSON_")), None)
    elif args.preset == "close-tree":
        # Geometry-Nodes-scattered trees have no separate object; linked
        # TreeTpl_* templates sit at world origin. Anchor on terrain so we
        # at least see the scattered ground; tree silhouettes will appear in
        # the scatter mask region.
        target = next((o for o in scene.objects
                       if o.type == "MESH" and "Terrain" in o.name), None)
    elif args.preset in ("close-ground-patch", "close-seam"):
        target = next((o for o in scene.objects
                       if o.type == "MESH" and ("Terrain" in o.name or "Plane" in o.name)),
                      None)
    if target is None:
        tx, ty = cx, cy
    else:
        # Use the target's world-space bbox center (object origin is unreliable
        # for terrain meshes where vertices encode absolute elevation but
        # object loc stays at 0,0,0).
        bbx, bby = [], []
        for v in target.bound_box:
            wv = target.matrix_world @ mathutils.Vector(v)
            bbx.append(wv.x); bby.append(wv.y)
        tx = (min(bbx) + max(bbx)) / 2
        ty = (min(bby) + max(bby)) / 2
    cam.location = (tx, ty - args.altitude, cz_ground + args.altitude * 0.5)
    # Look-at: aim camera at target XY, ground level — use track_to math.
    direction = mathutils.Vector((tx, ty, cz_ground)) - mathutils.Vector(cam.location)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = 50.0

# === Render ===
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = args.out
bpy.ops.render.render(write_still=True)
print(f"[render-slot] {args.slot} -> {args.out}")
