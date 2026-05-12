"""Diagnostic script — print terrain Z range, camera Z, building count, etc."""
from __future__ import annotations
import bpy
import math

blend = r"G:\Privat\Projekte\Work\OpenMap_Workflow\data\scene_allgaeu-forggensee.blend"
bpy.ops.wm.open_mainfile(filepath=blend)

scene = bpy.context.scene

# Terrain
terrain = None
for obj in bpy.data.objects:
    if obj.type == "MESH" and obj.name.startswith("Terrain"):
        terrain = obj
        break

if terrain:
    try:
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = terrain.evaluated_get(depsgraph)
        import mathutils
        bb = [evaluated.matrix_world @ mathutils.Vector(v) for v in evaluated.bound_box]
        zs = [v.z for v in bb]
        print(f"TERRAIN bbox Z: min={min(zs):.1f} max={max(zs):.1f}")
        print(f"TERRAIN name: {terrain.name}")
    except Exception as e:
        print(f"TERRAIN eval error: {e}")
        bb = terrain.bound_box
        zs = [v[2] for v in bb]
        print(f"TERRAIN local bbox Z: min={min(zs):.1f} max={max(zs):.1f}")
else:
    print("TERRAIN: not found")

# Camera
cam = scene.camera
if cam:
    print(f"CAMERA name: {cam.name}")
    print(f"CAMERA location: {cam.location.x:.1f}, {cam.location.y:.1f}, {cam.location.z:.1f}")
    if cam.parent:
        p = cam.parent
        print(f"CAMERA parent: {p.name} @ {p.location.x:.1f}, {p.location.y:.1f}, {p.location.z:.1f}")
else:
    print("CAMERA: not found")

# Curves
curves = [o for o in bpy.data.objects if o.type == "CURVE"]
for curve in curves:
    splines = curve.data.splines
    if splines:
        zvals = [bp.co.z for sp in splines for bp in sp.bezier_points]
        if not zvals:
            zvals = [p.co.z for sp in splines for p in sp.points]
        if zvals:
            print(f"CURVE {curve.name}: Z min={min(zvals):.1f} max={max(zvals):.1f} mean={sum(zvals)/len(zvals):.1f}")

# Buildings
building_count = 0
for coll in bpy.data.collections:
    if coll.name in ("Buildings", "CityJSON"):
        building_count = len([o for o in coll.objects if o.type == "MESH"])
print(f"BUILDINGS: {building_count}")

# Trees
tree_empties = [o for o in bpy.data.objects if o.type == "EMPTY" and "tree" in o.name.lower()]
print(f"TREE EMPTIES: {len(tree_empties)}")

# Clouds
cloud_objs = [o for o in bpy.data.objects if any(k in o.name.lower() for k in ("cloud", "cumulus", "cirrus"))]
print(f"CLOUD OBJECTS: {[o.name for o in cloud_objs]}")

# All objects summary
print(f"TOTAL OBJECTS: {len(list(bpy.data.objects))}")
print(f"TOTAL MESHES: {len([o for o in bpy.data.objects if o.type == 'MESH'])}")
print(f"FRAME RANGE: {scene.frame_start} – {scene.frame_end}")
print(f"ENGINE: {scene.render.engine}")
print(f"ANCHOR: {scene.get('utm32n_anchor', 'not set')}")
