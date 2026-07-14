# Coordinate conversions

Everything lives in `OpenMapUnifier.Core.Geodesy`, has zero dependencies, and
is verified against pyproj. This document explains what each CRS is, when you
meet it in German geodata, the conventions that cause 90 % of conversion bugs,
and the math underneath.

## The two conventions that break everyone's conversions

**1. Axis order.** There is no universal rule:

| Context | Order |
|---|---|
| Spoken/written coordinates, GPS, Google Maps | **lat, lon** |
| GeoJSON, WKT `POLYGON(...)`, most APIs with `always_xy` | **lon, lat** |
| Projected CRS (UTM, GK) | **easting (x), northing (y)** |
| German surveying tradition (Gauß-Krüger) | "Rechtswert" = easting, "Hochwert" = northing |

This framework: `GeoPoint(Latitude, Longitude)` (named fields, no ambiguity),
`UtmPoint(Easting, Northing)`, and `CrsRegistry`/CLI treat EPSG:4326 planar
(x, y) as **(lon, lat)** — the GIS `always_xy` convention. If your numbers
come out mirrored across Germany, your axes are swapped; run
`openmap detect <a> <b>` — it tests both orders and tells you.

**2. Datum vs projection.** "UTM zone 32" alone doesn't identify a CRS: the
same formulas on different datums give coordinates that differ by ~0.5–120 m.
EPSG:25832 (ETRS89) and EPSG:32632 (WGS84) agree to sub-meter (fine for this
pipeline); EPSG:31468 (DHDN, the legacy German datum) is **hundreds of meters
away** from UTM values. Never guess the datum from the numbers looking
"UTM-ish" — that's what `detect` is for.

## Supported CRS

| EPSG | Name | Where you meet it | Class |
|---|---|---|---|
| 4326 | WGS 84 geographic | GPS, KML, GeoJSON, all lat/lon | `GeoPoint`, `Dms` |
| 25832 | ETRS89 / UTM 32N | **the pipeline CRS**; most German states' tiles | `Etrs89UtmTransform.Zone32` |
| 25833 | ETRS89 / UTM 33N | Berlin, Brandenburg, Sachsen, MV | `Etrs89UtmTransform.Zone33` |
| 32632/3 | WGS 84 / UTM 32N/33N | GPS devices, international tools | `Wgs84UtmTransform` |
| 4647 | ETRS89 / UTM 32N (zE-N) | 8-digit eastings `32691607…`; Sachsen-Anhalt tile labels, INSPIRE | `ZonePrefixedUtmTransform.Zone32` |
| 5650 | ETRS89 / UTM 33N (zE-N) | same, zone 33 | `ZonePrefixedUtmTransform.Zone33` |
| 3857 | Web Mercator | anything exported from web maps / OSM tiles | `WebMercatorTransform` |
| 31466–69 | DHDN / Gauß-Krüger 2–5 | legacy datasets, old CAD/survey files; 7-digit "Rechtswert" starting 2–5 | `GaussKruegerTransform` |

### Recognizing a CRS from raw numbers (German data)

```
48.137222, 11.575556          → lat/lon degrees
691607.86, 5334760.39         → UTM (zone 32 or 33 — ambiguous! see below)
32691607.86, 5334760.39       → zone-prefixed UTM (EPSG:4647)
4468517.54, 5333330.45        → Gauß-Krüger (leading digit 4 = zone 4, EPSG:31468)
1288585.0, 6129714.1          → Web Mercator
```

A bare 6-digit easting cannot distinguish zone 32 from 33 — both may land in
Germany. `CoordinateDetector` returns **both** candidates; pick by knowing
which state the data comes from (east: BE/BB/SN/MV → 25833).

## Usage

```csharp
using OpenMapUnifier.Core.Geodesy;

// Simple: lat/lon <-> pipeline CRS
var utm = Etrs89UtmTransform.Zone32.ToUtm(new GeoPoint(48.137222, 11.575556));
var geo = Etrs89UtmTransform.Zone32.ToGeo(utm);

// Any-to-any via the registry (x/y in the source CRS)
var (x, y) = CrsRegistry.Convert(4468517.54, 5333330.45, fromEpsg: 31468, toEpsg: 25832);

// What is this pair? Ranked interpretations.
foreach (var guess in CoordinateDetector.Detect(4468517.54, 5333330.45))
    Console.WriteLine(guess); // EPSG:31468 (…GK zone 4…) [95%] …

// DMS strings
GeoPoint? p = Dms.ParsePair("48°08'14\"N 11°34'32\"E");
string s = Dms.Format(new GeoPoint(48.137222, 11.575556));

// Bring your own algorithm: implement ICoordinateTransform and pass it
// anywhere a transform is accepted (Polygon2D.FromKml, elevation extensions…).
```

CLI equivalents: `openmap convert <x> <y> --from <epsg> --to <epsg>`,
`openmap detect <a> <b>` (prints the position in every known CRS).

## The math (for debugging / porting)

- **Projection engine** — `TransverseMercator`: Karney/Krüger 6th-order series
  in the transverse Mercator mapping, parameterized by ellipsoid, central
  meridian, scale, false easting/northing. This is the same method proj uses;
  truncation error is sub-millimeter anywhere in a 6° zone. UTM = GRS80/WGS84
  ellipsoid, k0 = 0.9996, FE = 500 km. Gauß-Krüger = Bessel 1841, k0 = 1,
  3° strips, FE = zone·1,000,000 + 500,000.
- **Datum shifts** — `HelmertTransform`: 7-parameter similarity transform in
  the EPSG *position vector* convention (watch out: the *coordinate frame*
  convention flips the rotation signs!) applied in earth-centered cartesian
  (ECEF) space, plus geodetic↔ECEF conversions on each side. DHDN→WGS84 ships
  as `HelmertTransform.DhdnToWgs84` (EPSG:1777: 598.1, 73.7, 418.2 m /
  0.202″, 0.045″, −2.455″ / 6.7 ppm).
- **Web Mercator** — spherical formulas on radius 6378137 m per the EPSG:3857
  definition. Fine for positions; never measure distances in it.

## Accuracy

| Conversion | Error vs pyproj |
|---|---|
| ETRS89/WGS84 UTM, zE-N, Web Mercator | sub-millimeter |
| Gauß-Krüger (most of Germany) | ≤ 2 mm |
| Gauß-Krüger (Berlin/east edge cases) | ≤ 1 m |

The GK caveat: EPSG defines *regional* DHDN Helmert variants; pyproj picks
one per location (or the BeTA2007 grid when installed) while this framework
always uses EPSG:1777. The differences stay inside the ~1 m accuracy class of
the legacy datum itself — if you need better, your source data shouldn't be
in DHDN in the first place.

ETRS89 vs WGS84: the datums drift apart ~2.5 cm/year (plate motion), ~0.8 m
by 2026. Every German open-data product is ETRS89; treating GPS (WGS84)
coordinates as ETRS89 introduces that sub-meter offset, which is below the
tile resolution of everything this pipeline handles. If sub-decimeter
absolute georeferencing ever matters, add a time-dependent transformation.
