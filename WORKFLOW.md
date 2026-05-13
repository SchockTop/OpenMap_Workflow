# Workflow — building a cinematic scene from behind a proxy

Two roles (can be the same machine):
- **Download box** — has internet (through your proxy); runs OpenMap_Unifier and/or the repo's GDAL.
- **Blender box** — has Blender 5.1; builds & renders the scene. Doesn't need internet or GDAL.

## 0. One-time setup

**Download box** — clone with submodules:
```
git clone --recurse-submodules https://github.com/SchockTop/OpenMap_Workflow.git
cd OpenMap_Workflow
git lfs pull          # pulls the packed example scene (workflows/scenes/allgaeu-flyover/scene.blend, ~273 MB)
```
OpenMap_Unifier is proxy-aware: launch `OpenMap_Unifier\run.bat`, open **Proxy Settings**, hit
**Auto-detect** (or enter the proxy URL / Basic/NTLM creds / CA bundle), **Test Connections**. Both the
tile downloads *and* the DOM-Mesh cutout go through that session.

**Blender box** — install the extension once:
```
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" --command extension install-file ^
  <repo>\openmap_blender_tools\dist\blender_tools-0.1.0.zip --repo user_default --enable
```
(If you just want the finished example scene, copy `workflows/scenes/allgaeu-flyover/scene.blend` over and open it — done. The rest of this is for a *new* area.)

## 1. Download (on the download box)

Easiest — let the umbrella pipeline download **and** GDAL-preprocess in one go:
```
# add your polygon to workflows/region_presets.py (a named WGS84 WKT) — or use --bbox-utm32n
python workflows/full_pipeline.py --region <yourname> --datasets dgm1 dop40 lod2
```
This downloads DGM1 + DOP40 + LoD2 (~GBs — DOP40 dominates) and writes:
- `data/processed/heightmap.tif` — DGM1 mosaic
- `data/processed/ortho_udim/ortho.<udim>.jpg` — DOP40 UDIM tiles
- `data/processed/buildings.cityjson` — LoD2 buildings
- `data/flight_path.csv` — a synthetic camera path

Also grab the **forest mask** (so trees land on actual forest, not lakes/town): download the OSM
land-use layer for the same polygon via the OpenMap_Unifier GUI → `downloads_osm/land_use.geojson`,
then (on the box with `vendor/gdal-win64/`):
```
python -c "from openmap_blender_tools.geo_import import rasterize_forest_mask; rasterize_forest_mask('downloads_osm/land_use.geojson','data/processed/heightmap.tif','data/processed/forest_mask.tif')"
```
> Behind the proxy and `full_pipeline.py`'s downloader can't get out? Use OpenMap_Unifier's GUI to
> download the DGM1/DOP40/LoD2 tiles (it routes through your configured proxy), drop them in
> `data/raw/{dgm1,dop40,lod2}/`, then run `full_pipeline.py --skip-download --local-dgm data/raw/dgm1 --local-dop data/raw/dop40 --local-lod2 data/raw/lod2 --region <yourname>` (still does the GDAL step).

## 2. Carry the processed data to the Blender box

Copy this folder over (that's all the Blender box needs):
```
data/processed/        (heightmap.tif, ortho_udim/, buildings.cityjson, forest_mask.tif)
data/flight_path.csv
```

## 3. Build the scene (on the Blender box)

Blender ▸ 3D viewport ▸ press **N** ▸ **OpenMap** tab ▸ **Build Cinematic Scene from Folder** ▸
point it at the `processed/` folder ▸ set the options in the operator panel (sky time-of-day,
camera preset, quality, **clouds** + coverage, trees, building-textures, groundcover) ▸ OK.
Below that button are the individual steps if you want to run them one at a time (Import Heightmap /
Ortho / Buildings, Texture Buildings, Scatter Trees, **Add Clouds**, Apply Sky / Camera / Quality,
Render Preview).

> **Heads-up:** the one-button operator is new and doesn't yet contain every fix the example
> Allgäu scene needed (heightmap NoData clamp, the `<UDIM>` ortho load, mountain-aware camera
> framing, the clouds noise-scale fix, the forest-via-ortho ground overlay, exposure) — see
> `TODO.md` ▸ "Cinematic scene — open requirements" items #1–#3. Until those are folded in, the
> reliable path for a polished result is: open `workflows/scenes/allgaeu-flyover/scene.blend`
> (already built, all data packed) and adapt it, using `workflows/_assemble_allgaeu.py` as the
> reference for how the example was assembled.

## 4. Headless alternative

No GUI clicking — rebuild the example scene (or adapt the script for your folder):
```
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" --background --python workflows\_assemble_allgaeu.py
```
Then in Blender: File ▸ External Data ▸ Make All Paths Absolute, then Pack All Resources ▸ Save As.

## 5. Render & finish

Set a frame on the camera path, render (Cycles / OptiX, 128 spp + OpenImageDenoise, 1920×1080 is what
the example uses). Tuning knobs (cloud density/altitude, camera pitch/altitude, exposure, the backdrop
ridge) are listed in `workflows/scenes/allgaeu-flyover/README.md` ▸ "State after v6 — and how to finish it".

---
**TL;DR:** download box → `full_pipeline.py --region X` (uses your proxy) → copy `data/processed/` to the
Blender box → **Build Cinematic Scene from Folder** in the N-panel (or `_assemble_allgaeu.py` headless) →
render. For a guaranteed-good result *today*, just `git lfs pull` and open the bundled `scene.blend`.
</content>
