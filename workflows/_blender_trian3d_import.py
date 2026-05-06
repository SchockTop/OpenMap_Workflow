"""Imports a TRIAN3D-exported FBX into Blender, organizes the scene by
collections, applies material rules, and collapses duplicate meshes to
linked data. Saves the result as a .blend.

Runs INSIDE Blender — invoked by `workflows/trian3d_import.py` via:

    blender --background --python workflows/_blender_trian3d_import.py -- \\
        --fbx my.fbx --rules my_rules.json --out scene.blend

Pure-Python rule logic + bpy operations live in `trian3d_rules.py` and
`trian3d_apply.py`; this script is a thin orchestration layer.
"""
from __future__ import annotations
import argparse
import sys
from pathlib import Path


def _split_double_dash_args(argv: list[str]) -> list[str]:
    """Blender swallows everything before `--`; return what came after."""
    if "--" not in argv:
        return []
    return argv[argv.index("--") + 1:]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fbx", type=Path, required=True,
                        help="Path to the TRIAN3D-exported .fbx")
    parser.add_argument("--rules", type=Path, default=None,
                        help="Path to a rules JSON. Falls back to "
                             "workflows/trian3d_default_rules.json.")
    parser.add_argument("--out", type=Path, required=True,
                        help="Output .blend path")
    parser.add_argument("--no-collapse", action="store_true",
                        help="Skip the collapse-duplicate-meshes-to-instances "
                             "pass (cheaper for tiny scenes; never skip on "
                             ">10k object scenes).")
    parser.add_argument("--into-existing", type=Path, default=None,
                        help="Open this .blend before importing the FBX, so "
                             "the TRIAN3D scene drops on top of an existing "
                             "terrain build.")
    args = parser.parse_args(_split_double_dash_args(sys.argv))

    repo_root = Path(__file__).resolve().parent.parent
    sys.path.insert(0, str(repo_root))
    from workflows.trian3d_rules import RuleSet
    from workflows.trian3d_apply import (
        organize_scene,
        apply_material_rules,
        collapse_to_linked_data,
    )

    import bpy  # type: ignore[import-not-found]

    # 0. Load the existing terrain .blend if requested, else start fresh.
    if args.into_existing:
        bpy.ops.wm.open_mainfile(filepath=str(args.into_existing))
    else:
        # Wipe Blender's default cube/light/camera so they don't clutter the
        # imported scene.
        bpy.ops.wm.read_factory_settings(use_empty=True)

    # 1. Import the FBX. Blender's importer is robust enough to handle
    # TRIAN3D's multi-mesh, materials, custom properties.
    print(f"[trian3d] importing FBX: {args.fbx}")
    bpy.ops.import_scene.fbx(filepath=str(args.fbx))
    n_imported = sum(1 for _ in bpy.context.scene.objects)
    print(f"[trian3d] imported {n_imported} object(s)")

    # 2. Load rules (default or user-supplied).
    rules_path = args.rules or (repo_root / "workflows" / "trian3d_default_rules.json")
    print(f"[trian3d] loading rules: {rules_path}")
    ruleset = RuleSet.from_json(rules_path)
    print(f"[trian3d] {len(ruleset.organize)} organize rule(s), "
          f"{len(ruleset.materials)} material rule(s)")

    # 3. Organize.
    counts = organize_scene(ruleset.organize)
    print(f"[trian3d] organized: {counts}")

    # 4. Materials.
    if ruleset.materials:
        mat_counts = apply_material_rules(ruleset.materials)
        print(f"[trian3d] materials applied: {mat_counts}")

    # 5. Collapse duplicate meshes (skip with --no-collapse).
    if not args.no_collapse:
        relinked, unique = collapse_to_linked_data()
        print(f"[trian3d] collapsed {relinked} duplicate(s) onto "
              f"{unique} canonical mesh(es)")

    # 6. Save.
    args.out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(args.out))
    print(f"[trian3d] wrote {args.out} ({args.out.stat().st_size // 1024} KB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
