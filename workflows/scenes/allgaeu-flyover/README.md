# Allgäu fly-over scene

Self-contained cinematic scene for the ~45 km² Allgäu polygon around Forggensee /
Schwangau / Füssen (the Schwangau castles, pre-alpine lakes and forest).

> **Status:** v5 look pass complete. `scene.blend` (259 MB, packed) is in this folder
> but **not tracked in git** (>100 MB). See `MANIFEST.md` for regeneration instructions.
> This folder is meant to be liftable on its own; `MANIFEST.md` lists every commit/file
> added across the repo for this scene + the Blender-tool changes it depends on.
>
> v5 fixes from v4: **exposure corrected** (no more white-wash), **3D trees hidden** (no noise
> specks — forest reads via DOP ortho + forest overlay), **hand-placed camera keyframes** 
> (1600–2400m, 48–52° off nadir), **clouds** at 2100m base. 4 frames at 85–100s each (RTX 4070).

## Contents (when complete)

- `scene.blend` — the assembled scene with all data **packed** (`bpy.ops.file.pack_all()`):
  displaced DGM1 terrain, DOP40 ortho drape, LoD2 buildings textured from the ortho,
  forest-masked 3D trees, volumetric clouds, a cinematic fly-over camera.
- `renders/` — hero stills.
- `MANIFEST.md` — provenance: datasets, the polygon (WKT + KML), the exact commits/files
  that make up this scene + the `openmap_blender_tools` changes (clouds feature, the
  "build cinematic scene from folder" operator, tree forest-masking) it relies on.
- `region.wkt` / `region.kml` — the AOI.

## How it was built (summary)

1. AOI added as the `allgaeu-forggensee` preset in `workflows/region_presets.py`.
2. `python workflows/full_pipeline.py --region allgaeu-forggensee --datasets dgm1 dop40 lod2 ...`
   (download Bayern DGM1 + DOP40 + LoD2 → GDAL mosaic → `data/processed/`).
3. Blender assembly with `--enable buildings-textured trees clouds` (the improved
   `openmap_blender_tools` extension — see its repo / `MANIFEST.md`).
4. Render → independent review → iterate → `pack_all()` → saved here.

## Re-render

Open `scene.blend` in Blender 5.1 (with the `openmap_blender_tools` extension installed),
pick a frame on the camera path, render. Settings: Cycles / OptiX, 128 spp + OIDN, 1920×1080.

`scene.blend` is 258 MB (all data packed). To regenerate from processed data:
```
blender --background --python workflows/_assemble_allgaeu.py
```
Then pack via File → External Data → Pack All Resources.
</content>

---

## State after v6 (2026-05-12) — and how to finish it

**What's done & solid (committed + pushed):**
- Full data pipeline for the AOI: DGM1 terrain (nodata-water fixed), DOP40 ortho (renders on the ground via the `<UDIM>` token fix), 7,979 LoD2 buildings with ortho-projected roof colour, OSM forest mask, forest rendered as a canopy overlay on the ortho (the GN 3D trees are in the .blend but hidden from render — re-enable them for low passes), a procedural volumetric cumulus cloud deck, a Nishita sky, a terrain-elevation-aware camera, and a far-distance mountain-ridge backdrop.
- `workflows/_assemble_allgaeu.py` rebuilds the whole scene headless from `data/processed/` (`heightmap_clean.tif`, `ortho_udim/`, `buildings.cityjson`, `forest_mask.tif`) — `blender --background --python workflows/_assemble_allgaeu.py`.
- `scene.blend` (~270 MB, packed, **not in git** — GitHub 100 MB limit) is in this folder: terrain + ortho + buildings + forest + clouds + camera, all data baked in.
- Renders: `renders/allgaeu_v5_frame{0060,0180}.png` are photo-real oblique aerials (Forggensee, mudflat sandbar, meadows, hedgerows, forest canopy, red-roof buildings — exposure & colour right). `renders/allgaeu_v6_frame####.png` are the cinematic-framing pass — foreground terrain → Säuling/Tegelberg massif → valley with the lake & villages → an Alps ridge on the horizon → hazy sky.
- The `openmap_blender_tools` extension (rebuilt `dist/blender_tools-0.1.0.zip`): new `features/clouds.py` + `BLENDERTOOLS_OT_add_clouds`, forest-masked tree scatter + leaf translucency, OSM-forest→mask GeoTIFF rasterizer, the one-stop `BLENDERTOOLS_OT_build_cinematic_scene` operator + a reworked OpenMap N-panel (big "Build Cinematic Scene from Folder" button + per-step buttons), the `<UDIM>` ortho fix, the camera-elevation fix. Install on the Blender machine: `blender --command extension install-file dist/blender_tools-0.1.0.zip --repo user_default --enable`.

**Still rough in the v6 renders (the remaining work):**
1. **Grey "void wedges"** at the bottom corners of some v6 frames — the AOI terrain mesh is a finite 9×10 km plane, so when the camera looks past its flat edge it sees the mesh underside / black world. Fix: either (a) extend the far mountain-backdrop / a faded distance plane to cover behind the terrain edge, (b) add a downward "skirt" to the terrain plane edge, or (c) tighten the camera aim so the flat edges stay out of frame. Quick win: (a) or (c).
2. **Mountain backdrop ridge is too spiky/uniform** — make it lower-frequency, more varied, atmospheric-faded (more aerial perspective / desaturation with distance), or replace it with a real wider DGM mosaic of the Alps to the south.
3. **No clouds in the v6 frames** — the cumulus deck is in the scene but the v6 cameras don't catch it. Tune `features/clouds.py apply()` `base_altitude_m` / `coverage` so a broken deck sits between camera and peaks, and/or aim a frame upward through it.
4. **Sky is flat hazy grey-blue** — pick a warmer time of day (golden hour) and dial the Nishita/sun/exposure so the sky has some gradient and warmth without re-blowing the ortho.
These are all camera/lighting/backdrop tuning in `workflows/_assemble_allgaeu.py` (and the cloud kwargs) — no pipeline changes needed. The scene is built; this is finishing the shot.

**Regenerate the packed scene.blend:** `blender --background --python workflows/_assemble_allgaeu.py` → open `data/scene_allgaeu-forggensee.blend` in Blender → File ▸ External Data ▸ Make All Paths Absolute, then Pack All Resources → Save As `workflows/scenes/allgaeu-flyover/scene.blend`.
