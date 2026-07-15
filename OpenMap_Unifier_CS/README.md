# OpenMapUnifier .NET

C# implementation of the OpenMap_Unifier download engine: fetches German
OpenData geodata tiles and answers terrain queries — "what's the height at
this position?" — with zero external dependencies (pure .NET 8 BCL).

**All 16 Bundesländer are supported.** Every state's endpoints were verified
live (July 2026) and every state's DGM1 height query was tested end-to-end
against a real city coordinate:

| Code | State | CRS | DGM1 mechanism | License |
|---|---|---|---|---|
| by | Bayern | 25832 | static bayernwolke tile URLs | CC BY 4.0 |
| ni | Niedersachsen | 25832 | STAC API → S3 COGs | CC BY 4.0 |
| nw | Nordrhein-Westfalen | 25832 | XML index → GeoTIFF (per-tile year) | DL-DE Zero 2.0 |
| he | Hessen | 25832 | REST walk → remote-zip extraction | DL-DE Zero 2.0 |
| bw | Baden-Württemberg | 25832 | 2 km zips (odd-E/even-N cells), XYZ | DL-DE/BY 2.0 |
| rp | Rheinland-Pfalz | 25832 | ATOM index → GeoTIFF (per-tile year) | DL-DE/BY 2.0 |
| sl | Saarland | 25832 | Nextcloud share → remote-zip extraction | DL-DE/BY 2.0 |
| th | Thüringen | 25832 | static zips (epoch fallback as mirror) | DL-DE/BY 2.0 |
| sh | Schleswig-Holstein | 25832 | overview.php GeoJSON → massen.php, XYZ | CC BY 4.0 |
| hh | Hamburg | 25832 | CKAN → remote-zip extraction | DL-DE/BY 2.0 |
| hb | Bremen | 25832 | static city archives → remote-zip, XYZ | CC BY 4.0 |
| st | Sachsen-Anhalt | 25832 | page-scraped grid + 2-step prepare API | DL-DE/BY 2.0 |
| be | Berlin | **25833** | static 2 km zips, XYZ | DL-DE Zero 2.0 |
| bb | Brandenburg | **25833** | static 1 km zips, GeoTIFF | DL-DE/BY 2.0 |
| sn | Sachsen | **25833** | Nextcloud share, deterministic names | DL-DE/BY 2.0 |
| mv | Mecklenburg-Vorpommern | **25833** | ATOM download URLs, GeoTIFF | CC BY 4.0 |

Zone-33 states take planar coordinates in EPSG:25833; lat/lon input works for
every state because each provider carries its own transform. Where a state
only publishes multi-GB archives (HH/HB/SL/HE), single 1 km tiles are
range-extracted remotely — no full downloads.

**Detailed guides** live in [`docs/`](docs/): [conversions](docs/conversions.md)
(every CRS, axis-order conventions, the math, accuracy),
[chaotic-JSON import](docs/json-import.md), [proxy/TLS setup](docs/proxy.md),
and [architecture](docs/architecture.md) (interfaces, per-state mechanisms,
extension recipes).

## Layout

```
src/OpenMapUnifier.Core      Generic framework: geodesy (zones 32/33, GK, Web Mercator...),
                             km-grid math, polygon selection, downloader, remote-zip
                             reader, proxy manager, GeoTIFF/XYZ readers, elevation,
                             chaotic-JSON import
src/OpenMapUnifier.Germany   ALL 16 states behind one IGermanState interface
                             (GermanStates.Get("by"), .Get("ni"), ... — Bayern/
                             Niedersachsen extras live in the Bayern/ and
                             Niedersachsen/ subnamespaces: WMS renders, metalink,
                             STAC client)
src/OpenMapUnifier.Cli       `openmap` command-line demo
tests/OpenMapUnifier.Tests   xUnit suite (offline; no network needed)
```

