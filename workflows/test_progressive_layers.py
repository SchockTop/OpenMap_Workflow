"""test_progressive_layers.py — render the same camera with every pipeline
layer added one at a time, then run the blind ground-detector across the
output PNGs.

This exists because the existing showcase images (01_poster.png through
07_feature_groundcover.png) do not visibly contain a draped orthophoto: the
"ground" reads as flat dark color or empty space. We need a frame-by-frame
test that proves which layer is missing.

Workflow:
  1. Download tiles for the region (uses full_pipeline phase 1+2+3).
  2. Spawn Blender to run _blender_progressive_layers.py — produces
     showcase/ground_layer_test/00_sky.png ... 08_atmosphere.png.
  3. Run blind_ground_detector.py against the output dir — emits a verdict
     per frame plus a summary of which layer first introduced ortho-photo
     content (high color variance, photo-like spatial frequency).

Usage:
  python workflows/test_progressive_layers.py --region muc-marienplatz-50m
  python workflows/test_progressive_layers.py --skip-render  # re-score only
"""
from __future__ import annotations
import argparse, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "OpenMap_Unifier"))
sys.path.insert(0, str(ROOT / "openmap_blender_tools"))

BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--region", default="muc-marienplatz-50m")
    ap.add_argument("--datasets", nargs="+", default=["dgm1", "dop40", "lod2"])
    ap.add_argument("--out-dir", type=Path,
                    default=ROOT / "showcase" / "ground_layer_test")
    ap.add_argument("--data-dir", type=Path, default=ROOT / "data")
    ap.add_argument("--skip-render", action="store_true",
                    help="Re-run blind detector against an existing out-dir.")
    ap.add_argument("--engine", default="BLENDER_EEVEE_NEXT")
    args = ap.parse_args(argv)

    out_dir = args.out_dir
    out_dir.mkdir(parents=True, exist_ok=True)

    if not args.skip_render:
        from workflows.full_pipeline import (
            phase1_download, phase2_preprocess, phase3_lod2,
            _bbox_utm32n_for_polygon,
        )
        from workflows.region_presets import polygon_for_region

        poly = polygon_for_region(args.region)
        bbox = _bbox_utm32n_for_polygon(poly)
        raw = args.data_dir / "raw"
        proc = args.data_dir / "processed"
        downloads = phase1_download(poly, args.datasets, raw)
        heightmap, ortho_dir = phase2_preprocess(downloads, bbox, proc)
        cityjson = phase3_lod2(downloads, proc)
        if heightmap is None:
            print("[!] no DGM1 — cannot proceed", file=sys.stderr)
            return 1

        runner = ROOT / "workflows" / "_blender_progressive_layers.py"
        cmd = [str(BLENDER), "--background", "--python", str(runner), "--",
               "--heightmap", str(heightmap),
               "--bbox-utm32n", *map(str, bbox),
               "--out-dir", str(out_dir),
               "--engine", args.engine]
        if ortho_dir:
            cmd += ["--ortho-dir", str(ortho_dir)]
        if cityjson:
            cmd += ["--cityjson", str(cityjson)]
        rc = subprocess.call(cmd)
        if rc != 0:
            print(f"[!] Blender exited rc={rc}", file=sys.stderr)
            return rc

    # Score the rendered frames.
    detector = ROOT / "workflows" / "blind_ground_detector.py"
    return subprocess.call([sys.executable, str(detector), str(out_dir)])


if __name__ == "__main__":
    sys.exit(main())
