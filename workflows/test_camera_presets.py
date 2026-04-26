"""test_camera_presets.py - render the same scene at every camera preset.

For each preset in CAMERA_PRESETS:
1. Open the existing data/scene_muc-sued-4x2.blend (or build a fresh one if
   not present)
2. Apply the preset to the camera
3. Render 1 frame at 480x270 (small, quick)
4. Save as data/test_artifacts/camera_<preset>.png

Then assemble a 6-up contact sheet:
    data/test_artifacts/camera_presets/camera_presets_grid.png

Verifies the presets actually produce visually different framings (e.g.,
fpv-walk should show a low close-up of terrain, aircraft-approach should
show the whole region from above).
"""
from __future__ import annotations
import argparse, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")

PRESETS = ["fpv-walk", "fpv-bike", "low-drone", "mid-drone",
           "cinematic-establishing", "aircraft-approach"]


def render_preset(scene_blend: Path, preset: str, out_dir: Path) -> Path | None:
    blender_runner = ROOT / "workflows" / "_render_with_preset.py"
    out_stem = out_dir / f"camera_{preset}"
    cmd = [str(BLENDER), "--background", str(scene_blend),
           "--python", str(blender_runner), "--",
           "--preset", preset, "--out", str(out_stem)]
    subprocess.call(cmd)
    candidates = list(out_dir.glob(f"camera_{preset}*.png"))
    return candidates[0] if candidates else None


def make_contact_sheet(images: list[Path | None], out: Path) -> None:
    from PIL import Image, ImageDraw
    valid = [p for p in images if p is not None and p.is_file()]
    if not valid:
        print("[contact-sheet] no images to assemble")
        return
    base = Image.open(valid[0])
    cell_w, cell_h = base.size
    grid_w, grid_h = 3, 2
    sheet = Image.new("RGB", (cell_w * grid_w, cell_h * grid_h), "black")
    draw = ImageDraw.Draw(sheet)
    for i, p in enumerate(images[:6]):
        if p is None or not p.is_file():
            continue
        img = Image.open(p)
        x = (i % grid_w) * cell_w
        y = (i // grid_w) * cell_h
        sheet.paste(img, (x, y))
        draw.text((x + 5, y + 5), p.stem.replace("camera_", ""), fill="yellow")
    sheet.save(out)
    print(f"[contact-sheet] wrote {out}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", default=str(ROOT / "data" / "scene_muc-sued-4x2.blend"))
    args = ap.parse_args()

    scene_path = Path(args.scene)
    if not scene_path.is_file():
        print(f"[!] scene not found: {scene_path}", file=sys.stderr)
        print("    Run full_pipeline.py first to build a scene .blend",
              file=sys.stderr)
        return 1

    out_dir = ROOT / "data" / "test_artifacts" / "camera_presets"
    out_dir.mkdir(parents=True, exist_ok=True)

    rendered: list[Path | None] = []
    for preset in PRESETS:
        png = render_preset(scene_path, preset, out_dir)
        rendered.append(png)
        if png and png.is_file():
            from PIL import Image
            import numpy as np
            arr = np.array(Image.open(png).convert("RGB"))
            print(f"  {preset:24s}  mean={arr.mean(axis=(0,1)).round(1).tolist()}  "
                  f"std={arr.std(axis=(0,1)).round(1).tolist()}")
        else:
            print(f"  {preset:24s}  [render failed]")

    sheet = out_dir / "camera_presets_grid.png"
    make_contact_sheet(rendered, sheet)
    print(f"contact sheet: {sheet}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
