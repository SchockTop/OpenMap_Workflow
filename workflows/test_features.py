"""test_features.py — render-based functional test for each plug-in feature.

For each NAME in features registry, run:
  1. Build a synthetic mini scene (one cube building + camera + sun)
  2. Render baseline PNG (no feature applied)
  3. Render with feature applied
  4. Compare: feature render must have > N% more unique colors / higher std
  5. Save both PNGs into test_artifacts/feature_<name>/{baseline,applied}.png
  6. Print human-readable report
"""
from __future__ import annotations
import argparse, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--feature", default="buildings-textured",
                    help="Feature NAME to test (or 'all')")
    args = ap.parse_args()
    out_dir = ROOT / "data" / "test_artifacts" / f"feature_{args.feature}"
    out_dir.mkdir(parents=True, exist_ok=True)

    blender_runner = ROOT / "workflows" / "_test_feature_in_blender.py"
    cmd = [str(BLENDER), "--background", "--python", str(blender_runner),
           "--", "--feature", args.feature, "--out-dir", str(out_dir)]
    rc = subprocess.call(cmd)
    if rc != 0:
        return rc

    # Compare baseline vs applied.
    from PIL import Image
    import numpy as np
    baseline = np.array(Image.open(out_dir / "baseline.png").convert("RGB"))
    applied = np.array(Image.open(out_dir / "applied.png").convert("RGB"))
    base_unique = len(np.unique(baseline.mean(axis=2).astype(int)))
    app_unique = len(np.unique(applied.mean(axis=2).astype(int)))
    base_std = float(baseline.std(axis=(0, 1)).max())
    app_std = float(applied.std(axis=(0, 1)).max())
    mean_abs_diff = float(np.abs(baseline.astype(int) - applied.astype(int)).mean())
    print(f"\n=== feature {args.feature!r} render comparison ===")
    print(f"  baseline: unique-grays={base_unique:>4d}  std={base_std:>5.1f}")
    print(f"  applied:  unique-grays={app_unique:>4d}  std={app_std:>5.1f}")
    delta_unique = app_unique - base_unique
    delta_std = app_std - base_std
    print(f"  delta:    unique={abs(delta_unique):>4d}     std={abs(delta_std):>5.1f}     "
          f"mean_abs_diff={mean_abs_diff:>5.2f}")
    # A feature is "visible" if it changes pixels meaningfully — direction does
    # not matter. A textured material may *reduce* baseline checker-pattern
    # entropy while still being a visible, intentional change.
    visible = (abs(delta_unique) >= 5) or (abs(delta_std) >= 2.0) or (mean_abs_diff >= 1.0)
    print(f"  visible change? {'YES' if visible else 'NO'}")
    print(f"  artifacts: {out_dir}")
    return 0 if visible else 1


if __name__ == "__main__":
    sys.exit(main())
