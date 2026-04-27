"""cinematic_demo.py — one-button "make a poster" output for stakeholder demos.

Runs the full pipeline with ALL features enabled + golden-hour sky preset +
cinematic-establishing camera, then renders the final frame at higher quality.
Optionally produces a sky-preset comparison contact sheet on the same scene.

Usage:
    python workflows/cinematic_demo.py --region muc-sued-4x2 [--sky-comparison]

Outputs:
    data/poster_<region>.png       — the headline render
    data/sky_comparison_<region>.png — 6-up grid (one cell per sky preset)
"""
from __future__ import annotations
import argparse, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")

ALL_FEATURES = ["buildings-textured", "trees", "ground-shader", "groundcover"]
SKY_PRESETS = ["noon", "golden-hour", "blue-hour", "dawn", "overcast", "afternoon"]


def run_pipeline(region: str, sky_preset: str, camera_preset: str,
                 features: list[str], data_dir: Path,
                 quality: str = "preview") -> int:
    cmd = [
        "C:/ProgramData/anaconda3/python.exe",
        str(ROOT / "workflows" / "full_pipeline.py"),
        "--region", region,
        "--datasets", "dgm1", "dop40", "lod2",
        "--enable", *features,
        "--camera-preset", camera_preset,
        "--sky-preset", sky_preset,
        "--quality", quality,
        "--engine", "BLENDER_EEVEE_NEXT",
        "--render-preview",
        "--data-dir", str(data_dir),
    ]
    print(f"[demo] {' '.join(cmd[2:])}")
    return subprocess.call(cmd)


def render_with_sky(scene_blend: Path, sky_preset: str, out_path: Path,
                    resolution=(960, 540)) -> Path | None:
    script = f"""
import bpy, importlib
sp = importlib.import_module('bl_ext.user_default.blender_tools.sky_presets')
sp.apply_sky_preset(bpy.context.scene, {sky_preset!r})
bpy.context.scene.render.resolution_x = {resolution[0]}
bpy.context.scene.render.resolution_y = {resolution[1]}
bpy.context.scene.render.image_settings.file_format = 'PNG'
bpy.context.scene.render.filepath = {str(out_path.with_suffix('')) !r}
bpy.ops.render.render(write_still=True)
"""
    cmd = [str(BLENDER), "--background", str(scene_blend),
           "--python-expr", script]
    rc = subprocess.call(cmd)
    if rc != 0:
        return None
    candidates = list(out_path.parent.glob(f"{out_path.stem}*.png"))
    return candidates[0] if candidates else None


def make_grid(images: list[Path | None], out: Path, columns: int = 3,
              labels: list[str] | None = None):
    from PIL import Image, ImageDraw, ImageFont
    valid = [(p, l) for p, l in zip(images, labels or [""] * len(images)) if p]
    if not valid:
        print("[demo] no valid images for grid")
        return
    base = Image.open(valid[0][0])
    cw, ch = base.size
    rows = (len(images) + columns - 1) // columns
    sheet = Image.new("RGB", (cw * columns, ch * rows), "black")
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("arial.ttf", 28)
    except Exception:
        font = ImageFont.load_default()
    for i, (img_path, label) in enumerate(zip(images, labels or [""] * len(images))):
        row, col = divmod(i, columns)
        x = col * cw; y = row * ch
        if img_path:
            sheet.paste(Image.open(img_path), (x, y))
        else:
            draw.rectangle([x, y, x + cw, y + ch], fill=(50, 0, 0))
        if label:
            # Black bar with yellow caption.
            draw.rectangle([x, y + ch - 50, x + cw, y + ch], fill=(0, 0, 0))
            draw.text((x + 12, y + ch - 42), label, fill="yellow", font=font)
    sheet.save(out)
    print(f"[demo] grid -> {out}")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--region", default="muc-sued-4x2")
    ap.add_argument("--sky-preset", default="golden-hour",
                    choices=SKY_PRESETS,
                    help="Time-of-day for the headline poster")
    ap.add_argument("--camera-preset", default="cinematic-establishing")
    ap.add_argument("--features", nargs="*", default=ALL_FEATURES,
                    help="Subset of features to enable (default: all)")
    ap.add_argument("--quality", default="final",
                    choices=["draft", "preview", "final"],
                    help="Render quality envelope (default: final for poster)")
    ap.add_argument("--sky-comparison", action="store_true",
                    help="Also produce a 6-up sky-preset contact sheet")
    ap.add_argument("--data-dir", type=Path, default=ROOT / "data")
    args = ap.parse_args(argv)

    print(f"\n=== cinematic_demo: {args.region} ===")
    print(f"sky={args.sky_preset}  camera={args.camera_preset}  "
          f"features={args.features}\n")

    # 1. Pipeline run.
    rc = run_pipeline(args.region, args.sky_preset, args.camera_preset,
                      args.features, args.data_dir, quality=args.quality)
    if rc != 0:
        print(f"[demo] pipeline FAILED (exit {rc})", file=sys.stderr)
        return rc

    # 2. Headline poster: copy/rename the produced render.
    src_render = args.data_dir / f"render_{args.region}.png"
    poster = args.data_dir / f"poster_{args.region}.png"
    if src_render.is_file():
        import shutil
        shutil.copy(src_render, poster)
        print(f"[demo] HEADLINE POSTER: {poster}")
        # Pillow stats.
        from PIL import Image
        import numpy as np
        arr = np.array(Image.open(poster).convert("RGB"))
        print(f"  dimensions: {arr.shape[1]}x{arr.shape[0]}")
        print(f"  mean RGB: {arr.mean(axis=(0,1)).round(1).tolist()}")
        print(f"  std RGB:  {arr.std(axis=(0,1)).round(1).tolist()}")
        print(f"  unique grays: {len(np.unique(arr.mean(axis=2).astype(int)))}")

    # 3. Sky comparison (optional).
    if args.sky_comparison:
        scene = args.data_dir / f"scene_{args.region}.blend"
        if not scene.is_file():
            print(f"[demo] no scene for sky comparison")
            return 1
        out_dir = args.data_dir / "test_artifacts" / "sky_presets"
        out_dir.mkdir(parents=True, exist_ok=True)
        rendered: list[Path | None] = []
        labels: list[str] = []
        print("\n=== rendering sky-preset comparison ===")
        for sp in SKY_PRESETS:
            out = out_dir / f"sky_{sp}.png"
            print(f"  rendering {sp}...")
            png = render_with_sky(scene, sp, out)
            rendered.append(png)
            labels.append(sp)
            if png:
                from PIL import Image
                import numpy as np
                arr = np.array(Image.open(png).convert("RGB"))
                print(f"    mean RGB={arr.mean(axis=(0,1)).round(1).tolist()}")
        grid = args.data_dir / f"sky_comparison_{args.region}.png"
        make_grid(rendered, grid, columns=3, labels=labels)
        print(f"\n[demo] SKY COMPARISON: {grid}")

    print("\n=== cinematic_demo COMPLETE ===")
    return 0


if __name__ == "__main__":
    sys.exit(main())
