# DOM-Mesh Cutout Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user feed a Google Earth KML polygon into OpenMap_Unifier (GUI + web app) and get back a small Blender-ready slice (`.obj` + `.glb` + `meta.json`) of Bayern's DOM-Mesh photogrammetry mesh, range-fetched from the per-Los `DSM_Mesh.slpk`; plus a `openmap_blender_tools` operator that imports that slice into the anchored scene.

**Architecture:** New self-contained module `OpenMap_Unifier/backend/dommesh.py` (a `LosIndex` that picks the flight-day "Los" from the AOI, a `SlpkReader` that HTTP-Range-reads the ZIP64/I3S archive with per-Los caching, and a `cutout()` that fetches+decodes+clips overlapping I3S leaf nodes and writes OBJ/GLB). Wired into the GUI via a new `kind: "mesh"` catalog entry, into the web app via a `/start-download-dommesh` endpoint, and into the Blender addon via a new `dommesh_import.py` + `blender_tools.import_dommesh` operator. No new third-party wheels — stdlib + already-vendored numpy/shapely/pyproj/Pillow/requests; the GLB writer is hand-rolled.

**Tech Stack:** Python 3.10+ (`from __future__ import annotations` in every new module), `urllib.request` for Range I/O, `struct`/`gzip`/`zlib`/`json` for ZIP64+I3S decode, `shapely`+`pyproj` for the AOI math, pytest for tests, Blender 5.1 bpy for the addon side.

**Reference material the engineer should read first:**
- `experiments/dommesh_cutout/README.md` — the proven spike (format facts, the working `cutout.py`/`slpklib.py`/`slpk_index.py`).
- `docs/superpowers/specs/2026-05-12-dommesh-cutout-integration-design.md` — this plan's spec.
- `OpenMap_Unifier/backend/downloader.py` — the `BAYERN_DATASETS` catalog and the raw/wms download patterns being extended.
- `OpenMap_Unifier/gui.py` lines ~155–185 (checkbox loop), ~310–325 (`DOWNLOAD_FOLDERS`), ~630–760 (`start_bayern_download`, `_estimate_bayern_download`, `_run_bayern_raw`, `_run_bayern_wms`).
- `OpenMap_Unifier/app.py` — `/start-download-relief` + `run_relief_download` (the pattern to mirror).
- `openmap_blender_tools/citygml_import.py` + `operators.py` (`OBT_OT_*` classes, `_get_scene_anchor`, the `import_heightmap` anchor-seeding pattern) + `tests/test_citygml_import.py` (mock-bpy test pattern).

**Conventions:** Both submodules (`OpenMap_Unifier`, `openmap_blender_tools`) are git repos of their own — commit + push inside the submodule, then `git add <submodule>` in the parent and commit `chore: bump <submodule> (...)`, then push the parent. Commit messages end with `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`. Run tests with `& "C:\ProgramData\anaconda3\python.exe" -m pytest ...` (PowerShell). Never modify an existing test to make it pass.

---

## Phase 1 — `backend/dommesh.py`: pure-Python core (TDD)

All Phase-1 work is inside the `OpenMap_Unifier` submodule. Create the module file first so imports resolve.

### Task 1: Module skeleton + ZIP64 central-directory parse

**Files:**
- Create: `OpenMap_Unifier/backend/dommesh.py`
- Create: `OpenMap_Unifier/test_dommesh.py`

- [ ] **Step 1: Write the failing test**

Create `OpenMap_Unifier/test_dommesh.py`:

```python
"""Unit tests for backend/dommesh.py — DOM-Mesh SLPK cutout.

Pure-Python pieces are fully tested without network. The one network test is
marked `needs_network` and skipped unless the DOMMESH_LIVE env var is set
(see conftest.py).
"""
from __future__ import annotations

import io
import json
import struct
import zipfile
from pathlib import Path

import pytest

from backend import dommesh


def _build_zip64(names_and_blobs):
    """Build an in-memory ZIP (force ZIP64) and return (bytes, {name: stored_size})."""
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_STORED, allowZip64=True) as zf:
        for name, blob in names_and_blobs:
            zf.writestr(name, blob)
    return buf.getvalue(), {n: len(b) for n, b in names_and_blobs}


def test_parse_central_directory_returns_offsets_and_sizes():
    raw, sizes = _build_zip64([
        ("3dSceneLayer.json.gz", b"\x1f\x8b" + b"x" * 50),
        ("nodes/0/geometries/0.bin.gz", b"\x1f\x8b" + b"y" * 120),
    ])
    entries = dommesh.parse_central_directory(raw)
    assert set(entries) == {"3dSceneLayer.json.gz", "nodes/0/geometries/0.bin.gz"}
    for name, (offset, csize, usize, method) in entries.items():
        assert csize == sizes[name]
        assert method == 0  # ZIP_STORED
        # The local file header at `offset` starts with the PK\x03\x04 signature.
        assert raw[offset:offset + 4] == b"PK\x03\x04"
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: FAIL — `ImportError`/`AttributeError` (no `backend.dommesh` / no `parse_central_directory`).

- [ ] **Step 3: Write minimal implementation**

Create `OpenMap_Unifier/backend/dommesh.py`:

```python
"""DOM-Mesh (Bayern, pn=dommesh) polygon cutout.

Given a Google Earth KML polygon (EWKT, WGS84), pick the flight-day "Los",
HTTP-Range-read only the I3S leaf nodes of that Los's DSM_Mesh.slpk that
overlap the polygon, decode the uncompressed I3S triangle geometry + JPEG
textures, clip to the polygon, and write a Blender-ready OBJ and/or GLB.

Proven facts (see experiments/dommesh_cutout/README.md):
- SLPK = ZIP64 of I3S 1.9 meshpyramids; download{1,2}.bayernwolke.de honour
  HTTP Range (206 Partial Content).
- OBB centers/halfSizes are in EPSG:25832, identity quaternions -> AOI filter
  is a 2D AABB test.
- Geometry nodes/<res>/geometries/0.bin.gz: u32 vertexCount + u32 featureCount,
  then positions f32x3 (relative to OBB center), then uv0 f32x2; triangle soup.
- Texture nodes/<res>/textures/0.jpg: plain JPEG, UV in [0,1] (flip V for OBJ).
"""
from __future__ import annotations

import gzip
import json
import math
import os
import struct
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Callable, Optional

LOS_INDEX_KML_URL = (
    "https://geodaten.bayern.de/odd/m/3/daten/DOMMesh/DOM_Mesh_projektgebiete_2026.kml"
)
SLPK_MIRRORS = ("https://download1.bayernwolke.de", "https://download2.bayernwolke.de")


# --------------------------------------------------------------------------- #
# ZIP64 central directory                                                      #
# --------------------------------------------------------------------------- #
_EOCD64_LOCATOR_SIG = b"PK\x06\x07"
_EOCD64_SIG = b"PK\x06\x06"
_EOCD_SIG = b"PK\x05\x06"
_CD_SIG = b"PK\x01\x02"


def parse_central_directory(raw: bytes) -> dict[str, tuple[int, int, int, int]]:
    """Parse a (ZIP64) central directory blob -> {name: (local_offset, csize, usize, method)}.

    `raw` must contain at least the central directory and the end-of-central-
    directory records (i.e. the tail of the archive). Offsets are absolute into
    the original archive, so when you only fetched the tail you must pass the
    tail's start offset to the caller — here we assume `raw` is the whole file
    OR the caller has already aligned offsets (SlpkReader passes the tail and
    fixes offsets itself; tests pass the whole file).
    """
    # Find the (ZIP64) EOCD locator near the end.
    loc = raw.rfind(_EOCD64_LOCATOR_SIG)
    if loc != -1:
        eocd64_off = struct.unpack("<Q", raw[loc + 8:loc + 16])[0]
        # eocd64_off is absolute in the real archive; when raw is the whole
        # file this is a valid index. Locate the EOCD64 record.
        rec = raw.find(_EOCD64_SIG, max(0, loc - (1 << 20)))
        if rec == -1:
            rec = raw.find(_EOCD64_SIG)
        cd_size = struct.unpack("<Q", raw[rec + 40:rec + 48])[0]
        cd_off = struct.unpack("<Q", raw[rec + 48:rec + 56])[0]
    else:
        e = raw.rfind(_EOCD_SIG)
        cd_size = struct.unpack("<I", raw[e + 12:e + 16])[0]
        cd_off = struct.unpack("<I", raw[e + 16:e + 20])[0]
    # The central directory lives at cd_off..cd_off+cd_size within `raw`.
    cd = raw[cd_off:cd_off + cd_size]
    return _parse_cd_records(cd)


def _parse_cd_records(cd: bytes) -> dict[str, tuple[int, int, int, int]]:
    out: dict[str, tuple[int, int, int, int]] = {}
    p = 0
    while p + 4 <= len(cd) and cd[p:p + 4] == _CD_SIG:
        method = struct.unpack("<H", cd[p + 10:p + 12])[0]
        csize = struct.unpack("<I", cd[p + 20:p + 24])[0]
        usize = struct.unpack("<I", cd[p + 24:p + 28])[0]
        fnlen = struct.unpack("<H", cd[p + 28:p + 30])[0]
        eflen = struct.unpack("<H", cd[p + 30:p + 32])[0]
        cmlen = struct.unpack("<H", cd[p + 32:p + 34])[0]
        lho = struct.unpack("<I", cd[p + 42:p + 46])[0]
        name = cd[p + 46:p + 46 + fnlen].decode("utf-8", "replace")
        extra = cd[p + 46 + fnlen:p + 46 + fnlen + eflen]
        # ZIP64 extra field (id 0x0001): replaces 0xFFFFFFFF placeholders, in
        # the fixed order usize, csize, local-header-offset (only those that
        # were 0xFFFFFFFF are present).
        if (csize == 0xFFFFFFFF or usize == 0xFFFFFFFF or lho == 0xFFFFFFFF) and extra:
            q = 0
            while q + 4 <= len(extra):
                hid, hsz = struct.unpack("<HH", extra[q:q + 4])
                if hid == 0x0001:
                    vals = extra[q + 4:q + 4 + hsz]
                    vi = 0
                    if usize == 0xFFFFFFFF:
                        usize = struct.unpack("<Q", vals[vi:vi + 8])[0]; vi += 8
                    if csize == 0xFFFFFFFF:
                        csize = struct.unpack("<Q", vals[vi:vi + 8])[0]; vi += 8
                    if lho == 0xFFFFFFFF:
                        lho = struct.unpack("<Q", vals[vi:vi + 8])[0]; vi += 8
                    break
                q += 4 + hsz
        out[name] = (lho, csize, usize, method)
        p += 46 + fnlen + eflen + cmlen
    return out
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS.

- [ ] **Step 5: Commit (inside the submodule)**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): ZIP64 central-directory parser

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: I3S geometry decode

