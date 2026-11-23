using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Faros y luces de freno del Jeep (KartIHC/City/5 Kart Look lo configura).
    /// Solo lee el estado del kart; no toca física, WheelColliders ni entradas:
    /// - Conductor: freno activo si algún WheelCollider tiene brakeTorque > 0
    ///   (frenada, trampa policial o kart retenido en el menú).
    /// - Hacker (kart cinemático, sin entradas locales): desaceleración medida a partir
    ///   de la posición, suavizada.
    /// Las luces de freno cambian de material compartido (sin instanciar materiales).
    /// Si el kart está oculto (práctica del hacker) los faros se apagan.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KartLights : MonoBehaviour
    {
        [SerializeField] private Renderer tailRenderer;
        [SerializeField] private Material tailDim;
        [SerializeField] private Material tailBrake;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Light[] headlights = new Light[0];
        [SerializeField, Tooltip("Desaceleración (m/s²) que enciende el freno cuando no hay brakeTorque (hacker).")]
        private float decelerationThreshold = 4f;
        [SerializeField] private float minSpeed = 1.5f;
        [SerializeField, Tooltip("Tiempo mínimo encendidas para que no parpadeen.")]
        private float holdSeconds = 0.2f;

        private KartController kart;
        private Vector3 lastPosition;
        private float smoothedSpeed;
        private bool hasLast;
        private float brakeUntil;
        private bool headlightsOn = true;

        public void Configure(Renderer tail, Material dim, Material brake, Renderer body, Light[] lights)
        {
            tailRenderer = tail;
            tailDim = dim;
            tailBrake = brake;
            bodyRenderer = body;
            headlights = lights ?? new Light[0];
        }

        private void Awake()
        {
            kart = GetComponentInParent<KartController>();
        }

        private void OnEnable()
        {
            hasLast = false;
            brakeUntil = 0f;
            SetBrake(false);
        }

        private void LateUpdate()
        {
            bool visible = bodyRenderer == null || (bodyRenderer.enabled && bodyRenderer.gameObject.activeInHierarchy);
            if (visible != headlightsOn)
            {
                headlightsOn = visible;
                foreach (Light light in headlights)
                {
                    if (light != null) light.enabled = visible;
                }
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            Transform body = kart != null ? kart.transform : transform;
            Vector3 position = body.position;
            bool braking = WheelsBraking();
            if (hasLast)
            {
                float speed = Vector3.Dot(position - lastPosition, body.forward) / dt;
                float previous = smoothedSpeed;
                smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-dt * 8f));
                float deceleration = (Mathf.Abs(previous) - Mathf.Abs(smoothedSpeed)) / dt;
                if (Mathf.Abs(previous) > minSpeed && deceleration > decelerationThreshold)
                {
                    braking = true;
                }
            }
            lastPosition = position;
            hasLast = true;

            if (braking)
            {
                brakeUntil = Time.time + holdSeconds;
            }
            SetBrake(visible && Time.time < brakeUntil);
        }

        private bool WheelsBraking()
        {
            if (kart == null) return false;
            return Braking(kart.frontLeft) || Braking(kart.frontRight) || Braking(kart.rearLeft) || Braking(kart.rearRight);
        }

        private static bool Braking(WheelCollider wheel) => wheel != null && wheel.enabled && wheel.brakeTorque > 1f;

        private void SetBrake(bool on)
        {
            if (tailRenderer == null || tailDim == null || tailBrake == null) return;
            Material wanted = on ? tailBrake : tailDim;
            if (tailRenderer.sharedMaterial != wanted)
            {
                tailRenderer.sharedMaterial = wanted;
            }
        }
    }
}
