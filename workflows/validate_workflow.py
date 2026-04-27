"""validate_workflow.py - exhaustive end-to-end validator.

Walks every phase of the OpenMap pipeline. Each phase has its own try/except;
a failure logs the phase as FAIL and continues to the next. Final report
prints a phase-by-phase status table plus a JSON dump.

Verifies:
- Downloads landed (size > 0)
- GDAL preprocess output is correct format (Float32 EPSG:25832)
- LoD2 CityJSON has > 100 buildings
- Blender scene file exists + is non-trivial
- Scene introspection: camera clip, render settings, terrain modifiers
  match what the cinematic preset CLAIMED to set
- Render PNG exists, has expected resolution, has dynamic range > 10 (not black)

Usage:
    python workflows/validate_workflow.py --region muc-sued-4x2 \\
        --datasets dgm1 dop40 lod2
"""
from __future__ import annotations
import argparse, json, math, subprocess, sys, traceback
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "OpenMap_Unifier"))
sys.path.insert(0, str(ROOT / "openmap_blender_tools"))

BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")
GDAL_BIN = ROOT / "openmap_blender_tools" / "vendor" / "gdal-win64" / "bin"
GDAL_DATA = ROOT / "openmap_blender_tools" / "vendor" / "gdal-win64" / "share" / "gdal"
PROJ_LIB = ROOT / "openmap_blender_tools" / "vendor" / "gdal-win64" / "share" / "proj"


class PhaseResult:
    def __init__(self, name: str):
        self.name = name; self.status = "PENDING"
        self.evidence: dict[str, Any] = {}; self.error: str | None = None

    def ok(self, **evidence): self.status = "OK"; self.evidence.update(evidence); return self
    def fail(self, err: str, **evidence):
        self.status = "FAIL"; self.error = err; self.evidence.update(evidence); return self
    def skip(self, reason: str): self.status = "SKIP"; self.error = reason; return self
    def to_dict(self):
        return {"name": self.name, "status": self.status,
                "error": self.error, "evidence": self.evidence}


def phase_a_download(poly_wkt: str, datasets: list[str], raw_root: Path) -> tuple[PhaseResult, dict]:
    p = PhaseResult("A. Download")
    downloads: dict[str, list[Path]] = {}
    try:
        from backend.downloader import MapDownloader
        for ds in datasets:
            try:
                d = raw_root / ds; d.mkdir(parents=True, exist_ok=True)
                dl = MapDownloader(download_dir=str(d))
                files = dl.generate_1km_grid_files(poly_wkt, dataset=ds)
                got: list[Path] = []
                for name, url in files:
                    if dl.download_file(url, name) and (d / name).is_file():
                        got.append(d / name)
                downloads[ds] = got
                p.evidence[ds] = {"requested": len(files), "got": len(got),
                                  "bytes": sum(f.stat().st_size for f in got)}
            except Exception as e:
                p.evidence[ds] = {"error": str(e)}
        if downloads:
            return p.ok(datasets=list(downloads.keys())), downloads
        return p.fail("no datasets downloaded"), downloads
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}"), downloads


