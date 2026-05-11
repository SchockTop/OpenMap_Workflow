# 05 — Buildings & 3D

Where building geometry and attributes live in Bavarian OpenData, and the codelists you'd use to
drive procedural materials/windows/roofs. **Bottom line:** for *3D building geometry* there is
exactly one OpenData source — **LoD2 CityGML**. For *2D footprints without attributes* there's
**Hausumringe**. For *rich cadastral building attributes* you need **full ALKIS**, which is
**chargeable** (the OpenData proxy `parzellarkarte` is raster only and CC BY-ND). **ATKIS Basis-DLM
does NOT contain Bavarian buildings** (it defines the schema slot but Bavaria leaves it empty — see
[02-atkis-basis-dlm.md](02-atkis-basis-dlm.md) §Buildings and [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §1).

---

## 0. How the AAA model represents 3D buildings (so the LoD2 product makes sense)

GeoInfoDok 7.1.2 has a native 3D building area — **Objektartenbereich 100000 "Gebäude, Bauwerke,
Einrichtungen, Anlagen und Gestaltung 3D"** (in the AAA-Anwendungsschema HTML catalogue, around
anchor `_P1083`):
- **101000 "Angaben zum Gebäude 3D"** — Modellarten **LoD1 / LoD2 / LoD3** (*not* DLKM/Basis-DLM):
  `AX_Bauteil3D` (101001 — BuildingPart) · `AX_Abschlussflaeche3D` (101002 = CityGML ClosureSurface)
  · `AX_Bodenflaeche3D` (101003 = GroundSurface) · `AX_Dachflaeche3D` (101004 = RoofSurface) ·
  `AX_Wandflaeche3D` (101005 = WallSurface) · `AX_GebaeudeInstallation3D` (101011). (The 3D body
  hangs off the 2D `AX_Gebaeude` 31001 via the LoD geometry; the `*3D` flaeche objects are its
  semantic surfaces.)
- **102000 "Bauwerke, Einrichtungen, Anlagen 3D"** — `AX_Bauwerk3D` (102001).
- **103000 "Gestaltung 3D"** — material / texture definitions for 3D rendering.

