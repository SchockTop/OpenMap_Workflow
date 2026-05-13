# TODO — Open work across the OpenMap_Workflow ecosystem

Audit date: 2026-05-06. Covers the umbrella repo and both submodules
(`OpenMap_Unifier`, `openmap_blender_tools`). Each item lists where the work
lives, status, and next action.

> ## ⚠️ Environment note for future Claude / agent sessions
>
> **The Claude Code sandbox cannot reach Bayern OpenData endpoints.**
> Probed 2026-05-06: every request to `download1.bayernwolke.de`,
> `download2.bayernwolke.de`, and `geodaten.bayern.de` returns
> **HTTP 403 *host_not_allowed*** — sandbox firewall, not Bayern.
>
> Practical implications:
> - You cannot live-test the DGM1 / DGM5 / LoD2 download URL patterns.
> - You cannot verify which fix (incremental in main vs. per-dataset
>   refactor on the unmerged fix branch) actually resolves the 404s.
> - Don't waste time re-probing endpoints from this sandbox — the result
>   is deterministic.
> - Pipeline development should go through the offline / `--skip-download`
>   path and the synthetic-data harness in `workflows/_headless_*.py`.
>
> Live verification has to happen on a host with egress to Bayern
> (e.g. the user's laptop or a CI runner with appropriate allowlist).
> The same applies to the synthetic-Blender harness if you need a real
> Blender executable: this sandbox has only pip-installed `bpy`.

## Legend

- ✅ ready to merge — clean, tested, no blockers
- ⚠️ ready but needs review — clean code, but I couldn't run it end-to-end
- 🔴 blocked — needs human decision or external resources I don't have

---

## Umbrella repo (`schocktop/openmap_workflow`)

### Branches with unmerged work

| Branch | Commits ahead of `main` | Status | Action |
|---|---|---|---|
| `claude/import-existing-maps-jfhiJ` | 2 (`47de505`, `c79908a`) | ✅ | Offline / proxy mode (`--skip-download`, `--local-*`, `--bbox-utm32n`) + 15 unit tests + fool-proof README. All 27 tests pass. |
| `claude/test-ground-layer-detection-lRpol` | 3 (`cc9ec3d`, `5a1c602`, `3382ffa`) | ⚠️ | Synthetic-data harness + blind ground detector confirming the ortho-drape *function* works in isolation — proves the bug in `01_poster.png` is upstream of `apply_ortho_drape`. README adds a "Ground-layer audit" section explaining the finding. The harness can't be run in the sandbox (no Blender executable, no Bayern egress). |

### Open PRs / Issues

None. (`gh` MCP scope: `schocktop/openmap_workflow`.)

### Code-level TODOs

None in `workflows/` or `README.md`.

### Carry-over open question (from ground-layer branch)

After merging the ground-layer branch, this remains unresolved:
- **Ortho drape doesn't reach the showcase renders.**
  Frame `02_ortho_drape.png` from the synthetic harness scores
  `GROUND_VISIBLE`, but `showcase/01_poster.png` does not. The bug is one
  of: (a) DOP tiles weren't downloaded, (b) `--ortho-dir` wasn't passed
  to `_blender_assemble_full.py`, (c) tiles saved with a filename other
  than `ortho.<udim>.jpg`. Open the `.blend` and run
  `workflows/test_ortho_drape_present.py` to confirm.

---

## Submodule: `SchockTop/OpenMap_Unifier`

Umbrella pin: **`d63fbec`** (= upstream `main` tip — fully synced as of
2026-05-06, after PR #14 landed the mirror-fallback fix).

### Branches with unmerged work

| Branch | Ahead of main | Behind main | Status | Notes |
|---|---|---|---|---|
| `claude/fix-dgm5-lod2-downloads-wcK4n` | 2 (`33957da`, `b0763cb`) | 14 | 🔴 | The bigger per-dataset URL-layout refactor + `backend.api` + `probe_dataset`. Conflicts with main on `backend/downloader.py`. The most-needed piece (mirror fallback) was extracted into PR #14 and merged separately, so this branch is now lower priority. Decide: extract the rest piecemeal, or rebase the whole thing. |
| `claude/implement-todo-tasks-A1VWq` | 0 | 0 | — | Was the source of PR #14. Fully merged via `d63fbec`. Safe to delete. |
| `claude/fix-height-data-download-37edF` | 0 | 4 | — | Fully merged into main. Safe to delete. |
| `claude/fix-osm-406-error-ahHPh` | 0 | 19 | — | Fully merged. Safe to delete. |
| `claude/add-file-format-selection-hnXRe` | 0 | 44 | — | Fully merged. Safe to delete. |
| `auto/proxy-bayern-ux-rework` | 0 | 33 | — | Fully merged. Safe to delete. |

### The 404 problem — current state

PR #14 (`d63fbec`) landed:
- `5ac2f7e` — fix(proxy): persist host/username/auth across mode switches
- `e5071e0` — fix(bayern): mirror fallback so height tiles survive a flaky mirror

The mirror-fallback piece was the single most useful chunk of the stranded
fix branch. With it in main, a single dead Bayern mirror no longer kills
a download batch. The umbrella's `README.md` "Known issues" has been
updated to reflect this.

### The 404 problem — current state

`README.md` of the umbrella repo still warns:

> **DGM1 + LoD2 endpoints return HTTP 404** from `download1.bayernwolke.de`
> for the URL pattern OpenMap_Unifier's `generate_1km_grid_files` produces

Main of `OpenMap_Unifier` *does* contain partial fixes (`c7842ac` strips the
`32` UTM prefix; `e69c935` reworks LoD2 url_path/ext/grid/prefix). The user
report ("I still get 404 for DGM downloads") suggests these aren't enough.

**Stranded value on the fix branch (`33957da`, `b0763cb`):**
1. Per-dataset URL layout (`url_path` field replaces `url_key + url_subpath`).
2. **Mirror-fallback loop** in `download_file` — falls through alternate
   `<url>` mirrors on 4xx/5xx so a single dead mirror doesn't kill a batch.
   *Main does not have this.*
3. `backend/api.py`: programmatic + CLI surface
   (`list_datasets`, `tile_url`, `urls_for_polygon`, `probe_dataset`,
   `download_tiles`).
4. `probe_dataset`: HEAD-sweeps candidate URL paths to auto-pin layouts when
   Bayern surprises us again.
5. pytest suite + CI.

### Action — 🔴 blocked

Resolving the conflict requires:
- Network access to `download1.bayernwolke.de` to verify which strategy
  actually works (main's incremental fixes vs. the fix branch's refactor).
  **Confirmed unavailable from this sandbox** (2026-05-06 probe): every
  Bayern endpoint returns HTTP 403 *host_not_allowed*, including the bare
  hosts `download1.bayernwolke.de`, `download2.bayernwolke.de`, and
  `geodaten.bayern.de`. This is a sandbox firewall policy, not Bayern
  rejecting our requests — the same probe will succeed from any
  unrestricted host.
- Push access to `SchockTop/OpenMap_Unifier` (this session's GitHub MCP
  scope is `schocktop/openmap_workflow` only).

**Recommended path** (needs you):
1. In `SchockTop/OpenMap_Unifier`, rebase
   `claude/fix-dgm5-lod2-downloads-wcK4n` onto `main`, resolving the
   `backend/downloader.py` conflict in favour of the fix branch's
   per-dataset `url_path` + mirror fallback (the more general solution).
2. Run the pytest suite the fix branch added.
3. Run a live download against a known-good DOP20 tile and a previously-
   404ing DGM1 / LoD2 tile to confirm.
4. Merge to main and tag.
5. Bump the umbrella's submodule pointer to the new main tip and update
   the umbrella's README "Known issues" to remove the 404 warning.

**Tactical workaround until the above happens:** the umbrella now ships
`--skip-download` mode (this branch). Users behind a proxy or hitting
404s can fetch tiles manually from the LDBV portal and feed them in via
`--local-dgm` / `--local-dop` / `--local-lod2`.

---

## Submodule: `SchockTop/openmap_blender_tools`

Umbrella pin: `8536fbff` (= upstream `main` tip — fully synced). No unmerged
branches.

### Code-level TODOs

| File | Line | Item | Status |
|---|---|---|---|
| `hidden_geo_cull.py` | 120 | `cull_by_render_face_id_visibility` is a Phase-2 scaffold — currently returns `0` after computing camera positions. Full face-ID-based culling is not implemented. | ⚠️ deferred feature, not leftover. Document expected behaviour or remove from public API until implemented. |

---

## Summary of immediate work

1. ✅ Merge `claude/import-existing-maps-jfhiJ` into umbrella `main`.
2. ⚠️ Merge `claude/test-ground-layer-detection-lRpol` into umbrella `main`
   (clean code, but the open question it documents needs follow-up).
3. 🔴 OpenMap_Unifier 404 fix — blocked on cross-repo work + live testing.
4. ⚠️ Decide what to do with `cull_by_render_face_id_visibility` scaffold
   (out of scope for this umbrella merge).

---

## Cinematic scene — open requirements (added 2026-05-13)

Captured from a working session that built the `allgaeu-forggensee` fly-over scene
(`workflows/scenes/allgaeu-flyover/scene.blend` + `workflows/_assemble_allgaeu.py`).
These are the things to tackle so the **`openmap_blender_tools` extension button**
("Build Cinematic Scene from Folder") genuinely produces a good scene by itself, and so
the user can recreate/iterate without opening a dozen Python scripts.

### 🔴 1. Ground looks like a shiny smooth plain — no surface texture, specular sheen

Observed by the user in `scene.blend`: the terrain ground "is smooth … no material … it's
like a plain and it's shining back." The DOP ortho *should* be draped on it (the `OrthoDrape`
material), so this is one (or more) of:
- the ortho photo isn't actually showing on the terrain in the saved/packed `.blend`
  (viewport solid shading vs rendered; or the `ground_shader` feature re-clobbered `OrthoDrape`;
  or the UDIM image didn't pack/resolve) — **first check: render the scene, confirm the aerial
  photo is on the ground**;
- the material is too glossy — it's grass/fields/forest, should be **matte** (roughness ≈ 0.9–1.0,
  zero/near-zero specular, no highlight); right now it "shines back";
- the surface is geometrically flat between heightmap samples — macro relief from Displace, but
  no micro detail → reads as a smooth plain even with the photo on it. Needs a **detail normal /
  bump** from a procedural noise (scaled to grass/canopy size), a little roughness variation, and
  the forest-mask-driven darken+bump from the v5 work (verify it survived into `scene.blend`).
- Related: the long-standing carry-over above ("Ortho drape doesn't reach the showcase renders").

**Requirement:** the terrain in a built scene must show the real aerial photo, matte, with subtle
surface micro-detail (and noticeably darker/bumpier canopy on forest pixels) — not a glossy slab.

### 2. Fold the assemble-script fixes into `BLENDERTOOLS_OT_build_cinematic_scene`

The scene that actually works was built by `workflows/_assemble_allgaeu.py`, which calls the
individual ops *plus* a stack of fixes the operator doesn't have yet: `<UDIM>`-token ortho load,
heightmap NoData-clamp (use a `heightmap_clean.tif`), camera keyframes that look at the mountains
(not near-nadir / not past the flat terrain edge), the `clouds.py` noise-Scale workaround, the
forest-via-ortho overlay, the −1.5 EV / AgX exposure, the procedural backdrop ridge. Until those
are in the operator, **the recommended path for recreating/iterating is: open `scene.blend` and
tweak — not rebuild from scratch via the button.** Folding them in is the main "make it usable" task.

### 3. `features/clouds.py` noise-scale bug — fix at source

The cloud volume's density noise is wired to the box's *Object* texture coords assuming a 1×1×1
box, but the box mesh bakes in the box dimensions (~14 km) → noise runs at thousands of cycles =
invisible clouds. Currently worked around in the assemble script. Fix in `clouds.py`: use
*Generated* coords (or normalize Object coords by the box extent) + sanity-check the vertical
falloff. Also: default density was too high (renders as a dark overcast band, not bright broken
puffs) — drop to ~0.05–0.07, optionally a touch of Emission Strength.

### 4. Map-prep step for the proxy A→B workflow

The GDAL preprocessing (DGM tiles → `heightmap.tif`, DOP tiles → UDIM, GML → `buildings.cityjson`,
OSM land-use → `forest_mask.tif`) currently happens inside the Blender import ops or via
`full_pipeline.py`. The user downloads on machine A (OpenMap_Unifier GUI, behind a proxy) and
builds in Blender on machine B. Make the preprocessing a discrete step that runs on machine A
(it has the repo + vendored GDAL), so only `data/processed/` needs copying to B. Options: expose
"produce Blender-ready folder" in the OpenMap_Unifier GUI after a download, or a small
`workflows/preprocess.py`. `build_cinematic_scene` already accepts a processed folder.

### 5. "Diagnose scene" operator

A button that checks the recurring failure modes and reports: camera below terrain, ortho not
loaded/visible, NoData in the heightmap, clouds noise scale wrong, void wedges where a camera
looks past the finite terrain edge, missing forest mask. Would have saved hours this session.

### 6. Camera presets that frame the subject + terrain-edge handling

Generic generated paths don't aim at anything; for a region with mountains/a lake/a castle on one
side the camera should look there (a "look-at point" option, or per-region camera hints). Also: a
forward-looking camera that looks past the AOI's flat terrain edge sees the mesh underside / black
world — handle it properly (extend the plane a little, add a downward skirt, clip the mesh to the
AOI polygon, or a built-in far backdrop) instead of the current procedural-ridge kludge.

### 7. (nice-to-have) Tree LOD

From >~1.5 km altitude the 3D trees render as noise specks, so forest is currently carried by the
ortho overlay and the GN 3D-tree scatter is render-hidden. A LOD scheme — 3D trees only near the
camera, ortho-canopy beyond — would let one scene work for both high fly-overs and low passes.

### What new functionality already landed (for reference)

- `features/clouds.py` + **"Add Clouds"** operator — procedural volumetric cumulus deck (+ cirrus).
- **`BLENDERTOOLS_OT_build_cinematic_scene`** — folder → terrain + ortho + ortho-textured buildings +
  forest-masked trees + clouds + sky + camera + quality; accepts raw tiles *or* a processed folder;
  options in the redo panel; reworked OpenMap N-panel (big button on top + per-step buttons).
- **Forest-masked tree scatter** (`features/trees.py` takes a `mask_geotiff`) + subtle leaf translucency.
- **OSM-forest → mask GeoTIFF** — `geo_import.rasterize_forest_mask` / `greenness_mask`.
- **Terrain-elevation-aware fly-over camera** — `camera_presets.py` places the camera at terrain Z-max
  + AGL instead of an absolute Z (so it works over mountains).
- **`<UDIM>`-token ortho load** — `terrain_setup.apply_ortho_drape` / `buildings_textured` (fixes the
  ortho not rendering in headless Cycles).
- Reference: `workflows/_assemble_allgaeu.py` (headless build), `allgaeu-forggensee` region preset,
  `workflows/scenes/allgaeu-flyover/` (the packed `scene.blend` + renders + README/MANIFEST).
