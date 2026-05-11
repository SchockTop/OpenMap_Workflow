import bpy, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--")+1:]
obj_path = argv[0]
out_png  = argv[1]
view     = argv[2] if len(argv) > 2 else "oblique"   # oblique | top
res      = int(argv[3]) if len(argv) > 3 else 1600

# fresh scene
bpy.ops.wm.read_factory_settings(use_empty=True)

# import OBJ (geo data is Z-up, easting=X northing=Y)
bpy.ops.wm.obj_import(filepath=obj_path, up_axis='Z', forward_axis='Y')

objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not objs:
    raise SystemExit("no mesh imported")

# world bbox
mn = Vector(( 1e18,  1e18,  1e18))
mx = Vector((-1e18, -1e18, -1e18))
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        mn = Vector(map(min, mn, w))
        mx = Vector(map(max, mx, w))
center = (mn + mx) * 0.5
size = mx - mn
diag = size.length
print("BBOX min", mn, "max", mx, "size", size)

# recenter everything to origin (keep Z relative too for nicer framing)
for o in objs:
    o.location -= center

# flat-ish aerial look: emission-ish via strong world light + soft sun
world = bpy.data.worlds.new("W"); bpy.context.scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs[0].default_value = (1, 1, 1, 1)
bg.inputs[1].default_value = 1.5

sun_data = bpy.data.lights.new("Sun", 'SUN'); sun_data.energy = 3.0
sun = bpy.data.objects.new("Sun", sun_data); bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(45), 0, math.radians(35))

# make materials shadeless-ish (aerial textures already contain lighting): low roughness off, just diffuse
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf:
        bsdf.inputs["Roughness"].default_value = 1.0
        if "Specular IOR Level" in bsdf.inputs:
            bsdf.inputs["Specular IOR Level"].default_value = 0.0

# camera
cam_data = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam); bpy.context.scene.camera = cam
cam_data.lens = 35
r = diag * 0.85
if view == "top":
    cam.location = (0, 0, r)
    cam.rotation_euler = (0, 0, 0)
else:
    az = math.radians(35); el = math.radians(38)
    cam.location = (r*math.cos(el)*math.sin(az), -r*math.cos(el)*math.cos(az), r*math.sin(el))
    # aim at origin
    d = -Vector(cam.location)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = res
sc.render.resolution_y = int(res*0.75)
sc.render.film_transparent = False
sc.view_settings.view_transform = 'Standard'
sc.render.filepath = out_png
bpy.ops.render.render(write_still=True)
print("wrote", out_png)
