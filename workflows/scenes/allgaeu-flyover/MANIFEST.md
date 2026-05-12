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

**v5 limitation**: photo-real oblique aerials, but NO sky / mountains / horizon / clouds in any
frame (camera too downward-aimed; looking past the terrain edge showed mesh underside + void). → v6.

## v6 cinematic-framing pass

Changes vs v5 (`workflows/_assemble_allgaeu.py`) — goal: get **sky + a mountain horizon + broken
cumulus into frame** while keeping v5's exposure / ortho colours / forest-via-ortho / building roofs.

- **Backdrop ridge mesh** (`BackdropRidge`): the AOI terrain only reaches **1685 m** ASL (no in-AOI
  alpine peaks — the Säuling/Tegelberg are just outside the polygon). A procedural ridge plane is
  added ~11 km south of the AOI edge: a 37 km × 2.8 km grid displaced by two CLOUDS-noise textures to
  peaks ~2300–2900 m, base ~600 m, with a **pure-emission pale-haze-blue material** (no shading → only
  its jagged silhouette top edge reads against the sky, like distant mountains in atmospheric haze).
- **Aerial haze**: a faint (density 2.5e-6) low-level (z ≈ 250–2200 m) volume-scatter domain so
  distance fades and the ridge reads as far away. Kept thin/light to limit render cost & milkiness.
- **Cameras at altitude**: 4 look-at keyframes, cameras at **1900–2400 m** (above the AOI top), aimed
  at the AOI midground (Y ≈ −3000, Z ≈ 1200) → pitch ≈ 5–8° below horizontal → foreground lake/meadows
  in the lower half, the forested AOI hill as the midground subject, the distant ridge on the horizon,
  Nishita sky in the top ~25–30%. Lenses 28–35 mm, slight banks. Rotation via `Vector.to_track_quat` +
  a roll matrix (robust vs guessing Euler angles).
- **World Background Strength 0.30** (was 0.15) — the Nishita sky is now visible above the horizon
  without blowing the ground (kept tame by the −1.5 EV view transform).
- **Clouds**: deck 2050–2800 m (cameras just below it), coverage 0.45, density 0.16; cirrus at 6500 m.
  Plus a **`clouds.py` workaround** in the assemble script: `features/clouds.py` wires the noise to
  "Object" texcoords assuming a 1×1×1 box, but `_make_cloud_box` applies the box scale (≈14 km) → the
  noise ran at ~17500 cycles = incoherent per-voxel noise = invisible clouds; the script rescales the
  cumulus Noise `Scale` inputs by 1/(mesh half-extent) to restore "N blobs across the box".
- **Blank UDIM corner tiles fixed**: the AOI polygon is a rotated diamond, so ~24 corner tiles of the
  bbox-aligned DOP40 UDIM grid are empty (pure black) → rendered as a gray slab where the camera caught
  a bbox corner. Those tiles in `data/processed/ortho_udim/` were overwritten with a mottled
  dark-forest-green fill so corners read as forest.
- Render time: ~5 min/frame on RTX 4070 OptiX (volume haze + clouds). 4 frames ≈ 20 min.
  `scene.blend` packed ≈ 250 MB (not in git).
- **Honest status**: v6 frames have sky, a distant-ridge horizon and (depending on the noise pattern)
  some cumulus; v5's exposure + ortho colours + forest read are kept. But the look is "hazy pre-alpine
  aerial" — the backdrop ridge is a procedural stand-in (not real DGM), the haze can read milky in the
  high establishing shot, and a true sharp mountain-wall would need real alpine DGM south of the
  polygon. Shippable as a hero fly-over still; not flawless.

## How this scene was assembled (v6)

`blender --background --python workflows/_assemble_allgaeu.py` → imports `heightmap_clean.tif`,
DOP40 UDIM ortho (110 tiles via `<UDIM>` token; blank corner tiles backfilled), 7979 LoD2 buildings +
ortho-textured roofs, forest-masked GN trees (render-hidden for flyover), forest overlay on the
terrain material, a procedural distant **backdrop ridge** mesh ~11 km south, a faint **aerial-haze**
volume, a broken **cumulus** deck (2050–2800 m) + **cirrus** (6500 m), Nishita sky (sun 50° el /
150° az, energy 2.0 W, World strength 0.30, −1.5 EV AgX Med-High Contrast), and **4 look-at camera
keyframes** at 1900–2400 m (pitch ~5–8° down, aimed at the AOI midground + the ridge horizon). Saves
`data/scene_allgaeu-forggensee.blend`, renders 4 stills → `renders/allgaeu_v6_frame{0001,0060,0120,0180}.png`.
Final deliverable: `bpy.ops.file.make_paths_absolute()` + `bpy.ops.file.pack_all()` →
`workflows/scenes/allgaeu-flyover/scene.blend` (≈ 250 MB, not in git).
</content>

---

## FINAL STATE (2026-05-13) — see README.md for the up-to-date hand-off

- Parent repo `OpenMap_Workflow` @ `8698ec4` (pushed): region preset, plan, `workflows/_assemble_allgaeu.py` (v6), `workflows/_diag_v6*.py`, this folder's README/MANIFEST + `renders/allgaeu_v{3,4,5,6}_frame*.png`. (`workflows/_blender_assemble_full.py` rot was fixed in earlier commits — `setup_sky` removed, engine `BLENDER_EEVEE`, `--enable clouds`.)
- Submodule `openmap_blender_tools` @ `8a70ad5` (pushed; parent pointer matches): clouds feature (`0260824`), tree forest-mask + leaf translucency (`5a6841b`), OSM-forest→mask rasterizer (`a73ec8e`), `build_cinematic_scene` operator + N-panel (`3440a98`), camera-elevation fix (`9d56574`), `<UDIM>`-token ortho fix (`8a70ad5`). Rebuilt `dist/blender_tools-0.1.0.zip`.
- `scene.blend` (~270 MB, packed, **not in git**) is in this folder.
- Status: data pipeline + tooling complete; renders are photo-real aerial (v5) + cinematic-framed (v6) but v6 still has grey edge-void wedges, a too-spiky mountain backdrop, no clouds in frame, and a flat sky — finishing those is camera/lighting/backdrop tuning in `_assemble_allgaeu.py`, no pipeline changes. See README "State after v6 — and how to finish it".
