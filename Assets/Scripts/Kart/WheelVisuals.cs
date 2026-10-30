using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Makes the Jeep model's static wheel meshes spin, steer and follow the
    /// suspension of the kart's 4 WheelColliders, copying the full pose
    /// (position and rotation) from GetWorldPose. The colliders are aligned with
    /// the visual wheels by KartIHC/Calibrate Kart Physics. Each mesh is wrapped
    /// in a pivot at its visual center, so it spins in place even if the mesh
    /// pivot isn't centered.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class WheelVisuals : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureOnKart()
        {
            KartController kart = FindFirstObjectByType<KartController>();
            if (kart != null && kart.GetComponent<WheelVisuals>() == null)
            {
                kart.gameObject.AddComponent<WheelVisuals>();
            }
        }

        public const string FrontLeftName = "Wheel_Front_Left";
        public const string FrontRightName = "Wheel_Front_Right";
        public const string RearLeftName = "Wheel_Back_Left";
        public const string RearRightName = "Wheel_Back_Right";

        [Header("Wheel meshes (auto-found by name if empty)")]
        public Transform frontLeftMesh;
        public Transform frontRightMesh;
        public Transform rearLeftMesh;
        public Transform rearRightMesh;

        private class Wheel
        {
            public WheelCollider collider;
            public Transform pivot;
            public Quaternion rotationOffset;
        }

        private Wheel[] wheels;

        private void Start()
        {
            KartController kart = GetComponent<KartController>();

            if (frontLeftMesh == null) frontLeftMesh = FindDeepChild(transform, FrontLeftName);
            if (frontRightMesh == null) frontRightMesh = FindDeepChild(transform, FrontRightName);
            if (rearLeftMesh == null) rearLeftMesh = FindDeepChild(transform, RearLeftName);
            if (rearRightMesh == null) rearRightMesh = FindDeepChild(transform, RearRightName);

            wheels = new[]
            {
                CreateWheel(kart.frontLeft, frontLeftMesh, FrontLeftName),
                CreateWheel(kart.frontRight, frontRightMesh, FrontRightName),
                CreateWheel(kart.rearLeft, rearLeftMesh, RearLeftName),
                CreateWheel(kart.rearRight, rearRightMesh, RearRightName),
            };
        }

        private Wheel CreateWheel(WheelCollider collider, Transform mesh, string expectedName)
        {
            if (collider == null || mesh == null)
            {
                Debug.LogWarning($"[WheelVisuals] Missing {(collider == null ? "WheelCollider" : "mesh")} for '{expectedName}'. " +
                                 "Assign it in the inspector or check the wheel names in the Jeep hierarchy.");
                return null;
            }

            // Pivot exactly at the mesh's center (local mesh bounds, not the world AABB), so the
            // wheel spins in place and the pivot can take the pose position with no extra offset.
            MeshFilter meshFilter = mesh.GetComponentInChildren<MeshFilter>();
            Vector3 center = meshFilter != null && meshFilter.sharedMesh != null
                ? meshFilter.transform.TransformPoint(meshFilter.sharedMesh.bounds.center)
                : mesh.position;

            Transform pivot = new GameObject(mesh.name + "_Pivot").transform;
            pivot.SetParent(mesh.parent, false);
            pivot.position = center;
            pivot.rotation = transform.rotation;
            mesh.SetParent(pivot, true);

            collider.GetWorldPose(out _, out Quaternion poseRotation);

            return new Wheel
            {
                collider = collider,
                pivot = pivot,
                rotationOffset = Quaternion.Inverse(poseRotation) * pivot.rotation,
            };
        }

        private void LateUpdate()
        {
            if (wheels == null)
            {
                return;
            }

            foreach (Wheel wheel in wheels)
            {
                if (wheel == null || wheel.pivot == null)
                {
                    continue;
                }

                wheel.collider.GetWorldPose(out Vector3 posePosition, out Quaternion poseRotation);
                wheel.pivot.position = posePosition;
                wheel.pivot.rotation = poseRotation * wheel.rotationOffset;
            }
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
