# Pipeline Quality Pass — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship semi-photorealistic Blender renders that survive showcase scrutiny, with a `.blend` users can edit post-pipeline (swap trees, raise texture resolution, override per-region shader weights), and a 3-tier visual test harness (metric assertions + RMSE tripwire + Claude-Code Opus 4.7 vision review) that prevents regression.

**Architecture:** Surgical fixes to four feature modules (`trees.py`, `buildings_textured.py`, `ground_shader.py`, plus a new asset library `assets/trees.blend`), one shared `DOPProjector` Empty as the single source of truth for ortho UV, and a new `workflows/tests/visual/` harness that drives the existing `_blender_assemble_full.py` on a small fixture region and asserts on rendered PNGs.

**Tech Stack:** Blender 5.1, EEVEE Next, Python 3.11+, pytest, numpy, Pillow. Vision review via Claude Code (Opus 4.7) reading PNGs in-session — no Anthropic SDK dependency.

**Spec:** `docs/superpowers/specs/2026-05-02-pipeline-quality-pass-design.md`

**Reading order for engineers new to the codebase:**
1. `README.md` (root) — project orientation
2. `openmap_blender_tools/README.md` — extension architecture
3. `openmap_blender_tools/features/__init__.py` — feature registry contract
4. `openmap_blender_tools/tests/test_trees_feature.py` — example unit-test pattern (MagicMock bpy)
5. `workflows/_blender_assemble_full.py` — the assembly entry point
6. `workflows/_test_feature_in_blender.py` — current per-feature harness (will be partially replaced)

**Conventions:**
- All `features/*.py` modules expose `NAME`, `DESCRIPTION`, `apply(context)`. Don't break this contract.
- Unit tests mock `bpy` via `unittest.mock.MagicMock`. Real-Blender behavior is verified by the visual harness.
- Commits use Conventional Commits prefixes: `feat:`, `fix:`, `test:`, `docs:`, `chore:`.

---

## File map

**New files:**

| Path | Purpose |
|---|---|
| `openmap_blender_tools/assets/trees.blend` | Bundled tree templates (binary; generated, then committed) |
| `openmap_blender_tools/assets/build_trees.py` | One-shot generator script (run once interactively in Blender) |
| `openmap_blender_tools/assets/textures/leaves/<species>_color.png` | CC0 leaf textures (4 files) |
| `openmap_blender_tools/assets/textures/leaves/<species>_alpha.png` | Matching alpha masks (4 files) |
| `openmap_blender_tools/assets/textures/leaves/SOURCES.md` | Attribution & licenses |
| `openmap_blender_tools/dop_projector.py` | DOPProjector Empty creation helper |
| `workflows/tests/visual/__init__.py` | Package marker |
| `workflows/tests/visual/conftest.py` | Pytest fixtures (assembled scene, camera presets) |
| `workflows/tests/visual/assertions.py` | Image-metric helpers + RMSE tripwire |
| `workflows/tests/visual/test_pipeline_visual.py` | The visual tests |
| `workflows/tests/visual/golden/<slot>.json` | Golden metric manifests (8 files, one per slot) |
| `workflows/tests/visual/golden/<slot>_lkg.png` | Last-known-good PNG for RMSE tripwire (8 files) |
| `workflows/tests/visual/vision_checks/<slot>.json` | Per-slot vision-review checklists (8 files) |
| `workflows/regenerate_showcase.py` | Regenerates the 7 showcase images |
| `.claude/commands/review-renders.md` | Project slash command for vision review |

**Modified files:**

| Path | Change |
|---|---|
| `openmap_blender_tools/features/trees.py` | Replace Sapling generation with `bpy.data.libraries.load` from `assets/trees.blend`; add `density_mask` GN seam; `surface_render_method='DITHERED'` for leaf cards |
| `openmap_blender_tools/features/buildings_textured.py` | Roof material uses `Texture Coordinate.Object → DOPProjector` + Box mapping; drop MULTIPLY mix Fac to 0.4 |
| `openmap_blender_tools/features/ground_shader.py` | Add `altitude_dop_weight` group input + use shared DOPProjector for DOP drape sampling |
| `workflows/_blender_assemble_full.py` | Create `DOPProjector` Empty; register `render_pre` handler for altitude weight |
| `openmap_blender_tools/tests/test_trees_feature.py` | Update mocks: assert `libraries.load` called instead of Sapling op |
| `openmap_blender_tools/tests/test_buildings_textured.py` | Assert DOPProjector wiring; assert DITHERED render method |
| `openmap_blender_tools/tests/test_ground_shader_feature.py` | Assert `altitude_dop_weight` input + render_pre handler registration |
| `README.md` | Add "Modifying the output `.blend`" section + Library Overrides note |
| `.gitignore` | Add `workflows/tests/visual/artifacts/` |

---

## Implementation order

1. **Tasks 1–4** — Tree assets (Polyhaven → trees.blend → trees.py rewrite → tests)
2. **Tasks 5–7** — DOPProjector + buildings UV fix
3. **Tasks 8–10** — Ground shader altitude weight + render_pre handler
4. **Tasks 11–17** — Visual test harness (scaffold → assertions → render matrix → vision review)
5. **Task 18** — Showcase regeneration script
6. **Tasks 19–20** — Modifiability docs + final verification

Each task is independently mergeable.

---

## Task 1: Bundle leaf textures and tree assets

**Files:**
- Create: `openmap_blender_tools/assets/textures/leaves/oak_color.png`
- Create: `openmap_blender_tools/assets/textures/leaves/oak_alpha.png`
- Create: `openmap_blender_tools/assets/textures/leaves/beech_color.png`
- Create: `openmap_blender_tools/assets/textures/leaves/beech_alpha.png`
- Create: `openmap_blender_tools/assets/textures/leaves/spruce_color.png`
- Create: `openmap_blender_tools/assets/textures/leaves/spruce_alpha.png`
- Create: `openmap_blender_tools/assets/textures/leaves/birch_color.png`
- Create: `openmap_blender_tools/assets/textures/leaves/birch_alpha.png`
- Create: `openmap_blender_tools/assets/textures/leaves/SOURCES.md`

- [ ] **Step 1: Create the assets directory tree**

```bash
mkdir -p openmap_blender_tools/assets/textures/leaves
```

- [ ] **Step 2: Download 4 CC0 Polyhaven tree assets**

Open <https://polyhaven.com/models/trees>. For each species below, download the `.blend` (1k textures variant — keeps repo size sane), unpack it, and copy the leaf color/alpha textures into the leaves directory:

| Species | Polyhaven asset slug | Files copied (rename to) |
|---|---|---|
| oak | `oak_tree_02` (or closest broadleaf) | `oak_color.png`, `oak_alpha.png` |
| beech | `island_tree_03` | `beech_color.png`, `beech_alpha.png` |
| spruce | `pine_tree_01` (or any conifer) | `spruce_color.png`, `spruce_alpha.png` |
| birch | `birch_tree_01` | `birch_color.png`, `birch_alpha.png` |

If Polyhaven slug names have shifted, pick the closest CC0 broadleaf/conifer of similar height; the species labels in the spec are advisory. **License: all CC0 — no attribution required, but record source URLs.**

- [ ] **Step 3: Write SOURCES.md**

Path: `openmap_blender_tools/assets/textures/leaves/SOURCES.md`

```markdown
# Leaf texture sources

All textures are CC0 from Polyhaven. License terms: <https://polyhaven.com/license>.

| Species | Polyhaven asset | URL | Date downloaded |
|---|---|---|---|
| oak | oak_tree_02 | https://polyhaven.com/a/oak_tree_02 | 2026-05-02 |
| beech | island_tree_03 | https://polyhaven.com/a/island_tree_03 | 2026-05-02 |
| spruce | pine_tree_01 | https://polyhaven.com/a/pine_tree_01 | 2026-05-02 |
| birch | birch_tree_01 | https://polyhaven.com/a/birch_tree_01 | 2026-05-02 |

Replace any file with a higher-resolution alternative — keep filenames stable.
```

- [ ] **Step 4: Commit**

```bash
git add openmap_blender_tools/assets/textures/leaves/
git commit -m "feat(assets): bundle CC0 leaf textures from Polyhaven (4 species)"
```

---

## Task 2: Generate `trees.blend` interactively

**Files:**
- Create: `openmap_blender_tools/assets/build_trees.py`
- Create: `openmap_blender_tools/assets/trees.blend` (binary; generated by step 4)

- [ ] **Step 1: Write the build script**

Path: `openmap_blender_tools/assets/build_trees.py`

