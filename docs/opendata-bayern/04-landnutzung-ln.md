# 04 — Landnutzung (LN) + Landbedeckung (LB)

**Product key:** `ln` · **Page:** https://geodaten.bayern.de/opengeodata/OpenDataDetail.html?pn=ln

## What it is

"`ln`" = **Landnutzung**. A **derived "mapping product"** synthesized by rule-based fusion of
**ALKIS®** + **ATKIS®-Basis-DLM**, conforming to the nationwide AdV application schema
**"Landnutzung (LN)" v1.0.2 (Stand 2022-12-15)** — part of the AAA model, the "GeoBasis-DE
Landnutzung" layer. Publisher LDBV; schema authority AdV. Describes how areas are *socio-economically*
used (residential vs. public vs. commercial vs. industrial vs. supply/disposal vs. storage vs.
mining vs. recreation/sport vs. burial vs. road/rail/air/water transport vs. protective structures
vs. agriculture vs. forestry vs. aquaculture vs. water management vs. "no primary use"), with typed
function/sub-type attributes. Verbatim definition: *"Die Landnutzung (LN) beschreibt, wie Flächen
aktuell genutzt werden und welchen sozioökonomischen Zweck sie erfüllen. … basiert auf Daten des
Liegenschaftskatasters (ALKIS®) und dem Digitalen Landschaftsmodell (ATKIS®-Basis-DLM). Die Daten
sind objektbasiert, attributiert und vektoriell erfasst."*

