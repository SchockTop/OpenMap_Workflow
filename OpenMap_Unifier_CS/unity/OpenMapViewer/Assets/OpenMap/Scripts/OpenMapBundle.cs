using System;
using System.IO;
using UnityEngine;

namespace OpenMapViewer
{
    // ---- manifest.json -----------------------------------------------------
    // These classes mirror the JSON written by UnitySceneExport in the
    // OpenMapUnifier C# framework. JsonUtility needs exact field names.

    [Serializable]
    public class ManifestAnchor
    {
        public double utmEasting;
        public double utmNorthing;
        public int utmZone;
        public int epsg;
        public double latitude;
        public double longitude;
    }

    [Serializable]
    public class ManifestTerrain
    {
        public string heightsFile;
        public int width;
        public int height;
        public float pixelSize;
        public float originX;    // Unity x of the top-left cell center (east)
        public float originZTop; // Unity z of the top-left cell center (north)
        public float minHeight;
        public float maxHeight;
        public string orthoFile; // optional PNG/JPG draped over the terrain
    }

    [Serializable]
    public class ManifestOverlay
    {
        public string name;
        public string file;
        public int r;
        public int g;
        public int b;
        public bool visibleByDefault;
    }

    [Serializable]
    public class ManifestSensor
    {
        public string type; // pyramid | cone | cylinder
        public string name;
        public float maxRangeMeters;
        public float mountYawDeg;
        public float mountPitchDeg;
        public float mountRollDeg;
        public float fovHorizontalDeg;
        public float fovVerticalDeg;
        public float halfAngleDeg;
        public float radiusMeters;
    }

    [Serializable]
    public class Manifest
    {
        public int version;
        public ManifestAnchor anchor;
        public ManifestTerrain terrain;
        public ManifestOverlay[] overlays;
        public string trajectoryFile;
        public string pointsFile;
        public ManifestSensor sensor;
    }

    // ---- trajectory.json ----------------------------------------------------

    [Serializable]
    public class TrajectorySampleJson
    {
        public float t;
        public float x; // ENU east
        public float y; // ENU north
        public float z; // ENU up (meters above sea level)
        public float yawDeg;
        public float pitchDeg;
        public float rollDeg;
        public float qx;
        public float qy;
        public float qz;
        public float qw;
        public bool hasQuat;
    }

    [Serializable]
    public class TrajectoryJson
    {
        public TrajectorySampleJson[] samples;
    }

    // ---- points.json --------------------------------------------------------

    [Serializable]
    public class PointJson
    {
        public string name;
        public string tag;
        public float x;
        public float y;
        public float z;
        public float unityX;
        public float unityY;
        public float unityZ;
        public float unityEulerX;
        public float unityEulerY;
        public float unityEulerZ;
    }

    [Serializable]
    public class PointsJson
    {
        public PointJson[] points;
    }

    // ---- loading ------------------------------------------------------------

    public static class OpenMapBundle
    {
        public static Manifest LoadManifest(string bundleDir)
        {
            string path = Path.Combine(bundleDir, "manifest.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("No manifest.json in bundle folder: " + bundleDir);
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        }

        public static float[] LoadHeights(string bundleDir, ManifestTerrain terrain)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(bundleDir, terrain.heightsFile));
            float[] heights = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, heights, 0, bytes.Length);
            if (heights.Length != terrain.width * terrain.height)
                throw new InvalidDataException("Heightmap size does not match the manifest.");
            return heights;
        }

        public static TrajectoryJson LoadTrajectory(string bundleDir, string file)
        {
            string path = Path.Combine(bundleDir, file);
            return File.Exists(path)
                ? JsonUtility.FromJson<TrajectoryJson>(File.ReadAllText(path))
                : null;
        }

        public static PointsJson LoadPoints(string bundleDir, string file)
        {
            string path = Path.Combine(bundleDir, file);
            return File.Exists(path)
                ? JsonUtility.FromJson<PointsJson>(File.ReadAllText(path))
                : null;
        }

        public static Texture2D LoadTexture(string bundleDir, string file)
        {
            string path = Path.Combine(bundleDir, file);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path)); // resizes automatically
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }

    /// <summary>
    /// Frame conversion between the framework's ENU scene frame (x east,
    /// y north, z up, right-handed) and Unity's left-handed Y-up frame
    /// (x east, y up, z north). One place, used everywhere.
    /// </summary>
    public static class OpenMapFrames
    {
        public static Vector3 EnuToUnity(float east, float north, float up)
        {
            return new Vector3(east, up, north);
        }

        /// <summary>Body-to-ENU quaternion -> Unity rotation (y/z swap flips
        /// handedness, so the vector part is negated and swapped).</summary>
        public static Quaternion EnuQuatToUnity(float qx, float qy, float qz, float qw)
        {
            return new Quaternion(-qx, -qz, -qy, qw);
        }

        /// <summary>Aerospace yaw (CW from north) / pitch (up+) / roll (right+)
        /// -> Unity rotation. Unity's Euler order (y, then x, then z) matches
        /// the aerospace yaw-pitch-roll sequence exactly.</summary>
        public static Quaternion AttitudeToUnity(float yawDeg, float pitchDeg, float rollDeg)
        {
            return Quaternion.Euler(-pitchDeg, yawDeg, -rollDeg);
        }
    }
}
