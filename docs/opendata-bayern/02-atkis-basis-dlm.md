# 02 — ATKIS Basis-DLM

**Product key:** `atkis_basis_dlm` · **Page:** https://geodaten.bayern.de/opengeodata/OpenDataDetail.html?pn=atkis_basis_dlm

## What it is

The **Digitales Basis-Landschaftsmodell** — the highest-resolution member of the ATKIS DLM family
(Basis-DLM → DLM50 → DLM250 → DLM1000, the coarser ones generalized from it). A vector description
of the topographic objects of the landscape, target/content scale **1:25 000**, "vollständig,
längentreu, nicht kartographisch generalisiert" within model accuracy. Positional accuracy
(LDBV): **±3 m** for roads/railways, up to **±15 m** for minor objects. **Systematic annual**
statewide update; important changes faster; built to the nationwide **AAA / GeoInfoDok 7.1.2**
model (AFIS-ALKIS-ATKIS). It is the "OSM-equivalent" official layer.

Models: roads, paths/tracks, railways (centrelines + areas, with class/width/lanes), water bodies
(lines + areas + centrelines), land-use polygons (residential / industry / agriculture / forest /
heath / moor / water …), point objects (towers, masts, transit stops, helipads), bridges /
tunnels / dams / cuttings, administrative areas (Bund → Gemeinde, with Einwohnerzahl), legally-
protected areas, relief forms (Damm/Wall/Deich, Einschnitt, Höhleneingang, Felsen, Düne,
Höhenlinien, Soll). **The 7.1.2 schema also defines a Gebäude object area (`AX_Gebaeude`,
`AX_Bauteil`) — but Bavaria does NOT populate it** (see "Buildings" below). The Bavarian "plus"
variant adds signed leisure trails, one-way info, surface conditions, and POI-like point features
(Feuerwehr, Rettungsdienst, Kindergärten, Tankstellen…), plus TK25 sheet numbers.

## Formats — three downloads, all one bayernwide ZIP (no tiling)

| Format | What it is | File | URL | Size | Refresh |
|---|---|---|---|---|---|
| **NAS / GML** | full GeoInfoDok 7.1.2 NAS XML — full REO/ZUSO/NREO hierarchy | `nas_712.zip` | `https://geodaten.bayern.de/odd/m/2/basisdlm/nas/nas_712.zip` | ~2.17 GB | weekly |
| **Shape** | BKG-style flat layer-oriented Shapefiles per the AdV-Shape-Profil (simplified, no full NAS power) | `bkg_shape_712.zip` | `https://geodaten.bayern.de/odd/m/2/basisdlm/bkg_shape/bkg_shape_712.zip` | ~1.24 GB | monthly |
| **GeoPackage "Basis-DLM plus"** | GeoInfoDok 7.1.2 + Bavarian extensions, flattened, ships a `QGIS_Symbolisierung.qgs` | `by_basisdlm_plus.zip` | `https://geodaten.bayern.de/odd/m/2/basisdlm/plus/by_basisdlm_plus.zip` | ~1.85 GB | weekly |
| WFS (NAS output) | — | — | `https://geoservices.bayern.de/wfs/v1/ogc_atkis_basisdlm.cgi?` | — | weekly |

Per-Landkreis/Gemeinde extracts: only on request from the Kundenservice, not self-service.
**Docs:** `https://www.geodaten.bayern.de/odd/m/3/html/datenhinweise/datenhinweise_atkis_basisdlm_plus.html`,
PDFs `https://geodaten.bayern.de/odd/m/3/pdf/basisdlm_plus_datenformatbeschreibung.pdf` and
`…/basisdlm_plus_datenmodell.pdf` (full per-object-type catalogue with all value lists), LDBV
`https://www.ldbv.bayern.de/produkte/landschaftsinformationen/landschaftsmodell.html`.

