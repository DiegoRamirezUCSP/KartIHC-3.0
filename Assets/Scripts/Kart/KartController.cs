using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Drives a kart using WheelColliders: rear wheels apply motor torque,
    /// front wheels steer. Exposes simple setters so external input sources
    /// (network, keyboard, custom controllers, etc.) can drive the kart.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KartController : MonoBehaviour
    {
        [Header("Wheel Colliders")]
        public WheelCollider frontLeft;
        public WheelCollider frontRight;
        public WheelCollider rearLeft;
        public WheelCollider rearRight;

        [Header("Wheel Meshes")]
        public Transform frontLeftMesh;
        public Transform frontRightMesh;
        public Transform rearLeftMesh;
        public Transform rearRightMesh;

        [Header("Kart Parameters")]
        public float motorTorque = 1500f;
        public float brakeTorque = 3000f;
        public float maxSteerAngle = 30f;

        [Header("Input Gate")]
        public bool InputEnabled = false;

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.centerOfMass += new Vector3(0f, -0.5f, 0f);
        }

        public void SetInputEnabled(bool isEnabled)
        {
            InputEnabled = isEnabled;
        }

        public void SetAccelerate(float value)
        {
            if (!InputEnabled)
            {
                return;
            }

            float torque = value * motorTorque;
            rearLeft.motorTorque = torque;
            rearRight.motorTorque = torque;
        }

        public void SetBrake(float value)
        {
            if (!InputEnabled)
            {
                return;
            }

            float torque = value * brakeTorque;
            frontLeft.brakeTorque = torque;
            frontRight.brakeTorque = torque;
            rearLeft.brakeTorque = torque;
            rearRight.brakeTorque = torque;
        }

        public void SetSteer(float value)
        {
            if (!InputEnabled)
            {
                return;
            }

            float angle = value * maxSteerAngle;
            frontLeft.steerAngle = angle;
            frontRight.steerAngle = angle;
        }

        private void Update()
        {
            UpdateWheelMesh(frontLeft, frontLeftMesh);
            UpdateWheelMesh(frontRight, frontRightMesh);
            UpdateWheelMesh(rearLeft, rearLeftMesh);
            UpdateWheelMesh(rearRight, rearRightMesh);
        }

        private void UpdateWheelMesh(WheelCollider collider, Transform mesh)
        {
            if (collider == null || mesh == null)
            {
                return;
            }

            collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            mesh.position = position;
            mesh.rotation = rotation;
        }
    }
}
