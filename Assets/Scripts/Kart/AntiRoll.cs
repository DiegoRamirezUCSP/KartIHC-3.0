using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Simple anti-roll bar: for each axle, pushes down the more extended wheel
    /// and up the more compressed one, with a force proportional to the
    /// difference in suspension compression between left and right.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class AntiRoll : MonoBehaviour
    {
        [Tooltip("Force (N) applied for a full difference in suspension travel between left and right.")]
        [SerializeField] private float antiRollForce = 6000f;

        private KartController kart;
        private Rigidbody rb;

        private void Awake()
        {
            kart = GetComponent<KartController>();
            rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            ApplyAxle(kart.frontLeft, kart.frontRight);
            ApplyAxle(kart.rearLeft, kart.rearRight);
        }

        private void ApplyAxle(WheelCollider left, WheelCollider right)
        {
            if (left == null || right == null)
            {
                return;
            }

            bool leftGrounded = TryGetTravel(left, out float leftTravel);
            bool rightGrounded = TryGetTravel(right, out float rightTravel);
            float force = (leftTravel - rightTravel) * antiRollForce;

            if (leftGrounded)
            {
                rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            }

            if (rightGrounded)
            {
                rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
            }
        }

        /// <summary>Suspension travel: 0 = fully compressed, 1 = fully extended (or in the air).</summary>
        private static bool TryGetTravel(WheelCollider wheel, out float travel)
        {
            travel = 1f;
            if (!wheel.GetGroundHit(out WheelHit hit))
            {
                return false;
            }

            Vector3 wheelCenter = wheel.transform.TransformPoint(wheel.center);
            float distance = Vector3.Dot(wheelCenter - hit.point, wheel.transform.up) - wheel.radius;
            travel = wheel.suspensionDistance > 0f ? Mathf.Clamp01(distance / wheel.suspensionDistance) : 0f;
            return true;
        }
    }
}