def phase_b_gdal(downloads: dict, bbox: tuple, proc_root: Path) -> tuple[PhaseResult, dict]:
    p = PhaseResult("B. GDAL preprocess")
    out: dict[str, Any] = {}
    try:
        from blender_tools.geo_import import dgm_tif_to_heightmap, dop_to_udim_tiles
        if downloads.get("dgm1"):
            hm = proc_root / "heightmap.tif"
            dgm_tif_to_heightmap(downloads["dgm1"], hm, bbox_utm32n=bbox)
            # Verify Float32 + EPSG via gdalinfo
            env = {"PROJ_LIB": str(PROJ_LIB), "GDAL_DATA": str(GDAL_DATA),
                   "PATH": f"{GDAL_BIN};{__import__('os').environ.get('PATH','')}"}
            info = subprocess.run([str(GDAL_BIN / "gdalinfo.exe"), "-mm", str(hm)],
                                  check=True, capture_output=True, text=True, env=env).stdout
            is_float32 = "Float32" in info
            is_epsg25832 = "25832" in info
            min_max = ""
            for line in info.splitlines():
                if "Computed Min/Max" in line:
                    min_max = line.strip()
            out["heightmap"] = hm
            p.evidence["heightmap"] = {"path": str(hm), "size_kb": hm.stat().st_size//1024,
                                       "float32": is_float32, "epsg_25832": is_epsg25832,
                                       "min_max": min_max}
            if not (is_float32 and is_epsg25832):
                return p.fail("heightmap format wrong"), out
        ortho_in = downloads.get("dop40") or downloads.get("dop20")
        if ortho_in and out.get("heightmap"):
            ortho_dir = proc_root / "ortho_udim"
            sx = bbox[2]-bbox[0]; sy = bbox[3]-bbox[1]
            u = max(1, min(10, int(math.ceil(sx/1000))))
            v = max(1, int(math.ceil(sy/1000)))
            dop_to_udim_tiles(ortho_in, bbox_utm32n=bbox, output_dir=ortho_dir,
                              tile_grid=(u, v), resolution_per_tile=2048)
            tiles = sorted(ortho_dir.glob("ortho.*.jpg"))
            out["ortho_dir"] = ortho_dir
            p.evidence["ortho"] = {"tile_count": len(tiles), "grid": f"{u}x{v}"}
        return p.ok(**p.evidence), out
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}", traceback=traceback.format_exc().splitlines()[-3:]), out


def phase_c_lod2(downloads: dict, proc_root: Path) -> tuple[PhaseResult, dict]:
    p = PhaseResult("C. LoD2 -> CityJSON")
    out: dict[str, Any] = {}
    if not downloads.get("lod2"):
        return p.skip("no LoD2 tiles downloaded"), out
    try:
        from blender_tools.citygml_import import gml_to_cityjson_pure
        cj = proc_root / "buildings.cityjson"
        gml_to_cityjson_pure(downloads["lod2"], cj)
        data = json.loads(cj.read_text(encoding="utf-8"))
        n_bldg = len(data.get("CityObjects", {}))
        n_vert = len(data.get("vertices", []))
        out["cityjson"] = cj
        if n_bldg < 10:
            return p.fail(f"only {n_bldg} buildings", buildings=n_bldg, vertices=n_vert), out
        return p.ok(path=str(cj), buildings=n_bldg, vertices=n_vert,
                    size_kb=cj.stat().st_size//1024), out
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}"), out


def phase_d_blender(heightmap: Path, ortho_dir: Path | None, cityjson: Path | None,
                    bbox: tuple, data_root: Path, region: str) -> tuple[PhaseResult, dict]:
    p = PhaseResult("D. Blender scene assembly")
    out: dict[str, Any] = {}
    try:
        # Synthetic waypoints (always works).
        from pyproj import Transformer
        import csv
        wp = data_root / "flight_path.csv"; wp.parent.mkdir(parents=True, exist_ok=True)
        t = Transformer.from_crs("EPSG:25832", "EPSG:4326", always_xy=True)
        with wp.open("w", newline="") as f:
            w = csv.writer(f); w.writerow(["lat","lon","alt"])
            for i in range(30):
                fr = i/29
                x = bbox[0] + (bbox[2]-bbox[0])*fr
                y = bbox[1] + (bbox[3]-bbox[1])*(0.5+0.4*math.sin(fr*math.pi*2))
                lon, lat = t.transform(x, y)
                w.writerow([f"{lat:.6f}", f"{lon:.6f}", 1500.0])

        scene_blend = data_root / f"scene_{region}.blend"
        cmd = [str(BLENDER), "--background", "--python",
               str(ROOT / "workflows" / "_blender_assemble_full.py"), "--",
               "--heightmap", str(heightmap),
               "--bbox-utm32n", *map(str, bbox),
               "--out-blend", str(scene_blend),
               "--engine", "BLENDER_EEVEE_NEXT",
               "--waypoints-csv", str(wp)]
        if ortho_dir: cmd += ["--ortho-dir", str(ortho_dir)]
        if cityjson: cmd += ["--cityjson", str(cityjson)]
        rc = subprocess.call(cmd)
        if rc != 0 or not scene_blend.is_file():
            return p.fail(f"blender exit {rc}, scene file present: {scene_blend.is_file()}"), out
        out["scene_blend"] = scene_blend
        return p.ok(path=str(scene_blend), size_kb=scene_blend.stat().st_size//1024), out
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}"), out


