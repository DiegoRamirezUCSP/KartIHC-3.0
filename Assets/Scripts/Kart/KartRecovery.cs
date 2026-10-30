using KartIHC.Gameplay;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KartIHC.Kart
{
    /// <summary>
    /// Puts the kart back on the track when it flips (up.y below a threshold for
    /// a while), falls below a height, or is stuck stopped off the track. Also
    /// on demand with a key (T). The kart reappears on the last checkpoint passed
    /// (from LapManager), or at its starting pose, stopped and facing the racing
    /// direction.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KartRecovery : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureOnKart()
        {
            KartController kart = FindFirstObjectByType<KartController>();
            if (kart != null && kart.GetComponent<KartRecovery>() == null)
            {
                kart.gameObject.AddComponent<KartRecovery>();
            }
        }

        [Header("Volcado")]
        [SerializeField] private float minUpY = 0.3f;
        [SerializeField] private float flippedSeconds = 2f;

        [Header("Caida")]
        [SerializeField] private float minHeight = -5f;

        [Header("Detenido fuera de pista")]
        [SerializeField] private float stoppedSpeed = 0.5f;
        [SerializeField] private float stuckSeconds = 3f;
        [Tooltip("Margen (m) fuera del ancho de la pista antes de considerarse fuera.")]
        [SerializeField] private float offTrackMargin = 1f;

        [Header("Reaparicion")]
        [Tooltip("Margen (m) sobre la altura de reposo calculada, para que caiga suave sin hundirse.")]
        [SerializeField] private float respawnDrop = 0.05f;

#if ENABLE_INPUT_SYSTEM
        [SerializeField] private Key manualKey = Key.T;
#else
        [SerializeField] private KeyCode manualKey = KeyCode.T;
#endif

        public event System.Action Respawned;

        private Rigidbody rb;
        private float restHeight;
        private LapManager lapManager;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private float flippedTimer;
        private float stuckTimer;

        // Oval from TrackBuilder: ring around the origin, read from the StartLine.
        private bool hasTrackRing;
        private float ringInner;
        private float ringOuter;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            KartController kart = GetComponent<KartController>();
            restHeight = kart != null
                ? ComputeRestHeight(transform, new[] { kart.frontLeft, kart.frontRight, kart.rearLeft, kart.rearRight })
                : 0.5f;
            startPosition = transform.position;
            startRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, Vector3.up);

            GameObject startLine = GameObject.Find("StartLine");
            if (startLine != null)
            {
                Vector3 p = startLine.transform.position;
                float radius = new Vector2(p.x, p.z).magnitude;
                float halfWidth = startLine.transform.localScale.x * 0.5f;
                ringInner = radius - halfWidth - offTrackMargin;
                ringOuter = radius + halfWidth + offTrackMargin;
                hasTrackRing = true;

                Vector3 startForward = Vector3.ProjectOnPlane(startLine.transform.forward, Vector3.up).normalized;
                startPosition = new Vector3(p.x, startPosition.y, p.z);
                startRotation = Quaternion.LookRotation(startForward, Vector3.up);
            }
        }

        private void Start()
        {
            lapManager = FindFirstObjectByType<LapManager>();
        }

        private void Update()
        {
            if (Time.timeScale <= 0f)
            {
                return;
            }

            if (WasManualKeyPressed())
            {
                Respawn();
                return;
            }

            if (transform.position.y < minHeight)
            {
                Respawn();
                return;
            }

            flippedTimer = transform.up.y < minUpY ? flippedTimer + Time.deltaTime : 0f;
            if (flippedTimer > flippedSeconds)
            {
                Respawn();
                return;
            }

            bool stopped = rb.linearVelocity.magnitude < stoppedSpeed;
            stuckTimer = stopped && IsOffTrack() ? stuckTimer + Time.deltaTime : 0f;
            if (stuckTimer > stuckSeconds)
            {
                Respawn();
            }
        }

        private bool IsOffTrack()
        {
            if (hasTrackRing)
            {
                float distance = new Vector2(transform.position.x, transform.position.z).magnitude;
                if (distance < ringInner || distance > ringOuter)
                {
                    return true;
                }
            }

            // Nothing solid below the kart (e.g. outside the ground plane).
            return !Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        }

        public void Respawn()
        {
            Vector3 position = startPosition;
            Quaternion rotation = startRotation;

            if (lapManager != null && lapManager.TryGetRespawnPose(out Vector3 checkpointPosition, out Quaternion checkpointRotation))
            {
                position = checkpointPosition;
                rotation = checkpointRotation;
            }

            MoveTo(position, rotation);
        }

        /// <summary>Puts the kart on the track's start line, stopped and facing the racing direction.</summary>
        public void ResetToStart()
        {
            MoveTo(startPosition, startRotation);
        }

        private void MoveTo(Vector3 position, Quaternion rotation)
        {
            // Snap to the ground below, ignoring the kart's own colliders.
            float groundDistance = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 10f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != rb && hit.distance < groundDistance)
                {
                    groundDistance = hit.distance;
                    position.y = hit.point.y;
                }
            }

            position += Vector3.up * (restHeight + respawnDrop);

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);

            flippedTimer = 0f;
            stuckTimer = 0f;
            Respawned?.Invoke();
        }

        /// <summary>
        /// Height of the kart's pivot above the ground when resting on its wheels: each wheel
        /// sits targetPosition along its suspension travel, and touches the ground at its radius.
        /// </summary>
        public static float ComputeRestHeight(Transform kart, WheelCollider[] wheels)
        {
            float height = 0f;
            bool any = false;

            foreach (WheelCollider wheel in wheels)
            {
                if (wheel == null)
                {
                    continue;
                }

                Vector3 attach = kart.InverseTransformPoint(wheel.transform.TransformPoint(wheel.center));
                float wheelBottom = attach.y - wheel.suspensionDistance * wheel.suspensionSpring.targetPosition - wheel.radius;
                height = any ? Mathf.Max(height, -wheelBottom) : -wheelBottom;
                any = true;
            }

            return any ? height : 0.5f;
        }

        private bool WasManualKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current[manualKey].wasPressedThisFrame;
#else
            return Input.GetKeyDown(manualKey);
#endif
        }
    }
}