**Files:**
- Modify: `OpenMap_Unifier/backend/dommesh.py`
- Modify: `OpenMap_Unifier/test_dommesh.py`

- [ ] **Step 1: Write the failing test** — append to `test_dommesh.py`:

```python
def test_decode_geometry_reads_positions_and_uvs():
    verts = [(1.0, 2.0, 3.0), (4.0, 5.0, 6.0), (7.0, 8.0, 9.0)]
    uvs = [(0.1, 0.2), (0.3, 0.4), (0.5, 0.6)]
    blob = struct.pack("<II", len(verts), 1)
    for x, y, z in verts:
        blob += struct.pack("<fff", x, y, z)
    for u, v in uvs:
        blob += struct.pack("<ff", u, v)
    vcount, pos, uv = dommesh.decode_geometry(blob)
    assert vcount == 3
    assert pos == pytest.approx([c for vtx in verts for c in vtx])
    assert uv == pytest.approx([c for t in uvs for c in t])
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py::test_decode_geometry_reads_positions_and_uvs -v`
Expected: FAIL — `AttributeError: module 'backend.dommesh' has no attribute 'decode_geometry'`.

- [ ] **Step 3: Write minimal implementation** — add to `backend/dommesh.py`:

```python
# --------------------------------------------------------------------------- #
# I3S geometry decode                                                          #
# --------------------------------------------------------------------------- #
def decode_geometry(blob: bytes) -> tuple[int, list[float], list[float]]:
    """I3S meshpyramids PerAttributeArray, ordering [position(f32x3), uv0(f32x2)].

    Returns (vertex_count, flat_positions, flat_uvs). Non-indexed triangle soup
    -> vertex_count is a multiple of 3 (triangle i = vertices 3i, 3i+1, 3i+2).
    """
    vcount, _fcount = struct.unpack("<II", blob[:8])
    p = 8
    pos = list(struct.unpack("<%df" % (vcount * 3), blob[p:p + vcount * 12]))
    p += vcount * 12
    uv = list(struct.unpack("<%df" % (vcount * 2), blob[p:p + vcount * 8]))
    return vcount, pos, uv
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS (2 passing now).

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): I3S triangle-soup geometry decoder

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: AOI math — WKT→EPSG:25832 polygon, AABB overlap, triangle clip

**Files:**
- Modify: `OpenMap_Unifier/backend/dommesh.py`
- Modify: `OpenMap_Unifier/test_dommesh.py`

- [ ] **Step 1: Write the failing test** — append:

```python
def test_polygon_from_ewkt_projects_to_utm32():
    # A small square near Auerbach i.d.OPf. roughly (lon 11.6, lat 49.7).
    ewkt = ("SRID=4326;POLYGON((11.60 49.70, 11.61 49.70, 11.61 49.71, "
            "11.60 49.71, 11.60 49.70))")
    poly = dommesh.polygon_from_ewkt(ewkt)
    minx, miny, maxx, maxy = poly.bounds
    # EPSG:25832 easting in the 690 km range, northing ~5.5 Mm.
    assert 680_000 < minx < 700_000
    assert 5_500_000 < miny < 5_520_000
    assert maxx > minx and maxy > miny


def test_polygon_from_ewkt_drops_z():
    ewkt = "SRID=4326;POLYGON Z((11.6 49.7 0, 11.61 49.7 0, 11.61 49.71 0, 11.6 49.7 0))"
    poly = dommesh.polygon_from_ewkt(ewkt)
    assert poly.is_valid


def test_aabb_overlaps_bbox():
    node = {"cx": 100.0, "cy": 200.0, "hx": 10.0, "hy": 10.0}
    assert dommesh.aabb_overlaps(node, (95, 195, 105, 205))
    assert dommesh.aabb_overlaps(node, (90, 190, 95, 195))      # touching corner
    assert not dommesh.aabb_overlaps(node, (200, 200, 300, 300))


def test_clip_triangles_to_polygon_keeps_inside_centroids():
    from shapely.geometry import Polygon
    square = Polygon([(0, 0), (10, 0), (10, 10), (0, 10)])
    # vertices: world (x, y, z), one triangle inside, one outside.
    wx = [1, 2, 1,   100, 101, 100]
    wy = [1, 1, 2,   100, 100, 101]
    wz = [0, 0, 0,   0, 0, 0]
    uv = [0.0] * 12
    tris, used, remap = dommesh.clip_triangles(wx, wy, square)
    assert tris == [(0, 1, 2)]
    assert used == [0, 1, 2]
    assert remap == {0: 0, 1: 1, 2: 2}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -k "polygon_from_ewkt or aabb or clip_triangles" -v`
Expected: FAIL — those attributes don't exist yet.

- [ ] **Step 3: Write minimal implementation** — add to `backend/dommesh.py`:

```python
# --------------------------------------------------------------------------- #
# AOI math                                                                     #
# --------------------------------------------------------------------------- #
def polygon_from_ewkt(ewkt: str):
    """Parse `[SRID=4326;]POLYGON((...))` (WGS84, possibly with Z) and return a
    shapely Polygon in EPSG:25832 (X=easting, Y=northing, Z dropped)."""
    from shapely.wkt import loads as _wkt_loads
    from shapely.geometry import Polygon
    from pyproj import Transformer

    if ";" in ewkt:
        ewkt = ewkt.split(";", 1)[1]
    poly = _wkt_loads(ewkt)
    tf = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True)
    # Index c[0]/c[1] so POLYGON Z(...) (Google Earth always adds altitude)
    # survives — we work purely in 2D on the UTM grid.
    return Polygon([tf.transform(c[0], c[1]) for c in poly.exterior.coords])


def aabb_overlaps(node: dict, bbox: tuple[float, float, float, float]) -> bool:
    """2D AABB-vs-AABB overlap (inclusive). `node` has cx,cy,hx,hy; bbox is
    (minx, miny, maxx, maxy)."""
    return (node["cx"] + node["hx"] >= bbox[0] and node["cx"] - node["hx"] <= bbox[2]
            and node["cy"] + node["hy"] >= bbox[1] and node["cy"] - node["hy"] <= bbox[3])


def clip_triangles(wx: list[float], wy: list[float], polygon):
    """Keep triangles (consecutive vertex triples) whose centroid lies inside
    `polygon` (shapely). Returns (tris, used_vertices, remap) where tris is a
    list of (i0,i1,i2) original indices, used_vertices is the sorted unique set,
    and remap maps original index -> compact index."""
    from shapely.geometry import Point

    tris: list[tuple[int, int, int]] = []
    for ti in range(len(wx) // 3):
        i0, i1, i2 = 3 * ti, 3 * ti + 1, 3 * ti + 2
        cx = (wx[i0] + wx[i1] + wx[i2]) / 3.0
        cy = (wy[i0] + wy[i1] + wy[i2]) / 3.0
        if polygon.covers(Point(cx, cy)):
            tris.append((i0, i1, i2))
    used = sorted({v for t in tris for v in t})
    remap = {v: n for n, v in enumerate(used)}
    return tris, used, remap
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS (all so far).

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): AOI math (EWKT->UTM polygon, AABB overlap, triangle clip)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Submesh container + OBJ writer

**Files:**
- Modify: `OpenMap_Unifier/backend/dommesh.py`
- Modify: `OpenMap_Unifier/test_dommesh.py`

- [ ] **Step 1: Write the failing test** — append:

```python
def _tiny_submesh(node_id=42):
    return dommesh.SubMesh(
        node_id=node_id,
        verts=[(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)],
        uvs=[(0.0, 0.0), (1.0, 0.0), (0.0, 1.0)],   # already V-flipped for OBJ/GLB
        tris=[(0, 1, 2)],
        jpeg=b"\xff\xd8\xff\xd9",                    # minimal JPEG SOI+EOI marker bytes
    )


def test_write_obj_emits_mtl_and_texture(tmp_path):
    dommesh.write_obj(str(tmp_path), [_tiny_submesh(7)], anchor=(690000.0, 5506000.0))
    obj = (tmp_path / "cutout.obj").read_text()
    assert obj.startswith("mtllib cutout.mtl")
    assert "o node_7" in obj
    assert "\nv 0.0000 0.0000 0.0000" in obj
    assert "\nvt 0.000000 0.000000" in obj
    assert "\nf 1/1 2/2 3/3" in obj            # 1-based indices
    mtl = (tmp_path / "cutout.mtl").read_text()
    assert "newmtl m7" in mtl and "map_Kd tex/node_7.jpg" in mtl
    assert (tmp_path / "tex" / "node_7.jpg").read_bytes() == b"\xff\xd8\xff\xd9"
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py::test_write_obj_emits_mtl_and_texture -v`
Expected: FAIL — no `SubMesh`, no `write_obj`.

- [ ] **Step 3: Write minimal implementation** — add to `backend/dommesh.py` (add `from dataclasses import dataclass, field` to the imports):

```python
# --------------------------------------------------------------------------- #
# Output: SubMesh + OBJ writer                                                 #
# --------------------------------------------------------------------------- #
@dataclass
class SubMesh:
    """One I3S leaf node's surviving geometry, ready to serialise.

    `verts` are anchor-relative EPSG:25832 (x=easting-anchorx, y=northing-anchory,
    z=height). `uvs` are already V-flipped (i.e. OBJ/GLB convention, origin
    bottom-left). `tris` index into `verts`. `jpeg` is the raw texture file.
    """
    node_id: int
    verts: list[tuple[float, float, float]]
    uvs: list[tuple[float, float]]
    tris: list[tuple[int, int, int]]
    jpeg: bytes


def write_obj(out_dir: str, submeshes: list[SubMesh], anchor: tuple[float, float]) -> None:
    """Write cutout.obj + cutout.mtl + tex/node_<id>.jpg. `anchor` is only
    recorded indirectly (verts are already anchor-relative); it is unused here
    but kept in the signature for symmetry with write_glb / meta.json."""
    tex_dir = os.path.join(out_dir, "tex")
    os.makedirs(tex_dir, exist_ok=True)
    obj = ["mtllib cutout.mtl"]
    mtl: list[str] = []
    vbase = 0
    for sm in submeshes:
        texname = f"node_{sm.node_id}.jpg"
        with open(os.path.join(tex_dir, texname), "wb") as fh:
            fh.write(sm.jpeg)
        mname = f"m{sm.node_id}"
        mtl += [f"newmtl {mname}", "Ka 1 1 1", "Kd 1 1 1", "d 1", "illum 1",
                f"map_Kd tex/{texname}", ""]
        obj.append(f"o node_{sm.node_id}")
        for x, y, z in sm.verts:
            obj.append("v %.4f %.4f %.4f" % (x, y, z))
        for u, v in sm.uvs:
            obj.append("vt %.6f %.6f" % (u, v))
        obj.append(f"usemtl {mname}")
        for a, b, c in sm.tris:
            ia, ib, ic = vbase + a + 1, vbase + b + 1, vbase + c + 1
            obj.append(f"f {ia}/{ia} {ib}/{ib} {ic}/{ic}")
        vbase += len(sm.verts)
    with open(os.path.join(out_dir, "cutout.obj"), "w") as fh:
        fh.write("\n".join(obj) + "\n")
    with open(os.path.join(out_dir, "cutout.mtl"), "w") as fh:
        fh.write("\n".join(mtl) + "\n")
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): SubMesh dataclass + OBJ/MTL writer

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: GLB writer (binary glTF 2.0, embedded JPEGs, Y-up)

**Files:**
- Modify: `OpenMap_Unifier/backend/dommesh.py`
- Modify: `OpenMap_Unifier/test_dommesh.py`

- [ ] **Step 1: Write the failing test** — append:

```python
def _parse_glb(data: bytes):
    magic, version, length = struct.unpack("<4sII", data[:12])
    assert magic == b"glTF" and version == 2 and length == len(data)
    p = 12
    chunks = []
    while p < len(data):
        clen, ctype = struct.unpack("<I4s", data[p:p + 8]); p += 8
        chunks.append((ctype, data[p:p + clen])); p += clen
    return chunks


