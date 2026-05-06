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

> **Behind a corporate proxy or air-gapped?** Skip the auto-download —
> see [Offline / behind-a-proxy: bring your own tiles](#offline--behind-a-proxy-bring-your-own-tiles)
> for fetching tiles manually and feeding them in with `--skip-download`.

**Available regions:** `muc-marienplatz-50m` (1 tile, ~30 s), `muc-sued-4x2` (~10 min),
`muc-sued-10x4` (cinematic 10×4 km baseline, ~30 min, ~3 GB).

**Available datasets:** `dgm1` (1m heightmap), `dop20` / `dop40` (orthophoto, 40 cm = 4× smaller),
`lod2` (CityGML 3D buildings).

## Offline / behind-a-proxy: bring your own tiles

If your network blocks `download1.bayernwolke.de` / `geodaten.bayern.de`
(corporate proxy, air-gapped machine, hotel Wi-Fi…) the pipeline can still
run — you fetch the tiles yourself by whatever means, then feed them in with
`--skip-download`. No code changes required.

### Step 1 — get the tiles

Pick **one** of these, whichever your environment allows:

| Method | When to use |
|---|---|
| **Browser (manual)** | Open [geodaten.bayern.de/opengeodata](https://geodaten.bayern.de/opengeodata/), pick the dataset, draw or enter your AOI, download the tiles. Slowest but always works. |
| **`curl` / `wget` through your proxy** | `export HTTPS_PROXY=http://user:pass@proxy:8080` then `curl -O <url>` for each tile. |
| **`aria2c`** | Faster bulk download if you can extract a URL list. Respects `HTTPS_PROXY`. |
| **Sneakernet / shared drive** | Have a colleague run a download somewhere with open egress, then copy the resulting `data/raw/` tree to your machine. |

You need **at minimum** DGM1 (heightmap). DOP (orthophoto) and LoD2
(buildings) are optional — the pipeline degrades gracefully and skips
features whose inputs are missing.

### Step 2 — drop the files into a folder

The layout is up to you. The pipeline accepts files OR directories
(scanned recursively) and matches by extension:

| Dataset | Flag | File extensions accepted |
|---|---|---|
| 1 m heightmap (DGM1) | `--local-dgm` | `*.tif`, `*.tiff` |
| Orthophoto (DOP20/40) | `--local-dop` | `*.tif`, `*.tiff` |
| LoD2 buildings | `--local-lod2` | `*.gml`, `*.xml`, `*.zip` (zipped GML stays zipped) |

A typical layout:

```
my_tiles/
├── dgm/
│   ├── 32_690_5333.tif
│   └── 32_691_5333.tif
├── dop/
│   └── 32_690_5333.tif
└── lod2/
    └── 32_690_5333.zip
```

### Step 3 — run the pipeline

```bash
python workflows/full_pipeline.py --skip-download --region muc-sued-4x2 \
    --local-dgm  my_tiles/dgm \
    --local-dop  my_tiles/dop \
    --local-lod2 my_tiles/lod2 \
    --render-preview
```

Or, if your AOI isn't one of the named region presets, supply an explicit
bbox in EPSG:25832 (UTM zone 32N) metres:

```bash
python workflows/full_pipeline.py --skip-download \
    --bbox-utm32n 686000 5331000 690000 5333000 \
    --local-dgm my_tiles/dgm \
    --render-preview
```

Outputs land in `data/scene_<region>.blend` (+ `render_<region>.png`
when `--render-preview` is on). Region tag falls back to `custom` when
you used `--bbox-utm32n` without `--region`.

### Cheat sheet — flag interactions

- Passing **any** `--local-*` flag implies `--skip-download` automatically.
- `--skip-download` with no `--local-*` flags falls back to
  `data/raw/<dataset>/`. Useful for re-running preprocessing after a
  one-time download without hitting the network again.
- `--bbox-utm32n` overrides the bbox derived from `--region` when both
  are supplied.
- `--region` is required only if you neither pass `--bbox-utm32n` nor
  rely on the `data/raw/` fallback.

### Verifying the offline mode is wired correctly

Before downloading hundreds of MB, sanity-check the plumbing:

```bash
# 1. CLI parses cleanly (no submodules required for --help):
python workflows/full_pipeline.py --help

# 2. Run the offline-mode unit tests:
python -m pytest workflows/tests/test_full_pipeline_local.py -v
# -> 15 passed
```

Both should succeed even on a fresh checkout where the submodules
haven't been initialised yet.

### Common pitfalls

- **`[!] no DGM1 tiles available`** — the pipeline could not find any
  `*.tif` files under what you passed to `--local-dgm` (or under
  `data/raw/dgm1/`). Double-check the path and extension.
- **Buildings missing from the render** — LoD2 input was empty. The
  scene still builds; you just get terrain + ortho. Pass `--local-lod2`
  to add buildings.
- **Render looks oddly cropped** — your tiles don't fully cover the
  bbox. Either widen the tile set or shrink `--bbox-utm32n`.
- **`ModuleNotFoundError: backend`** — you tried to run without
  `--skip-download` but the `OpenMap_Unifier` submodule isn't checked
  out. Either `git submodule update --init`, or stick to skip-download
  mode.

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
