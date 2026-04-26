"""_blender_introspect.py - load a .blend and dump actual setting values to JSON.

Run inside Blender:
    blender --background <scene>.blend --python _blender_introspect.py -- --out report.json
"""
import argparse, json, sys
from pathlib import Path
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--out", required=True)
args = ap.parse_args(argv)

scene = bpy.context.scene
report = {
    "render": {
        "engine": scene.render.engine,
        "resolution_x": scene.render.resolution_x,
        "resolution_y": scene.render.resolution_y,
        "use_simplify": bool(scene.render.use_simplify),
        "simplify_subdivision": int(scene.render.simplify_subdivision),
        "image_settings_format": scene.render.image_settings.file_format,
    },
    "view_settings": {
        "view_transform": scene.view_settings.view_transform,
        "look": scene.view_settings.look,
    },
    "cycles": {
        "samples": int(getattr(scene.cycles, "samples", -1)),
        "use_denoising": bool(getattr(scene.cycles, "use_denoising", False)),
    } if hasattr(scene, "cycles") else {},
    "eevee": {
        "taa_render_samples": int(getattr(scene.eevee, "taa_render_samples", -1)),
        "use_volumetric_shadows": bool(getattr(scene.eevee, "use_volumetric_shadows", False)),
    } if hasattr(scene, "eevee") else {},
    "objects": [],
    "materials": [],
    "collections": [c.name for c in bpy.data.collections],
    "scene_props": {
        k: list(scene[k]) if hasattr(scene[k], "__iter__") else scene[k]
        for k in scene.keys() if k not in {"_RNA_UI"}
    },
}

for obj in bpy.data.objects:
    o = {
        "name": obj.name, "type": obj.type,
        "location": list(obj.location), "scale": list(obj.scale),
        "modifiers": [],
    }
    for m in obj.modifiers:
        m_info = {"name": m.name, "type": m.type}
        if m.type == "SUBSURF":
            m_info["levels"] = m.levels; m_info["render_levels"] = m.render_levels
            m_info["subdivision_type"] = m.subdivision_type
        elif m.type == "DISPLACE":
            m_info["strength"] = m.strength; m_info["mid_level"] = m.mid_level
            m_info["texture_coords"] = m.texture_coords
            m_info["texture"] = m.texture.name if m.texture else None
        o["modifiers"].append(m_info)
    if obj.type == "MESH":
        depsgraph = bpy.context.evaluated_depsgraph_get()
        eval_obj = obj.evaluated_get(depsgraph)
        o["vert_count"] = len(eval_obj.data.vertices)
        o["face_count"] = len(eval_obj.data.polygons)
        o["base_vert_count"] = len(obj.data.vertices)  # for debug
        o["material_slots"] = [s.material.name if s.material else None for s in obj.material_slots]
    if obj.type == "CAMERA":
        o["camera"] = {
            "lens": obj.data.lens, "sensor_width": obj.data.sensor_width,
            "clip_start": obj.data.clip_start, "clip_end": obj.data.clip_end,
        }
    if obj.type == "CURVE":
        o["curve"] = {
            "path_duration": obj.data.path_duration,
            "use_path": obj.data.use_path,
            "spline_count": len(obj.data.splines),
            "anim_data": bool(obj.data.animation_data and obj.data.animation_data.action),
        }
    report["objects"].append(o)

for mat in bpy.data.materials:
    m = {"name": mat.name, "use_nodes": mat.use_nodes, "node_types": []}
    if mat.use_nodes and mat.node_tree:
        m["node_types"] = [n.type for n in mat.node_tree.nodes]
        for n in mat.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image:
                m["image_source"] = n.image.source
                m["image_filepath"] = n.image.filepath
    report["materials"].append(m)

Path(args.out).parent.mkdir(parents=True, exist_ok=True)
Path(args.out).write_text(json.dumps(report, indent=2, default=str), encoding="utf-8")
print(f"[introspect] report -> {args.out}")
