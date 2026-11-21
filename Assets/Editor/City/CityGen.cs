using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Helpers comunes de KartIHC/City. Toda la ciudad vive en un único prefab
    /// (Assets/Generated/City/EscapeCity.prefab) con una sola instancia en EscapeCourse;
    /// cada etapa reconstruye su propia sección hija ("City_...") dentro del prefab.
    ///
    /// Reglas que se validan aquí:
    /// - Solo se trabaja sobre EscapeCourse.
    /// - Nada del prefab lleva Collider (el kart, las patrullas y el dron,
    ///   que es un CharacterController, chocarían con él). Si aparece uno, no se guarda.
    ///
    /// Undo: la instancia en la escena y los ajustes de escena se deshacen con Cmd+Z;
    /// el contenido del prefab (un asset) se regenera volviendo a ejecutar la etapa.
    /// </summary>
    public static class CityGen
    {
        public const string MenuRoot = "KartIHC/City/";
        public const string ScenePath = "Assets/Scenes/EscapeCourse.unity";
        public const string RootName = "EscapeCity";

        public const string Root = "Assets/Generated/City";
        public const string TexturesFolder = Root + "/Textures";
        public const string MaterialsFolder = Root + "/Materials";
        public const string MeshesFolder = Root + "/Meshes";
        public const string SettingsFolder = Root + "/Settings";
        public const string PrefabPath = Root + "/EscapeCity.prefab";

        // ---------- Escena ----------

        /// <summary>True si la escena activa es EscapeCourse; si no, avisa y devuelve false.</summary>
        public static bool EnsureEscapeScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path == ScenePath)
            {
                return true;
            }

            string message = $"Abre {ScenePath} antes de generar la ciudad (escena activa: '{scene.path}').";
            Debug.LogError("[City] " + message);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("KartIHC City", message, "OK");
            }
            return false;
        }

        public static void MarkSceneDirty() => EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        // ---------- Carpetas y assets ----------

        public static void EnsureFolders()
        {
            EnsureFolder(TexturesFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(MeshesFolder);
            EnsureFolder(SettingsFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Escribe un PNG en Textures/, deja que 'configure' ajuste el importador y devuelve la textura.</summary>
        public static Texture2D SaveTexture(Color[] pixels, int width, int height, string name,
            Action<TextureImporter> configure)
        {
            EnsureFolders();
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            string path = $"{TexturesFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            configure?.Invoke(importer);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Carga el material de la ruta o lo crea con el shader indicado. Registra Undo si ya existía.</summary>
        public static Material GetOrCreateMaterial(string path, string shaderName)
        {
            EnsureFolders();
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"[City] Shader '{shaderName}' no encontrado.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                Undo.RecordObject(material, "Edit " + material.name);
                if (material.shader != shader)
                {
                    material.shader = shader;
                }
                return material;
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------- Prefab ----------

        public static GameObject LoadPrefabAsset() => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        /// <summary>
        /// Sustituye la sección hija 'sectionName' del prefab por la que construye 'build'
        /// y guarda el prefab. Si el resultado tiene algún Collider, lanza y no guarda.
        /// </summary>
        public static void ReplacePrefabSection(string sectionName, Action<Transform> build)
        {
            EnsurePrefabAsset();
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform previous = contents.transform.Find(sectionName);
                if (previous != null)
                {
                    Object.DestroyImmediate(previous.gameObject);
                }

                var section = new GameObject(sectionName);
                section.transform.SetParent(contents.transform, false);
                build(section.transform);

                AssertNoColliders(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException("[City] No se pudo guardar " + PrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void EnsurePrefabAsset()
        {
            EnsureFolders();
            if (LoadPrefabAsset() != null)
            {
                return;
            }

            // Raíz vacía temporal (sin Undo) solo para escribir el asset; se destruye enseguida.
            var root = new GameObject(RootName);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Instancias del prefab de la ciudad (raíces) en la escena activa.</summary>
        public static List<GameObject> FindSceneInstances()
        {
            var result = new List<GameObject>();
            GameObject asset = LoadPrefabAsset();
            if (asset == null)
            {
                return result;
            }

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(root) == asset)
                {
                    result.Add(root);
                }
            }
            return result;
        }

        /// <summary>Garantiza exactamente una instancia del prefab en el origen de la escena (con Undo).</summary>
        public static GameObject EnsureSingleSceneInstance()
        {
            List<GameObject> instances = FindSceneInstances();
            for (int i = 1; i < instances.Count; i++)
            {
                Undo.DestroyObjectImmediate(instances[i]);
            }

            if (instances.Count > 0)
            {
                return instances[0];
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadPrefabAsset(), SceneManager.GetActiveScene());
            instance.name = RootName;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(instance, "Create " + RootName);
            return instance;
        }

        public static void AssertNoColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
            {
                return;
            }

            var names = new List<string>();
            foreach (Collider collider in colliders)
            {
                names.Add(collider.gameObject.name);
            }
            throw new InvalidOperationException(
                $"[City] La ciudad no puede tener colliders ({colliders.Length}): {string.Join(", ", names)}");
        }

        // ---------- Remove ----------

        [MenuItem(MenuRoot + "Remove Generated City", priority = 400)]
        public static void RemoveGeneratedCity()
        {
            if (!EnsureEscapeScene())
            {
                return;
            }

            EnvGen.BeginUndo("Remove Generated City");
            foreach (GameObject instance in FindSceneInstances())
            {
                Undo.DestroyObjectImmediate(instance);
            }
            CityAtmosphereBuilder.RestoreSceneLighting();
            EnvGen.EndUndo();
            MarkSceneDirty();
            Debug.Log("[City] Ciudad retirada de EscapeCourse. Los assets de " + Root + " se conservan.");
        }

        // ---------- Utilidades ----------

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }

        /// <summary>Dirección hacia el punto del cielo (azimut 0 = +X, 90 = +Z, como EscapeTrackGeometry).</summary>
        public static Vector3 SkyDirection(float azimuthDegrees, float elevationDegrees)
        {
            float az = azimuthDegrees * Mathf.Deg2Rad;
            float el = elevationDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
        }
    }
}
