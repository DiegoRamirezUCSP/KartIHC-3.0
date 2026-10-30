using KartIHC.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// An irregular pond in the infield (behind the inner wall, so the kart can't
    /// reach it): a wet-mud rim and a translucent, glossy water surface that
    /// reflects the sky, with scrolling ripples (WaterRipples) and reeds around.
    /// Flat meshes just above the ground, no colliders.
    /// </summary>
    public static class WaterBuilder
    {
        public const string RootName = "Environment_Water";
        private const string WaterMaterialPath = EnvGen.MaterialsFolder + "/Mat_Water.mat";
        private const string MudMaterialPath = EnvGen.MaterialsFolder + "/Mat_WetMud.mat";
        private const int Seed = 77;

        [MenuItem("KartIHC/Environment/6 Water (Infield Pond)", priority = 106)]
        public static void BuildWater()
        {
            EnvGen.EnsureFolders();
            EnvGen.Track track = EnvGen.GetTrack();
            if (EnvGen.PondClearance > track.Inner - 2f)
            {
                Debug.LogWarning("[WaterBuilder] The infield is too small for the pond; skipped.");
                return;
            }

            EnvGen.BeginUndo("Build Water");

            Mesh mud = EnvGen.SaveMesh(PondMesh(EnvGen.PondRadius + 1.8f, Seed), "Pond_Mud");
            Mesh water = EnvGen.SaveMesh(PondMesh(EnvGen.PondRadius, Seed), "Pond_Water");

            GameObject root = EnvGen.RecreateRoot(RootName);
            root.transform.position = EnvGen.PondCenter;

            GameObject mudGO = EnvGen.CreateChild(root.transform, "Mud", mud, new[] { MudMaterial() }, ShadowCastingMode.Off);
            mudGO.transform.localPosition = Vector3.up * 0.03f;

            GameObject waterGO = EnvGen.CreateChild(root.transform, "Water", water, new[] { WaterMaterial() }, ShadowCastingMode.Off, isStatic: false);
            waterGO.transform.localPosition = Vector3.up * 0.06f;
            waterGO.GetComponent<MeshRenderer>().receiveShadows = true;
            waterGO.GetComponent<MeshRenderer>().reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            waterGO.AddComponent<WaterRipples>();

            BuildReeds(root.transform);

            EnvGen.EndUndo();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Irregular disc (triangle fan) with noisy radius; UVs in metres / 4.</summary>
        private static Mesh PondMesh(float radius, int seed)
        {
            const int segments = 48;
            var vertices = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var normals = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uvs[0] = Vector2.zero;

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float noise = Mathf.PerlinNoise(Mathf.Cos(angle) * 1.4f + seed, Mathf.Sin(angle) * 1.4f + seed);
                float r = radius * (0.75f + noise * 0.45f) * (1f + 0.25f * Mathf.Cos(angle));   // slightly oval
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                uvs[i + 1] = new Vector2(vertices[i + 1].x, vertices[i + 1].z) / 4f;
            }

            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            for (int i = 0; i < segments; i++)
            {
                // Clockwise seen from above (front face up).
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh { vertices = vertices, uv = uvs, normals = normals, triangles = triangles };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material WaterMaterial()
        {
            Material material = EnvGen.GetOrCreateLit(WaterMaterialPath);
            Texture2D normal = EnvGen.LoadTexture("Water_Normal");
            EnvGen.SetLit(material, new Color(0.16f, 0.3f, 0.32f, 0.72f), null, normal, 0.35f, 0.95f, Vector2.one);
            EnvGen.MakeTransparent(material);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_SpecularHighlights", 1f);
            return material;
        }

        private static Material MudMaterial()
        {
            Material material = EnvGen.GetOrCreateLit(MudMaterialPath);
            EnvGen.SetLit(material, new Color(0.42f, 0.33f, 0.25f), EnvGen.LoadTexture("Dirt_Albedo"), EnvGen.LoadTexture("Dirt_Normal"),
                0.6f, 0.55f, new Vector2(0.6f, 0.6f));
            return material;
        }

        private static void BuildReeds(Transform root)
        {
            var mb = new EnvGen.MeshBuilder();
            var random = new System.Random(Seed);
            Vector2 reed = EnvGen.Uv(EnvGen.Swatch.Reed), green = EnvGen.Uv(EnvGen.Swatch.DryGreen);

            for (int clump = 0; clump < 14; clump++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = EnvGen.PondRadius * (0.85f + (float)random.NextDouble() * 0.35f);
                Vector3 center = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                int blades = 5 + random.Next(5);
                for (int b = 0; b < blades; b++)
                {
                    Vector3 basePoint = center + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 0.6f;
                    Vector3 lean = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 0.5f;
                    float height = 0.8f + (float)random.NextDouble() * 0.9f;
                    mb.Cylinder(0, basePoint, basePoint + Vector3.up * height + lean, 0.035f, 0f, 3,
                        random.Next(3) == 0 ? green : reed);
                }
            }

            Mesh mesh = EnvGen.SaveMesh(mb.ToMesh(), "Pond_Reeds");
            GameObject go = EnvGen.CreateChild(root, "Reeds", mesh, new[] { EnvGen.PaletteMaterial() }, ShadowCastingMode.Off);
            EnvGen.AddCullingLod(go, 0.02f);
        }
    }
}
