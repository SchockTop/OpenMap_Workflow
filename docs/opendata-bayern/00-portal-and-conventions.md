# 00 — Portal mechanics & conventions

How the Bavarian geo-OpenData portal is wired. Read this before writing any downloader.

## The catalogue (the one source of truth)

```
https://geodaten.bayern.de/opengeodata/json/opengeodata_datensaetze.json
```
A single JSON: `{ "@type": …, "datensaetze": [ … ] }`. ~**121 distribution ("abgabe") records
across ~35 products**. Each record has these keys (verified):

`abgabe_name, abgabe_titel, abgabe_beschreibung, abgabe_link, abgabe_transfertype,
abgabe_datenformate, abgabe_datenaktualitaet, abgabe_gebiet, abgabe_srs, abgabe_datenhinweise,
abgabe_datenmenge, produkt_produktname, produkt_titel, produkt_beschreibung, produkt_lizenz,
produkt_hinweise, produkt_vorschau, produkt_metadatenid` (the last is `null` for all records;
there is **no** `kategorie` field).

The HTML portal pages (`https://geodaten.bayern.de/opengeodata/OpenDataDetail.html?pn=<produkt_produktname>`)
are JS apps that render *from* this JSON — fetching the HTML gives you nothing useful. **Always
parse the JSON.** (See pitfall #5 in [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md).)

## `abgabe_transfertype`

- **`DOWNLOAD`** — a single small file ("Einzeldownload"). The `abgabe_link` is usually either a
  direct file URL, or a KML "selector" (`…/meta/kml/kachelung.kml?service=kml` — pick a tile), or
  the `opendatagrid` WMS tile-picker (see below).
- **`MASSENDOWNLOAD`** — large. Delivered via Metalink (`.meta4`) for whole-Bavaria / per
  Regierungsbezirk / Landkreis / Gemeinde, or via the `poly2metalink` service for a drawn polygon
  (small areas come back as a ZIP).
- **`SERVICE`** — a WMS / WMTS / WFS / Vector-Tiles endpoint, not a download.

## URL layout

| Path prefix | Holds |
|---|---|
| `https://geodaten.bayern.de/odd/m/2/<product>/<format>/<file>` | bulk geobasis files (e.g. `…/odd/m/2/basisdlm/nas/nas_712.zip`, `…/odd/m/2/basisdlm/bkg_shape/bkg_shape_712.zip`, `…/odd/m/2/basisdlm/plus/by_basisdlm_plus.zip`, `…/odd/m/2/dtkvektor/dtk50/DTK50_Vektor_QGIS.zip`, `…/odd/m/2/webvektor/webkarte_vektor_bayern.tar.gz`, `…/odd/m/2/freizeitwege/…`) |
| `https://geodaten.bayern.de/odd/m/3/daten/<product>/<file>` | smaller bayernwide files (e.g. `…/odd/m/3/daten/tn/Nutzung_kreis.gpkg`, `…/odd/m/3/daten/ln/landnutzung.gpkg`, `…/odd/m/3/daten/dtk500/tif/dtk500_tif.zip`, `…/odd/m/3/daten/DOMMesh/DOM_Mesh_projektgebiete_2026.kml`, `…/odd/m/3/daten/historischekarten/…`) |
| `https://geodaten.bayern.de/odd/m/3/pdf/<file>.pdf` | Datenformatbeschreibungen + howto PDFs (often image-only scans). **Not browsable.** |
| `https://www.geodaten.bayern.de/odd/m/3/html/datenhinweise/datenhinweise_<x>.html` | the "Datenhinweise" HTML pages. **Not browsable.** |
| `https://geodaten.bayern.de/odd/m/3/vorschau/produkt_<key>.png` | product preview thumbnails |
| `https://geodaten.bayern.de/odd/m/4/<x>` | ALKIS-derived Shape zips (e.g. `…/odd/m/4/verwaltung/alkis-verwaltung.zip`, `…/odd/m/4/verwaltung/alkis-katasterbezirk.zip`, `…/odd/m/4/tn/lkr/kml/kreis.kml`) |
| `https://geodaten.bayern.de/odd/m/8/<x>` | e.g. `…/odd/m/8/baeume3d/data/<code>_baeume.gpkg`, `…/odd/m/8/baeume3d/kml/Einzelbaumstandorte.kml` |
| `https://geodaten.bayern.de/odd/a/<product-path>/meta/metalink/<key>.meta4` | static Metalink indices (see below) |
| `https://geodaten.bayern.de/odd/a/<product-path>/meta/kml/{gemeinde,kreis,regierungsbezirk,kachelung}.kml?service=kml` | KML selectors that map an admin unit / tile to its `.meta4` or file |
| `https://download1.bayernwolke.de/a/<product-path>/<file>` (mirror `download2.bayernwolke.de`) | the actual tile/data files referenced inside `.meta4` (DOM-Mesh uses `/p/dom-mesh-slpk/<los>/DSM_Mesh.slpk` instead of `/a/…`) |
| `https://www.ldbv.bayern.de/mam/ldbv/dateien/<file>.pdf` | LDBV-hosted PDFs (some Datenformatbeschreibungen, faltblätter) |

## Metalink (`.meta4`) — the bulk-download mechanism

A `.meta4` is IETF Metalink 4 XML (`urn:ietf:params:xml:ns:metalink`), generator `BVV-MetaLinker`,
with `<published>`, `<size>`, `<hash type="sha-256">` and two mirror `<url>`s (`download1`/
`download2.bayernwolke.de`) per file. Static indices live at
`https://geodaten.bayern.de/odd/a/<product-path>/meta/metalink/<key>.meta4` where `<key>` is:
- `09` — all of Bavaria (Bayern's AdV/ARS Land code is `09`). Confirmed for: `dgm/dgm1`,
  `dgm/dgm5xyz`, `lod2/citygml`, `dtk/k/{dok,dtk25,dtk50,dtk100}`.
- `091`…`097` — per Regierungsbezirk (confirmed live for `dop40`: `091.meta4`…`097.meta4`). The
  digit→Bezirk mapping is **[UNVERIFIED]** (likely 091 Oberbayern … 097 Schwaben).
- an 8-digit Amtlicher Gemeindeschlüssel — per Gemeinde (e.g. `…/lod2/citygml/meta/metalink/09276115.meta4`).
- a Landkreis key — per Landkreis.
Use a download manager: `aria2c -V --follow-metalink=mem --dir=<dir> <metalink_url>`. Howto PDFs:
`https://www.geodaten.bayern.de/odd/m/3/pdf/informationen_metalink.pdf`, `…/metalink_aria2c.pdf`,
`…/metalink_browser.pdf`.

## `poly2metalink` — polygonal selection

Base: `https://geoservices.bayern.de/services/poly2metalink/`. Sub-paths:
- `datasets` — JSON map of dataset-token → `{description, maxPointsPerGeom:20000, areaLimitQkm,
  maxTilesToZip, type:"geopackage"|"wms", imageFormat}`. **Dataset tokens** (≠ product keys):
  `dgm1` (10000 km²/25 tiles), `dgm5xyz`, `dom20dom` (100/4), `dop40` (1000/10), `dop20rgb` (100/4),
  `dop20cir` (25/4), `laser` (2000/25), `relief` (25/10), `hl` (1000/100), `lod2` (1000/25),
  `parzellarkarte` (10/4).
- `datasets/<token>` — that token's limits. Unknown token → `"Invalider Dataset-Name."`.
- `metalink/<token>?data=<token>&service=polygon` — the URL the portal SPA shows; it POSTs the
  drawn polygon's EWKT and gets back a `.meta4` (or a zip-start URL for small areas).
- `zip/start/<token>/<uuid>` + `zip/progress/…` — async ZIP build.
- `shortInfo/<token>` — pre-flight size/tile-count estimate.

## The `opendatagrid` helper WMS (the "Einzeldownload" tile picker)

`https://geoservices.bayern.de/od/wms/grid/v1/opendatagrid?title=Opendata_Auswahl_<X>&layers=<layer>&service=wms`
— not a content WMS; it's the clickable km-grid that the portal uses for single-tile downloads
(`<layer>` ∈ `dop40`, `dop20`, `dgm1`, `dgm1xyz`, `dgm5xyz`, `dom20_DOM`, `lod2`, `laser`, …).

## Licence & attribution

Page: `https://www.geodaten.bayern.de/odd/m/3/html/nutzungsbedingungen.html`. Default:
**CC BY 4.0** (`https://creativecommons.org/licenses/by/4.0/deed.de`). **Exception:** the
**ALKIS-Parzellarkarte** is **CC BY-ND 4.0** (`…/by-nd/4.0/…` — you may not distribute a modified
version). Required attribution string for everything:
> **Datenquelle: Bayerische Vermessungsverwaltung – www.geodaten.bayern.de**
(sometimes with a year, e.g. `© Bayerische Vermessungsverwaltung – www.geodaten.bayern.de, 2026`).
Most geobasis products are flagged HVD (High-Value Dataset under the EU Open Data Directive).

## CRS & height systems (all products)

- Horizontal CRS: **EPSG:25832** — ETRS89 / UTM Zone 32N. Axis order Easting/Northing (X/Y), meters.
  Coordinates in raw products carry the false-easting 500000 but **no** zone digit prepended
  (e.g. `692345.123`); the "32" you sometimes see is only in *filenames* (DOP/LoD2), not in the data.
- Vertical: **DHHN2016** (= NHN, Normalhöhennull) for DGM/DOM/DOP/LoD2 etc.; reference ellipsoid
  GRS80, datum ETRS89. (Some metadata also cites EPSG:7837 for the height component of 3D products.)
- WMS/WFS additionally serve EPSG 25833, 31468 (GK4), 4258 (ETRS89 geo), 4326 (WGS84), 3857
  (Pseudo-Mercator), occasionally 5678/5679.

## Web services index (OpenData)

- **WMTS** (one endpoint, many layers): `https://geoservices.bayern.de/od/wmts/geobasis/v1/1.0.0/WMTSCapabilities.xml`
  — layers `by_webkarte`, `by_webkarte_grau`, `by_label`, `by_amtl_karte` (DOK/DTK25/50/100/500/
  Parzellarkarte), `by_dop` ("Luftbild Bayern"), `by_dop_cir`. TileMatrixSets `bvv_gk4`, `smerc`,
  `adv_utm32`. Tile URL template `https://wmtsod[1-9].bayernwolke.de/wmts/{Layer}/{TileMatrixSet}/{TileMatrix}/{TileCol}/{TileRow}`.
- **WMS** (`https://geoservices.bayern.de/od/wms/...`):
  `dop/v1/dop20?` (layers `DOP20`, `by_dop20c`, `by_dop20g`, `by_dop20cir`, `by_dop20_info`),
  `histdop/v1/histdop?`, `dtk/v1/{dok,dtk25,dtk50,dtk100,dtk500}?`, `atkis/v1/freizeitwege?`,
  `alkis/v1/{verwaltungsgrenzen,tn,parzellarkarte}?`, `afis/v1/festpunkte?`, `dgm/v1/relief?`,
  `dgm/v1/hl?`. (The DOP `dop40` is served via the `dop20` WMS layers / the WMTS — there's no
  separate `dop40` WMS.) For the relief WMS the DGM-derived hillshade base URL is
  `https://geoservices.bayern.de/pro/wms/dgm/v1/relief` (the `pro/` host) for non-OpenData, and
  `https://geoservices.bayern.de/od/wms/dgm/v1/relief?` for OpenData.
- **WFS**: `https://geoservices.bayern.de/wfs/v1/ogc_atkis_basisdlm.cgi?` — ATKIS Basis-DLM (NAS).
- **Vector Tiles**: `https://vtod1.bayernwolke.de/styles/by_style_*.json` (standard/grau/luftbild/
  nacht/wandern/radln/oepnv/baeume/light/gelaende/winter).

## Community tooling

- `https://github.com/mueckl/opendata_bayern_download` — a downloader for Bayern OpenData (handy
  reference for the Metalink loops, e.g. DOP40 = `091…097.meta4`).
- `https://github.com/mueckl/bvv-offline` — offline-tile tooling.
