"""blind_ground_detector.py — does the bottom of this image look like real
aerial/satellite photography of the ground, or like a flat colored block?

Reads every PNG in a directory and prints, per file:
  * unique-hue count in the lower half of the frame
  * RGB std-dev in the lower half (photo-like terrain has std > ~20)
  * mid-frequency edge density (real DOP imagery has lots of small edges;
    flat material has almost none)
  * verdict: GROUND_VISIBLE / FLAT / EMPTY

It does not know the filenames mean anything. It scores each frame on its
own pixels, then the harness compares scores across the sequence.

Usage:
  python workflows/blind_ground_detector.py showcase/ground_layer_test
  python workflows/blind_ground_detector.py showcase   # score the originals
"""
from __future__ import annotations
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def score(path: Path) -> dict:
    img = np.array(Image.open(path).convert("RGB"))
    h, w, _ = img.shape
    bottom = img[h // 2:]
    # Hue diversity proxy: count of unique 5-bit-quantised RGB triples.
    q = (bottom // 32).astype(np.int32)
    keys = q[..., 0] * 64 * 64 + q[..., 1] * 64 + q[..., 2]
    unique_hues = int(np.unique(keys).size)
    rgb_std = float(bottom.std(axis=(0, 1)).mean())
    # Edge density: |dx| + |dy| on luma, threshold at 12.
    luma = bottom.mean(axis=2)
    dx = np.abs(np.diff(luma, axis=1))[:-1, :]
    dy = np.abs(np.diff(luma, axis=0))[:, :-1]
    edge = dx + dy
    edge_density = float((edge > 12).mean())

    if rgb_std < 6 and unique_hues < 25:
        verdict = "EMPTY"
    elif unique_hues >= 60 and rgb_std >= 18 and edge_density >= 0.06:
        verdict = "GROUND_VISIBLE"
    else:
        verdict = "FLAT"
    return {
        "file": path.name,
        "unique_hues": unique_hues,
        "rgb_std": round(rgb_std, 1),
        "edge_density": round(edge_density, 3),
        "verdict": verdict,
    }


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print("usage: blind_ground_detector.py <dir-of-pngs>", file=sys.stderr)
        return 2
    target = Path(argv[1])
    if not target.is_dir():
        print(f"not a directory: {target}", file=sys.stderr)
        return 2

    pngs = sorted(target.glob("*.png"))
    if not pngs:
        print(f"no PNGs in {target}", file=sys.stderr)
        return 2

    rows = [score(p) for p in pngs]
    width = max(len(r["file"]) for r in rows)
    print(f"\n=== blind ground detector — {target} ===\n")
    print(f"  {'file':<{width}}  {'hues':>4} {'std':>5} {'edges':>5}  verdict")
    for r in rows:
        print(f"  {r['file']:<{width}}  {r['unique_hues']:>4} "
              f"{r['rgb_std']:>5.1f} {r['edge_density']:>5.3f}  {r['verdict']}")

    visible = [r for r in rows if r["verdict"] == "GROUND_VISIBLE"]
    if not visible:
        print("\n  SUMMARY: no frame contains photo-like ground.")
        print("           The orthophoto / DOP drape is not landing on the terrain.")
        return 1
    first = visible[0]
    print(f"\n  SUMMARY: ground first appears in {first['file']!r}")
    print(f"           ({first['unique_hues']} unique hues, "
          f"std={first['rgb_std']}, edges={first['edge_density']})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
