using System.IO;
using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// The one component you add to an empty scene. Point it at a bundle
    /// folder written by the OpenMapUnifier framework ("unityScene" output of
    /// `openmap scene`, or UnitySceneExport.Write) and press Play: terrain,
    /// overlays, flight playback, sensor frustum, render-target points,
    /// camera, light and HUD are all created at runtime — no scene setup,
    /// no packages.
    /// </summary>
    public class OpenMapSceneLoader : MonoBehaviour
    {
        [Tooltip("Folder containing manifest.json. Leave empty to use Assets/StreamingAssets/OpenMapBundle.")]
        public string bundlePath = "";

        [Tooltip("Terrain mesh vertex budget; larger regions are downsampled to fit.")]
        public int maxTerrainVertices = 1000000;

        [Tooltip("Aircraft size in meters; 0 picks one from the scene size.")]
        public float aircraftScale = 0f;

        public bool Loaded { get; private set; }
        public Manifest Manifest { get; private set; }

        private float[] _heights;
        private Texture2D _ortho;
        private Texture2D[] _overlayTextures = new Texture2D[0];
        private bool[] _overlayVisible = new bool[0];
        private Material _terrainMaterial;
        private GameObject _terrain;

        public int OverlayCount { get { return _overlayVisible.Length; } }
        public string OverlayName(int i) { return Manifest.overlays[i].name; }
        public bool IsOverlayVisible(int i) { return _overlayVisible[i]; }

        public void SetOverlayVisible(int i, bool visible)
        {
            _overlayVisible[i] = visible;
            RebakeColormap();
        }

        private void Start()
        {
            string dir = string.IsNullOrEmpty(bundlePath)
                ? Path.Combine(Application.streamingAssetsPath, "OpenMapBundle")
                : bundlePath;
            if (!File.Exists(Path.Combine(dir, "manifest.json")))
            {
                Debug.LogError("OpenMap: no manifest.json found at '" + dir +
                    "'. Set Bundle Path on the OpenMapSceneLoader component, or copy the " +
                    "exported bundle folder to Assets/StreamingAssets/OpenMapBundle.");
                return;
            }

            Manifest = OpenMapBundle.LoadManifest(dir);
            _heights = OpenMapBundle.LoadHeights(dir, Manifest.terrain);
            if (!string.IsNullOrEmpty(Manifest.terrain.orthoFile))
                _ortho = OpenMapBundle.LoadTexture(dir, Manifest.terrain.orthoFile);

            int overlayCount = Manifest.overlays != null ? Manifest.overlays.Length : 0;
            _overlayTextures = new Texture2D[overlayCount];
            _overlayVisible = new bool[overlayCount];
            for (int i = 0; i < overlayCount; i++)
            {
                _overlayTextures[i] = OpenMapBundle.LoadTexture(dir, Manifest.overlays[i].file);
                _overlayVisible[i] = Manifest.overlays[i].visibleByDefault;
            }

            _terrain = OpenMapTerrain.BuildMesh(Manifest.terrain, _heights, maxTerrainVertices);
            _terrainMaterial = OpenMapTerrain.TerrainMaterial(BakeColormap());
            _terrain.GetComponent<MeshRenderer>().sharedMaterial = _terrainMaterial;

            EnsureLight();
            var camera = EnsureCamera();
            var orbit = camera.GetComponent<OrbitCamera>();
            if (orbit == null) orbit = camera.gameObject.AddComponent<OrbitCamera>();
            orbit.frameBounds = _terrain.GetComponent<MeshRenderer>().bounds;
            orbit.FrameTerrain();

            FlightPlayback playback = null;
            SensorVisualizer sensorVisualizer = null;
            TrajectoryJson trajectory = string.IsNullOrEmpty(Manifest.trajectoryFile)
                ? null
                : OpenMapBundle.LoadTrajectory(dir, Manifest.trajectoryFile);
            if (trajectory != null && trajectory.samples != null && trajectory.samples.Length >= 2)
            {
                float scale = aircraftScale > 0f ? aircraftScale : AutoAircraftScale();
                var aircraft = BuildAircraft(scale);
                playback = gameObject.AddComponent<FlightPlayback>();
                playback.aircraft = aircraft.transform;
                playback.SetData(trajectory);
                orbit.followTarget = aircraft.transform;
                BuildTrajectoryLine(playback.PathPositions(), scale);

                if (Manifest.sensor != null && !string.IsNullOrEmpty(Manifest.sensor.type))
                {
                    var sensorGo = new GameObject("Sensor Visualizer");
                    sensorVisualizer = sensorGo.AddComponent<SensorVisualizer>();
                    sensorVisualizer.playback = playback;
                    sensorVisualizer.sensor = Manifest.sensor;
                }
            }

            PointsJson points = string.IsNullOrEmpty(Manifest.pointsFile)
                ? null
                : OpenMapBundle.LoadPoints(dir, Manifest.pointsFile);
            if (points != null && points.points != null)
                BuildPointMarkers(points);

            var hud = gameObject.AddComponent<OpenMapHud>();
            hud.loader = this;
            hud.playback = playback;
            hud.sensorVisualizer = sensorVisualizer;
            hud.orbitCamera = orbit;

            Loaded = true;
            Debug.Log("OpenMap: loaded bundle from " + dir);
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
            if (Object.FindObjectOfType<Light>() != null) return;
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static Camera EnsureCamera()
        {
            if (Camera.main != null) return Camera.main;
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

            AddPart(root, material, new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.4f, 2.2f));   // body
            AddPart(root, material, new Vector3(0f, 0f, 0.2f), new Vector3(3.2f, 0.08f, 0.7f)); // wings
            AddPart(root, material, new Vector3(0f, 0f, -1.0f), new Vector3(1.2f, 0.08f, 0.4f)); // stabilizer
            AddPart(root, material, new Vector3(0f, 0.35f, -1.0f), new Vector3(0.08f, 0.6f, 0.4f)); // fin

            root.transform.localScale = new Vector3(scale, scale, scale);
            return root;
        }

        private static void AddPart(GameObject parent, Material material,
            Vector3 localPosition, Vector3 localScale)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(part.GetComponent<Collider>()); // raycasts must only hit terrain
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
        }

        private static void BuildTrajectoryLine(Vector3[] positions, float scale)
        {
            if (positions.Length < 2) return;
            var go = new GameObject("Trajectory Path");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.widthMultiplier = Mathf.Max(0.3f, scale * 0.15f);
            line.material = OpenMapTerrain.LineMaterial(new Color(1f, 0.85f, 0.2f, 0.9f));
        }

        private void BuildPointMarkers(PointsJson points)
        {
            var root = new GameObject("Points");
            float size = Mathf.Max(1f, AutoAircraftScale() * 0.5f);
            foreach (var p in points.points)
            {
                // The full path is already drawn as a line; sphere-per-sample
                // would only clutter it.
                if (p.tag == "trajectory") continue;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(marker.GetComponent<Collider>());
                marker.name = string.IsNullOrEmpty(p.name) ? "point" : p.name;
                marker.transform.SetParent(root.transform, false);
                marker.transform.position = new Vector3(p.unityX, p.unityY, p.unityZ);
                marker.transform.localScale = new Vector3(size, size, size);
                marker.GetComponent<MeshRenderer>().sharedMaterial =
                    OpenMapTerrain.LineMaterial(TagColor(p.tag));
            }
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
