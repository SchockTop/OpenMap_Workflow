# OSM-Driven Semantic Scatter — Design

**Date:** 2026-05-13
**Scope:** Trees + grass scatter. Per-class ground-material tinting is deferred.

## Problem

`features/trees.py` scatters on a single grayscale `ForestMask.tif`. It can't tell
roads, water, or building footprints apart from forest floor, so trees appear in
places they shouldn't. There's no grass system at all. OpenStreetMap, which
`OpenMap_Unifier` already downloads, has all the polygons we need to fix this.

## Goal

Use OSM polygons to drive both tree scatter (with hard exclusions for
roads/water/buildings and per-class density modulation) and a new grass scatter,
through a shared per-class mask pipeline. Three customization levels: preset,
per-class sliders, custom JSON mapping.

## Non-goals

- Per-class ground-material tinting on the DOP ortho (deferred — own design pass).
- POI / amenity / public-transport class consumption.
- Tree species beyond the optional `forest_conifer` second mask.
- Per-tree variance from `natural=tree` node tags (`circumference`, `height`).
- Building / 3D consumption — LoD2 handles that.

## Architecture

```
OpenMap_Unifier (existing)
    └── osm_<bbox>.geojson                       # WGS84, raw Overpass output

openmap_blender_tools/
    osm_classes.py        (NEW)  tag → class-name dict + JSON override loader
    osm_rasterize.py      (NEW)  GeoJSON → N per-class GeoTIFFs (uses vendored GDAL)
    features/
        trees.py          (REWRITE)  consumes include + exclude masks
        grass.py          (NEW)  scatter mirror of trees.py for grass classes
    operators.py          (EDIT)  + BLENDERTOOLS_OT_apply_osm_layers
    ui.py / panels        (EDIT)  + OSM Layers sub-panel
```

Pipeline scripts `workflows/_blender_assemble_full.py` and
`workflows/_blender_progressive_layers.py` gain an OSM stage that runs after
terrain+ortho and before features.

### Module boundaries

| Module | Depends on | bpy? | GDAL? | Testable without |
|---|---|---|---|---|
| `osm_classes.py` | stdlib | no | no | both |
| `osm_rasterize.py` | `osm_classes`, GDAL CLI | no | yes (shells out) | bpy; GDAL tests gated by `@pytest.mark.needs_gdal` |
| `features/trees.py`, `features/grass.py` | bpy | yes | no | tests use existing mocked-bpy harness |

## Data flow

1. Unifier downloads OSM GeoJSON for the AOI (unchanged).
2. `osm_rasterize.build_masks(geojson, terrain_bbox, resolution_m, out_dir, mapping=None)`:
   - Reprojects 4326 → 25832 with `ogrcmd -t_srs EPSG:25832`.
   - Buffers `highway=*` linestrings by class (table below).
   - For each class, runs `gdal_rasterize -burn 1 -ot Byte -te ... -tr ...`
     into `osm_mask_<class>.tif` aligned to the terrain grid.
