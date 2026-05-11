# 01 — Product catalogue

Every OpenData product on `geodaten.bayern.de` (state ≈ May 2026, from
`opengeodata_datensaetze.json` — ~35 products / ~121 distributions). For the ones relevant to the
pipeline there's a dedicated chapter; here is the full overview + the download endpoints. All
**CC BY 4.0** except where noted; all **EPSG:25832**; height **DHHN2016**.

`DL` = single-file Download · `MD` = Massendownload (Metalink/poly2metalink) · `SVC` = service.

## Raster geobasis

| Product key | What | Distributions (format, granularity, size, refresh) |
|---|---|---|
| `dop40` | DOP RGB 40 cm | DL GeoTIFF, 1×1 km tile, ~12–20 MB, *losweise* — `…/od/wms/grid/v1/opendatagrid?title=Opendata_Auswahl_DOP40&layers=dop40&service=wms` ; MD per Gemeinde `…/odd/a/dop40/meta/kml/gemeinde.kml` (≤4 GB) / Landkreis `…/kml/kreis.kml` (~30 GB) / Bezirk `…/kml/regierungsbezirk.kml` (~300 GB) ; static `.meta4` per Bezirk `…/odd/a/dop40/meta/metalink/091.meta4`…`097.meta4` ; SVC WMTS `by_dop`. Tiles: `https://download1.bayernwolke.de/a/dop40/data/32<E_km>_<N_km>.tif`. dh `datenhinweise_dop.html` |
| `dop20rgb` | DOP RGB 20 cm | DL GeoTIFF, 1×1 km, ~20–50 MB, *losweise* — `…opendatagrid?…layers=dop20…` ; MD poly2metalink `dop20rgb`, per Gemeinde `…/odd/a/dop20/meta/kml/gemeinde.kml` (≤24 GB) / Landkreis (≤126 GB) ; SVC WMS `…/od/wms/dop/v1/dop20?` (layers `DOP20`,`by_dop20c`,`by_dop20g`,`by_dop20cir`,`by_dop20_info`) + Historische-DOP WMS `…/histdop/v1/histdop?` + WMTS |
| `dop20_cir` | DOP Colorinfrarot 20 cm | MD GeoTIFF only, poly2metalink token `dop20cir`, ~20 MB/tile ; SVC via the dop20 WMS layer `by_dop20cir` + WMTS `by_dop_cir` |
| `dgm1` | DGM 1 m (bare-earth terrain) | DL GeoTIFF `…opendatagrid?…layers=dgm1…` (~3–4 MB/tile) ; DL XYZ-ASCII `…layers=dgm1xyz…` (~4 MB/tile) ; MD GeoTIFF poly2metalink `dgm1` ; per Gemeinde `…/odd/a/dgm/dgm1/meta/kml/gemeinde.kml` (≤500 MB) / Landkreis (~5 GB) ; komplett `…/odd/a/dgm/dgm1/meta/metalink/09.meta4` (~240 GB). Tiles `https://download1.bayernwolke.de/a/dgm/dgm1/<E_km>_<N_km>.tif`. NoData −9999 (conventional; not in the HTML doc). dh `datenhinweise_dgm.html` + `hinweise_daten_dgm1_download.pdf` |
| `dgm5` | DGM 5 m | DL **`.zip` containing XYZ-ASCII** `…opendatagrid?…layers=dgm5xyz…` (~0.2 MB/tile, 1×1 km) ; MD poly2metalink `dgm5xyz` ; komplett `…/odd/a/dgm/dgm5xyz/meta/metalink/09.meta4` (~14 GB). Tiles `…/a/dgm/dgm5xyz/<E_km>_<N_km>.zip`. **No GeoTIFF variant.** dh `datenhinweise_dgm.html` |
| `dom20` | DOM 20 cm (surface incl. trees+buildings) | DL GeoTIFF, 1×1 km, ~30–50 MB, *losweise* — `…opendatagrid?…layers=dom20_DOM…` ; MD poly2metalink token `dom20dom` ; per Gemeinde `…/odd/a/dom20/meta/DOM/kml/gemeinde.kml` (≤24 GB) / Landkreis (≤126 GB). dh `ldbv…/landschaftsinformationen/dom.html` |
| `dommesh` | DOM-Mesh — textured 3D mesh | DL **SLPK** (zipped OGC I3S), per Flugtag/Los, **50–200 GB each**, whole BY ~8.3 TB — selector `…/odd/m/3/daten/DOMMesh/DOM_Mesh_projektgebiete_2026.kml` ; files `https://download1.bayernwolke.de/p/dom-mesh-slpk/<los>/DSM_Mesh.slpk`. dh `datenhinweise_dommesh.html` |
| `laserdaten` | ALS point cloud | DL **LAZ**, 1×1 km tile, up to ~1 GB/tile, *losweise* — `…opendatagrid?…layers=laser…` ; MD poly2metalink `laser` ; per Gemeinde `…/odd/a/laser/meta/kml/gemeinde.kml` (≤100 GB). No `09.meta4` (404). dh `laserdaten_punktklassenbeschreibung.pdf` |
| `gelaenderelief` | Hillshade (from DGM) | SVC WMS `…/od/wms/dgm/v1/relief?` ; MD GeoTIFF poly2metalink `relief`, 1×1 km |
| `hoehenlinien` | Contour lines (from DGM5) | SVC WMS `…/od/wms/dgm/v1/hl?` ; MD GeoTIFF poly2metalink `hl`, 1×1 km |

