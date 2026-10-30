using KartIHC.Kart;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Creates the particle / shield materials as assets (so their shaders are
    /// included in builds) and adds KartVFX to KartBody with them assigned.
    /// </summary>
    public static class KartVFXSetup
    {
        private const string DustPath = EnvGen.MaterialsFolder + "/Mat_ParticleDust.mat";
        private const string GlowPath = EnvGen.MaterialsFolder + "/Mat_ParticleGlow.mat";
        private const string BubblePath = EnvGen.MaterialsFolder + "/Mat_ShieldBubble.mat";

        [MenuItem("KartIHC/Environment/7 Kart VFX (Dust, Turbo, Shield, Respawn)", priority = 107)]
        public static void SetupKartVFX()
        {
            GameObject kartBody = GameObject.Find("KartBody");
            if (kartBody == null || kartBody.GetComponent<KartController>() == null)
            {
                Debug.LogWarning("[KartVFXSetup] No 'KartBody' with KartController in the scene.");
                return;
            }

            EnvGen.EnsureFolders();
            EnvGen.BeginUndo("Setup Kart VFX");

            Texture2D soft = EnvGen.LoadTexture("Particle_Soft");
            if (soft == null)
            {
                Texture2D generated = KartVFX.CreateSoftCircle(64);
                soft = EnvGen.SaveTexture(generated.GetPixels(), 64, 64, "Particle_Soft", EnvGen.TextureKind.Color, TextureWrapMode.Clamp);
                Object.DestroyImmediate(generated);
            }

            Material dust = ParticleMaterial(DustPath, soft, additive: false);
            Material glow = ParticleMaterial(GlowPath, soft, additive: true);

            Material bubble = EnvGen.GetOrCreateLit(BubblePath);
            EnvGen.SetLit(bubble, new Color(0.35f, 0.85f, 1f, 0.22f), null, null, 0f, 0.95f, Vector2.one);
            EnvGen.MakeTransparent(bubble);
            bubble.EnableKeyword("_EMISSION");
            bubble.SetColor("_EmissionColor", new Color(0.1f, 0.45f, 0.6f));
            bubble.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            KartVFX vfx = kartBody.GetComponent<KartVFX>();
            if (vfx == null)
            {
                vfx = Undo.AddComponent<KartVFX>(kartBody);
            }
            Undo.RecordObject(vfx, "Kart VFX materials");
            vfx.dustMaterial = dust;
            vfx.glowMaterial = glow;
            vfx.shieldBubbleMaterial = bubble;
            EditorUtility.SetDirty(vfx);

            EnvGen.EndUndo();
            EditorSceneManager.MarkSceneDirty(kartBody.scene);
            AssetDatabase.SaveAssets();
        }

        private static Material ParticleMaterial(string path, Texture2D texture, bool additive)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = KartVFX.CreateParticleMaterial(texture, additive);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                Undo.RecordObject(material, "Particle material");
                KartVFX.ConfigureParticleMaterial(material, texture, additive);
            }

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
