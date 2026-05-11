# OpenData Bayern — reference documentation

In-depth notes on the Bavarian geo-OpenData ecosystem (Bayerische Vermessungsverwaltung /
LDBV — `geodaten.bayern.de`), focused on what's useful for the OpenMap → Blender pipeline.
Researched May 2026 via the live portal/services + the official PDFs + the AdV GeoInfoDok.
**Nothing here is implemented yet** — this is the "understand first" deliverable.

> Supersedes the old single file `docs/opendata-bayern-datasets.md` (deleted). If you read an
> earlier version of that file that said *"ATKIS Basis-DLM has buildings"* — see
> [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md), §1. Short version: the AdV
> *schema* defines a Gebäude object area; **Bavaria does not populate it** (verified by reading
> the actual download). Buildings in Bavaria = ALKIS + LoD2 only.

## Files

| File | Contents |
|---|---|
| [00-portal-and-conventions.md](00-portal-and-conventions.md) | How the portal works: the `opengeodata_datensaetze.json` catalogue, URL layout, Metalink / `poly2metalink`, the `download1.bayernwolke.de` CDN, KML selectors, licence + attribution, CRS, height systems. **Read this first if you're writing a downloader.** |
| [01-product-catalog.md](01-product-catalog.md) | The full inventory — ~35 products / ~121 distributions — with download URLs, formats, sizes, update cycles. Includes the "what's NOT there" list (no DOP80, no DGM20/25, no Hausnummern download, etc.). |
| [02-atkis-basis-dlm.md](02-atkis-basis-dlm.md) | ATKIS Basis-DLM deep dive: the AAA object catalogue, the **verified** Shape-layer list (from the actual `bkg_shape_712.zip`), per-layer attribute fields, NAS vs Shape vs GeoPackage-"plus", the buildings question (definitively answered), the WFS feature-type list. |
| [03-alkis-tatsaechliche-nutzung.md](03-alkis-tatsaechliche-nutzung.md) | ALKIS "Tatsächliche Nutzung" — the parcel-sharp land-use polygons, per-Landkreis Shapes, the empty-`bez` caveat. |
| [04-landnutzung-ln.md](04-landnutzung-ln.md) | "Landnutzung (LN)" — the derived socio-economic land-use GeoPackage; + a note on its sibling "Landbedeckung (LB)". |
| [05-buildings-and-3d.md](05-buildings-and-3d.md) | **Everything about buildings & 3D**: LoD2 CityGML (the only source of 3D building geometry — attributes, semantic surfaces, tiling, download), ALKIS `AX_Gebaeude` attribute catalogue, the `AX_Dachform` / `AX_Gebaeudefunktion` / `AX_Bauweise_Gebaeude` codelists, Hausumringe, DOM-Mesh. |
| [06-einzelbaeume.md](06-einzelbaeume.md) | "Einzelbäume" — single-tree point layer (real tree positions + heights). |
| [07-raster-dgm-dop-dom-laser.md](07-raster-dgm-dop-dom-laser.md) | The raster/point-cloud geobasis products: DGM1, DGM5 (XYZ-zip), DOM20, DOP20 RGB/CIR, DOP40, DOM-Mesh, Laserdaten (LAZ), Geländerelief, Höhenlinien — formats, NoData, tiling, Metalink URLs. |
| [08-adv-geoinfodok-specs.md](08-adv-geoinfodok-specs.md) | The authoritative AdV documents: GeoInfoDok 7.1.2 (AAA-Anwendungsschema, ATKIS-OK, ALKIS-OK), all the AdV-Produktspezifikationen (Shape/GeoPackage/WFS/...), the Landnutzung/Landbedeckung schemas, the GDI-DE codelist registry. With direct PDF URLs. |
| [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) | **Where this documentation (or its authors) were wrong, and common mistakes.** Read this if a claim elsewhere in the docs seems off, or before you repeat a mistake someone already made. |

## Conventions used in these files

- **`[VERIFIED]`** — confirmed by directly fetching the data/service/PDF (e.g. parsed the DBF
  header, queried GetCapabilities, read the PDF text).
- **`[UNVERIFIED]`** — stated in a source but not independently checked, or inferred. Treat with
  caution; verify before relying on it.
- **`[CONFLICT]`** — two sources disagree; both versions noted.
- All URLs are quoted verbatim. The `adv-online.de` PDF URLs carry opaque `?imgUid=…` query
  strings that are part of the working link; if one 404s, browse from the page given alongside.

## TL;DR for the pipeline

- **Terrain**: `DGM1` (1 m GeoTIFF, NoData −9999) — what we already use. `DGM5` only as XYZ-in-ZIP.
- **Imagery**: `DOP40` (40 cm GeoTIFF) — what we already use. `DOP20` RGB/CIR for higher res.
- **Buildings (3D)**: `LoD2` CityGML — what we already use. The only source of 3D building
  geometry; carries `function`/`roofType`/`measuredHeight`/`storeysAboveGround`/address + semantic
  RoofSurface/WallSurface faces. **Not in ATKIS.** Full ALKIS building attrs are chargeable.
- **Buildings (2D footprints, no attrs)**: `Hausumringe` (Shape, per Regierungsbezirk).
- **Real tree positions**: `Einzelbäume` (GeoPackage points, per Bayernbefliegung block; `dgmhoehe`
  = height above ground). Replaces our procedural scatter if we want real placement.
- **Roads / rail / water lines / land-use polygons / admin / relief**: `ATKIS Basis-DLM`
  (GeoPackage "plus" — single bayernwide ZIP — is the most convenient). The "OSM-equivalent" but
  official, ±3 m on roads, weekly-refreshed. **No buildings in it.**
- **Land-use polygons, parcel-sharp**: `Tatsächliche Nutzung` (per-Landkreis Shape, tiny) or
  `Landnutzung (LN)` (socio-economic function, 5.3 GiB single GeoPackage).
- **Catalogue entry point for any downloader**: `https://geodaten.bayern.de/opengeodata/json/opengeodata_datensaetze.json`
- **Licence**: CC BY 4.0 (except ALKIS-Parzellarkarte = CC BY-ND 4.0). Attribution string:
  `Datenquelle: Bayerische Vermessungsverwaltung – www.geodaten.bayern.de`.
- **CRS**: EPSG:25832 everywhere. Height: DHHN2016 / NHN.
