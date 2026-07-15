# MapScene — the map as a living object

`OpenMapUnifier.MapScene` turns downloaded geodata into something you can
*compute against and build tools on*: a scene with terrain, imagery and
classification layers, trajectories loaded from your flight logs, sensors
that look at the ground, and area definitions ("is trees", "within 5 m of
buildings", "seen by the sensor") that combine like sets.

It sits on top of the module chain (`Germany` → `MapScene` → your tools) and
follows the same rules: zero external packages, one concept per class,
everything an interface-sized building block.

## Design principles (the vision alignment)

1. **Independent but connectable.** Concepts are deliberately shaped like the
   ones in Cesium (scene / layers / entities / timeline) and Unity
   (components on a scene), so anyone coming from those tools feels at home —
   but nothing is imported. Interop happens through boring, universal files:
   GeoTIFF out (masks, terrain), GeoJSON/CSV out (trajectories, hits),
   CSV/JSON in (your logs). Every tool you'll ever connect reads those.
2. **One anchor, small floats.** A scene has a `SceneAnchor` (UTM origin);
   all scene math runs in local ENU meters (X=east, Y=north, Z=up,
   `System.Numerics.Vector3`). That is float32-safe for graphics pipelines —
   the same trick the Blender pipeline uses — and one method call away from
   exact UTM/lat-lon.
3. **Everything is "value at position".** Terrain, surface model,
   classification, masks — all samplers. Conditions are plain predicates
   (`Func<UtmPoint, bool>`), so "bendable" is literal: any C# expression
   becomes an area.
4. **Masks share the terrain grid.** All area definitions live on one aligned
   raster, so Union/Intersect/Subtract/Dilate are exact array operations —
   easy to read, impossible to misalign.
5. **Analyses are dumb sweeps of simple primitives.** Coverage = frames ×
   frustum rays × one ray-march. No spatial index cleverness until a real
   region proves too slow; clarity first.

## The pieces

| Class | One-liner |
|---|---|
| `Map` | front door: load a region (terrain + optional surface model + classification), get all tools pre-wired |
| `SceneAnchor` | UTM origin ↔ float32-safe local ENU frame |
| `TerrainLayer` | merged height grid of the region (from providers or files); `HeightAt` |
| `RasterLayer` | classification raster; `ClassAt` (nearest-neighbor, never interpolated) |
| `Trajectory` / `TrajectorySample` | time-stamped poses, interpolation, fixed-rate `Frames()` |
| `TabularMapping` + `TrajectoryLoader` | CSV/JSON → trajectory with user-defined column mapping; `SuggestMapping` proposes one |
| `SensorModel` family | `PyramidSensor` (camera frustum), `ConeSensor` (circular FOV), `CylinderSensor` (fixed ground radius); mount angles on the body, boresight + ray bundle per pose |
| `LineOfSight` | ray-march the terrain: `HitGround`, `CanSee`, `BoresightGroundPoint` |
| `GroundMask` | areas on the ground: by polygon, by condition, by class, by object height (nDSM); set algebra + `Dilate` |
| `CoverageAnalyzer` | ground actually seen by a sensor along the whole flight (occlusion-aware) |
| `PointSet` + `UnityPointsExport` | points you set (markers, trajectory, boresight track) → JSON render targets for Unity |
| `UnitySceneExport` | whole scene as a bundle folder for the interactive Unity viewer (unity/OpenMapViewer) |
| `SceneDocument` + `SceneRunner` | the whole session as one JSON file, executed by `openmap scene` |
| `GeoTiffWriter` (in Raster) | write masks/terrain as georeferenced GeoTIFF for QGIS/Blender |

## A complete session

```csharp
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.MapScene;
using OpenMapUnifier.MapScene.Tabular;

// 1. Map: 4x4 km around Munich, terrain + surface model, cached locally.
var region = BoundingBox.Around(new UtmPoint(691_600, 5_334_800), radiusMeters: 2000);
var map = await Map.LoadAsync("by", region, "tilecache", resolution: 1.0,
    withSurfaceModel: true);

// 2. Trajectory: your CSV, your column names.
var mapping = TrajectoryLoader.SuggestMapping("flight.csv"); // review it!
mapping.Fields["speed"] = FieldRole.Extra;
var flight = TrajectoryLoader.LoadCsv("flight.csv", mapping, map.Anchor);

// 3. Sensor & per-frame looking point.
var camera = new PyramidSensor("nadir", FovHorizontalDeg: 60, FovVerticalDeg: 45,
    MountPitchDeg: -90);
foreach (var pose in flight.Frames(1.0))
{
    var looking = map.LineOfSight.BoresightGroundPoint(camera, pose);
    // -> where the camera looks this frame (null: above horizon / off-map)
}

// 4. Areas on the ground, combined like sets.
var trees = map.ObjectsTallerThan(3);            // nDSM: surface - terrain
var nearTrees = trees.Dilate(5);                 // within 5 m of a tree
var seen = map.Coverage.SeenGround(flight, camera, frameStepSeconds: 0.5);
var seenOpenGround = seen.Subtract(nearTrees);   // seen AND not near trees
Console.WriteLine($"covered: {seenOpenGround.AreaSquareMeters() / 1e6:F2} km²");

// 5. Hand off to any GIS/3D tool.
seenOpenGround.SaveGeoTiff("coverage.tif");

// 6. Render targets for the Unity project.
var targets = new PointSet(map.Anchor)
    .Add("landmark", new UtmPoint(691_607.86, 5_334_760.39), z: 520, tag: "poi")
    .AddTrajectory(flight, stepSeconds: 1)
    .AddBoresightTrack(flight, camera, map.LineOfSight, stepSeconds: 1);
UnityPointsExport.WriteFile("points.json", targets);
```

## Conventions (read once, save hours)

- **Frames**: local ENU, Z up, meters. Yaw = heading clockwise from north
  (aviation), pitch up positive, roll right positive. A nadir camera is
  `MountPitchDeg = -90`.
- **Time**: seconds on a double timeline; source units adapted via
  `TabularMapping.TimeDivisor`. No time column → sample index ×
  `FallbackSampleInterval`.
- **Flat earth**: LOS ignores curvature (~2 m drop at 5 km); fine for
  low-altitude sensor work, revisit for long-range links.
- **NoData**: outside coverage, `HeightAt` returns null and rays terminate —
  no silent zeros.

## Sensors

All sensors share `SensorModel`: a name, `MaxRangeMeters`, and mount angles
that attach the device rigidly to the body (`MountPitchDeg: -90` = looking
straight down, the default). Each shape only defines the ray bundle it emits
(`Rays(pose, quality)` — a `SensorRay` carries origin + direction), so LOS
and coverage work identically for every shape and new ones are one small
record:

| Sensor | Shape | Knobs |
|---|---|---|
| `PyramidSensor` | rectangular frustum — the classic camera/imager | `FovHorizontalDeg`, `FovVerticalDeg` |
| `ConeSensor` | circular FOV around the boresight — spot sensors, conical scanners | `HalfAngleDeg` |
| `CylinderSensor` | fixed ground radius under the platform, independent of altitude — downward radar / proximity | `RadiusMeters` (mount angles ignored; sampled as parallel vertical rays, `MaxRangeMeters` = depth) |

`quality` scales ray density (pyramid: rays across ≈ quality; cone/cylinder:
rings ≈ quality/3 with circumference-proportional counts). Denser = finer
footprint, linearly slower.

## Rotation data: "could be anything and everything"

Both attitude representations are first-class:

- **Euler columns** — map `Yaw`/`Pitch`/`Roll` roles (aerospace convention;
  radians via `AngleToDegrees = 180/Math.PI`).
- **Quaternion columns** — map `QuatX/Y/Z/W`; when all four are present they
  *win* over Euler columns. The quaternion is the body→ENU rotation, is
  normalized on load, kept exactly on `TrajectorySample.Orientation`
  (interpolation uses `Quaternion.Slerp`), and the Euler fields are derived
  from it for display. `SuggestMapping` recognizes `qx/qy/qz/qw`,
  `quat_x`…, and scalar-first `q0..q3` column names.

Anything stranger (rotation matrices, axis-angle) → convert to a quaternion
in a pre-pass, or ask for a loader extension.

## Unity render-target export

`UnityPointsExport` writes a `PointSet` as JSON that `JsonUtility` can
deserialize directly (flat fields, no dictionaries):

```jsonc
{
  "anchor": {                     // place one GameObject here = world origin
    "utmEasting": 691607.86, "utmNorthing": 5334760.39,
    "utmZone": 32, "epsg": 25832,
    "latitude": 48.13711, "longitude": 11.57538,
    "frame": "local ENU meters; unity = (east, up, north), left-handed Y-up"
  },
  "points": [{
    "name": "boresight_0", "tag": "boresight",
    "x": 12.3, "y": -4.5, "z": 520.0,          // ENU (X east, Y north, Z up)
    "unityX": 12.3, "unityY": 520.0, "unityZ": -4.5,   // assign verbatim
    "unityEulerX": 15.0, "unityEulerY": 90.0, "unityEulerZ": -5.0
  }]
}
```

Axis handover: ENU is right-handed Z-up, Unity is left-handed Y-up, so
`unity = (east, up, north)` and `unityEuler = (−pitch, yaw, −roll)` for
`Transform.eulerAngles`. Both coordinate sets are in the file so nothing is
lost if your importer prefers to do the swap itself.

## The scene document (`openmap scene file.json`)

The whole session above as one declarative JSON file — same building blocks,
no C# required. Paths are relative to the document. `SceneRunner.RunAsync`
is the library entry if you want it inside your own tool.

```jsonc
{
  "map": {
    // offline:  "terrainFile": "terrain.tif", "surfaceFile": "surface.tif"
    "state": "by",
    "bbox": [689600, 5332800, 693600, 5336800],
    "cache": "tilecache",
    "surfaceModel": true,
    "classificationFile": "classes.tif"      // optional
  },
  "trajectory": {
    "file": "flight.csv",
    "mapping": {                              // omit to auto-suggest
      "fields": { "time": "Time", "easting": "X", "northing": "Y",
                  "alt": "Z", "heading": "Yaw" },
      "positionEpsg": 25832
    }
  },
  "sensor": {
    "type": "pyramid",                        // pyramid | cone | cylinder
    "name": "cam", "mountPitchDeg": -90,
    "fovHorizontalDeg": 60, "fovVerticalDeg": 45
  },
  "areas": [
    { "name": "nearTrees", "objectsTallerThan": 3, "dilateMeters": 5,
      "geoTiff": "near_trees.tif" }
  ],
  "outputs": {
    "coverageGeoTiff": "coverage.tif", "frameStepSeconds": 1, "quality": 32,
    "unityPoints": "points.json", "pointStepSeconds": 1,
    "includeTrajectory": true, "includeBoresight": true,
    "unityScene": "unity_bundle"
  }
}
```

## The Unity viewer (`unity/OpenMapViewer`)

`"unityScene"` writes a complete viewer bundle: manifest + raw heightmap +
overlay PNGs (coverage is added automatically when trajectory + sensor are
present) + trajectory + points. Copy the `Assets/OpenMap` folder from
[unity/OpenMapViewer](../unity/OpenMapViewer/README.md) into any Unity
project (no packages needed), point the `OpenMapSceneLoader` component at
the bundle, press Play — terrain, flight playback with timeline, live sensor
frustum + boresight ground point, overlay toggles and point markers, all
interactive. `UnitySceneExport.Write(...)` is the same thing as a library
call.

## Decisions log (was: open questions)

1. **Rendering target** — Unity. Render-target points export via
   `UnityPointsExport`, and the full interactive visualization ships as a
   drop-in Unity script folder (see the Unity viewer section above).
   Masks/terrain still go out as GeoTIFF for GIS tools.
2. **Rotation source** — both Euler and quaternions, quaternions win when
   present (see above).
3. **Sensor models** — the typical three: pyramid / cone (level) / cylinder.
   Gimbal-from-columns and squint remain easy extensions of `SensorModel`.
4. **Scene document** — built (`openmap scene`), see above.