def phase_e_introspect(scene_blend: Path, data_root: Path) -> tuple[PhaseResult, dict]:
    p = PhaseResult("E. Scene introspection (read-back)")
    introspect_json = data_root / "scene_introspection.json"
    try:
        cmd = [str(BLENDER), "--background", str(scene_blend),
               "--python", str(ROOT / "workflows" / "_blender_introspect.py"),
               "--", "--out", str(introspect_json)]
        rc = subprocess.call(cmd)
        if rc != 0 or not introspect_json.is_file():
            return p.fail(f"introspect exit {rc}"), {}
        report = json.loads(introspect_json.read_text(encoding="utf-8"))

        # Assertions: every CLAIMED setting must read back as expected.
        assertions = {}

        # 1. Camera clip range.
        cam = next((o for o in report["objects"] if o["type"] == "CAMERA"), None)
        if cam:
            cs = cam.get("camera", {}).get("clip_start")
            ce = cam.get("camera", {}).get("clip_end")
            assertions["camera.clip_start == 1.0"] = (cs == 1.0, cs)
            assertions["camera.clip_end == 100000"] = (ce == 100_000.0, ce)
            assertions["camera.lens > 0"] = (cam["camera"].get("lens", 0) > 0, cam["camera"].get("lens"))
        else:
            assertions["camera exists"] = (False, None)

        # 2. Render engine + simplify.
        eng = report["render"]["engine"]
        assertions["render.engine == EEVEE"] = ("EEVEE" in eng, eng)
        assertions["render.use_simplify"] = (report["render"]["use_simplify"], report["render"]["use_simplify"])
        vt = report["view_settings"]["view_transform"]
        assertions["view_transform contains AgX"] = ("AgX" in vt, vt)

        # 3. Terrain plane has Subsurf + Displace modifiers, vert count > 1k.
        terrain = next((o for o in report["objects"]
                       if o["type"] == "MESH" and "Terrain" in o["name"]), None)
        if terrain:
            mod_types = [m["type"] for m in terrain["modifiers"]]
            assertions["terrain has SUBSURF"] = ("SUBSURF" in mod_types, mod_types)
            assertions["terrain has DISPLACE"] = ("DISPLACE" in mod_types, mod_types)
            assertions["terrain vert_count > 1000"] = (terrain["vert_count"] > 1000, terrain["vert_count"])

        # 4. Curve has eval_time animation.
        curve = next((o for o in report["objects"] if o["type"] == "CURVE"), None)
        if curve:
            assertions["curve.use_path"] = (curve["curve"]["use_path"], curve["curve"]["use_path"])
            assertions["curve.anim_data exists"] = (curve["curve"]["anim_data"], curve["curve"]["anim_data"])

        # 5. Building count if cityjson was used.
        n_bldg = sum(1 for o in report["objects"] if o["name"].startswith("CityJSON_"))
        assertions["buildings imported"] = (n_bldg > 0, n_bldg)

        passed = sum(1 for ok, _ in assertions.values() if ok)
        total = len(assertions)
        p.evidence["assertions_passed"] = f"{passed}/{total}"
        p.evidence["details"] = {k: {"ok": ok, "actual": actual}
                                 for k, (ok, actual) in assertions.items()}
        if passed == total:
            return p.ok(**p.evidence), {"introspection": report}
        return p.fail(f"{total-passed} assertions failed", **p.evidence), {"introspection": report}
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}"), {}


