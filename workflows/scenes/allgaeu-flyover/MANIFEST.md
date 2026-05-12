# MANIFEST — Allgäu fly-over scene + the Blender-tool changes it depends on

Everything below was added/changed for this scene. Three git repos are involved:
the parent `OpenMap_Workflow`, and the `openmap_blender_tools` submodule (the Blender
extension). The `OpenMap_Unifier` submodule was **not** changed for this scene.

> If you want to split this out: take this whole `workflows/scenes/allgaeu-flyover/`
> folder + `scene.blend` (data is packed into it), and — if you want the tooling too —
> the `openmap_blender_tools` commits listed below. The scene `.blend` itself is
> self-contained once packed; the tooling commits are only needed to *re-generate* or
> tweak it, or to build other scenes.

## The AOI

- Region: `allgaeu-forggensee` — Forggensee / Schwangau / Füssen, Allgäu (~45 km²,
  bbox ≈ 9.2 × 10.4 km). EPSG:25832 bbox ≈ (628971, 5268342, 638194, 5278717).
- WKT (EPSG:4326): `POLYGON((10.7725730737063 47.55479949436138, 10.83963591262588 47.6342185213709, 10.78083675631973 47.64802419330076, 10.71481653953351 47.57153264515765, 10.75609510295647 47.5653219635225, 10.7725730737063 47.55479949436138))`
- Original Google-Earth KML supplied by the user (kept in this folder as `region.kml`).
- Datasets: Bayern **DGM1** (1 m terrain), **DOP40** (40 cm ortho), **LoD2** (3D buildings),
  + OSM land-use (forest mask). All © Bayerische Vermessungsverwaltung / OSM contributors.

## Source data (under `data/` in the repo — large, gitignored; regenerable)

- `data/raw/{dgm1,dop40,lod2}/` — 65 + 65 + 22 downloaded 1 km tiles (~2.2 GB).
- `data/processed/heightmap.tif` — Float32 DGM1 mosaic (9223×10376 px, 122 MB).
- `data/processed/ortho_udim/` — 110 UDIM JPG ortho tiles (10×11), from DOP40.
- `data/processed/buildings.cityjson` — LoD2 buildings (~10 MB).
- `data/processed/forest_mask.tif` — Float32 0/1 forest mask (from OSM land-use, 240 polys).
- `data/flight_path.csv` — 40-point cinematic fly-over camera path.
- `downloads_osm/land_use.geojson` — OSM land-use for the AOI (862 features).
- Regenerate everything: `python workflows/full_pipeline.py --region allgaeu-forggensee --datasets dgm1 dop40 lod2 ...` (preset added in `workflows/region_presets.py`).

## Commits — parent repo `OpenMap_Workflow`

- `region_presets.py`: added the `allgaeu-forggensee` preset.
- `docs/superpowers/plans/2026-05-12-allgaeu-flyover-and-blender-scene-tool.md`: the plan.
- `workflows/scenes/allgaeu-flyover/` (this folder): README, MANIFEST, region files, `scene.blend`, `renders/`.
- `workflows/_assemble_allgaeu.py`: headless Blender script that builds this scene from `data/processed/`.
- `workflows/_blender_assemble_full.py`: fixed bit-rot (`setup_sky` → `apply_sky_preset`, `BLENDER_EEVEE_NEXT` → `BLENDER_EEVEE`) + `--enable clouds`. *(pending — see plan step "fix `_blender_assemble_full.py`")*
- (commit SHAs filled in at delivery time)

## Commits — submodule `openmap_blender_tools` (all on `main`, pushed)

- `0260824` — `feat(clouds): procedural volumetric cloud layer (cumulus + optional cirrus)` — `features/clouds.py`, `BLENDERTOOLS_OT_add_clouds` in `operators.py`, `tests/smoke_clouds.py`, `tests/test_features_registry.py`.
- `5a6841b` — `feat(trees): forest-mask the scatter from a mask GeoTIFF + subtle leaf translucency` — `features/trees.py`, `tests/test_trees_feature.py`.
- `a73ec8e` — `feat(geo): rasterize OSM forest land-use to a density mask GeoTIFF` — `geo_import.py` (`rasterize_forest_mask`, `greenness_mask`, helpers), `tests/test_geo_import.py`.
- `3440a98` — `feat(scene): one-stop 'build cinematic scene from folder' operator` — `BLENDERTOOLS_OT_build_cinematic_scene` in `operators.py`, updated `scatter_trees` op, N-panel ("Build Cinematic Scene from Folder" button + per-step buttons).
- Rebuilt extension: `dist/blender_tools-0.1.0.zip` (install on the Blender machine via `blender --command extension install-file dist/blender_tools-0.1.0.zip --repo user_default --enable`).
- (later: any iteration commits for cloud/tree tuning)

## Packed scene.blend