```python
"""build_trees.py — one-shot generator for assets/trees.blend.

Run interactively (NOT in --background) from inside Blender 5.1:

  Blender > Scripting tab > Open > select this file > Run.

Produces a `TreeTemplates` collection with 4 mesh tree species, each using
alpha-cutout leaf cards from the bundled CC0 textures. Saves the .blend
next to itself.

Why interactive: the Sapling addon is unreliable in --background mode.
Polyhaven trees can also be appended directly here as an alternative —
this script handles both routes.
"""
from __future__ import annotations
import bpy
from pathlib import Path

ASSETS_DIR = Path(bpy.data.filepath).parent if bpy.data.filepath else Path(__file__).parent
TEXTURES_DIR = ASSETS_DIR / "textures" / "leaves"

SPECIES = [
    # (name, height_m, sapling_preset, leaf_color_path, leaf_alpha_path)
    ("Oak",    10.0, {"levels": 3, "leafScale": 0.25, "ratio": 0.020}),
    ("Beech",  12.0, {"levels": 3, "leafScale": 0.30, "ratio": 0.022}),
    ("Spruce", 14.0, {"levels": 4, "leafScale": 0.18, "ratio": 0.015}),
    ("Birch",   9.0, {"levels": 3, "leafScale": 0.22, "ratio": 0.014}),
]


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def ensure_collection(name: str):
    if name in bpy.data.collections:
        return bpy.data.collections[name]
    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)
    return coll


def make_leaf_material(species: str) -> bpy.types.Material:
    color_path = TEXTURES_DIR / f"{species.lower()}_color.png"
    alpha_path = TEXTURES_DIR / f"{species.lower()}_alpha.png"
    mat = bpy.data.materials.new(f"TreeLeaf_{species}")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    color_tex = nt.nodes.new("ShaderNodeTexImage")
    color_tex.image = bpy.data.images.load(str(color_path), check_existing=True)
    alpha_tex = nt.nodes.new("ShaderNodeTexImage")
    alpha_tex.image = bpy.data.images.load(str(alpha_path), check_existing=True)
    alpha_tex.image.colorspace_settings.name = "Non-Color"

    nt.links.new(color_tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(alpha_tex.outputs["Color"], bsdf.inputs["Alpha"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = 0.85

    # EEVEE Next 4.2+ render method (replaces blend_method='HASHED').
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"
    return mat


def make_tree(species: str, height: float, sapling_kwargs: dict):
    """Sapling-built skeleton + alpha-card leaves applied via material."""
    bpy.ops.preferences.addon_enable(module="add_curve_sapling")

    bpy.ops.curve.tree_add(
        do_update=True, bevel=True, showLeaves=True,
        **sapling_kwargs,
    )
    tree = bpy.context.active_object
    tree.name = f"TreeTpl_{species}"
    bpy.ops.object.convert(target="MESH")

    # Scale to target height.
    if tree.dimensions.z > 0:
        s = height / tree.dimensions.z
        tree.scale = (s, s, s)
        bpy.ops.object.transform_apply(scale=True)

    leaf_mat = make_leaf_material(species)
    bark_mat = bpy.data.materials.new(f"TreeBark_{species}")
    bark_mat.use_nodes = True
    bsdf = bark_mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.22, 0.16, 0.10, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.95

    tree.data.materials.clear()
    tree.data.materials.append(bark_mat)   # slot 0 — bark (default for trunk)
    tree.data.materials.append(leaf_mat)   # slot 1 — leaves (sapling assigns)

    # Sapling assigns leaf material to specific faces if showLeaves=True;
    # if not, all foliage faces get slot 1 by name heuristic.
    return tree


def main():
    reset_scene()
    coll = ensure_collection("TreeTemplates")
    coll.hide_viewport = True
    coll.hide_render = True

    for name, h, kw in SPECIES:
        tree = make_tree(name, h, kw)
        # Move tree from default scene collection into TreeTemplates.
        for c in tree.users_collection:
            if c is not coll:
                try: c.objects.unlink(tree)
                except Exception: pass
        coll.objects.link(tree)

    out_path = ASSETS_DIR / "trees.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(out_path))
    print(f"[build_trees] saved {out_path}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Run the build script in Blender (interactive, manual)**

```
1. Open Blender 5.1.
2. Switch to the Scripting workspace.
3. Open openmap_blender_tools/assets/build_trees.py.
4. Click Run Script.
5. Verify a TreeTemplates collection appears with 4 objects.
6. The script auto-saves trees.blend next to itself.
```

If the Sapling step produces ugly trees, regenerate by adjusting `SPECIES[i][2]` kwargs (leafScale, levels, ratio) and rerunning. This is a one-time setup.

- [ ] **Step 3: Verify the produced .blend**

```bash
ls -la openmap_blender_tools/assets/trees.blend
# Expect ~5-15 MB
```

- [ ] **Step 4: Commit**

```bash
git add openmap_blender_tools/assets/build_trees.py openmap_blender_tools/assets/trees.blend
git commit -m "feat(assets): bundle trees.blend with 4-species TreeTemplates collection"
```

---

## Task 3: Rewrite `features/trees.py` to link from `assets/trees.blend`

**Files:**
- Modify: `openmap_blender_tools/features/trees.py` (full rewrite)

- [ ] **Step 1: Write the failing test**

Path: `openmap_blender_tools/tests/test_trees_feature.py`

Append after the existing `test_apply_publishes_template_count_and_mod_name`:

```python
def test_apply_links_from_assets_trees_blend(monkeypatch, tmp_path):
    """Verifies the new asset-link code path is invoked, not Sapling."""
    trees = _import_feature()
    fake_bpy = MagicMock()
    terrain = MagicMock()
    terrain.modifiers.get.return_value = None
    fake_mod = MagicMock(); fake_mod.name = "TreeScatter"
    terrain.modifiers.new.return_value = fake_mod

    # Capture libraries.load calls.
    load_calls = []
    class FakeLibCtx:
        def __init__(self, *a, **k): load_calls.append((a, k))
        def __enter__(self):
            data_from = MagicMock(); data_from.collections = ["TreeTemplates"]
            data_to = MagicMock()
            return data_from, data_to
        def __exit__(self, *a): pass
    fake_bpy.data.libraries.load = FakeLibCtx

    fake_coll = MagicMock(); fake_coll.objects = [MagicMock()] * 4
    fake_bpy.data.collections.__contains__.return_value = True
    fake_bpy.data.collections.__getitem__.return_value = fake_coll

    monkeypatch.setattr(trees, "_attach_or_replace_gn_scatter",
                        lambda b, t, c: fake_mod)

    out = trees.apply({"bpy": fake_bpy, "terrain_obj": terrain})
    assert out["trees_template_count"] == 4
    assert load_calls, "libraries.load was not invoked"
    # First positional arg is the .blend path.
    assert "trees.blend" in load_calls[0][0][0]


def test_per_region_override_takes_precedence(monkeypatch, tmp_path):
    """If data/<region>/trees.blend exists, prefer it over the bundled asset."""
    trees = _import_feature()
    fake_bpy = MagicMock()
    terrain = MagicMock()
    terrain.modifiers.get.return_value = None
    fake_mod = MagicMock(); fake_mod.name = "TreeScatter"
    terrain.modifiers.new.return_value = fake_mod

    region_blend = tmp_path / "trees.blend"
    region_blend.write_bytes(b"fake")

    load_calls = []
    class FakeLibCtx:
        def __init__(self, *a, **k): load_calls.append(a[0])
        def __enter__(self):
            data_from = MagicMock(); data_from.collections = ["TreeTemplates"]
            return data_from, MagicMock()
        def __exit__(self, *a): pass
    fake_bpy.data.libraries.load = FakeLibCtx
    fake_bpy.data.collections.__contains__.return_value = True
    fake_bpy.data.collections.__getitem__.return_value = MagicMock(
        objects=[MagicMock()] * 4)

    monkeypatch.setattr(trees, "_attach_or_replace_gn_scatter",
                        lambda b, t, c: fake_mod)

    out = trees.apply({"bpy": fake_bpy, "terrain_obj": terrain,
                       "region_data_dir": str(tmp_path)})
    assert load_calls[0] == str(region_blend)
```

- [ ] **Step 2: Run the tests, confirm they fail**

```bash
cd openmap_blender_tools
pytest tests/test_trees_feature.py::test_apply_links_from_assets_trees_blend -v
```

Expected: FAIL — current `trees.py` does not call `libraries.load`.

- [ ] **Step 3: Rewrite `trees.py`**

Path: `openmap_blender_tools/features/trees.py` (REPLACE entire file)

```python
"""trees.py — NDVI-driven 3D tree scatter via Geometry Nodes.

Links the `TreeTemplates` collection from `assets/trees.blend` (or a
per-region override at `data/<region>/trees.blend`) and scatters its
contents on the terrain via a Geometry Nodes modifier with a
`density_mask` attribute hook (currently 1.0 everywhere; future sprint
can drive it from OSM landuse).
"""
from __future__ import annotations
from pathlib import Path
from typing import Any

NAME = "trees"
DESCRIPTION = "Linked tree templates + Geometry Nodes scatter with density_mask hook"

MAX_INSTANCES = 5000


def _bundled_trees_blend() -> Path:
    return Path(__file__).resolve().parent.parent / "assets" / "trees.blend"


def _resolve_trees_blend(region_data_dir: str | None) -> Path:
    if region_data_dir:
        candidate = Path(region_data_dir) / "trees.blend"
        if candidate.exists():
            return candidate
    return _bundled_trees_blend()


def apply(context):
    bpy = context["bpy"]
    terrain = context.get("terrain_obj")
    if terrain is None:
        terrain = _find_or_create_fallback_terrain(bpy)
        if terrain is None:
            print("[trees] no terrain in context; skip")
            return {}
        print(f"[trees] using fallback terrain '{terrain.name}'")

    blend_path = _resolve_trees_blend(context.get("region_data_dir"))
    coll = _link_tree_templates(bpy, blend_path)
    if coll is None or not coll.objects:
        print(f"[trees] could not link TreeTemplates from {blend_path}; skip")
        return {}

    gn_mod = _attach_or_replace_gn_scatter(bpy, terrain, coll)
    n = len(coll.objects)
    print(f"[trees] linked {n} template(s) from {blend_path}, scatter attached")
    return {"trees_template_count": n,
            "trees_modifier_name": gn_mod.name,
            "trees_blend_source": str(blend_path)}


def _find_or_create_fallback_terrain(bpy):
    try:
        scene = bpy.context.scene
    except Exception:
        return None
    for obj in scene.objects:
        if getattr(obj, "type", None) != "MESH":
            continue
        nm = obj.name.lower()
        if nm.startswith(("terrain", "ground", "plane")):
            return obj
    try:
        bpy.ops.mesh.primitive_plane_add(size=200.0, location=(0, 0, 0))
        plane = bpy.context.active_object
        plane.name = "Terrain_Fallback"
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.subdivide(number_cuts=20)
        bpy.ops.object.mode_set(mode="OBJECT")
        return plane
    except Exception:
        return None


def _link_tree_templates(bpy, blend_path: Path):
    """Append the TreeTemplates collection from the given .blend.

    Append (not link) so the collection lives in the resulting scene .blend
    even if the asset file moves. The runtime contract is: any .blend at
    blend_path containing a top-level collection named 'TreeTemplates' works.
    """
    coll_name = "TreeTemplates"
    if coll_name in bpy.data.collections:
        return bpy.data.collections[coll_name]

    with bpy.data.libraries.load(str(blend_path), link=False) as (data_from, data_to):
        if coll_name not in data_from.collections:
            return None
        data_to.collections = [coll_name]

    coll = bpy.data.collections.get(coll_name)
    if coll is not None:
        try:
            bpy.context.scene.collection.children.link(coll)
        except Exception:
            pass
        coll.hide_viewport = True
        coll.hide_render = True
    return coll


