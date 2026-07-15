# Architecture

## Projects

```
OpenMapUnifier.Core      zero-dependency framework: geodesy, grid math, geometry,
                         downloading (incl. remote-zip), proxy, raster readers,
                         elevation engine, chaotic-JSON import
OpenMapUnifier.Germany   ALL 16 states behind IGermanState (registry: GermanStates).
                         Subnamespaces Germany.Bayern / Germany.Niedersachsen hold
                         those states' extra machinery (WMS renders, metalink
                         parsing, the STAC client) — usable directly when you need
                         more than the uniform interface.
OpenMapUnifier.Cli       `openmap` command-line front end
OpenMapUnifier.Tests     offline xUnit suite (no network)
```

Dependency rule: Germany depends on Core only; Core knows no agency.
(History note: Bayern and Niedersachsen started as separate packages before
the IGermanState registry existed; they were folded in once all 16 states
were implemented, so there is exactly ONE way to reach any state.)

## The seams (swap any of these independently)

| Interface | Contract | Implementations |
|---|---|---|
| `ICoordinateTransform` | geographic ↔ planar for ONE CRS | `Etrs89UtmTransform`, `Wgs84UtmTransform`, `ZonePrefixedUtmTransform`, `GaussKruegerTransform`, `WebMercatorTransform`, or your own |
| `IDownloader` | fetch a `DownloadJob` (mirror list) to a directory | `HttpTileDownloader` (retries, atomic writes, skip-existing, classified errors) |
| `IHeightTileResolver` | position → `TileId` → job → parsed `HeightGrid` | per-product resolvers in every state package |
| `ITileFetcher` | optional resolver upgrade: fetch the tile file yourself | `ArchiveTileResolver` (remote-zip states) |
| `IElevationProvider` | height at a position (+ its `Transform`) | `TiledElevationProvider` |
| `IGermanState` | datasets, jobs-for-area, elevation factory | 14 classes in `Germany.States` |

## Elevation query pipeline

```
GetElevationAsync(position)
  └─ resolver.TileFor(position)              km-grid snap (zone-aware, per-dataset grid)
  └─ cache lookup (in-memory LRU of parsed grids, then disk)
  └─ resolver is ITileFetcher?
       yes → FetchAsync: RemoteZipReader range-extracts the tile from a
             multi-GB archive (HH/HB/SL/HE)
       no  → JobForAsync (may hit an index/API to mint the URL)
             → IDownloader.DownloadAsync (mirror fallback, .part + atomic rename)
  └─ resolver.Parse → HeightGrid (GeoTIFF or XYZ reader; NoData from the file)
  └─ grid.Sample(position)                   bilinear, NoData-aware
```

Everything is cached: downloaded tiles on disk (skip-if-exists), parsed grids
in a small LRU, index files / STAC results / archive directories in memory
per provider instance.

## How each state distributes data (July 2026, all verified live)

| Mechanism | States | Key classes |
|---|---|---|
| deterministic tile URLs | BY, BB, BE, SN, MV, TH (epoch fallback via mirror list), BW (odd/even 2 km cells) | catalog + `TileGrid` |
| index-resolved URLs (per-tile year in filename) | NW (directory XML), RP (atomfeed) | `UrlIndex` |
| query APIs | NI (STAC), SH (overview.php GeoJSON), ST (page-scraped grid + 2-step prepare) | `StacClient`, per-state code |
| whole archives, tiles range-extracted | HH (CKAN-resolved), HB (static), SL (Nextcloud), HE (REST walk) | `RemoteZipReader`, `RemoteArchiveFetcher` |

`RemoteZipReader` reads a zip's central directory via HTTP range requests
(zip64 supported) and extracts single entries — a 1 km tile costs a few MB of
traffic even when the archive is 37 GB.

## Raster readers (pure C#)

- `GeoTiffReader`: classic TIFF, II/MM, strips + tiles, uncompressed / LZW /
  Deflate, predictor 1/2 (incl. the 32-bit-lane float variant Brandenburg and
  RLP use), float32/int samples, georeferencing from ModelTiepoint/PixelScale,
  NoData from the GDAL tag (varies per state — never assume −9999).
- `XyzGridReader`: ASCII XYZ (pixel centers), plain or zipped, entry filters
  for multi-tile zips.
- `HeightGrid`: bilinear sampling with NoData handling, bounds checks.

## Extension recipes

**Add a state/product**: implement `IHeightTileResolver` (or reuse
`DelegateTileResolver` / `ArchiveTileResolver`), add a catalog entry, register
in `GermanStates`. Pin the URL shape with an offline test; verify one live
height query.

**Add a CRS**: implement `ICoordinateTransform` (compose `TransverseMercator`
and/or `HelmertTransform` if it's a projected CRS on a shifted datum), add it
to `CrsRegistry.Known`, and — if raw numbers should be auto-recognized — a
range heuristic in `CoordinateDetector`.

**Consume from your own code**: reference `OpenMapUnifier.Core` plus the state
package(s) you need. Everything the CLI does is a thin wrapper over public
library APIs.

## Documentation map

- [conversions.md](conversions.md) — every CRS, the conventions, the math, accuracy
- [json-import.md](json-import.md) — chaotic-JSON importer guide
- [proxy.md](proxy.md) — corporate proxy/TLS setup and 407 debugging
- ../README.md — state support matrix, quick start, verification notes