### GeoPackage "plus" naming convention
Object types → `<G>_<Kennung>_<NameOhneAX_>` with G ∈ {F=Fläche, L=Linie, P=Punkt} — e.g.
`F_41001_Wohnbauflaeche`, `L_42003_Strassenachse`, `F_43002_Wald`. Attributes →
`<Kennung>_<Name>` e.g. `BEB_artDerBebauung`. ZUSO/NREO attributes are merged onto the related
REO. Universal attrs: `ONR_objektnummer`, `ADA_aenderungsdatum`, `TK25` (BY plus only).
Layer-overlay semantics: `F_4*` = Tatsächliche Nutzung (redundancy-free full coverage); `F_5*` =
overlay TN with more landscape detail; `F_7*` = overlay legal definitions (Naturschutzgebiete,
Verwaltungsflächen). `LZE_lageZurErdoberflaeche_BY` (−2…+2) = simplified `HDU_hatDirektUnten`.

## The AdV object catalogue (GeoInfoDok / ATKIS-OK 7.1.2)

**Objektartenbereiche → Objektartengruppen:**
- **Gebäude (30000)**: *Angaben zum Gebäude (31000)* — `AX_Gebaeude` 31001, `AX_Bauteil` 31002,
  `AX_BesondereGebaeudelinie` 31003, `AX_Firstlinie` 31004, `AX_BesondererGebaeudepunkt` 31005,
  `AX_Nutzung_Gebaeude` 31006 (datatype), `AX_Gebaeude_Kerndaten` 31007 (abstract), `AX_RelativeHoehe`
  31008 (datatype). *(Schema membership only — see "Buildings" for what Bavaria actually ships.)*
- **Tatsächliche Nutzung (40000)**: Siedlung (41000), Verkehr (42000), Vegetation (43000), Gewässer (44000)
- **Bauwerke, Einrichtungen und sonstige Angaben (50000)**: Bauwerke/Einrichtungen in Siedlungsflächen (51000),
  Besondere Anlagen (52000), Verkehrsbauwerke (53000), Besondere Vegetationsmerkmale (54000),
  Besondere Eigenschaften Gewässer (55000/57000), Hilfslinien (58000), Angaben zum Straßennetz (56000)
- **Relief (60000)**: Reliefformen (61000), Primäres DGM / 3D-Messdaten (62000), Sekundäres DGM (63000)
- **Gesetzliche Festlegungen, Gebietseinheiten, Kataloge (70000)**: Öffentlich-rechtl./sonstige Festlegungen (71000),
  Geographische Gebietseinheiten (74000), Administrative Gebietseinheiten (75000), + Lagebezeichnungen (12000)

