# DOM-Mesh cutout → OpenMap_Unifier + Blender addon — design

**Date:** 2026-05-12
**Status:** approved (brainstorm), ready for implementation plan

## Goal

Productionise the `experiments/dommesh_cutout/` spike: let a user feed a Google
Earth KML polygon into **OpenMap_Unifier** (GUI *and* web app) and get back a
small, Blender-ready slice of Bayern's DOM-Mesh photogrammetry mesh —
range-fetched from the per-Los `DSM_Mesh.slpk` instead of downloading the
50–200 GB archive. The **openmap_blender_tools** extension gets an operator to
import that slice into the OpenMap anchored scene, with tests.

Background / proven facts: see `experiments/dommesh_cutout/README.md` and the
`dommesh-cutout-spike` memory. Key ones reused verbatim:
- SLPK = ZIP64 of I3S 1.9 `meshpyramids`; `download{1,2}.bayernwolke.de` honours
  HTTP `Range` (`206`).
- Central directory (~62 MB) holds entry offsets; `nodepages/*.json.gz` (~13 MB)
  holds every node's OBB; both contiguous near EOF.
- OBB centers/halfSizes are in EPSG:25832, identity quaternions → AOI filter is a
  2D AABB test.
- Geometry `nodes/<res>/geometries/0.bin.gz`: u32 `vertexCount` + u32
  `featureCount`, then positions f32×3 (relative to OBB center), then uv0 f32×2;
  non-indexed triangle soup. Texture `nodes/<res>/textures/0.jpg` plain JPEG, UV
  [0,1] (flip V for OBJ).

## Decisions (from brainstorm)

- Lives in **both** the customtkinter GUI and the FastAPI web app.
- AOI input is the **existing Google Earth KML polygon** flow (same as DOP/DGM/LoD2).
- The DOM-Mesh **Los is auto-picked** from the AOI via
  `DOM_Mesh_projektgebiete_2026.kml`; on the Nord/Süd overlap, try each
  candidate Los and keep whichever yields geometry, with a warning.
- Output: **both** `cutout.obj` (+ `.mtl` + `tex/`) **and** `cutout.glb` (single
  binary glTF 2.0, embedded JPEGs, one PBR material per node texture — no atlas).
- Triangles are clipped to the **polygon** (centroid point-in-polygon), not just
  its bounding box.
- New `mesh3d` UI category (`"3D Meshes (photogrammetry)"`) next to `buildings`.
- No new third-party wheels — stdlib + already-vendored numpy / shapely / pyproj /
  Pillow / requests only. The GLB writer is hand-rolled.

## Architecture

```
KML file ──PolygonExtractor──▶ EWKT (SRID=4326;POLYGON((...)))
                                   │
        ┌──────────────────────────┴───────────────────────────┐
   GUI dispatch (kind=="mesh")                 web /start-download-dommesh
        └──────────────────────────┬───────────────────────────┘
                                   ▼
                  backend.dommesh.cutout(polygon_wkt, out_dir, formats, progress)
                                   │
   ┌───────────────┬───────────────┼───────────────────┬──────────────────┐
 LosIndex      SlpkReader.entries() SlpkReader.nodes()  per-node fetch     writers
 (point→Los)  (ZIP64 CD, cached)   (OBB list, cached)   +decode +clip      OBJ / GLB
                                   ▼
                  downloads_bayern/dommesh/  →  cutout.obj/.mtl/tex/, cutout.glb, meta.json
                                   │
                                   ▼
        openmap_blender_tools  ▶  operator blender_tools.import_dommesh
        (import GLB, re-anchor objects into scene's UTM-local frame)
```

## Components

### 1. `OpenMap_Unifier/backend/dommesh.py` (new)

Cohesive single module (split into `i3s.py` + `dommesh.py` only if it grows past
~450 lines). Public surface:

- `class LosIndex`
  - Downloads & caches `https://geodaten.bayern.de/odd/m/3/daten/DOMMesh/DOM_Mesh_projektgebiete_2026.kml`.
  - `los_ids_for_point(e: float, n: float) -> list[str]` — KML polygons projected
    to EPSG:25832, shapely `Point.within`; returns all matches (ambiguity on the
    Nord/Süd seam is expected and handled by the caller).