`Core` knows nothing about any agency; everything state-specific sits behind
`IGermanState` in one package. Each seam is an interface, so pieces can be
swapped independently:

| Interface | Role | Default |
|---|---|---|
| `IGermanState` | one state: datasets, jobs-for-area, elevation factory | 16 classes, registry `GermanStates` |
| `ICoordinateTransform` | lat/lon ↔ planar CRS | `Etrs89UtmTransform.Zone32/33` (Karney/Krüger, sub-mm) |
| `IDownloader` | fetching files | `HttpTileDownloader` (mirrors, retries, atomic writes) |
| `IHeightTileResolver` (+`ITileFetcher`) | position → tile → parsed grid | grid math / index / API / remote-zip per state |
| `IElevationProvider` | height queries | `TiledElevationProvider` (on-demand download + LRU) |

## Coordinate conversions — every CRS German geodata comes in

`OpenMapUnifier.Core.Geodesy` covers the full zoo (all verified against
pyproj; the projection engine is the ellipsoid-generic Karney/Krüger series):

| EPSG | CRS | Class |
|---|---|---|
| 4326 | WGS 84 lat/lon (+ DMS strings) | `GeoPoint`, `Dms` |
| 25832 / 25833 | ETRS89 / UTM 32N & 33N | `Etrs89UtmTransform` |
| 32632 / 32633 | WGS 84 / UTM 32N & 33N | `Wgs84UtmTransform` |
| 4647 / 5650 | zone-prefixed UTM ("zE-N", 32xxxxxx eastings) | `ZonePrefixedUtmTransform` |
| 3857 | Web Mercator (web map exports) | `WebMercatorTransform` |
| 31466–31469 | Gauß-Krüger 2–5 (legacy DHDN/Bessel, incl. the 7-param Helmert datum shift) | `GaussKruegerTransform` |

Building blocks are public too: `TransverseMercator` (any ellipsoid),
`HelmertTransform` (7-parameter position-vector + geodetic↔ECEF),
`Ellipsoid` (GRS80/WGS84/Bessel), `CrsRegistry.Convert(x, y, fromEpsg, toEpsg)`
for any-to-any conversion.

**"I have no clue what CRS these numbers are":**

```bash
openmap detect 4468517.5 5333330.5
# -> EPSG:31468 (Gauß-Krüger zone 4) [95%] ... plus the position in EVERY known CRS
openmap convert 4468517.54 5333330.45 --from 31468 --to 25832
```

`CoordinateDetector.Detect(a, b)` returns ranked interpretations (both axis
orders) scored by whether the result lands in Germany. Genuinely ambiguous
inputs (a bare UTM easting fits zone 32 AND 33) return multiple candidates —
check the ranked list instead of trusting one blindly.

**Chaotic JSON import** — recover coordinates from any messy JSON, whatever
the format mix:

```bash
openmap import-json flightplan.json --to 25832 --out normalized.geojson
```

`ChaoticJsonImporter` walks the whole document and recognizes named pairs in
any spelling (lat/lon/lng/breite/laenge, x/y, easting/northing,
Rechtswert/Hochwert, ostwert/nordwert, utm_x/utm_y — numbers or numeric
strings, dot or comma decimals), GeoJSON-style arrays, DMS strings
("48°08'14\"N 11°34'32\"E"), coordinate pairs inside prose, and z/alt/hoehe
elevations. Each find reports its JSON path, the detected CRS and a
confidence — low-confidence rows are flagged for manual review. Output is a
normalized GeoJSON with full provenance per point.

## Corporate proxy support

`OpenMapUnifier.Core.Proxy.ProxyManager` ports the Python Unifier's proxy
manager with the same hard-won specifics:

- Auto-detect from `HTTPS_PROXY`/`HTTP_PROXY` (upper- and lowercase); manual
  configuration wins over detection and is never wiped by a failed detect.
