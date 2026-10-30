using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Smoothly follows a target (typically the kart) from an offset that only
    /// rotates with the target's yaw, so the camera never inherits the kart's
    /// pitch/roll (bumps, tilting, flipping). Looks at the target smoothly.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 offset = new Vector3(0f, 4f, -7f);

        [SerializeField]
        private float smoothSpeed = 5f;

        [SerializeField]
        private float rotationSmoothSpeed = 6f;

        [Tooltip("Height above the target's pivot that the camera aims at.")]
        [SerializeField]
        private float lookHeight = 0.8f;

        [Header("Dynamic FOV")]
        [SerializeField] private float baseFov = 60f;
        [SerializeField] private float speedFovBoost = 10f;
        [SerializeField] private float turboFovBoost = 6f;
        [SerializeField] private float speedForMaxFov = 25f;
        [SerializeField] private float fovSmoothSpeed = 3f;

        private Camera cachedCamera;
        private Rigidbody targetBody;
        private KartController targetKart;

        public Transform Target => target;

        /// <summary>Follows a new target; with snap, jumps straight behind it instead of lerping.</summary>
        public void SetTarget(Transform newTarget, bool snap = true)
        {
            target = newTarget;

            if (snap && target != null)
            {
                Quaternion yawOnly = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
                transform.position = target.position + yawOnly * offset;
                transform.rotation = Quaternion.LookRotation((target.position + Vector3.up * lookHeight) - transform.position, Vector3.up);
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Quaternion yawOnly = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            Vector3 desiredPosition = target.position + yawOnly * offset;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

            Vector3 lookDirection = (target.position + Vector3.up * lookHeight) - transform.position;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);

            UpdateFov();
        }

        /// <summary>Widens the field of view with speed and while the turbo is active.</summary>
        private void UpdateFov()
        {
            if (cachedCamera == null) cachedCamera = GetComponent<Camera>();
            if (targetBody == null || targetBody.transform != target)
            {
                targetBody = target.GetComponentInParent<Rigidbody>();
                targetKart = target.GetComponentInParent<KartController>();
            }

            if (cachedCamera == null || targetBody == null)
            {
                return;
            }

            float speed01 = Mathf.Clamp01(targetBody.linearVelocity.magnitude / speedForMaxFov);
            float desiredFov = baseFov + speed01 * speedFovBoost + (targetKart != null && targetKart.IsTurboActive ? turboFovBoost : 0f);
            cachedCamera.fieldOfView = Mathf.Lerp(cachedCamera.fieldOfView, desiredFov, fovSmoothSpeed * Time.deltaTime);
        }
    }
}