`workflows/scenes/allgaeu-flyover/scene.blend` — all data packed (110 UDIM ortho tiles +
tree textures + building materials + heightmap). Size: ~258 MB.

**Not tracked in git** (GitHub 100 MB limit). Regenerate:
1. `blender --background --python workflows/_assemble_allgaeu.py`
2. Open `data/scene_allgaeu-forggensee.blend` in Blender
3. File → External Data → Make All Paths Absolute → Pack All Resources
4. Save As `workflows/scenes/allgaeu-flyover/scene.blend`

## v4 cinematic look pass

Changes vs v3 (see git history):
- Ortho UDIM fix, ground shader skip, camera pitch 50° off nadir, afternoon sky 5W sun, GPU rendering, 1920×1080.

## v5 cinematic look pass

Changes vs v4 (`workflows/_assemble_allgaeu.py`):

- **Exposure fixed**: sun energy 2.0 W (was 5.0), World Background Strength 0.15 (was 1.0),
  view exposure -1.5 EV (was +0.3), AgX "Medium High Contrast" look. DOP ortho now reads as
  a real aerial photo — visible greens, browns, teal water — not a white wash.
- **Trees hidden from render** (`show_render=False` on TreeScatter GN modifier): at 1600–2400 m
  AGL, decimated 3D trees render as dark noise specks. Forest reads via the DOP ortho + forest overlay.
- **Forest overlay on OrthoDrape material**: loads `forest_mask.tif` (Non-Color), darkens forest
  pixels by −32% (forests absorb more light) and adds a noise-bump perturbation (scale ~120 UV units)
  so canopy reads as textured mass rather than flat photo. GN trees still in the .blend for close-up use.
- **Camera**: 4 hand-placed keyframes (no FOLLOW_PATH), multi-altitude (1600–2400 m absolute),
  50mm lens, aimed toward the lake/valleys/southern foothills. Avoids the near-nadir look
  and the fly-path-edge problems of v4.
- **Clouds**: base 2100 m, thickness 400 m, coverage 0.40, cirrus at 6000 m.
- **UDIM seams**: less prominent at correct exposure. Not post-processed further.
- Render time: ~85–100 s/frame on RTX 4070 OptiX. 4 frames = ~380 s total.
- `scene.blend` packed: 259.4 MB (not in git).

**Notes on "cinematic" limitations**: The Allgäu AOI is 10 km × 9 km. With a planar terrain mesh,
any forward-looking composition aimed past the terrain boundary shows the mesh edge + black void.
The v5 frames are aerial-photography style (near-oblique, 48–52° off nadir), not "mountains on
horizon + sky" cinematic. Getting a true horizon shot would require extending the terrain to ≥30 km,
or adding a distant mountain backdrop mesh. This is noted as a future improvement.

## How this scene was assembled

`blender --background --python workflows/_assemble_allgaeu.py` → imports heightmap_clean.tif,
DOP40 UDIM ortho (110 tiles via `<UDIM>` token), 7979 LoD2 buildings, forest-masked GN trees
(render-hidden for flyover), forest overlay on terrain material, volumetric cumulus deck (2100m base),
Nishita sky (sun 50° el / 150° az, energy 2.0 W, World strength 0.15), 4 keyframe camera positions
at 1600–2400m absolute, saves `data/scene_allgaeu-forggensee.blend`, renders 4 stills.
Final deliverable: `bpy.ops.file.pack_all()` → `scene.blend` (259 MB, not in git).
</content>

---

## FINAL STATE (2026-05-13) — see README.md for the up-to-date hand-off

- Parent repo `OpenMap_Workflow` @ `8698ec4` (pushed): region preset, plan, `workflows/_assemble_allgaeu.py` (v6), `workflows/_diag_v6*.py`, this folder's README/MANIFEST + `renders/allgaeu_v{3,4,5,6}_frame*.png`. (`workflows/_blender_assemble_full.py` rot was fixed in earlier commits — `setup_sky` removed, engine `BLENDER_EEVEE`, `--enable clouds`.)
- Submodule `openmap_blender_tools` @ `8a70ad5` (pushed; parent pointer matches): clouds feature (`0260824`), tree forest-mask + leaf translucency (`5a6841b`), OSM-forest→mask rasterizer (`a73ec8e`), `build_cinematic_scene` operator + N-panel (`3440a98`), camera-elevation fix (`9d56574`), `<UDIM>`-token ortho fix (`8a70ad5`). Rebuilt `dist/blender_tools-0.1.0.zip`.
- `scene.blend` (~270 MB, packed, **not in git**) is in this folder.
- Status: data pipeline + tooling complete; renders are photo-real aerial (v5) + cinematic-framed (v6) but v6 still has grey edge-void wedges, a too-spiky mountain backdrop, no clouds in frame, and a flat sky — finishing those is camera/lighting/backdrop tuning in `_assemble_allgaeu.py`, no pipeline changes. See README "State after v6 — and how to finish it".
