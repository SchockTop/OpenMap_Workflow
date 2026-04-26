"""full_pipeline.py — end-to-end orchestrator.

Phases (CPython unless noted):
  1. Download tiles via OpenMap_Unifier.MapDownloader.
  2. GDAL preprocess: heightmap mosaic + UDIM ortho tiles.
  3. LoD2 GML -> CityJSON (pure-Python).
  4. (Optional) generate synthetic waypoint CSV from bbox if none provided.
  5. Spawn Blender to assemble scene + render preview.

Usage:
    python workflows/full_pipeline.py --region muc-sued-4x2 \\
        --datasets dgm1 dop40 lod2 --render-preview
"""
from __future__ import annotations
import argparse, csv, math, subprocess, sys
from pathlib import Path
from typing import Optional

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "OpenMap_Unifier"))
sys.path.insert(0, str(ROOT / "openmap_blender_tools"))

from backend.downloader import MapDownloader  # noqa: E402
from blender_tools.geo_import import dgm_tif_to_heightmap, dop_to_udim_tiles  # noqa: E402
from blender_tools.citygml_import import gml_to_cityjson_pure  # noqa: E402
from workflows.region_presets import polygon_for_region  # noqa: E402

BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")


def phase1_download(poly_wkt: str, datasets: list[str], out_root: Path) -> dict[str, list[Path]]:
    out_root.mkdir(parents=True, exist_ok=True)
    result: dict[str, list[Path]] = {}
    for ds in datasets:
        dl_dir = out_root / ds
        dl_dir.mkdir(parents=True, exist_ok=True)
        dl = MapDownloader(download_dir=str(dl_dir))
        files = dl.generate_1km_grid_files(poly_wkt, dataset=ds)
        print(f"[1] {ds}: {len(files)} tile(s)")
        downloaded: list[Path] = []
        for name, url in files:
            ok = dl.download_file(url, name)
            if ok and (dl_dir / name).is_file():
                downloaded.append(dl_dir / name)
        result[ds] = downloaded
    return result


def phase2_preprocess(downloads: dict[str, list[Path]],
                      bbox_utm32n: tuple[float, float, float, float],
                      out_root: Path) -> tuple[Optional[Path], Optional[Path]]:
    out_root.mkdir(parents=True, exist_ok=True)
    heightmap = None
    if downloads.get("dgm1"):
        heightmap = out_root / "heightmap.tif"
        dgm_tif_to_heightmap(downloads["dgm1"], heightmap, bbox_utm32n=bbox_utm32n)
        print(f"[2] heightmap: {heightmap} ({heightmap.stat().st_size//1024} KB)")

    ortho_dir = None
    ortho_input = downloads.get("dop40") or downloads.get("dop20")
    if ortho_input:
        ortho_dir = out_root / "ortho_udim"
        # Compute tile grid covering the bbox in 1024-px square tiles roughly
        # matching the source resolution (DOP20 = 0.2 m/px, DOP40 = 0.4 m/px).
        size_x = bbox_utm32n[2] - bbox_utm32n[0]
        size_y = bbox_utm32n[3] - bbox_utm32n[1]
        # 1 UDIM tile per ~1 km — conservative.
        u_tiles = max(1, int(math.ceil(size_x / 1000)))
        v_tiles = max(1, int(math.ceil(size_y / 1000)))
        if u_tiles > 10:
            u_tiles = 10  # UDIM convention caps u at 10 wide.
        dop_to_udim_tiles(ortho_input, bbox_utm32n=bbox_utm32n,
                          output_dir=ortho_dir,
                          tile_grid=(u_tiles, v_tiles),
                          resolution_per_tile=2048)
        print(f"[2] ortho UDIM: {ortho_dir} ({u_tiles}x{v_tiles} tiles)")
    return heightmap, ortho_dir


def phase3_lod2(downloads: dict[str, list[Path]], out_root: Path) -> Optional[Path]:
    gmls = downloads.get("lod2", [])
    if not gmls:
        return None
    out_root.mkdir(parents=True, exist_ok=True)
    out = out_root / "buildings.cityjson"
    gml_to_cityjson_pure(gmls, out)
    print(f"[3] CityJSON: {out} ({out.stat().st_size//1024} KB)")
    return out