def test_write_glb_structure_and_roundtrip(tmp_path):
    out = tmp_path / "cutout.glb"
    dommesh.write_glb(str(out), [_tiny_submesh(3), _tiny_submesh(4)],
                      anchor=(690000.0, 5506000.0))
    data = out.read_bytes()
    chunks = _parse_glb(data)
    assert chunks[0][0] == b"JSON"
    assert chunks[1][0] == b"BIN\x00"
    assert len(chunks[1][1]) % 4 == 0          # BIN chunk is 4-byte aligned
    gltf = json.loads(chunks[0][1])
    assert gltf["asset"]["version"] == "2.0"
    assert len(gltf["meshes"]) == 2 and len(gltf["nodes"]) == 2
    assert len(gltf["images"]) == 2 and len(gltf["materials"]) == 2
    assert len(gltf["scenes"]) == 1 and set(gltf["scenes"][0]["nodes"]) == {0, 1}
    # accessors: 3 per submesh (POSITION, TEXCOORD_0, indices) -> 6 total
    assert len(gltf["accessors"]) == 6
    # The embedded JPEG bytes survive: find an image bufferView and slice the BIN.
    bv = gltf["bufferViews"][gltf["images"][0]["bufferView"]]
    blob = chunks[1][1][bv["byteOffset"]:bv["byteOffset"] + bv["byteLength"]]
    assert blob == b"\xff\xd8\xff\xd9"


def test_write_glb_maps_to_yup():
    # POSITION in glTF must be (easting, height, -northing); verts here are
    # already anchor-relative, so vert (1, 2, 3) -> (1, 3, -2).
    sm = dommesh.SubMesh(node_id=1, verts=[(1.0, 2.0, 3.0), (0, 0, 0), (0, 0, 0)],
                         uvs=[(0, 0)] * 3, tris=[(0, 1, 2)], jpeg=b"\xff\xd8\xff\xd9")
    import tempfile, os as _os
    path = _os.path.join(tempfile.mkdtemp(), "g.glb")
    dommesh.write_glb(path, [sm], anchor=(0.0, 0.0))
    chunks = _parse_glb(open(path, "rb").read())
    gltf = json.loads(chunks[0][1])
    pos_acc = gltf["meshes"][0]["primitives"][0]["attributes"]["POSITION"]
    acc = gltf["accessors"][pos_acc]
    bv = gltf["bufferViews"][acc["bufferView"]]
    raw = chunks[1][1][bv["byteOffset"]:bv["byteOffset"] + bv["byteLength"]]
    first = struct.unpack("<fff", raw[:12])
    assert first == pytest.approx((1.0, 3.0, -2.0))
    # accessor min/max present (glTF validators require it for POSITION).
    assert "min" in acc and "max" in acc
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -k glb -v`
Expected: FAIL — no `write_glb`.

- [ ] **Step 3: Write minimal implementation** — add to `backend/dommesh.py` (add `import base64` to imports if you prefer; not needed — we embed binary, no data URIs):

```python
# --------------------------------------------------------------------------- #
# Output: GLB writer (binary glTF 2.0)                                         #
# --------------------------------------------------------------------------- #
def _pad4(b: bytes, fill: bytes = b"\x00") -> bytes:
    return b + fill * ((4 - len(b) % 4) % 4)


def write_glb(out_path: str, submeshes: list[SubMesh], anchor: tuple[float, float]) -> None:
    """Write a single binary glTF 2.0 file. One mesh/material/image/node per
    submesh. POSITION is (easting, height, -northing) so the model is Y-up like
    every other glTF (Blender's importer applies its own Z-up correction)."""
    bin_parts: list[bytes] = []
    bin_len = 0
    buffer_views: list[dict] = []
    accessors: list[dict] = []
    images: list[dict] = []
    samplers = [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}]
    textures: list[dict] = []
    materials: list[dict] = []
    meshes: list[dict] = []
    nodes: list[dict] = []

    def add_view(blob: bytes, target: Optional[int] = None) -> int:
        nonlocal bin_len
        blob = _pad4(blob)
        bv = {"buffer": 0, "byteOffset": bin_len, "byteLength": len(blob)}
        if target is not None:
            bv["target"] = target
        buffer_views.append(bv)
        bin_parts.append(blob)
        bin_len += len(blob)
        return len(buffer_views) - 1

    for sm in submeshes:
        # ---- index buffer (u32) ----
        idx = b"".join(struct.pack("<III", a, b, c) for a, b, c in sm.tris)
        idx_bv = add_view(idx, target=34963)  # ELEMENT_ARRAY_BUFFER
        idx_count = len(sm.tris) * 3
        accessors.append({"bufferView": idx_bv, "componentType": 5125,  # UNSIGNED_INT
                          "count": idx_count, "type": "SCALAR"})
        idx_acc = len(accessors) - 1
        # ---- POSITION (f32x3, Y-up) ----
        ys = [(e, z, -n) for (e, n, z) in sm.verts]
        pos = b"".join(struct.pack("<fff", *v) for v in ys)
        pos_bv = add_view(pos, target=34962)  # ARRAY_BUFFER
        mins = [min(c[i] for c in ys) for i in range(3)]
        maxs = [max(c[i] for c in ys) for i in range(3)]
        accessors.append({"bufferView": pos_bv, "componentType": 5126,  # FLOAT
                          "count": len(ys), "type": "VEC3", "min": mins, "max": maxs})
        pos_acc = len(accessors) - 1
        # ---- TEXCOORD_0 (f32x2) ----
        uvb = b"".join(struct.pack("<ff", u, v) for u, v in sm.uvs)
        uv_bv = add_view(uvb, target=34962)
        accessors.append({"bufferView": uv_bv, "componentType": 5126,
                          "count": len(sm.uvs), "type": "VEC2"})
        uv_acc = len(accessors) - 1
        # ---- texture image ----
        img_bv = add_view(sm.jpeg)
        images.append({"bufferView": img_bv, "mimeType": "image/jpeg",
                       "name": f"node_{sm.node_id}"})
        textures.append({"sampler": 0, "source": len(images) - 1})
        materials.append({"name": f"m{sm.node_id}", "doubleSided": True,
                          "pbrMetallicRoughness": {
                              "baseColorTexture": {"index": len(textures) - 1},
                              "metallicFactor": 0.0, "roughnessFactor": 1.0}})
        meshes.append({"name": f"node_{sm.node_id}", "primitives": [{
            "attributes": {"POSITION": pos_acc, "TEXCOORD_0": uv_acc},
            "indices": idx_acc, "material": len(materials) - 1, "mode": 4}]})
        nodes.append({"name": f"node_{sm.node_id}", "mesh": len(meshes) - 1})

    bin_blob = _pad4(b"".join(bin_parts))
    gltf = {
        "asset": {"version": "2.0", "generator": "OpenMap_Unifier dommesh"},
        "extras": {"anchor_epsg25832": list(anchor)},
        "scene": 0, "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes, "meshes": meshes,
        "materials": materials, "textures": textures, "images": images,
        "samplers": samplers, "accessors": accessors, "bufferViews": buffer_views,
        "buffers": [{"byteLength": len(bin_blob)}],
    }
    json_blob = _pad4(json.dumps(gltf, separators=(",", ":")).encode("utf-8"), b" ")
    total = 12 + 8 + len(json_blob) + 8 + len(bin_blob)
    with open(out_path, "wb") as fh:
        fh.write(struct.pack("<4sII", b"glTF", 2, total))
        fh.write(struct.pack("<I4s", len(json_blob), b"JSON")); fh.write(json_blob)
        fh.write(struct.pack("<I4s", len(bin_blob), b"BIN\x00")); fh.write(bin_blob)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): hand-rolled binary glTF 2.0 (GLB) writer

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: `LosIndex` — point → flight-day Los from the project-areas KML

**Files:**
- Modify: `OpenMap_Unifier/backend/dommesh.py`
- Modify: `OpenMap_Unifier/test_dommesh.py`

KML structure to parse: a `<Document>` of `<Placemark>`s; each has a `<name>` (the Los id, e.g. `125023_0`) and a `<Polygon><outerBoundaryIs><LinearRing><coordinates>` of `lon,lat,alt` triples (WGS84). We parse with `xml.etree.ElementTree`, stripping namespaces by matching on tag suffixes (same trick `backend/geometry.py` already uses).

- [ ] **Step 1: Write the failing test** — append:

```python
_FAKE_LOS_KML = """<?xml version="1.0" encoding="UTF-8"?>
<kml xmlns="http://www.opengis.net/kml/2.2"><Document>
 <Placemark><name>111111_0</name><Polygon><outerBoundaryIs><LinearRing>
   <coordinates>11.50,49.60,0 11.70,49.60,0 11.70,49.80,0 11.50,49.80,0 11.50,49.60,0</coordinates>
 </LinearRing></outerBoundaryIs></Polygon></Placemark>
 <Placemark><name>222222_0</name><Polygon><outerBoundaryIs><LinearRing>
   <coordinates>12.00,50.00,0 12.10,50.00,0 12.10,50.10,0 12.00,50.10,0 12.00,50.00,0</coordinates>
 </LinearRing></outerBoundaryIs></Polygon></Placemark>
</Document></kml>"""


def test_los_index_point_in_polygon(tmp_path):
    kml_path = tmp_path / "los.kml"
    kml_path.write_text(_FAKE_LOS_KML)
    idx = dommesh.LosIndex(cached_kml_path=str(kml_path))
    # A point near (11.6, 49.7) -> EPSG:25832 roughly (690k, 5506k). Use the
    # transformer to find a coordinate inside the first polygon.
    from pyproj import Transformer
    e, n = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True).transform(11.6, 49.7)
    assert idx.los_ids_for_point(e, n) == ["111111_0"]
    e2, n2 = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True).transform(11.6, 60.0)
    assert idx.los_ids_for_point(e2, n2) == []
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -k los_index -v`
Expected: FAIL — no `LosIndex`.

