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

### Step 3 — run the pipeline (zero-config)

```bash
python workflows/full_pipeline.py --skip-download \
    --local-dgm  my_tiles/dgm \
    --local-dop  my_tiles/dop \
    --local-lod2 my_tiles/lod2 \
    --render-preview
```

That's it. No `--region`, no bounding-box arithmetic. The AOI is read
straight from the union of the supplied DGM GeoTIFFs' metadata. Tiles in
a different CRS get reprojected to EPSG:25832 (UTM 32N) automatically.

If you need to override the auto-derived AOI (cropping the scene to a
sub-rectangle of your tiles, for example), pass `--bbox-utm32n` and it
wins over both auto-bbox and `--region`:

```bash
python workflows/full_pipeline.py --skip-download \
    --bbox-utm32n 686000 5331000 690000 5333000 \
    --local-dgm my_tiles/dgm \
    --render-preview
```

Outputs land in `data/scene_<region>.blend` (+ `render_<region>.png`
when `--render-preview` is on). Region tag falls back to `custom` when
you didn't use `--region`.

### Cheat sheet — flag interactions

- Passing **any** `--local-*` flag implies `--skip-download` automatically.
- `--skip-download` with no `--local-*` flags falls back to
  `data/raw/<dataset>/`. Useful for re-running preprocessing after a
  one-time download without hitting the network again.
- **AOI resolution priority** (highest first):
  `--bbox-utm32n` → `--region` → auto-derived from DGM GeoTIFF tags.
- `--region` is only ever required when **downloading** (phase 1 needs
  the polygon to know which 1 km tiles to fetch).

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
- **`ValueError: ... is not a georeferenced GeoTIFF`** — the auto-bbox
  reader couldn't find ModelTiepoint / ModelPixelScale tags. Your
  `.tif` is a plain TIFF without geo-metadata (often the case after
  `gdal_translate` without `-co GEOTIFF=YES`, or files exported as
  PNG-then-renamed). Fix: re-export with georeference, ship a `.jgw`
  world file alongside, or supply `--bbox-utm32n` manually.
- **`ModuleNotFoundError: backend`** — you tried to run without
  `--skip-download` but the `OpenMap_Unifier` submodule isn't checked
  out. Either `git submodule update --init`, or stick to skip-download
  mode.
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

## TRIAN3D Builder import

If you build the surrounding region in [TRIAN3D Builder](https://www.trian3dbuilder.de/),
export it as **Autodesk FBX** (with Materials, Textures, and Hierarchy
ticked) and feed it into:

```bash
# Fresh scene (just the TRIAN3D import):
python workflows/trian3d_import.py --fbx my_export.fbx --out scene.blend

# Drop on top of the existing terrain build:
python workflows/trian3d_import.py --fbx my_export.fbx \
    --into-existing data/scene_custom.blend \
    --out data/scene_custom_with_trian.blend
```

What it does, in three passes:

1. **Organize** — every imported object is moved into a Blender Collection
   based on a regex match against its name. Defaults match TRIAN3D's
   `bldg_…`, `road_…`, `veg_tree_…`, `water_river_…`, `field_…` naming, so
   you get this hierarchy out of the box:

   ```
   Scene Collection
   ├── Buildings/{Residential, Industrial, Commercial, Other}
   ├── Vegetation/{Trees, Forest, Other}
   ├── Roads/{Highway, Secondary, Local, Other}
   ├── Water/{Rivers, Lakes, Other}
   ├── Land use/{Fields, Other}
   └── Reference (cameras, lights, helpers)
   ```

   Override the rules with `--rules my_rules.json`. Schema example:

   ```json
   {
     "version": 1,
     "organize": [
       {"collection": "Buildings/Heritage", "match": {"name_regex": "^bldg_listed_"}}
     ],
     "materials": [
       {"material": "Field_Wheat", "match": {"prop": "osm_class", "equals": "wheat"}},
       {"material": "Field_Corn",  "match": {"prop": "osm_id",    "in": [123, 456, 789]}}
     ]
   }
   ```

   Match predicates: `name_regex`, `material_name_regex`, or `prop` with
   `equals` / `in` / `regex`. Combine multiple keys for AND logic. First
   matching rule wins — order more-specific rules first.

2. **Apply materials** — each `materials` rule swaps material slot 0 of
   matching objects to the named material. The material must already
   exist in the .blend (load via `--into-existing` from a scene that
   contains it, or via Blender's Asset Library).

3. **Collapse duplicate meshes to linked data** — TRIAN3D output often
   contains thousands of objects with bit-identical meshes (same building
   mesh repeated, same tree, etc.). This pass groups them by
   `(vertex_count, edge_count, polygon_count, first_vertex_coord)` and
   relinks every duplicate to a single canonical Mesh datablock. On a
   real 130 km² scene this can drop scene memory by 80–95 %. Pass
   `--no-collapse` to skip.

### Behind the scenes (extending the Blender plugin)

The pure-Python pieces live in:

- `workflows/trian3d_rules.py` — rule schema + matcher (no bpy)
- `workflows/trian3d_apply.py` — `organize_scene` / `apply_material_rules`
  / `collapse_to_linked_data` (lazy bpy)
- `workflows/_blender_trian3d_import.py` — Blender entry point
- `workflows/trian3d_import.py` — CPython orchestrator

These can be wrapped as `bpy.types.Operator`s in
`openmap_blender_tools/operators.py` to expose the same actions as
N-panel buttons. Pattern matches the existing `BLENDERTOOLS_OT_cull_hidden`
operator in that submodule.

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
