# Sprint 7 — Pipeline Quality Pass

**Date:** 2026-05-02
**Scope:** B (pipeline quality pass; not a workflow redesign)
**Status:** Draft v2 — revised after independent research review (Blosm, Polyhaven, Blender 4.2 EEVEE Next release notes, T78893 driver bug, VF-BlenderPlanarUV, 3dfier ortho-on-LoD2 conventions)

## Problem

Showcase renders in the README misrepresent and underdeliver against the workflow's actual capability:

- `06_feature_ground_shader.png` shows almost no ground (mostly sky).
- `05_feature_trees.png` shows low-poly cylinder+icosphere fallbacks.
- `01_poster.png` and aerial views read as washed-out white "monopoly piece" buildings on flat noise.
- The per-feature test harness invents synthetic mini-scenes that don't match production framing, so a passing test does not imply a usable showcase.

User wants: semi-photorealistic output, no overlapping architecture, clean textures, **and** a `.blend` that can be modified post-pipeline to drop in higher-resolution textures or different tree assets per project.

## Root-cause diagnosis

| Symptom | Root cause | File |
|---|---|---|
| Ground missing in feature shot | Camera at `(0,-50,4)` + 82° pitch + 8 m displacement → frame is mostly sky | `workflows/_test_feature_in_blender.py:136-138` |
| Low-poly trees | `add_curve_sapling` addon is unreliable in `--background`; falls through to procedural primitive fallback | `features/trees.py:117,144` |
| Uniform "monopoly" roofs | `Texture Coordinate.Generated` is per-object 0..1 → every building samples the same patch of the DOP UDIM, not the orthophoto pixel above it | `features/buildings_textured.py:99-102` |
| Top-down ground washed out | Slope mask contributes 0 in plan view; tile-color mix dominates DOP at fixed `Fac=0.7` | `features/ground_shader.py` |
| Aerial haze too dominant | Recent commit reduced cube height but did not adapt opacity per altitude | `_blender_assemble_full.py` |
| Showcase ≠ production | Per-feature harness uses synthetic scenes, not crops of the real assembled scene | `_test_feature_in_blender.py` |

The shaders themselves (ground-shader 4-layer blend, buildings PBR walls, groundcover GN scatter) are reasonable. The fixes are surgical, not architectural.

## Approach overview

Five threads, all bounded:

1. **Asset seam for trees** — replace runtime Sapling with a pre-baked `assets/trees.blend` library, link/append in.
2. **Buildings roof UV fix** — switch from `Generated` to world-XY object coords so each roof samples the right DOP pixel.
3. **Ground shader top-down bias** — altitude-driven mix factor so DOP detail dominates at high altitude.
4. **Visual integration test harness** — heuristic image assertions (ground-visible, color diversity, no haze overpower) on real assembled-scene crops, with golden-metric manifests in git.
5. **Modifiability seams** — documented per-region overrides so each project can swap textures/trees without code changes.

Out of scope: render engine swap, paid assets, refactor of `_blender_assemble_full.py`, CityGML changes, support for non-Bavarian regions.

## Section 1 — Tree asset seam

### Deliverable

`openmap_blender_tools/assets/trees.blend` containing one collection `TreeTemplates` with 4 mesh objects:

- `TreeTpl_Oak` — broadleaf, ~10 m, autumn-warm leaf card
- `TreeTpl_Beech` — broadleaf, ~12 m, mid-green
- `TreeTpl_Spruce` — conifer, ~14 m, dark blue-green
- `TreeTpl_Birch` — slender broadleaf, ~9 m, light green

### Asset sourcing (revised after research review)

Two acceptable sources, both CC0, both flow into the same `TreeTemplates` collection:

**Default — Polyhaven trees (recommended).** Download 4 ready-made Polyhaven tree assets (e.g. `island_tree_03`, `pine_tree_01`, `oak_tree_02`, `birch_tree_01`), strip per-species LODs to one mesh + one leaf-card material with proper alpha, save into `assets/trees.blend` under the standard collection name. ~30 MB total — strictly higher visual quality than Sapling+leaf cards, with one less moving part (no generator step).

**Alternate — Sapling-baked.** If Polyhaven licensing or asset weight is a concern, fall back to: open Blender 5.1 interactively with `add_curve_sapling` enabled, run `assets/build_trees.py`, generate each species with species-specific parameters, convert to mesh, scatter alpha-cutout leaf cards on branch tips via Geometry Nodes, apply one CC0 leaf texture per species. ~10 MB total.

