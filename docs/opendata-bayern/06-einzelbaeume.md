# 06 — Einzelbäume (single-tree points)

**Product key:** `einzelbaeume` · **Page:** https://geodaten.bayern.de/opengeodata/OpenDataDetail.html?pn=einzelbaeume

## What it is

Statewide **point** layer; each point = the position of **one tree**, derived **fully
automatically** from **DOM20** (surface model 20 cm) + **DOP20** (orthophoto) + **DGM1** (bare-earth
terrain) — local canopy maxima, colour-checked against the orthophoto, height = DOM − DGM.
Publisher: LDBV. Each point is a point object only — **not** a crown polygon, **no species, no crown
diameter, no trunk diameter, no date attribute**. Quality caveat (verbatim Datenhinweise):
fully-automatic derivation → errors especially **at high-voltage power lines, in shadowed areas, at
building edges**. LDBV positions it as "good for visualization" (3D/VR/AR scenes, line-of-sight,
tree-height derivation) — not a survey-grade tree cadastre. (The same trees are also a styled
BayernAtlas "Web Vektor Standard" vector-tile layer — that's not the OpenData download.)

## Schema (the whole thing — 3 attributes + point geometry)

| field | type | meaning |
|---|---|---|
| `id` | Integer64 | per-file running ID (not a stable Bavaria-wide tree ID; treat as such since each block is re-derived) |
| `dgmhoehe` | Real | tree height **above the DGM** (canopy top minus bare ground = tree height above ground), meters |
| `baumhoehe` | Real | **absolute** elevation of the tree top (terrain elevation + tree height), meters |

X/Y is the point geometry (EPSG:25832). Z is **not** in the geometry — only the two height attrs.
`[UNVERIFIED]`: the internal layer/table name inside the `.gpkg` (the format PDF documents the
columns but not the layer name — `ogrinfo` a downloaded `.gpkg` to confirm).

## Format & spatial organization

- **GeoPackage** (`.gpkg`, MIME `application/geopackage+sqlite3`). No Shapefile/GML/CSV.
- **NOT** tiled on the 1 km UTM grid. Organized by **Bayernbefliegung project block**
  ("Projektgebiet") — **86 blocks** covering all Bavaria, one `.gpkg` per block, ~120 MB–1 GB each
  (the catalogue lists the product class as "200 MB – 1 GB"). Block code: first 3 digits = survey
  year (`123…`=2023, `124…`=2024, `125…`=2025), rest = block sequence; each block ~300–1130 km², has
  a town/region name. Re-flown blocks get a new year prefix (e.g. `124004 Eichstätt` and `125040
  Eichstätt` cover different fragments). As of the index KML (last-modified 2026-03-16): mixed
  mosaic — 2023 (a few Oberpfalz blocks: Tirschenreuth, Weiden, Amberg, Schwandorf), 2024 (~36
  blocks, Niederbayern/Schwaben/much of Oberbayern), 2025 (~46 blocks, Unter-/Ober-/Mittelfranken +
  parts of Oberbayern/Schwaben). No single bayernwide file, no per-Gemeinde/Landkreis split.

## Download

```
Direct per-block GeoPackage (note path segment "baeume3d", not "einzelbaeume"):
  https://geodaten.bayern.de/odd/m/8/baeume3d/data/<gebietscode>_baeume.gpkg
  e.g.  https://geodaten.bayern.de/odd/m/8/baeume3d/data/124028_baeume.gpkg   (München)
        https://geodaten.bayern.de/odd/m/8/baeume3d/data/124004_baeume.gpkg   (Eichstätt; 145,002,496 bytes, verified)

Machine-readable index of all 86 blocks (footprint polygon in WGS84, name, vintage year, download
size, direct link — parse this to enumerate):
  https://geodaten.bayern.de/odd/m/8/baeume3d/kml/Einzelbaumstandorte.kml?service=kml   (~326 KB, 86 Placemarks)
```
The portal's draw-a-polygon downloader POSTs EWKT to `poly2metalink`, but the dataset token isn't
cleanly exposed (and `…/poly2metalink/datasets/einzelbaeume` → `"Invalider Dataset-Name."`) — use
the direct `.gpkg` URLs + the index KML instead. No pre-built whole-Bavaria `.meta4`.

## Update / license / docs

Updated **block-by-block** on the Bayernbefliegung cycle (~2–3 yr full coverage; a block is
re-derived when its DOM/DOP refresh). CC BY 4.0. Datenformatbeschreibung PDF (2 pp, exported
2025-04-11): `https://geodaten.bayern.de/odd/m/3/pdf/einzelbaeume_datenformat.pdf`. Datenhinweise:
`https://www.geodaten.bayern.de/odd/m/3/html/datenhinweise/datenhinweise_einzelbaeume.html`. LDBV
product page `https://www.ldbv.bayern.de/produkte/landschaftsinformationen/einzelbaeume.html`.
GDI-DE metadata record `https://gdk.gdi-de.org/geonetwork/srv/api/records/af2cd8cd-13c9-4a1d-a65c-ab2fa33d88c0`.

## Use in the pipeline

Drop a tree instance (from `assets/trees.blend`) at each point, scaled by `dgmhoehe` (height above
ground). This is the "real tree positions" answer vs. the current procedural scatter. Potentially
millions of points over a city → needs instancing + a bbox filter on import (clip to the scene's
`bbox_utm32n` before instancing). Pick which `.gpkg` block(s) intersect the AOI via the index KML.