- [ ] **Step 3: Write minimal implementation** — add to `backend/dommesh.py` (add `import xml.etree.ElementTree as ET` to imports):

```python
# --------------------------------------------------------------------------- #
# LosIndex — which flight-day Los covers a point                               #
# --------------------------------------------------------------------------- #
def _http_get(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "OpenMap_Unifier/dommesh"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


class LosIndex:
    """The DOM-Mesh project-areas KML, parsed into (los_id, shapely-Polygon-in-25832).

    Pass `cached_kml_path` to load a local copy (and to cache a downloaded one);
    if it doesn't exist and `download=True`, the KML is fetched once from
    LOS_INDEX_KML_URL and written there.
    """
    def __init__(self, cached_kml_path: Optional[str] = None, download: bool = False):
        if cached_kml_path and os.path.exists(cached_kml_path):
            raw = Path(cached_kml_path).read_bytes()
        elif download:
            raw = _http_get(LOS_INDEX_KML_URL)
            if cached_kml_path:
                os.makedirs(os.path.dirname(cached_kml_path) or ".", exist_ok=True)
                Path(cached_kml_path).write_bytes(raw)
        else:
            raise FileNotFoundError(
                "Los index KML not available locally and download=False")
        self._polys = self._parse(raw)

    @staticmethod
    def _parse(raw: bytes):
        from shapely.geometry import Polygon
        from pyproj import Transformer
        tf = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True)
        root = ET.fromstring(raw)
        out = []
        for pm in (e for e in root.iter() if e.tag.endswith("Placemark")):
            name = next((c.text.strip() for c in pm.iter()
                         if c.tag.endswith("name") and c.text), None)
            coords_text = next((c.text.strip() for c in pm.iter()
                                if c.tag.endswith("coordinates") and c.text), None)
            if not name or not coords_text:
                continue
            pts = []
            for token in coords_text.split():
                parts = token.split(",")
                if len(parts) >= 2:
                    pts.append(tf.transform(float(parts[0]), float(parts[1])))
            if len(pts) >= 3:
                out.append((name, Polygon(pts)))
        return out

    def los_ids_for_point(self, easting: float, northing: float) -> list[str]:
        from shapely.geometry import Point
        p = Point(easting, northing)
        return [name for name, poly in self._polys if poly.covers(p)]
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): LosIndex — point -> flight-day Los from project-areas KML

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 2 — `SlpkReader` (network I/O + caching) and `cutout()`

### Task 7: `SlpkReader` — Range I/O, entries(), nodes(), read_entry()

**Files:**
- Modify: `OpenMap_Unifier/backend/dommesh.py`
- Modify: `OpenMap_Unifier/test_dommesh.py` (only a small unit test for the local-header offset math; the heavy lifting is exercised by the `needs_network` test in Task 10)

Notes for the engineer:
- The ZIP64 EOCD locator sits in the **last ~64 bytes** of the archive, the EOCD64 record just before it, and the central directory just before that. The spike's `probe1.py`/`probe2.py` did this in two steps; here, fetch the **last 70 MB** of the file (`Range: bytes=-73400320`) in one go — that comfortably covers the ~62 MB central directory + the EOCD records observed on the test Los — then run `parse_central_directory` on it **after fixing offsets**: the bytes you got start at `file_size - len(tail)`, so the absolute local-header offsets must have that base subtracted to index into `tail`. Implementation: get `file_size` from a `HEAD`/`Content-Range`, compute `base = file_size - len(tail)`, and in this reader call a variant that subtracts `base` from `cd_off`/`lho`. Keep `parse_central_directory` (whole-file form) as-is for the unit test; add `_entries_from_tail(tail, base)` here.
- Per-Los cache dir: `<cache_root>/<losid>/` with `entries.json` (the dict, JSON-encoded with string keys) and `nodes.json` (list of node dicts). `cache_root` defaults to `<download_dir>/.dommesh_cache`.
- `nodes()`: read `3dSceneLayer.json.gz` to learn `nodePages.nodesPerPage`, then read `nodepages/0.json.gz`, `nodepages/1.json.gz`, … until a page is missing. Each page's `nodes[]` entry has `index`, `obb` `{center:[x,y,z], halfSize:[hx,hy,hz], quaternion:[...]}`, `mesh.geometry.resource` (the `<res>` for `geometries/<res>/...`), `mesh.material.resource`, and either `children:[...]` or no children. Keep entries where `mesh` exists and `children` is empty/absent; store `{"i": index, "cx":..,"cy":..,"cz":.., "hx":..,"hy":..,"hz":.., "geom_res":.., "mat_res":..}`. (This matches the spike's `slpk_index.py` / `cutout.py`; if a field name differs in the live data, trust the live JSON and adjust.)

- [ ] **Step 1: Write the failing test** — append:

```python
def test_local_header_payload_offset():
    # A ZIP local file header is 30 bytes + filename + extra; the payload
    # follows. dommesh._payload_offset(local_header_bytes, local_offset) returns
    # the absolute byte offset of the stored data.
    name = b"nodes/0/geometries/0.bin.gz"
    extra = b"\x01\x00\x08\x00" + b"\x00" * 8
    hdr = b"PK\x03\x04" + b"\x00" * 22 + struct.pack("<HH", len(name), len(extra)) + name + extra
    assert dommesh._payload_offset(hdr, 1000) == 1000 + 30 + len(name) + len(extra)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py::test_local_header_payload_offset -v`
Expected: FAIL — no `_payload_offset`.

- [ ] **Step 3: Write minimal implementation** — add to `backend/dommesh.py`:

```python
# --------------------------------------------------------------------------- #
# SlpkReader — HTTP-Range reader for a per-Los DSM_Mesh.slpk                    #
# --------------------------------------------------------------------------- #
def _payload_offset(local_header: bytes, local_offset: int) -> int:
    """Given the first >=30 bytes of a ZIP local file header located at
    `local_offset`, return the absolute offset of the stored payload."""
    assert local_header[:4] == b"PK\x03\x04", local_header[:4]
    fnlen, eflen = struct.unpack("<HH", local_header[26:30])
    return local_offset + 30 + fnlen + eflen


def _entries_from_tail(tail: bytes, base: int) -> dict[str, tuple[int, int, int, int]]:
    """Like parse_central_directory but for an archive *tail* that starts at
    absolute offset `base`; all returned local-header offsets are absolute."""
    e = parse_central_directory(_RebaseBytes(tail, base))  # see note below
    return e


class _RebaseBytes:
    """Thin shim: behaves enough like `bytes` for parse_central_directory but
    rfind/find/slice are offset by `base` so EOCD offsets (absolute in the real
    archive) index correctly into the tail.

    Simpler than this shim: just inline a tail-aware copy of the parser. The
    engineer may replace `_entries_from_tail` with that inline version — the
    only contract is `SlpkReader.entries()` returns absolute offsets."""
    def __init__(self, data: bytes, base: int):
        self.data = data
        self.base = base
    # NOTE: implementing the full bytes protocol here is fiddly. PREFER to
    # delete _RebaseBytes and write _entries_from_tail as a direct copy of
    # _parse_cd_records that does: cd_off_abs = <from EOCD>; cd = tail[cd_off_abs - base : ...];
    # then for each record, lho stays absolute. Do that.


class SlpkReader:
    def __init__(self, losid: str, cache_root: str):
        self.losid = losid
        self.cache_dir = os.path.join(cache_root, losid)
        os.makedirs(self.cache_dir, exist_ok=True)
        self._mirrors = [f"{m}/p/dom-mesh-slpk/{losid}/DSM_Mesh.slpk" for m in SLPK_MIRRORS]
        self._size: Optional[int] = None
        self._entries: Optional[dict] = None
        self._nodes: Optional[list] = None
        self.bytes_fetched = 0

    # ---- low-level range I/O with mirror fallback ----
    def _request(self, headers: dict) -> tuple[bytes, dict]:
        last = None
        for url in self._mirrors:
            try:
                req = urllib.request.Request(url, headers={
                    "User-Agent": "OpenMap_Unifier/dommesh", **headers})
                with urllib.request.urlopen(req, timeout=120) as r:
                    data = r.read()
                    self.bytes_fetched += len(data)
                    return data, dict(r.headers)
            except Exception as ex:  # noqa: BLE001 - we genuinely want to try the next mirror
                last = ex
        raise RuntimeError(f"all mirrors failed for {self.losid}: {last}")

    def _rng(self, a: int, b: int) -> bytes:
        data, _ = self._request({"Range": f"bytes={a}-{b}"})
        return data

    def file_size(self) -> int:
        if self._size is None:
            data, hdrs = self._request({"Range": "bytes=0-0"})
            cr = hdrs.get("Content-Range", "")
            self._size = int(cr.split("/")[-1]) if "/" in cr else None
            if not self._size:
                raise RuntimeError("server did not report file size via Content-Range")
        return self._size

    # ---- entries (ZIP64 central directory), cached ----
    def entries(self) -> dict[str, tuple[int, int, int, int]]:
        if self._entries is not None:
            return self._entries
        cache = os.path.join(self.cache_dir, "entries.json")
        if os.path.exists(cache):
            raw = json.loads(Path(cache).read_text())
            self._entries = {k: tuple(v) for k, v in raw.items()}
            return self._entries
        size = self.file_size()
        tail_len = min(size, 70 * 1024 * 1024)   # comfortably covers CD + EOCD records
        base = size - tail_len
        tail = self._rng(base, size - 1)
        self._entries = _entries_from_tail(tail, base)
        Path(cache).write_text(json.dumps({k: list(v) for k, v in self._entries.items()}))
        return self._entries

    # ---- read one stored entry by name ----
    def read_entry(self, name: str) -> bytes:
        off, csize, _usize, _method = self.entries()[name]
        hdr = self._rng(off, off + 30 + 512)     # local header + filename + (small) extra
        ds = _payload_offset(hdr, off)
        data = self._rng(ds, ds + csize - 1)
        assert len(data) == csize, (name, len(data), csize)
        return gzip.decompress(data) if name.endswith(".gz") else data

    # ---- leaf node OBB list, cached ----
    def nodes(self) -> list[dict]:
        if self._nodes is not None:
            return self._nodes
        cache = os.path.join(self.cache_dir, "nodes.json")
        if os.path.exists(cache):
            self._nodes = json.loads(Path(cache).read_text())
            return self._nodes
        scene = json.loads(self.read_entry("3dSceneLayer.json.gz"))
        out: list[dict] = []
        page = 0
        while True:
            try:
                pg = json.loads(self.read_entry(f"nodepages/{page}.json.gz"))
            except Exception:
                break
            for nd in pg.get("nodes", []):
                if "mesh" not in nd or nd.get("children"):
                    continue
                obb = nd["obb"]
                c, h = obb["center"], obb["halfSize"]
                mesh = nd["mesh"]
                out.append({
                    "i": nd.get("index", nd.get("resourceId", page)),
                    "cx": c[0], "cy": c[1], "cz": c[2],
                    "hx": h[0], "hy": h[1], "hz": h[2],
                    "geom_res": mesh.get("geometry", {}).get("resource"),
                    "mat_res": mesh.get("material", {}).get("resource"),
                })
            page += 1
        # Carry the spatial-reference note from the scene layer for the record.
        self._nodes = out
        Path(cache).write_text(json.dumps(out))
        return self._nodes
