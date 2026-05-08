# OpenMap_Workflow

Geospatial-to-Blender pipeline: downloads Bayern OpenData (DGM1/5, DOP20/40, LoD2), preprocesses via GDAL, assembles cinematic 3D scenes in Blender.

## Architecture

- `openmap_blender_tools/` — Blender extension (submodule, separate repo). Operators, materials, features.
- `OpenMap_Unifier/` — Download engine (submodule). Fetches tiles from Bayern LDBV.
- `workflows/` — Pipeline orchestration (`full_pipeline.py`), region presets, visual tests.
- CRS: EPSG:25832 (UTM Zone 32N). All coordinates in meters.
- Anchor system: large UTM coords shifted to Blender-local origin to avoid float32 precision loss.

## Build & Test

```powershell
# Unit tests require Python 3.11+ (Anaconda). System Python 3.10 can't install the package.
# First-time setup: cd openmap_blender_tools && pip install -e .
& "C:\ProgramData\anaconda3\python.exe" -m pytest openmap_blender_tools/tests/ -v --ignore=openmap_blender_tools/tests/smoke_*.py
& "C:\ProgramData\anaconda3\python.exe" -m pytest workflows/tests/ -v --ignore=workflows/tests/visual/

# Smoke tests (need Blender on PATH)
blender --background --python openmap_blender_tools/tests/smoke_terrain.py

# Visual regression tests (need full pipeline output)
& "C:\ProgramData\anaconda3\python.exe" -m pytest workflows/tests/visual/ -v
```

## Submodule Workflow

Both `openmap_blender_tools` and `OpenMap_Unifier` are git submodules. When changing code inside them:
1. Commit inside the submodule first, push it.
2. Then `git add <submodule>` in parent and commit as `chore: bump <submodule> (<what changed>)`.
3. Push parent.

## Code Style

- Python 3.10+. Type hints on public functions.
- `from __future__ import annotations` in every module.
- No comments unless explaining a non-obvious WHY (hidden constraint, workaround, gotcha).
- Match existing style in whatever file you're editing. Don't reformat adjacent code.
- Blender operators: follow existing pattern in `operators.py` (bl_idname, bl_label, props, execute, invoke).

## Testing Rules

- Write failing test first, then implement. Run tests after writing them.
- Never modify existing tests to make them pass. Fix the implementation.
- Tests marked `@pytest.mark.needs_gdal` need GDAL on PATH (skipped by default).
- Smoke tests (`smoke_*.py`) need Blender — run only when specifically testing Blender integration.
- Visual tests use golden PNGs + JSON vision checks. Use `/review-renders` skill for visual verification.

## GDAL Gotchas

- Vendored GDAL lives in `vendor/gdal-win64/bin/`. Code auto-discovers it.
- Set `PROJ_LIB` and `GDAL_DATA` when using vendored binaries (handled in `geo_import.py`).
- NoData value is `-9999` for DGM tiles. Always pass `-srcnodata -9999` to gdalbuildvrt.
- Coordinate axis ordering: EPSG:25832 is Easting/Northing (X/Y), not lat/lon.
- VRT files use relative paths to source datasets. Don't move VRTs after creation.

## Blender Gotchas

- Displacement modifier samples heightmap via UV coords. Subdivision level must match data resolution or you get Z-fighting.
- Auto-subdivision (default=0) calculates optimal level from `scene_size / pixel_resolution`.
- UDIM tiles: max 10 columns (Blender limitation). Rows unlimited.
- Image colorspace for heightmaps: "Non-Color". For ortho: "sRGB".
- Interpolation for heightmap texture: "Cubic" (prevents staircase artifacts).
- Always set `image.source = "TILED"` before registering UDIM tiles.
- After modifying objects, call `context.view_layer.update()` if downstream code depends on evaluated state.

## Common Mistakes to Avoid

- Don't create new files when you can edit existing ones.
- Don't add error handling for scenarios that can't happen inside the pipeline.
- Don't build abstractions for single-use code. Three similar lines > premature helper function.
- Don't add features beyond what was asked. A bug fix is just a bug fix.
- Don't touch imports, formatting, or naming in code adjacent to your changes.
- When removing code, also remove any imports/variables that YOUR removal made unused.
- Don't commit `.env`, credentials, or large binary files.

## Verification Before Completion

Before claiming work is done:
1. Run the relevant test suite and confirm it passes.
2. If you changed Blender operators, verify the operator registers without errors.
3. If you changed GDAL processing, test with at least one real GeoTIFF (or the pure-Python unit tests).
4. Check `git diff` to confirm every changed line traces to the task.

## Context Management

- Use subagents for codebase exploration (more than 3 files to read).
- `/clear` between unrelated tasks.
- After 2 failed correction attempts on the same issue, `/clear` and restart with a better prompt.
- When compacting, preserve: modified file list, test commands + output, architectural decisions.