→ details: [07-raster-dgm-dop-dom-laser.md](07-raster-dgm-dop-dom-laser.md)

## Vector geobasis

| Product key | What | Distributions |
|---|---|---|
| `atkis_basis_dlm` | ATKIS Basis-DLM | DL **NAS** `…/odd/m/2/basisdlm/nas/nas_712.zip` (~2.17 GB, weekly) ; DL **GeoPackage "plus"** `…/odd/m/2/basisdlm/plus/by_basisdlm_plus.zip` (~1.85 GB, weekly, + QGIS project) ; DL **Shape** `…/odd/m/2/basisdlm/bkg_shape/bkg_shape_712.zip` (~1.24 GB, monthly) ; SVC WFS `…/wfs/v1/ogc_atkis_basisdlm.cgi?`. dh `datenhinweise_atkis_basisdlm_plus.html` (PDFs `basisdlm_plus_datenformatbeschreibung.pdf`, `basisdlm_plus_datenmodell.pdf`) + LDBV `landschaftsinformationen/landschaftsmodell.html`. → [02-atkis-basis-dlm.md](02-atkis-basis-dlm.md) |
| `tatsaechlichenutzung` | ALKIS Tatsächliche Nutzung | DL **Shape per Landkreis** — selector `…/odd/m/4/tn/lkr/kml/kreis.kml`; tiles `https://download1.bayernwolke.de/a/tn/lkr/tn_<KRS>.zip` (~2–25 MB), monthly ; DL **GeoPackage bayernwide** `…/odd/m/3/daten/tn/Nutzung_kreis.gpkg` (~5.3 GiB, monthly) ; SVC WMS `…/od/wms/alkis/v1/tn?`. dh `hinweise_daten_tn_download.pdf`. → [03-alkis-tatsaechliche-nutzung.md](03-alkis-tatsaechliche-nutzung.md) |
| `ln` | Landnutzung (LN) | DL **GeoPackage bayernwide** `…/odd/m/3/daten/ln/landnutzung.gpkg` (~5.3 GiB, annual; Aktualität 01.01.2026). dh LDBV `liegenschaftsinformationen/landnutzung.html` + `dfb_landnutzung_nicht_barrierefrei.pdf`. → [04-landnutzung-ln.md](04-landnutzung-ln.md) |
| `einzelbaeume` | Single-tree points | DL **GeoPackage per Bayernbefliegung block** (200 MB–1 GB each, *losweise*) — index KML `…/odd/m/8/baeume3d/kml/Einzelbaumstandorte.kml?service=kml`; files `…/odd/m/8/baeume3d/data/<gebietscode>_baeume.gpkg`. dh `datenhinweise_einzelbaeume.html` + `einzelbaeume_datenformat.pdf`. → [06-einzelbaeume.md](06-einzelbaeume.md) |
| `hausumringe` | Building footprint polygons (no ALKIS attrs) | DL **Shape per Regierungsbezirk** (~100 MB each, quarterly) — selector `…/odd/m/3/daten/hausumringe/bezirk/kml/HU_regierungsbezirk.kml?service=kml`. → [05-buildings-and-3d.md](05-buildings-and-3d.md) |
| `lod2` | 3D-Gebäudemodelle LoD2 | DL **CityGML 1.0**, 2×2 km tile, ~10–100 MB, **weekly** — `…opendatagrid?…layers=lod2…` ; MD poly2metalink `lod2` ; per Gemeinde `…/odd/a/lod2/citygml/meta/kml/gemeinde.kml` (≤500 MB; ≤6 GB kreisfreie Städte) / Landkreis (≤6 GB) ; komplett `…/odd/a/lod2/citygml/meta/metalink/09.meta4` (~150 GB). Tiles `https://download1.bayernwolke.de/a/lod2/citygml/<E_km>_<N_km>.gml`. dh `hinweise_daten_lod2_download.pdf`. → [05-buildings-and-3d.md](05-buildings-and-3d.md) |
| `verwaltung` | ALKIS Verwaltungsgebiete + Katasterbezirke | DL **Shape bayernwide** `…/odd/m/4/verwaltung/alkis-verwaltung.zip` (~130 MB, monthly) + `…/alkis-katasterbezirk.zip` (~50 MB) ; SVC WMS `…/od/wms/alkis/v1/verwaltungsgrenzen?` (daily). dh `hinweise_daten_verwaltungsgebiete_download.pdf`, `hinweise_daten_katasterbezirke_download.pdf` |
| `parzellarkarte` | ALKIS-Parzellarkarte (Flurkarte w/o parcel numbers) — **CC BY-ND 4.0** | SVC WMS `…/od/wms/alkis/v1/parzellarkarte?` + WMTS ; MD GeoTIFF poly2metalink `parzellarkarte`, 1×1 km, daily |