```

> **Engineer note:** delete `_RebaseBytes`/the shim and implement `_entries_from_tail`
> as a tail-aware copy of `_parse_cd_records` (the comment in the code says how).
> Do NOT ship the shim. Verify against the live archive in Task 10.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS (the `_payload_offset` test; network-dependent paths are untested until Task 10).

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): SlpkReader (HTTP-Range ZIP64/I3S reader, per-Los cache)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: `cutout()` — the public entry point

**Files:**
- Modify: `OpenMap_Unifier/backend/dommesh.py`
- Modify: `OpenMap_Unifier/test_dommesh.py`

We unit-test `cutout()` with a **fake reader** injected via a parameter so no network is needed: `cutout(..., _reader_factory=..., _los_index=...)`. The real GUI/web callers use the defaults.

- [ ] **Step 1: Write the failing test** — append:

```python
class _FakeReader:
    """Stands in for SlpkReader: one leaf node, one triangle inside the AOI."""
    def __init__(self, *_a, **_k):
        self.bytes_fetched = 1234
    def nodes(self):
        return [{"i": 9, "cx": 690000.0, "cy": 5506000.0, "cz": 400.0,
                 "hx": 50.0, "hy": 50.0, "hz": 30.0, "geom_res": 0, "mat_res": 0}]
    def read_entry(self, name):
        if name.endswith(".jpg"):
            return b"\xff\xd8\xff\xd9"
        # geometry: a triangle whose vertices sit ~ at the node center (so its
        # centroid lands inside any AOI that contains the center).
        verts = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)]
        uvs = [(0.0, 0.0), (1.0, 0.0), (0.0, 1.0)]
        blob = struct.pack("<II", 3, 1)
        for x, y, z in verts:
            blob += struct.pack("<fff", x, y, z)
        for u, v in uvs:
            blob += struct.pack("<ff", u, v)
        return blob


class _FakeLosIndex:
    def __init__(self, *_a, **_k):
        pass
    def los_ids_for_point(self, e, n):
        return ["999999_0"]


def test_cutout_writes_obj_glb_meta(tmp_path):
    # A ~200 m square around the fake node center, in WGS84 EWKT.
    from pyproj import Transformer
    tf = Transformer.from_crs("EPSG:25832", "EPSG:4326", always_xy=True)
    cx, cy = 690000.0, 5506000.0
    corners = [(cx - 100, cy - 100), (cx + 100, cy - 100),
               (cx + 100, cy + 100), (cx - 100, cy + 100), (cx - 100, cy - 100)]
    ll = [tf.transform(x, y) for x, y in corners]
    ewkt = "SRID=4326;POLYGON((" + ", ".join(f"{lon} {lat}" for lon, lat in ll) + "))"

    progress_calls = []
    meta = dommesh.cutout(ewkt, str(tmp_path), formats=("obj", "glb"),
                          progress=lambda *a, **k: progress_calls.append(a),
                          _reader_factory=_FakeReader, _los_index_factory=_FakeLosIndex)
    assert (tmp_path / "cutout.obj").exists()
    assert (tmp_path / "cutout.glb").exists()
    assert (tmp_path / "meta.json").exists()
    assert meta["losid"] == "999999_0"
    assert meta["triangles"] == 1 and meta["leaf_nodes"] == 1
    assert "anchor_epsg25832" in meta and "bbox_epsg25832" in meta
    assert progress_calls  # at least one progress tick


def test_cutout_no_los_returns_error(tmp_path):
    class _Empty(_FakeLosIndex):
        def los_ids_for_point(self, e, n):
            return []
    meta = dommesh.cutout("SRID=4326;POLYGON((11.6 49.7, 11.61 49.7, 11.61 49.71, 11.6 49.7))",
                          str(tmp_path), _reader_factory=_FakeReader, _los_index_factory=_Empty)
    assert "error" in meta and "DOM-Mesh" in meta["error"]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -k cutout -v`
Expected: FAIL — no `cutout`.

- [ ] **Step 3: Write minimal implementation** — add to `backend/dommesh.py`:

```python
# --------------------------------------------------------------------------- #
# cutout() — public entry point                                                #
# --------------------------------------------------------------------------- #
ProgressFn = Callable[..., None]


def cutout(polygon_ewkt: str, out_dir: str, formats: tuple[str, ...] = ("obj", "glb"),
           progress: Optional[ProgressFn] = None, *,
           cache_root: Optional[str] = None,
           _reader_factory=None, _los_index_factory=None) -> dict:
    """Cut a DOM-Mesh slice for `polygon_ewkt` (Google Earth KML polygon as
    EWKT/WGS84) into `out_dir`. Writes the requested `formats` ("obj", "glb")
    plus meta.json. Returns the meta dict, or {"error": "..."} on a known
    failure (no coverage / no overlapping mesh). `progress(name, percent,
    status, speed="-", eta="-")` is called per node so it plugs into the GUI
    download list and the web progress_state."""
    t0 = time.time()
    os.makedirs(out_dir, exist_ok=True)
    cache_root = cache_root or os.path.join(out_dir, ".dommesh_cache")
    name_tag = "dommesh"

    def _tick(pct, status):
        if progress:
            progress(name_tag, int(pct), status, "-", "-")

    poly = polygon_from_ewkt(polygon_ewkt)
    minx, miny, maxx, maxy = poly.bounds
    bbox = (minx, miny, maxx, maxy)
    anchor = (math.floor(minx), math.floor(miny))

    li_factory = _los_index_factory or (lambda: LosIndex(
        cached_kml_path=os.path.join(cache_root, "losindex.kml"), download=True))
    los_ids = li_factory().los_ids_for_point(*poly.representative_point().coords[0])
    if not los_ids:
        return {"error": "This area isn't covered by Bayern's DOM-Mesh, "
                         "or no flight-day Los matched the polygon."}
    if len(los_ids) > 1:
        print(f"[WARN] dommesh: AOI overlaps {len(los_ids)} Los ({los_ids}); "
              f"using {los_ids[0]}.")

    reader = None
    leaves: list[dict] = []
    chosen = None
    rf = _reader_factory or (lambda lid: SlpkReader(lid, cache_root))
    for lid in los_ids:
        _tick(2, f"Indexing Los {lid}…")
        r = rf(lid)
        nds = r.nodes()
        sel = [nd for nd in nds if aabb_overlaps(nd, bbox)]
        if sel:
            reader, leaves, chosen = r, sel, lid
            break
    if not leaves:
        return {"error": "No mesh nodes overlap your polygon."}
    leaves.sort(key=lambda nd: nd["i"])

    submeshes: list[SubMesh] = []
    done = 0
    total = len(leaves)

    def _fetch_node(nd: dict) -> Optional[SubMesh]:
        try:
            g = reader.read_entry(f"nodes/{nd['geom_res']}/geometries/0.bin.gz")
            tex = reader.read_entry(f"nodes/{nd['mat_res']}/textures/0.jpg")
        except Exception as ex:  # noqa: BLE001
            print(f"[WARN] dommesh: skip node {nd['i']}: {ex}")
            return None
        vcount, pos, uv = decode_geometry(g)
        if vcount == 0:
            return None
        ocx, ocy, ocz = nd["cx"], nd["cy"], nd["cz"]
        wx = [ocx + pos[3 * k] for k in range(vcount)]
        wy = [ocy + pos[3 * k + 1] for k in range(vcount)]
        wz = [ocz + pos[3 * k + 2] for k in range(vcount)]
        tris, used, remap = clip_triangles(wx, wy, poly)
        if not tris:
            return None
        verts = [(wx[v] - anchor[0], wy[v] - anchor[1], wz[v]) for v in used]
        uvs = [(uv[2 * v], 1.0 - uv[2 * v + 1]) for v in used]
        rtris = [(remap[a], remap[b], remap[c]) for a, b, c in tris]
        return SubMesh(node_id=nd["i"], verts=verts, uvs=uvs, tris=rtris, jpeg=tex)

    with ThreadPoolExecutor(max_workers=8) as ex:
        futs = {ex.submit(_fetch_node, nd): nd for nd in leaves}
        for fut in as_completed(futs):
            sm = fut.result()
            if sm is not None:
                submeshes.append(sm)
            done += 1
            _tick(2 + 90 * done / total, f"Fetching mesh nodes {done}/{total}…")

    if not submeshes:
        return {"error": "No mesh nodes overlap your polygon."}
    submeshes.sort(key=lambda s: s.node_id)

    _tick(95, "Writing files…")
    if "obj" in formats:
        write_obj(out_dir, submeshes, anchor)
    if "glb" in formats:
        write_glb(os.path.join(out_dir, "cutout.glb"), submeshes, anchor)

    nverts = sum(len(s.verts) for s in submeshes)
    ntris = sum(len(s.tris) for s in submeshes)
    meta = {
        "losid": chosen,
        "slpk": f"{SLPK_MIRRORS[0]}/p/dom-mesh-slpk/{chosen}/DSM_Mesh.slpk",
        "polygon_epsg25832": [list(c) for c in poly.exterior.coords],
        "bbox_epsg25832": list(bbox),
        "anchor_epsg25832": list(anchor),
        "leaf_nodes": len(submeshes),
        "vertices": nverts,
        "triangles": ntris,
        "bytes_fetched": getattr(reader, "bytes_fetched", None),
        "seconds": round(time.time() - t0, 1),
        "files": [f for f, on in (("cutout.obj", "obj" in formats),
                                  ("cutout.glb", "glb" in formats)) if on] + ["meta.json"],
    }
    Path(os.path.join(out_dir, "meta.json")).write_text(json.dumps(meta, indent=1))
    _tick(100, "Completed")
    return meta
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: PASS (all unit tests).

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add backend/dommesh.py test_dommesh.py
git commit -m "feat(dommesh): cutout() — KML polygon -> OBJ/GLB DOM-Mesh slice

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: `conftest.py` + `needs_network` marker

**Files:**
- Create: `OpenMap_Unifier/conftest.py`

- [ ] **Step 1: Create the conftest**

`OpenMap_Unifier/conftest.py`:

```python
"""Pytest config for OpenMap_Unifier.

`needs_network` marks tests that hit live Bayern servers (DOM-Mesh SLPK range
requests). They are skipped unless the DOMMESH_LIVE env var is set, so the
default `pytest` run stays offline & fast.
"""
import os
import pytest


def pytest_configure(config):
    config.addinivalue_line("markers", "needs_network: hits live servers; "
                            "run only when DOMMESH_LIVE is set")


def pytest_collection_modifyitems(config, items):
    if os.environ.get("DOMMESH_LIVE"):
        return
    skip = pytest.mark.skip(reason="needs_network: set DOMMESH_LIVE=1 to run")
    for item in items:
        if "needs_network" in item.keywords:
            item.add_marker(skip)
```

- [ ] **Step 2: Verify default run still green and the marker registers**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v --strict-markers`
Expected: PASS, no "unknown marker" warnings.

- [ ] **Step 3: Commit**

```bash
cd OpenMap_Unifier
git add conftest.py
git commit -m "test(dommesh): conftest with needs_network marker (gated by DOMMESH_LIVE)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Live end-to-end test (network-gated) — also the real-data shakeout

**Files:**
- Modify: `OpenMap_Unifier/test_dommesh.py`

This is where the engineer verifies `SlpkReader` against the actual archive (the `_entries_from_tail` rebasing, the nodepages field names). If the live JSON disagrees with the assumptions in Task 7, fix `nodes()` here and re-run — trust the live data.

- [ ] **Step 1: Add the live test** — append to `test_dommesh.py`:

```python
@pytest.mark.needs_network
def test_live_cutout_auerbach(tmp_path):
    # The spike's "out_altstadt" rectangle: center EPSG:25832 (690137, 5506889),
    # ~160 m half-size, expressed as a 4-corner WGS84 polygon.
    from pyproj import Transformer
    tf = Transformer.from_crs("EPSG:25832", "EPSG:4326", always_xy=True)
    cx, cy, h = 690137.0, 5506889.0, 160.0
    corners = [(cx - h, cy - h), (cx + h, cy - h), (cx + h, cy + h),
               (cx - h, cy + h), (cx - h, cy - h)]
    ll = [tf.transform(x, y) for x, y in corners]
    ewkt = "SRID=4326;POLYGON((" + ", ".join(f"{lon} {lat}" for lon, lat in ll) + "))"

    meta = dommesh.cutout(ewkt, str(tmp_path), formats=("obj", "glb"))
    assert "error" not in meta, meta
    assert meta["triangles"] > 1000
    assert meta["leaf_nodes"] >= 1
    assert meta["bytes_fetched"] is not None
    assert (tmp_path / "cutout.obj").stat().st_size > 0
    assert (tmp_path / "cutout.glb").stat().st_size > 0
    # Sanity: nowhere near a full-Los download (first run includes the ~62 MB
    # central directory; cached runs are far smaller).
    assert meta["bytes_fetched"] < 200 * 1024 * 1024
```

- [ ] **Step 2: Run it for real (once, manually)**

Run: `cd OpenMap_Unifier; $env:DOMMESH_LIVE=1; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py::test_live_cutout_auerbach -v -s; Remove-Item Env:\DOMMESH_LIVE`
Expected: PASS. If it fails on ZIP64 offsets or nodepages field names, fix `backend/dommesh.py` (`_entries_from_tail`, `SlpkReader.nodes()`), keeping the unit tests green, and re-run.

- [ ] **Step 3: Confirm the default (offline) run still skips it**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py -v`
Expected: the live test shows as SKIPPED; everything else PASS.

- [ ] **Step 4: Commit (include any real-data fixes from Step 2)**

```bash
cd OpenMap_Unifier
git add test_dommesh.py backend/dommesh.py
git commit -m "test(dommesh): live end-to-end cutout test + real-data fixes

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 3 — GUI integration (OpenMap_Unifier)

### Task 11: Catalog entry + Downloads-tab folder

**Files:**
- Modify: `OpenMap_Unifier/backend/downloader.py` (in `BAYERN_DATASETS` and `BAYERN_CATEGORY_LABELS`)
- Modify: `OpenMap_Unifier/gui.py` (in `DOWNLOAD_FOLDERS`, ~line 310)

- [ ] **Step 1: Add the catalog entry** — in `backend/downloader.py`, inside `BAYERN_DATASETS`, after the `"lod2"` entry, add:

```python
    # ---- 3D MESH (photogrammetry, range-fetched) ----
    "dommesh": {
        # Range-fetched out of Bayern's per-Los DSM_Mesh.slpk (I3S) — see
        # backend/dommesh.py. Cut to the user's KML polygon, not the 50-200 GB
        # district archive.
        "label": "DOM-Mesh — Photogrammetric 3D city mesh (textured)",
        "category": "mesh3d",
        "description": "Textured photogrammetry mesh (buildings + trees + terrain) "
                       "cut to your polygon. Range-fetched from Bayern's SLPK — "
                       "no multi-GB download. Writes cutout.obj + cutout.glb.",
        "ext": ".glb",
        "resolution": "photogrammetry mesh",
        "kind": "mesh",
    },
```

And add to `BAYERN_CATEGORY_LABELS`:

```python
    "mesh3d":     "3D Meshes (photogrammetry)",
```

- [ ] **Step 2: Add the Downloads-tab folder** — in `gui.py`, in `DOWNLOAD_FOLDERS`, after the `"Bayern: LoD2 (3D buildings)"` line, add:

```python
        ("Bayern: DOM-Mesh 3D",            os.path.join("downloads_bayern", "dommesh")),
```

- [ ] **Step 3: Smoke-check it imports & the catalog is well-formed**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -c "from backend.downloader import BAYERN_DATASETS, BAYERN_CATEGORY_LABELS; m=BAYERN_DATASETS['dommesh']; assert m['kind']=='mesh' and m['category']=='mesh3d'; assert 'mesh3d' in BAYERN_CATEGORY_LABELS; print('ok')"`
Expected: prints `ok`.

- [ ] **Step 4: Commit**

```bash
cd OpenMap_Unifier
git add backend/downloader.py gui.py
git commit -m "feat(dommesh): catalog entry (mesh3d category) + Downloads-tab folder

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 12: GUI download dispatch — the `kind == "mesh"` branch

**Files:**
- Modify: `OpenMap_Unifier/gui.py` — `start_bayern_download` (~line 662), `_estimate_bayern_download` (~line 688), and add `_run_bayern_dommesh` (next to `_run_bayern_wms`, ~line 760).

- [ ] **Step 1: Branch on `kind` in `start_bayern_download`** — replace the `if meta["kind"] == "raw": … else: # wms …` block (lines ~666–681) with:

```python
            if meta["kind"] == "raw":
                self.add_download_row(meta["label"], "Generating tile list...", 0, "Calculating", "...")
                threading.Thread(
                    target=self._run_bayern_raw,
                    args=(poly, key, out_dir),
                    daemon=True,
                ).start()
            elif meta["kind"] == "mesh":
                self.add_download_row(meta["label"], "Picking flight-day Los...", 0, "Calculating", "...")
                threading.Thread(
                    target=self._run_bayern_dommesh,
                    args=(poly, key, out_dir),
                    daemon=True,
                ).start()
            else:  # wms
                wms_fmt = fmt if key == "dop40_wms" else "tiff"
                self.add_download_row(meta["label"], "Generating WMS tiles...", 0, "Calculating", "...")
                threading.Thread(
                    target=self._run_bayern_wms,
                    args=(poly, key, wms_fmt, high_res, out_dir),
                    daemon=True,
                ).start()
```

- [ ] **Step 2: Skip mesh in the size estimate** — in `_estimate_bayern_download`, inside the `for key in selected_keys:` loop, before the `if meta["kind"] == "raw":`, add:

```python
                if meta["kind"] == "mesh":
                    # Size depends entirely on AOI size & node count; we don't
                    # know until we've indexed the Los. Don't block on it.
                    per_dataset.append((meta["label"], 0, 0))
                    continue
```

- [ ] **Step 3: Add `_run_bayern_dommesh`** — after `_run_bayern_wms` (find the `def _run_bayern_wms(self, ...)` block, add after it):

```python
    def _run_bayern_dommesh(self, poly, key, out_dir):
        """Download a DOM-Mesh slice (OBJ + GLB) cut to the polygon.

        Range-fetches only the I3S leaf nodes that overlap the polygon out of
        Bayern's per-Los DSM_Mesh.slpk (see backend/dommesh.py) — no multi-GB
        district download.
        """
        from backend.dommesh import cutout

        def progress_cb(name, percent, status, speed="-", eta="-"):
            # Re-use the single download row keyed by the dataset label.
            self.after(0, lambda: self.update_download_row(
                BAYERN_DATASETS[key]["label"], status, percent, speed, eta))

        try:
            meta = cutout(poly, out_dir, formats=("obj", "glb"), progress=progress_cb)
        except Exception as e:  # noqa: BLE001 - surface anything to the UI
            self.after(0, lambda: self.update_download_row(
                BAYERN_DATASETS[key]["label"], f"Error: {e}", 0, "-", "-"))
            print(f"[ERROR] dommesh: {e}")
            return
        if "error" in meta:
            self.after(0, lambda: self.update_download_row(
                BAYERN_DATASETS[key]["label"], meta["error"], 0, "-", "-"))
            return
        msg = (f"Done — {meta['triangles']} tris, {meta['leaf_nodes']} nodes, "
               f"Los {meta['losid']}")
        self.after(0, lambda: self.update_download_row(
            BAYERN_DATASETS[key]["label"], msg, 100, "-", "-"))
        print(f"[INFO] dommesh -> {out_dir}: {json.dumps(meta)}")
```

> **Engineer note:** the GUI's download-row API may be named `update_download_row`
> or the rows may be updated differently — check how `_run_bayern_wms` /
> `run_downloads_batch` / `add_download_row` update an existing row's text &
> percent (search `gui.py` for `add_download_row` and the progress callback the
> raw/wms paths pass to `MapDownloader.download_file`). Use the same mechanism;
> the names above are the expected ones.

- [ ] **Step 4: Smoke-check the GUI module imports**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -c "import ast; ast.parse(open('gui.py',encoding='utf-8').read()); print('parsed ok')"`
Expected: prints `parsed ok`. (A full GUI launch needs a display; parsing is the CI-safe check.)

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add gui.py
git commit -m "feat(dommesh): GUI download dispatch for kind=='mesh'

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 4 — Web app integration (OpenMap_Unifier)

### Task 13: `/start-download-dommesh` endpoint + page button

**Files:**
- Modify: `OpenMap_Unifier/app.py`
- Modify: `OpenMap_Unifier/templates/index.html`
- Modify: `OpenMap_Unifier/static/script.js`

- [ ] **Step 1: Add the endpoint** — in `app.py`, after `run_relief_download` (line ~103), add:

```python
@app.post("/start-download-dommesh")
async def start_download_dommesh(background_tasks: BackgroundTasks, polygon: str = Form(...)):
    out_dir = "downloads_dommesh"
    progress_state["dommesh"] = {"percent": 0, "status": "Pending"}
    background_tasks.add_task(run_dommesh_download, polygon, out_dir)
    return {"message": "Download started"}


