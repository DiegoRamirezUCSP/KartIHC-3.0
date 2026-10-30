using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Generates tileable procedural textures (albedo + normal map) for sand, track
    /// dirt, rock and water, and applies them to the existing URP materials used by
    /// Ground, the walls and the decorative rocks. Also lays a visual-only dirt road
    /// (TrackSurface, no collider) over the driving ring, slightly above the ground.
    /// </summary>
    public static class ProceduralMaterials
    {
        private const int Size = 512;
        public const string TrackSurfaceMaterialPath = EnvGen.MaterialsFolder + "/Mat_TrackSurface.mat";

        [MenuItem("KartIHC/Environment/2 Materials (Procedural Textures)", priority = 102)]
        public static void GenerateMaterials()
        {
            EnvGen.BeginUndo("Procedural Materials");
            try
            {
                EditorUtility.DisplayProgressBar("Procedural Materials", "Sand", 0.1f);
                GenerateSand(out Texture2D sand, out Texture2D sandNormal);
                EditorUtility.DisplayProgressBar("Procedural Materials", "Dirt", 0.35f);
                GenerateDirt(out Texture2D dirt, out Texture2D dirtNormal);
                EditorUtility.DisplayProgressBar("Procedural Materials", "Rock", 0.6f);
                GenerateRock(out Texture2D rock, out Texture2D rockNormal);
                EditorUtility.DisplayProgressBar("Procedural Materials", "Water", 0.8f);
                GenerateWaterNormal();

                // Ground plane is 116 m: ~5 m per tile.
                EnvGen.SetLit(EnvGen.GetOrCreateLit("Assets/Materials/Mat_OffroadGround.mat"), Hex("#FFFFFF"), sand, sandNormal, 0.8f, 0.12f, new Vector2(22f, 22f));
                EnvGen.SetLit(EnvGen.GetOrCreateLit("Assets/Materials/Mat_OffroadWallA.mat"), Hex("#C9A887"), rock, rockNormal, 1f, 0.15f, new Vector2(1f, 0.6f));
                EnvGen.SetLit(EnvGen.GetOrCreateLit("Assets/Materials/Mat_OffroadWallB.mat"), Hex("#E8D3B4"), rock, rockNormal, 1f, 0.15f, new Vector2(1f, 0.6f));
                EnvGen.SetLit(EnvGen.GetOrCreateLit("Assets/Materials/Mat_Rock.mat"), Hex("#D8D0C4"), rock, rockNormal, 1.2f, 0.2f, new Vector2(1f, 1f));
                EnvGen.SetLit(EnvGen.GetOrCreateLit(TrackSurfaceMaterialPath), Hex("#FFFFFF"), dirt, dirtNormal, 1f, 0.1f, Vector2.one);
                EnvGen.PaletteMaterial();

                EditorUtility.DisplayProgressBar("Procedural Materials", "Track surface", 0.95f);
                BuildTrackSurface();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EnvGen.EndUndo();
                AssetDatabase.SaveAssets();
            }
        }

        // ---------- Textures ----------

        private static void GenerateSand(out Texture2D albedo, out Texture2D normal)
        {
            var colors = new Color[Size * Size];
            var height = new float[Size * Size];
            Color light = Hex("#CDB38A"), dark = Hex("#A88A62"), pebble = Hex("#8C7A64");

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float large = EnvGen.TileFbm(u, v, 4, 5, 11);
                    float grain = EnvGen.TileNoise(u, v, 128, 12);
                    float ripples = Mathf.Sin((v * 9f + EnvGen.TileFbm(u, v, 3, 3, 13) * 2.5f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float stones = Mathf.Clamp01((EnvGen.TileNoise(u, v, 64, 14) - 0.82f) * 8f);

                    Color c = Color.Lerp(dark, light, large * 0.8f + grain * 0.2f);
                    c = Color.Lerp(c, pebble, stones * 0.7f);
                    colors[y * Size + x] = c;
                    height[y * Size + x] = large * 0.6f + ripples * 0.15f + grain * 0.15f + stones * 0.4f;
                }
            }

            albedo = EnvGen.SaveTexture(colors, Size, Size, "Sand_Albedo", EnvGen.TextureKind.Color);
            normal = EnvGen.SaveTexture(EnvGen.NormalsFromHeight(height, Size, 6f), Size, Size, "Sand_Normal", EnvGen.TextureKind.Normal);
        }

        /// <summary>Compacted dirt road; u runs across the road (two tire ruts per tile), v along it.</summary>
        private static void GenerateDirt(out Texture2D albedo, out Texture2D normal)
        {
            var colors = new Color[Size * Size];
            var height = new float[Size * Size];
            Color baseColor = Hex("#8E6E4E"), dark = Hex("#5E4632"), light = Hex("#B0916C");

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float large = EnvGen.TileFbm(u, v, 4, 5, 21);
                    float detail = EnvGen.TileFbm(u, v, 32, 3, 22);
                    float pebbles = Mathf.Clamp01((EnvGen.TileNoise(u, v, 96, 23) - 0.78f) * 6f);
                    float wobble = (EnvGen.TileFbm(u, v, 2, 2, 24) - 0.5f) * 0.06f;
                    float rut = Rut(u + wobble, 0.27f) + Rut(u + wobble, 0.73f);

                    Color c = Color.Lerp(dark, light, large * 0.7f + detail * 0.3f);
                    c = Color.Lerp(c, baseColor, 0.35f);
                    c = Color.Lerp(c, dark * 0.85f, rut * 0.45f);
                    c = Color.Lerp(c, light, pebbles * 0.5f);
                    colors[y * Size + x] = c;
                    height[y * Size + x] = large * 0.4f + detail * 0.3f - rut * 0.35f + pebbles * 0.35f;
                }
            }

            albedo = EnvGen.SaveTexture(colors, Size, Size, "Dirt_Albedo", EnvGen.TextureKind.Color);
            normal = EnvGen.SaveTexture(EnvGen.NormalsFromHeight(height, Size, 7f), Size, Size, "Dirt_Normal", EnvGen.TextureKind.Normal);
        }

        private static float Rut(float u, float center)
        {
            float d = Mathf.Abs(u - center) / 0.07f;
            return Mathf.Clamp01(1f - d * d);
        }

        private static void GenerateRock(out Texture2D albedo, out Texture2D normal)
        {
            var colors = new Color[Size * Size];
            var height = new float[Size * Size];
            Color light = Hex("#A39A8C"), dark = Hex("#5F574D"), warm = Hex("#8C7058");

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float large = EnvGen.TileFbm(u, v, 3, 5, 31);
                    float ridged = 1f - Mathf.Abs(EnvGen.TileFbm(u, v, 6, 4, 32) * 2f - 1f);
                    float cracks = Mathf.Clamp01((ridged - 0.86f) * 7f);
                    float strata = Mathf.Sin((v * 7f + large * 1.5f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float grain = EnvGen.TileNoise(u, v, 128, 33);

                    Color c = Color.Lerp(dark, light, large * 0.75f + grain * 0.25f);
                    c = Color.Lerp(c, warm, strata * 0.25f);
                    c = Color.Lerp(c, dark * 0.6f, cracks);
                    colors[y * Size + x] = c;
                    height[y * Size + x] = large * 0.7f + strata * 0.1f - cracks * 0.5f + grain * 0.1f;
                }
            }

            albedo = EnvGen.SaveTexture(colors, Size, Size, "Rock_Albedo", EnvGen.TextureKind.Color);
            normal = EnvGen.SaveTexture(EnvGen.NormalsFromHeight(height, Size, 9f), Size, Size, "Rock_Normal", EnvGen.TextureKind.Normal);
        }

        private static void GenerateWaterNormal()
        {
            const int size = 256;
            var height = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    height[y * size + x] = EnvGen.TileFbm(u, v, 6, 4, 41);
                }
            }

            EnvGen.SaveTexture(EnvGen.NormalsFromHeight(height, size, 4f), size, size, "Water_Normal", EnvGen.TextureKind.Normal);
        }

        // ---------- Visual road over the driving ring ----------

        private static void BuildTrackSurface()
        {
            EnvGen.Track track = EnvGen.GetTrack();
            const int segments = 128;
            const float tileLength = 9f;        // metres of road per texture tile (along)
            const float tilesAcross = 2f;
            // Slightly wider than the driving ring so the edges hide under the wall segments.
            float inner = track.Inner - 0.3f, outer = track.Outer + 0.3f;

            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 6];
            float circumference = 2f * Mathf.PI * track.midRadius;
            float alongTiles = Mathf.Round(circumference / tileLength);

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = t * Mathf.PI * 2f;
                vertices[i * 2] = new Vector3(Mathf.Cos(angle) * inner, 0f, Mathf.Sin(angle) * inner);
                vertices[i * 2 + 1] = new Vector3(Mathf.Cos(angle) * outer, 0f, Mathf.Sin(angle) * outer);
                uvs[i * 2] = new Vector2(0f, t * alongTiles);
                uvs[i * 2 + 1] = new Vector2(tilesAcross, t * alongTiles);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;

                if (i < segments)
                {
                    int b = i * 2, k = i * 6;
                    triangles[k] = b; triangles[k + 1] = b + 2; triangles[k + 2] = b + 1;
                    triangles[k + 3] = b + 1; triangles[k + 4] = b + 2; triangles[k + 5] = b + 3;
                }
            }

            var mesh = new Mesh { vertices = vertices, uv = uvs, normals = normals, triangles = triangles };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh = EnvGen.SaveMesh(mesh, "TrackSurface");

            GameObject root = EnvGen.RecreateRoot("TrackSurface");
            root.transform.position = new Vector3(0f, 0.015f, 0f);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(TrackSurfaceMaterialPath);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }
    }
}