## Topographic maps (raster)

| Key | What | Notes |
|---|---|---|
| `dok` | Digitale Ortskarte 1:10 000 | GeoTIFF (LZW), 10×10 km tile, ~25 MB, quarterly. `…/odd/a/dtk/k/dok/meta/kml/{kachelung,gemeinde,kreis}.kml` ; komplett `…/odd/a/dtk/k/dok/meta/metalink/09.meta4` (~15 GB) ; WMS `…/od/wms/dtk/v1/dok?` + WMTS. 6 colour palettes (.pal). Grundriss from ALKIS-Flurkarte, rest from ATKIS Basis-DLM. dh `datenhinweise_dtk.html` |
| `dtk25` | DTK 1:25 000 | GeoTIFF, 20×20 km, annual. `…/odd/a/dtk/k/dtk25/…` ; komplett `…/metalink/09.meta4` (~8 GB) ; WMS `…/dtk/v1/dtk25?` |
| `dtk50` | DTK 1:50 000 | GeoTIFF, 40×40 km, annual ; komplett `…/dtk/k/dtk50/meta/metalink/09.meta4` (~3 GB) ; WMS `…/dtk/v1/dtk50?` |
| `dtk100` | DTK 1:100 000 | GeoTIFF, 40×40 km, annual, reduced content ; komplett `…/dtk100/…/09.meta4` (~1 GB) ; WMS `…/dtk/v1/dtk100?` |
| `dtk500` | DTK 1:500 000 | TIF, bayernwide `…/odd/m/3/daten/dtk500/tif/dtk500_tif.zip` (~40 MB) ; WMS `…/dtk/v1/dtk500?` |
| `tk2000` | ÜK Bayern 1:2 Mio | PDF, bayernwide — `…/odd/m/3/daten/tk2000/pdf/tk2000_bayern_{n,v,a}.pdf` |
| `tk50_tk100` | TK50/TK100 PDF (zivilmilitärisch) | PDF per TK-Blatt — selectors `…/odd/m/2/tkpdf/meta/kml/tkpdf_50.kml`, `…/tkpdf100.kml` |
| `dtkvektor` | DTK Vektor (DTK50 + DTK500 vector) | GeoPackage `…/odd/m/2/dtkvektor/dtk50/DTK50_Vektor_QGIS.zip` (~1.3 GB) + ArcGIS variant (~2.3 GB) + DTK500 vector (~19/32 MB), annual, incl. project files. dh `datenhinweise_dtkvektor.html` (datamodel PDFs `DTK50_Vektor_Datenmodell.pdf`, `DTK500_Vektor_Datenmodell.pdf`, …) |
| `webkarte` | Webkarte Bayern | SVC WMTS `by_webkarte` ; DL Vector Tiles `…/odd/m/2/webvektor/webkarte_vektor_bayern.tar.gz` (~2.8 GB, Pseudo-Mercator, monthly) ; SVC Vector Tiles `https://vtod1.bayernwolke.de/styles/by_style_standard.json` (+ grau/luftbild/nacht/wandern/radln/oepnv/baeume/light/gelaende/winter). dh `datenhinweise_webvektor.html` |
| `bvv_histkarten` | Historische Karten & Wening-Ansichten | PDF (hist TK25 1919–2008, TK50 1957–2018, TK100 1971–2005) + JPEG (Wening Ortsansichten ab 1696) — selectors under `…/odd/m/3/daten/historischekarten/…/kml/…`. dh `ldbv…/karten/hist_tk.html`, `…/karten/wening.html` |

