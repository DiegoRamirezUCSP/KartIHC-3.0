using KartIHC.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KartIHC.Editor
{
    /// <summary>Builds the two real mode scenes from editor-authored scene objects.</summary>
    public static class ModeSceneSetup
    {
        private const string MenuPath = "Assets/Scenes/ModeMenu.unity";
        private const string EscapePath = "Assets/Scenes/EscapeCourse.unity";

        [MenuItem("Kart IHC/Configurar escenas de modos")]
        public static void RebuildModeScenes()
        {
            CreateMenuScene();
            CreateEscapeScene();
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Escenas listas: ModeMenu (inicio), SampleScene (carrera) y EscapeCourse (escape).");
        }

        [MenuItem("Kart IHC/Escape/Actualizar modelos visibles de cámaras")]
        public static void UpdateSavedEscapeCameraModels()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.OpenScene(EscapePath, OpenSceneMode.Single);
            Material housingMaterial = MakeMaterial("Camera Housing", new Color(0.13f, 0.18f, 0.21f));
            Material lensMaterial = MakeMaterial("Camera Lens", new Color(0.035f, 0.09f, 0.12f));
            Material indicatorMaterial = MakeMaterial("Camera Indicator", new Color(0.95f, 0.2f, 0.06f));
            int added = 0;
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera camera in cameras)
            {
                if (!camera.name.StartsWith("EscapeSecurityCamera_") || camera.transform.Find("Industrial Housing") != null) continue;
                int index = 1;
                int.TryParse(camera.name.Substring("EscapeSecurityCamera_".Length), out index);
                AddCameraVisuals(camera, index, housingMaterial, lensMaterial, indicatorMaterial, true);
                added++;
            }
            EditorSceneManager.SaveScene(scene, EscapePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Se agregaron modelos industriales a {added} cámaras en EscapeCourse.");
        }

        private static void CreateMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientLight = new Color(0.13f, 0.16f, 0.22f);

            var cameraObject = new GameObject("Menu Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.045f, 0.075f);
            cameraObject.AddComponent<AudioListener>();

            var lightObject = new GameObject("Menu Light");
            lightObject.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            lightObject.AddComponent<Light>().type = LightType.Directional;

            var voiceObject = new GameObject("Voice Input");
            voiceObject.AddComponent<VoicePowerUpInput>();
            EditorSceneManager.SaveScene(scene, MenuPath);
        }

        private static void CreateEscapeScene()
        {
            Scene scene = EditorSceneManager.OpenScene(EscapePath, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                string n = root.name;
                bool raceWorld = n.StartsWith("Environment_") || n == "Track" || n == "TrackSurface" ||
                    n.StartsWith("OuterWall_") || n.StartsWith("InnerWall_") || n == "Item" || n == "Item (1)" ||
                    n.Contains("Checkpoint") || n.Contains("LapManager") || n.Contains("RaceManager");
                if (raceWorld) Object.DestroyImmediate(root);
            }
            GameObject previous = GameObject.Find("EscapeEnvironment");
            if (previous != null) Object.DestroyImmediate(previous);

            var environment = new GameObject("EscapeEnvironment");
            Material ground = MakeMaterial("Escape Ground", new Color(0.20f, 0.23f, 0.25f));
            Material road = MakeMaterial("Escape Road", new Color(0.12f, 0.14f, 0.16f));
            Material barrier = MakeMaterial("Escape Barrier", new Color(0.31f, 0.37f, 0.42f));
            Material gate = MakeMaterial("Escape Door", new Color(0.94f, 0.38f, 0.09f));
            MakeCube(environment.transform, "EscapeGround", new Vector3(0f, -0.2f, 0f), new Vector3(100f, 0.4f, 100f), ground);
            BuildRing(environment.transform, "EscapeRoad", 18f, 32f, 0.18f, road, false);
            BuildRing(environment.transform, "EscapeInnerBarrier", 17.3f, 17.9f, 2.3f, barrier, true);
            BuildRing(environment.transform, "EscapeOuterBarrier", 32.1f, 32.7f, 2.3f, barrier, true);
            BuildDoors(environment.transform, gate);
            BuildCameras(environment.transform);
            BuildFinish(environment.transform);

            var spawn = new GameObject("EscapeKartSpawn");
            spawn.transform.SetParent(environment.transform);
            spawn.transform.SetPositionAndRotation(new Vector3(25f, 1.1f, 5f), Quaternion.identity);
            EditorSceneManager.SaveScene(scene, EscapePath);
        }

        private static void BuildRing(Transform parent, string name, float radiusA, float radiusB, float height, Material material, bool wall)
        {
            const int segments = 48;
            float radius = (radiusA + radiusB) * .5f;
            float width = Mathf.Abs(radiusB - radiusA);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 delta = p1 - p0;
                GameObject part = MakeCube(parent, $"{name}_{i + 1:00}", (p0 + p1) * .5f + (wall ? Vector3.up * height * .5f : Vector3.zero),
                    new Vector3(width, wall ? height : .18f, delta.magnitude + .08f), material);
                part.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            }
        }

        private static void BuildDoors(Transform parent, Material material)
        {
            for (int i = 0; i < 3; i++)
            {
                float angle = (i * 120f + 25f) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                Vector3 center = radial * 25f;
                var pivot = new GameObject($"EscapeDoor_{i + 1}");
                pivot.transform.SetParent(parent);
                pivot.transform.SetPositionAndRotation(center + radial * 6.2f + Vector3.up * 1.2f, Quaternion.LookRotation(tangent));
                GameObject panel = MakeCube(pivot.transform, "Door Panel", new Vector3(-6.2f, 0f, 0f), new Vector3(12.4f, 2.4f, .55f), material);
                EscapeDoor door = pivot.AddComponent<EscapeDoor>();
                door.Initialize(i, panel.transform);
            }
        }

        private static void BuildCameras(Transform parent)
        {
            Material housingMaterial = MakeMaterial("Camera Housing", new Color(0.13f, 0.18f, 0.21f));
            Material lensMaterial = MakeMaterial("Camera Lens", new Color(0.035f, 0.09f, 0.12f));
            Material indicatorMaterial = MakeMaterial("Camera Indicator", new Color(0.95f, 0.2f, 0.06f));
            for (int i = 0; i < 3; i++)
            {
                float cameraAngle = (i * 120f + 55f) * Mathf.Deg2Rad;
                float doorAngle = (i * 120f + 25f) * Mathf.Deg2Rad;
                Vector3 camPos = new Vector3(Mathf.Cos(cameraAngle), 0f, Mathf.Sin(cameraAngle)) * 34f + Vector3.up * 5f;
                Vector3 target = new Vector3(Mathf.Cos(doorAngle), 0f, Mathf.Sin(doorAngle)) * 25f + Vector3.up * 1.2f;
                Vector3 polePosition = new Vector3(Mathf.Cos(cameraAngle), 0f, Mathf.Sin(cameraAngle)) * 34f;
                GameObject pole = MakeCube(parent, $"EscapeCameraPole_{i + 1}", polePosition + Vector3.up * 2.4f, new Vector3(.24f, 4.8f, .24f), housingMaterial);
                pole.GetComponent<Collider>().enabled = false;
                GameObject mount = MakeCube(parent, $"EscapeCameraMount_{i + 1}", polePosition + Vector3.up * 4.65f, new Vector3(.72f, .14f, .2f), housingMaterial);
                mount.GetComponent<Collider>().enabled = false;
                var cameraObject = new GameObject($"EscapeSecurityCamera_{i + 1}");
                cameraObject.transform.SetParent(parent);
                cameraObject.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(target - camPos));
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 58f;
                camera.nearClipPlane = .8f;
                camera.enabled = false;
                cameraObject.AddComponent<AudioListener>().enabled = false;
                AddCameraVisuals(camera, i + 1, housingMaterial, lensMaterial, indicatorMaterial, false);
            }
        }

        private static void AddCameraVisuals(Camera camera, int index, Material housingMaterial, Material lensMaterial, Material indicatorMaterial, bool registerUndo)
        {
            camera.nearClipPlane = .8f;
            Transform parent = camera.transform.parent;
            Vector3 position = camera.transform.position;
            Vector3 radial = new Vector3(position.x, 0f, position.z).normalized;
            GameObject pole = MakeCube(parent, $"EscapeCameraPole_{index}", radial * 34f + Vector3.up * 2.4f,
                new Vector3(.24f, 4.8f, .24f), housingMaterial);
            GameObject mount = MakeCube(parent, $"EscapeCameraMount_{index}", radial * 34f + Vector3.up * 4.65f,
                new Vector3(.72f, .14f, .2f), housingMaterial);
            pole.GetComponent<Collider>().enabled = false;
            mount.GetComponent<Collider>().enabled = false;

            if (registerUndo)
            {
                Undo.RegisterCreatedObjectUndo(pole, "Add industrial camera pole");
                Undo.RegisterCreatedObjectUndo(mount, "Add industrial camera mount");
            }

            AddCameraPrimitive(camera.transform, PrimitiveType.Cube, "Industrial Housing", Vector3.zero,
                Quaternion.identity, new Vector3(.62f, .38f, .82f), housingMaterial, registerUndo);
            AddCameraPrimitive(camera.transform, PrimitiveType.Cylinder, "Lens Barrel", new Vector3(0f, 0f, .42f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(.23f, .16f, .23f), housingMaterial, registerUndo);
            AddCameraPrimitive(camera.transform, PrimitiveType.Sphere, "Lens Glass", new Vector3(0f, 0f, .57f),
                Quaternion.identity, new Vector3(.18f, .18f, .08f), lensMaterial, registerUndo);
            AddCameraPrimitive(camera.transform, PrimitiveType.Sphere, "Recording Indicator", new Vector3(.23f, .13f, .12f),
                Quaternion.identity, Vector3.one * .07f, indicatorMaterial, registerUndo);
        }

        private static void AddCameraPrimitive(Transform parent, PrimitiveType primitive, string name, Vector3 position,
            Quaternion rotation, Vector3 scale, Material material, bool registerUndo)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            part.GetComponent<Collider>().enabled = false;
            if (registerUndo) Undo.RegisterCreatedObjectUndo(part, "Add industrial camera model");
        }

        private static void BuildFinish(Transform parent)
        {
            var finish = new GameObject("EscapeFinishZone");
            finish.transform.SetParent(parent);
            finish.transform.position = new Vector3(25f, .2f, 0f);
            BoxCollider trigger = finish.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(15f, 3f, 2f);
            finish.AddComponent<EscapeFinishZone>();
        }

        private static GameObject MakeCube(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            const string folder = "Assets/Generated/ModeMaterials";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
                AssetDatabase.CreateFolder("Assets/Generated", "ModeMaterials");
            }
            string path = $"{folder}/{name.Replace(' ', '_')}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { existing.color = color; EditorUtility.SetDirty(existing); return existing; }
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuPath, true),
                new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true),
                new EditorBuildSettingsScene(EscapePath, true),
                new EditorBuildSettingsScene("Assets/Scenes/PhoneControllerScene.unity", false)
            };
        }
    }
}