def phase4_synthetic_waypoints(bbox: tuple[float, float, float, float],
                               out: Path,
                               altitude_agl_m: float = 1500.0,
                               n_points: int = 30) -> Path:
    """Generate a 30-waypoint S-curve across the bbox at constant altitude."""
    out.parent.mkdir(parents=True, exist_ok=True)
    from pyproj import Transformer
    t = Transformer.from_crs("EPSG:25832", "EPSG:4326", always_xy=True)
    xmin, ymin, xmax, ymax = bbox
    with out.open("w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["lat", "lon", "alt"])
        for i in range(n_points):
            frac = i / (n_points - 1)
            x = xmin + (xmax - xmin) * frac
            # Sinusoidal Y so the path isn't a boring straight line.
            y = ymin + (ymax - ymin) * (0.5 + 0.4 * math.sin(frac * math.pi * 2))
            lon, lat = t.transform(x, y)
            w.writerow([f"{lat:.6f}", f"{lon:.6f}", altitude_agl_m])
    print(f"[4] waypoints: {out} ({n_points} points)")
    return out


def phase5_blender(heightmap: Path,
                   ortho_dir: Optional[Path],
                   cityjson: Optional[Path],
                   waypoints_csv: Path,
                   bbox_utm32n: tuple[float, float, float, float],
                   out_blend: Path,
                   render_png: Optional[Path],
                   engine: str = "BLENDER_EEVEE_NEXT") -> int:
    blender_script = ROOT / "workflows" / "_blender_assemble_full.py"
    cmd = [
        str(BLENDER), "--background", "--python", str(blender_script), "--",
        "--heightmap", str(heightmap),
        "--bbox-utm32n", *map(str, bbox_utm32n),
        "--out-blend", str(out_blend),
        "--engine", engine,
        "--waypoints-csv", str(waypoints_csv),
    ]
    if ortho_dir:
        cmd += ["--ortho-dir", str(ortho_dir)]
    if cityjson:
        cmd += ["--cityjson", str(cityjson)]
    if render_png:
        cmd += ["--render-png", str(render_png)]
    print(f"[5] Blender -> {out_blend}")
    return subprocess.call(cmd)


def _bbox_utm32n_for_polygon(poly_wkt: str) -> tuple[float, float, float, float]:
    from shapely.wkt import loads
    from shapely.geometry import Polygon
    from pyproj import Transformer
    poly = loads(poly_wkt)
    t = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True)
    proj = Polygon([t.transform(x, y) for x, y in poly.exterior.coords])
    return proj.bounds  # (xmin, ymin, xmax, ymax)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--region", required=True, help="Named region from region_presets.")
    ap.add_argument("--datasets", nargs="+", default=["dgm1", "dop40", "lod2"])
    ap.add_argument("--engine", default="BLENDER_EEVEE_NEXT")
    ap.add_argument("--render-preview", action="store_true")
    ap.add_argument("--data-dir", type=Path, default=ROOT / "data")
    args = ap.parse_args(argv)

    poly = polygon_for_region(args.region)
    bbox = _bbox_utm32n_for_polygon(poly)
    print(f"Region {args.region!r} bbox UTM32N: {bbox}")

    raw = args.data_dir / "raw"
    proc = args.data_dir / "processed"
    downloads = phase1_download(poly, args.datasets, raw)
    heightmap, ortho_dir = phase2_preprocess(downloads, bbox, proc)
    if heightmap is None:
        print("[!] no DGM1 downloaded — cannot build terrain", file=sys.stderr)
        return 1
    cityjson = phase3_lod2(downloads, proc)
    waypoints = phase4_synthetic_waypoints(
        bbox, args.data_dir / "flight_path.csv",
    )
    out_blend = args.data_dir / f"scene_{args.region}.blend"
    render_png = args.data_dir / f"render_{args.region}.png" if args.render_preview else None
    return phase5_blender(heightmap, ortho_dir, cityjson, waypoints, bbox,
                          out_blend, render_png, engine=args.engine)


if __name__ == "__main__":
    sys.exit(main())
