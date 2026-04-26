"""Convert downloaded Bayern OpenData tiles into a Blender .blend scene.

Pipeline (offline-capable, uses vendored GDAL inside openmap_blender_tools):
1. DGM tiles  -> Float32 GeoTIFF heightmap (geo_import.dgm_tif_to_heightmap)
2. DOP tiles  -> UDIM JPEG tiles (geo_import.dop_to_udim_tiles)
3. Inside Blender: terrain plane + Sky + Domain cube + Camera waypoint rig.

Usage:
    blender --background --python workflows/tile_to_blender_scene.py
"""
from __future__ import annotations
import importlib
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "openmap_blender_tools"))

import bpy  # noqa: E402

DATA_RAW = ROOT / "data" / "raw"
DATA_OUT = ROOT / "data" / "processed"
SCENE_OUT = ROOT / "data" / "scene_munich.blend"


def step_heightmap(dgm_dir: Path, out_path: Path) -> Path | None:
    from blender_tools.geo_import import dgm_tif_to_heightmap

    tifs = sorted(dgm_dir.glob("*.tif"))
    if not tifs:
        print(f"[!] no DGM tiles in {dgm_dir} — skipping heightmap")
        return None
    return dgm_tif_to_heightmap(tifs, out_path)


def step_udim_orthos(dop_dir: Path, out_dir: Path) -> list[Path]:
    """Skipped here unless DGM also exists — UDIM needs a real bbox.

    Computing the bbox from the DOP TIF directly via gdalinfo would work but
    is left out of this minimal demo.
    """
    print(f"[i] UDIM ortho tiling not wired in this minimal demo — DOP tiles "
          f"in {dop_dir} can be applied as a single texture instead")
    return []


def build_scene() -> None:
    # Load extension explicitly (read_factory_settings would unload it).
    ext = importlib.import_module("bl_ext.user_default.blender_tools")
    print(f"[i] extension loaded: v{ext.__version__}")
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in list(bpy.data.collections):
        if coll.name != "Collection":
            bpy.data.collections.remove(coll)

    # Sky.
    bpy.ops.blender_tools.setup_sky(preset="client-default")
    # Domain cube (matches our 1km tile size).
    bpy.ops.blender_tools.add_domain_cube(bbox=(1000.0, 1000.0, 200.0),
                                          preset="airbus-clean")

    # Heightmap from real DGM (if downloaded).
    DATA_OUT.mkdir(parents=True, exist_ok=True)
    hm = step_heightmap(DATA_RAW / "dgm1", DATA_OUT / "heightmap.tif")

    if hm is not None:
        terrain_setup = importlib.import_module(
            "bl_ext.user_default.blender_tools.terrain_setup"
        )
        terrain_setup.build_terrain_from_heightmap(
            heightmap_exr=hm,
            size_meters=(1000.0, 1000.0),
            subdivisions=8,
            strength=30.0,
            anchor_utm32n=(691000.0, 5334000.0, 0.0),
        )
        print(f"[OK] terrain built from {hm}")

    bpy.ops.wm.save_as_mainfile(filepath=str(SCENE_OUT))
    print(f"[OK] scene saved: {SCENE_OUT}")


if __name__ == "__main__":
    build_scene()