- `class SlpkReader(losid: str, cache_dir: Path)`
  - `_urls()` → `[download1.../p/dom-mesh-slpk/<losid>/DSM_Mesh.slpk, download2...]`.
  - `_rng(a, b)` — `Range: bytes=a-b`, mirror-2 fallback, returns bytes.
  - `entries() -> dict[str, tuple[int,int,int,int]]` — `name → (offset, csize,
    usize, method)`. Parses the ZIP64 EOCD locator/record → central directory
    (~62 MB read), cached as `<cache_dir>/<losid>/entries.json`.
  - `nodes() -> list[NodeRec]` — leaf records `(i, cx, cy, cz, hx, hy, hz,
    geom_res, mat_res)` from `nodepages/*.json.gz` + `3dSceneLayer.json.gz` (~13 MB
    read), cached as `<cache_dir>/<losid>/nodes.json`. Only `mesh` leaves with no
    children are kept.
  - `read_entry(name) -> bytes` — local-header parse → range-fetch payload →
    `gzip.decompress` if `.gz`.
- `decode_geometry(blob) -> (vcount, positions, uvs)` — the I3S
  `PerAttributeArray` decode (port of the spike's `decode_geometry`).
- `write_obj(out_dir, submeshes, anchor)` — `cutout.obj` + `cutout.mtl` +
  `tex/node_<i>.jpg` (port of the spike).
- `write_glb(out_path, submeshes, anchor)` — minimal binary glTF 2.0: one `BIN`
  buffer holding, per submesh, `indices` (u32), `POSITION` (f32×3, Y-up: map
  EPSG:25832 (E,N,Z)-relative-to-anchor → glTF (E, Z, −N) so it lands Y-up like
  every other glTF), `TEXCOORD_0` (f32×2, V already flipped), then the JPEG bytes
  as image buffer-views; `meshes`/`accessors`/`bufferViews`/`materials`
  (`pbrMetallicRoughness.baseColorTexture`, `metallicFactor` 0, `roughnessFactor`
  1)/`images`/`textures`/`samplers`/one root `node` per submesh under one `scene`.
  Asset `{"version":"2.0","generator":"OpenMap_Unifier dommesh"}`.
- `cutout(polygon_wkt: str, out_dir: str, formats=("obj","glb"), progress=None) -> dict`
  1. Strip `SRID=…;`, `shapely.wkt.loads`, project exterior to EPSG:25832
     (`pyproj.Transformer`, `always_xy=True`, drop Z), build `Polygon` + `bounds`.
  2. `los_ids = LosIndex().los_ids_for_point(*polygon.representative_point())`.
     If empty → raise/return error *"This area isn't covered by Bayern's DOM-Mesh,
     or no flight-day Los matched."*
  3. For each candidate Los (in order): `r = SlpkReader(los, cache_dir)`;
     `entries = r.entries()`; `nodes = r.nodes()`; select leaves whose OBB-AABB
     overlaps `bounds`. First Los with ≥1 overlapping leaf wins; if 2+ candidates
     overlap, log a warning naming both and use the first.
  4. `anchor = (floor(minx), floor(miny))` (SW corner; Z anchor 0).
  5. `ThreadPoolExecutor(max_workers=8)` over the selected leaves:
     `g = r.read_entry(f"nodes/{geom_res}/geometries/0.bin.gz")`,
     `tex = r.read_entry(f"nodes/{mat_res}/textures/0.jpg")`, decode, lift verts to
     world `(ocx+px, ocy+py, ocz+pz)`, keep triangles whose centroid is
     `polygon.contains(Point(ccx, ccy))`, remap to a compact submesh
     `{verts:[(x-anchorx, y-anchory, z)…], uvs:[(u, 1-v)…], tris:[(a,b,c)…],
     jpeg: bytes, node_id}`. `progress(name, pct, status, …)` after each future
     resolves (`name = f"dommesh {losid}"`, `pct = done/total`).
  6. If no submesh survived → error *"No mesh nodes overlap your polygon."*
  7. Write requested `formats`; write `meta.json` with the same key set as the
     spike (`slpk`/`losid`, `polygon_epsg25832`, `bbox_epsg25832`,
     `anchor_epsg25832`, `leaf_nodes`, `vertices`, `triangles`, `bytes_fetched`,
     `seconds`). Return `meta`.

Cache dir defaults to `<out_dir>/.dommesh_cache`; the KML index is cached there
too (`losindex.kml`).

### 2. `OpenMap_Unifier/backend/downloader.py` — catalog entry

Add to `BAYERN_DATASETS`:
```python
"dommesh": {
    "label": "DOM-Mesh — Photogrammetric 3D city mesh (textured)",
    "category": "mesh3d",
    "description": "Textured photogrammetry mesh (buildings + trees + terrain) "
                   "cut to your polygon. Range-fetched from Bayern's SLPK — "
                   "no multi-GB download.",
    "ext": ".glb",
    "resolution": "photogrammetry mesh",
    "kind": "mesh",
},
```
Add `BAYERN_CATEGORY_LABELS["mesh3d"] = "3D Meshes (photogrammetry)"`.
`downloader.py` itself gains nothing else — `cutout` lives in `dommesh.py`; the
catalog stays the single source of truth for the GUI.

### 3. `OpenMap_Unifier/gui.py`

- Category/checkbox loop already renders any `BAYERN_DATASETS` entry by
  `category` → `mesh3d` shows up automatically.
- Add `("Bayern: DOM-Mesh 3D", os.path.join("downloads_bayern", "dommesh"))` to
  the download-folder map (~line 314).
- In the download-dispatch block (~lines 663–747), add
  `elif meta["kind"] == "mesh":` → on the worker thread:
  `from backend.dommesh import cutout; cutout(poly, out_dir, progress=self._progress_cb)`,
  wrapped in the same try/except + status-line update used by the raw/wms
  branches. No new UI controls (OBJ+GLB always written). Surface `cutout`'s
  error string in the status line / console tab on failure.

### 4. `OpenMap_Unifier/app.py` + `templates/index.html` + `static/script.js`

- New endpoint, structured exactly like `/start-download-relief`:
  ```python
  @app.post("/start-download-dommesh")
  async def start_download_dommesh(background_tasks: BackgroundTasks, polygon: str = Form(...)):
      out_dir = "downloads_dommesh"
      progress_state["dommesh"] = {"percent": 0, "status": "Pending"}
      background_tasks.add_task(run_dommesh, polygon, out_dir)
      return {"message": "Download started"}

  async def run_dommesh(polygon, out_dir):
      from backend.dommesh import cutout
      loop = asyncio.get_event_loop()
      await loop.run_in_executor(None, cutout, polygon, out_dir, ("obj", "glb"),
                                 ProgressManager.update_progress)
  ```
  (Errors from `cutout` → `progress_state["dommesh"]["status"]`.)
- A "Download DOM-Mesh 3D (cut to polygon)" button + handler in the page, wired
  like the existing relief button (posts the analysed polygon to the new
  endpoint, then polls `/progress`).

### 5. `openmap_blender_tools/dommesh_import.py` (new) + operator

Mirrors `citygml_import.py` / its operator:
- Pure-Python: `read_dommesh_meta(path) -> dict`; `anchor_offset(meta, scene_anchor)`
  → the `(dx, dy)` translation to move imported verts from the cutout's anchor
  into the scene's `utm32n_anchor` frame (0,0 if scene has no anchor yet — then
  also set `scene["utm32n_anchor"]` from the cutout, like `import_heightmap`).