**Key object types** (`AX_*`, Objektartenkennung):
`AX_Gebaeude` 31001 · `AX_Bauteil` 31002 · `AX_Turm` 51001 · `AX_Wohnbauflaeche` 41001 ·
`AX_IndustrieUndGewerbeflaeche` 41002 · `AX_Halde` 41003 · `AX_Bergbaubetrieb` 41004 ·
`AX_TagebauGrubeSteinbruch` 41005 · `AX_FlaecheGemischterNutzung` 41006 ·
`AX_FlaecheBesondererFunktionalerPraegung` 41007 · `AX_SportFreizeitUndErholungsflaeche` 41008 ·
`AX_Friedhof` 41009 · `AX_Strassenverkehr` 42001 · `AX_Strasse` 42002 (ZUSO) ·
**`AX_Strassenachse` 42003** · `AX_Fahrbahnachse` 42005 · `AX_Weg` 42006 · `AX_Fahrwegachse` 42008 ·
`AX_Platz` 42009 · `AX_Bahnverkehr` 42010 · **`AX_Bahnstrecke` 42014** · `AX_Flugverkehr` 42015 ·
`AX_Schiffsverkehr` 42016 · **`AX_Landwirtschaft` 43001** · **`AX_Wald` 43002** · `AX_Gehoelz` 43003 ·
`AX_Heide` 43004 · `AX_Moor` 43005 · `AX_Sumpf` 43006 · `AX_UnlandVegetationsloseFlaeche` 43007 ·
**`AX_Fliessgewaesser` 44001** · `AX_Wasserlauf` 44002 (ZUSO) · `AX_Kanal` 44003 (ZUSO) ·
**`AX_Gewaesserachse` 44004** · `AX_Hafenbecken` 44005 · `AX_StehendesGewaesser` 44006 · `AX_Meer` 44007 ·
`AX_Turm` 51001 · `AX_BauwerkOderAnlageFuerIndustrieUndGewerbe` 51002 · `AX_VorratsbehaelterSpeicherbauwerk` 51003 ·
`AX_Transportanlage` 51004 · `AX_Leitung` 51005 · `AX_BauwerkOderAnlageFuerSportFreizeitUndErholung` 51006 ·
`AX_HistorischesBauwerkOderHistorischeEinrichtung` 51007 · `AX_SonstigesBauwerkOderSonstigeEinrichtung` 51009 ·
`AX_EinrichtungInOeffentlichenBereichen` 51010 · `AX_Ortslage` 52001 · `AX_Hafen` 52002 · `AX_Schleuse` 52003 ·
`AX_Testgelaende` 52005 · `AX_BauwerkImVerkehrsbereich` 53001 (bridges/tunnels) · `AX_Strassenverkehrsanlage` 53002 ·
`AX_WegPfadSteig` 53003 · `AX_Bahnverkehrsanlage` 53004 · `AX_SeilbahnSchwebebahn` 53005 · `AX_Gleis` 53006 ·
`AX_Flugverkehrsanlage` 53007 · `AX_EinrichtungenFuerDenSchiffsverkehr` 53008 · `AX_BauwerkImGewaesserbereich` 53009 ·
**`AX_Vegetationsmerkmal` 54001** (hedges/tree rows/scrub) · `AX_Gewaessermerkmal` 55001 · `AX_Polder` 55003 ·
`AX_Wasserspiegelhoehe` 57001 · `AX_SchifffahrtslinieFaehrverkehr` 57002 · `AX_Gewaesserstationierungsachse` 57003 ·
`AX_Sickerstrecke` 57004 · `AX_Nullpunkt` 56002 · `AX_Abschnitt` 56003 · `AX_Ast` 56004 · `AX_Netzknoten` 56001 (ZUSO) ·
`AX_DammWallDeich` 61003 · `AX_Einschnitt` 61004 · `AX_Hoehleneingang` 61005 · `AX_FelsenFelsblockFelsnadel` 61006 ·
`AX_Duene` 61007 · `AX_Hoehenlinie` 61008 · `AX_Soll` 61010 · `AX_BoeschungKliff` 61001 (ZUSO) ·
`AX_Punkt3D` 62020 · `AX_Strukturlinie3D` 62030 · `AX_Flaeche3D` 62040 · `AX_AbgeleiteteHoehenlinie` 63020 ·
`AX_AndereFestlegungNachWasserrecht` 71004 · `AX_NaturUmweltOderBodenschutzrecht` 71006 ·
`AX_Denkmalschutzrecht` 71009 · `AX_SonstigesRecht` 71011 · `AX_Schutzzone` 71012 ·
`AX_SchutzgebietNachWasserrecht` 71005 (ZUSO) · `AX_SchutzgebietNachNaturUmweltOderBodenschutzrecht` 71007 (ZUSO) ·
`AX_Landschaft` 74001 · `AX_Gewann` 74003 · `AX_Insel` 74004 · `AX_Wohnplatz` 74005 ·
`AX_KommunalesGebiet` 75003 · `AX_Gebiet_Bundesland` 75005 · `AX_Gebiet_Regierungsbezirk` 75006 ·
**`AX_Gebiet_Kreis` 75007** · `AX_Kondominium` 75008 · `AX_Gebietsgrenze` 75009 ·
`AX_Gebiet_Verwaltungsgemeinschaft` 75011 · `AX_KommunalesTeilgebiet` 75012 (+ attribute carriers
`AX_Bundesland` 73002 / `AX_Regierungsbezirk` 73003 / `AX_KreisRegion` 73004 / `AX_Gemeinde` 73005 /
`AX_Gemeindeteil` 73006 / `AX_Verwaltungsgemeinschaft` 73009 / `AX_Gemeindekennzeichen` 73014).

**Hierarchy:** REO (carries geometry) · ZUSO (groups REOs, no geometry — e.g. `AX_Strasse` 42002,
`AX_Wasserlauf`/`AX_Kanal`, `AX_Netzknoten`) · NREO (attributes only). The **NAS** preserves all
three; the **Shape** and **GeoPackage "plus"** flatten to REOs with ZUSO/NREO attrs merged in.