So **3D buildings are a first-class part of the official data model**, and the Bavarian LoD2
OpenData product (§A) is just the **CityGML export** of these AAA `*3D` objects — `bldg:RoofSurface`
↔ `AX_Dachflaeche3D`, `bldg:WallSurface` ↔ `AX_Wandflaeche3D`, `bldg:GroundSurface` ↔
`AX_Bodenflaeche3D`, `bldg:ClosureSurface` ↔ `AX_Abschlussflaeche3D`, `bldg:BuildingPart` ↔
`AX_Bauteil3D`. LoD2 (= OpenData) and LoD3 (= richer, not OpenData) are levels within the same
model. **Caveat:** "Gestaltung 3D" means the *schema supports* textured 3D — but Bavaria's OpenData
LoD2 ships generic/prototypic models **without** façade textures (you'd need LoD3 / a textured
product; the closest OpenData thing is the photo-textured `DOM-Mesh` SLPK in §E, which is a surface
mesh, not per-building bodies). Same "schema allows it ≠ Bavaria delivers it" pattern as the ATKIS
buildings situation ([99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §1). The 2D
`AX_Gebaeude` (31001, area 30000) lists `Modellarten: DLKM Basis-DLM DLM50 DLM250 DLM1000` —
confirming the Basis-DLM membership that Bavaria nonetheless doesn't populate.

## A. 3D-Gebäudemodelle LoD2 — the OpenData CityGML product

**Product key:** `lod2` · **Page:** https://geodaten.bayern.de/opengeodata/OpenDataDetail.html?pn=lod2
(`&active=MASSENDOWNLOAD` for the bulk tab) · LDBV: `https://www.ldbv.bayern.de/produkte/liegenschaftsinformationen/gebaeudemodell.html`
(LoD2-BY) and `https://www.ldbv.bayern.de/vermessung/zshh/lod2-de.html` (the federal aggregate).

**What it is:** 3D building/structure models with standardized roof shapes + descriptive attributes.
Modelling basis (verbatim): footprints from **ALKIS®** (fallback ATKIS), roofs from **airborne
laser scanning / ALKIS®-3D-Gebäudeeinmessung / image-based DOM**. Coverage: all Bavaria, **~9.8
million objects** (per the 2023 download-hints PDF — likely higher now). Rolling, municipality-wise
updates; the OpenData mirror is refreshed **weekly** (the federal LoD2-DE aggregate is annual; the
per-building format is identical). Ridge/eave height accuracy normally 20–30 cm; complex-roof
generalization can deviate >1 m in individual cases. **Underground buildings are not included.**

**Format: CityGML 1.0(.0)** (OGC 08-007r1), AdV-CityGML profile. `[VERIFIED]` from both the
Bavarian download-hints PDF (*"das vom 'Open Geospatial Consortium (ogc)' definierte cityGML 1.0"*)
and the AdV "Datenformatbeschreibung 3D-Gebäudemodell Deutschland (LoD2-DE)" v3.1 (2025-05-22:
*"CityGML Version 1.0.0 … Encoding Standard 08-007r1"*). **Not 2.0/3.0, not CityJSON.** No
`app:Appearance` / façade textures — generic/prototypic models only (for textured 3D see DOM-Mesh,
§D). The chargeable GeodatenOnline channel also offers KML/KMZ, 3D-Shape, DXF, 3DS, 3D-PDF; for
OpenData you'd convert the CityGML yourself (FZKVIEWER → DXF, or `citygml-tools`/`3dcitydb`).
AdV CityGML schemas: `https://repository.gdi-de.org/schemas/adv/citygml/`.

### Per-building attributes (AdV DFB v3.1, which Bavaria follows)

One CityGML `CityModel` per file (model name like `LoD2_32_<E_km>_<N_km>_2_BY`, one `gml:Envelope`).
Per `bldg:Building` / `bldg:BuildingPart`:

| element | meaning |
|---|---|
| `gml:id` | OID, form `"DE" + <2-letter Land> + …` → for Bavaria **`DEBY…`** |
| `core:externalReference` | → the 2D building: `informationSystem` = `http://repository.gdi-de.org/schemas/adv/citygml/fdv/art.htm#_9100`, `externalObject/name` = the **ALKIS object ID / Gebäudekennzeichen** (or ATKIS OID) |
| `core:creationDate` | production date `YYYY-MM-DD` (when imported into the Land's DB — not the object's currency) |
| `bldg:function` | **value = `<objektart-Kennung>_<GFK/BWF code>`**, e.g. `31001_1121` (only the first value if several). Codelist: `https://repository.gdi-de.org/schemas/adv/citygml/Codelisten/BuildingFunctionTypeAdV.xml`. (`bldg:class` / `bldg:usage` not populated.) Source object groups: AX_Gebaeude, AX_Turm, AX_BauwerkOderAnlageFuerIndustrieUndGewerbe, AX_VorratsbehaelterSpeicherbauwerk, AX_BauwerkOderAnlageFuerSportFreizeitUndErholung, AX_SonstigesBauwerk…, AX_HistorischesBauwerk…, AX_BauwerkImVerkehrsbereich |
| `bldg:measuredHeight` | highest − lowest reference point above NN, m, 3 decimals, `uom="urn:adv:uom:m"` |
| `bldg:roofType` | generalized roof form, GeoInfoDok enum (= the `AX_Dachform` codes, see §C). Special value **`9999`** = LoD2 building auto-derived from a LoD1 flat-roof object |
| `bldg:storeysAboveGround` | only where the Land records it (`storeysBelowGround` not provided in the DFB) |
| `bldg:address` (xAL) | only where ALKIS/ATKIS holds it; may be partial (Land/Ort/Straße/Hausnr) |
| `gml:name` | the building's proper name only (not the function text), optional |
| `bldg:lod2TerrainIntersection` | terrain-intersection polygon, optional |
| **generic attributes** (`gen:stringAttribute`) | `Gemeindeschluessel` (8-digit AGS), `Grundrissaktualitaet` (date of last sync with the ALKIS/ATKIS footprint), `DatenquelleDachhoehe`, `DatenquelleLage`, `DatenquelleBodenhoehe`, `DatenquelleGeschossanzahl` (source/quality codes), `Geometrietyp2DReferenz` (code) |

> `[UNVERIFIED]`: `bldg:yearOfConstruction` and `storeysBelowGround` are *not* in the DFB — treat
> as not provided. Whether Bavaria emits `bldg:ClosureSurface`/`OuterCeilingSurface`/`OuterFloorSurface`
> or any `gen:doubleAttribute` — unconfirmed. Whether the OpenData tiles carry the long
> `LoD2_…_BY` `gml:name` or just `<E_km>_<N_km>` — unconfirmed.

### Geometry

LoD2 `gml:Solid` bodies + `gml:MultiSurface` aggregates; `gml:posList`/`gml:pos`, `srsDimension="3"`,
3 decimals. **Semantic surfaces:** `bldg:RoofSurface`, `bldg:WallSurface`, `bldg:GroundSurface` are
the standard set (these are what you key procedural materials / window placement on); `bldg:ClosureSurface`,
`OuterCeilingSurface`, `OuterFloorSurface` are valid CityGML 1.0 and may appear on complex bodies.
Shared geometries between adjacent buildings are stored **redundantly**. `bldg:BuildingPart`s exist
(the attribute set applies per Building *or* BuildingPart). **No LoD1 in this product** (LoD1 is
derived on demand from the current LoD2 — there's no `pn=lod1` OpenData entry).

### Tiling & download

- **2 km × 2 km grid**, files `<E_km>_<N_km>.gml` (even km), ~10–100 MB/tile depending on density.
- Direct tiles: `https://download1.bayernwolke.de/a/lod2/citygml/<E_km>_<N_km>.gml` (mirror `download2…`).
- Single tile (interactive grid): `https://geoservices.bayern.de/od/wms/grid/v1/opendatagrid?title=Opendata_Auswahl_LoD2&layers=lod2&service=wms`
- Polygon → metalink: `https://geoservices.bayern.de/services/poly2metalink/metalink/lod2?data=lod2&service=polygon`
- Per Gemeinde: `https://geodaten.bayern.de/odd/a/lod2/citygml/meta/kml/gemeinde.kml?service=kml` (≤500 MB; ≤6 GB kreisfreie Städte). Per Landkreis: `…/meta/kml/kreis.kml?service=kml` (≤6 GB). Per-Gemeinde metalink: `…/meta/metalink/<AGS8>.meta4`.
- Whole Bavaria: `https://geodaten.bayern.de/odd/a/lod2/citygml/meta/metalink/09.meta4` (~150 GB).

**Docs:** `https://geodaten.bayern.de/odd/m/3/pdf/hinweise_daten_lod2_download.pdf` (Stand 2023-02-22);
AdV LoD2-DE DFB v3.1 `https://www.ldbv.bayern.de/mam/ldbv/dateien/datenformatbeschreibung_lod-de_v_3.1.pdf`;
AdV LoD1-DE DFB `https://sg.geodatenzentrum.de/web_public/gdz/dokumentation/deu/lod1-de_datenformatbeschreibung.pdf`;
LDBV faltblatt `https://www.ldbv.bayern.de/mam/ldbv/dateien/faltblatt_gebäudemodelle.pdf`;
AdV "3D-Gebäudemodelle LoD" `https://www.adv-online.de/AdV-Produkte/Weitere-Produkte/3D-Gebaeudemodelle-LoD/`,
PQS (ZIP) under `…/Standards-der-Geotopographie/`. ZSHH (the central office): `https://www.ldbv.bayern.de/vermessung/zshh/`, `zshh@ldbv.bayern.de`.

---

## B. ALKIS `AX_Gebaeude` — the rich cadastral building object (CHARGEABLE, not OpenData)

The cadastral building object. Carried in the chargeable ALKIS-NAS / ALKIS-Shape Komplettdaten /
ALKIS-GeoPackage (via GeodatenOnline) — **not** in any OpenData product (the OpenData `parzellarkarte`
is raster, `hausumringe` is geometry-only). Documented here because it's the same attribute model
that LoD2 references (`externalReference` → the ALKIS OID) and that the (Bavaria-empty) ATKIS
`SIE05` layer would carry. Object type **`AX_Gebaeude`** Kennung **31001**, REO, derived from
`AX_Gebaeude_Kerndaten` + `AG_Objekt`. Definition: *"'Gebäude' ist ein dauerhaft errichtetes
Bauwerk, dessen Nachweis wegen seiner Bedeutung als Liegenschaft erforderlich ist …"*.

| Attribute | Kennung | Type / codelist | Notes |
|---|---|---|---|
| gebaeudefunktion | GFK | **AX_Gebaeudefunktion** (codelist, several hundred values) | "vorherrschende funktionale Bedeutung (Dominanzprinzip)" — see §C |
| weitereGebaeudefunktion | WGF | AX_Weitere_Gebaeudefunktion | 0..* — functions besides the dominant one |
| name | NAM | CharacterString | 0..* |
| nutzung | NTZ | AX_Nutzung_Gebaeude (datatype: `anteil` Integer + `nutzung` → **AX_Nutzung**: 1000 Zivil / 1100 Privat / 1200 öffentlich / 1300 religiös / 2000 militärisch) | 0..* — Nutzungsanteile summieren zu 100 |
| bauweise | BAW | **AX_Bauweise_Gebaeude** | 1100 Freistehendes Einzelgebäude · 1200 Freistehender Gebäudeblock · 1300 Einzelgarage · 1400 Doppelgarage · 1500 Sammelgarage · 2100 Doppelhaushälfte · 2200 Reihenhaus · 2300 Haus in Reihe · 2400 Gruppenhaus · 2500 Gebäudeblock in geschlossener Bauweise · 4000 Offene Halle · 9999 Sonstiges |
| hochhaus | HOH | Boolean | i.d.R. ab 8 oberird. Geschossen bzw. ≥22 m (landesabhängig) |
| zustand | ZUS | **AX_Zustand_Gebaeude** | 1000 In behelfsmäßigem Zustand · 2000 In ungenutztem Zustand · 2100 Außer Betrieb/stillgelegt/verlassen · 2200 Verfallen/zerstört · 2300 Teilweise zerstört · 3000 Geplant und beantragt · 4000 Im Bau |
| geschossflaeche | GFL | Area (m²) | |
| grundflaeche | GRF | Area (m²) | |
| dachgeschossausbau | DGA | **AX_Dachgeschossausbau_Gebaeude** | 1000 Nicht ausbaufähig · 2000 Ausbaufähig · 3000 Ausgebaut · 4000 Ausbaufähigkeit unklar |
| gebaeudekennzeichen | GKN | CharacterString(24) | 8 (Gemeinde) + 5 (Straße) + 4 (Hausnr) + 4 (Adressierungszusatz) + 3 (lfd. Nr.), rechtsbündig, mit Nullen aufgefüllt; "nur in ATKIS dauerhaft geführt" |
| **inherited from `AX_Gebaeude_Kerndaten` (31007):** | | | |
| anzahlDerOberirdischenGeschosse | AOG | Integer | |
| anzahlDerUnterirdischenGeschosse | AUG | Integer | |
| objekthoehe | HHO | **AX_RelativeHoehe** (datatype) | 0..* — Höhendifferenz unterer↔oberer Bezugspunkt, m |
| dachform | DAF | **AX_Dachform** | see §C |
| umbauterRaum | URA | Volume (m³) | |
| baujahr | BJA | Integer | 0..* — Jahr der Fertigstellung / baulichen Veränderung |
| lageZurErdoberflaeche | OFL | AX_LageZurErdoberflaeche_Gebaeude | nur bei aufgeständerten/beweglichen/drehbaren/unterirdischen Gebäuden (e.g. 1200 "Unter der Erdoberfläche") |

Plus the AAA-Basisschema metadata (`identifikator`, `lebenszeitintervall`, `modellart`, `anlass`,
…) and geometry (Fläche / `GM_Surface`). Relations: `zeigtAuf` (Lagebezeichnung), `gehoertZuBauwerk`,
etc. **`AX_Bauteil` (31002):** `bauart` (BAR → AX_Bauart_Bauteil: 2610/2620 Tor/Durchfahrt, 2710
Schornstein im Gebäude, 2720 Turm im Gebäude, …), `durchfahrtshoehe` (DHU, only with bauart 2610/2620),
plus the same Kerndaten inheritance; geometry PolyhedralSurface/CompositeSurface; always within its
Gebäude.

---

## C. The roof-shape and building-function codelists

These are the GeoInfoDok / AdV codelists that LoD2 `roofType`/`function` and ALKIS `dachform`/
`gebaeudefunktion` use — i.e. what you'd switch procedural materials/window-density/roof-treatment on.
Authoritative source: GDI-DE registry `https://registry.gdi-de.org/codelist/de.adv-online.gid/<Name>`.

**`AX_Dachform`** — `…/AX_Dachform`:
| code | shape | code | shape |
|---|---|---|---|
| 1000 | Flachdach | 3500 | Zeltdach |
| 2100 | Pultdach | 3600 | Kegeldach |
| 2200 | Versetztes Pultdach | 3700 | Kuppeldach |
| 3100 | Satteldach | 3800 | Sheddach |
| 3200 | Walmdach | 3900 | Bogendach |
| 3300 | Krüppelwalmdach | 4000 | Turmdach |
| 3400 | Mansardendach | 5000 | Mischform |
| | | 9999 | Sonstiges |
*(The ATKIS-OK PDF prints these with some column-misalignment; the registry pairing above is canonical.)*

**`AX_Gebaeudefunktion`** — `…/AX_Gebaeudefunktion` (several hundred values; round-number group heads):
1000 Wohngebäude · 1010 Wohngebäude · 1020 Wohnhaus · 1021 Wohnheim · 1022 Kinderheim · 1023
Seniorenheim · 1100 Gemischt genutztes Gebäude mit Wohnen · 1300 Gebäude zur Freizeitgestaltung ·
2000 Gebäude für Wirtschaft oder Gewerbe · 2100 Gebäude für Gewerbe und Industrie · 2200 Sonstiges
Gebäude für Gewerbe und Industrie · 2300 Gebäude für Handel/Industrie mit Wohnen · 2400
Betriebsgebäude zu Verkehrsanlagen · 2500 Gebäude zur Versorgung · 2600 Gebäude zur Entsorgung ·
2700 Land-/forstwirtschaftliches Betriebsgebäude · 3000 Gebäude für öffentliche Zwecke (3010
Verwaltungsgebäude, 3020 Gebäude für Bildung/Forschung, 3071 Polizei, 3072 Feuerwehr, 3073 Kaserne,
3075 Schutzbunker, 3080 Justizvollzugsanstalt, 3091 Bahnhofsgebäude, 3092 Flughafengebäude, …) ·
3100 Gebäude für öffentliche Zwecke mit Wohnen · 3200 Gebäude für Erholungszwecke (3211 Sport-/
Turnhalle, 3222 Hallenbad, 3290 Schutzhütte, …) · 9998 Nach Quellenlage nicht zu spezifizieren.
The exhaustive list is in `OK_Basis-DLM_712.pdf` §8.2 / `OK_DLKM_712.pdf` and machine-readable in
the NAS XSD codelist file. **`[UNVERIFIED]`:** whether the Basis-DLM `AX_Gebaeudefunktion` enum is
a strict subset of the ALKIS one — they share the datatype, so assume identical.

**`AX_Zustand_Gebaeude`**, **`AX_Bauweise_Gebaeude`**, **`AX_Dachgeschossausbau_Gebaeude`**,
**`AX_Nutzung`**, **`AX_LageZurErdoberflaeche_Gebaeude`**, **`AX_Bauart_Bauteil`** — values in §B.
**`AX_Vegetationsmerkmal_Wald`** / **`_Landwirtschaft`** / **`_Gehoelz`** — for land-use, see
[03-alkis-tatsaechliche-nutzung.md](03-alkis-tatsaechliche-nutzung.md). Per-Nutzungsart `AX_Funktion_*`
and per-object-type `AX_Zustand_*` codelist names — see [08-adv-geoinfodok-specs.md](08-adv-geoinfodok-specs.md) §4.

---

## D. Hausumringe — 2D footprint polygons (OpenData, no ALKIS attributes)

**Product key:** `hausumringe`. Georeferenced footprint polygons of the Liegenschaftskataster
buildings, distributed **without** the ALKIS attribute payload (cf. the AdV "HU-DE" product). Format
**ESRI Shape**, EPSG:25832. The only OpenData distribution is **per Regierungsbezirk** (~100 MB
each, **quarterly**), selected via `https://geodaten.bayern.de/odd/m/3/daten/hausumringe/bezirk/kml/HU_regierungsbezirk.kml?service=kml`.
No metalink / whole-Bavaria / Gemeinde option self-service. The catalogue record's `datenhinweise`
field is empty — no Bavaria-specific DFB PDF located; related: ZSHH `https://www.ldbv.bayern.de/vermessung/zshh/`,
AdV Hauskoordinaten/Hausumringe `https://www.adv-online.de/AdV-Produkte/Liegenschaftskataster/Amtliche-Folgeprodukte/Amtliche-Hauskoordinaten/`,
HK-DE DFB v5.2 `https://www.ldbv.bayern.de/mam/ldbv/dateien/datenformatbeschreibung_hk-de_v_5.2.pdf`,
Bayern Hauskoordinaten DFB `https://www.ldbv.bayern.de/mam/ldbv/dateien/hauskoordinaten-by_datenformatbeschreibung.pdf`.
**Hauskoordinaten / Hausnummern** (address points) are **NOT** OpenData — chargeable via ZSHH; only
a "WFS Hauskoordinaten" (via geodatenonline) exists. `[UNVERIFIED]`: the exact Hausumringe attribute
table (whether even an OID is retained).

---

## E. DOM-Mesh — textured 3D surface mesh (for visualization, very large)

**Product key:** `dommesh`. A visible-surface 3D mesh (buildings, vegetation, terrain) built from
the DOM20 grid + RGB nadir textures from the Bayernbefliegung; 2.5D (one height per planar coord).
Covers all Bavaria; updated in the Bayernbefliegung cycle. **Format: SLPK** — a zipped OGC **I3S**
(Indexed 3D Scene Layer, with LoD pyramid) dataset, one `.slpk` per Los/flight day. CRS EPSG:25832,
height DHHN2016 (the page also cites EPSG:7837 for the height component); position/height accuracy
±4–6 dm. **Project-area-wise** (per flight day) — not a regular grid; selection via
`https://geodaten.bayern.de/odd/m/3/daten/DOMMesh/DOM_Mesh_projektgebiete_2026.kml?service=kml`;
files `https://download1.bayernwolke.de/p/dom-mesh-slpk/<los_id>/DSM_Mesh.slpk` (mirror `download2…`),
e.g. `aria2c https://download1.bayernwolke.de/p/dom-mesh-slpk/125042_0/DSM_Mesh.slpk --dir=… -x 5 -s 1000`.
**Size: 50–200 GB per flight day; whole Bavaria ~8.3 TB.** Known issues: blurry roof edges, "hanging"
trees, smeared façades, outliers (cranes/power lines), overlap-zone seams. Viewer: ArcGIS Earth /
any I3S client. dh `https://www.geodaten.bayern.de/odd/m/3/html/datenhinweise/datenhinweise_dommesh.html`
(references the AdV "Leitfaden Mesh V1.0"). — Practical for the pipeline only as a *reference
surface* for small areas; not something you'd bulk-ingest. For per-building geometry, LoD2 is the
right product. A working proof of concept that extracts a small AOI out of one Los via HTTP range
requests (≈5–15 MB fetched instead of the whole 50–200 GB SLPK) — and documents the internal
ZIP64 / I3S `meshpyramids` layout (node pages, OBBs in EPSG:25832, the `position`+`uv0` geometry
buffer) — is in [`experiments/dommesh_cutout/`](../../experiments/dommesh_cutout/).

---

## F. So how do we get "more building info into Blender"?

1. **Mine the LoD2 CityGML we already download** — per-building `function` (Gebäudefunktion code →
   residential / office / commercial / industrial / public / agricultural …), `roofType` (Dachform
   code → flat / pent / gable / hip / mansard / …), `measuredHeight`, `storeysAboveGround`, address;
   and the semantic `RoofSurface` / `WallSurface` / `GroundSurface` faces. That's enough to drive
   procedural wall materials, window density (per storey / per function), roof treatment (per
   Dachform), and roughness/colour variation. No textures in the data — we generate them.
2. If we ever buy **ALKIS** (chargeable): join `AX_Gebaeude` attrs (`bauweise`, `baujahr`, exact
   `geschosse`, `dachgeschossausbau`, `zustand`) by ALKIS-ID onto the LoD2 buildings for more
   fidelity. Not OpenData, so not a near-term option.
3. **Do NOT look in ATKIS Basis-DLM** for Bavarian buildings — it's empty there (§02).
