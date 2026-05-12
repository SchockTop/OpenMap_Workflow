"""Ray-cast from camera A through the bottom-right region to ID the gray slab."""
from __future__ import annotations
import bpy, math, mathutils

bpy.ops.wm.open_mainfile(filepath=r"G:\Privat\Projekte\Work\OpenMap_Workflow\data\scene_allgaeu-forggensee.blend")
scene = bpy.context.scene
dg = bpy.context.evaluated_depsgraph_get()
cam = scene.camera
scene.frame_set(1)
dg = bpy.context.evaluated_depsgraph_get()

cm = cam.matrix_world
loc = cm.translation
print(f"cam @ {loc.x:.0f},{loc.y:.0f},{loc.z:.0f} lens={cam.data.lens}")

# Sample a grid of view rays via camera frame.
# camera.view_frame() gives the 4 corners of the view plane at depth 1.
frame = cam.data.view_frame(scene=scene)  # list of 4 Vector in camera space
# corners: top-right, bottom-right, bottom-left, top-left (Blender order)
import itertools
names = ["TR","BR","BL","TL"]
for nm, corner in zip(names, frame):
    world_corner = cm @ corner
    direction = (world_corner - loc).normalized()
    hit, hloc, hnrm, hidx, hobj, hmat = scene.ray_cast(dg, loc + direction*1.0, direction)
    matname = ""
    if hit and hobj and hobj.data and hasattr(hobj.data, "materials") and len(hobj.data.materials):
        try:
            matname = hobj.data.materials[hmat].name if hmat < len(hobj.data.materials) else "?"
        except Exception:
            matname = "?"
    print(f"  corner {nm}: hit={hobj.name if hit else 'MISS(sky)'} mat={matname} @ {(hloc-loc).length if hit else 0:.0f}m loc={tuple(round(c) for c in hloc) if hit else None}")

# Sample the bottom-right quadrant more densely.
print("bottom-right quadrant samples (u in [0.5,1], v in [0,0.5] of view plane):")
TR, BR, BL, TL = frame
for u in (0.6, 0.75, 0.9, 0.98):
    for v in (0.05, 0.2, 0.35):
        # bilerp: along bottom edge BL->BR is +x, along left edge BL->TL is +y
        # view_frame corners in camera space; build point
        bottom = BL.lerp(BR, u)
        top = TL.lerp(TR, u)
        p = bottom.lerp(top, v)
        wp = cm @ p
        d = (wp - loc).normalized()
        hit, hloc, hnrm, hidx, hobj, hmat = scene.ray_cast(dg, loc + d*1.0, d)
        matname = ""
        if hit and hobj and hobj.data and hasattr(hobj.data,"materials") and len(hobj.data.materials):
            try: matname = hobj.data.materials[hmat].name if hmat < len(hobj.data.materials) else "?"
            except Exception: matname = "?"
        print(f"  u={u} v={v}: {hobj.name if hit else 'MISS'} mat={matname} @ {(hloc-loc).length if hit else 0:.0f}m")
