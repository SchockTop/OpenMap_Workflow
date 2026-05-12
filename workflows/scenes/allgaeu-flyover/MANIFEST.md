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

## How this scene was assembled

`blender --background --python workflows/_assemble_allgaeu.py` → calls
`bpy.ops.blender_tools.build_cinematic_scene` on `data/processed/` (sky preset, camera
preset = cinematic-establishing, quality = preview, clouds on, forest-masked trees,
ortho-textured buildings) → saves `data/scene_allgaeu-forggensee.blend` → renders stills.
Final deliverable: `bpy.ops.file.pack_all()` on that blend → saved here as `scene.blend`.
</content>
