"""trian3d_apply.py — bpy-side rule application.

Three operations driven by `trian3d_rules.RuleSet`:

- `organize_scene`           — move objects into named Blender Collections.
- `apply_material_rules`     — replace material slot 0 by rule-matched name.
- `collapse_to_linked_data`  — find duplicate meshes, link them to a single
                               canonical Mesh datablock (huge memory win on
                               TRIAN3D scenes with many repeated assets).

`bpy` is imported lazily inside each function so this module is importable
under pytest with a mocked bpy. The collection-path syntax uses `/` to
nest, so "Buildings/Residential" creates `Buildings` and links a
`Residential` child collection underneath it.
"""
from __future__ import annotations

import hashlib
from collections import defaultdict
from typing import Any, Optional

from workflows.trian3d_rules import Rule, RuleSet, first_match


# ---------------------------------------------------------------------------
# Helpers (pure-Python — testable without bpy)
# ---------------------------------------------------------------------------

def split_collection_path(path: str) -> list[str]:
    """'Buildings/Residential' → ['Buildings', 'Residential']. Empty
    segments are stripped so leading/trailing slashes don't create empty
    collections."""
    return [p for p in path.split("/") if p]


def mesh_signature(vertex_count: int,
                    edge_count: int,
                    poly_count: int,
                    first_vert_co: tuple[float, float, float] | None = None) -> str:
    """Cheap signature used to bucket meshes that COULD be duplicates. Final
    de-duplication still requires a per-vertex check (caller's job)."""
    payload = f"{vertex_count}|{edge_count}|{poly_count}|"
    if first_vert_co is not None:
        payload += f"{first_vert_co[0]:.6f},{first_vert_co[1]:.6f},{first_vert_co[2]:.6f}"
    return hashlib.sha1(payload.encode()).hexdigest()[:16]


def _require_bpy() -> Any:
    try:
        import bpy  # type: ignore[import-not-found]
    except ImportError as e:
        raise RuntimeError(
            "trian3d_apply requires Blender's bundled Python (bpy). "
            "Run via: blender --background --python <script>.py"
        ) from e
    return bpy


# ---------------------------------------------------------------------------
# bpy-side: nested-collection plumbing
# ---------------------------------------------------------------------------

def ensure_collection_path(bpy: Any, path: str) -> Any:
    """Walk `path` ('Buildings/Residential'), creating missing collections
    and linking each child under its parent. Returns the leaf collection."""
    parts = split_collection_path(path)
    if not parts:
        raise ValueError(f"empty collection path: {path!r}")
    parent = bpy.context.scene.collection
    leaf = parent
    for name in parts:
        existing = bpy.data.collections.get(name)
        if existing is None:
            existing = bpy.data.collections.new(name)
            parent.children.link(existing)
        elif existing.name not in [c.name for c in parent.children]:
            # Collection exists in bpy.data but is parented elsewhere; link
            # it here so the path resolves correctly. Blender allows a
            # collection to be linked under multiple parents.
            try:
                parent.children.link(existing)
            except RuntimeError:
                # Already linked — Blender raises on double-link.
                pass
        parent = existing
        leaf = existing
    return leaf


def _unlink_from_all_collections(bpy: Any, obj: Any) -> None:
    """Remove obj from every collection it currently lives in."""
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)


# ---------------------------------------------------------------------------
# bpy-side: organize / materials / collapse
# ---------------------------------------------------------------------------

def organize_scene(rules: list[Rule],
                   *,
                   unmatched_collection: Optional[str] = "Unmatched",
                   ) -> dict[str, int]:
    """Walk every object in the scene; for each, find the first matching
    rule and move the object into that rule's target collection. Returns
    a {collection_path: n_moved} count.

    Objects with no matching rule go into `unmatched_collection` (or stay
    where they are if `unmatched_collection` is None).
    """
    bpy = _require_bpy()
    counts: dict[str, int] = defaultdict(int)
    leaves: dict[str, Any] = {}
    for obj in list(bpy.context.scene.objects):
        rule = first_match(rules, obj)
        target = rule.target if rule is not None else unmatched_collection
        if target is None:
            continue  # leave the object where it is
        leaf = leaves.get(target)
        if leaf is None:
            leaf = ensure_collection_path(bpy, target)
            leaves[target] = leaf
        # Skip if already in the target collection.
        if obj.name in [o.name for o in leaf.objects]:
            continue
        _unlink_from_all_collections(bpy, obj)
        leaf.objects.link(obj)
        counts[target] += 1
    return dict(counts)


def apply_material_rules(rules: list[Rule]) -> dict[str, int]:
    """Replace material slot 0 of each object with the rule-matched
    material. The material must already exist in bpy.data.materials.

    Returns a {material_name: n_objects_changed} count.
    """
    bpy = _require_bpy()
    counts: dict[str, int] = defaultdict(int)
    missing: set[str] = set()
    for obj in list(bpy.context.scene.objects):
        rule = first_match(rules, obj)
        if rule is None:
            continue
        mat = bpy.data.materials.get(rule.target)
        if mat is None:
            missing.add(rule.target)
            continue
        if not obj.material_slots:
            # Add a slot via the data block so this works in headless mode.
            if hasattr(obj.data, "materials"):
                obj.data.materials.append(mat)
            else:
                continue
        else:
            obj.material_slots[0].material = mat
        counts[rule.target] += 1
    if missing:
        print(f"[trian3d] warning: {len(missing)} material(s) not found in "
              f"bpy.data.materials: {sorted(missing)}")
    return dict(counts)


def collapse_to_linked_data(*, collection_name: Optional[str] = None
                            ) -> tuple[int, int]:
    """Group objects whose mesh has identical (vert_count, edge_count,
    poly_count, first_vert_co) and link them all to one canonical
    bpy.types.Mesh datablock. The other Mesh datablocks become orphan
    and Blender will purge them on next save.

    Returns (n_objects_relinked, n_unique_meshes_kept).

    If `collection_name` is given, only objects under that collection (and
    its descendants) are considered.
    """
    bpy = _require_bpy()
    if collection_name is None:
        objs = list(bpy.context.scene.objects)
    else:
        coll = bpy.data.collections.get(collection_name)
        if coll is None:
            return (0, 0)
        seen: set[str] = set()
        objs = []
        # Recursive walk including children-of-children.
        stack = [coll]
        while stack:
            c = stack.pop()
            for o in c.objects:
                if o.name not in seen:
                    seen.add(o.name)
                    objs.append(o)
            stack.extend(c.children)

    # Group by signature.
    buckets: dict[str, list[Any]] = defaultdict(list)
    for obj in objs:
        mesh = getattr(obj, "data", None)
        if mesh is None or not hasattr(mesh, "vertices"):
            continue
        v0 = tuple(mesh.vertices[0].co) if len(mesh.vertices) > 0 else None
        sig = mesh_signature(
            vertex_count=len(mesh.vertices),
            edge_count=len(mesh.edges),
            poly_count=len(mesh.polygons),
            first_vert_co=v0,
        )
        buckets[sig].append(obj)

    relinked = 0
    unique_kept = 0
    for sig, group in buckets.items():
        if len(group) < 2:
            continue
        canonical = group[0].data
        unique_kept += 1
        for obj in group[1:]:
            if obj.data is canonical:
                continue
            obj.data = canonical
            relinked += 1
    return (relinked, unique_kept)
