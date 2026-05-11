# 03 — ALKIS Tatsächliche Nutzung (TN)

**Product key:** `tatsaechlichenutzung` · **Page:** https://geodaten.bayern.de/opengeodata/OpenDataDetail.html?pn=tatsaechlichenutzung

## What it is

The **land-use layer of ALKIS®** (the cadastre). A **wall-to-wall, gapless, overlap-free** polygon
coverage of how every piece of land is actually used, captured at ~**1:1 000** (parcel-sharp). The
TN polygon boundaries are an independent layer (decoupled from parcel boundaries). Four
Hauptgruppen → ~140 Nutzungsarten. Currency: officially "besser als drei Jahre" bayernweit, much
fresher in settlement areas. (Distinct from the ATKIS Basis-DLM "Tatsächliche Nutzung" block, which
is the same AAA classes at topographic accuracy; and from "Landnutzung (LN)" which is a derived
socio-economic product — see [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §8.)

## Object types (= values of the `nutzart` field) — 24 in Bavaria

- **Siedlung:** Wohnbaufläche, Industrie- und Gewerbefläche, Halde, Bergbaubetrieb, Tagebau/Grube/
  Steinbruch, Fläche gemischter Nutzung, Fläche besonderer funktionaler Prägung, Sport-/Freizeit-/
  Erholungsfläche, Friedhof
- **Verkehr:** Straßenverkehr, Weg, Platz, Bahnverkehr, Flugverkehr, Schiffsverkehr
- **Vegetation:** Landwirtschaft, Wald, Gehölz, Heide, Moor, Sumpf, Unland/Vegetationslose Fläche
- **Gewässer:** Fließgewässer, Hafenbecken, Stehendes Gewässer  *(`AX_Meer` exists in the AAA
  catalogue but Bavaria is landlocked — not present; so 24 types, not 25)*

Sub-classification (`vegetationsmerkmal` / `funktion` / `zustand` codes) — see the AdV codelists
in [05-buildings-and-3d.md](05-buildings-and-3d.md) and [08-adv-geoinfodok-specs.md](08-adv-geoinfodok-specs.md):
e.g. `AX_Wald.vegetationsmerkmal` 1100 Laubholz / 1200 Nadelholz / 1300 Laub-und-Nadelholz /
1310 Laubholz mit Nadelbäumen / 1320 Nadelholz mit Laubbäumen; `AX_Landwirtschaft.vegetationsmerkmal`
1010 Ackerland / 1011 Streuobstacker / 1012 Hopfen / 1013 Spargel / 1014 Hanf / 1020 Grünland /
1021 Streuobstwiese / 1030 Gartenbauland / 1031 Baumschule / 1040 Rebfläche / 1050/1051/1052
Obst-/Nussplantage / 1060 Weihnachtsbaumkultur / 1100 Kurzumtriebsplantage / 1200 Brachland.

> **⚠ Caveat:** the OpenData **Shape** product does NOT expose those numeric codes — it flattens
> `funktion`/`vegetationsmerkmal`/`zustand` into a single text field `bez`, and for several object
> types (Wald, Gehölz, Heide, Moor, Sumpf, Fließgewässer, Stehendes Gewässer, Bahnverkehr,
> Straßenverkehr) `bez` is **empty** in practice — so the Laub/Nadel split is *not* carried in this
> product. (The ATKIS Basis-DLM Shape *does* carry it, in `veg02_f.VEG`. See pitfall #9.) Full
> code-level detail is only in the chargeable ALKIS-NAS.

## Schema — `Nutzung.shp` (polygon only, encoding per `.cpg`) `[VERIFIED on tn_09161.zip]`

Documented attributes: `oid` (Objektidentifikator, ends `…TN`, e.g. `DEBYvAAAAAAGWY2cTN`),
`aktualit` (Date, `YYYY-MM-DDZ`), `nutzart` (the AAA Objektart name, e.g. `Platz`), `bez`
(sub-type as plain text, often empty — e.g. `Fußgängerzone`, `Ackerland`, `Grünland`,
`Umspannstation`, `Spielplatz, Bolzplatz`, `Segelfluggelände`), `name` (Eigenname). The real
downloads also include a `gml_id` C(80) GML-fid string (6 fields total in the DBF, not the 5 the
doc PDF lists). **No area field, no numeric codes** — compute area from geometry. The `.prj`:
`PROJCS["ETRS_1989_UTM_Zone_32N", …, Scale_Factor 0.9996, False_Easting 500000, UNIT Meter]`.
(The bayernwide GeoPackage variant may carry more — not verified; layer `Nutzung_kreis`.)

## Format & download

- **Shape, one ZIP per Landkreis / kreisfreie Stadt** (96 units = 71 Landkreise + 25 kreisfreie
  Städte; no grid tiling, no Gemeinde split). Inside every ZIP the layer is always literally
  `Nutzung.*`, clipped to the Kreis boundary. Per-ZIP ≈ 2–25 MB.
- **GeoPackage, one file bayernwide** (`Nutzung_kreis.gpkg`, ~5.3 GiB).
- **WMS** raster service.
- No NAS/GML/CSV/KML as OpenData (a `kreis.kml` exists only as the download selection index). Full
  ALKIS (parcels, buildings, owners) is **chargeable** via GeodatenOnline, not OpenData.

```
Per-Landkreis Shape ZIP (CDN):  https://download1.bayernwolke.de/a/tn/lkr/tn_<KRS>.zip
  <KRS> = 5-digit Kreisschlüssel; "09…" = Bavaria. e.g. tn_09161 (Ingolstadt Stadt),
  tn_09162 (München Stadt), tn_09184 (Lkr München), tn_09180 (Garmisch).
Selection index (KML, 1 Placemark + href per Kreis):
  https://geodaten.bayern.de/odd/m/4/tn/lkr/kml/kreis.kml?service=kml
Bayernwide GeoPackage (~5.3 GiB, range-capable):
  https://geodaten.bayern.de/odd/m/3/daten/tn/Nutzung_kreis.gpkg
WMS:  https://geoservices.bayern.de/od/wms/alkis/v1/tn?
```
No `.meta4` for TN. Bulk recipe: parse `kreis.kml` → 96 `download1.bayernwolke.de/a/tn/lkr/tn_*.zip`
hrefs, or just grab the one GeoPackage.

## Update / docs / license

Shape & GeoPackage regenerated **monthly**; WMS daily. CC BY 4.0. Conforms to AdV "ALKIS-WFS und
Ausgabeformate (Shape, CSV)" v2.0.0. Datenformatbeschreibung PDF:
`https://geodaten.bayern.de/odd/m/3/pdf/hinweise_daten_tn_download.pdf`. The chargeable full
**ALKIS-Shape Komplettdaten** (GeodatenOnline) documents the same `Nutzung.shp` plus
`Flurstueck.shp`, `GebaeudeBauwerk.shp`, `KatasterBezirk.shp`, `VerwaltungsEinheit.shp` — DFB
`https://geodatenonline.bayern.de/geodatenonline/gdoresources/pdfs/Datenformatbeschreibung_ALKIS-Shape.pdf`.
LDBV product page `https://www.ldbv.bayern.de/produkte/liegenschaftsinformationen/tat_nutzung.html`.

## Use in the pipeline

Higher-precision (1:1000) land-use polygons than ATKIS Basis-DLM — good for material/scatter masks
at city scale. Per-Landkreis ZIPs are tiny and convenient. The empty-`bez` caveat limits its
forest-type usefulness; for Laub/Nadel use ATKIS Basis-DLM `veg02_f.VEG` instead.
