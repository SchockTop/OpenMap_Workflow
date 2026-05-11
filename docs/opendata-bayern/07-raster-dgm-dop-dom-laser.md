# 07 — Raster & point-cloud geobasis products

DGM (terrain), DOP (orthophotos), DOM (surface model), DOM-Mesh, Laserdaten (point cloud),
Geländerelief, Höhenlinien. All EPSG:25832, height DHHN2016/NHN, CC BY 4.0. Tile/data files on
`https://download1.bayernwolke.de/...` (mirror `download2…`). For the portal/Metalink mechanics see
[00-portal-and-conventions.md](00-portal-and-conventions.md).

## DGM1 — Digitales Geländemodell 1 m  (key `dgm1`)

Bare-earth terrain (no vegetation, no buildings), regular 1 m grid, from airborne laser scanning,
updated by re-flights. **What we use in the pipeline.**
- **Formats:** (1) **GeoTIFF** — single-band float elevation (Float32; bit depth not stated in the
  official HTML doc — `[UNVERIFIED]` but the standard ALS-DGM GeoTIFF is 32-bit float), internal
  tiling, ~3–4 MB/tile. (2) **ASCII-TXT (XYZ)** — `X Y Z`, ~4 MB/tile.
- **NoData: `-9999`** — conventional / what the pipeline uses; **not stated in the official
  Datenhinweise text** `[UNVERIFIED from the doc; verify via the GeoTIFF tag — but this is correct
  in practice]`.
- **Tiling: 1 km × 1 km grid.** Filename **`<east_km>_<north_km>.tif`** — **no "32" prefix** (e.g.
  `615_5406.tif`). URL `https://download1.bayernwolke.de/a/dgm/dgm1/615_5406.tif`.
- **Download:** single GeoTIFF tile `…/od/wms/grid/v1/opendatagrid?title=Opendata_Auswahl_DGM1&layers=dgm1&service=wms`
  ; single XYZ tile `…&layers=dgm1xyz…` ; polygon→metalink `…/poly2metalink/metalink/dgm1?data=dgm1&service=polygon`
  ; per Gemeinde `…/odd/a/dgm/dgm1/meta/kml/gemeinde.kml?service=kml` (≤500 MB) ; per Landkreis
  `…/kml/kreis.kml` (~5 GB) ; **whole Bavaria** `https://geodaten.bayern.de/odd/a/dgm/dgm1/meta/metalink/09.meta4` (~240 GB).
- **WMS/WCS:** no dedicated DGM1 elevation WMS in OpenData; the hillshade is the `gelaenderelief`
  product. No public WCS found `[UNVERIFIED]`.
- **Update:** *losweise* (lot-by-lot on each re-flight) — no fixed cycle.
- **Docs:** `…/datenhinweise_dgm.html` + `https://geodaten.bayern.de/odd/m/3/pdf/hinweise_daten_dgm1_download.pdf`
  (Stand 2023-01-24); GeoTIFF helper PDFs: `geotiff-konvertierung_xyz.pdf`, `geotiff-tfw-datei_erzeugen.pdf`,
  `geotiff-kacheln_zusammenfuegen.pdf`, `geotiff-kachel_zuschneiden.pdf`, `geotiff-kachel_punktreduzierung.pdf`.

## DGM5 — Digitales Geländemodell 5 m  (key `dgm5`, internal `dgm5xyz`)

Same bare-earth terrain, 5 m grid spacing. **No GeoTIFF variant — only XYZ-ASCII inside a `.zip`.**
- **Format:** `.zip` archive each containing one ASCII-TXT `X Y Z` file (5 m spacing, 200×200 grid
  per 1 km tile). ~0.2 MB/tile.
- **Tiling: 1 km × 1 km grid.** Filename `<east_km>_<north_km>.zip` (no "32" prefix). URL
  `https://download1.bayernwolke.de/a/dgm/dgm5xyz/615_5406.zip`.
- **Download:** single tile `…opendatagrid?title=Opendata_Auswahl_DGM5_XYZ&layers=dgm5xyz&service=wms`
  ; polygon→metalink `…/poly2metalink/metalink/dgm5xyz?data=dgm5xyz&service=polygon` ; whole Bavaria
  `https://geodaten.bayern.de/odd/a/dgm/dgm5xyz/meta/metalink/09.meta4` (~14 GB).
