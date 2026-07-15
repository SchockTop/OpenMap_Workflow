using System.Collections.Generic;
using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// Live wireframe of the sensor volume (pyramid / cone / cylinder, same
    /// conventions as the framework's SensorModel) attached to the aircraft
    /// pose, plus the boresight ray and its ground hit — "where is it looking
    /// this frame", answered by a physics raycast against the terrain mesh.
    /// </summary>
    public class SensorVisualizer : MonoBehaviour
    {
        public FlightPlayback playback;
        public ManifestSensor sensor;
        public bool show = true;

        /// <summary>Whether the boresight hit the ground this frame.</summary>
        public bool HasHit { get; private set; }
        /// <summary>Ground point the sensor looks at (Unity/world coordinates).</summary>
        public Vector3 HitPoint { get; private set; }

        private Mesh _lines;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _indices = new List<int>();
        private Transform _hitMarker;
        private MeshRenderer _renderer;

        private static readonly Color FrustumColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        private static readonly Color BoresightColor = new Color(1f, 0.25f, 0.2f, 1f);
        private static readonly Color GroundColor = new Color(0.3f, 1f, 0.4f, 1f);

        private void Start()
        {
            _lines = new Mesh();
            _lines.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _lines;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = OpenMapTerrain.LineMaterial(Color.white);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Boresight Hit";
            Object.Destroy(marker.GetComponent<Collider>());
            marker.GetComponent<MeshRenderer>().sharedMaterial =
                OpenMapTerrain.LineMaterial(BoresightColor);
            _hitMarker = marker.transform;
            _hitMarker.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            if (_renderer == null) return;
            bool active = show && playback != null && playback.HasData && sensor != null;
            _renderer.enabled = active;
            if (!active)
            {
                HasHit = false;
                if (_hitMarker != null) _hitMarker.gameObject.SetActive(false);
                return;
            }

            Vector3 pos;
            Quaternion body;
            playback.SamplePose(playback.TimeSeconds, out pos, out body);
            Quaternion mount = OpenMapFrames.AttitudeToUnity(
                sensor.mountYawDeg, sensor.mountPitchDeg, sensor.mountRollDeg);
            Quaternion rot = body * mount;
            float range = Mathf.Min(sensor.maxRangeMeters, 20000f);

            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();

            string type = sensor.type != null ? sensor.type.ToLowerInvariant() : "pyramid";
            if (type == "cone") BuildCone(pos, rot, range);
            else if (type == "cylinder") BuildCylinder(pos, range);
            else BuildPyramid(pos, rot, range);

            BuildBoresight(pos, type == "cylinder" ? Quaternion.identity * Quaternion.Euler(90, 0, 0) : rot, range);

            _lines.Clear();
            _lines.SetVertices(_vertices);
            _lines.SetColors(_colors);
            _lines.SetIndices(_indices.ToArray(), MeshTopology.Lines, 0);
            _lines.RecalculateBounds();
        }

        // ---- shapes ----------------------------------------------------------

        private void BuildPyramid(Vector3 pos, Quaternion rot, float range)
        {
            float tanH = Mathf.Tan(sensor.fovHorizontalDeg * Mathf.Deg2Rad * 0.5f);
            float tanV = Mathf.Tan(sensor.fovVerticalDeg * Mathf.Deg2Rad * 0.5f);
            var ends = new Vector3[4];
            var corners = new Vector2[]
                { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = rot * new Vector3(corners[i].x * tanH, corners[i].y * tanV, 1f).normalized;
                ends[i] = RayEnd(pos, dir, range);
                AddLine(pos, ends[i], FrustumColor);
            }
            for (int i = 0; i < 4; i++)
                AddLine(ends[i], ends[(i + 1) % 4], FrustumColor);
        }

        private void BuildCone(Vector3 pos, Quaternion rot, float range)
        {
            const int segments = 24;
            float tan = Mathf.Tan(sensor.halfAngleDeg * Mathf.Deg2Rad);
            var ends = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float phi = 2f * Mathf.PI * i / segments;
                Vector3 dir = rot * new Vector3(
                    Mathf.Cos(phi) * tan, Mathf.Sin(phi) * tan, 1f).normalized;
                ends[i] = RayEnd(pos, dir, range);
                if (i % 3 == 0) AddLine(pos, ends[i], FrustumColor);
            }
            for (int i = 0; i < segments; i++)
                AddLine(ends[i], ends[(i + 1) % segments], FrustumColor);
        }

        private void BuildCylinder(Vector3 pos, float range)
        {
            const int segments = 24;
            var top = new Vector3[segments];
            var bottom = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float phi = 2f * Mathf.PI * i / segments;
                top[i] = pos + new Vector3(
                    Mathf.Cos(phi) * sensor.radiusMeters, 0f, Mathf.Sin(phi) * sensor.radiusMeters);
                bottom[i] = RayEnd(top[i], Vector3.down, range);
                if (i % 3 == 0) AddLine(top[i], bottom[i], FrustumColor);
            }
            for (int i = 0; i < segments; i++)
            {
                AddLine(top[i], top[(i + 1) % segments], FrustumColor);
                AddLine(bottom[i], bottom[(i + 1) % segments], GroundColor);
            }
        }

        private void BuildBoresight(Vector3 pos, Quaternion rot, float range)
        {
            Vector3 dir = rot * Vector3.forward;
            RaycastHit hit;
            bool hasHit = Physics.Raycast(pos, dir, out hit, range);
            HasHit = hasHit;
            if (hasHit) HitPoint = hit.point;
            Vector3 end = hasHit ? hit.point : pos + dir * range;
            AddLine(pos, end, BoresightColor);

            if (_hitMarker != null)
            {
                _hitMarker.gameObject.SetActive(hasHit);
                if (hasHit)
                {
                    _hitMarker.position = hit.point;
                    float s = Mathf.Max(1f, range * 0.004f);
                    _hitMarker.localScale = new Vector3(s, s, s);
                }
            }
        }

        // ---- helpers ---------------------------------------------------------

        private Vector3 RayEnd(Vector3 origin, Vector3 dir, float range)
        {
            RaycastHit hit;
            return Physics.Raycast(origin, dir, out hit, range) ? hit.point : origin + dir * range;
        }

        private void AddLine(Vector3 a, Vector3 b, Color color)
        {
            _indices.Add(_vertices.Count);
            _vertices.Add(a);
            _colors.Add(color);
            _indices.Add(_vertices.Count);
            _vertices.Add(b);
            _colors.Add(color);
        }
    }
}