**Recurring coded attributes:** `OBJART` (Objektartenkennung) · `FKT/funktion` · `WDM/widmung`
(road class: Bundesautobahn/Bundes-/Staats-/Kreis-/Gemeindestraße/nicht öffentlich/im Bau/sonstige
— from BAYSIS) · `BVB` besondereVerkehrsbedeutung · `BRF` breiteDerFahrbahn · `FSZ`
anzahlDerFahrstreifen · `FTR` fahrbahntrennung · `ZUS/zustand` (In Betrieb / Außer Betrieb / Im Bau)
· `OFM` oberflaechenmaterial · `STS` strassenschluessel (17-char) · `BEZ` bezeichnung (e.g. "B85",
"NES28", "St2346") · `ART` (subtype on Vegetationsmerkmal/Gewässermerkmal/Friedhof/…) · `BEB`
artDerBebauung (offen/geschlossen) · `HDU_X`/`LZE_lageZurErdoberflaeche_BY` (over/under-pass level
−2…+2) · `NAM`/`ZNM` name · `VEG` vegetationsmerkmal (1100 Laubholz / 1200 Nadelholz / … on Wald;
1010 Ackerland / 1020 Grünland / 1040 Rebfläche / … on Landwirtschaft) · `NTZ` nutzung (1000 etc.).
Universal: `ONR_objektnummer` / `OBJID` (Germany-wide unique object id, form `DEBYBDLM<2c><6hex>`,
e.g. `DEBYBDLMCI0000el`), `ADA_aenderungsdatum` / `BEGINN`/`ENDE` (Lebenszeitintervall), `MODELLART`
(= `Basis-DLM#DTK25` — the combined model). Missing numeric attr in Shape = `-9999` (or `-9998` if
unset in source); text attr left empty when not applicable in a merged layer.

## The Shape product — verified layer list `[VERIFIED]`

From the actual `bkg_shape_712.zip` (2025-09-16). Layer name = `<theme><NN>_<geom>` where `geom` ∈
`f`=Fläche / `l`=Linie / `p`=Punkt / `b`=Null-Shape (no geometry, e.g. relation tables) / `n`=
Tabellendaten ohne Geometrie. Encoding declared per `.cpg`. The **Bavarian export is a SUBSET of
the AdV Shape profile** — no `pra0*` (Präsentationsobjekte), no `fdv01` (Fachdatenverbindung table),
no `hho01` (Objekthöhen table), no `rel02`/`rel03` (3D-Messdaten / abgeleitete Höhenlinien).