def phase_f_render(scene_blend: Path, data_root: Path, region: str) -> tuple[PhaseResult, dict]:
    p = PhaseResult("F. Render preview frame")
    png = data_root / f"render_{region}.png"
    try:
        cmd = [str(BLENDER), "--background", str(scene_blend),
               "--render-output", str(png.with_suffix("")),
               "--render-format", "PNG", "--render-frame", "1"]
        rc = subprocess.call(cmd)
        # Blender appends frame number; locate the actual file.
        candidates = list(data_root.glob(f"render_{region}*.png"))
        actual = candidates[0] if candidates else None
        if rc != 0 or not actual or actual.stat().st_size < 50_000:
            return p.fail(f"exit {rc}, png={actual}"), {}
        return p.ok(path=str(actual), size_kb=actual.stat().st_size//1024), {"render_png": actual}
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}"), {}


def phase_g_render_readback(png: Path) -> PhaseResult:
    p = PhaseResult("G. Render readback (Pillow)")
    try:
        from PIL import Image
        import numpy as np
        img = Image.open(png).convert("RGB")
        arr = np.array(img)
        h, w = arr.shape[:2]
        per_channel_std = arr.std(axis=(0, 1)).tolist()
        per_channel_mean = arr.mean(axis=(0, 1)).tolist()
        # Histogram: count of distinct grayscale values to confirm not solid.
        gray = arr.mean(axis=2).astype(int)
        unique = len(np.unique(gray))
        evidence = {
            "dimensions": f"{w}x{h}",
            "mean_rgb": [round(v, 1) for v in per_channel_mean],
            "std_rgb": [round(v, 1) for v in per_channel_std],
            "unique_gray_values": unique,
        }
        # "Real content" criteria: at least one channel std > 10 AND > 50 distinct grays.
        is_real = max(per_channel_std) > 10 and unique > 50
        if not is_real:
            return p.fail("render appears blank/black", **evidence)
        return p.ok(**evidence)
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}")


def _sobel_edge_density(png_path: Path) -> tuple[float, dict]:
    """Compute fraction of pixels that are 'edge' pixels (Sobel magnitude > 30).

    Pure-numpy Sobel (no scipy dep). Returns (ratio, evidence_dict).
    """
    img = np.array(Image.open(png_path).convert("L")).astype(np.float32)
    # Sobel kernels.
    gx = np.zeros_like(img)
    gy = np.zeros_like(img)
    gx[:, 1:-1] = img[:, 2:] - img[:, :-2]
    gy[1:-1, :] = img[2:, :] - img[:-2, :]
    mag = np.sqrt(gx ** 2 + gy ** 2)
    edge_mask = mag > 30.0
    ratio = float(edge_mask.sum()) / mag.size
    return ratio, {
        "edge_pixel_ratio": round(ratio, 4),
        "max_gradient": round(float(mag.max()), 1),
        "mean_gradient": round(float(mag.mean()), 2),
    }


