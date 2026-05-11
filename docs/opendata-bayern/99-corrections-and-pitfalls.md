# Corrections & pitfalls — where we were wrong, and mistakes not to repeat

This file exists so that (a) if a claim elsewhere in this folder looks wrong, you can check
whether it's a known error, and (b) if you hit a confusing situation, you can see whether someone
already untangled it. Newest correction first.

---

## 1. "ATKIS Basis-DLM has individual building footprints" — **half-true, and misleading**

**The mistake:** An earlier draft of these notes (the deleted `docs/opendata-bayern-datasets.md`)
first said *"ATKIS Basis-DLM does NOT contain individual buildings"*, then — after reading the
AdV Produktspezifikation ATKIS-Basis-DLM-Shape v2.1 PDF, which clearly defines an
**Objektartenbereich `30000 Gebäude`** with `AX_Gebaeude` (31001) and `AX_Bauteil` (31002) and a
Shape layer `SIE05` for them — *over-corrected* to *"yes, Basis-DLM has buildings with Dachform,
Geschosse, Baujahr, …"*.

**The truth (both pieces):**
- The **AdV GeoInfoDok 7.1.2 schema** *does* define the Gebäude object area for the Basis-DLM
  modellart. `AX_Gebaeude` has rich attributes (`gebaeudefunktion`, `dachform`, `bauweise`,
  `anzahlDerOberirdischenGeschosse`, `objekthoehe`, `umbauterRaum`, `baujahr`, `zustand`,
  `gebaeudekennzeichen`, …). The Shape profile reserves `SIE05` for it. **[VERIFIED]** from
  `AdV-PS_ATKIS-Basis-DLM_Shape_v2.1` and the ATKIS-OK Basis-DLM 7.1.2 PDF.
- **Bavaria does NOT populate it.** **[VERIFIED two ways:]**
  1. The Bavarian ATKIS-Basis-DLM **WFS** GetCapabilities lists exactly **80 feature types** and
     `AX_Gebaeude` / `AX_Bauteil` are **not** among them (it has `AX_Turm`, `AX_Ortslage`, the
     `AX_Bauwerk*` classes, but no building footprints; nor `AX_Punkt3D` / `AX_Flaeche3D`).
  2. Downloading the actual `bkg_shape_712.zip` (~1.24 GB, the bayernwide Basis-DLM Shape) and
     parsing the DBF headers: layer **`sie05_f` has 5 records total, all `OBJART = 51001
     AX_Turm`** — zero `AX_Gebaeude`/`AX_Bauteil`. Layer `sie05_p` has 10,805 records, **all
     `AX_Turm`** (towers/masts). The `GFK`/`DAF`/`BJA`/`AOG` columns exist in the DBF schema but
     are `-9999`/empty even for those tower rows. So the SIE05 "building" layer is, in Bavaria,
     effectively empty.

**So:** in **Bavaria**, buildings live **only** in ALKIS (cadastre — full version chargeable;
OpenData "Hausumringe" = footprint polygons without attributes) and in **LoD2 CityGML** (3D
geometry + attributes, OpenData). Do **not** look in ATKIS Basis-DLM for Bavarian buildings.
**Other German states may populate the ATKIS Gebäude area** — that's per-state ("objektbildende
Eigenschaften sind länderspezifisch im Erhebungsprozess zu berücksichtigen"); check the relevant
Landesprofil before assuming. See [02-atkis-basis-dlm.md](02-atkis-basis-dlm.md) §"Buildings".

**Lesson:** "the AdV schema defines X" ≠ "this state's dataset contains X". Always distinguish
*schema membership* from *actual population*, and verify population against the live data, not the
spec PDF.

---

## 2. DOP / DGM are GeoTIFF only — there's no JPEG/JP2 download

The Bavarian OpenData DOP20/DOP40 (and DGM1) downloads are **GeoTIFF only**. The portal even
ships a tutorial "GeoTIFF → JPEG" (`geotiff-konvertierung_*.pdf`) — which only makes sense
*because* JPEG isn't a download option. JPEG output exists **only** via the WMS/WMTS services
(`by_dop20c` / `by_dop40c` layers can render `image/jpeg`), at lower fidelity than the source
tiles. (DGM5 is the odd one: **no GeoTIFF at all**, only an XYZ-ASCII text file inside a `.zip`.)
The Blender pipeline converts the GeoTIFFs to JPEG UDIM tiles itself (`ortho.1001.jpg` etc.).

---

