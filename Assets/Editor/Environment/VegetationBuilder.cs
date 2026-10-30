using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Procedural low-poly vegetation and rocks scattered around the circuit with a
    /// fixed seed: pines, broadleaf trees, bushes, cacti (palette material) and
    /// irregular rocks (rock material). A few mesh variants per type are saved as
    /// assets and shared by every instance; objects are static (batched), have no
    /// colliders, and use a LODGroup that culls them when small on screen. Nothing
    /// is placed on the driving ring, near the walls, or in the infield pond.
    /// </summary>
    public static class VegetationBuilder
    {
        public const string RootName = "Environment_Vegetation";
        private const int Seed = 1234;

        private enum Kind { Pine, Broadleaf, Bush, Cactus, Rock, Boulder }

        private struct Spec
        {
            public Kind kind;
            public int outside;      // count outside the outer wall
            public int infield;      // count inside the inner wall
            public float spacing;    // minimum distance to any other placed object
            public Vector2 scale;
            public float cullHeight; // LOD cull (fraction of screen height)
            public bool castShadows;
        }

        private static readonly Spec[] Specs =
        {
            new Spec { kind = Kind.Pine,      outside = 70,  infield = 6,  spacing = 4.5f, scale = new Vector2(0.8f, 1.4f), cullHeight = 0.012f, castShadows = true },
            new Spec { kind = Kind.Broadleaf, outside = 45,  infield = 6,  spacing = 5f,   scale = new Vector2(0.8f, 1.3f), cullHeight = 0.012f, castShadows = true },
            new Spec { kind = Kind.Cactus,    outside = 35,  infield = 5,  spacing = 3f,   scale = new Vector2(0.7f, 1.3f), cullHeight = 0.015f, castShadows = true },
            new Spec { kind = Kind.Bush,      outside = 150, infield = 28, spacing = 2.2f, scale = new Vector2(0.6f, 1.4f), cullHeight = 0.02f,  castShadows = false },
            new Spec { kind = Kind.Rock,      outside = 90,  infield = 14, spacing = 2.2f, scale = new Vector2(0.4f, 1.3f), cullHeight = 0.02f,  castShadows = false },
            new Spec { kind = Kind.Boulder,   outside = 22,  infield = 3,  spacing = 6f,   scale = new Vector2(1.8f, 3.5f), cullHeight = 0.01f,  castShadows = true },
        };

        [MenuItem("KartIHC/Environment/4 Vegetation and Rocks", priority = 104)]
        public static void BuildVegetation()
        {
            EnvGen.EnsureFolders();
            EnvGen.Track track = EnvGen.GetTrack();
            Material palette = EnvGen.PaletteMaterial();
            Material rock = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Rock.mat") ?? palette;

            EnvGen.BeginUndo("Build Vegetation");
            try
            {
                EditorUtility.DisplayProgressBar("Vegetation", "Meshes", 0.2f);
                var variants = new Dictionary<Kind, Mesh[]>
                {
                    [Kind.Pine] = Variants("Pine", 2, PineMesh),
                    [Kind.Broadleaf] = Variants("Broadleaf", 2, BroadleafMesh),
                    [Kind.Bush] = Variants("Bush", 3, BushMesh),
                    [Kind.Cactus] = Variants("Cactus", 3, CactusMesh),
                    [Kind.Rock] = Variants("Rock", 6, RockMesh),
                };
                variants[Kind.Boulder] = variants[Kind.Rock];

                EditorUtility.DisplayProgressBar("Vegetation", "Scattering", 0.6f);
                GameObject root = EnvGen.RecreateRoot(RootName);
                var random = new System.Random(Seed);
                var placed = new List<Vector4>();   // xyz + spacing

                foreach (Spec spec in Specs)
                {
                    Transform group = new GameObject(spec.kind.ToString()).transform;
                    group.SetParent(root.transform, false);
                    bool isRock = spec.kind == Kind.Rock || spec.kind == Kind.Boulder;
                    Material[] materials = { isRock ? rock : palette };

                    Scatter(spec, spec.outside, false, track, random, placed, variants[spec.kind], materials, group);
                    Scatter(spec, spec.infield, true, track, random, placed, variants[spec.kind], materials, group);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EnvGen.EndUndo();
                AssetDatabase.SaveAssets();
            }
        }

        private static void Scatter(Spec spec, int count, bool infield, EnvGen.Track track, System.Random random,
            List<Vector4> placed, Mesh[] meshes, Material[] materials, Transform parent)
        {
            // Allowed radii: infield keeps 3 m inside the inner wall; outside starts 4 m past the outer wall.
            float minRadius = infield ? 0f : track.Outer + 4f;
            float maxRadius = infield ? track.Inner - 3f : 175f;
            int made = 0;

            for (int attempt = 0; attempt < count * 30 && made < count; attempt++)
            {
                // Area-uniform radius, biased towards the near hills so the circuit looks framed.
                float t = (float)random.NextDouble();
                float radius = infield
                    ? Mathf.Sqrt(t) * maxRadius
                    : Mathf.Lerp(minRadius, maxRadius, t * t);
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                if (infield && Vector3.Distance(position, EnvGen.PondCenter) < EnvGen.PondClearance + spec.spacing * 0.5f)
                {
                    continue;
                }

                if (!infield && radius < minRadius)
                {
                    continue;
                }

                if (TooClose(position, spec.spacing, placed))
                {
                    continue;
                }

                // Cacti prefer flatter, sandy ground; trees avoid the steepest slopes.
                position.y = EnvGen.GroundHeight(position);
                float slope = Slope(position);
                if ((spec.kind == Kind.Cactus && slope > 15f) || (spec.kind != Kind.Rock && spec.kind != Kind.Boulder && slope > 30f))
                {
                    continue;
                }

                float scale = Mathf.Lerp(spec.scale.x, spec.scale.y, (float)random.NextDouble());
                Mesh mesh = meshes[random.Next(meshes.Length)];
                float sink = spec.kind == Kind.Boulder || spec.kind == Kind.Rock ? 0.25f * scale : 0.05f;

                GameObject go = EnvGen.CreateChild(parent, $"{spec.kind}_{made}", mesh, materials,
                    spec.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
                go.transform.position = position - Vector3.up * sink;
                go.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                go.transform.localScale = spec.kind == Kind.Rock || spec.kind == Kind.Boulder
                    ? new Vector3(scale, scale * Mathf.Lerp(0.6f, 1f, (float)random.NextDouble()), scale)
                    : Vector3.one * scale;
                EnvGen.AddCullingLod(go, spec.cullHeight);

                placed.Add(new Vector4(position.x, position.y, position.z, spec.spacing));
                made++;
            }
        }

        private static bool TooClose(Vector3 position, float spacing, List<Vector4> placed)
        {
            foreach (Vector4 other in placed)
            {
                float minDistance = Mathf.Max(spacing, other.w) * 0.75f;
                float dx = other.x - position.x, dz = other.z - position.z;
                if (dx * dx + dz * dz < minDistance * minDistance)
                {
                    return true;
                }
            }
            return false;
        }

        private static float Slope(Vector3 position)
        {
            const float d = 1.5f;
            float hx = EnvGen.GroundHeight(position + Vector3.right * d) - EnvGen.GroundHeight(position - Vector3.right * d);
            float hz = EnvGen.GroundHeight(position + Vector3.forward * d) - EnvGen.GroundHeight(position - Vector3.forward * d);
            return Mathf.Atan(new Vector2(hx, hz).magnitude / (2f * d)) * Mathf.Rad2Deg;
        }

        // ---------- Meshes ----------

        private static Mesh[] Variants(string name, int count, System.Func<int, Mesh> build)
        {
            var meshes = new Mesh[count];
            for (int i = 0; i < count; i++)
            {
                meshes[i] = EnvGen.SaveMesh(build(Seed + i * 31), $"{name}_{i}");
            }
            return meshes;
        }

        private static Mesh PineMesh(int seed)
        {
            var random = new System.Random(seed);
            var mb = new EnvGen.MeshBuilder();
            float trunkHeight = 1.6f + (float)random.NextDouble() * 0.6f;
            mb.Cylinder(0, Vector3.zero, Vector3.up * (trunkHeight + 1f), 0.22f, 0.1f, 6, EnvGen.Uv(EnvGen.Swatch.Trunk));

            Vector2 green = EnvGen.Uv(random.Next(2) == 0 ? EnvGen.Swatch.PineGreen : EnvGen.Swatch.OliveGreen);
            float y = trunkHeight;
            float radius = 2.1f + (float)random.NextDouble() * 0.4f;
            for (int layer = 0; layer < 3; layer++)
            {
                float height = 2.2f - layer * 0.35f;
                mb.Cylinder(0, Vector3.up * y, Vector3.up * (y + height), radius, 0f, 8, green, capBottom: true,
                    twist: layer * 0.4f);
                y += height * 0.55f;
                radius *= 0.72f;
            }
            return mb.ToMesh();
        }

        private static Mesh BroadleafMesh(int seed)
        {
            var random = new System.Random(seed);
            var mb = new EnvGen.MeshBuilder();
            Vector2 trunk = EnvGen.Uv(EnvGen.Swatch.Trunk);
            Vector3 bend = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 0.8f;
            Vector3 mid = Vector3.up * 1.6f + bend * 0.5f;
            Vector3 top = Vector3.up * 3.2f + bend;
            mb.Cylinder(0, Vector3.zero, mid, 0.28f, 0.2f, 6, trunk);
            mb.Cylinder(0, mid, top, 0.2f, 0.12f, 6, trunk);

            Vector2 leaves = EnvGen.Uv(random.Next(3) == 0 ? EnvGen.Swatch.DryGreen : EnvGen.Swatch.OliveGreen);
            int clumps = 3 + random.Next(2);
            for (int i = 0; i < clumps; i++)
            {
                float angle = i / (float)clumps * Mathf.PI * 2f + (float)random.NextDouble();
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0.2f * (float)random.NextDouble(), Mathf.Sin(angle)) * 1.1f;
                float size = 1.3f + (float)random.NextDouble() * 0.5f;
                mb.Blob(0, top + offset + Vector3.up * 0.4f, new Vector3(size, size * 0.7f, size), 1, 0.35f, seed + i, leaves);
            }
            mb.Blob(0, top + Vector3.up * 0.9f, new Vector3(1.6f, 1.1f, 1.6f), 1, 0.3f, seed + 9, leaves);
            return mb.ToMesh();
        }

        private static Mesh BushMesh(int seed)
        {
            var random = new System.Random(seed);
            var mb = new EnvGen.MeshBuilder();
            Vector2 color = EnvGen.Uv(random.Next(3) == 0 ? EnvGen.Swatch.DryGreen : EnvGen.Swatch.BushGreen);
            int clumps = 2 + random.Next(2);
            for (int i = 0; i < clumps; i++)
            {
                Vector3 offset = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 1.1f;
                float size = 0.55f + (float)random.NextDouble() * 0.35f;
                mb.Blob(0, offset + Vector3.up * size * 0.55f, new Vector3(size, size * 0.75f, size), 1, 0.4f, seed + i, color);
            }
            return mb.ToMesh();
        }

        private static Mesh CactusMesh(int seed)
        {
            var random = new System.Random(seed);
            var mb = new EnvGen.MeshBuilder();
            Vector2 green = EnvGen.Uv(EnvGen.Swatch.CactusGreen);
            float height = 2.6f + (float)random.NextDouble() * 1.2f;
            const float radius = 0.32f;

            mb.Cylinder(0, Vector3.zero, Vector3.up * height, radius, radius * 0.9f, 8, green, capTop: false);
            mb.Cylinder(0, Vector3.up * height, Vector3.up * (height + 0.3f), radius * 0.9f, 0f, 8, green);

            int arms = 1 + random.Next(2);
            for (int i = 0; i < arms; i++)
            {
                float side = i == 0 ? 1f : -1f;
                float armY = height * (0.35f + (float)random.NextDouble() * 0.2f);
                float armOut = 0.7f + (float)random.NextDouble() * 0.2f;
                float armUp = 0.8f + (float)random.NextDouble() * 0.6f;
                Vector3 basePoint = new Vector3(0f, armY, 0f);
                Vector3 elbow = new Vector3(side * armOut, armY + 0.1f, 0f);
                Vector3 tip = elbow + Vector3.up * armUp;
                mb.Cylinder(0, basePoint, elbow, radius * 0.6f, radius * 0.6f, 6, green);
                mb.Cylinder(0, elbow - Vector3.up * 0.15f, tip, radius * 0.6f, radius * 0.55f, 6, green, capTop: false);
                mb.Cylinder(0, tip, tip + Vector3.up * 0.2f, radius * 0.55f, 0f, 6, green);
            }
            return mb.ToMesh();
        }

        private static Mesh RockMesh(int seed)
        {
            var random = new System.Random(seed);
            var mb = new EnvGen.MeshBuilder { boxUvScale = 0.6f };
            Vector3 radii = new Vector3(
                0.8f + (float)random.NextDouble() * 0.5f,
                0.55f + (float)random.NextDouble() * 0.35f,
                0.7f + (float)random.NextDouble() * 0.5f);
            mb.Blob(0, Vector3.up * radii.y * 0.6f, radii, 2, 0.45f, seed, Vector2.zero);
            return mb.ToMesh();
        }
    }
}
