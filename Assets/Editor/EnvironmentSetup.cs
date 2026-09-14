using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Editor utility that dresses the scene with a warm off-road atmosphere:
    /// a procedural sunset skybox, depth fog, a warm directional light, and a
    /// URP Volume with color grading / vignette / bloom overrides.
    /// </summary>
    public static class EnvironmentSetup
    {
        private const string SkyboxPath = "Assets/Materials/Skybox_Offroad.mat";
        private const string VolumeProfilePath = "Assets/Settings/VolumeProfile_Offroad.asset";

        [MenuItem("KartIHC/Setup Environment")]
        public static void SetupEnvironment()
        {
            SetupSkybox();
            SetupFog();
            SetupLighting();
            SetupPostProcessing();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
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

            if (ColorUtility.TryParseHtmlString("#F2C48A", out Color skyTint))
            {
                skybox.SetColor("_SkyTint", skyTint);
            }
            if (ColorUtility.TryParseHtmlString("#5C4530", out Color groundColor))
            {
                skybox.SetColor("_GroundColor", groundColor);
            }
            skybox.SetFloat("_SunSize", 0.06f);
            skybox.SetFloat("_AtmosphereThickness", 1f);
            skybox.SetFloat("_Exposure", 1.2f);

            RenderSettings.skybox = skybox;
        }

        private static void SetupFog()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 140f;

            if (ColorUtility.TryParseHtmlString("#D8C4A0", out Color fogColor))
            {
                RenderSettings.fogColor = fogColor;
            }
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

            if (ColorUtility.TryParseHtmlString("#FFE1B3", out Color warmColor))
            {
                directionalLight.color = warmColor;
            }
            directionalLight.intensity = 1.3f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void SetupPostProcessing()
        {
            GameObject volumeGO = GameObject.Find("Global Volume");
            if (volumeGO == null)
            {
                volumeGO = new GameObject("Global Volume");
            }

            Volume volume = volumeGO.GetComponent<Volume>();
            if (volume == null)
            {
                volume = volumeGO.AddComponent<Volume>();
            }
            volume.isGlobal = true;

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            ConfigureColorAdjustments(profile);
            ConfigureVignette(profile);
            ConfigureBloom(profile);

            volume.profile = profile;
        }

        private static void ConfigureColorAdjustments(VolumeProfile profile)
        {
            if (!profile.TryGet(out ColorAdjustments colorAdjustments))
            {
                colorAdjustments = profile.Add<ColorAdjustments>(true);
            }

            colorAdjustments.active = true;

            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = 15f;

            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = 0.2f;

            if (ColorUtility.TryParseHtmlString("#FFE8C2", out Color warmFilter))
            {
                colorAdjustments.colorFilter.overrideState = true;
                colorAdjustments.colorFilter.value = warmFilter;
            }
        }

        private static void ConfigureVignette(VolumeProfile profile)
        {
            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>(true);
            }

            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.25f;
            vignette.color.overrideState = true;
            vignette.color.value = Color.black;
        }

        private static void ConfigureBloom(VolumeProfile profile)
        {
            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }

            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.1f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.3f;
        }
    }
}