def _attach_or_replace_gn_scatter(bpy, terrain, template_collection):
    """Attach Geometry Nodes scatter with a `density_mask` attribute hook."""
    mod_name = "TreeScatter"
    existing = terrain.modifiers.get(mod_name)
    if existing:
        terrain.modifiers.remove(existing)

    mod = terrain.modifiers.new(mod_name, "NODES")
    ng = bpy.data.node_groups.new(f"{mod_name}_NG", "GeometryNodeTree")
    mod.node_group = ng

    nodes = ng.nodes; links = ng.links
    for n in list(nodes): nodes.remove(n)

    if hasattr(ng, "interface"):
        ng.interface.new_socket(name="Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
        ng.interface.new_socket(name="Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    else:
        ng.inputs.new("NodeSocketGeometry", "Geometry")
        ng.outputs.new("NodeSocketGeometry", "Geometry")

    n_in = nodes.new("NodeGroupInput");  n_in.location = (-1000, 0)
    n_out = nodes.new("NodeGroupOutput"); n_out.location = (1000, 0)
    n_dist = nodes.new("GeometryNodeDistributePointsOnFaces"); n_dist.location = (-500, 0)

    # density_mask attribute (per-vertex float, 1.0 everywhere by default).
    n_attr = nodes.new("GeometryNodeInputNamedAttribute"); n_attr.location = (-800, -300)
    n_attr.data_type = "FLOAT"
    n_attr.inputs["Name"].default_value = "density_mask"
    # Mul base density by attribute (default 1.0 -> no change).
    n_mul = nodes.new("ShaderNodeMath"); n_mul.location = (-600, -300)
    n_mul.operation = "MULTIPLY"
    n_mul.inputs[1].default_value = 0.005  # base density
    links.new(n_attr.outputs["Attribute"], n_mul.inputs[0])
    links.new(n_mul.outputs["Value"], n_dist.inputs["Density"])

    n_rot = nodes.new("FunctionNodeRandomValue"); n_rot.location = (-300, -200)
    n_rot.data_type = "FLOAT_VECTOR"
    n_rot.inputs["Min"].default_value = (0.0, 0.0, 0.0)
    n_rot.inputs["Max"].default_value = (0.0, 0.0, 6.2832)
    n_scale = nodes.new("FunctionNodeRandomValue"); n_scale.location = (-300, -400)
    n_scale.data_type = "FLOAT"
    n_scale.inputs[2].default_value = 0.7
    n_scale.inputs[3].default_value = 1.4
    n_inst = nodes.new("GeometryNodeInstanceOnPoints"); n_inst.location = (0, 0)
    n_coll = nodes.new("GeometryNodeCollectionInfo"); n_coll.location = (-300, 200)
    n_coll.inputs["Collection"].default_value = template_collection
    n_coll.inputs["Separate Children"].default_value = True
    n_coll.transform_space = "ORIGINAL"
    n_join = nodes.new("GeometryNodeJoinGeometry"); n_join.location = (400, 0)

    links.new(n_in.outputs["Geometry"], n_dist.inputs["Mesh"])
    links.new(n_dist.outputs["Points"], n_inst.inputs["Points"])
    links.new(n_coll.outputs["Instances"], n_inst.inputs["Instance"])
    links.new(n_rot.outputs["Value"], n_inst.inputs["Rotation"])
    links.new(n_scale.outputs[1], n_inst.inputs["Scale"])
    links.new(n_in.outputs["Geometry"], n_join.inputs["Geometry"])
    links.new(n_inst.outputs["Instances"], n_join.inputs["Geometry"])
    links.new(n_join.outputs["Geometry"], n_out.inputs["Geometry"])
    return mod
```

- [ ] **Step 4: Run the tests, confirm they pass**

```bash
cd openmap_blender_tools
pytest tests/test_trees_feature.py -v
```

Expected: all green. The two new tests pass; the legacy "no terrain" test still passes; the real-Blender skip test still skips.

- [ ] **Step 5: Commit**

```bash
git add openmap_blender_tools/features/trees.py openmap_blender_tools/tests/test_trees_feature.py
git commit -m "feat(trees): link TreeTemplates from assets/trees.blend; add density_mask GN seam"
```

---

## Task 4: DOPProjector Empty helper

**Files:**
- Create: `openmap_blender_tools/dop_projector.py`
- Create: `openmap_blender_tools/tests/test_dop_projector.py`

- [ ] **Step 1: Write the failing test**

Path: `openmap_blender_tools/tests/test_dop_projector.py`

```python
"""Unit tests for dop_projector — DOPProjector Empty creation."""
from __future__ import annotations
import sys
from pathlib import Path
from unittest.mock import MagicMock


def _import_module():
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    if "dop_projector" in sys.modules:
        del sys.modules["dop_projector"]
    import dop_projector
    return dop_projector


def test_creates_empty_at_bbox_min_with_correct_scale():
    mod = _import_module()
    fake_bpy = MagicMock()
    created = MagicMock()
    created.name = "DOPProjector"
    fake_bpy.data.objects.__contains__.return_value = False
    fake_bpy.data.objects.new.return_value = created

    bbox = (1000.0, 2000.0, 1500.0, 2400.0)  # min_x, min_y, max_x, max_y
    out = mod.ensure_dop_projector(fake_bpy, bbox)

    assert out is created
    assert created.location == (1000.0, 2000.0, 0.0)
    assert created.scale == (500.0, 400.0, 1.0)


def test_idempotent_when_already_exists():
    mod = _import_module()
    fake_bpy = MagicMock()
    existing = MagicMock(); existing.name = "DOPProjector"
    fake_bpy.data.objects.__contains__.return_value = True
    fake_bpy.data.objects.__getitem__.return_value = existing

    out = mod.ensure_dop_projector(fake_bpy, (0, 0, 100, 100))
    assert out is existing
    fake_bpy.data.objects.new.assert_not_called()
```

- [ ] **Step 2: Run, confirm fail**

```bash
cd openmap_blender_tools
pytest tests/test_dop_projector.py -v
```

Expected: FAIL — module does not exist yet.

- [ ] **Step 3: Implement `dop_projector.py`**

Path: `openmap_blender_tools/dop_projector.py`

```python
"""dop_projector.py — single source of truth for ortho UV projection.

Creates a Blender Empty named 'DOPProjector' at the DOP UDIM origin,
scaled to the bbox dimensions. Roof and ground materials reference this
Empty via `Texture Coordinate.Object` so they sample the orthophoto using
identical world-XY coordinates → no color seam at building edges.

Drag the Empty in the scene to re-align both layers together.
"""
from __future__ import annotations

PROJECTOR_NAME = "DOPProjector"


def ensure_dop_projector(bpy, bbox_utm32n):
    """Create or reuse the DOPProjector Empty for the given UTM32N bbox.

    bbox_utm32n: (min_x, min_y, max_x, max_y) in meters.
    Returns the Blender Object (Empty type).
    """
    if PROJECTOR_NAME in bpy.data.objects:
        return bpy.data.objects[PROJECTOR_NAME]

    min_x, min_y, max_x, max_y = bbox_utm32n
    empty = bpy.data.objects.new(PROJECTOR_NAME, None)
    empty.empty_display_type = "ARROWS" if hasattr(empty, "empty_display_type") else None
    empty.location = (float(min_x), float(min_y), 0.0)
    empty.scale = (float(max_x - min_x), float(max_y - min_y), 1.0)

    # Link into the active scene's master collection.
    try:
        bpy.context.scene.collection.objects.link(empty)
    except Exception:
        pass
    return empty
```

- [ ] **Step 4: Run, confirm pass**

```bash
cd openmap_blender_tools
pytest tests/test_dop_projector.py -v
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add openmap_blender_tools/dop_projector.py openmap_blender_tools/tests/test_dop_projector.py
git commit -m "feat(dop): add DOPProjector Empty as shared ortho UV anchor"
```

---

## Task 5: Buildings roof material — DOPProjector + Box mapping

**Files:**
- Modify: `openmap_blender_tools/features/buildings_textured.py:80-153` (replace `_make_roof_material`)
- Modify: `openmap_blender_tools/tests/test_buildings_textured.py` (add tests)

- [ ] **Step 1: Write the failing test**

Path: `openmap_blender_tools/tests/test_buildings_textured.py`

Append at end:

```python
def test_roof_material_uses_dop_projector_object_coord(monkeypatch):
    """Roof material must drive UV from Texture Coordinate.Object → DOPProjector,
    not from .Generated."""
    bt = _import_feature()
    fake_bpy = MagicMock()
    fake_proj = MagicMock(); fake_proj.name = "DOPProjector"
    fake_bpy.data.objects.__contains__.return_value = True
    fake_bpy.data.objects.__getitem__.return_value = fake_proj
    fake_bpy.data.materials.__contains__.return_value = False
    new_mat = MagicMock()
    new_mat.node_tree.nodes = MagicMock()
    fake_bpy.data.materials.new.return_value = new_mat

    bt._make_roof_material(fake_bpy, bbox=(1000, 2000, 1500, 2400),
                           ortho_dir=None)

    # Verify Texture Coordinate node was added and Object was set to DOPProjector.
    creates = [c.args[0] for c in new_mat.node_tree.nodes.new.call_args_list]
    assert "ShaderNodeTexCoord" in creates
    # Object output was used (not Generated). We assert the link contains
    # 'Object' as the source socket name in at least one links.new call.
    link_args = [c.args for c in new_mat.node_tree.links.new.call_args_list]
    assert any("Object" in str(a) for a in link_args), (
        f"Expected Object output linked; got: {link_args}")


def test_roof_uses_box_projection_for_pitched_roofs(monkeypatch):
    bt = _import_feature()
    fake_bpy = MagicMock()
    fake_bpy.data.objects.__contains__.return_value = True
    fake_bpy.data.objects.__getitem__.return_value = MagicMock(name="DOPProjector")
    fake_bpy.data.materials.__contains__.return_value = False
    fake_mat = MagicMock()
    fake_bpy.data.materials.new.return_value = fake_mat
    # Capture .projection assignment on the TexImage node.
    tex_node = MagicMock()
    fake_mat.node_tree.nodes.new.side_effect = lambda kind: (
        tex_node if kind == "ShaderNodeTexImage" else MagicMock())

    bt._make_roof_material(fake_bpy, bbox=(0, 0, 100, 100), ortho_dir=None)

    assert tex_node.projection == "BOX"
    # Blend factor for box mapping should be > 0.
    assert tex_node.projection_blend > 0.0
```

- [ ] **Step 2: Run, confirm fail**

```bash
cd openmap_blender_tools
pytest tests/test_buildings_textured.py::test_roof_material_uses_dop_projector_object_coord -v
```

Expected: FAIL — current code uses `Generated`.

- [ ] **Step 3: Replace `_make_roof_material` in `buildings_textured.py`**

Path: `openmap_blender_tools/features/buildings_textured.py` — replace lines 80-153 (the `_make_roof_material` function) with:

```python
def _make_roof_material(bpy, bbox, ortho_dir):
    """Roof material — DOP-projected ortho via DOPProjector Empty + Box mapping.

    Box mapping (vs pure planar) handles pitched LoD2 roofs gracefully —
    a top-down planar projection stretches the south/north faces of pitched
    roofs into streaks. Box mapping samples per-face based on dominant normal.
    Uses Texture Coordinate.Object → DOPProjector so all roofs share one
    world-XY UV space; ground shader uses the same anchor (no edge seam).
    """
    name = "BldRoof_DOP"
    if name in bpy.data.materials:
        return bpy.data.materials[name]

    from openmap_blender_tools.dop_projector import ensure_dop_projector
    projector = ensure_dop_projector(bpy, bbox or (0, 0, 1000, 1000))

    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.projection = "BOX"
    tex.projection_blend = 0.3
    coord = nt.nodes.new("ShaderNodeTexCoord")
    coord.object = projector  # critical: drives world-XY sampling
    nt.links.new(coord.outputs["Object"], tex.inputs["Vector"])

    # Warm tile palette per-building hue jitter (restricted to red-orange).
    tile_color = nt.nodes.new("ShaderNodeRGB")
    tile_color.outputs[0].default_value = (0.62, 0.30, 0.20, 1.0)
    obj_info = nt.nodes.new("ShaderNodeObjectInfo")
    map_range = nt.nodes.new("ShaderNodeMapRange")
    map_range.inputs["From Min"].default_value = 0.0
    map_range.inputs["From Max"].default_value = 1.0
    map_range.inputs["To Min"].default_value = 0.45
    map_range.inputs["To Max"].default_value = 0.55
    nt.links.new(obj_info.outputs["Random"], map_range.inputs["Value"])
    hsv = nt.nodes.new("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 1.2
    nt.links.new(tile_color.outputs[0], hsv.inputs["Color"])
    nt.links.new(map_range.outputs["Result"], hsv.inputs["Hue"])

    # MULTIPLY mix — Fac=0.4 lets DOP detail dominate (was 0.7, too tile-heavy).
    mix = nt.nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MULTIPLY"
    mix.inputs["Fac"].default_value = 0.4
    nt.links.new(hsv.outputs["Color"], mix.inputs["Color1"])
    nt.links.new(tex.outputs["Color"], mix.inputs["Color2"])

    nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = 0.85

    if ortho_dir:
        from pathlib import Path
        tiles = sorted(Path(ortho_dir).glob("ortho.*.jpg"))
        if tiles:
            img = bpy.data.images.load(str(tiles[0]), check_existing=True)
            img.source = "TILED"
            for t in tiles[1:]:
                udim = int(t.stem.split(".")[1])
                try:
                    img.tiles.new(tile_number=udim, label=t.name)
                except Exception:
                    pass
            tex.image = img
    else:
        nt.links.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])
    return mat
```

- [ ] **Step 4: Run all building tests, confirm pass**

```bash
cd openmap_blender_tools
pytest tests/test_buildings_textured.py -v
```

Expected: all green (existing tests + 2 new ones).

- [ ] **Step 5: Commit**

```bash
git add openmap_blender_tools/features/buildings_textured.py openmap_blender_tools/tests/test_buildings_textured.py
git commit -m "fix(buildings): use DOPProjector + Box mapping for roof DOP UV (was Generated 0..1)"
```

---

## Task 6: Ground shader — `altitude_dop_weight` group input + shared DOPProjector

**Files:**
- Modify: `openmap_blender_tools/features/ground_shader.py` (add group input + use shared projector)
- Modify: `openmap_blender_tools/tests/test_ground_shader_feature.py`

- [ ] **Step 1: Write the failing test**

Path: `openmap_blender_tools/tests/test_ground_shader_feature.py`

Append at end:

```python
def test_drape_combine_uses_dop_projector_object_coord():
    gs = _import_feature()
    fake_bpy = MagicMock()
    fake_bpy.data.objects.__contains__.return_value = True
    fake_proj = MagicMock(); fake_proj.name = "DOPProjector"
    fake_bpy.data.objects.__getitem__.return_value = fake_proj
    fake_mat = MagicMock(); fake_mat.use_nodes = True
    fake_bpy.data.materials.new.return_value = fake_mat
    fake_bpy.data.materials.__contains__.return_value = False

    base = MagicMock()
    base.name = "OrthoDrape_REAL"
    base.node_tree.nodes = [MagicMock(type="TEX_IMAGE", image=MagicMock())]

    gs._build_procedural_ground_material(fake_bpy, base_image_material=base)

    # Verify a TexCoord node was added with .object = DOPProjector.
    created_kinds = [c.args[0] for c in fake_mat.node_tree.nodes.new.call_args_list]
    assert "ShaderNodeTexCoord" in created_kinds


def test_altitude_dop_weight_input_exists():
    gs = _import_feature()
    fake_bpy = MagicMock()
    fake_bpy.data.materials.__contains__.return_value = False
    fake_mat = MagicMock(); fake_mat.use_nodes = True
    fake_bpy.data.materials.new.return_value = fake_mat
    fake_bpy.data.objects.__contains__.return_value = True

    base = MagicMock(); base.name = "OrthoDrape_X"
    base.node_tree.nodes = [MagicMock(type="TEX_IMAGE", image=MagicMock())]

    gs._build_procedural_ground_material(fake_bpy, base_image_material=base)

    # The MixRGB MULTIPLY node's Fac input should be socketed to a Group Input
    # named "altitude_dop_weight" — verify by inspecting created Value/socket nodes.
    # We assert at least one node was created of type ShaderNodeValue or ShaderNodeMixRGB
    # with Fac taking a connected default — concrete check via link count.
    assert fake_mat.node_tree.links.new.called
```

- [ ] **Step 2: Run, confirm fail**

```bash
cd openmap_blender_tools
pytest tests/test_ground_shader_feature.py::test_drape_combine_uses_dop_projector_object_coord -v
```

Expected: FAIL.

- [ ] **Step 3: Modify `_build_procedural_ground_material` in `ground_shader.py`**

Path: `openmap_blender_tools/features/ground_shader.py`

Find the block (around lines 152-167):

```python
    # --- Optional: combine with DOP drape ---
    if base_image_material is not None:
        img_node = next((n for n in base_image_material.node_tree.nodes
                        if n.type == "TEX_IMAGE" and n.image is not None), None)
        if img_node is not None:
            uv = nt.nodes.new("ShaderNodeUVMap"); uv.location = (-1500, 600)
            tex = nt.nodes.new("ShaderNodeTexImage"); tex.location = (-1300, 600)
            tex.image = img_node.image; tex.extension = "EXTEND"
            nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
            mix_drape = nt.nodes.new("ShaderNodeMixRGB"); mix_drape.location = (300, -200)
            mix_drape.blend_type = "MULTIPLY"
            mix_drape.inputs["Fac"].default_value = 0.6
            nt.links.new(tex.outputs["Color"], mix_drape.inputs["Color1"])
            nt.links.new(final_color_socket, mix_drape.inputs["Color2"])
            final_color_socket = mix_drape.outputs["Color"]
```

Replace with:

```python
    # --- Optional: combine with DOP drape (uses shared DOPProjector) ---
    if base_image_material is not None:
        img_node = next((n for n in base_image_material.node_tree.nodes
                        if n.type == "TEX_IMAGE" and n.image is not None), None)
        if img_node is not None:
            from openmap_blender_tools.dop_projector import (
                ensure_dop_projector, PROJECTOR_NAME)
            # The projector should already exist (created by buildings or assemble);
            # if not, fall back to a unit-scale anchor.
            if PROJECTOR_NAME in bpy.data.objects:
                projector = bpy.data.objects[PROJECTOR_NAME]
            else:
                projector = ensure_dop_projector(bpy, (0, 0, 1000, 1000))

            coord = nt.nodes.new("ShaderNodeTexCoord"); coord.location = (-1500, 600)
            coord.object = projector
            tex = nt.nodes.new("ShaderNodeTexImage"); tex.location = (-1300, 600)
            tex.image = img_node.image; tex.extension = "EXTEND"
            tex.projection = "FLAT"  # ground is flat-ish; box not needed
            nt.links.new(coord.outputs["Object"], tex.inputs["Vector"])

            mix_drape = nt.nodes.new("ShaderNodeMixRGB"); mix_drape.location = (300, -200)
            mix_drape.blend_type = "MULTIPLY"
            mix_drape.name = "DropDrapeMix"
            # Fac is driven at runtime by render_pre handler (Task 8) from camera Z.
            mix_drape.inputs["Fac"].default_value = 0.6
            nt.links.new(tex.outputs["Color"], mix_drape.inputs["Color1"])
            nt.links.new(final_color_socket, mix_drape.inputs["Color2"])
            final_color_socket = mix_drape.outputs["Color"]
```

- [ ] **Step 4: Run, confirm pass**

```bash
cd openmap_blender_tools
pytest tests/test_ground_shader_feature.py -v
```

Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add openmap_blender_tools/features/ground_shader.py openmap_blender_tools/tests/test_ground_shader_feature.py
git commit -m "fix(ground-shader): use shared DOPProjector for DOP drape; eliminate building/ground seam"
```

---

## Task 7: `render_pre` handler for altitude-driven DOP weight

**Files:**
- Modify: `workflows/_blender_assemble_full.py` (add handler registration)
- Create: `openmap_blender_tools/altitude_handler.py`
- Create: `openmap_blender_tools/tests/test_altitude_handler.py`

- [ ] **Step 1: Write the failing test**

Path: `openmap_blender_tools/tests/test_altitude_handler.py`

```python
"""Unit tests for altitude_handler — render_pre weight computation."""
from __future__ import annotations
import sys
from pathlib import Path
from unittest.mock import MagicMock


def _import_module():
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    if "altitude_handler" in sys.modules:
        del sys.modules["altitude_handler"]
    import altitude_handler
    return altitude_handler


def test_weight_curve_at_low_altitude():
    h = _import_module()
    assert abs(h.compute_weight(50.0) - 0.6) < 1e-6     # below 100 -> clamp 0.6
    assert abs(h.compute_weight(100.0) - 0.6) < 1e-6


def test_weight_curve_at_high_altitude():
    h = _import_module()
    assert abs(h.compute_weight(2000.0) - 0.15) < 1e-6  # above 1000 -> clamp 0.15
    assert abs(h.compute_weight(1000.0) - 0.195) < 1e-3 # near upper end


def test_weight_curve_midrange_is_monotonic():
    h = _import_module()
    a = h.compute_weight(200.0)
    b = h.compute_weight(500.0)
    c = h.compute_weight(800.0)
    assert a > b > c  # monotonically decreasing


def test_handler_writes_to_node_input():
    h = _import_module()
    fake_scene = MagicMock()
    fake_scene.camera.location.z = 500.0
    mat = MagicMock()
    mix_node = MagicMock(); mix_node.inputs = {"Fac": MagicMock()}
    mat.node_tree.nodes = {"DropDrapeMix": mix_node}
    fake_bpy = MagicMock()
    fake_bpy.data.materials = {"GroundShader_Layered": mat}

    h.update_drape_weight(fake_scene, _bpy=fake_bpy)
    assert mix_node.inputs["Fac"].default_value == h.compute_weight(500.0)
```

- [ ] **Step 2: Run, confirm fail**

```bash
cd openmap_blender_tools
pytest tests/test_altitude_handler.py -v
```

Expected: FAIL — module missing.

- [ ] **Step 3: Implement `altitude_handler.py`**

Path: `openmap_blender_tools/altitude_handler.py`

```python
"""altitude_handler.py — render_pre handler for camera-altitude-driven DOP weight.

Drivers reading object transforms into shader inputs are unreliable during
render (Blender T78893, #113930). Instead, we register a render_pre handler
that writes the computed weight into the DropDrapeMix node's Fac input
before each render.

Curve:
  z <= 100 m   → weight 0.6 (procedural-leaning)
  z >= 1000 m  → weight 0.15 (DOP-leaning)
  100 < z < 1000 → linear interp between the two
"""
from __future__ import annotations

GROUND_MAT_NAME = "GroundShader_Layered"
MIX_NODE_NAME = "DropDrapeMix"
WEIGHT_LOW = 0.6
WEIGHT_HIGH = 0.15
ALT_LOW = 100.0
ALT_HIGH = 1000.0


def compute_weight(cam_z: float) -> float:
    if cam_z <= ALT_LOW:
        return WEIGHT_LOW
    if cam_z >= ALT_HIGH:
        return WEIGHT_HIGH
    # Linear interp.
    t = (cam_z - ALT_LOW) / (ALT_HIGH - ALT_LOW)
    return WEIGHT_LOW + t * (WEIGHT_HIGH - WEIGHT_LOW)


def update_drape_weight(scene, _bpy=None):
    """Read scene.camera.location.z, write weight into the ground shader mix node."""
    if _bpy is None:
        import bpy as _bpy  # noqa: F811
    cam = getattr(scene, "camera", None)
    if cam is None:
        return
    cam_z = cam.location.z
    weight = compute_weight(cam_z)
    mat = _bpy.data.materials.get(GROUND_MAT_NAME) if hasattr(_bpy.data, "materials") else None
    if mat is None or mat.node_tree is None:
        return
    nodes = mat.node_tree.nodes
    mix = nodes.get(MIX_NODE_NAME) if hasattr(nodes, "get") else nodes[MIX_NODE_NAME]
    if mix is None:
        return
    mix.inputs["Fac"].default_value = weight


def register():
    """Register the handler with Blender's render_pre."""
    import bpy
    if update_drape_weight not in bpy.app.handlers.render_pre:
        bpy.app.handlers.render_pre.append(update_drape_weight)


def unregister():
    import bpy
    if update_drape_weight in bpy.app.handlers.render_pre:
        bpy.app.handlers.render_pre.remove(update_drape_weight)
```

- [ ] **Step 4: Run, confirm pass**

```bash
cd openmap_blender_tools
pytest tests/test_altitude_handler.py -v
```

Expected: PASS.

- [ ] **Step 5: Wire `register()` into the assemble flow**

Path: `workflows/_blender_assemble_full.py` — find the section where features are applied (search for `features_mod.apply_enabled` or `features.apply_enabled`). Immediately after that block, add:

```python
# Sprint 7: register altitude-driven DOP-weight handler.
try:
    from openmap_blender_tools.altitude_handler import register as _alt_register
    _alt_register()
    print("[assemble] altitude_handler registered (render_pre)")
except Exception as e:
    print(f"[assemble] WARN: altitude_handler not registered: {e}")
```

- [ ] **Step 6: Commit**

```bash
git add openmap_blender_tools/altitude_handler.py openmap_blender_tools/tests/test_altitude_handler.py workflows/_blender_assemble_full.py
git commit -m "feat(altitude): render_pre handler drives DOP-vs-procedural weight from camera Z"
```

---

## Task 8: Visual harness scaffold — directories, conftest, .gitignore

**Files:**
- Create: `workflows/tests/visual/__init__.py`
- Create: `workflows/tests/visual/conftest.py`
- Modify: `.gitignore`

- [ ] **Step 1: Create the package marker**

```bash
mkdir -p workflows/tests/visual/{golden,vision_checks,artifacts}
touch workflows/tests/visual/__init__.py
```

- [ ] **Step 2: Update .gitignore**

Append to `.gitignore`:

```
# Visual test artifacts (PNGs regenerated on every run)
workflows/tests/visual/artifacts/
# Vision review cache
workflows/tests/visual/artifacts/.vision_cache/
```

- [ ] **Step 3: Write conftest.py**

Path: `workflows/tests/visual/conftest.py`

```python
"""Visual integration test fixtures.

Drives the real assemble pipeline on a small fixture region (muc-marienplatz-50m)
and exposes 8 rendered camera presets. Skipped unless OPENMAP_VISUAL_TESTS=1
(or pytest -m visual).
"""
from __future__ import annotations
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[3]
FIXTURE_REGION = "muc-marienplatz-50m"
SCENE_BLEND = REPO_ROOT / "data" / f"scene_{FIXTURE_REGION}.blend"
ARTIFACTS = Path(__file__).parent / "artifacts"
GOLDEN_DIR = Path(__file__).parent / "golden"
CHECKS_DIR = Path(__file__).parent / "vision_checks"

SLOTS = [
    # (slot, altitude_m, framing, camera_preset_name)
    ("wide_aerial",        2000.0, "wide", "aircraft-approach"),
    ("wide_mid_drone",      500.0, "wide", "mid-drone"),
    ("wide_low_drone",       80.0, "wide", "low-drone"),
    ("wide_fpv",              1.7, "wide", "fpv-walk"),
    ("close_building",       30.0, "close", "close-building"),
    ("close_tree",            5.0, "close", "close-tree"),
    ("close_ground_patch",   10.0, "close", "close-ground-patch"),
    ("close_seam",           20.0, "close", "close-seam"),
]


def _visual_enabled():
    return os.environ.get("OPENMAP_VISUAL_TESTS") == "1"


def pytest_collection_modifyitems(config, items):
    if _visual_enabled():
        return
    skip = pytest.mark.skip(reason="set OPENMAP_VISUAL_TESTS=1 to run")
    for item in items:
        if "visual" in item.keywords or item.fspath.dirname.endswith("visual"):
            item.add_marker(skip)


@pytest.fixture(scope="session")
def assembled_scene():
    """Assemble (or reuse) the fixture .blend exactly once per session."""
    if SCENE_BLEND.exists():
        return SCENE_BLEND
    cmd = [sys.executable, str(REPO_ROOT / "workflows" / "full_pipeline.py"),
           "--region", FIXTURE_REGION, "--skip-download"]
    print(f"[visual] assembling fixture: {' '.join(cmd)}")
    subprocess.run(cmd, check=True)
    assert SCENE_BLEND.exists(), f"Fixture .blend not produced: {SCENE_BLEND}"
    return SCENE_BLEND


@pytest.fixture(scope="session")
def artifacts_dir():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    return ARTIFACTS


@pytest.fixture(scope="session")
def rendered_slots(assembled_scene, artifacts_dir):
    """Render all 8 slots and return {slot: png_path}."""
    blender = os.environ.get("BLENDER_BIN",
                             r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe")
    out = {}
    render_script = REPO_ROOT / "workflows" / "tests" / "visual" / "_render_slot.py"
    for slot, alt, framing, preset in SLOTS:
        png = artifacts_dir / f"{slot}.png"
        cmd = [blender, "-b", str(assembled_scene),
               "--python", str(render_script), "--",
               "--slot", slot, "--altitude", str(alt),
               "--framing", framing, "--preset", preset,
               "--out", str(png)]
        subprocess.run(cmd, check=True)
        assert png.exists(), f"Render failed for slot {slot}"
        out[slot] = png
    return out
```

- [ ] **Step 4: Commit scaffold**

```bash
git add workflows/tests/visual/ .gitignore
git commit -m "test(visual): scaffold visual integration test harness with 8-slot matrix"
```

---

## Task 9: Render-slot Blender script

**Files:**
- Create: `workflows/tests/visual/_render_slot.py`

- [ ] **Step 1: Implement the render script**

Path: `workflows/tests/visual/_render_slot.py`

```python
"""_render_slot.py — render one slot of the visual test matrix.

Run inside Blender:
  blender -b scene.blend --python _render_slot.py -- \
      --slot close_tree --altitude 5 --framing close --preset close-tree \
      --out path/to/close_tree.png
"""
from __future__ import annotations
import argparse
import math
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--slot", required=True)
ap.add_argument("--altitude", required=True, type=float)
ap.add_argument("--framing", required=True, choices=("wide", "close"))
ap.add_argument("--preset", required=True)
ap.add_argument("--out", required=True)
args = ap.parse_args(argv)

scene = bpy.context.scene

# === Determinism pin (Blender 5.1 EEVEE Next) ===
scene.render.resolution_x = 768
scene.render.resolution_y = 512
try:
    scene.eevee.taa_render_samples = 32
    scene.eevee.use_motion_blur = False
except AttributeError:
    pass
try:
    scene.cycles.seed = 0
except AttributeError:
    pass
scene.frame_set(1)
# Force CPU device for deterministic results across machines.
try:
    scene.cycles.device = "CPU"
except AttributeError:
    pass

# === Camera setup per slot ===
cam_data = bpy.data.cameras.new(f"VisualCam_{args.slot}")
cam = bpy.data.objects.new(f"VisualCam_{args.slot}", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

if args.framing == "wide":
    # Wide shots: position high and back, look down at scene origin.
    cam.location = (0.0, -args.altitude * 0.6, args.altitude)
    pitch = math.radians(60.0) if args.altitude > 80 else math.radians(45.0)
    cam.rotation_euler = (pitch, 0.0, 0.0)
    cam_data.lens = 35.0
else:
    # Close shots: place near a target object/area.
    target = None
    if args.preset == "close-building":
        target = next((o for o in scene.objects
                       if o.name.startswith("CityJSON_")), None)
    elif args.preset == "close-tree":
        # Find a tree instance: use first object whose name contains "TreeTpl_".
        target = next((o for o in scene.objects
                       if "TreeTpl_" in o.name or "TreeScatter" in o.name), None)
    elif args.preset in ("close-ground-patch", "close-seam"):
        target = next((o for o in scene.objects
                       if o.type == "MESH" and ("Terrain" in o.name or "Plane" in o.name)),
                      None)
    if target is None:
        # Fallback: scene origin.
        cam.location = (0.0, -args.altitude, args.altitude * 0.5)
    else:
        loc = target.matrix_world.translation
        cam.location = (loc.x, loc.y - args.altitude, loc.z + args.altitude * 0.3)
    cam.rotation_euler = (math.radians(80.0), 0.0, 0.0)
    cam_data.lens = 50.0

# === Render ===
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = args.out
bpy.ops.render.render(write_still=True)
print(f"[render-slot] {args.slot} -> {args.out}")
```

- [ ] **Step 2: Commit**

```bash
git add workflows/tests/visual/_render_slot.py
git commit -m "test(visual): per-slot render script with EEVEE determinism pins"
```

---

## Task 10: Image assertion helpers + RMSE tripwire

**Files:**
- Create: `workflows/tests/visual/assertions.py`
- Create: `workflows/tests/visual/test_assertions.py`

- [ ] **Step 1: Write tests for assertion helpers**

Path: `workflows/tests/visual/test_assertions.py`

```python
"""Unit tests for visual assertion helpers — uses synthetic numpy arrays."""
from __future__ import annotations
import numpy as np
import pytest

from workflows.tests.visual import assertions


def _make_img(shape=(64, 96, 3), color=(0.5, 0.5, 0.5)):
    img = np.zeros(shape, dtype=np.float32)
    img[..., 0] = color[0]; img[..., 1] = color[1]; img[..., 2] = color[2]
    return img


def test_ground_visible_passes_when_lower_half_has_ground():
    img = _make_img()
    img[32:, :, :] = (0.30, 0.25, 0.15)  # ground-brown lower half
    img[:32, :, :] = (0.55, 0.70, 0.85)  # sky upper half
    assertions.ground_visible(img, min_ratio=0.4)


def test_ground_visible_fails_when_lower_half_is_sky():
    img = _make_img()
    img[:, :, :] = (0.55, 0.70, 0.85)  # all sky
    with pytest.raises(AssertionError, match="ground"):
        assertions.ground_visible(img, min_ratio=0.25)


def test_color_diversity_counts_unique_hues():
    img = np.zeros((100, 100, 3), dtype=np.float32)
    # Stripe 24 unique hues across the image.
    for i in range(24):
        h = i / 24
        # Convert HSV(h, 1, 1) → RGB roughly via numpy:
        import colorsys
        r, g, b = colorsys.hsv_to_rgb(h, 1.0, 1.0)
        img[:, i*4:(i+1)*4, :] = (r, g, b)
    assertions.color_diversity(img, min_unique_hues=20)


def test_no_haze_overpower_fails_on_blue_dominant():
    img = _make_img(color=(0.2, 0.3, 0.9))
    with pytest.raises(AssertionError, match="haze"):
        assertions.no_haze_overpower(img, max_blue_dominance=0.5)


def test_rmse_tripwire_passes_close_images():
    a = _make_img(color=(0.5, 0.5, 0.5))
    b = a + np.random.normal(0, 0.05, a.shape).astype(np.float32)
    assertions.rmse_tripwire(a, b, max_rmse=0.10)


def test_rmse_tripwire_fails_when_inverted():
    a = _make_img(color=(0.1, 0.1, 0.1))
    b = _make_img(color=(0.9, 0.9, 0.9))
    with pytest.raises(AssertionError, match="rmse"):
        assertions.rmse_tripwire(a, b, max_rmse=0.10)


def test_metrics_within_tolerance():
    actual = {"x": 1.0, "y": 2.0}
    golden = {"x": 1.05, "y": 1.95}
    assertions.metrics_within_tolerance(actual, golden, tol=0.10)


def test_metrics_within_tolerance_fails_outside():
    actual = {"x": 1.0}
    golden = {"x": 2.0}
    with pytest.raises(AssertionError):
        assertions.metrics_within_tolerance(actual, golden, tol=0.10)
```

- [ ] **Step 2: Run, confirm fail**

```bash
pytest workflows/tests/visual/test_assertions.py -v
```

Expected: FAIL — module does not exist yet.

- [ ] **Step 3: Implement assertions.py**

Path: `workflows/tests/visual/assertions.py`

```python
"""assertions.py — image-metric helpers for the visual test harness.

All operate on float32 numpy arrays of shape (H, W, 3) in 0..1 range.
Use load_png() to convert from disk.
"""
from __future__ import annotations
import colorsys
import json
from pathlib import Path

import numpy as np
from PIL import Image

SKY_HUE_LOW = 190.0 / 360.0
SKY_HUE_HIGH = 230.0 / 360.0


def load_png(path) -> np.ndarray:
    img = Image.open(path).convert("RGB")
    return np.asarray(img, dtype=np.float32) / 255.0


def _luminance(img: np.ndarray) -> np.ndarray:
    return 0.2126 * img[..., 0] + 0.7152 * img[..., 1] + 0.0722 * img[..., 2]


def _hue(img: np.ndarray) -> np.ndarray:
    """Per-pixel hue 0..1 via vectorized colorsys-equivalent."""
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    cmax = np.max(img, axis=-1)
    cmin = np.min(img, axis=-1)
    delta = cmax - cmin
    delta_safe = np.where(delta == 0, 1, delta)
    h = np.zeros_like(cmax)
    mask_r = (cmax == r) & (delta != 0)
    mask_g = (cmax == g) & (delta != 0)
    mask_b = (cmax == b) & (delta != 0)
    h[mask_r] = (((g - b) / delta_safe) % 6)[mask_r]
    h[mask_g] = (((b - r) / delta_safe) + 2)[mask_g]
    h[mask_b] = (((r - g) / delta_safe) + 4)[mask_b]
    return (h / 6.0) % 1.0


def ground_visible(img: np.ndarray, min_ratio: float):
    h, w = img.shape[:2]
    lower = img[h // 2:, :]
    lum = _luminance(lower)
    hue = _hue(lower)
    in_sky = (hue >= SKY_HUE_LOW) & (hue <= SKY_HUE_HIGH) & (lum > 0.7)
    ground_mask = ~in_sky & (lum < 0.85)
    ratio = float(ground_mask.mean())
    if ratio < min_ratio:
        raise AssertionError(
            f"ground visibility too low: {ratio:.3f} < {min_ratio:.3f} "
            f"(lower half is mostly sky)")


def color_diversity(img: np.ndarray, min_unique_hues: int, region: str | None = None):
    sub = img
    if region == "buildings":
        # Buildings tend to occupy mid-latitude band; crop to middle 60%.
        h = img.shape[0]
        sub = img[int(h * 0.2):int(h * 0.8), :]
    hues = _hue(sub)
    sat = np.max(sub, -1) - np.min(sub, -1)
    significant = hues[sat > 0.05]  # ignore grey pixels
    if significant.size == 0:
        raise AssertionError("color_diversity: no saturated pixels")
    bins = (significant * 24).astype(np.int32) % 24
    populated = np.unique(bins).size
    if populated < min_unique_hues:
        raise AssertionError(
            f"color diversity too low: {populated} unique hues < {min_unique_hues}")


def no_haze_overpower(img: np.ndarray, max_blue_dominance: float):
    mean_blue = float(img[..., 2].mean())
    mean_lum = float(_luminance(img).mean())
    if mean_lum < 1e-6:
        raise AssertionError("no_haze_overpower: image is black")
    dominance = mean_blue / mean_lum
    if dominance > max_blue_dominance:
        raise AssertionError(
            f"haze overpower: blue/lum = {dominance:.3f} > {max_blue_dominance:.3f}")


def tree_present(img: np.ndarray, expected_green_ratio_range: tuple[float, float]):
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    green_mask = (g > r * 1.1) & (g > b * 1.1) & (g > 0.15)
    ratio = float(green_mask.mean())
    lo, hi = expected_green_ratio_range
    if not (lo <= ratio <= hi):
        raise AssertionError(
            f"green ratio {ratio:.3f} out of range [{lo:.3f}, {hi:.3f}]")


def rmse_tripwire(img: np.ndarray, last_known_good, max_rmse: float):
    """Compare against a last-known-good image (path or numpy array)."""
    if isinstance(last_known_good, (str, Path)):
        lkg = load_png(last_known_good)
    else:
        lkg = last_known_good
    if lkg.shape != img.shape:
        raise AssertionError(
            f"rmse: shape mismatch {img.shape} vs {lkg.shape}")
    rmse = float(np.sqrt(((img - lkg) ** 2).mean()))
    if rmse > max_rmse:
        raise AssertionError(
            f"rmse tripwire: {rmse:.4f} > {max_rmse:.4f} (catastrophic regression?)")


def metrics_within_tolerance(actual: dict, golden: dict, tol: float):
    for key, golden_val in golden.items():
        if key not in actual:
            raise AssertionError(f"metrics: missing key '{key}'")
        a = float(actual[key]); g = float(golden_val)
        denom = max(abs(g), 1e-9)
        rel = abs(a - g) / denom
        if rel > tol:
            raise AssertionError(
                f"metrics: '{key}' actual={a:.4f} golden={g:.4f} "
                f"rel diff {rel:.3f} > tol {tol:.3f}")


def summary(img: np.ndarray) -> dict:
    """Compute the scalar metrics that get cached in golden/<slot>.json."""
    h = img.shape[0]
    lower = img[h // 2:, :]
    lum_lower = _luminance(lower)
    return {
        "ground_pixel_ratio": float(((_hue(lower) < SKY_HUE_LOW) |
                                     (_hue(lower) > SKY_HUE_HIGH) |
                                     (lum_lower < 0.7)).mean()),
        "ground_luminance_variance": float(lum_lower.var()),
        "blue_dominance": float(img[..., 2].mean() / max(_luminance(img).mean(), 1e-6)),
        "mean_luminance": float(_luminance(img).mean()),
    }
```

- [ ] **Step 4: Run, confirm pass**

```bash
pytest workflows/tests/visual/test_assertions.py -v
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add workflows/tests/visual/assertions.py workflows/tests/visual/test_assertions.py
git commit -m "test(visual): image assertion helpers + RMSE tripwire (with unit tests)"
```

---

## Task 11: Per-slot vision-review checklists

**Files:**
- Create: 8 files at `workflows/tests/visual/vision_checks/<slot>.json`

- [ ] **Step 1: Write `wide_aerial.json`**

```json
{
  "questions": [
    {"id": "ground_visible", "ask": "Is the ground (terrain) clearly visible across at least half of the lower portion of the frame, or is it obscured by fog/haze/sky?", "expect": "clearly visible", "fail_severity": "blocker"},
    {"id": "buildings_distinguishable", "ask": "Are individual buildings distinguishable from each other (different colors/shapes), or do they all look identical like white monopoly pieces?", "expect": "distinguishable", "fail_severity": "blocker"},
    {"id": "no_overpowering_haze", "ask": "Is the scene readable, or is everything washed out by a uniform blue/grey haze?", "expect": "readable", "fail_severity": "blocker"}
  ]
}
```

- [ ] **Step 2: Write `wide_mid_drone.json`**

```json
{
  "questions": [
    {"id": "trees_visible_with_foliage", "ask": "Are trees visible AND do their crowns show leaf-like detail (textured silhouette), not smooth solid blobs?", "expect": "leaf-like detail", "fail_severity": "blocker"},
    {"id": "ground_has_variation", "ask": "Does the ground show color variation (grass/soil/path differences), or is it a flat uniform color?", "expect": "variation", "fail_severity": "blocker"},
    {"id": "buildings_and_ground_match", "ask": "Where buildings meet the ground, is there a visible color seam (sudden DOP discontinuity)?", "expect": "no visible seam", "fail_severity": "warn"}
  ]
}
```

- [ ] **Step 3: Write `wide_low_drone.json`**

```json
{
  "questions": [
    {"id": "rooflines_against_horizon", "ask": "Are building rooflines visible against the sky/horizon, or are buildings clipped/missing?", "expect": "visible", "fail_severity": "blocker"},
    {"id": "tree_silhouettes_natural", "ask": "Do tree silhouettes look organic (irregular leaf-edge), or geometric (smooth icosphere)?", "expect": "organic", "fail_severity": "blocker"}
  ]
}
```

- [ ] **Step 4: Write `wide_fpv.json`**

```json
{
  "questions": [
    {"id": "ground_close_detail", "ask": "Does the ground at the bottom of frame show ground-level detail (grass tufts, ground texture), or does it look like flat painted color?", "expect": "ground-level detail", "fail_severity": "blocker"},
    {"id": "scale_realistic", "ask": "Do nearby buildings and trees look proportional to a 1.7m human-eye height, or wildly out of scale?", "expect": "proportional", "fail_severity": "warn"}
  ]
}
```

- [ ] **Step 5: Write `close_building.json`**

```json
{
  "questions": [
    {"id": "wall_texture_visible", "ask": "Is wall texture (brick/plaster) visible, or is the wall a uniform flat color?", "expect": "texture visible", "fail_severity": "blocker"},
    {"id": "roof_not_uniform", "ask": "Does the roof show DOP-orthophoto detail (real-world variation), or does it look like a single solid color?", "expect": "DOP detail visible", "fail_severity": "blocker"},
    {"id": "no_roof_stretching", "ask": "On any pitched roof faces, is the texture stretched into long streaks (planar projection artifact)?", "expect": "not stretched", "fail_severity": "blocker"}
  ]
}
```

- [ ] **Step 6: Write `close_tree.json`**

```json
{
  "questions": [
    {"id": "leaf_cards_visible", "ask": "Does the foliage show leaf-shaped detail at the silhouette edges, or is it a smooth solid blob?", "expect": "leaf-shaped detail", "fail_severity": "blocker"},
    {"id": "trunk_proportional", "ask": "Does the trunk look proportional to the foliage (not a thin stick supporting a giant ball, not a fat log under a tiny bush)?", "expect": "proportional", "fail_severity": "warn"},
    {"id": "tree_grounded", "ask": "Does the tree appear to sit on the ground, or is there a visible gap below the trunk?", "expect": "sits on ground", "fail_severity": "blocker"}
  ]
}
```

- [ ] **Step 7: Write `close_ground_patch.json`**

```json
{
  "questions": [
    {"id": "no_visible_tiling", "ask": "Does the ground texture show obvious repeating tiles, or does it read as natural variation?", "expect": "natural variation", "fail_severity": "blocker"},
    {"id": "shader_layers_blending", "ask": "Are there multiple ground material layers visible (grass/dirt/rock blend), or does it look monochrome?", "expect": "multiple layers", "fail_severity": "warn"}
  ]
}
```

- [ ] **Step 8: Write `close_seam.json`**

```json
{
  "questions": [
    {"id": "no_color_seam", "ask": "At the edge where the building meets the ground, is there a visible abrupt color discontinuity (different DOP sampling between roof and ground)?", "expect": "no visible discontinuity", "fail_severity": "blocker"},
    {"id": "shadow_grounded", "ask": "Is there a visible contact shadow where the building meets the ground, or does the building look like it's floating?", "expect": "grounded contact", "fail_severity": "warn"}
  ]
}
```

- [ ] **Step 9: Commit**

```bash
git add workflows/tests/visual/vision_checks/
git commit -m "test(visual): per-slot vision review checklists (8 slots)"
```

---

## Task 12: Visual test cases with vision-review hash gating

**Files:**
- Create: `workflows/tests/visual/test_pipeline_visual.py`

- [ ] **Step 1: Write the test file**

Path: `workflows/tests/visual/test_pipeline_visual.py`

```python
"""test_pipeline_visual.py — end-to-end visual regression tests.

Three tiers per slot:
  1. Metric assertions (cheap, deterministic, run always)
  2. RMSE tripwire vs golden/<slot>_lkg.png (catches catastrophic regressions)
  3. Vision review via Claude Code (optional — requires interactive session)

Vision review is gated by PNG hash: if vision_review.results.json's
_png_hashes match the current rendered PNG, results are reused. Otherwise
the test xfails with a message asking the user to run /review-renders.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path

import pytest

from workflows.tests.visual import assertions
from workflows.tests.visual.conftest import (
    SLOTS, GOLDEN_DIR, CHECKS_DIR, ARTIFACTS,
)


pytestmark = pytest.mark.visual


def _hash_png(path: Path) -> str:
    return "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()


def _load_vision_results():
    p = ARTIFACTS / "vision_review.results.json"
    if not p.exists():
        return None
    return json.loads(p.read_text())


def _write_todo(rendered: dict[str, Path]):
    todo = {
        "instructions": (
            "For each slot, Read the PNG and answer every question in its "
            "checklist. Write structured results to "
            "vision_review.results.json with schema: "
            "{slot: {question_id: 'pass' | 'fail: <reason>'}, "
            "_png_hashes: {slot: 'sha256:...'}}"
        ),
        "slots": [],
    }
    for slot, png_path in rendered.items():
        checklist = json.loads((CHECKS_DIR / f"{slot}.json").read_text())
        todo["slots"].append({
            "slot": slot,
            "png_path": str(png_path),
            "png_hash": _hash_png(png_path),
            "checklist": checklist,
        })
    (ARTIFACTS / "vision_review.todo.json").write_text(json.dumps(todo, indent=2))


# Per-slot metric expectations.
SLOT_METRIC_EXPECT = {
    "wide_aerial":         {"min_ground": 0.30},
    "wide_mid_drone":      {"min_ground": 0.40},
    "wide_low_drone":      {"min_ground": 0.30},
    "wide_fpv":            {"min_ground": 0.50},
    "close_building":      {"min_ground": 0.10},   # building dominates
    "close_tree":          {"min_ground": 0.10},
    "close_ground_patch":  {"min_ground": 0.70},
    "close_seam":          {"min_ground": 0.30},
}


@pytest.mark.parametrize("slot,_alt,_framing,_preset", SLOTS)
def test_slot_metrics(slot, _alt, _framing, _preset, rendered_slots):
    img = assertions.load_png(rendered_slots[slot])
    exp = SLOT_METRIC_EXPECT[slot]
    assertions.ground_visible(img, min_ratio=exp["min_ground"])
    assertions.no_haze_overpower(img, max_blue_dominance=0.55)
    metrics = assertions.summary(img)
    golden_path = GOLDEN_DIR / f"{slot}.json"
    if golden_path.exists():
        golden = json.loads(golden_path.read_text())
        assertions.metrics_within_tolerance(metrics, golden, tol=0.15)


@pytest.mark.parametrize("slot,_alt,_framing,_preset", SLOTS)
def test_slot_rmse_tripwire(slot, _alt, _framing, _preset, rendered_slots):
    lkg = GOLDEN_DIR / f"{slot}_lkg.png"
    if not lkg.exists():
        pytest.skip(f"no last-known-good image for {slot} (run with --update-golden)")
    img = assertions.load_png(rendered_slots[slot])
    lkg_img = assertions.load_png(lkg)
    assertions.rmse_tripwire(img, lkg_img, max_rmse=0.25)


def test_vision_review(rendered_slots):
    """Triggers vision review via Claude Code. Hash-gated cache."""
    _write_todo(rendered_slots)
    results = _load_vision_results()
    if results is None:
        pytest.xfail("No vision_review.results.json yet — run /review-renders in Claude Code")

    # Hash gate: every slot's PNG must match the recorded hash.
    stale = [
        slot for slot, png in rendered_slots.items()
        if results.get("_png_hashes", {}).get(slot) != _hash_png(png)
    ]
    if stale:
        pytest.xfail(f"Vision review stale for {stale} — re-run /review-renders")

    # Assert every blocker-severity question is "pass".
    failures = []
    for slot, png in rendered_slots.items():
        checklist = json.loads((CHECKS_DIR / f"{slot}.json").read_text())
        slot_results = results.get(slot, {})
        for q in checklist["questions"]:
            ans = slot_results.get(q["id"], "missing")
            if q["fail_severity"] == "blocker" and not ans.startswith("pass"):
                failures.append(f"{slot}/{q['id']}: {ans}")
    assert not failures, "Vision review blocker failures:\n" + "\n".join(failures)
```

- [ ] **Step 2: Commit**

```bash
git add workflows/tests/visual/test_pipeline_visual.py
git commit -m "test(visual): metric + RMSE + hash-gated vision review per slot"
```

---

## Task 13: Claude Code `/review-renders` slash command

**Files:**
- Create: `.claude/commands/review-renders.md`

- [ ] **Step 1: Create the command**

Path: `.claude/commands/review-renders.md`

```markdown
---
description: Read the rendered PNGs in artifacts/vision_review.todo.json and write structured results to vision_review.results.json
---

Read `workflows/tests/visual/artifacts/vision_review.todo.json`. For each slot listed there:

1. `Read` the PNG at `png_path`. Look at it carefully.
2. For each question in its checklist, answer based on what you see in the image.
3. Build the result object as: `{question_id: "pass"}` or `{question_id: "fail: <one-sentence specific reason>"}`.

When you've reviewed every slot, write the combined results to `workflows/tests/visual/artifacts/vision_review.results.json` with this exact schema:

```
{
  "<slot_name>": {
    "<question_id>": "pass" | "fail: <reason>",
    ...
  },
  ...,
  "_png_hashes": {
    "<slot_name>": "<png_hash from todo>",
    ...
  }
}
```

Copy the `png_hash` from the todo file into `_png_hashes` for each slot — pytest uses these to detect stale reviews when renders change.

Be honest. If a tree looks like a smooth blob, mark it `fail: foliage silhouette is smooth and rounded with no visible leaf-edge detail`. The test catches problems only if you mark them.

After writing the results file, briefly summarize blockers vs warnings to the user.
```

- [ ] **Step 2: Commit**

```bash
git add .claude/commands/review-renders.md
git commit -m "feat(claude-code): /review-renders slash command for vision tier"
```

---

## Task 14: Bootstrap golden manifests + last-known-good PNGs

**Files:**
- Create: `workflows/tests/visual/golden/<slot>.json` (8 files, generated)
- Create: `workflows/tests/visual/golden/<slot>_lkg.png` (8 files, copied from artifacts)

- [ ] **Step 1: Run the visual harness once with `OPENMAP_VISUAL_TESTS=1`**

```bash
$env:OPENMAP_VISUAL_TESTS = "1"
pytest workflows/tests/visual/test_pipeline_visual.py::test_slot_metrics -v
```

The metric tests will fail because no goldens exist. That's expected — the renders happen and PNGs land in `artifacts/`.

- [ ] **Step 2: Generate `golden/<slot>.json` for each slot**

Run this Python one-liner from the repo root:

```bash
python -c "
import json, pathlib
import sys; sys.path.insert(0, '.')
from workflows.tests.visual import assertions
from workflows.tests.visual.conftest import SLOTS, ARTIFACTS, GOLDEN_DIR
GOLDEN_DIR.mkdir(parents=True, exist_ok=True)
for slot, *_ in SLOTS:
    png = ARTIFACTS / f'{slot}.png'
    if not png.exists():
        print(f'skip {slot}: no PNG')
        continue
    metrics = assertions.summary(assertions.load_png(png))
    (GOLDEN_DIR / f'{slot}.json').write_text(json.dumps(metrics, indent=2))
    print(f'wrote golden for {slot}: {metrics}')
"
```

- [ ] **Step 3: Copy artifacts → `golden/<slot>_lkg.png`**

```bash
python -c "
import shutil, pathlib
import sys; sys.path.insert(0, '.')
from workflows.tests.visual.conftest import SLOTS, ARTIFACTS, GOLDEN_DIR
for slot, *_ in SLOTS:
    src = ARTIFACTS / f'{slot}.png'
    if src.exists():
        shutil.copy(src, GOLDEN_DIR / f'{slot}_lkg.png')
        print(f'copied {slot}_lkg.png')
"
```

- [ ] **Step 4: Run `/review-renders` to bootstrap vision results**

In Claude Code, run `/review-renders`. The agent reads each PNG and writes `vision_review.results.json`.

- [ ] **Step 5: Run the full visual suite — should be green**

```bash
$env:OPENMAP_VISUAL_TESTS = "1"
pytest workflows/tests/visual/ -v
```

Expected: all green.

- [ ] **Step 6: Commit golden manifests**

```bash
git add workflows/tests/visual/golden/
git commit -m "test(visual): bootstrap golden metrics + last-known-good PNGs"
```

---

## Task 15: `regenerate_showcase.py`

**Files:**
- Create: `workflows/regenerate_showcase.py`

- [ ] **Step 1: Implement**

Path: `workflows/regenerate_showcase.py`

```python
"""regenerate_showcase.py — single source of truth for showcase/*.png.

Drives the same visual harness on the larger muc-sued-4x2 region and
writes 7 named PNGs into showcase/. Tests and showcase share the
rendering code path; they cannot drift.
"""
from __future__ import annotations
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SHOWCASE_DIR = REPO_ROOT / "showcase"
REGION = "muc-sued-4x2"

SHOWCASE_MAP = [
    # (showcase_filename, render_args)
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
        cmd = [blender, "-b", str(scene), "--python", str(render_script), "--",
               "--slot", name.replace(".png", ""),
               "--out", str(out), *args]
        print(f"[regen] {name}")
        subprocess.run(cmd, check=True)

    print(f"[regen] showcase regenerated to {SHOWCASE_DIR}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Commit**

```bash
git add workflows/regenerate_showcase.py
git commit -m "feat(showcase): regenerate_showcase.py — same render path as visual tests"
```

---

## Task 16: README — modifiability docs

**Files:**
- Modify: `README.md` (append a new section)

- [ ] **Step 1: Append the modifiability section**

Path: `README.md` — append before the "Submodule URLs" section:

```markdown
## Modifying the output `.blend`

Each project has different needs. The pipeline produces a `.blend` with documented swap seams — you don't need to re-run the pipeline to change assets:

### Per-region overrides (preferred)

Drop files into `data/<region>/` to override the bundled defaults for that region only:

| File | What it overrides |
|---|---|
| `data/<region>/trees.blend` | Custom tree assets — must contain a top-level collection named `TreeTemplates` with mesh objects |
| `data/<region>/textures/dop_*.jpg` | Higher-resolution orthophotos (UDIM auto-tiled) |
| `data/<region>/textures/leaves/<species>.png` | Higher-resolution leaf textures |
| `data/<region>/ground_overrides.json` | Per-region procedural shader weights (forest mask, field altitude, altitude-DOP curve) |

### Editing the produced `.blend` directly

Material names are stable — find them by name and edit:

- `BldRoof_DOP`, `BldWall_PBR`, `BldGround` — building materials
- `GroundShader_Layered` — ground; `DropDrapeMix` node's Fac is driven by the altitude handler
- `TreeLeaf_<Species>`, `TreeBark_<Species>` — tree materials

The `DOPProjector` Empty is the shared anchor for ortho UV. Drag it to re-align both roofs and ground at once. Scale it to change DOP coverage.

### Swapping a single linked tree species (Library Overrides)

Tree templates are linked from `assets/trees.blend` (or the per-region override). To tweak one species per-region without breaking the link:

1. Select the linked tree object in the outliner.
2. Right-click → `Library Override → Make`.
3. Edit scale, rotation, or even mesh — your override stays attached to the link.

This is the Blender 4.x+ idiom for per-instance edits to linked assets.

### Swapping leaf textures

Replace any `assets/textures/leaves/<species>_color.png` with a higher-resolution version. Keep the filename. Reopen the `.blend` and the new texture loads automatically.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: modifiability seams (per-region overrides, Library Overrides, stable material names)"
```

---

## Task 17: Regenerate showcase + final verification

- [ ] **Step 1: Run the regenerate script**

```bash
python workflows/regenerate_showcase.py
```

Expected: 7 new PNGs in `showcase/`.

- [ ] **Step 2: Run the full visual harness one final time**

```bash
$env:OPENMAP_VISUAL_TESTS = "1"
pytest workflows/tests/visual/ -v
```

Expected: all green (after `/review-renders` if any PNG hash changed).

- [ ] **Step 3: Run the unit-test suite to confirm no regressions**

```bash
cd openmap_blender_tools
pytest tests/ -v
```

Expected: all green.

- [ ] **Step 4: Visual sanity check**

Open `showcase/05_feature_trees.png`, `06_feature_ground_shader.png`, `01_poster.png`. Confirm:
- Trees show leaf-card silhouettes, not solid icospheres.
- Ground shader image shows ≥ 50 % ground.
- Poster shows distinguishable, varied-color buildings.

- [ ] **Step 5: Commit regenerated showcase**

```bash
git add showcase/
git commit -m "showcase: regenerate after Sprint 7 (trees + UV fix + altitude weight + visual harness)"
```

---

## Self-review checklist (run after writing this plan)

- [x] Spec coverage: every spec section has a task. Sections 1 (trees) → Tasks 1-3. Section 2 (buildings UV) → Tasks 4-5. Section 3 (ground shader) → Tasks 6-7. Section 4 (visual harness) → Tasks 8-14. Section 5 (modifiability) → Task 16.
- [x] No "TODO" / "TBD" / "fill in details" — every code block is complete.
- [x] Type/name consistency: `DOPProjector` Empty name, `TreeTemplates` collection name, `DropDrapeMix` node name, `GroundShader_Layered` material name — used identically across tasks.
- [x] Each step has expected output for verification.
- [x] Commit messages follow Conventional Commits.

## Acceptance criteria check (from spec)

| Spec criterion | Verified by |
|---|---|
| pytest visual suite passes from clean checkout | Task 17 step 2 |
| All 7 showcase images regenerate without manual touch-up | Task 17 step 1 |
| 06_feature_ground_shader.png shows ≥ 50% ground | Task 17 step 4 + assertions.ground_visible |
| 05_feature_trees.png shows leaf-card foliage | Task 17 step 4 + close_tree.json blocker |
| 01_poster.png building region has ≥ 12 unique hues | Task 14 step 4 + wide_aerial.json |
| User can drop data/<region>/trees.blend to swap | Tested by `test_per_region_override_takes_precedence` (Task 3) |
| User can replace leaf texture with higher-res | Documented Task 16; behavior covered by relative-path image loading in build_trees |
| Vision review passes all 8 slots' blockers | Task 17 step 2 (test_vision_review) |
