# OSM Scatter Visual Regression

Three-frame render + qualitative vision check for OSM-driven trees + grass scatter
on a small Allgäu forest-edge AOI.

## Prerequisites

- Blender on PATH.
- Downloaded layers for the AOI (DGM1, DOP20, LoD2, OSM GeoJSON). Use:
  `OpenMap_Unifier` GUI or CLI with the bbox / polygon for the region preset
  at `workflows/tests/visual/regions/allgaeu_osm_test.json`.
- GDAL CLI on PATH (`gdal_rasterize`, `ogr2ogr`).

## Run

```
pytest workflows/tests/visual/test_osm_scatter.py -v --render-visuals
```

This renders three frames into `osm_scatter/renders/`. Acceptance via
`/review-renders` skill against the PNGs:

- topdown: no trees within road buffers, no trees on water, no trees on
  building footprints.
- hero: visible grass clumps on meadow / park polygons.
- ground: forest density higher than scrub.

If the test errors before rendering, full_pipeline.py likely needs the
`--region-preset` / `--render-frames` / `--render-out` flags to be wired up
(scaffolded by Task 9 but not in scope of the OSM feature itself).
