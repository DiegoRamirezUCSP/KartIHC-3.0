using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Builds a Unity Terrain with gentle hills around the circuit. Inside the
    /// driving area the terrain stays flat 0.3 m BELOW the existing Ground plane,
    /// so the track floor and its colliders are untouched; hills only rise from a
    /// few metres outside the outer wall. Textured with sand / dirt / rock layers
    /// (rock on steep slopes) from stage 2.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public const string RootName = "Environment_Terrain";
        private const string TerrainDataPath = EnvGen.Root + "/OffroadTerrain.asset";
        private const string TerrainMaterialPath = EnvGen.MaterialsFolder + "/Mat_Terrain.mat";

        private const float TerrainSize = 440f;
        private const float MaxHeight = 45f;
        private const float SunkenOffset = -0.3f;
        private const int HeightmapResolution = 257;
        private const int AlphamapResolution = 256;

        [MenuItem("KartIHC/Environment/3 Terrain (Hills)", priority = 103)]
        public static void BuildTerrain()
        {
            EnvGen.EnsureFolders();
            EnvGen.Track track = EnvGen.GetTrack();

            if (EnvGen.LoadTexture("Sand_Albedo") == null)
            {
                Debug.LogWarning("[EnvironmentBuilder] Run 'KartIHC/Environment/2 Materials' first for terrain textures.");
            }

            EnvGen.BeginUndo("Build Terrain");

            GameObject root = EnvGen.RecreateRoot(RootName);

            AssetDatabase.DeleteAsset(TerrainDataPath);
            var data = new TerrainData
            {
                heightmapResolution = HeightmapResolution,
                alphamapResolution = AlphamapResolution,
                baseMapResolution = 512,
            };
            data.size = new Vector3(TerrainSize, MaxHeight, TerrainSize);
            data.terrainLayers = CreateLayers();
            data.SetHeights(0, 0, ComputeHeights(track));
            data.SetAlphamaps(0, 0, ComputeSplat(data));
            AssetDatabase.CreateAsset(data, TerrainDataPath);

            GameObject terrainGO = Terrain.CreateTerrainGameObject(data);
            terrainGO.name = "OffroadTerrain";
            terrainGO.transform.SetParent(root.transform, false);
            terrainGO.transform.position = new Vector3(-TerrainSize * 0.5f, SunkenOffset, -TerrainSize * 0.5f);

            Terrain terrain = terrainGO.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 220f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;
            terrain.drawTreesAndFoliage = false;
            GameObjectUtility.SetStaticEditorFlags(terrainGO, StaticEditorFlags.BatchingStatic);

            EnvGen.EndUndo();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = terrainGO;
        }

        /// <summary>World height (above y=0) of the hills at an XZ point; 0 inside the circuit area.</summary>
        public static float HillHeight(float x, float z, EnvGen.Track track)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float start = track.Outer + 6f;

            // Smooth ramp from flat to hills; far ring rises into a bigger ridge that closes the horizon.
            float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, start + 45f, r));
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start + 70f, TerrainSize * 0.5f, r));

            float rolling = EnvGen.Fbm(x * 0.012f, z * 0.012f, 4, 3.7f);
            float ridges = EnvGen.Fbm(x * 0.006f, z * 0.006f, 3, 8.1f);

            return near * (2.5f + rolling * 14f) + far * (ridges * 22f);
        }

        private static float[,] ComputeHeights(EnvGen.Track track)
        {
            int res = HeightmapResolution;
            var heights = new float[res, res];
            for (int iz = 0; iz < res; iz++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float x = ix / (float)(res - 1) * TerrainSize - TerrainSize * 0.5f;
                    float z = iz / (float)(res - 1) * TerrainSize - TerrainSize * 0.5f;
                    // Terrain origin is SunkenOffset below y=0. Flat areas stay at the origin (hidden under
                    // the Ground plane); within the first metre of rise the offset is blended in, so hills
                    // reach their world height without a step.
                    float hill = HillHeight(x, z, track);
                    float local = hill - SunkenOffset * Mathf.Clamp01(hill);
                    heights[iz, ix] = Mathf.Clamp01(local / MaxHeight);
                }
            }
            return heights;
        }

        /// <summary>Layer weights: rock on steep slopes, dirt patches by noise, sand elsewhere.</summary>
        private static float[,,] ComputeSplat(TerrainData data)
        {
            int res = AlphamapResolution;
            var maps = new float[res, res, 3];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)(res - 1), v = y / (float)(res - 1);
                    float slope = data.GetSteepness(u, v);
                    float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 34f, slope));
                    float dirt = Mathf.Clamp01((EnvGen.Fbm(u * 9f, v * 9f, 3, 5.3f) - 0.45f) * 2.5f) * (1f - rock);
                    float sand = Mathf.Max(0f, 1f - rock - dirt);
                    float sum = rock + dirt + sand;
                    maps[y, x, 0] = sand / sum;
                    maps[y, x, 1] = dirt / sum;
                    maps[y, x, 2] = rock / sum;
                }
            }
            return maps;
        }

        private static TerrainLayer[] CreateLayers()
        {
            return new[]
            {
                Layer("Layer_Sand", "Sand_Albedo", "Sand_Normal", 6f, 0.8f),
                Layer("Layer_Dirt", "Dirt_Albedo", "Dirt_Normal", 9f, 1f),
                Layer("Layer_Rock", "Rock_Albedo", "Rock_Normal", 10f, 1.2f),
            };
        }

        private static TerrainLayer Layer(string name, string albedo, string normal, float tileSize, float normalScale)
        {
            string path = $"{EnvGen.MaterialsFolder}/{name}.terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }

            layer.diffuseTexture = EnvGen.LoadTexture(albedo);
            layer.normalMapTexture = EnvGen.LoadTexture(normal);
            layer.normalScale = normalScale;
            layer.tileSize = new Vector2(tileSize, tileSize);
            layer.smoothness = 0.1f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static Material TerrainMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (shader == null)
                {
                    return null;     // Unity falls back to the pipeline's default terrain material.
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, TerrainMaterialPath);
            }

            material.enableInstancing = true;
            return material;
        }
    }
}