| Layer(s) | Object types | record count (verified where noted) | key attribute fields |
|---|---|---|---|
| `sie01_f` | `AX_Ortslage` 52001 | — | LAND, MODELLART, OBJART, OBJART_TXT, OBJID, BEGINN, ENDE, HDU_X, FDV_X, NAM, RGS |
| `sie02_f` | 41001-41009 (Wohnbau / Industrie&Gewerbe / Halde / Bergbau / Tagebau / GemischteNutzung / BesondereFunktionalePraegung / SportFreizeitErholung / Friedhof) | (huge — 532 MB dbf) | …+ DLU, EDU, IWN, AGT, BEB, BEZ, FGT, **FKT**, LGT, NAM, PEG, ZNM, **ZUS** |
| `sie03_f`/`_l`/`_p` | 51002-51010 (Bauwerke/Einrichtungen in Siedlungsflächen, except Turm) | — | …+ EDU, ATP, BEZ, **BWF** (Bauwerksfunktion), **DAF** (Dachform — on 51009), **FKT**, HHO_X, HYD, NAM, OFL, SPE, SPO, BRO, SPG, PRO, KMA, ART, **ZUS** |
| `sie04_f`/`_l`/`_p` | 52002 Hafen / 52003 Schleuse / 52005 Testgelaende | — | …+ EDU, HFK (Hafenkategorie), HHO_X, KON, NAM, NTZ, **ZUS** |
| **`sie05_f`** | **31001 AX_Gebaeude / 31002 AX_Bauteil / 51001 AX_Turm** | **5 records — ALL `OBJART=51001 AX_Turm`; zero AX_Gebaeude/AX_Bauteil** `[VERIFIED]` | 41 fields incl. OBJID_G, DPL (Herkunft), HNR/PNR/LNR/SCH, AOG/AUG, BAT, BAW, **BJA** (Baujahr), **BWF**, **DAA** (Dachart), **DAF** (Dachform), **DGA**, DHU, **GFK** (Gebäudefunktion), **GFL** (Geschossfläche), GKN, **GRF** (Grundfläche), HOH (Hochhaus), NAM, NTZ, OFL, **URA** (umbauter Raum), **WGF**, ZNM, **ZUS** — **all `-9999`/empty in practice** |
| **`sie05_p`** | 31001 / 51001 + 12002/12003 address attrs | **10,805 records — ALL `OBJART=51001 AX_Turm`** (towers/masts: Vexierturm, Thürnersturm, …) `[VERIFIED]` | 39 fields (same set as sie05_f minus DHU/BAT) — building attrs all empty |
| `ver01_f`/`_l` | 42001 Strassenverkehr / 42003 Strassenachse / 42005 Fahrbahnachse / 42009 Platz (+ ZUSO 42002 Strasse) | `_l` = **1,527,717 records** `[VERIFIED]` | LAND…OBJID, OBJART_Z/OBJID_Z (the merged-in AX_Strasse ZUSO), **BEZ** ("NES28"), BFS, **BRF** (Fahrbahnbreite), BRV, **BVB**, FAR, **FKT**, **FSZ** (lanes), **FTR**, IBD, NAM, OFM, RGS, **STS** (17-char Straßenschlüssel), **WDM** (Widmung code, e.g. 1306/1307), ZNM, ZUS |
| `ver02_l` | 42006 Weg / 42008 Fahrwegachse / 53003 WegPfadSteig | (1.7 GB dbf — millions) | similar to ver01_l |
| `ver03_f`/`_l` | 42010 Bahnverkehr / 42014 Bahnstrecke / 53005 SeilbahnSchwebebahn / 53006 Gleis | — | rail attrs (BKT Bahnkategorie, ELK Elektrifizierung, SPW Spurweite, …) |
| `ver04_f` | 42015 Flugverkehr | — | ARTFunktion / NUTZung / ZUStand etc. |
| `ver05_f`/`_l` | 42016 Schiffsverkehr / 57002 SchifffahrtslinieFaehrverkehr | — | |
| `ver06_f`/`_l`/`_p` | 53001 BauwerkImVerkehrsbereich (bridges/tunnels) / 53002 Strassenverkehrsanlage / 53004 Bahnverkehrsanlage / 53007 Flugverkehrsanlage / 53008 EinrichtungenFuerDenSchiffsverkehr / 53009 BauwerkImGewaesserbereich | (207 MB dbf — many) | BWF Bauwerksfunktion, ZUS, NAM, BRF, OFL, HHO_X, … |
| `veg01_f` | **43001 AX_Landwirtschaft** | (424 MB dbf — huge) | LAND…OBJID, DLU, EDU, NAM, NTZ, RGS, **VEG** (vegetationsmerkmal: Ackerland/Grünland/Reb/…), ZUS |
| `veg02_f` | **43002 AX_Wald** | **620,872 records** `[VERIFIED]` | LAND, MODELLART, OBJART, OBJART_TXT, OBJID, BEGINN, ENDE, HDU_X, FDV_X, DLU, EDU, NAM, NTZ (1000), RGS, **VEG** (1100=Laubholz, 1200=Nadelholz, 1300=Laub-und-Nadelholz, 1310/1320 mixed), ZUS — **the Laub/Nadel code IS populated here** (unlike the ALKIS-TN Shape) |
| `veg03_f` | 43003 Gehoelz / 43004 Heide / 43005 Moor / 43006 Sumpf / 43007 UnlandVegetationsloseFlaeche | — | FKT/ART/ZUS/NAM/… |
| `veg04_f`/`_l`/`_p` | 54001 AX_Vegetationsmerkmal (hedges, tree rows, scrub, Streuobst, …) | — | ART (subtype), ZUS, NAM |
| `gew01_f`/`_l` | 44001 Fliessgewaesser / 44004 Gewaesserachse / 44005 Hafenbecken / 44006 StehendesGewaesser / 44007 Meer (+ ZUSO 44002 Wasserlauf / 44003 Kanal) | `_l` = 679 MB dbf — huge | FKT, GWK (Gewässerkennzahl), HYD, NAM, SKZ, WDM (Widmung), ZNM, ZUS, FLR (Fließrichtung), … |
| `gew02_f`/`_l`/`_p` | 55001 Gewaessermerkmal / 55003 Polder / 57001 Wasserspiegelhoehe / 57004 Sickerstrecke | — | ART, HHO_X, NAM, ZUS |
| `gew03_l` | 57003 Gewaesserstationierungsachse | — | |
| `geb01_f`/`_l` | 75003 KommunalesGebiet / 75005 Gebiet_Bundesland / 75006 Gebiet_Regierungsbezirk / 75007 Gebiet_Kreis / 75009 Gebietsgrenze / 75011 Gebiet_Verwaltungsgemeinschaft / 75012 KommunalesTeilgebiet (incl. attrs from 73002-73014) | `_f` = **2,637 records** `[VERIFIED]` | LAND, MODELLART, OBJART, OBJART_TXT, OBJID, BEGINN, ENDE, HDU_X, FDV_X, **ADF** (administrative Funktion code), **BEZ_GEM / BEZ_GMT / BEZ_KRS / BEZ_LAN / BEZ_RBZ / BEZ_VWG** (names at each admin level), **EWZ** (Einwohnerzahl), HIE, HIN, RGS, **SCH** (Schlüssel: "09"=Bayern, "094"=Oberfranken, …), ZWN |
| `geb02_f` | 74001 Landschaft / 74003 Gewann / 74004 Insel / 74005 Wohnplatz | — | NAM, ART |
| `geb03_f`/`_l`/`_p` | 71004 AndereFestlegungNachWasserrecht / 71006 NaturUmweltOderBodenschutzrecht / 71009 Denkmalschutzrecht / 71011 SonstigesRecht / 71012 Schutzzone (+ ZUSO 71005/71007) | — | ADF, NAM, ART, ZUS |
| `rel01_l`/`_p` | 61003 DammWallDeich / 61004 Einschnitt / 61005 Hoehleneingang / 61006 FelsenFelsblockFelsnadel / 61007 Duene / 61008 Hoehenlinie / 61010 Soll | — | ART, HHO_X, ZUS, NAM |
| `hdu01_b` | HatDirektUnten relations (over/underpass) — no geometry (Null-Shape) | (16 MB dbf — many) | OBJID + OBJID_unten pairs; the `HDU_X` flag on other layers points here |

