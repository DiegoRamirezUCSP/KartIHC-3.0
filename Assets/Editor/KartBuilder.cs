using KartIHC.Kart;
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
            kartBody.transform.localScale = new Vector3(1.5f, 0.5f, 2.5f);

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

            var wheelColliders = new System.Collections.Generic.Dictionary<string, WheelCollider>();
            var wheelMeshes = new System.Collections.Generic.Dictionary<string, Transform>();

            foreach (WheelSpec spec in wheelSpecs)
            {
                GameObject wheelGO = new GameObject(spec.name);
                wheelGO.transform.SetParent(kartBody.transform, false);
                wheelGO.transform.localPosition = spec.localPosition;

                WheelCollider wheelCollider = wheelGO.AddComponent<WheelCollider>();
                wheelColliders[spec.name] = wheelCollider;

                GameObject meshGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                meshGO.name = spec.name + "_Mesh";
                meshGO.transform.SetParent(wheelGO.transform, false);
                meshGO.transform.localPosition = Vector3.zero;
                meshGO.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                meshGO.transform.localScale = new Vector3(0.4f, 0.1f, 0.4f);

                CapsuleCollider capsuleCollider = meshGO.GetComponent<CapsuleCollider>();
                if (capsuleCollider != null)
                {
                    Object.DestroyImmediate(capsuleCollider);
                }

                wheelMeshes[spec.name] = meshGO.transform;
            }

            KartController controller = kartBody.AddComponent<KartController>();
            controller.frontLeft = wheelColliders["FL"];
            controller.frontRight = wheelColliders["FR"];
            controller.rearLeft = wheelColliders["RL"];
            controller.rearRight = wheelColliders["RR"];
            controller.frontLeftMesh = wheelMeshes["FL"];
            controller.frontRightMesh = wheelMeshes["FR"];
            controller.rearLeftMesh = wheelMeshes["RL"];
            controller.rearRightMesh = wheelMeshes["RR"];

            Selection.activeGameObject = kartBody;
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
    }
}
