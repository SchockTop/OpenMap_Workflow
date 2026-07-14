# OpenMapUnifier .NET

C# implementation of the OpenMap_Unifier download engine: fetches German
OpenData geodata tiles and answers terrain queries — "what's the height at
this position?" — with zero external dependencies (pure .NET 8 BCL).

Supported states:

- **Bayern (LDBV)**: DGM1/DGM5 terrain, DOM20 surface, DOP20/40 orthophotos,
  LoD2 buildings, LiDAR, WMS renders — static bayernwolke tile URLs.
- **Niedersachsen (LGLN OpenGeoData)**: DGM1 terrain, DOM1 surface, DOP
  RGB/RGBI orthophotos — resolved through LGLN's STAC APIs
  (`dgm|dom|dop.stac.lgln.niedersachsen.de`), because the S3 download URLs
  embed acquisition batch and flight date and cannot be computed from the
  grid. Where a tile was flown multiple times, the newest epoch wins.

All coordinates are EPSG:25832 (ETRS89 / UTM zone 32N), meters — the same
convention as the rest of OpenMap_Workflow (both states publish in it).

## Layout

```
src/OpenMapUnifier.Core           Generic framework: geodesy, km-grid math, polygon
                                  selection, downloader, proxy manager, GeoTIFF/XYZ
                                  readers, elevation
src/OpenMapUnifier.Bayern         Bayern LDBV: catalog, tile naming, WMS, metalink
src/OpenMapUnifier.Niedersachsen  LGLN OpenGeoData: STAC client, catalog, resolvers
src/OpenMapUnifier.Cli            `openmap` command-line demo
tests/OpenMapUnifier.Tests        xUnit suite (offline; no network needed)
```

State-specific code lives in its own package; `Core` knows nothing about
either agency. Each seam is an interface, so pieces can be swapped
independently:

| Interface | Role | Default |
|---|---|---|
| `ICoordinateTransform` | lat/lon ↔ UTM32 | `Etrs89Utm32Transform` (Karney/Krüger series, sub-mm) |
| `IDownloader` | fetching files | `HttpTileDownloader` (mirrors, retries, atomic writes) |
| `IHeightTileResolver` | position → tile → parsed grid | Bayern: grid math; Niedersachsen: STAC lookup |
| `IElevationProvider` | height queries | `TiledElevationProvider` (on-demand download + LRU) |
| `IDatasetCatalog` | dataset metadata | `BayernCatalog` / `NiedersachsenCatalog` |

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
using var terrain = NiedersachsenElevation.CreateDgm1Provider("tilecache", downloader, proxy);
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

# Niedersachsen: same commands, --state ni (Hannover)
dotnet run -- height --latlon 52.374 9.738 --state ni
dotnet run -- height 550500.5 5802500.5 --state ni --dataset dom1
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
using OpenMapUnifier.Bayern;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Geometry;

// --- Height at a position (tiles fetched + cached on demand) ---------------
using var terrain = BayernElevation.CreateDgm1Provider("tilecache");
double? h = await terrain.GetElevationAsync(new Utm32Point(729500.5, 5433500.5));
double? h2 = await terrain.GetElevationAsync(new GeoPoint(48.137222, 11.575556));

// Niedersachsen: identical API, tiles resolved via LGLN's STAC catalog
using var niTerrain = NiedersachsenElevation.CreateDgm1Provider("tilecache");
double? h3 = await niTerrain.GetElevationAsync(new GeoPoint(52.374, 9.738));

// Handy derived queries
var profile = await terrain.GetProfileAsync(a, b, samples: 200);
var slopeAspect = await terrain.GetSlopeAspectAsync(p);

// Object height above ground (nDSM) = surface minus terrain
using var surface = BayernElevation.CreateDom20Provider("tilecache");
double? ndsm = await surface.GetElevationAsync(p) - await terrain.GetElevationAsync(p);

// --- Bulk download ----------------------------------------------------------
var source = new BayernTileSource();
var area = BoundingBox.Around(new Utm32Point(691_607, 5_334_760), radiusMeters: 1500);
using var downloader = new HttpTileDownloader(new DownloaderOptions { MaxParallel = 6 });
var results = await downloader.DownloadAllAsync(source.JobsFor("dop20", area), "downloads");

// Region from a Google Earth KML polygon instead of a box
var polygon = Polygon2D.FromKml(File.ReadAllText("region.kml"));
var jobs = source.JobsFor("dgm1", polygon);
```

## Verified against real data

- Coordinate transform matches pyproj (EPSG:4326→25832) to sub-millimeter.
- Height sampling matches numpy/tifffile readings of live DGM1 tiles from
  BOTH states bit-for-bit (Bayern: strip-LZW GeoTIFF; Niedersachsen:
  tile-organized COG — same reader, different layout paths).
- Bayern DGM1 (GeoTIFF) and DGM5 (zipped XYZ) agree within centimeters at the
  same position.
- Tile naming/URL shapes are pinned by tests to patterns verified live
  (Bayern: `backend/downloader.py`; Niedersachsen: STAC responses from
  July 2026).

Niedersachsen notes:

- Elevation COGs are float32 LZW with NoData `-9999`, like Bayern.
- LGLN's `DOP` collection mixes 20 cm and newer 10 cm epochs under the same
  asset keys; "newest flight per tile" therefore returns 10 cm imagery in
  reflown regions (see the STAC `bodenpixelgroesse` property per item).
- No LoD2/DGM5 equivalents are exposed through STAC; LoD2 for Niedersachsen is
  distributed per municipality, not on the km grid (not ported).

Not ported (yet): DOM-Mesh SLPK range-fetch cutout, Sentinel-2/WorldCover
sources, OSM/Overpass, the GUI/web app.

Data: Bayerische Vermessungsverwaltung — CC BY 4.0 — www.geodaten.bayern.de
Data: LGLN Niedersachsen, OpenGeoData.NI — CC BY 4.0 — opengeodata.lgln.niedersachsen.de
