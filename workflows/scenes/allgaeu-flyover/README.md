# Allgäu fly-over scene

Self-contained cinematic scene for the ~45 km² Allgäu polygon around Forggensee /
Schwangau / Füssen (the Schwangau castles, pre-alpine lakes and forest).

> **Status:** v6 cinematic-framing pass complete. `scene.blend` (~250 MB, packed) is in this folder
> but **not tracked in git** (>100 MB). See `MANIFEST.md` for regeneration instructions.
> This folder is meant to be liftable on its own; `MANIFEST.md` lists every commit/file
> added across the repo for this scene + the Blender-tool changes it depends on.
>
> v6 (`renders/allgaeu_v6_frame####.png`): cameras at 1900–2400 m looking ~5–8° below horizontal
> south → foreground lake/meadows → forested AOI hill → a distant procedural mountain-ridge backdrop
> on the horizon → broken cumulus + Nishita sky in the top ~25–30%. Keeps v5's tamed exposure
> (−1.5 EV AgX), natural DOP ortho colours, forest-via-ortho overlay, and ortho-textured building roofs.
> ~5 min/frame on an RTX 4070 (volume haze + clouds). The AOI's own terrain tops out at 1685 m
> (no real alpine peaks inside the polygon) so the horizon ridge is a hazy procedural stand-in.

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

**Still rough in the v6 renders (the remaining polish):**
1. **Cumulus reads as a dark band, not bright broken puffs** — density 0.16 makes the volume too opaque
   and the low −1.5 EV exposure + AgX leaves it grey. Drop the cloud `density` (~0.05–0.07), maybe add a
   touch of `Emission Strength` to the Principled Volume, and consider raising the deck so it spreads
   across the sky rather than squashing into a horizon band. (Tune in `_do_clouds` in the assemble script
   + the `clouds.py` noise-Scale workaround there.)
2. **Backdrop ridge can read flat / a dark "gap band" sits between the AOI edge and the ridge** — the
   8.7 km gap south of the AOI renders as the Nishita-sky horizon (a desaturated band). Push the ridge
   closer, or add a wider faded distance plane to fill the gap, or use real alpine DGM south of the polygon.
3. **High establishing frame (0180) is a bit milky** — the aerial-haze volume over a long sightline. Drop
   `density` further or stratify it (denser near the ground only).
4. **A faint gray/tan corner slab can still appear** — the AOI polygon is a rotated diamond so the
   bbox-aligned terrain plane has corners with no DOP coverage. v6 backfilled the empty UDIM tiles with a
   mottled dark green; any *truly uncovered* UV (outside the [0,10]×[0,11] tile grid) still shows the
   image fallback. Cleanest fix: clip the terrain mesh to the AOI polygon.
5. **Warmer time of day** — a golden-hour Nishita + sun + exposure could give the sky a gradient/warmth
   without re-blowing the ortho.
All of these are camera/lighting/backdrop tuning in `workflows/_assemble_allgaeu.py` (and the cloud
kwargs) — no pipeline changes needed. The scene is built; this is finishing the shot.

**Regenerate the packed scene.blend:** `blender --background --python workflows/_assemble_allgaeu.py` → open `data/scene_allgaeu-forggensee.blend` in Blender → File ▸ External Data ▸ Make All Paths Absolute, then Pack All Resources → Save As `workflows/scenes/allgaeu-flyover/scene.blend`.
