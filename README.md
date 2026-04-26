# OpenMap_Workflow

Umbrella repo that wires Bayern OpenData (DGM/DOP/LoD2) through a Blender
pipeline for terrain + cinematic renders. Two submodules:

```
OpenMap_Workflow/
  OpenMap_Unifier/        (submodule — Bayern OpenData downloader)
  openmap_blender_tools/  (submodule — vendored GDAL + Blender pipeline modules)
  workflows/              umbrella scripts that combine both
  data/                   downloaded raw tiles + processed outputs (gitignored)
```

## Quickstart

```bash
git clone --recurse-submodules <umbrella-url>
cd OpenMap_Workflow

# 1. Download a Marienplatz test tile (DOP20 works; see "Known issues" below)
python workflows/download_munich_test_tile.py

# 2. Build a Blender scene from the downloaded tiles
"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" \
  --background --python workflows/tile_to_blender_scene.py
```

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
