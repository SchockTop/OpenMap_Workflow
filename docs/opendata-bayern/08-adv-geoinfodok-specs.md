# 08 — AdV / GeoInfoDok specification documents

The authoritative documents behind the German official-geodata vector formats. Compiled May 2026.
Current AdV reference: **GeoInfoDok / AAA-Anwendungsschema 7.1.2** (AdV-Referenzversion 7.1,
effective 2024-01-01; catalogue documents carry "Stand: 01.11.2022, Version 7.1.2"). NAS XSDs at
`https://repository.gdi-de.org/schemas/adv/nas/7.1/` (dated 2024-05-27).

> **`adv-online.de` URL note:** the site is a CMS; PDF links carry opaque `?imgUid=…&uBasVariant=11111111-1111-1111-1111-111111111111`
> query strings that are part of the working URL. If a direct link 404s, browse from the page given
> alongside. Some `imgUid` values below may be mis-transcribed by the scraper — `[UNVERIFIED]` where
> noted; treat the *page* + *filename* as the reliable part.
>
> Many of the official Bayern `…/odd/m/3/pdf/*.pdf` Datenformatbeschreibungen are **image-only
> scans** — for machine-readable schema/attribute data prefer the AdV **HTML** catalogues (`OK_*.html`)
> or the **GDI-DE codelist registry** (`registry.gdi-de.org/codelist/de.adv-online.gid/...`).

---

## 1. GeoInfoDok 7.1.2 — the AAA-Anwendungsschema

Page: `https://www.adv-online.de/GeoInfoDok/Aktuelle-Anwendungsschemata/AAA-Anwendungsschema-7.1.2-Referenz-7.1/`

| Document | Stand | Covers |
|---|---|---|
| **OK AAA-Anwendungsschema** (PDF ~8.6 MB / HTML) | 7.1.2 / 2022-11-01 | the master object catalogue (AAA-Basisschema + AAA-Fachschema = AFIS/ALKIS/ATKIS) |
| OK AAA-Basisschema (PDF ~1.0 MB) | 7.1.2 | abstract base schema (AA_*, AG_Objekt, geometry/topology, versioning) |
| OK AAA-Fachschema (PDF ~7.2 MB) | 7.1.2 | the domain schema = all AFIS/ALKIS/ATKIS object types |
| **OK Basis-DLM = "ATKIS-Objektartenkatalog Basis-DLM"** (PDF ~3.0 MB / HTML ~1.4 MB) | 7.1.2 | the ATKIS-OK — all Basis-DLM object types incl. the (new in 7.1.x) Objektartenbereich Gebäude |
| **Erläuterungen zum ATKIS Basis-DLM** (PDF ~8.3 MB) | 7.1.2 / 2026-02-25 | explanatory text: history, modelling principles, ALKIS harmonisation, application rules |
| **OK DLKM = "ALKIS-Objektartenkatalog"** (PDF ~5.2 MB / HTML ~3.0 MB) | 7.1.2 | the ALKIS-OK — Flurstücke, Lage, Punkte, **Gebäude (AX_Gebaeude)**, Tatsächliche Nutzung, Bauwerke, Festlegungen, Nutzerprofile |
| "Erläuterungen zum ALKIS" | 7.1.0, "tagesaktuell" | published as a **living Wiki**, not a versioned PDF: `https://services.interactive-instruments.de/qsm/projects/alkis/wiki/Wiki` |
| OK DLM50 (PDF ~2.2 MB) + Erläuterungen DLM50 (PDF ~6.3 MB, 2026-02-25) | 7.1.2 | ATKIS DLM50 (1:50 000 generalised) |
| OK DLM250 (PDF ~1.4 MB) / OK DLM1000 (PDF ~1.1 MB) | 7.1.2 | ATKIS DLM250 / DLM1000 |
| OK DFGM (PDF ~1.2 MB) + AFIS-Erläuterungen (PDF ~121 KB, 2019-06-01) | 7.1.2 / 7.1.0 | AFIS — Festpunktinformation |
| UML data models | 2024-05-27 | Enterprise Architect EAP (~9.9 MB) + QEA (~11.3 MB) on the GeoInfoDok page |

