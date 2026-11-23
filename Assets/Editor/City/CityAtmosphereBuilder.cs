using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Etapa 4 de la ciudad: ambiente de "hora azul" para EscapeCourse.
    /// - Cielo panorámico generado (degradado azul, resplandor cálido al oeste, estrellas).
    /// - Luz direccional fría y baja, sombras suaves moderadas, ambiente Trilight y niebla exp².
    /// - Volume propio (prioridad 10, sección City_Atmosphere del prefab) con ACES, etalonaje,
    ///   bloom a un cuarto de resolución y viñeta. No modifica SampleSceneProfile,
    ///   Skybox_Offroad ni los assets de URP (los comparte SampleScene).
    /// Los ajustes originales de la escena se guardan en Settings/AtmosphereBackup.json
    /// y "Remove Generated City" los restaura.
    /// </summary>
    public static class CityAtmosphereBuilder
    {
        public const string SectionName = "City_Atmosphere";
        private const string SkyTextureName = "Sky_BlueHour";
        private const string SkyMaterialPath = CityGen.MaterialsFolder + "/Sky_BlueHour.mat";
        private const string ProfilePath = CityGen.SettingsFolder + "/VolumeProfile_CityBlueHour.asset";
        private const string BackupPath = CityGen.SettingsFolder + "/AtmosphereBackup.json";
        private const int SkyWidth = 2048;
        private const int SkyHeight = 1024;
        private const int SkySeed = 1127;

        // Azimut 0 = +X, 90 = +Z (norte, hacia la comisaría). El resplandor del sol ya puesto
        // queda al oeste-suroeste; la luz principal ("cielo") llega desde ese lado, alta,
        // para que el dron (al sur, mirando al norte) vea las caras iluminadas.
        private const float GlowAzimuth = 195f;
        private const float KeyAzimuth = 215f;
        private const float KeyElevation = 40f;

        // ===== Valores para ajustar rápido (vuelve a correr "4 Atmosphere (Blue Hour)") =====
        // Nota: los materiales de Diego (Escape_Road/Ground/Barrier) ya son grises azulados y
        // oscuros; por eso la luz y el etalonaje van casi neutros, para no volverlos azul marino.

        // Luz principal: lavanda casi neutra, fuerte; sombras suaves para que la pista no se ennegrezca.
        private static readonly Color KeyColor = CityGen.Hex("#D8D4F0");
        private const float KeyIntensity = 1.4f;
        private const float KeyShadowStrength = 0.55f;

        // Ambiente Trilight. "Sky" ilumina lo que mira hacia arriba (pista y suelo: es la que más
        // los aclara); "Ground" ilumina lo que mira hacia abajo y los bajos del kart.
        private static readonly Color AmbientSky = CityGen.Hex("#8C92BC");
        private static readonly Color AmbientEquator = CityGen.Hex("#8E8698");
        private static readonly Color AmbientGround = CityGen.Hex("#5E5C68");
        private const float ReflectionIntensity = 0.6f;

        // Niebla exp²: ~10 % a 60 m (el anillo entero se ve), ~26 % a 100 m, ~50 % a 150 m.
        private static readonly Color FogColor = CityGen.Hex("#7A7A98");
        private const float FogDensity = 0.0055f;

        // Cielo: un poco más oscuro que la escena para conservar la sensación de anochecer.
        private const float SkyExposure = 0.8f;

        // Post-procesado.
        private const float PostExposure = 0.7f;
        private const float Contrast = 16f;      // separa pista, suelo y barreras
        private const float Saturation = 8f;     // el kart rojo y las puertas naranjas destacan
        private const float WhiteBalanceTemperature = -3f; // apenas frío: el rojo no vira a morado
        private const float WhiteBalanceTint = 4f;          // leve magenta = lavanda
        private static readonly Color SplitShadows = CityGen.Hex("#6B6E80");    // casi gris: asfalto gris, no azul
        private static readonly Color SplitHighlights = CityGen.Hex("#E6C29C"); // luces cálidas
        private const float SplitBalance = 20f;  // positivo = menos tinte en sombras
        private const float BloomThreshold = 1f; // solo emisivos HDR
        private const float BloomIntensity = 0.6f;
        private const float VignetteIntensity = 0.22f;

        [MenuItem(CityGen.MenuRoot + "4 Atmosphere (Blue Hour)", priority = 304)]
        public static void Build()
        {
            if (!CityGen.EnsureEscapeScene())
            {
                return;
            }

            EnvGen.BeginUndo("City Atmosphere (Blue Hour)");
            try
            {
                Material sky = BuildSkyMaterial();
                VolumeProfile profile = BuildVolumeProfile();
                BackupSceneLightingIfNeeded(sky);
                CityGen.ReplacePrefabSection(SectionName, section => AddVolume(section, profile));
                CityGen.EnsureSingleSceneInstance();
                ApplySceneLighting(sky);
            }
            finally
            {
                EnvGen.EndUndo();
            }

            CityGen.MarkSceneDirty();
            AssetDatabase.SaveAssets();
            Debug.Log("[City] Ambiente de hora azul aplicado. Guarda la escena para conservarlo.");
        }

        /// <summary>Entrada para -batchmode -executeMethod: abre EscapeCourse, aplica y guarda.</summary>
        public static void BuildFromCommandLine()
        {
            EditorSceneManager.OpenScene(CityGen.ScenePath);
            Build();
            EditorSceneManager.SaveOpenScenes();
        }

        // ---------- Cielo ----------

        private static Material BuildSkyMaterial()
        {
            Texture2D texture = CityGen.SaveTexture(GenerateSkyPixels(), SkyWidth, SkyHeight, SkyTextureName, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;          // evita la costura del panorama
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC7: sin bandas visibles
                importer.maxTextureSize = SkyWidth;
            });

            Material material = CityGen.GetOrCreateMaterial(SkyMaterialPath, "Skybox/Panoramic");
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f)); // neutro
            material.SetFloat("_Exposure", SkyExposure);
            material.SetFloat("_Rotation", 0f);
            material.SetFloat("_Mapping", 1f);   // latitud-longitud
            material.SetFloat("_ImageType", 0f); // 360°
            material.SetFloat("_MirrorOnBack", 0f);
            material.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            material.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Panorama lat-long como lo lee Skybox/Panoramic: u = 0.5 - azimut/2π, v = 0.5 + elevación/π.
        /// </summary>
        private static Color[] GenerateSkyPixels()
        {
            Color zenith = CityGen.Hex("#0B1433");
            Color upper = CityGen.Hex("#1D2F63");
            Color middle = CityGen.Hex("#3A4C84");
            Color low = CityGen.Hex("#6A6590");
            Color horizon = CityGen.Hex("#86708F");
            Color below = CityGen.Hex("#1E1F2A");
            Color glowCore = CityGen.Hex("#E3895A");
            Color glowWide = CityGen.Hex("#B8708A");
            Color cityHaze = CityGen.Hex("#6E5C72");

            var pixels = new Color[SkyWidth * SkyHeight];
            var random = new System.Random(SkySeed);
            for (int y = 0; y < SkyHeight; y++)
            {
                float elevation = ((y + 0.5f) / SkyHeight - 0.5f) * 180f;
                for (int x = 0; x < SkyWidth; x++)
                {
                    float azimuth = (0.5f - (x + 0.5f) / SkyWidth) * 360f;
                    float fromGlow = Mathf.Abs(Mathf.DeltaAngle(azimuth, GlowAzimuth));
                    Color color;
                    if (elevation >= 0f)
                    {
                        color = elevation < 4f ? Color.Lerp(horizon, low, elevation / 4f)
                            : elevation < 16f ? Color.Lerp(low, middle, Smooth((elevation - 4f) / 12f))
                            : elevation < 45f ? Color.Lerp(middle, upper, Smooth((elevation - 16f) / 29f))
                            : Color.Lerp(upper, zenith, Smooth((elevation - 45f) / 45f));

                        float core = Mathf.Exp(-Sq(fromGlow / 32f)) * Mathf.Exp(-elevation / 6f);
                        float wide = Mathf.Exp(-Sq(fromGlow / 85f)) * Mathf.Exp(-elevation / 20f);
                        color += glowCore * (0.42f * core) + glowWide * (0.22f * wide);
                    }
                    else
                    {
                        color = Color.Lerp(horizon, below, Smooth(Mathf.Clamp01(-elevation / 5f)));
                    }

                    // Resplandor de la ciudad sobre todo el horizonte.
                    color += cityHaze * (0.22f * Mathf.Exp(-Mathf.Abs(elevation) / 5f));
                    // Dither de ±0.5/255 para que el degradado no forme bandas al cuantizar.
                    float dither = ((float)random.NextDouble() - 0.5f) / 255f;
                    color.r += dither;
                    color.g += dither;
                    color.b += dither;
                    color.a = 1f;
                    pixels[y * SkyWidth + x] = color;
                }
            }

            AddStars(pixels, random);
            return pixels;
        }

        private static void AddStars(Color[] pixels, System.Random random)
        {
            const int starCount = 1400;
            for (int i = 0; i < starCount; i++)
            {
                // Dirección uniforme sobre la semiesfera superior.
                float elevation = Mathf.Asin((float)random.NextDouble()) * Mathf.Rad2Deg;
                float azimuth = (float)random.NextDouble() * 360f;
                float visibility = Smooth(Mathf.InverseLerp(14f, 40f, elevation));
                // Menos estrellas dentro del resplandor del oeste.
                visibility *= 1f - 0.8f * Mathf.Exp(-Sq(Mathf.DeltaAngle(azimuth, GlowAzimuth) / 45f));
                if (visibility <= 0.02f)
                {
                    continue;
                }

                float brightness = Mathf.Lerp(0.25f, 0.85f, Sq((float)random.NextDouble())) * visibility;
                Color tint = Color.Lerp(CityGen.Hex("#CFE0FF"), CityGen.Hex("#FFF1D6"), (float)random.NextDouble());
                float u = Mathf.Repeat(0.5f - azimuth / 360f, 1f);
                float v = 0.5f + elevation / 180f;
                int px = Mathf.Clamp((int)(u * SkyWidth), 0, SkyWidth - 1);
                int py = Mathf.Clamp((int)(v * SkyHeight), 0, SkyHeight - 1);
                pixels[py * SkyWidth + px] += tint * brightness;
                // Halo tenue de 1 px para que las más brillantes sobrevivan al filtrado.
                if (brightness > 0.5f)
                {
                    float halo = brightness * 0.25f;
                    AddPixel(pixels, px + 1, py, tint * halo);
                    AddPixel(pixels, px - 1, py, tint * halo);
                    AddPixel(pixels, px, py + 1, tint * halo);
                    AddPixel(pixels, px, py - 1, tint * halo);
                }
            }
        }

        private static void AddPixel(Color[] pixels, int x, int y, Color add)
        {
            if (y < 0 || y >= SkyHeight)
            {
                return;
            }
            x = (x % SkyWidth + SkyWidth) % SkyWidth;
            pixels[y * SkyWidth + x] += add;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static float Sq(float t) => t * t;

        // ---------- Post-procesado ----------

        private static VolumeProfile BuildVolumeProfile()
        {
            CityGen.EnsureFolders();
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.Override(PostExposure);
            color.contrast.Override(Contrast);
            color.saturation.Override(Saturation);
            color.colorFilter.Override(Color.white);

            WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>(profile);
            whiteBalance.temperature.Override(WhiteBalanceTemperature);
            whiteBalance.tint.Override(WhiteBalanceTint);

            // Sombras casi neutras y luces cálidas (faroles), sin teñir el asfalto de azul.
            SplitToning splitToning = GetOrAdd<SplitToning>(profile);
            splitToning.shadows.Override(SplitShadows);
            splitToning.highlights.Override(SplitHighlights);
            splitToning.balance.Override(SplitBalance);

            // Umbral 1: solo brillan emisivos HDR (ventanas, faroles, faros), no el cielo.
            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(5);

            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(0.4f);
            vignette.color.Override(Color.black);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            component.active = true;
            EditorUtility.SetDirty(component);
            return component;
        }

        private static void AddVolume(Transform section, VolumeProfile profile)
        {
            var volumeObject = new GameObject("City_PostVolume");
            volumeObject.transform.SetParent(section, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f; // por encima del Global Volume de la escena (prioridad 0)
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        // ---------- Ajustes de escena ----------

        private static void ApplySceneLighting(Material sky)
        {
            RecordRenderSettings();
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = ReflectionIntensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.fogColor = FogColor;

            Light key = FindSceneSun();
            if (key != null)
            {
                Undo.RecordObject(key, "City Key Light");
                Undo.RecordObject(key.transform, "City Key Light");
                key.useColorTemperature = false;
                key.color = KeyColor;
                key.intensity = KeyIntensity;
                key.shadows = LightShadows.Soft;
                key.shadowStrength = KeyShadowStrength;
                key.transform.rotation = Quaternion.LookRotation(-CityGen.SkyDirection(KeyAzimuth, KeyElevation), Vector3.up);
                RenderSettings.sun = key;
            }
            else
            {
                Debug.LogWarning("[City] No hay luz direccional en la escena; solo se aplican cielo, ambiente y niebla.");
            }

            DynamicGI.UpdateEnvironment();
        }

        private static Light FindSceneSun()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
            {
                return RenderSettings.sun;
            }

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    return light;
                }
            }
            return null;
        }

        /// <summary>RenderSettings no tiene instancia pública; se registra por el accesor interno.</summary>
        private static void RecordRenderSettings()
        {
            MethodInfo getter = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
            if (getter?.Invoke(null, null) is Object renderSettings)
            {
                Undo.RecordObject(renderSettings, "Render Settings");
            }
        }

        // ---------- Copia de seguridad ----------

        [Serializable]
        private class AtmosphereBackup
        {
            public string skyboxGuid;
            public AmbientMode ambientMode;
            public Color ambientSky, ambientEquator, ambientGround;
            public float ambientIntensity;
            public DefaultReflectionMode reflectionMode;
            public float reflectionIntensity;
            public bool fog;
            public FogMode fogMode;
            public Color fogColor;
            public float fogDensity, fogStart, fogEnd;
            public bool hasLight;
            public bool lightUseTemperature;
            public Color lightColor;
            public float lightIntensity;
            public LightShadows lightShadows;
            public float lightShadowStrength;
            public Quaternion lightRotation;
        }

        /// <summary>Guarda los ajustes actuales si todavía no son los de la ciudad (cielo distinto).</summary>
        private static void BackupSceneLightingIfNeeded(Material citySky)
        {
            if (RenderSettings.skybox == citySky && File.Exists(BackupPath))
            {
                return;
            }

            Light key = FindSceneSun();
            var backup = new AtmosphereBackup
            {
                skyboxGuid = RenderSettings.skybox != null
                    ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(RenderSettings.skybox)) : "",
                ambientMode = RenderSettings.ambientMode,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                reflectionMode = RenderSettings.defaultReflectionMode,
                reflectionIntensity = RenderSettings.reflectionIntensity,
                fog = RenderSettings.fog,
                fogMode = RenderSettings.fogMode,
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance,
                hasLight = key != null,
            };
            if (key != null)
            {
                backup.lightUseTemperature = key.useColorTemperature;
                backup.lightColor = key.color;
                backup.lightIntensity = key.intensity;
                backup.lightShadows = key.shadows;
                backup.lightShadowStrength = key.shadowStrength;
                backup.lightRotation = key.transform.rotation;
            }

            CityGen.EnsureFolders();
            File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
            AssetDatabase.ImportAsset(BackupPath);
        }

        /// <summary>Restaura los ajustes guardados (con Undo). Lo usa "Remove Generated City".</summary>
        public static void RestoreSceneLighting()
        {
            if (!File.Exists(BackupPath))
            {
                return;
            }

            var backup = JsonUtility.FromJson<AtmosphereBackup>(File.ReadAllText(BackupPath));
            RecordRenderSettings();
            string skyPath = AssetDatabase.GUIDToAssetPath(backup.skyboxGuid);
            RenderSettings.skybox = string.IsNullOrEmpty(skyPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            RenderSettings.ambientMode = backup.ambientMode;
            RenderSettings.ambientSkyColor = backup.ambientSky;
            RenderSettings.ambientEquatorColor = backup.ambientEquator;
            RenderSettings.ambientGroundColor = backup.ambientGround;
            RenderSettings.ambientIntensity = backup.ambientIntensity;
            RenderSettings.defaultReflectionMode = backup.reflectionMode;
            RenderSettings.reflectionIntensity = backup.reflectionIntensity;
            RenderSettings.fog = backup.fog;
            RenderSettings.fogMode = backup.fogMode;
            RenderSettings.fogColor = backup.fogColor;
            RenderSettings.fogDensity = backup.fogDensity;
            RenderSettings.fogStartDistance = backup.fogStart;
            RenderSettings.fogEndDistance = backup.fogEnd;

            Light key = FindSceneSun();
            if (backup.hasLight && key != null)
            {
                Undo.RecordObject(key, "Restore Key Light");
                Undo.RecordObject(key.transform, "Restore Key Light");
                key.useColorTemperature = backup.lightUseTemperature;
                key.color = backup.lightColor;
                key.intensity = backup.lightIntensity;
                key.shadows = backup.lightShadows;
                key.shadowStrength = backup.lightShadowStrength;
                key.transform.rotation = backup.lightRotation;
            }

            DynamicGI.UpdateEnvironment();
        }
    }
}
