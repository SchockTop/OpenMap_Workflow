# OpenMapUnifier .NET

C# implementation of the OpenMap_Unifier download engine: fetches Bayern
OpenData tiles (DGM1/DGM5 terrain, DOM20 surface, DOP20/40 orthophotos, LoD2
buildings, LiDAR) and answers terrain queries — "what's the height at this
position?" — with zero external dependencies (pure .NET 8 BCL).

All coordinates are EPSG:25832 (ETRS89 / UTM zone 32N), meters — the same
convention as the rest of OpenMap_Workflow.

## Layout

```
src/OpenMapUnifier.Core      Generic framework: geodesy, km-grid math, polygon
                             selection, downloader, GeoTIFF/XYZ readers, elevation
src/OpenMapUnifier.Bayern    Bayern LDBV specifics: dataset catalog, tile naming,
                             WMS renders, metalink parsing, elevation resolvers
src/OpenMapUnifier.Cli       `openmap` command-line demo
tests/OpenMapUnifier.Tests   xUnit suite (offline; no network needed)
```

Everything Bavaria-specific lives in `OpenMapUnifier.Bayern`; `Core` knows
nothing about LDBV. Each seam is an interface, so pieces can be swapped
independently:

| Interface | Role | Default |
|---|---|---|
| `ICoordinateTransform` | lat/lon ↔ UTM32 | `Etrs89Utm32Transform` (Karney/Krüger series, sub-mm) |
| `IDownloader` | fetching files | `HttpTileDownloader` (mirrors, retries, atomic writes) |
| `IHeightTileResolver` | position → tile → parsed grid | DGM1/DGM5/DOM20 resolvers in `BayernElevation` |
| `IElevationProvider` | height queries | `TiledElevationProvider` (on-demand download + LRU) |
| `IDatasetCatalog` | dataset metadata | `BayernCatalog` |

**Bring your own coordinate algorithms:** implement `ICoordinateTransform`
and pass it wherever a transform is accepted (`Polygon2D.FromWgs84Wkt`,
`GetElevationAsync(GeoPoint, transform)`, ...). Nothing else needs to change.

## Build & test

```bash
dotnet build
dotnet test        # 64 offline unit tests
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
- Height sampling matches numpy/tifffile readings of a live DGM1 tile
  bit-for-bit (LZW GeoTIFF decode + bilinear interpolation).
- DGM1 (GeoTIFF) and DGM5 (zipped XYZ) agree within centimeters at the same
  position.
- Tile naming/URL shapes are pinned by tests to the patterns verified live in
  the Python Unifier (`backend/downloader.py`).

Not ported (yet): DOM-Mesh SLPK range-fetch cutout, Sentinel-2/WorldCover
sources, OSM/Overpass, the GUI/web app.

Data: Bayerische Vermessungsverwaltung — CC BY 4.0 — www.geodaten.bayern.de
