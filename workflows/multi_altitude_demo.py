"""multi_altitude_demo.py - render one frame per camera preset, build contact sheet.

Final demo artifact: a single grid PNG showing the same scene at every camera
envelope from FPV-walk to high-altitude approach. Proves the toolkit works
across the full altitude range.

Usage:
    python workflows/multi_altitude_demo.py --scene data/scene_muc-sued-4x2.blend
"""
from __future__ import annotations
import argparse, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")
PRESETS = ["fpv-walk", "fpv-bike", "low-drone", "mid-drone",
           "cinematic-establishing", "aircraft-approach"]


def render_one(scene_blend: Path, preset: str, out_path: Path,
               resolution: tuple[int, int] = (960, 540)) -> Path | None:
    blender_runner = ROOT / "workflows" / "_render_with_preset.py"
    cmd = [str(BLENDER), "--background", str(scene_blend),
           "--python", str(blender_runner), "--",
           "--preset", preset, "--out", str(out_path.with_suffix(""))]
    rc = subprocess.call(cmd)
    if rc != 0:
        print(f"  [{preset}] FAIL: blender exit {rc}")
        return None
    candidates = list(out_path.parent.glob(f"{out_path.stem}*.png"))
    if not candidates:
        print(f"  [{preset}] FAIL: no png produced")
        return None
    return candidates[0]


def make_contact_sheet(images: list[Path | None], out: Path,
                       columns: int = 3, label_size: int = 24):
    from PIL import Image, ImageDraw, ImageFont
    valid = [p for p in images if p is not None]
    if not valid:
        print("[contact-sheet] no valid renders to montage")
        return
    base = Image.open(valid[0])
    cw, ch = base.size
    rows = (len(images) + columns - 1) // columns
    sheet = Image.new("RGB", (cw * columns, ch * rows), "black")
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("arial.ttf", label_size)
    except Exception:
        font = ImageFont.load_default()
    for i, p in enumerate(images):
        row, col = divmod(i, columns)
        x = col * cw; y = row * ch
        if p is None:
            draw.rectangle([x, y, x + cw, y + ch], fill=(40, 0, 0))
            label = PRESETS[i] if i < len(PRESETS) else f"slot-{i}"
            draw.text((x + 10, y + 10), f"FAILED: {label}",
                     fill="red", font=font)
        else:
            sheet.paste(Image.open(p), (x, y))
            draw.rectangle([x, y, x + cw, y + 40], fill=(0, 0, 0))
            draw.text((x + 10, y + 5), p.stem, fill="yellow", font=font)
    sheet.save(out)
    print(f"[contact-sheet] -> {out}")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--scene", type=Path, default=ROOT / "data" / "scene_muc-sued-4x2.blend")
    ap.add_argument("--out-dir", type=Path, default=ROOT / "data")
    args = ap.parse_args(argv)

    if not args.scene.is_file():
        print(f"scene file not found: {args.scene}", file=sys.stderr)
        return 1

    rendered: list[Path | None] = []
    for preset in PRESETS:
        out_path = args.out_dir / f"render_{args.scene.stem}_{preset}.png"
        print(f"[multi-altitude] rendering {preset} -> {out_path.name}")
        rendered.append(render_one(args.scene, preset, out_path))

    sheet = args.out_dir / f"render_{args.scene.stem}_grid.png"
    make_contact_sheet(rendered, sheet)

    # Per-preset Pillow stats.
    print("\n=== per-preset stats ===")
    from PIL import Image
    import numpy as np
    successful = 0
    for preset, path in zip(PRESETS, rendered):
        if path is None:
            print(f"  {preset:24s}  FAILED")
            continue
        arr = np.array(Image.open(path).convert("RGB"))
        std = arr.std(axis=(0,1)).max()
        unique = len(np.unique(arr.mean(axis=2).astype(int)))
        non_black = (std > 5 and unique > 30)
        marker = "OK" if non_black else "BLANK?"
        print(f"  {preset:24s}  std={std:5.1f} unique={unique:3d} {marker}")
        if non_black:
            successful += 1
    print(f"\n{successful}/{len(PRESETS)} presets produced non-blank renders")
    print(f"contact sheet: {sheet}")
    return 0 if successful >= len(PRESETS) - 1 else 1  # 1 fail tolerated


if __name__ == "__main__":
    sys.exit(main())
