# Chaotic JSON import

`ChaoticJsonImporter` (namespace `OpenMapUnifier.Import`) recovers
coordinates from JSON files whose structure and coordinate formats are
inconsistent — mixed CRS, mixed spellings, numbers as strings, German decimal
commas, coordinates buried in free text. You point it at the document; it
returns every coordinate it can find, each with provenance and a confidence.

```bash
openmap import-json flight.json --to 25832 --out normalized.geojson
```

```csharp
using OpenMapUnifier.Import;

var found = ChaoticJsonImporter.ScanFile("flight.json");
foreach (var f in found)
{
    var (x, y) = f.In(25832);             // converted to the pipeline CRS
    Console.WriteLine($"{f.Path}: {x:F2} {y:F2} " +
        $"(was EPSG:{f.Guess.Epsg}, {f.Guess.Confidence:P0}, {f.Guess.Reason})");
}
NormalizedGeoJson.WriteFile("normalized.geojson", found, targetEpsg: 25832);
```

## What gets recognized

The importer walks the ENTIRE document (any nesting) and matches four shapes:

**1. Named pairs in objects** — field names are case-insensitive; values may
be JSON numbers or numeric strings (dot or single-comma decimals):

| Role | Built-in names |
|---|---|
| latitude | `lat`, `latitude`, `breite`, `phi` |
| longitude | `lon`, `lng`, `long`, `longitude`, `laenge`, `länge`, `lambda` |
| planar X | `x`, `e`, `east`, `easting`, `rechtswert`, `rw`, `ostwert`, `utm_x`, `utm_e` |
| planar Y | `y`, `n`, `north`, `northing`, `hochwert`, `hw`, `nordwert`, `utm_y`, `utm_n` |
| elevation | `z`, `alt`, `altitude`, `height`, `hoehe`, `höhe`, `ele`, `elevation` |

Extra names can be added via `ImportOptions.LatitudeKeys` etc. A lat/lon pair
is taken as EPSG:4326 directly (confidence 0.95); an out-of-range "lat/lon"
falls through to detection (usually swapped axes or actually planar). A
planar X/Y pair runs through `CoordinateDetector`.

**2. Bare numeric arrays** of 2–3 elements — `[11.5756, 48.1372]`,
`[691607.9, 5334760.4, 521.3]`. GeoJSON's lon-lat order and every planar CRS
are tried; the third element becomes Z.

**3. Coordinate strings** — DMS in most spellings (`48°08'14"N 11°34'32"E`,
`N48 8.233 E11 34.533`, `48 08 14 N, 11 34 32 E`) and plain number pairs
inside strings, including inside prose (`"office at 52.52, 13.405"`).

**4. Z values** ride along from sibling fields or third elements.

## How detection decides (and when to distrust it)

Planar pairs are interpreted in every plausible CRS (UTM 32/33,
zone-prefixed, Gauß-Krüger 2–5, Web Mercator, both axis orders) and each
interpretation is scored by whether the resulting position lands inside the
expected region (Germany by default). What you should know:

- **Confidence ≥ 0.9**: explicit naming or an unambiguous number shape.
- **Ambiguity is surfaced, not hidden**: a bare UTM easting can be zone 32 OR
  33; the best guess wins, but if your points end up ~400 km sideways, the
  other zone was the right one. Fix: `--assume-epsg`.
- **Junk filtering**: unnamed arrays/strings below `MinConfidence` (default
  0.6) are dropped — that's what keeps `[1, 2]` and `{"x":1920,"y":1080}`
  out of your results. Named planar pairs use the lower `MinNamedConfidence`
  (0.5) since the field names vouch for them.
- Rows the CLI marks `LOW CONFIDENCE` deserve a manual look.

## Options (`ImportOptions`)

```csharp
var options = new ImportOptions
{
    AssumeEpsg = 31468,        // "my x/y are ALWAYS GK zone 4" — skips guessing
    MinConfidence = 0.7,       // stricter junk filter for unnamed finds
    Region = DetectionRegion.CentralEurope, // data not strictly inside Germany
};
options.XKeys.Add("east_m");   // exporter-specific field names
options.YKeys.Add("north_m");
var found = ChaoticJsonImporter.Scan(json, options);
```

CLI: `--assume-epsg 31468`, `--min-confidence 0.7`,
`--region germany|central-europe`.

`AssumeEpsg` applies to planar/unnamed pairs only; fields explicitly named
lat/lon stay geographic, and values numerically impossible for the assumed
CRS fall back to auto-detection instead of producing garbage.

## Output

- **In memory**: `FoundCoordinate` records — JSON path (`$.flug[0]`), the raw
  matched text, the winning `CoordinateGuess` (EPSG, name, confidence,
  reason, axes-swapped flag), optional Z, `Geo` (WGS84) and `In(epsg)` for
  any registered CRS.
- **On disk**: `NormalizedGeoJson.Write[File]` — a GeoJSON FeatureCollection
  (Point geometry in WGS84 per spec) with per-feature provenance properties
  (`sourcePath`, `sourceRaw`, `detectedEpsg`, `detectedCrs`, `confidence`,
  `reason`) plus the coordinate pre-converted to your target CRS
  (`epsg25832_x/y`). Loads directly in QGIS/Blender-GIS for eyeballing.

## Worked example

Input (six formats, same place, plus junk):

```json
{
  "camera": { "lat": 48.137222, "lon": 11.575556, "alt": 520.0 },
  "gk": { "Rechtswert": 4468517.54, "Hochwert": 5333330.45, "hoehe": "521,3" },
  "utm_str": { "x": "691607,86", "y": "5334760,39" },
  "marker": [11.575556, 48.137222],
  "poi": "48°08'14.0\"N 11°34'32.0\"E",
  "prefixed": { "ostwert": 32691607.9, "nordwert": 5334760.4 },
  "junk": [1, 2],
  "resolution": { "x": 1920, "y": 1080 }
}
```

`openmap import-json chaos.json --to 25832` recovers six coordinates — all
converging on 691607.86 / 5334760.39 — labels each with its source CRS
(4326, 31468, 25832, 4326 swapped-order, 4326 DMS, 4647), and ignores the
junk and the screen resolution.
