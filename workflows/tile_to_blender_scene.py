"""End-to-end: process downloaded Bayern OpenData tiles into a Blender .blend.

Two phases:
1. CPython (this script): preprocess DGM tiles -> Float32 GeoTIFF heightmap
   via openmap_blender_tools.geo_import (uses vendored GDAL).
2. Blender (subprocess): consume the heightmap + assemble scene
   (sky, domain cube, terrain Subsurf+Displace, save .blend).

Usage:
    python workflows/tile_to_blender_scene.py
"""
from __future__ import annotations
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "openmap_blender_tools"))

DATA_RAW = ROOT / "data" / "raw"
DATA_OUT = ROOT / "data" / "processed"
SCENE_OUT = ROOT / "data" / "scene_munich.blend"
BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")


def phase1_preprocess() -> Path | None:
    """Convert DGM1 tiles -> Float32 GeoTIFF heightmap. Returns the path or None."""
    from blender_tools.geo_import import dgm_tif_to_heightmap

    DATA_OUT.mkdir(parents=True, exist_ok=True)
    tifs = sorted((DATA_RAW / "dgm1").glob("*.tif"))
    if not tifs:
        print(f"[!] no DGM1 tiles in {DATA_RAW / 'dgm1'} — run download_munich_test_tile.py first")
        return None
    out = DATA_OUT / "heightmap.tif"
    print(f"[1/2] geo_import: {len(tifs)} DGM1 tile(s) -> {out}")
    dgm_tif_to_heightmap(tifs, out)
    print(f"      written: {out} ({out.stat().st_size / 1024:.0f} KB)")
    return out


def phase2_blender(heightmap: Path) -> int:
    """Spawn Blender to build the scene from the heightmap."""
    blender_script = ROOT / "workflows" / "_blender_assemble.py"
    blender_script.write_text(_BLENDER_SCRIPT, encoding="utf-8")
    cmd = [
        str(BLENDER),
        "--background",
        "--python", str(blender_script),
        "--",
        "--heightmap", str(heightmap),
        "--out", str(SCENE_OUT),
    ]
    print(f"[2/2] Blender: assembling scene -> {SCENE_OUT}")
    return subprocess.call(cmd)


_BLENDER_SCRIPT = '''"""Blender-side scene assembler. Invoked by tile_to_blender_scene.py."""
import argparse
import importlib
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--heightmap", required=True)
ap.add_argument("--out", required=True)
args = ap.parse_args(argv)

ext = importlib.import_module("bl_ext.user_default.blender_tools")
print(f"[blender] extension v{ext.__version__}")

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for c in list(bpy.data.collections):
    if c.name != "Collection":
        bpy.data.collections.remove(c)

# Sky + atmospheric haze.
bpy.ops.blender_tools.setup_sky(preset="client-default")
bpy.ops.blender_tools.add_domain_cube(bbox=(1000.0, 1000.0, 200.0), preset="airbus-clean")

# Terrain from real DGM1.
terrain_setup = importlib.import_module("bl_ext.user_default.blender_tools.terrain_setup")
terrain_setup.build_terrain_from_heightmap(
    heightmap_exr=args.heightmap,
    size_meters=(1000.0, 1000.0),
    subdivisions=8,
    strength=30.0,
    anchor_utm32n=(691000.0, 5334000.0, 0.0),
)
print(f"[blender] terrain built from {args.heightmap}")

bpy.ops.wm.save_as_mainfile(filepath=args.out)
print(f"[blender] saved: {args.out}")
'''


if __name__ == "__main__":
    hm = phase1_preprocess()
    if hm is None:
        sys.exit(1)
    sys.exit(phase2_blender(hm))
