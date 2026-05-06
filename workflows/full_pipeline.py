"""full_pipeline.py — end-to-end orchestrator.

Phases (CPython unless noted):
  1. Download tiles via OpenMap_Unifier.MapDownloader.
  2. GDAL preprocess: heightmap mosaic + UDIM ortho tiles.
  3. LoD2 GML -> CityJSON (pure-Python).
  4. (Optional) generate synthetic waypoint CSV from bbox if none provided.
  5. Spawn Blender to assemble scene + render preview.

Usage (download + render):
    python workflows/full_pipeline.py --region muc-sued-4x2 \\
        --datasets dgm1 dop40 lod2 --render-preview

Usage (offline / behind a proxy — bring your own tiles):
    python workflows/full_pipeline.py --skip-download --region muc-sued-4x2 \\
        --local-dgm  path/to/dgm_dir \\
        --local-dop  path/to/dop_dir \\
        --local-lod2 path/to/lod2_dir \\
        --render-preview

    # Or with an explicit bbox (no named region required):
    python workflows/full_pipeline.py --skip-download \\
        --bbox-utm32n 686000 5331000 690000 5333000 \\
        --local-dgm path/to/dgm_dir --render-preview
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


# Map of dataset key -> file extensions accepted when collecting local tiles.
_LOCAL_EXT_BY_DATASET: dict[str, tuple[str, ...]] = {
    "dgm1":  (".tif", ".tiff"),
    "dop20": (".tif", ".tiff"),
    "dop40": (".tif", ".tiff"),
    "lod2":  (".gml", ".xml", ".zip"),
}


def _collect_local_files(paths: list[Path], extensions: tuple[str, ...]) -> list[Path]:
    """Expand a mixed list of files/directories into a flat list of files
    matching `extensions` (case-insensitive)."""
    out: list[Path] = []
    exts = tuple(e.lower() for e in extensions)
    for p in paths:
        p = Path(p)
        if p.is_dir():
            for child in sorted(p.rglob("*")):
                if child.is_file() and child.suffix.lower() in exts:
                    out.append(child)
        elif p.is_file():
            out.append(p)
        else:
            print(f"[1] warning: local path does not exist: {p}", file=sys.stderr)
    return out


def phase1_collect_local(local_inputs: dict[str, list[Path]]) -> dict[str, list[Path]]:
    """Skip-download counterpart of `phase1_download`. Returns the same
    `{dataset: [files...]}` shape from user-supplied paths."""
    result: dict[str, list[Path]] = {}
    for ds, paths in local_inputs.items():
        if not paths:
            continue
        exts = _LOCAL_EXT_BY_DATASET.get(ds, (".tif", ".tiff"))
        files = _collect_local_files(paths, exts)
        result[ds] = files
        print(f"[1] {ds} (local): {len(files)} file(s)")
        if not files:
            print(f"[1] warning: no {exts} files found for {ds} under {paths}",
                  file=sys.stderr)
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
                               preset_name: str = "cinematic-establishing") -> Path:
    """Generate waypoints appropriate for the given camera preset.

    Dispatches via blender_tools.waypoint_generators; falls back to a generic
    S-curve at 1500 m AGL if the dispatcher import fails.
    """
    out.parent.mkdir(parents=True, exist_ok=True)
    try:
        from blender_tools.waypoint_generators import generate_waypoints_for_preset
        pts = generate_waypoints_for_preset(preset_name, bbox)
    except Exception as e:
        print(f"[4] preset waypoint gen failed ({e}); falling back to S-curve")
        from pyproj import Transformer
        t = Transformer.from_crs("EPSG:25832", "EPSG:4326", always_xy=True)
        pts = []
        for i in range(30):
            frac = i / 29
            x = bbox[0] + (bbox[2] - bbox[0]) * frac
            y = bbox[1] + (bbox[3] - bbox[1]) * (0.5 + 0.4 * math.sin(frac * math.pi * 2))
            lon, lat = t.transform(x, y)
            pts.append((lat, lon, 1500.0))
    with out.open("w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["lat", "lon", "alt"])
        for p in pts:
            w.writerow([f"{p[0]:.6f}", f"{p[1]:.6f}", p[2]])
    print(f"[4] waypoints: {out} ({len(pts)} points, preset={preset_name!r})")
    return out


def phase5_blender(heightmap: Path,
                   ortho_dir: Optional[Path],
                   cityjson: Optional[Path],
                   waypoints_csv: Path,
                   bbox_utm32n: tuple[float, float, float, float],
                   out_blend: Path,
                   render_png: Optional[Path],
                   engine: str = "BLENDER_EEVEE_NEXT",
                   enable: Optional[list[str]] = None,
                   camera_preset: str = "cinematic-establishing",
                   sky_preset: str = "afternoon",
                   quality: str = "preview") -> int:
    blender_script = ROOT / "workflows" / "_blender_assemble_full.py"
    cmd = [
        str(BLENDER), "--background", "--python", str(blender_script), "--",
        "--heightmap", str(heightmap),
        "--bbox-utm32n", *map(str, bbox_utm32n),
        "--out-blend", str(out_blend),
        "--engine", engine,
        "--waypoints-csv", str(waypoints_csv),
        "--camera-preset", camera_preset,
        "--sky-preset", sky_preset,
        "--quality", quality,
    ]
    if ortho_dir:
        cmd += ["--ortho-dir", str(ortho_dir)]
    if cityjson:
        cmd += ["--cityjson", str(cityjson)]
    if render_png:
        cmd += ["--render-png", str(render_png)]
    if enable:
        cmd += ["--enable", *enable]
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
    ap.add_argument("--region", default=None,
                    help="Named region from region_presets. Required unless "
                         "--bbox-utm32n is supplied alongside --skip-download.")
    ap.add_argument("--bbox-utm32n", nargs=4, type=float, metavar=("XMIN", "YMIN", "XMAX", "YMAX"),
                    help="Explicit AOI bbox in EPSG:25832 metres. Overrides "
                         "the bbox derived from --region when supplied.")
    ap.add_argument("--datasets", nargs="+", default=["dgm1", "dop40", "lod2"])
    ap.add_argument("--skip-download", action="store_true",
                    help="Skip phase 1 entirely and ingest already-downloaded "
                         "tiles via --local-dgm / --local-dop / --local-lod2 "
                         "(useful behind a proxy, or when fetching manually "
                         "from the LDBV portal).")
    ap.add_argument("--local-dgm", nargs="+", type=Path, default=[],
                    help="Files or directories with DGM .tif tiles "
                         "(implies --skip-download).")
    ap.add_argument("--local-dop", nargs="+", type=Path, default=[],
                    help="Files or directories with DOP orthophoto .tif tiles "
                         "(implies --skip-download).")
    ap.add_argument("--local-lod2", nargs="+", type=Path, default=[],
                    help="Files or directories with LoD2 .gml/.xml/.zip files "
                         "(implies --skip-download).")
    ap.add_argument("--engine", default="BLENDER_EEVEE_NEXT")
    ap.add_argument("--camera-preset", default="cinematic-establishing",
                    choices=["fpv-walk", "fpv-bike", "low-drone", "mid-drone",
                             "cinematic-establishing", "aircraft-approach"],
                    help="Camera altitude envelope")
    ap.add_argument("--sky-preset", default="afternoon",
                    choices=["noon", "golden-hour", "blue-hour", "dawn",
                             "overcast", "afternoon"],
                    help="Time-of-day lighting mood")
    ap.add_argument("--quality", default="preview",
                    choices=["draft", "preview", "final"],
                    help="Render quality envelope (resolution + samples + simplify).")
    ap.add_argument("--render-preview", action="store_true")
    ap.add_argument("--enable", nargs="*", default=[],
                    help="Feature modules to apply (e.g. buildings-textured trees)")
    ap.add_argument("--data-dir", type=Path, default=ROOT / "data")
    args = ap.parse_args(argv)

    skip_download = args.skip_download or any(
        [args.local_dgm, args.local_dop, args.local_lod2]
    )

    # Resolve bbox: explicit --bbox-utm32n wins; otherwise derive from --region.
    poly: Optional[str] = None
    if args.region:
        poly = polygon_for_region(args.region)
    if args.bbox_utm32n:
        bbox = tuple(args.bbox_utm32n)  # type: ignore[assignment]
        source = "explicit"
    elif poly is not None:
        bbox = _bbox_utm32n_for_polygon(poly)
        source = f"region {args.region!r}"
    else:
        ap.error("must supply --region (or --bbox-utm32n with --skip-download)")
    print(f"AOI bbox UTM32N ({source}): {bbox}")

    if not skip_download and poly is None:
        ap.error("--region is required to download tiles; use --skip-download "
                 "with --local-* paths if you've fetched the data yourself")

    raw = args.data_dir / "raw"
    proc = args.data_dir / "processed"
    if skip_download:
        # Auto-fall-back to data/raw/<ds>/ if the user passed --skip-download
        # without explicit local paths (lets you re-run after a prior download).
        local_inputs: dict[str, list[Path]] = {
            "dgm1":  list(args.local_dgm)  or ([raw / "dgm1"]  if (raw / "dgm1").is_dir() else []),
            "dop40": list(args.local_dop)  or ([raw / "dop40"] if (raw / "dop40").is_dir() else []),
            "dop20": ([raw / "dop20"] if (raw / "dop20").is_dir() and not args.local_dop else []),
            "lod2":  list(args.local_lod2) or ([raw / "lod2"]  if (raw / "lod2").is_dir() else []),
        }
        downloads = phase1_collect_local(local_inputs)
    else:
        downloads = phase1_download(poly, args.datasets, raw)

    heightmap, ortho_dir = phase2_preprocess(downloads, bbox, proc)
    if heightmap is None:
        print("[!] no DGM1 tiles available — cannot build terrain "
              "(supply --local-dgm or run without --skip-download)",
              file=sys.stderr)
        return 1
    cityjson = phase3_lod2(downloads, proc)
    waypoints = phase4_synthetic_waypoints(
        bbox, args.data_dir / "flight_path.csv",
        preset_name=args.camera_preset,
    )
    region_tag = args.region or "custom"
    out_blend = args.data_dir / f"scene_{region_tag}.blend"
    render_png = args.data_dir / f"render_{region_tag}.png" if args.render_preview else None
    return phase5_blender(heightmap, ortho_dir, cityjson, waypoints, bbox,
                          out_blend, render_png, engine=args.engine,
                          enable=args.enable,
                          camera_preset=args.camera_preset,
                          sky_preset=args.sky_preset,
                          quality=args.quality)


if __name__ == "__main__":
    sys.exit(main())