> **NOT** the InVeKoS/IACS agricultural data (Feldstücke, Schläge, per-crop-per-year, EU-subsidy
> reference parcels, StMELF/LfL). That's a separate product, not keyed `ln`, not on this portal.
> See [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §8 for LN vs. TN vs. ATKIS-TN.

## Object groups → object types (= GeoPackage layers, lowercased `ln_*`)

- **Siedlung:** ln_wohnnutzung [221100], ln_oeffentlicheeinrichtungen [221210],
  ln_kulturundunterhaltung [221220], ln_gewerblichedienstleistungen [221310],
  ln_industrieundverarbeitendesgewerbe [221320], ln_versorgungundentsorgung [221330],
  ln_lagerung [221340], ln_abbau [221350], ln_freiluftundnaherholung [221410],
  ln_freizeitanlage [221420], ln_sportanlage [221430], ln_bestattung [221500]
- **Verkehr und Infrastruktur:** ln_strassenundwegeverkehr [222100], ln_bahnverkehr [222200],
  ln_flugverkehr [222300], ln_schiffsverkehr [222400], ln_schutzanlage
- **Land-, Forst- und Fischereiwirtschaft:** ln_landwirtschaft [223100], ln_forstwirtschaft [223200],
  ln_aquakulturundfischereiwirtschaft
- **Gewässer:** ln_wasserwirtschaft
- **Keine primäre Nutzung:** ln_ohnenutzung

All polygons (REO, flat hierarchy). Abstract superclass `LN_Landnutzung` [220001] derived from
`TA_SurfaceComponent` (the polygons tessellate a surface partition, like ATKIS).

## Attributes

Common (inherited from `LN_Landnutzung`): `datumDerLetztenUeberpruefung` (DLU),
`ergebnisDerUeberpruefung` (EDU; 1000 Fehlerkorrektur / 2000 Bestätigung Ist-Zustand / 3000
Erfassung neues Objekt / 4000 Geometrieänderung), `istWeitereNutzung` (IWN; 1000 "überlagernd" —
secondary uses outside the partition), **`mappingannahme` (MAN; Boolean — `true` = polygon was
inferred by a derivation rule, not unambiguously derived; key quality flag since LN is
auto-generated)**. Plus standard AAA object-id + change-date columns.

Per-type examples: `ln_wohnnutzung.zeitlichkeit` (permanent vs. seasonal/weekend), `.zustand` ·
`ln_flugverkehr.funktion` (5310 Flugverkehrsfläche, 5311 Start-/Landebahn, 5312 Begleitfläche,
5313 Zurollbahn, 5314 Vorfeld, 5320 Betriebsfläche), `.nutzung` (1000 zivil / 2000 militärisch) ·
`ln_schiffsverkehr.funktion` (5410…5424 incl. 5424 Schleuse), `.hafenkategorie` (Container/Öl/
Fischerei/Sport-Yacht/Fähr/Stückgut/Massengut/Hafenbecken) · `ln_schutzanlage.funktion` (5510
Hochwasserschutz, 5520 Lärmschutz Wall/Wand, 5540 Windschutz Hecke/Knick) · `ln_industrie…art`,
`ln_versorgungundentsorgung.primaerenergie`, `ln_forstwirtschaft.art`.

`ln_landwirtschaft` [223100] — the closest LN comes to "crop type" (coarse, ~15 classes,
predominant use at survey time per the "Dominanzprinzip"):
- `bewirtschaftung`: 1010 Ackerland · 1011 Streuobstacker · 1012 Hopfen · 1013 Spargel · 1014 Hanf
  · 1020 Mahd- und Weideland · 1021 Streuobstwiese · 1030 Gartenbauland · 1040 Rebfläche · 1050
  Obst- und Nussplantage · 1060 Kurzumtriebsplantage · 1070 Baumschule · 1080 Weihnachtsbaumkultur
  · 1200 Brachland · 1300 Betriebsfläche Landwirtschaft
- `artDerBetriebsflaeche`: 1000 Tierhaltung · 2000 Pflanzliche Produktion (only when bewirtschaftung=1300)

No field ID, no annual crop attribute, no farmer/owner. Exact GeoPackage column names not in the
PDF — confirm with `ogrinfo -so landnutzung.gpkg <layer>`.

## Format & download

- **GeoPackage** (`landnutzung.gpkg`), **single file, all Bavaria**, ~5.3 GiB. No Shapefile/GML,
  no tiling, no Metalink.
```
Direct download (~5.3 GiB, Last-Modified 2026-03-03, range-capable):
  https://geodaten.bayern.de/odd/m/3/daten/ln/landnutzung.gpkg
Catalogue entry: produkt_produktname == "ln" (abgabe_name "landnuzung_download_einzel" [sic])
```

## Update / license / docs

**Annual** snapshots; portal serves only the current edition (Aktualität 01.01.2026). No archive
of prior years. CC BY 4.0. Implements AdV "Objektartenkatalog Landnutzung (LN)" v1.0.2 (2022-12-15).
LDBV DFB PDF `https://www.ldbv.bayern.de/mam/ldbv/dateien/dfb_landnutzung_nicht_barrierefrei.pdf`
(exported 2024-10-30, 2 pp). AdV OK + Spezifikation + Beispiele + Visualisierung PDFs: see
[08-adv-geoinfodok-specs.md](08-adv-geoinfodok-specs.md). LDBV product page
`https://www.ldbv.bayern.de/produkte/liegenschaftsinformationen/landnutzung.html`. There's also a
PQS Fachschema Landnutzung v1.0 (2023-07-10) and an AdV-Produktspezifikation GeoPackage LN v1.0
(2023-06-13) — URLs in §08.

## Landbedeckung (LB) — the sibling layer

The AdV also defines a **"Landbedeckung (LB)" v1.0.1 (2021-11-12)** application schema — land
*cover* (LB_* object types; codelists `LB_Vegetationsmerkmal_HolzigeVegetation`,
`LB_Vegetationsmerkmal_KrautigeVegetation`, …). NAS schema at
`https://repository.gdi-de.org/schemas/adv/nas-lb/1.0/`. **As of this research it is NOT (yet) an
OpenData product in Bavaria** — only LN is published. Worth watching.

## Use in the pipeline

Like TN/ATKIS land-use but with richer *function* sub-types (useful for "this block is a school /
factory / power station / cemetery / vineyard"). Watch `mappingannahme` — some polygons are
guesses. The 5.3 GiB single GeoPackage is awkward; clip on import.
