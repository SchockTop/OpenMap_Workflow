using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// The heart of the viewer. On Play it loads the first bundle it finds
    /// (inspector path → Assets/StreamingAssets/OpenMapBundle → the last
    /// bundle used on this machine); if none is found the HUD shows a bundle
    /// picker. Everything — terrain, overlays, aircraft, sensor, points,
    /// camera, light, HUD — is created at runtime, and a different bundle can
    /// be loaded at any time without leaving Play mode.
    /// </summary>
    public class OpenMapSceneLoader : MonoBehaviour
    {
        [Tooltip("Folder containing manifest.json. Leave empty to auto-detect.")]
        public string bundlePath = "";

        [Tooltip("Terrain mesh vertex budget; larger regions are downsampled to fit.")]
        public int maxTerrainVertices = 1000000;

        [Tooltip("Aircraft size in meters; 0 picks one from the scene size.")]
        public float aircraftScale = 0f;

        public bool Loaded { get; private set; }
        public string LoadedPath { get; private set; }
        public string LastError { get; private set; }
        public Manifest Manifest { get; private set; }
        public FlightPlayback Playback { get; private set; }
        public SensorVisualizer Sensor { get; private set; }
        public OrbitCamera Orbit { get; private set; }

        private const string LastPathPref = "OpenMap.LastBundlePath";

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private float[] _heights;
        private Texture2D _ortho;
        private Texture2D[] _overlayTextures = new Texture2D[0];
        private bool[] _overlayVisible = new bool[0];
        private Material _terrainMaterial;

        public int OverlayCount { get { return _overlayVisible.Length; } }
        public string OverlayName(int i) { return Manifest.overlays[i].name; }
        public Color OverlayColor(int i)
        {
            var o = Manifest.overlays[i];
            return new Color(o.r / 255f, o.g / 255f, o.b / 255f);
        }
        public bool IsOverlayVisible(int i) { return _overlayVisible[i]; }

        public void SetOverlayVisible(int i, bool visible)
        {
            _overlayVisible[i] = visible;
            RebakeColormap();
        }

        public static string DefaultStreamingBundle
        {
            get { return Path.Combine(Application.streamingAssetsPath, "OpenMapBundle"); }
        }

        public static string RememberedPath
        {
            get { return PlayerPrefs.GetString(LastPathPref, ""); }
        }

        private void Start()
        {
            var hud = gameObject.AddComponent<OpenMapHud>();
            hud.loader = this;

            foreach (string candidate in new[]
                     { bundlePath, DefaultStreamingBundle, RememberedPath })
            {
                if (!string.IsNullOrEmpty(candidate) && IsBundle(candidate))
                {
                    LoadBundle(candidate);
                    return;
                }
            }
        }

        public static bool IsBundle(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "manifest.json"));
        }

        /// <summary>Load (or switch to) a bundle folder. Returns false and sets
        /// LastError when something is wrong with it.</summary>
        public bool LoadBundle(string dir)
        {
            LastError = null;
            if (!IsBundle(dir))
            {
                LastError = "No manifest.json in: " + dir;
                return false;
            }
            try
            {
                ClearScene();
                BuildScene(dir);
                Loaded = true;
                LoadedPath = dir;
                PlayerPrefs.SetString(LastPathPref, dir);
                PlayerPrefs.Save();
                Debug.Log("OpenMap: loaded bundle from " + dir);
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError("OpenMap: failed to load bundle: " + e);
                ClearScene();
                return false;
            }
        }

        private void ClearScene()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
            if (Playback != null) Destroy(Playback);
            Playback = null;
            Sensor = null;
            Loaded = false;
            Manifest = null;
            _heights = null;
            _ortho = null;
            _overlayTextures = new Texture2D[0];
            _overlayVisible = new bool[0];
        }

        private void BuildScene(string dir)
        {
            Manifest = OpenMapBundle.LoadManifest(dir);
            _heights = OpenMapBundle.LoadHeights(dir, Manifest.terrain);
            _ortho = string.IsNullOrEmpty(Manifest.terrain.orthoFile)
                ? null
                : OpenMapBundle.LoadTexture(dir, Manifest.terrain.orthoFile);

            int overlayCount = Manifest.overlays != null ? Manifest.overlays.Length : 0;
            _overlayTextures = new Texture2D[overlayCount];
            _overlayVisible = new bool[overlayCount];
            for (int i = 0; i < overlayCount; i++)
            {
                _overlayTextures[i] = OpenMapBundle.LoadTexture(dir, Manifest.overlays[i].file);
                _overlayVisible[i] = Manifest.overlays[i].visibleByDefault;
            }

            var terrain = OpenMapTerrain.BuildMesh(Manifest.terrain, _heights, maxTerrainVertices);
            _spawned.Add(terrain);
            _terrainMaterial = OpenMapTerrain.TerrainMaterial(BakeColormap());
            terrain.GetComponent<MeshRenderer>().sharedMaterial = _terrainMaterial;

            EnsureLight();
            var camera = EnsureCamera();
            Orbit = camera.GetComponent<OrbitCamera>();
            if (Orbit == null) Orbit = camera.gameObject.AddComponent<OrbitCamera>();
            Orbit.frameBounds = terrain.GetComponent<MeshRenderer>().bounds;
            Orbit.FrameTerrain();

            TrajectoryJson trajectory = string.IsNullOrEmpty(Manifest.trajectoryFile)
                ? null
                : OpenMapBundle.LoadTrajectory(dir, Manifest.trajectoryFile);
            if (trajectory != null && trajectory.samples != null && trajectory.samples.Length >= 2)
            {
                float scale = aircraftScale > 0f ? aircraftScale : AutoAircraftScale();
                var aircraft = BuildAircraft(scale);
                _spawned.Add(aircraft);
                Playback = gameObject.AddComponent<FlightPlayback>();
                Playback.aircraft = aircraft.transform;
                Playback.SetData(trajectory);
                Orbit.followTarget = aircraft.transform;
                var line = BuildTrajectoryLine(Playback.PathPositions(), scale);
                if (line != null) _spawned.Add(line);

                if (Manifest.sensor != null && !string.IsNullOrEmpty(Manifest.sensor.type))
                {
                    var sensorGo = new GameObject("Sensor Visualizer");
                    _spawned.Add(sensorGo);
                    Sensor = sensorGo.AddComponent<SensorVisualizer>();
                    Sensor.playback = Playback;
                    Sensor.sensor = Manifest.sensor;
                }
            }

            PointsJson points = string.IsNullOrEmpty(Manifest.pointsFile)
                ? null
                : OpenMapBundle.LoadPoints(dir, Manifest.pointsFile);
            if (points != null && points.points != null)
                _spawned.Add(BuildPointMarkers(points));
        }

        private Texture2D BakeColormap()
        {
            return OpenMapTerrain.BakeColormap(Manifest.terrain, _heights, _ortho,
                Manifest.overlays, _overlayTextures, _overlayVisible);
        }

        private void RebakeColormap()
        {
            if (_terrainMaterial == null) return;
            var old = _terrainMaterial.mainTexture;
            _terrainMaterial.mainTexture = BakeColormap();
            if (old != null) Destroy(old);
        }

        private float AutoAircraftScale()
        {
            float size = Mathf.Max(
                Manifest.terrain.width * Manifest.terrain.pixelSize,
                Manifest.terrain.height * Manifest.terrain.pixelSize);
            return Mathf.Clamp(size / 150f, 1f, 25f);
        }

        // ---- scene furniture -------------------------------------------------

        private static void EnsureLight()
        {
            if (UnityEngine.Object.FindObjectOfType<Light>() != null) return;
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static Camera EnsureCamera()
        {
            if (Camera.main != null)
            {
                Camera.main.farClipPlane = 100000f;
                return Camera.main;
            }
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 100000f;
            return cam;
        }

        private static GameObject BuildAircraft(float scale)
        {
            var root = new GameObject("Aircraft");
            var material = OpenMapTerrain.LineMaterial(new Color(1f, 0.55f, 0.1f));

            AddPart(root, material, new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.4f, 2.2f));    // body
            AddPart(root, material, new Vector3(0f, 0f, 0.2f), new Vector3(3.2f, 0.08f, 0.7f));  // wings
            AddPart(root, material, new Vector3(0f, 0f, -1.0f), new Vector3(1.2f, 0.08f, 0.4f)); // stabilizer
            AddPart(root, material, new Vector3(0f, 0.35f, -1.0f), new Vector3(0.08f, 0.6f, 0.4f)); // fin

            root.transform.localScale = new Vector3(scale, scale, scale);
            return root;
        }

        private static void AddPart(GameObject parent, Material material,
            Vector3 localPosition, Vector3 localScale)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(part.GetComponent<Collider>()); // raycasts must only hit terrain
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
        }

        private static GameObject BuildTrajectoryLine(Vector3[] positions, float scale)
        {
            if (positions.Length < 2) return null;
            var go = new GameObject("Trajectory Path");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.widthMultiplier = Mathf.Max(0.3f, scale * 0.15f);
            line.material = OpenMapTerrain.LineMaterial(new Color(1f, 0.85f, 0.2f, 0.9f));
            return go;
        }

        private GameObject BuildPointMarkers(PointsJson points)
        {
            var root = new GameObject("Points");
            float size = Mathf.Max(1f, AutoAircraftScale() * 0.5f);
            foreach (var p in points.points)
            {
                // The full path is already drawn as a line; sphere-per-sample
                // would only clutter it.
                if (p.tag == "trajectory") continue;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                UnityEngine.Object.Destroy(marker.GetComponent<Collider>());
                marker.name = string.IsNullOrEmpty(p.name) ? "point" : p.name;
                marker.transform.SetParent(root.transform, false);
                marker.transform.position = new Vector3(p.unityX, p.unityY, p.unityZ);
                marker.transform.localScale = new Vector3(size, size, size);
                marker.GetComponent<MeshRenderer>().sharedMaterial =
                    OpenMapTerrain.LineMaterial(TagColor(p.tag));
            }
            return root;
        }

        private static Color TagColor(string tag)
        {
            if (tag == "boresight") return new Color(1f, 0.3f, 0.25f);
            if (string.IsNullOrEmpty(tag)) return Color.white;
            // Stable pseudo-random hue per tag so groups are telling-apart-able.
            float hue = Mathf.Abs(tag.GetHashCode() % 1000) / 1000f;
            return Color.HSVToRGB(hue, 0.7f, 1f);
        }
    }
}
