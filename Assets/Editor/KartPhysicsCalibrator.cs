using KartIHC.Kart;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Calibrates the kart's physics against the Jeep model, in place (no rebuild,
    /// so scene references on KartBody survive):
    /// - removes KartBody's non-uniform scale (children keep their world size),
    /// - moves each WheelCollider onto its visual wheel (X/Z, axle height) with the
    ///   visual wheel's real radius, and a stable suspension,
    /// - fits the body BoxCollider to the chassis, clear of the ground,
    /// - sets a low, centered center of mass, interpolation, continuous collisions,
    /// - adds AntiRoll, and drops the kart onto the ground at its resting height.
    /// </summary>
    public static class KartPhysicsCalibrator
    {
        private const float SuspensionDistance = 0.2f;
        private const float SpringStrength = 35000f;
        private const float SpringDamper = 4500f;
        private const float TargetPosition = 0.5f;
        private const float ForwardStiffness = 1.5f;
        private const float SidewaysStiffness = 1.8f;
        private const float AngularDamping = 1.5f;
        private const float MinBodyClearance = 0.25f;
        private const float CenterOfMassHeightFactor = 0.55f;   // fraction of the axle height

        [MenuItem("KartIHC/Calibrate Kart Physics")]
        public static void CalibrateMenu()
        {
            GameObject kartBody = GameObject.Find("KartBody");
            if (kartBody == null)
            {
                Debug.LogWarning("[KartPhysicsCalibrator] No 'KartBody' in the scene.");
                return;
            }

            Calibrate(kartBody);
            Selection.activeGameObject = kartBody;
        }

        public static void Calibrate(GameObject kartBody)
        {
            KartController kart = kartBody.GetComponent<KartController>();
            Rigidbody rb = kartBody.GetComponent<Rigidbody>();
            if (kart == null || rb == null)
            {
                Debug.LogWarning("[KartPhysicsCalibrator] KartBody needs KartController and Rigidbody.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(kartBody, "Calibrate Kart Physics");
            Transform body = kartBody.transform;

            RemoveNonUniformScale(body);

            WheelCollider[] colliders = { kart.frontLeft, kart.frontRight, kart.rearLeft, kart.rearRight };
            string[] wheelNames = { WheelVisuals.FrontLeftName, WheelVisuals.FrontRightName, WheelVisuals.RearLeftName, WheelVisuals.RearRightName };

            float axleHeight = 0f;
            float wheelZSum = 0f;
            int alignedWheels = 0;
            string report = "";

            for (int i = 0; i < colliders.Length; i++)
            {
                WheelCollider wheel = colliders[i];
                if (wheel == null)
                {
                    continue;
                }

                Transform visual = FindDeepChild(body, wheelNames[i]);
                if (visual != null && TryGetWheelGeometry(body, visual, out Vector3 axle, out float radius))
                {
                    // Transform on the axle; center raised by the rest travel, so at rest
                    // (targetPosition along suspensionDistance) the wheel center is on the axle
                    // and its bottom edge is tangent to the ground under the visual wheel.
                    wheel.transform.localPosition = axle;
                    wheel.center = Vector3.up * (SuspensionDistance * TargetPosition);
                    wheel.radius = radius;
                    axleHeight += axle.y;
                    wheelZSum += axle.z;
                    alignedWheels++;
                    report += $"\n  {wheelNames[i]} -> {wheel.name}: axle {axle.ToString("F3")}, radius {radius:F3}";
                }
                else
                {
                    Debug.LogWarning($"[KartPhysicsCalibrator] Visual wheel '{wheelNames[i]}' not found; {wheel.name} keeps its position/radius.");
                }

                // Repairs wheels left scaled by an earlier calibration.
                wheel.transform.localRotation = Quaternion.identity;
                wheel.transform.localScale = Vector3.one;
                EditorUtility.SetDirty(wheel.transform);
                wheel.suspensionDistance = SuspensionDistance;
                wheel.suspensionSpring = new JointSpring { spring = SpringStrength, damper = SpringDamper, targetPosition = TargetPosition };
                wheel.forwardFriction = WithStiffness(wheel.forwardFriction, ForwardStiffness);
                wheel.sidewaysFriction = WithStiffness(wheel.sidewaysFriction, SidewaysStiffness);
                EditorUtility.SetDirty(wheel);
            }

            axleHeight = alignedWheels > 0 ? axleHeight / alignedWheels : 0.4f;
            float wheelCenterZ = alignedWheels > 0 ? wheelZSum / alignedWheels : 0f;
            float centerOfMassHeight = axleHeight * CenterOfMassHeightFactor;

            // Tire/suspension forces applied at the center-of-mass height cause no roll torque.
            foreach (WheelCollider wheel in colliders)
            {
                if (wheel != null)
                {
                    wheel.forceAppPointDistance = centerOfMassHeight;
                }
            }

            FitBodyCollider(kartBody, body);

            rb.automaticCenterOfMass = false;
            rb.centerOfMass = new Vector3(0f, centerOfMassHeight, wheelCenterZ);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.angularDamping = AngularDamping;
            EditorUtility.SetDirty(rb);

            if (kartBody.GetComponent<AntiRoll>() == null)
            {
                Undo.AddComponent<AntiRoll>(kartBody);
            }

            DropOntoGround(kartBody, colliders);

            EditorSceneManager.MarkSceneDirty(kartBody.scene);
            Debug.Log($"[KartPhysicsCalibrator] Calibrated KartBody. Center of mass {rb.centerOfMass.ToString("F3")}.{report}");
        }

        /// <summary>
        /// Sets KartBody's scale to 1, scaling its BoxCollider and direct children so nothing
        /// changes size in the world. WheelColliders under a non-uniform scale behave unreliably.
        /// </summary>
        private static void RemoveNonUniformScale(Transform body)
        {
            Vector3 oldScale = body.localScale;
            if (oldScale == Vector3.one)
            {
                return;
            }

            BoxCollider box = body.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.size = Vector3.Scale(box.size, oldScale);
                box.center = Vector3.Scale(box.center, oldScale);
            }

            foreach (Transform child in body)
            {
                child.localPosition = Vector3.Scale(child.localPosition, oldScale);

                // WheelColliders must stay at scale 1 (Unity scales their radius/suspension with
                // the transform); every other child keeps its world size (identity local rotation).
                if (child.GetComponent<WheelCollider>() == null)
                {
                    child.localScale = Vector3.Scale(child.localScale, oldScale);
                }
            }

            body.localScale = Vector3.one;
        }

        /// <summary>Axle position (KartBody space) and radius of a visual wheel, from its mesh bounds.</summary>
        private static bool TryGetWheelGeometry(Transform body, Transform visual, out Vector3 axle, out float radius)
        {
            axle = Vector3.zero;
            radius = 0f;

            MeshFilter filter = visual.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Bounds bounds = filter.sharedMesh.bounds;
            Transform meshTransform = filter.transform;
            axle = body.InverseTransformPoint(meshTransform.TransformPoint(bounds.center));

            // The wheel rolls around its X axis: the radius is the larger of its Y/Z half-sizes.
            Vector3 scale = meshTransform.lossyScale;
            radius = Mathf.Max(bounds.extents.y * Mathf.Abs(scale.y), bounds.extents.z * Mathf.Abs(scale.z));
            return radius > 0.01f;
        }

        /// <summary>Fits the body BoxCollider to the Jeep's chassis ("Base"), kept above the ground.</summary>
        private static void FitBodyCollider(GameObject kartBody, Transform body)
        {
            BoxCollider box = kartBody.GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Transform chassis = FindDeepChild(body, "Base");
            MeshFilter filter = chassis != null ? chassis.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogWarning("[KartPhysicsCalibrator] Jeep 'Base' mesh not found; BoxCollider left as is.");
                return;
            }

            Bounds meshBounds = filter.sharedMesh.bounds;
            Bounds local = new Bounds(body.InverseTransformPoint(filter.transform.TransformPoint(meshBounds.center)), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = Vector3.Scale(meshBounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                local.Encapsulate(body.InverseTransformPoint(filter.transform.TransformPoint(meshBounds.center + offset)));
            }

            Vector3 min = local.min;
            min.y = Mathf.Max(min.y, MinBodyClearance);
            local.SetMinMax(min, local.max);

            box.center = local.center;
            box.size = local.size;
            EditorUtility.SetDirty(box);
        }

        /// <summary>Places the kart so its wheels rest on the ground below it.</summary>
        private static void DropOntoGround(GameObject kartBody, WheelCollider[] colliders)
        {
            Transform body = kartBody.transform;
            float restHeight = KartRecovery.ComputeRestHeight(body, colliders);
            Physics.SyncTransforms();

            float nearest = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(body.position + Vector3.up * 5f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == body || hit.transform.IsChildOf(body) || hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                body.position = new Vector3(body.position.x, hit.point.y + restHeight + 0.02f, body.position.z);
            }
        }

        private static WheelFrictionCurve WithStiffness(WheelFrictionCurve curve, float stiffness)
        {
            curve.stiffness = stiffness;
            return curve;
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