async def run_dommesh_download(polygon: str, out_dir: str):
    from backend.dommesh import cutout
    loop = asyncio.get_event_loop()

    def _job():
        try:
            meta = cutout(polygon, out_dir, formats=("obj", "glb"),
                          progress=ProgressManager.update_progress)
        except Exception as e:  # noqa: BLE001
            ProgressManager.update_progress("dommesh", 0, f"Error: {e}")
            return
        if "error" in meta:
            ProgressManager.update_progress("dommesh", 0, meta["error"])
        else:
            ProgressManager.update_progress(
                "dommesh", 100,
                f"Completed — {meta['triangles']} tris, Los {meta['losid']}")

    await loop.run_in_executor(None, _job)
```

- [ ] **Step 2: Add the button** — in `templates/index.html`, next to the existing "Download Relief" / metalink controls, add a button mirroring the relief one (find the relief button by searching for `start-download-relief` in `script.js` and the corresponding element id in `index.html`):

```html
<button id="btn-dommesh" type="button">Download DOM-Mesh 3D (cut to polygon)</button>
```

- [ ] **Step 3: Wire the button** — in `static/script.js`, find the relief button handler (it reads the analysed polygon string and POSTs `polygon=<wkt>` to `/start-download-relief`, then starts the `/progress` poll). Add an analogous handler:

```javascript
document.getElementById("btn-dommesh").addEventListener("click", async () => {
  const polygon = currentPolygon;            // same variable the relief button uses
  if (!polygon) { alert("Analyze a KML polygon first."); return; }
  const body = new URLSearchParams({ polygon });
  const res = await fetch("/start-download-dommesh", { method: "POST", body });
  if (!res.ok) { alert("Failed to start DOM-Mesh download."); return; }
  startProgressPolling();                     // same poller the other downloads use
});
```

> **Engineer note:** `currentPolygon` / `startProgressPolling` are placeholders for
> whatever the existing relief/metalink handlers use — copy that handler and
> change only the URL and the button id. Don't introduce a new polling mechanism.

- [ ] **Step 4: Smoke-check the app imports**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -c "import app; print([r.path for r in app.app.routes])"`
Expected: the printed route list includes `/start-download-dommesh`.

- [ ] **Step 5: Commit**

```bash
cd OpenMap_Unifier
git add app.py templates/index.html static/script.js
git commit -m "feat(dommesh): web endpoint /start-download-dommesh + page button

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 14: Update OpenMap_Unifier README + push the submodule

**Files:**
- Modify: `OpenMap_Unifier/README.md`

- [ ] **Step 1: Document the feature** — in `README.md`, under "Features", add:

```markdown
- **DOM-Mesh 3D cutout**: Cut a small textured photogrammetry-mesh slice (OBJ + GLB)
  out of Bayern's DOM-Mesh from a Google Earth KML polygon — range-fetched, no
  multi-GB download. (`backend/dommesh.py`; "DOM-Mesh — Photogrammetric 3D city mesh"
  in the Bayern picker / `POST /start-download-dommesh`.)
```

- [ ] **Step 2: Run the full UU test suite**

Run: `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest -v`
Expected: all PASS (the `needs_network` live test SKIPPED).

- [ ] **Step 3: Commit & push the submodule**

```bash
cd OpenMap_Unifier
git add README.md
git commit -m "docs: DOM-Mesh 3D cutout feature

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
git push
```

- [ ] **Step 4: Bump the submodule in the parent repo**

```bash
cd ..
git add OpenMap_Unifier
git commit -m "chore: bump OpenMap_Unifier (DOM-Mesh cutout: backend.dommesh + GUI/web)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
git push
```

---

## Phase 5 — Blender addon: import the DOM-Mesh slice

All Phase-5 work is inside the `openmap_blender_tools` submodule. Read `citygml_import.py`, the `OBT_OT_import_buildings`/`import_heightmap` operators in `operators.py`, `_get_scene_anchor`, and `tests/test_citygml_import.py` first.

### Task 15: `dommesh_import.py` — pure-Python helpers (TDD)

**Files:**
- Create: `openmap_blender_tools/dommesh_import.py`
- Create: `openmap_blender_tools/tests/test_dommesh_import.py`
- Create: `openmap_blender_tools/tests/fixtures/dommesh_meta.json`

- [ ] **Step 1: Create the fixture** — `openmap_blender_tools/tests/fixtures/dommesh_meta.json`:

```json
{
 "losid": "125023_0",
 "slpk": "https://download1.bayernwolke.de/p/dom-mesh-slpk/125023_0/DSM_Mesh.slpk",
 "bbox_epsg25832": [689977.0, 5506729.0, 690297.0, 5507049.0],
 "anchor_epsg25832": [689977.0, 5506729.0],
 "leaf_nodes": 14,
 "vertices": 35000,
 "triangles": 11666,
 "files": ["cutout.obj", "cutout.glb", "meta.json"]
}
```

- [ ] **Step 2: Write the failing test** — `openmap_blender_tools/tests/test_dommesh_import.py`:

```python
"""Unit tests for dommesh_import.py.

Pure-Python helpers tested without bpy. The bpy-dependent importer is covered by
smoke_dommesh.py (needs Blender).
"""
from __future__ import annotations

import json
from pathlib import Path

from blender_tools.dommesh_import import read_dommesh_meta, anchor_offset

FIX = Path(__file__).parent / "fixtures" / "dommesh_meta.json"


def test_read_dommesh_meta_returns_dict():
    meta = read_dommesh_meta(str(FIX))
    assert meta["losid"] == "125023_0"
    assert meta["anchor_epsg25832"] == [689977.0, 5506729.0]


def test_anchor_offset_with_scene_anchor():
    meta = read_dommesh_meta(str(FIX))
    # Scene anchored at (689900, 5506700); the cutout anchor is (689977, 5506729);
    # imported verts (which are cutout-anchor-relative) must move by
    # (cutout_anchor - scene_anchor) = (77, 29).
    dx, dy = anchor_offset(meta, scene_anchor=(689900.0, 5506700.0, 0.0))
    assert (dx, dy) == (77.0, 29.0)


def test_anchor_offset_without_scene_anchor_is_zero():
    meta = read_dommesh_meta(str(FIX))
    dx, dy = anchor_offset(meta, scene_anchor=None)
    assert (dx, dy) == (0.0, 0.0)
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd openmap_blender_tools; & "C:\ProgramData\anaconda3\python.exe" -m pytest tests/test_dommesh_import.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'blender_tools.dommesh_import'`.

- [ ] **Step 4: Write minimal implementation** — `openmap_blender_tools/dommesh_import.py`:

```python
"""DOM-Mesh slice (cutout.glb + meta.json from OpenMap_Unifier) -> Blender import.

Pure-Python entry points (`read_dommesh_meta`, `anchor_offset`) are unit-tested
without bpy. `import_dommesh_glb` is bpy-dependent: it imports the GLB via
Blender's built-in glTF importer, then translates the result so the model lands
in the scene's UTM-local frame (the OpenMap anchor system; same idea as
import_heightmap / import_buildings).
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Optional, Any


def read_dommesh_meta(meta_path: str) -> dict:
    """Load the meta.json written next to cutout.glb."""
    return json.loads(Path(meta_path).read_text())


def anchor_offset(meta: dict, scene_anchor: Optional[tuple[float, float, float]]
                  ) -> tuple[float, float]:
    """How far to translate the imported (cutout-anchor-relative) vertices so
    they sit correctly in the scene's UTM-local frame.

    If the scene has no anchor yet, the caller should adopt the cutout's anchor
    as the scene anchor and import at the origin -> offset (0, 0).
    """
    if scene_anchor is None:
        return (0.0, 0.0)
    ca = meta["anchor_epsg25832"]
    return (float(ca[0]) - float(scene_anchor[0]), float(ca[1]) - float(scene_anchor[1]))


def import_dommesh_glb(glb_path: str, meta_path: Optional[str] = None,
                       scene_anchor: Optional[tuple[float, float, float]] = None
                       ) -> dict[str, Any]:
    """Import cutout.glb into the current Blender scene.

    Returns {"objects": [bpy.types.Object, ...], "empty": <parent empty>,
    "adopted_anchor": <(x,y,0) or None>}. Requires bpy.
    """
    import bpy  # noqa: PLC0415 - bpy only exists inside Blender

    # Make sure the glTF importer addon is available (it ships with Blender).
    if not hasattr(bpy.ops.import_scene, "gltf"):
        bpy.ops.preferences.addon_enable(module="io_scene_gltf2")

    meta = read_dommesh_meta(meta_path) if meta_path else {}
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=glb_path)
    new_objs = [o for o in bpy.data.objects if o not in before]

    adopted = None
    if scene_anchor is None and meta.get("anchor_epsg25832"):
        ca = meta["anchor_epsg25832"]
        adopted = (float(ca[0]), float(ca[1]), 0.0)
        bpy.context.scene["utm32n_anchor"] = list(adopted)

    dx, dy = anchor_offset(meta, scene_anchor) if meta else (0.0, 0.0)

    # Parent everything under one empty so the slice moves/hides as a unit.
    empty = bpy.data.objects.new("DOM-Mesh", None)
    bpy.context.scene.collection.objects.link(empty)
    empty.location = (dx, dy, 0.0)
    for o in new_objs:
        if o.parent is None:
            o.parent = empty
        # Photogrammetry textures are colour data, not data maps.
        for slot in getattr(o, "material_slots", []):
            mat = slot.material
            if not mat or not mat.use_nodes:
                continue
            for node in mat.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    node.image.colorspace_settings.name = "sRGB"
    return {"objects": new_objs, "empty": empty, "adopted_anchor": adopted}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd openmap_blender_tools; & "C:\ProgramData\anaconda3\python.exe" -m pytest tests/test_dommesh_import.py -v`
Expected: PASS.

- [ ] **Step 6: Commit (inside the submodule)**

```bash
cd openmap_blender_tools
git add dommesh_import.py tests/test_dommesh_import.py tests/fixtures/dommesh_meta.json
git commit -m "feat: dommesh_import — DOM-Mesh slice (.glb + meta.json) -> Blender

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 16: `OBT_OT_import_dommesh` operator + registration

**Files:**
- Modify: `openmap_blender_tools/operators.py` — add the operator class, add it to the `classes` tuple, add a panel button next to "Import Buildings".
- Modify: `openmap_blender_tools/tests/test_features_registry.py` (or whichever test enumerates the operator classes) — extend the expected set with `OBT_OT_import_dommesh`.

- [ ] **Step 1: Find the registration test's expected set** — open the test that asserts on registered operators (search `tests/` for `OBT_OT_import_buildings` or `bl_idname`). Add `"blender_tools.import_dommesh"` (or `OBT_OT_import_dommesh`, matching that test's convention) to its expected collection. Run it first to see it fail:

Run: `cd openmap_blender_tools; & "C:\ProgramData\anaconda3\python.exe" -m pytest tests/test_features_registry.py -v`
Expected: FAIL — the new id isn't registered yet.

- [ ] **Step 2: Add the operator** — in `operators.py`, near `OBT_OT_import_buildings`, add (match the surrounding operator style — `bl_idname`, `bl_label`, `bl_options`, the `filepath`/`filter_glob` props pattern, `invoke` opening the file browser, `execute` doing the work):

```python
class OBT_OT_import_dommesh(bpy.types.Operator):
    """Import a DOM-Mesh slice (cutout.glb) produced by OpenMap_Unifier.

    Reads the sibling meta.json for the EPSG:25832 anchor and places the mesh in
    the scene's UTM-local frame (seeding scene["utm32n_anchor"] if unset, like
    Import Heightmap)."""
    bl_idname = "blender_tools.import_dommesh"
    bl_label = "Import DOM-Mesh Slice (.glb)"
    bl_options = {"REGISTER", "UNDO"}

    filepath: bpy.props.StringProperty(subtype="FILE_PATH")
    filter_glob: bpy.props.StringProperty(default="*.glb", options={"HIDDEN"})

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {"RUNNING_MODAL"}

    def execute(self, context):
        import os
        from . import dommesh_import
        glb = self.filepath
        meta = os.path.join(os.path.dirname(glb), "meta.json")
        meta = meta if os.path.exists(meta) else None
        anchor = _get_scene_anchor(context)
        try:
            result = dommesh_import.import_dommesh_glb(glb, meta_path=meta, scene_anchor=anchor)
        except Exception as e:  # noqa: BLE001
            self.report({"ERROR"}, f"DOM-Mesh import failed: {e}")
            return {"CANCELLED"}
        n = len(result["objects"])
        if result.get("adopted_anchor"):
            self.report({"INFO"}, f"Imported {n} mesh object(s); set scene anchor "
                                  f"{tuple(round(v) for v in result['adopted_anchor'])}.")
        else:
            self.report({"INFO"}, f"Imported {n} DOM-Mesh object(s).")
        return {"FINISHED"}
```

- [ ] **Step 3: Register it** — add `OBT_OT_import_dommesh` to the `classes` tuple (find the existing `classes = (...)` near the bottom of `operators.py`, next to `OBT_OT_import_buildings`). If there's a panel `draw()` listing import buttons, add `layout.operator("blender_tools.import_dommesh")` next to the buildings one.

- [ ] **Step 4: Run the registry test**

Run: `cd openmap_blender_tools; & "C:\ProgramData\anaconda3\python.exe" -m pytest tests/test_features_registry.py tests/test_dommesh_import.py -v`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd openmap_blender_tools
git add operators.py tests/test_features_registry.py
git commit -m "feat: OBT_OT_import_dommesh operator + registration

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 17: `smoke_dommesh.py` — headless Blender import smoke test

**Files:**
- Create: `openmap_blender_tools/tests/smoke_dommesh.py`

This needs Blender; it's not part of the default pytest run (same as the other `smoke_*.py`).

- [ ] **Step 1: Create the smoke test** — `openmap_blender_tools/tests/smoke_dommesh.py`:

```python
"""Headless smoke test: build a tiny cutout.glb + meta.json, run the operator,
assert objects imported and translated. Needs Blender.