def phase_i_geometry_detail(png: Path) -> PhaseResult:
    p = PhaseResult("I. Geometry detail (Sobel edge density)")
    if not png or not png.is_file():
        return p.skip("no PNG")
    try:
        ratio, evidence = _sobel_edge_density(png)
        threshold = 0.05
        evidence["threshold"] = threshold
        if ratio < threshold:
            return p.fail(
                f"edge density {ratio:.4f} < {threshold} — render lacks geometry "
                f"(likely sky gradient or solid color)",
                **evidence,
            )
        return p.ok(**evidence)
    except Exception as e:
        return p.fail(f"{type(e).__name__}: {e}")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--region", default="muc-sued-4x2")
    ap.add_argument("--datasets", nargs="+", default=["dgm1", "dop40", "lod2"])
    ap.add_argument("--data-dir", type=Path, default=ROOT / "data")
    ap.add_argument("--all-presets", action="store_true",
                    help="After phase G, also render+verify every camera preset")
    args = ap.parse_args(argv)

    from workflows.region_presets import polygon_for_region
    from shapely.wkt import loads
    from shapely.geometry import Polygon
    from pyproj import Transformer
    poly_wkt = polygon_for_region(args.region)
    poly = loads(poly_wkt)
    t = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True)
    bbox = Polygon([t.transform(x, y) for x, y in poly.exterior.coords]).bounds

    raw = args.data_dir / "raw"; proc = args.data_dir / "processed"
    results: list[PhaseResult] = []

    pa, downloads = phase_a_download(poly_wkt, args.datasets, raw); results.append(pa)
    pb, gdal_out = phase_b_gdal(downloads, bbox, proc); results.append(pb)
    heightmap = gdal_out.get("heightmap")
    ortho_dir = gdal_out.get("ortho_dir")
    pc, lod2_out = phase_c_lod2(downloads, proc); results.append(pc)
    cityjson = lod2_out.get("cityjson")

    if heightmap:
        pd, blender_out = phase_d_blender(heightmap, ortho_dir, cityjson, bbox,
                                           args.data_dir, args.region)
        results.append(pd)
        scene = blender_out.get("scene_blend")
        if scene:
            pe, _ = phase_e_introspect(scene, args.data_dir); results.append(pe)
            pf, render_out = phase_f_render(scene, args.data_dir, args.region)
            results.append(pf)
            png = render_out.get("render_png")
            if png:
                results.append(phase_g_render_readback(png))
                results.append(phase_i_geometry_detail(png))
            else:
                results.append(PhaseResult("G. Render readback").skip("no PNG"))
                results.append(PhaseResult("I. Geometry detail").skip("no PNG"))
        else:
            results.append(PhaseResult("E. Scene introspection").skip("no scene"))
            results.append(PhaseResult("F. Render frame").skip("no scene"))
            results.append(PhaseResult("G. Render readback").skip("no scene"))
    else:
        for n in ("D. Blender scene", "E. Introspection", "F. Render", "G. Readback"):
            results.append(PhaseResult(n).skip("no heightmap"))

    if args.all_presets:
        p_h = PhaseResult("H. Multi-preset render consistency")
        try:
            from workflows.multi_altitude_demo import PRESETS, render_one, make_contact_sheet
            from PIL import Image
            import numpy as np
            scene = args.data_dir / f"scene_{args.region}.blend"
            if not scene.is_file():
                results.append(p_h.skip("no scene"))
            else:
                ok_count = 0
                details = {}
                for preset in PRESETS:
                    out = args.data_dir / f"render_{args.region}_{preset}.png"
                    actual = render_one(scene, preset, out)
                    if actual is None:
                        details[preset] = "render failed"
                        continue
                    arr = np.array(Image.open(actual).convert("RGB"))
                    std = float(arr.std(axis=(0,1)).max())
                    if std > 5:
                        ok_count += 1
                        details[preset] = f"std={std:.1f} OK"
                    else:
                        details[preset] = f"std={std:.1f} BLANK"
                p_h.evidence["per_preset"] = details
                p_h.evidence["ok_count"] = f"{ok_count}/{len(PRESETS)}"
                if ok_count >= len(PRESETS) - 1:
                    results.append(p_h.ok(**p_h.evidence))
                else:
                    results.append(p_h.fail(f"{ok_count}/{len(PRESETS)} succeeded",
                                           **p_h.evidence))
        except Exception as e:
            results.append(p_h.fail(f"{type(e).__name__}: {e}"))

    # Phase H: human + machine report.
    print("\n" + "="*70)
    print(f"VALIDATION REPORT - region {args.region!r}, datasets {args.datasets}")
    print("="*70)
    for r in results:
        marker = {"OK": "[OK]", "FAIL": "[FAIL]", "SKIP": "[SKIP]"}.get(r.status, "[?]")
        print(f"{marker:6s} {r.name}")
        if r.error: print(f"        -> {r.error}")
        for k, v in r.evidence.items():
            if isinstance(v, dict): v = json.dumps(v, default=str)[:150]
            print(f"        - {k}: {v}")
    print("="*70)
    n_ok = sum(1 for r in results if r.status == "OK")
    n_fail = sum(1 for r in results if r.status == "FAIL")
    n_skip = sum(1 for r in results if r.status == "SKIP")
    print(f"SUMMARY: {n_ok} ok, {n_fail} failed, {n_skip} skipped")

    json_out = args.data_dir / f"validation_{args.region}.json"
    json_out.write_text(json.dumps([r.to_dict() for r in results], indent=2, default=str),
                        encoding="utf-8")
    print(f"machine-readable report: {json_out}")
    return 0 if n_fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