3. Operator/pipeline sets `scene["osm_masks_dir"] = <path>`.
4. `features/trees.py` and `features/grass.py` load each mask present in the
   directory as a `Non-Color` Image datablock and wire them into the existing
   GN scatter graph:
   - Include term: `I(x,y) = clamp(Σ_i include_mask_i(x,y)·m_i, 0, 1)`
     (sum then clamp so overlapping include polygons don't blow up density).
   - Exclude term: `E(x,y) = Π_j (1 − exclude_mask_j(x,y)·s_j)`
     where `s_j` is the per-class exclusion strength slider (0 = no effect,
     1 = full cutout). Default `s_j = 1.0` for `road`/`water`/`building`.
   - Final: `density(x,y) = base_density · I(x,y) · E(x,y)`.
   - All masks are Byte (0/255) on disk, loaded as Non-Color Image datablocks
     and normalized to 0..1 via the existing Image Texture node convention.

### Road buffer table (motorway → footpath)

| `highway=` value | Half-width buffer (m) |
|---|---|
| `motorway`, `motorway_link`, `trunk` | 12 |
| `primary`, `secondary` | 8 |
| `tertiary`, `residential`, `unclassified` | 5 |
| `service`, `living_street` | 3 |
| `track`, `footway`, `path`, `cycleway`, `pedestrian`, `steps` | 2 |
| (default for unknown values) | 3 |

## Class taxonomy

Default OSM-tag → class-name mapping (in `osm_classes.DEFAULT_MAPPING`).
Users override via JSON path passed through scene prop or operator arg.

**Exclude classes** (subtract from density):

- `building` — `building=*`
- `road` — `highway=*` (buffered)
- `water` — `natural=water`, `waterway=*`, `landuse=basin`, `landuse=reservoir`

**Include classes for trees:**

- `forest` — `landuse=forest`, `natural=wood`
- `forest_conifer` — same polygons that ALSO carry `leaf_type=needleleaf`.
  Mask is always baked when present in the data. `features/trees.py` wires it
  to a `Trees_Conifer` collection if one exists in the scene; silently ignored
  otherwise. Zero cost when unused.
- `scrub` — `natural=scrub`, `natural=heath`
- `orchard` — `landuse=orchard`

**Include classes for grass:**

- `meadow` — `landuse=meadow`, `natural=grassland`
- `park` — `leisure=park`, `leisure=garden`
- `farmland` — `landuse=farmland` (low density)
- `grass` — `landuse=grass`, `landuse=recreation_ground`

**Custom mapping JSON schema:**

```json
{
  "classes": {
    "forest": [{"key": "landuse", "value": "forest"},
               {"key": "natural", "value": "wood"}],
    "my_custom_class": [{"key": "landuse", "value": "cemetery"}]
  },
  "trees": {"include": ["forest", "scrub", ...], "exclude": ["building", ...]},
  "grass": {"include": ["meadow", "park", ...], "exclude": ["building", "water"]}
}
```

**Merge semantics:** user JSON is shallow-merged over the default.

- `classes`: a class name present in the user JSON fully replaces the default
  tag list for that name. A new class name (not in default) is added. To
  remove a default class, set its value to an empty list.
- `trees.include` / `trees.exclude` / `grass.include` / `grass.exclude`: each
  list, if present in the user JSON, fully replaces the corresponding default
  list. (No element-level merge — easier to reason about.)
- Adding a new class without also listing it in `trees.*` or `grass.*` bakes
  the mask but doesn't consume it. Intentional: lets users author for future
  use or for the deferred material feature.

## Customization levels

1. **Preset dropdown** on the OSM Layers panel — `Off`, `Default`, `High Detail`.
   `Off` clears scene["osm_masks_dir"]; both scatters revert to ForestMask
   fallback. `High Detail` bakes at 1 m/px and bumps base density.

2. **Per-class density multipliers** — one slider per class whose mask file
   exists in the directory. Stored in `scene["osm_class_multipliers"]` as a
   `{class: float}` dict. Read by the GN modifier through Float drivers so
   slider edits update the viewport without re-baking.

3. **Custom mapping JSON** — `scene["osm_class_mapping_path"]`. Reloaded on
   "Apply OSM Layers" operator run.

## UI

A new sub-panel `OSM Layers` under the main BlenderTools panel:

```
▼ OSM Layers
   GeoJSON:   [browse] osm.geojson
   Preset:    [Default ▾]
   [Apply OSM Layers]
   Masks dir: data/.../osm_masks      (read-only)
   ▼ Tune classes  (collapsed by default; only shows classes with a mask)
       forest      [▬▬▬●▬▬▬] 1.0
       meadow      [▬▬●▬▬▬▬] 0.6
       road        [▬▬▬▬▬●▬] (exclusion strength)
       …
   Custom mapping: [browse] (optional)
```

## Pipeline integration

Insert one stage in `_blender_assemble_full.py` and
`_blender_progressive_layers.py`, after ortho and before features:

```python
osm_geojson = Path(args.osm_geojson) if args.osm_geojson else None
if osm_geojson and osm_geojson.is_file():
    masks_dir = work_dir / "osm_masks"
    osm_rasterize.build_masks(
        osm_geojson, terrain_bbox, resolution_m=1.0, out_dir=masks_dir
    )
    bpy.context.scene["osm_masks_dir"] = str(masks_dir)
```

Features (`trees`, `grass`) automatically pick up the masks. Existing
ForestMask code path stays as the no-OSM fallback.

## Testing

### Unit (pure-Python, run by default)

`test_osm_classes.py`:
- Tag dict resolves to expected class for each default-mapping entry.
- User JSON merges over default (added classes appear; overridden replace).
- Unknown tag → no class assignment (returns `None`).

`test_osm_rasterize.py`:
- `_road_buffer_meters(value)` returns the right buffer per highway type
  (including the default fallback for unknown values).
- `_build_rasterize_cmd(...)` constructs the expected GDAL argv (mocked,
  no GDAL execution).
- Input validation: missing GeoJSON → clear error; empty class set → no-op
  with warning, not crash.

### GDAL-gated integration (`@pytest.mark.needs_gdal`)

`test_osm_rasterize_live.py`:
- Tiny hand-rolled fixture GeoJSON (one forest polygon, one road line, one
  water polygon) in EPSG:4326.
- Call `build_masks` with a known bbox.
- Open the resulting TIFFs with GDAL Python API; assert pixel values at
  pre-computed coordinates inside vs outside each polygon.
- Assert the road TIFF shows the buffered linestring as a band of width
  ≈ 2 × buffer.

### Blender-side (mocked bpy)

`test_features_trees_osm.py`:
- Mock a masks_dir containing `osm_mask_forest.tif` + `osm_mask_road.tif` +
  `manifest.json`; call `trees.apply(context)`; assert the resulting GN
  modifier has exactly one Image Texture node per mask file present (named
  `tex_<class>`), one Math/Mul-Add node per include, one (1 − x) chain per
  exclude, and that node-tree input names match the class names.
- Per-class multiplier slider edits propagate via drivers (asserted on
  driver targets, not by running Blender).

`test_features_grass.py` (NEW): mirror tests for grass.

### Visual regression (manual + `/review-renders`)

Small Allgäu forest-edge AOI (~500 m). Three frames:

1. Top-down 200 m altitude, full AOI.
2. Hero perspective, low angle across forest/meadow boundary.
3. Ground-level POV looking toward a road through trees (should show road
   clearing).

Acceptance via `/review-renders`:
- No trees on water polygons.
- No trees on road buffers.
- No trees on building footprints.
- Visible grass density on meadows / parks.
- Forest mass denser than scrub (qualitative).

Goldens stored under `workflows/tests/visual/osm_scatter/` as PNGs + JSON
checks.

## Risks & open questions

- **GDAL `gdal_rasterize` perf on large GeoJSONs**: should be fine for AOIs
  up to a few km², but worth confirming on the Munich tile (~5 MB GeoJSON).
  Mitigation: clip GeoJSON to bbox via `ogr2ogr -clipsrc` before rasterizing.
- **Mask file count**: with all classes present, ~10 GeoTIFFs per scene.
  Probably fine; bookkeep via a `masks_dir/manifest.json` listing classes
  present, so features iterate the manifest instead of `glob *.tif`.
- **GN modifier complexity**: each include/exclude mask adds two nodes
  (Image Texture + Math). 10 classes → ~20 nodes. Tested-tolerable count.
- **Image colorspace pitfall (per CLAUDE.md)**: masks must be `Non-Color`.
  Helper sets it explicitly; smoke test asserts.

## Out-of-scope follow-ups

After v1 ships and renders look right:

1. Per-class ground material tinting (mix DOP × class color/roughness).
2. `natural=tree` node import as individual placed instances with
   `species` / `height` driving per-tree variance.
3. Public transport: railway lines as a separate scatter exclusion (and
   maybe a future "railway features" feature for sleepers/ballast).
4. Building-footprint mask hand-off into LoD2 building placement (already
   done via terrain snap shrinkwrap, but rooftop scatter — chimney/AC —
   could reuse the infra).

## Acceptance

- Allgäu AOI renders pass `/review-renders` vision checks listed above.
- All new unit tests pass; GDAL-gated test passes when GDAL is on PATH.
- Existing trees scatter regression suite still green (the fallback path
  must continue to work when `scene["osm_masks_dir"]` is unset).
- No regressions in the existing terrain/ortho/buildings tests.
