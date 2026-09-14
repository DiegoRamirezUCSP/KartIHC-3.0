using UnityEditor;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Editor utility that generates the base URP materials used by the
    /// generated kart and track, so KartBuilder/TrackBuilder have something
    /// to assign instead of the default pink/gray primitives.
    /// </summary>
    public static class MaterialSetup
    {
        private const string MaterialsFolder = "Assets/Materials";

        private static readonly (string name, string hex)[] MaterialDefs =
        {
            ("Mat_KartBody", "#5FD3C4"),
            ("Mat_Wheel", "#2B2B2B"),
            ("Mat_TrackWallA", "#E4572E"),
            ("Mat_TrackWallB", "#F2F2F2"),
            ("Mat_Ground", "#4A4E54"),
            ("Mat_StartLine", "#F2C14E"),
        };

        [MenuItem("KartIHC/Setup Materials")]
        public static void SetupMaterials()
        {
            EnsureMaterialsFolder();

            foreach ((string name, string hex) in MaterialDefs)
            {
                GetOrCreateMaterial(name, hex);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureMaterialsFolder()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }
        }

        private static Material GetOrCreateMaterial(string name, string hex)
        {
            string path = $"{MaterialsFolder}/{name}.mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material material = new Material(shader);

            if (ColorUtility.TryParseHtmlString(hex, out Color color))
            {
                material.color = color;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
