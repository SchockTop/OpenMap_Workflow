# TODO — Open work across the OpenMap_Workflow ecosystem

Audit date: 2026-05-06. Covers the umbrella repo and both submodules
(`OpenMap_Unifier`, `openmap_blender_tools`). Each item lists where the work
lives, status, and next action.

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

Umbrella pin: `e69c9353` (= upstream `main` tip — fully synced).

### Branches with unmerged work

| Branch | Ahead of main | Behind main | Status | Notes |
|---|---|---|---|---|
| `claude/fix-dgm5-lod2-downloads-wcK4n` | 2 (`33957da`, `b0763cb`) | 12 | 🔴 | The DGM5 / LoD2 / DGM1 404 fix. Conflicts with main on `backend/downloader.py` — both sides refactored the same file independently. Main went incremental (6 small fixes); this branch went big (per-dataset URL layout + mirror fallback + `backend.api` + pytest + CI). |
| `claude/fix-height-data-download-37edF` | 0 | 2 | — | Fully merged into main. Safe to delete. |
| `claude/fix-osm-406-error-ahHPh` | 0 | 17 | — | Fully merged. Safe to delete. |
| `claude/add-file-format-selection-hnXRe` | 0 | 42 | — | Fully merged. Safe to delete. |
| `auto/proxy-bayern-ux-rework` | 0 | 31 | — | Fully merged. Safe to delete. |

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
