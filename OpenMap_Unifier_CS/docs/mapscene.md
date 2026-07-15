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
| `Sensor` | FOV + mount angles on the body; boresight and frustum rays per pose |
| `LineOfSight` | ray-march the terrain: `HitGround`, `CanSee`, `BoresightGroundPoint` |
| `GroundMask` | areas on the ground: by polygon, by condition, by class, by object height (nDSM); set algebra + `Dilate` |
| `CoverageAnalyzer` | ground actually seen by a sensor along the whole flight (occlusion-aware) |
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
var camera = new Sensor("nadir", FovHorizontalDeg: 60, FovVerticalDeg: 45,
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

## Open questions (waiting on your input)

1. **Rendering target.** Masks/terrain export as GeoTIFF and trajectories as
   CSV today. What does your 3D viewer want — glTF meshes, Blender-python
   handoff (the existing pipeline), or a Unity-style stream? That decides the
   next export module.
2. **Rotation source of truth.** Yaw/pitch/roll (aerospace, current) or
   quaternions in your logs? Both can be mapped; the interpolation should
   match what your instruments emit.
3. **Sensor models.** Rectangular frustum is in. Conical (spinning LiDAR),
   side-looking with squint, or gimbal-driven (mount angles from CSV columns)?
   All are small extensions of `Sensor`.
4. **Scene document.** A JSON project file ("region + layers + trajectory +
   mapping + analyses + outputs") that one CLI command executes is sketched
   but not built — worth it once the interactive workflow settles. Same for
   per-frame outputs (looking-point track as CSV/GeoJSON) — trivial to add,
   tell me which columns you want.