## 3. DGM5 is `.zip` → `.txt` (XYZ-ASCII), tiled on a **1 km** grid, path `dgm5xyz`

Easy to get wrong because the GeoInfoDok / AdV grid for "DGM5"-class products is 2 km, and earlier
versions of our downloader catalogue had `dgm5` as `.tif`, `grid_km=2`, path `dgm/dgm5`. The
**actual** Bavarian OpenData DGM5 is: product key `dgm5`, internal `dgm5xyz`, `.zip` archives each
containing one ASCII-TXT `X Y Z` file at 5 m spacing, **1 km × 1 km tiles**, filename
`<east_km>_<north_km>.zip` (no "32" prefix), path `https://download1.bayernwolke.de/a/dgm/dgm5xyz/`,
whole-Bavaria Metalink `https://geodaten.bayern.de/odd/a/dgm/dgm5xyz/meta/metalink/09.meta4`
(~14 GB). Our downloader was fixed for this in `OpenMap_Unifier/backend/downloader.py`.

---

## 4. DOP filenames carry a "32" prefix; DGM filenames do NOT

`DOP20`/`DOP40` tiles are named `32<east_km>_<north_km>.tif` — the leading `32` is the UTM zone
number. `DGM1`/`DGM5` tiles are named `<east_km>_<north_km>.tif` (or `.zip`) — **no** prefix.
LoD2 tiles are `<east_km>_<north_km>.gml` on a **2 km** grid (even km). Getting the prefix wrong
→ 404s.

---

## 5. The portal pages are JavaScript apps — scrape the JSON, not the HTML

`https://geodaten.bayern.de/opengeodata/OpenDataDetail.html?pn=<x>` returns only an empty shell;
a `WebFetch` of it gives you page chrome, not data. The real catalogue is
`https://geodaten.bayern.de/opengeodata/json/opengeodata_datensaetze.json` — a single JSON with
~121 distribution records across ~35 products. That's the canonical source for any tooling. (One
of our research agents initially reported "14 products" from a `WebFetch` summary of that JSON —
that was a summarization artifact; the real count is ~35. Always parse the JSON yourself.)
Likewise the `…/odd/m/3/pdf/`, `…/html/datenhinweise/`, `…/opengeodata/json/` directories are
**not** browsable (access-denied) — you can't get a file listing; you assemble the PDF list from
links inside the catalogue + the datenhinweise pages.

---

## 6. Most of the official "Datenformatbeschreibung" PDFs are image-only scans

Many `…/odd/m/3/pdf/*.pdf` and `ldbv.bayern.de/mam/...pdf` files are rasterized — text extraction
fails. If you need a machine-readable version of a schema/attribute list, prefer: the AdV
**GeoInfoDok HTML** catalogues (`OK_*.html`), the **GDI-DE codelist registry**
(`registry.gdi-de.org/codelist/de.adv-online.gid/...`), or just open a sample download and inspect
it (`ogrinfo`, or parse the DBF/GML directly).

---

## 7. LoD2 CityGML version: **1.0**, not 2.0  `[CONFLICT — resolved]`

The Bavarian OpenData "Datenhinweise LoD2" PDF (`hinweise_daten_lod2_download.pdf`) states
verbatim *"Das Abgabeformat ist das vom 'Open Geospatial Consortium (ogc)' definierte cityGML 1.0"*,
and the AdV "Datenformatbeschreibung 3D-Gebäudemodell Deutschland (LoD2-DE)" v3.1 (2025-05-22)
says *"CityGML Version 1.0.0, … Encoding Standard 08-007r1"*. One research pass misread it as
"CityGML 2.0" — **wrong; it's 1.0(.0)**. (CityJSON is *not* offered; you'd convert it yourself.)
Note also: it ships **no façade textures** (generic/prototypic models only — for textured 3D, see
the separate `DOM-Mesh` SLPK/I3S product).

---

## 8. "Tatsächliche Nutzung" exists in three different official datasets — don't confuse them

1. **ALKIS "Tatsächliche Nutzung" (TN)** — OpenData product `tatsaechlichenutzung`. Parcel-sharp
   (~1:1000), the cadastral land-use layer. Per-Landkreis Shape + bayernwide GeoPackage.
2. **The "Tatsächliche Nutzung" block *inside* ATKIS Basis-DLM** — the 4xxxx object types
   (Siedlung/Verkehr/Vegetation/Gewässer). Same AAA `AX_*` classes, but topographic accuracy
   (~1:25k, generalized), different data store. Comes with the Basis-DLM download.