**3D buildings in the AAA model** (in the AAA-Anwendungsschema catalogue; HTML anchor ≈ `_P1083`):
Objektartenbereich **100000 "Gebäude, Bauwerke, Einrichtungen, Anlagen und Gestaltung 3D"** →
**101000 "Angaben zum Gebäude 3D"** (Modellarten **LoD1 / LoD2 / LoD3**): `AX_Bauteil3D` 101001 ·
`AX_Abschlussflaeche3D` 101002 (= CityGML ClosureSurface) · `AX_Bodenflaeche3D` 101003 (= GroundSurface)
· `AX_Dachflaeche3D` 101004 (= RoofSurface) · `AX_Wandflaeche3D` 101005 (= WallSurface) ·
`AX_GebaeudeInstallation3D` 101011 · **102000 "Bauwerke, Einrichtungen, Anlagen 3D"**: `AX_Bauwerk3D`
102001 · **103000 "Gestaltung 3D"**: material/texture definitions for 3D rendering. This is the
AAA-internal model that the **LoD2 CityGML OpenData product exports** (semantic surfaces map 1:1 to
`bldg:RoofSurface`/`WallSurface`/`GroundSurface`/`ClosureSurface`; `AX_Bauteil3D` ↔ `bldg:BuildingPart`).
LoD2 (= OpenData) and LoD3 (= richer, not OpenData) are levels within the same model; "Gestaltung 3D"
means the *schema* supports textured 3D — Bavaria's OpenData LoD2 ships untextured prototypic models.
See [05-buildings-and-3d.md](05-buildings-and-3d.md) §0. HTML catalogue URL:
`https://www.adv-online.de/GeoInfoDok/Aktuelle-Anwendungsschemata/AAA-Anwendungsschema-7.1.2-Referenz-7.1/OK_AAA-Anwendungsschema_7125415.html`

Legacy: GeoInfoDok 6.0.1 docs still hosted (e.g. "Erläuterungen zum ATKIS Basis-DLM 6.0.1" via BKG
mirror `https://sg.geodatenzentrum.de/web_public/gdz/dokumentation/deu/Erlaeuterungen%20zum%20ATKIS%20Basis-DLM%206_0_1.pdf`).
Version note: the *reference version* is "7.1"; the *schema version* "7.1.2" (Stand 2022-11-01) is
operational since 2024. There was a "7.1 rc.1" (Stand 2018-07-31); no public "7.1.1".

**Direct PDF URLs (relative to the GeoInfoDok 7.1.2 page above):**
- `OK_AAA-Anwendungsschema_712.pdf?imgUid=8bdf7a5be-17ae-4819-393b-216067bef8a0&uBasVariant=11111111-1111-1111-1111-111111111111` `[UNVERIFIED uid]`
- `OK_AAA-Anwendungsschema_7125415.html?imgUid=78f7a5be-17ae-4819-393b-216067bef8a0&uBasVariant=…`
- `OK_AAA-Basisschema_7120975.pdf?imgUid=bdf7a5be-17ae-4819-393b-216067bef8a0&uBasVariant=…`
- `OK_AAA-Fachschema_7124316.pdf?imgUid=4ff7a5be-17ae-4819-393b-216067bef8a0&uBasVariant=…`
- `OK_Basis-DLM_7128207.pdf?imgUid=5ef70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…`
- `OK_Basis-DLM_712b3ad.html?imgUid=eef70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…`
- `AAA-AS7.1.2_Basis-DLM_Erlaeuterungen_260225_PDF.pdf?imgUid=cf4200f2-be1d-e991-c03f-f836c845193e&uBasVariant=…`
- `OK_DLKM_712bf62.pdf?imgUid=28f70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…`
- `OK_DLKM_71227ab.html?imgUid=f8f70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…`
- `OK_DLM50_7125f7a.pdf?imgUid=84f70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…` ; `AAA-AS7.1.2_DLM50_Erlaeuterungen_260225_PDF.pdf?imgUid=be4200f2-be1d-e991-c03f-f836c845193e&uBasVariant=…`
- `OK_DLM250_7125ef8.pdf?imgUid=f2f70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…` ; `OK_DLM1000_712824a.pdf?imgUid=61f70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…`
- `OK_DFGM_7120441.pdf?imgUid=4cf70989-a7b6-0581-9393-b216067bef8a&uBasVariant=…` ; `20190601-Erlaeuterungen%20zu%20AFIS%207.1.0456b.pdf?imgUid=61e40780-c5f2-bc61-f27f-31c403b36c4c&uBasVariant=…`
- BKG nationwide mirror of the ATKIS-OK Basis-DLM 7.1.2: `https://sg.geodatenzentrum.de/web_public/gdz/dokumentation/deu/OK_Basis-DLM_712.pdf`

---

## 2. AdV-Produktspezifikationen (AdV-PS) & Produkt-/Qualitätsstandards (PQS)