- Credentials ride on the proxy CONNECT tunnel (`NetworkCredential`), never as
  a session `Proxy-Authorization` header — that header is useless for HTTPS
  and leaks to the destination server. Special characters in passwords can't
  break anything because nothing is URL-embedded (.NET side-steps the
  percent-encoding bugs that caused most 407s in Python).
- With an explicit manual proxy, environment variables are ignored
  (`trust_env=False` semantics) so stale env values can't override configured
  credentials; without one, the environment stays active as the fallback.
- NTLM (Windows domain) auth is supported natively — no extra package.
- TLS: custom CA bundle (`.pem`) for TLS-inspecting proxies takes precedence
  over the verify toggle; verification can be disabled for dev.
- Failures are classified like the Python Unifier: `PROXY_AUTH`, `SSL`,
  `PROXY`, `TIMEOUT`, `DNS`, `HTTP`, `OTHER` — and `openmap proxy-test`
  prints a credential-masked diagnostic snapshot plus per-data-source
  connectivity results (corporate proxies often ACL per URL).
- Config persists to `proxy_config.json` — the password is never written.

```csharp
var proxy = new ProxyManager();
proxy.SetManualProxy("proxy.company.com:8080", ProxyAuthType.Basic, "user", "p@ss!");
proxy.SetSsl(sslVerify: true, caBundlePath: @"C:\corp\rootca.pem");
using var downloader = new HttpTileDownloader(new DownloaderOptions { Proxy = proxy });
using var terrain = GermanStates.Get("ni").CreateElevationProvider("dgm1", "tilecache", downloader, proxy);
```

**Bring your own coordinate algorithms:** implement `ICoordinateTransform`
and pass it wherever a transform is accepted (`Polygon2D.FromWgs84Wkt`,
`GetElevationAsync(GeoPoint, transform)`, ...). Nothing else needs to change.

## Build & test

```bash
dotnet build
dotnet test        # 86 offline unit tests
```

## CLI quick tour

```bash
cd src/OpenMapUnifier.Cli

# What's available?
dotnet run -- datasets

# Coordinates (Marienplatz)
dotnet run -- convert --to-utm 48.137222 11.575556     # -> E=691607.86 N=5334760.39

# Terrain height (downloads + caches the covering DGM1 tile automatically)
dotnet run -- height --latlon 48.137222 11.575556
dotnet run -- height 729500.5 5433500.5 --dataset dgm5

# Any state: same commands, --state CODE (see table above)
dotnet run -- height --latlon 52.374 9.738 --state ni      # Hannover
dotnet run -- height --latlon 52.52 13.405 --state be      # Berlin (zone 33)
dotnet run -- height --latlon 50.9375 6.9603 --state nw    # Köln
dotnet run -- height --latlon 53.5503 9.992 --state hh     # Hamburg (tile extracted
                                                           #  from the 1.4 GB city zip)
dotnet run -- datasets --state sn                          # per-state products + license
dotnet run -- tiles dop20rgbi --state ni --bbox 550000,5802000,552000,5804000

# Proxy diagnostics (auto-detects HTTPS_PROXY/HTTP_PROXY when no --proxy given)
dotnet run -- proxy-test --proxy proxy.corp:8080 --proxy-user u --proxy-pass p

# Elevation profile along a line
dotnet run -- profile 729000 5433000 730000 5434000 --samples 20

# Which tiles would a download touch, and how big is it?
dotnet run -- tiles dop20 --bbox 691000,5334000,693000,5335000

# Batch download (mirror fallback, skip-existing, .tfw/.prj sidecars)
dotnet run -- download dgm1 --bbox 691000,5334000,693000,5335000 --out downloads --sidecars
```

## Library usage

