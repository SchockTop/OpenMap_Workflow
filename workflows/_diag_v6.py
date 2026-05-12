"""Quick v6 camera diagnostic — terrain heights at cam XY, ray-cast view dir."""
from __future__ import annotations
import bpy, math, mathutils

blend = r"G:\Privat\Projekte\Work\OpenMap_Workflow\data\scene_allgaeu-forggensee.blend"
bpy.ops.wm.open_mainfile(filepath=blend)
scene = bpy.context.scene
dg = bpy.context.evaluated_depsgraph_get()

terrain = next((o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Terrain")), None)
print(f"terrain: {terrain.name if terrain else None}")

# bbox of evaluated terrain
ev = terrain.evaluated_get(dg)
bb = [ev.matrix_world @ mathutils.Vector(v) for v in ev.bound_box]
xs = [v.x for v in bb]; ys = [v.y for v in bb]; zs = [v.z for v in bb]
print(f"terrain bbox: X[{min(xs):.0f},{max(xs):.0f}] Y[{min(ys):.0f},{max(ys):.0f}] Z[{min(zs):.0f},{max(zs):.0f}]")

def height_at(x, y):
    # ray down from high up
    origin = mathutils.Vector((x, y, 5000.0))
    direction = mathutils.Vector((0, 0, -1))
    hit, loc, nrm, idx, obj, mat = scene.ray_cast(dg, origin, direction)
    return loc.z if hit else None

cam = scene.camera
print(f"camera: {cam.name}")
# eval keyframes
for frame in (1, 60, 120, 180):
    scene.frame_set(frame)
    cm = cam.matrix_world
    loc = cm.translation
    # view direction = -Z of camera matrix
    vdir = -(cm.to_3x3() @ mathutils.Vector((0,0,1)))
    vdir.normalize()
    h = height_at(loc.x, loc.y)
    # ray-cast along view dir
    hit, hloc, hnrm, hidx, hobj, hmat = scene.ray_cast(dg, loc + vdir*5.0, vdir)
    hitobj = hobj.name if hit else "MISS"
    dist = (hloc - loc).length if hit else 0
    # pitch below horizontal
    pitch = math.degrees(math.asin(max(-1,min(1,vdir.z))))
    # heading (atan2 of x,y) ; -Y = south = 180
    head = math.degrees(math.atan2(vdir.x, -vdir.y))
    print(f"f{frame}: cam=({loc.x:.0f},{loc.y:.0f},{loc.z:.0f}) groundZ={h} "
          f"vdir=({vdir.x:.2f},{vdir.y:.2f},{vdir.z:.2f}) pitch={pitch:.1f}° heading={head:.0f}° "
          f"firsthit={hitobj} @ {dist:.0f}m lens={cam.data.lens}mm")

# heights at key spots
print("\nground heights:")
for nm, x, y in [("center",0,0), ("Forggensee~",-1500,2800), ("S-edge mid",0,-4800),
                 ("S-edge E",1200,-3500), ("S-edge W",-2000,-4500),
                 ("camB pos",300,2200), ("camD pos",-1000,3800), ("camA pos",-1800,3500),
                 ("camC pos",2500,2000)]:
    print(f"  {nm}: ({x},{y}) -> {height_at(x,y)}")