- bpy-dependent: `import_dommesh_glb(glb_path, meta_path, anchor_utm32n) -> list[Object]`
  — `bpy.ops.import_scene.gltf(filepath=glb_path)`, collect the newly added
  objects, parent under one empty named `DOM-Mesh`, translate by `anchor_offset`,
  set `image.colorspace_settings.name = "sRGB"` on the textures, return objects.
- `operators.py`: new `OBT_OT_import_dommesh` (`bl_idname =
  "blender_tools.import_dommesh"`, file-select `.glb`, reads sibling `meta.json`),
  registered in the `classes` tuple and surfaced in the panel next to "Import
  Buildings". Follows the existing operator pattern (`bl_idname`, `bl_label`,
  props, `invoke` → file browser, `execute`).
- `blender_manifest.toml` — no change needed (no new perms/deps; glTF import is
  a built-in Blender addon, ensure it's enabled in `execute` like other ops do).

## Error handling

| Situation | Behaviour |
|---|---|
| AOI outside DOM-Mesh coverage / no Los polygon contains it | `cutout` returns `{"error": "...not covered by Bayern's DOM-Mesh..."}`; GUI status line + web `progress_state` show it. |
| `Range` refused / 4xx / 5xx on download1 | fall through to download2; if both fail, raise with the HTTP error. |
| 2+ candidate Los overlap the AOI (Nord/Süd seam) | use the first, log a warning naming both. |
| Zero overlapping leaf nodes after AABB filter, or zero triangles after polygon clip | error *"No mesh nodes overlap your polygon."* |
| Blender import: scene has no `utm32n_anchor` yet | seed it from the cutout's `meta.json` anchor (parity with `import_heightmap`). |

## Testing

### `OpenMap_Unifier/test_dommesh.py` (new, pytest, written failing first)

Pure-Python, no network:
- `test_zip64_central_directory_parse` — hand-built ZIP64 EOCD + one central-dir
  record → `entries()` returns the right `(offset, csize, usize, method)`.
- `test_i3s_geometry_decode` — synthetic `<II>` header + f32 positions + f32 uvs →
  `decode_geometry` returns matching counts/values.
- `test_los_lookup_point_in_polygon` — minimal KML with one named Los polygon →
  point inside → `[losid]`; point outside → `[]`.
- `test_triangle_polygon_clip` — a handful of triangles + a polygon → only those
  with centroid inside survive, indices remapped contiguously.
- `test_glb_writer_roundtrip` — build a 1-triangle textured submesh →
  `write_glb` → re-read: assert `b"glTF"` magic, version `2`, total length matches,
  JSON chunk has `meshes`/`accessors`/`bufferViews`/`images`/`materials`, BIN
  chunk length is 4-byte aligned, the embedded JPEG bytes round-trip.
- `test_obj_writer` — `write_obj` output has `mtllib`, `v`/`vt`/`f` lines,
  `tex/node_*.jpg` written, faces are 1-based.
- `test_anchor_and_yup_mapping` — a known EPSG:25832 vertex → expected
  anchor-relative + Y-up glTF coordinate.
- `@pytest.mark.needs_network test_live_cutout_small_aoi` — skipped unless
  `DOMMESH_LIVE` env set; tiny AOI in a known Los (e.g. the spike's Auerbach
  rectangle as a 4-point polygon) → non-empty mesh, `bytes_fetched < 30 MB`,
  `meta.json` well-formed. Register the `needs_network` marker in a small
  `OpenMap_Unifier/conftest.py` (auto-skip when the env var is unset), mirroring
  how the blender-tools repo gates `needs_gdal`.

### `openmap_blender_tools/tests/test_dommesh_import.py` (new) + smoke

- Pure-Python: `read_dommesh_meta` parses a fixture `meta.json`;
  `anchor_offset` math (scene anchor present vs absent).
- bpy-dependent (mock-bpy fixture, same pattern as `test_citygml_import.py`):
  `import_dommesh_glb` calls `bpy.ops.import_scene.gltf` with the right path,
  parents under a `DOM-Mesh` empty, applies the translation, sets sRGB on
  textures.
- `tests/test_features_registry.py` / operator-registration test already iterates
  the `classes` tuple — extend its expected set with `OBT_OT_import_dommesh`.
- New `tests/smoke_dommesh.py` (needs Blender): build a tiny `cutout.glb` +
  `meta.json` fixture in a temp dir, run the operator headless, assert objects
  imported and positioned. Documented in CLAUDE.md's smoke-test list, not run by
  default.

### Commands (added to / consistent with CLAUDE.md)

```powershell
# UU unit tests (pure-Python; live test gated by $env:DOMMESH_LIVE)
& "C:\ProgramData\anaconda3\python.exe" -m pytest OpenMap_Unifier/test_dommesh.py -v
# blender-tools unit tests
& "C:\ProgramData\anaconda3\python.exe" -m pytest openmap_blender_tools/tests/test_dommesh_import.py -v
# smoke (needs Blender 5.1)
& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" --background --python openmap_blender_tools/tests/smoke_dommesh.py
```

## Out of scope

- Draco geometry variant (`geometries/1.bin.gz`) — the uncompressed one is right
  next to it and smaller to handle.
- Texture atlas / mesh merge — keep one material per node texture.
- Parallelising the ZIP-central-directory / nodepages reads themselves (they're
  one big sequential read each and then cached).
- A typed lat/lon or center+radius input — KML polygon only, like the rest of UU.
- Any change to the non-LLM production path beyond what's described here.

## Submodule / repo workflow

- `OpenMap_Unifier` and `openmap_blender_tools` are git submodules: commit + push
  inside each, then `git add <submodule>` in the parent and commit as
  `chore: bump <submodule> (<what changed>)`, then push the parent.
- `experiments/dommesh_cutout/` (parent repo) stays as-is — the spike/docs.
- This design doc lives in the parent repo at
  `docs/superpowers/specs/2026-05-12-dommesh-cutout-integration-design.md`.

## Implementation order (for the plan)

1. `backend/dommesh.py` skeleton + pure-Python pieces (ZIP64 parse, geometry
   decode, Los lookup, polygon clip, OBJ writer, GLB writer) — TDD against
   `test_dommesh.py`.
2. `SlpkReader` range I/O + caching; wire `cutout()` end to end; run the
   `DOMMESH_LIVE` test once manually.
3. Catalog entry + GUI dispatch branch + folder map.
4. Web endpoint + page button/handler.
5. `openmap_blender_tools/dommesh_import.py` + operator + registration; TDD
   against `test_dommesh_import.py`; smoke test.
6. CLAUDE.md test-command updates; commit submodules; bump parent.
</content>
