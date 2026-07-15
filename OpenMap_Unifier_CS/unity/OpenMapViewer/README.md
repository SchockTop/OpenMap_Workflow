# OpenMap Unity Viewer

Interactive 3D visualization of OpenMapUnifier scenes inside Unity — terrain,
flight trajectory playback, live sensor frustum with boresight ground point,
coverage/area overlays and render-target points. Pure C# scripts on Unity's
built-in APIs: **no packages, no store assets, nothing to download.**

## Setup (once, ~2 minutes)

1. Create a normal Unity project (Unity Hub → New project → **3D (Built-in)**,
   any Unity 2021.3 LTS or newer).
2. Copy the `Assets/OpenMap` folder from here into your project's `Assets/`.
3. Done. No packages, no project settings to touch.

## Produce a scene bundle (framework side)

Add `"unityScene"` to the outputs of your scene document and run it:

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

That writes `unity_bundle/` with `manifest.json`, the raw heightmap, overlay
PNGs (coverage is added automatically when a trajectory + sensor exist),
`trajectory.json` and `points.json`. Or call `UnitySceneExport.Write(...)`
from your own C# code.

## Load it in Unity

Either way works:

- **Menu**: `OpenMap → Create Scene Loader...`, pick the bundle folder,
  press Play.
- **Manual**: empty GameObject → add the `OpenMapSceneLoader` component →
  set *Bundle Path* (or copy the bundle to
  `Assets/StreamingAssets/OpenMapBundle` and leave the path empty) → Play.

Everything else — terrain mesh, camera, light, aircraft, HUD — is created at
runtime.

## What you get at Play

- **Terrain**: hillshaded height-gradient mesh (with collider). Drop a
  georeferenced PNG/JPG next to the manifest and set `"orthoFile"` in
  `manifest.json` to drape real imagery instead.
- **Playback HUD** (top left): play/pause, timeline scrubbing, speed 0.5–25x,
  loop, follow-camera.
- **Aircraft** flying the trajectory with true attitude (quaternions when the
  log had them, slerp interpolation), plus the full path as a yellow line.
- **Sensor**: live wireframe of the pyramid/cone/cylinder volume, red
  boresight ray, and the ground hit marker — computed per frame with a
  physics raycast against the terrain, so it reacts to scrubbing.
- **Overlays**: coverage and area masks tinted onto the terrain, toggleable
  in the HUD.
- **Points**: every render-target point from `points.json` as a colored
  marker (colors grouped by tag; boresight track red).

Camera: right-drag orbit, middle-drag pan, scroll zoom, `F` frame terrain.

## Frame conventions (matches `docs/mapscene.md`)

- 1 Unity unit = 1 meter. Scene origin = the bundle's anchor (its UTM and
  lat/lon are in the manifest and the HUD).
- Framework ENU (x east, y north, z up, right-handed) → Unity
  (x east, y up, z north, left-handed): positions swap y/z, rotations map as
  `Quaternion(-qx, -qz, -qy, qw)` / `Euler(-pitch, yaw, -roll)`.
  All conversions live in one file: `Scripts/OpenMapBundle.cs`
  (`OpenMapFrames`).

## Using the data in your own project

The viewer is deliberately thin: `OpenMapBundle` (parsing),
`OpenMapFrames` (axis conversion) and `FlightPlayback.SamplePose` are the
reusable pieces. To feed your own render pipeline, read `points.json` — its
`unityX/Y/Z`, `unityEulerX/Y/Z` fields are pre-converted and JsonUtility-ready.