(Not in the Bavarian export but defined in the AdV profile: `ver07_*` Angaben zum Straßennetz
56002/56003/56004 + ZUSO 56001 · `rel02_*` Messdaten 3D 62020/62030/62040 + ZUSO 61001 ·
`rel03_l` 63020 AX_AbgeleiteteHoehenlinie · `hho01_*` Objekthöhen (AX_RelativeHoehe) · `fdv01_*`
Fachdatenverbindung (zeigtAufExternes) · `pra01_*`/`pra02_*`/`pra03_*` Präsentationsobjekte
AP_PPO/AP_LPO/AP_FPO/AP_PTO/AP_LTO/AP_Darstellung 02310-02350.)

## The WFS feature-type list `[VERIFIED]`

`https://geoservices.bayern.de/wfs/v1/ogc_atkis_basisdlm.cgi?SERVICE=WFS&REQUEST=GetCapabilities&VERSION=2.0.0`
advertises **exactly 80 feature types** (namespace `adv:`): all the 4xxxx land-use classes, the
verkehr classes (`AX_Strasse`, `AX_Strassenachse`, `AX_Fahrbahnachse`, `AX_Bahnstrecke`,
`AX_Gleis`, …), the vegetation classes (`AX_Wald`, `AX_Gehoelz`, `AX_Landwirtschaft`,
`AX_Vegetationsmerkmal`, `AX_Heide`, `AX_Moor`, `AX_Sumpf`, `AX_UnlandVegetationsloseFlaeche`),
the water classes, the bauwerk classes (`AX_Turm`, `AX_BauwerkImVerkehrsbereich`, the
`AX_BauwerkOderAnlage*`, `AX_Transportanlage`, `AX_Leitung`, `AX_VorratsbehaelterSpeicherbauwerk`,
`AX_Hafen`, `AX_Schleuse`, `AX_SeilbahnSchwebebahn`, `AX_EinrichtungenFuerDenSchiffsverkehr`,
`AX_BauwerkImGewaesserbereich`, …), the relief classes (`AX_DammWallDeich`, `AX_Einschnitt`,
`AX_Hoehleneingang`, `AX_FelsenFelsblockFelsnadel`), the legal/area classes (`AX_Schutzzone`,
`AX_NaturUmweltOderBodenschutzrecht`, `AX_SonstigesRecht`, `AX_Insel`, `AX_KommunalesGebiet`,
`AX_Gebiet_Bundesland`/`Regierungsbezirk`/`Kreis`/`Verwaltungsgemeinschaft`, `AX_Gebietsgrenze`,
`AX_Bundesland`/`Regierungsbezirk`/`KreisRegion`/`Gemeinde`/`Verwaltungsgemeinschaft`,
`AX_Polder`, `AX_Wasserspiegelhoehe`, `AX_SchifffahrtslinieFaehrverkehr`,
`AX_Gewaesserstationierungsachse`, `AX_Sickerstrecke`, `AX_Gewaessermerkmal`).
**`AX_Gebaeude` and `AX_Bauteil` are NOT in the list.** Neither are `AX_Punkt3D` / `AX_Flaeche3D`
/ `AX_AbgeleiteteHoehenlinie`. Output formats: GML 3.1.1 / 3.2.1; CRS 25832 / 25833 / 4326 / 4258
/ 3857.