```csharp
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Geometry;
using OpenMapUnifier.Germany;

// --- One registry for all 16 states -----------------------------------------
var bayern = GermanStates.Get("by");
var berlin = GermanStates.Get("be");   // zone-33 state — Transform handles it

// --- Height at a position (tiles fetched + cached on demand) ---------------
using var terrain = bayern.CreateElevationProvider("dgm1", "tilecache");
double? h = await terrain.GetElevationAsync(new UtmPoint(729500.5, 5433500.5));
double? h2 = await terrain.GetElevationAsync(new GeoPoint(48.137222, 11.575556));

using var beTerrain = berlin.CreateElevationProvider("dgm1", "tilecache");
double? h3 = await beTerrain.GetElevationAsync(new GeoPoint(52.52, 13.405));

// Handy derived queries
var profile = await terrain.GetProfileAsync(a, b, samples: 200);
var slopeAspect = await terrain.GetSlopeAspectAsync(p);

// Object height above ground (nDSM) = surface minus terrain
using var surface = bayern.CreateElevationProvider("dom20", "tilecache");
double? ndsm = await surface.GetElevationAsync(p) - await terrain.GetElevationAsync(p);

// --- Bulk download ----------------------------------------------------------
var area = BoundingBox.Around(new UtmPoint(691_607, 5_334_760), radiusMeters: 1500);
var jobs = await bayern.JobsForAsync("dop20", area);
using var downloader = new HttpTileDownloader(new DownloaderOptions { MaxParallel = 6 });
var results = await downloader.DownloadAllAsync(jobs, "downloads");

// Bayern extras (WMS renders, metalink, polygon selection) live on:
using OpenMapUnifier.Germany.Bayern;   // BayernTileSource, BayernWmsSource, MetalinkParser
var polygon = Polygon2D.FromKml(File.ReadAllText("region.kml"));
var polyJobs = new BayernTileSource().JobsFor("dgm1", polygon);
```

## Verified against real data

- Coordinate transforms match pyproj for BOTH zones (EPSG:4326→25832 and
  →25833) to sub-millimeter.
- Height sampling matches numpy/tifffile readings of live DGM1 tiles
  bit-for-bit (Bayern strip-LZW, Niedersachsen tiled COG, Saarland
  stored-in-zip, Brandenburg/RLP LZW+floating predictor-2 lanes).
- All 16 states' DGM1 height queries were run live against a city coordinate
  during development (Köln 46.5 m, Berlin 35.6 m, Dresden 113.3 m, Hamburg
  5.6 m, Stuttgart 249.6 m, ...); tile naming and URL shapes are pinned by
  offline tests.

Per-state quirks worth knowing (all handled internally):

- **NoData varies**: -9999 in most states, float32 −MAX in Hamburg, absent in
  MV — always read from the tile's GDAL tag, never assumed.
- **Grid anchoring varies**: BW's 2 km cells sit at odd-E/even-N km; LoD2
  grids are 2 km in TH/RP/ST/SN/MV but 1 km in NRW/Berlin/SL/HH.
- **Hessen has no spatial index** — first height query in a new region scans
  municipality archives' central directories until the tile is found (can take
  minutes); results are cached on disk afterwards.
- **Sachsen-Anhalt** URLs are one-time (a "prepare" API mints them); tile ids
  are scraped from the product pages' embedded GeoJSON grid.
- Berlin/Bremen/SH/BW serve DGM1 as ASCII-XYZ (parsed by the XYZ reader);
  everything else is GeoTIFF.

Not ported (yet): DOM-Mesh SLPK range-fetch cutout, Sentinel-2/WorldCover
sources, OSM/Overpass, the GUI/web app.

Data: © the respective state survey agencies (LDBV, LGLN, Geobasis NRW, HVBG,
LGL-BW, LVermGeoRP, LVGL-SL, GDI-Th, LVermGeo SH, LGV Hamburg, Landesamt
GeoInformation Bremen, LVermGeo LSA, Geoportal Berlin, LGB, GeoSN, LAiV M-V) —
licenses per state in the table above. Attribution strings are exposed as
`Attribution` on every catalog/state.