### Geotopography — page `https://www.adv-online.de/AdV-Produkte/Standards-und-Produktblaetter/Standards-der-Geotopographie/`

| Document | Version | Filename + query (relative to that page) |
|---|---|---|
| **AdV-PS ATKIS-Basis-DLM-Shape** | **v2.2** (v2.1 archived) | `AdV-PS_ATKIS-Basis-DLM_Shape_v2.2.pdf?imgUid=652238a1-a694-8918-446d-5330fc20eb4b&uBasVariant=…` (v2.1: `AdV-PS_ATKIS-Basis-DLM_Shape_v2.168aa.pdf?imgUid=652238a1-…`) — *this is the spec the user has locally; defines the SIE/VER/VEG/GEW/GEB/REL/HDU/FDV/HHO/PRA layer structure* |
| AdV-PS ATKIS-DLM-WFS | — | `adv-ps%20atkis-dlm-wfsce57.pdf?imgUid=90320307-0b71-ee71-7657-80b6a757628a&uBasVariant=…` |
| AdV-PS ATKIS-DOP20-WMS | — | `Produktspezifikation%20ATKIS-DOP20-WMS6ea3.pdf?imgUid=c5419114-249e-4711-1fea-f5203b36c4c2&uBasVariant=…` |
| AdV-PS basemap.de Web Raster WMTS / WMS / Schummerung WMS / Schummerung WMTS | v1.0 | `AdV-PS%20WMTS%20Web_Raster_1.01afc.pdf?…` · `AdV-PS%20WMS%20Web_Raster_1.0749a.pdf?…` · `AdV-PS%20basemap.de%20Web_Raster_Schummerung_WMS%201.0f831.pdf?…` · `AdV-Produktspezifikation_WMTS-basemap.de%20Web_Raster_Schummerung_1.0bc3d.pdf?…` |
| **AdV CityGML-Schemadateien** (ZIP) | 2016-11-30 | `2016_11_30_AdV-CityGML-Schemadateiene4d7.zip?imgUid=25322501-81d5-b81c-0172-bc609c18f093&uBasVariant=…` — the LoD1/LoD2 CityGML application schema (also at `https://repository.gdi-de.org/schemas/adv/citygml/`, codelist `…/Codelisten/BuildingFunctionTypeAdV.xml`) |
| **PQS ATKIS Basis-DLM** | v1.1.0 | `PQS%20ATKIS%20Basis-DLM%20Version%201.1.07b5f.pdf?…` |
| PQS DOP / PQS DGM (909R9) / PQS DTK / PQS 3D-Gebäudemodelle (1071R12, ZIP) / PQS DOM (1605R2) / PQS bDOM / PQS 3D-Messdaten (1593R3) | — | various `…?imgUid=…` under the same page |
| PQS Produktgruppe basemap.de / Web Raster v1.0 / Web Vektor v1.0.1 (1704R1) / Präsentationsausgaben | — | various |
| Produktblatt Basis-DLM | — | `https://www.adv-online.de/AdV-Produkte/Standards-und-Produktblaetter/Produktblaetter/Produktblatt%20Basis-DLM.pdf` |
| AdV-GPKG-Profil (generic GeoPackage profile underpinning the AAA GeoPackage product specs) | — | documented in the GeoSN Redmine AdV-public wiki: `https://www.landesvermessung.sachsen.de/redmine2/projects/adv-public/wiki/Geopackage-profil`. **No standalone "AdV-PS ATKIS-Basis-DLM-GeoPackage" PDF found on adv-online.de** `[UNVERIFIED whether one exists]` — Bavaria's "Basis-DLM plus" GeoPackage is documented by the Bavaria-specific PDFs (`basisdlm_plus_datenformatbeschreibung.pdf`, `basisdlm_plus_datenmodell.pdf`). |

### Liegenschaftskataster — page `https://www.adv-online.de/AdV-Produkte/Standards-und-Produktblaetter/Standards-des-Liegenschaftskatasters/`