## Buildings — the definitive answer

- **Schema:** GeoInfoDok 7.1.2 *does* define `AX_Gebaeude` (31001) and `AX_Bauteil` (31002) with
  Modellart `Basis-DLM`. The ATKIS-OK Basis-DLM 7.1.2 PDF says for 31001: *"Erfassungskriterien
  Basis-DLM: Vollzählig, mit Ausnahme von untergeordneten Gebäuden … mit einer Fläche < 50 qm"* and
  *"Bildungsregeln Basis-DLM: Objektbildende Eigenschaften sind länderspezifisch im Erhebungsprozess
  zu berücksichtigen."* — i.e. it's there *by design*, but capture is each Land's call. The AdV
  Shape profile reserves layer `SIE05` for it. `AX_Gebaeude` carries `gebaeudefunktion` (GFK),
  `weitereGebaeudefunktion` (WGF), `bauweise` (BAW), `hochhaus` (HOH), `zustand` (ZUS),
  `geschossflaeche` (GFL), `grundflaeche` (GRF), `dachgeschossausbau` (DGA), `gebaeudekennzeichen`
  (GKN), and (inherited from `AX_Gebaeude_Kerndaten`) `anzahlDerOberirdischenGeschosse` (AOG),
  `anzahlDerUnterirdischenGeschosse` (AUG), `objekthoehe` (HHO → AX_RelativeHoehe),
  `dachform` (DAF), `umbauterRaum` (URA), `baujahr` (BJA), `lageZurErdoberflaeche` (OFL). See
  [05-buildings-and-3d.md](05-buildings-and-3d.md) for the codelists.
- **Bavaria's reality:** **does NOT populate it.** `[VERIFIED two ways]` — (1) the WFS has 80
  feature types, no `AX_Gebaeude`/`AX_Bauteil`; (2) in `bkg_shape_712.zip`, `sie05_f` = 5 records
  all `AX_Turm`, `sie05_p` = 10,805 records all `AX_Turm`, zero `AX_Gebaeude`/`AX_Bauteil`, and
  the GFK/DAF/BJA/AOG columns are `-9999`/empty even on the tower rows. Bavarian buildings live in
  **ALKIS** (full = chargeable; OpenData `hausumringe` = footprints w/o attrs) and **LoD2 CityGML**
  (3D + attrs, OpenData).
- **Other states:** per-state. State Landesprofile for 7.1.2 exist (BW, BB, TH, SN, HE, NRW, ST, …)
  — check the specific profile before assuming buildings are present. Concretely confirmed: Bavaria
  does *not*. (See [99-corrections-and-pitfalls.md](99-corrections-and-pitfalls.md) §1.)

## Use in the pipeline

Roads/rail → curves with width from `BRF`/`FSZ`; water lines/areas → curves / flat planes; land-use
polygons → material zones / scatter masks (forest via `veg02_f.VEG`, agriculture via `veg01_f.VEG`,
residential/industry from `sie02_f`); relief forms → optional detail; admin areas from `geb01_f`
(with Einwohnerzahl if you want population-driven density). **No buildings here** — use LoD2.
Convenient single download: the GeoPackage "plus" ZIP.
