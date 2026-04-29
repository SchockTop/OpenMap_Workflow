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

## Ground-layer audit (open question)

The current `showcase/*.png` images do not visibly show a draped DOP / aerial
photograph on the terrain — the "ground" reads as flat color or dark water.
A blind per-image scorer (`workflows/blind_ground_detector.py`, knows nothing
about which layer each image is) confirms this:

```
file                          hues   std edges  verdict
04_feature_buildings.png         8   2.7 0.002  EMPTY
06_feature_ground_shader.png     2   0.4 0.000  EMPTY
05_feature_trees.png            15  27.5 0.021  FLAT
07_feature_groundcover.png      20  27.8 0.100  FLAT
01_poster.png                   50  69.0 0.289  FLAT
03_altitude_comparison.png      58  65.1 0.157  FLAT
```

`workflows/test_progressive_layers.py` re-renders the same camera with one
layer added at a time (sky → flat plane → **ortho drape** → heightmap →
ground-shader → groundcover → trees → buildings → atmosphere) and runs the
blind detector across the frames. Frame `02_ortho_drape.png` must score
visibly higher than `01_terrain_flat.png`; if it doesn't, the orthophoto
isn't reaching the terrain material and that's the bug to chase first.

```bash
python workflows/test_progressive_layers.py --region muc-marienplatz-50m
# -> showcase/ground_layer_test/00_sky.png ... 08_atmosphere.png + verdict
```

**Headless plumbing run** (no Blender executable, no GPU, no Bayern data):
`workflows/_headless_make_synth_data.py` generates a synthetic heightmap +
a vivid synthetic UDIM ortho (`ortho.1001.jpg`), then
`_headless_progressive.py` renders four frames using pip-installed `bpy`
and Cycles CPU. Result, scored by the blind detector:

```
file                         hues   std edges  verdict
00_sky.png                      2   0.3 0.000  EMPTY
01_terrain_flat.png             6  10.0 0.002  FLAT
02_ortho_drape.png             58  36.6 0.079  FLAT
03_heightmap_plus_drape.png    82  38.6 0.275  GROUND_VISIBLE
```

Frame 02 jumps std 10→36, hues 6→58, edges 0.002→0.079 — the
`apply_ortho_drape` code path works. So the missing ground in the
existing `showcase/01_poster.png` etc. is **upstream of the drape
function**: either the DOP tiles weren't downloaded, weren't passed to
`_blender_assemble_full.py` via `--ortho-dir`, or were saved with a
filename other than `ortho.<udim>.jpg`. Open the actual scene .blend
and run `workflows/test_ortho_drape_present.py` against it to confirm.

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