| Document | Version / date | Filename + query |
|---|---|---|
| **AdV-PS ALKIS-WFS und Ausgabeformate (GeoPackage, Shape, CSV)** | **v2.1.0 / 2025-03-13** (v2.1 adds GeoPackage) | `LK2025-04_Anlage_2025-03-13_AdV-Alkis-WFS-Produktspezifikation_Version_2.1_Beschlussfassung5ac2.pdf?imgUid=8ca602a2-618f-7991-c03f-f836c845193e&uBasVariant=…` |
| AdV-PS ALKIS-WFS und Ausgabeformate (Shape, CSV) | v2.0.1 / 2023-02-23 | `LK2023-01_AdV-Alkis-WFS-Produktspezifikation_Version_2.0.1_Stand_2023-02-23c639.pdf?imgUid=81370c46-be11-fb81-cdd7-f85109c6b2f5&uBasVariant=…` |
| AdV-PS ALKIS-WFS und Ausgabeformate (Shape, CSV) | v2.0.0 / 2019-03-08 | `2019-03-08_AdV-Alkis-WFS-Produktspezifikation_finale1fb.pdf?imgUid=f49502a0-36fa-6b61-c2d2-1bf43b36c4c2&uBasVariant=…` *(this is the spec the Bayern OpenData TN Shape conforms to)* |
| AdV-ALKIS-Shape-Produktspezifikation | v1.0.1 / 2016-04-14 | `Anlage%205.1%20c_AdV-ALKIS-Shape-Produktspezifikationb479.pdf?nocache=true&time=1466093041225&imgUid=194606af-6d37-4551-97b5-9b77072e13d6&uBasVariant=…&vcVerNr=00000000000000000000.00000000000000000001` |
| AdV-ALKIS-WFS-Produktspezifikation v1.0 (2016-04-14) / AdV-ALKIS-WMS-Produktspezifikation v1.0 (2011-03-31) | | `Anlage%205.1%20b_AdV-ALKIS-WFS-Produktspezifikatione87d.pdf?…` · `Anlage%203.1%20b%20Produktspezifikation%20AdV-ALKIS-WMS29e4.pdf?…` |
| PQS Fachschema Landnutzung v1.0 (2023-07-10) / Produktblatt Landnutzung (2023-01-24) | | `LK2023-06_PQS_Landnutzung_v1.0_Anlageda85.pdf?…` · `LK2023-06_PQS_Landnutzung_v1.0_Anlage_Produktblattfcae.pdf?…` |
| **AdV-PS GeoPackage Landnutzung (LN)** v1.0 / 2023-06-13 | | `LK2023-10_AdV-Produktspezifikation_Geopackage_LN_v1.0_Anlagedda4.pdf?…` |

**Hausumringe / Hauskoordinaten / Verwaltungsgebiete** — documented mainly by the **ZSHH** (at
LGL-BW) and the **BKG** rather than as numbered AdV-PS PDFs: ZSHH product sheet (docplayer copy)
`https://docplayer.org/35642570-…`; BKG VG250/VG1000 docs `https://sgx.geodatenzentrum.de/web_public/gdz/dokumentation/deu/vg250.pdf`,
`…/anlagen_vg.pdf`; Bayern Hauskoordinaten DFB `https://www.ldbv.bayern.de/mam/ldbv/dateien/hauskoordinaten-by_datenformatbeschreibung.pdf`,
HK-DE DFB v5.2 `https://www.ldbv.bayern.de/mam/ldbv/dateien/datenformatbeschreibung_hk-de_v_5.2.pdf`.
**3D buildings:** AdV LoD2-DE DFB v3.1 `https://www.ldbv.bayern.de/mam/ldbv/dateien/datenformatbeschreibung_lod-de_v_3.1.pdf`;
AdV LoD1-DE DFB `https://sg.geodatenzentrum.de/web_public/gdz/dokumentation/deu/lod1-de_datenformatbeschreibung.pdf`.

---

## 3. Landnutzung (LN) 1.0.2 & Landbedeckung (LB) 1.0.1 application schemas

**Landnutzung 1.0.2** — page `https://www.adv-online.de/GeoInfoDok/Aktuelle-Anwendungsschemata/Landnutzung-1.0.2/`,
NAS XSD `https://repository.gdi-de.org/schemas/adv/nas-ln/1.0/`. PDFs:
- OK Anwendungsschema Landnutzung 1.0.2 (PDF ~697 KB / HTML ~246 KB, 2022-12-15): `OK_Anwendungsschema_Landnutzung_1028e05.pdf?imgUid=0f12989a-7b60-5819-393b-216067bef8a0&uBasVariant=…` ; `OK_Anwendungsschema_Landnutzung_1022fe7.html?imgUid=be12989a-…` ; diff vs 1.0.1 `OK_Anwendungsschema_Landnutzung_102-difffd43.html?imgUid=5f12989a-…`
- Erläuterungen/Spezifikation LN (PDF ~2 MB, 2024-03-15): `20210312_Spezifikation%20LN_%c3%bcberarbeitung_v1e8d7.pdf?imgUid=ca64006b-171d-e814-0347-2710afb90a9f&uBasVariant=…`
- Beispiele LN (PDF ~1 MB, 2021-03-11): `Information_Beispiele_LN_Daten027f.pdf?imgUid=07433fbd-a20a-771d-478d-9d43b36c4c2e&uBasVariant=…`
- Visualisierung LN (PDF ~2 MB, 2024-04-15): `Visualisierung%20des%20Anwendungsschema%20Landnutzung_iwneb07.pdf?imgUid=aa6602a2-618f-7991-c03f-f836c845193e&uBasVariant=…`

