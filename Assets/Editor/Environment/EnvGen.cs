using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Shared helpers for the KartIHC/Environment builders: generated asset folders,
    /// track geometry (read from the StartLine), tileable noise, PNG textures, URP Lit
    /// materials, a shared color palette, a small mesh builder, and undoable scene roots.
    /// </summary>
    public static class EnvGen
    {
        public const string Root = "Assets/Generated/Environment";
        public const string TexturesFolder = Root + "/Textures";
        public const string MeshesFolder = Root + "/Meshes";
        public const string MaterialsFolder = Root + "/Materials";

        // Infield pond (stage 6); vegetation keeps away from it.
        public static readonly Vector3 PondCenter = new Vector3(0f, 0f, 0f);
        // The pond outline reaches up to 1.5x its radius (noise x1.2, oval x1.25); the mud rim adds 1.8 m.
        public const float PondRadius = 6.5f;
        public const float PondClearance = 13f;

        // ---------- Track geometry ----------

        public struct Track
        {
            public float midRadius;
            public float width;
            public float startAngle;      // radians, position of the StartLine on the circle
            public float direction;       // +1 counter-clockwise (seen from above), -1 clockwise
            public float Inner => midRadius - width * 0.5f;
            public float Outer => midRadius + width * 0.5f;

            public Vector3 PointAt(float angle, float radius) =>
                new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            public Vector3 TangentAt(float angle) =>
                direction * new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }

        /// <summary>TrackBuilder's oval: a ring around the origin through the StartLine.</summary>
        public static Track GetTrack()
        {
            var track = new Track { midRadius = 39f, width = 18f, startAngle = 0f, direction = 1f };
            GameObject startLine = GameObject.Find("StartLine");
            if (startLine == null)
            {
                Debug.LogWarning("[EnvGen] No 'StartLine' found; using TrackBuilder's default oval (r 39, width 18).");
                return track;
            }

            Vector3 p = startLine.transform.position;
            track.midRadius = new Vector2(p.x, p.z).magnitude;
            track.width = startLine.transform.localScale.x;
            track.startAngle = Mathf.Atan2(p.z, p.x);
            Vector3 forward = Vector3.ProjectOnPlane(startLine.transform.forward, Vector3.up).normalized;
            Vector3 ccw = new Vector3(-Mathf.Sin(track.startAngle), 0f, Mathf.Cos(track.startAngle));
            track.direction = Vector3.Dot(forward, ccw) >= 0f ? 1f : -1f;
            return track;
        }

        /// <summary>Ground height at a world XZ point: the flat track plane (y=0) or the terrain, whichever is higher.</summary>
        public static float GroundHeight(Vector3 position)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, terrain.SampleHeight(position) + terrain.transform.position.y);
        }

        // ---------- Scene roots / undo ----------

        public static void BeginUndo(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
        }

        public static void EndUndo()
        {
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        /// <summary>Destroys the previous root with that name (undoable) and creates an empty one.</summary>
        public static GameObject RecreateRoot(string name)
        {
            RemoveRoot(name);
            GameObject root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Create " + name);
            return root;
        }

        public static void RemoveRoot(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
        }

        public static GameObject CreateChild(Transform parent, string name, Mesh mesh, Material[] materials,
            ShadowCastingMode shadows, bool isStatic = true)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = shadows;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (isStatic)
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
            return go;
        }

        /// <summary>Single-LOD LODGroup: the object is culled when it gets smaller than cullHeight of the screen.</summary>
        public static void AddCullingLod(GameObject go, float cullHeight)
        {
            LODGroup group = go.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(cullHeight, go.GetComponentsInChildren<Renderer>()) });
            group.RecalculateBounds();
        }

        // ---------- Assets ----------

        public static void EnsureFolders()
        {
            EnsureFolder("Assets/Generated");
            EnsureFolder(Root);
            EnsureFolder(TexturesFolder);
            EnsureFolder(MeshesFolder);
            EnsureFolder(MaterialsFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Saves (or overwrites in place, keeping references) a mesh asset.</summary>
        public static Mesh SaveMesh(Mesh mesh, string name)
        {
            EnsureFolders();
            string path = $"{MeshesFolder}/{name}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }

            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public enum TextureKind { Color, Normal, PointColor }

        /// <summary>Writes a PNG under Textures/, configures its importer and returns the imported texture.</summary>
        public static Texture2D SaveTexture(Color[] pixels, int width, int height, string name, TextureKind kind,
            TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            EnsureFolders();
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, kind == TextureKind.Normal);
            texture.SetPixels(pixels);
            texture.Apply();
            string path = $"{TexturesFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = kind == TextureKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.wrapMode = wrap;
            importer.sRGBTexture = kind != TextureKind.Normal;
            importer.mipmapEnabled = kind != TextureKind.PointColor;
            importer.filterMode = kind == TextureKind.PointColor ? FilterMode.Point : FilterMode.Trilinear;
            importer.anisoLevel = kind == TextureKind.PointColor ? 0 : 4;
            importer.alphaIsTransparency = true;
            importer.textureCompression = kind == TextureKind.PointColor ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Texture2D LoadTexture(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{name}.png");

        /// <summary>Loads a URP Lit material at the path, or creates it. Records undo on existing ones.</summary>
        public static Material GetOrCreateLit(string path)
        {
            EnsureFolders();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                Undo.RecordObject(material, "Edit " + material.name);
                return material;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static void SetLit(Material material, Color color, Texture2D baseMap, Texture2D normalMap, float normalScale,
            float smoothness, Vector2 tiling)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (material.shader != lit)
            {
                material.shader = lit;
            }

            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);

            if (normalMap != null)
            {
                material.SetTexture("_BumpMap", normalMap);
                material.SetFloat("_BumpScale", normalScale);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        /// <summary>Switches a URP Lit material to alpha-blended transparent.</summary>
        public static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        // ---------- Palette (one texture/material for all vegetation and props) ----------

        public enum Swatch
        {
            Trunk, PineGreen, OliveGreen, DryGreen, BushGreen, CactusGreen,
            Orange, White, Dark, Red, Yellow, Blue, Reed, Metal, DarkBrown, Spare
        }

        private static readonly string[] SwatchHex =
        {
            "#5A3E28", "#2F4A2A", "#5E6B34", "#8C8A45", "#4E6230", "#4F7A3A",
            "#FF6A13", "#F2F2F2", "#1E1E1E", "#D7263D", "#F4C21E", "#1E6FD9", "#9C8B5A", "#7A7A7A", "#3B2A1C", "#FFFFFF"
        };

        public const string PaletteMaterialPath = MaterialsFolder + "/Mat_EnvPalette.mat";

        public static Vector2 Uv(Swatch swatch) => new Vector2(((int)swatch + 0.5f) / SwatchHex.Length, 0.5f);

        /// <summary>Palette material (creates its texture the first time).</summary>
        public static Material PaletteMaterial()
        {
            Texture2D palette = LoadTexture("Palette");
            if (palette == null)
            {
                var pixels = new Color[SwatchHex.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    ColorUtility.TryParseHtmlString(SwatchHex[i], out pixels[i]);
                }
                palette = SaveTexture(pixels, pixels.Length, 1, "Palette", TextureKind.PointColor, TextureWrapMode.Clamp);
            }

            Material material = GetOrCreateLit(PaletteMaterialPath);
            SetLit(material, Color.white, palette, null, 0f, 0.15f, Vector2.one);
            return material;
        }

        // ---------- Noise ----------

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static int Wrap(int value, int period) => ((value % period) + period) % period;

        /// <summary>Tileable value noise in [0,1]; u,v in [0,1) repeat every 1 with 'period' cells.</summary>
        public static float TileNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(Wrap(x0, period), Wrap(y0, period), seed);
            float b = Hash(Wrap(x0 + 1, period), Wrap(y0, period), seed);
            float c = Hash(Wrap(x0, period), Wrap(y0 + 1, period), seed);
            float d = Hash(Wrap(x0 + 1, period), Wrap(y0 + 1, period), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable fractal noise in [0,1].</summary>
        public static float TileFbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amplitude = 0.5f, norm = 0f;
            int period = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                sum += TileNoise(u, v, period, seed + i * 101) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        /// <summary>Non-tiling fractal Perlin noise in [0,1] for world-space use.</summary>
        public static float Fbm(float x, float y, int octaves, float seedOffset)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * frequency + seedOffset + i * 17.3f, y * frequency - seedOffset + i * 9.1f) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / norm;
        }

        /// <summary>Tangent-space normal map from a tileable height field.</summary>
        public static Color[] NormalsFromHeight(float[] height, int size, float strength)
        {
            var normals = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float left = height[y * size + Wrap(x - 1, size)];
                    float right = height[y * size + Wrap(x + 1, size)];
                    float down = height[Wrap(y - 1, size) * size + x];
                    float up = height[Wrap(y + 1, size) * size + x];
                    Vector3 n = new Vector3((left - right) * strength, (down - up) * strength, 1f).normalized;
                    normals[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            return normals;
        }

        // ---------- Mesh building ----------

        /// <summary>Flat-shaded low-poly mesh builder with submeshes.</summary>
        public class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] submeshes;

            /// <summary>When > 0, triangles get box-projected UVs (world units * this) instead of the given UV.</summary>
            public float boxUvScale;

            public MeshBuilder(int submeshCount = 1)
            {
                submeshes = new List<int>[submeshCount];
                for (int i = 0; i < submeshCount; i++)
                {
                    submeshes[i] = new List<int>();
                }
            }

            public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f)
                {
                    return;
                }
                normal.Normalize();

                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);

                if (boxUvScale > 0f)
                {
                    uvs.Add(BoxUv(a, normal)); uvs.Add(BoxUv(b, normal)); uvs.Add(BoxUv(c, normal));
                }
                else
                {
                    uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
                }

                submeshes[submesh].Add(start);
                submeshes[submesh].Add(start + 1);
                submeshes[submesh].Add(start + 2);
            }

            public void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv)
            {
                Triangle(submesh, a, b, c, uv);
                Triangle(submesh, a, c, d, uv);
            }

            private Vector2 BoxUv(Vector3 p, Vector3 n)
            {
                Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                if (an.y >= an.x && an.y >= an.z) return new Vector2(p.x, p.z) * boxUvScale;
                if (an.x >= an.z) return new Vector2(p.z, p.y) * boxUvScale;
                return new Vector2(p.x, p.y) * boxUvScale;
            }

            /// <summary>Tapered prism between two points, with optional caps.</summary>
            public void Cylinder(int submesh, Vector3 bottom, Vector3 top, float radiusBottom, float radiusTop, int sides,
                Vector2 uv, bool capBottom = false, bool capTop = true, float twist = 0f)
            {
                Vector3 axis = (top - bottom).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 side2 = Vector3.Cross(axis, side);

                for (int i = 0; i < sides; i++)
                {
                    float a0 = (i / (float)sides) * Mathf.PI * 2f + twist;
                    float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f + twist;
                    Vector3 d0 = side * Mathf.Cos(a0) + side2 * Mathf.Sin(a0);
                    Vector3 d1 = side * Mathf.Cos(a1) + side2 * Mathf.Sin(a1);
                    Vector3 b0 = bottom + d0 * radiusBottom, b1 = bottom + d1 * radiusBottom;
                    Vector3 t0 = top + d0 * radiusTop, t1 = top + d1 * radiusTop;

                    // Winding: Unity's front face is clockwise, i.e. normal = cross(b - a, c - a) points outwards.
                    if (radiusTop > 0.0001f)
                    {
                        Quad(submesh, b0, b1, t1, t0, uv);
                    }
                    else
                    {
                        Triangle(submesh, b0, b1, top, uv);
                    }

                    if (capTop && radiusTop > 0.0001f) Triangle(submesh, top, t0, t1, uv);
                    if (capBottom) Triangle(submesh, bottom, b1, b0, uv);
                }
            }

            /// <summary>Noisy icosphere (rocks, foliage clumps). Displacement is per shared vertex, so it stays closed.</summary>
            public void Blob(int submesh, Vector3 center, Vector3 radii, int subdivisions, float roughness, int seed, Vector2 uv)
            {
                Icosphere(subdivisions, out List<Vector3> points, out List<int> triangles);
                var random = new System.Random(seed);
                float offset = seed * 0.37f;
                var displaced = new Vector3[points.Count];
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 p = points[i];
                    float lowFrequency = Mathf.PerlinNoise(p.x * 1.3f + offset, p.z * 1.3f + p.y + offset) - 0.5f;
                    float jitter = (float)random.NextDouble() - 0.5f;
                    float scale = 1f + roughness * (lowFrequency * 1.2f + jitter * 0.5f);
                    displaced[i] = center + Vector3.Scale(p * scale, radii);
                }

                for (int i = 0; i < triangles.Count; i += 3)
                {
                    Triangle(submesh, displaced[triangles[i]], displaced[triangles[i + 1]], displaced[triangles[i + 2]], uv);
                }
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = submeshes.Length;
                for (int i = 0; i < submeshes.Length; i++)
                {
                    mesh.SetTriangles(submeshes[i], i);
                }
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }

        private static void Icosphere(int subdivisions, out List<Vector3> points, out List<int> triangles)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            points = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;

            triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>();
                List<Vector3> pts = points;
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int index)) return index;
                    pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
                    cache[key] = pts.Count - 1;
                    return pts.Count - 1;
                }

                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles = next;
            }
        }

        // ---------- Menu: remove everything generated in the scene ----------

        public static readonly string[] GeneratedRoots =
        {
            "TrackSurface", "Environment_Terrain", "Environment_Vegetation", "Environment_TrackProps", "Environment_Water"
        };

        [MenuItem("KartIHC/Environment/Build All (1-7)", priority = 150)]
        public static void BuildAll()
        {
            EnvironmentSetup.SetupEnvironment();
            ProceduralMaterials.GenerateMaterials();
            EnvironmentBuilder.BuildTerrain();
            VegetationBuilder.BuildVegetation();
            TrackPropsBuilder.BuildProps();
            WaterBuilder.BuildWater();
            KartVFXSetup.SetupKartVFX();
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        [MenuItem("KartIHC/Environment/Remove Generated Scene Objects", priority = 200)]
        public static void RemoveGeneratedObjects()
        {
            BeginUndo("Remove Generated Environment");
            foreach (string root in GeneratedRoots)
            {
                RemoveRoot(root);
            }
            EndUndo();
        }
    }
}
