"""trian3d_import.py — CPython entry for TRIAN3D FBX → organized Blender scene.

Spawns Blender with `_blender_trian3d_import.py`, which imports the FBX,
organizes objects into collections, optionally applies material rules,
and collapses duplicate meshes to linked data.

Usage:

    # Standalone — fresh .blend with just the TRIAN3D import:
    python workflows/trian3d_import.py --fbx my.fbx --out scene.blend

    # Drop on top of an existing terrain build:
    python workflows/trian3d_import.py --fbx my.fbx \\
        --into-existing data/scene_custom.blend \\
        --out data/scene_custom_with_trian.blend

    # Use a project-specific rules file:
    python workflows/trian3d_import.py --fbx my.fbx --rules my_rules.json \\
        --out scene.blend
"""
from __future__ import annotations
import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Match the existing pipeline default — same Blender install path.
BLENDER = Path(r"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--fbx", type=Path, required=True,
                    help="Path to the TRIAN3D-exported .fbx file.")
    ap.add_argument("--rules", type=Path, default=None,
                    help="Path to a rules JSON. Defaults to "
                         "workflows/trian3d_default_rules.json.")
    ap.add_argument("--out", type=Path, required=True,
                    help="Output .blend path.")
    ap.add_argument("--into-existing", type=Path, default=None,
                    help="Open this .blend before importing (e.g. an "
                         "existing terrain build). The TRIAN3D scene gets "
                         "added on top.")
    ap.add_argument("--no-collapse", action="store_true",
                    help="Skip the collapse-duplicate-meshes-to-instances "
                         "pass.")
    ap.add_argument("--blender", type=Path, default=BLENDER,
                    help=f"Blender executable (default: {BLENDER}).")
    args = ap.parse_args(argv)

    if not args.fbx.is_file():
        print(f"[!] FBX not found: {args.fbx}", file=sys.stderr)
        return 2

    blender_script = ROOT / "workflows" / "_blender_trian3d_import.py"
    cmd = [
        str(args.blender), "--background",
        "--python", str(blender_script),
        "--",
        "--fbx", str(args.fbx),
        "--out", str(args.out),
    ]
    if args.rules:
        cmd += ["--rules", str(args.rules)]
    if args.into_existing:
        cmd += ["--into-existing", str(args.into_existing)]
    if args.no_collapse:
        cmd += ["--no-collapse"]

    print(f"[trian3d] spawning Blender: {' '.join(map(str, cmd))}")
    return subprocess.call(cmd)


if __name__ == "__main__":
    sys.exit(main())
