import bpy, sys, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--")+1:]
obj_path, out_png = argv[0], argv[1]
res = int(argv[2]) if len(argv) > 2 else 1800
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=obj_path, up_axis='Z', forward_axis='Y')
objs = [o for o in bpy.context.scene.objects if o.type=='MESH']
mn=Vector((1e18,)*3); mx=Vector((-1e18,)*3)
for o in objs:
    for c in o.bound_box:
        w=o.matrix_world@Vector(c); mn=Vector(map(min,mn,w)); mx=Vector(map(max,mx,w))
center=(mn+mx)*0.5; size=mx-mn
for o in objs: o.location-=center
w=bpy.data.worlds.new("W"); bpy.context.scene.world=w; w.use_nodes=True
bg=w.node_tree.nodes["Background"]; bg.inputs[0].default_value=(1,1,1,1); bg.inputs[1].default_value=1.6
sd=bpy.data.lights.new("S",'SUN'); sd.energy=2.5
s=bpy.data.objects.new("S",sd); bpy.context.collection.objects.link(s)
s.rotation_euler=(math.radians(50),0,math.radians(40))
for m in bpy.data.materials:
    if not m.use_nodes: continue
    b=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    if b:
        b.inputs["Roughness"].default_value=1.0
        if "Specular IOR Level" in b.inputs: b.inputs["Specular IOR Level"].default_value=0.0
cd=bpy.data.cameras.new("C"); cam=bpy.data.objects.new("C",cd)
bpy.context.collection.objects.link(cam); bpy.context.scene.camera=cam; cd.lens=50
diag=Vector((size.x,size.y,0)).length
el=math.radians(18); az=math.radians(20); r=diag*0.62
cam.location=(r*math.cos(el)*math.sin(az), -r*math.cos(el)*math.cos(az), max(size.z*1.2, r*math.sin(el)))
look=Vector((0,0,size.z*0.15))-Vector(cam.location)
cam.rotation_euler=look.to_track_quat('-Z','Y').to_euler()
sc=bpy.context.scene; sc.render.engine='BLENDER_EEVEE'
sc.render.resolution_x=res; sc.render.resolution_y=int(res*0.62)
sc.view_settings.view_transform='Standard'; sc.render.filepath=out_png
bpy.ops.render.render(write_still=True); print("wrote",out_png)
