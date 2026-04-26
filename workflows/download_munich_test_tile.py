"""Download a 1x1 km Bavaria OpenData test tile (Marienplatz, München).

Pulls DGM1 (heightmap), DOP20 (orthophoto), and LoD2 (CityGML buildings) for
the single 1km grid cell that contains Marienplatz, into ./data/raw/<dataset>/.

Usage:
    python workflows/download_munich_test_tile.py
"""
from __future__ import annotations
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT / "OpenMap_Unifier"))
sys.path.insert(0, str(ROOT / "OpenMap_Unifier" / "backend"))

from backend.downloader import MapDownloader  # noqa: E402

# Tiny WKT polygon (~50 m square) centred on Marienplatz, München (WGS84).
# generate_1km_grid_files will resolve this to the single 1km tile that
# contains the polygon (likely 32691_5334).
MARIENPLATZ_WKT = (
    "POLYGON((11.5750 48.1370, 11.5760 48.1370, 11.5760 48.1378, "
    "11.5750 48.1378, 11.5750 48.1370))"
)

DATASETS = ["dgm1", "dop20", "lod2"]


def main() -> int:
    out_root = ROOT / "data" / "raw"
    out_root.mkdir(parents=True, exist_ok=True)

    print(f"[*] Output root: {out_root}")
    print(f"[*] Region: Marienplatz (~50 m square) -> 1 km tile lookup")

    grand_total = 0
    for dataset in DATASETS:
        out_dir = out_root / dataset
        out_dir.mkdir(parents=True, exist_ok=True)
        dl = MapDownloader(download_dir=str(out_dir))
        files = dl.generate_1km_grid_files(MARIENPLATZ_WKT, dataset=dataset)
        print(f"\n--- {dataset.upper()} — {len(files)} tile(s) ---")
        for name, url in files:
            print(f"    {name}  <-  {url}")
            ok = dl.download_file(url, name)
            target = out_dir / name
            if ok and target.is_file():
                size_mb = target.stat().st_size / (1024 * 1024)
                grand_total += target.stat().st_size
                print(f"    OK ({size_mb:.1f} MB) -> {target}")
            else:
                print(f"    FAIL")

    print(f"\n[*] Total downloaded: {grand_total / (1024 * 1024):.1f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
