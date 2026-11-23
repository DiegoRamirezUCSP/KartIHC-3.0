using KartIHC.Kart;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Etapa 5 de la ciudad: aspecto del Jeep (KartBody/JeepModel) en EscapeCourse.
    /// - Pintura roja brillante (metalizado ligero que refleja el cielo), cristal oscuro y
    ///   lentes emisivas de faros y pilotos. Son copias en Generated/City/Materials asignadas
    ///   como overrides de la instancia; los materiales del asset del Jeep no se tocan.
    /// - City_KartLights (hijo de JeepModel): dos focos sin sombras y KartLights, que
    ///   enciende las luces de freno.
    /// No toca WheelColliders, Rigidbody, BoxCollider ni la calibración (KartPhysicsCalibrator
    /// mide la malla "Base", que no cambia). Nada nuevo lleva Collider.
    /// Si alguien vuelve a correr KartIHC/Build Kart, basta con volver a correr este menú.
    /// </summary>
    public static class CityKartLookBuilder
    {
        public const string LightsRootName = "City_KartLights";
        private static readonly string[] Parts = { "Base", "Glass", "Lights_Front", "Lights_Back" };

        // ===== Valores para ajustar rápido (vuelve a correr "5 Kart Look") =====
        private static readonly Color PaintTint = new Color(1f, 0.26f, 0.22f); // el original es (1, 0.37, 0.37)
        private const float PaintSmoothness = 0.62f;
        private const float PaintMetallic = 0.2f;
        private static readonly Color GlassColor = CityGen.Hex("#1C232C");
        private const float GlassSmoothness = 0.92f;
        private static readonly Color HeadlightColor = CityGen.Hex("#FFF1D6");
        private const float HeadlightLensEmission = 3f;
        // Rojo casi puro: con algo de verde, ACES y el etalonaje cálido lo vuelven naranja.
        private static readonly Color TailColor = new Color(1f, 0.01f, 0.005f);
        private const float TailDimEmission = 0.5f;   // pilotos de posición: rojo tenue
        private const float TailBrakeEmission = 4.5f; // frenando: rojo intenso, entra en el bloom
        private const float HeadlightIntensity = 2.5f;
        private const float HeadlightRange = 28f;
        private const float HeadlightAngle = 55f;
        private const float HeadlightTiltDegrees = 6f; // hacia abajo
        private const float HeadlightSpread = 0.7f;    // fracción del ancho del faro donde va cada foco

        [MenuItem(CityGen.MenuRoot + "5 Kart Look", priority = 305)]
        public static void Build()
        {
            if (!CityGen.EnsureEscapeScene() || !FindParts(out Transform kartBody, out Transform jeep, out Renderer[] renderers))
            {
                return;
            }

            EnvGen.BeginUndo("City Kart Look");
            try
            {
                Material original = OriginalMaterial(renderers[0]);
                Material paint = Lit("Mat_KartPaint", PaintTint, original != null ? original.GetTexture("_BaseMap") : null,
                    PaintSmoothness, PaintMetallic, Color.black);
                Material glass = Lit("Mat_KartGlass", GlassColor, null, GlassSmoothness, 0f, Color.black);
                Material headlight = Lit("Mat_KartHeadlight", Color.white, null, 0.8f, 0f, HeadlightColor * HeadlightLensEmission);
                Material tailDim = Lit("Mat_KartTailDim", TailColor * 0.35f, null, 0.7f, 0f, TailColor * TailDimEmission);
                Material tailBrake = Lit("Mat_KartTailBrake", TailColor * 0.8f, null, 0.7f, 0f, TailColor * TailBrakeEmission);

                Assign(renderers[0], paint);
                Assign(renderers[1], glass);
                Assign(renderers[2], headlight);
                Assign(renderers[3], tailDim);

                RemoveLights(jeep);
                var root = new GameObject(LightsRootName);
                Undo.RegisterCreatedObjectUndo(root, "Create " + LightsRootName);
                root.transform.SetParent(jeep, false);
                Light[] lights = CreateHeadlights(root.transform, kartBody, renderers[2].bounds);
                KartLights component = Undo.AddComponent<KartLights>(root);
                component.Configure(renderers[3], tailDim, tailBrake, renderers[0], lights);
                CityGen.AssertNoColliders(root);
            }
            finally
            {
                EnvGen.EndUndo();
            }

            CityGen.MarkSceneDirty();
            AssetDatabase.SaveAssets();
            Debug.Log("[City] Aspecto del kart aplicado (pintura, cristal, faros y luces de freno). Guarda la escena.");
        }

        /// <summary>Deshace el aspecto del kart (con Undo). Lo usa "Remove Generated City".</summary>
        public static void Remove()
        {
            if (!FindParts(out _, out Transform jeep, out Renderer[] renderers, quiet: true))
            {
                return;
            }

            RemoveLights(jeep);
            foreach (Renderer renderer in renderers)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                {
                    PrefabUtility.RevertObjectOverride(renderer, InteractionMode.UserAction);
                }
            }
        }

        // ---------- Partes ----------

        private static bool FindParts(out Transform kartBody, out Transform jeep, out Renderer[] renderers, bool quiet = false)
        {
            kartBody = GameObject.Find("KartBody")?.transform;
            jeep = kartBody != null ? kartBody.Find("JeepModel") : null;
            renderers = new Renderer[Parts.Length];
            if (jeep == null)
            {
                if (!quiet) Report("No se encontró KartBody/JeepModel en la escena (corre KartIHC/Build Kart).");
                return false;
            }

            for (int i = 0; i < Parts.Length; i++)
            {
                Transform part = FindDeep(jeep, Parts[i]);
                renderers[i] = part != null ? part.GetComponent<Renderer>() : null;
                if (renderers[i] == null)
                {
                    if (!quiet) Report($"No se encontró la pieza '{Parts[i]}' del Jeep.");
                    return false;
                }
            }
            return true;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }
            return null;
        }

        private static void Report(string message)
        {
            Debug.LogError("[City] " + message);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("KartIHC City · Kart", message, "OK");
        }

        /// <summary>Material del prefab original (aunque la instancia ya tenga el nuestro).</summary>
        private static Material OriginalMaterial(Renderer renderer)
        {
            Renderer source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            return source != null ? source.sharedMaterial : renderer.sharedMaterial;
        }

        private static void Assign(Renderer renderer, Material material)
        {
            Undo.RecordObject(renderer, "Kart material");
            renderer.sharedMaterials = new[] { material };
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        // ---------- Luces ----------

        private static void RemoveLights(Transform jeep)
        {
            Transform previous = jeep.Find(LightsRootName);
            if (previous != null)
            {
                Undo.DestroyObjectImmediate(previous.gameObject);
            }
        }

        private static Light[] CreateHeadlights(Transform parent, Transform kartBody, Bounds lens)
        {
            Vector3 forward = kartBody.forward;
            Vector3 right = kartBody.right;
            if (Vector3.Dot(lens.center - kartBody.position, forward) < 0f)
            {
                Debug.LogWarning("[City] Lights_Front queda detrás del centro del kart; revisa la orientación del Jeep.");
            }

            Vector3 e = lens.extents;
            float halfWidth = Mathf.Abs(e.x * right.x) + Mathf.Abs(e.y * right.y) + Mathf.Abs(e.z * right.z);
            Quaternion aim = Quaternion.LookRotation(Quaternion.AngleAxis(HeadlightTiltDegrees, right) * forward, Vector3.up);

            var lights = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var go = new GameObject(i == 0 ? "City_Headlight_L" : "City_Headlight_R");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(lens.center + right * (side * halfWidth * HeadlightSpread) + forward * 0.15f, aim);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = HeadlightColor;
                light.intensity = HeadlightIntensity;
                light.range = HeadlightRange;
                light.spotAngle = HeadlightAngle;
                light.innerSpotAngle = HeadlightAngle * 0.55f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.Auto;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                lights[i] = light;
            }
            return lights;
        }

        // ---------- Materiales ----------

        private static Material Lit(string name, Color color, Texture texture, float smoothness, float metallic, Color emission)
        {
            Material material = CityGen.GetOrCreateMaterial($"{CityGen.MaterialsFolder}/{name}.mat", "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            bool emissive = emission.maxColorComponent > 0f;
            material.SetColor("_EmissionColor", emissive ? emission : Color.black);
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