**Landbedeckung 1.0.1** — page `https://www.adv-online.de/GeoInfoDok/Aktuelle-Anwendungsschemata/Landbedeckung-1.0.1/`,
NAS XSD `https://repository.gdi-de.org/schemas/adv/nas-lb/1.0/`. PDFs:
- OK Landbedeckung 1.0.1 (PDF ~300 KB / HTML ~85 KB, 2021-11-12): `OK%20Anwendungsschema%20Landbedeckung%201.0.13f3a.pdf?imgUid=25130147-1420-4e71-0832-12914a39df1f&uBasVariant=…&isDownload=true` ; `OK%20Anwendungsschema%20Landbedeckung%201.0.1c5f0.html?imgUid=0814908b-…`
- Erläuterungen Landbedeckung (PDF ~6 MB, 2024-03-15): `Erl%c3%a4uterungen%20zum%20Anwendungsschema%20Landbedeckung%20LB_20240307_finaleb51.pdf?imgUid=f964006b-171d-e814-0347-2710afb90a9f&uBasVariant=…`
- Beispiele LB (PDF ~12 MB, 2021-03-11): `Information_Beispiele_LB_Datend94b.pdf?imgUid=01213fbd-a20a-771d-478d-9d43b36c4c2e&uBasVariant=…`

(Landnutzung is published as a Bavarian OpenData product — see [04-landnutzung-ln.md](04-landnutzung-ln.md);
Landbedeckung is **not** OpenData in Bavaria yet.)

---

## 4. GDI-DE / AdV codelist registry — `https://registry.gdi-de.org/codelist/de.adv-online.gid`

The authoritative external codelist registry for the AAA model. URL pattern:
`https://registry.gdi-de.org/codelist/de.adv-online.gid/<Name>`. Key building/vegetation/Nutzungsart
codelists (values for the building ones are in [05-buildings-and-3d.md](05-buildings-and-3d.md) §C;
vegetation ones in [03-alkis-tatsaechliche-nutzung.md](03-alkis-tatsaechliche-nutzung.md)):

- `AX_Gebaeudefunktion`, `AX_Weitere_Gebaeudefunktion`, `AX_Dachform`, `AX_Bauweise_Gebaeude`,
  `AX_Zustand_Gebaeude`, `AX_Dachgeschossausbau_Gebaeude` `[UNVERIFIED name]`,
  `AX_LageZurErdoberflaeche_Gebaeude`, `AX_Bauart_Bauteil`, `AX_Nutzung` (1000 Zivil / 1100 Privat /
  1200 öffentlich / 1300 religiös / 2000 militärisch)
- `AX_Vegetationsmerkmal_Wald`, `AX_Vegetationsmerkmal_Landwirtschaft`, `AX_Vegetationsmerkmal_Gehoelz`,
  `AX_Funktion_Vegetationsmerkmal`
- `AX_Funktion_<Nutzungsart>` — one per land-use object type: `AX_Funktion_Wohnbauflaeche`,
  `AX_Funktion_IndustrieUndGewerbeflaeche`, `AX_Funktion_SportFreizeitUndErholungsflaeche`,
  `AX_Funktion_Friedhof`, `AX_Funktion_Siedlungsflaeche`, `AX_Funktion_FlaecheBesondererFunktionalerPraegung`,
  `AX_Funktion_FlaecheGemischterNutzung`, `AX_Funktion_Strasse`/`Strassenachse`/`Fahrbahnachse`,
  `AX_Funktion_Weg`/`Wegachse`, `AX_Funktion_Bahnverkehr`, `AX_Funktion_Flugverkehr`,
  `AX_Funktion_Schiffsverkehr`, `AX_Funktion_Hafenbecken`, `AX_Funktion_Fliessgewaesser`/`Gewaesserachse`/
  `StehendesGewaesser`/`Meer`/`UntergeordnetesGewaesser`, `AX_Funktion_Polder`, `AX_Funktion_Gehoelz`,
  `AX_Funktion_UnlandVegetationsloseFlaeche`, `AX_Funktion_TagebauGrubeSteinbruch`, `AX_Funktion_Bergbaubetrieb`,
  `AX_Funktion_Platz`, `AX_Funktion_DammWallDeich`, `AX_Funktion_Einschnitt`, `AX_Funktion_Bauwerk`,
  `AX_Funktion_GebaeudeInstallation3D`, `AX_Funktion_SchutzgebietNachWasserrecht`, plus AFIS
  `AX_Funktion_Lagefestpunkt`/`Schwerefestpunkt`/`Referenzstationspunkt`
