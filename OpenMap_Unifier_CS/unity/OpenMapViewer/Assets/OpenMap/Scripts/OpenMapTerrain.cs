using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenMapViewer
{
    /// <summary>
    /// Builds the terrain as a single mesh (32-bit indices, downsampled to a
    /// vertex budget) plus a baked colormap texture: ortho imagery when the
    /// bundle has one, otherwise hillshaded height gradient — with overlay
    /// masks (coverage, areas) tinted on top. Rebaking the colormap is how
    /// overlay visibility toggles work.
    /// </summary>
    public static class OpenMapTerrain
    {
        public static GameObject BuildMesh(ManifestTerrain t, float[] heights, int maxVertices)
        {
            int w = t.width, h = t.height;
            int stride = 1;
            while (((w - 1) / stride + 1) * ((h - 1) / stride + 1) > maxVertices)
                stride++;
            int vw = (w - 1) / stride + 1;
            int vh = (h - 1) / stride + 1;

            var vertices = new List<Vector3>(vw * vh);
            var uvs = new List<Vector2>(vw * vh);
            for (int vr = 0; vr < vh; vr++)
            {
                int row = Mathf.Min(vr * stride, h - 1);
                for (int vc = 0; vc < vw; vc++)
                {
                    int col = Mathf.Min(vc * stride, w - 1);
                    float x = t.originX + col * t.pixelSize;
                    float z = t.originZTop - row * t.pixelSize;
                    vertices.Add(new Vector3(x, heights[row * w + col], z));
                    uvs.Add(new Vector2(col / (float)(w - 1), 1f - row / (float)(h - 1)));
                }
            }

            var triangles = new int[(vw - 1) * (vh - 1) * 6];
            int i = 0;
            for (int vr = 0; vr < vh - 1; vr++)
            {
                for (int vc = 0; vc < vw - 1; vc++)
                {
                    int a = vr * vw + vc;          // top-left
                    int b = a + 1;                 // top-right
                    int c = a + vw;                // bottom-left
                    int d = c + 1;                 // bottom-right
                    triangles[i++] = a; triangles[i++] = b; triangles[i++] = d;
                    triangles[i++] = a; triangles[i++] = d; triangles[i++] = c;
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        public static Texture2D BakeColormap(ManifestTerrain t, float[] heights,
            Texture2D ortho, ManifestOverlay[] overlays, Texture2D[] overlayTextures,
            bool[] overlayVisible)
        {
            int w = t.width, h = t.height;
            int scale = 1;
            while (w / scale > 2048 || h / scale > 2048) scale++;
            int tw = w / scale, th = h / scale;

            Vector3 light = new Vector3(-0.45f, 0.8f, 0.4f).normalized;
            float range = Mathf.Max(0.001f, t.maxHeight - t.minHeight);
            var pixels = new Color32[tw * th];

            for (int tr = 0; tr < th; tr++)
            {
                int row = Mathf.Min(tr * scale, h - 1);
                for (int tc = 0; tc < tw; tc++)
                {
                    int col = Mathf.Min(tc * scale, w - 1);
                    float u = col / (float)(w - 1);
                    float v = 1f - row / (float)(h - 1);

                    Color baseColor;
                    if (ortho != null)
                    {
                        baseColor = ortho.GetPixelBilinear(u, v);
                    }
                    else
                    {
                        float k = (heights[row * w + col] - t.minHeight) / range;
                        baseColor = HeightGradient(k) * Hillshade(heights, w, h, row, col, t.pixelSize, light);
                        baseColor.a = 1f;
                    }

                    if (overlays != null)
                    {
                        for (int o = 0; o < overlays.Length; o++)
                        {
                            if (!overlayVisible[o] || overlayTextures[o] == null) continue;
                            if (overlayTextures[o].GetPixelBilinear(u, v).r > 0.5f)
                            {
                                var tint = new Color(overlays[o].r / 255f,
                                    overlays[o].g / 255f, overlays[o].b / 255f);
                                baseColor = Color.Lerp(baseColor, tint, 0.5f);
                            }
                        }
                    }

                    // Color32 rows go bottom-first; our raster rows are top-first.
                    pixels[(th - 1 - tr) * tw + tc] = baseColor;
                }
            }

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static Color HeightGradient(float k)
        {
            var low = new Color(0.35f, 0.48f, 0.28f);   // valley green
            var mid = new Color(0.62f, 0.52f, 0.38f);   // slope brown
            var high = new Color(0.92f, 0.92f, 0.92f);  // summit gray
            return k < 0.5f ? Color.Lerp(low, mid, k * 2f) : Color.Lerp(mid, high, (k - 0.5f) * 2f);
        }

        private static float Hillshade(float[] heights, int w, int h, int row, int col,
            float pixelSize, Vector3 light)
        {
            float left = heights[row * w + Mathf.Max(col - 1, 0)];
            float right = heights[row * w + Mathf.Min(col + 1, w - 1)];
            float north = heights[Mathf.Max(row - 1, 0) * w + col];
            float south = heights[Mathf.Min(row + 1, h - 1) * w + col];
            var normal = new Vector3(left - right, 2f * pixelSize, south - north).normalized;
            return 0.35f + 0.65f * Mathf.Clamp01(Vector3.Dot(normal, light));
        }

        public static Material TerrainMaterial(Texture2D colormap)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Diffuse");
            var mat = new Material(shader);
            mat.mainTexture = colormap;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            return mat;
        }

        public static Material LineMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}