## Points / reference / leisure

| Key | What |
|---|---|
| `bvv_referenzpunkte` | Geodätische Referenzpunkte (GNSS check points) — KML `…/odd/m/3/daten/referenzpunkte/referenzpunkte.kml`, half-yearly |
| `bvv_mittelpunkte` | Mittelpunkte aller Landkreise/Regierungsbezirke — KML `…/odd/m/3/daten/mittelpunkte/mittelpunkte.kml`, annual |
| `festpunkte` | AFIS Lage-/Höhenfestpunkte — SVC WMS `…/od/wms/afis/v1/festpunkte?` |
| `bvv_freizeit` | Freizeitthemen (point KMLs: Schwimmbäder, Freilichtmuseen, Freizeitparks, Zoos, Sommerrodelbahnen, Thermen, Hallenbäder, Eislaufsport, Winterrodelbahnen) — `…/odd/m/2/freizeitthemen/kml/*.kml`, monthly |
| `bvv_radwege` | Beschilderte Rad-/Mountainbikewege — Shape `…/odd/m/2/freizeitwege/radwege/shape/radwege_shape.zip` (~42 MB) + GPX, monthly ; WMS `…/od/wms/atkis/v1/freizeitwege?` |
| `bvv_bayernnetzradler` | Bayernnetz für Radler (~123 Fernrouten, ~9000 km) — Shape `…/odd/m/2/freizeitwege/bayernnetzradler/shape/bayernnetzradler_shape.zip` (~3 MB) + GPX |
| `bvv_wanderwege` | Beschilderte Wanderwege — Shape `…/odd/m/2/freizeitwege/wanderwege/shape/wanderwege_shape.zip` (~55 MB) + GPX, monthly |

## Explicitly NOT present (so stop looking)

- **DOP80**, **DGM20 / DGM25 / DGM10 / DGM50**, **DOM1** — not OpenData.
- **Hausnummern / Hauskoordinaten** as a download — chargeable via ZSHH; only a "WFS
  Hauskoordinaten" exists. (`Hausumringe` polygons *are* OpenData.)
- **Editable ALKIS Flurkarte** — not OpenData; the proxy is the `parzellarkarte` (CC BY-ND).
- **Standalone "Gewässer" / "Geländekanten·Bruchkanten" / "3D-Punktwolke" / "DGK5" products** —
  no (water is inside ATKIS/ALKIS-TN/DTK; LiDAR = `laserdaten` LAZ; 3D-Strukturlinien are in the
  chargeable 3D-Messdaten).
- **A browsable directory listing** — access-denied; fetch files by known name.
