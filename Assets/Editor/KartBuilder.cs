using System.Collections.Generic;
using KartIHC.Kart;
using KartIHC.Network;
using UnityEditor;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Editor utility that assembles a placeholder kart (body, wheel colliders,
    /// wheel meshes) with a wired-up KartController, so gameplay/network code
    /// has something to drive before final art is ready.
    /// </summary>
    public static class KartBuilder
    {
        private struct WheelSpec
        {
            public string name;
            public Vector3 localPosition;

            public WheelSpec(string name, Vector3 localPosition)
            {
                this.name = name;
                this.localPosition = localPosition;
            }
        }

        [MenuItem("KartIHC/Build Kart")]
        public static void BuildKart()
        {
            EnsureGround();

            GameObject existingKartBody = GameObject.Find("KartBody");
            if (existingKartBody != null)
            {
                Object.DestroyImmediate(existingKartBody);
            }

            GameObject kartBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kartBody.name = "KartBody";
            kartBody.transform.position = new Vector3(0f, 1f, 0f);
            kartBody.transform.localScale = new Vector3(1.3f, 0.4f, 2.3f);

            // The cube's own mesh is now just a physics proxy (BoxCollider stays); the visible
            // chassis comes from the imported jeep model attached below.
            MeshRenderer bodyRenderer = kartBody.GetComponent<MeshRenderer>();
            if (bodyRenderer != null)
            {
                bodyRenderer.enabled = false;
            }

            Rigidbody rb = kartBody.AddComponent<Rigidbody>();
            rb.mass = 800f;
            rb.linearDamping = 0.3f;
            rb.centerOfMass = new Vector3(0f, -0.3f, 0f);

            WheelSpec[] wheelSpecs =
            {
                new WheelSpec("FL", new Vector3(-0.7f, -0.2f, 1.1f)),
                new WheelSpec("FR", new Vector3(0.7f, -0.2f, 1.1f)),
                new WheelSpec("RL", new Vector3(-0.7f, -0.2f, -1.1f)),
                new WheelSpec("RR", new Vector3(0.7f, -0.2f, -1.1f)),
            };

            var wheelColliders = new Dictionary<string, WheelCollider>();

            foreach (WheelSpec spec in wheelSpecs)
            {
                // Empty GameObject holding just the WheelCollider — the visual wheel mesh now
                // comes from JeepModel (the imported prefab), so no Cylinder is created here.
                GameObject wheelGO = new GameObject(spec.name);
                wheelGO.transform.SetParent(kartBody.transform, false);
                wheelGO.transform.localPosition = spec.localPosition;

                WheelCollider wheelCollider = wheelGO.AddComponent<WheelCollider>();
                wheelColliders[spec.name] = wheelCollider;
            }

            GameObject jeepInstance = AttachJeepModel(kartBody, wheelColliders);
            if (jeepInstance == null && bodyRenderer != null)
            {
                // No prefab found: fall back to the plain cube so the kart is still visible.
                bodyRenderer.enabled = true;
            }

            KartController controller = kartBody.AddComponent<KartController>();
            controller.frontLeft = wheelColliders["FL"];
            controller.frontRight = wheelColliders["FR"];
            controller.rearLeft = wheelColliders["RL"];
            controller.rearRight = wheelColliders["RR"];
            // Wheel mesh fields are left unassigned — JeepModel's own wheel meshes are the
            // visible ones now, so there's nothing to point frontLeftMesh/etc. at here.

            AssignNetworkReceiverTarget(controller);
            AssignCameraTarget(kartBody);

            Selection.activeGameObject = kartBody;
        }

        private const string JeepPrefabPath = "Assets/Arcade Racing Cars/Pickup Jeep/Prefabs/ARC_Jeep_LWRP.prefab";
        private const string JeepPrefabSearchFolder = "Assets/Arcade Racing Cars/Pickup Jeep/Prefabs";

        /// <summary>
        /// Instantiates the imported jeep model as KartBody's visual mesh. Since KartBody has a
        /// non-uniform scale, the instance's scale is neutralized against it, then re-scaled
        /// uniformly so the model's length roughly matches the kart's own wheelbase (measured from
        /// the already-built WheelColliders) — that keeps it from looking gigantic or tiny next to
        /// the existing wheels regardless of the source FBX's native unit scale.
        /// </summary>
        private static GameObject AttachJeepModel(GameObject kartBody, Dictionary<string, WheelCollider> wheelColliders)
        {
            GameObject prefabAsset = LoadJeepPrefab();
            if (prefabAsset == null)
            {
                Debug.LogWarning($"[KartBuilder] Could not find the ARC_Jeep prefab under {JeepPrefabSearchFolder}. Skipping visual model.");
                return null;
            }

            GameObject jeepInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, kartBody.transform);
            jeepInstance.name = "JeepModel";
            jeepInstance.transform.localPosition = Vector3.zero;
            jeepInstance.transform.localRotation = Quaternion.identity;

            // The prefab ships with its own BoxCollider and Rigidbody; KartBody's own Rigidbody
            // and BoxCollider already drive the physics, so strip both from the model to avoid
            // double collision and a second, uncontrolled Rigidbody fighting the kart's physics.
            RemoveAllColliders(jeepInstance);
            RemoveAllRigidbodies(jeepInstance);

            Vector3 parentScale = kartBody.transform.lossyScale;

            // Neutralize KartBody's stretch to measure the model's true, undistorted size.
            jeepInstance.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

            Renderer[] renderers = jeepInstance.GetComponentsInChildren<Renderer>();
            float modelLength = 0f;
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
                modelLength = bounds.size.z;
            }

            float wheelbase = Mathf.Abs(wheelColliders["FL"].transform.position.z - wheelColliders["RL"].transform.position.z);
            float targetLength = wheelbase > 0.01f ? wheelbase : 2.3f;
            float uniformScale = modelLength > 0.01f ? targetLength / modelLength : 1f;

            jeepInstance.transform.localScale = new Vector3(
                uniformScale / parentScale.x,
                uniformScale / parentScale.y,
                uniformScale / parentScale.z);

            return jeepInstance;
        }

        private static GameObject LoadJeepPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JeepPrefabPath);
            if (prefab != null)
            {
                return prefab;
            }

            string[] guids = AssetDatabase.FindAssets("ARC_Jeep", new[] { JeepPrefabSearchFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject found = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void RemoveAllColliders(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(collider);
            }
        }

        private static void RemoveAllRigidbodies(GameObject root)
        {
            foreach (Rigidbody rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(rigidbody);
            }
        }

        private static void EnsureGround()
        {
            if (GameObject.Find("Ground") != null)
            {
                return;
            }

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
        }

        private static void AssignNetworkReceiverTarget(KartController controller)
        {
            NetworkReceiver networkReceiver = Object.FindFirstObjectByType<NetworkReceiver>();
            if (networkReceiver == null)
            {
                return;
            }

            SerializedObject serializedReceiver = new SerializedObject(networkReceiver);
            SerializedProperty kartControllerProperty = serializedReceiver.FindProperty("kartController");
            kartControllerProperty.objectReferenceValue = controller;
            serializedReceiver.ApplyModifiedProperties();
        }

        private static void AssignCameraTarget(GameObject kartBody)
        {
            GameObject cameraGO = Camera.main != null ? Camera.main.gameObject : GameObject.Find("Main Camera");
            if (cameraGO == null)
            {
                return;
            }

            CameraFollow cameraFollow = cameraGO.GetComponent<CameraFollow>();
            if (cameraFollow == null)
            {
                return;
            }

            SerializedObject serializedCamera = new SerializedObject(cameraFollow);
            SerializedProperty targetProperty = serializedCamera.FindProperty("target");
            targetProperty.objectReferenceValue = kartBody.transform;
            serializedCamera.ApplyModifiedProperties();
        }

        private static void ApplyMaterial(GameObject go, string materialName)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{materialName}.mat");
            if (material == null)
            {
                Debug.LogWarning($"[KartBuilder] Material '{materialName}' not found in Assets/Materials. Run KartIHC/Setup Materials first.");
                return;
            }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }
}
