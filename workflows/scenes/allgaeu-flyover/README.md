# Allgäu fly-over scene

Self-contained cinematic scene for the ~45 km² Allgäu polygon around Forggensee /
Schwangau / Füssen (the Schwangau castles, pre-alpine lakes and forest).

> **Status:** v4 cinematic look pass complete. `scene.blend` (258 MB, packed) is in this folder
> but **not tracked in git** (>100 MB). See `MANIFEST.md` for regeneration instructions.
> This folder is meant to be liftable on its own; `MANIFEST.md` lists every commit/file
> added across the repo for this scene + the Blender-tool changes it depends on.

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