- `AX_Zustand` and per-object-type `AX_Zustand_<X>` (Strasse, Bahnverkehr, Wald, Gebietsgrenze, Turm,
  Halde, IndustrieUndGewerbeflaeche, FlaecheBesondererFunktionalerPraegung, Friedhof, Flugverkehr,
  Schiffsverkehr, Schleuse, Kanal, StehendesGewaesser, Gewaesserachse, Vegetationsmerkmal, Hoehleneingang,
  BoeschungKliff, NaturUmweltOderBodenschutzrecht, Wohnbauflaeche, SportFreizeitUndErholungsflaeche,
  TagebauGrubeSteinbruch, EinrichtungenFuerDenSchiffsverkehr, Flugverkehrsanlage, Bahnverkehrsanlage,
  Bergbaubetrieb, …) and `AX_Zustandsstufe`
- LN/LB: `LN_Art_Forstwirtschaft`, `LN_Art_IndustrieUndVerarbeitendesGewerbe`, `LN_Funktion_Bahnverkehr`/
  `Flugverkehr`/`Schiffsverkehr`/`StrassenUndWegeverkehr`, `LN_Zustand_*`, `LN_Bewirtschaftung_Landwirtschaft`,
  `LN_Hafenkategorie_*`, `LB_Vegetationsmerkmal_HolzigeVegetation`, `LB_Vegetationsmerkmal_KrautigeVegetation`

---

## 5. State Basis-DLM Landesprofile (for the "do buildings appear?" question)

Each Land publishes its own 7.1.2 Landesprofil (which object types it actually maintains). Useful
when checking whether *that state's* ATKIS Basis-DLM contains buildings (Bavaria's does not — see
[02-atkis-basis-dlm.md](02-atkis-basis-dlm.md)):
- Bayern: the OpenData product itself (`atkis_basis_dlm`); WFS GetCapabilities `https://geoservices.bayern.de/wfs/v1/ogc_atkis_basisdlm.cgi?service=WFS&request=GetCapabilities&version=2.0.0` (80 feature types, **no** `AX_Gebaeude`) `[VERIFIED]`; WFS landing `https://geodatenonline.bayern.de/geodatenonline/seiten/wfs_atkis`
- BW: `https://www.lgl-bw.de/export/sites/lgl/Produkte/Galerien/Dokumente/OK_Basis-DLM_712.pdf`
- BB: `https://geobasis-bb.de/sixcms/media.php/9/2025-08-25%20ATKIS-OK_Basis-DLM_BB_7.1.2-2.pdf`
- TH: `https://geoportal.geoportal-th.de/dlm/Katalog_Basis-DLM_TH_712.pdf`
- SN: `https://www.landesvermessung.sachsen.de/service/BasisDLM_SN.pdf` (Stand 2023-07-17)
- HE: `https://hvbg.hessen.de/sites/hvbg.hessen.de/files/2022-09/objektartenkatalog_basis-dlm_hessen.pdf`
- ST: `https://www.lvermgeo.sachsen-anhalt.de/de/datei/download/id/166811,501/lsa_profil_atkis_basis_dlm_7.1.2.pdf`
- BKG nationwide extract: `https://sg.geodatenzentrum.de/web_public/gdz/dokumentation/deu/OK_Basis-DLM_712.pdf`

---

## 6. Other AAA application schemas (on the "Aktuelle Anwendungsschemata" overview)

`https://www.adv-online.de/GeoInfoDok/Aktuelle-Anwendungsschemata/` also lists: AAA-Ausgabekatalog
v2.0.0, Geometrische Verbesserung v1.0.0, Bodenrichtwerte v3.0.1 (and 2.1.0), Geographische
Informationen v1.0.0. The AAA-Revision ticket system (version history): `https://services.interactive-instruments.de/qsm/projects/aaa-revision`.
