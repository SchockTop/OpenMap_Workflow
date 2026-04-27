"""Verify that fpv-walk render shows BOTH sky and ground (horizon visible).

Top half mean RGB should differ from bottom half by > 30 — proves a horizon line.
Run AFTER multi_altitude_demo.py has produced render_*_fpv-walk.png.
"""
import sys
from pathlib import Path
from PIL import Image
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
default_path = ROOT / "data" / "render_scene_muc-sued-4x2_fpv-walk.png"


def check(png_path: Path, threshold: float = 30.0) -> bool:
    img = np.array(Image.open(png_path).convert("RGB"))
    h = img.shape[0]
    top = img[:h // 2].mean(axis=(0, 1))
    bot = img[h // 2:].mean(axis=(0, 1))
    diff = float(np.linalg.norm(top - bot))
    print(f"  top half RGB:    {top.round(1).tolist()}")
    print(f"  bottom half RGB: {bot.round(1).tolist()}")
    print(f"  difference:      {diff:.1f}  (threshold {threshold})")
    return diff >= threshold


def main():
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else default_path
    print(f"=== horizon visibility check: {target.name} ===")
    if not target.is_file():
        print(f"  MISSING — run multi_altitude_demo.py first")
        return 2
    if check(target):
        print("  PASS — horizon visible")
        return 0
    print("  FAIL — top and bottom halves are too similar (no horizon)")
    return 1


if __name__ == "__main__":
    sys.exit(main())