- **Update:** *losweise* (re-derived from DGM1 on re-flight). Docs: `…/datenhinweise_dgm.html`.
- *(Our downloader handles this — see [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §3.
  The pipeline converts the XYZ→GeoTIFF via `gdal_translate` — `geo_import.dgm5_xyz_to_geotiffs()`.)*

## DOM20 — Digitales Oberflächenmodell 20 cm  (key `dom20`)

Surface model **including** objects on the ground (vegetation, buildings), grid form, 0.2 m,
derived by dense image matching from the Bayernbefliegung aerial photos. Position/height accuracy
0.4–0.6 m.
- **Format:** GeoTIFF (single-band float; bit depth `[UNVERIFIED]`). NoData `[UNVERIFIED; conventionally
  -9999 or NaN]`.
- **Tiling: 1 km × 1 km.** Internal layer name `dom20_DOM` / token `dom20dom`. `[UNVERIFIED]`: exact
  tile filename pattern. Tile ~30–50 MB.
- **Download:** single tile `…opendatagrid?…layers=dom20_DOM…` ; polygon→metalink `…/poly2metalink/metalink/dom20dom?data=dom20dom&service=polygon`
  ; per Gemeinde `…/odd/a/dom20/meta/DOM/kml/gemeinde.kml?service=kml` (≤24 GB) ; per Landkreis (≤126 GB).
  `[UNVERIFIED]` whether a whole-Bavaria DOM20 metalink exists.
- **Update:** in the Bayernbefliegung cycle (*losweise*). Docs: `https://www.ldbv.bayern.de/produkte/landschaftsinformationen/dom.html`.

## DOP20 RGB / DOP20 CIR / DOP40 RGB — Digitale Orthophotos

Rectified, scale-true aerial orthophotos from the Bayernbefliegung. **DOP40 is what the pipeline
uses.** GeoTIFF only as a download (JPEG only via WMS/WMTS — see [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §2).
- **DOP40 RGB** (`dop40`): 40 cm GSD, RGB GeoTIFF (~12–20 MB/tile), 1×1 km. Filename
  **`32<east_km>_<north_km>.tif`** — note the **"32" prefix** (UTM zone), e.g. `32638_5334.tif`. URL
  `https://download1.bayernwolke.de/a/dop40/data/32638_5334.tif`. Download: single tile
  `…opendatagrid?…layers=dop40…` ; per Gemeinde `…/odd/a/dop40/meta/kml/gemeinde.kml` (≤4 GB) /
  Landkreis (~30 GB) / Bezirk (~300 GB) ; static metalink **per Regierungsbezirk**
  `https://geodaten.bayern.de/odd/a/dop40/meta/metalink/091.meta4`…`097.meta4` (`[VERIFIED]` live);
  whole-set `…/dop40/meta/metalink/09.meta4` `[UNVERIFIED]`. WMTS layer `by_dop`.
- **DOP20 RGB** (`dop20rgb`): 20 cm GSD, RGB GeoTIFF (~20–50 MB/tile), 1×1 km, `32<E>_<N>.tif` under
  `…/a/dop20/data/…`. Download: single tile `…layers=dop20…` ; polygon→metalink token `dop20rgb` ;
  per Gemeinde `…/odd/a/dop20/meta/kml/gemeinde.kml` (≤24 GB) / Landkreis (≤126 GB). WMS
  `https://geoservices.bayern.de/od/wms/dop/v1/dop20?` — layers `DOP20`, `by_dop20c` (Farbe),
  `by_dop20g` (Graustufen), `by_dop20cir` (CIR), `by_dop20_info` (Metadaten); formats jpeg/png/tiff/
  vnd.jpeg-png; CRS 25832/25833/31468/4258/4326/3857/5678. Historische-DOP WMS
  `…/od/wms/histdop/v1/histdop?`.
- **DOP20 CIR** (`dop20_cir`): 20 cm, CIR (NIR/R/G) GeoTIFF (~20 MB/tile), 1×1 km. Download: only
  polygon→metalink token `dop20cir` (small areas as ZIP) + WMS layer `by_dop20cir` + WMTS `by_dop_cir`.
- **Common docs:** `https://www.geodaten.bayern.de/odd/m/3/html/datenhinweise/datenhinweise_dop.html`
  + GeoTIFF helper PDFs (GeoTIFF→JPEG, .tfw, merge, clip). DOP bit depth: 8-bit/channel `[UNVERIFIED;
  standard]`. Update: *losweise* (~2-yr full-state Bayernbefliegung cycle). Flight-planning metadata:
  GeoPackage + JSON linked from the DOP Datenhinweise page.
- **WMTS** (all DOP): `https://geoservices.bayern.de/od/wmts/geobasis/v1/1.0.0/WMTSCapabilities.xml`
  (layers `by_dop`, `by_dop_cir`; TileMatrixSets `bvv_gk4`/`smerc`/`adv_utm32`; JPEG/PNG; Nutzungshinweise
  `https://www.geodaten.bayern.de/odd/m/3/pdf/WMTS_Nutzungshinweise.pdf`).

## DOM-Mesh — textured 3D mesh  (key `dommesh`)

See [05-buildings-and-3d.md](05-buildings-and-3d.md) §E. SLPK/I3S, per flight day, 50–200 GB each,
whole Bavaria ~8.3 TB. `https://download1.bayernwolke.de/p/dom-mesh-slpk/<los>/DSM_Mesh.slpk`.
Partial-AOI extraction without downloading the full Los (HTTP range requests over the SLPK's
ZIP64 / I3S node pages) + the decoded geometry-buffer format: see the working spike in
[`experiments/dommesh_cutout/`](../../experiments/dommesh_cutout/).

## Laserdaten — ALS point cloud  (key `laserdaten`)

Airborne-laser-scan point cloud, first-pulse / last-pulse, 3D coords per return. CRS EPSG:25832,
height DHHN2016. Point density / accuracy not stated `[UNVERIFIED; typical Bavarian ALS ~4–10+ pts/m²]`.
- **Format: LAZ** (compressed LAS). **LAS version not stated** `[UNVERIFIED; likely 1.2 or 1.4]`.
- **Classification scheme** (from `laserdaten_punktklassenbeschreibung.pdf`, Stand Sep 2023 — note
  it deviates from the ASPRS standard; the PDF table extraction was partly garbled, so this is
  **`[UNVERIFIED — verify against the PDF]`**): 1 = unklassifiziert · 2 = Bodenpunkt (ground) · 6 =
  Gebäudepunkt (building) · 20/22 = Gewässerpunkt (water; only sporadically in older data) · 23 =
  (until 2020 Brückenpunkt / from 2021 Kellerabgang) · 24 = Objektpunkt (e.g. vegetation) · plus
  "synthetischer/abgeleiteter Bodenpunkt" classes (from terrestrial survey / from bDOM update).
- **Tiling: 1 km × 1 km grid.** Internal layer `laser`; files under `https://download1.bayernwolke.de/a/laser/...`
  `[UNVERIFIED exact filename — likely `<E_km>_<N_km>.laz`]`. **Up to ~1 GB per km² tile.**
- **Download:** single tile `…opendatagrid?title=Opendata_Auswahl_Laserdaten&layers=laser&service=wms`
  ; polygon→metalink token `laser` ; per Gemeinde `…/odd/a/laser/meta/kml/gemeinde.kml?service=kml`
  (≤100 GB). **No `…/a/laser/meta/metalink/09.meta4`** (404 — no whole-Bavaria download).
- **Update:** *losweise* (each new ALS campaign). Docs PDF:
  `https://www.ldbv.bayern.de/mam/ldbv/dateien/laserdaten_punktklassenbeschreibung.pdf`.

## Geländerelief (hillshade)  (key `gelaenderelief`)  /  Höhenlinien  (key `hoehenlinien`)

- **Geländerelief:** greyscale hillshade from the DGM. WMS `https://geoservices.bayern.de/od/wms/dgm/v1/relief?`
  (CRS 25832/25833/31468/4258/4326/3857; PNG/JPEG/TIF). GeoTIFF download via polygon→metalink token
  `relief`, 1×1 km. (Non-OpenData hillshade base: `https://geoservices.bayern.de/pro/wms/dgm/v1/relief`.)
- **Höhenlinien:** contour lines derived from DGM5. WMS `https://geoservices.bayern.de/od/wms/dgm/v1/hl?`.
  GeoTIFF download via polygon→metalink token `hl`, 1×1 km.

## Pipeline notes

- We already ingest **DGM1 GeoTIFF** (terrain) and **DOP40 GeoTIFF** (ortho). DGM1 values are
  Float32 elevation (~400–600 m ASL near Munich) — they look "white" in a normal image viewer; that's
  correct. Load heightmaps as colorspace "Non-Color", ortho as "sRGB".
- **DGM5** needs the `.zip`→`.txt`→GeoTIFF conversion (`geo_import.dgm5_xyz_to_geotiffs`).
- The "32" prefix on DOP filenames vs. no prefix on DGM filenames is a frequent 404 source — see
  [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §4.
- For higher detail than DGM1, **Laserdaten (LAZ)** is the raw source — but it's huge (up to 1 GB/km²)
  and there's no whole-Bavaria download; pull only the AOI tiles via the polygon→metalink or
  per-Gemeinde KML.
