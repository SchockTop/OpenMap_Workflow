# OpenMap_Workflow

Umbrella repo that wires Bayern OpenData (DGM/DOP/LoD2) through a Blender
pipeline for terrain + cinematic renders.

## Showcase

**Headline poster** — 1920×1080, golden-hour, München-Süd 4×2 km corridor with
27 730 LoD2 buildings + procedural trees + multi-layer ground shader:

![Headline Poster](showcase/01_poster.png)

**Sky envelope** — same scene at 6 named time-of-day presets
(noon · golden-hour · blue-hour · dawn · overcast · afternoon).
Inter-cell color distance: **290** (3× the spec threshold for "dramatic mood difference"):

![Sky Comparison](showcase/02_sky_comparison.png)

**Camera altitude envelope** — same scene through 6 named camera presets
(fpv-walk 1.7 m · fpv-bike 1.7 m · low-drone 80 m · mid-drone 500 m ·
cinematic-establishing 2 000 m · aircraft-approach 4 500 m).
Inter-cell color distance: **222**:

![Altitude Comparison](showcase/03_altitude_comparison.png)

**Per-feature isolation tests** — each plug-in feature renders an A/B against
a baseline cube building / plane to prove the feature applied:

| Buildings textured | Trees | Ground shader | Groundcover |
|:---:|:---:|:---:|:---:|
| ![Buildings](showcase/04_feature_buildings.png) | ![Trees](showcase/05_feature_trees.png) | ![Ground shader](showcase/06_feature_ground_shader.png) | ![Groundcover](showcase/07_feature_groundcover.png) |

---

## Architecture

Two submodules:

```
OpenMap_Workflow/
  OpenMap_Unifier/        (submodule — Bayern OpenData downloader)
  openmap_blender_tools/  (submodule — vendored GDAL + Blender pipeline modules)
  workflows/              umbrella scripts that combine both
  data/                   downloaded raw tiles + processed outputs (gitignored)
```

## Quickstart — one command

```bash
git clone --recurse-submodules https://github.com/SchockTop/OpenMap_Workflow.git
cd OpenMap_Workflow

# 1. Install + enable Blender extension (one-time):
"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" \
  --command extension install-file --repo user_default --enable \
  openmap_blender_tools/dist/blender_tools-0.1.0.zip

# 2. Run end-to-end (downloads ~700 MB, takes ~10–15 min):
python workflows/full_pipeline.py --region muc-sued-4x2 \
    --datasets dgm1 dop40 lod2 --render-preview
# -> data/scene_muc-sued-4x2.blend  + data/render_muc-sued-4x2.png

# OR: open Blender, hit N-panel "OpenMap" -> "Build cinematic scene from region"
```

**Available regions:** `muc-marienplatz-50m` (1 tile, ~30 s), `muc-sued-4x2` (~10 min),
`muc-sued-10x4` (cinematic 10×4 km baseline, ~30 min, ~3 GB).

**Available datasets:** `dgm1` (1m heightmap), `dop20` / `dop40` (orthophoto, 40 cm = 4× smaller),
`lod2` (CityGML 3D buildings).

## Known issues

- **DGM1 + LoD2 endpoints return HTTP 404** from `download1.bayernwolke.de`
  for the URL pattern OpenMap_Unifier's `generate_1km_grid_files` produces
  (`/a/dgm1/data/<tile>.tif`, `/a/lod2/data/<tile>.zip`). Documented in
  `OpenMap_Unifier/DOCUMENTARY.md`. **DOP20 + DOP40 work**.
  - Workaround until the real URL pattern is found: fetch a `.meta4` file
    manually from the LDBV portal and use `MapDownloader.parse_metalink()`.

## Submodule URLs

Currently `file://` paths for offline development. Re-point to GitHub once the
remotes exist:

```bash
git config -f .gitmodules submodule.OpenMap_Unifier.url \
  https://github.com/Kleinschock/OpenMap_Unifier.git
git config -f .gitmodules submodule.openmap_blender_tools.url \
  https://github.com/<owner>/openmap_blender_tools.git
git submodule sync
```
