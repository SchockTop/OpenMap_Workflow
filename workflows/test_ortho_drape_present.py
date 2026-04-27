"""Verify the produced scene .blend has DOP ortho material on the terrain."""
import argparse, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", default=str(ROOT / "data" / "scene_muc-sued-4x2.blend"))
    args = ap.parse_args()
    scene = Path(args.scene)
    if not scene.is_file():
        print(f"FAIL: scene not found {scene}")
        return 1
    out_json = ROOT / "data" / "ortho_drape_check.json"
    runner_script = """
import bpy, json, sys
from pathlib import Path
result = {"materials": [], "found_dop_image": False, "found_terrain": False}
for obj in bpy.data.objects:
    if obj.type != "MESH": continue
    if "Terrain" in obj.name or "Plane" in obj.name:
        result["found_terrain"] = True
        for slot in obj.material_slots:
            if slot.material is None: continue
            mat_info = {"name": slot.material.name, "image_sources": []}
            if slot.material.use_nodes:
                for n in slot.material.node_tree.nodes:
                    if n.type == "TEX_IMAGE" and n.image is not None:
                        src = getattr(n.image, "source", "")
                        fp = getattr(n.image, "filepath", "")
                        mat_info["image_sources"].append({"source": src, "filepath": fp})
                        if "ortho" in fp.lower() or "dop" in fp.lower() or src == "TILED":
                            result["found_dop_image"] = True
            result["materials"].append(mat_info)
Path(sys.argv[-1]).write_text(json.dumps(result, indent=2))
"""
    cmd = [str(BLENDER), "--background", str(scene),
           "--python-expr", runner_script, "--", str(out_json)]
    rc = subprocess.call(cmd)
    if rc != 0 or not out_json.is_file():
        print(f"FAIL: introspection error rc={rc}")
        return 1
    import json
    data = json.loads(out_json.read_text())
    print(f"  found_terrain: {data['found_terrain']}")
    print(f"  found_dop_image: {data['found_dop_image']}")
    print(f"  materials: {data['materials']}")
    if data["found_terrain"] and data["found_dop_image"]:
        print("PASS - DOP ortho is on terrain")
        return 0
    print("FAIL - DOP ortho not found on terrain")
    return 1


if __name__ == "__main__":
    sys.exit(main())