Either way, the runtime code path is identical — the asset file is the only contract. The build script and the Polyhaven download script are both checked in; `assets/trees.blend` is the authoritative artifact.

**Source citation:** Blosm ships exactly this pattern (`vegetation.blend` collection swapped by users) — confirmed standard in this niche.

### Runtime

`features/trees.py` rewritten:

```python
def apply(context):
    bpy = context["bpy"]
    terrain = context.get("terrain_obj") or _find_or_create_fallback_terrain(bpy)
    if terrain is None:
        return {}

    coll = _link_tree_templates(bpy)  # bpy.data.libraries.load
    gn_mod = _attach_or_replace_gn_scatter(bpy, terrain, coll)
    return {"trees_template_count": len(coll.objects),
            "trees_modifier_name": gn_mod.name}
```

`_link_tree_templates` resolves in this priority order:

1. `data/<region>/trees.blend` if present (per-project override)
2. `openmap_blender_tools/assets/trees.blend` (bundled default)

Sapling code path and primitive fallback deleted from `trees.py`. The Sapling addon enable line is removed.

### Render-engine note (corrected)

In Blender 4.2+ EEVEE Next, the legacy `blend_method='HASHED'` enum was **removed**. The new API is the `surface_render_method` / Render Method enum: use **`DITHERED`** for leaf cards (replaces Hashed; small alias risk at distance, mitigated by EEVEE Next's default TAA). Alpha clip is now done with a `Math > Greater Than` node in the shader graph, not a property.

For leaf-card materials we set `material.surface_render_method = 'DITHERED'` and rely on TAA. Cycles needs no special handling.

**Source:** Blender 4.2 EEVEE release notes; issue #122489.

### Tests

Existing `tests/test_trees_feature.py` updated to mock `bpy.data.libraries.load` and assert it is called with the expected path. New visual assertion (Section 4) covers the rendered output.

## Section 2 — Buildings roof UV fix

### Change (revised after research review)

The standard archviz idiom for "DOP planar projection from above" is **Texture Coordinate → Object** with an Empty placed at the DOP UDIM origin and scaled to the bbox. This survives modification (drag the Empty in the .blend to re-align) and avoids stamping bbox numbers into every material.

In `features/buildings_textured.py`:

1. **Add a scene-level `DOPProjector` Empty** in `apply()`, positioned at `(bbox.min_x, bbox.min_y, 0)` with scale `(bbox.width, bbox.height, 1)`. Stable name; idempotent.
2. **Roof material** uses `Texture Coordinate.Object → DOPProjector` driving a **Box Mapping** (`ShaderNodeMapping`'s mapping is fine, but the texture node's projection is set to `BOX` with blend ≈ 0.3). Box mapping handles pitched LoD2 roofs gracefully — pure planar mapping stretches the south/north faces of pitched roofs into streaks. **This is the most common visible artifact in this genre and the v1 spec missed it.**
3. **Drop MULTIPLY mix `Fac`** from `0.7` → `0.4` so DOP detail dominates and tile color tints rather than overrides.

### Building/ground DOP color seam (added after research review)

Roofs and ground both sample the DOP, but in v1 they used different sampling chains and different mix factors → visible color discontinuity at building edges from drone altitude. Fix: ground's DOP `Texture Coordinate` chain uses the **same** `DOPProjector` Empty as the roof material. The procedural-vs-DOP mix factor in the ground shader (Section 3) is allowed to vary by altitude, but the DOP color itself comes from one shared sampling chain → no seam.

### Result

Each building's roof samples the orthophoto pixel actually above it. Adjacent identical-cube buildings will get distinct roof colors from the DOP. Tile color stays as a hue stabilizer so reflective/snowy/shadowed DOP patches don't produce blue or black roofs.

### Tests

- Unit: assert the `DOPProjector` Empty is created at expected location/scale, and that the roof material's `Texture Coordinate` is connected to it via `Object` output (not `Generated`).
- Unit: assert `surface_render_method` for leaf-card materials is `DITHERED` after apply.
- Visual: poster crop must have ≥ 12 unique hues in the building region (Section 4); building/ground edge transition has hue delta < threshold (no seam).

## Section 3 — Ground shader altitude bias

### Change (revised after research review — driver dropped)

Drivers reading object transforms into shader inputs have a long-running render-time update bug (Blender T78893, #113930). v1 anticipated this with a fallback hook; research review confirms we should skip the driver entirely and go straight to the hook — fewer moving parts.

In `features/ground_shader.py`, when `base_image_material` is present, the existing `MULTIPLY` mix gets a node-group input `altitude_dop_weight` (float, default 0.6).

The pipeline registers a `bpy.app.handlers.render_pre` handler that, before each render, computes:

```python
cam_z = scene.camera.location.z
weight = max(0.15, min(0.6, 0.6 - (cam_z - 100.0) * 0.00045))
mat.node_tree.nodes["GroundShader"].inputs["altitude_dop_weight"].default_value = weight
```

Curve: z=100 → 0.6 (procedural-leaning), z=1000 → 0.15 (DOP-leaning), linear between, clamped at the ends. Per-region overrides via `data/<region>/ground_overrides.json` (Section 5) replace the curve.

The handler is registered on extension load and unregistered on unload — same pattern the existing fps-tracker uses.

### Tests

- Unit: assert the `render_pre` handler is registered and writes the expected weight given a fixture camera Z.
- Visual: poster crop ground luminance variance ≥ threshold (proves DOP detail is showing through, not just procedural noise).

## Section 4 — Visual integration test harness

### Layout

```
workflows/tests/visual/
  __init__.py
  conftest.py             # fixtures: assembled-scene path, region, camera presets
  assertions.py           # image-metric helpers
  golden/
    poster.json
    fpv-walk.json
    mid-drone.json
    aircraft-approach.json
  test_pipeline_visual.py # the pytest file
  artifacts/              # gitignored — written on test run for inspection
```

### How it runs

1. Pytest fixture (session-scoped) calls `python workflows/full_pipeline.py --region muc-marienplatz-50m --skip-download` (uses cached tiles in `data/raw/muc-marienplatz-50m/`). Produces `data/scene_muc-marienplatz-50m.blend`. Cached across tests in the session.
2. **Determinism pin (added after research review):** the fixture forces, before any render: `scene.eevee.taa_render_samples = 32`, `scene.cycles.seed = 0`, `scene.frame_set(1)`, `scene.eevee.use_motion_blur = False`, EEVEE device = CPU. Without these pins ±10% tolerances are not enough — Blender's own render suite pins the same trio. (Sources: Blender render-test handbook.)
3. For each of 4 camera presets, render at 512×384 to `workflows/tests/visual/artifacts/<preset>.png`.
4. Each test loads its rendered PNG and runs assertions:

```python
def test_poster_has_visible_ground():
    img = load("artifacts/poster.png")
    assertions.ground_visible(img, min_ratio=0.25)
    assertions.color_diversity(img, region="buildings", min_unique_hues=12)
    assertions.no_haze_overpower(img, max_blue_dominance=0.5)
    metrics = assertions.summary(img)
    golden = load_json("golden/poster.json")
    assertions.metrics_within_tolerance(metrics, golden, tol=0.10)
```

### Assertion helpers (`assertions.py`)

All operate on numpy arrays from `PIL.Image`. Pure Python + numpy + Pillow; no OpenCV.

- `ground_visible(img, min_ratio)` — lower-half of frame, count pixels with luminance < 0.85 and HSV-hue not in sky-blue band (190-230°). Ratio ≥ min_ratio or fail with the actual value and a saved diff annotation.
- `color_diversity(img, region, min_unique_hues)` — quantize hue to 24 bins, count populated bins in given pixel region.
- `no_haze_overpower(img, max_blue_dominance)` — mean blue channel / mean luminance ≤ max.
- `tree_present(img, expected_green_ratio_range)` — green-dominant pixels / total ∈ range.
- `metrics_within_tolerance(actual, golden, tol)` — every numeric key in golden must be within `±tol` (relative) of actual; new keys allowed, missing keys fail.
- **`rmse_tripwire(img, last_known_good_path, max_rmse=0.25)` (added after research review)** — coarse pixel-wise RMSE vs. the previous-known-good PNG, normalised 0..1. Catches catastrophic regressions (all buildings invisible, sky inverted) that metric heuristics can pass through. The "last known good" PNG lives at `workflows/tests/visual/golden/<preset>_lkg.png`, refreshed by an explicit `--update-golden` flag, never auto-refreshed.

### Golden manifests

Plain JSON, e.g. `golden/poster.json`:

```json
{
  "ground_pixel_ratio": 0.42,
  "unique_hues_buildings": 18,
  "blue_dominance": 0.31,
  "ground_luminance_variance": 0.012
}
```

Golden values are recorded by running the harness once after the fixes land and copying the `metrics` blob into the JSON. Tolerances absorb procedural-shader seed drift.

### Render matrix — wide + close at multiple altitudes

The visual harness renders **8 shots per run**, not 4. Three altitude bands × wide-or-close framings, plus two synthetic close-ups for per-feature isolation. This is what the LLM-vision tier (next subsection) actually inspects.

| Slot | Altitude | Framing | Purpose |
|---|---|---|---|
| `wide_aerial` | 2000 m | wide, looking down ~60° | building density, ground/roof seam at altitude |
| `wide_mid_drone` | 500 m | wide, looking down ~45° | tree distribution, neighborhood-scale scene composition |
| `wide_low_drone` | 80 m | wide, horizontal | building heights, rooflines against horizon |
| `wide_fpv` | 1.7 m | wide, horizontal | groundcover, ground-level realism, eye-level building scale |
| `close_building` | 30 m | one building filling frame | wall PBR, roof DOP texture, no roof tiling artifacts |
| `close_tree` | 5 m | one tree filling frame | leaf-card alpha, trunk mesh, foliage silhouette (NOT solid blob) |
| `close_ground_patch` | 10 m | 5×5 m ground patch | shader detail, no obvious tiling, slope blending |
| `close_seam` | 20 m | edge of one building meeting ground | DOP color seam test (Section 2) |

All rendered to `workflows/tests/visual/artifacts/<slot>.png` at 768×512. Total render budget ~80 s on the Marienplatz fixture (renders are small and the scene is already loaded).

### LLM-vision review tier (added)

A third layer of test defense, running after the metric assertions and the RMSE tripwire. For each of the 8 rendered images, a vision-capable agent (called via `pytest --vision-review` opt-in flag) reads the PNG and answers a structured checklist tailored to that slot. Catches things that pass numeric metrics but are still wrong: floating trees, visible texture tiling, smooth-blob foliage, hue-correct-but-wrong-color roofs, sky occluding the foreground, etc.

**Per-slot checklists** (`workflows/tests/visual/vision_checks/<slot>.json`):

```json
// close_tree.json
{
  "questions": [
    {"id": "leaf_cards_visible",
     "ask": "Does the tree have visible leaf-shaped detail along the foliage silhouette, or is the foliage a smooth solid blob?",
     "expect": "leaf-shaped detail",
     "fail_severity": "blocker"},
    {"id": "trunk_proportional",
     "ask": "Does the trunk look proportional to the foliage (not a thin stick supporting a giant ball, not a fat log under a tiny bush)?",
     "expect": "yes",
     "fail_severity": "warn"},
    {"id": "tree_grounded",
     "ask": "Does the tree appear to sit on the ground, or is it floating with visible gap below the trunk?",
     "expect": "sits on ground",
     "fail_severity": "blocker"}
  ]
}
```

```json
// close_seam.json
{
  "questions": [
    {"id": "no_color_seam",
     "ask": "At the edge where the building meets the ground, is there a visible abrupt color discontinuity (different DOP sampling)?",
     "expect": "no visible discontinuity",
     "fail_severity": "blocker"},
    {"id": "roof_pitch_no_stretch",
     "ask": "On the pitched roof, does the texture appear stretched into streaks on the steep faces?",
     "expect": "not stretched",
     "fail_severity": "blocker"}
  ]
}
```

**Agent invocation.** From the pytest harness:

```python
from anthropic import Anthropic
client = Anthropic()  # ANTHROPIC_API_KEY from env

def vision_review(slot: str, png_path: Path) -> dict:
    checklist = json.loads((CHECKS_DIR / f"{slot}.json").read_text())
    img_b64 = base64.b64encode(png_path.read_bytes()).decode()
    msg = client.messages.create(
        model="claude-haiku-4-5-20251001",  # cheap; vision quality sufficient
        max_tokens=1024,
        system="You are a graphics QA reviewer. For each question, answer strictly: 'pass' or 'fail: <one-sentence reason>'. Output JSON only.",
        messages=[{
            "role": "user",
            "content": [
                {"type": "image", "source": {"type": "base64",
                  "media_type": "image/png", "data": img_b64}},
                {"type": "text", "text":
                  f"Review this {slot} render. Answer each question:\n" +
                  "\n".join(f"- {q['id']}: {q['ask']}" for q in checklist["questions"]) +
                  "\n\nReturn JSON: {\"<question_id>\": \"pass\" | \"fail: <reason>\"}"}
            ]
        }]
    )
    return json.loads(msg.content[0].text)
```

**Test integration.** Each pytest test calls `vision_review(slot, artifact_path)` and asserts every blocker-severity question returns `"pass"`. Warn-severity failures are logged but don't fail the test. Per-call cost ~$0.001 with Haiku 4.5; suite total ~$0.01. Caching: the harness skips re-review if the PNG hash matches a cached result in `artifacts/.vision_cache/`.

**Why three tiers, not just LLM:**

- Metrics (cheap, deterministic) — catch dimensional regressions, run in every dev loop.
- RMSE tripwire (cheap, deterministic) — catches "everything went black" class of failures.
- LLM-vision (slow, costs cents, opt-in) — catches semantic regressions metrics miss. Run in CI and before showcase regeneration, not on every save.

Failures from any tier write annotated diff PNGs to `artifacts/` for human review.

### Showcase regeneration

A new `workflows/regenerate_showcase.py` invokes the same harness on the larger `muc-sued-4x2` region and writes `showcase/01_poster.png` … `showcase/07_feature_*.png`. README is updated to make it clear the showcase is the test harness output. **Single source of truth — tests and showcase cannot drift.**

### Performance

- Marienplatz fixture: assemble ~30 s, 4 renders ~10 s each → total ~70 s for the visual suite. Acceptable for local CI.
- Skipped by default in unit-test runs; opt-in via `pytest -m visual` or env var `OPENMAP_VISUAL_TESTS=1`.

## Section 5 — Modifiability seams (documented contract)

Per-region override layout:

```
data/<region>/
  trees.blend              # optional — overrides bundled tree assets
  textures/
    dop_*.jpg              # any resolution, UDIM auto-tiled by buildings_textured
    leaves/<species>.png   # optional override of bundled leaf textures
  ground_overrides.json    # optional — per-region procedural shader weights
```

`ground_overrides.json` schema:

```json
{
  "forest_mask_cutoff": [0.55, 0.7],
  "field_altitude_threshold_m": 60.0,
  "altitude_dop_weight_curve": {"100m": 0.6, "1000m": 0.15}
}
```

When present, the assembler reads it before applying `ground-shader` and writes the values into the corresponding shader-input defaults / driver expression. Absent → defaults from this spec apply.

Output `.blend` invariants (so post-hoc editing is safe):

- Material names are stable: `BldRoof_DOP`, `BldWall_PBR`, `BldGround`, `GroundShader_Layered`, `TreeLeaf_<species>`.
- The `DOPProjector` Empty is the single source of truth for ortho UV. Drag it in the .blend → both roofs and ground re-align together.
- Tree templates linked, not appended (so users can replace `assets/trees.blend` and reopen the scene to get new trees without re-running the pipeline).
- Tree leaf textures referenced by relative path so swapping the file at `assets/textures/leaves/oak_color.png` updates the scene.
- **Library Overrides (added after research review):** when a user wants to tweak a per-region tree (e.g. scale the linked Oak by 0.8) without breaking the link, `Object → Library Override → Make` is the supported 4.x+ idiom. Documented in the modifiability section of the README.

### Forest-mask hook (deferred but documented)

OSM2World and Blosm both drive tree scatter density from `landuse=forest` polygons rather than just terrain slope. The current GN scatter is unconstrained — at scale it puts trees in fields and on roads. We **add** a `density_mask` named attribute input on the tree-scatter Geometry Nodes graph (a per-vertex float on the terrain mesh), wired into the Distribute Points on Faces density. The pipeline currently writes `1.0` everywhere. Future sprint can populate it from OSM landuse without touching the GN graph. This is a no-op visual change but a structural one — it preserves the seam.

These guarantees are documented in `README.md` under a new "Modifying the output `.blend`" section.

## Implementation order

1. Tree assets (Section 1) — needed first because visual tests depend on real trees being present.
2. Buildings UV fix (Section 2) — small, isolated.
3. Ground shader bias (Section 3) — small, isolated.
4. Visual harness (Section 4) — depends on 1-3 to set golden values.
5. Showcase regeneration (Section 4 last step).
6. Modifiability docs (Section 5) — purely additive, last.

Each step is independently mergeable.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Sapling-built trees still look stylized → not "semi-photoreal" | Leaf cards (alpha-cutout textured planes) are the dominant visual element; trunk topology is secondary at typical camera distance. If trees still look wrong after Section 1, escalate to user before Section 2. |
| EEVEE Next alpha-blend artifacts at distance | If visible, fall back to alpha-clip (HASHED → CLIP) on leaf cards. Not a structural change. |
| Driver on shader input doesn't update mid-render in EEVEE | If observed, replace with pre-render hook that bakes the value before each render. The hook is already used for fps/seed. |
| Visual test golden values differ across machines (GPU vs CPU EEVEE) | Tolerances of ±10% relative are generous enough; if they're not, switch to CPU-only EEVEE in the test fixture for determinism. |
| Showcase region (`muc-sued-4x2`) takes ~10 min to assemble — slow regeneration loop | Showcase regen script is opt-in; visual tests use the 30 s `muc-marienplatz-50m` fixture for the dev loop. |

## Acceptance criteria

- [ ] `pytest workflows/tests/visual/ -m visual` passes from a clean checkout (after one-time submodule fetch + tile download).
- [ ] All 7 showcase images regenerate without manual touch-up.
- [ ] `06_feature_ground_shader.png` shows ≥ 50 % ground in lower half of frame.
- [ ] `05_feature_trees.png` shows trees with visible leaf-card foliage (not solid icospheres).
- [ ] `01_poster.png` building region has ≥ 12 unique hues (sample of 100 buildings).
- [ ] User can drop `data/muc-sued-4x2/trees.blend` containing a `TreeTemplates` collection, re-open the assembled `.blend`, and see different trees without re-running the pipeline.
- [ ] User can replace `assets/textures/leaves/oak_color.png` with a higher-resolution version, reopen the scene, and see the new texture.
- [ ] `pytest --vision-review` passes all 8 slots' blocker-severity vision checks on a clean run.

## Open questions

None at spec-write time.

## Research-review changelog (v1 → v2)

Independent research reviewer cross-checked the v1 spec against Blosm, BlenderGIS, OSM2World, Polyhaven, Blender 4.2 EEVEE Next release notes, T78893, VF-BlenderPlanarUV, 3dfier, Blender's own render-test suite. Diff:

| # | Change | Rationale |
|---|---|---|
| 1 | Added Polyhaven trees as the recommended default; Sapling-baked is the alternate | Strictly higher quality at the same CC0 cost; Blosm uses the same pattern |
| 2 | Corrected `blend_method='HASHED'` → `surface_render_method='DITHERED'` | API removed in Blender 4.2; v1 would have failed at runtime |
| 3 | Replaced `Position`+bbox custom props with `Texture Coordinate.Object` + `DOPProjector` Empty | Standard archviz idiom; user-editable; survives modification |
| 4 | Added Box mapping (was planar-only) for pitched LoD2 roofs | Most common artifact in the genre, missed in v1 |
| 5 | Added building/ground DOP color seam mitigation | Same `DOPProjector` for roofs and ground avoids edge discontinuity at altitude |
| 6 | Removed driver-on-shader-input; render_pre handler only | T78893 — drivers don't update during render reliably |
| 7 | Added EEVEE determinism pin (TAA samples, seed, motion blur, CPU device) in test fixture | Blender's own suite pins these; v1's ±10% tolerances aren't enough without |
| 8 | Added RMSE tripwire alongside metric assertions | Catches catastrophic regressions that hue/luminance metrics pass through |
| 9 | Added `density_mask` attribute hook on tree GN scatter (deferred wiring) | OSM2World/Blosm precedent; no-op now but preserves the seam for landuse-driven scatter |
| 10 | Added Library Overrides documentation note | Blender 4.x+ idiom for per-instance tweaks of linked assets |
| 11 | Render matrix expanded 4 → 8 slots (3 altitude bands wide + 4 close-ups) | Per-feature isolation needs close shots; aerial wide alone hides leaf/seam/tile artifacts |
| 12 | Added LLM-vision review tier (Haiku 4.5 with image input + per-slot YAML checklists) | Catches semantic regressions metrics + RMSE pass through (floating trees, smooth-blob foliage, color-correct wrong roofs); ~$0.01/run, opt-in |

The v1 acceptance criteria are unchanged. The implementation order is unchanged.
