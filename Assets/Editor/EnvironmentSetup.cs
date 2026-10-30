using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Editor utility that dresses the scene with a warm late-afternoon off-road
    /// atmosphere: procedural sky with a low sun, soft-shadowed directional light,
    /// light exponential fog, sky ambient, moderate URP shadow settings and a URP
    /// Volume with tonemapping, color grading, white balance, bloom and vignette.
    /// Undoable (Ctrl/Cmd+Z) and safe to run again.
    /// </summary>
    public static class EnvironmentSetup
    {
        private const string SkyboxPath = "Assets/Materials/Skybox_Offroad.mat";
        private const string VolumeProfilePath = "Assets/Settings/VolumeProfile_Offroad.asset";

        [MenuItem("KartIHC/Setup Environment")]
        [MenuItem("KartIHC/Environment/1 Atmosphere (Sky, Light, Fog, Post)", priority = 101)]
        public static void SetupEnvironment()
        {
            EnvGen.BeginUndo("Setup Environment");

            RecordRenderSettings();
            SetupSkybox();
            SetupFog();
            SetupLighting();
            SetupShadowQuality();
            SetupCamera();
            SetupPostProcessing();
            DynamicGI.UpdateEnvironment();

            EnvGen.EndUndo();
            AssetDatabase.SaveAssets();
        }

        /// <summary>RenderSettings has no public instance; record it through the internal accessor when available.</summary>
        private static void RecordRenderSettings()
        {
            MethodInfo getter = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
            if (getter?.Invoke(null, null) is Object renderSettings)
            {
                Undo.RecordObject(renderSettings, "Render Settings");
            }
        }

        private static void SetupSkybox()
        {
            Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (skybox == null)
            {
                Shader skyShader = Shader.Find("Skybox/Procedural");
                if (skyShader == null)
                {
                    Debug.LogWarning("[EnvironmentSetup] Shader 'Skybox/Procedural' not found in this project. Skipping skybox setup.");
                    return;
                }

                skybox = new Material(skyShader);
                AssetDatabase.CreateAsset(skybox, SkyboxPath);
            }
            else
            {
                Undo.RecordObject(skybox, "Skybox");
            }

            // Late afternoon: warm, slightly hazy atmosphere with a crisp sun disk.
            skybox.SetFloat("_SunDisk", 2f);               // high quality
            skybox.SetFloat("_SunSize", 0.045f);
            skybox.SetFloat("_SunSizeConvergence", 6f);
            skybox.SetFloat("_AtmosphereThickness", 1.25f);
            skybox.SetColor("_SkyTint", Hex("#B9A48C"));
            skybox.SetColor("_GroundColor", Hex("#6B5A48"));
            skybox.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(skybox);

            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.8f;
        }

        private static void SetupFog()
        {
            // Exponential squared: clear near the kart, hazy on the far hills (~30% at 100 m, ~75% at 200 m).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0055f;
            RenderSettings.fogColor = Hex("#D9B896");
        }

        private static void SetupLighting()
        {
            GameObject lightGO = GameObject.Find("Directional Light");
            if (lightGO == null)
            {
                Debug.LogWarning("[EnvironmentSetup] No 'Directional Light' GameObject found in the scene. Skipping lighting setup.");
                return;
            }

            Light directionalLight = lightGO.GetComponent<Light>();
            if (directionalLight == null)
            {
                Debug.LogWarning("[EnvironmentSetup] 'Directional Light' has no Light component. Skipping lighting setup.");
                return;
            }

            Undo.RecordObject(directionalLight, "Sun");
            Undo.RecordObject(lightGO.transform, "Sun");

            directionalLight.color = Hex("#FFD3A1");
            directionalLight.intensity = 1.7f;
            directionalLight.shadows = LightShadows.Soft;
            directionalLight.shadowStrength = 0.85f;
            lightGO.transform.rotation = Quaternion.Euler(28f, -35f, 0f);
            RenderSettings.sun = directionalLight;
        }

        /// <summary>Moderate shadows on the active URP asset: 2 cascades, 60 m, medium soft shadows.</summary>
        private static void SetupShadowQuality()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset))
            {
                return;
            }

            var serialized = new SerializedObject(urpAsset);
            SetInt(serialized, "m_ShadowCascadeCount", 2);
            SetFloat(serialized, "m_ShadowDistance", 60f);
            SetInt(serialized, "m_SoftShadowQuality", 2);          // medium
            SetInt(serialized, "m_MainLightShadowmapResolution", 2048);
            serialized.ApplyModifiedProperties();                    // records undo
        }

        private static void SetupCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Undo.RecordObject(camera, "Camera");
            camera.farClipPlane = 600f;

            UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
            {
                Undo.RecordObject(data, "Camera");
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            }
        }

        private static void SetupPostProcessing()
        {
            GameObject volumeGO = GameObject.Find("Global Volume");
            if (volumeGO == null)
            {
                volumeGO = new GameObject("Global Volume");
                Undo.RegisterCreatedObjectUndo(volumeGO, "Global Volume");
            }

            Volume volume = volumeGO.GetComponent<Volume>();
            if (volume == null)
            {
                volume = Undo.AddComponent<Volume>(volumeGO);
            }
            Undo.RecordObject(volume, "Volume");
            volume.isGlobal = true;

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.Override(0.15f);
            color.contrast.Override(12f);
            color.saturation.Override(8f);
            color.colorFilter.Override(Hex("#FFF0DC"));

            WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>(profile);
            whiteBalance.temperature.Override(8f);

            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);

            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);

            EditorUtility.SetDirty(profile);
            volume.profile = profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            Undo.RecordObject(component, "Post Processing");
            component.active = true;
            EditorUtility.SetDirty(component);
            return component;
        }

        private static void SetInt(SerializedObject serialized, string property, int value)
        {
            SerializedProperty p = serialized.FindProperty(property);
            if (p != null) p.intValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string property, float value)
        {
            SerializedProperty p = serialized.FindProperty(property);
            if (p != null) p.floatValue = value;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }
    }
}
