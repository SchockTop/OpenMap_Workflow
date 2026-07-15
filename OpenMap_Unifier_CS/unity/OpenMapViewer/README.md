# OpenMap Unity Viewer

A **complete, ready-to-open Unity project** for interactive 3D visualization
of OpenMapUnifier scenes: terrain, flight trajectory playback with a
timeline, live sensor frustum with boresight ground point, coverage/area
overlays and render-target points. Pure C# on Unity's built-in APIs —
**no packages, no store assets, nothing downloaded.**

## Open it (2 minutes, no assembly)

1. Unity Hub → **Add** → **Add project from disk** → select this folder
   (`unity/OpenMapViewer`).
2. Open it with any **Unity 2021.3 LTS or newer** (it will offer to upgrade —
   accept). Use the **3D / Built-in** render pipeline default.
3. Press **Play**. That's it — a demo scene (ridge terrain, orbiting camera
   flight, sensor frustum, coverage overlay) loads from
   `Assets/StreamingAssets/OpenMapBundle` so you immediately see everything
   working.

No scene setup is needed: `OpenMapBootstrap` starts the viewer in whatever
scene is open, and the HUD does the rest.

## The UI

**Bundle picker** (shown when no bundle is found, or via *Load bundle…*):
paste or browse to any bundle folder and press *Load scene* — also works
mid-Play, no restart. The last-used bundle is remembered.

**Control panel** (left side):

| Section | What it does |
|---|---|
| Header | scene origin (lat/lon, EPSG), scale note |
| Flight playback | Play/Pause (or Space), timeline scrubbing, speed 0.5–25x, loop, follow-camera |
| Aircraft | live UTM position, altitude, yaw/pitch/roll |
| Sensor | wireframe on/off; live boresight ground point in UTM |
| Ground overlays | color legend + per-overlay toggle (coverage, areas) |
| Buttons | *Load bundle…*, *Screenshot* (saved under `persistentDataPath/OpenMapScreenshots`) |

Camera: **RMB** orbit · **MMB** pan · **scroll** zoom · **F** frame terrain ·
**Space** play/pause.

## Feed it your own scenes

Add `"unityScene"` to the outputs of a scene document and run it:

```jsonc
// demo.json
{
  "map": { "state": "by", "bbox": [689600, 5332800, 693600, 5336800],
           "cache": "tilecache", "surfaceModel": true },
  "trajectory": { "file": "flight.csv" },
  "sensor": { "type": "pyramid", "name": "cam",
              "fovHorizontalDeg": 60, "fovVerticalDeg": 45, "mountPitchDeg": -90 },
  "areas": [ { "name": "trees", "objectsTallerThan": 3 } ],
  "outputs": { "unityScene": "unity_bundle" }
}
```

```
openmap scene demo.json
```

Then load `unity_bundle/` via the bundle picker — or copy it over
`Assets/StreamingAssets/OpenMapBundle` to make it the startup scene.
Coverage is added as an overlay automatically when a trajectory + sensor
exist; every `areas` entry becomes a toggleable overlay too.
`UnitySceneExport.Write(...)` does the same from your own C# code.

**Real imagery**: drop a PNG/JPG of the region next to `manifest.json` and
set its name as `"orthoFile"` in the manifest — it is draped over the
terrain instead of the hillshaded height gradient.

## What's in a bundle

| File | Content |
|---|---|
| `manifest.json` | anchor georeference, terrain metadata, overlay list, sensor spec |
| `terrain_heights.r32` | raw little-endian float32 heightmap, row 0 = north |
| `overlay_*.png` | grayscale masks (255 = inside), one per overlay |
| `trajectory.json` | ENU samples with Euler + optional quaternion attitude |
| `points.json` | render-target points, pre-converted Unity coordinates |

## Using the scripts in your own project

Copy `Assets/OpenMap` into your project's `Assets/`. Delete
`Scripts/OpenMapBootstrap.cs` if you don't want the viewer to auto-start,
and add the `OpenMapSceneLoader` component manually instead (menu:
`OpenMap → Create Scene Loader...`). The reusable pieces are deliberately
small: `OpenMapBundle` (parsing), `OpenMapFrames` (all ENU↔Unity axis and
quaternion conversion in one file), `FlightPlayback.SamplePose` (pose at any
time), and `points.json` whose `unityX/Y/Z`, `unityEulerX/Y/Z` fields are
JsonUtility-ready.

## Frame conventions (matches `docs/mapscene.md`)

- 1 Unity unit = 1 meter; scene origin = the bundle's anchor (UTM + lat/lon
  shown in the HUD).
- Framework ENU (x east, y north, z up, right-handed) → Unity (x east, y up,
  z north, left-handed): positions swap y/z; rotations map as
  `Quaternion(-qx, -qz, -qy, qw)` and `Euler(-pitch, yaw, -roll)`.
