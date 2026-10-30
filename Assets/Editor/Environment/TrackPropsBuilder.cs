using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Track dressing, all visual only (no colliders) and placed just behind the
    /// walls so nothing changes on the driving ring: orange posts with white
    /// stripes along both edges, cones at the start and at each quarter of the
    /// lap, a START / META arch over the start line, and strings of pennants.
    /// Everything shares the palette material (plus a checker for the arch beam).
    /// </summary>
    public static class TrackPropsBuilder
    {
        public const string RootName = "Environment_TrackProps";
        private const string CheckerMaterialPath = EnvGen.MaterialsFolder + "/Mat_Checker.mat";
        private const string SignMaterialPath = EnvGen.MaterialsFolder + "/Mat_SignPanel.mat";

        private const float EdgeOffset = 0.9f;       // distance behind the wall's centre line
        private const float PostEveryDegrees = 10f;
        private const float ArchHeight = 6.2f;

        [MenuItem("KartIHC/Environment/5 Track Props (Posts, Cones, Arch, Pennants)", priority = 105)]
        public static void BuildProps()
        {
            EnvGen.EnsureFolders();
            EnvGen.Track track = EnvGen.GetTrack();
            Material palette = EnvGen.PaletteMaterial();
            Material[] paletteOnly = { palette };

            EnvGen.BeginUndo("Build Track Props");

            Mesh post = EnvGen.SaveMesh(PostMesh(), "Prop_Post");
            Mesh cone = EnvGen.SaveMesh(ConeMesh(), "Prop_Cone");
            Mesh pole = EnvGen.SaveMesh(PoleMesh(), "Prop_FlagPole");

            GameObject root = EnvGen.RecreateRoot(RootName);
            Transform posts = Group(root, "Posts");
            Transform cones = Group(root, "Cones");
            Transform pennants = Group(root, "Pennants");

            float innerEdge = track.Inner - EdgeOffset;
            float outerEdge = track.Outer + EdgeOffset;

            // Posts along both edges.
            int postCount = Mathf.RoundToInt(360f / PostEveryDegrees);
            for (int i = 0; i < postCount; i++)
            {
                if (i == 0) continue;      // the arch pillars stand there
                float angle = track.startAngle + i * PostEveryDegrees * Mathf.Deg2Rad;
                Place(posts, $"Post_In_{i}", post, paletteOnly, track.PointAt(angle, innerEdge), angle, 0.015f);
                Place(posts, $"Post_Out_{i}", post, paletteOnly, track.PointAt(angle, outerEdge), angle, 0.015f);
            }

            // Cones: a row at the start on both sides, and small clusters at each quarter.
            for (int i = -4; i <= 4; i++)
            {
                if (Mathf.Abs(i) <= 1) continue;   // keep clear of the arch pillars
                float angle = track.startAngle + i * 1.5f * Mathf.Deg2Rad;
                Place(cones, $"Cone_StartIn_{i}", cone, paletteOnly, track.PointAt(angle, innerEdge - 0.6f), angle, 0.02f);
                Place(cones, $"Cone_StartOut_{i}", cone, paletteOnly, track.PointAt(angle, outerEdge + 0.6f), angle, 0.02f);
            }
            for (int quarter = 1; quarter < 4; quarter++)
            {
                float baseAngle = track.startAngle + quarter * Mathf.PI * 0.5f;
                for (int k = -1; k <= 1; k++)
                {
                    float angle = baseAngle + k * 1.2f * Mathf.Deg2Rad;
                    Place(cones, $"Cone_Q{quarter}_In_{k}", cone, paletteOnly, track.PointAt(angle, innerEdge - 0.6f), angle, 0.02f);
                    Place(cones, $"Cone_Q{quarter}_Out_{k}", cone, paletteOnly, track.PointAt(angle, outerEdge + 0.6f), angle, 0.02f);
                }
            }

            BuildArch(root.transform, track, palette);

            // Pennant strings between tall poles behind the outer and inner walls, around the start.
            Mesh pennantMeshOuter = null, pennantMeshInner = null;
            Vector3[] outerPoles = PolesAround(track, outerEdge + 1.2f, 7, 4f);
            Vector3[] innerPoles = PolesAround(track, innerEdge - 1.2f, 7, 4f);
            for (int i = 0; i < outerPoles.Length; i++)
            {
                Place(pennants, $"Pole_Out_{i}", pole, paletteOnly, outerPoles[i], 0f, 0.015f);
                Place(pennants, $"Pole_In_{i}", pole, paletteOnly, innerPoles[i], 0f, 0.015f);
            }
            pennantMeshOuter = EnvGen.SaveMesh(PennantString(outerPoles, 3.4f), "Prop_PennantsOuter");
            pennantMeshInner = EnvGen.SaveMesh(PennantString(innerPoles, 3.4f), "Prop_PennantsInner");
            EnvGen.CreateChild(pennants, "PennantString_Out", pennantMeshOuter, paletteOnly, ShadowCastingMode.Off);
            EnvGen.CreateChild(pennants, "PennantString_In", pennantMeshInner, paletteOnly, ShadowCastingMode.Off);

            EnvGen.EndUndo();
            AssetDatabase.SaveAssets();
        }

        private static Transform Group(GameObject root, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(root.transform, false);
            return group;
        }

        private static void Place(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 position, float angle, float cull)
        {
            GameObject go = EnvGen.CreateChild(parent, name, mesh, materials, ShadowCastingMode.On);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
            EnvGen.AddCullingLod(go, cull);
        }

        private static Vector3[] PolesAround(EnvGen.Track track, float radius, int count, float degreesApart)
        {
            var poles = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = track.startAngle + (i - (count - 1) * 0.5f) * degreesApart * Mathf.Deg2Rad;
                // Skip the arch pillar spot at the line itself by nudging the middle pole sideways.
                if (i == (count - 1) / 2) angle += 1.2f * Mathf.Deg2Rad;
                poles[i] = track.PointAt(angle, radius);
            }
            return poles;
        }

        // ---------- Arch ----------

        private static void BuildArch(Transform root, EnvGen.Track track, Material palette)
        {
            Transform arch = new GameObject("StartArch").transform;
            arch.SetParent(root, false);

            Vector3 radial = track.PointAt(track.startAngle, 1f);
            Vector3 tangent = track.TangentAt(track.startAngle);
            float innerX = track.Inner - 1.6f, outerX = track.Outer + 1.6f;
            Vector3 center = radial * ((innerX + outerX) * 0.5f);
            arch.position = center;
            arch.rotation = Quaternion.LookRotation(tangent, Vector3.up);   // local +z = racing direction, local x = radial

            float halfSpan = (outerX - innerX) * 0.5f;
            Mesh pillar = EnvGen.SaveMesh(PillarMesh(ArchHeight), "Prop_ArchPillar");
            Material[] paletteOnly = { palette };
            GameObject left = EnvGen.CreateChild(arch, "Pillar_Inner", pillar, paletteOnly, ShadowCastingMode.On);
            left.transform.localPosition = new Vector3(-halfSpan * Mathf.Sign(Vector3.Dot(arch.right, radial)), 0f, 0f);
            GameObject right = EnvGen.CreateChild(arch, "Pillar_Outer", pillar, paletteOnly, ShadowCastingMode.On);
            right.transform.localPosition = -left.transform.localPosition;

            // Checkered beam across the track.
            Material checker = CheckerMaterial();
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "Beam";
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(arch, false);
            beam.transform.localPosition = new Vector3(0f, ArchHeight - 0.6f, 0f);
            beam.transform.localScale = new Vector3(halfSpan * 2f + 0.8f, 1.2f, 0.5f);
            beam.GetComponent<MeshRenderer>().sharedMaterial = checker;
            GameObjectUtility.SetStaticEditorFlags(beam, StaticEditorFlags.BatchingStatic);

            // Dark sign panels with the text on both faces.
            Material sign = SignMaterial();
            for (int side = 0; side < 2; side++)
            {
                float facing = side == 0 ? -1f : 1f;      // -1: faces karts approaching the line
                GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = side == 0 ? "Sign_Front" : "Sign_Back";
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                panel.transform.SetParent(arch, false);
                panel.transform.localPosition = new Vector3(0f, ArchHeight + 0.75f, facing * 0.05f);
                panel.transform.localScale = new Vector3(9f, 1.5f, 0.2f);
                panel.GetComponent<MeshRenderer>().sharedMaterial = sign;
                GameObjectUtility.SetStaticEditorFlags(panel, StaticEditorFlags.BatchingStatic);

                var textGO = new GameObject(side == 0 ? "Text_Front" : "Text_Back");
                textGO.transform.SetParent(arch, false);
                textGO.transform.localPosition = new Vector3(0f, ArchHeight + 0.75f, facing * 0.17f);
                textGO.transform.localRotation = side == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                TextMesh text = textGO.AddComponent<TextMesh>();
                text.text = "START  ·  META";
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 96;
                text.characterSize = 0.1f;
                text.fontStyle = FontStyle.Bold;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = new Color(1f, 0.62f, 0.15f);
                textGO.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
                textGO.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // Pennants hanging under the beam.
            Vector3 a = new Vector3(-halfSpan, ArchHeight - 1.2f, 0f);
            Vector3 b = new Vector3(halfSpan, ArchHeight - 1.2f, 0f);
            Mesh beamPennants = EnvGen.SaveMesh(PennantString(new[] { a, b }, 0f, sag: 0.25f), "Prop_PennantsArch");
            EnvGen.CreateChild(arch, "Pennants_Beam", beamPennants, paletteOnly, ShadowCastingMode.Off);
        }

        private static Material CheckerMaterial()
        {
            Texture2D texture = EnvGen.LoadTexture("Checker");
            if (texture == null)
            {
                const int size = 8;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = ((x + y) & 1) == 0 ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.08f, 0.08f, 0.08f);
                texture = EnvGen.SaveTexture(pixels, size, size, "Checker", EnvGen.TextureKind.PointColor);
            }

            Material material = EnvGen.GetOrCreateLit(CheckerMaterialPath);
            EnvGen.SetLit(material, Color.white, texture, null, 0f, 0.3f, new Vector2(4f, 0.25f));
            return material;
        }

        private static Material SignMaterial()
        {
            Material material = EnvGen.GetOrCreateLit(SignMaterialPath);
            EnvGen.SetLit(material, new Color(0.07f, 0.08f, 0.1f), null, null, 0f, 0.4f, Vector2.one);
            return material;
        }

        // ---------- Prop meshes (palette UVs) ----------

        private static Mesh PostMesh()
        {
            var mb = new EnvGen.MeshBuilder();
            Vector2 orange = EnvGen.Uv(EnvGen.Swatch.Orange), white = EnvGen.Uv(EnvGen.Swatch.White), dark = EnvGen.Uv(EnvGen.Swatch.Dark);
            const float r = 0.09f;
            mb.Cylinder(0, Vector3.zero, Vector3.up * 0.08f, 0.18f, 0.16f, 8, dark, capTop: true);
            mb.Cylinder(0, Vector3.up * 0.08f, Vector3.up * 0.75f, r, r, 8, orange, capTop: false);
            mb.Cylinder(0, Vector3.up * 0.75f, Vector3.up * 0.95f, r, r, 8, white, capTop: false);
            mb.Cylinder(0, Vector3.up * 0.95f, Vector3.up * 1.15f, r, r, 8, orange, capTop: false);
            mb.Cylinder(0, Vector3.up * 1.15f, Vector3.up * 1.3f, r, r, 8, white, capTop: true);
            return mb.ToMesh();
        }

        private static Mesh ConeMesh()
        {
            var mb = new EnvGen.MeshBuilder();
            Vector2 orange = EnvGen.Uv(EnvGen.Swatch.Orange), white = EnvGen.Uv(EnvGen.Swatch.White), dark = EnvGen.Uv(EnvGen.Swatch.Dark);
            // Square rubber base.
            Vector3 p0 = new Vector3(-0.22f, 0f, -0.22f), p1 = new Vector3(0.22f, 0f, -0.22f);
            Vector3 p2 = new Vector3(0.22f, 0f, 0.22f), p3 = new Vector3(-0.22f, 0f, 0.22f);
            Vector3 up = Vector3.up * 0.04f;
            mb.Quad(0, p0 + up, p3 + up, p2 + up, p1 + up, dark);
            mb.Quad(0, p0, p0 + up, p1 + up, p1, dark);
            mb.Quad(0, p1, p1 + up, p2 + up, p2, dark);
            mb.Quad(0, p2, p2 + up, p3 + up, p3, dark);
            mb.Quad(0, p3, p3 + up, p0 + up, p0, dark);
            // Body with a white reflective band.
            mb.Cylinder(0, Vector3.up * 0.04f, Vector3.up * 0.3f, 0.16f, 0.11f, 10, orange, capTop: false);
            mb.Cylinder(0, Vector3.up * 0.3f, Vector3.up * 0.42f, 0.11f, 0.085f, 10, white, capTop: false);
            mb.Cylinder(0, Vector3.up * 0.42f, Vector3.up * 0.7f, 0.085f, 0.02f, 10, orange, capTop: true);
            return mb.ToMesh();
        }

        private static Mesh PoleMesh()
        {
            var mb = new EnvGen.MeshBuilder();
            mb.Cylinder(0, Vector3.zero, Vector3.up * 3.6f, 0.06f, 0.05f, 6, EnvGen.Uv(EnvGen.Swatch.Metal));
            mb.Cylinder(0, Vector3.up * 3.6f, Vector3.up * 3.75f, 0.08f, 0f, 6, EnvGen.Uv(EnvGen.Swatch.Red));
            return mb.ToMesh();
        }

        private static Mesh PillarMesh(float height)
        {
            var mb = new EnvGen.MeshBuilder();
            Vector2 orange = EnvGen.Uv(EnvGen.Swatch.Orange), white = EnvGen.Uv(EnvGen.Swatch.White), dark = EnvGen.Uv(EnvGen.Swatch.Dark);
            mb.Cylinder(0, Vector3.zero, Vector3.up * 0.4f, 0.55f, 0.5f, 4, dark, twist: Mathf.PI * 0.25f);
            int bands = 6;
            float bandHeight = (height - 0.4f) / bands;
            for (int i = 0; i < bands; i++)
            {
                float y0 = 0.4f + i * bandHeight;
                mb.Cylinder(0, Vector3.up * y0, Vector3.up * (y0 + bandHeight), 0.4f, 0.4f, 4, i % 2 == 0 ? orange : white,
                    capTop: i == bands - 1, twist: Mathf.PI * 0.25f);
            }
            return mb.ToMesh();
        }

        /// <summary>Double-sided triangular pennants hanging along a sagging line through the given points.</summary>
        private static Mesh PennantString(Vector3[] anchors, float anchorHeight, float sag = 0.5f)
        {
            var mb = new EnvGen.MeshBuilder();
            EnvGen.Swatch[] colors = { EnvGen.Swatch.Red, EnvGen.Swatch.Yellow, EnvGen.Swatch.Blue, EnvGen.Swatch.White, EnvGen.Swatch.Orange };
            int colorIndex = 0;
            const float flagWidth = 0.45f, flagDrop = 0.55f, gap = 0.12f;

            for (int s = 0; s < anchors.Length - 1; s++)
            {
                Vector3 a = anchors[s] + Vector3.up * anchorHeight;
                Vector3 b = anchors[s + 1] + Vector3.up * anchorHeight;
                float length = Vector3.Distance(a, b);
                int flags = Mathf.Max(1, Mathf.FloorToInt(length / (flagWidth + gap)));

                for (int f = 0; f < flags; f++)
                {
                    float t0 = (f * (flagWidth + gap) + gap * 0.5f) / length;
                    float t1 = t0 + flagWidth / length;
                    Vector3 p0 = Catenary(a, b, t0, sag), p1 = Catenary(a, b, t1, sag);
                    Vector3 tip = Catenary(a, b, (t0 + t1) * 0.5f, sag) - Vector3.up * flagDrop;
                    Vector2 uv = EnvGen.Uv(colors[colorIndex++ % colors.Length]);
                    mb.Triangle(0, p0, p1, tip, uv);
                    mb.Triangle(0, p1, p0, tip, uv);
                }
            }
            return mb.ToMesh();
        }

        private static Vector3 Catenary(Vector3 a, Vector3 b, float t, float sag)
        {
            return Vector3.Lerp(a, b, t) - Vector3.up * (sag * 4f * t * (1f - t));
        }
    }
}