3. **"Landnutzung (LN)"** — OpenData product `ln`. A *derived* product (rule-based fusion of ALKIS
   + ATKIS) re-expressing land use as socio-economic *function*; flagged `mappingannahme` where it
   was inferred. Single bayernwide GeoPackage (~5.3 GiB), annual.
None of the three carry buildings or per-crop-per-year detail; for crop codes you'd need the
StMELF/iBALIS Feldstücke data, which is **not** in this portal.

---

## 9. The OpenData TN **Shape** product drops the numeric attribute codes

The AAA model has rich coded attributes on land-use polygons (`AX_Wald.vegetationsmerkmal`
1100=Laubholz/1200=Nadelholz/…, `AX_Funktion_*`, `AX_Zustand_*`). The OpenData **Tatsächliche
Nutzung Shape** (`Nutzung.shp`) flattens all of that into a single text field `bez` — and for
several object types (Wald, Gehölz, Heide, Moor, Sumpf, Fließgewässer, Stehendes Gewässer,
Bahnverkehr, Straßenverkehr) `bez` is **empty** in practice, i.e. the Laub/Nadel split is **not
carried**. If you need the codes, use the chargeable ALKIS-NAS — or note that the **ATKIS
Basis-DLM Shape** *does* carry them (the `veg02_f` Wald layer has a populated `VEG` field with
1100/1200/… codes). `[VERIFIED]` against a real `tn_09161.zip` and a real `bkg_shape_712.zip`.

---

## 10. There's no `vendor/gdal-win64` in the *parent* repo — it's under `openmap_blender_tools/`

CLAUDE.md says "Vendored GDAL lives in `vendor/gdal-win64/bin/`" — that path is relative to the
**submodule** (`openmap_blender_tools/vendor/gdal-win64/bin/`), and it only contains
`gdal_translate.exe`, `gdalbuildvrt.exe`, `gdalinfo.exe`, `gdal.dll` — **no `ogrinfo`**. To
inspect a Shapefile/GML without GDAL, parse the DBF/SHX headers directly (the DBF header at byte
4-7 is the 32-bit LE record count; `(filesize(.shx) - 100) / 8` is the feature count). On this
machine `python3` is shadowed by a Windows Store stub — use `& "C:\ProgramData\anaconda3\python.exe"`.

---

## 11. Things the portal does NOT have (so stop looking)

- **DOP80**, **DGM20 / DGM25 / DGM10 / DGM50** — not OpenData. Only DGM1, DGM5(XYZ), DOM20,
  DOP20 RGB, DOP20 CIR, DOP40 RGB. (Older/coarser resolutions: chargeable via GeodatenOnline, or
  the "Historische DOP" WMS.)
- **Hausnummern / Hauskoordinaten** as a *download* — not OpenData (chargeable via ZSHH; only a
  "WFS Hauskoordinaten" exists). `Hausumringe` (footprint polygons, no attrs) *is* OpenData.
- **The editable ALKIS Flurkarte** — not OpenData; the OpenData proxy is the **ALKIS-Parzellarkarte**
  (WMS/WMTS + polygonal GeoTIFF), and it's **CC BY-ND 4.0** (no redistributing modified versions),
  not CC BY 4.0 like everything else.
- **A standalone "Gewässer" / "Geländekanten / Bruchkanten" / "3D-Punktwolke" / "DGK5" product** —
  no. Water is inside ATKIS/ALKIS-TN/DTK; the LiDAR product is called `laserdaten` (LAZ);
  3D-Strukturlinien are in the (chargeable) 3D-Messdaten, not OpenData.
- **A directory listing anywhere on the portal** — access-denied; fetch files by known name.

---

## 12. `poly2metalink` dataset tokens ≠ product keys

The polygonal-download service `https://geoservices.bayern.de/services/poly2metalink/` uses its
own dataset tokens, which differ from the OpenData product keys: e.g. `dgm5xyz` (not `dgm5`),
`dom20dom` (not `dom20`), `dop20rgb`/`dop20cir` (not `dop20rgb`/`dop20_cir`... actually those match),
`laser`, `relief`, `hl`, `parzellarkarte`. List the available tokens at
`https://geoservices.bayern.de/services/poly2metalink/datasets`. Calling `…/datasets/<token>`
with the wrong name returns `"Invalider Dataset-Name."`. (This is why we couldn't get a metalink
for `einzelbaeume` that way — its token isn't exposed; use the direct `.gpkg` URLs + the index KML
instead.)
