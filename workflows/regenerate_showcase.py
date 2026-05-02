"""regenerate_showcase.py — single source of truth for showcase/*.png.

Drives the same _render_slot.py the visual harness uses, but on the larger
muc-sued-4x2 region, and writes named PNGs into showcase/. Tests and
showcase share the rendering code path; they cannot drift.

Usage:
  python workflows/regenerate_showcase.py
  python workflows/regenerate_showcase.py "C:\\Path\\to\\blender.exe"
"""
from __future__ import annotations
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SHOWCASE_DIR = REPO_ROOT / "showcase"
REGION = "muc-sued-4x2"

# (showcase_filename, render-script args)
SHOWCASE_MAP = [
    ("01_poster.png",                ["--altitude", "2000", "--framing", "wide", "--preset", "aircraft-approach"]),
    ("02_sky_comparison.png",        ["--altitude", "1500", "--framing", "wide", "--preset", "sky-grid"]),
    ("03_altitude_comparison.png",   ["--altitude", "500",  "--framing", "wide", "--preset", "altitude-grid"]),
    ("04_feature_buildings.png",     ["--altitude", "30",   "--framing", "close", "--preset", "close-building"]),
    ("05_feature_trees.png",         ["--altitude", "5",    "--framing", "close", "--preset", "close-tree"]),
    ("06_feature_ground_shader.png", ["--altitude", "10",   "--framing", "close", "--preset", "close-ground-patch"]),
    ("07_feature_groundcover.png",   ["--altitude", "1.7",  "--framing", "wide", "--preset", "fpv-walk"]),
]


def main():
    blender = sys.argv[1] if len(sys.argv) > 1 else (
        r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe")
    scene = REPO_ROOT / "data" / f"scene_{REGION}.blend"
    if not scene.exists():
        print(f"missing fixture {scene}; run full_pipeline.py --region {REGION} first")
        sys.exit(1)
    SHOWCASE_DIR.mkdir(exist_ok=True)
    render_script = REPO_ROOT / "workflows" / "tests" / "visual" / "_render_slot.py"

    for name, args in SHOWCASE_MAP:
        out = SHOWCASE_DIR / name
        slot = name.replace(".png", "")
        cmd = [blender, "-b", str(scene), "--python", str(render_script), "--",
               "--slot", slot, "--out", str(out), *args]
        print(f"[regen] {name}")
        subprocess.run(cmd, check=True)

    print(f"[regen] showcase regenerated to {SHOWCASE_DIR}")


if __name__ == "__main__":
    main()