Run: blender --background --python tests/smoke_dommesh.py
"""
import json
import os
import sys
import tempfile

import bpy

# --- build a 1-triangle GLB the same way backend/dommesh.write_glb does, but
#     standalone (this file can't import OpenMap_Unifier). Minimal valid glTF: ---
import struct


def _pad4(b, fill=b"\x00"):
    return b + fill * ((4 - len(b) % 4) % 4)


def _tiny_glb(path):
    # one triangle, Y-up, no texture (keep it minimal — colorspace path is
    # exercised by the unit test's mock; here we only need geometry to land).
    verts = [(0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (0.0, 0.0, -10.0)]
    idx = struct.pack("<III", 0, 1, 2)
    pos = b"".join(struct.pack("<fff", *v) for v in verts)
    bin_blob = _pad4(idx + pos)
    gltf = {
        "asset": {"version": "2.0"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 1}, "indices": 0, "mode": 4}]}],
        "accessors": [
            {"bufferView": 0, "componentType": 5125, "count": 3, "type": "SCALAR"},
            {"bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3",
             "min": [0.0, 0.0, -10.0], "max": [10.0, 0.0, 0.0]},
        ],
        "bufferViews": [
            {"buffer": 0, "byteOffset": 0, "byteLength": 12, "target": 34963},
            {"buffer": 0, "byteOffset": 16, "byteLength": 36, "target": 34962},
        ],
        "buffers": [{"byteLength": len(bin_blob)}],
    }
    jb = _pad4(json.dumps(gltf, separators=(",", ":")).encode(), b" ")
    total = 12 + 8 + len(jb) + 8 + len(bin_blob)
    with open(path, "wb") as fh:
        fh.write(struct.pack("<4sII", b"glTF", 2, total))
        fh.write(struct.pack("<I4s", len(jb), b"JSON")); fh.write(jb)
        fh.write(struct.pack("<I4s", len(bin_blob), b"BIN\x00")); fh.write(bin_blob)


def main():
    # Make the addon importable as `blender_tools`.
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    sys.path.insert(0, os.path.dirname(repo))  # parent of openmap_blender_tools
    # The package may be installed as an extension; if `import blender_tools`
    # fails, fall back to importing from the repo dir.
    try:
        import blender_tools  # noqa: F401
    except ModuleNotFoundError:
        sys.path.insert(0, repo)
        import blender_tools  # noqa: F401
    from blender_tools import dommesh_import

    d = tempfile.mkdtemp()
    glb = os.path.join(d, "cutout.glb")
    _tiny_glb(glb)
    json.dump({"losid": "T", "anchor_epsg25832": [690000.0, 5506000.0]},
              open(os.path.join(d, "meta.json"), "w"))

    # Empty scene.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    res = dommesh_import.import_dommesh_glb(glb, meta_path=os.path.join(d, "meta.json"),
                                            scene_anchor=None)
    assert res["objects"], "no objects imported"
    assert res["empty"].name == "DOM-Mesh"
    assert bpy.context.scene.get("utm32n_anchor") == [690000.0, 5506000.0]
    print("smoke_dommesh OK:", len(res["objects"]), "object(s)")


main()
```

- [ ] **Step 2: Run the smoke test**

Run: `cd openmap_blender_tools; & "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" --background --python tests/smoke_dommesh.py`
Expected: prints `smoke_dommesh OK: 1 object(s)` and exits 0.

- [ ] **Step 3: Commit**

```bash
cd openmap_blender_tools
git add tests/smoke_dommesh.py
git commit -m "test: smoke_dommesh — headless Blender import of a DOM-Mesh slice

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 18: Update CLAUDE.md, push the blender submodule, bump the parent

**Files:**
- Modify: `CLAUDE.md` (parent repo) — add the new test commands.
- Modify: `openmap_blender_tools/README.md` (one line under features/operators, optional but nice).

- [ ] **Step 1: Add test commands to `CLAUDE.md`** — in the "Build & Test" block, append to the unit-test invocations:

```powershell
& "C:\ProgramData\anaconda3\python.exe" -m pytest OpenMap_Unifier/test_dommesh.py -v   # set $env:DOMMESH_LIVE=1 for the live cutout test
```

and to the smoke-test list:

```powershell
blender --background --python openmap_blender_tools/tests/smoke_dommesh.py
```

- [ ] **Step 2: Run both unit suites once more**

Run:
```
cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest -v; cd ..
& "C:\ProgramData\anaconda3\python.exe" -m pytest openmap_blender_tools/tests/test_dommesh_import.py openmap_blender_tools/tests/test_features_registry.py -v
```
Expected: all PASS.

- [ ] **Step 3: Commit & push the blender submodule**

```bash
cd openmap_blender_tools
git add README.md
git commit -m "docs: mention DOM-Mesh slice import operator

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
git push
```

- [ ] **Step 4: Commit CLAUDE.md + bump the submodule in the parent**

```bash
cd ..
git add CLAUDE.md openmap_blender_tools
git commit -m "chore: bump openmap_blender_tools (DOM-Mesh slice import) + CLAUDE.md test cmds

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
git push
```

---

## Done — final verification checklist

- [ ] `cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest -v` — all green, live test skipped.
- [ ] `$env:DOMMESH_LIVE=1; cd OpenMap_Unifier; & "C:\ProgramData\anaconda3\python.exe" -m pytest test_dommesh.py::test_live_cutout_auerbach -v -s; Remove-Item Env:\DOMMESH_LIVE` — green, `bytes_fetched` printed, far below a full-Los download.
- [ ] `& "C:\ProgramData\anaconda3\python.exe" -m pytest openmap_blender_tools/tests/ -v --ignore=openmap_blender_tools/tests/smoke_*.py` — all green.
- [ ] `& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" --background --python openmap_blender_tools/tests/smoke_dommesh.py` — prints OK.
- [ ] GUI: launch `OpenMap_Unifier/run.bat`, paste a Bavaria KML polygon, tick "DOM-Mesh — Photogrammetric 3D city mesh", Download → `downloads_bayern/dommesh/cutout.obj` + `cutout.glb` + `meta.json` appear; the Downloads tab shows the `Bayern: DOM-Mesh 3D` folder.
- [ ] Web: `run_web.bat`, analyze a KML, click "Download DOM-Mesh 3D", `/progress` shows `dommesh` going to 100, files land in `downloads_dommesh/`.
- [ ] Blender: install/reload the extension, `Import DOM-Mesh Slice (.glb)` → pick `cutout.glb` → textured mesh appears, parented under a `DOM-Mesh` empty, scene anchor set.
- [ ] `git diff` in both submodules and the parent: every changed line traces to this plan.

---

## Self-review notes (addressed)

- **`_RebaseBytes` shim in Task 7** is deliberately marked "do not ship — inline a tail-aware parser instead". The contract (`SlpkReader.entries()` returns absolute offsets) is what later tasks depend on; the implementation detail is the engineer's, verified live in Task 10. Flagged because it's the one place the plan can't hand over copy-paste-final code without a live archive to test against.
- **GUI download-row update API** (`update_download_row`) and **web JS globals** (`currentPolygon`, `startProgressPolling`) are named on the assumption they match the existing raw/wms/relief paths; Tasks 12 & 13 tell the engineer to copy the existing handler and change only URL/id, so a name mismatch is self-correcting.
- Spec coverage: `LosIndex`→T6; `SlpkReader`/caching→T7; `cutout`/clip/anchor→T3,T8; OBJ→T4; GLB→T5; catalog `mesh3d`→T11; GUI dispatch→T12; web endpoint→T13; Blender import+operator+tests→T15–17; `needs_network` gating→T9; live shakeout→T10; docs/CLAUDE.md/submodule bumps→T14,T18. No gaps.
</content>
